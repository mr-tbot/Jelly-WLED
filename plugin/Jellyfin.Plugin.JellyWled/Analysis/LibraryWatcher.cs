using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyWled.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyWled.Analysis;

/// <summary>
/// Queues newly added movies and episodes for analysis when "Analyze new items automatically" is on.
/// </summary>
public sealed class LibraryWatcher : IHostedService
{
    private readonly ILibraryManager _library;
    private readonly AnalysisQueue _queue;
    private readonly ILogger<LibraryWatcher> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryWatcher"/> class.
    /// </summary>
    /// <param name="library">Library manager.</param>
    /// <param name="queue">Analysis queue.</param>
    /// <param name="logger">Logger.</param>
    public LibraryWatcher(ILibraryManager library, AnalysisQueue queue, ILogger<LibraryWatcher> logger)
    {
        _library = library;
        _queue = queue;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _library.ItemAdded += OnItemAdded;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _library.ItemAdded -= OnItemAdded;
        return Task.CompletedTask;
    }

    private void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        try
        {
            var cfg = Plugin.Instance?.Configuration ?? new PluginConfiguration();
            if (!cfg.AnalyzeNewItems || e.Item is not (Movie or Episode) || e.Item.IsVirtualItem)
            {
                return;
            }

            _queue.Enqueue(e.Item.Id, null, 0, "library");
            _logger.LogDebug("[JellyWled] Queued new item {Name} for analysis", e.Item.Name);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[JellyWled] ItemAdded handler failed");
        }
    }
}
