# Builds the Windows installer with Velopack.
#
# Prerequisites (one time):
#   dotnet tool install --global vpk
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File scripts\pack-windows.ps1 [-Version 0.1.0]
#
# Output: artifacts\releases\LabelForge-win-Setup.exe (plus the update packages
# Velopack uses for delta auto-updates once a distribution feed exists).

param([string]$Version = "0.1.0")

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$publishDir = Join-Path $root "artifacts\publish\win-x64"
$releaseDir = Join-Path $root "artifacts\releases"

Write-Host "Publishing self-contained win-x64 build..."
dotnet publish (Join-Path $root "src\LabelForge.App\LabelForge.App.csproj") `
    -c Release -r win-x64 --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "Packing installer with Velopack..."
vpk pack --packId LabelForge --packVersion $Version --packDir $publishDir `
    --mainExe LabelForge.App.exe --packTitle "LabelForge" --outputDir $releaseDir
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }

Write-Host "Done. Installer at: $releaseDir"
