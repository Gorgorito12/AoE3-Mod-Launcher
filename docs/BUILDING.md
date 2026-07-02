# Building & releasing

Requires **.NET 8 SDK** on Windows. This is a `net8.0-windows` + WPF project — it
**cannot** be built or run on Linux/macOS.

## Quick build (development)

```powershell
cd WarsOfLibertyLauncher
dotnet build -c Release
```

Output: `bin\Release\net8.0-windows\Aoe3ModLauncher.exe` (framework-dependent,
needs .NET 8 runtime on the machine that runs it).

## Single-file portable .exe (recommended for distribution)

Use the included PowerShell script — it cleans previous output, runs `dotnet
publish` with the right flags, signs the binary with the local code-signing
cert, and prints the path / size / SHA-256 / signature status:

```powershell
cd WarsOfLibertyLauncher
.\build-release.ps1 -Version 1.0.5   # release builds MUST pass -Version
```

`-Version` accepts a WoL-style letter suffix (`1.0.5a`): the numeric core is
stamped into the AssemblyVersion and the full string into the
InformationalVersion — the self-updater relies on both, so don't omit it for
a release build.

Output: `WarsOfLibertyLauncher\publish\Aoe3ModLauncher.exe` (**~190 MB**, fully
self-contained — no .NET install required on the target machine). It's ~190 MB
instead of ~120 MB because single-file **compression is deliberately OFF**
(`EnableCompressionInSingleFile=false`): the self-extracting decompression was
the #1 trigger for Defender's `Win32/Injector` packer heuristic. Compression
comes back once releases are signed by a real trusted cert (SignPath).

The script:
- Closes any running launcher instance to free file locks.
- Wipes the previous `publish/` folder so leftovers don't pollute the build.
- Publishes single-file, self-contained, win-x64, with native libs embedded
  (`IncludeNativeLibrariesForSelfExtract=true`) so the `.exe` leaves no temp
  artefacts on disk.
- Signs the `.exe` via the post-publish target in the `.csproj` (uses the
  cert thumbprint in `<SignCertThumbprint>` — see comments in the `.csproj`
  for one-time setup of `New-SelfSignedCertificate`).
- Prints a SHA-256 hash to paste into GitHub release notes.

## Manual publish (without the script)

```powershell
dotnet publish WarsOfLibertyLauncher\WarsOfLibertyLauncher.csproj `
    -c Release `
    -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o WarsOfLibertyLauncher\publish
```

The post-publish signing target in the `.csproj` runs automatically as long as
the cert exists at `Cert:\CurrentUser\My\<thumbprint>`.

## Distributing a release

**Official channel — CI (recommended):** push a `vX.Y.Z` tag (or run
`.github/workflows/release.yml` manually via *workflow_dispatch*). The
`windows-latest` runner runs the unit tests, builds the same self-contained
single-file `.exe` **unsigned** (`-p:SignOutput=false`) and prints its SHA-256
to the run summary. Building in CI is a **SignPath Foundation requirement** —
once the pending application is approved, the workflow's `sign` job (gated on
the `SIGNPATH_ORGANIZATION_ID` repo variable) signs the artifact automatically.

**Local/ad-hoc channel:**
1. Run `.\build-release.ps1 -Version X.Y.Z` and copy the SHA-256 hash it prints.
2. Create a new release on GitHub with a matching `vX.Y.Z` tag.
3. Attach `publish\Aoe3ModLauncher.exe` as a release asset.
4. Paste the SHA-256 in the release notes so users can verify the download
   (the self-updater also reads it to verify before swapping).
5. Link to [`INSTALL.md`](../WarsOfLibertyLauncher/INSTALL.md) (or copy its
   content) so users know what to do if SmartScreen / Smart App Control
   blocks the binary on first launch.
6. (Optional) Submit the `.exe` to
   [Microsoft Defender Sample Submission](https://www.microsoft.com/en-us/wdsi/filesubmission)
   — Microsoft analysis improves Smart App Control reputation in 1–3 days.
