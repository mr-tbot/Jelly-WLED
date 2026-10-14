package com.mrtbot.jellywled.glow

import java.io.Closeable
import java.io.IOException
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import kotlin.concurrent.thread
import kotlin.math.max
import kotlin.math.min

/**
 * Splits one RGB frame into DDP packets (PROTOCOL.md section 5): 10-byte header, version 1, push flag on the last
 * packet, RGB 8-bit data type, destination 1, big-endian byte offset and length, at most 1440 data bytes.
 * Port of the plugin's DdpPacketizer.cs.
 */
object Ddp {
    const val HEADER_LENGTH = 10

    /** Maximum data bytes per packet (480 RGB LEDs), what WLED accepts. */
    const val MAX_DATA_PER_PACKET = 1440

    /** Builds the packets for one frame, starting at [sequence] (0-15). A zero-length frame yields one empty push packet. */
    fun build(rgb: ByteArray, sequence: Int = 0, maxData: Int = MAX_DATA_PER_PACKET): List<ByteArray> {
        val packets = ArrayList<ByteArray>()
        val total = rgb.size
        var offset = 0
        var seq = sequence and 0x0F
        do {
            val len = min(maxData, total - offset)
            val last = offset + len >= total
            val packet = ByteArray(HEADER_LENGTH + len)
            packet[0] = (0x40 or if (last) 0x01 else 0x00).toByte()
            packet[1] = seq.toByte()
            packet[2] = 0x0B
            packet[3] = 0x01
            packet[4] = (offset ushr 24).toByte()
            packet[5] = (offset ushr 16).toByte()
            packet[6] = (offset ushr 8).toByte()
            packet[7] = offset.toByte()
            packet[8] = (len ushr 8).toByte()
            packet[9] = len.toByte()
            rgb.copyInto(packet, HEADER_LENGTH, offset, offset + len)
            packets += packet
            offset += max(1, len)
            seq = (seq + 1) and 0x0F
        } while (offset < total)
        return packets
    }
}

/**
 * Sends DDP frames to one WLED node. [send] never blocks on DNS: a host name is resolved on a background thread
 * and frames are dropped until it resolves (retried every 5 seconds after a failure). Not thread safe; the
 * service's single render loop owns each sender.
 */
class UdpSender(private val host: String, port: Int) : Closeable {
    private val port = if (port <= 0) 4048 else port
    private var socket: DatagramSocket? = null
    private var sequence = 0

    @Volatile private var address: InetAddress? = null
    @Volatile private var resolving = false
    @Volatile private var retryAtMs = 0L

    /** The last error, or null after a successful send. */
    @Volatile var lastError: String? = null
        private set

    /** Sends one frame; returns true when every packet was handed to the socket. */
    fun send(rgb: ByteArray): Boolean {
        val target = address
        if (target == null) {
            resolve()
            return false
        }
        return try {
            val s = socket ?: DatagramSocket().also { socket = it }
            val packets = Ddp.build(rgb, sequence)
            sequence = (sequence + packets.size) and 0x0F
            for (p in packets) s.send(DatagramPacket(p, p.size, target, port))
            lastError = null
            true
        } catch (e: IOException) {
            lastError = e.message ?: e.javaClass.simpleName
            close()
            address = null
            retryAtMs = System.currentTimeMillis() + 5_000
            false
        }
    }

    private fun resolve() {
        if (resolving || System.currentTimeMillis() < retryAtMs) return
        resolving = true
        thread(name = "ddp-resolve", isDaemon = true) {
            try {
                address = InetAddress.getByName(host.trim())
            } catch (e: IOException) {
                lastError = "Cannot resolve $host"
                retryAtMs = System.currentTimeMillis() + 5_000
            } finally {
                resolving = false
            }
        }
    }

    override fun close() {
        socket?.close()
        socket = null
    }
}
