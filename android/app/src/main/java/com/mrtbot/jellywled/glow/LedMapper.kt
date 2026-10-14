package com.mrtbot.jellywled.glow

import kotlin.math.max

/**
 * Turns a ring frame (zone colors) into one raw RGB color per LED for a specific strip layout. The zone lists
 * are computed once; [map] does not allocate. Port of the plugin's LedMapper.cs.
 *
 * @param average true for "average" mode: every LED gets the mean of the whole ring.
 */
class LedMapper(layout: List<LedSpan>, ringWidth: Int, ringHeight: Int, private val average: Boolean = false) {
    private val zones = Array(layout.size) { RingLayout.zonesFor(layout[it], ringWidth, ringHeight) }

    /** The number of ring zones expected in a frame. */
    val zoneCount = RingLayout.zoneCount(ringWidth, ringHeight)

    /** The number of LEDs. */
    val ledCount: Int get() = zones.size

    /** Maps [ring] (zoneCount * 3 bytes) into [output] (ledCount * 3 bytes). */
    fun map(ring: ByteArray, output: ByteArray) {
        require(ring.size >= zoneCount * 3) { "Ring frame too short." }
        require(output.size >= zones.size * 3) { "Output buffer too short." }

        if (average) {
            var r = 0
            var g = 0
            var b = 0
            for (z in 0 until zoneCount) {
                r += ring[z * 3].toInt() and 0xFF
                g += ring[z * 3 + 1].toInt() and 0xFF
                b += ring[z * 3 + 2].toInt() and 0xFF
            }
            val n = max(1, zoneCount)
            for (i in zones.indices) {
                output[i * 3] = (r / n).toByte()
                output[i * 3 + 1] = (g / n).toByte()
                output[i * 3 + 2] = (b / n).toByte()
            }
            return
        }

        for (i in zones.indices) {
            val list = zones[i]
            var r = 0
            var g = 0
            var b = 0
            for (z in list) {
                r += ring[z * 3].toInt() and 0xFF
                g += ring[z * 3 + 1].toInt() and 0xFF
                b += ring[z * 3 + 2].toInt() and 0xFF
            }
            val n = max(1, list.size)
            output[i * 3] = (r / n).toByte()
            output[i * 3 + 1] = (g / n).toByte()
            output[i * 3 + 2] = (b / n).toByte()
        }
    }
}
