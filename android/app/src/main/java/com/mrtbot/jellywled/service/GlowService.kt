package com.mrtbot.jellywled.service

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.net.wifi.WifiManager
import android.os.Build
import android.os.IBinder
import android.util.Log
import com.mrtbot.jellywled.R
import com.mrtbot.jellywled.data.GlowSettings
import com.mrtbot.jellywled.data.Settings
import com.mrtbot.jellywled.data.WledNode
import com.mrtbot.jellywled.glow.ColorPipeline
import com.mrtbot.jellywled.glow.LedMapper
import com.mrtbot.jellywled.glow.LedSpan
import com.mrtbot.jellywled.glow.RingLayout
import com.mrtbot.jellywled.glow.SegmentHeader
import com.mrtbot.jellywled.glow.SyncClock
import com.mrtbot.jellywled.glow.TestPatterns
import com.mrtbot.jellywled.glow.TrackBuffer
import com.mrtbot.jellywled.glow.UdpSender
import com.mrtbot.jellywled.net.GlowEvent
import com.mrtbot.jellywled.net.JellyfinClient
import com.mrtbot.jellywled.net.SessionState
import com.mrtbot.jellywled.net.TrackStream
import com.mrtbot.jellywled.net.Wled
import com.mrtbot.jellywled.ui.MainActivity
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.awaitCancellation
import kotlinx.coroutines.cancel
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.IOException
import java.util.Locale
import java.util.concurrent.ConcurrentHashMap
import kotlin.math.abs
import kotlin.math.min

/** One node as the Status screen shows it. */
data class NodeStatus(val id: String, val name: String, val state: String, val fps: Double, val error: String?)

/** What the bridge is doing, for the UI. */
data class GlowStatus(
    val running: Boolean = false,
    val connection: String = "Not running",
    val session: String? = null,
    val track: String? = null,
    val nodes: List<NodeStatus> = emptyList(),
    val lastError: String? = null,
)

/**
 * The bridge: follows a Jellyfin playback session over the plugin's event stream, streams its glow track and
 * pushes DDP frames to every enabled WLED node from one render loop. Runs as a connectedDevice foreground
 * service while glow is on or a test pattern is running, and stops itself otherwise.
 */
class GlowService : Service() {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    private val clock = SyncClock()
    private val sessions = ConcurrentHashMap<String, SessionState>()
    private val seenAt = ConcurrentHashMap<String, Long>()
    private val pausedSince = ConcurrentHashMap<String, Long>()
    private val tests = ConcurrentHashMap<String, TestRun>()
    private val runtimes = ConcurrentHashMap<String, NodeRuntime>()
    private var wifiLocks: List<WifiManager.WifiLock> = emptyList()
    private var started = false
    @Volatile private var lastStartId = 0
    private val startLock = Any()
    private var staleJob: Job? = null

    @Volatile private var settings = GlowSettings()
    @Volatile private var track: ActiveTrack? = null
    @Volatile private var seekGeneration = 0
    @Volatile private var helloGeometry = Triple(32, 18, 10)
    @Volatile private var connection = "Starting"
    @Volatile private var trackLine: String? = null
    @Volatile private var lastError: String? = null

