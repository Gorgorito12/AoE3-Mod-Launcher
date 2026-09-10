<#
.SYNOPSIS
    Turns an extracted AoE3 mod install into the payload the launcher installs, plus the optional
    userdata.zip that seeds the mod's "My Games" folder.

.DESCRIPTION
    Three things this does that are easy to get wrong by hand:

    1. THE ZIP ROOT IS THE CONTENTS OF bin\.
       The launcher clones the player's AoE3, flattens the clone's bin\ into the install root
       (NativeInstallService.FlattenBinSubfolder), and only THEN lays the payload over it. A
       payload that ships its own bin\ folder is never flattened — it just sits at
       <install>\bin\ and the mod loads nothing. NormalizePayloadRoot will not rescue it either:
       it descends only while a folder has exactly one subdirectory and no loose files, and a mod
       folder has bin\ + directx\ + msxml\ + unins000.*.

    2. FILES ALREADY IN THE BASE GAME ARE DROPPED.
       The launcher supplies them from the player's own AoE3 clone. Shipping them again bloats
       the download and redistributes Microsoft's files, which is not ours to do. Comparison is
       by SHA-256, never by size: a size match on a small XML is entirely plausible and would
       silently drop a file the mod had modified.

    3. THE RESULT IS SPLIT UNDER GitHub's 2 GB PER-ASSET LIMIT.
       Parts are named <name>.zip.001, .002, ... — contiguous from 001, which is exactly the
       shape GitHubReleaseDownloader.PickPayloadPartIndices accepts. A gap makes the launcher
       refuse the release by name, so never upload a partial set.

    READ THE CATEGORY REPORT BEFORE UPLOADING. "Differs from stock" does not mean "the mod
    changed it": if your reference AoE3 is a different LANGUAGE from the one the mod was built
    on, every voice line and cinematic differs. On the machine this script was written for that
    was 8,084 files under Sound\ (519 MB) and 8 under avi\ (376 MB) — none of it mod content.
    Use -NewOnlyDir on those folders to keep the mod's own additions and drop only the files that
    merely differ from stock; -ExcludeDir is the blunt version that drops the folder entirely.

.PARAMETER ModFolder
    The extracted mod install: the folder that CONTAINS bin\.

.PARAMETER StockAoe3
    A clean Age of Empires III: The Asian Dynasties install, ideally the same language as the one
    the mod was built from. Used only to decide what to leave out.

.PARAMETER OutputFolder
    Where the payload parts and userdata.zip are written. Created if missing.

.PARAMETER UserDataFolder
    Optional: a "Documents\My Games\<mod>" folder to turn into userdata.zip.

.PARAMETER ExcludeDir
    Path prefixes (relative to the zip root) to leave out entirely, after reviewing the report.
    An entry may be a folder at any depth or one exact file - e.g. -ExcludeDir avi,'art\Art5.bar'.

.PARAMETER NewOnlyDir
    Top-level folder names where only files the base game does NOT have are shipped, and files
    that merely DIFFER from stock are dropped — e.g. -NewOnlyDir Sound,avi,Language.

    This is the setting that separates the mod's own content from localization noise, and it is
    usually what you want in those three folders. A mod adds sounds under its own names
    (`karcher_snds.xml`); a file that shares a stock name and differs is almost always the same
    base-game asset in another LANGUAGE, because your reference AoE3 was built in a different one.
    -ExcludeDir is the blunt version: it would throw the mod's own additions away with the noise.

.PARAMETER ReportOnly
    Print the report and stop, without building anything.

