namespace Jellyfin.Plugin.JellyWled.Api;

/// <summary>
/// One SSE event.
/// </summary>
/// <param name="Name">Event name.</param>
/// <param name="Json">JSON data.</param>
public sealed record SseEvent(string Name, string Json);
