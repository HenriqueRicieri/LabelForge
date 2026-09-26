param([string]$PublishDirectory)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($PublishDirectory)) {
    $PublishDirectory = Join-Path $root 'artifacts\publish\win-x64'
}

$manifestPath = Join-Path $root 'licenses\manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$deps = Get-Content -LiteralPath (Join-Path $PublishDirectory 'LabelForge.App.deps.json') -Raw | ConvertFrom-Json
$targetName = $deps.runtimeTarget.name
if (-not $targetName.EndsWith('/' + $manifest.runtimeIdentifier)) {
    throw "Unreviewed runtime target: $targetName"
}
$target = $deps.targets.PSObject.Properties[$targetName].Value
$reviewed = @{}
foreach ($package in $manifest.packages) {
    $prefix = if ($package.kind -eq 'runtimepack') { 'runtimepack.' } else { '' }
    $key = "$prefix$($package.id)/$($package.version)"
    if ($reviewed.ContainsKey($key)) { throw "Duplicate reviewed package: $key" }
    $reviewed[$key] = $package
}

$actual = @{}
foreach ($entry in $target.PSObject.Properties) {
    $library = $deps.libraries.PSObject.Properties[$entry.Name].Value
    if ($library.type -eq 'project') { continue }
    if (-not $reviewed.ContainsKey($entry.Name)) {
        throw "Review licenses for the published dependency: $($entry.Name)"
    }
    $actual[$entry.Name] = $true
}
foreach ($key in $reviewed.Keys) {
    if (-not $actual.ContainsKey($key)) { throw "Remove stale license inventory entry: $key" }
}

$files = @{}
foreach ($file in $manifest.files) {
    $files[$file.path] = $true
    foreach ($directory in @((Join-Path $root 'licenses'), (Join-Path $PublishDirectory 'licenses'))) {
        $path = Join-Path $directory $file.path
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Missing license/notice file: $path"
        }
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) {
            throw "License/notice bytes differ from the reviewed source: $path"
        }
    }
}
foreach ($component in @($manifest.packages) + @($manifest.fonts)) {
    if (@($component.notices).Count -eq 0) { throw "No notices assigned to $($component.id)" }
    foreach ($notice in $component.notices) {
        if (-not $files.ContainsKey($notice)) { throw "Unlisted notice for $($component.id): $notice" }
    }
}

$native = Get-Content -LiteralPath (Join-Path $root "licenses\$($manifest.nativeInventory)") -Raw | ConvertFrom-Json
$bundleBytes = [System.IO.File]::ReadAllBytes((Join-Path $root "licenses\$($native.bundle)"))
$hasher = [System.Security.Cryptography.SHA256]::Create()
try {
    foreach ($package in $native.packages) {
        if (@($package.notices).Count -eq 0) { throw "No native notices for $($package.id)" }
        foreach ($notice in $package.notices) {
            $hash = [System.BitConverter]::ToString($hasher.ComputeHash($bundleBytes, $notice.offset, $notice.bytes)).Replace('-', '')
            if ($hash -ne $notice.sha256) { throw "Native notice bytes differ: $($package.id)/$($notice.name)" }
        }
        if ($package.standardLicense -and -not $files.ContainsKey($package.standardLicense)) {
            throw "Unlisted standard license for $($package.id)"
        }
    }
} finally { $hasher.Dispose() }
foreach ($component in @($native.webview2Loader, $native.rustStandardLibrary)) {
    foreach ($notice in $component.notices) {
        if (-not $files.ContainsKey($notice)) { throw "Unlisted supplemental native notice: $notice" }
    }
}

foreach ($name in @('LICENSE', 'THIRD-PARTY-NOTICES.md', 'licenses\manifest.json')) {
    $source = Join-Path $root $name
    $published = Join-Path $PublishDirectory $name
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $published -Algorithm SHA256).Hash) {
        throw "Published notice differs from source: $name"
    }
}
$fontLicense = Join-Path $root 'src\LabelForge.Core\Rendering\Fonts\OFL.txt'
if ((Get-FileHash -LiteralPath $fontLicense -Algorithm SHA256).Hash -ne
    (Get-FileHash -LiteralPath (Join-Path $PublishDirectory 'OFL.txt') -Algorithm SHA256).Hash) {
    throw 'Published Roboto font license differs from source.'
}

Write-Host "Verified $($actual.Count) runtime packages, $($manifest.fonts.Count) fonts, $($native.packages.Count) native inventory entries and $($files.Count) notice/inventory files."
foreach ($remaining in $manifest.remainingReview) { Write-Host "Remaining release review: $remaining" }