.EXAMPLE
    .\package-mod-payload.ps1 -ModFolder 'D:\KnB 1.3.6' -StockAoe3 'C:\...\Age Of Empires 3' `
        -OutputFolder 'D:\out' -UserDataFolder "$HOME\Documents\My Games\Knights and Barbarians" -ReportOnly
#>
#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]   $ModFolder,
    [Parameter(Mandatory = $true)][string]   $StockAoe3,
    [Parameter(Mandatory = $true)][string]   $OutputFolder,
    [string]   $UserDataFolder = '',
    [string]   $PayloadName    = 'payload.zip',
    [string[]] $ExcludeDir     = @(),
    [string[]] $NewOnlyDir     = @(),
    [long]     $PartSizeBytes  = 1900MB,
    [switch]   $ReportOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Never shipped. directx\ and msxml\ come from the player's own AoE3 clone; unins000.* is the
# mod's Inno uninstaller, meaningless for a launcher-managed install; and register.bat only does
# "cd msxml && regsvr32 msxml4.dll", so without the msxml\ folder beside it it is a script
# pointing at nothing (and the player's own AoE3 registered that DLL when THEY installed it).
$SkipTopLevel = @('directx', 'msxml')
$SkipRootFiles = @('unins000.exe', 'unins000.dat', 'register.bat')

function Write-Head([string] $text) {
    Write-Host ''
    Write-Host $text -ForegroundColor Cyan
    Write-Host ('-' * $text.Length) -ForegroundColor DarkCyan
}

# .NET rather than Get-FileHash: no dependency on the Utility module (which does not always
# autoload in a restricted shell), and it streams, which matters on a multi-GB payload.
# -ExcludeDir / -NewOnlyDir entries are PATH PREFIXES, not just top-level folder names: an entry
# matches the whole relative path ("DataP.bar"), a folder ("avi"), or any depth ("Sound\Chats").
# That precision is the difference between dropping the base game's localized voice lines and
# dropping the mod's own music sitting two folders away.
# With `pwsh -File` (and from cmd, or any shell script) a comma-separated value arrives as ONE
# string: PowerShell only splits commas when it parses the call itself. Without this, passing
# -ExcludeDir a,b,c silently matches nothing at all and the script cheerfully packages everything.
# Normalising here makes the option behave the same from a PowerShell prompt and from a script.
function Split-List([string[]] $values) {
    $out = @()
    foreach ($v in $values) {
        if ([string]::IsNullOrWhiteSpace($v)) { continue }
        foreach ($part in ($v -split '[,;]')) {
            if (-not [string]::IsNullOrWhiteSpace($part)) { $out += $part.Trim() }
        }
    }
    return ,$out
}

function Test-PathPrefix([string] $rel, [string[]] $prefixes) {
    foreach ($p in $prefixes) {
        if ([string]::IsNullOrWhiteSpace($p)) { continue }
        $q = $p.Trim('\')
        if ($rel -ieq $q) { return $true }
        if ($rel.StartsWith($q + '\', [System.StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

function Get-Sha256([string] $path) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $stream = [System.IO.File]::OpenRead($path)
        try { return [System.BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
        finally { $stream.Dispose() }
    }
    finally { $sha.Dispose() }
}

$ExcludeDir = Split-List $ExcludeDir
$NewOnlyDir = Split-List $NewOnlyDir

if (-not (Test-Path -LiteralPath $ModFolder))  { throw "ModFolder not found: $ModFolder" }
if (-not (Test-Path -LiteralPath $StockAoe3))  { throw "StockAoe3 not found: $StockAoe3" }

$modFull   = (Resolve-Path -LiteralPath $ModFolder).Path
$stockFull = (Resolve-Path -LiteralPath $StockAoe3).Path
$binPath   = Join-Path $modFull 'bin'
if (-not (Test-Path -LiteralPath $binPath)) {
    throw "No bin\ folder inside '$modFull'. Point -ModFolder at the folder that CONTAINS bin\."
}

# ---------------------------------------------------------------- stage 1: the flattened layout
#
# Build the map "zip-root-relative path -> source file" WITHOUT copying anything yet, so a
# collision between a bin\ file and a root-level file of the same name is caught before any work.

Write-Head 'Stage 1 - flattening (zip root = contents of bin\)'

$map = [ordered]@{}
function Add-Mapped([string] $rel, [string] $source) {
    $key = $rel.ToLowerInvariant()
    if ($map.Contains($key)) {
        throw "Collision at '$rel': both '$($map[$key].Source)' and '$source' map to it. Resolve it in the mod folder first."
    }
    $map[$key] = [pscustomobject]@{ Rel = $rel; Source = $source }
}

Get-ChildItem -LiteralPath $binPath -Recurse -File -Force | ForEach-Object {
    Add-Mapped $_.FullName.Substring($binPath.Length + 1) $_.FullName
}

Get-ChildItem -LiteralPath $modFull -Force | ForEach-Object {
    if ($_.PSIsContainer) {
        if ($_.Name -ieq 'bin') { return }
        if ($SkipTopLevel -contains $_.Name.ToLowerInvariant()) { return }
        Get-ChildItem -LiteralPath $_.FullName -Recurse -File -Force | ForEach-Object {
            Add-Mapped $_.FullName.Substring($modFull.Length + 1) $_.FullName
        }
    }
    elseif ($SkipRootFiles -notcontains $_.Name.ToLowerInvariant()) {
        Add-Mapped $_.Name $_.FullName
    }
}

Write-Host ("  {0,8:N0} files in the mod's flattened layout" -f $map.Count)

# ---------------------------------------------------------------- stage 2: drop base-game files
#
# A file counts as base-game when the SAME relative path exists in stock with the SAME bytes.
# The stock lookup tries both "<stock>\<rel>" and "<stock>\bin\<rel>", because the clone flattens
# bin\ and the mod's layout is already flattened.

Write-Head 'Stage 2 - excluding files identical to the base game'
if ($ExcludeDir.Count) { Write-Host ("  excluding entirely : {0}" -f ($ExcludeDir -join ', ')) }
if ($NewOnlyDir.Count) { Write-Host ("  new files only in  : {0}" -f ($NewOnlyDir -join ', ')) }

$categories = @{}
$keep = New-Object System.Collections.Generic.List[object]
$identical = 0; $identicalBytes = 0L
$done = 0

foreach ($entry in $map.Values) {
    $done++
    if ($done % 2000 -eq 0) { Write-Host ("  ...{0:N0} / {1:N0}" -f $done, $map.Count) }

    $top = ($entry.Rel -split '\\')[0]
    if ($entry.Rel -notmatch '\\') { $top = '(root)' }

    if (Test-PathPrefix $entry.Rel $ExcludeDir) { continue }

    $size = (Get-Item -LiteralPath $entry.Source).Length
    $stockCandidates = @(
        (Join-Path $stockFull $entry.Rel),
        (Join-Path (Join-Path $stockFull 'bin') $entry.Rel)
    )

    $state = 'new'
    foreach ($cand in $stockCandidates) {
        if (-not (Test-Path -LiteralPath $cand -PathType Leaf)) { continue }
        $state = 'changed'
        if ((Get-Item -LiteralPath $cand).Length -ne $size) { continue }
        if ((Get-Sha256 $cand) -eq (Get-Sha256 $entry.Source)) { $state = 'same'; break }
    }

    if (-not $categories.ContainsKey($top)) {
        $categories[$top] = [pscustomobject]@{
            New = 0; NewBytes = 0L; Changed = 0; ChangedBytes = 0L; Same = 0; SameBytes = 0L
        }
    }
    # In a -NewOnlyDir folder a merely-CHANGED file is base-game content in another language,
    # not something the mod authored. Dropping it keeps the mod's own additions and leaves
    # Microsoft's localized assets where they belong: in the player's own clone.
    if ($state -eq 'changed' -and (Test-PathPrefix $entry.Rel $NewOnlyDir)) { $state = 'same' }

    $c = $categories[$top]
    switch ($state) {
        'new'     { $c.New++;     $c.NewBytes     += $size; $keep.Add($entry) }
        'changed' { $c.Changed++; $c.ChangedBytes += $size; $keep.Add($entry) }
        'same'    { $c.Same++;    $c.SameBytes    += $size; $identical++; $identicalBytes += $size }
    }
}

Write-Head 'Report - what would ship, by top-level folder'
Write-Host ('  {0,-22} {1,8} {2,10} {3,8} {4,10}' -f 'folder', 'new', 'new MB', 'changed', 'chg MB')
foreach ($k in ($categories.Keys | Sort-Object)) {
    $c = $categories[$k]
    if ($c.New -eq 0 -and $c.Changed -eq 0) { continue }
    Write-Host ('  {0,-22} {1,8:N0} {2,10:N1} {3,8:N0} {4,10:N1}' -f `
        $k, $c.New, ($c.NewBytes / 1MB), $c.Changed, ($c.ChangedBytes / 1MB))
}
$keepBytes = 0L
if ($keep.Count -gt 0) {
    $keepBytes = [long](($keep | ForEach-Object { (Get-Item -LiteralPath $_.Source).Length } | Measure-Object -Sum).Sum)
}
Write-Host ''
Write-Host ("  would ship : {0,8:N0} files, {1:N2} GB" -f $keep.Count, ($keepBytes / 1GB)) -ForegroundColor Green
Write-Host ("  excluded   : {0,8:N0} files, {1:N2} GB (identical to the base game)" -f $identical, ($identicalBytes / 1GB))

