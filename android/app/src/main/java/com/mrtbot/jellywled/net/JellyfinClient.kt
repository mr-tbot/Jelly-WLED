package com.mrtbot.jellywled.net

import android.os.Build
import com.mrtbot.jellywled.glow.SegmentHeader
import com.mrtbot.jellywled.glow.SyncClock
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.callbackFlow
import kotlinx.coroutines.flow.flow
import kotlinx.coroutines.withContext
import okhttp3.Call
import okhttp3.HttpUrl
import okhttp3.HttpUrl.Companion.toHttpUrlOrNull
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import okhttp3.Response
import okhttp3.sse.EventSource
import okhttp3.sse.EventSourceListener
import okhttp3.sse.EventSources
import org.json.JSONArray
import org.json.JSONObject
import java.io.Closeable
import java.io.EOFException
import java.io.IOException
import java.util.concurrent.TimeUnit

/** One playback session as the plugin reports it (PROTOCOL.md section 2, "state"). */
data class SessionState(
    val sessionId: String,
    val deviceId: String,
    val deviceName: String,
    val client: String,
    val userId: String,
    val userName: String,
    val itemId: String,
    val itemName: String,
    val mediaSourceId: String?,
    val runTimeTicks: Long,
    val positionTicks: Long,
    val reportedAtMs: Long,
    val isPaused: Boolean,
    /** complete, partial, analyzing or none. */
    val trackState: String,
    val trackFps: Int,
    val trackFrames: Long,
)

/** What the plugin's event stream delivers, plus a marker when the stream drops. */
sealed interface GlowEvent {
    data class Hello(
        val serverTimeMs: Long,
        val receivedAtMs: Long,
        val rttMs: Long?,
        val pluginVersion: String,
        val analysisFps: Int,
        val ringW: Int,
        val ringH: Int,
    ) : GlowEvent

    data class State(val state: SessionState) : GlowEvent
    data class Stopped(val sessionId: String, val deviceId: String) : GlowEvent
    data class Ping(val serverTimeMs: Long, val receivedAtMs: Long) : GlowEvent
    data class Disconnected(val reason: String) : GlowEvent
}

data class ServerInfo(val serverName: String, val version: String)
data class SignIn(val accessToken: String, val userId: String, val userName: String)

/**
 * The Jellyfin and Jelly-WLED REST surface the bridge needs. [token] is an access token or an API key; null
 * before sign-in. Blocking calls run on the IO dispatcher.
 */
class JellyfinClient(serverUrl: String, private val token: String?, private val deviceId: String) {
    private val base: HttpUrl = normalizeUrl(serverUrl) ?: throw IOException("That does not look like a server address.")

    /** The server address in the form it is stored. */
    val baseUrl: String = base.toString().trimEnd('/')

    private fun authorization(): String = buildString {
        append("MediaBrowser Client=\"Jelly-WLED Glow\", Device=\"").append(headerSafe(Build.MODEL ?: "Android"))
        append("\", DeviceId=\"").append(headerSafe(deviceId)).append("\", Version=\"1.0.0\"")
        if (!token.isNullOrBlank()) append(", Token=\"").append(headerSafe(token)).append('"')
    }

    private fun url(path: String, vararg query: Pair<String, String?>): HttpUrl {
        val b = base.newBuilder()
        path.trim('/').split('/').forEach { b.addPathSegment(it) }
        query.forEach { (k, v) -> if (v != null) b.addQueryParameter(k, v) }
        return b.build()
    }

    private fun request(url: HttpUrl) = Request.Builder().url(url).header("Authorization", authorization())

    private suspend fun json(req: Request): String = withContext(Dispatchers.IO) {
        http.newCall(req).execute().use { r ->
            if (!r.isSuccessful) throw HttpException(r.code, describe(r.code))
            r.body.string()
        }
    }

    /** GET /System/Info/Public: proves the address is a Jellyfin server. */
    suspend fun publicInfo(): ServerInfo {
        val o = JSONObject(json(request(url("System/Info/Public")).build()))
        return ServerInfo(o.optString("ServerName"), o.optString("Version"))
    }

