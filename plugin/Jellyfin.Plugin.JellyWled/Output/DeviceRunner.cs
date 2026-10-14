using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyWled.Configuration;
using Jellyfin.Plugin.JellyWled.Glow;
using Jellyfin.Plugin.JellyWled.Sessions;
using Jellyfin.Plugin.JellyWled.Tracks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyWled.Output;

/// <summary>
/// The output loop of one device: picks the session to follow, reads the ring frame for the extrapolated
/// position, maps and processes it and sends DDP at the device's frame rate. Idles cheaply when nothing plays.
/// </summary>
public sealed class DeviceRunner : IDisposable
{
    private readonly SessionTracker _tracker;
    private readonly TrackStore _store;
    private readonly WledHttpClient _wled;
    private readonly ILogger _logger;
    private readonly Action<PlaybackState>? _onMissingFrame;
    private readonly object _lock = new();
    private readonly Queue<long> _sent = new();
    private WledDevice _device;
    private string _layoutKey = string.Empty;
    private string _senderKey = string.Empty;
    private LedMapper? _mapper;
    private ColorPipeline? _pipeline;
    private LedSpan[] _layout = [];
    private byte[] _ring = [];
    private byte[] _raw = [];
    private byte[] _previous = [];
    private byte[] _current = [];
    private DdpSender? _sender;
    private (string Pattern, DateTime Until, DateTime Started)? _test;
    private string? _followingSessionId;
    private DateTime _lastMissReport = DateTime.MinValue;
    private Task? _loop;
    private CancellationTokenSource? _cts;
    private bool _active;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeviceRunner"/> class.
    /// </summary>
    /// <param name="device">Device configuration.</param>
    /// <param name="tracker">Session tracker.</param>
    /// <param name="store">Track store.</param>
    /// <param name="wled">WLED HTTP client.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="onMissingFrame">Called (at most every 10 s) when the followed session has no frame at its position.</param>
    public DeviceRunner(WledDevice device, SessionTracker tracker, TrackStore store, WledHttpClient wled, ILogger logger, Action<PlaybackState>? onMissingFrame = null)
    {
        _onMissingFrame = onMissingFrame;
        _device = device;
        _tracker = tracker;
        _store = store;
        _wled = wled;
        _logger = logger;
        Status = new DeviceStatus { DeviceId = device.Id, Name = device.Name, Host = device.Host, State = "idle" };
    }

    /// <summary>Gets the live status.</summary>
    public DeviceStatus Status { get; }

    /// <summary>
    /// Replaces the device configuration (picked up on the next frame).
    /// </summary>
    /// <param name="device">New configuration.</param>
    public void Update(WledDevice device)
    {
        lock (_lock)
        {
            _device = device;
            Status.Name = device.Name;
            Status.Host = device.Host;
        }
    }

    /// <summary>
    /// Starts a test pattern.
    /// </summary>
    /// <param name="pattern">Pattern name.</param>
    /// <param name="seconds">Duration.</param>
    public void StartTest(string pattern, int seconds)
    {
        lock (_lock)
        {
            _test = (pattern, DateTime.UtcNow.AddSeconds(seconds), DateTime.UtcNow);
        }
    }

