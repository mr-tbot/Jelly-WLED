package com.mrtbot.jellywled.net

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** The event payloads exactly as the plugin's EventHub serializes them (camelCase, dashed GUIDs). */
class ParseTest {
    @Test
    fun parsesAStateEvent() {
        val json = """{"sessionId":"abc","deviceId":"dev-1","deviceName":"Living Room","client":"Android TV","userId":"6f1c3d9e-2b4a-4c1e-9d3f-0a1b2c3d4e5f","userName":"tbot","itemId":"0a0b0c0d-0000-0000-0000-000000000001","itemName":"Movie","mediaSourceId":null,"runTimeTicks":72000000000,"positionTicks":600000000,"reportedAtMs":1760000000000,"isPaused":false,"track":{"state":"complete","frames":72000,"expectedFrames":72000,"fps":10,"ringW":32,"ringH":18,"segments":[],"analyzing":null}}"""
        val e = JellyfinClient.parseEvent("state", json, 5) as GlowEvent.State
        val s = e.state
        assertEquals("dev-1", s.deviceId)
        assertEquals("Movie", s.itemName)
        assertNull(s.mediaSourceId) // JSON null must not become the string "null"
        assertEquals(600000000L, s.positionTicks)
        assertEquals(1760000000000L, s.reportedAtMs)
        assertEquals("complete", s.trackState)
        assertEquals(10, s.trackFps)
        // Jellyfin's own endpoints return ids without dashes.
        assertTrue(JellyfinClient.sameId(s.userId, "6F1C3D9E2B4A4C1E9D3F0A1B2C3D4E5F"))
    }

    @Test
    fun parsesHelloPingAndStopped() {
        val hello = JellyfinClient.parseEvent("hello", """{"serverTimeMs":1000,"pluginVersion":"1.0.0.0","analysisFps":10,"ringW":32,"ringH":18}""", 900) as GlowEvent.Hello
        assertEquals(1000L, hello.serverTimeMs)
        assertEquals(900L, hello.receivedAtMs)
        assertEquals(32, hello.ringW)
        val ping = JellyfinClient.parseEvent("ping", """{"serverTimeMs":2000}""", 1950) as GlowEvent.Ping
        assertEquals(2000L, ping.serverTimeMs)
        val stopped = JellyfinClient.parseEvent("stopped", """{"sessionId":"abc","deviceId":"dev-1"}""", 0) as GlowEvent.Stopped
        assertEquals("abc", stopped.sessionId)
        assertNull(JellyfinClient.parseEvent("unknown", "{}", 0))
    }

    @Test
    fun normalizesServerAddresses() {
        assertEquals("http://192.168.1.20:8096/", JellyfinClient.normalizeUrl(" 192.168.1.20:8096/ ").toString())
        assertEquals("https://jf.example.com/jellyfin", JellyfinClient.normalizeUrl("https://jf.example.com/jellyfin/").toString().trimEnd('/'))
        assertNull(JellyfinClient.normalizeUrl(""))
    }
}
