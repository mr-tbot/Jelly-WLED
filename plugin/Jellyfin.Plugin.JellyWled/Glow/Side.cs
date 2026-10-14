namespace Jellyfin.Plugin.JellyWled.Glow;

/// <summary>
/// One edge of the picture, seen from the front of the TV.
/// </summary>
public enum Side
{
    /// <summary>Top edge, zones run left to right.</summary>
    Top = 0,

    /// <summary>Right edge, zones run top to bottom.</summary>
    Right = 1,

    /// <summary>Bottom edge, zones run right to left in ring order.</summary>
    Bottom = 2,

    /// <summary>Left edge, zones run bottom to top in ring order.</summary>
    Left = 3,
}
