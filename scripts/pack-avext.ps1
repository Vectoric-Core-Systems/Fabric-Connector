<#
.SYNOPSIS
  Packs a built extension directory (or several, one per platform) into <Name>-<version>.avext, the zip Window > Extensions > Install from file reads.

.DESCRIPTION
  An extension directory is what the build deploys to <engine>/bin/Extensions/FabricConnector (cmake/Extension.cmake): extension.json, native/<platform>/,
  managed/, tools/, data/. The package holds
    extension.json
    native/<platform>/*   the runtime libraries only (.dll, .so, .dylib), never .pdb .ilk .lib .exp
    managed/*  tools/*  data/*   (tools: the script converter and what it needs; no debug symbols, no apphost)
    LICENSE  README.md    from the directory, else from the repository root
  Several directories MERGE into one package: native/<platform>/ comes from the directory that built it (native/win-x64, native/linux-x64,
  native/osx-arm64), the rest from the first directory that has the file. extension.json must agree in name and version across them.
  Works in PowerShell 7 on Windows, Linux and macOS and in Windows PowerShell 5.1. Zip entries use '/' and carry Unix modes (0644, 0755 for what is
  executable on a Unix host).

.EXAMPLE
  pwsh scripts/pack-avext.ps1 -ExtensionDir C:\engine\bin\Extensions\FabricConnector -OutDir dist

.EXAMPLE
  pwsh scripts/pack-avext.ps1 -ExtensionDir win\FabricConnector, linux\FabricConnector, osx\FabricConnector -OutDir dist
#>
param(
    [Parameter(Mandatory = $true)] [string[]] $ExtensionDir,
    [string] $OutDir = '.',
    [string] $RepoRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (-not $RepoRoot) { $RepoRoot = Split-Path -Parent $PSScriptRoot }
$onUnix = ($PSVersionTable.PSEdition -eq 'Core') -and (-not $IsWindows)

# The reader (Avext.cpp, ZipLimits) refuses more than this; failing here says so before a release is cut.
$maxEntries = 4096
$maxEntryBytes = 512MB
$maxTotalBytes = 2048MB

function Get-RelativePath([string] $root, [string] $full) {
    return $full.Substring($root.Length).TrimStart('\', '/').Replace('\', '/')
}

$dirs = @()
foreach ($d in $ExtensionDir) {
    if (-not (Test-Path -LiteralPath $d -PathType Container)) { throw "Extension directory not found: $d" }
    $full = (Resolve-Path -LiteralPath $d).ProviderPath.TrimEnd('\', '/')
    if (-not (Test-Path -LiteralPath (Join-Path $full 'extension.json'))) { throw "No extension.json in $full" }
    $dirs += $full
}

$manifestText = [System.IO.File]::ReadAllText((Join-Path $dirs[0] 'extension.json'))
$manifest = $manifestText | ConvertFrom-Json
$name = [string] $manifest.name
$version = [string] $manifest.version
if ($name -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$') { throw "extension.json name '$name' cannot be a folder name" }
if (-not $version) { throw 'extension.json has no version' }
foreach ($d in $dirs) {
    $m = [System.IO.File]::ReadAllText((Join-Path $d 'extension.json')) | ConvertFrom-Json
    if ([string] $m.name -ne $name -or [string] $m.version -ne $version) {
        throw "$d holds $($m.name) $($m.version), not $name $version"
    }
}

$nativeBase = [string] $manifest.native
$libraryPattern = '\.(dll|so|dylib)$|\.so\.[0-9.]+$'
$skipPattern = '\.(pdb|ilk|lib|exp|obj|exe)$'

# relative path -> source file; the first directory that has a file wins (managed, tools and data are the same everywhere)
$files = New-Object 'System.Collections.Generic.SortedDictionary[string,string]' ([StringComparer]::Ordinal)
$platforms = @{}

foreach ($d in $dirs) {
    $all = Get-ChildItem -LiteralPath $d -Recurse -File -Force | Where-Object { -not ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) }
    foreach ($f in $all) {
        $rel = Get-RelativePath $d $f.FullName
        $top = $rel.Split('/')[0]
        if ($rel -eq 'extension.json') {
            if (-not $files.ContainsKey($rel)) { $files[$rel] = $f.FullName }
            continue
        }
        if ($rel -eq 'LICENSE' -or $rel -eq 'README.md') {
            if (-not $files.ContainsKey($rel)) { $files[$rel] = $f.FullName }
            continue
        }
        if ($top -eq 'native') {
            $parts = $rel.Split('/')
            if ($parts.Length -ne 3) { continue }
            if ($f.Name -notmatch $libraryPattern) { continue }
            $platform = $parts[1]
            if ($platforms.ContainsKey($platform) -and $platforms[$platform] -ne $d) { throw "native/$platform is in both $($platforms[$platform]) and $d" }
            $platforms[$platform] = $d
            $files[$rel] = $f.FullName
            continue
        }
        if ($top -eq 'managed' -or $top -eq 'tools' -or $top -eq 'data') {
            if ($f.Name -match $skipPattern) { continue }
            # the .NET apphost: a file <name> next to <name>.dll (the tool is run as `dotnet <name>.dll`)
            if ($top -eq 'tools' -and (Test-Path -LiteralPath ($f.FullName + '.dll'))) { continue }
            if (-not $files.ContainsKey($rel)) { $files[$rel] = $f.FullName }
        }
    }
}

if ($platforms.Count -eq 0) { throw "No native/<platform>/ library found in: $($dirs -join ', ')" }
if ($nativeBase) {
    foreach ($p in $platforms.Keys) {
        $has = $false
        foreach ($k in $files.Keys) { if ($k.StartsWith("native/$p/$nativeBase.")) { $has = $true } }
        if (-not $has) { throw "native/$p has no $nativeBase library" }
    }
}
foreach ($k in 'LICENSE', 'README.md') {
    if (-not $files.ContainsKey($k)) {
        $src = Join-Path $RepoRoot $k
        if (Test-Path -LiteralPath $src) { $files[$k] = $src } else { Write-Warning "$k not found in the extension directory or $RepoRoot" }
    }
}

if ($files.Count -gt $maxEntries) { throw "$($files.Count) files; the installer reads at most $maxEntries" }
$total = 0L
foreach ($k in $files.Keys) {
    $len = (Get-Item -LiteralPath $files[$k]).Length
    if ($len -gt $maxEntryBytes) { throw "$k is $len bytes; the installer reads at most $maxEntryBytes per file" }
    $total += $len
}
if ($total -gt $maxTotalBytes) { throw "The package would unpack to $total bytes; the installer reads at most $maxTotalBytes" }

$null = New-Item -ItemType Directory -Force -Path $OutDir
$outFull = (Resolve-Path -LiteralPath $OutDir).ProviderPath
$package = Join-Path $outFull "$name-$version.avext"
$temp = "$package.tmp"
if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force }

$order = @('extension.json') + @($files.Keys | Where-Object { $_ -ne 'extension.json' })
$zip = [System.IO.Compression.ZipFile]::Open($temp, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($rel in $order) {
        $src = $files[$rel]
        $mode = 420   # 0644
        if ($onUnix) {
            $unix = [int] [System.IO.File]::GetUnixFileMode($src)
            if ($unix -band 73) { $mode = 493 }   # any execute bit -> 0755
        }
        $entry = $zip.CreateEntry($rel, [System.IO.Compression.CompressionLevel]::Optimal)
        $entry.ExternalAttributes = [int] (([long] (32768 -bor $mode)) * 65536 - 4294967296)   # regular file + mode, in the high 16 bits
        $stamp = (Get-Item -LiteralPath $src).LastWriteTime
        if ($stamp.Year -lt 1980) { $stamp = [DateTime]'1980-01-01' }
        $entry.LastWriteTime = [DateTimeOffset] $stamp
        $in = [System.IO.File]::OpenRead($src)
        try {
            $out = $entry.Open()
            try { $in.CopyTo($out) } finally { $out.Dispose() }
        } finally { $in.Dispose() }
    }
} finally {
    $zip.Dispose()
}
Move-Item -LiteralPath $temp -Destination $package -Force

$hash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
$leaf = Split-Path -Leaf $package
[System.IO.File]::WriteAllText("$package.sha256", "$hash *$leaf`n")

Write-Host "$leaf  $([math]::Round((Get-Item -LiteralPath $package).Length / 1MB, 2)) MB, $($files.Count) files, unpacks to $([math]::Round($total / 1MB, 2)) MB"
Write-Host "platforms: $((@($platforms.Keys) | Sort-Object) -join ', ')"
Write-Host "sha256:    $hash"
Write-Output $package