    /** GET /System/Info: needs a valid token or API key, so it verifies an API key. */
    suspend fun privateInfo(): ServerInfo {
        val o = JSONObject(json(request(url("System/Info")).build()))
        return ServerInfo(o.optString("ServerName"), o.optString("Version"))
    }

    /** POST /Users/AuthenticateByName. */
    suspend fun authenticate(user: String, password: String): SignIn {
        val body = JSONObject().put("Username", user).put("Pw", password).toString()
            .toRequestBody("application/json".toMediaType())
        val o = try {
            JSONObject(json(request(url("Users/AuthenticateByName")).post(body).build()))
        } catch (e: HttpException) {
            if (e.code == 401) throw IOException("Wrong user name or password.") else throw e
        }
        val u = o.optJSONObject("User")
        return SignIn(o.optString("AccessToken"), u?.optString("Id").orEmpty(), u?.optString("Name") ?: user)
    }

    /** GET /JellyWled/Sessions: the sessions this user may see, with their track state. */
    suspend fun sessions(): List<SessionState> {
        val a = JSONArray(json(request(url("JellyWled/Sessions")).build()))
        return List(a.length()) { parseState(a.getJSONObject(it)) }
    }

    /**
     * GET /JellyWled/Events as a never-ending flow: reconnects with exponential backoff (1 s up to 30 s), emits
     * [GlowEvent.Disconnected] whenever the stream drops, and starts again with a fresh hello and states.
     */
    fun events(): Flow<GlowEvent> = flow {
        var backoff = 1_000L
        while (true) {
            try {
                connect().collect {
                    if (it is GlowEvent.Hello) backoff = 1_000L
                    emit(it)
                }
                emit(GlowEvent.Disconnected("The server closed the event stream."))
            } catch (e: IOException) {
                emit(GlowEvent.Disconnected(e.message ?: e.javaClass.simpleName))
            }
            delay(backoff)
            backoff = (backoff * 2).coerceAtMost(30_000L)
        }
    }

    private fun connect(): Flow<GlowEvent> = callbackFlow {
        val req = request(url("JellyWled/Events")).header("Accept", "text/event-stream").build()
        var rtt: Long? = null
        val source = EventSources.createFactory(http).newEventSource(req, object : EventSourceListener() {
            override fun onOpen(eventSource: EventSource, response: Response) {
                rtt = response.receivedResponseAtMillis - response.sentRequestAtMillis
            }

            override fun onEvent(eventSource: EventSource, id: String?, type: String?, data: String) {
                val event = runCatching { parseEvent(type, data, SyncClock.nowMs(), rtt) }.getOrNull()
                if (event != null) trySend(event)
            }

            override fun onClosed(eventSource: EventSource) {
                channel.close()
            }

            override fun onFailure(eventSource: EventSource, t: Throwable?, response: Response?) {
                val reason = when {
                    response != null && !response.isSuccessful -> describe(response.code)
                    t != null -> t.message ?: t.javaClass.simpleName
                    else -> "The event stream failed."
                }
                channel.close(IOException(reason))
            }
        })
        awaitClose { source.cancel() }
    }

    /**
     * GET /JellyWled/Track/{itemId} from [fromMs] with on-demand analysis. Returns null when the server has no
     * track and will not make one (404, header X-JellyWled-Track: none).
     */
    suspend fun openTrack(itemId: String, mediaSourceId: String?, fromMs: Long): TrackStream? = withContext(Dispatchers.IO) {
        val req = request(
            url("JellyWled/Track/$itemId", "mediaSourceId" to mediaSourceId, "fromMs" to fromMs.coerceAtLeast(0).toString(), "analyze" to "true"),
        ).build()
        val call = http.newCall(req)
        val response = call.execute()
        if (response.code == 404) {
            response.close()
            return@withContext null
        }
        if (!response.isSuccessful) {
            response.close()
            throw HttpException(response.code, describe(response.code))
        }
        try {
            TrackStream(call, response)
        } catch (e: IOException) {
            response.close()
            throw e
        }
    }

