package com.mrtbot.jellywled.glow

import kotlin.math.abs
import kotlin.math.ceil
import kotlin.math.floor
import kotlin.math.pow
import kotlin.math.round

/**
 * Picture colors to strip bytes: saturation, gamma, brightness, then smoothing against the previous frame.
 * PROTOCOL.md section 4; a port of the plugin's ColorPipeline.cs that must stay byte-identical with it.
 *
 * @param saturationPercent color richness, 100 = unchanged (0-300).
 * @param gamma gamma exponent (0.2-5.0).
 * @param brightnessPercent brightness (0-100).
 * @param smoothingPercent how much of the previous frame is kept (0-95).
 */
class ColorPipeline(saturationPercent: Int, gamma: Double, brightnessPercent: Int, smoothingPercent: Int) {
    private val saturation = saturationPercent.coerceIn(0, 300) / 100.0
    private val lut = gammaLut(gamma.coerceIn(0.2, 5.0))
    private val level = brightnessPercent.coerceIn(0, 100) / 100.0
    private val keep = smoothingPercent.coerceIn(0, 95) / 100.0

    /**
     * Processes one frame of raw per-LED RGB (from [LedMapper]) into [output]. [previous] is the previous output
     * frame; null or a different length means no smoothing. [output] must not be the same array as [previous].
     */
    fun process(raw: ByteArray, previous: ByteArray?, output: ByteArray) {
        val n = raw.size / 3
        val hasPrevious = previous != null && previous.size == raw.size
        for (i in 0 until n) {
            var r = raw[i * 3].toInt() and 0xFF
            var g = raw[i * 3 + 1].toInt() and 0xFF
            var b = raw[i * 3 + 2].toInt() and 0xFF
            if (abs(saturation - 1.0) > 1e-9) {
                val gray = 0.299 * r + 0.587 * g + 0.114 * b
                r = (gray + (r - gray) * saturation).toInt().coerceIn(0, 255)
                g = (gray + (g - gray) * saturation).toInt().coerceIn(0, 255)
                b = (gray + (b - gray) * saturation).toInt().coerceIn(0, 255)
            }

            var cr = (lut[r] * level).toInt()
            var cg = (lut[g] * level).toInt()
            var cb = (lut[b] * level).toInt()
            if (hasPrevious && keep > 0.0) {
                cr = step(previous[i * 3].toInt() and 0xFF, cr)
                cg = step(previous[i * 3 + 1].toInt() and 0xFF, cg)
                cb = step(previous[i * 3 + 2].toInt() and 0xFF, cb)
            }

            output[i * 3] = cr.toByte()
            output[i * 3 + 1] = cg.toByte()
            output[i * 3 + 2] = cb.toByte()
        }
    }

    private fun step(previous: Int, current: Int): Int {
        val v = previous + (current - previous) * (1.0 - keep)
        // Always move at least one level toward the target so heavy smoothing still reaches black or white.
        return if (current > previous) ceil(v).toInt() else floor(v).toInt()
    }

    companion object {
        /**
         * The 256-entry gamma table. kotlin.math.round rounds half to even, exactly like C#'s Math.Round,
         * which the plugin uses; roundToInt would round half up and break byte parity.
         */
        fun gammaLut(gamma: Double): IntArray = IntArray(256) { round(255.0 * (it / 255.0).pow(gamma)).toInt() }
    }
}
