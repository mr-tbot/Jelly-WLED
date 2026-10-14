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
    private readonly FfmpegAnalyzer _analyzer;
    private readonly TrackStore _store;
    private readonly AnalysisQueue _queue;
    private readonly ILogger<AnalyzeLibraryTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnalyzeLibraryTask"/> class.
    /// </summary>
    /// <param name="library">Library manager.</param>
    /// <param name="resolver">Media resolver.</param>
    /// <param name="analyzer">Analyzer.</param>
    /// <param name="store">Track store.</param>
    /// <param name="queue">Analysis queue (to avoid double work).</param>
    /// <param name="logger">Logger.</param>
    public AnalyzeLibraryTask(ILibraryManager library, MediaResolver resolver, FfmpegAnalyzer analyzer, TrackStore store, AnalysisQueue queue, ILogger<AnalyzeLibraryTask> logger)
    {
        _library = library;
        _resolver = resolver;
        _analyzer = analyzer;
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
        var libraryIds = cfg.TaskLibraryIds.Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToArray();
        if (libraryIds.Length > 0)
        {
            query.TopParentIds = libraryIds;
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

            // Produce whatever is missing from the start; the analyzer stops at the first existing finalized segment.
            var next = info.Segments.Where(s => s.IsFinalized && s.StartFrame > 0).Select(s => (long?)s.StartFrame).DefaultIfEmpty(null).Min();
            var startFrame = info.Segments.FirstOrDefault(s => s.IsFinalized && s.StartFrame == 0)?.EndFrame ?? 0;
            if (next is not null && startFrame >= next.Value)
            {
                continue;
            }

            try
            {
                _logger.LogInformation("[JellyWled] Task analyzing {Name}", source.Name);
                await _analyzer.AnalyzeAsync(source, _store, startFrame, next, null, cancellationToken).ConfigureAwait(false);
                done++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[JellyWled] Task failed to analyze {Name}", source.Name);
            }
        }

        progress.Report(100);
        _logger.LogInformation("[JellyWled] Task finished: {Done} item(s) analyzed, {Considered} considered", done, considered);
    }
}
