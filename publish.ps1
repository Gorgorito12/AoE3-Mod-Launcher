#Requires -Version 5.1
<#
.SYNOPSIS
    One-command release entry point: builds the signed, versioned single-file
    .exe and (optionally) creates the matching git tag.

.DESCRIPTION
    This wrapper lives at the repo root for convenience. It deliberately does
    NOT duplicate the build / sign / hash logic — that is the job of the
    canonical WarsOfLibertyLauncher\build-release.ps1, the single source of
    truth. Keeping the pipeline in one place is why the two scripts used to
    drift (this one once looked for a "WarsOfLibertyLauncher.exe" that the
    <AssemblyName> rename made "Aoe3ModLauncher.exe", so its success output
    never printed and it never showed the SHA-256).

    What it does:
      1. Delegates the whole release build to build-release.ps1, forwarding the
         optional -Version override (and -Configuration / -Runtime). That script
         closes any running launcher, wipes publish\, runs the single-file
         self-contained `dotnet publish`, lets the .csproj's MSBuild targets
         Authenticode-sign the .exe, verifies the signature, and prints the path,
         version, size and the SHA-256 line you paste into the GitHub release
         notes (the launcher's self-update parses it for the integrity check).
      2. On a successful build, if -Tag was passed, creates the LOCAL git tag
         vX.Y.Z. It never pushes — releases are synced/published via GitHub
         Desktop. A genuinely new SemVer tag is also what arms the launcher's
         "don't update backwards" self-update guard, so tag releases with SemVer.

    The signed, versioned binary lands at:
        WarsOfLibertyLauncher\publish\Aoe3ModLauncher.exe

    Windows-only (WPF + Authenticode signing). See build-release.ps1's help for
    the signing-cert / .NET-SDK requirements; signing silently no-ops if the
    cert isn't installed, so the build still succeeds (just unsigned).

.PARAMETER Version
    Optional SemVer string ("0.9.9", "1.0.0-rc1") stamped into THIS build only;
    it is not written back to the .csproj. When omitted, the build uses the
    <Version> already declared in WarsOfLibertyLauncher.csproj.

.PARAMETER Tag
    After a successful build, create the local git tag vX.Y.Z. Requires -Version
    (the tag name is derived from it). Never pushes — run `git push origin
    vX.Y.Z` yourself, or just publish the tag from GitHub Desktop. A pre-existing
    tag or a non-git checkout is a soft warning, not a build failure.

.PARAMETER Configuration
    Build configuration, forwarded verbatim to build-release.ps1. Defaults to
    Release; use Debug only to diagnose publish-pipeline issues.

.PARAMETER Runtime
    .NET runtime identifier, forwarded verbatim to build-release.ps1. Defaults to
    win-x64 (this launcher is Windows-only).

.EXAMPLE
    .\publish.ps1
    Signed release using whatever <Version> the .csproj declares.

.EXAMPLE
    .\publish.ps1 0.9.9
    Signed release stamped 0.9.9 -> WarsOfLibertyLauncher\publish\Aoe3ModLauncher.exe

.EXAMPLE
    .\publish.ps1 0.9.9 -Tag
    Same build, then creates the local git tag v0.9.9 (no push).
#>

[CmdletBinding()]
param(
    [string]$Version,
    [switch]$Tag,
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildScript = Join-Path $repoRoot 'WarsOfLibertyLauncher\build-release.ps1'

if (-not (Test-Path $buildScript)) {
    throw "Canonical build script not found: $buildScript"
}

# The tag name is vX.Y.Z derived from -Version, so refuse -Tag without a version
# up front rather than building for two minutes and then silently skipping the
# tag at the very end (the old script's behaviour, which read as "the switch did
# nothing").
if ($Tag -and -not $Version) {
    throw '-Tag requires -Version (the tag name is vX.Y.Z). Example: .\publish.ps1 0.9.9 -Tag'
}

# Hand the entire build / sign / hash pipeline to the canonical script. Only the
# flags that actually vary are forwarded; -Version is omitted entirely when the
# caller didn't pass one so build-release.ps1 falls back to the csproj <Version>.
$buildArgs = @{
    Configuration = $Configuration
    Runtime       = $Runtime
}
if ($Version) { $buildArgs.Version = $Version }

& $buildScript @buildArgs

# build-release.ps1 throws on hard failures (propagated to us by $ErrorAction=
# 'Stop', which aborts this script before the tag block — so we never tag a
# build that didn't complete) and `exit 1`s only if it can't close a running,
# elevated launcher. A clean run falls off its end leaving $LASTEXITCODE at the
# publish step's 0. Guard on that too so an aborted build can't get tagged.
if (($null -ne $LASTEXITCODE) -and ($LASTEXITCODE -ne 0)) {
    Write-Error "build-release.ps1 exited with code $LASTEXITCODE - not tagging."
    exit $LASTEXITCODE
}

if ($Tag) {
    $tagName = "v$Version"
    # The signed .exe already exists at this point (that's the goal), so a tag
    # hiccup — no git on PATH, not a clone, tag already present — is a soft
    # warning, never a reason to fail the whole release.
    try {
        $insideRepo = & git -C $repoRoot rev-parse --is-inside-work-tree 2>$null
        if ($LASTEXITCODE -ne 0 -or $insideRepo -ne 'true') {
            Write-Warning "Not a git repository - skipping tag $tagName (tag it from GitHub Desktop)."
        }
        elseif (& git -C $repoRoot tag --list $tagName) {
            Write-Warning "Tag $tagName already exists - leaving it untouched."
        }
        else {
            & git -C $repoRoot tag $tagName
            if ($LASTEXITCODE -ne 0) { throw "git tag exited $LASTEXITCODE" }
            Write-Host ''
            Write-Host "Created local tag $tagName. Push it with: git push origin $tagName" -ForegroundColor Yellow
            Write-Host '(or just publish the tag from GitHub Desktop)' -ForegroundColor DarkGray
        }
    }
    catch {
        Write-Warning "Could not create tag ${tagName}: $($_.Exception.Message). Tag it from GitHub Desktop instead."
    }
}
