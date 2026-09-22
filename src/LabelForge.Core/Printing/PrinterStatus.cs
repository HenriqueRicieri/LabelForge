using System.Globalization;

namespace LabelForge.Core.Printing;

[Flags]
public enum PrinterFaults
{
    None = 0,
    PaperOut = 1,
    Paused = 2,
    HeadOpen = 4,
    RibbonOut = 8,
    BufferFull = 16,
    DiagnosticMode = 32,
    PartialFormat = 64,
    CorruptMemory = 128,
    UnderTemperature = 256,
    OverTemperature = 512
}

public sealed record PrinterStatus(PrinterFaults Faults, int FormatsQueued, int LabelsRemaining, bool LabelWaiting)
{
    public bool IsReady => Faults == PrinterFaults.None;

    /// <summary>Parses the three STX/ETX records returned by Zebra's ~HS command.</summary>
    public static PrinterStatus Parse(string reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        string[][] records = new string[3][];
        ReadOnlySpan<char> remaining = reply.AsSpan();
        for (int i = 0; i < records.Length; i++)
        {
            remaining = remaining.TrimStart("\r\n");
            int end = remaining.IndexOf('\x03');
            if (remaining.Length == 0 || remaining[0] != '\x02' || end < 1)
            {
                throw InvalidReply();
            }
            records[i] = remaining[1..end].ToString().Split(',');
            remaining = remaining[(end + 1)..];
        }
        if (!remaining.Trim("\r\n").IsEmpty || records[0].Length != 12 ||
            records[1].Length != 11 || records[2].Length != 2)
        {
            throw InvalidReply();
        }

        string[] first = records[0];
        string[] second = records[1];
        // Print mode can be a letter (for example K for kiosk), not just a digit.
        for (int row = 0; row < records.Length; row++)
        {
            for (int field = 0; field < records[row].Length; field++)
            {
                if (row == 1 && field == 5)
                {
                    if (records[row][field].Length != 1 || !"0123456789KSA".Contains(records[row][field][0]))
                    {
                        throw InvalidReply();
                    }
                }
                else
                {
                    _ = Number(records[row][field]);
                }
            }
        }

        PrinterFaults faults = PrinterFaults.None;
        Add(first[1], PrinterFaults.PaperOut);
        Add(first[2], PrinterFaults.Paused);
        Add(first[5], PrinterFaults.BufferFull);
        Add(first[6], PrinterFaults.DiagnosticMode);
        Add(first[7], PrinterFaults.PartialFormat);
        Add(first[9], PrinterFaults.CorruptMemory);
        Add(first[10], PrinterFaults.UnderTemperature);
        Add(first[11], PrinterFaults.OverTemperature);
        Add(second[2], PrinterFaults.HeadOpen);
        bool ribbonOut = Flag(second[3]);
        bool thermalTransfer = Flag(second[4]);
        if (ribbonOut && thermalTransfer)
        {
            faults |= PrinterFaults.RibbonOut;
        }
        return new PrinterStatus(faults, Number(first[4]), Number(second[8]), Flag(second[7]));

        void Add(string field, PrinterFaults fault)
        {
            if (Flag(field))
            {
                faults |= fault;
            }
        }
    }

    private static int Number(string field) =>
        int.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
            ? value : throw InvalidReply();

    private static bool Flag(string field) => field switch
    {
        "0" => false,
        "1" => true,
        _ => throw InvalidReply()
    };

    private static InvalidDataException InvalidReply() => new("The printer returned an invalid ~HS status response.");
}

public enum PrintDelivery
{
    NotSent,
    Sent,
    Uncertain
}

/// <summary>Sent means the TCP write completed; it does not confirm physical printing.</summary>
public sealed record NetworkPrintResult(
    PrintDelivery Delivery, PrinterStatus? Before = null, PrinterStatus? After = null, string? Error = null);