    class HttpException(val code: Int, message: String) : IOException(message)

    companion object {
        /** Shared client. The read timeout outlasts the plugin's 5-second pings and its 15-second analysis wait. */
        val http: OkHttpClient = OkHttpClient.Builder()
            .connectTimeout(10, TimeUnit.SECONDS)
            .readTimeout(30, TimeUnit.SECONDS)
            .build()

        fun normalizeUrl(input: String): HttpUrl? {
            val t = input.trim().trimEnd('/')
            if (t.isEmpty()) return null
            return (if (t.contains("://")) t else "http://$t").toHttpUrlOrNull()
        }

        /** Jellyfin ids come with and without dashes depending on the endpoint; compare them without. */
        fun sameId(a: String, b: String): Boolean = a.replace("-", "").equals(b.replace("-", ""), ignoreCase = true)

        private fun headerSafe(s: String) = s.filter { it in ' '..'~' && it != '"' && it != ',' }

        private fun describe(code: Int) = when (code) {
            401 -> "The server refused the sign-in (HTTP 401). Sign in again."
            403 -> "This account may not do that (HTTP 403)."
            404 -> "Not found (HTTP 404). Is the Jelly-WLED plugin installed?"
            else -> "The server answered HTTP $code."
        }

        private fun JSONObject.str(key: String): String? = if (isNull(key)) null else optString(key)

        fun parseState(o: JSONObject): SessionState {
            val t = o.optJSONObject("track")
            return SessionState(
                sessionId = o.str("sessionId").orEmpty(),
                deviceId = o.str("deviceId").orEmpty(),
                deviceName = o.str("deviceName").orEmpty(),
                client = o.str("client").orEmpty(),
                userId = o.str("userId").orEmpty(),
                userName = o.str("userName").orEmpty(),
                itemId = o.str("itemId").orEmpty(),
                itemName = o.str("itemName").orEmpty(),
                mediaSourceId = o.str("mediaSourceId")?.ifBlank { null },
                runTimeTicks = o.optLong("runTimeTicks"),
                positionTicks = o.optLong("positionTicks"),
                reportedAtMs = o.optLong("reportedAtMs"),
                isPaused = o.optBoolean("isPaused"),
                trackState = t?.str("state") ?: "none",
                trackFps = t?.optInt("fps") ?: 0,
                trackFrames = t?.optLong("frames") ?: 0,
            )
        }

        fun parseEvent(type: String?, data: String, receivedAtMs: Long, rttMs: Long?): GlowEvent? {
            val o = JSONObject(data)
            return when (type) {
                "hello" -> GlowEvent.Hello(
                    o.optLong("serverTimeMs"), receivedAtMs, rttMs, o.str("pluginVersion").orEmpty(),
                    o.optInt("analysisFps"), o.optInt("ringW"), o.optInt("ringH"),
                )
                "state" -> GlowEvent.State(parseState(o))
                "stopped" -> GlowEvent.Stopped(o.str("sessionId").orEmpty(), o.str("deviceId").orEmpty())
                "ping" -> GlowEvent.Ping(o.optLong("serverTimeMs"), receivedAtMs)
                else -> null
            }
        }
    }
}

/**
 * A running track stream: the 64-byte header, then frames as the server produces them. [readFrame] blocks; call
 * [cancel] from another thread to abort it, and [close] when done.
 */
class TrackStream internal constructor(private val call: Call, private val response: Response) : Closeable {
    private val source = response.body.source()
    val header: SegmentHeader = SegmentHeader.parse(source.readByteArray(SegmentHeader.SIZE.toLong()))

    /** What the server has for the item: complete, partial, analyzing. */
    val trackState: String? = response.header("X-JellyWled-Track")

    /** The next frame (header.frameBytes bytes), or null when the stream ended. */
    fun readFrame(): ByteArray? = try {
        source.readByteArray(header.frameBytes.toLong())
    } catch (_: EOFException) {
        null
    }

    fun cancel() = call.cancel()

    override fun close() = response.close()
}
