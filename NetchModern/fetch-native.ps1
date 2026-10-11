#Requires -Version 7.0
<#
.SYNOPSIS
    Downloads and verifies the pinned native components listed in native-manifest.json.

.DESCRIPTION
    Reads the manifest, downloads each asset once per distinct URL into a local cache,
    verifies the asset SHA-256, extracts the required entry (zip / 7z / plain file),
    verifies the extracted file SHA-256 and places it into the destination directory
    (atomically via a temp name + Move-Item). Used by GitHub Actions (windows-latest,
    pwsh) and locally by developers; runs under PowerShell 7 on Windows and Linux.

.PARAMETER Manifest
    Path to the native-manifest.json file.

.PARAMETER Destination
    Directory where the verified target files are placed.

.PARAMETER CacheDir
    Directory used to cache downloaded assets and temporary extraction results.

.PARAMETER RequiredOnly
    Skip components with required=false.

.PARAMETER Force
    Re-place target files even if an existing target already has the right SHA-256.

.EXAMPLE
    pwsh ./fetch-native.ps1

.EXAMPLE
    pwsh ./fetch-native.ps1 -RequiredOnly -Force
#>
[CmdletBinding()]
param(
    [string]$Manifest = (Join-Path $PSScriptRoot 'native/native-manifest.json'),
    [string]$Destination = (Join-Path $PSScriptRoot 'native'),
    [string]$CacheDir = (Join-Path ([System.IO.Path]::GetTempPath()) 'gameplus-native-cache'),
    [switch]$RequiredOnly,
    [switch]$Force
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# The progress bar slows Invoke-WebRequest down dramatically on large downloads.
$ProgressPreference = 'SilentlyContinue'

Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-Sha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-Asset([string]$Url, [string]$Sha256) {
    $expected = $Sha256.ToLowerInvariant()
    $dir = Join-Path $CacheDir $expected
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $uri = [System.Uri]$Url
    $leaf = [uri]::UnescapeDataString(($uri.AbsolutePath -split '/')[-1])
    if (-not $leaf) { $leaf = 'asset' }
    $final = Join-Path $dir $leaf
    if ((Test-Path -LiteralPath $final) -and (Get-Sha256 $final) -eq $expected) {
        return $final
    }
    $part = "$final.part"
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            Invoke-WebRequest -Uri $Url -OutFile $part -UseBasicParsing
            break
        } catch {
            if (Test-Path -LiteralPath $part) { Remove-Item -LiteralPath $part -Force }
            if ($attempt -eq 3) { throw }
            Start-Sleep -Seconds 5
        }
    }
    $got = Get-Sha256 $part
    if ($got -ne $expected) {
        Remove-Item -LiteralPath $part -Force
        throw "SHA-256 mismatch for ${Url}: expected $expected, got $got"
    }
    Move-Item -LiteralPath $part -Destination $final -Force
    return $final
}

function Expand-ZipEntry([string]$ZipPath, [string]$EntryPath, [string]$OutFile) {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $entry = $zip.Entries |
            Where-Object { ($_.FullName -replace '\\', '/') -eq $EntryPath } |
            Select-Object -First 1
        if (-not $entry) { throw "Entry '$EntryPath' not found in archive '$ZipPath'." }
        $stream = $entry.Open()
        try {
            $fs = [System.IO.File]::Create($OutFile)
            try { $stream.CopyTo($fs) } finally { $fs.Dispose() }
        } finally { $stream.Dispose() }
    } finally { $zip.Dispose() }
}

function Find-SevenZip {
    foreach ($name in @('7z', '7zz')) {
        $cmd = Get-Command $name -ErrorAction SilentlyContinue
        if ($cmd) { return $cmd.Source }
    }
    $standard = 'C:\Program Files\7-Zip\7z.exe'
    if (Test-Path -LiteralPath $standard) { return $standard }
    throw "7-Zip not found (tried '7z', '7zz' and '$standard'). Install 7-Zip to fetch 7z components."
}

