using System;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyWled.Output;

/// <summary>
/// The small part of WLED's JSON API the plugin uses: read info and state, post state changes.
/// </summary>
public sealed class WledHttpClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };
    private readonly ILogger<WledHttpClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WledHttpClient"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public WledHttpClient(ILogger<WledHttpClient> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Builds the base URL of a device.
    /// </summary>
    /// <param name="host">Host name or IP.</param>
    /// <param name="httpPort">HTTP port.</param>
    /// <returns>The URL without a trailing slash.</returns>
    public static string BaseUrl(string host, int httpPort)
    {
        var h = (host ?? string.Empty).Trim();
        if (h.Contains(':', StringComparison.Ordinal) && !h.StartsWith('['))
        {
            h = "[" + h + "]"; // IPv6 literal
        }

        return httpPort is 80 or <= 0
            ? "http://" + h
            : string.Create(CultureInfo.InvariantCulture, $"http://{h}:{httpPort}");
    }

    /// <summary>
    /// Fetches /json/info.
    /// </summary>
    /// <param name="host">Host.</param>
    /// <param name="httpPort">HTTP port.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The raw JSON.</returns>
    public async Task<JsonDocument> GetInfoAsync(string host, int httpPort, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(new Uri(BaseUrl(host, httpPort) + "/json/info"), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonDocument.Parse(text);
    }

    /// <summary>
    /// Fetches /json/state and returns the on flag and brightness.
    /// </summary>
    /// <param name="host">Host.</param>
    /// <param name="httpPort">HTTP port.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>On and brightness, or null on failure.</returns>
    public async Task<(bool On, int Bri)?> GetStateAsync(string host, int httpPort, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(new Uri(BaseUrl(host, httpPort) + "/json/state"), cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(text);
            var on = doc.RootElement.TryGetProperty("on", out var onEl) && onEl.ValueKind == JsonValueKind.True;
            var bri = doc.RootElement.TryGetProperty("bri", out var briEl) && briEl.TryGetInt32(out var b) ? b : 128;
            return (on, bri);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogDebug(ex, "[JellyWled] WLED state read failed for {Host}", host);
            return null;
        }
    }

    /// <summary>
    /// Posts a state change, for example {"live":false}.
    /// </summary>
    /// <param name="host">Host.</param>
    /// <param name="httpPort">HTTP port.</param>
    /// <param name="json">The JSON body.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>True on success.</returns>
    public async Task<bool> PostStateAsync(string host, int httpPort, string json, CancellationToken cancellationToken)
    {
        try
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(new Uri(BaseUrl(host, httpPort) + "/json/state"), content, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogDebug(ex, "[JellyWled] WLED state post failed for {Host}", host);
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();
}
