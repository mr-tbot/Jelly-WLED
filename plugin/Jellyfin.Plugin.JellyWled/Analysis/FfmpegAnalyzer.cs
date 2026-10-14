using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyWled.Configuration;
using Jellyfin.Plugin.JellyWled.Glow;
using Jellyfin.Plugin.JellyWled.Tracks;
using MediaBrowser.Controller.MediaEncoding;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyWled.Analysis;

/// <summary>
/// Runs ffmpeg at low priority and turns every output frame into a ring of edge colors.
/// </summary>
public sealed partial class FfmpegAnalyzer
{
    private readonly IMediaEncoder _encoder;
    private readonly ILogger<FfmpegAnalyzer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FfmpegAnalyzer"/> class.
    /// </summary>
    /// <param name="encoder">Jellyfin's media encoder (for the ffmpeg path).</param>
    /// <param name="logger">Logger.</param>
    public FfmpegAnalyzer(IMediaEncoder encoder, ILogger<FfmpegAnalyzer> logger)
    {
        _encoder = encoder;
        _logger = logger;
    }

    /// <summary>
    /// Computes the ring of one thumbnail frame (PROTOCOL.md 1.1).
    /// </summary>
    /// <param name="thumb">W x H rgb24 pixels.</param>
    /// <param name="width">Thumbnail width (ring width).</param>
    /// <param name="height">Thumbnail height (ring height).</param>
    /// <param name="band">Edge band depth in pixels (at least 1).</param>
    /// <param name="ring">Receives (2W + 2H) * 3 bytes in ring order.</param>
    public static void ComputeRing(ReadOnlySpan<byte> thumb, int width, int height, int band, Span<byte> ring)
    {
        band = Math.Clamp(band, 1, Math.Max(1, Math.Min(width, height)));
        for (var x = 0; x < width; x++)
        {
            Average(thumb, width, x, x + 1, 0, band, ring, RingLayout.ZoneIndex(Side.Top, x, width, height));
            Average(thumb, width, x, x + 1, height - band, height, ring, RingLayout.ZoneIndex(Side.Bottom, x, width, height));
        }

        for (var y = 0; y < height; y++)
        {
            Average(thumb, width, width - band, width, y, y + 1, ring, RingLayout.ZoneIndex(Side.Right, y, width, height));
            Average(thumb, width, 0, band, y, y + 1, ring, RingLayout.ZoneIndex(Side.Left, y, width, height));
        }
    }

