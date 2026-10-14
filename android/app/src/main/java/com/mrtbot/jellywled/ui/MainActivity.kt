package com.mrtbot.jellywled.ui

import android.content.pm.PackageManager
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.SystemBarStyle
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.mrtbot.jellywled.data.Settings
import com.mrtbot.jellywled.service.GlowService

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        // Dark theme only: light system bar icons whatever the system theme is.
        enableEdgeToEdge(
            statusBarStyle = SystemBarStyle.dark(android.graphics.Color.TRANSPARENT),
            navigationBarStyle = SystemBarStyle.dark(android.graphics.Color.TRANSPARENT),
        )
        super.onCreate(savedInstanceState)
        val tv = packageManager.hasSystemFeature(PackageManager.FEATURE_LEANBACK)
        setContent { GlowTheme { App(tv) } }
    }
}

private enum class Tab(val title: String) { Connect("Connect"), Follow("Follow"), Nodes("Nodes"), Status("Status") }

@Composable
private fun App(tv: Boolean) {
    val context = LocalContext.current
    val store = remember { Settings(context) }
    val loaded by store.flow.collectAsStateWithLifecycle(initialValue = null)
    val status by GlowService.status.collectAsStateWithLifecycle()
    val s = loaded
    Box(Modifier.fillMaxSize().background(Ink)) {
        if (s == null) return@Box
        var tab by rememberSaveable { mutableStateOf(if (s.signedIn) Tab.Status else Tab.Connect) }
        val firstFocus = remember { FocusRequester() }

        // Glow is on but the bridge is not running (app updated, process killed): bring it back.
        LaunchedEffect(s.glowEnabled) { if (s.glowEnabled && !GlowService.status.value.running) GlowService.start(context) }
        // A TV has no touch: put the focus on the current tab so the remote works at once.
        LaunchedEffect(Unit) { if (tv) runCatching { firstFocus.requestFocus() } }

        Column(Modifier.fillMaxSize().safeDrawingPadding()) {
            Row(
                Modifier.fillMaxWidth().padding(start = 24.dp, end = 24.dp, top = 14.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Box(Modifier.size(12.dp).clip(CircleShape).background(if (status.running) GlowBrush else SolidColor(Line)))
                Spacer(Modifier.width(10.dp))
                Text("Jelly-WLED Glow", style = MaterialTheme.typography.headlineMedium, color = TextHi)
            }
            Row(
                Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()).padding(horizontal = 14.dp, vertical = 8.dp),
                horizontalArrangement = Arrangement.spacedBy(6.dp),
            ) {
                Tab.entries.forEach { t ->
                    TabButton(t.title, t == tab, if (t == tab) Modifier.focusRequester(firstFocus) else Modifier) { tab = t }
                }
            }
            Box(Modifier.weight(1f)) {
                when (tab) {
                    Tab.Connect -> ConnectScreen(s, store) { tab = Tab.Follow }
                    Tab.Follow -> FollowScreen(s, store)
                    Tab.Nodes -> NodesScreen(s, store, status)
                    Tab.Status -> StatusScreen(s, store, status)
                }
            }
        }
    }
}

@Composable
private fun TabButton(title: String, selected: Boolean, modifier: Modifier, onClick: () -> Unit) {
    val shape = RoundedCornerShape(12.dp)
    Column(
        modifier
            .glowFocus(shape)
            .clip(shape)
            .clickable(onClick = onClick)
            .heightIn(min = 48.dp)
            .padding(horizontal = 16.dp, vertical = 8.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Text(title, style = MaterialTheme.typography.titleMedium, color = if (selected) TextHi else TextLo)
        Box(
            Modifier.padding(top = 6.dp).height(3.dp).width(28.dp).clip(RoundedCornerShape(2.dp))
                .background(if (selected) GlowBrush else SolidColor(Color.Transparent)),
        )
    }
}
