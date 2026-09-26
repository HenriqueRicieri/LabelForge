param([string]$VelopackDirectory)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$manifest = Get-Content -LiteralPath (Join-Path $root 'licenses\native-manifest.json') -Raw | ConvertFrom-Json
$tool = $manifest.tool

if ([string]::IsNullOrWhiteSpace($VelopackDirectory)) {
    $command = Get-Command vpk -ErrorAction Stop
    $toolDirectory = Split-Path $command.Source -Parent
    $VelopackDirectory = Join-Path $toolDirectory ".store\vpk\$($tool.version)\vpk\$($tool.version)"
}
if (-not (Test-Path -LiteralPath $VelopackDirectory -PathType Container)) {
    throw "Reviewed vpk $($tool.version) not found. Install with: dotnet tool install --global vpk --version $($tool.version)"
}

[xml]$package = Get-Content -LiteralPath (Join-Path $VelopackDirectory 'vpk.nuspec')
if ($package.package.metadata.version -ne $tool.version -or
    $package.package.metadata.repository.commit -ne $tool.commit) {
    throw 'Review notices for this Velopack version/source before packaging.'
}
foreach ($file in $tool.files) {
    $path = Join-Path $VelopackDirectory $file.path
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing reviewed Velopack input: $($file.path)"
    }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) {
        throw "Review notices for changed Velopack input: $($file.path)"
    }
}

Write-Host "Verified vpk $($tool.version) packer and three pinned $($manifest.architecture) native helpers."
return (Join-Path $VelopackDirectory $tool.entryPoint)
