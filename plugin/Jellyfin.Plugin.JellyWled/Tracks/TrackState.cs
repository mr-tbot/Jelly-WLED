namespace Jellyfin.Plugin.JellyWled.Tracks;

/// <summary>
/// What the server has for an item.
/// </summary>
public enum TrackState
{
    /// <summary>No frames at all.</summary>
    None,

    /// <summary>Some frames, with gaps or an unfinished end.</summary>
    Partial,

    /// <summary>A finalized segment covers the whole runtime.</summary>
    Complete,
}