    private data class TestRun(val pattern: String, val startedMs: Long, val untilMs: Long)
    private class ActiveTrack(val key: String, val buffer: TrackBuffer)

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        val nm = getSystemService(NotificationManager::class.java)
        nm.createNotificationChannel(NotificationChannel(CHANNEL, "Glow bridge", NotificationManager.IMPORTANCE_LOW))
        val wifi = applicationContext.getSystemService(WIFI_SERVICE) as WifiManager
        @Suppress("DEPRECATION")
        val modes = if (Build.VERSION.SDK_INT >= 29) {
            listOf(WifiManager.WIFI_MODE_FULL_HIGH_PERF, WifiManager.WIFI_MODE_FULL_LOW_LATENCY)
        } else {
            listOf(WifiManager.WIFI_MODE_FULL_HIGH_PERF)
        }
        wifiLocks = modes.map { wifi.createWifiLock(it, "jellywled-glow-$it").apply { setReferenceCounted(false) } }
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        val n = notification("Waiting for playback")
        if (Build.VERSION.SDK_INT >= 29) {
            startForeground(NOTIFICATION_ID, n, ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE)
        } else {
            startForeground(NOTIFICATION_ID, n)
        }
        if (intent?.action == ACTION_TEST) {
            val id = intent.getStringExtra(EXTRA_NODE)
            val pattern = intent.getStringExtra(EXTRA_PATTERN) ?: "sides"
            val seconds = intent.getIntExtra(EXTRA_SECONDS, 10).coerceIn(1, 600)
            val now = System.currentTimeMillis()
            if (id != null) synchronized(startLock) { tests[id] = TestRun(pattern, now, now + seconds * 1000L) }
        }
        synchronized(startLock) { lastStartId = startId }
        if (!started) {
            started = true
            scope.launch { run() }
        }
        return START_STICKY
    }

    override fun onDestroy() {
        scope.cancel()
        wifiLocks.forEach { if (it.isHeld) it.release() }
        _status.value = GlowStatus()
        super.onDestroy()
    }

    private data class Connection(val server: String, val token: String, val deviceId: String, val enabled: Boolean)

    private suspend fun run() = coroutineScope {
        val store = Settings(this@GlowService)
        settings = store.flow.first()
        launch { store.flow.collect { settings = it } }
        launch(Dispatchers.IO) { renderLoop() }
        launch { statusLoop() }
        store.flow
            .map { Connection(it.serverUrl, it.accessToken, it.deviceId, it.glowEnabled) }
            .distinctUntilChanged()
            .collectLatest { follow(it) }
    }

    /** Subscribes to the plugin's events and keeps the track buffer filled, until the connection settings change. */
    private suspend fun follow(c: Connection) {
        sessions.clear()
        track = null
        trackLine = null
        if (!c.enabled) {
            connection = "Glow is off"
            return
        }
        if (c.server.isBlank() || c.token.isBlank()) {
            connection = "Not signed in"
            return
        }
        val client = try {
            JellyfinClient(c.server, c.token, c.deviceId)
        } catch (e: IOException) {
            connection = e.message ?: "Invalid server address"
            return
        }
        connection = "Connecting to ${client.baseUrl}"
        coroutineScope {
            launch { trackLoop(client) }
            client.events().collect { handle(it) }
        }
    }

    private fun handle(e: GlowEvent) {
        val now = System.currentTimeMillis()
        when (e) {
            is GlowEvent.Hello -> {
                clock.hello(e.serverTimeMs, e.receivedAtMs, e.rttMs)
                helloGeometry = Triple(e.ringW.takeIf { it > 0 } ?: 32, e.ringH.takeIf { it > 0 } ?: 18, e.analysisFps.takeIf { it > 0 } ?: 10)
                connection = "Connected (Jelly-WLED ${e.pluginVersion})"
                lastError = null
                // The server re-sends every active session right after hello; forget the ones it no longer has.
                staleJob?.cancel()
                staleJob = scope.launch {
                    delay(3_000)
                    sessions.keys.removeAll { (seenAt[it] ?: 0L) < now }
                }
            }
            is GlowEvent.Ping -> clock.ping(e.serverTimeMs, e.receivedAtMs)
            is GlowEvent.State -> {
                val s = e.state
                val old = sessions.put(s.sessionId, s)
                seenAt[s.sessionId] = now
                if (s.isPaused) pausedSince.putIfAbsent(s.sessionId, now) else pausedSince.remove(s.sessionId)
                if (old != null && old.itemId == s.itemId) {
                    val predicted = positionMs(old)
                    if (abs(predicted - s.positionTicks / 10_000) > SEEK_MS) seekGeneration++
                }
            }
            is GlowEvent.Stopped -> {
                sessions.remove(e.sessionId)
                pausedSince.remove(e.sessionId)
            }
            is GlowEvent.Disconnected -> {
                connection = "Reconnecting: ${e.reason}"
                lastError = e.reason
                Log.i(TAG, "Event stream dropped: ${e.reason}")
                // Keep following through short outages; give up on the sessions after a minute without the server.
                staleJob?.cancel()
                staleJob = scope.launch {
                    delay(60_000)
                    sessions.clear()
                }
            }
        }
    }

    /** The session's position right now, on the server's clock as estimated from the monotonic local clock. */
    private fun positionMs(s: SessionState) = clock.positionMs(s.positionTicks, s.reportedAtMs, s.isPaused, SyncClock.nowMs())

    private fun key(s: SessionState) = s.itemId + "|" + s.mediaSourceId

    /** The followed session: the configured device, or any session of this user; playing ones first, then the newest. */
    private fun followed(): SessionState? {
        val cfg = settings
        return sessions.values
            .filter { s ->
                if (cfg.followMode == FOLLOW_ANY) {
                    cfg.userId.isBlank() || JellyfinClient.sameId(s.userId, cfg.userId)
                } else {
                    cfg.followedDeviceId.isNotBlank() && s.deviceId.equals(cfg.followedDeviceId, ignoreCase = true)
                }
            }
            .minWithOrNull(compareBy<SessionState> { it.isPaused }.thenByDescending { it.reportedAtMs })
    }

    /** The followed session unless glow is off or it has been paused for longer than [PAUSE_RELEASE_MS]. */
    private fun followedForLights(now: Long): SessionState? {
        if (!settings.glowEnabled) return null
        val s = followed() ?: return null
        val since = pausedSince[s.sessionId]
        return if (s.isPaused && since != null && now - since > PAUSE_RELEASE_MS) null else s
    }

    // ---- Track stream -------------------------------------------------------------------------------------

    private suspend fun trackLoop(client: JellyfinClient) {
        while (true) {
            val s = followed()
            if (s == null) {
                track = null
                trackLine = null
                delay(500)
                continue
            }
            val key = key(s)
            val current = track?.takeIf { it.key == key }
            if (current == null) track = null
            val buf = current?.buffer
            val fps = buf?.header?.fps ?: s.trackFps.takeIf { it > 0 } ?: helloGeometry.third
            val frame = SyncClock.frameIndex(positionMs(s), 0, fps).coerceAtLeast(0)
            val continues = buf != null && frame >= buf.startFrame && frame <= buf.endFrame
            // The last stream ended (a gap, the analysis paused, or the end of the item) but plenty is buffered:
            // ask again only when the position gets close to the end of what we have.
            if (continues && buf.endFrame - frame > fps * 10L) {
                delay(500)
                continue
            }
            val fromFrame = if (continues) buf.endFrame else frame
            val stream = try {
                client.openTrack(s.itemId, s.mediaSourceId, SyncClock.msForFrame(fromFrame, fps))
            } catch (e: IOException) {
                trackLine = "Track request failed: ${e.message}"
                lastError = e.message
                delay(5_000)
                continue
            }
            if (stream == null) {
                trackLine = "No glow track for this item; retrying every 10 s"
                delay(10_000)
                continue
            }
            val h = stream.header
            val target = if (continues && buf.header.sameGeometry(h) && h.startFrame == buf.endFrame) {
                buf
            } else {
                TrackBuffer(h).also { track = ActiveTrack(key, it) }
            }
            trackLine = "Track ${stream.trackState ?: "streaming"} · ${h.ringWidth}x${h.ringHeight} at ${h.fps} fps"
            val got = read(stream, target, key)
            if (got == 0) delay(3_000)
        }
    }

    private fun SegmentHeader.sameGeometry(o: SegmentHeader) = ringWidth == o.ringWidth && ringHeight == o.ringHeight && fps == o.fps

    /**
     * Reads frames into [buffer] until the stream ends, the followed item changes, or a seek of more than 5 seconds
     * lands outside the buffer. A watcher cancels the blocking read for the last two, and on cancellation.
     */
    private suspend fun read(stream: TrackStream, buffer: TrackBuffer, key: String): Int = coroutineScope {
        val generation = seekGeneration
        val watcher = launch {
            try {
                while (true) {
                    delay(500)
                    val s = followed() ?: break
                    if (key(s) != key) break
                    if (seekGeneration != generation) {
                        val f = SyncClock.frameIndex(positionMs(s), 0, buffer.header.fps)
                        if (f < buffer.startFrame || f > buffer.endFrame) break
                    }
                }
            } finally {
                stream.cancel()
            }
        }
        var count = 0
        try {
            withContext(Dispatchers.IO) {
                while (true) {
                    val f = stream.readFrame() ?: break
                    buffer.append(f)
                    count++
                }
            }
        } catch (_: IOException) {
            // Canceled by the watcher or the network dropped; the track loop asks again from the position.
        } finally {
            watcher.cancel()
            stream.close()
        }
        count
    }

    // ---- Render loop --------------------------------------------------------------------------------------

    private suspend fun renderLoop() {
        try {
            while (true) {
                val cfg = settings
                val now = System.currentTimeMillis()
                val nowNs = System.nanoTime()
                val session = followedForLights(now)
                val buffer = track?.takeIf { session != null && it.key == key(session) }?.buffer
                val (ringW, ringH) = buffer?.header?.let { it.ringWidth to it.ringHeight } ?: (helloGeometry.first to helloGeometry.second)
                val nodes = cfg.nodes.filter { it.host.isNotBlank() && (it.enabled || tests.containsKey(it.id)) }.associateBy { it.id }

                // Nodes that were removed or disabled are released once and forgotten.
                for (id in runtimes.keys.filter { it !in nodes }) runtimes.remove(id)?.dispose()

                var nextNs = nowNs + 100_000_000L
                for (node in nodes.values) {
                    val rt = runtimes.getOrPut(node.id) { NodeRuntime() }
                    rt.configure(node, ringW, ringH)
                    if (nowNs < rt.dueNs) {
                        nextNs = min(nextNs, rt.dueNs)
                        continue
                    }
                    val period = 1_000_000_000L / node.fps.coerceIn(1, 60)
                    rt.dueNs = if (nowNs - rt.dueNs > period) nowNs + period else rt.dueNs + period
                    nextNs = min(nextNs, rt.dueNs)
                    val test = tests[node.id]?.takeIf { now < it.untilMs }
                    when {
                        test != null -> rt.test(test, now)
                        session != null && node.enabled -> rt.stream(session, buffer)
                        rt.active -> rt.release()
                    }
                }
                tests.entries.removeAll { now >= it.value.untilMs }

                val anyActive = runtimes.values.any { it.active }
                wifiLocks.forEach { if (anyActive && !it.isHeld) it.acquire() else if (!anyActive && it.isHeld) it.release() }
                // Nothing to do: stop, unless glow was just turned on (read from the store, the flow may lag) or a
                // start request (a test) arrived since the last one seen.
                if (!cfg.glowEnabled && tests.isEmpty() && !anyActive && !Settings(this@GlowService).current().glowEnabled) {
                    val stopping = synchronized(startLock) { tests.isEmpty() && stopSelfResult(lastStartId) }
                    if (stopping) awaitCancellation()
                }
                delay(((nextNs - System.nanoTime()) / 1_000_000L).coerceAtLeast(1L))
            }
        } finally {
            withContext(NonCancellable) { runtimes.values.forEach { it.dispose(blockingHttp = true) } }
        }
    }

    /** Per-node state owned by the render loop: layout, pipeline, frame buffers, sender and statistics. */
    private inner class NodeRuntime {
        private var node = WledNode()
        private var layoutKey = ""
        private var senderKey = ""
        private var layout: List<LedSpan> = emptyList()
        private var mapper: LedMapper? = null
        private var pipeline: ColorPipeline? = null
        private var ring = ByteArray(0)
        private var raw = ByteArray(0)
        private var prev = ByteArray(0)
        private var cur = ByteArray(0)
        private var sender: UdpSender? = null
        private val sent = ArrayDeque<Long>()

        var active = false
            private set
        var dueNs = 0L

        @Volatile var fps = 0.0
            private set
        @Volatile var state = "idle"
            private set
        val error: String? get() = sender?.lastError

        fun configure(n: WledNode, ringW: Int, ringH: Int) {
            node = n
            val k = listOf(n.ledsTop, n.ledsRight, n.ledsBottom, n.ledsLeft, n.startCorner, n.direction, n.mode, n.saturation, n.gamma, n.brightness, n.smoothing, ringW, ringH).joinToString("|")
            if (k != layoutKey) {
                layoutKey = k
                layout = RingLayout.build(n.ledsTop, n.ledsRight, n.ledsBottom, n.ledsLeft, n.startCorner, n.direction)
                val m = LedMapper(layout, ringW, ringH, n.mode.equals("average", ignoreCase = true))
                mapper = m
                pipeline = ColorPipeline(n.saturation, n.gamma, n.brightness, n.smoothing)
                ring = ByteArray(m.zoneCount * 3)
                val size = layout.size * 3
                if (cur.size != size) {
                    raw = ByteArray(size)
                    prev = ByteArray(size)
                    cur = ByteArray(size)
                }
            }
            val sk = "${n.host.trim()}|${n.ddpPort}"
            if (sk != senderKey) {
                sender?.close()
                sender = UdpSender(n.host, n.ddpPort)
                senderKey = sk
            }
        }

        /** Test patterns bypass the color pipeline, exactly as on the server. */
        fun test(t: TestRun, now: Long) {
            TestPatterns.render(t.pattern, layout, (now - t.startedMs) / 1000.0, cur)
            send("testing ${t.pattern}")
        }

        /** One frame of the followed session; a missing frame fades to black through the pipeline. */
        fun stream(s: SessionState, buffer: TrackBuffer?) {
            val frame = buffer?.let { SyncClock.frameIndex(positionMs(s), node.syncOffsetMs.toLong(), it.header.fps) }
            val has = buffer != null && frame != null && buffer.tryGet(frame, ring)
            if (has) mapper!!.map(ring, raw) else raw.fill(0)
            pipeline!!.process(raw, if (active) prev else null, cur)
            send(if (has) "streaming" else "waiting for track frames")
        }

        private fun send(newState: String) {
            val s = sender ?: return
            active = true
            state = newState
            val ok = s.send(cur)
            val t = System.nanoTime()
            if (ok) {
                sent.addLast(t)
                while (sent.size > 50) sent.removeFirst()
                fps = if (sent.size >= 2) (sent.size - 1) / ((t - sent.first()) / 1e9) else 0.0
            } else {
                fps = 0.0
            }
            val swap = prev
            prev = cur
            cur = swap
        }

        /** One black frame, then {"live":false} so WLED returns to its own effect at once (when asked to). */
        fun release(blockingHttp: Boolean = false) {
            if (!active) return
            active = false
            cur.fill(0)
            prev.fill(0)
            sender?.send(cur)
            sent.clear()
            fps = 0.0
            state = "idle"
            if (node.leaveLiveOnStop) {
                val n = node
                if (blockingHttp) {
                    Wled.postState(n.host, n.httpPort, "{\"live\":false}")
                } else {
                    scope.launch(Dispatchers.IO) { Wled.postState(n.host, n.httpPort, "{\"live\":false}") }
                }
            }
        }

        fun dispose(blockingHttp: Boolean = false) {
            release(blockingHttp)
            sender?.close()
            sender = null
        }
    }

    // ---- Status and notification -----------------------------------------------------------------------------

    private suspend fun statusLoop() {
        var lastText = ""
        val nm = getSystemService(NotificationManager::class.java)
        while (true) {
            val now = System.currentTimeMillis()
            val cfg = settings
            val s = followedForLights(now)
            val nodes = cfg.nodes.map { n ->
                val rt = runtimes[n.id]
                NodeStatus(n.id, n.name, if (!n.enabled && rt == null) "disabled" else rt?.state ?: "idle", rt?.fps ?: 0.0, rt?.error)
            }
            val sessionLine = followed()?.let { f ->
                val pos = positionMs(f)
                val paused = if (f.isPaused) " · paused" else ""
                "${f.itemName} on ${f.deviceName} (${f.client}) · ${clockText(pos)} / ${clockText(f.runTimeTicks / 10_000)}$paused"
            }
            val buffered = track?.buffer?.let { b ->
                val fps = b.header.fps
                " · buffered ${clockText(b.startFrame * 1000 / fps)} to ${clockText(b.endFrame * 1000 / fps)}"
            }.orEmpty()
            _status.value = GlowStatus(
                running = true,
                connection = connection,
                session = sessionLine,
                track = trackLine?.let { it + buffered },
                nodes = nodes,
                lastError = lastError,
            )
            val enabled = cfg.nodes.count { it.enabled }
            val activeFps = runtimes.values.filter { it.active }.map { it.fps }
            val text = if (s != null) {
                val fps = if (activeFps.isEmpty()) 0.0 else activeFps.average()
                "Following ${s.itemName} on ${s.deviceName} · $enabled ${if (enabled == 1) "node" else "nodes"} · ${String.format(Locale.US, "%.1f", fps)} fps"
            } else {
                "Waiting for playback"
            }
            if (text != lastText) {
                nm.notify(NOTIFICATION_ID, notification(text))
                lastText = text
            }
            delay(1_000)
        }
    }

    private fun clockText(ms: Long): String {
        val t = (ms / 1000).coerceAtLeast(0)
        return String.format(Locale.US, "%d:%02d:%02d", t / 3600, t / 60 % 60, t % 60)
    }

    private fun notification(text: String): Notification {
        val open = PendingIntent.getActivity(
            this, 0, Intent(this, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        return Notification.Builder(this, CHANNEL)
            .setSmallIcon(R.drawable.ic_notification)
            .setContentTitle(getString(R.string.app_name))
            .setContentText(text)
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .setCategory(Notification.CATEGORY_SERVICE)
            .setContentIntent(open)
            .build()
    }

    companion object {
        private const val TAG = "JellyWledGlow"
        private const val CHANNEL = "glow"
        private const val NOTIFICATION_ID = 1
        private const val ACTION_TEST = "com.mrtbot.jellywled.TEST"
        private const val EXTRA_NODE = "node"
        private const val EXTRA_PATTERN = "pattern"
        private const val EXTRA_SECONDS = "seconds"
        const val FOLLOW_ANY = "anyOfUser"

        /** A position jump this large between reports is a seek. */
        private const val SEEK_MS = 5_000L

        /** Paused longer than this, the lights are released until playback resumes. */
        private const val PAUSE_RELEASE_MS = 120_000L

        private val _status = MutableStateFlow(GlowStatus())
        val status: StateFlow<GlowStatus> = _status

        /** Starts the bridge (call from the foreground UI or from boot). */
        fun start(context: Context) {
            context.startForegroundService(Intent(context, GlowService::class.java))
        }

        /** Runs [pattern] (sides, chase, white, off) on one node for [seconds], starting the service if needed. */
        fun test(context: Context, nodeId: String, pattern: String, seconds: Int) {
            context.startForegroundService(
                Intent(context, GlowService::class.java)
                    .setAction(ACTION_TEST)
                    .putExtra(EXTRA_NODE, nodeId)
                    .putExtra(EXTRA_PATTERN, pattern)
                    .putExtra(EXTRA_SECONDS, seconds),
            )
        }
    }
}

/** Starts the bridge after a reboot when the user turned on both glow and start on boot. */
class BootReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action != Intent.ACTION_BOOT_COMPLETED) return
        val pending = goAsync()
        CoroutineScope(Dispatchers.IO).launch {
            try {
                val s = Settings(context).current()
                if (s.startOnBoot && s.glowEnabled) GlowService.start(context)
            } catch (e: Exception) {
                Log.w("JellyWledGlow", "Boot start failed", e)
            } finally {
                pending.finish()
            }
        }
    }
}
