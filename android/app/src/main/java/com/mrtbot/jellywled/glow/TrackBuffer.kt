package com.mrtbot.jellywled.glow

/**
 * The frames received from one or more consecutive track streams: a contiguous run starting at the absolute frame
 * [startFrame]. The stream reader appends, the render loop reads; both are synchronized.
 * ponytail: one array per frame and nothing is ever dropped, about 11 MB per hour at 32x18 and 10 fps; trim
 * frames far behind the position if weak TV boxes run short of memory on very long items.
 */
class TrackBuffer(val header: SegmentHeader) {
    private val frames = ArrayList<ByteArray>()

    val startFrame: Long get() = header.startFrame

    /** One past the last frame held. */
    val endFrame: Long @Synchronized get() = startFrame + frames.size

    @Synchronized
    fun append(frame: ByteArray) {
        require(frame.size == header.frameBytes) { "Frame has ${frame.size} bytes, expected ${header.frameBytes}." }
        frames += frame
    }

    fun covers(frame: Long): Boolean = frame >= startFrame && frame < endFrame

    /** Copies absolute frame [frame] into [out]; false when it is not held. */
    @Synchronized
    fun tryGet(frame: Long, out: ByteArray): Boolean {
        val i = frame - startFrame
        if (i < 0 || i >= frames.size) return false
        frames[i.toInt()].copyInto(out)
        return true
    }
}
