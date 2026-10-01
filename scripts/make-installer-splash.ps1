# Draws the image Velopack's Setup shows while it installs (vpk pack --splashImage).
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File scripts\make-installer-splash.ps1
#
# Output: scripts\installer-splash.png, committed beside this script so packaging needs
# no drawing step. Rerun it after the icon or the name changes.

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
$iconPath = Join-Path $root "src\LabelForge.App\Assets\labelforge.ico"
$outPath = Join-Path $PSScriptRoot "installer-splash.png"

# The 256 px frame of the app icon is stored as a PNG inside the .ico; read it from the
# directory rather than through Icon, which scales a smaller frame up.
$bytes = [System.IO.File]::ReadAllBytes($iconPath)
$count = [BitConverter]::ToUInt16($bytes, 4)
$best = $null
for ($i = 0; $i -lt $count; $i++) {
    $entry = 6 + 16 * $i
    $width = if ($bytes[$entry] -eq 0) { 256 } else { $bytes[$entry] }
    $size = [BitConverter]::ToInt32($bytes, $entry + 8)
    $offset = [BitConverter]::ToInt32($bytes, $entry + 12)
    if ($null -eq $best -or $width -gt $best.Width) {
        $best = @{ Width = $width; Size = $size; Offset = $offset }
    }
}
$frame = New-Object System.IO.MemoryStream(, $bytes[$best.Offset..($best.Offset + $best.Size - 1)])
$icon = [System.Drawing.Image]::FromStream($frame)

$width = 600
$height = 340
$bitmap = New-Object System.Drawing.Bitmap($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$graphics.Clear([System.Drawing.Color]::FromArgb(255, 30, 34, 40))

$iconSize = 140
$graphics.DrawImage($icon, 56, [int](($height - $iconSize) / 2), $iconSize, $iconSize)

$titleFont = New-Object System.Drawing.Font("Segoe UI Semibold", 34, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$subtitleFont = New-Object System.Drawing.Font("Segoe UI", 16, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 240, 242, 245))
$gray = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 160, 168, 178))
$graphics.DrawString("LabelForge", $titleFont, $white, 224, 128)
$graphics.DrawString("Zebra label designer and ZPL viewer", $subtitleFont, $gray, 228, 178)

$bitmap.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose()
$bitmap.Dispose()
$icon.Dispose()
Write-Host "Wrote $outPath"
