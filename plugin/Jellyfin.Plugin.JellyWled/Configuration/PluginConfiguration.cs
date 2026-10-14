using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyWled.Configuration;

/// <summary>
/// Plugin configuration. Every property here is editable on the dashboard page;
/// the XML file under plugins/configurations mirrors it one to one.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    // ---- Analysis (glow tracks) ----

    /// <summary>
    /// Gets or sets how many ring frames per second the analyzer stores. Changing it invalidates existing tracks.
    /// </summary>
    public int AnalysisFps { get; set; } = 10;

    /// <summary>
    /// Gets or sets the width of the analysis thumbnail; the top and bottom ring segments have this many zones.
    /// </summary>
    public int RingWidth { get; set; } = 32;

    /// <summary>
    /// Gets or sets the height of the analysis thumbnail; the left and right ring segments have this many zones.
    /// </summary>
    public int RingHeight { get; set; } = 18;

    /// <summary>
    /// Gets or sets how deep into the picture each edge is sampled, as a percentage of the picture height.
    /// </summary>
    public int BandPercent { get; set; } = 8;

    /// <summary>
    /// Gets or sets a value indicating whether black letterbox and pillarbox bars are detected and cropped before analysis.
    /// </summary>
    public bool DetectLetterbox { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether an item without a track is analyzed when someone starts playing it.
    /// </summary>
    public bool AnalyzeOnPlayback { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether items are queued for analysis when they are added to the library.
    /// </summary>
    public bool AnalyzeNewItems { get; set; }

    /// <summary>
    /// Gets or sets the number of ffmpeg decode threads per analysis.
    /// </summary>
    public int AnalysisThreads { get; set; } = 2;

    /// <summary>
    /// Gets or sets a value indicating whether ffmpeg runs at the lowest CPU and I/O priority (nice / ionice on Linux).
    /// </summary>
    public bool LowPriority { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the decoder skips B-frames for speed (slightly less accurate colors).
    /// </summary>
    public bool FastDecode { get; set; }

    /// <summary>
    /// Gets or sets how many analyses may run at the same time.
    /// </summary>
    public int MaxConcurrentAnalyses { get; set; } = 1;

    /// <summary>
    /// Gets or sets the track cache directory. Empty means the plugin data directory (data/jellywled/tracks).
    /// </summary>
    public string CacheDirectory { get; set; } = string.Empty;

    // ---- Scheduled task "Analyze library for Jelly-WLED" ----

    /// <summary>
    /// Gets or sets a value indicating whether the scheduled task analyzes movies.
    /// </summary>
    public bool TaskIncludeMovies { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the scheduled task analyzes episodes.
    /// </summary>
    public bool TaskIncludeEpisodes { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of items one task run analyzes (0 = unlimited).
    /// </summary>
    public int TaskMaxItemsPerRun { get; set; } = 50;

    /// <summary>
    /// Gets or sets a value indicating whether the task works through the most recently added items first.
    /// </summary>
    public bool TaskNewestFirst { get; set; } = true;

    /// <summary>
    /// Gets or sets the library (virtual folder) ids the task is limited to. Empty means every library.
    /// </summary>
    public string[] TaskLibraryIds { get; set; } = [];

    // ---- Output ----

    /// <summary>
    /// Gets or sets a value indicating whether the server streams to its configured devices at all
    /// (off = the plugin only serves tracks and events to bridge apps).
    /// </summary>
    public bool EnableServerOutput { get; set; } = true;

    /// <summary>
    /// Gets or sets the WLED devices driven directly by the server.
    /// </summary>
    public WledDevice[] Devices { get; set; } = [];
}
