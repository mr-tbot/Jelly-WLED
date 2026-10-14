using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.JellyWled.Configuration;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyWled.Tracks;

/// <summary>
/// On-disk layout of glow tracks: cache, item id, media source, start frame ".seg" (PROTOCOL.md 1.2).
/// Keeps a small pool of open readers so the output loop and the track stream never reopen files per frame.
/// </summary>
public sealed partial class TrackStore : IDisposable
{
    private const int MaxOpenReaders = 32;
    private readonly IApplicationPaths _paths;
    private readonly ILogger<TrackStore> _logger;
    private readonly object _lock = new();
    private readonly Dictionary<string, (SegmentReader Reader, long LastUse)> _readers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (DateTime At, List<string> Files)> _scans = new(StringComparer.Ordinal);
    private long _useCounter;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrackStore"/> class.
    /// </summary>
    /// <param name="paths">Application paths.</param>
    /// <param name="logger">Logger.</param>
    public TrackStore(IApplicationPaths paths, ILogger<TrackStore> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    /// <summary>Gets the current plugin configuration.</summary>
    private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

    /// <summary>
    /// Gets the cache root directory (created on demand).
    /// </summary>
    public string RootDirectory
    {
        get
        {
            var configured = Config.CacheDirectory;
            var root = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(_paths.DataPath, "jellywled", "tracks")
                : configured.Trim();
            Directory.CreateDirectory(root);
            return root;
        }
    }

    /// <summary>
    /// Gets the directory of one item's track.
    /// </summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="mediaSourceId">Media source id or null for the default source.</param>
    /// <returns>The directory path (not created).</returns>
    public string ItemDirectory(Guid itemId, string? mediaSourceId)
    {
        var item = itemId.ToString("N", CultureInfo.InvariantCulture);
        var source = string.IsNullOrWhiteSpace(mediaSourceId) || string.Equals(mediaSourceId, item, StringComparison.OrdinalIgnoreCase)
            ? "default"
            : SafeName().Replace(mediaSourceId, "_");
        return Path.Combine(RootDirectory, item, source);
    }

    /// <summary>
    /// Gets the segment path for a start frame.
    /// </summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="mediaSourceId">Media source id.</param>
    /// <param name="startFrame">First frame.</param>
    /// <returns>The ".seg" path.</returns>
    public string SegmentPath(Guid itemId, string? mediaSourceId, long startFrame)
        => Path.Combine(ItemDirectory(itemId, mediaSourceId), startFrame.ToString("D10", CultureInfo.InvariantCulture) + ".seg");

    /// <summary>
    /// Lists the segments of an item that match the current analysis parameters, newest-finalized first.
    /// </summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="mediaSourceId">Media source id.</param>
    /// <returns>Segments.</returns>
    public IReadOnlyList<SegmentInfo> ListSegments(Guid itemId, string? mediaSourceId)
    {
        var dir = ItemDirectory(itemId, mediaSourceId);
        var cfg = Config;
        var list = new List<SegmentInfo>();
        foreach (var file in ScanDirectory(dir))
        {
            try
            {
                var reader = GetReader(file);
                var h = reader.Header;
                if (h.RingWidth != cfg.RingWidth || h.RingHeight != cfg.RingHeight || h.Fps != cfg.AnalysisFps)
                {
                    continue;
                }

                list.Add(new SegmentInfo(file, h, reader.ReadableFrames));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "[JellyWled] Skipping unreadable segment {Path}", file);
            }
        }

        return list
            .OrderByDescending(s => s.IsFinalized)
            .ThenByDescending(s => s.ReadableFrames)
            .ToList();
    }

    /// <summary>
    /// Summarizes an item's track.
    /// </summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="mediaSourceId">Media source id.</param>
    /// <param name="runTimeTicks">Item runtime in ticks, if known.</param>
    /// <returns>The summary.</returns>
    public TrackInfo GetInfo(Guid itemId, string? mediaSourceId, long? runTimeTicks)
    {
        var cfg = Config;
        var segments = ListSegments(itemId, mediaSourceId);
        var expected = runTimeTicks is > 0 ? (long)Math.Floor(runTimeTicks.Value / 10_000_000.0 * cfg.AnalysisFps) : 0;
        var frames = segments.Sum(s => s.ReadableFrames);
        var state = TrackState.None;
        if (frames > 0)
        {
            // Complete when one finalized segment starts at 0 and reaches the expected end (allow one second of slack).
            var covers = segments.Any(s => s.IsFinalized && s.StartFrame == 0 && (expected == 0 || s.EndFrame >= expected - cfg.AnalysisFps));
            state = covers ? TrackState.Complete : TrackState.Partial;
        }

        return new TrackInfo(state, cfg.AnalysisFps, cfg.RingWidth, cfg.RingHeight, expected, frames, segments);
    }

    /// <summary>
    /// Finds the segment that can serve a frame (finalized segments win, then the longest).
    /// </summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="mediaSourceId">Media source id.</param>
    /// <param name="frame">Absolute frame.</param>
    /// <returns>The reader or null.</returns>
    public SegmentReader? FindReader(Guid itemId, string? mediaSourceId, long frame)
    {
        foreach (var s in ListSegments(itemId, mediaSourceId))
        {
            if (s.Contains(frame))
            {
                return GetReader(s.Path);
            }
        }

        return null;
    }

