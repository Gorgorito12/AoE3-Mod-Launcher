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
    .\build-release.ps1 -Configuration Debug
    Debug-flavored single-file (rare; for diagnosing publish issues).
#>

[CmdletBinding()]
param(
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
Write-Host "Publishing ($Configuration | $Runtime, single-file, self-contained)..." -ForegroundColor Cyan
& dotnet publish $projectFile `
    -c $Configuration `
    -r $Runtime `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishRoot `
    -nologo
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
$sizeMB = [math]::Round((Get-Item $exePath).Length / 1MB, 1)
$hash = (Get-FileHash -Algorithm SHA256 -Path $exePath).Hash

Write-Host ''
Write-Host '=== Build complete ===' -ForegroundColor Green
Write-Host "  Path:      $exePath"
Write-Host "  Size:      $sizeMB MB"
Write-Host "  SHA-256:   $hash"
Write-Host "  Signature: $($sig.Status)"
if ($sig.SignerCertificate) {
    Write-Host "  Signer:    $($sig.SignerCertificate.Subject)"
}
Write-Host ''

# Sanity check: signature should be Valid. If it's NotSigned, the post-build
# target in the .csproj didn't run - most likely the cert thumbprint in
# <SignCertThumbprint> doesn't match anything in Cert:\CurrentUser\My.
if ($sig.Status -ne 'Valid') {
    Write-Warning "Signature status is '$($sig.Status)' - expected 'Valid'."
    Write-Warning 'Check that <SignCertThumbprint> in the .csproj matches a cert in Cert:\CurrentUser\My, and that the cert chain is trusted.'
}

Write-Host 'Ready to upload to GitHub Releases. Include the SHA-256 above in the release notes so users can verify the download.' -ForegroundColor Cyan
Write-Host ''
