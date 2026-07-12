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

    /// <summary>Bound on establishing the connection, so a wrong address fails fast
    /// with a clear message instead of waiting out the operating system's own connect
    /// timeout (which can be tens of seconds). Only the connection is bounded: once the
    /// printer is reached, a large label (embedded graphics) on a slow link must not be
    /// cut off mid-transfer.</summary>
    public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(8);

    public static async Task SendAsync(
        string host, int port, string zpl,
        CancellationToken cancellationToken = default, TimeSpan? connectTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentNullException.ThrowIfNull(zpl);
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        TimeSpan timeout = connectTimeout ?? DefaultConnectTimeout;

        using var client = new TcpClient();
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectCts.CancelAfter(timeout);
        try
        {
            await client.ConnectAsync(host, port, connectCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Could not reach {host}:{port} within {timeout.TotalSeconds:0} seconds.");
        }

        // The write runs under the caller's token only (see DefaultConnectTimeout).
        await using NetworkStream stream = client.GetStream();
        byte[] payload = Encoding.UTF8.GetBytes(zpl);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
