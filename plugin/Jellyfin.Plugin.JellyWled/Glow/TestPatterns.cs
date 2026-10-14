using System;

namespace Jellyfin.Plugin.JellyWled.Glow;

/// <summary>
/// Installer patterns, identical to the ButterFlyEffect-Player glow feature: "sides" colors each edge
/// (top red, right green, bottom blue, left yellow) with LED 0 white; "chase" walks one white LED around
/// the strip; "white" lights everything; "off" is black.
/// </summary>
public static class TestPatterns
{
    /// <summary>
    /// Renders a pattern frame.
    /// </summary>
    /// <param name="pattern">sides, chase, white or off.</param>
    /// <param name="layout">The LED layout.</param>
    /// <param name="elapsedSeconds">Seconds since the pattern started (for chase).</param>
    /// <param name="output">Receives layout.Length * 3 bytes.</param>
    public static void Render(string pattern, LedSpan[] layout, double elapsedSeconds, Span<byte> output)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var n = layout.Length;
        output[..(n * 3)].Clear();
        switch ((pattern ?? "off").ToLowerInvariant())
        {
            case "chase":
                if (n > 0)
                {
                    var pos = (int)(elapsedSeconds * 20) % n;
                    output[pos * 3] = 255;
                    output[(pos * 3) + 1] = 255;
                    output[(pos * 3) + 2] = 255;
                }

                break;
            case "white":
                output[..(n * 3)].Fill(255);
                break;
            case "sides":
                for (var i = 0; i < n; i++)
                {
                    var (r, g, b) = layout[i].Side switch
                    {
                        Side.Top => ((byte)255, (byte)0, (byte)0),
                        Side.Right => ((byte)0, (byte)255, (byte)0),
                        Side.Bottom => ((byte)0, (byte)60, (byte)255),
                        _ => ((byte)255, (byte)200, (byte)0),
                    };
                    output[i * 3] = r;
                    output[(i * 3) + 1] = g;
                    output[(i * 3) + 2] = b;
                }

                if (n > 0)
                {
                    output[0] = 255;
                    output[1] = 255;
                    output[2] = 255;
                }

                break;
            default:
                break;
        }
    }
}
