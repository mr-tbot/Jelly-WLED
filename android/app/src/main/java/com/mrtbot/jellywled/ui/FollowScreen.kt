package com.mrtbot.jellywled.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.unit.dp
import com.mrtbot.jellywled.data.GlowSettings
import com.mrtbot.jellywled.data.Settings
import com.mrtbot.jellywled.net.JellyfinClient
import com.mrtbot.jellywled.net.SessionState
import com.mrtbot.jellywled.service.GlowService
import kotlinx.coroutines.launch

/** Picks the playback session the lights follow. */
@Composable
fun FollowScreen(s: GlowSettings, store: Settings) {
    val scope = rememberCoroutineScope()
    var sessions by remember { mutableStateOf<List<SessionState>?>(null) }
    var error by remember { mutableStateOf<String?>(null) }
    var refresh by remember { mutableIntStateOf(0) }
    val anyMode = s.followMode == GlowService.FOLLOW_ANY

    LaunchedEffect(refresh, s.serverUrl, s.accessToken) {
        if (!s.signedIn) return@LaunchedEffect
        error = null
        try {
            sessions = JellyfinClient(s.serverUrl, s.accessToken, s.deviceId).sessions()
        } catch (e: Exception) {
            error = friendly(e)
        }
    }

    ScreenColumn {
        SectionTitle("Follow a session")
        Hint("The lights follow one playback session: pick the TV or phone that plays, or follow whatever this account plays.")
        if (!s.signedIn) {
            Outcome("Sign in on the Connect screen first.", null)
            return@ScreenColumn
        }
        Card {
            Text("Following", style = MaterialTheme.typography.labelLarge, color = TextLo)
            Text(
                when {
                    anyMode -> "Any session of ${s.userName.ifBlank { "this account" }}"
                    s.followedDeviceId.isBlank() -> "Nothing picked yet"
                    else -> s.followedDeviceName.ifBlank { s.followedDeviceId }
                },
                style = MaterialTheme.typography.titleLarge,
                color = TextHi,
            )
            if (!anyMode && s.followedDeviceId.isBlank()) Hint("Start playback on the TV, refresh, and pick its session below.")
        }
        ToggleRow("Any session of my user", anyMode, "Otherwise the lights follow the device picked below") { on ->
            scope.launch { store.update { it.copy(followMode = if (on) GlowService.FOLLOW_ANY else "device") } }
        }
        ButtonRow { GlowButton("Refresh") { refresh++ } }
        error?.let { Outcome(it, false) }
        val list = sessions
        when {
            list == null && error == null -> Hint("Loading sessions...")
            list != null && list.isEmpty() -> Hint("Nothing is playing right now. Start playback on the TV, then refresh.")
            list != null -> list.forEach { session ->
                val selected = !anyMode && session.deviceId.equals(s.followedDeviceId, ignoreCase = true)
                SessionCard(session, selected) {
                    scope.launch {
                        store.update { it.copy(followMode = "device", followedDeviceId = session.deviceId, followedDeviceName = session.deviceName) }
                    }
                }
            }
        }
    }
}

@Composable
private fun SessionCard(session: SessionState, selected: Boolean, onPick: () -> Unit) {
    val shape = RoundedCornerShape(16.dp)
    Column(
        Modifier
            .fillMaxWidth()
            .glowFocus(shape)
            .clip(shape)
            .background(Panel)
            .border(1.dp, if (selected) Orange else Panel, shape)
            .clickable(onClick = onPick)
            .padding(16.dp),
    ) {
        Text(session.deviceName.ifBlank { session.deviceId }, style = MaterialTheme.typography.titleMedium, color = if (selected) Orange else TextHi)
        Text("${session.client} · ${session.userName}", style = MaterialTheme.typography.bodySmall, color = TextLo)
        val state = if (session.isPaused) "Paused" else "Playing"
        Text("$state: ${session.itemName}", style = MaterialTheme.typography.bodyMedium, color = TextHi)
        Text("Glow track: ${session.trackState}", style = MaterialTheme.typography.bodySmall, color = TextLo)
    }
}
