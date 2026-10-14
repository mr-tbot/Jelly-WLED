package com.mrtbot.jellywled.glow

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.IOException

/** Port of the plugin's GlowTests.cs: the same vectors must pass on both sides. */
class GlowTest {
    private fun bytes(vararg v: Int) = ByteArray(v.size) { v[it].toByte() }
    private fun ByteArray.slice(from: Int, to: Int) = copyOfRange(from, to)

    @Test
    fun layoutClockwiseTopLeftWalksTopThenRightThenBottomThenLeft() {
        val layout = RingLayout.build(2, 1, 2, 1, "top_left", "cw")
        assertEquals(6, layout.size)
        assertEquals(LedSpan(Side.TOP, 0.0, 0.5), layout[0])
        assertEquals(LedSpan(Side.TOP, 0.5, 1.0), layout[1])
        assertEquals(LedSpan(Side.RIGHT, 0.0, 1.0), layout[2])
        // Bottom runs right to left: the first bottom LED covers the right half.
        assertEquals(LedSpan(Side.BOTTOM, 0.5, 1.0), layout[3])
        assertEquals(LedSpan(Side.BOTTOM, 0.0, 0.5), layout[4])
        assertEquals(LedSpan(Side.LEFT, 0.0, 1.0), layout[5])
    }

    @Test
    fun layoutCounterClockwiseBottomRightStartsOnRightWalkingUp() {
        // ccw order is left (top->bottom), bottom (left->right), right (bottom->top), top (right->left);
        // starting at bottom_right means right first (walking up), then top, then left, then bottom.
        val layout = RingLayout.build(1, 2, 1, 2, "bottom_right", "ccw")
        assertEquals(Side.RIGHT, layout[0].side)
        assertEquals(LedSpan(Side.RIGHT, 0.5, 1.0), layout[0])
        assertEquals(LedSpan(Side.RIGHT, 0.0, 0.5), layout[1])
        assertEquals(Side.TOP, layout[2].side)
        assertEquals(Side.LEFT, layout[3].side)
        assertEquals(LedSpan(Side.LEFT, 0.0, 0.5), layout[3])
        assertEquals(Side.BOTTOM, layout[5].side)
    }

    @Test
    fun zoneIndexFollowsRingOrder() {
        val w = 4
        val h = 2 // ring: top 0-3, right 4-5, bottom 6-9 (right to left), left 10-11 (bottom to top)
        assertEquals(0, RingLayout.zoneIndex(Side.TOP, 0, w, h))
        assertEquals(3, RingLayout.zoneIndex(Side.TOP, 3, w, h))
        assertEquals(4, RingLayout.zoneIndex(Side.RIGHT, 0, w, h))
        assertEquals(9, RingLayout.zoneIndex(Side.BOTTOM, 0, w, h)) // leftmost bottom zone is last in ring order
        assertEquals(6, RingLayout.zoneIndex(Side.BOTTOM, 3, w, h))
        assertEquals(11, RingLayout.zoneIndex(Side.LEFT, 0, w, h)) // top-most left zone is last
        assertEquals(10, RingLayout.zoneIndex(Side.LEFT, 1, w, h))
    }

    @Test
    fun mapperAveragesOverlappingZonesAndSidesLandOnTheRightSides() {
        val w = 4
        val h = 2
        val ring = ByteArray(RingLayout.zoneCount(w, h) * 3)
        fun set(zone: Int, r: Int, g: Int, b: Int) {
            ring[zone * 3] = r.toByte(); ring[zone * 3 + 1] = g.toByte(); ring[zone * 3 + 2] = b.toByte()
        }
        for (k in 0 until w) {
            set(RingLayout.zoneIndex(Side.TOP, k, w, h), 255, 0, 0)
            set(RingLayout.zoneIndex(Side.BOTTOM, k, w, h), 0, 0, 255)
        }
        for (k in 0 until h) {
            set(RingLayout.zoneIndex(Side.RIGHT, k, w, h), 0, 255, 0)
            set(RingLayout.zoneIndex(Side.LEFT, k, w, h), 255, 255, 0)
        }

        val layout = RingLayout.build(2, 1, 2, 1, "top_left", "cw")
        val mapper = LedMapper(layout, w, h)
        val leds = ByteArray(mapper.ledCount * 3)
        mapper.map(ring, leds)
        assertArrayEquals(bytes(255, 0, 0), leds.slice(0, 3))
        assertArrayEquals(bytes(0, 255, 0), leds.slice(6, 9))
        assertArrayEquals(bytes(0, 0, 255), leds.slice(9, 12))
        assertArrayEquals(bytes(255, 255, 0), leds.slice(15, 18))

        val avg = LedMapper(layout, w, h, average = true)
        avg.map(ring, leds)
        // 4 red + 4 blue + 2 green + 2 yellow zones over 12: R=(4*255+2*255)/12=127, G=(2*255+2*255)/12=85, B=4*255/12=85
        assertArrayEquals(bytes(127, 85, 85), leds.slice(0, 3))
    }