$tmpDirs = [System.Collections.Generic.List[string]]::new()
try {
    if (-not (Test-Path -LiteralPath $Manifest)) { throw "Manifest not found: $Manifest" }
    $json = Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json
    if ($json.schemaVersion -ne 1) { throw "Unsupported manifest schemaVersion: $($json.schemaVersion) (expected 1)" }
    $components = @($json.components)
    if ($components.Count -eq 0) { throw "Manifest contains no components." }

    foreach ($c in $components) {
        $name = if ($c.PSObject.Properties['name']) { [string]$c.name } else { '<unnamed>' }
        foreach ($field in @('name', 'file', 'version', 'required', 'url', 'sha256', 'archive', 'fileSha256')) {
            if (-not $c.PSObject.Properties[$field]) { throw "Component '$name': missing field '$field'." }
        }
        foreach ($field in @('file', 'url', 'sha256', 'fileSha256')) {
            if ([string]::IsNullOrWhiteSpace([string]$c.$field)) { throw "Component '$name': field '$field' is empty." }
        }
        if ($c.sha256 -notmatch '^[0-9a-fA-F]{64}$') { throw "Component '$($c.name)': 'sha256' must be 64 hex chars." }
        if ($c.fileSha256 -notmatch '^[0-9a-fA-F]{64}$') { throw "Component '$($c.name)': 'fileSha256' must be 64 hex chars." }
        if ($c.archive -notin @('zip', '7z', 'file')) { throw "Component '$($c.name)': unsupported archive type '$($c.archive)'." }
        if ($c.archive -ne 'file') {
            if (-not $c.PSObject.Properties['pathInArchive'] -or [string]::IsNullOrWhiteSpace([string]$c.pathInArchive)) {
                throw "Component '$($c.name)': 'pathInArchive' is required for archive type '$($c.archive)'."
            }
        }
        if (-not $c.url.StartsWith('https://')) { throw "Component '$($c.name)': 'url' must start with https://." }
        if ($c.file -match '[/\\]' -or $c.file -like '*..*') {
            throw "Component '$($c.name)': 'file' must be a plain file name."
        }
    }

    if (-not (Test-Path -LiteralPath $CacheDir)) { New-Item -ItemType Directory -Path $CacheDir -Force | Out-Null }
    $assetCache = @{}
    $placed = 0
    $upToDate = 0

    foreach ($c in $components) {
        if ($RequiredOnly -and -not $c.required) { continue }
        $target = Join-Path $Destination $c.file
        if (-not $Force -and (Test-Path -LiteralPath $target) -and (Get-Sha256 $target) -eq $c.fileSha256.ToLowerInvariant()) {
            Write-Host "[skip] $($c.file) already up to date"
            $upToDate++
            continue
        }

        if (-not $assetCache.ContainsKey($c.url)) {
            $assetCache[$c.url] = Get-Asset -Url $c.url -Sha256 $c.sha256
        }
        $asset = $assetCache[$c.url]

        if ($c.archive -eq 'file') {
            $extracted = $asset
        } else {
            $tmp = Join-Path $CacheDir ("extract-" + [guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $tmp -Force | Out-Null
            $tmpDirs.Add($tmp)
            if ($c.archive -eq 'zip') {
                $out = Join-Path $tmp $c.file
                Expand-ZipEntry -ZipPath $asset -EntryPath $c.pathInArchive -OutFile $out
            } else {
                $sevenZip = Find-SevenZip
                & $sevenZip e $asset "-o$tmp" $c.pathInArchive -y | Out-Null
                if ($LASTEXITCODE -ne 0) {
                    throw "7z failed (exit $LASTEXITCODE) extracting '$($c.pathInArchive)' from '$asset'."
                }
                $leaf = ($c.pathInArchive -split '/')[-1]
                $out = Join-Path $tmp $leaf
                if (-not (Test-Path -LiteralPath $out)) {
                    throw "7z did not produce expected file '$leaf' for component '$($c.name)'."
                }
            }
            $extracted = $out
        }

        $got = Get-Sha256 $extracted
        if ($got -ne $c.fileSha256.ToLowerInvariant()) {
            throw "Component '$($c.name)': extracted file hash mismatch: expected $($c.fileSha256), got $got"
        }

        if (-not (Test-Path -LiteralPath $Destination)) { New-Item -ItemType Directory -Path $Destination -Force | Out-Null }
        $tmpTarget = Join-Path $Destination ("." + $c.file + ".tmp-" + [guid]::NewGuid().ToString('N'))
        Copy-Item -LiteralPath $extracted -Destination $tmpTarget -Force
        Move-Item -LiteralPath $tmpTarget -Destination $target -Force
        Write-Host "[ok] $($c.file) $($c.version) $($c.fileSha256)"
        $placed++
    }

    Write-Host "fetch-native: $placed placed, $upToDate up to date"
    exit 0
} catch {
    $msg = $_.Exception.Message
    if ($env:GITHUB_ACTIONS -eq 'true') {
        Write-Host "::error::$msg"
    } else {
        [Console]::Error.WriteLine("fetch-native: ERROR: $msg")
    }
    exit 1
} finally {
    foreach ($d in $tmpDirs) {
        if ($d -and (Test-Path -LiteralPath $d)) { Remove-Item -LiteralPath $d -Recurse -Force }
    }
}
