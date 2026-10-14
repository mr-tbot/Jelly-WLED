package com.mrtbot.jellywled.data

import android.content.Context
import androidx.datastore.preferences.core.MutablePreferences
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.booleanPreferencesKey
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.onStart
import org.json.JSONArray
import org.json.JSONObject
import java.util.UUID

/** One WLED controller on the local network and how its strip is laid out around the TV. */
data class WledNode(
    val id: String = UUID.randomUUID().toString(),
    val name: String = "WLED",
    val host: String = "",
    val ddpPort: Int = 4048,
    val httpPort: Int = 80,
    val ledsTop: Int = 60,
    val ledsRight: Int = 34,
    val ledsBottom: Int = 60,
    val ledsLeft: Int = 34,
    /** Where LED 0 sits: top_left, top_right, bottom_right or bottom_left. */
    val startCorner: String = "top_left",
    /** cw or ccw, as seen from the front. */
    val direction: String = "cw",
    /** ring (each LED follows its edge of the picture) or average (the whole strip shows the mean color). */
    val mode: String = "ring",
    val brightness: Int = 60,
    val saturation: Int = 120,
    val gamma: Double = 2.2,
    val smoothing: Int = 35,
    val fps: Int = 25,
    val syncOffsetMs: Int = 0,
    val leaveLiveOnStop: Boolean = true,
    val enabled: Boolean = true,
) {
    val ledCount: Int get() = ledsTop + ledsRight + ledsBottom + ledsLeft

    fun toJson(): JSONObject = JSONObject()
        .put("id", id).put("name", name).put("host", host).put("ddpPort", ddpPort).put("httpPort", httpPort)
        .put("ledsTop", ledsTop).put("ledsRight", ledsRight).put("ledsBottom", ledsBottom).put("ledsLeft", ledsLeft)
        .put("startCorner", startCorner).put("direction", direction).put("mode", mode)
        .put("brightness", brightness).put("saturation", saturation).put("gamma", gamma).put("smoothing", smoothing)
        .put("fps", fps).put("syncOffsetMs", syncOffsetMs).put("leaveLiveOnStop", leaveLiveOnStop).put("enabled", enabled)

    companion object {
        fun fromJson(o: JSONObject): WledNode {
            val d = WledNode(id = o.optString("id").ifBlank { UUID.randomUUID().toString() })
            return d.copy(
                name = o.optString("name", d.name), host = o.optString("host", d.host),
                ddpPort = o.optInt("ddpPort", d.ddpPort), httpPort = o.optInt("httpPort", d.httpPort),
                ledsTop = o.optInt("ledsTop", d.ledsTop), ledsRight = o.optInt("ledsRight", d.ledsRight),
                ledsBottom = o.optInt("ledsBottom", d.ledsBottom), ledsLeft = o.optInt("ledsLeft", d.ledsLeft),
                startCorner = o.optString("startCorner", d.startCorner), direction = o.optString("direction", d.direction),
                mode = o.optString("mode", d.mode), brightness = o.optInt("brightness", d.brightness),
                saturation = o.optInt("saturation", d.saturation), gamma = o.optDouble("gamma", d.gamma),
                smoothing = o.optInt("smoothing", d.smoothing), fps = o.optInt("fps", d.fps),
                syncOffsetMs = o.optInt("syncOffsetMs", d.syncOffsetMs),
                leaveLiveOnStop = o.optBoolean("leaveLiveOnStop", d.leaveLiveOnStop), enabled = o.optBoolean("enabled", d.enabled),
            )
        }
    }
}

/** Everything the app remembers. */
data class GlowSettings(
    val serverUrl: String = "",
    /** A Jellyfin access token from sign-in, or an API key. */
    val accessToken: String = "",
    val userId: String = "",
    val userName: String = "",
    /** Generated once; Jellyfin uses it to tell this bridge apart from other devices. */
    val deviceId: String = "",
    /** device: follow [followedDeviceId]; anyOfUser: follow any session of the signed-in user. */
    val followMode: String = "device",
    val followedDeviceId: String = "",
    val followedDeviceName: String = "",
    val nodes: List<WledNode> = emptyList(),
    val glowEnabled: Boolean = false,
    val startOnBoot: Boolean = true,
) {
    val signedIn: Boolean get() = serverUrl.isNotBlank() && accessToken.isNotBlank()
}

private val Context.store by preferencesDataStore(name = "glow")

/** DataStore-backed settings. Cheap to create: every instance shares the one process-wide store. */
class Settings(context: Context) {
    private val store = context.applicationContext.store

    val flow: Flow<GlowSettings> = store.data
        .onStart { store.edit { if (it[DEVICE_ID].isNullOrBlank()) it[DEVICE_ID] = UUID.randomUUID().toString() } }
        .map { it.read() }

    suspend fun current(): GlowSettings = flow.first()

    suspend fun update(change: (GlowSettings) -> GlowSettings) {
        store.edit { it.write(change(it.read())) }
    }

    private companion object {
        val SERVER_URL = stringPreferencesKey("serverUrl")
        val ACCESS_TOKEN = stringPreferencesKey("accessToken")
        val USER_ID = stringPreferencesKey("userId")
        val USER_NAME = stringPreferencesKey("userName")
        val DEVICE_ID = stringPreferencesKey("deviceId")
        val FOLLOW_MODE = stringPreferencesKey("followMode")
        val FOLLOWED_DEVICE_ID = stringPreferencesKey("followedDeviceId")
        val FOLLOWED_DEVICE_NAME = stringPreferencesKey("followedDeviceName")
        val NODES = stringPreferencesKey("nodes")
        val GLOW_ENABLED = booleanPreferencesKey("glowEnabled")
        val START_ON_BOOT = booleanPreferencesKey("startOnBoot")

        fun Preferences.read(): GlowSettings {
            val d = GlowSettings()
            val nodes = runCatching {
                val a = JSONArray(this[NODES] ?: "[]")
                List(a.length()) { WledNode.fromJson(a.getJSONObject(it)) }
            }.getOrDefault(emptyList())
            return GlowSettings(
                serverUrl = this[SERVER_URL] ?: d.serverUrl,
                accessToken = this[ACCESS_TOKEN] ?: d.accessToken,
                userId = this[USER_ID] ?: d.userId,
                userName = this[USER_NAME] ?: d.userName,
                deviceId = this[DEVICE_ID] ?: d.deviceId,
                followMode = this[FOLLOW_MODE] ?: d.followMode,
                followedDeviceId = this[FOLLOWED_DEVICE_ID] ?: d.followedDeviceId,
                followedDeviceName = this[FOLLOWED_DEVICE_NAME] ?: d.followedDeviceName,
                nodes = nodes,
                glowEnabled = this[GLOW_ENABLED] ?: d.glowEnabled,
                startOnBoot = this[START_ON_BOOT] ?: d.startOnBoot,
            )
        }

        fun MutablePreferences.write(s: GlowSettings) {
            this[SERVER_URL] = s.serverUrl
            this[ACCESS_TOKEN] = s.accessToken
            this[USER_ID] = s.userId
            this[USER_NAME] = s.userName
            if (s.deviceId.isNotBlank()) this[DEVICE_ID] = s.deviceId
            this[FOLLOW_MODE] = s.followMode
            this[FOLLOWED_DEVICE_ID] = s.followedDeviceId
            this[FOLLOWED_DEVICE_NAME] = s.followedDeviceName
            this[NODES] = JSONArray(s.nodes.map { it.toJson() }).toString()
            this[GLOW_ENABLED] = s.glowEnabled
            this[START_ON_BOOT] = s.startOnBoot
        }
    }
}
