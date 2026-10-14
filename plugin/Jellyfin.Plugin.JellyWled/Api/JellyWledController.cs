using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Mime;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JellyWled.Analysis;
using Jellyfin.Plugin.JellyWled.Configuration;
using Jellyfin.Plugin.JellyWled.Output;
using Jellyfin.Plugin.JellyWled.Sessions;
using Jellyfin.Plugin.JellyWled.Tracks;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyWled.Api;

/// <summary>
/// The Jelly-WLED REST surface (PROTOCOL.md section 6).
/// </summary>
[ApiController]
[Route("JellyWled")]
[Authorize]
[Produces(MediaTypeNames.Application.Json)]
public class JellyWledController : ControllerBase
{
    private const int StreamFrameChunk = 256;

    /// <summary>Maximum jobs one non-administrator may have queued at once.</summary>
    private const int MaxJobsPerUser = 3;
    private readonly SessionTracker _tracker;
    private readonly TrackStore _store;
    private readonly AnalysisQueue _queue;
    private readonly OutputManager _output;
    private readonly EventHub _hub;
    private readonly WledHttpClient _wled;
    private readonly ILibraryManager _library;
    private readonly MediaResolver _resolver;
    private readonly IUserManager _users;
    private readonly ILogger<JellyWledController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyWledController"/> class.
    /// </summary>
    /// <param name="tracker">Session tracker.</param>
    /// <param name="store">Track store.</param>
    /// <param name="queue">Analysis queue.</param>
    /// <param name="output">Output manager.</param>
    /// <param name="hub">Event hub.</param>
    /// <param name="wled">WLED HTTP client.</param>
    /// <param name="library">Library manager.</param>
    /// <param name="resolver">Media resolver.</param>
    /// <param name="users">User manager.</param>
    /// <param name="logger">Logger.</param>
    public JellyWledController(SessionTracker tracker, TrackStore store, AnalysisQueue queue, OutputManager output, EventHub hub, WledHttpClient wled, ILibraryManager library, MediaResolver resolver, IUserManager users, ILogger<JellyWledController> logger)
    {
        _users = users;
        _tracker = tracker;
        _store = store;
        _queue = queue;
        _output = output;
        _hub = hub;
        _wled = wled;
        _library = library;
        _resolver = resolver;
        _logger = logger;
    }

    private bool IsAdmin => User.IsInRole("Administrator");

    private bool IsApiKey => string.Equals(User.FindFirstValue("Jellyfin-IsApiKey"), "true", StringComparison.OrdinalIgnoreCase);

    private Guid? CallerUserId => Guid.TryParse(User.FindFirstValue("Jellyfin-UserId"), out var g) && g != Guid.Empty ? g : null;

    /// <summary>Gets the calling user entity (null for API keys).</summary>
    private User? CallerUser => CallerUserId is { } id ? _users.GetUserById(id) : null;

