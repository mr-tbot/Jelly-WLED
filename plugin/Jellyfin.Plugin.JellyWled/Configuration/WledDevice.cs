using System;

namespace Jellyfin.Plugin.JellyWled.Configuration;

/// <summary>
/// One WLED controller driven directly by the server, with its LED layout, color settings and the
/// Jellyfin sessions it follows.
/// </summary>
public class WledDevice
{
    /// <summary>
    /// Gets or sets the stable identifier of this device (assigned by the dashboard page).
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string Name { get; set; } = "Living room TV";

    /// <summary>
    /// Gets or sets a value indicating whether this device is active.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the WLED hostname or IP address.
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the UDP port for DDP (WLED default 4048).
    /// </summary>
    public int DdpPort { get; set; } = 4048;

    /// <summary>
    /// Gets or sets the HTTP port of the WLED JSON API (default 80).
    /// </summary>
    public int HttpPort { get; set; } = 80;

    /// <summary>
    /// Gets or sets the number of LEDs along the top edge of the TV, seen from the front.
    /// </summary>
    public int LedsTop { get; set; } = 60;

    /// <summary>
    /// Gets or sets the number of LEDs along the right edge.
    /// </summary>
    public int LedsRight { get; set; } = 34;

    /// <summary>
    /// Gets or sets the number of LEDs along the bottom edge.
    /// </summary>
    public int LedsBottom { get; set; } = 60;

    /// <summary>
    /// Gets or sets the number of LEDs along the left edge.
    /// </summary>
    public int LedsLeft { get; set; } = 34;

    /// <summary>
    /// Gets or sets where LED 0 sits: top_left, top_right, bottom_right or bottom_left.
    /// </summary>
    public string StartCorner { get; set; } = "top_left";

    /// <summary>
    /// Gets or sets the direction the strip runs, seen from the front: cw or ccw.
    /// </summary>
    public string Direction { get; set; } = "cw";

    /// <summary>
    /// Gets or sets how the picture maps to this node: "ring" (each LED follows its edge position) or
    /// "average" (every LED shows the mean color of the whole picture edge, for lamps and light bars).
    /// </summary>
    public string Mode { get; set; } = "ring";

    /// <summary>
    /// Gets or sets the brightness in percent (0-100).
    /// </summary>
    public int Brightness { get; set; } = 60;

    /// <summary>
    /// Gets or sets the color richness (saturation) in percent; 100 = as in the picture.
    /// </summary>
    public int Saturation { get; set; } = 120;

    /// <summary>
    /// Gets or sets the gamma applied to the colors.
    /// </summary>
    public double Gamma { get; set; } = 2.2;

    /// <summary>
    /// Gets or sets the smoothing in percent: how much of the previous frame's color is kept (0-95).
    /// </summary>
    public int Smoothing { get; set; } = 35;

    /// <summary>
    /// Gets or sets the number of frames per second sent to the strip (1-60).
    /// </summary>
    public int Fps { get; set; } = 25;

    /// <summary>
    /// Gets or sets the milliseconds added to the playback position (positive = lights earlier).
    /// </summary>
    public int SyncOffsetMs { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether WLED's previous on/off state and brightness are restored when playback stops.
    /// </summary>
    public bool RestoreState { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether this strip follows every playback session on the server.
    /// </summary>
    public bool FollowAllSessions { get; set; }

    /// <summary>
    /// Gets or sets the Jellyfin device ids this strip follows.
    /// </summary>
    public string[] FollowDeviceIds { get; set; } = [];

    /// <summary>
    /// Gets or sets the Jellyfin user ids this strip follows (any device of these users).
    /// </summary>
    public string[] FollowUserIds { get; set; } = [];
}
