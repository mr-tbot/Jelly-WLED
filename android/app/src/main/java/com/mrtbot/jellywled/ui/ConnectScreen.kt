package com.mrtbot.jellywled.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.text.input.KeyboardType
import com.mrtbot.jellywled.data.GlowSettings
import com.mrtbot.jellywled.data.Settings
import com.mrtbot.jellywled.net.JellyfinClient
import kotlinx.coroutines.launch

/** Server address and sign-in, with a connection test that needs no account. */
@Composable
fun ConnectScreen(s: GlowSettings, store: Settings, onSignedIn: () -> Unit) {
    val scope = rememberCoroutineScope()
    var url by rememberSaveable { mutableStateOf(s.serverUrl) }
    var useKey by rememberSaveable { mutableStateOf(s.signedIn && s.userId.isBlank()) }
    var user by rememberSaveable { mutableStateOf(if (s.userId.isNotBlank()) s.userName else "") }
    var password by remember { mutableStateOf("") }
    var apiKey by remember { mutableStateOf("") }
    var busy by remember { mutableStateOf(false) }
    var result by remember { mutableStateOf<Pair<Boolean?, String>?>(null) }

    fun work(block: suspend () -> Pair<Boolean?, String>) {
        busy = true
        result = null to "Working..."
        scope.launch {
            result = try {
                block()
            } catch (e: Exception) {
                false to friendly(e)
            }
            busy = false
        }
    }

    ScreenColumn {
        SectionTitle("Jellyfin server")
        Hint("The server with the Jelly-WLED plugin. This device streams the glow from it to the WLED nodes on this network.")
        if (s.signedIn) {
            Card {
                Outcome("Signed in to ${s.serverUrl} as ${s.userName}", true)
                ButtonRow {
                    GlowButton("Sign out") {
                        scope.launch { store.update { it.copy(accessToken = "", userId = "", userName = "", glowEnabled = false) } }
                        result = null
                    }
                }
            }
        }
        Field("Server address", url, keyboard = KeyboardType.Uri, supporting = "For example http://192.168.1.20:8096") { url = it }
        ToggleRow("Sign in with an API key", useKey, "Instead of a user name and password") { useKey = it }
        if (useKey) {
            Field("API key", apiKey, password = true) { apiKey = it.trim() }
        } else {
            Field("User name", user) { user = it }
            Field("Password", password, keyboard = KeyboardType.Password, password = true) { password = it }
        }
        ButtonRow {
            GlowButton("Test connection", enabled = !busy) {
                work {
                    val info = JellyfinClient(url, null, s.deviceId).publicInfo()
                    true to "Found ${info.serverName.ifBlank { "a Jellyfin server" }}, version ${info.version}."
                }
            }
            GlowButton("Sign in", primary = true, enabled = !busy && (if (useKey) apiKey.isNotBlank() else user.isNotBlank())) {
                work {
                    val token: String
                    val signedIn: String
                    val client: JellyfinClient
                    if (useKey) {
                        client = JellyfinClient(url, apiKey, s.deviceId)
                        val info = client.privateInfo()
                        token = apiKey
                        signedIn = "Signed in to ${info.serverName} with an API key."
                        store.update { it.copy(serverUrl = client.baseUrl, accessToken = token, userId = "", userName = "API key") }
                    } else {
                        client = JellyfinClient(url, null, s.deviceId)
                        val r = client.authenticate(user.trim(), password)
                        token = r.accessToken
                        signedIn = "Signed in as ${r.userName}."
                        store.update { it.copy(serverUrl = client.baseUrl, accessToken = token, userId = r.userId, userName = r.userName) }
                    }
                    password = ""
                    val plugin = runCatching { JellyfinClient(client.baseUrl, token, s.deviceId).sessions() }
                    if (plugin.isSuccess) {
                        onSignedIn()
                        true to "$signedIn The Jelly-WLED plugin answered."
                    } else {
                        false to "$signedIn But the Jelly-WLED plugin did not answer: ${friendly(plugin.exceptionOrNull()!!)}"
                    }
                }
            }
        }
        result?.let { (ok, text) -> Outcome(text, ok) }
    }
}