foreach ($k in ($categories.Keys | Sort-Object)) {
    if (@('sound', 'avi', 'language') -notcontains $k.ToLowerInvariant()) { continue }
    $c = $categories[$k]
    if ($c.Changed -lt 50) { continue }
    Write-Host ''
    Write-Host ("  WARNING: '{0}' has {1:N0} CHANGED files ({2:N0} MB)." -f $k, $c.Changed, ($c.ChangedBytes / 1MB)) -ForegroundColor Yellow
    Write-Host  "           That is usually a LANGUAGE difference between your reference AoE3 and" -ForegroundColor Yellow
    Write-Host  "           the one the mod was built from, not mod content. Confirm with the mod" -ForegroundColor Yellow
    Write-Host ("           author, then re-run with -NewOnlyDir {0} - that keeps the mod's own" -f $k) -ForegroundColor Yellow
    Write-Host  "           files there and drops only the ones that merely differ." -ForegroundColor Yellow
}

# Compiled XML shadowing: AoE3 reads data\<name>.xml.XMB in preference to the loose <name>.xml,
# and an IsolatedFolder install is a CLONE of the player's own game - so an .xml you ship with no
# .XMB beside it arrives underneath THEIR compiled copy, in THEIR language and with THEIR data.
# This is where the author finds out, before publishing, rather than from a player reporting the
# wrong language. See docs/MODDING.md for the catalog flag that fixes it.
$shadowed = @()
foreach ($e in $keep) {
    if ($e.Rel -notlike '*.xml') { continue }
    $compiled = $e.Rel + '.XMB'
    if ($map.Contains($compiled.ToLowerInvariant())) { continue }   # you ship one yourself
    # Same two candidates the identical-file check uses: a stock install may be flat or have
    # everything under bin\.
    $inStock = (Test-Path -LiteralPath (Join-Path $stockFull $compiled)) -or
               (Test-Path -LiteralPath (Join-Path (Join-Path $stockFull 'bin') $compiled))
    if ($inStock) { $shadowed += $compiled }
}
if ($shadowed.Count -gt 0) {
    Write-Host ''
    Write-Host ("  NOTE: {0:N0} file(s) ship as .xml with no .XMB beside them, and the base game has" -f $shadowed.Count) -ForegroundColor Cyan
    Write-Host  "        a compiled version of each. AoE3 reads the compiled file first, so on an" -ForegroundColor Cyan
    Write-Host  "        isolated install the player's own copy wins over yours - their language," -ForegroundColor Cyan
    Write-Host  "        and the base game's data instead of your mod's." -ForegroundColor Cyan
    Write-Host  "" -ForegroundColor Cyan
    Write-Host  "        If your mod is distributed as a COMPLETE game folder that carries no" -ForegroundColor Cyan
    Write-Host  "        compiled tables of its own, set  install.supersedeCompiledXml: true  in" -ForegroundColor Cyan
    Write-Host  "        your catalog entry and the launcher will remove them at install." -ForegroundColor Cyan
    Write-Host  "        Do NOT set it if your mod installs OVER the player's AoE3: their peers" -ForegroundColor Cyan
    Write-Host  "        keep those files, and removing them causes LAN version mismatches." -ForegroundColor Cyan
    foreach ($s in ($shadowed | Sort-Object)) { Write-Host ("          {0}" -f $s) -ForegroundColor DarkCyan }
}

