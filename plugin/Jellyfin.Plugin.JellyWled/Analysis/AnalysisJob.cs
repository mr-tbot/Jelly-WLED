using System;
using System.Threading;

namespace Jellyfin.Plugin.JellyWled.Analysis;

/// <summary>
/// One analysis request and its live progress.
/// </summary>
public sealed class AnalysisJob
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AnalysisJob"/> class.
    /// </summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="mediaSourceId">Media source id or null.</param>
    /// <param name="fromMs">Start position in milliseconds.</param>
    /// <param name="reason">Why it was queued (playback, task, api, library).</param>
    public AnalysisJob(Guid itemId, string? mediaSourceId, long fromMs, string reason)
    {
        ItemId = itemId;
        MediaSourceId = mediaSourceId;
        FromMs = Math.Max(0, fromMs);
        Reason = reason;
        QueuedAt = DateTime.UtcNow;
    }

    /// <summary>Gets the item id.</summary>
    public Guid ItemId { get; }

    /// <summary>Gets the media source id.</summary>
    public string? MediaSourceId { get; }

    /// <summary>Gets the start position in milliseconds.</summary>
    public long FromMs { get; }

    /// <summary>Gets the reason.</summary>
    public string Reason { get; }

    /// <summary>Gets when the job was queued.</summary>
    public DateTime QueuedAt { get; }

    /// <summary>Gets or sets the item name once resolved.</summary>
    public string ItemName { get; set; } = string.Empty;

    /// <summary>Gets or sets the first frame being produced.</summary>
    public long StartFrame { get; set; }

    /// <summary>Gets or sets the frames the job is expected to produce (0 = unknown).</summary>
    public long ExpectedFrames { get; set; }

    /// <summary>Gets or sets the frames produced so far.</summary>
    public long DoneFrames { get; set; }

    /// <summary>Gets or sets the state: queued, running, done, failed, canceled, skipped.</summary>
    public string State { get; set; } = "queued";

    /// <summary>Gets or sets the error message.</summary>
    public string? Error { get; set; }

    /// <summary>Gets or sets when the job started running.</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>Gets or sets when the job finished.</summary>
    public DateTime? FinishedAt { get; set; }

    /// <summary>Gets or sets a position (ms) to restart from after this job is canceled by a seek.</summary>
    public long? RetargetFromMs { get; set; }

    /// <summary>Gets the cancellation source.</summary>
    public CancellationTokenSource Cancellation { get; } = new();

    /// <summary>Gets the dedupe key.</summary>
    public string Key => $"{ItemId:N}|{MediaSourceId ?? string.Empty}";
}
