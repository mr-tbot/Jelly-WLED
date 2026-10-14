using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyWled.Sessions;

/// <summary>
/// Follows every playback session through Jellyfin's session events and keeps the latest state per session.
/// Event handlers never block: they update a dictionary and raise <see cref="Changed"/> synchronously to
/// subscribers that must themselves return immediately.
/// </summary>
public sealed class SessionTracker : IHostedService, IDisposable
{
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<SessionTracker> _logger;
    private readonly ConcurrentDictionary<string, PlaybackState> _states = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionTracker"/> class.
    /// </summary>
    /// <param name="sessionManager">Session manager.</param>
    /// <param name="logger">Logger.</param>
    public SessionTracker(ISessionManager sessionManager, ILogger<SessionTracker> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    /// <summary>
    /// Raised on every tracked change. Handlers must not block.
    /// </summary>
    public event Action<SessionChange, PlaybackState>? Changed;

    /// <summary>
    /// Gets the current states of all sessions that are playing something.
    /// </summary>
    public IReadOnlyCollection<PlaybackState> Active => _states.Values.ToList();

    /// <summary>
    /// Gets the state of one session.
    /// </summary>
    /// <param name="sessionId">Session id.</param>
    /// <returns>The state or null.</returns>
    public PlaybackState? Get(string sessionId) => _states.GetValueOrDefault(sessionId);

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart += OnPlaybackStart;
        _sessionManager.PlaybackProgress += OnPlaybackProgress;
        _sessionManager.PlaybackStopped += OnPlaybackStopped;
        _sessionManager.SessionEnded += OnSessionEnded;
        try
        {
            // Sessions that were already playing when the plugin loaded (server restart mid-film).
            foreach (var session in _sessionManager.Sessions)
            {
                var item = session.FullNowPlayingItem;
                if (item is null || session.PlayState is null)
                {
                    continue;
                }

                var state = new PlaybackState(
                    session.Id,
                    session.DeviceId ?? string.Empty,
                    session.DeviceName ?? string.Empty,
                    session.Client ?? string.Empty,
                    session.UserId,
                    session.UserName ?? string.Empty,
                    item.Id,
                    item.Name ?? string.Empty,
                    session.PlayState.MediaSourceId,
                    item.RunTimeTicks ?? 0,
                    session.PlayState.PositionTicks ?? 0,
                    session.PlayState.IsPaused);
                _states[state.SessionId] = state;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[JellyWled] Could not seed existing sessions");
        }

        _logger.LogInformation("[JellyWled] Session tracker started ({Count} active)", _states.Count);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart -= OnPlaybackStart;
        _sessionManager.PlaybackProgress -= OnPlaybackProgress;
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        _sessionManager.SessionEnded -= OnSessionEnded;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Events are detached in StopAsync; nothing else to release.
    }

    private static PlaybackState? Build(PlaybackProgressEventArgs e)
    {
        var session = e.Session;
        if (session is null || e.Item is null)
        {
            return null;
        }

        var position = e.PlaybackPositionTicks ?? session.PlayState?.PositionTicks ?? 0;
        var userName = session.UserName ?? e.Users?.FirstOrDefault()?.Username ?? string.Empty;
        return new PlaybackState(
            session.Id,
            e.DeviceId ?? session.DeviceId ?? string.Empty,
            e.DeviceName ?? session.DeviceName ?? string.Empty,
            e.ClientName ?? session.Client ?? string.Empty,
            session.UserId,
            userName,
            e.Item.Id,
            e.Item.Name ?? string.Empty,
            e.MediaSourceId ?? session.PlayState?.MediaSourceId,
            e.Item.RunTimeTicks ?? 0,
            position,
            e.IsPaused);
    }

    private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
    {
        try
        {
            var state = Build(e);
            if (state is null)
            {
                return;
            }

            _states[state.SessionId] = state;
            _logger.LogInformation("[JellyWled] Playback started: {Item} on {Device} ({Client}) at {Position} ms", state.ItemName, state.DeviceName, state.Client, state.PositionMsNow());
            Changed?.Invoke(SessionChange.Started, state);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[JellyWled] PlaybackStart handler failed");
        }
    }

    private void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs e)
    {
        try
        {
            if (e.IsAutomated)
            {
                // The server synthesizes one of these per second between real reports; we extrapolate ourselves.
                return;
            }

            var state = Build(e);
            if (state is null)
            {
                return;
            }

            var previous = _states.GetValueOrDefault(state.SessionId);
            _states[state.SessionId] = state;
            var change = previous is null || previous.ItemId != state.ItemId ? SessionChange.Started : SessionChange.Progress;
            Changed?.Invoke(change, state);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[JellyWled] PlaybackProgress handler failed");
        }
    }

    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
    {
        try
        {
            var sessionId = e.Session?.Id;
            if (sessionId is null)
            {
                return;
            }

            if (_states.TryRemove(sessionId, out var state))
            {
                _logger.LogInformation("[JellyWled] Playback stopped: {Item} on {Device}", state.ItemName, state.DeviceName);
                Changed?.Invoke(SessionChange.Stopped, state);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[JellyWled] PlaybackStopped handler failed");
        }
    }

    private void OnSessionEnded(object? sender, SessionEventArgs e)
    {
        try
        {
            var sessionId = e.SessionInfo?.Id;
            if (sessionId is not null && _states.TryRemove(sessionId, out var state))
            {
                Changed?.Invoke(SessionChange.Stopped, state);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[JellyWled] SessionEnded handler failed");
        }
    }
}
