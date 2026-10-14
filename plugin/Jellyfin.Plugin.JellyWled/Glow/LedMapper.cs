using System;

namespace Jellyfin.Plugin.JellyWled.Glow;

/// <summary>
/// Turns a ring frame (zone colors) into one raw RGB color per LED for a specific strip layout.
/// Precomputes the zone lists once; <see cref="Map"/> is allocation-free.
/// </summary>
public sealed class LedMapper
{
    private readonly int[][] _zones;
    private readonly int _zoneCount;
    private readonly bool _average;

    /// <summary>
    /// Initializes a new instance of the <see cref="LedMapper"/> class.
    /// </summary>
    /// <param name="layout">The LED spans in strip order.</param>
    /// <param name="ringWidth">Ring zones on horizontal sides.</param>
    /// <param name="ringHeight">Ring zones on vertical sides.</param>
    /// <param name="average">True to give every LED the mean of the whole ring ("average" mode).</param>
    public LedMapper(LedSpan[] layout, int ringWidth, int ringHeight, bool average = false)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _zoneCount = RingLayout.ZoneCount(ringWidth, ringHeight);
        _average = average;
        _zones = new int[layout.Length][];
        for (var i = 0; i < layout.Length; i++)
        {
            _zones[i] = RingLayout.ZonesFor(layout[i], ringWidth, ringHeight);
        }
    }

    /// <summary>
    /// Gets the number of LEDs.
    /// </summary>
    public int LedCount => _zones.Length;

    /// <summary>
    /// Gets the number of ring zones expected in a frame.
    /// </summary>
    public int ZoneCount => _zoneCount;

    /// <summary>
    /// Maps a ring frame to per-LED colors.
    /// </summary>
    /// <param name="ring">Ring frame, ZoneCount * 3 bytes.</param>
    /// <param name="output">Receives LedCount * 3 bytes.</param>
    public void Map(ReadOnlySpan<byte> ring, Span<byte> output)
    {
        if (ring.Length < _zoneCount * 3)
        {
            throw new ArgumentException("Ring frame too short.", nameof(ring));
        }

        if (output.Length < _zones.Length * 3)
        {
            throw new ArgumentException("Output buffer too short.", nameof(output));
        }

        if (_average)
        {
            int r = 0, g = 0, b = 0;
            for (var z = 0; z < _zoneCount; z++)
            {
                r += ring[z * 3];
                g += ring[(z * 3) + 1];
                b += ring[(z * 3) + 2];
            }

            var n = Math.Max(1, _zoneCount);
            for (var i = 0; i < _zones.Length; i++)
            {
                output[i * 3] = (byte)(r / n);
                output[(i * 3) + 1] = (byte)(g / n);
                output[(i * 3) + 2] = (byte)(b / n);
            }

            return;
        }

        for (var i = 0; i < _zones.Length; i++)
        {
            var zones = _zones[i];
            int r = 0, g = 0, b = 0;
            foreach (var z in zones)
            {
                r += ring[z * 3];
                g += ring[(z * 3) + 1];
                b += ring[(z * 3) + 2];
            }

            var n = Math.Max(1, zones.Length);
            output[i * 3] = (byte)(r / n);
            output[(i * 3) + 1] = (byte)(g / n);
            output[(i * 3) + 2] = (byte)(b / n);
        }
    }
}
