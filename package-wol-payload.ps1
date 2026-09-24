<#
.SYNOPSIS
    Packs a patched Wars of Liberty folder into the multi-part WolPayload.zip the launcher installs.

.DESCRIPTION
    WoL is not packaged like a community mod (see package-mod-payload.ps1): its payload is the
    WoL folder AS IT STANDS after the official patch chain — no bin\ to flatten and nothing
    dropped for being identical to the base game. The launcher lays it over a clone of the
    player's AoE3 byte-for-byte (RemoveStaleBuildArtifacts is a documented no-op), so what goes
    into the zip is exactly what every player gets.

    Things this guards against:

    1. THE WRONG wolai.upl. The one that ships is the ROOT wolai.upl. The old AI3\wolai.upl is
       removed by the official patch 1.1.1b (etc\111b_delete.lst), so finding it means the folder
       is unpatched or a mix of versions: that is a warning, while a missing root wolai.upl is an
       error.

    2. NOTHING IS LEFT OUT BY DEFAULT. README.md, all_paths.txt, data.txt and the validate_*.py
       look like development leftovers, but the official patches install them — a fresh 1.2.0e
       install carries them — so dropping them would make the payload differ from a canonical
       install. -ExcludeFiles exists for ad-hoc use only.

    3. THE WRONG NUMBER OF PARTS. The zip is split by COUNT (-Parts, default 3 — the three urls
       the WoL profile lists), each part ceil(zip / Parts) bytes, and refused if a part would
       reach GitHub's 2 GiB per-asset limit. Parts are .001, .002, ... contiguous, the shape
       DownloadAndConcatenatePartsAsync concatenates. -SplitOnly re-splits existing parts
       without recompressing.

.PARAMETER SourceFolder
    The patched WoL folder (the one holding data\, art\, age3y.exe).

.PARAMETER OutputFolder
    Where the parts are written. Created if missing. The staging copy is made here too, so it
    must be on a volume with room for about twice the folder's size.

.PARAMETER ExcludeFiles
    Root-level file names to leave out. Empty by default (see point 2 above).

.PARAMETER Parts
    How many parts to produce. Default 3. Adding a part means adding its url to ModRegistry.cs.

.PARAMETER SplitOnly
    Skip everything up to compression: take WolPayload.zip in -OutputFolder (or rebuild it from
    the existing WolPayload.zip.NNN parts), verify it, and split it into -Parts again.

.PARAMETER ReleaseTag
    The GitHub release tag the parts will be uploaded to (e.g. 1.2.0f). The script prints the
    ready-to-paste payloadZipUrls + payloadSha256 block for mods/wol/mod.json in the catalog —
    the launcher only accepts a catalog payload that carries the SHA-256 of every part.

.PARAMETER ReportOnly
    Run the preflight and the report, then stop.

.EXAMPLE
    .\package-wol-payload.ps1 -SourceFolder 'C:\Users\me\Downloads\wol' -OutputFolder 'C:\Users\me\Downloads\wol-payload' -ReportOnly
#>
#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $SourceFolder,
    [Parameter(Mandatory = $true)][string] $OutputFolder,
    [string]   $PayloadName   = 'WolPayload.zip',
    [ValidateRange(1, 99)][int] $Parts = 3,
    [string[]] $ExcludeFiles  = @(),
    [switch]   $SplitOnly,
    [string]   $ReleaseTag   = '',
    [string]   $ReleaseRepo  = 'papillo12/Updater',
    [switch]   $ReportOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Head([string] $text) {
    Write-Host ''
    Write-Host $text -ForegroundColor Cyan
    Write-Host ('-' * $text.Length) -ForegroundColor DarkCyan
}

