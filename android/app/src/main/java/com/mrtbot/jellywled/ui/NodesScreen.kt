package com.mrtbot.jellywled.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import com.mrtbot.jellywled.data.GlowSettings
import com.mrtbot.jellywled.data.Settings
import com.mrtbot.jellywled.data.WledNode
import com.mrtbot.jellywled.net.FoundWled
import com.mrtbot.jellywled.net.Wled
import com.mrtbot.jellywled.service.GlowService
import com.mrtbot.jellywled.service.GlowStatus
import kotlinx.coroutines.launch
import kotlinx.coroutines.withTimeoutOrNull
import java.util.Locale

/** The WLED nodes this bridge drives: list, discovery, and the per-node editor. */
@Composable
fun NodesScreen(s: GlowSettings, store: Settings, status: GlowStatus) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    var editing by remember { mutableStateOf<WledNode?>(null) }
    var searching by remember { mutableStateOf(false) }
    var searchError by remember { mutableStateOf<String?>(null) }
    val found = remember { mutableStateListOf<FoundWled>() }

    LaunchedEffect(searching) {
        if (!searching) return@LaunchedEffect
        found.clear()
        searchError = null
        try {
            withTimeoutOrNull(20_000) {
                Wled.discover(context).collect { f -> if (found.none { it.host == f.host }) found += f }
            }
        } catch (e: Exception) {
            searchError = friendly(e)
        }
        searching = false
    }

    val e = editing
    if (e != null) {
        key(e.id) {
            NodeEditor(
                initial = e,
                isNew = s.nodes.none { it.id == e.id },
                save = { n -> store.update { st -> st.copy(nodes = if (st.nodes.any { it.id == n.id }) st.nodes.map { if (it.id == n.id) n else it } else st.nodes + n) } },
                remove = { scope.launch { store.update { st -> st.copy(nodes = st.nodes.filter { it.id != e.id }) } }; editing = null },
                close = { editing = null },
            )
        }
        return
    }

    ScreenColumn {
        SectionTitle("WLED nodes")
        Hint("Every enabled node follows the same session. Each has its own layout, colors and timing.")
        if (s.nodes.isEmpty()) Outcome("No nodes yet. Add one by address or find them on this network.", null)
        s.nodes.forEach { node ->
            val st = status.nodes.find { it.id == node.id }
            NodeCard(node, st?.let { if (it.fps > 0) "${it.state} · ${String.format(Locale.US, "%.1f", it.fps)} fps" else it.state }) { editing = node }
        }
        ButtonRow {
            GlowButton("Add node", primary = true) { editing = WledNode(name = "WLED ${s.nodes.size + 1}") }
            // Stays enabled while searching so the remote's focus does not jump away; a second press does nothing.
            GlowButton(if (searching) "Searching..." else "Find on network") { searching = true }
        }
        searchError?.let { Outcome(it, false) }
        if (searching || found.isNotEmpty()) {
            Text("Found on this network", style = MaterialTheme.typography.titleMedium, color = TextHi)
            if (found.isEmpty()) Hint("Looking for WLED nodes (up to 20 seconds)...")
            found.forEach { f ->
                val known = s.nodes.any { it.host == f.host }
                NodeCard(WledNode(name = f.name, host = f.host, httpPort = f.port), if (known) "already added" else "select to add", discovered = true) {
                    if (!known) editing = WledNode(name = f.name, host = f.host, httpPort = f.port)
                }
            }
        }
    }
}

@Composable
private fun NodeCard(node: WledNode, state: String?, discovered: Boolean = false, onClick: () -> Unit) {
    val shape = RoundedCornerShape(16.dp)
    Column(
        Modifier.fillMaxWidth().glowFocus(shape).clip(shape).background(Panel).clickable(onClick = onClick).padding(16.dp),
    ) {
        Text(node.name, style = MaterialTheme.typography.titleMedium, color = if (node.enabled) TextHi else TextLo)
        Text(
            when {
                discovered -> "${node.host}:${node.httpPort}"
                else -> "${node.host.ifBlank { "no address" }} · ${node.ledCount} LEDs · ${node.mode}" + if (node.enabled) "" else " · disabled"
            },
            style = MaterialTheme.typography.bodySmall,
            color = TextLo,
        )
        if (state != null) Text(state, style = MaterialTheme.typography.bodySmall, color = TextLo)
    }
}

