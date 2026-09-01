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

### Every release, whichever channel

Two files in this repo have to be committed to `main` **before the tag**, and neither is
optional:

1. **`releases/vX.Y.Z.md`** — the notes themselves: Spanish first, then `---`, then the same
   sections in English (copy the shape of the previous one). The GitHub release body is then
   just the bare URL to this file on `main`; the launcher's update dialog turns it into a
   clickable link.
2. **`announcements.json`** — one entry, newest first, so the release reaches the notification
   bell. **Without it the release is silent:** only somebody who happens to open the update
   dialog ever finds out. Publishing is a commit — no deploy, no SSH. The notifier service reads
   the file on its next poll and republishes it in the manifest every launcher already fetches.

Three things about that entry, each of them a way to get it wrong quietly:

- **`id` is permanent, and it is the dedup key.** Never edit one after publishing (it
  re-announces the item to everybody) and never reuse one (it silently suppresses the new
  announcement for everyone who saw the old). The convention is the version without dots:
  `v1.0.13l` → `v1013l`.
- **`url` points at `blob/main/releases/vX.Y.Z.md`**, so the notes file has to be on `main`
  already — the same ordering trap as the release body. Announce first and whoever taps the bell
  gets a 404.
- **It is not instant, and the real figure is closer to an hour than to a minute.** No deploy is
  needed, but three delays stack: the notifier polls the file every **10 minutes** (plus GitHub's
  raw CDN), the manifest is cached for **5 minutes**, and a running launcher only re-reads the
  feed **at startup and then every ~30 minutes**. Don't read "nothing in the bell" five minutes
  after committing as a failure.
- **A player with "check for updates on startup" turned off never sees announcements at all.**
  The feed read is gated on that setting, so for them the bell stays quiet and the release notes
  are the only channel. That is deliberate — the setting means "metered, stay off the network" —
  but it is worth knowing before concluding that a delivery failed.

Check it actually landed: `curl -s https://wol-notify.duckdns.org/manifest` should list the new
`id`. **If the response's `etag` did not change, no launcher will ever see it** — that is this
feature's worst failure mode and it is completely silent. (The notifier has a test for exactly
that, `manifest.test.ts`'s "a NEW announcement moves the ETag".)

The file's own `_readme` carries the full field list.

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