if ($ReportOnly) {
    Write-Host ''
    Write-Host 'ReportOnly: stopping before building anything.' -ForegroundColor DarkGray
    return
}

# ---------------------------------------------------------------- stage 3: zip and split

New-Item -ItemType Directory -Force -Path $OutputFolder | Out-Null
$outFull = (Resolve-Path -LiteralPath $OutputFolder).Path
$staging = Join-Path ([System.IO.Path]::GetTempPath()) ("modpayload-" + [guid]::NewGuid().ToString('N'))

Write-Head 'Stage 3 - staging and zipping'
New-Item -ItemType Directory -Force -Path $staging | Out-Null
try {
    foreach ($entry in $keep) {
        $dest = Join-Path $staging $entry.Rel
        $destDir = Split-Path -Parent $dest
        if (-not (Test-Path -LiteralPath $destDir)) { New-Item -ItemType Directory -Force -Path $destDir | Out-Null }
        Copy-Item -LiteralPath $entry.Source -Destination $dest -Force
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $combined = Join-Path $outFull $PayloadName
    if (Test-Path -LiteralPath $combined) { Remove-Item -LiteralPath $combined -Force }
    Write-Host '  compressing (this takes a while on a multi-GB payload)...'
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $staging, $combined, [System.IO.Compression.CompressionLevel]::Optimal, $false)

    $zipSize = (Get-Item -LiteralPath $combined).Length
    Write-Host ("  {0} -> {1:N2} GB" -f $PayloadName, ($zipSize / 1GB))

    if ($zipSize -le $PartSizeBytes) {
        Write-Host '  under the split threshold: uploading this single asset is enough.' -ForegroundColor Green
        Write-Host ''
        Write-Host ("  {0}  {1}" -f $PayloadName, (Get-Sha256 $combined))
    }
    else {
        Write-Host ("  splitting into parts of at most {0:N0} MB..." -f ($PartSizeBytes / 1MB))
        $in = [System.IO.File]::OpenRead($combined)
        try {
            $buffer = New-Object byte[] (8MB)
            $part = 1
            $parts = @()
            while ($in.Position -lt $in.Length) {
                $partPath = '{0}.{1:D3}' -f $combined, $part
                $out = [System.IO.File]::Create($partPath)
                try {
                    $remaining = $PartSizeBytes
                    while ($remaining -gt 0 -and $in.Position -lt $in.Length) {
                        $want = [Math]::Min([long]$buffer.Length, $remaining)
                        $read = $in.Read($buffer, 0, $want)
                        if ($read -le 0) { break }
                        $out.Write($buffer, 0, $read)
                        $remaining -= $read
                    }
                }
                finally { $out.Dispose() }
                $parts += $partPath
                $part++
            }
        }
        finally { $in.Dispose() }

        Remove-Item -LiteralPath $combined -Force
        Write-Host ''
        Write-Host '  Upload EVERY part to the release. A gap makes the launcher refuse it by name.' -ForegroundColor Green
        foreach ($p in $parts) {
            Write-Host ("  {0,-24} {1,8:N0} MB  {2}" -f `
                (Split-Path -Leaf $p), ((Get-Item -LiteralPath $p).Length / 1MB), (Get-Sha256 $p))
        }
    }
}
finally {
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
}

# ---------------------------------------------------------------- stage 4: userdata.zip

if ([string]::IsNullOrWhiteSpace($UserDataFolder)) { return }
if (-not (Test-Path -LiteralPath $UserDataFolder)) { throw "UserDataFolder not found: $UserDataFolder" }

Write-Head 'Stage 4 - userdata.zip'

# The folders the mod expects to exist. Most ship empty; their EXISTENCE is the point.
$skeleton = @(
    'AI', 'AI2', 'AI3', 'campaign', 'Data', 'HomeCities', 'RM3', 'RM3\groupings',
    'Savegame', 'Scenario', 'Screenshots', 'Startup', 'Trigger3', 'Users', 'Users2', 'Users3'
)

# Never shipped. The logs and dumps are the author's own session leftovers, and
# AI3\*.personality is worse than clutter: the launcher's AiGameStats feature harvests those,
# so a stranger's file would show up as the player's own match history from install day one.
$dropExact = @('age3log.txt', 'logfile.txt')
$dropPatterns = @('*.dmp.txt', '*.personality', 'trigtemp.xs')

$udStaging = Join-Path ([System.IO.Path]::GetTempPath()) ("userdata-" + [guid]::NewGuid().ToString('N'))
$udRoot = Join-Path $udStaging (Split-Path -Leaf $UserDataFolder.TrimEnd('\'))
New-Item -ItemType Directory -Force -Path $udRoot | Out-Null
try {
    $srcFull = (Resolve-Path -LiteralPath $UserDataFolder).Path
    $kept = 0; $dropped = 0
    Get-ChildItem -LiteralPath $srcFull -Recurse -File -Force | ForEach-Object {
        $name = $_.Name.ToLowerInvariant()
        if ($dropExact -contains $name) { $dropped++; Write-Host "  dropped  $($_.Name)"; return }
        foreach ($pat in $dropPatterns) {
            if ($name -like $pat) { $dropped++; Write-Host "  dropped  $($_.Name)"; return }
        }
        $rel = $_.FullName.Substring($srcFull.Length + 1)
        $dest = Join-Path $udRoot $rel
        $destDir = Split-Path -Parent $dest
        if (-not (Test-Path -LiteralPath $destDir)) { New-Item -ItemType Directory -Force -Path $destDir | Out-Null }
        Copy-Item -LiteralPath $_.FullName -Destination $dest -Force
        $kept++
    }

    foreach ($d in $skeleton) {
        $p = Join-Path $udRoot $d
        if (-not (Test-Path -LiteralPath $p)) { New-Item -ItemType Directory -Force -Path $p | Out-Null }
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $udZip = Join-Path $outFull 'userdata.zip'
    if (Test-Path -LiteralPath $udZip) { Remove-Item -LiteralPath $udZip -Force }
    # includeBaseDirectory:$true keeps the "<mod>\" wrapper. The launcher accepts the zip rooted
    # either at that folder or at its contents, and the wrapper makes the archive self-describing.
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $udStaging, $udZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

    $udSize = (Get-Item -LiteralPath $udZip).Length
    Write-Host ''
    Write-Host ("  kept {0} file(s), dropped {1}, skeleton of {2} folder(s)" -f $kept, $dropped, $skeleton.Count)
    Write-Host ("  userdata.zip  {0:N0} KB  {1}" -f ($udSize / 1KB), (Get-Sha256 $udZip))
    if ($udSize -gt 50MB) {
        Write-Host '  WARNING: that is very large for a seed - real saved games have probably got in.' -ForegroundColor Yellow
    }
}
finally {
    if (Test-Path -LiteralPath $udStaging) { Remove-Item -LiteralPath $udStaging -Recurse -Force }
}
