<#
.SYNOPSIS
    Publishes the Game+ backend (Netch.Host) and stages it into electron-app/resources/backend,
    which electron-builder packages as resources/backend in the distributable.

.EXAMPLE
    ./build.ps1                          # self-contained win-x64 Release
    ./build.ps1 -SelfContained:$false    # framework-dependent
    ./build.ps1 -AllowMissingNative      # dev/CI packaging test: warn instead of failing on missing cores
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [bool]$SelfContained = $true,
    [string]$Version = '',
    [switch]$AllowMissingNative
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
# The Electron app expects the published backend (gameplus-host.exe + native cores) unpacked under
# resources/backend, matching the prod path in src/main/sidecar.ts.
$stage = Join-Path $root '../electron-app/resources/backend'

$proj = Join-Path $root 'src/Netch.Host/Netch.Host.csproj'

# Single source of truth for the version: the Electron app's package.json (electron-builder reads the
# same field for the installer/artifact name). Build the host with the same version so the two never
# drift. Override by passing -Version explicitly.
if (-not $Version) {
    $pkg = Join-Path $root '../electron-app/package.json'
    if (-not (Test-Path $pkg)) { throw "package.json not found: $pkg" }
    $Version = (Get-Content $pkg -Raw | ConvertFrom-Json).version
    if (-not $Version) { throw "no version in $pkg" }
}

# Single source of truth for the native components. Required = the app cannot accelerate without
# them; Optional = process mode only (NetFilter SDK is commercially licensed). Keep in sync with
# native/README.md and THIRD_PARTY_NOTICES.md.
$requiredNative = @(
    'xray.exe',          # proxy core: ss / trojan / vmess / vless / socks
    'sing-box.exe',      # proxy core: hysteria2, Shadowsocks plugins
    'geoip.dat',         # IP-based routing data
    'geosite.dat',       # domain-based routing data
    'wintun.dll',        # WinTUN adapter (TUN mode)
    'tun2socks.bin',     # TUN-mode traffic capture helper
    'RouteHelper.bin'    # route-table helper for TUN mode
)
$optionalNative = @(
    'Redirector.bin',    # process-mode traffic capture helper (netfilter2)
    'nfapi.dll',         # NetFilter SDK API
    'nfdriver.sys'       # NetFilter SDK kernel driver
)

Write-Host "==> Publishing backend v$Version ($Configuration / $Runtime / self-contained=$SelfContained)" -ForegroundColor Cyan
# dotnet publish -o does NOT clear the output dir, so an incremental build would leave stale DLLs /
# outdated native binaries in the staged folder (which electron-builder then packages). Clear it first.
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

# NOT single-file: a self-contained exe unpacks native libs to temp AND JITs everything on every launch
# (slow cold start). A plain folder starts near-instantly. We ship a folder anyway.
dotnet publish $proj `
    -c $Configuration `
    -r $Runtime `
    --self-contained $SelfContained `
    -p:PublishReadyToRun=true `
    -p:TieredCompilation=true `
    -p:TieredPGO=true `
    -p:Version=$Version `
    -p:DebugType=none `
    -o $stage
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

# Copy ALL native components into bin\ as LOOSE files. The publish drops the .exe/.dll cores (xray.exe,
# wintun.dll, nfapi.dll) from the output, so copy them explicitly here - the host resolves them by path
# (bin\xray.exe etc.), which requires them on disk, not embedded. Exclude README.md so the tracked
# native/README.md is not shipped into bin\.
$bin = Join-Path $stage 'bin'
New-Item -ItemType Directory -Force $bin | Out-Null
$nativeSrc = Join-Path $root 'native'
if (Test-Path $nativeSrc) {
    Copy-Item (Join-Path $nativeSrc '*') $bin -Recurse -Force -Exclude 'README.md'
    Write-Host "==> Copied native components into bin\" -ForegroundColor Cyan
} else {
    Write-Host "==> native\ not found - nothing copied (see native/README.md)" -ForegroundColor Yellow
}

# Verify what actually landed in the staged bin\ and report it truthfully.
$missingRequired = @($requiredNative | Where-Object { -not (Test-Path (Join-Path $bin $_)) })
$missingOptional = @($optionalNative | Where-Object { -not (Test-Path (Join-Path $bin $_)) })

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("Native components staged into bin\ by build.ps1 (generated at build time).")
$lines.Add("")
$lines.Add("Required (the app cannot accelerate without them):")
foreach ($name in $requiredNative) {
    $path = Join-Path $bin $name
    if (Test-Path $path) {
        $item = Get-Item $path
        $hash = (Get-FileHash $path -Algorithm SHA256).Hash
        $lines.Add(("  {0,-18} present  {1,12:N0} bytes  SHA-256 {2}" -f $name, $item.Length, $hash))
    } else {
        $lines.Add(("  {0,-18} MISSING" -f $name))
    }
}
$lines.Add("")
$lines.Add("Optional (process mode only; NetFilter SDK is commercially licensed):")
foreach ($name in $optionalNative) {
    $path = Join-Path $bin $name
    if (Test-Path $path) {
        $item = Get-Item $path
        $hash = (Get-FileHash $path -Algorithm SHA256).Hash
        $lines.Add(("  {0,-18} present  {1,12:N0} bytes  SHA-256 {2}" -f $name, $item.Length, $hash))
    } else {
        $lines.Add(("  {0,-18} MISSING" -f $name))
    }
}
$lines.Add("")
$lines.Add("See NetchModern/native/README.md for purpose, licences and download URLs.")
$lines | Set-Content (Join-Path $bin 'NATIVE_COMPONENTS.txt') -Encoding utf8

if ($missingOptional.Count -gt 0) {
    Write-Host "==> WARNING: optional native components missing: $($missingOptional -join ', ')" -ForegroundColor Yellow
    Write-Host "    Process mode will be unavailable; Game+ falls back to TUN/system-proxy." -ForegroundColor Yellow
}

if ($missingRequired.Count -gt 0) {
    $msg = "Missing REQUIRED native components in bin\: $($missingRequired -join ', '). " +
           "Populate NetchModern/native/ (see NetchModern/native/README.md) and re-run build.ps1."
    if ($AllowMissingNative) {
        Write-Host "==> WARNING: $msg" -ForegroundColor Yellow
        Write-Host "    -AllowMissingNative set: continuing with an incomplete package (dev/CI only)." -ForegroundColor Yellow
    } else {
        throw $msg
    }
}

$exe = Join-Path $stage 'gameplus-host.exe'
$size = if (Test-Path $exe) { '{0:N1} MB' -f ((Get-Item $exe).Length / 1MB) } else { 'n/a' }
Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  gameplus-host.exe : $size"
Write-Host "  Staged for : electron-app/resources/backend"
