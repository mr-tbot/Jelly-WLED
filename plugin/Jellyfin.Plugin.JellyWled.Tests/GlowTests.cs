using Jellyfin.Plugin.JellyWled.Glow;
using Jellyfin.Plugin.JellyWled.Output;
using Jellyfin.Plugin.JellyWled.Tracks;
using Xunit;

namespace Jellyfin.Plugin.JellyWled.Tests;

public class GlowTests
{
    [Fact]
    public void Layout_Clockwise_TopLeft_WalksTopThenRightThenBottomThenLeft()
    {
        var layout = RingLayout.Build(2, 1, 2, 1, "top_left", "cw");
        Assert.Equal(6, layout.Length);
        Assert.Equal(new LedSpan(Side.Top, 0.0, 0.5), layout[0]);
        Assert.Equal(new LedSpan(Side.Top, 0.5, 1.0), layout[1]);
        Assert.Equal(new LedSpan(Side.Right, 0.0, 1.0), layout[2]);
        // Bottom runs right to left: the first bottom LED covers the right half.
        Assert.Equal(new LedSpan(Side.Bottom, 0.5, 1.0), layout[3]);
        Assert.Equal(new LedSpan(Side.Bottom, 0.0, 0.5), layout[4]);
        Assert.Equal(new LedSpan(Side.Left, 0.0, 1.0), layout[5]);
    }

    [Fact]
    public void Layout_CounterClockwise_BottomRight_StartsOnBottomGoingRightToLeft()
    {
        // ccw order is left (top->bottom), bottom (left->right), right (bottom->top), top (right->left);
        // starting at bottom_right means right first (walking up), then top, then left, then bottom.
        var layout = RingLayout.Build(1, 2, 1, 2, "bottom_right", "ccw");
        Assert.Equal(Side.Right, layout[0].Side);
        Assert.Equal(new LedSpan(Side.Right, 0.5, 1.0), layout[0]);
        Assert.Equal(new LedSpan(Side.Right, 0.0, 0.5), layout[1]);
        Assert.Equal(Side.Top, layout[2].Side);
        Assert.Equal(Side.Left, layout[3].Side);
        Assert.Equal(new LedSpan(Side.Left, 0.0, 0.5), layout[3]);
        Assert.Equal(Side.Bottom, layout[5].Side);
    }

    [Fact]
    public void ZoneIndex_FollowsRingOrder()
    {
        const int W = 4, H = 2; // ring: top 0-3, right 4-5, bottom 6-9 (right to left), left 10-11 (bottom to top)
        Assert.Equal(0, RingLayout.ZoneIndex(Side.Top, 0, W, H));
        Assert.Equal(3, RingLayout.ZoneIndex(Side.Top, 3, W, H));
        Assert.Equal(4, RingLayout.ZoneIndex(Side.Right, 0, W, H));
        Assert.Equal(9, RingLayout.ZoneIndex(Side.Bottom, 0, W, H)); // leftmost bottom zone is last in ring order
        Assert.Equal(6, RingLayout.ZoneIndex(Side.Bottom, 3, W, H));
        Assert.Equal(11, RingLayout.ZoneIndex(Side.Left, 0, W, H)); // top-most left zone is last
        Assert.Equal(10, RingLayout.ZoneIndex(Side.Left, 1, W, H));
    }

    [Fact]
    public void Mapper_AveragesOverlappingZones_AndSidesPatternLandsOnRightSides()
    {
        const int W = 4, H = 2;
        var ring = new byte[RingLayout.ZoneCount(W, H) * 3];
        // Paint the ring with the "sides" colors by zone.
        for (var k = 0; k < W; k++)
        {
            Set(ring, RingLayout.ZoneIndex(Side.Top, k, W, H), 255, 0, 0);
            Set(ring, RingLayout.ZoneIndex(Side.Bottom, k, W, H), 0, 0, 255);
        }

        for (var k = 0; k < H; k++)
        {
            Set(ring, RingLayout.ZoneIndex(Side.Right, k, W, H), 0, 255, 0);
            Set(ring, RingLayout.ZoneIndex(Side.Left, k, W, H), 255, 255, 0);
        }

        var layout = RingLayout.Build(2, 1, 2, 1, "top_left", "cw");
        var mapper = new LedMapper(layout, W, H);
        var leds = new byte[mapper.LedCount * 3];
        mapper.Map(ring, leds);
        Assert.Equal(new byte[] { 255, 0, 0 }, leds[0..3]);
        Assert.Equal(new byte[] { 0, 255, 0 }, leds[6..9]);
        Assert.Equal(new byte[] { 0, 0, 255 }, leds[9..12]);
        Assert.Equal(new byte[] { 255, 255, 0 }, leds[15..18]);

        var avg = new LedMapper(layout, W, H, average: true);
        avg.Map(ring, leds);
        // 4 red + 4 blue + 2 green + 2 yellow zones over 12: R=(4*255+2*255)/12=127, G=(2*255+2*255)/12=85, B=4*255/12=85
        Assert.Equal(new byte[] { 127, 85, 85 }, leds[0..3]);
    }

