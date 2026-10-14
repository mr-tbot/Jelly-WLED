using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyWled.Tracks;

/// <summary>
/// Summary of an item's track.
/// </summary>
/// <param name="State">Overall state.</param>
/// <param name="Fps">Frames per second (current configuration).</param>
/// <param name="RingWidth">Ring width.</param>
/// <param name="RingHeight">Ring height.</param>
/// <param name="ExpectedFrames">Frames the whole runtime would need, or 0 when unknown.</param>
/// <param name="Frames">Frames readable across segments (overlaps not deduplicated).</param>
/// <param name="Segments">Segments found.</param>
public sealed record TrackInfo(TrackState State, int Fps, int RingWidth, int RingHeight, long ExpectedFrames, long Frames, IReadOnlyList<SegmentInfo> Segments);
