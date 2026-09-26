# Builds the Windows installer with Velopack.
#
# Prerequisites (one time):
#   dotnet tool install --global vpk
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File scripts\pack-windows.ps1 [-Version x.y.z] [-OutputDirectory path]
#
# Output: artifacts\releases\LabelForge-win-Setup.exe (plus the update packages
# Velopack uses for delta auto-updates once a distribution feed exists).

param([string]$Version, [string]$OutputDirectory)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$projectPath = Join-Path $root "src\LabelForge.App\LabelForge.App.csproj"
if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$project = Get-Content -LiteralPath $projectPath
    $Version = [string]($project.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
}
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
    throw "Version must use x.y.z or x.y.z-prerelease format."
}
$publishDir = Join-Path $root "artifacts\publish\win-x64"
$releaseDir = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $root "artifacts\releases"
} elseif ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory
} else {
    Join-Path $root $OutputDirectory
}

Write-Host "Publishing self-contained win-x64 build..."
dotnet publish $projectPath `
    -c Release -r win-x64 --self-contained true -p:Version=$Version -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "Packing installer with Velopack..."
vpk pack --packId LabelForge --packVersion $Version --packDir $publishDir `
    --mainExe LabelForge.App.exe --packTitle "LabelForge" --outputDir $releaseDir
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }

Write-Host "Done. Installer at: $releaseDir"
