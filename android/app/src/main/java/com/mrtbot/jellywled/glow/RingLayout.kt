package com.mrtbot.jellywled.glow

import kotlin.math.ceil
import kotlin.math.floor
import kotlin.math.max

/** One edge of the picture, seen from the front of the TV. */
enum class Side { TOP, RIGHT, BOTTOM, LEFT }

/**
 * One LED of the strip: the edge it sits on and the fraction of that edge it covers, measured in picture
 * coordinates (left to right for top and bottom, top to bottom for left and right). PROTOCOL.md section 3.
 */
data class LedSpan(val side: Side, val t0: Double, val t1: Double)

/**
 * Builds LED layouts and locates ring zones. A line-for-line port of the plugin's RingLayout.cs: both sides
 * must produce identical layouts, so keep the arithmetic exactly as it is there.
 */
object RingLayout {
    /**
     * Builds the LED order for a strip with the given LED counts per edge. [startCorner] is where LED 0 sits
     * (top_left, top_right, bottom_right or bottom_left) and [direction] is cw or ccw as seen from the front.
     */
    fun build(top: Int, right: Int, bottom: Int, left: Int, startCorner: String, direction: String): List<LedSpan> {
        val counts = mapOf(
            Side.TOP to max(0, top),
            Side.RIGHT to max(0, right),
            Side.BOTTOM to max(0, bottom),
            Side.LEFT to max(0, left),
        )
        val corner = startCorner.lowercase()
        // Clockwise from the top-left corner: top left->right, right top->bottom, bottom right->left,
        // left bottom->top. The flag says the side is walked against its screen axis.
        val order: List<Pair<Side, Boolean>>
        val start: Int
        if (direction.equals("ccw", ignoreCase = true)) {
            order = listOf(Side.LEFT to false, Side.BOTTOM to false, Side.RIGHT to true, Side.TOP to true)
            start = when (corner) {
                "bottom_left" -> 1
                "bottom_right" -> 2
                "top_right" -> 3
                else -> 0
            }
        } else {
            order = listOf(Side.TOP to false, Side.RIGHT to false, Side.BOTTOM to true, Side.LEFT to true)
            start = when (corner) {
                "top_right" -> 1
                "bottom_right" -> 2
                "bottom_left" -> 3
                else -> 0
            }
        }

        val result = ArrayList<LedSpan>()
        for (i in 0 until 4) {
            val (side, reversed) = order[(start + i) % 4]
            val n = counts.getValue(side)
            for (k in 0 until n) {
                val a = k.toDouble() / n
                val b = (k + 1).toDouble() / n
                result += if (reversed) LedSpan(side, 1.0 - b, 1.0 - a) else LedSpan(side, a, b)
            }
        }
        return result
    }

    /** The number of ring zones for a ring size. */
    fun zoneCount(ringWidth: Int, ringHeight: Int): Int = 2 * ringWidth + 2 * ringHeight

    /**
     * The ring index of zone [k] on [side], where [k] counts in picture direction (left to right or top to
     * bottom). Ring order is clockwise from top-left: top (left to right), right (top to bottom),
     * bottom (right to left), left (bottom to top).
     */
    fun zoneIndex(side: Side, k: Int, ringWidth: Int, ringHeight: Int): Int = when (side) {
        Side.TOP -> k
        Side.RIGHT -> ringWidth + k
        Side.BOTTOM -> ringWidth + ringHeight + (ringWidth - 1 - k)
        Side.LEFT -> 2 * ringWidth + ringHeight + (ringHeight - 1 - k)
    }

    /** The ring zone indices overlapping [span] on its side (at least one). */
    fun zonesFor(span: LedSpan, ringWidth: Int, ringHeight: Int): IntArray {
        val n = if (span.side == Side.TOP || span.side == Side.BOTTOM) ringWidth else ringHeight
        val z0 = floor(span.t0 * n).toInt().coerceIn(0, max(0, n - 1))
        val z1 = ceil(span.t1 * n).toInt().coerceIn(z0 + 1, n)
        return IntArray(z1 - z0) { zoneIndex(span.side, z0 + it, ringWidth, ringHeight) }
    }
}
