package com.mrtbot.jellywled.ui

import android.Manifest
import android.content.pm.PackageManager
import android.os.Build
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.core.content.ContextCompat
import com.mrtbot.jellywled.data.GlowSettings
import com.mrtbot.jellywled.data.Settings
import com.mrtbot.jellywled.service.GlowService
import com.mrtbot.jellywled.service.GlowStatus
import kotlinx.coroutines.launch
import java.util.Locale

/** The glow switch, start on boot, and what the bridge is doing right now. */
@Composable
fun StatusScreen(s: GlowSettings, store: Settings, status: GlowStatus) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    fun notificationsAllowed() = Build.VERSION.SDK_INT < 33 ||
        ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED
    var allowed by remember { mutableStateOf(notificationsAllowed()) }
    val ask = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { allowed = it }

    ScreenColumn {
        SectionTitle("Status")
        Card {
            Box(
                Modifier.fillMaxWidth().height(4.dp).clip(RoundedCornerShape(2.dp))
                    .background(if (status.running && s.glowEnabled) GlowBrush else SolidColor(Panel2)),
            )
            Text(
                when {
                    !s.glowEnabled -> "Glow is off"
                    status.session != null -> "Glowing"
                    else -> "Glow is on"
                },
                style = MaterialTheme.typography.titleLarge,
                color = TextHi,
            )
            Hint(status.connection)
        }
        ToggleRow(
            "Glow",
            s.glowEnabled,
            when {
                !s.signedIn -> "Sign in on the Connect screen first"
                s.nodes.none { it.enabled } -> "Add a WLED node on the Nodes screen"
                else -> "Follow the session and drive the nodes"
            },
        ) { on ->
            if (on && !allowed && Build.VERSION.SDK_INT >= 33) ask.launch(Manifest.permission.POST_NOTIFICATIONS)
            scope.launch {
                store.update { it.copy(glowEnabled = on) }
                if (on) GlowService.start(context)
            }
        }
        ToggleRow("Start on boot", s.startOnBoot, "Start the bridge when this device boots, if glow is on") { on ->
            scope.launch { store.update { it.copy(startOnBoot = on) } }
        }
        if (!allowed) {
            Card {
                Hint("Allow notifications so the bridge can show what it is following while it runs in the background.")
                ButtonRow { GlowButton("Allow notifications") { ask.launch(Manifest.permission.POST_NOTIFICATIONS) } }
            }
        }

        Card {
            InfoLine("Session", status.session ?: if (s.glowEnabled) "No followed session is playing" else "Not following")
            InfoLine("Track", status.track ?: "None")
            if (s.nodes.isEmpty()) InfoLine("Nodes", "None")
            status.nodes.ifEmpty { null }?.forEach { n ->
                val fps = if (n.fps > 0) " · ${String.format(Locale.US, "%.1f", n.fps)} fps" else ""
                InfoLine(n.name, n.state + fps + (n.error?.let { " · $it" } ?: ""))
            }
            if (status.nodes.isEmpty() && s.nodes.isNotEmpty()) s.nodes.forEach { InfoLine(it.name, if (it.enabled) "idle" else "disabled") }
        }
        status.lastError?.let { Outcome("Last error: $it", false) }
    }
}

@Composable
private fun InfoLine(label: String, value: String) {
    Text(label, style = MaterialTheme.typography.labelLarge, color = TextLo)
    Text(value, style = MaterialTheme.typography.bodyMedium, color = TextHi)
}