    /// <summary>
    /// Plugin status: analysis jobs, tracked sessions, devices and cache size.
    /// </summary>
    /// <returns>The status.</returns>
    [HttpGet("Status")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public ActionResult<object> Status()
    {
        var (items, bytes) = _store.Stats();
        var cfg = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        return WebJson(new
        {
            pluginVersion = Plugin.Instance?.Version.ToString() ?? "0",
            serverTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            serverOutput = cfg.EnableServerOutput,
            cache = new { directory = _store.RootDirectory, items, bytes },
            jobs = _queue.Pending.Select(JobPayload).ToArray(),
            recentJobs = _queue.Recent.Select(JobPayload).ToArray(),
            sessions = _tracker.Active.Select(s => _hub.StatePayload(s)).ToArray(),
            devices = _output.Statuses.Select(d => new
            {
                deviceId = d.DeviceId,
                name = d.Name,
                host = d.Host,
                state = d.State,
                sessionId = d.SessionId,
                itemName = d.ItemName,
                fps = d.Fps,
                ledCount = d.LedCount,
                hasFrame = d.HasFrame,
                lastError = d.LastError,
                lastPacketUtc = d.LastPacketUtc,
                positionMs = d.PositionMs,
            }).ToArray(),
        });
    }

    /// <summary>
    /// Sessions visible to the caller with their track state.
    /// </summary>
    /// <returns>Sessions.</returns>
    [HttpGet("Sessions")]
    public ActionResult<object> Sessions()
    {
        var userId = IsApiKey ? null : CallerUserId;
        var admin = IsAdmin || IsApiKey;
        return WebJson(_tracker.Active.Where(s => EventHub.CanSee(s, userId, admin)).Select(s => _hub.StatePayload(s)).ToArray());
    }

    /// <summary>
    /// Server-Sent Events stream of playback state for the caller's sessions.
    /// </summary>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Never returns normally while the client is connected.</returns>
    [HttpGet("Events")]
    [Produces("text/event-stream")]
    public async Task Events(CancellationToken cancellationToken)
    {
        var userId = IsApiKey ? null : CallerUserId;
        var admin = IsAdmin || IsApiKey;
        Response.StatusCode = 200;
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        var (id, reader) = _hub.Subscribe(userId, admin);
        try
        {
            await WriteEventAsync("hello", JsonSerializer.Serialize(EventHub.HelloPayload(), EventHub.JsonOptions), cancellationToken).ConfigureAwait(false);
            foreach (var s in _tracker.Active.Where(s => EventHub.CanSee(s, userId, admin)))
            {
                await WriteEventAsync("state", JsonSerializer.Serialize(_hub.StatePayload(s), EventHub.JsonOptions), cancellationToken).ConfigureAwait(false);
            }

            // Pings go out on a fixed five-second clock regardless of traffic: clients use them as clock samples.
            var nextPing = DateTime.UtcNow.AddSeconds(5);
            while (!cancellationToken.IsCancellationRequested)
            {
                var wait = nextPing - DateTime.UtcNow;
                SseEvent? evt = null;
                if (wait > TimeSpan.Zero)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(wait);
                    try
                    {
                        evt = await reader.ReadAsync(timeout.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        // ping due
                    }
                    catch (ChannelClosedException)
                    {
                        break;
                    }
                }

                if (evt is null)
                {
                    await WriteEventAsync("ping", JsonSerializer.Serialize(new { serverTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }, EventHub.JsonOptions), cancellationToken).ConfigureAwait(false);
                    nextPing = DateTime.UtcNow.AddSeconds(5);
                }
                else
                {
                    await WriteEventAsync(evt.Name, evt.Json, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // client went away
        }
        finally
        {
            _hub.Unsubscribe(id);
        }
    }

    /// <summary>
    /// Streams glow track frames from a position (PROTOCOL.md 1.3).
    /// </summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="mediaSourceId">Media source id.</param>
    /// <param name="fromMs">Start position in milliseconds.</param>
    /// <param name="analyze">Start on-demand analysis when frames are missing.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Binary stream.</returns>
    [HttpGet("Track/{itemId}")]
    [Produces(MediaTypeNames.Application.Octet)]
    public async Task<IActionResult> Track([FromRoute] Guid itemId, [FromQuery] string? mediaSourceId, [FromQuery] long fromMs = 0, [FromQuery] bool analyze = true, CancellationToken cancellationToken = default)
    {
        if (!CanAccessItem(itemId))
        {
            return NotFound();
        }

        var source = _resolver.Resolve(itemId, mediaSourceId, IsAdmin || IsApiKey ? null : CallerUser);
        if (source is null)
        {
            return NotFound(); // not a local video file: no track can exist
        }

        var msid = source.MediaSourceId;
        var cfg = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var fps = cfg.EffectiveFps;
        fromMs = Math.Clamp(fromMs, 0, Math.Max(0, source.RunTimeTicks / 10_000));
        var frame = fromMs * fps / 1000;
        var info = _store.GetInfo(itemId, msid, source.RunTimeTicks);
        AnalysisJob? job = null;
        if (analyze && info.State != TrackState.Complete && !info.Segments.Any(s => s.IsFinalized && s.Contains(frame)) && AllowNewJob())
        {
            job = _output.EnsureTrack(itemId, msid, fromMs, "api", force: IsAdmin || IsApiKey);
            if (job is not null && job.RequestedBy is null && !IsApiKey)
            {
                job.RequestedBy = CallerUserId;
            }
        }

        job ??= _queue.Find(itemId, msid);
        if (info.State == TrackState.None && job is null)
        {
            Response.Headers["X-JellyWled-Track"] = "none";
            return NotFound();
        }

        var header = new SegmentHeader
        {
            RingWidth = (ushort)cfg.EffectiveRingWidth,
            RingHeight = (ushort)cfg.EffectiveRingHeight,
            Fps = (ushort)fps,
            StartFrame = (uint)Math.Min(uint.MaxValue, frame),
            BandPermille = (ushort)(cfg.BandPercent * 10),
        };
        var first = info.Segments.Count > 0 ? info.Segments[0] : null;
        if (first is not null)
        {
            header.SourceSize = first.Header.SourceSize;
            header.SourceMtimeMs = first.Header.SourceMtimeMs;
            header.CropX = first.Header.CropX;
            header.CropY = first.Header.CropY;
            header.CropW = first.Header.CropW;
            header.CropH = first.Header.CropH;
        }

        Response.StatusCode = 200;
        Response.ContentType = MediaTypeNames.Application.Octet;
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-JellyWled-Track"] = info.State switch
        {
            TrackState.Complete => "complete",
            TrackState.Partial => job is not null ? "analyzing" : "partial",
            _ => "analyzing",
        };
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        await Response.Body.WriteAsync(header.ToBytes(), cancellationToken).ConfigureAwait(false);
        await Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);

        var buffer = new byte[header.FrameBytes * StreamFrameChunk];
        var idleSince = DateTime.UtcNow;
        var current = frame;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int n;
                try
                {
                    var reader = _store.FindReader(itemId, msid, current);
                    n = reader?.ReadRun(current, buffer) ?? 0;
                }
                catch (Exception ex) when (ex is System.IO.IOException or ObjectDisposedException)
                {
                    // A segment was replaced or pruned under us; try again on the next pass.
                    n = 0;
                }

                if (n > 0)
                {
                    await Response.Body.WriteAsync(buffer.AsMemory(0, n * header.FrameBytes), cancellationToken).ConfigureAwait(false);
                    await Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
                    current += n;
                    idleSince = DateTime.UtcNow;
                    continue;
                }

                // Nothing readable at this frame: wait only while an analysis is producing frames that will reach it.
                var pending = _queue.Find(itemId, msid);
                var willArrive = pending is not null && pending.State is "running" or "queued" && pending.StartFrame <= current;
                if (!willArrive || DateTime.UtcNow - idleSince > TimeSpan.FromSeconds(15))
                {
                    break;
                }

                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // client went away
        }

        return new EmptyResult();
    }

    /// <summary>
    /// Track summary for an item.
    /// </summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="mediaSourceId">Media source id.</param>
    /// <returns>The summary.</returns>
    [HttpGet("Track/{itemId}/Info")]
    public ActionResult<object> TrackInfo([FromRoute] Guid itemId, [FromQuery] string? mediaSourceId)
    {
        if (!CanAccessItem(itemId))
        {
            return NotFound();
        }

        var source = _resolver.Resolve(itemId, mediaSourceId, IsAdmin || IsApiKey ? null : CallerUser);
        if (source is null)
        {
            return NotFound();
        }

        var info = _store.GetInfo(itemId, source.MediaSourceId, source.RunTimeTicks);
        return WebJson(EventHub.TrackPayload(info, _queue.Find(itemId, source.MediaSourceId)));
    }

    /// <summary>
    /// Queues an analysis of an item.
    /// </summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="mediaSourceId">Media source id.</param>
    /// <param name="fromMs">Start position.</param>
    /// <returns>The job.</returns>
    [HttpPost("Track/{itemId}/Analyze")]
    public ActionResult<object> Analyze([FromRoute] Guid itemId, [FromQuery] string? mediaSourceId, [FromQuery] long fromMs = 0)
    {
        if (!CanAccessItem(itemId))
        {
            return NotFound();
        }

        var source = _resolver.Resolve(itemId, mediaSourceId, IsAdmin || IsApiKey ? null : CallerUser);
        if (source is null)
        {
            return BadRequest(new { error = "Item is not a local video file." });
        }

        if (!AllowNewJob())
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "Too many analyses queued for this user." });
        }

