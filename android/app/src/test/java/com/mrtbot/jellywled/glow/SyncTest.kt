package com.mrtbot.jellywled.glow

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class SyncTest {
    @Test
    fun positionExtrapolatesWithTheServerClockAndHoldsWhilePaused() {
        val clock = SyncClock()
        assertNull(clock.offsetMs)
        // The server is 1000 ms ahead; the hello took 40 ms round trip and arrived 20 ms after it was stamped.
        clock.hello(serverTimeMs = 101_000, receivedAtMs = 100_020, rttMs = 40)
        assertEquals(1000L, clock.offsetMs)
        // Reported at server time 101 500 at 60 s; local now 101 000 = server 102 000, so 500 ms later.
        val ticks = 60_000L * 10_000
        assertEquals(60_500L, clock.positionMs(ticks, 101_500, false, 101_000))
        assertEquals(60_000L, clock.positionMs(ticks, 101_500, true, 101_000))
    }

    @Test
    fun theFastestRecentSampleWinsAndOldSamplesAge() {
        val clock = SyncClock(window = 3)
        clock.hello(10_000, 9_100, rttMs = 100) // low bound 900, + 50 = 950
        clock.ping(15_000, 14_200) // a slow ping (low bound 800) does not lower the estimate
        assertEquals(950L, clock.offsetMs)
        clock.ping(20_000, 19_090) // a faster one raises it
        assertEquals(960L, clock.offsetMs)
        clock.ping(25_000, 24_200)
        clock.ping(30_000, 29_200)
        clock.ping(35_000, 34_200) // the fast samples left the window of 3
        assertEquals(850L, clock.offsetMs)
    }

    @Test
    fun frameIndexFloorsAndAppliesTheSyncOffset() {
        assertEquals(10L, SyncClock.frameIndex(1_000, 0, 10))
        assertEquals(10L, SyncClock.frameIndex(1_099, 0, 10))
        assertEquals(11L, SyncClock.frameIndex(1_000, 100, 10))
        assertEquals(-1L, SyncClock.frameIndex(0, -50, 10)) // before the start: no frame, never frame 0
        assertEquals(1L, SyncClock.frameIndex(42, 0, 24))
        // msForFrame is the smallest fromMs the server maps back to the same frame.
        for (fps in listOf(10, 24, 25, 30)) {
            for (f in 0L..200L) {
                val ms = SyncClock.msForFrame(f, fps)
                assertEquals(f, Math.floorDiv(ms * fps, 1000L))
                assertEquals(f - 1, Math.floorDiv((ms - 1) * fps, 1000L))
            }
        }
    }

    @Test
    fun trackBufferHoldsAContiguousRunFromItsStartFrame() {
        val h = SegmentHeader(ringWidth = 2, ringHeight = 1, fps = 10, startFrame = 100)
        val buffer = TrackBuffer(h)
        val out = ByteArray(h.frameBytes)
        assertFalse(buffer.tryGet(100, out))
        assertEquals(100L, buffer.endFrame)
        buffer.append(ByteArray(h.frameBytes) { 1 })
        buffer.append(ByteArray(h.frameBytes) { 2 })
        assertEquals(102L, buffer.endFrame)
        assertTrue(buffer.covers(101))
        assertFalse(buffer.covers(102))
        assertFalse(buffer.covers(99))
        assertTrue(buffer.tryGet(101, out))
        assertTrue(out.all { it == 2.toByte() })
        assertFalse(buffer.tryGet(99, out))
        try {
            buffer.append(ByteArray(3))
            throw AssertionError("expected a size check")
        } catch (_: IllegalArgumentException) {
        }
    }
}
