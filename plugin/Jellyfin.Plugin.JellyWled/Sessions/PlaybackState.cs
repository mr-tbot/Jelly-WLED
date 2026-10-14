using System;
using System.Diagnostics;

namespace Jellyfin.Plugin.JellyWled.Sessions;

/// <summary>
/// The last reported playback state of one session, with position extrapolation (PROTOCOL.md section 2).
/// Immutable; the tracker replaces the instance on every report.
/// </summary>
public sealed class PlaybackState
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackState"/> class.
    /// </summary>
    /// <param name="sessionId">Jellyfin session id.</param>
    /// <param name="deviceId">Device id.</param>
    /// <param name="deviceName">Device name.</param>
    /// <param name="client">Client application name.</param>
    /// <param name="userId">User id.</param>
    /// <param name="userName">User name.</param>
    /// <param name="itemId">Now playing item id.</param>
    /// <param name="itemName">Now playing item name.</param>
    /// <param name="mediaSourceId">Media source id.</param>
    /// <param name="runTimeTicks">Runtime in ticks.</param>
    /// <param name="positionTicks">Reported position in ticks.</param>
    /// <param name="isPaused">Whether paused.</param>
    public PlaybackState(string sessionId, string deviceId, string deviceName, string client, Guid userId, string userName, Guid itemId, string itemName, string? mediaSourceId, long runTimeTicks, long positionTicks, bool isPaused)
    {
        SessionId = sessionId;
        DeviceId = deviceId;
        DeviceName = deviceName;
        Client = client;
        UserId = userId;
        UserName = userName;
        ItemId = itemId;
        ItemName = itemName;
        MediaSourceId = mediaSourceId;
        RunTimeTicks = runTimeTicks;
        PositionTicks = positionTicks;
        IsPaused = isPaused;
        ReportedAtUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        ReportedAtStopwatch = Stopwatch.GetTimestamp();
    }

    /// <summary>Gets the session id.</summary>
    public string SessionId { get; }

    /// <summary>Gets the device id.</summary>
    public string DeviceId { get; }

    /// <summary>Gets the device name.</summary>
    public string DeviceName { get; }

    /// <summary>Gets the client name.</summary>
    public string Client { get; }

    /// <summary>Gets the user id.</summary>
    public Guid UserId { get; }

    /// <summary>Gets the user name.</summary>
    public string UserName { get; }

    /// <summary>Gets the item id.</summary>
    public Guid ItemId { get; }

    /// <summary>Gets the item name.</summary>
    public string ItemName { get; }

    /// <summary>Gets the media source id.</summary>
    public string? MediaSourceId { get; }

    /// <summary>Gets the runtime in ticks.</summary>
    public long RunTimeTicks { get; }

    /// <summary>Gets the reported position in ticks.</summary>
    public long PositionTicks { get; }

    /// <summary>Gets a value indicating whether playback is paused.</summary>
    public bool IsPaused { get; }

    /// <summary>Gets the server wall clock (Unix ms) when the report arrived.</summary>
    public long ReportedAtUtcMs { get; }

    /// <summary>Gets the monotonic timestamp when the report arrived.</summary>
    public long ReportedAtStopwatch { get; }

    /// <summary>
    /// Gets the extrapolated position in milliseconds right now.
    /// </summary>
    /// <returns>Position in ms.</returns>
    public long PositionMsNow()
    {
        var reported = PositionTicks / 10_000;
        if (IsPaused)
        {
            return reported;
        }

        var elapsed = Stopwatch.GetElapsedTime(ReportedAtStopwatch);
        return reported + (long)elapsed.TotalMilliseconds;
    }
}
