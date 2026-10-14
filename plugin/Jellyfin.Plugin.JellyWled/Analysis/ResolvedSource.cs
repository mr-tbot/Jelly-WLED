using System;

namespace Jellyfin.Plugin.JellyWled.Analysis;

/// <summary>
/// A library item resolved to a local media file.
/// </summary>
/// <param name="ItemId">Item id.</param>
/// <param name="MediaSourceId">The media source id actually used.</param>
/// <param name="Path">Local file path.</param>
/// <param name="RunTimeTicks">Runtime in ticks (0 when unknown).</param>
/// <param name="Name">Display name.</param>
public sealed record ResolvedSource(Guid ItemId, string MediaSourceId, string Path, long RunTimeTicks, string Name);
