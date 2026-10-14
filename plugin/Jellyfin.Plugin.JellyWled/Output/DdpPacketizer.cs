using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyWled.Output;

/// <summary>
/// Splits one RGB frame into DDP packets (PROTOCOL.md section 5): 10-byte header, version 1, push flag on
/// the last packet, RGB 8-bit data type, destination 1, big-endian byte offset and length, 1440 data bytes max.
/// </summary>
public static class DdpPacketizer
{
    /// <summary>
    /// The DDP header length.
    /// </summary>
    public const int HeaderLength = 10;

    /// <summary>
    /// Maximum data bytes per packet (480 RGB LEDs), what WLED accepts.
    /// </summary>
    public const int MaxDataPerPacket = 1440;

    /// <summary>
    /// Builds the packets for one frame. A zero-length frame yields one empty push packet.
    /// </summary>
    /// <param name="rgb">The frame, 3 bytes per LED.</param>
    /// <param name="sequence">Starting sequence number (0-15); incremented per packet.</param>
    /// <param name="maxData">Maximum data bytes per packet.</param>
    /// <returns>The packets in send order.</returns>
    public static IReadOnlyList<byte[]> Build(ReadOnlySpan<byte> rgb, byte sequence = 0, int maxData = MaxDataPerPacket)
    {
        var packets = new List<byte[]>();
        var total = rgb.Length;
        var offset = 0;
        var seq = sequence & 0x0F;
        do
        {
            var len = Math.Min(maxData, total - offset);
            var last = offset + len >= total;
            var packet = new byte[HeaderLength + len];
            packet[0] = (byte)(0x40 | (last ? 0x01 : 0x00));
            packet[1] = (byte)seq;
            packet[2] = 0x0B;
            packet[3] = 0x01;
            packet[4] = (byte)((offset >> 24) & 0xFF);
            packet[5] = (byte)((offset >> 16) & 0xFF);
            packet[6] = (byte)((offset >> 8) & 0xFF);
            packet[7] = (byte)(offset & 0xFF);
            packet[8] = (byte)((len >> 8) & 0xFF);
            packet[9] = (byte)(len & 0xFF);
            rgb.Slice(offset, len).CopyTo(packet.AsSpan(HeaderLength));
            packets.Add(packet);
            offset += Math.Max(1, len);
            seq = (seq + 1) & 0x0F;
        }
        while (offset < total);

        return packets;
    }
}
