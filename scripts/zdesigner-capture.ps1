# Captures the ZPL the ZDesigner Windows driver generates, for use as comparison
# fixtures against our own generator (backlog E4).
#
# The driver is what turns a ZebraDesigner design into ZPL; ZebraDesigner itself does
# not. So the only way to see its output is to print through it into a file.
#
# Prerequisite, and it is not scriptable: the ZDesigner driver has to be installed
# first. It is not in Windows' driver store and Add-PrinterDriver cannot fetch it, and
# staging a new driver package needs administrator rights. Adding a QUEUE for a driver
# that is already installed does not, which is why the rest of this runs unelevated.
#
# Usually there is nothing to download. Zebra Setup Utilities bundles this driver, and
# any model-specific queue ("ZDesigner ZT230-200dpi ZPL" and the like) is the same
# package with a model INF entry, so a machine that already prints to a Zebra already
# has it. Seagull Scientific builds it for Zebra under contract, which is why their
# download and Zebra's are the same driver rather than two choices.
#
# NONE OF THIS IS A RUNTIME DEPENDENCY. LabelForge generates its own ZPL and prints raw
# bytes, over TCP 9100 or through the spooler's RAW datatype, which passes the driver
# by. The driver is a development tool for producing comparison fixtures, used by
# whoever makes them and by nobody who installs the app.
#
# ZebraDesigner is not needed. The driver exposes the printer's resident fonts and
# barcodes as GDI DEVICE fonts, which is exactly what ZebraDesigner's "printer font"
# dropdown is reading, so -Print asks for them directly and gets ^A and ^B commands out.
# -ListFonts shows which ones this driver offers.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File scripts\zdesigner-capture.ps1 -Setup
#   powershell -ExecutionPolicy Bypass -File scripts\zdesigner-capture.ps1 -ListFonts
#   powershell -ExecutionPolicy Bypass -File scripts\zdesigner-capture.ps1 `
#       -Print scripts\zdesigner-designs\printer-fonts.txt
#   powershell -ExecutionPolicy Bypass -File scripts\zdesigner-capture.ps1 -Capture printer-fonts
#   powershell -ExecutionPolicy Bypass -File scripts\zdesigner-capture.ps1 -Remove
#
# A design file holds one "x,y,height,face,text" per line, all in printer dots, with
# blank lines and # comments ignored. It is the reference design, kept beside the
# fixture it produces so anyone with the driver can reproduce it. Designing in
# ZebraDesigner still works and is the route for anything this cannot express.
#
# The port is a LOCAL port whose name is a file path, not the built-in FILE: port.
# FILE: opens a "save as" dialog on every print, which makes a capture run a sequence
# of dialogs; a file-path local port writes straight to disk with nothing to click.

