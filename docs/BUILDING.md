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

> **Every release now installs itself, on every machine, without anybody clicking.** The
> launcher checks at startup and, when something newer exists, downloads it and restarts into
> it before its window opens (`Services/StartupUpdateGate.cs`). Nobody reviews a release before
> it reaches them, so the two things below stop being paperwork:
>
> - **Always pass `-Version`.** A build published without it reports an older informational tag
>   than the tag it ships under, so every launcher that installs it comes back still being
>   offered the same release. That loops — download, restart, download — and the anti-loop latch
>   is what stops it, at the cost of the release never installing itself for anyone. There is no
>   way to fix such a release except publishing another one.
> - **The asset and the SHA-256 have to be right.** The launcher verifies both before it swaps,
>   so a wrong hash does not break anybody's install — it just means the update silently never
>   applies (the launcher burns that tag and falls back to the gold pill). Check the release
>   after publishing rather than assuming.
>
> **No build of yours will ever update itself.** Anything running out of a build output — the
> `*.deps.json` and `*.runtimeconfig.json` beside the `.exe` give it away, and the published
> single-file build has neither — is exempt in any configuration, started any way. So are a Debug
> build, one under a debugger, and `--no-update-gate`. Only the published `.exe` updates itself.

Two files in this repo have to be committed to `main` **before the tag**, and neither is
optional:

1. **`releases/vX.Y.Z.md`** — the notes themselves: **English first, then `---`, then the same
   sections in Spanish**, whose H1 carries `(Español)`. The pointer line in the opening
   blockquote is addressed to the readers who have to scroll, so it leads in Spanish:
   *«La versión en español está más abajo, en este mismo documento.»* Copy the shape of
   `v1.0.14k.md`, which is the first one this way — **anything from `v1.0.14j` back is Spanish
   first and stays that way**, because a published note has one URL and rewriting the record is
   not worth it. The GitHub release body is then just the bare URL to this file on `main`; the
   launcher's update dialog turns it into a clickable link.

   Only `releases/` flipped. The other bilingual player pages — `docs/ELO.md`,
   `docs/IS-IT-A-VIRUS.md`, `docs/AUDIT.md` — are still Spanish first: they are read by the
   player base, which is mostly Spanish-speaking, while a release note is also read by anyone
   arriving from the GitHub release page.

   **If the player never saw it, it is not a fix — it is a feature.** Write what the launcher
   does now, not what it used to get wrong, and keep "Fixes" / «Arreglos» for what people
   actually lived through: what they reported, or what was visibly wrong on screen in the
   previous release. The case that set the rule: 1.0.14d first announced that Germany's flag
   had been the base game's instead of the mod's — but nobody had ever known those flags could
   be wrong, so the note taught the reader a bug they never suffered, and made a release of new
   work read like a patch.

2. **`announcements.json`** — one entry, newest first, so the release reaches the notification
   bell. **Without it the release is silent:** only somebody who happens to open the update
   dialog ever finds out. Publishing is a commit — no deploy, no SSH. The notifier service reads
   the file on its next poll and republishes it in the manifest every launcher already fetches.

   **The title and body are in English, always.** The bell shows one line to every player at
   once and cannot pick a language, and every entry before 1.0.14d was English; two written in
   Spanish that day stood out as the odd ones. The bilingual detail belongs in the release
   note the `url` points at, which from `v1.0.14k` opens in English and carries the Spanish
   below — so the bell and the top of the note now read in the same language.

Three things about that entry, each of them a way to get it wrong quietly:

- **`id` is permanent, and it is the dedup key.** Never edit one after publishing (it
  re-announces the item to everybody) and never reuse one (it silently suppresses the new
  announcement for everyone who saw the old). The convention is the version without dots:
  `v1.0.13l` → `v1013l`.
- **`url` points at `blob/main/releases/vX.Y.Z.md`**, so the notes file has to be on `main`
  already — the same ordering trap as the release body. Announce first and whoever taps the bell
  gets a 404.
- **Commit the entry AFTER the GitHub Release exists, in its own commit** — the notes file goes
  with the code, the announcement does not. The rule above only protects the notes file; the
  notes file's own ⬇ link points at `releases/tag/vX.Y.Z`, which does not exist until you
  publish. Announce at the same time as the code and, for however long the build and the testing
  take, the bell hands everybody a download that 404s. It is not a disaster if it slips into the
  code commit — worst case is that window — but the separate commit costs nothing and closes it.
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

**Local/ad-hoc channel:** the root `publish.ps1` wraps steps 1-2 — it forwards
`-Version` / `-Configuration` / `-Runtime` to `build-release.ps1` (the single source of
truth for build, sign and hash) and, with `-Tag`, creates the local `vX.Y.Z` git tag.
It never pushes. Either run it or follow the steps by hand:

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
