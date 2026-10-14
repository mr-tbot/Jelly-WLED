using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.JellyWled.Analysis;
using Jellyfin.Plugin.JellyWled.Configuration;
using Jellyfin.Plugin.JellyWled.Tracks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyWled.ScheduledTasks;

/// <summary>
/// Pre-analyzes movies and episodes that have no complete glow track yet, newest first, at low priority.
/// </summary>
public class AnalyzeLibraryTask : IScheduledTask, IConfigurableScheduledTask
{
    private readonly ILibraryManager _library;
    private readonly MediaResolver _resolver;
    private readonly TrackStore _store;
    private readonly AnalysisQueue _queue;
    private readonly ILogger<AnalyzeLibraryTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnalyzeLibraryTask"/> class.
    /// </summary>
    /// <param name="library">Library manager.</param>
    /// <param name="resolver">Media resolver.</param>
    /// <param name="store">Track store.</param>
    /// <param name="queue">Analysis queue (to avoid double work).</param>
    /// <param name="logger">Logger.</param>
    public AnalyzeLibraryTask(ILibraryManager library, MediaResolver resolver, TrackStore store, AnalysisQueue queue, ILogger<AnalyzeLibraryTask> logger)
    {
        _library = library;
        _resolver = resolver;
        _store = store;
        _queue = queue;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Analyze library for Jelly-WLED";

    /// <inheritdoc />
    public string Key => "JellyWledAnalyzeLibrary";

    /// <inheritdoc />
    public string Description => "Builds glow tracks (edge colors over time) for movies and episodes that do not have one yet, so the lights follow from the first second.";

    /// <inheritdoc />
    public string Category => "Jelly-WLED";

    /// <inheritdoc />
    public bool IsHidden => false;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public bool IsLogged => true;

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return
        [
            new TaskTriggerInfo { Type = TaskTriggerInfoType.DailyTrigger, TimeOfDayTicks = TimeSpan.FromHours(3).Ticks, MaxRuntimeTicks = TimeSpan.FromHours(6).Ticks },
        ];
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var cfg = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var pruned = _store.Prune(TimeSpan.FromHours(6));
        if (pruned > 0)
        {
            _logger.LogInformation("[JellyWled] Pruned {Count} stale track file(s)", pruned);
        }

        var kinds = new List<BaseItemKind>();
        if (cfg.TaskIncludeMovies)
        {
            kinds.Add(BaseItemKind.Movie);
        }

        if (cfg.TaskIncludeEpisodes)
        {
            kinds.Add(BaseItemKind.Episode);
        }

        if (kinds.Count == 0)
        {
            return;
        }

        var query = new InternalItemsQuery
        {
            IncludeItemTypes = [.. kinds],
            IsVirtualItem = false,
            Recursive = true,
            OrderBy = [(ItemSortBy.DateCreated, cfg.TaskNewestFirst ? SortOrder.Descending : SortOrder.Ascending)],
        };
        // The dashboard stores CollectionFolder ids; queries filter on the physical top folders behind them.
        var libraryIds = cfg.TaskLibraryIds.Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToArray();
        if (libraryIds.Length > 0)
        {
            query.TopParentIds = libraryIds
                .SelectMany(id => _library.GetItemById<BaseItem>(id) is CollectionFolder cf ? cf.PhysicalFolderIds.ToArray() : [id])
                .Distinct()
                .ToArray();
        }

        var items = _library.GetItemList(query);
        var max = cfg.TaskMaxItemsPerRun <= 0 ? int.MaxValue : cfg.TaskMaxItemsPerRun;
        var done = 0;
        var considered = 0;
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (done >= max)
            {
                break;
            }

            considered++;
            progress.Report(Math.Min(99.0, 100.0 * considered / Math.Max(1, items.Count)));
            var source = _resolver.Resolve(item.Id, null);
            if (source is null)
            {
                continue;
            }

            var info = _store.GetInfo(item.Id, source.MediaSourceId, source.RunTimeTicks);
            if (info.State == TrackState.Complete || _queue.Find(item.Id, source.MediaSourceId) is not null)
            {
                continue;
            }

            // Produce whatever is missing from the first uncovered frame; the queue stops at the next existing segment.
            var startFrame = TrackStore.CoveredFrom(info.Segments, 0);
            if (info.ExpectedFrames > 0 && startFrame >= info.ExpectedFrames - cfg.EffectiveFps)
            {
                continue;
            }

            // Everything goes through the queue so the task never writes the same segment as an on-demand job
            // and the concurrency limit holds.
            var job = _queue.Enqueue(item.Id, source.MediaSourceId, (long)(startFrame * 1000.0 / cfg.EffectiveFps), "task");
            if (job is null)
            {
                _logger.LogWarning("[JellyWled] Task could not queue {Name} (queue full); stopping this run", source.Name);
                break;
            }

            _logger.LogInformation("[JellyWled] Task queued {Name} from frame {Start}", source.Name, startFrame);
            await _queue.WaitAsync(job, cancellationToken).ConfigureAwait(false);
            if (job.State == "done")
            {
                done++;
            }
        }

        progress.Report(100);
        _logger.LogInformation("[JellyWled] Task finished: {Done} item(s) analyzed, {Considered} considered", done, considered);
    }
}