[CmdletBinding(DefaultParameterSetName = "Setup")]
param(
    [Parameter(ParameterSetName = "Setup")][switch]$Setup,
    [Parameter(ParameterSetName = "Setup")][string]$DriverName,
    [Parameter(ParameterSetName = "Capture", Mandatory = $true)][string]$Capture,
    [Parameter(ParameterSetName = "Remove")][switch]$Remove,
    [Parameter(ParameterSetName = "ListFonts")][switch]$ListFonts,
    [Parameter(ParameterSetName = "Print", Mandatory = $true)][string]$Print
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$workDir = Join-Path $root "artifacts\zdesigner"
$capturePath = Join-Path $workDir "capture.prn"
$fixtureDir = Join-Path $workDir "fixtures"
$queueName = "LabelForge ZDesigner Capture"

# A precondition the user has to act on is not an exception. Say it and stop, rather
# than burying one sentence under a PowerShell stack trace.
function Fail([string]$message) {
    Write-Host $message -ForegroundColor Red
    exit 1
}

function Find-ZDesignerDriver {
    # The driver has gone by several names across versions (ZDesigner, Zebra Technologies
    # ZTC, and the Seagull-built "Zebra ..." line), so match on what they share rather
    # than on one spelling.
    return @(Get-PrinterDriver | Where-Object { $_.Name -match "ZDesigner|Zebra|ZTC" })
}

# GDI, not GDI+. GDI+ draws glyphs itself and the driver never sees a font request at
# all, which is why printing text the ordinary way comes back as a bitmap. Selecting a
# font by face name through CreateFontIndirect is what reaches the driver's device fonts.
$gdi = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class Gdi
{
    public static List<string> DeviceFonts(string printer)
    {
        var found = new List<string>();
        IntPtr dc = CreateDC("WINSPOOL", printer, null, IntPtr.Zero);
        if (dc == IntPtr.Zero) return found;

        EnumFontFamiliesEx(dc, new LOGFONT(), delegate(ref ENUMLOGFONTEX lf, IntPtr tm, uint type, IntPtr p)
        {
            // Bit 1 is DEVICE, bit 2 is TRUETYPE. A driver reports every system TrueType
            // font as a device font too, meaning "I can download that one"; the printer's
            // own resident fonts are the ones that are device WITHOUT being TrueType.
            if ((type & 2) != 0 && (type & 4) == 0) found.Add(lf.elfLogFont.lfFaceName);
            return 1;
        }, IntPtr.Zero, 0);

        DeleteDC(dc);
        found.Sort();
        return found;
    }

    public static string Print(string printer, string[] items)
    {
        IntPtr dc = CreateDC("WINSPOOL", printer, null, IntPtr.Zero);
        if (dc == IntPtr.Zero) return "could not open a device context for " + printer;

        var info = new DOCINFO();
        info.cbSize = Marshal.SizeOf(typeof(DOCINFO));
        info.lpszDocName = "LabelForge fixture";
        if (StartDoc(dc, info) <= 0) { DeleteDC(dc); return "StartDoc failed"; }

        StartPage(dc);
        SetBkMode(dc, 1);
        foreach (string item in items)
        {
            string[] p = item.Split(new char[] { ',' }, 5);
            if (p.Length < 5) { EndPage(dc); EndDoc(dc); DeleteDC(dc); return "bad item: " + item; }

            var lf = new LOGFONT();
            lf.lfHeight = -int.Parse(p[2]);
            lf.lfFaceName = p[3];
            IntPtr font = CreateFontIndirect(lf);
            IntPtr old = SelectObject(dc, font);
            TextOut(dc, int.Parse(p[0]), int.Parse(p[1]), p[4], p[4].Length);
            SelectObject(dc, old);
            DeleteObject(font);
        }

        EndPage(dc);
        EndDoc(dc);
        DeleteDC(dc);
        return null;
    }

    delegate int EnumProc(ref ENUMLOGFONTEX lf, IntPtr tm, uint type, IntPtr lParam);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateDC(string d, string dev, string o, IntPtr i);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern int StartDoc(IntPtr dc, [In] DOCINFO di);
    [DllImport("gdi32.dll")] static extern int StartPage(IntPtr dc);
    [DllImport("gdi32.dll")] static extern int EndPage(IntPtr dc);
    [DllImport("gdi32.dll")] static extern int EndDoc(IntPtr dc);
    [DllImport("gdi32.dll")] static extern int SetBkMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateFontIndirect([In] LOGFONT lf);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern bool TextOut(IntPtr dc, int x, int y, string s, int len);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern int EnumFontFamiliesEx(IntPtr dc, [In] LOGFONT lf, EnumProc proc, IntPtr lParam, uint flags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    class DOCINFO
    {
        public int cbSize;
        [MarshalAs(UnmanagedType.LPTStr)] public string lpszDocName;
        [MarshalAs(UnmanagedType.LPTStr)] public string lpszOutput;
        [MarshalAs(UnmanagedType.LPTStr)] public string lpszDatatype;
        public int fwType;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    class LOGFONT
    {
        public int lfHeight, lfWidth, lfEscapement, lfOrientation, lfWeight;
        public byte lfItalic, lfUnderline, lfStrikeOut, lfCharSet, lfOutPrecision, lfClipPrecision, lfQuality, lfPitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string lfFaceName = "";
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct LOGFONTS
    {
        public int lfHeight, lfWidth, lfEscapement, lfOrientation, lfWeight;
        public byte lfItalic, lfUnderline, lfStrikeOut, lfCharSet, lfOutPrecision, lfClipPrecision, lfQuality, lfPitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string lfFaceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ENUMLOGFONTEX
    {
        public LOGFONTS elfLogFont;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string elfFullName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string elfStyle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string elfScript;
    }
}
'@

if ($PSCmdlet.ParameterSetName -eq "ListFonts") {
    Add-Type -TypeDefinition $gdi -ErrorAction Stop
    if (-not (Get-Printer -Name $queueName -ErrorAction SilentlyContinue)) {
        Fail "The capture queue does not exist yet. Run -Setup first."
    }

    $fonts = [Gdi]::DeviceFonts($queueName)
    if ($fonts.Count -eq 0) {
        Fail "This driver exposes no resident fonts, so every capture through it will be a bitmap."
    }

    Write-Host "Resident fonts this driver offers (use one as the 'face' of a -Print item):"
    $fonts | ForEach-Object { Write-Host "  $_" }
    return
}

if ($PSCmdlet.ParameterSetName -eq "Print") {
    Add-Type -TypeDefinition $gdi -ErrorAction Stop
    if (-not (Get-Printer -Name $queueName -ErrorAction SilentlyContinue)) {
        Fail "The capture queue does not exist yet. Run -Setup first."
    }

    # Items come from a file rather than the command line, for two reasons. PowerShell's
    # -File flattens an array argument into one string, so several items on one line
    # silently become one; and a design worth capturing is worth keeping, so the file IS
    # the reference design and anyone with the driver can reproduce the fixture from it.
    if (-not (Test-Path $Print)) {
        Fail "No such design file: $Print. It holds one 'x,y,height,face,text' per line."
    }

    $items = @(Get-Content $Print |
        Where-Object { $_.Trim().Length -gt 0 -and -not $_.TrimStart().StartsWith("#") })
    if ($items.Count -eq 0) { Fail "$Print holds no items." }

    Remove-Item $capturePath -ErrorAction SilentlyContinue
    $problem = [Gdi]::Print($queueName, $items)
    if ($problem) { Fail $problem }

    # The spooler writes the port's file after the job leaves; without the wait the very
    # next -Capture would find nothing and say so misleadingly.
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline -and -not (Test-Path $capturePath)) {
        Start-Sleep -Milliseconds 300
    }

    if (-not (Test-Path $capturePath)) { Fail "The job was sent but nothing reached $capturePath." }
    Start-Sleep -Milliseconds 700
    Write-Host "Printed $($items.Count) item(s); $((Get-Item $capturePath).Length) bytes captured."
    Write-Host "Next: scripts\zdesigner-capture.ps1 -Capture <fixture-name>"
    return
}

if ($PSCmdlet.ParameterSetName -eq "Remove") {
    Get-Printer -Name $queueName -ErrorAction SilentlyContinue | ForEach-Object {
        Remove-Printer -Name $queueName
        Write-Host "Removed queue: $queueName"
    }

    Get-PrinterPort -Name $capturePath -ErrorAction SilentlyContinue | ForEach-Object {
        Remove-PrinterPort -Name $capturePath
        Write-Host "Removed port: $capturePath"
    }

    Write-Host "Captured fixtures under $fixtureDir were left alone."
    return
}

if ($PSCmdlet.ParameterSetName -eq "Capture") {
    if (-not (Test-Path $capturePath)) {
        Fail "Nothing captured yet: $capturePath does not exist. Print a design to '$queueName' first."
    }

    New-Item -ItemType Directory -Force $fixtureDir | Out-Null
    $target = Join-Path $fixtureDir "$Capture.zpl"
    Move-Item $capturePath $target -Force

    $bytes = (Get-Item $target).Length
    Write-Host "Captured $bytes bytes to $target"

    # Said rather than assumed: a GDI driver rasterizes the page unless the design asks
    # for the printer's own fonts and barcodes, and a fixture full of ~DG graphics
    # compares nothing about fonts or barcode parameters.
    $text = Get-Content $target -Raw
    $graphics = ([regex]::Matches($text, "~DG|\^GF")).Count
    $fields = ([regex]::Matches($text, "\^A[0-9A-H]|\^B[CEUX37Q]")).Count
    Write-Host "  text/barcode commands: $fields, graphic downloads: $graphics"
    if ($fields -eq 0 -and $graphics -gt 0) {
        Write-Warning @"
This capture is all graphics. The driver rasterized the design, so it says nothing
about fonts or barcode parameters. In ZebraDesigner, set each text object to a printer
font and each barcode to a printer barcode, then print again.
"@
    }

    return
}

# Setup.
New-Item -ItemType Directory -Force $workDir | Out-Null

$drivers = Find-ZDesignerDriver
if ($drivers.Count -eq 0) {
    Fail @"
No Zebra printer driver is installed, so there is nothing to capture through.

There is probably nothing to download. Zebra Setup Utilities bundles this driver, and any
model-specific queue ("ZDesigner ZT230-200dpi ZPL" and the like) is the same package with a
model INF entry, so any machine that already prints to a Zebra already has it. Running this
on that machine is the shortest route. Otherwise install Zebra Setup Utilities, or the
driver from Zebra's own download page for the printer model in question.

Installing it needs administrator rights, and Windows cannot fetch it: Add-PrinterDriver
answers "the specified driver does not exist in the driver repository" for every ZDesigner
name. Once it is installed, run this again - adding a queue for a driver already present
needs no elevation.

This is a development tool for producing comparison fixtures. LabelForge itself needs no
driver: it generates its own ZPL and prints raw bytes.
"@
}

if ($DriverName) {
    $driver = $drivers | Where-Object Name -eq $DriverName | Select-Object -First 1
    if (-not $driver) {
        Fail ("Driver '$DriverName' is not installed. Installed Zebra drivers:`n  " +
              (($drivers | Select-Object -ExpandProperty Name) -join "`n  "))
    }
} elseif ($drivers.Count -gt 1) {
    Fail ("Several Zebra drivers are installed; pick one with -DriverName:`n  " +
          (($drivers | Select-Object -ExpandProperty Name) -join "`n  "))
} else {
    $driver = $drivers[0]
}

if (-not (Get-PrinterPort -Name $capturePath -ErrorAction SilentlyContinue)) {
    Add-PrinterPort -Name $capturePath
}

$existing = Get-Printer -Name $queueName -ErrorAction SilentlyContinue
if ($existing) {
    # Re-point rather than refuse: running -Setup twice, or after installing a different
    # driver, should end at the state asked for.
    Set-Printer -Name $queueName -DriverName $driver.Name -PortName $capturePath
} else {
    Add-Printer -Name $queueName -DriverName $driver.Name -PortName $capturePath
}

Write-Host "Queue:  $queueName"
Write-Host "Driver: $($driver.Name)"
Write-Host "Writes: $capturePath"
Write-Host ""
Write-Host "Next, either:"
Write-Host "  -ListFonts                                  see the printer's resident fonts"
Write-Host "  -Print scripts\zdesigner-designs\<x>.txt    print a design through the driver"
Write-Host "  ... or design one in ZebraDesigner and print it to '$queueName'"
Write-Host "then:"
Write-Host "  -Capture <fixture-name>"