    /// <summary>
    /// Analyzes a range of a file into a new segment.
    /// </summary>
    /// <param name="source">The resolved media file.</param>
    /// <param name="store">The track store that receives the segment.</param>
    /// <param name="startFrame">First frame to produce.</param>
    /// <param name="endFrame">Frame index to stop at (exclusive), or null for the end of the file.</param>
    /// <param name="progress">Called with frames written so far.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The number of frames written.</returns>
    public async Task<long> AnalyzeAsync(ResolvedSource source, TrackStore store, long startFrame, long? endFrame, Action<long>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(store);
        var cfg = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var fps = Math.Clamp(cfg.AnalysisFps, 1, 60);
        var width = Math.Clamp(cfg.RingWidth, 4, 128);
        var height = Math.Clamp(cfg.RingHeight, 3, 72);
        var bandPercent = Math.Clamp(cfg.BandPercent, 1, 50);
        var band = Math.Max(1, (int)Math.Round(bandPercent / 100.0 * height));

        var fileInfo = new FileInfo(source.Path);
        var crop = cfg.DetectLetterbox
            ? await DetectCropAsync(source.Path, source.RunTimeTicks, cancellationToken).ConfigureAwait(false)
            : CropInfo.None;

        var header = new SegmentHeader
        {
            RingWidth = (ushort)width,
            RingHeight = (ushort)height,
            Fps = (ushort)fps,
            StartFrame = (uint)Math.Min(uint.MaxValue, startFrame),
            SourceSize = (ulong)fileInfo.Length,
            SourceMtimeMs = (ulong)new DateTimeOffset(fileInfo.LastWriteTimeUtc).ToUnixTimeMilliseconds(),
            BandPermille = (ushort)(bandPercent * 10),
            CropX = crop.X,
            CropY = crop.Y,
            CropW = crop.W,
            CropH = crop.H,
        };

        var filter = new StringBuilder();
        if (crop.Filter.Length > 0)
        {
            filter.Append(crop.Filter).Append(',');
        }

        filter.Append(CultureInfo.InvariantCulture, $"fps={fps},scale={width}:{height}:flags=area");

        var args = new List<string> { "-nostdin", "-v", "error", "-threads", Math.Clamp(cfg.AnalysisThreads, 1, 16).ToString(CultureInfo.InvariantCulture) };
        if (cfg.FastDecode)
        {
            args.AddRange(["-skip_frame", "bidir"]);
        }

        if (startFrame > 0)
        {
            args.AddRange(["-ss", ((double)startFrame / fps).ToString("F3", CultureInfo.InvariantCulture)]);
        }

        args.AddRange(["-i", source.Path, "-an", "-sn", "-dn"]);
        if (endFrame is not null && endFrame.Value > startFrame)
        {
            args.AddRange(["-t", ((double)(endFrame.Value - startFrame) / fps).ToString("F3", CultureInfo.InvariantCulture)]);
        }

        args.AddRange(["-vf", filter.ToString(), "-f", "rawvideo", "-pix_fmt", "rgb24", "-"]);

        using var process = StartFfmpeg(args, cfg.LowPriority, redirectStdout: true);
        var stderrTask = ReadTailAsync(process.StandardError, cancellationToken);
        using var writer = store.CreateWriter(source.ItemId, source.MediaSourceId, header);
        var frameBytes = width * height * 3;
        var thumb = new byte[frameBytes];
        var ring = new byte[RingLayout.ZoneCount(width, height) * 3];
        long frames = 0;
        try
        {
            var stdout = process.StandardOutput.BaseStream;
            while (true)
            {
                var read = await ReadFullyAsync(stdout, thumb, cancellationToken).ConfigureAwait(false);
                if (read < frameBytes)
                {
                    break;
                }

                ComputeRing(thumb, width, height, band, ring);
                writer.WriteFrame(ring);
                frames++;
                if (frames % 20 == 0)
                {
                    progress?.Invoke(frames);
                }
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var tail = await stderrTask.ConfigureAwait(false);
            if (process.ExitCode != 0 && frames == 0)
            {
                throw new InvalidOperationException($"ffmpeg exit {process.ExitCode}: {tail}");
            }

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("[JellyWled] ffmpeg ended with exit {Code} after {Frames} frames for {Name}: {Tail}", process.ExitCode, frames, source.Name, tail);
            }

            if (frames == 0)
            {
                throw new InvalidOperationException("ffmpeg produced no frames: " + tail);
            }

            writer.Complete();
            progress?.Invoke(frames);
            return frames;
        }
        catch
        {
            writer.Abort();
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // already gone
            }

            throw;
        }
    }

    /// <summary>
    /// Samples cropdetect at a few points of the file and returns the most common crop.
    /// </summary>
    /// <param name="path">Media path.</param>
    /// <param name="runTimeTicks">Runtime in ticks (0 = unknown).</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The crop, or <see cref="CropInfo.None"/>.</returns>
    public async Task<CropInfo> DetectCropAsync(string path, long runTimeTicks, CancellationToken cancellationToken)
    {
        var durationS = runTimeTicks / 10_000_000.0;
        var points = durationS > 20
            ? new[] { 0.1, 0.3, 0.5, 0.7, 0.9 }.Select(f => f * durationS).ToArray()
            : [Math.Max(0, durationS / 2)];
        var votes = new Dictionary<string, int>(StringComparer.Ordinal);
        int srcW = 0, srcH = 0;
        foreach (var t in points)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var args = new List<string> { "-nostdin", "-v", "info", "-ss", t.ToString("F2", CultureInfo.InvariantCulture), "-i", path, "-an", "-sn", "-dn", "-t", "1.5", "-vf", "cropdetect=24:2:0", "-f", "null", "-" };
            try
            {
                using var process = StartFfmpeg(args, lowPriority: true, redirectStdout: false);
                var text = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                var dims = VideoDims().Match(text);
                if (dims.Success && srcW == 0)
                {
                    srcW = int.Parse(dims.Groups[1].Value, CultureInfo.InvariantCulture);
                    srcH = int.Parse(dims.Groups[2].Value, CultureInfo.InvariantCulture);
                }

                foreach (Match m in CropLine().Matches(text))
                {
                    var key = m.Groups[1].Value;
                    votes[key] = votes.GetValueOrDefault(key) + 1;
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                _logger.LogDebug(ex, "[JellyWled] cropdetect sample failed at {Time}s for {Path}", t, path);
            }
        }

        if (srcW == 0 || srcH == 0 || votes.Count == 0)
        {
            return CropInfo.None;
        }

        var best = votes.OrderByDescending(kv => kv.Value).First().Key; // "w:h:x:y"
        var parts = best.Split(':').Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        int w = parts[0], h = parts[1], x = parts[2], y = parts[3];
        if (w <= 0 || h <= 0 || (long)w * h < (long)srcW * srcH / 2)
        {
            return CropInfo.None;
        }

        if (w >= srcW - 4 && h >= srcH - 4)
        {
            return CropInfo.None;
        }

        return new CropInfo(
            (ushort)(1000L * x / srcW),
            (ushort)(1000L * y / srcH),
            (ushort)(1000L * w / srcW),
            (ushort)(1000L * h / srcH),
            string.Create(CultureInfo.InvariantCulture, $"crop={w}:{h}:{x}:{y}"));
    }

    [GeneratedRegex(@"crop=(\d+:\d+:\d+:\d+)")]
    private static partial Regex CropLine();

    [GeneratedRegex(@"Video:.*?[, ](\d{2,5})x(\d{2,5})[, \[]")]
    private static partial Regex VideoDims();

    private static void Average(ReadOnlySpan<byte> thumb, int width, int x0, int x1, int y0, int y1, Span<byte> ring, int zone)
    {
        int r = 0, g = 0, b = 0, n = 0;
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var o = ((y * width) + x) * 3;
                r += thumb[o];
                g += thumb[o + 1];
                b += thumb[o + 2];
                n++;
            }
        }

        n = Math.Max(1, n);
        ring[zone * 3] = (byte)(r / n);
        ring[(zone * 3) + 1] = (byte)(g / n);
        ring[(zone * 3) + 2] = (byte)(b / n);
    }

    private static async Task<int> ReadFullyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false);
            if (n <= 0)
            {
                break;
            }

            total += n;
        }

        return total;
    }

    private static async Task<string> ReadTailAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        // Drain stderr continuously (a damaged file can print a line per bad frame) but keep only the tail.
        var tail = new Queue<string>();
        try
        {
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                tail.Enqueue(line);
                if (tail.Count > 8)
                {
                    tail.Dequeue();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // stop draining
        }

        return string.Join(" | ", tail);
    }

    private Process StartFfmpeg(List<string> ffmpegArgs, bool lowPriority, bool redirectStdout)
    {
        var psi = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = redirectStdout,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        var ffmpeg = _encoder.EncoderPath;
        if (lowPriority && OperatingSystem.IsLinux() && File.Exists("/usr/bin/nice"))
        {
            psi.FileName = "/usr/bin/nice";
            psi.ArgumentList.Add("-n");
            psi.ArgumentList.Add("19");
            if (File.Exists("/usr/bin/ionice"))
            {
                psi.ArgumentList.Add("/usr/bin/ionice");
                psi.ArgumentList.Add("-c");
                psi.ArgumentList.Add("3");
            }

            psi.ArgumentList.Add(ffmpeg);
        }
        else
        {
            psi.FileName = ffmpeg;
        }

        foreach (var a in ffmpegArgs)
        {
            psi.ArgumentList.Add(a);
        }

        var process = new Process { StartInfo = psi };
        process.Start();
        if (lowPriority && !OperatingSystem.IsLinux())
        {
            try
            {
                process.PriorityClass = ProcessPriorityClass.Idle;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                _logger.LogDebug(ex, "[JellyWled] Could not lower ffmpeg priority");
            }
        }

        return process;
    }
}
