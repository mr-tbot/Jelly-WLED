package com.mrtbot.jellywled.glow

/**
 * Estimates the server clock and extrapolates the playback position (PROTOCOL.md section 2).
 *
 * Every hello and ping carries the server's wall clock at send time, so `serverTimeMs - receivedAtMs` is the
 * clock offset minus that message's one-way delay: a lower bound of the offset. The largest bound among recent
 * samples belongs to the fastest message (the min-RTT selection). The one-way delay it still contains is
 * estimated as half the smallest HTTP round trip measured when the event stream opened (request sent to hello
 * received), and added back. Recent samples only, so slow drift between the two clocks is followed.
 *
 * Local times come from [nowMs], a monotonic clock, so a phone that steps or slews its wall clock (NTP, carrier
 * time) does not move the lights; only the server's clock is a wall clock.
 */
class SyncClock(private val window: Int = 12) {
    private val lows = ArrayDeque<Long>()
    private val rtts = ArrayDeque<Long>()

    /** Records a hello: the server time, when it arrived, and the round trip of the request that opened the stream. */
    @Synchronized
    fun hello(serverTimeMs: Long, receivedAtMs: Long, rttMs: Long?) {
        if (rttMs != null && rttMs >= 0) {
            rtts.addLast(rttMs)
            if (rtts.size > 5) rtts.removeFirst()
        }
        ping(serverTimeMs, receivedAtMs)
    }

    /** Records a ping: the server time and when it arrived. */
    @Synchronized
    fun ping(serverTimeMs: Long, receivedAtMs: Long) {
        lows.addLast(serverTimeMs - receivedAtMs)
        if (lows.size > window) lows.removeFirst()
    }

    /** Server clock minus local clock in milliseconds, or null before the first sample. */
    val offsetMs: Long?
        @Synchronized get() = lows.maxOrNull()?.let { it + (rtts.minOrNull() ?: 0L) / 2 }

    /**
     * The playback position at local monotonic time [nowMs]: the reported position plus the time since the report on the
     * server clock, or just the reported position while paused.
     */
    fun positionMs(positionTicks: Long, reportedAtMs: Long, isPaused: Boolean, nowMs: Long): Long {
        val reported = positionTicks / 10_000
        return if (isPaused) reported else reported + (nowMs + (offsetMs ?: 0L) - reportedAtMs)
    }

    companion object {
        /** The local clock every sample and position uses: monotonic milliseconds, unrelated to the wall clock. */
        fun nowMs(): Long = System.nanoTime() / 1_000_000

        /** frame = floor((positionMs + syncOffsetMs) * fps / 1000). */
        fun frameIndex(positionMs: Long, syncOffsetMs: Long, fps: Int): Long =
            Math.floorDiv((positionMs + syncOffsetMs) * fps, 1000L)

        /** The smallest fromMs whose frame (floor(fromMs * fps / 1000)) is [frame]; used to request a stream at a frame. */
        fun msForFrame(frame: Long, fps: Int): Long = Math.floorDiv(frame * 1000 + fps - 1, fps.toLong())
    }
}
