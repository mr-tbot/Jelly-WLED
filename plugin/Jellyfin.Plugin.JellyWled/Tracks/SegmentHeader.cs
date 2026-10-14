using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Jellyfin.Plugin.JellyWled.Tracks;

/// <summary>
/// The 64-byte header of a glow track segment (PROTOCOL.md section 1.2).
/// </summary>
public sealed class SegmentHeader
{
    /// <summary>
    /// The header size in bytes.
    /// </summary>
    public const int Size = 64;

    /// <summary>
    /// Flag bit: the segment is complete and <see cref="FrameCount"/> is valid.
    /// </summary>
    public const ushort FlagFinalized = 0x0001;

    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("JWGLOW01");

    /// <summary>Gets or sets the ring width (zones on horizontal sides).</summary>
    public ushort RingWidth { get; set; }

    /// <summary>Gets or sets the ring height (zones on vertical sides).</summary>
    public ushort RingHeight { get; set; }

    /// <summary>Gets or sets the frames per second.</summary>
    public ushort Fps { get; set; }

    /// <summary>Gets or sets the flags.</summary>
    public ushort Flags { get; set; }

    /// <summary>Gets or sets the absolute index of the first frame.</summary>
    public uint StartFrame { get; set; }

    /// <summary>Gets or sets the frame count (valid when finalized).</summary>
    public uint FrameCount { get; set; }

    /// <summary>Gets or sets the media file size at analysis time.</summary>
    public ulong SourceSize { get; set; }

    /// <summary>Gets or sets the media file modification time (Unix milliseconds) at analysis time.</summary>
    public ulong SourceMtimeMs { get; set; }

    /// <summary>Gets or sets the edge band depth in per mille of the height.</summary>
    public ushort BandPermille { get; set; }

    /// <summary>Gets or sets the crop x in per mille of the source width.</summary>
    public ushort CropX { get; set; }

    /// <summary>Gets or sets the crop y in per mille of the source height.</summary>
    public ushort CropY { get; set; }

    /// <summary>Gets or sets the crop width in per mille (1000 = no crop).</summary>
    public ushort CropW { get; set; } = 1000;

    /// <summary>Gets or sets the crop height in per mille (1000 = no crop).</summary>
    public ushort CropH { get; set; } = 1000;

    /// <summary>Gets a value indicating whether the segment is finalized.</summary>
    public bool IsFinalized => (Flags & FlagFinalized) != 0;

    /// <summary>Gets the number of zones per frame.</summary>
    public int ZoneCount => (2 * RingWidth) + (2 * RingHeight);

    /// <summary>Gets the number of bytes per frame.</summary>
    public int FrameBytes => ZoneCount * 3;

    /// <summary>
    /// Parses a header.
    /// </summary>
    /// <param name="bytes">At least 64 bytes.</param>
    /// <returns>The header.</returns>
    /// <exception cref="InvalidDataException">Bad magic or size.</exception>
    public static SegmentHeader Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < Size || !bytes[..8].SequenceEqual(Magic))
        {
            throw new InvalidDataException("Not a glow track segment.");
        }

        var h = new SegmentHeader
        {
            RingWidth = BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]),
            RingHeight = BinaryPrimitives.ReadUInt16LittleEndian(bytes[10..]),
            Fps = BinaryPrimitives.ReadUInt16LittleEndian(bytes[12..]),
            Flags = BinaryPrimitives.ReadUInt16LittleEndian(bytes[14..]),
            StartFrame = BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]),
            FrameCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes[20..]),
            SourceSize = BinaryPrimitives.ReadUInt64LittleEndian(bytes[24..]),
            SourceMtimeMs = BinaryPrimitives.ReadUInt64LittleEndian(bytes[32..]),
            BandPermille = BinaryPrimitives.ReadUInt16LittleEndian(bytes[40..]),
            CropX = BinaryPrimitives.ReadUInt16LittleEndian(bytes[44..]),
            CropY = BinaryPrimitives.ReadUInt16LittleEndian(bytes[46..]),
            CropW = BinaryPrimitives.ReadUInt16LittleEndian(bytes[48..]),
            CropH = BinaryPrimitives.ReadUInt16LittleEndian(bytes[50..]),
        };
        if (h.RingWidth == 0 || h.RingHeight == 0 || h.Fps == 0)
        {
            throw new InvalidDataException("Glow track segment has an empty ring or zero fps.");
        }

        return h;
    }

    /// <summary>
    /// Writes the header.
    /// </summary>
    /// <param name="bytes">At least 64 bytes; fully overwritten.</param>
    public void Write(Span<byte> bytes)
    {
        bytes[..Size].Clear();
        Magic.CopyTo(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[8..], RingWidth);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[10..], RingHeight);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[12..], Fps);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[14..], Flags);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[16..], StartFrame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[20..], FrameCount);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[24..], SourceSize);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[32..], SourceMtimeMs);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[40..], BandPermille);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[44..], CropX);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[46..], CropY);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[48..], CropW);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[50..], CropH);
    }

    /// <summary>
    /// Serializes the header to a new array.
    /// </summary>
    /// <returns>64 bytes.</returns>
    public byte[] ToBytes()
    {
        var b = new byte[Size];
        Write(b);
        return b;
    }
}
