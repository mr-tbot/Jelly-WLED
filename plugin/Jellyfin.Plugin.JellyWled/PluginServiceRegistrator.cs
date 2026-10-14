using Jellyfin.Plugin.JellyWled.Analysis;
using Jellyfin.Plugin.JellyWled.Api;
using Jellyfin.Plugin.JellyWled.Output;
using Jellyfin.Plugin.JellyWled.Sessions;
using Jellyfin.Plugin.JellyWled.Tracks;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.JellyWled;

/// <summary>
/// Registers the plugin's services and background workers with the server.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<TrackStore>();
        serviceCollection.AddSingleton<MediaResolver>();
        serviceCollection.AddSingleton<FfmpegAnalyzer>();
        serviceCollection.AddSingleton<WledHttpClient>();

        serviceCollection.AddSingleton<AnalysisQueue>();
        serviceCollection.AddHostedService(sp => sp.GetRequiredService<AnalysisQueue>());

        serviceCollection.AddSingleton<SessionTracker>();
        serviceCollection.AddHostedService(sp => sp.GetRequiredService<SessionTracker>());

        serviceCollection.AddSingleton<OutputManager>();
        serviceCollection.AddHostedService(sp => sp.GetRequiredService<OutputManager>());

        serviceCollection.AddSingleton<EventHub>();
        serviceCollection.AddHostedService(sp => sp.GetRequiredService<EventHub>());

        serviceCollection.AddHostedService<LibraryWatcher>();
    }
}
