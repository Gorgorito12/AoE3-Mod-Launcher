# Publishes the launcher as a self-contained single-file exe with the
# version baked in. Usage:
#
#   .\publish.ps1 0.7.0
#   .\publish.ps1 0.7.0 -Tag        # also creates a git tag v0.7.0
#   .\publish.ps1                   # uses whatever <Version> is in csproj
#
# The exe ends up in .\publish\WarsOfLibertyLauncher.exe.

param(
    [string]$Version,
    [switch]$Tag
)

$ErrorActionPreference = 'Stop'

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$csproj = Join-Path $projectDir 'WarsOfLibertyLauncher\WarsOfLibertyLauncher.csproj'
$outputDir = Join-Path $projectDir 'publish'

if (-not (Test-Path $csproj)) {
    Write-Error "csproj not found at $csproj"
    exit 1
}

# Build the dotnet args
$dotnetArgs = @(
    'publish', $csproj,
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', 'true',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-o', $outputDir
)

if ($Version) {
    $dotnetArgs += "-p:Version=$Version"
    Write-Host "Publishing version $Version..." -ForegroundColor Cyan
} else {
    Write-Host "Publishing using version from csproj..." -ForegroundColor Cyan
}

& dotnet @dotnetArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed."
    exit 1
}

$exePath = Join-Path $outputDir 'WarsOfLibertyLauncher.exe'
if (Test-Path $exePath) {
    $size = (Get-Item $exePath).Length / 1MB
    Write-Host ""
    Write-Host "Done." -ForegroundColor Green
    Write-Host ("  -> {0} ({1:N1} MB)" -f $exePath, $size)
}

if ($Tag -and $Version) {
    $tagName = "v$Version"
    Write-Host ""
    Write-Host "Creating git tag $tagName..." -ForegroundColor Cyan
    git -C $projectDir tag $tagName
    Write-Host "Run 'git push origin $tagName' to push it." -ForegroundColor Yellow
}
