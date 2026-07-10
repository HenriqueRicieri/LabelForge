using System.Net.Sockets;
using System.Text;

namespace LabelForge.Core.Printing;

/// <summary>
/// Sends raw ZPL to a network printer over TCP port 9100 (the standard raw printing
/// port on Zebra printers). The stream is UTF-8 encoded end to end, matching the
/// ^CI28 header our generator always emits.
/// </summary>
public static class RawNetworkPrinter
{
    public const int DefaultPort = 9100;

    public static async Task SendAsync(string host, int port, string zpl, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentNullException.ThrowIfNull(zpl);
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        using var client = new TcpClient();
        await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);

        await using NetworkStream stream = client.GetStream();
        byte[] payload = Encoding.UTF8.GetBytes(zpl);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
