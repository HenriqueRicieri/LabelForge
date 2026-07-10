using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace LabelForge.Core.Printing;

/// <summary>
/// Sends raw ZPL through the Windows spooler with the RAW datatype (the path for
/// USB-connected Zebra printers) and enumerates installed printer queues.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsRawPrinter
{
    public static IReadOnlyList<string> GetInstalledPrinters()
    {
        const uint flags = 2 | 4; // PRINTER_ENUM_LOCAL | PRINTER_ENUM_CONNECTIONS

        EnumPrintersW(flags, null, 4, IntPtr.Zero, 0, out uint needed, out _);
        if (needed == 0)
        {
            return [];
        }

        IntPtr buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!EnumPrintersW(flags, null, 4, buffer, needed, out _, out uint count))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var result = new List<string>((int)count);
            int size = Marshal.SizeOf<PrinterInfo4>();
            for (int i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<PrinterInfo4>(buffer + i * size);
                if (info.PrinterName is { Length: > 0 } name)
                {
                    result.Add(name);
                }
            }

            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static void Send(string printerName, string zpl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);
        ArgumentNullException.ThrowIfNull(zpl);

        if (!OpenPrinterW(printerName, out IntPtr printer, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not open printer '{printerName}'");
        }

        try
        {
            var doc = new DocInfo1 { DocName = "LabelForge label", OutputFile = null, Datatype = "RAW" };
            if (StartDocPrinterW(printer, 1, ref doc) == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                if (!StartPagePrinter(printer))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                byte[] payload = Encoding.UTF8.GetBytes(zpl);
                if (!WritePrinter(printer, payload, (uint)payload.Length, out uint written) ||
                    written != payload.Length)
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(), "The spooler did not accept the whole document");
                }

                EndPagePrinter(printer);
            }
            finally
            {
                EndDocPrinter(printer);
            }
        }
        finally
        {
            ClosePrinter(printer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PrinterInfo4
    {
        public string? PrinterName;
        public string? ServerName;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DocInfo1
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string DocName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? OutputFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string Datatype;
    }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumPrintersW(
        uint flags, string? name, uint level, IntPtr printers, uint bufferSize,
        out uint bytesNeeded, out uint returned);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinterW(string printerName, out IntPtr printer, IntPtr defaults);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr printer);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint StartDocPrinterW(IntPtr printer, uint level, ref DocInfo1 docInfo);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr printer);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr printer);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr printer);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr printer, byte[] data, uint length, out uint written);
}
