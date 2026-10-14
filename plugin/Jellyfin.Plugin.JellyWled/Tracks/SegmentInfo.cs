namespace Jellyfin.Plugin.JellyWled.Tracks;

/// <summary>
/// One segment of a glow track as found on disk.
/// </summary>
/// <param name="Path">The file path (".seg" or ".seg.part").</param>
/// <param name="Header">The parsed header.</param>
/// <param name="ReadableFrames">Frames readable right now (from the file size).</param>
public sealed record SegmentInfo(string Path, SegmentHeader Header, long ReadableFrames)
{
    /// <summary>Gets the first frame index.</summary>
    public long StartFrame => Header.StartFrame;

    /// <summary>Gets the frame index after the last readable frame.</summary>
    public long EndFrame => Header.StartFrame + ReadableFrames;

    /// <summary>Gets a value indicating whether the segment is finalized.</summary>
    public bool IsFinalized => Header.IsFinalized;

    /// <summary>
    /// Checks whether a frame index falls inside the readable range.
    /// </summary>
    /// <param name="frame">Absolute frame index.</param>
    /// <returns>True when readable.</returns>
    public bool Contains(long frame) => frame >= StartFrame && frame < EndFrame;
}