    /// <summary>
    /// Reads one frame of an item's track.
    /// </summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="mediaSourceId">Media source id.</param>
    /// <param name="frame">Absolute frame.</param>
    /// <param name="destination">Receives the ring bytes.</param>
    /// <returns>True when a frame was read.</returns>
    public bool TryReadFrame(Guid itemId, string? mediaSourceId, long frame, Span<byte> destination)
    {
        var reader = FindReader(itemId, mediaSourceId, frame);
        return reader is not null && reader.TryRead(frame, destination);
    }

    /// <summary>
    /// Creates a writer for a new segment.
    /// </summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="mediaSourceId">Media source id.</param>
    /// <param name="header">Header with StartFrame and parameters set.</param>
    /// <returns>The writer.</returns>
    public SegmentWriter CreateWriter(Guid itemId, string? mediaSourceId, SegmentHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        var path = SegmentPath(itemId, mediaSourceId, header.StartFrame);
        ForgetReader(path);
        ForgetReader(path + ".part");
        InvalidateScan(ItemDirectory(itemId, mediaSourceId));
        return new SegmentWriter(path, header);
    }

    /// <summary>
    /// Deletes everything stored for an item.
    /// </summary>
    /// <param name="itemId">Item id.</param>
    public void Delete(Guid itemId)
    {
        var dir = Path.Combine(RootDirectory, itemId.ToString("N", CultureInfo.InvariantCulture));
        lock (_lock)
        {
            foreach (var key in _readers.Keys.Where(k => k.StartsWith(dir, StringComparison.Ordinal)).ToList())
            {
                _readers[key].Reader.Dispose();
                _readers.Remove(key);
            }

            foreach (var key in _scans.Keys.Where(k => k.StartsWith(dir, StringComparison.Ordinal)).ToList())
            {
                _scans.Remove(key);
            }
        }

        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// Removes segments that no longer match the analysis parameters and stale part files.
    /// </summary>
    /// <param name="partMaxAge">Part files older than this are deleted.</param>
    /// <returns>Files deleted.</returns>
    public int Prune(TimeSpan partMaxAge)
    {
        var cfg = Config;
        var deleted = 0;
        var now = DateTime.UtcNow;
        foreach (var file in Directory.EnumerateFiles(RootDirectory, "*.seg*", SearchOption.AllDirectories))
        {
            try
            {
                var isPart = file.EndsWith(".part", StringComparison.Ordinal);
                if (isPart && now - File.GetLastWriteTimeUtc(file) < partMaxAge)
                {
                    continue;
                }

                var reader = GetReader(file);
                var h = reader.Header;
                var stale = h.RingWidth != cfg.RingWidth || h.RingHeight != cfg.RingHeight || h.Fps != cfg.AnalysisFps;
                if (stale || isPart)
                {
                    ForgetReader(file);
                    File.Delete(file);
                    deleted++;
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "[JellyWled] Prune skipped {Path}", file);
            }
        }

        return deleted;
    }

    /// <summary>
    /// Gets cache statistics.
    /// </summary>
    /// <returns>Item count and total bytes.</returns>
    public (int Items, long Bytes) Stats()
    {
        var root = RootDirectory;
        var items = Directory.EnumerateDirectories(root).Count();
        long bytes = 0;
        foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            try
            {
                bytes += new FileInfo(f).Length;
            }
            catch (IOException)
            {
                // ignore
            }
        }

        return (items, bytes);
    }

    /// <summary>
    /// Drops a cached reader (after a file was replaced or deleted).
    /// </summary>
    /// <param name="path">File path.</param>
    public void ForgetReader(string path)
    {
        lock (_lock)
        {
            if (_readers.Remove(path, out var entry))
            {
                entry.Reader.Dispose();
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var entry in _readers.Values)
            {
                entry.Reader.Dispose();
            }

            _readers.Clear();
        }
    }

    [GeneratedRegex("[^A-Za-z0-9_-]")]
    private static partial Regex SafeName();

    /// <summary>
    /// Directory listing cached for one second: the output loop asks for segments 25 times per second per device.
    /// Readable frame counts are never cached (they come from the live file size).
    /// </summary>
    private List<string> ScanDirectory(string dir)
    {
        lock (_lock)
        {
            if (_scans.TryGetValue(dir, out var cached) && DateTime.UtcNow - cached.At < TimeSpan.FromSeconds(1))
            {
                return cached.Files;
            }

            var files = new List<string>();
            if (Directory.Exists(dir))
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*.seg*"))
                {
                    if (file.EndsWith(".seg", StringComparison.Ordinal) || file.EndsWith(".seg.part", StringComparison.Ordinal))
                    {
                        files.Add(file);
                    }
                }
            }

            _scans[dir] = (DateTime.UtcNow, files);
            return files;
        }
    }

    private void InvalidateScan(string dir)
    {
        lock (_lock)
        {
            _scans.Remove(dir);
        }
    }

    private SegmentReader GetReader(string path)
    {
        lock (_lock)
        {
            if (_readers.TryGetValue(path, out var entry))
            {
                _readers[path] = (entry.Reader, ++_useCounter);
                return entry.Reader;
            }

            if (_readers.Count >= MaxOpenReaders)
            {
                var victim = _readers.OrderBy(kv => kv.Value.LastUse).First();
                victim.Value.Reader.Dispose();
                _readers.Remove(victim.Key);
            }

            var reader = new SegmentReader(path);
            _readers[path] = (reader, ++_useCounter);
            return reader;
        }
    }
}
