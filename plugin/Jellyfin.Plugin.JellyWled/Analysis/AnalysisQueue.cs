using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyWled.Configuration;
using Jellyfin.Plugin.JellyWled.Tracks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyWled.Analysis;

/// <summary>
/// Serializes analysis work: one job per item at a time, a bounded number of jobs in parallel,
/// gap-aware (a job only produces the frames that are missing from the position it starts at).
/// </summary>
public sealed class AnalysisQueue : IHostedService, IDisposable
{
    private const int MaxQueued = 64;
    private readonly FfmpegAnalyzer _analyzer;
    private readonly MediaResolver _resolver;
    private readonly TrackStore _store;
    private readonly ILogger<AnalysisQueue> _logger;
    private readonly Channel<AnalysisJob> _channel = Channel.CreateBounded<AnalysisJob>(new BoundedChannelOptions(MaxQueued) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly ConcurrentDictionary<string, AnalysisJob> _byKey = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<AnalysisJob> _history = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly List<Task> _workers = [];
    private SemaphoreSlim _slots = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="AnalysisQueue"/> class.
    /// </summary>
    /// <param name="analyzer">The analyzer.</param>
    /// <param name="resolver">Media resolver.</param>
    /// <param name="store">Track store.</param>
    /// <param name="logger">Logger.</param>
    public AnalysisQueue(FfmpegAnalyzer analyzer, MediaResolver resolver, TrackStore store, ILogger<AnalysisQueue> logger)
    {
        _analyzer = analyzer;
        _resolver = resolver;
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Raised when a job makes progress or finishes (for the status page and the track stream).
    /// </summary>
    public event Action<AnalysisJob>? Progress;

    /// <summary>
    /// Gets the jobs that are queued or running.
    /// </summary>
    public IReadOnlyList<AnalysisJob> Pending => _byKey.Values.OrderBy(j => j.QueuedAt).ToList();

    /// <summary>
    /// Gets the last finished jobs (newest first, up to 20).
    /// </summary>
    public IReadOnlyList<AnalysisJob> Recent => _history.Reverse().Take(20).ToList();

    /// <summary>
    /// Gets the running job for an item, if any.
    /// </summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="mediaSourceId">Media source id.</param>
    /// <returns>The job or null.</returns>
    public AnalysisJob? Find(Guid itemId, string? mediaSourceId)
        => _byKey.GetValueOrDefault(KeyOf(itemId, mediaSourceId)) ?? _byKey.Values.FirstOrDefault(j => j.ItemId == itemId);

    /// <summary>
    /// Queues an analysis unless one is already pending for the item.
    /// </summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="mediaSourceId">Media source id.</param>
    /// <param name="fromMs">Position to start at.</param>
    /// <param name="reason">Reason for the status page.</param>
    /// <returns>The pending job (new or existing), or null when the queue is full.</returns>
    public AnalysisJob? Enqueue(Guid itemId, string? mediaSourceId, long fromMs, string reason)
    {
        var job = new AnalysisJob(itemId, mediaSourceId, fromMs, reason);
        var existing = _byKey.GetOrAdd(job.Key, job);
        if (!ReferenceEquals(existing, job))
        {
            return existing;
        }

        if (!_channel.Writer.TryWrite(job))
        {
            _byKey.TryRemove(job.Key, out _);
            _logger.LogWarning("[JellyWled] Analysis queue full; dropped request for {Item}", itemId);
            return null;
        }

        return job;
    }

    /// <summary>
    /// Moves the analysis of an item to a new position (after a seek): cancels the running job and queues a new
    /// one from <paramref name="fromMs"/> as soon as it has stopped.
    /// </summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="mediaSourceId">Media source id.</param>
    /// <param name="fromMs">New start position.</param>
    /// <param name="reason">Reason.</param>
    /// <returns>The job that will run (the existing one until it stops).</returns>
    public AnalysisJob? Retarget(Guid itemId, string? mediaSourceId, long fromMs, string reason)
    {
        var existing = _byKey.GetValueOrDefault(KeyOf(itemId, mediaSourceId));
        if (existing is null)
        {
            return Enqueue(itemId, mediaSourceId, fromMs, reason);
        }

        existing.RetargetFromMs = fromMs;
        existing.Cancellation.Cancel();
        return existing;
    }

    /// <summary>
    /// Cancels pending work for an item (before deleting its track).
    /// </summary>
    /// <param name="itemId">Item id.</param>
    public void Cancel(Guid itemId)
    {
        foreach (var job in _byKey.Values.Where(j => j.ItemId == itemId))
        {
            job.Cancellation.Cancel();
        }
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var parallel = Math.Clamp((Plugin.Instance?.Configuration ?? new PluginConfiguration()).MaxConcurrentAnalyses, 1, 4);
        _slots = new SemaphoreSlim(parallel, parallel);
        for (var i = 0; i < parallel; i++)
        {
            _workers.Add(Task.Run(() => WorkerAsync(_stopping.Token), CancellationToken.None));
        }

        _logger.LogInformation("[JellyWled] Analysis queue started with {Workers} worker(s)", parallel);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        _channel.Writer.TryComplete();
        foreach (var job in _byKey.Values)
        {
            await job.Cancellation.CancelAsync().ConfigureAwait(false);
        }

        try
        {
            await Task.WhenAll(_workers).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
            // shutting down anyway
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _stopping.Dispose();
        _slots.Dispose();
    }

    private static string KeyOf(Guid itemId, string? mediaSourceId) => $"{itemId:N}|{mediaSourceId ?? string.Empty}";

    private async Task WorkerAsync(CancellationToken stopping)
    {
        try
        {
            await foreach (var job in _channel.Reader.ReadAllAsync(stopping).ConfigureAwait(false))
            {
                await _slots.WaitAsync(stopping).ConfigureAwait(false);
                try
                {
                    await RunAsync(job, stopping).ConfigureAwait(false);
                }
                finally
                {
                    _slots.Release();
                    _byKey.TryRemove(job.Key, out _);
                    job.FinishedAt = DateTime.UtcNow;
                    _history.Enqueue(job);
                    while (_history.Count > 40 && _history.TryDequeue(out _))
                    {
                    }

                    Progress?.Invoke(job);
                    if (job.RetargetFromMs is { } retarget && !stopping.IsCancellationRequested)
                    {
                        Enqueue(job.ItemId, job.MediaSourceId, retarget, job.State == "done" ? "backfill" : "seek");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // stopping
        }
    }

    private async Task RunAsync(AnalysisJob job, CancellationToken stopping)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stopping, job.Cancellation.Token);
        var ct = linked.Token;
        job.State = "running";
        job.StartedAt = DateTime.UtcNow;
        try
        {
            var source = _resolver.Resolve(job.ItemId, job.MediaSourceId);
            if (source is null)
            {
                job.State = "skipped";
                job.Error = "Item is not a local video file.";
                return;
            }

            job.ItemName = source.Name;
            var cfg = Plugin.Instance?.Configuration ?? new PluginConfiguration();
            var fps = Math.Clamp(cfg.AnalysisFps, 1, 60);
            var startFrame = job.FromMs * fps / 1000;
            var expectedTotal = source.RunTimeTicks > 0 ? (long)Math.Floor(source.RunTimeTicks / 10_000_000.0 * fps) : 0;

            // Gap analysis: skip what is already covered from the start position; stop at the next existing segment.
            var segments = _store.ListSegments(source.ItemId, source.MediaSourceId);
            var covering = segments.FirstOrDefault(s => s.IsFinalized && s.Contains(startFrame));
            if (covering is not null)
            {
                startFrame = covering.EndFrame;
                if (expectedTotal > 0 && startFrame >= expectedTotal - fps)
                {
                    job.State = "skipped";
                    job.Error = "Already analyzed.";
                    return;
                }
            }

            long? endFrame = segments
                .Where(s => s.IsFinalized && s.StartFrame > startFrame)
                .Select(s => (long?)s.StartFrame)
                .DefaultIfEmpty(null)
                .Min();
            job.StartFrame = startFrame;
            job.ExpectedFrames = endFrame is not null ? endFrame.Value - startFrame : Math.Max(0, expectedTotal - startFrame);
            _logger.LogInformation("[JellyWled] Analyzing {Name} from frame {Start} ({Reason})", source.Name, startFrame, job.Reason);

            var frames = await _analyzer.AnalyzeAsync(
                source,
                _store,
                startFrame,
                endFrame,
                done =>
                {
                    job.DoneFrames = done;
                    Progress?.Invoke(job);
                },
                ct).ConfigureAwait(false);
            job.DoneFrames = frames;
            job.State = "done";
            _logger.LogInformation("[JellyWled] Analyzed {Name}: {Frames} frames from frame {Start}", source.Name, frames, startFrame);

            // Playback started mid-file: fill in the beginning afterwards so the next play is complete.
            // The follow-up is queued by the worker once this job's key is released (Enqueue would dedupe it now).
            if (startFrame > 0 && !segments.Any(s => s.IsFinalized && s.StartFrame == 0))
            {
                job.RetargetFromMs ??= 0;
            }
        }
        catch (OperationCanceledException)
        {
            job.State = "canceled";
        }
        catch (Exception ex)
        {
            job.State = "failed";
            job.Error = ex.Message;
            _logger.LogWarning(ex, "[JellyWled] Analysis failed for {Item}", job.ItemName.Length > 0 ? job.ItemName : job.ItemId.ToString());
        }
    }
}
