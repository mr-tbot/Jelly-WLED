using Jellyfin.Plugin.JellyWled.Configuration;
using Xunit;

namespace Jellyfin.Plugin.JellyWled.Tests;

public class ConfigurationTests
{
    [Fact]
    public void Defaults_MatchProtocol()
    {
        var c = new PluginConfiguration();
        Assert.Equal(10, c.AnalysisFps);
        Assert.Equal(32, c.RingWidth);
        Assert.Equal(18, c.RingHeight);
        var d = new WledDevice();
        Assert.Equal(4048, d.DdpPort);
        Assert.Equal("top_left", d.StartCorner);
    }
}
