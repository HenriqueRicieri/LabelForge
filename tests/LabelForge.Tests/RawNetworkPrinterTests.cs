using System.Net;
using System.Net.Sockets;
using System.Text;
using LabelForge.Core.Printing;

namespace LabelForge.Tests;

public sealed class RawNetworkPrinterTests
{
    [Fact]
    public async Task SendAsync_DeliversUtf8ZplBytes_ToTheSocket()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        const string zpl = "^XA^CI28^FO10,10^A0N,30^FDAcentuação ##VAR##^FS^XZ";

        Task<byte[]> receive = Task.Run(async () =>
        {
            using TcpClient client = await listener.AcceptTcpClientAsync();
            await using NetworkStream stream = client.GetStream();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            return buffer.ToArray();
        });

        await RawNetworkPrinter.SendAsync("127.0.0.1", port, zpl);
        byte[] received = await receive.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(Encoding.UTF8.GetBytes(zpl), received);
    }

    [Fact]
    public async Task SendAsync_RejectsInvalidArguments()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => RawNetworkPrinter.SendAsync(" ", 9100, "^XA^XZ"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => RawNetworkPrinter.SendAsync("h", 0, "^XA^XZ"));
    }

    [Fact]
    public void SnapshotHistory_Clear_EmptiesEverything()
    {
        var history = new LabelForge.Core.Editing.SnapshotHistory();
        history.Record("a");
        history.Record("b");

        history.Clear();

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Null(history.Undo());
    }
}
