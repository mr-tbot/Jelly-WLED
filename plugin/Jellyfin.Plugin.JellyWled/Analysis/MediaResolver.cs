using System;
using System.IO;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.MediaInfo;

namespace Jellyfin.Plugin.JellyWled.Analysis;

/// <summary>
/// Finds the local file behind an item and media source id.
/// </summary>
public sealed class MediaResolver
{
    private readonly ILibraryManager _libraryManager;
    private readonly IMediaSourceManager _mediaSourceManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaResolver"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="mediaSourceManager">Media source manager.</param>
    public MediaResolver(ILibraryManager libraryManager, IMediaSourceManager mediaSourceManager)
    {
        _libraryManager = libraryManager;
        _mediaSourceManager = mediaSourceManager;
    }

    /// <summary>
    /// Resolves an item to a local file.
    /// </summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="mediaSourceId">Preferred media source id, or null.</param>
    /// <param name="user">The requesting user, to hide items and alternate versions they may not see; null for the server itself.</param>
    /// <returns>The resolved source, or null when the item is missing or not a local file.</returns>
    public ResolvedSource? Resolve(Guid itemId, string? mediaSourceId, Jellyfin.Database.Implementations.Entities.User? user = null)
    {
        var item = user is null ? _libraryManager.GetItemById<BaseItem>(itemId) : _libraryManager.GetItemById<BaseItem>(itemId, user);
        if (item is not Video)
        {
            return null;
        }

        mediaSourceId = Tracks.TrackStore.NormalizeMediaSourceId(mediaSourceId);
        var sources = _mediaSourceManager.GetStaticMediaSources(item, false, user);
        MediaSourceInfo? source = null;
        foreach (var s in sources)
        {
            if (!string.IsNullOrEmpty(mediaSourceId) && string.Equals(s.Id, mediaSourceId, StringComparison.OrdinalIgnoreCase))
            {
                source = s;
                break;
            }

            if (source is null && s.Protocol == MediaProtocol.File)
            {
                source = s;
            }
        }

        if (source is null && sources.Count > 0)
        {
            source = sources[0];
        }

        if (source is null || source.Protocol != MediaProtocol.File || string.IsNullOrEmpty(source.Path) || !File.Exists(source.Path))
        {
            return null;
        }

        var id = Tracks.TrackStore.NormalizeMediaSourceId(source.Id) ?? itemId.ToString("N", System.Globalization.CultureInfo.InvariantCulture);
        return new ResolvedSource(itemId, id, source.Path, source.RunTimeTicks ?? item.RunTimeTicks ?? 0, item.Name ?? itemId.ToString());
    }
}
