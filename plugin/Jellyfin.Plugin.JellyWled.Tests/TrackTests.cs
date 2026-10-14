using Jellyfin.Plugin.JellyWled.Analysis;
using Jellyfin.Plugin.JellyWled.Glow;
using Jellyfin.Plugin.JellyWled.Tracks;
using Xunit;

namespace Jellyfin.Plugin.JellyWled.Tests;

public class TrackTests
{
    [Fact]
    public void ComputeRing_AveragesEdgeBands_InRingOrder()
    {
        const int W = 4, H = 3, band = 1;
        var thumb = new byte[W * H * 3];
        // Row 0 red, row 2 blue, column 0 yellow-ish override, column 3 green; middle row gray.
        for (var x = 0; x < W; x++)
        {
            Set(thumb, W, x, 0, 255, 0, 0);
            Set(thumb, W, x, 1, 40, 40, 40);
            Set(thumb, W, x, 2, 0, 0, 255);
        }

        Set(thumb, W, 3, 1, 0, 255, 0);
        Set(thumb, W, 0, 1, 255, 200, 0);
        var ring = new byte[RingLayout.ZoneCount(W, H) * 3];
        FfmpegAnalyzer.ComputeRing(thumb, W, H, band, ring);
        Assert.Equal(new byte[] { 255, 0, 0 }, ring[0..3]); // top zone 0
        Assert.Equal(new byte[] { 255, 0, 0 }, ring[(3 * 3)..(3 * 3 + 3)]); // top zone 3
        var right1 = RingLayout.ZoneIndex(Side.Right, 1, W, H);
        Assert.Equal(new byte[] { 0, 255, 0 }, ring[(right1 * 3)..(right1 * 3 + 3)]);
        var bottom0 = RingLayout.ZoneIndex(Side.Bottom, 0, W, H);
        Assert.Equal(new byte[] { 0, 0, 255 }, ring[(bottom0 * 3)..(bottom0 * 3 + 3)]);
        var left1 = RingLayout.ZoneIndex(Side.Left, 1, W, H);
        Assert.Equal(new byte[] { 255, 200, 0 }, ring[(left1 * 3)..(left1 * 3 + 3)]);
    }

    [Fact]
    public void SegmentWriter_ThenReader_RoundTrips_AndGrowsWhileWriting()
    {
        var dir = Path.Combine(Path.GetTempPath(), "jellywled-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "0000000010.seg");
        var header = new SegmentHeader { RingWidth = 4, RingHeight = 3, Fps = 10, StartFrame = 10 };
        using var writer = new SegmentWriter(path, header);
        var frame = new byte[header.FrameBytes];
        for (var i = 0; i < 7; i++)
        {
            Array.Fill(frame, (byte)(i + 1));
            writer.WriteFrame(frame);
        }

        writer.Flush();
        using (var growing = new SegmentReader(writer.PartPath))
        {
            Assert.False(growing.Header.IsFinalized);
            Assert.Equal(7, growing.ReadableFrames);
            var dest = new byte[header.FrameBytes];
            Assert.True(growing.TryRead(12, dest));
            Assert.Equal(3, dest[0]);
            Assert.False(growing.TryRead(17, dest));
            Assert.False(growing.TryRead(9, dest));
        }

        var final = writer.Complete();
        Assert.True(File.Exists(final));
        Assert.False(File.Exists(path + ".part"));
        using var reader = new SegmentReader(final);
        Assert.True(reader.Header.IsFinalized);
        Assert.Equal(7u, reader.Header.FrameCount);
        Assert.Equal(7, reader.ReadableFrames);
        var run = new byte[header.FrameBytes * 10];
        Assert.Equal(5, reader.ReadRun(12, run));
        Assert.Equal(3, run[0]);
        Assert.Equal(7, run[4 * header.FrameBytes]);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void ReferenceSegment_FromPythonTool_Parses()
    {
        // tools/glowtrack.py write --ring 32x18 --fps 10 --frames 5 --pattern sides, generated during the build of the tools.
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "ref_sides.seg"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "ref_sides.seg")),
        };
        var path = candidates.FirstOrDefault(File.Exists);
        if (path is null)
        {
            return; // reference vector not generated on this machine
        }

        using var reader = new SegmentReader(path);
        Assert.Equal(32, reader.Header.RingWidth);
        Assert.Equal(18, reader.Header.RingHeight);
        Assert.Equal(10, reader.Header.Fps);
        Assert.Equal(5, reader.ReadableFrames);
        var ring = new byte[reader.Header.FrameBytes];
        Assert.True(reader.TryRead(0, ring));
        Assert.Equal(new byte[] { 255, 0, 0 }, ring[0..3]);
        var right0 = RingLayout.ZoneIndex(Side.Right, 0, 32, 18) * 3;
        Assert.Equal(new byte[] { 0, 255, 0 }, ring[right0..(right0 + 3)]);
        var bottom0 = RingLayout.ZoneIndex(Side.Bottom, 0, 32, 18) * 3;
        Assert.Equal(new byte[] { 0, 60, 255 }, ring[bottom0..(bottom0 + 3)]);
        var left0 = RingLayout.ZoneIndex(Side.Left, 0, 32, 18) * 3;
        Assert.Equal(new byte[] { 255, 200, 0 }, ring[left0..(left0 + 3)]);
    }

    private static void Set(byte[] thumb, int width, int x, int y, byte r, byte g, byte b)
    {
        var o = ((y * width) + x) * 3;
        thumb[o] = r;
        thumb[o + 1] = g;
        thumb[o + 2] = b;
    }
}