@Composable
private fun NodeEditor(initial: WledNode, isNew: Boolean, save: suspend (WledNode) -> Unit, remove: () -> Unit, close: () -> Unit) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    var n by remember { mutableStateOf(initial) }
    var message by remember { mutableStateOf<Pair<Boolean?, String>?>(null) }

    /** Tests run on the saved node, so save the draft first. */
    fun test(pattern: String, seconds: Int) {
        if (n.host.isBlank()) {
            message = false to "Enter the node's address first."
            return
        }
        scope.launch {
            save(n)
            GlowService.test(context, n.id, pattern, seconds)
            message = null to if (pattern == "off") "Strip off." else "Showing \"$pattern\" for $seconds seconds."
        }
    }

    ScreenColumn {
        SectionTitle(if (isNew) "Add a node" else n.name.ifBlank { "Node" })
        Field("Name", n.name) { n = n.copy(name = it) }
        Field("Host or IP address", n.host, keyboard = KeyboardType.Uri) { n = n.copy(host = it.trim()) }
        Pair2({ IntField("DDP port", n.ddpPort, 1..65535, it) { v -> n = n.copy(ddpPort = v) } }) { IntField("HTTP port", n.httpPort, 1..65535, it) { v -> n = n.copy(httpPort = v) } }

        Text("LEDs per edge", style = MaterialTheme.typography.titleMedium, color = TextHi)
        Pair2({ IntField("Top", n.ledsTop, 0..2000, it) { v -> n = n.copy(ledsTop = v) } }) { IntField("Right", n.ledsRight, 0..2000, it) { v -> n = n.copy(ledsRight = v) } }
        Pair2({ IntField("Bottom", n.ledsBottom, 0..2000, it) { v -> n = n.copy(ledsBottom = v) } }) { IntField("Left", n.ledsLeft, 0..2000, it) { v -> n = n.copy(ledsLeft = v) } }
        Choice(
            "LED 0 sits at",
            listOf("top_left" to "Top left", "top_right" to "Top right", "bottom_right" to "Bottom right", "bottom_left" to "Bottom left"),
            n.startCorner,
        ) { n = n.copy(startCorner = it) }
        Choice("Direction, seen from the front", listOf("cw" to "Clockwise", "ccw" to "Counterclockwise"), n.direction) { n = n.copy(direction = it) }
        Choice("Mode", listOf("ring" to "Ring: each edge follows the picture", "average" to "Average: one color"), n.mode) { n = n.copy(mode = it) }

        Text("Color and timing", style = MaterialTheme.typography.titleMedium, color = TextHi)
        Pair2({ IntField("Brightness %", n.brightness, 0..100, it) { v -> n = n.copy(brightness = v) } }) { IntField("Saturation %", n.saturation, 0..300, it) { v -> n = n.copy(saturation = v) } }
        Pair2({ DecimalField("Gamma", n.gamma, 0.2..5.0, it) { v -> n = n.copy(gamma = v) } }) { IntField("Smoothing %", n.smoothing, 0..95, it) { v -> n = n.copy(smoothing = v) } }
        Pair2({ IntField("Frames per second", n.fps, 1..60, it) { v -> n = n.copy(fps = v) } }) { IntField("Sync offset (ms)", n.syncOffsetMs, -5000..5000, it) { v -> n = n.copy(syncOffsetMs = v) } }
        Hint("A positive sync offset makes the lights run ahead to make up for a TV's picture delay.")
        ToggleRow("Leave live mode on stop", n.leaveLiveOnStop, "WLED returns to its own effect as soon as playback stops") { n = n.copy(leaveLiveOnStop = it) }
        ToggleRow("Enabled", n.enabled, "Disabled nodes keep their settings but get no frames") { n = n.copy(enabled = it) }

        Text("Check the strip", style = MaterialTheme.typography.titleMedium, color = TextHi)
        Hint("Show sides paints top red, right green, bottom blue and left yellow, with LED 0 white: use it to find the start corner and direction.")
        ButtonRow {
            GlowButton("Show sides") { test("sides", 10) }
            GlowButton("Chase") { test("chase", 10) }
            GlowButton("White") { test("white", 10) }
            GlowButton("Off") { test("off", 1) }
            GlowButton("Probe") {
                message = null to "Asking ${n.host}..."
                scope.launch {
                    message = try {
                        val info = Wled.info(n.host, n.httpPort)
                        true to "${info.name.ifBlank { "WLED" }}: version ${info.version}, ${info.ledCount ?: "unknown"} LEDs (this layout has ${n.ledCount})."
                    } catch (e: Exception) {
                        false to friendly(e)
                    }
                }
            }
        }
        message?.let { (ok, text) -> Outcome(text, ok) }

        ButtonRow {
            GlowButton("Save", primary = true) { scope.launch { save(n); close() } }
            GlowButton("Cancel") { close() }
            if (!isNew) GlowButton("Remove") { remove() }
        }
    }
}

/** Two fields side by side. */
@Composable
private fun Pair2(first: @Composable (Modifier) -> Unit, second: @Composable (Modifier) -> Unit) {
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
        first(Modifier.weight(1f))
        second(Modifier.weight(1f))
    }
}
