using System.Net;
using System.Net.Sockets;
using System.Text;
using LabelForge.Core.Printing;

namespace LabelForge.Tests;

[Collection("Printer status")]
public sealed class PrinterStatusTests
{
    private const string Ready = "\x02" + "000,0,0,0800,000,0,0,0,000,0,0,0\x03\r\n"
        + "\x02" + "000,0,0,0,0,2,4,0,00000000,1,000\x03\r\n\x02" + "0000,0\x03\r\n";
    private const string Job = "^XA^CI28^FO10,10^A0N,30^FDAcentuação^FS^XZ";

    [Fact]
    public void Parse_ReadsQueueCountsAndPeelState_WithoutClaimingCompletion()
    {
        string reply = Field(Field(Field(Ready, 0, 4, "003"), 1, 8, "00000012"), 1, 7, "1");
        PrinterStatus status = PrinterStatus.Parse(reply);
        Assert.True(status.IsReady);
        Assert.Equal(3, status.FormatsQueued);
        Assert.Equal(12, status.LabelsRemaining);
        Assert.True(status.LabelWaiting);
    }

    [Theory]
    [InlineData(0, 1, PrinterFaults.PaperOut)]
    [InlineData(0, 2, PrinterFaults.Paused)]
    [InlineData(0, 5, PrinterFaults.BufferFull)]
    [InlineData(0, 6, PrinterFaults.DiagnosticMode)]
    [InlineData(0, 7, PrinterFaults.PartialFormat)]
    [InlineData(0, 9, PrinterFaults.CorruptMemory)]
    [InlineData(0, 10, PrinterFaults.UnderTemperature)]
    [InlineData(0, 11, PrinterFaults.OverTemperature)]
    [InlineData(1, 2, PrinterFaults.HeadOpen)]
    public void Parse_IdentifiesEachBlockingFault(int row, int column, PrinterFaults expected)
    {
        PrinterStatus status = PrinterStatus.Parse(Field(Ready, row, column, "1"));
        Assert.False(status.IsReady);
        Assert.Equal(expected, status.Faults);
    }

    [Fact]
    public void Parse_ReportsSimultaneousFaults_AndRibbonOnlyForThermalTransfer()
    {
        string reply = Field(Field(Field(Ready, 0, 1, "1"), 0, 2, "1"), 1, 3, "1");
        Assert.Equal(PrinterFaults.PaperOut | PrinterFaults.Paused, PrinterStatus.Parse(reply).Faults);
        Assert.Equal(PrinterFaults.PaperOut | PrinterFaults.Paused | PrinterFaults.RibbonOut,
            PrinterStatus.Parse(Field(reply, 1, 4, "1")).Faults);
    }

    [Theory]
    [InlineData("K")]
    [InlineData("S")]
    [InlineData("A")]
    public void Parse_AcceptsDocumentedLetterPrintModes(string mode) =>
        Assert.True(PrinterStatus.Parse(Field(Ready, 1, 5, mode)).IsReady);

    [Theory]
    [InlineData(0, 1, "2")]
    [InlineData(0, 2, "")]
    [InlineData(0, 4, "-1")]
    [InlineData(0, 4, "99999999999999")]
    [InlineData(1, 3, "garbage")]
    [InlineData(1, 4, "9")]
    [InlineData(1, 7, "2")]
    [InlineData(1, 8, " 1")]
    [InlineData(1, 5, "?")]
    public void Parse_RejectsMalformedFields(int row, int column, string value) =>
        Assert.Throws<InvalidDataException>(() => PrinterStatus.Parse(Field(Ready, row, column, value)));

    [Fact]
    public void Parse_RequiresThreeCompleteFramedRecords()
    {
        Assert.Throws<InvalidDataException>(() => PrinterStatus.Parse(Ready.Replace("\x02", "")));
        Assert.Throws<InvalidDataException>(() => PrinterStatus.Parse(Ready[..Ready.LastIndexOf('\x02')]));
        Assert.Throws<InvalidDataException>(() => PrinterStatus.Parse(Ready + "unexpected"));
        Assert.Throws<InvalidDataException>(() => PrinterStatus.Parse(Ready.Replace("0000,0", "0000,0,0")));
        Assert.True(PrinterStatus.Parse("\r\n" + Ready).IsReady);
    }

