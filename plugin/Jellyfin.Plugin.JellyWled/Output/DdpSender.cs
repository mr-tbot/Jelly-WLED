using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JellyWled.Output;

/// <summary>
/// Sends DDP frames to one WLED controller over UDP; resolves the host lazily and again after failures.
/// </summary>
public sealed class DdpSender : IDisposable
{
    private readonly string _host;
    private readonly int _port;
    private UdpClient? _client;
    private IPEndPoint? _endpoint;
    private byte _sequence;
    private DateTime _lastResolve = DateTime.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="DdpSender"/> class.
    /// </summary>
    /// <param name="host">Host name or IP.</param>
    /// <param name="port">UDP port (4048).</param>
    public DdpSender(string host, int port)
    {
        _host = host;
        _port = port <= 0 ? 4048 : port;
    }

    /// <summary>Gets the last error, if any.</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Sends one frame.
    /// </summary>
    /// <param name="rgb">The frame bytes.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>True when every packet was handed to the socket.</returns>
    public async Task<bool> SendAsync(ReadOnlyMemory<byte> rgb, CancellationToken cancellationToken)
    {
        try
        {
            if (_endpoint is null || DateTime.UtcNow - _lastResolve > TimeSpan.FromMinutes(5))
            {
                await ResolveAsync(cancellationToken).ConfigureAwait(false);
            }

            _client ??= new UdpClient(_endpoint!.AddressFamily);
            var packets = DdpPacketizer.Build(rgb.Span, _sequence);
            _sequence = (byte)((_sequence + packets.Count) & 0x0F);
            foreach (var packet in packets)
            {
                await _client.SendAsync(packet, _endpoint!, cancellationToken).ConfigureAwait(false);
            }

            LastError = null;
            return true;
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException or InvalidOperationException)
        {
            LastError = ex.Message;
            _client?.Dispose();
            _client = null;
            _endpoint = null;
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _client?.Dispose();
        _client = null;
    }

    private async Task ResolveAsync(CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(_host, out var ip))
        {
            _endpoint = new IPEndPoint(ip, _port);
        }
        else
        {
            var addresses = await Dns.GetHostAddressesAsync(_host, cancellationToken).ConfigureAwait(false);
            if (addresses.Length == 0)
            {
                throw new SocketException((int)SocketError.HostNotFound);
            }

            var preferred = Array.Find(addresses, a => a.AddressFamily == AddressFamily.InterNetwork) ?? addresses[0];
            _endpoint = new IPEndPoint(preferred, _port);
        }

        _lastResolve = DateTime.UtcNow;
        if (_client is not null && _client.Client.AddressFamily != _endpoint.AddressFamily)
        {
            _client.Dispose();
            _client = null;
        }
    }
}
