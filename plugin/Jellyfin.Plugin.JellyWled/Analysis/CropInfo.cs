namespace Jellyfin.Plugin.JellyWled.Analysis;

/// <summary>
/// Detected crop rectangle in per mille of the source frame.
/// </summary>
/// <param name="X">Left offset.</param>
/// <param name="Y">Top offset.</param>
/// <param name="W">Width.</param>
/// <param name="H">Height.</param>
/// <param name="Filter">The ffmpeg crop filter text, or empty for no crop.</param>
public sealed record CropInfo(ushort X, ushort Y, ushort W, ushort H, string Filter)
{
    /// <summary>Gets the no-crop value.</summary>
    public static CropInfo None { get; } = new(0, 0, 1000, 1000, string.Empty);
}