function Get-Hash([string] $path, [string] $algorithm) {
    $h = if ($algorithm -eq 'MD5') { [System.Security.Cryptography.MD5]::Create() }
         else { [System.Security.Cryptography.SHA256]::Create() }
    try {
        $stream = [System.IO.File]::OpenRead($path)
        try { return [System.BitConverter]::ToString($h.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
        finally { $stream.Dispose() }
    }
    finally { $h.Dispose() }
}

# GitHub refuses a release asset of 2 GiB or more; 1 MiB of slack keeps us clear of the edge.
$MaxPartBytes = 2GB - 1MB

function Test-Payload([string] $zip, [int] $expectedFiles) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
    try {
        $count = @($archive.Entries | Where-Object { $_.Name -ne '' }).Count
        $hasAi = $null -ne $archive.GetEntry('wolai.upl')
    }
    finally { $archive.Dispose() }
    if ($expectedFiles -ge 0 -and $count -ne $expectedFiles) { throw "Zip holds $count files, expected $expectedFiles." }
    if (-not $hasAi) { throw 'The zip has no wolai.upl entry.' }
    Write-Host ("  verified: {0:N0} file entries, wolai.upl present" -f $count) -ForegroundColor Green
}

function Split-Payload([string] $zip, [int] $count) {
    $length = (Get-Item -LiteralPath $zip).Length
    $partSize = [long][Math]::Ceiling($length / [double]$count)
    if ($partSize -gt $MaxPartBytes) {
        $needed = [int][Math]::Ceiling($length / [double]$MaxPartBytes)
        throw ("The zip is {0:N0} bytes; {1} parts would be {2:N0} bytes each, over GitHub's 2 GiB limit. " +
               "Use -Parts {3} and add the extra url(s) to ModRegistry.cs.") -f $length, $count, $partSize, $needed
    }

    Write-Head ("Splitting into {0} parts of up to {1:N0} MB" -f $count, ($partSize / 1MB))
    $written = @()
    $in = [System.IO.File]::OpenRead($zip)
    try {
        $buffer = New-Object byte[] (8MB)
        for ($n = 1; $n -le $count; $n++) {
            $partPath = '{0}.{1:D3}.tmp' -f $zip, $n
            $o = [System.IO.File]::Create($partPath)
            try {
                $remaining = $partSize
                while ($remaining -gt 0 -and $in.Position -lt $in.Length) {
                    $read = $in.Read($buffer, 0, [int][Math]::Min([long]$buffer.Length, $remaining))
                    if ($read -le 0) { break }
                    $o.Write($buffer, 0, $read)
                    $remaining -= $read
                }
            }
            finally { $o.Dispose() }
            $written += $partPath
        }
    }
    finally { $in.Dispose() }

    # Only now drop whatever parts were there before, so a failure above leaves them intact.
    Get-ChildItem -LiteralPath (Split-Path -Parent $zip) -File |
        Where-Object { $_.Name -match ('^' + [regex]::Escape((Split-Path -Leaf $zip)) + '\.\d{3}$') } |
        Remove-Item -Force
    $final = foreach ($t in $written) { $f = $t.Substring(0, $t.Length - 4); Move-Item -LiteralPath $t -Destination $f; $f }
    Remove-Item -LiteralPath $zip -Force

    Write-Host ''
    foreach ($p in $final) {
        Write-Host ("  {0,-20} {1,14:N0} bytes  sha256 {2}" -f (Split-Path -Leaf $p), (Get-Item -LiteralPath $p).Length, (Get-Hash $p 'SHA256'))
    }
    Write-Host ''
    Write-Host "  Upload EVERY part to the release. A missing part makes every install fail." -ForegroundColor Green

    $tag = if ($ReleaseTag) { $ReleaseTag } else { '<TAG>' }
    $urls = foreach ($p in $final) { '        "https://github.com/{0}/releases/download/{1}/{2}"' -f $ReleaseRepo, $tag, (Split-Path -Leaf $p) }
    $shas = foreach ($p in $final) { '        "{0}"' -f (Get-Hash $p 'SHA256') }
    Write-Head 'For mods/wol/mod.json in the catalog (update.wol)'
    Write-Host '      "payloadZipUrls": ['
    Write-Host ($urls -join ",`n")
    Write-Host '      ],'
    Write-Host '      "payloadSha256": ['
    Write-Host ($shas -join ",`n")
    Write-Host '      ]'
    Write-Host ''
    Write-Host '  Upload the parts FIRST, then open the catalog PR. Without the hashes the launcher ignores the urls.' -ForegroundColor DarkGray
}

if ($SplitOnly) {
    $out = (Resolve-Path -LiteralPath $OutputFolder).Path
    $combined = Join-Path $out $PayloadName
    if (-not (Test-Path -LiteralPath $combined)) {
        $existing = @(Get-ChildItem -LiteralPath $out -File |
            Where-Object { $_.Name -match ('^' + [regex]::Escape($PayloadName) + '\.(\d{3})$') } |
            Sort-Object Name)
        if ($existing.Count -eq 0) { throw "No $PayloadName or $PayloadName.NNN parts in '$out'." }
        for ($k = 0; $k -lt $existing.Count; $k++) {
            if ($existing[$k].Name -ne ('{0}.{1:D3}' -f $PayloadName, ($k + 1))) { throw "Part $('{0:D3}' -f ($k + 1)) is missing." }
        }
        Write-Head ("Rebuilding {0} from {1} existing parts" -f $PayloadName, $existing.Count)
        $o = [System.IO.File]::Create($combined)
        try { foreach ($e in $existing) { $s = [System.IO.File]::OpenRead($e.FullName); try { $s.CopyTo($o) } finally { $s.Dispose() } } }
        finally { $o.Dispose() }
    }
    Test-Payload $combined -1
    Split-Payload $combined $Parts
    return
}

if (-not (Test-Path -LiteralPath $SourceFolder -PathType Container)) { throw "SourceFolder not found: $SourceFolder" }
$src = (Resolve-Path -LiteralPath $SourceFolder).Path.TrimEnd('\')

# ---------------------------------------------------------------- preflight

Write-Head 'Preflight'

if (Test-Path -LiteralPath (Join-Path $src 'bin')) {
    throw "'$src' contains a bin\ folder. The WoL payload root must be the folder holding data\ and age3y.exe."
}

$required = @('data\protoy.xml', 'data\techtreey.xml', 'data\stringtabley.xml', 'art\zulushield', 'age3y.exe', 'wolai.upl')
foreach ($r in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $src $r))) { throw "Missing '$r' - this does not look like a patched Wars of Liberty folder." }
}

