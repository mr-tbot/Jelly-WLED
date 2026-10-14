package com.mrtbot.jellywled.net

import android.content.Context
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo
import android.net.wifi.WifiManager
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.callbackFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withContext
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONObject
import java.io.IOException
import java.util.concurrent.TimeUnit
import kotlin.coroutines.resume

data class WledInfo(val name: String, val version: String, val ledCount: Int?)
data class FoundWled(val name: String, val host: String, val port: Int)

/** The small part of WLED's JSON API the bridge uses, and network discovery of WLED nodes. */
object Wled {
    private val http = JellyfinClient.http.newBuilder()
        .connectTimeout(3, TimeUnit.SECONDS)
        .readTimeout(3, TimeUnit.SECONDS)
        .build()

    private fun baseUrl(host: String, port: Int): String {
        var h = host.trim()
        if (h.contains(':') && !h.startsWith("[")) h = "[$h]" // IPv6 literal
        return if (port == 80 || port <= 0) "http://$h" else "http://$h:$port"
    }

    /** GET /json/info. */
    suspend fun info(host: String, port: Int): WledInfo = withContext(Dispatchers.IO) {
        http.newCall(Request.Builder().url(baseUrl(host, port) + "/json/info").build()).execute().use { r ->
            if (!r.isSuccessful) throw IOException("WLED answered HTTP ${r.code}.")
            val o = JSONObject(r.body.string())
            val leds = o.optJSONObject("leds")
            WledInfo(o.optString("name"), o.optString("ver"), leds?.optInt("count", -1)?.takeIf { it >= 0 })
        }
    }

    /** GET /json/state "on", or null when the node does not answer. Blocking. */
    fun isOn(host: String, port: Int): Boolean? = runCatching {
        http.newCall(Request.Builder().url(baseUrl(host, port) + "/json/state").build()).execute().use { r ->
            if (r.isSuccessful) JSONObject(r.body.string()).optBoolean("on") else null
        }
    }.getOrNull()

    /** POST /json/state, for example {"live":false}. Blocking; false on any failure. */
    fun postState(host: String, port: Int, json: String): Boolean = runCatching {
        val body = json.toRequestBody("application/json".toMediaType())
        http.newCall(Request.Builder().url(baseUrl(host, port) + "/json/state").post(body).build()).execute().use { it.isSuccessful }
    }.getOrDefault(false)

    /**
     * Discovers `_wled._tcp` services and resolves them one at a time (older Android allows only one resolve in
     * flight). Holds a Wi-Fi multicast lock while collecting.
     */
    fun discover(context: Context): Flow<FoundWled> = callbackFlow {
        val app = context.applicationContext
        val nsd = app.getSystemService(Context.NSD_SERVICE) as NsdManager
        val wifi = app.getSystemService(Context.WIFI_SERVICE) as WifiManager
        val lock = wifi.createMulticastLock("jellywled-discovery").apply { setReferenceCounted(false); acquire() }
        val found = Channel<NsdServiceInfo>(Channel.UNLIMITED)
        val listener = object : NsdManager.DiscoveryListener {
            override fun onServiceFound(info: NsdServiceInfo) {
                found.trySend(info)
            }

            override fun onStartDiscoveryFailed(serviceType: String, errorCode: Int) {
                close(IOException("Network discovery failed to start (error $errorCode)."))
            }

            override fun onStopDiscoveryFailed(serviceType: String, errorCode: Int) = Unit
            override fun onDiscoveryStarted(serviceType: String) = Unit
            override fun onDiscoveryStopped(serviceType: String) = Unit
            override fun onServiceLost(info: NsdServiceInfo) = Unit
        }
        nsd.discoverServices("_wled._tcp.", NsdManager.PROTOCOL_DNS_SD, listener)
        launch {
            for (info in found) {
                val resolved = resolve(nsd, info) ?: continue
                @Suppress("DEPRECATION")
                val host = resolved.host?.hostAddress ?: continue
                send(FoundWled(resolved.serviceName, host, resolved.port))
            }
        }
        awaitClose {
            runCatching { nsd.stopServiceDiscovery(listener) }
            lock.release()
        }
    }

    @Suppress("DEPRECATION")
    private suspend fun resolve(nsd: NsdManager, info: NsdServiceInfo): NsdServiceInfo? = suspendCancellableCoroutine { c ->
        nsd.resolveService(info, object : NsdManager.ResolveListener {
            override fun onServiceResolved(resolved: NsdServiceInfo) {
                if (c.isActive) c.resume(resolved)
            }

            override fun onResolveFailed(failed: NsdServiceInfo, errorCode: Int) {
                if (c.isActive) c.resume(null)
            }
        })
    }

}
