using Jellyfin.Plugin.JellyWled.Sessions;
using Jellyfin.Plugin.JellyWled.Tracks;
using Xunit;

namespace Jellyfin.Plugin.JellyWled.Tests;

public class StoreAndSyncTests
{
    private static SegmentInfo Seg(long start, long frames, bool finalized)
    {
        var h = new SegmentHeader { RingWidth = 4, RingHeight = 3, Fps = 10, StartFrame = (uint)start, FrameCount = (uint)frames, Flags = finalized ? SegmentHeader.FlagFinalized : (ushort)0 };
        return new SegmentInfo("/dev/null", h, frames);
    }

    [Fact]
    public void CoveredFrom_FollowsChainedFinalizedSegments_AndIgnoresParts()
    {
        var segments = new[] { Seg(310, 290, true), Seg(0, 310, true), Seg(600, 50, false) };
        Assert.Equal(600, TrackStore.CoveredFrom(segments, 0));
        Assert.Equal(600, TrackStore.CoveredFrom(segments, 400));
        Assert.Equal(650, TrackStore.CoveredFrom(new[] { Seg(600, 50, true) }, 600));
        Assert.Equal(5, TrackStore.CoveredFrom(new[] { Seg(10, 5, true) }, 5)); // gap before the segment: nothing covered
        Assert.Equal(0, TrackStore.CoveredFrom([], 0));
    }

    [Fact]
    public void NormalizeMediaSourceId_CollapsesGuidForms()
    {
        Assert.Equal("cec6d39757e91ebd1e17b7f342e59bcd", TrackStore.NormalizeMediaSourceId("cec6d397-57e9-1ebd-1e17-b7f342e59bcd"));
        Assert.Equal("cec6d39757e91ebd1e17b7f342e59bcd", TrackStore.NormalizeMediaSourceId("CEC6D39757E91EBD1E17B7F342E59BCD"));
        Assert.Equal("strm-x", TrackStore.NormalizeMediaSourceId("strm-x"));
        Assert.Null(TrackStore.NormalizeMediaSourceId(null));
    }

    [Fact]
    public void PlaybackState_ExtrapolatesFromReportTime_AndHoldsWhenPaused()
    {
        var playing = new PlaybackState("s", "d", "dev", "client", Guid.NewGuid(), "u", Guid.NewGuid(), "item", null, 600_000_000, 30_000 * 10_000, false, DateTime.UtcNow.AddSeconds(-2));
        var pos = playing.PositionMsNow();
        Assert.InRange(pos, 31_900, 32_300); // 30 s reported two seconds ago

        var paused = new PlaybackState("s", "d", "dev", "client", Guid.NewGuid(), "u", Guid.NewGuid(), "item", null, 600_000_000, 30_000 * 10_000, true, DateTime.UtcNow.AddSeconds(-2));
        Assert.Equal(30_000, paused.PositionMsNow());

        // A report time far in the past (stale clock) is ignored and "now" is used instead.
        var stale = new PlaybackState("s", "d", "dev", "client", Guid.NewGuid(), "u", Guid.NewGuid(), "item", null, 600_000_000, 0, false, DateTime.UtcNow.AddMinutes(-5));
        Assert.InRange(stale.PositionMsNow(), 0, 200);
    }
}
