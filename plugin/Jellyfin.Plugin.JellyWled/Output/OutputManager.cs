using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyWled.Analysis;
using Jellyfin.Plugin.JellyWled.Configuration;
using Jellyfin.Plugin.JellyWled.Sessions;
using Jellyfin.Plugin.JellyWled.Tracks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyWled.Output;

/// <summary>
/// Drives the server-side WLED devices: one <see cref="DeviceRunner"/> per enabled device, reconciled against the
/// configuration every two seconds, plus on-demand analysis requests for sessions that start or seek.
/// </summary>
public sealed class OutputManager : IHostedService, IDisposable
{
    private readonly SessionTracker _tracker;
    private readonly TrackStore _store;
    private readonly AnalysisQueue _queue;
    private readonly WledHttpClient _wled;
    private readonly ILogger<OutputManager> _logger;
    private readonly ConcurrentDictionary<Guid, DeviceRunner> _runners = new();
    private readonly ConcurrentDictionary<string, PlaybackState> _lastStates = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _stopping = new();
    private Task? _supervisor;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutputManager"/> class.
    /// </summary>
    /// <param name="tracker">Session tracker.</param>
    /// <param name="store">Track store.</param>
    /// <param name="queue">Analysis queue.</param>
    /// <param name="wled">WLED HTTP client.</param>
    /// <param name="logger">Logger.</param>
    public OutputManager(SessionTracker tracker, TrackStore store, AnalysisQueue queue, WledHttpClient wled, ILogger<OutputManager> logger)
    {
        _tracker = tracker;
        _store = store;
        _queue = queue;
        _wled = wled;
        _logger = logger;
    }

    /// <summary>
    /// Gets the status of every configured device.
    /// </summary>
    public IReadOnlyList<DeviceStatus> Statuses
    {
        get
        {
            var cfg = Plugin.Instance?.Configuration ?? new PluginConfiguration();
            var list = new List<DeviceStatus>();
            foreach (var device in cfg.Devices)
            {
                if (_runners.TryGetValue(device.Id, out var runner))
                {
                    list.Add(runner.Status);
                }
                else
                {
                    list.Add(new DeviceStatus { DeviceId = device.Id, Name = device.Name, Host = device.Host, State = device.Enabled && cfg.EnableServerOutput ? "starting" : "disabled" });
                }
            }

            return list;
        }
    }

    /// <summary>
    /// Runs a test pattern on a device.
    /// </summary>
    /// <param name="deviceId">Device id.</param>
    /// <param name="pattern">sides, chase, white or off.</param>
    /// <param name="seconds">Duration.</param>
    /// <returns>True when the device is running.</returns>
    public bool StartTest(Guid deviceId, string pattern, int seconds)
    {
        if (!_runners.TryGetValue(deviceId, out var runner))
        {
            return false;
        }

        runner.StartTest(pattern, Math.Clamp(seconds, 1, 300));
        return true;
    }

    /// <summary>
    /// Makes sure a track exists (or is being produced) for the position a session is at. Used by the API too.
    /// </summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="mediaSourceId">Media source id.</param>
    /// <param name="positionMs">Position.</param>
    /// <param name="reason">Reason.</param>
    /// <returns>The pending job, or null when nothing was needed or on-demand analysis is off.</returns>
    public AnalysisJob? EnsureTrack(Guid itemId, string? mediaSourceId, long positionMs, string reason)
    {
        var cfg = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        if (!cfg.AnalyzeOnPlayback && reason != "api")
        {
            return null;
        }

        var fps = Math.Clamp(cfg.AnalysisFps, 1, 60);
        var frame = positionMs * fps / 1000;
        var segments = _store.ListSegments(itemId, mediaSourceId);
        if (segments.Any(s => s.IsFinalized && s.Contains(frame)))
        {
            return null;
        }

        var job = _queue.Find(itemId, mediaSourceId);
        if (job is not null && job.State == "running")
        {
            var head = job.StartFrame + job.DoneFrames;
            var startsBefore = job.StartFrame <= frame;
            var closeAhead = frame - head < fps * 30; // the running job will reach it within half a minute
            if (startsBefore && closeAhead)
            {
                return job;
            }

            return _queue.Retarget(itemId, mediaSourceId, positionMs, reason);
        }

        return job ?? _queue.Enqueue(itemId, mediaSourceId, positionMs, reason);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _tracker.Changed += OnSessionChanged;
        _supervisor = Task.Run(() => SuperviseAsync(_stopping.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _tracker.Changed -= OnSessionChanged;
        await _stopping.CancelAsync().ConfigureAwait(false);
        foreach (var runner in _runners.Values)
        {
            await runner.StopAsync().ConfigureAwait(false);
            runner.Dispose();
        }

        _runners.Clear();
        if (_supervisor is not null)
        {
            try
            {
                await _supervisor.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
            {
                // shutting down
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => _stopping.Dispose();

    private void OnSessionChanged(SessionChange change, PlaybackState state)
    {
        try
        {
            switch (change)
            {
                case SessionChange.Started:
                    _lastStates[state.SessionId] = state;
                    EnsureTrack(state.ItemId, state.MediaSourceId, state.PositionMsNow(), "playback");
                    break;
                case SessionChange.Progress:
                    // A seek shows as a jump between where the previous report says we should be now and the new report.
                    var previous = _lastStates.GetValueOrDefault(state.SessionId);
                    _lastStates[state.SessionId] = state;
                    var now = state.PositionMsNow();
                    if (previous is not null && Math.Abs(now - previous.PositionMsNow()) > 5000)
                    {
                        EnsureTrack(state.ItemId, state.MediaSourceId, now, "seek");
                    }

                    break;
                default:
                    _lastStates.TryRemove(state.SessionId, out _);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[JellyWled] Session change handling failed");
        }
    }

    private async Task SuperviseAsync(CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                var cfg = Plugin.Instance?.Configuration ?? new PluginConfiguration();
                var wanted = cfg.EnableServerOutput
                    ? cfg.Devices.Where(d => d.Enabled && !string.IsNullOrWhiteSpace(d.Host)).ToDictionary(d => d.Id)
                    : new Dictionary<Guid, WledDevice>();
                foreach (var (id, runner) in _runners.ToArray())
                {
                    if (!wanted.TryGetValue(id, out var device))
                    {
                        _runners.TryRemove(id, out _);
                        await runner.StopAsync().ConfigureAwait(false);
                        runner.Dispose();
                        _logger.LogInformation("[JellyWled] Stopped output for device {Name}", runner.Status.Name);
                    }
                    else
                    {
                        runner.Update(device);
                    }
                }

                foreach (var (id, device) in wanted)
                {
                    if (!_runners.ContainsKey(id))
                    {
                        var runner = new DeviceRunner(device, _tracker, _store, _wled, _logger);
                        if (_runners.TryAdd(id, runner))
                        {
                            runner.Start(stopping);
                            _logger.LogInformation("[JellyWled] Started output for device {Name} at {Host}", device.Name, device.Host);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[JellyWled] Output supervisor error");
            }

            try
            {
                await Task.Delay(2000, stopping).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