    [Fact]
    public async Task QueryStatus_ReadsFragmentedRecords_WithoutWaitingForConnectionClose()
    {
        await WithPrinter(async stream =>
        {
            Assert.Equal("~HS", await ReadText(stream, 3));
            byte[] reply = Encoding.ASCII.GetBytes(Ready.TrimEnd('\r', '\n'));
            for (int offset = 0; offset < reply.Length; offset += 7)
            {
                await stream.WriteAsync(reply.AsMemory(offset, Math.Min(7, reply.Length - offset)));
                await Task.Delay(10);
            }
            Assert.Equal(0, await stream.ReadAsync(new byte[1]));
        }, async port =>
        {
            Assert.True((await RawNetworkPrinter.QueryStatusAsync("127.0.0.1", port)).IsReady);
        });
    }

    [Fact]
    public async Task CheckedSend_UsesOneConnection_AndPreservesUtf8JobBytesBetweenQueries()
    {
        string after = Field(Field(Ready, 0, 4, "002"), 1, 8, "00000005");
        await WithPrinter(async stream =>
        {
            Assert.Equal("~HS", await ReadText(stream, 3));
            // Leave the first response's final CR/LF for the next status read.
            await stream.WriteAsync(Encoding.ASCII.GetBytes(Ready.TrimEnd('\r', '\n')));
            Assert.Equal(Job, await ReadText(stream, Encoding.UTF8.GetByteCount(Job)));
            Assert.Equal("~HS", await ReadText(stream, 3));
            await stream.WriteAsync(Encoding.ASCII.GetBytes("\r\n" + after));
            Assert.Equal(0, await stream.ReadAsync(new byte[1]));
        }, async port =>
        {
            NetworkPrintResult result = await RawNetworkPrinter.SendWithStatusAsync("127.0.0.1", port, Job);
            Assert.Equal(PrintDelivery.Sent, result.Delivery);
            Assert.True(result.Before!.IsReady);
            Assert.Equal(2, result.After!.FormatsQueued);
            Assert.Equal(5, result.After.LabelsRemaining);
            Assert.Null(result.Error);
        });
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    public async Task CheckedSend_DoesNotWriteJob_WhenPrinterReportsFault(int row, int column)
    {
        await WithPrinter(async stream =>
        {
            Assert.Equal("~HS", await ReadText(stream, 3));
            await stream.WriteAsync(Encoding.ASCII.GetBytes(Field(Ready, row, column, "1")));
            Assert.Equal(0, await stream.ReadAsync(new byte[1]));
        }, async port =>
        {
            NetworkPrintResult result = await RawNetworkPrinter.SendWithStatusAsync("127.0.0.1", port, Job);
            Assert.Equal(PrintDelivery.NotSent, result.Delivery);
            Assert.False(result.Before!.IsReady);
            Assert.Null(result.After);
        });
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("closed")]
    [InlineData("oversized")]
    [InlineData("silent")]
    public async Task CheckedSend_DoesNotWriteJob_WhenStatusCannotBeRead(string failure)
    {
        await WithPrinter(async stream =>
        {
            Assert.Equal("~HS", await ReadText(stream, 3));
            if (failure == "closed")
            {
                return;
            }
            if (failure == "malformed")
            {
                await stream.WriteAsync(Encoding.ASCII.GetBytes(Field(Ready, 0, 1, "?")));
            }
            if (failure == "oversized")
            {
                await stream.WriteAsync(new byte[4096]);
            }
            Assert.Equal(0, await stream.ReadAsync(new byte[1]));
        }, async port =>
        {
            NetworkPrintResult result = await RawNetworkPrinter.SendWithStatusAsync("127.0.0.1", port, Job,
                statusTimeout: TimeSpan.FromMilliseconds(250));
            Assert.Equal(PrintDelivery.NotSent, result.Delivery);
            Assert.Null(result.Before);
            Assert.NotEmpty(result.Error!);
        });
    }

    [Theory]
    [InlineData("closed")]
    [InlineData("silent")]
    [InlineData("fault")]
    public async Task CheckedSend_StillReportsSent_WhenPostStatusFailsOrReportsAFault(string response)
    {
        await WithPrinter(async stream =>
        {
            Assert.Equal("~HS", await ReadText(stream, 3));
            await stream.WriteAsync(Encoding.ASCII.GetBytes(Ready));
            Assert.Equal(Job, await ReadText(stream, Encoding.UTF8.GetByteCount(Job)));
            Assert.Equal("~HS", await ReadText(stream, 3));
            if (response == "fault")
            {
                await stream.WriteAsync(Encoding.ASCII.GetBytes(Field(Ready, 1, 2, "1")));
            }
            else if (response == "silent")
            {
                Assert.Equal(0, await stream.ReadAsync(new byte[1]));
            }
        }, async port =>
        {
            NetworkPrintResult result = await RawNetworkPrinter.SendWithStatusAsync("127.0.0.1", port, Job, statusTimeout: TimeSpan.FromMilliseconds(250));
            Assert.Equal(PrintDelivery.Sent, result.Delivery);
            if (response == "fault")
            {
                Assert.Equal(PrinterFaults.HeadOpen, result.After!.Faults);
            }
            else
            {
                Assert.Null(result.After);
                Assert.NotNull(result.Error);
            }
        });
    }

    [Fact]
    public async Task QueryStatus_PropagatesCallerCancellation()
    {
        using var cancelled = new CancellationTokenSource();
        await WithPrinter(async stream =>
        {
            Assert.Equal("~HS", await ReadText(stream, 3));
            await cancelled.CancelAsync();
            Assert.Equal(0, await stream.ReadAsync(new byte[1]));
        }, async port =>
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                RawNetworkPrinter.QueryStatusAsync("127.0.0.1", port, cancelled.Token));
        });
    }

    [Fact]
    public async Task CheckedSend_CancellationAfterWritingDoesNotClaimNothingWasSent()
    {
        using var cancelled = new CancellationTokenSource();
        await WithPrinter(async stream =>
        {
            Assert.Equal("~HS", await ReadText(stream, 3));
            await stream.WriteAsync(Encoding.ASCII.GetBytes(Ready));
            Assert.Equal(Job, await ReadText(stream, Encoding.UTF8.GetByteCount(Job)));
            Assert.Equal("~HS", await ReadText(stream, 3));
            await cancelled.CancelAsync();
            Assert.Equal(0, await stream.ReadAsync(new byte[1]));
        }, async port =>
        {
            NetworkPrintResult result = await RawNetworkPrinter.SendWithStatusAsync("127.0.0.1", port, Job, cancelled.Token);
            Assert.Equal(PrintDelivery.Sent, result.Delivery);
            Assert.Null(result.After);
            Assert.NotNull(result.Error);
        });
    }

    [Fact]
    public async Task CheckedSend_ConnectionResetDuringSendDoesNotClaimNothingWasSent()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Server.ReceiveBufferSize = 1024;
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Task server = Task.Run(async () =>
        {
            using TcpClient client = await listener.AcceptTcpClientAsync();
            client.ReceiveBufferSize = 1024;
            using NetworkStream stream = client.GetStream();
            Assert.Equal("~HS", await ReadText(stream, 3));
            await stream.WriteAsync(Encoding.ASCII.GetBytes(Ready));
            _ = await ReadText(stream, 32);
            client.Client.LingerState = new LingerOption(true, 0);
        });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        NetworkPrintResult result = await RawNetworkPrinter.SendWithStatusAsync("127.0.0.1", port,
            new string('x', 8 * 1024 * 1024), deadline.Token);
        await server.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotEqual(PrintDelivery.NotSent, result.Delivery);
        Assert.NotNull(result.Error);
        Assert.Null(result.After);
    }

    private static async Task WithPrinter(Func<NetworkStream, Task> serve, Func<int, Task> test)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task server = Task.Run(async () =>
        {
            using TcpClient client = await listener.AcceptTcpClientAsync(deadline.Token);
            using NetworkStream stream = client.GetStream();
            using var stop = deadline.Token.Register(client.Dispose);
            await serve(stream);
        });
        await Task.WhenAll(test(port), server).WaitAsync(TimeSpan.FromSeconds(12));
    }

    private static async Task<string> ReadText(NetworkStream stream, int length)
    {
        byte[] data = new byte[length];
        await stream.ReadExactlyAsync(data);
        return Encoding.UTF8.GetString(data);
    }

    private static string Field(string reply, int row, int column, string value)
    {
        string[] records = reply.Split('\x02');
        int end = records[row + 1].IndexOf('\x03');
        string tail = records[row + 1][end..];
        string[] fields = records[row + 1][..end].Split(',');
        fields[column] = value;
        records[row + 1] = string.Join(',', fields) + tail;
        return string.Join('\x02', records);
    }
}

// Socket deadlines need an idle worker pool, separate from the parallel rendering corpus.
[CollectionDefinition("Printer status", DisableParallelization = true)]
public sealed class PrinterStatusCollection { }
