package com.mrtbot.jellywled.glow

import java.io.IOException
import java.nio.ByteBuffer
import java.nio.ByteOrder

/**
 * The 64-byte little-endian header of a glow track segment and of a track stream (PROTOCOL.md sections 1.2
 * and 1.3). Unsigned 16-bit fields are held in Int, unsigned 32-bit fields in Long.
 */
data class SegmentHeader(
    val ringWidth: Int,
    val ringHeight: Int,
    val fps: Int,
    val flags: Int = 0,
    val startFrame: Long = 0,
    val frameCount: Long = 0,
    val sourceSize: Long = 0,
    val sourceMtimeMs: Long = 0,
    val bandPermille: Int = 0,
    val cropX: Int = 0,
    val cropY: Int = 0,
    val cropW: Int = 1000,
    val cropH: Int = 1000,
) {
    val isFinalized: Boolean get() = flags and FLAG_FINALIZED != 0
    val zoneCount: Int get() = 2 * ringWidth + 2 * ringHeight
    val frameBytes: Int get() = zoneCount * 3

    fun toBytes(): ByteArray {
        val b = ByteBuffer.allocate(SIZE).order(ByteOrder.LITTLE_ENDIAN)
        b.put(MAGIC)
        b.putShort(8, ringWidth.toShort())
        b.putShort(10, ringHeight.toShort())
        b.putShort(12, fps.toShort())
        b.putShort(14, flags.toShort())
        b.putInt(16, startFrame.toInt())
        b.putInt(20, frameCount.toInt())
        b.putLong(24, sourceSize)
        b.putLong(32, sourceMtimeMs)
        b.putShort(40, bandPermille.toShort())
        b.putShort(44, cropX.toShort())
        b.putShort(46, cropY.toShort())
        b.putShort(48, cropW.toShort())
        b.putShort(50, cropH.toShort())
        return b.array()
    }

    companion object {
        const val SIZE = 64
        const val FLAG_FINALIZED = 0x0001
        private val MAGIC = "JWGLOW01".toByteArray(Charsets.US_ASCII)

        /** Parses a header from the first 64 bytes of [bytes]; throws [IOException] on bad magic or an empty ring. */
        fun parse(bytes: ByteArray): SegmentHeader {
            if (bytes.size < SIZE || !bytes.copyOf(8).contentEquals(MAGIC)) throw IOException("Not a glow track segment.")
            val b = ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN)
            fun u16(at: Int) = b.getShort(at).toInt() and 0xFFFF
            fun u32(at: Int) = b.getInt(at).toLong() and 0xFFFFFFFFL
            val h = SegmentHeader(
                ringWidth = u16(8),
                ringHeight = u16(10),
                fps = u16(12),
                flags = u16(14),
                startFrame = u32(16),
                frameCount = u32(20),
                sourceSize = b.getLong(24),
                sourceMtimeMs = b.getLong(32),
                bandPermille = u16(40),
                cropX = u16(44),
                cropY = u16(46),
                cropW = u16(48),
                cropH = u16(50),
            )
            if (h.ringWidth == 0 || h.ringHeight == 0 || h.fps == 0) {
                throw IOException("Glow track segment has an empty ring or zero fps.")
            }
            return h
        }
    }
}