    @Test
    fun pipelineAppliesSaturationGammaBrightnessAndSmoothingStepsTowardTarget() {
        val raw = bytes(200, 100, 50)
        val output = ByteArray(3)
        ColorPipeline(100, 1.0, 100, 0).process(raw, null, output)
        assertArrayEquals(raw, output)

        ColorPipeline(100, 1.0, 50, 0).process(raw, null, output)
        assertArrayEquals(bytes(100, 50, 25), output)

        ColorPipeline(100, 1.0, 100, 50).process(raw, bytes(0, 0, 0), output)
        assertArrayEquals(bytes(100, 50, 25), output)

        // Heavy smoothing still moves at least one level toward the target.
        ColorPipeline(100, 1.0, 100, 95).process(bytes(0, 0, 0), bytes(1, 1, 1), output)
        assertArrayEquals(bytes(0, 0, 0), output)

        ColorPipeline(100, 2.2, 100, 0).process(bytes(128, 255, 0), null, output)
        assertEquals(56, output[0].toInt() and 0xFF)
        assertEquals(255, output[1].toInt() and 0xFF)
        assertEquals(0, output[2].toInt() and 0xFF)

        ColorPipeline(0, 1.0, 100, 0).process(bytes(255, 0, 0), null, output)
        assertArrayEquals(bytes(76, 76, 76), output) // gray of pure red = 0.299 * 255
    }

    @Test
    fun gammaTableMatchesTheReferenceBankersRounding() {
        // Generated with Python (glibc pow, round-half-even), the same rounding as C#'s Math.Round.
        val expected = "000000000000000000000000000000010101010101010101010202020202020203030303030404040405050505060606060707070808080909090a0a0b0b0b0c0c0d0d0d0e0e0f0f1010111112121313141415161617171819191a1a1b1c1c1d1e1e1f202121222323242526272728292a2b2b2c2d2e2f30313132333435363738393a3b3c3d3e3f4041424344454647494a4b4c4d4e4f51525354555758595a5b5d5e5f616263646667696a6b6d6e6f717274757778797b7c7e7f8182848587898a8c8d8f9192949597999a9c9e9fa1a3a5a6a8aaacadafb1b3b5b6b8babcbec0c2c4c5c7c9cbcdcfd1d3d5d7d9dbdddfe1e3e5e7eaeceef0f2f4f6f8fbfdff"
        val actual = ColorPipeline.gammaLut(2.2).joinToString("") { "%02x".format(it) }
        assertEquals(expected, actual)
        // Half-even rounding at an exact midpoint: 255 * (i/255)^1 can never be .5, but round(2.5) must be 2.
        assertEquals(2.0, kotlin.math.round(2.5), 0.0)
    }

    @Test
    fun ddpSplitsAt1440BytesWithPushOnLastPacket() {
        val frame = ByteArray(1500 * 3) // 1500 LEDs = 4500 bytes -> 1440, 1440, 1440, 180
        frame[0] = 7
        frame[4499] = 9
        val packets = Ddp.build(frame, 3)
        assertEquals(4, packets.size)
        assertEquals(0x40, packets[0][0].toInt())
        assertEquals(3, packets[0][1].toInt())
        assertEquals(0x0B, packets[0][2].toInt())
        assertEquals(1, packets[0][3].toInt())
        assertArrayEquals(bytes(0, 0, 0, 0, 0x05, 0xA0), packets[0].slice(4, 10))
        assertEquals(7, packets[0][10].toInt())
        assertEquals(0x41, packets[3][0].toInt())
        assertEquals(6, packets[3][1].toInt())
        assertArrayEquals(bytes(0, 0, 0x10, 0xE0, 0, 180), packets[3].slice(4, 10))
        assertEquals(9, packets[3].last().toInt())
        assertEquals(Ddp.HEADER_LENGTH + 180, packets[3].size)

        val empty = Ddp.build(ByteArray(0))
        assertEquals(1, empty.size)
        assertEquals(0x41, empty[0][0].toInt())
    }

