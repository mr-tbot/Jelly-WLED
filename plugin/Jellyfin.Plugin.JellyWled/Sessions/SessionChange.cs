namespace Jellyfin.Plugin.JellyWled.Sessions;

/// <summary>
/// What changed about a tracked session.
/// </summary>
public enum SessionChange
{
    /// <summary>Playback of an item started (or a new item replaced the previous one).</summary>
    Started,

    /// <summary>A progress report arrived (position, pause state).</summary>
    Progress,

    /// <summary>Playback stopped or the session ended.</summary>
    Stopped,
}
