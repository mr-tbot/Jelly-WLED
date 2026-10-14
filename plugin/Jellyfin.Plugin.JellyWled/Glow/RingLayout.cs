using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyWled.Glow;

/// <summary>
/// Builds LED layouts and locates ring zones.
/// </summary>
public static class RingLayout
{
    /// <summary>
    /// Builds the LED order for a strip.
    /// </summary>
    /// <param name="top">LEDs along the top edge.</param>
    /// <param name="right">LEDs along the right edge.</param>
    /// <param name="bottom">LEDs along the bottom edge.</param>
    /// <param name="left">LEDs along the left edge.</param>
    /// <param name="startCorner">Where LED 0 sits: top_left, top_right, bottom_right or bottom_left.</param>
    /// <param name="direction">cw or ccw, as seen from the front.</param>
    /// <returns>One span per LED in strip order.</returns>
    public static LedSpan[] Build(int top, int right, int bottom, int left, string startCorner, string direction)
    {
        var counts = new Dictionary<Side, int>
        {
            [Side.Top] = Math.Max(0, top),
            [Side.Right] = Math.Max(0, right),
            [Side.Bottom] = Math.Max(0, bottom),
            [Side.Left] = Math.Max(0, left),
        };

        // Clockwise from the top-left corner: top left->right, right top->bottom,
        // bottom right->left, left bottom->top. The flag says the side is walked against its screen axis.
        (Side Side, bool Reversed)[] order;
        int start;
        var corner = (startCorner ?? "top_left").ToLowerInvariant();
        if (string.Equals(direction, "ccw", StringComparison.OrdinalIgnoreCase))
        {
            order =
            [
                (Side.Left, false),
                (Side.Bottom, false),
                (Side.Right, true),
                (Side.Top, true),
            ];
            start = corner switch
            {
                "bottom_left" => 1,
                "bottom_right" => 2,
                "top_right" => 3,
                _ => 0,
            };
        }
        else
        {
            order =
            [
                (Side.Top, false),
                (Side.Right, false),
                (Side.Bottom, true),
                (Side.Left, true),
            ];
            start = corner switch
            {
                "top_right" => 1,
                "bottom_right" => 2,
                "bottom_left" => 3,
                _ => 0,
            };
        }

        var result = new List<LedSpan>();
        for (var i = 0; i < 4; i++)
        {
            var (side, reversed) = order[(start + i) % 4];
            var n = counts[side];
            for (var k = 0; k < n; k++)
            {
                double a = (double)k / n;
                double b = (double)(k + 1) / n;
                if (reversed)
                {
                    (a, b) = (1.0 - b, 1.0 - a);
                }

                result.Add(new LedSpan(side, a, b));
            }
        }

        return [.. result];
    }

    /// <summary>
    /// Gets the number of ring zones for a ring size.
    /// </summary>
    /// <param name="ringWidth">Zones on the top and bottom edges.</param>
    /// <param name="ringHeight">Zones on the left and right edges.</param>
    /// <returns>The zone count.</returns>
    public static int ZoneCount(int ringWidth, int ringHeight) => (2 * ringWidth) + (2 * ringHeight);

    /// <summary>
    /// Gets the ring zone index for a position along a side. Ring order is clockwise from top-left:
    /// top (left to right), right (top to bottom), bottom (right to left), left (bottom to top).
    /// </summary>
    /// <param name="side">The side.</param>
    /// <param name="k">The zone number along the side counted in picture direction (left to right or top to bottom).</param>
    /// <param name="ringWidth">Zones on horizontal sides.</param>
    /// <param name="ringHeight">Zones on vertical sides.</param>
    /// <returns>The index into the ring.</returns>
    public static int ZoneIndex(Side side, int k, int ringWidth, int ringHeight)
    {
        return side switch
        {
            Side.Top => k,
            Side.Right => ringWidth + k,
            Side.Bottom => ringWidth + ringHeight + (ringWidth - 1 - k),
            _ => (2 * ringWidth) + ringHeight + (ringHeight - 1 - k),
        };
    }

    /// <summary>
    /// Gets the ring zone indices overlapping a span on its side (at least one).
    /// </summary>
    /// <param name="span">The LED span.</param>
    /// <param name="ringWidth">Zones on horizontal sides.</param>
    /// <param name="ringHeight">Zones on vertical sides.</param>
    /// <returns>Zone indices.</returns>
    public static int[] ZonesFor(LedSpan span, int ringWidth, int ringHeight)
    {
        var n = span.Side is Side.Top or Side.Bottom ? ringWidth : ringHeight;
        var z0 = Math.Clamp((int)Math.Floor(span.T0 * n), 0, Math.Max(0, n - 1));
        var z1 = Math.Clamp((int)Math.Ceiling(span.T1 * n), z0 + 1, n);
        var zones = new int[z1 - z0];
        for (var k = z0; k < z1; k++)
        {
            zones[k - z0] = ZoneIndex(span.Side, k, ringWidth, ringHeight);
        }

        return zones;
    }
}
