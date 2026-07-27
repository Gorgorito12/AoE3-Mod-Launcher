#Requires -Version 5.1
<#
.SYNOPSIS
    Builds and publishes a signed, single-file .exe ready to share.

.DESCRIPTION
    End-to-end release build for AoE3 Mod Launcher:

      1. Closes any running Aoe3ModLauncher.exe so file locks don't break the
         publish step.
      2. Wipes the previous publish/ folder so leftovers from older builds
         don't sneak into the .exe we're about to share.
      3. Runs `dotnet publish` with the launcher's distribution flags:
         single-file (one .exe, ~120 MB), self-contained (.NET runtime
         embedded so users don't need .NET installed), win-x64, native
         libraries embedded so the .exe leaves no temp-folder artefacts.
      4. Verifies the .exe is Authenticode-signed by the local cert
         (Subject = CN=Gorgorito by default).
      5. Prints the path, size, and SHA-256 hash — paste the hash into the
         GitHub release notes so users can verify the download.

.PARAMETER Version
    Optional. Overrides the <Version> baked into WarsOfLibertyLauncher.csproj
    for this build only — it is NOT written back to disk. Format:
    MAJOR.MINOR.PATCH with an OPTIONAL WoL-style letter suffix, e.g. "1.0.5" or
    "1.0.5a". The letter is split off for stamping: the numeric core
    ("1.0.5") becomes AssemblyVersion/FileVersion (System.Version is numeric-only)
    and the FULL string ("1.0.5a") becomes InformationalVersion, which the
    self-updater reads so a binary recognises its own letter version. When omitted,
    the build uses whatever <Version> the csproj declares.

    The version flows into:
      * Assembly metadata (file properties shown by right-click → Properties
        → Details on the .exe).
      * The launcher's startup log line ("AssemblyVersion: …") and the
        self-update tag comparison.

.PARAMETER Configuration
    Build configuration. Defaults to Release. Use Debug only for diagnosing
    publish-pipeline issues.

.PARAMETER Runtime
    .NET runtime identifier. Defaults to win-x64; this launcher is
    Windows-only so there's no real reason to change it.

.NOTES
    Requires:
      * .NET 8 SDK on PATH (`dotnet --version` returns 8.x)
      * The signing cert at Cert:\CurrentUser\My\<SignCertThumbprint>.
        See <SignCertThumbprint> in WarsOfLibertyLauncher.csproj for the
        thumbprint. If the cert is regenerated, update the .csproj.
      * Windows. Mac/Linux skip signing automatically (the .csproj target's
        Windows-only condition takes care of it).

.EXAMPLE
    .\build-release.ps1
    Standard release build → <repo>\WarsOfLibertyLauncher\publish\Aoe3ModLauncher.exe

.EXAMPLE
    .\build-release.ps1 -Version 0.7.0
    Same release build, but stamps the .exe with version 0.7.0.

.EXAMPLE
    .\build-release.ps1 -Configuration Debug
    Debug-flavored single-file (rare; for diagnosing publish issues).
#>

[CmdletBinding()]
param(
    [string]$Version,
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectFile = Join-Path $projectRoot 'WarsOfLibertyLauncher.csproj'

if (-not (Test-Path $projectFile)) {
    throw "Project file not found: $projectFile"
}

Write-Host ''
Write-Host '=== AoE3 Mod Launcher - Release build ===' -ForegroundColor Cyan
Write-Host ''

# 1. Close any running launcher. The previous publish output is what users
#    actually run; if a copy is open during this script, dotnet publish will
#    fail at the file-copy step at the end with the .exe locked.
#
#    The launcher's manifest declares requireAdministrator, so the running
#    process is elevated. Stop-Process from a non-elevated PowerShell hits
#    "Access denied" — we treat that as a hard stop and ask the user to
#    close it themselves rather than half-running with a stale lock.
$running = Get-Process -Name 'Aoe3ModLauncher' -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Closing running launcher (PIDs: $($running.Id -join ', '))..." -ForegroundColor Yellow
    try {
        $running | Stop-Process -Force -ErrorAction Stop
        Start-Sleep -Seconds 1
    } catch {
        Write-Host ''
        Write-Host 'ERROR: Could not stop the running launcher (likely elevated).' -ForegroundColor Red
        Write-Host '       Close Aoe3ModLauncher.exe manually and re-run this script.' -ForegroundColor Red
        Write-Host "       (PIDs: $($running.Id -join ', '))" -ForegroundColor Red
        Write-Host ''
        exit 1
    }
}

# 2. Clean stale publish output. Without this, the publish step silently
#    keeps old loose files around from previous publish runs that had
#    different flags (e.g. when single-file was off), polluting the
#    distribution folder. Output goes to <repo>\publish\ - a top-level
#    folder so the .exe is easy to find and upload to GitHub Releases.
$publishRoot = Join-Path $projectRoot 'publish'
if (Test-Path $publishRoot) {
    Write-Host "Cleaning previous publish output: $publishRoot" -ForegroundColor Yellow
    Remove-Item -Recurse -Force $publishRoot
}

# 3. Publish. The other distribution flags (PublishReadyToRun, single-file
#    compression) are baked into the .csproj, so we only pass what changes
#    per build here.
#      * SelfContained=true            -> bundle the .NET runtime into the
#                                         .exe; users don't need .NET.
#      * PublishSingleFile=true        -> one .exe instead of a folder.
#      * IncludeNativeLibrariesForSelfExtract=true
#                                      -> embed native DLLs (zstd, etc.)
#                                         and extract them in-memory at
#                                         runtime. Without this, native
#                                         libs get unpacked into a temp
#                                         folder under %TEMP% on first
#                                         launch, leaving disk artefacts.
# Assemble the args list incrementally so the optional -Version override
# only appears when the caller passed one. Using -p:Version on the dotnet
# command line takes precedence over the csproj for this build only —
# the file on disk is not modified.
# Version handling. A WoL-style LETTER suffix ("1.0.5a") is allowed, but
# AssemblyVersion/FileVersion are System.Version (integers only) and can't hold
# the letter — so we SPLIT: the numeric core ("1.0.5") feeds -p:Version (whence
# AssemblyVersion/FileVersion), and the FULL string ("1.0.5a") feeds
# -p:InformationalVersion, which the self-updater reads for self-recognition.
$VersionNumeric = $null
$VersionFull = $null
if ($Version) {
    if ($Version -notmatch '^\d+\.\d+\.\d+[A-Za-z]?$') {
        Write-Host ''
        Write-Host "ERROR: -Version must look like 1.0.5 or 1.0.5a (MAJOR.MINOR.PATCH + optional letter)." -ForegroundColor Red
        Write-Host "       Got: '$Version'" -ForegroundColor Red
        Write-Host ''
        exit 1
    }
    $VersionFull = $Version
    $VersionNumeric = ($Version -replace '[A-Za-z]+$', '')   # strip trailing letter
}

# Say out loud WHICH code is being built. v1.0.11 was published from a stale
# feature branch that was still checked out — it shipped without the stock-exe
# support, so the two mods that need it would have launched plain AoE3, and
# nothing in this script's output hinted at it. The commit ends up embedded in
# InformationalVersion, so this is just surfacing it before the upload instead
# of after.
#
# Captured here but REPORTED in the summary at the end. A warning printed before
# `dotnet publish` scrolls away behind a hundred lines of build output, and the
# moment that matters is when you are copying the SHA-256 out of the summary —
# so that is where it has to be visible.
$srcBranch = $null; $srcCommit = $null; $srcDirty = $false
try {
    $srcBranch = (& git rev-parse --abbrev-ref HEAD 2>$null)
    $srcCommit = (& git rev-parse --short HEAD 2>$null)
    $srcDirty = [bool](& git status --porcelain 2>$null)
    if ($srcBranch) {
        Write-Host "Source: branch '$srcBranch' at $srcCommit" -ForegroundColor DarkGray
    }
} catch {
    # Not a git checkout, or no git on PATH — the build itself doesn't need it.
}

$publishLabel = if ($VersionFull) { "$Configuration | $Runtime | v$VersionFull" } else { "$Configuration | $Runtime" }
Write-Host "Publishing ($publishLabel, single-file, self-contained)..." -ForegroundColor Cyan

$publishArgs = @(
    'publish', $projectFile,
    '-c', $Configuration,
    '-r', $Runtime,
    '--self-contained',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-o', $publishRoot,
    '-nologo'
)
if ($VersionFull) {
    # Numeric core → AssemblyVersion/FileVersion (must be numeric); full string
    # (with letter) → InformationalVersion (drives the self-update tag/recognition).
    $publishArgs += "-p:Version=$VersionNumeric"
    $publishArgs += "-p:FileVersion=$VersionNumeric"
    $publishArgs += "-p:AssemblyVersion=$VersionNumeric"
    $publishArgs += "-p:InformationalVersion=$VersionFull"
}

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed (exit $LASTEXITCODE)"
}