    [Fact]
    public void Pipeline_AppliesSaturationGammaBrightness_AndSmoothingStepsTowardTarget()
    {
        var p = new ColorPipeline(100, 1.0, 100, 0);
        var raw = new byte[] { 200, 100, 50 };
        var output = new byte[3];
        p.Process(raw, [], output);
        Assert.Equal(raw, output);

        var half = new ColorPipeline(100, 1.0, 50, 0);
        half.Process(raw, [], output);
        Assert.Equal(new byte[] { 100, 50, 25 }, output);

        var smooth = new ColorPipeline(100, 1.0, 100, 50);
        var previous = new byte[] { 0, 0, 0 };
        smooth.Process(raw, previous, output);
        Assert.Equal(new byte[] { 100, 50, 25 }, output);
        // Heavy smoothing still moves at least one level toward the target.
        var heavy = new ColorPipeline(100, 1.0, 100, 95);
        heavy.Process(new byte[] { 0, 0, 0 }, new byte[] { 1, 1, 1 }, output);
        Assert.Equal(new byte[] { 0, 0, 0 }, output);

        var gamma = new ColorPipeline(100, 2.2, 100, 0);
        gamma.Process(new byte[] { 128, 255, 0 }, [], output);
        Assert.Equal(56, output[0]);
        Assert.Equal(255, output[1]);
        Assert.Equal(0, output[2]);

        var sat = new ColorPipeline(0, 1.0, 100, 0);
        sat.Process(new byte[] { 255, 0, 0 }, [], output);
        Assert.Equal(76, output[0]); // gray of pure red = 0.299 * 255
        Assert.Equal(76, output[1]);
        Assert.Equal(76, output[2]);
    }

    [Fact]
    public void Ddp_SplitsAt1440Bytes_WithPushOnLastPacket()
    {
        var frame = new byte[1500 * 3]; // 1500 LEDs = 4500 bytes -> 1440, 1440, 1440, 180
        frame[0] = 7;
        frame[4499] = 9;
        var packets = DdpPacketizer.Build(frame, 3);
        Assert.Equal(4, packets.Count);
        Assert.Equal(0x40, packets[0][0]);
        Assert.Equal(3, packets[0][1]);
        Assert.Equal(0x0B, packets[0][2]);
        Assert.Equal(1, packets[0][3]);
        Assert.Equal(new byte[] { 0, 0, 0, 0, 0x05, 0xA0 }, packets[0][4..10]);
        Assert.Equal(7, packets[0][10]);
        Assert.Equal(0x41, packets[3][0]);
        Assert.Equal(6, packets[3][1]);
        Assert.Equal(new byte[] { 0, 0, 0x10, 0xE0, 0, 180 }, packets[3][4..10]);
        Assert.Equal(9, packets[3][^1]);
        Assert.Equal(DdpPacketizer.HeaderLength + 180, packets[3].Length);

        var empty = DdpPacketizer.Build([]);
        Assert.Single(empty);
        Assert.Equal(0x41, empty[0][0]);
    }

    [Fact]
    public void SegmentHeader_RoundTrips()
    {
        var h = new SegmentHeader
        {
            RingWidth = 32,
            RingHeight = 18,
            Fps = 10,
            Flags = SegmentHeader.FlagFinalized,
            StartFrame = 1234,
            FrameCount = 56,
            SourceSize = 1_234_567_890_123,
            SourceMtimeMs = 1_760_000_000_000,
            BandPermille = 80,
            CropX = 0,
            CropY = 128,
            CropW = 1000,
            CropH = 744,
        };
        var bytes = h.ToBytes();
        Assert.Equal(64, bytes.Length);
        Assert.Equal("JWGLOW01", System.Text.Encoding.ASCII.GetString(bytes, 0, 8));
        var back = SegmentHeader.Parse(bytes);
        Assert.Equal(32, back.RingWidth);
        Assert.Equal(18, back.RingHeight);
        Assert.Equal(10, back.Fps);
        Assert.True(back.IsFinalized);
        Assert.Equal(1234u, back.StartFrame);
        Assert.Equal(56u, back.FrameCount);
        Assert.Equal(1_234_567_890_123ul, back.SourceSize);
        Assert.Equal(1_760_000_000_000ul, back.SourceMtimeMs);
        Assert.Equal(80, back.BandPermille);
        Assert.Equal(128, back.CropY);
        Assert.Equal(744, back.CropH);
        Assert.Equal(300, back.FrameBytes);
        Assert.Throws<InvalidDataException>(() => SegmentHeader.Parse(new byte[64]));
    }

    [Fact]
    public void TestPattern_Sides_PaintsEdges_AndLedZeroWhite()
    {
        var layout = RingLayout.Build(2, 1, 2, 1, "top_left", "cw");
        var output = new byte[layout.Length * 3];
        TestPatterns.Render("sides", layout, 0, output);
        Assert.Equal(new byte[] { 255, 255, 255 }, output[0..3]);
        Assert.Equal(new byte[] { 255, 0, 0 }, output[3..6]);
        Assert.Equal(new byte[] { 0, 255, 0 }, output[6..9]);
        Assert.Equal(new byte[] { 0, 60, 255 }, output[9..12]);
        Assert.Equal(new byte[] { 255, 200, 0 }, output[15..18]);
        TestPatterns.Render("chase", layout, 0.1, output); // 0.1 s * 20 = LED 2
        Assert.Equal(new byte[] { 255, 255, 255 }, output[6..9]);
        Assert.Equal(0, output[0]);
    }

    private static void Set(byte[] ring, int zone, byte r, byte g, byte b)
    {
        ring[zone * 3] = r;
        ring[(zone * 3) + 1] = g;
        ring[(zone * 3) + 2] = b;
    }
}
