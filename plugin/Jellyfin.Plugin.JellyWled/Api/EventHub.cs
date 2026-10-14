using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyWled.Analysis;
using Jellyfin.Plugin.JellyWled.Configuration;
using Jellyfin.Plugin.JellyWled.Sessions;
using Jellyfin.Plugin.JellyWled.Tracks;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.JellyWled.Api;

/// <summary>
/// Fan-out of playback state changes to Server-Sent Events subscribers (PROTOCOL.md section 2).
/// </summary>
public sealed class EventHub : IHostedService
{
    /// <summary>
    /// JSON options for every payload the plugin emits (camelCase, no nulls dropped).
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SessionTracker _tracker;
    private readonly AnalysisQueue _queue;
    private readonly TrackStore _store;
    private readonly ConcurrentDictionary<Guid, Subscriber> _subscribers = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="EventHub"/> class.
    /// </summary>
    /// <param name="tracker">Session tracker.</param>
    /// <param name="queue">Analysis queue.</param>
    /// <param name="store">Track store.</param>
    public EventHub(SessionTracker tracker, AnalysisQueue queue, TrackStore store)
    {
        _tracker = tracker;
        _queue = queue;
        _store = store;
    }

    /// <summary>
    /// Builds the "state" payload for a session.
    /// </summary>
    /// <param name="state">Playback state.</param>
    /// <returns>An anonymous object ready for serialization.</returns>
    public object StatePayload(PlaybackState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var info = _store.GetInfo(state.ItemId, state.MediaSourceId, state.RunTimeTicks);
        var job = _queue.Find(state.ItemId, state.MediaSourceId);
        return new
        {
            sessionId = state.SessionId,
            deviceId = state.DeviceId,
            deviceName = state.DeviceName,
            client = state.Client,
            userId = state.UserId,
            userName = state.UserName,
            itemId = state.ItemId,
            itemName = state.ItemName,
            mediaSourceId = state.MediaSourceId,
            runTimeTicks = state.RunTimeTicks,
            positionTicks = state.PositionTicks,
            reportedAtMs = state.ReportedAtUtcMs,
            isPaused = state.IsPaused,
            track = TrackPayload(info, job),
        };
    }

    /// <summary>
    /// Builds the "track" payload.
    /// </summary>
    /// <param name="info">Track info.</param>
    /// <param name="job">Pending job, if any.</param>
    /// <returns>An anonymous object.</returns>
    public static object TrackPayload(TrackInfo info, AnalysisJob? job)
    {
        ArgumentNullException.ThrowIfNull(info);
        var state = info.State switch
        {
            TrackState.Complete => "complete",
            TrackState.Partial => job is not null && job.State is "running" or "queued" ? "analyzing" : "partial",
            _ => job is not null && job.State is "running" or "queued" ? "analyzing" : "none",
        };
        return new
        {
            state,
            frames = info.Frames,
            expectedFrames = info.ExpectedFrames,
            fps = info.Fps,
            ringW = info.RingWidth,
            ringH = info.RingHeight,
            segments = info.Segments.Select(s => new { startFrame = s.StartFrame, frames = s.ReadableFrames, finalized = s.IsFinalized }).ToArray(),
            analyzing = job is null ? null : new { state = job.State, startFrame = job.StartFrame, doneFrames = job.DoneFrames, expectedFrames = job.ExpectedFrames },
        };
    }

    /// <summary>
    /// Builds the "hello" payload.
    /// </summary>
    /// <returns>An anonymous object.</returns>
    public static object HelloPayload()
    {
        var cfg = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        return new
        {
            serverTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            pluginVersion = Plugin.Instance?.Version.ToString() ?? "0",
            analysisFps = cfg.EffectiveFps,
            ringW = cfg.EffectiveRingWidth,
            ringH = cfg.EffectiveRingHeight,
        };
    }

    /// <summary>
    /// Subscribes a client.
    /// </summary>
    /// <param name="userId">The caller's user id (filters sessions) or null for an API key.</param>
    /// <param name="isAdmin">Whether the caller sees every session.</param>
    /// <returns>The subscription id and the reader to drain.</returns>
    public (Guid Id, ChannelReader<SseEvent> Reader) Subscribe(Guid? userId, bool isAdmin)
    {
        var channel = Channel.CreateBounded<SseEvent>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest });
        var id = Guid.NewGuid();
        _subscribers[id] = new Subscriber(channel, userId, isAdmin);
        return (id, channel.Reader);
    }

    /// <summary>
    /// Removes a subscription.
    /// </summary>
    /// <param name="id">Subscription id.</param>
    public void Unsubscribe(Guid id)
    {
        if (_subscribers.TryRemove(id, out var sub))
        {
            sub.Channel.Writer.TryComplete();
        }
    }

    /// <summary>
    /// Checks whether a caller may see a session.
    /// </summary>
    /// <param name="state">Session state.</param>
    /// <param name="userId">Caller user id or null for API keys.</param>
    /// <param name="isAdmin">Caller is an administrator.</param>
    /// <returns>True when visible.</returns>
    public static bool CanSee(PlaybackState state, Guid? userId, bool isAdmin)
    {
        ArgumentNullException.ThrowIfNull(state);
        return isAdmin || userId is null || state.UserId == userId.Value;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _tracker.Changed += OnSessionChanged;
        _queue.Progress += OnAnalysisProgress;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _tracker.Changed -= OnSessionChanged;
        _queue.Progress -= OnAnalysisProgress;
        foreach (var id in _subscribers.Keys.ToList())
        {
            Unsubscribe(id);
        }

        return Task.CompletedTask;
    }

    private void Broadcast(PlaybackState state, SseEvent evt)
    {
        foreach (var sub in _subscribers.Values)
        {
            if (CanSee(state, sub.UserId, sub.IsAdmin))
            {
                sub.Channel.Writer.TryWrite(evt);
            }
        }
    }

    private void OnSessionChanged(SessionChange change, PlaybackState state)
    {
        if (_subscribers.IsEmpty)
        {
            return;
        }

        if (change == SessionChange.Stopped)
        {
            Broadcast(state, new SseEvent("stopped", JsonSerializer.Serialize(new { sessionId = state.SessionId, deviceId = state.DeviceId }, JsonOptions)));
            return;
        }

        Broadcast(state, new SseEvent("state", JsonSerializer.Serialize(StatePayload(state), JsonOptions)));
    }

    private void OnAnalysisProgress(AnalysisJob job)
    {
        // Only state transitions are interesting to clients: a track that just became usable or finished.
        if (_subscribers.IsEmpty || job.State is "running" or "queued")
        {
            return;
        }

        foreach (var state in _tracker.Active.Where(s => s.ItemId == job.ItemId))
        {
            Broadcast(state, new SseEvent("state", JsonSerializer.Serialize(StatePayload(state), JsonOptions)));
        }
    }

    private sealed record Subscriber(Channel<SseEvent> Channel, Guid? UserId, bool IsAdmin);
}
