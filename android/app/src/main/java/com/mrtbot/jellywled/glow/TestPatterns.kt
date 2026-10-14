package com.mrtbot.jellywled.glow

/**
 * Installer patterns, identical to the plugin and the ButterFlyEffect-Player glow feature: "sides" colors each
 * edge (top red, right green, bottom blue, left yellow) with LED 0 white; "chase" walks one white LED around the
 * strip at 20 LEDs per second; "white" lights everything; "off" is black.
 */
object TestPatterns {
    val NAMES = listOf("sides", "chase", "white", "off")

    /** Renders [pattern] for [layout] at [elapsedSeconds] since the start into [output] (layout.size * 3 bytes). */
    fun render(pattern: String, layout: List<LedSpan>, elapsedSeconds: Double, output: ByteArray) {
        val n = layout.size
        output.fill(0, 0, n * 3)
        when (pattern.lowercase()) {
            "chase" -> if (n > 0) {
                val pos = (elapsedSeconds * 20).toInt() % n
                output.fill(0xFF.toByte(), pos * 3, pos * 3 + 3)
            }
            "white" -> output.fill(0xFF.toByte(), 0, n * 3)
            "sides" -> {
                for (i in 0 until n) {
                    val rgb = when (layout[i].side) {
                        Side.TOP -> intArrayOf(255, 0, 0)
                        Side.RIGHT -> intArrayOf(0, 255, 0)
                        Side.BOTTOM -> intArrayOf(0, 60, 255)
                        Side.LEFT -> intArrayOf(255, 200, 0)
                    }
                    output[i * 3] = rgb[0].toByte()
                    output[i * 3 + 1] = rgb[1].toByte()
                    output[i * 3 + 2] = rgb[2].toByte()
                }
                if (n > 0) output.fill(0xFF.toByte(), 0, 3)
            }
        }
    }
}