        fromMs = Math.Clamp(fromMs, 0, Math.Max(0, source.RunTimeTicks / 10_000));
        var job = _output.EnsureTrack(itemId, source.MediaSourceId, fromMs, "api", force: IsAdmin || IsApiKey) ?? _queue.Find(itemId, source.MediaSourceId);
        if (job is not null && job.RequestedBy is null && !IsApiKey)
        {
            job.RequestedBy = CallerUserId;
        }

        return job is null ? WebJson(new { state = "complete" }) : WebJson(JobPayload(job));
    }

    /// <summary>
    /// Deletes an item's track.
    /// </summary>
    /// <param name="itemId">Item id.</param>
    /// <returns>No content.</returns>
    [HttpDelete("Track/{itemId}")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public IActionResult DeleteTrack([FromRoute] Guid itemId)
    {
        _queue.Cancel(itemId);
        _store.Delete(itemId);
        return NoContent();
    }

    /// <summary>
    /// Runs a test pattern on a server-driven device.
    /// </summary>
    /// <param name="deviceId">Device id.</param>
    /// <param name="pattern">sides, chase, white or off.</param>
    /// <param name="seconds">Duration.</param>
    /// <returns>Ok or not found.</returns>
    [HttpPost("Devices/{deviceId}/Test")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public IActionResult Test([FromRoute] Guid deviceId, [FromQuery] string pattern = "sides", [FromQuery] int seconds = 10)
    {
        return _output.StartTest(deviceId, pattern, seconds)
            ? Ok(new { ok = true })
            : NotFound(new { error = "Device is not running (disabled, no host, or server output is off). Save first." });
    }

    /// <summary>
    /// Fetches /json/info from a configured device through the server.
    /// </summary>
    /// <param name="deviceId">Device id.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The WLED info object.</returns>
    [HttpGet("Devices/{deviceId}/Probe")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public Task<ActionResult<object>> ProbeDevice([FromRoute] Guid deviceId, CancellationToken cancellationToken)
    {
        var cfg = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var device = cfg.Devices.FirstOrDefault(d => d.Id == deviceId);
        if (device is null)
        {
            return Task.FromResult<ActionResult<object>>(NotFound());
        }

        return Probe(device.Host, device.HttpPort, cancellationToken);
    }

    /// <summary>
    /// Fetches /json/info from any host through the server (for devices not saved yet).
    /// </summary>
    /// <param name="host">Host.</param>
    /// <param name="httpPort">HTTP port.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The WLED info object.</returns>
    [HttpGet("Probe")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<ActionResult<object>> Probe([FromQuery] string host, [FromQuery] int httpPort = 80, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return BadRequest(new { error = "host is required" });
        }

        try
        {
            using var doc = await _wled.GetInfoAsync(host, httpPort, cancellationToken).ConfigureAwait(false);
            var root = doc.RootElement;
            return WebJson(new
            {
                ok = true,
                version = root.TryGetProperty("ver", out var v) ? v.ToString() : null,
                name = root.TryGetProperty("name", out var n) ? n.ToString() : null,
                ledCount = root.TryGetProperty("leds", out var leds) && leds.TryGetProperty("count", out var c) && c.TryGetInt32(out var count) ? count : (int?)null,
                live = root.TryGetProperty("live", out var l) && l.ValueKind == JsonValueKind.True,
                liveSource = root.TryGetProperty("lip", out var lip) ? lip.ToString() : null,
                ip = root.TryGetProperty("ip", out var ip) ? ip.ToString() : null,
            });
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or JsonException or UriFormatException)
        {
            _logger.LogInformation("[JellyWled] Probe of {Host} failed: {Error}", host, ex.Message);
            return WebJson(new { ok = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Serializes with the same options as the SSE stream (camelCase, dashed Guids) so clients see one dialect.
    /// </summary>
    private static ContentResult WebJson(object payload) => new()
    {
        Content = JsonSerializer.Serialize(payload, EventHub.JsonOptions),
        ContentType = MediaTypeNames.Application.Json,
        StatusCode = StatusCodes.Status200OK,
    };

    private bool AllowNewJob() => IsAdmin || IsApiKey || (CallerUserId is { } id && _queue.PendingFor(id) < MaxJobsPerUser);

    private static object JobPayload(AnalysisJob j) => new
    {
        itemId = j.ItemId,
        mediaSourceId = j.MediaSourceId,
        itemName = j.ItemName,
        reason = j.Reason,
        state = j.State,
        startFrame = j.StartFrame,
        doneFrames = j.DoneFrames,
        expectedFrames = j.ExpectedFrames,
        error = j.Error,
        queuedAt = j.QueuedAt,
        startedAt = j.StartedAt,
        finishedAt = j.FinishedAt,
    };

    private bool CanAccessItem(Guid itemId)
    {
        if (IsApiKey || IsAdmin)
        {
            return _library.GetItemById<BaseItem>(itemId) is not null;
        }

        var userId = CallerUserId;
        return userId is not null && _library.GetItemById<BaseItem>(itemId, userId.Value) is not null;
    }

    private async Task WriteEventAsync(string name, string json, CancellationToken cancellationToken)
    {
        var text = string.Create(CultureInfo.InvariantCulture, $"event: {name}\ndata: {json}\n\n");
        await Response.Body.WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken).ConfigureAwait(false);
        await Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
