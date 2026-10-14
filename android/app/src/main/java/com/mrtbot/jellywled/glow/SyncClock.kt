package com.mrtbot.jellywled.glow

/**
 * Estimates the server clock and extrapolates the playback position (PROTOCOL.md section 2).
 *
 * Every hello and ping carries the server's wall clock at send time, so `serverTimeMs - receivedAtMs` is the
 * clock offset minus that message's one-way delay. Following the protocol, the offset is the sample with the
 * smallest delay, which is the largest of these values, among recent samples only so slow drift between the two
 * clocks is followed. The remaining error is the fastest message's one-way delay: the lights run that much late
 * (milliseconds on a LAN), which the per-node sync offset covers on long paths. No round-trip correction is added:
 * on Wi-Fi the request round trip is inflated by power save and server work and over-corrects (measured 180 ms
 * early against a real Jellyfin from a phone).
 *
 * Local times come from [nowMs], a monotonic clock, so a phone that steps or slews its wall clock (NTP, carrier
 * time) does not move the lights; only the server's clock is a wall clock.
 */
class SyncClock(private val window: Int = 12) {
    private val lows = ArrayDeque<Long>()

    /** Records a hello or ping: the server time it carries and the local monotonic time it arrived. */
    @Synchronized
    fun sample(serverTimeMs: Long, receivedAtMs: Long) {
        lows.addLast(serverTimeMs - receivedAtMs)
        if (lows.size > window) lows.removeFirst()
    }

    /** Server clock minus local clock in milliseconds, or null before the first sample. */
    val offsetMs: Long?
        @Synchronized get() = lows.maxOrNull()

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
