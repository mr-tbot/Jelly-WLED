using System;
using System.Collections.Concurrent;

namespace Jellyfin.Plugin.JellyWled.Glow;

/// <summary>
/// Picture colors to strip bytes: saturation, gamma, brightness, then smoothing against the previous frame.
/// PROTOCOL.md section 4; identical math on the Android side.
/// </summary>
public sealed class ColorPipeline
{
    private static readonly ConcurrentDictionary<double, byte[]> GammaLuts = new();

    private readonly double _saturation;
    private readonly byte[] _lut;
    private readonly double _level;
    private readonly double _keep;

    /// <summary>
    /// Initializes a new instance of the <see cref="ColorPipeline"/> class.
    /// </summary>
    /// <param name="saturationPercent">Color richness, 100 = unchanged.</param>
    /// <param name="gamma">Gamma exponent.</param>
    /// <param name="brightnessPercent">Brightness 0-100.</param>
    /// <param name="smoothingPercent">How much of the previous frame is kept, 0-95.</param>
    public ColorPipeline(int saturationPercent, double gamma, int brightnessPercent, int smoothingPercent)
    {
        _saturation = Math.Clamp(saturationPercent, 0, 300) / 100.0;
        _lut = GammaLut(Math.Clamp(gamma, 0.2, 5.0));
        _level = Math.Clamp(brightnessPercent, 0, 100) / 100.0;
        _keep = Math.Clamp(smoothingPercent, 0, 95) / 100.0;
    }

    /// <summary>
    /// Gets a cached gamma lookup table.
    /// </summary>
    /// <param name="gamma">Gamma exponent.</param>
    /// <returns>256-entry table.</returns>
    public static byte[] GammaLut(double gamma)
    {
        return GammaLuts.GetOrAdd(gamma, g =>
        {
            var lut = new byte[256];
            for (var i = 0; i < 256; i++)
            {
                lut[i] = (byte)Math.Round(255.0 * Math.Pow(i / 255.0, g));
            }

            return lut;
        });
    }

    /// <summary>
    /// Processes one frame.
    /// </summary>
    /// <param name="raw">Raw per-LED RGB (from <see cref="LedMapper"/>).</param>
    /// <param name="previous">The previous output frame (same length) or empty for no smoothing.</param>
    /// <param name="output">Receives the processed RGB bytes; may alias <paramref name="previous"/> only if it is a copy.</param>
    public void Process(ReadOnlySpan<byte> raw, ReadOnlySpan<byte> previous, Span<byte> output)
    {
        var n = raw.Length / 3;
        var hasPrevious = previous.Length == raw.Length;
        for (var i = 0; i < n; i++)
        {
            int r = raw[i * 3];
            int g = raw[(i * 3) + 1];
            int b = raw[(i * 3) + 2];
            if (Math.Abs(_saturation - 1.0) > 1e-9)
            {
                var gray = (0.299 * r) + (0.587 * g) + (0.114 * b);
                r = Math.Clamp((int)(gray + ((r - gray) * _saturation)), 0, 255);
                g = Math.Clamp((int)(gray + ((g - gray) * _saturation)), 0, 255);
                b = Math.Clamp((int)(gray + ((b - gray) * _saturation)), 0, 255);
            }

            var cr = (int)(_lut[r] * _level);
            var cg = (int)(_lut[g] * _level);
            var cb = (int)(_lut[b] * _level);
            if (hasPrevious && _keep > 0.0)
            {
                cr = Step(previous[i * 3], cr);
                cg = Step(previous[(i * 3) + 1], cg);
                cb = Step(previous[(i * 3) + 2], cb);
            }

            output[i * 3] = (byte)cr;
            output[(i * 3) + 1] = (byte)cg;
            output[(i * 3) + 2] = (byte)cb;
        }
    }

    private int Step(int previous, int current)
    {
        var v = previous + ((current - previous) * (1.0 - _keep));

        // Always move at least one level toward the target so heavy smoothing still reaches black or white.
        return current > previous ? (int)Math.Ceiling(v) : (int)Math.Floor(v);
    }
}
