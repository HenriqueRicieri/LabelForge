using System.Net.Sockets;
using System.Text;
using LabelForge.Core.Io;

namespace LabelForge.Core.Printing;

public static class RawNetworkPrinter
{
    public const int DefaultPort = 9100;
    public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(8);
    public static readonly TimeSpan DefaultStatusTimeout = TimeSpan.FromSeconds(3);
    private const int MaximumStatusBytes = 4096;

    public static async Task SendAsync(
        string host, int port, string zpl,
        CancellationToken cancellationToken = default, TimeSpan? connectTimeout = null)
    {
        Validate(host, port);
        ArgumentNullException.ThrowIfNull(zpl);
        using TcpClient client = await ConnectAsync(host, port, cancellationToken, connectTimeout).ConfigureAwait(false);
        await WriteJobAsync(client.GetStream(), zpl, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<PrinterStatus> QueryStatusAsync(
        string host, int port, CancellationToken cancellationToken = default,
        TimeSpan? connectTimeout = null, TimeSpan? statusTimeout = null)
    {
        Validate(host, port);
        using TcpClient client = await ConnectAsync(host, port, cancellationToken, connectTimeout).ConfigureAwait(false);
        return await ReadStatusAsync(client.GetStream(), cancellationToken, statusTimeout).ConfigureAwait(false);
    }

    public static async Task<NetworkPrintResult> SendWithStatusAsync(
        string host, int port, string zpl, CancellationToken cancellationToken = default,
        TimeSpan? connectTimeout = null, TimeSpan? statusTimeout = null)
    {
        Validate(host, port);
        ArgumentNullException.ThrowIfNull(zpl);
        TcpClient client;
        try
        {
            client = await ConnectAsync(host, port, cancellationToken, connectTimeout).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsNetworkError(ex))
        {
            return new(PrintDelivery.NotSent, Error: ex.Message);
        }
        using (client)
        {
            NetworkStream stream = client.GetStream();
            PrinterStatus before;
            try
            {
                before = await ReadStatusAsync(stream, cancellationToken, statusTimeout).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsNetworkError(ex))
            {
                return new(PrintDelivery.NotSent, Error: ex.Message);
            }
            if (!before.IsReady)
            {
                return new(PrintDelivery.NotSent, Before: before);
            }

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await WriteJobAsync(stream, zpl, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsNetworkError(ex) || ex is OperationCanceledException)
            {
                // A failed write may have delivered part or all of the label. Never retry it here.
                return new(PrintDelivery.Uncertain, Before: before, Error: ex.Message);
            }
            try
            {
                PrinterStatus after = await ReadStatusAsync(stream, cancellationToken, statusTimeout).ConfigureAwait(false);
                return new(PrintDelivery.Sent, before, after);
            }
            catch (Exception ex) when (IsNetworkError(ex) || ex is OperationCanceledException)
            {
                return new(PrintDelivery.Sent, Before: before, Error: ex.Message);
            }
        }
    }

    private static async Task<TcpClient> ConnectAsync(
        string host, int port, CancellationToken cancellationToken, TimeSpan? connectTimeout)
    {
        TimeSpan timeout = connectTimeout ?? DefaultConnectTimeout;
        var client = new TcpClient();
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectCts.CancelAfter(timeout);
        try
        {
            await client.ConnectAsync(host, port, connectCts.Token).ConfigureAwait(false);
            return client;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            client.Dispose();
            throw new TimeoutException($"Could not reach {host}:{port} within {timeout.TotalSeconds:0} seconds.");
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task WriteJobAsync(NetworkStream stream, string zpl, CancellationToken cancellationToken)
    {
        // Only connection/status queries have deadlines: large jobs must not time out mid-transfer.
        await stream.WriteAsync(ZplTextFile.ToBytes(zpl), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<PrinterStatus> ReadStatusAsync(
        NetworkStream stream, CancellationToken cancellationToken, TimeSpan? statusTimeout)
    {
        using var statusCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        statusCts.CancelAfter(statusTimeout ?? DefaultStatusTimeout);
        try
        {
            await stream.WriteAsync("~HS"u8.ToArray(), statusCts.Token).ConfigureAwait(false);
            byte[] reply = new byte[MaximumStatusBytes];
            int length = 0;
            int records = 0;
            while (length < reply.Length)
            {
                int read = await stream.ReadAsync(reply.AsMemory(length), statusCts.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new IOException("The printer closed the connection before returning a complete status response.");
                }
                for (int i = length; i < length + read; i++)
                {
                    if (reply[i] == 3)
                    {
                        records++;
                    }
                }
                length += read;
                if (records >= 3)
                {
                    return PrinterStatus.Parse(Encoding.ASCII.GetString(reply, 0, length));
                }
            }
            throw new InvalidDataException("The printer status response exceeded 4096 bytes.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("No complete printer status response arrived in time. The printer may be offline, in an error state, or may not support ~HS.");
        }
    }

    private static bool IsNetworkError(Exception ex) => ex is IOException or InvalidDataException or SocketException or TimeoutException;

    private static void Validate(string host, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }
    }
}
