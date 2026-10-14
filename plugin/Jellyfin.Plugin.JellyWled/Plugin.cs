using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.JellyWled.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.JellyWled;

/// <summary>
/// Jelly-WLED: ambient glow lighting for Jellyfin playback, driving WLED strips over DDP.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "Jelly-WLED";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("7568fce5-c039-4d52-952f-38487b033df0");

    /// <inheritdoc />
    public override string Description => "Ambient glow lighting: drive WLED LED strips from what is playing.";

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        var ns = GetType().Namespace;
        return
        [
            new PluginPageInfo
            {
                Name = "JellyWled",
                DisplayName = "Jelly-WLED",
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", ns),
                EnableInMainMenu = true,
                MenuSection = "server",
                MenuIcon = "light_mode"
            },
            new PluginPageInfo
            {
                Name = "JellyWledConfig.js",
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.JellyWledConfig.js", ns)
            }
        ];
    }
}