# 4. Verify what came out. PublishSingleFile writes exactly one .exe to the
#    publish folder (everything else gets embedded), so we expect a single
#    file of ~120 MB.
$exePath = Join-Path $publishRoot 'Aoe3ModLauncher.exe'
if (-not (Test-Path $exePath)) {
    throw "Expected output not found: $exePath"
}

$sig = Get-AuthenticodeSignature -FilePath $exePath

# 4b. Sign here if the .csproj's post-publish target didn't manage it.
#
# That target shells out to `powershell` from inside MSBuild, and in some
# environments Set-AuthenticodeSignature fails there with "the module could not
# be loaded" while working perfectly in an ordinary shell — which is how an
# unsigned v1.0.11 got built and published. Rather than diagnose MSBuild's host,
# do the signing from this script, which runs in a normal session. The target
# stays for plain `dotnet build`; this is the belt to its braces, and a no-op
# whenever it already worked.
if ($sig.Status -eq 'NotSigned') {
    Write-Host 'Publish output is unsigned; signing here instead...' -ForegroundColor Yellow
    try {
        Import-Module Microsoft.PowerShell.Security -ErrorAction Stop

        $thumb = ([xml](Get-Content $projectFile)).Project.PropertyGroup.SignCertThumbprint |
                 Where-Object { $_ } | Select-Object -First 1
        $tsUrl = ([xml](Get-Content $projectFile)).Project.PropertyGroup.SignTimestampServer |
                 Where-Object { $_ } | Select-Object -First 1
        if (-not $thumb) { throw 'No <SignCertThumbprint> in the .csproj.' }

        $store = [System.Security.Cryptography.X509Certificates.X509Store]::new('My', 'CurrentUser')
        $store.Open('ReadOnly')
        $cert = $store.Certificates | Where-Object { $_.Thumbprint -eq $thumb } | Select-Object -First 1
        $store.Close()
        if (-not $cert) { throw "Certificate $thumb not found in Cert:\CurrentUser\My." }

        Set-AuthenticodeSignature -FilePath $exePath -Certificate $cert `
            -TimestampServer $tsUrl -HashAlgorithm SHA256 | Out-Null
        $sig = Get-AuthenticodeSignature -FilePath $exePath
        Write-Host "  Signed: $($sig.SignerCertificate.Subject)" -ForegroundColor Green
    }
    catch {
        Write-Warning "Signing from the script also failed: $($_.Exception.Message)"
    }
}

# Read the size and hash AFTER any signing above — signing rewrites the file, so
# a hash taken before it would not match what gets uploaded. Publishing that hash
# is its own outage: the self-updater compares it and rejects the download.
$sizeMB = [math]::Round((Get-Item $exePath).Length / 1MB, 1)
$hash = (Get-FileHash -Algorithm SHA256 -Path $exePath).Hash

# The .exe always carries a 4-part FileVersion (e.g. "0.7.0.0"). Reading
# it back from disk is the truth — confirms the -p:Version override actually
# applied and matches whatever the csproj declared if no override was passed.
$fileVersion = (Get-Item $exePath).VersionInfo.ProductVersion

Write-Host ''
Write-Host '=== Build complete ===' -ForegroundColor Green
Write-Host "  Path:      $exePath"
Write-Host "  Version:   $fileVersion"
Write-Host "  Size:      $sizeMB MB"
Write-Host "  SHA-256:   $hash"
Write-Host "  Signature: $($sig.Status)"
if ($sig.SignerCertificate) {
    Write-Host "  Signer:    $($sig.SignerCertificate.Subject)"
}
if ($srcBranch) {
    Write-Host "  Source:    branch '$srcBranch' at $srcCommit"
}
Write-Host ''

# The two ways a build can still be publishable-looking but wrong. Both are shown
# HERE, beside the SHA-256 you are about to copy, because that is the last moment
# before the file gets uploaded.
if ($srcBranch -and $srcBranch -ne 'main') {
    Write-Warning "Built from '$srcBranch', not 'main'. v1.0.11 shipped this way and was missing half the release."
}
if ($srcDirty) {
    Write-Warning 'Working tree had uncommitted changes, so this build includes work that is not in any commit.'
}
if (-not $VersionFull) {
    Write-Warning "No -Version passed, so this is stamped '$fileVersion' from the .csproj. A release needs -Version, or the self-updater misreads which version this is."
}

# An UNSIGNED build must not reach a release. The launcher's self-update refuses
# any update whose Authenticode signer doesn't match the running binary's, so
# publishing an unsigned .exe leaves every existing user downloading ~170 MB and
# then being told "verification failed" — with no way forward. That is exactly
# how v1.0.11 shipped: this script warned, then printed "Ready to upload" and the
# SHA line anyway, and exited 0.
#
# The distinction matters. A self-signed CN=Gorgorito cert that isn't in Root
# reports 'UnknownError' (untrusted chain) on EVERY correct build — see the code
# signing note in CLAUDE.md — so treating anything != 'Valid' as suspicious cried
# wolf on every run and trained the warning away. Only a genuinely missing
# signature is fatal.
$signerSubject = $sig.SignerCertificate.Subject
if ($sig.Status -eq 'NotSigned' -or [string]::IsNullOrWhiteSpace($signerSubject)) {
    Write-Host ''
    Write-Error @"
The published .exe is NOT signed, so it must not be uploaded.

The launcher's self-update rejects an unsigned update, so publishing this would
break updating for every existing user.

Check that <SignCertThumbprint> in the .csproj matches a certificate in
Cert:\CurrentUser\My, then build again.
"@
    exit 1
}

# 'UnknownError' here means "signed, but the chain isn't trusted on this machine",
# which is the normal and expected state for the self-signed cert.
if ($sig.Status -ne 'Valid') {
    Write-Host "  (Signature status '$($sig.Status)' is expected for the self-signed cert — it is signed.)" -ForegroundColor DarkGray
}

Write-Host 'Ready to upload to GitHub Releases. Include the SHA-256 above in the release notes so users can verify the download.' -ForegroundColor Cyan
Write-Host ''
# Paste-ready line for the GitHub release notes. The launcher's self-update
# (LauncherUpdateService.ExtractExpectedSha256) parses a "SHA256:" line out of
# the release body as its integrity-check fallback when GitHub's asset digest
# field isn't available, so copy this verbatim into the release description.
Write-Host 'Copy this line into the GitHub release notes (used by the launcher to verify the download):' -ForegroundColor Cyan
Write-Host "SHA256: $hash"
Write-Host ''
