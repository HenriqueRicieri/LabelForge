# Captures the ZPL the ZDesigner Windows driver generates, for use as comparison
# fixtures against our own generator (backlog E4).
#
# The driver is what turns a ZebraDesigner design into ZPL; ZebraDesigner itself does
# not. So the only way to see its output is to print through it into a file.
#
# Prerequisite, and it is not scriptable: the ZDesigner driver has to be installed
# first. It is not in Windows' driver store and it is not obtainable from Windows
# Update, so it comes off Zebra's (or Seagull's) download portal behind a registration
# form and a EULA, and staging a new driver package needs administrator rights. Adding
# a QUEUE for a driver that is already installed does not, which is why the rest of
# this runs unelevated.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File scripts\zdesigner-capture.ps1 -Setup
#   ... design a label in ZebraDesigner and print it to the queue named below ...
#   powershell -ExecutionPolicy Bypass -File scripts\zdesigner-capture.ps1 -Capture text-basic
#   powershell -ExecutionPolicy Bypass -File scripts\zdesigner-capture.ps1 -Remove
#
# The port is a LOCAL port whose name is a file path, not the built-in FILE: port.
# FILE: opens a "save as" dialog on every print, which makes a capture run a sequence
# of dialogs; a file-path local port writes straight to disk with nothing to click.

[CmdletBinding(DefaultParameterSetName = "Setup")]
param(
    [Parameter(ParameterSetName = "Setup")][switch]$Setup,
    [Parameter(ParameterSetName = "Setup")][string]$DriverName,
    [Parameter(ParameterSetName = "Capture", Mandatory = $true)][string]$Capture,
    [Parameter(ParameterSetName = "Remove")][switch]$Remove
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

Install the ZDesigner Windows driver first. It needs administrator rights and it is not
available from Windows Update: Add-PrinterDriver reports "the specified driver does not
exist in the driver repository" for every ZDesigner name. Once it is installed, run this
again - adding a queue for a driver that is already present needs no elevation.
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
Write-Host "Next: design a label in ZebraDesigner, print it to '$queueName', then run"
Write-Host "  scripts\zdesigner-capture.ps1 -Capture <fixture-name>"
