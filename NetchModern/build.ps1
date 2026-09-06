<#
.SYNOPSIS
    Publishes the Game+ backend (Netch.Host) and stages it into electron-app/resources/backend,
    which electron-builder packages as resources/backend in the distributable.

.EXAMPLE
    ./build.ps1                          # self-contained win-x64 Release
    ./build.ps1 -SelfContained:$false    # framework-dependent
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [bool]$SelfContained = $true,
    [string]$Version = ''
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
# wintun.dll, nfapi.dll) from the output, so copy them explicitly here — the host resolves them by path
# (bin\xray.exe etc.), which requires them on disk, not embedded.
$bin = Join-Path $stage 'bin'
New-Item -ItemType Directory -Force $bin | Out-Null
$nativeSrc = Join-Path $root 'native'
if (Test-Path $nativeSrc) {
    Copy-Item (Join-Path $nativeSrc '*') $bin -Recurse -Force
    Write-Host "==> Copied native components into bin\" -ForegroundColor Cyan
}
@'
Bundled here already (real engine works out of the box):
  xray.exe        proxy core (ss / trojan / vmess / vless / socks)
  sing-box.exe    proxy core (hysteria2)
  wintun.dll      WinTUN adapter
  geoip.dat, geosite.dat  routing data

Optional — only for the transparent capture modes (Process / TUN), which need admin + drivers:
  Redirector.bin, nfdriver.sys   Process mode (netfilter2)
  tun2socks.bin, RouteHelper.bin TUN mode
'@ | Set-Content (Join-Path $bin 'NATIVE_COMPONENTS.txt') -Encoding utf8

$exe = Join-Path $stage 'gameplus-host.exe'
$size = if (Test-Path $exe) { '{0:N1} MB' -f ((Get-Item $exe).Length / 1MB) } else { 'n/a' }
Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  gameplus-host.exe : $size"
Write-Host "  Staged for : electron-app/resources/backend"