if (Test-Path -LiteralPath (Join-Path $src 'AI3\wolai.upl')) {
    Write-Host ''
    Write-Host "  WARNING: AI3\wolai.upl is present. The official patch 1.1.1b deletes it" -ForegroundColor Yellow
    Write-Host "  (etc\111b_delete.lst), so this folder is unpatched or mixes versions." -ForegroundColor Yellow
}

foreach ($k in 'protoy', 'techtreey', 'stringtabley') {
    Write-Host ("  {0,-16} md5 {1}" -f "$k.xml", (Get-Hash (Join-Path $src "data\$k.xml") 'MD5'))
}
Write-Host '  Compare these with the <version> entry in UpdateInfo.xml before uploading.' -ForegroundColor DarkGray

# ---------------------------------------------------------------- report

Write-Head 'Report'

$excludeSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($e in $ExcludeFiles) { if ($e) { [void]$excludeSet.Add($e.Trim()) } }

$keep = New-Object System.Collections.Generic.List[object]
$excluded = New-Object System.Collections.Generic.List[string]
Get-ChildItem -LiteralPath $src -Recurse -File -Force | ForEach-Object {
    $rel = $_.FullName.Substring($src.Length + 1)
    if ($rel -notmatch '\\' -and $excludeSet.Contains($rel)) { $excluded.Add($rel); return }
    $keep.Add([pscustomobject]@{ Rel = $rel; Source = $_.FullName; Length = $_.Length })
}

$keep | Group-Object { if ($_.Rel -match '\\') { $_.Rel.Split('\')[0] } else { '(root)' } } |
    Sort-Object { ($_.Group | Measure-Object Length -Sum).Sum } -Descending |
    ForEach-Object {
        Write-Host ("  {0,-12} {1,8:N0} files {2,9:N1} MB" -f $_.Name, $_.Count, (($_.Group | Measure-Object Length -Sum).Sum / 1MB))
    }
$totalBytes = [long](($keep | Measure-Object Length -Sum).Sum)
Write-Host ''
Write-Host ("  would ship : {0:N0} files, {1:N2} GB uncompressed" -f $keep.Count, ($totalBytes / 1GB)) -ForegroundColor Green
if ($excluded.Count) { Write-Host ("  excluded   : {0}" -f ($excluded -join ', ')) }

if ($ReportOnly) {
    Write-Host ''
    Write-Host 'ReportOnly: stopping before building anything.' -ForegroundColor DarkGray
    return
}

# ---------------------------------------------------------------- stage, zip, verify

New-Item -ItemType Directory -Force -Path $OutputFolder | Out-Null
$out = (Resolve-Path -LiteralPath $OutputFolder).Path
if ($out.StartsWith($src + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputFolder must not be inside SourceFolder.'
}

$staging = Join-Path $out ('staging-' + [guid]::NewGuid().ToString('N'))
$combined = Join-Path $out $PayloadName
Get-ChildItem -LiteralPath $out -Filter "$PayloadName*" -File | Remove-Item -Force

Write-Head 'Staging'
New-Item -ItemType Directory -Force -Path $staging | Out-Null
try {
    $i = 0
    foreach ($entry in $keep) {
        $i++
        if ($i % 5000 -eq 0) { Write-Host ("  ...{0:N0} / {1:N0}" -f $i, $keep.Count) }
        $dest = Join-Path $staging $entry.Rel
        $dir = Split-Path -Parent $dest
        if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
        Copy-Item -LiteralPath $entry.Source -Destination $dest -Force
    }

    # Re-check after the copy: an antivirus acts on the copy too, and the staging folder is
    # exactly where it would.
    if (-not (Test-Path -LiteralPath (Join-Path $staging 'wolai.upl'))) {
        throw 'wolai.upl vanished from the staging copy - an antivirus removed it. Exclude the output folder and retry.'
    }

    Write-Head 'Compressing (this takes a while)'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $staging, $combined, [System.IO.Compression.CompressionLevel]::Optimal, $false)
}
finally {
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
}

$zipSize = (Get-Item -LiteralPath $combined).Length
Write-Host ("  {0} -> {1:N2} GB" -f $PayloadName, ($zipSize / 1GB))

Test-Payload $combined $keep.Count

# ---------------------------------------------------------------- split

Split-Payload $combined $Parts
