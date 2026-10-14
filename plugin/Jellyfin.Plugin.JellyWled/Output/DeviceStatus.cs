using System;

namespace Jellyfin.Plugin.JellyWled.Output;

/// <summary>
/// What a server-driven device is doing right now (for the status panel).
/// </summary>
public sealed class DeviceStatus
{
    /// <summary>Gets or sets the device id.</summary>
    public Guid DeviceId { get; set; }

    /// <summary>Gets or sets the device name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the host.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Gets or sets the state: disabled, idle, streaming, testing, error.</summary>
    public string State { get; set; } = "idle";

    /// <summary>Gets or sets the followed session id.</summary>
    public string? SessionId { get; set; }

    /// <summary>Gets or sets the followed item name.</summary>
    public string? ItemName { get; set; }

    /// <summary>Gets or sets the measured output frames per second.</summary>
    public double Fps { get; set; }

    /// <summary>Gets or sets the number of LEDs.</summary>
    public int LedCount { get; set; }

    /// <summary>Gets or sets a value indicating whether the last frame came from a track (false = fading or no frame).</summary>
    public bool HasFrame { get; set; }

    /// <summary>Gets or sets the last error.</summary>
    public string? LastError { get; set; }

    /// <summary>Gets or sets when the last packet was sent.</summary>
    public DateTime? LastPacketUtc { get; set; }

    /// <summary>Gets or sets the extrapolated playback position in milliseconds.</summary>
    public long? PositionMs { get; set; }
}