    /// <summary>
    /// Starts the loop.
    /// </summary>
    /// <param name="stopping">Global stop token.</param>
    public void Start(CancellationToken stopping)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        _loop = Task.Run(() => LoopAsync(_cts.Token), CancellationToken.None);
    }

    /// <summary>
    /// Stops the loop and releases the strip.
    /// </summary>
    /// <returns>A task.</returns>
    public async Task StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        await _cts.CancelAsync().ConfigureAwait(false);
        if (_loop is not null)
        {
            try
            {
                await _loop.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
            {
                // stopping anyway
            }
        }

        await ReleaseAsync(CancellationToken.None).ConfigureAwait(false);
        _sender?.Dispose();
        _sender = null;
        _cts.Dispose();
        _cts = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _sender?.Dispose();
        _sender = null;
        _cts?.Dispose();
        _cts = null;
    }

    private static bool Matches(WledDevice device, PlaybackState state)
    {
        if (device.FollowAllSessions)
        {
            return true;
        }

        if (device.FollowDeviceIds.Any(id => string.Equals(id, state.DeviceId, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return device.FollowUserIds.Any(id => Guid.TryParse(id, out var g) && g == state.UserId);
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var tickStart = Stopwatch.GetTimestamp();
            double fps = 25;
            try
            {
                WledDevice device;
                (string Pattern, DateTime Until, DateTime Started)? test;
                lock (_lock)
                {
                    device = _device;
                    test = _test;
                    if (test is not null && test.Value.Until < DateTime.UtcNow)
                    {
                        _test = null;
                        test = null;
                    }
                }

                EnsureLayout(device);
                fps = Math.Clamp(device.Fps, 1, 60);
                var cfg = Plugin.Instance?.Configuration ?? new PluginConfiguration();
                var analysisFps = cfg.EffectiveFps;

                if (test is not null)
                {
                    TestPatterns.Render(test.Value.Pattern, _layout, (DateTime.UtcNow - test.Value.Started).TotalSeconds, _raw);
                    var plain = new ColorPipeline(100, 1.0, 100, 0);
                    plain.Process(_raw, [], _current);
                    await SendAsync(device, ct).ConfigureAwait(false);
                    Status.State = "testing";
                    Status.SessionId = null;
                    Status.ItemName = test.Value.Pattern;
                    Status.HasFrame = true;
                    _active = true;
                }
                else
                {
                    // Stick with the session being followed while it is still playing; otherwise take the newest match.
                    var matching = _tracker.Active.Where(s => Matches(device, s)).ToList();
                    var session = matching.FirstOrDefault(s => s.SessionId == _followingSessionId)
                        ?? matching.OrderByDescending(s => s.ReportedAtStopwatch).FirstOrDefault();
                    _followingSessionId = session?.SessionId;
                    if (session is null)
                    {
                        if (_active)
                        {
                            await ReleaseAsync(ct).ConfigureAwait(false);
                        }

                        Status.State = Status.LastError is null ? "idle" : "error";
                        Status.SessionId = null;
                        Status.ItemName = null;
                        Status.HasFrame = false;
                        Status.PositionMs = null;
                        await Task.Delay(500, ct).ConfigureAwait(false);
                        continue;
                    }

                    var positionMs = session.PositionMsNow() + device.SyncOffsetMs;
                    var frame = Math.Max(0, positionMs * analysisFps / 1000);
                    var has = _store.TryReadFrame(session.ItemId, session.MediaSourceId, frame, _ring);
                    if (has)
                    {
                        _mapper!.Map(_ring, _raw);
                    }
                    else
                    {
                        Array.Clear(_raw);
                        if (DateTime.UtcNow - _lastMissReport > TimeSpan.FromSeconds(10))
                        {
                            _lastMissReport = DateTime.UtcNow;
                            _onMissingFrame?.Invoke(session); // let the manager queue analysis for an uncovered stretch
                        }
                    }

                    _pipeline!.Process(_raw, _active ? _previous : [], _current);
                    await SendAsync(device, ct).ConfigureAwait(false);
                    _active = true;
                    Status.State = "streaming";
                    Status.SessionId = session.SessionId;
                    Status.ItemName = session.ItemName;
                    Status.HasFrame = has;
                    Status.PositionMs = positionMs;
                }

                (_previous, _current) = (_current, _previous);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Status.LastError = ex.Message;
                Status.State = "error";
                _logger.LogWarning(ex, "[JellyWled] Output loop error for {Name}", Status.Name);
                try
                {
                    await Task.Delay(1000, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            var period = TimeSpan.FromSeconds(1.0 / fps);
            var remaining = period - Stopwatch.GetElapsedTime(tickStart);
            if (remaining > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(remaining, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private void EnsureLayout(WledDevice device)
    {
        var cfg = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var key = string.Join('|', device.LedsTop, device.LedsRight, device.LedsBottom, device.LedsLeft, device.StartCorner, device.Direction, device.Mode, device.Brightness, device.Saturation, device.Gamma, device.Smoothing, cfg.EffectiveRingWidth, cfg.EffectiveRingHeight, device.Host, device.DdpPort);
        if (key == _layoutKey && _mapper is not null)
        {
            return;
        }

        _layoutKey = key;
        _layout = RingLayout.Build(device.LedsTop, device.LedsRight, device.LedsBottom, device.LedsLeft, device.StartCorner, device.Direction);
        _mapper = new LedMapper(_layout, cfg.EffectiveRingWidth, cfg.EffectiveRingHeight, string.Equals(device.Mode, "average", StringComparison.OrdinalIgnoreCase));
        _pipeline = new ColorPipeline(device.Saturation, device.Gamma, device.Brightness, device.Smoothing);
        _ring = new byte[_mapper.ZoneCount * 3];
        _raw = new byte[_layout.Length * 3];
        _previous = new byte[_layout.Length * 3];
        _current = new byte[_layout.Length * 3];
        Status.LedCount = _layout.Length;
        var senderKey = device.Host + ":" + device.DdpPort;
        if (_sender is null || !string.Equals(_senderKey, senderKey, StringComparison.Ordinal))
        {
            _sender?.Dispose();
            _sender = new DdpSender(device.Host, device.DdpPort);
            _senderKey = senderKey;
        }
    }

    private async Task SendAsync(WledDevice device, CancellationToken ct)
    {
        _sender ??= new DdpSender(device.Host, device.DdpPort);
        var ok = await _sender.SendAsync(_current, ct).ConfigureAwait(false);
        var now = Stopwatch.GetTimestamp();
        if (ok)
        {
            Status.LastError = null;
            Status.LastPacketUtc = DateTime.UtcNow;
            _sent.Enqueue(now);
            while (_sent.Count > 50)
            {
                _sent.Dequeue();
            }

            if (_sent.Count >= 2)
            {
                var span = Stopwatch.GetElapsedTime(_sent.Peek(), now).TotalSeconds;
                Status.Fps = span > 0 ? Math.Round((_sent.Count - 1) / span, 1) : 0;
            }
        }
        else
        {
            Status.LastError = _sender.LastError;
            Status.Fps = 0;
        }
    }

    private async Task ReleaseAsync(CancellationToken ct)
    {
        _active = false;
        _sent.Clear();
        Status.Fps = 0;
        if (_sender is not null && _current.Length > 0)
        {
            Array.Clear(_current);
            await _sender.SendAsync(_current, ct).ConfigureAwait(false);
        }

        WledDevice device;
        lock (_lock)
        {
            device = _device;
        }

        if (device.RestoreState)
        {
            await _wled.PostStateAsync(device.Host, device.HttpPort, "{\"live\":false}", ct).ConfigureAwait(false);
        }
    }
}