    @Test
    fun segmentHeaderRoundTrips() {
        val h = SegmentHeader(
            ringWidth = 32, ringHeight = 18, fps = 10, flags = SegmentHeader.FLAG_FINALIZED,
            startFrame = 1234, frameCount = 56, sourceSize = 1_234_567_890_123, sourceMtimeMs = 1_760_000_000_000,
            bandPermille = 80, cropX = 0, cropY = 128, cropW = 1000, cropH = 744,
        )
        val bytes = h.toBytes()
        assertEquals(64, bytes.size)
        assertEquals("JWGLOW01", String(bytes, 0, 8, Charsets.US_ASCII))
        val back = SegmentHeader.parse(bytes)
        assertEquals(32, back.ringWidth)
        assertEquals(18, back.ringHeight)
        assertEquals(10, back.fps)
        assertTrue(back.isFinalized)
        assertEquals(1234L, back.startFrame)
        assertEquals(56L, back.frameCount)
        assertEquals(1_234_567_890_123L, back.sourceSize)
        assertEquals(1_760_000_000_000L, back.sourceMtimeMs)
        assertEquals(80, back.bandPermille)
        assertEquals(128, back.cropY)
        assertEquals(744, back.cropH)
        assertEquals(300, back.frameBytes)
        assertEquals(h, back)
        try {
            SegmentHeader.parse(ByteArray(64))
            throw AssertionError("expected IOException")
        } catch (_: IOException) {
        }
        // Unsigned fields survive values above the signed range.
        val big = h.copy(startFrame = 0xFFFF_FFF0L, cropH = 65_000)
        assertEquals(big, SegmentHeader.parse(big.toBytes()))
    }

    @Test
    fun testPatternSidesPaintsEdgesAndLedZeroWhite() {
        val layout = RingLayout.build(2, 1, 2, 1, "top_left", "cw")
        val output = ByteArray(layout.size * 3)
        TestPatterns.render("sides", layout, 0.0, output)
        assertArrayEquals(bytes(255, 255, 255), output.slice(0, 3))
        assertArrayEquals(bytes(255, 0, 0), output.slice(3, 6))
        assertArrayEquals(bytes(0, 255, 0), output.slice(6, 9))
        assertArrayEquals(bytes(0, 60, 255), output.slice(9, 12))
        assertArrayEquals(bytes(255, 200, 0), output.slice(15, 18))
        TestPatterns.render("chase", layout, 0.1, output) // 0.1 s * 20 = LED 2
        assertArrayEquals(bytes(255, 255, 255), output.slice(6, 9))
        assertEquals(0, output[0].toInt())
        TestPatterns.render("white", layout, 0.0, output)
        assertTrue(output.all { it == 0xFF.toByte() })
        TestPatterns.render("off", layout, 0.0, output)
        assertTrue(output.all { it == 0.toByte() })
    }

    @Test
    fun referenceSegmentFromGlowtrackParsesAndMapsToTheSidesColors() {
        // tools/glowtrack.py write ref.seg --ring 32x18 --fps 10 --frames 5 --pattern sides
        val file = javaClass.classLoader!!.getResourceAsStream("ref.seg")!!.readBytes()
        val h = SegmentHeader.parse(file)
        assertEquals(32, h.ringWidth)
        assertEquals(18, h.ringHeight)
        assertEquals(10, h.fps)
        assertTrue(h.isFinalized)
        assertEquals(0L, h.startFrame)
        assertEquals(5L, h.frameCount)
        assertEquals(80, h.bandPermille)
        assertEquals(1000, h.cropW)
        assertEquals(1000, h.cropH)
        assertEquals(5, (file.size - SegmentHeader.SIZE) / h.frameBytes)

        val buffer = TrackBuffer(h)
        for (i in 0 until 5) {
            val at = SegmentHeader.SIZE + i * h.frameBytes
            buffer.append(file.copyOfRange(at, at + h.frameBytes))
        }
        val ring = ByteArray(h.frameBytes)
        assertTrue(buffer.tryGet(4, ring))

        val layout = RingLayout.build(60, 34, 60, 34, "top_left", "cw")
        val leds = ByteArray(layout.size * 3)
        LedMapper(layout, h.ringWidth, h.ringHeight).map(ring, leds)
        val pattern = ByteArray(layout.size * 3)
        TestPatterns.render("sides", layout, 0.0, pattern)
        // Every LED shows its side's color, which is what the sides pattern paints (LED 0 aside, it is white there).
        assertArrayEquals(pattern.copyOfRange(3, pattern.size), leds.copyOfRange(3, leds.size))
        assertArrayEquals(bytes(255, 0, 0), leds.slice(0, 3))
        assertArrayEquals(bytes(0, 255, 0), leds.slice(60 * 3, 61 * 3))
        assertArrayEquals(bytes(0, 60, 255), leds.slice(94 * 3, 95 * 3))
        assertArrayEquals(bytes(255, 200, 0), leds.slice(154 * 3, 155 * 3))
    }
}
