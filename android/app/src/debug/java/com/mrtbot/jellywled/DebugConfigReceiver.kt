package com.mrtbot.jellywled

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.util.Base64
import android.util.Log
import com.mrtbot.jellywled.data.Settings
import com.mrtbot.jellywled.data.WledNode
import com.mrtbot.jellywled.service.GlowService
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import org.json.JSONArray

/**
 * Debug builds only. Applies settings sent from adb so the end-to-end test can drive the app without the UI:
 *
 * adb shell am broadcast -n com.mrtbot.jellywled/.DebugConfigReceiver -a com.mrtbot.jellywled.DEBUG_CONFIG \
 *   --es serverUrl http://192.168.1.20:8096 --es token <token> --es userId <id> --es followMode anyOfUser \
 *   --es nodesB64 "$(echo -n '[{"name":"sim","host":"192.168.1.20","ddpPort":4050,"httpPort":8090}]' | base64 -w0)" --ez glowEnabled true
 *
 * Extras are optional; only the ones present are changed. `test=<pattern>` with `testNode=<node id or name>` and
 * `testSeconds=<n>` runs a test pattern.
 */
class DebugConfigReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action != ACTION) return
        val pending = goAsync()
        val app = context.applicationContext
        CoroutineScope(Dispatchers.IO).launch {
            try {
                val store = Settings(app)
                store.update { s ->
                    var next = s
                    intent.getStringExtra("serverUrl")?.let { next = next.copy(serverUrl = it) }
                    intent.getStringExtra("token")?.let { next = next.copy(accessToken = it) }
                    intent.getStringExtra("userId")?.let { next = next.copy(userId = it) }
                    intent.getStringExtra("userName")?.let { next = next.copy(userName = it) }
                    intent.getStringExtra("followMode")?.let { next = next.copy(followMode = it) }
                    intent.getStringExtra("followedDeviceId")?.let { next = next.copy(followedDeviceId = it) }
                    intent.getStringExtra("followedDeviceName")?.let { next = next.copy(followedDeviceName = it) }
                    // The shell mangles quotes in a JSON extra, so the test sends it base64 encoded (nodesB64).
                    val nodesJson = intent.getStringExtra("nodesB64")?.let { String(Base64.decode(it, Base64.DEFAULT), Charsets.UTF_8) }
                        ?: intent.getStringExtra("nodes")
                    nodesJson?.let { json ->
                        val a = JSONArray(json)
                        next = next.copy(nodes = List(a.length()) { WledNode.fromJson(a.getJSONObject(it)) })
                    }
                    if (intent.hasExtra("glowEnabled")) next = next.copy(glowEnabled = intent.getBooleanExtra("glowEnabled", false))
                    if (intent.hasExtra("startOnBoot")) next = next.copy(startOnBoot = intent.getBooleanExtra("startOnBoot", true))
                    next
                }
                val s = store.current()
                Log.i(TAG, "Debug config applied: server=${s.serverUrl} follow=${s.followMode} nodes=${s.nodes.size} glow=${s.glowEnabled}")
                val pattern = intent.getStringExtra("test")
                if (pattern != null) {
                    val which = intent.getStringExtra("testNode")
                    val node = s.nodes.firstOrNull { it.id == which || it.name == which } ?: s.nodes.firstOrNull()
                    if (node != null) GlowService.test(app, node.id, pattern, intent.getIntExtra("testSeconds", 5))
                } else if (s.glowEnabled) {
                    GlowService.start(app)
                }
            } catch (e: Exception) {
                Log.w(TAG, "Debug config failed", e)
            } finally {
                pending.finish()
            }
        }
    }

    private companion object {
        const val ACTION = "com.mrtbot.jellywled.DEBUG_CONFIG"
        const val TAG = "JellyWledDebug"
    }
}
