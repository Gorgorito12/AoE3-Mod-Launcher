# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A native Windows desktop launcher (WPF, .NET 8) for *Age of Empires III*
total-conversion mods — currently Wars of Liberty and the Improvement Mod. It
replaces the legacy Java updater + Inno Setup installer with a single
self-contained `.exe` that installs, updates, verifies, and launches the mods,
plus a multiplayer matchmaking tab.

It also exposes the **stock, unmodded Age of Empires III: The Asian Dynasties**
as a built-in *detect-only* entry (`aoe3-tad`): the launcher locates an existing
install and runs it — single-player plus the same Radmin multiplayer the mods
use — but **never installs, updates, or uninstalls the base game** (that's the
user's own legally-owned copy). See the `IsStockGame` gotcha below.

The repo also contains `aoe3-mods-catalog-template/` (a template for the
separate community mod-catalog GitHub repo, with `mod.schema.json` and PR
auto-merge Actions), `aoe3-translations-template/` (the matching template for a
community **translations** repo, with `translation.schema.json` + a README
documenting the `translations/<id>/<version>/` folder/version-history layout), and
`docs/MODDING.md` (mod-authoring guide).

## Platform constraint (read first)

This is a **Windows-only** project: `net8.0-windows` + `UseWPF`. It **cannot be
built or run on Linux/macOS** — `dotnet build` fails off-Windows. **Where you can
verify depends on where this session runs:** in a Linux cloud session you
**cannot** compile/run/smoke-test — reason about the code statically, rely on the
user to build on Windows, and say so rather than claiming a change is verified.
**On a local Windows checkout (the maintainer's setup, where this repo is usually
worked on) `dotnet build -c Release` works and you SHOULD run it to verify before
declaring done** — that's how the multiplayer global-chat client was checked
(0 errors). Never claim a verification you didn't actually run.

## Build & run

All commands run from `WarsOfLibertyLauncher/`.

| Goal | Command |
| --- | --- |
| Dev build (framework-dependent, needs .NET 8 runtime) | `dotnet build -c Release` |
| Release single-file `.exe` (publish + sign + print SHA-256) | `.\build-release.ps1` (PowerShell, Windows-only) |
| Manual publish | `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish` |
| CI release build (unsigned → SignPath) | push a `vX.Y.Z` tag, or run `.github/workflows/release.yml` manually |

- Dev build output: `bin/Release/net8.0-windows/Aoe3ModLauncher.exe`.
- Release output: `publish/Aoe3ModLauncher.exe` (~190 MB, self-contained). The
  `publish/` folder is git-ignored — release binaries go to GitHub Releases.
  **Single-file compression is OFF on purpose** (`EnableCompressionInSingleFile=false`
  in the `.csproj`, ~line 50): the self-extracting decompression was the #1
  trigger for Defender's `Win32/Injector` packer heuristic, which quarantined the
  `.exe`. That's why the binary is ~190 MB instead of the old ~120-130 MB.
  **Re-enable compression (`true`, recovers ~70 MB) only once the binary is signed
  by a REAL trusted cert (SignPath)** — a trust-valid signature suppresses the
  packer FP; the self-signed `CN=Gorgorito` cert does not.
- `--update-now` is a launch argument that auto-resumes the update flow elevated
  (used after a UAC relaunch).
- **`build-release.ps1 -Version` accepts a WoL-style LETTER suffix and splits it
  across two assembly attributes — release builds MUST pass `-Version`.** The
  input is `MAJOR.MINOR.PATCH[LETTER]` validated by the regex
  `^\d+\.\d+\.\d+[A-Za-z]?$` (so `1.0.5` or a single trailing letter like `1.0.5a`;
  the `?` allows at most ONE letter, so `1.0.5abc` is rejected by validation — only the
  numeric-core *stripping* step uses `[A-Za-z]+$`).
  Because `System.Version` is integers-only, the script publishes the **numeric
  core** (`1.0.5a` → `1.0.5`) into `-p:Version` / `-p:FileVersion` /
  `-p:AssemblyVersion`, and the **full string with the letter** into
  `-p:InformationalVersion` (`1.0.5a`). The self-updater reads that informational
  attribute (`LauncherUpdateService.CurrentInformationalTag`) to recognise its own
  letter-versioned binary — see the self-update bullet. So a release with a letter
  suffix that ships only the numeric AssemblyVersion (or omits `-Version`
  entirely) breaks both the "don't update backwards" guard and self-recognition.
- Two publish scripts exist: `WarsOfLibertyLauncher/build-release.ps1` is the
  canonical one (cleans, publishes, signs, verifies the signature, prints
  SHA-256). The root `publish.ps1` is now a thin **wrapper** around it — it
  forwards `-Version`/`-Configuration`/`-Runtime` to `build-release.ps1` (single
  source of truth for the build/sign/hash pipeline) and, on a successful build,
  optionally creates the **local** `vX.Y.Z` git tag via `-Tag` (which requires
  `-Version`; it never pushes, and a pre-existing tag / non-git checkout is a
  soft warning, not a failure). Because it delegates, its output now lands at
  `WarsOfLibertyLauncher/publish/Aoe3ModLauncher.exe` (the canonical location),
  NOT a separate root `publish/`. It used to be a standalone copy that looked for
  a stale `WarsOfLibertyLauncher.exe` (the `<AssemblyName>` rename made it
  `Aoe3ModLauncher.exe`), so its success output never printed — that drift is
  the reason it was collapsed into a wrapper.
- **CI release builds run in GitHub Actions, NOT locally — this is a SignPath
  requirement.** `.github/workflows/release.yml` builds the same self-contained
  single-file `Aoe3ModLauncher.exe` on a `windows-latest` runner (runs the unit
  tests first), but UNSIGNED: it passes `-p:SignOutput=false` so the `.csproj`'s
  local `CN=Gorgorito` Authenticode targets are skipped. SignPath Foundation (free
  OSS code signing) only signs binaries built in CI on GitHub-hosted runners and
  origin-verified — a locally built/signed `.exe` is not accepted — so the release
  artifact must come from here, not `build-release.ps1` (which stays the LOCAL,
  self-signed path for ad-hoc builds). Triggers: a `v*` tag push or manual
  `workflow_dispatch` (with an optional version input; a tagged build derives the
  SemVer from the tag, same contract as `build-release.ps1 -Version`). The
  downstream `sign` job is auto-skipped (`if: vars.SIGNPATH_ORGANIZATION_ID != ''`)
  until the SignPath project is approved and the repo variables/secret it documents
  are set, so the pipeline stays green pre-approval and the `build` job alone
  produces the verifiable unsigned artifact SignPath reviews. Application
  progress: CI (the big one) ✅, privacy policy (`PRIVACY.md`) + wired telemetry
  opt-out ✅, code-signing policy (`CODE_SIGNING_POLICY.md`, linked from the README
  home page with the verbatim SignPath attribution) ✅ — the remaining gaps are
  operational, not code: enable MFA on GitHub/SignPath for all team members (the
  policy doc already asserts it), and confirm the lobby backend repo is public OSS.

### Tests & verification

There is now a **minimal unit-test project** — `WarsOfLibertyLauncher.Tests/`
(xUnit; a SIBLING of the main project so the main project's compile globs don't
pick it up). It targets `net8.0-windows` + `UseWPF` because the testable logic
lives in the WPF assembly (`Aoe3ModLauncher.dll`), so the test host needs the
WindowsDesktop runtime to load those types. It covers **pure logic with no UI
dependency** — the first test pins `LauncherConfig.GetSiblingInstallPaths` (the
stock-game sibling-exclusion regression in the install-gate gotcha below). Run:
`dotnet test WarsOfLibertyLauncher.Tests` (Windows-only). Coverage is
deliberately small; **add a test when you fix a pure-logic bug** rather than
chasing UI coverage. Beyond the install-gate test, the suite now pins:
`VerifyServiceTests` (per-file overlay/engine verification, covered-file
snapshot hashing, deterministic ordering — see the file-verify gotcha),
`ManifestRecognitionTests` (`UpdateService.RecognizeFromManifestData` baseline
vs migration paths + `ResolveVersionInfo` — see the manifest-recognition
gotcha), `DiagnosticLogTests` (`ExportBundle` includes `*.log`/`*snapshot*`,
**excludes the config**, overwrites a stale zip), and `LauncherUpdateServiceTests`
(letter-version comparison + the informational-tag self-recognition fallback).

Everything UI / install-pipeline still needs a **manual smoke test on Windows**.
Two cheap gates beyond a green build:
- **Smoke-launch** — a green build does NOT prove the app starts: a
  `{StaticResource}` that fails to resolve throws at *runtime*, not compile (this
  bit us once with `RadiusMd`). Run `dotnet
  bin/Release/net8.0-windows/Aoe3ModLauncher.dll` for ~10 s — it stays up
  (timeout-kill = OK) or prints the unhandled exception + stack. The `.exe` needs
  UAC, so run the `.dll` via the dotnet host to capture startup crashes from a
  plain (non-elevated) shell.
- **Install** — the installer can produce a broken-but-"successful" result
  (missing base game), so a real install needs an actual AoE3 + payload download;
  the integrity gate (below) is the in-process backstop.

## Important gotchas

- **AssemblyName ≠ RootNamespace, on purpose.** The shipped binary is
  `Aoe3ModLauncher.exe` (`<AssemblyName>`), but every file's namespace and
  `using` is `WarsOfLibertyLauncher` (`<RootNamespace>`). This mismatch is
  intentional — do not "fix" it by renaming namespaces.

- **The README's multiplayer story is aspirational, and the original CLAUDE.md
  wording was itself stale — here is the verified reality.** The README describes
  P2P UDP hole-punching, STUN, and a WinDivert virtual LAN; **none of that code
  exists** (no `PeerMesh`/`VirtualLanService`/`WinDivertNative`). Game traffic
  rides **user-managed Radmin VPN** (its 26.0.0.0/8 LAN; AoE3's stock LAN
  discovery finds peers). The launcher only *assists* with Radmin — detect /
  install / launch its GUI and copy the network name to the clipboard for manual
  paste; it **cannot join a network programmatically**. It DOES detect current
  network membership by parsing Radmin's own
  `%PROGRAMDATA%\Famatech\Radmin VPN\service.log` **plus every rotated
  backup** `service (N).log` in that directory (English, tab-delimited,
  stable across Radmin VPN 2.x) for `UPDATE\tYou joined/left network 'X'`
  events — that's how `RadminAssistantService.ProbeAsync` promotes its overlay
  checklist from `LoggedIn` → `InAoE3Network`. Reading only `service.log`
  silently fails the morning Radmin rotates the file at ~1 MB (the live log
  starts empty even though the user is still session-tracked in a network);
  `RadminLogService` enumerates `service*.log` in the directory, sorts by
  `LastWriteTimeUtc` ascending so newer events overwrite older ones in the
  same dict, and combines the result. An ICMP ping to a known seed peer is
  the fallback signal when no log file is readable (deleted, ACL'd, sandboxed
  account) (`Services/RadminVpnService.cs`, `RadminAssistantService.cs`,
  `RadminLogService.cs`). The launcher is
  the *meta layer* (sign-in, lobbies, chat, mod-hash gating) over a **self-hosted
  Node/Fastify backend at `wol-lobby.duckdns.org`** — **not** a Cloudflare
  Worker. Sign-in is **Discord OAuth** (a state flow shaped like device flow),
  **not** GitHub, yielding a JWT cached in `launcher-config.json`. Match-history/
  ELO (`ReportMatchAsync`) and replay upload (`UploadAsync`) are scaffolded but
  have **no live caller**. Authoritative source: the `MultiplayerSession.cs`
  class doc-comment + `LobbyApiClient.cs`. Scattered `WinDivert` / `PeerMesh` /
  `n2n` / `ZeroTier` mentions are historical comments. **Trust the code over both
  the README and stale comments here.**

- **Single-file publish deliberately omits `IncludeAllContentForSelfExtract`.**
  Turning it on would point `AppContext.BaseDirectory` at a `%TEMP%` extract dir.
  **(Update: user data — config, log, snapshot, telemetry — now lives in
  `%LocalAppData%\AoE3ModLauncher\` via `Services/AppPaths.cs`, NOT next to the
  `.exe`, so it no longer depends on BaseDirectory; see the AppPaths bullet under
  Runtime conventions.)** The flag stays omitted anyway — it's unnecessary and the
  `%TEMP%` indirection is still undesirable. See the long comment in
  `WarsOfLibertyLauncher.csproj`.

- **Code signing is automatic but optional.** MSBuild `AfterBuild`/`AfterPublish`
  targets Authenticode-sign the binary with a self-signed `CN=Gorgorito` cert
  (thumbprint in `<SignCertThumbprint>`). They are Windows-only and silently
  no-op if the cert isn't present, so builds still succeed without it. **The
  full regenerate recipe lives in the `.csproj` comment right above
  `<SignCertThumbprint>`** — read it before touching the cert. Three gotchas it
  encodes: (1) **a stale thumbprint silently no-ops signing** (the build's
  `Get-Item Cert:\CurrentUser\My\<thumb>` finds nothing → "skipping"), so always
  repaste `$cert.Thumbprint` into the `.csproj` after regenerating — this exact
  drift shipped unsigned `.exe`s until it was caught. (2) Adding the cert to
  **`CurrentUser\Root` CANNOT be done non-interactively** — it pops a Windows
  "Security Warning" that needs a human "Yes" click (`Import-Certificate` errors
  "UI is not allowed in this operation"; the raw `X509Store.Add` just *hangs* on
  the dialog), so the Root-trust step must run in an interactive shell, never
  from CI/headless. Without Root the file is still signed (`Signer=CN=Gorgorito`,
  timestamped) but `Get-AuthenticodeSignature` reports `UnknownError` (untrusted
  chain), not `Valid`; `TrustedPublisher` + `My` add silently. (3) Keep the
  cert's **Subject = `CN=Gorgorito`** across regenerations — the launcher
  self-update's authenticity check is *same-signer by Subject*, not thumbprint
  (`LauncherUpdateService`), so a new key with the same Subject preserves
  self-update continuity. (A self-signed cert never satisfies SmartScreen on
  *other* machines — that's expected; the trust only matters on the build
  machine and for the self-update Subject match.) **CI skips this signing
  entirely:** the GitHub Actions release pipeline passes `-p:SignOutput=false`
  (added to both `Sign*` targets' `Condition`), producing an UNSIGNED `.exe` that
  SignPath signs downstream — see the CI bullet under *Build & run*. Omitting the
  flag (every local build) keeps the self-signed behaviour described above.

- **`third_party/**` and `native/**` are excluded from compile** in the
  `.csproj`. Those dirs don't currently exist; the excludes are defensive guards
  against duplicate-attribute build errors if vendored native code is re-added.

- **`NativeInstallService` is the only live install path** (download multi-part
  ZIP → clone → flatten → overlay). `InstallerService` is a **thin vestige** of a
  legacy Inno-Setup flow (run a setup `.exe` silently): its Inno-Setup methods have
  been **removed** — the class now holds only `TryCleanupTemp` (temp-dir sweep) +
  `IsPaused` (pause flag) + `TempDirectory`, which are all the UI still uses. Don't
  reintroduce the Inno methods; do install work through `NativeInstallService`. (Its
  old companion `InstallProgressMonitor` was removed once nothing referenced it.)

- **`NativeInstallService.RemoveStaleBuildArtifacts` is now a deliberate NO-OP — the
  launcher installs the WoL payload byte-faithfully and strips NOTHING.** It used to
  delete "dev-leftover junk" (`.bak`, stray `.rar` under `art\`, `(enhanced).wav` under
  `Sound\WoL\`, `cópia`/extensionless `data\tactics\` orphans) AND the whole
  `art\WoL\interns\` subtree AND, earlier still, every `.xml.xmb`. Every one of those
  removals turned out to be the SAME bug repeated: an on-disk diff of a launcher install
  vs a clean canonical 1.2.0d install (Inno installer + Java updater → what an
  original-installer peer actually has) showed the canonical **ships all of them**, and
  the `WolPayload.zip` snapshot **contains all of them** (e.g. 986 `interns` entries in
  the zip's central directory). So every sweep made the launcher the odd one out vs the
  community — the classic inversion. Two parts of the saga:
  (1) **`.xml.xmb`**: AoE3 hashes them for its LAN version match; the official build
  ships them and never regenerates, so deleting ours → version-mismatch / OOS.
  (2) **`art\WoL\interns\`** (~981 files, ~177 MB): the GAME REFERENCES it —
  `data\protoy.xml` 97×, `data\techtreey.xml` 50× (plus `data\abilities\powers.xml`)
  point at unit models (`.gr2`), icons/portraits (`.ddt`) there (e.g. Outlaw units
  Cuchillero/Gavillero/Malevo, zipa_dh batidor/ordenanza/aduanero) — so deleting it
  left those units with missing art.
  The remaining `.bak`/`.rar`/`(enhanced).wav`/`data\tactics` files are inert (no
  proto/techtree references them) so removing them never broke gameplay, BUT they are
  STILL present in a canonical peer's install, so stripping them still diverged the
  file set. The maintainer's explicit call: **be 100% byte-faithful — keep everything
  the payload ships.** Sim/MP was never at risk (the 209 `.xml.xmb` + 158 `data\*.xml`
  are byte-identical between launcher and canonical, no OOS); the goal is a launcher
  install that is an exact **superset** of a canonical one (canonical's full file set +
  the launcher's own 12 bookkeeping/payload extras: `install-manifest.json`, the
  `translations\_originals\` backups, `etc\*_delete.lst`, `sarna.xml`). The method is
  **kept as a documented no-op** (not deleted) because it's still invoked from three
  call sites — install (`InstallAsync` Phase 5b), post-update (`UpdateService`), and
  startup self-heal (`MainWindow`) — and is the single home for the "strip nothing"
  policy if it ever needs to change. Pinned by
  `WarsOfLibertyLauncher.Tests/InstallParityTests` (`RemoveStaleBuildArtifacts_IsNoOp_KeepsEveryFile`).

- **Patch `deleteList`s are install-RELATIVE paths, NOT URLs — and the snapshot
  install bypasses them, so the payload must be pre-cleaned.** Each `<download>` in
  `UpdateInfo.xml` can carry `deleteList="etc\<name>_delete.lst"`: a path to a text
  file the patch's own `.tar.xz` extracts into the install, one relative path per line
  to delete. The original Java updater reads it locally and deletes those files.
  `UpdateService` used to treat `dl.DeleteList` as a URL (`DownloadStringAsync`), which
  silently failed (caught) for every real WoL patch, so patch deletions never applied.
  Fixed: a `http(s)://` value is still downloaded, otherwise it's read locally via
  `ArchiveService.ReadLocalDeleteList` (`Path.Combine(InstallPath, dl.DeleteList)`) and
  applied with `ApplyDeleteList`. **Caveat:** this only runs during incremental
  patching. The normal install lays down a pre-built `WolPayload.zip` snapshot already
  newer than the latest `UpdateInfo` version, so NO patch — and no delete-list — runs;
  any file the patch chain removed must already be absent from the payload. A diff of a
  launcher install vs a canonical one found ~158 such stray files (e.g.
  `data\homecityhabsburgs.xml`, `art\War of the Triple Alliance\*`, `Sound\WOLConsulate*`)
  that the official `etc\*_delete.lst` remove — so the payload must be (re)built from a
  properly-patched install. Do NOT "fix" this by blindly applying all shipped
  `etc\*_delete.lst` to the final snapshot: ~11 of those entries (e.g.
  `data\tactics\*.tactics` like `sarna`/`batidor`) were re-added by a later patch and ARE
  present in the canonical build, so a blind sweep would wrongly delete them → new OOS.
  Pinned by `WarsOfLibertyLauncher.Tests/InstallParityTests`. (When a patch's
  delete-list DOES remove files during incremental patching, the manifest's per-file
  hashes for them must be pruned — `PruneMissingHashes`; see the manifest-recognition
  bullet.)

- **The full-clone install has an integrity GATE — `InstallAsync` aborts loudly
  if the AoE3 clone copied 0 files, and `GetSiblingInstallPaths` MUST skip the
  stock game.** A real, nasty bug lived here: the installer clones AoE3 (then
  `FlattenBinSubfolder` moves `bin\` → the mod root) and EXCLUDES every *other*
  mod's install path so it doesn't scoop a sibling mod into the new install
  (`LauncherConfig.GetSiblingInstallPaths`). That list iterated **all**
  `ModRegistry.All` profiles — including the stock base game `aoe3-tad`
  (`IsStockGame`), whose `InstallPath` is the user's real AoE3 = `…\Age Of
  Empires 3\bin`. Excluding `bin` made the clone copy **0 base files**, so the mod
  shipped with no engine DLLs (`RockallDLL`/`binkw32`/`granny2`/`deformerdlly`) or
  `data\*.xml` and the game exited instantly on launch — yet the install reported
  *success* (the generic `VerifyInstallation` layer only checked that the mod
  payload landed, not that the base was cloned — fix (3) below closes that gap;
  non-WoL/`GitHubReleases` mods got only that layer). The bug was **latent**: it
  armed the moment the stock game got
  detected (which persists `aoe3-tad → …\bin` into the config's `mods` dict).
  Three fixes, all load-bearing: (1) `GetSiblingInstallPaths` now `continue`s on
  `p.IsStockGame` — the base game is what we CLONE, never a sibling to exclude
  (pinned by `WarsOfLibertyLauncher.Tests`); (2) `CloneAsync` returns the copied
  count and `InstallAsync` throws `InstallBaseGameMissingException` when it's 0,
  BEFORE the overlay / registry / shortcuts, so a misconfigured clone fails fast
  with a clear, localized error (`StatusInstallBaseMissing`) instead of producing
  a silent broken install; (3) `VerifyInstallation`'s GENERIC layer (not just the
  WoL-specific one) now checks the three `data\` version-key files
  (`protoy/techtreey/stringtabley.xml`) for `WolPatcher` / `GitHubReleases`
  installs, so a PARTIAL clone (copied SOME files but no `data\`, which slips
  past the 0-file gate) is reported as "incomplete" instead of a silent success —
  this is what closes the gap for non-WoL/`GitHubReleases` mods that previously
  got only the payload-landed check. Don't re-add the stock game to any cross-mod
  file/exclusion loop, don't drop the clone-count gate, and keep the base-file
  check generic (it's the backstop that protects non-WoL mods). The clone-count
  check ALSO runs as a **pre-flight** (`FolderCloneService.CountCloneableFiles`,
  a dry-run enumeration sharing the exact exclusion set via the extracted
  `BuildExcludedSubtrees`) at the very START of `InstallAsync` — BEFORE the
  multi-GB payload download — so a misconfigured source/exclusion fails fast
  instead of after a long download; the post-clone gate stays as a backstop.
  `BuildExcludedSubtrees` also **drops + warns** on any exclusion that is (or is
  a parent of) the clone source itself (it would empty the whole clone — never
  legitimate). (Unrelated: `SharpCompress` stays at the tested 0.37.2 with
  advisory `GHSA-6c8g-7p36-r338` suppressed via `NuGetAuditSuppress` in the
  `.csproj` — it's unpatched in every release incl. the latest, and the launcher
  only extracts its OWN CRC32-verified `.tar.xz`, so the malicious-archive vector
  doesn't apply; the suppress is targeted so NuGet audit still flags any future
  vuln.)

- **"Verify files" is a real per-file integrity pass (`Services/VerifyService.cs`),
  and Repair VERIFIES FIRST then re-lays the WHOLE mod overlay when anything is
  damaged (it is NOT granular — that was reverted; see "picks a path" below). Two
  rules are load-bearing: overlay-vs-engine separation, and
  covered-file snapshot hashing.** Installs from this build forward stamp per-file
  fingerprints into the manifest (see the `InstallManifest` bullet), so verify can
  name the exact damaged files instead of a blind spot-check (and decide whether a
  repair needs to run at all). `VerifyService` is static + testable (`VerifyServiceTests`) and
  **VERIFY-ONLY** — the repair lives in `MainWindow`. Its API: `VerifyAgainstManifest`
  (the overlay map, hashed in parallel ≤4 threads, **size-first then SHA-256**,
  results sorted `Ordinal` so the parallel order is deterministic) and
  `VerifyEngineFiles` (a SEPARATE list), plus shared helpers
  `HasFileHashes`/`HasEngineHashes`/`BuildCoveredSet`/`OriginalsFolderOf`/
  `ResolveHashTarget`/`ComputeFingerprintOf` (also used by
  `NativeInstallService.RecaptureHashes`). Results are records
  `VerifyResult(MissingItems, CorruptItems, TotalFilesChecked)` +
  `VerifyProgress(Done, Total, CurrentFile, BytesDone, BytesTotal)`.
  **(1) Overlay vs engine is a hard split.** Overlay files (the mod payload) are
  repairable by re-copying from the payload ZIP; base-game **engine** files (cloned
  from AoE3 — the curated `EngineCandidates`: the 3 `data\` version-key files +
  `RockallDLL.dll`/`binkw32.dll`/`granny2.dll`/`deformerdlly.dll`) are NOT — a
  corrupt engine file is reported with the `VerifyEngineSuffix` ("reinstall AoE3")
  string and is **never** routed into the repair set. `ComputeEngineHashes` skips
  any overlay-owned file so a file is never in both maps; don't merge them, or a
  corrupt WoL data file would be mislabelled an unrepairable engine file.
  **(2) Covered files verify against the English snapshot, not the live file.**
  Translation overlays (e.g. `data\stringtabley.xml`) are hashed against the
  `translations\_originals\` snapshot via `ResolveHashTarget` — the SAME
  canonical-English read-side trick `ModHashService` and version detection use —
  so an applied translation doesn't false-flag as corrupt; with no snapshot the
  file's existence is confirmed but the hash is skipped (not counted in
  `TotalFilesChecked`). **Repair (`MainWindow.RepairInstallAsync`) picks a path
  (the maintainer's explicit model: "reinstale todo junto con las
  actualizaciones"):** a PLAIN repair (not an update, not a version switch, manifest
  `HasFileHashes`) **verifies first**, then — **(a) intact** (no damaged/missing
  files): SKIP the multi-GB download entirely (don't re-lay), show
  `StatusRepairNothing`, but **don't `return`** — fall through so the pending-update
  continuation still runs; **(b) damaged**: re-lay the **WHOLE** overlay via
  `InstallModOnlyAsync` (NOT a granular per-file copy — the old granular branch and
  `NativeInstallService.RepairFilesAsync` were REMOVED) and do a STRUCTURAL recheck
  only (`hashPass:false` — a per-file hash over a multi-GB freshly-written install
  re-reads everything and looks frozen). The **full re-overlay** is also the path for
  an update, a version switch, or an old manifest with no `FileHashes`.
  `InstallModOnlyAsync` downloads the full ZIP (the host has no per-file URLs),
  **rewrites the manifest**, and runs `ApplyUpdateDeletions` (GitHubReleases) / the
  delete-list strip. **Updates ride along automatically:** because a plain repair
  keeps `asUpdate == false`, BOTH the intact and re-overlay paths land in the
  post-`finally` `else if (updated)` branch → `CheckAsync()` →
  `if (_pendingDownloads.Count > 0) ApplyUpdateWithElevationCheckAsync()`, so a WoL
  repair that re-laid the base snapshot (or an intact-but-behind install) continues
  straight into the pending patches. **Scope caveat:** verify + repair cover only the
  mod OVERLAY — base-game **engine** files (the AoE3 clone) are NOT in the verify set
  and are NOT re-laid by repair (no AoE3 re-clone), so an install broken ONLY by a
  corrupt engine file verifies as intact and skips; a corrupt engine file surfaced by
  an explicit "Verify files" still reports `VerifyEngineSuffix` ("reinstall AoE3") and
  is never repairable here. The gear "Verify files" item
  is **cancellable** with live progress (current file, bytes, speed via
  `SpeedTracker`, ETA), disabled for `IsStockGame`; an old install with no
  `FileHashes` emits no per-file ticks and degrades to the legacy structural
  spot-check. Strings: `StatusRevalidating`, `StatusRepairingFiles`,
  `StatusRepairNothing`, `VerifyEngineSuffix`.
  **Repair's progress UI MUST mirror `InstallAsync`'s — two ways it drifted and
  both froze/confused the dashboard strip.** `RepairInstallAsync` reports through
  the SAME `ProgressPanelControl` the dashboard hero strip mirrors
  (`SyncDashboardProgressFromLegacyPanel`: `OverallProgress` →
  `DashboardProgressBar`/`DashboardProgressPercent`, `SpeedText` →
  `DashboardProgressSpeed` under the static "VELOCIDAD" header). (1) It used to
  pass `extractProgress: null` + `overlayProgress: null` to the repair install
  calls and tie the download to a literal `p.Percentage * 0.6`, so
  the bar **froze at ~60 %** through extraction + overlay even though
  `ExtractPayloadAsync` / `CopyPayloadToDestinationAsync` already report progress
  (passing `null` just discarded it). Fix: define phase weights (DL 60 / Extract
  20 / Overlay 20) and real `extractProgress`/`overlayProgress`/`phaseProgress`
  handlers, exactly like `InstallAsync` (~7610-7640). The `phaseProgress` (even a
  minimal one that just `speed.Reset()`s + clears Speed/Eta) is load-bearing — its
  shared `SpeedTracker` would otherwise carry the download's byte history into the
  extract speed. (2) Those handlers must set `SpeedText` with the **phase-aware**
  keys (`ProgressSpeedDownload` 📡 / `ProgressSpeedExtract` 📦 / `ProgressSpeedCopy`
  💾 — the same mapping `SpeedLabelKeyForPhase` gives install), NOT the generic
  `ProgressSpeed` ("Velocidad: X/s"); otherwise the dashboard's VELOCIDAD field
  reads a bare speed and the user can't tell it's extracting (install "says it's
  doing something" precisely because it shows "📦 Extracción: X/s"). Repair knows
  each handler's phase statically, so it hardcodes the key per handler rather than
  threading `_currentInstallPhase` (install-flow state).

- **The install manifest now carries hashes, and `UpdateService` recognises the
  launcher's own byte-faithful payload FROM the manifest — keep the three maps in
  sync, and re-fingerprint after every patch (after the snapshot, after deletions).**
  `Models/InstallManifest.cs` gained three install-relative (forward-slash) maps
  plus a `FileFingerprint{size, sha256}` class: `KeyFileHashes` (Dict→**MD5** hex of
  the 3 `data\` version-key files), `FileHashes` (Dict→`FileFingerprint`, the overlay
  per-file SHA-256), and `EngineFileHashes` (Dict→`FileFingerprint`, the engine files
  — separate on purpose, see the verify bullet). Overlay hashes are captured **during
  the copy** in `CopyPayloadToDestinationAsync` (canonical **pre-translation** bytes,
  consistent with what verify compares against the `_originals` snapshot); `WriteManifest`
  gained a `fileHashes` param, prunes it, and splits it into overlay vs engine. All
  three are empty on manifests written before this feature — verify/recognition degrade
  gracefully. **Why `KeyFileHashes` exists:** the launcher installs a byte-faithful
  copy of a canonical install, whose `data\` bytes never MD5-match any `UpdateInfo.xml`
  version, so the old MD5-vs-UpdateInfo detection couldn't identify a launcher install.
  `UpdateService.DetectCurrentVersionAsync` now falls through to `RecognizeFromManifest`
  → the pure, testable `RecognizeFromManifestData` (`ManifestRecognitionTests`): the
  **baseline path** trusts the manifest's recorded `Version` **only if the 3 live MD5s
  still match `KeyFileHashes`** (drift → returns null, don't trust it); the **migration
  path** trusts a pre-baseline manifest's `Version` outright (the next Repair re-stamps
  a real baseline). `ResolveVersionInfo` maps the recognised version to its
  `MinReqDownload`, synthesising one with `MinReqDownload=0` when the payload is newer
  than every known `UpdateInfo` entry (nothing pending). **Two invariants you must not
  break:** (a) **re-fingerprint after a patch** — `UpdateService.ApplyUpdatesAsync`
  tracks the files each `.tar.xz` touched (via `ArchiveService.ExtractTarXzWithBackupAsync`,
  which now RETURNS the created+overwritten set) and, **after** the translation snapshot
  refresh (so covered files hash `_originals`) and the delete-list, calls
  `NativeInstallService.RecaptureHashes` → merges into `FileHashes`, `PruneMissingHashes`,
  recomputes `EngineFileHashes`, saves. It's wrapped non-fatal (try/catch +
  `DiagnosticLog`) — a patched install is the normal WoL state and must stay verifiable.
  (b) **`PruneMissingHashes` is mandatory after any deletion** (delete-list,
  `ApplyUpdateDeletions`): a fingerprint left for a deleted file makes verify report a
  false "missing" and granular Repair **resurrect** a file the pipeline intentionally
  stripped — inverting the strip. (See also the byte-faithful + delete-list bullets.)

- **Install detection is by CONTENT, never by folder name —
  `InstallProbeFile` + an optional `InstallMarker`, unified in
  `Services/ModInstallProbe.cs`.** The historical bug: WoL (an
  `IsolatedFolder` mod) was only recognised when its folder was literally
  named "Wars of Liberty", so renaming/moving it — or pointing the launcher at
  it via "Change mod folder" — made it read as **not installed** (the next
  `CheckAsync` re-rejected the path and wiped it). Root cause: WoL's probe file
  `data\stringtabley.xml` ALSO ships in vanilla AoE3, so the old code used the
  **leaf folder name** (must equal `DisplayName`) as a proxy for "this isn't
  vanilla". The fix replaces that proxy with a content **marker** — a file/dir
  unique to the mod and absent from the base game it clones/overlays
  (`ModProfile.InstallMarker`; WoL = `art\zulushield`, the SAME marker the
  legacy Java updater and `RegistryService.IsValidInstall` already use). The
  single rule lives in `ModInstallProbe.LooksLikeModInstall(path, profile)`:
  folder exists → probe present (if declared) → marker present (if declared) →
  true; the **folder name is never consulted**. `UpdateService` uses it two
  ways: `LooksLikeRealModInstall` (renamed from `CachedPathLeafLooksValid` — the
  marker gate layered on `IsProfileInstalled`'s probe check, applied to the
  cached/saved path AND each disk-scan candidate so a renamed install survives
  the re-check), and `IsolatedCandidates`' SECOND pass, which enumerates one
  level under the AoE3 root / its `bin\` / the parent-that-holds-AoE3-as-sibling
  and yields any child that passes the content check — so WoL is auto-detected
  in a folder with ANY name (the first pass still tries the name-based happy
  path as a fast guess). `MainWindow`'s tile-side `SavedPathLooksValid` and the
  manual-picker `LooksLikeModInstall` delegate to the same helper (the picker
  keeps its WoL Inno-registry fallback). **This is GENERIC, not WoL-specific:**
  Improvement Mod needs nothing (overlay, probe `age3m.exe` is exclusive, never
  used the name); catalog mods can declare `install.marker` in `mod.json`
  (exposed in `mod.schema.json`, documented in `docs/MODDING.md`). Declare a
  marker only when your probe file is shared with AoE3 — if the probe is already
  exclusive, it suffices alone, and the content scan needs at least one signal
  (probe or marker) or it bails (else every subfolder would match). Don't
  reintroduce a folder-name check; the marker is the anti-vanilla signal now.
  Pinned by `WarsOfLibertyLauncher.Tests/ModInstallProbeTests`.

- **Finding the BASE AoE3 install (`AoE3Detector.FindAll`) is by CONTENT too —
  the signal is the file `age3y.exe`, NOT the folder name.** `age3y.exe` is the
  exclusive executable of *The Asian Dynasties* (vanilla = `age3.exe`, WarChiefs =
  `age3x.exe`), so probing for it can't false-positive on another game and is the
  right identity check for the stock `aoe3-tad` profile (no SHA needed — a hash
  would only pin a patch/language and is fragile). `FindAll()` runs layered passes:
  hardcoded default paths per fixed drive (Steam/GOG/retail folder-name variants),
  a Steam-library pass reading `libraryfolders.vdf` (catches AoE3 on other disks),
  GOG + Microsoft-Games registry passes, all deduped via `seenFolders`. **The
  folder-name-independent pass** enumerates every `steamapps\common\*` directory of
  each Steam library (`SafeEnumerateDirectories`, swallows IO/ACL errors) and probes
  `<game>\bin\age3y.exe` / `<game>\age3y.exe` — so a RENAMED or localized AoE3
  folder is still detected (the hardcoded name list only covers the default). Don't
  drop this pass back to name-only matching. **Known gap (out of scope):** the
  Microsoft Store / Definitive Edition uses a different engine (no `age3y.exe`) and
  is incompatible with the mod fingerprint, so it's intentionally not detected.

- **The top nav tab ORDER is runtime-driven, not the XAML order.** The three
  tabs (LIBRARY / WORKSHOP / MULTIPLAYER) are declared in a fixed left-to-right
  order in `MainWindow.xaml` (`TopTabBar` StackPanel), but that's just the
  default-config order. On startup `ApplyTopTabOrder(switchToFirst: true)`
  re-parents the button children to match `LauncherConfig.TopTabOrder` (stable
  ids `"library"/"workshop"/"multiplayer"`) and opens the **first** tab in that
  order — the user's "opens on launch" choice. Users reorder via Launcher
  Settings → Interface (↑/↓ buttons). **Never read `TopTabOrder` raw** — go
  through `LauncherConfig.GetTopTabOrder()`, which sanitises a stale/corrupt/
  hand-edited value (drops unknown ids, de-dupes, appends any missing canonical
  tab) so a bad config can't permanently hide a tab. After a Settings save the
  bar re-orders via `ApplyTopTabOrder(switchToFirst: false)` (reorder only — it
  does NOT yank the user off their current tab; "first opens" is a launch-time
  rule only).

- **MainWindow's title bar + nav strip are deliberately ONE seamless surface.**
  The custom title bar (`Grid.Row=0`) and the nav-tab strip (`Grid.Row=1`)
  both fill with the **same** `BgSidebar` brush, and the title bar has **no
  bottom border on purpose** — adding one draws a visible seam through what's
  meant to read as a single continuous chrome block from the window top down
  to the tabs. The only border in that region is the nav strip's own
  `BorderThickness="0,0,0,1"`, which delimits chrome from content below. Don't
  "fix" the title bar by giving it a divider — the missing border is the
  feature. Two related guards keep the **content below** from reading as if it
  invades this chrome: (1) the content host (`Grid.Row=2`) is
  `ClipToBounds="True"` so nothing in any tab — the PlayView full-bleed
  background image, the hero gradients, the scaled hero block — can render *up*
  over the title bar / nav strip; it's defensive (clips content only, never the
  chrome, so the one-surface look is intact) and on its own a near-no-op,
  because the image is a `Border` background and is already clipped to its
  bounds. (2) The dashboard hero is a **single-layer brush stretched
  `Stretch.Fill`** by `DashboardBgFill`'s background — no blur, no scale, no
  overlay, **no aspect-ratio preservation**. It fills the whole panel
  edge-to-edge with no side bands and no crop, at the cost of **distorting the
  hero's aspect ratio** in a non-16:9 window (squished/stretched horizontally).
  The maintainer reached this by elimination, rejecting in order: (a) the
  two-layer "image fits + blurred margins" design — perceived as visible side
  bands ("franjas"); (b) a single-layer `UniformToFill` cover that crops a
  slice off top/bottom — disliked the crop. Stretch-to-fill (accept the
  distortion) was the explicit ask. Other historical rejects: a blur fill
  **with a dark scrim** read as "black borders"; `AlignmentY=Top` only moved
  the crop to the bottom. **Implementation gotcha:** the hero brush is now
  built by `MainWindow.BuildHeroFillBrush` (NOT the shared `TryLoadTileImage`,
  whose cached brush is `UniformToFill` and SHARED with the icon/tile paths —
  don't change its stretch globally or every mod icon distorts). `BuildHeroFillBrush`
  makes a fresh `Stretch.Fill` `ImageBrush` with `IgnoreImageCache` and a
  **decode cap** (`HeroDecodeWidth=2560`, applied only when the source is wider,
  so 1080p/1440p decode native and a 4K hero downscales — `HighQuality` scaling
  on the host Borders keeps it crisp; keeps a 4K hero off ~33 MB of RAM).
  Fallback when a profile ships no hero is a neutral dark gradient. **There are
  now TWO stacked Border layers** (`DashboardBgFill` = base, `DashboardBgFillB`
  = crossfade overlay, opacity 0 at rest), BEFORE the dim-gradient Rectangles —
  both owned by `ApplyDashboardHero`. This is the **rotating-hero** feature: a
  catalog `mod.json` may declare `heroImages` (2–6, each 16:9/≤5 MB, cached as
  `{modId}-hero-{i}` by `ModAssetCacheService.GetHeroImagePathsAsync`); with ≥2
  the dashboard cycles them every `HeroRotateSeconds` (7) by painting the next
  into `DashboardBgFillB` and fading its opacity in, then snapping the base. The
  `_heroRotateTimer` runs ONLY while `PlayView.IsVisible` (see
  `UpdateHeroRotationTimer`, hooked to `PlayView.IsVisibleChanged`) and is reset
  on every `RefreshActiveModBanner` (mod switch). A single `heroImage` (or none)
  never starts the timer — byte-for-byte the old static behaviour. Effective
  list = `LocalHeroImagePaths` (rotating) → single `LocalHeroImagePath` →
  `LocalBannerPath` → gradient. **Don't add a dark scrim back** (→ "black
  borders" again), and keep `DashboardBgFillB` BEFORE the readability gradients
  or the overlay covers them.

- **The title-bar brand button's hover illumination has a WPF-precedence
  trap — we hit it as a bug twice.** `TitleBarBrandButton` ("AoE3 Mod
  Launcher ▾") brightens on hover/press/open like the nav tabs: text idle
  = `Secondary`, hover = `#E6EEF8`, pressed/open = `#FFFFFF` white. For that
  to work, the idle `Foreground` **must be a `Style` setter (or template
  default), never a local `Foreground="…"` attribute on the `<Button>`** —
  a local value (precedence 3) beats `ControlTemplate.Triggers` (4-6), so a
  local Foreground silently kills the hover/press/open colour. Equally, the
  `ContentPresenter` in the template must stay **default** (no explicit
  `TextElement.Foreground`): a `ContentControl`'s ContentPresenter
  auto-propagates the templated parent's `Foreground` to the content text,
  so flipping the Button's `Foreground` in a trigger flows to the wordmark;
  setting `TextElement.Foreground` directly on the ContentPresenter does
  **not** propagate (that looked dead). The icon next to it is a *bitmap*,
  not a glyph, so it can't follow `Foreground` — it illuminates via
  `Opacity` (0.7 → 1.0) on an `Image.Style` whose `DataTrigger`s bind to the
  ancestor button's `IsMouseOver`/`Tag` (the Image lives in the button
  CONTENT, out of reach of template triggers). The chevron flips ▾↔▴ and
  the button holds `Tag="open"` while the brand popup lives, both set in
  `BrandMenuButton_Click` + `popup.Closed`. Same precedence rule governs
  `NavTabButton` — copy that recipe for any new chrome button, don't
  reach for a local `Foreground`. **The icon's source is `AppIcon.png`,
  NOT `AppIcon.ico`, and both are intentionally shipped as `<Resource>`
  in the .csproj — don't "consolidate" them.** WPF's ICO codec picks the
  smallest frame `>=` the requested logical size and downscales it: a
  20-logical-pixel Image painted from the .ico selects the 24×24 frame
  and stretches, which is visibly soft at every DPI. The PNG (the
  256×256 frame extracted from the .ico) routes through the PNG codec
  instead, which lets `BitmapScalingMode="HighQuality"` bicubic-downscale
  from 256 straight to the physical pixel size — crisp at 100/125/150%.
  The `.ico` stays because (a) the .exe's Windows icon
  (`<ApplicationIcon>`) needs it, and (b) the `TaskbarIcon` (system
  tray, `MainWindow.xaml` line ~1063) genuinely needs an `.ico` because
  the Windows tray uses HICON natively — switching the tray to the
  PNG would either fail or render worse. The brand-button Image is the
  only in-app surface that uses the PNG; everywhere else still uses
  the .ico. The PNG Image sets `DecodePixelWidth=256` (the native frame
  size) so the only resample is the renderer's single HighQuality bicubic
  downscale to the 20-40 physical px render size. An earlier `=64` cap
  pre-shrank in the decoder, so a non-integer 64→40 second downscale
  *softened* the icon at some DPIs (reported as "se ve en baja
  resolución"); decoding native keeps it crisp. Costs ~256 KB RAM vs
  ~16 KB — trivial, and the right trade for a sharp brand mark. The Image
  stays 20x20 (sharper, NOT bigger — the user explicitly didn't want it
  enlarged). Don't drop it back to 64 to "save memory".

- **Popup menus use a TWO-TONE "punched-out" rim — don't reduce it back to a
  single border.** The gear ContextMenu + its cascading submenu
  (`ActionPanel.xaml`'s `MoreMenu` template) and the dashboard mod-switch
  popup (`MainWindow.xaml.cs` `DashboardChangeModButton_Click`) all wrap a
  bright 2px inner border (`MenuBorder` = `#7C8794`) in a 1px near-black
  outer band (`MenuBorderOuter` = `#000000`), painted via the outer
  `Border`'s `Background` + `Padding="1"`. Together that's a 3px effective
  boundary that reads as a discrete card over **any** backdrop: the dark
  outer rim pops against bright surfaces (hero image, the lighter
  `BgSidebar` chrome), and the bright inner line pops against dark
  surfaces (`BgPanel` interior, `BgBase` content). The first attempt was a
  single brighter brush at 2px (the `MpDivider` lift recipe `#2C313A →
  #3A434F` reapplied) — the maintainer reported "yo lo veo igual", so the
  recipe escalated to two-tone. The drop shadow lives on the OUTER band so
  it skirts the whole composite rim; don't move it to the inner Border or
  the shadow gets clipped behind the black band. **Don't apply this rim to
  the brand popup** (`BuildBrandPopup`) — that one uses an `AccentBrush`
  (gold) border on purpose, which is already visually distinctive and
  marks it as the launcher's primary menu. Standard sibling popups
  (settings, mod switch, gear) get the two-tone rim; the gold brand popup
  is the deliberate exception.

- **Hand-built `Popup`s are coordinated centrally by `Controls/ChromePopups.cs`
  — don't add per-handler close logic.** The launcher's code-behind transient
  menus (the title-bar brand dropdown `BuildBrandPopup` and the dashboard MODS
  switcher `DashboardChangeModButton_Click`) are `AllowsTransparency=true` +
  `StaysOpen=false`, and WPF's auto-dismiss for that combo is **unreliable when a
  non-modal Window steals activation** — so a popup lingered behind a freshly-
  opened dialog (the reported "open MODS, click the gear, the menu stays open"
  bug). `ChromePopups` enforces a single-open invariant app-wide: each popup calls
  `ChromePopups.Track(popup)` once at construction (wires `Opened` = claim slot +
  close the previously-tracked popup → mutual exclusion, and `Closed` = release
  slot with a `ReferenceEquals` guard), and `ChromePopups.CloseOpen()` closes
  whatever is open. The **close-on-dialog-open** hook lives in
  `App.OnAnyWindowLoaded` (the same `Window.Loaded` class handler that does HiDPI
  / rounded corners / maximize-fix): for any window that is **not `MainWindow`**
  it calls `CloseOpen()` (covers a fresh dialog instance — `Loaded` fires per
  open, e.g. `ModPropertiesDialog` is rebuilt each time) AND subscribes
  `w.Activated += CloseOpen` (covers single-instance dialogs reused via
  `Activate()`). `MainWindow` is **excluded on purpose** — its own activation must
  never close a popup that legitimately lives on it (qualify the type as
  `WarsOfLibertyLauncher.MainWindow` in `App`, since bare `MainWindow` binds to
  the inherited `Application.MainWindow` property). WPF `ContextMenu`s (gear
  `MoreButton`, `ModsBrowser` menus) are NOT tracked — they capture input and
  auto-dismiss reliably on their own. A new hand-built popup only needs
  `ChromePopups.Track(popup, ownerButton)` to inherit all of this.
  **These openers are TOGGLES, but via TWO different mechanisms — don't assume
  `ConsumeToggleOff` is the toggle for every popup.** A re-click of a brand / MODS
  opener should CLOSE its popup, not reopen it — but a `StaysOpen=false` popup
  already auto-dismisses on the mouse-down that re-clicks its own opener, so by the
  time the button's `Click` fires the popup is gone and a naive "open on click"
  would immediately reopen it (the "just reopens" flicker).
  **(a) The brand popup** (`BrandMenuButton_Click`) uses the timing trick: it calls
  `ChromePopups.ConsumeToggleOff(btn)` at the TOP of its handler and returns when
  it's `true` — `Track(popup, owner)` stamps the owner+tick on `Closed`, and
  `ConsumeToggleOff` reports "a popup owned by this button was dismissed within the
  last 300 ms" (i.e. this click is the toggle-off). This depends on `Closed` firing
  (mouse-down) BEFORE `Click` (mouse-up), which is **runtime-fragile** — it failed
  for the MODS button ("solo se vuelve a abrir"). Pass the SAME stable button
  instance to both `Track` and `ConsumeToggleOff` for the owner match.
  **(b) The MODS switcher** (`DashboardChangeModButton_Click`) NO LONGER uses
  `ConsumeToggleOff` — it uses the **field-based model the gear uses**: a
  `_modSwitchPopup` field holds the live popup, the click handler returns early
  (`_modSwitchPopup.IsOpen = false`) when it's non-null, and the popup's `Closed`
  clears the field **DEFERRED at `DispatcherPriority.Background`** so the clear runs
  AFTER the opener's `Click` (Input priority beats Background). That ordering
  defeats the auto-dismiss/Click race deterministically without the 300 ms guess:
  on a re-click, mouse-down auto-dismisses + QUEUES the clear, then Click fires with
  the field still non-null → closes and returns; an outside click clears the field a
  moment later so the next MODS click opens fresh. `Track(popup, btn)` is still
  called (for mutual exclusion + close-on-dialog-open); only the *toggle* moved off
  `ConsumeToggleOff`. **The brand popup is a candidate for the same field-based fix**
  if it ever toggles wrong. The **gear** is a toggle too but via a different path: it
  opens a real Window (`ModPropertiesDialog`), not a popup, so
  `DashboardSettingsButton_Click` simply does `if (_modPropertiesDialog != null)
  { _modPropertiesDialog.Close(); return; }` before `OpenModPropertiesDialog`
  (the dialog is non-modal, so the gear stays clickable behind it; `Close()`
  raises `Closed` synchronously which nulls the field + refreshes the chrome).

- **The cinema dashboard hero scales with the window — via one transform, not
  per-element font sizes.** The PlayView "Layer 4" Grid (title + description +
  version chip + action row + progress strip, `HeroContentGrid`) is scaled by
  the **shared window-size scaler** — `UiScale.Attach(HeroContentGrid, PlayView,
  1500, 760, Kind.Render, (0,1))` in `MainWindow`'s ctor (`Controls/UiScale.cs`,
  see its own bullet). The hero-private `HeroScaleTransform` / `UpdateHeroScale()`
  / `Hero*` consts are **retired** — folded into the shared scaler with the
  hero's SAME reference + floor + render-pin + crispness toggle, so the hero
  looks byte-for-byte what it did before. (Below, `HeroRefWidth`/`HeroRefHeight`/
  `HeroMinScale` describe the literals now passed to `Attach`, not consts.)
  The whole block scales as a unit so proportions stay fixed: it **adapts to the
  window size** — roughly **0.90 at a typical ~1357-wide window**, up to **1:1
  only on a large / maximized window**, and down to a floor of `HeroMinScale`
  (**0.82**) on small windows, so it neither shrinks to tiny nor blows up cramped.
  The scale is
  `min(PlayView.ActualWidth/HeroRefWidth, PlayView.ActualHeight/HeroRefHeight)`
  clamped to `[0.82, 1.0]`, where `HeroRefWidth`/`HeroRefHeight` are **fixed
  constants** (**1500x760** — deliberately a bit ABOVE a typical window's content
  area, so the hero keeps adapting across normal window sizes instead of capping
  at 1:1). `PlayView`'s ActualWidth/Height already exclude the 96 px chrome (title
  bar `Grid.Row=0`=40 + nav strip `Grid.Row=1`=56), so there is **no `- 96` in the
  code**; if you change those row heights, re-tune `HeroRefHeight`. **Two
  load-bearing tuning lessons:** (a) the FLOOR (`HeroMinScale`) controls how small
  the hero gets on small windows — raise it (it went 0.65→0.82) if it looks tiny
  there; (b) the REFERENCE controls when it caps at 1:1 and MUST stay above
  typical window widths, or the adjust dies (dropping it to ~1080 once made every
  normal window cap at 1:1, so the hero stopped adapting and looked oversized —
  the wrong lever; the fix for "too small on a small window" is the floor, not the
  reference). Three deliberate, non-obvious
  choices: (1) it's a **`RenderTransform` (not `LayoutTransform`) with
  `RenderTransformOrigin="0,1"`** so the block shrinks in place pinned to its
  bottom-left corner without reflowing or nudging the gradient/background layers
  behind it; (2) it's hooked to **`PlayView.SizeChanged`, not the window's** —
  switching tabs collapses PlayView to a 0-size (guarded no-op) and switching
  back grows it from 0 to its real size, a size change that recomputes the scale
  even though the window never resized (the window's own `SizeChanged` would
  miss that); (3) it caps at 1.0 so it never grows *past* the maximized look on
  huge monitors. Don't add per-XAML `FontSize` scaling, a `Viewbox`, or DPI
  tweaks to make the hero responsive — route everything through the shared
  `UiScale` scaler (`Kind.Render` for the hero). **Text crispness is coupled to
  the global HiDPI setup:**
  `App.OnStartup` renders all text in `TextFormattingMode=Display` (pixel-
  snapped) + `Fixed` hinting + `ClearType` — razor-sharp at 1:1 but **blurry
  once the scale transform shrinks the glyphs** (they're rasterised for the
  pre-transform pixel grid, then squashed). So the scaler
  (`UiScale.SetTextCrispForScale`) flips the `HeroContentGrid` subtree to `Ideal`
  formatting + `Animated` hinting + `Grayscale` rendering whenever scale < 1.0
  (WPF's mode for text under a transform), and restores the
  `Display`/`ClearType`/`Fixed` trio at exactly 1.0 so the maximized hero stays
  pixel-crisp. Don't hard-set static `TextOptions`
  on the hero subtree — the toggle owns them. (The reference is now a fixed
  window-content footprint, not the monitor's work area, so maximizing on any
  monitor lands at 1.0 — the old multi-monitor "secondary screen won't hit 1.0"
  edge case is gone.)

- **`LauncherConfig` is per-mod.** Real state lives in a `mods` dictionary of
  `ModState` keyed by mod id and selected by `activeModId`; the flat
  `modInstallPath` / `gameExecutable` / `activeTranslationId` fields are LEGACY,
  migrated into `mods[...]` on `Load()` (which also rewrites a retired
  `multiplayer.lobbyBaseUrl` and clears the session token). The config schema +
  example live in `docs/CONFIGURATION.md` (per-mod dict, not the legacy flat
  fields). `Save()` is non-atomic and runs from background threads.

- **Multi-install copies (`ModState.OtherInstalls`) are hidden/deduped, never
  hard-deleted — a phantom "Mod (2)" in the MODS switcher was a stale registration.**
  The flat `ModState` fields ARE the active install; inactive copies live in
  `OtherInstalls` (a `ModInstall` each: `Id`/`Label`/`InstallPath`/version/pin/
  translation). Copies are registered ONLY by "install another copy"
  (`InstallAsync(addNewSlot:true)`) and rotated by `SwitchActiveInstallAsync` — the
  disk scan NEVER writes copies. Three rules keep the switcher honest: (1) **dedup on
  load** — `ModState.NormalizeInstalls` drops empty entries and any copy whose
  `ModState.PathEquals`-normalized path equals the active `InstallPath` or an
  earlier-kept copy (pure, no disk I/O); (2) **hide non-existent at render** —
  `AppendInstallCopiesToModPopup` (`MainWindow`) skips OTHER copies whose folder fails
  `Directory.Exists` and collapses the whole "Installed copies" section when no live
  extra copy remains, so a deleted-folder phantom disappears WITHOUT being purged from
  config (a disconnected external drive re-appears when reconnected); (3) **manual
  remove** — `ModState.RemoveInstall(id)` + `Save()` only forgets the registration
  (does NOT delete files — that's Uninstall; string `RemoveInstallCopy`). The
  **switcher popup (`AppendInstallCopiesToModPopup`) is a pure copy SELECTOR — every
  row just switches, it has NO ✕ remove button** (that was deliberately dropped: quick
  copy-switching and destructive housekeeping are separate concerns). Removing a
  registered copy lives ONLY in ModProperties → LOCAL FILES → "Manage installs" (the
  `Remove` button, `RemoveInstallBtn`). Registration also dedups: `BrowseButton_Click`
  ("change mod folder") drops any `OtherInstalls` entry equal to the new active path,
  and the `addNewSlot` rotation guard compares via `PathEquals` and removes a stale
  entry equal to the new active folder. **Don't hard-prune non-existent copies on load**
  (the disconnected-drive footgun) and don't reintroduce a folder-name compare — use
  `ModState.PathEquals` (`Path.GetFullPath` + `OrdinalIgnoreCase` + trimmed separators).
  Pinned by `MultiInstallModelTests`.
  **The dashboard hero shows which copy is ACTIVE via a chip next to the version chip —
  ONLY when `HasMultipleInstalls`.** `RefreshActiveCopyChip` (`MainWindow`) fills
  `DashboardCopyChip`/`DashboardCopyText` with the active copy's real folder leaf
  (`CopyDisplayLabel(null, _updateService.InstallPath)`); a single-install mod (and the stock
  game) hides it, so the hero is byte-for-byte the old look for the common case. The chip is
  a `Button` reusing `DashboardChangeModButton_Click` (opens the same copy switcher, anchored
  to the chip). It's refreshed everywhere the version chip is (the two `DashboardVersionChip`
  sites) plus after `SwitchActiveInstallAsync` so it tracks the active copy live. Tooltip
  `DashboardActiveCopyTooltip`. So the user always knows which copy PLAY will launch without
  opening the MODS popup.
  **Multiplayer ALWAYS uses the ACTIVE copy, and now SHOWS it (create-room dialog +
  lobby) + lets the host CHOOSE it — all as active-copy switches, never a per-room
  override.** Both the fingerprint that gates join (`ModHashService.FingerprintAsync`
  over `_config.GetState(id).InstallPath`) and the game launch resolve the ACTIVE copy;
  the copy was invisible in the UI (reported: "I don't know which copy MP uses"). The
  create-room dialog (`CreateLobbyDialog`) now renders a copy row (`CopyRow`/`CopyCombo`,
  Collapsed unless `HasMultipleInstalls`): `MultiplayerTab.BuildCopyInfo` builds a
  `Models.ModCopyInfo`/`ModCopyChoice` (active first, labels via
  `PathDisplay.DisambiguateLabels`, leaf via `MultiplayerTab.CopyLeaf`). The combo is
  **interactive only when the selected mod is the active dashboard mod** (`CanSwitch`);
  choosing a copy calls the new `switchActiveCopy` Attach callback →
  `MainWindow.SwitchActiveInstallAsync(installId)` (rotates the active copy — single
  source of truth) then the dialog recomputes the fingerprint so the room's required
  hash matches what will launch. For a non-active mod it's display-only with a hint
  (`MpCreateDialogCopyHintReadonly`). The lobby `RoomInfoCard` gained a `RoomCopyRow`
  showing the room mod's active-copy leaf, again only when that mod `HasMultipleInstalls`.
  **Load-bearing:** don't thread a separate chosen-copy path through fingerprint/launch —
  choosing IS an active-copy switch, so the whole existing active-copy pipeline stays the
  one source of truth (avoids a host launching a different copy than the room's hash). The
  switch runs mid-modal (`ShowDialog`), which re-renders the dashboard behind the dialog —
  intended. Strings `MpCreateDialogCopyLabel`/`MpCreateDialogCopyHintReadonly`/`MpRoomFieldCopy`.
  **Switcher LABELS are folder-leaf-derived and disambiguated at display.** A copy's
  label defaults to its install-folder leaf (`ActiveInstallLabel`/`ModInstall.Label`), and
  `MakeUniqueInstallFolder` only guarantees full-PATH uniqueness within one parent — two
  copies in different parents can share a leaf ("Wars of Liberty (2)" twice). So
  `AppendInstallCopiesToModPopup` disambiguates at render: labels colliding
  case-insensitively get the distinguishing parent folder appended
  (`PathDisplay.ParentFolderName`), and the path subtitle uses
  `PathDisplay.CompactPathMiddle` (ellipsis in the MIDDLE, keeps the distinguishing TAIL —
  WPF `TextTrimming` only trims the end and would hide it). Both helpers are pure/static in
  `Services/PathDisplay.cs` (no WPF deps → unit-testable off the UI thread, pinned by
  `PathDisplayTests`; do NOT move them back into `MainWindow`, whose static brush fields
  throw off an STA-less test thread).
  **The full copy manager lives in ModProperties → LOCAL FILES → "Manage installs".**
  `LoadManageInstalls`/`BuildInstallCard` (`ModPropertiesDialog`) render one card per install
  (active + each `OtherInstalls`) with a **read-only name = the real FOLDER name** (renaming
  was REMOVED — a custom label that doesn't match the folder is misleading; the display derives
  from the path via `DeriveLeaf`/`CopyDisplayLabel(null, path)` everywhere, ignoring any stale
  stored `Label`), path (`PathDisplay.CompactPathMiddle`), version, and Active-badge / **Switch**
  (`SwitchActiveInstallAsync`) / **Remove** (`ModState.RemoveInstall`) actions, plus
  **"Add existing folder"** (`MainWindow.AddExistingCopy` — reuses `BrowseButton_Click`'s
  picker + `ModInstallProbe` validation, then `ModState.RegisterInstall` which adopts a real
  on-disk folder WITHOUT reinstalling; this is the way to bring back a removed copy) and
  **"Install new copy"** (`InstallAsync(addNewSlot:true)`). Callbacks are threaded through the
  `ModPropertiesDialog` ctor (`switchInstall`/`removeInstall`/`addExistingFolder`).
  (`ModState.RenameInstall` remains as a dormant, unit-tested model method with no UI — don't
  re-add a rename textbox.) Names are made UNIQUE for display by
  `PathDisplay.DisambiguateLabels` (append parent folder, then a stable `#N` when copies share
  both name and parent), used by both the switcher popup and the manager. The stock game hides
  the whole section.

- **Long ops (install / update) run in the BACKGROUND — the user can switch to another
  installed mod and PLAY it while one installs. Still ONE op at a time.** The op is owned by
  `_operatingModId` (the mod the live op belongs to, set/cleared in `SetBusy` for real ops)
  and `_operatingCts` (its OWN cancellation token — Install/Update/Repair/Verify use it, and
  `CancelButton_Click` cancels IT, so a mod-switch's `CheckAsync` reassigning `_cts` can't
  cross-cancel the op). Button gating flipped from "block everything while `_isBusy`" to
  **`RefreshOperationGate`**: the visible hero buttons key off **`DisplayedModIsOperating`** —
  which is now **per-INSTALL, not per-mod**: `_isBusy && !_isCheckOnly && operating mod ==
  displayed mod && (displayed install path is empty [fresh install] OR
  `PathEquals(_operatingInstallPath, displayed install path)`)`. `_operatingInstallPath` is the
  folder the op TARGETS (set in `SetBusy` = the active install; **overridden in `InstallAsync`
  to the NEW `installFolder`** for a copy install, since during a copy install
  `_updateService.InstallPath` is still the PREVIOUS copy). So installing a NEW copy of a mod
  leaves a DIFFERENT already-installed copy of the SAME mod **playable**; only the copy being
  written is gated. Only the **primary CTA** is disabled (and only for the operating install);
  the **gear (Settings) and MODS switch are ALWAYS enabled** — Properties (settings, manage
  installs, view logs) must stay reachable during an op, and its destructive actions
  (Verify/Repair/Uninstall) self-gate on `_isBusy` while the language tab locks via `SetModBusy`.
  `RefreshOperationGate` is called from `SetBusy`, `SetPrimaryAction`, and after a mod switch.
  **Only Install/Update are backgroundable** (`_operationIsBackgroundable`, set after their
  `SetBusy(true)`): they capture `svc`/`pending`/token LOCALLY so a mid-op `_updateService`
  swap can't corrupt them or mis-attribute the completion bell (attributed to `svc`, not the
  displayed mod). **Both tails (`InstallAsync`'s auto-continue+re-check AND `ApplyAsync`'s
  re-check) are GUARDED on `svc.Profile.Id == _updateService.Profile.Id`:** if the user
  switched away, the tail skips `CheckAsync`/`MaybeAutoContinueUpdateAfterInstall` (which read
  the DISPLAYED mod) — it would otherwise patch the wrong mod — and instead drops the operating
  mod's stale cache entry; that mod's pending patches surface as an Update CTA when the user
  returns to it. Don't remove either tail guard. The shorter Repair/Verify/Uninstall re-read `_updateService`, so the switch
  guard (`LoadModProfile`) still blocks a switch during them (`&& !_operationIsBackgroundable`);
  `SwitchActiveInstallAsync` blocks only when `DisplayedModIsOperating`. **The switch does NOT
  run the network `CheckAsync` while a background op is live** (`ReloadActiveServiceAsync`
  renders the newly-displayed mod from cache / install-presence instead) — `CheckAsync` toggles
  the shared `_isBusy`/`_cts` and would disrupt the op (its existing `if (_isBusy && !_isCheckOnly)
  return;` guard already keeps it from firing mid-op). **The single progress strip ALWAYS shows
  the current op** (`SyncDashboardProgressFromLegacyPanel`, `idle = _progressState == Idle` only)
  — its title already names the mod (`ProgressTitleInstalling` = "Installing Wars of Liberty"),
  so it doubles as the "what's installing in the background" indicator on EVERY mod's dashboard;
  pause/cancel show with it, and the displayed mod's PLAY stays live via the per-install gate.
  Do NOT re-gate the strip to idle on a non-operating mod (that produced the "Ready for
  operations" label over a live 84% bar bug). **`ResetProgressUI()` is routed through
  `MaybeResetProgressUI()` (a no-op while `_operatingModId != null`) at the mod/copy-switch
  reset points — `ReloadActiveServiceAsync` AND every terminal branch of `ApplyCheckResult`**
  (which the switch-to-a-copy local refresh calls) — so a switch during a live op doesn't zero
  the running bar/speed/eta (that "progress trail lost" flicker). Op FINALLYs keep their raw
  `ResetProgressUI()` (they run after `SetBusy(false)` cleared `_operatingModId`, so they still
  reset).
  **"Install another copy" NEVER auto-switches to the new copy** (the old `addNewSlot` rotation
  that made it active is gone): when there's already an active copy, the new folder is
  `RegisterInstall`ed as INACTIVE at its snapshot version (`keptCurrentActive` +
  `keptCopyVersion`), the tail skips `CheckAsync`/auto-continue (they'd target the still-active
  OTHER copy), and it just bells "Copy installed" + `RefreshActiveModBanner`. You stay on your
  current copy (playable throughout via the per-install gate) and switch to the new one by hand
  (it shows an Update CTA to patch it). A first install with no prior active copy still becomes
  active. **This is UI/async — smoke-test on Windows:** install a copy of WoL, PLAY the active
  copy while it installs, the strip reads "Installing Wars of Liberty" even on another mod's
  dashboard, Pause/Cancel act on the install, and when the copy finishes you stay on your copy
  (the new one appears in Manage installs). Don't reintroduce a global `_isBusy` gate on the
  visible buttons.

- **`ModState.PinnedVersion` pauses update PROMPTS, it never auto-updates — and it
  self-corrects when stale.** Empty (default) = follow the latest, normal
  behaviour. When it equals the installed version, `MainWindow.IsUpdatePausedByPin`
  returns true and `ApplyCheckResult` keeps the PLAY button as **Play** (instead of
  flipping it to Update) and hides the secondary Update button — for BOTH WoL
  (`WolPatcher`, patches-pending branch) and `GitHubReleases` (the `ghPaused` split);
  status shows `StatusUpdatePausedPinned`. The user sets/clears it from the Mod
  Properties General tab (`StayOnVersionCheck`, pins to `_service.CurrentVersion.Ver`),
  and the dialog calls back `onUpdatePolicyChanged` so MainWindow re-applies the
  CACHED check result (no network). `ApplyCheckResult` is the SOLE place that raises
  the update CTA (the only `SetPrimaryAction(PrimaryAction.Update)` + the only
  `UpdateButton.Visibility = Visible`), so gating it there is complete — don't raise
  the update CTA from anywhere else. The pin only suppresses while it matches the
  installed version, so after a real update it stops matching and updates resume on
  their own (the checkbox also reads unchecked then). Nothing is ever auto-applied;
  this is purely "stop nagging me". Caveat surfaced to the user in the hint string:
  skipping updates can break multiplayer version-match with other players.

- **Offline mode is GLOBAL and the launcher stays playable with no internet — the
  update-check NEVER throws on a network error, and the cold-start UI renders PLAY
  from LOCAL state.** The launch engine + install detection are already 100% local;
  the two things that used to break offline were (1) `UpdateService.CheckAsync`'s
  WolPatcher branch fetched `UpdateInfo.xml` unguarded, so an offline throw discarded
  the locally-computed `valid` and `MainWindow.CheckAsync`'s catch only showed
  `Error: …` (PLAY stayed greyed for an installed WoL on a cold start), and (2) cold
  start had no local-first button render (unlike the mod-switch path
  `ReloadActiveServiceAsync`, which sets `_modIsInstalled` + `UpdateGameUI()` BEFORE
  the check). Both fixed: **(a)** `CheckAsync` computes `InstallPath`/`valid` locally,
  then wraps the WHOLE network core (`CheckCoreAsync`) in one try/catch — `catch
  (OperationCanceledException) { throw; }` then a generic catch that does
  `ct.ThrowIfCancellationRequested()` FIRST (the inner catch in
  `UpdateInfoService.FetchAsync` is UNFILTERED and rewraps a cancellation as
  `InvalidOperationException`, so without this guard a user-cancel reads as offline),
  reports offline, and returns `BuildOfflineResult(valid)`. This is GLOBAL: it covers
  every `ModUpdateMechanism` (the non-WolPatcher branches already short-circuit
  locally and never throw), not a WoL special-case — mirrors
  `LauncherUpdateService.CheckAsync`, which likewise never throws offline. The offline
  `CheckResult` (pure, testable `BuildOfflineResultData`) sets `CurrentVersion` NON-null
  when `valid` (cached `ModState.LastKnownVersion` → install-manifest `Version` → empty
  marker) so the UI renders PLAY not Install, `LatestVersion == CurrentVersion` (clean
  "up to date" status, not the misleading "reinstall from website"), `PendingDownloads`
  empty (no manifest ⇒ no verifiable update ⇒ don't nag), and **`Degraded = true`**.
  **`MainWindow.CheckAsync` MUST NOT cache a `Degraded` result** (`if (!result.Degraded)
  _checkResultCache[...] = result`) — else the sync cache fast-path would replay the
  stale offline result all session and never surface real updates after reconnect.
  **(b)** The `Loaded` handler sets `_modIsInstalled` from `_updateService.InstallPath`
  (resolved synchronously in the ctor) + `UpdateGameUI()` BEFORE the gated startup
  check, so PLAY is live at cold start for ANY installed mod — this also fixes the
  `CheckUpdatesOnStartup=false` case where the startup check is skipped entirely.
  **Connectivity is OBSERVED, never probed** (`Services/ConnectivityState.cs`, static):
  network code reports the outcome of calls it already makes — `ReportSuccess()` only
  from a call that actually reached the net (`UpdateInfoService.FetchAsync` success,
  `LauncherUpdateService.CheckAsync` after `SendAsync` returns — gated by a
  `reachedServer` flag so a post-response HTTP error / cancel isn't "offline",
  `ModCatalogService.FetchAsync` after the listing succeeds — the recurring call that
  clears the chip on reconnect), `ReportFailure(ex)` only when `IsNetworkError(ex)`
  (walks the inner-exception chain — the wrapper type isn't the signal, its inner
  `HttpRequestException`/`SocketException`/timeout is). A probe was rejected: a
  corporate proxy / TLS inspection / captive portal / active Radmin VPN adapter
  produce false negatives that would wrongly disable online features for an ONLINE
  user; observed never has that failure mode. **Don't report success unconditionally
  from `CheckAsync`** — the non-WolPatcher branches do NO network, so that would
  falsely read "online". UI: `MainWindow.ApplyOfflineModeUi` (subscribed to
  `ConnectivityState.OfflineChanged`, marshalled to the dispatcher) toggles a title-bar
  **offline chip** (overlay left of the notification bell, reuses the `MpStatusOffline`
  brush; clicking it re-probes via self-update + active-mod check) and greys the
  online-only controls — hides the self-update pill, and delegates to
  `ModsBrowser.SetOfflineMode` (Workshop "Actualizar") /
  `MultiplayerTab.SetOfflineMode` (sign-in/create/refresh, re-applied at the end of
  `RefreshFromSession` so a session refresh can't silently re-enable them while
  offline) / `ModPropertiesDialog.ApplyConnectivityGate` (check-updates + refresh
  translations; the version picker self-disables offline). Strings are passed INTO
  `ModsBrowser` (it doesn't import the Localization layer). Pinned by
  `OfflineFallbackTests` (`BuildOfflineResultData` + `IsNetworkError`).

- **Version picker (Mod Properties → General) is GitHubReleases-only and installs
  through the SHARED re-overlay path — not a new install flow.** For an installed
  `GitHubReleases` mod, `ModPropertiesDialog.LoadVersions` lists the repo's releases
  (`GitHubReleaseDownloader.ListReleasesAsync` → `/releases?per_page=100`, the same
  endpoint the translation registry uses) and `InstallVersionBtn` installs the
  chosen tag via `MainWindow.InstallGitHubVersionAsync` →
  `RepairInstallAsync(asUpdate:true, targetReleaseTag:<tag>)`. The tag threads
  through `ResolvePayloadUrlsAsync`/`ResolveInstallVersion`/`GitHubReleaseDownloader.
  ResolveAssetAsync` (all gained an `overrideTag`/`targetReleaseTag` param defaulting
  to null = the EFFECTIVE tag — the approved tag, or the resolved latest for
  follow-latest mods; see the follow-latest gotcha below). **Three load-bearing rules:**
  (1) the section is gated to GitHubReleases + installed + NOT external-hosted —
  external hosts pin a SHA-256 for the approved tag only, so other versions can't be
  verified (`ResolveAssetAsync` throws on a non-approved tag for external hosts).
  (2) Installing a version OTHER than the effective baseline auto-sets
  `ModState.PinnedVersion` to it (reusing the pin above) so the launcher doesn't
  immediately offer to "update" the user back; installing the baseline clears the
  pin. The baseline is `EffectiveGitHubTag`, NOT the raw approved tag — for a
  follow-latest mod, picking the latest is "following the recommendation", and
  pinning it would suppress every future follow-latest update.
  (3) **Rollback is as clean as a forward update because it IS one** — the re-overlay
  runs `ApplyUpdateDeletions`, which auto-removes the previous overlay's net-new
  files the chosen version no longer ships (`previous.OverlayNetNew` minus the new
  capture), so downgrading doesn't leave v2's added files orphaned. The residual
  (base-game files v2 *modified* aren't reverted unless the payload's `delete.lst`
  says so) is identical to forward-update behaviour. Fresh first-time installs still
  take the recommended tag through the normal Install flow — version choice there is
  a deferred follow-up. Known follow-up: `ListReleasesAsync` hits GitHub
  unauthenticated on every dialog open (60/h per IP — no ETag/304 like the self-
  updater yet). The picker's "recommended" badge reads
  `UpdateService.ResolveEffectiveGitHubTag`, not the newest list item —
  `ListReleasesAsync` KEEPS prereleases, so the first item may not be the effective
  latest.

- **`GitHubReleases` mods can OPT INTO follow-latest (`update.github.followLatest`)
  — the launcher then resolves "latest" from the modder's newest stable release
  instead of the catalog-pinned tag. Four rules are load-bearing.**
  `UpdateService.CheckCoreAsync`'s GitHubReleases branch calls
  `GitHubReleaseDownloader.GetLatestReleaseTagAsync` (`GET /releases/latest`, which
  excludes drafts + prereleases by definition — a modder's prerelease is never
  auto-published) with a per-mod conditional ETag
  (`ModState.LatestReleaseETag`, paired with `LastKnownLatestVersion`; 304s are
  free against the unauthenticated 60/h limit; the ETag is only SENT when the
  cached tag is non-empty, because a 304 has no body). Fallback chain: fresh tag →
  cached `LastKnownLatestVersion` → `ApprovedReleaseTag` (which stays REQUIRED in
  the catalog — seed for offline first installs; `ProjectToProfile`'s guard is
  unchanged). (1) **`GetLatestReleaseTagAsync` never throws except cancellation** —
  a failure inside `CheckCoreAsync` that bubbled to `CheckAsync`'s offline catch
  would degrade the WHOLE check to `BuildOfflineResult`, suppressing even the
  approved-tag update path that needs no network; it reports `ConnectivityState`
  itself via the reachedServer pattern. (2) **The effective install/update target
  is centralized in the pure `UpdateService.ResolveEffectiveGitHubTag(gh,
  cachedLatest)`** (pinned by `GitHubFollowLatestTests`) + the MainWindow wrapper
  `EffectiveGitHubTag(profile)`; `ResolveInstallVersion` / `ResolvePayloadUrlsAsync`
  / `TryApplyGitHubDeltaAsync` (via `DeltaPatchService.TryPrepareAsync`'s
  `targetTag` param) use it when `overrideTag == null`. External hosting resolves
  to approved INSIDE the rule, so `ResolveAssetAsync`'s external guard can never
  see a non-approved tag from a default path. (3) **Never thread the resolved
  latest through `targetReleaseTag`/`overrideTag`** — those mean "the USER chose a
  version" and trigger the version-picker auto-pin, which would pin the mod and
  kill every future follow-latest update. (4) The cache is fresh by construction
  (the Update CTA only appears after a `CheckAsync`, which writes it). Follow-up
  (repo `notifier-server`, non-blocking): the central feed publishes
  `latestVersion` from the catalog's approved tag, so the bell for INSTALLED
  NON-ACTIVE follow-latest mods lags behind until the feed also queries
  `/releases/latest`; the active mod's check and the sweep's per-mod fallback
  resolve the real latest.

- **`GitHubReleases` mods can ship OPT-IN incremental delta patches — a small
  "changed files only" update that's a best-effort shortcut with a GUARANTEED
  full fallback. Never make the delta path a hard requirement.** Owned by
  `Services/DeltaPatchService.cs` (+ `NativeInstallService.ApplyGitHubDeltaAsync`,
  `ArchiveService.ExtractZipWithBackupAsync`, `GitHubReleaseDownloader.ListAssetsAsync`).
  Gated by `GitHubReleasesSettings.DeltaPatches` (catalog `update.github.deltaPatches:
  true`, threaded through `ModCatalogManifest`→`ModRegistry.ProjectToProfile`). **The
  modder** ships, on release vY alongside the full `.zip`, two extra assets:
  `patch-<X>-to-<Y>.zip` (only changed/added overlay files) + `patch-<X>-to-<Y>.json`
  (descriptor: `fromTag`/`toTag`/`payload`/`payloadSha256`/`changed[]{path,fromSha256,sha256}`/
  `deleted[]`), produced by the in-app **Settings → Packager → "Generate patch"** tool
  (`PatchGeneratorDialog` → `DeltaPatchService.GeneratePatchAsync`, diffs two overlay zips
  by SHA-256). **Consumer flow** rides INSIDE `RepairInstallAsync`'s update `else` branch
  (`MainWindow.TryApplyGitHubDeltaAsync`): only for a normal update (`asUpdate && targetReleaseTag
  == null`) of an eligible mod (`DeltaPatchService.IsEligible`: GitHubReleases + flag + NOT
  external-hosted), it tries the delta and, on ANY false, falls through to the existing full
  `InstallModOnlyAsync` — so the shared tail (recheck, `LastKnownVersion` write,
  `ReconcileAfterUpdate`, notifications, `CheckAsync`) is inherited unchanged. `TryPrepareAsync`
  discovers the `patch-*.json` on the approved release (`ListAssetsAsync` — the assets are already
  in the release JSON `ResolveAssetAsync` fetches), matches by `SelectPatch` (`toTag==approved &&
  fromTag==LastKnownVersion` — **single-hop only**; a version skip → full), **pre-verifies**
  (`PreVerify`: each `changed` file's recorded pre-state must match — strong via `fromSha256` vs
  `manifest.FileHashes`, degraded via live-file-vs-manifest; covered files through
  `ResolveHashTarget`/`_originals`), downloads + `payloadSha256`-verifies the zip.
  `ApplyGitHubDeltaAsync` extracts with backup/rollback, **post-verifies** against `changed[].sha256`
  (rollback → false → full on mismatch), reuses `ApplyUpdateDeletions` (auto-removes only
  net-new files no longer present — a base-shadowing file is NEVER deleted, no holes), recaptures
  hashes from the live canonical bytes, `ClassifyOverlay` + `WriteManifest` (Version=toTag,
  preserving shortcuts/source), `RefreshOriginalsSnapshot`. **Load-bearing invariants:** the
  result is byte-identical to a full update (MP-safe); the delta backup lives in `%TEMP%` (NOT the
  install, or `WriteManifest`'s enumeration would pick it up); the manifest is written LAST so a
  crash leaves the OLD version and the next update self-heals via full; hashes in the descriptor
  are OPTIONAL (verified when present, degraded when absent — same trust level as today's
  hash-less full GitHubReleases update). Pinned by `DeltaPatchTests` (diff/select/eligible/
  pre-verify + generator round-trip). Docs: `docs/MODDING.md` §5.1 "Incremental delta patches",
  catalog schema `update.github.deltaPatches`. **Out of scope (follow-ups):** multi-hop chaining,
  delta on the arbitrary version-picker, auto-detection without the flag.

- **`config.GameExecutable` is a GLOBAL exe cache that two profiles share — it
  MUST be cleared on mod switch.** Despite the per-mod `Mods` dictionary, the
  resolved game-exe path is cached in the flat, launcher-wide
  `config.GameExecutable` (legacy field), and `GameLauncher.EnumerateCandidates`
  trusts it as candidate #1 on a **filename-only** match. Both the WoL built-in
  and the stock `aoe3-tad` profile declare `GameExecutable="age3y.exe"`, so a
  path cached while one was active satisfies the match for the other and the
  **wrong game launches after a switch** (play AoE3 → switch to WoL → PLAY
  opened AoE3). The blessed fix is to clear `config.GameExecutable = ""` whenever
  the active mod changes — `LoadModProfile` does this right after setting
  `ActiveModId`, mirroring the existing clear-on-uninstall step
  (`UninstallMenuItem_Click` / browser uninstall). After the clear, resolution
  falls through to the new mod's per-mod install-folder walk + disk scan, which
  is mod-correct. Don't reintroduce a code path that swaps the active profile
  without clearing this cache.
  **Multiplayer is a SECOND, switch-less variant of the same trap.** The MP game
  launch runs the **room's** mod (`LaunchActiveModGame` resolves it from
  `_currentLobbyModId`, not the Play tab), which can differ from the active
  dashboard mod *without any mod switch* — host/join a WoL room while AoE3 is
  active and `config.GameExecutable` still legitimately holds AoE3's
  `age3y.exe`, so trusting it opens AoE3 for a WoL room (reported bug: "changed
  to a WoL room, still opened Asian Dynasties"). The clear-on-switch fix can't
  help here (there's no switch). The fix is a `trustConfigCache` parameter
  threaded through `GameLauncher.Find` / `EnumerateCandidates` / `LaunchAndWatch`:
  the MP launch callback (in `MainWindow`'s `MultiplayerView.Attach`) passes
  **`trustConfigCache: false`**, which (a) skips the cache as candidate #1 so the
  exe resolves purely from the **room mod's** install folder, and (b) skips
  writing the resolved exe back to `config.GameExecutable` — otherwise a WoL MP
  launch would poison the cache and the dashboard's next AoE3 PLAY would open WoL
  (the reverse bug). The dashboard `GameLauncher.Launch` keeps `trustConfigCache`
  at its default `true` (it launches the active mod, so the cache is correct and
  worth persisting). Don't drop the `false` on the MP path.

- **The game is launched RE-PARENTED under `explorer.exe`, not as a child of the
  launcher — don't "simplify" it back to a plain `Process.Start`.** Windows Task
  Manager's "End task" force-terminates the target's whole **process tree**, so a
  game launched as a normal child of the launcher gets killed when the user force-
  closes the launcher that way (reported bug: "closing the launcher via Task
  Manager also killed discord.exe" — a process-tree cascade; the launcher has NO
  code that touches Discord — no Job Objects, no `discord://`, no RPC — so the only
  fix vector is making launched processes break out of the launcher's tree).
  `Services/DetachedProcessLauncher.StartReparented` uses
  `CreateProcess` + `PROC_THREAD_ATTRIBUTE_PARENT_PROCESS` (via a
  `STARTUPINFOEX` attribute list) pointing at `explorer.exe` in the current session,
  so the game becomes explorer's child — outside the launcher's tree — and survives
  an "End task" on the launcher. Both `GameLauncher.Launch` (dashboard, fire-and-
  forget) and `GameLauncher.LaunchAndWatch` (MP in-lobby) use it. **Two load-bearing
  rules:** (1) it's **fail-safe** — `StartReparented` returns `-1` (never throws)
  when re-parenting isn't possible (no explorer, insufficient rights, interop
  failure), and BOTH callers **fall back** to the original `Process.Start` path, so
  launching the game can never fail because of this hardening; (2) `LaunchAndWatch`
  still needs the `Exited` callback + a `Process` handle (for the cancel/leave
  `Kill(entireProcessTree:true)`), so after the re-parented spawn it opens the new
  pid via `Process.GetProcessById(pid)` + `EnableRaisingEvents` — `Process.Exited`
  fires for any process you hold a handle to, not only children. The game keeps the
  launcher's token/env/desktop (only the tree parent is explorer), so elevation and
  the window show unchanged. Pinned by
  `WarsOfLibertyLauncher.Tests/DetachedProcessLauncherTests` (interop launches
  without throwing; returns a valid pid or the `-1` fallback).

- **Community translations are PROFILE-scoped, and the index is re-fetched on
  mod switch.** Two coupled rules: (1) `UpdateService.EffectiveTranslationsRepo()`
  returns the repo from the active `ModProfile.Translations` block, or `""` when
  the profile has none (the stock `aoe3-tad` game, any mod without a Translations
  block). It must NOT blanket-fall-back to the global, WoL-centric
  `config.TranslationsRepo` for those — doing so made every mod inherit WoL's
  `papillo12/translations` packs, so the stock game offered WoL's Spanish pack
  and applying it would overwrite the base game's `data\stringtabley.xml` /
  `unithelpstringsy.xml` with WoL strings. The global field stays as an override
  only for a profile that already participates. (2) `_cachedTranslationIndex` is
  a single launcher-wide field, reset to null on every mod switch in
  `LoadModProfile`; the switch path must therefore **re-fetch it**
  (`RefreshTranslationIndexAsync`, gated on `CheckUpdatesOnStartup` like the boot
  pre-fetch) before `PopulateGameLanguageMenu`, or the gear-menu / Mod Properties
  language list comes up empty and previously-visible community translations
  vanish until the user hits Refresh. The fetch itself was never the problem
  (`launcher-debug.log` shows "Translation releases scanned: N valid entries") —
  the index was just being discarded without re-fetching.

- **Translations are discovered in DUAL MODE: a `translations/` FOLDER on main +
  legacy GitHub releases — keyed by content hash, not a release tag.** Publishing
  via release assets (upload `translation.json` + `.zip`) was clunky, so packs can
  now be **committed as files** under `translations/<id>/` (a `translation.json` +
  its `.zip`) on a dedicated repo's main branch. **Each translation also keeps a
  VERSION HISTORY:** versions live in `translations/<id>/<version>/` subfolders;
  `FetchFromRepoFolderAsync` reads the whole repo tree in ONE call (the **Git Trees
  API**, recursive), regex-matches `translations/<lang>(/<version>)?/translation.json`,
  groups by `<lang>`, reads each manifest via raw CDN, and builds **one
  `TranslationIndexEntry` per language** whose top-level fields = the NEWEST version
  (so the menu / dedup / notification are unchanged) plus a
  `Versions[]` list (newest-first, `TranslationCompat.OrderVersions`: by `date`
  desc then version, capped at `MaxTranslationVersions`=10). `translation.json`
  gained a `date` (packager-stamped) for reliable ordering; a flat
  `translations/<id>/translation.json` (no version subfolder) is read as a single
  version (back-compat). `TranslationRegistryService` has
  three entry points: `FetchFromReleasesAsync` (legacy, unchanged),
  `FetchFromRepoFolderAsync` (the tree-API folder scan above), and
  `FetchAsync(folderRepo, releasesRepo)` which runs **both** and
  merges by id with **folder packs winning** (and sorted first, so they rank as
  "newest" in `OrderForDisplay`). **UI:** the Properties → Language tab shows a
  **version picker** (combo + Apply, mirroring the GitHubReleases version picker)
  for any pack with `Versions.Count > 1` (`ModPropertiesDialog.BuildVersionedLanguageCard`);
  applying a non-newest version clones the entry with that version's
  URL/hash/compat (`ApplyChosenVersion`). The applied version is remembered in
  `ModState.ActiveTranslationVersion` (cleared on revert-to-English). The gear
  menu stays single-entry (newest). The notifier mirrors the tree-API scan and
  emits the **newest** version's `id@contentHash` (one bell per new version). The repos come from the profile's
  `Translations.FolderRepo` (new) + `.Repo` (legacy), resolved by
  `EffectiveTranslationsFolderRepo()` + `EffectiveTranslationsRepo()`; the catalog
  `mod.json` `translations` block gained a `folderRepo` field. **A user-facing
  GLOBAL override exists** (`config.TranslationsFolderRepo`, Settings → TRANSLATIONS
  tab, three-way `""`/`"none"`/`"owner/repo"` like `ModsCatalogRepo`): `""` = the
  profile's own folder repo (default); `"none"` = no community packs; a custom
  `owner/repo` replaces the folder repo AND suppresses the legacy releases path
  (`EffectiveTranslationsRepo()` returns `""` whenever the folder override is set or
  `"none"`), so the chosen repo is the single source. The override still passes
  through the participation gate — `EffectiveTranslationsFolderRepo()` returns `""`
  for a profile with no Translations block regardless of the override, so it can't
  inject packs into a mod that opted out. Pinned by
  `TranslationsFolderRepoOverrideTests`. **The dedup /
  notification key is centralized in `TranslationCompat.KeyOf`:** release tag →
  `id@contentHash` (folder packs) → `id@version` (legacy). `contentHash` is the
  manifest's field (written by the packager) or recomputed from the files'
  `translatedHash` via `TranslationCompat.ComputeContentHash` — sort files by
  path, join `path\ntranslatedHash` with `\n`, SHA-256, first 16 hex. **An
  IMPROVED pack (changed bytes) yields a new hash → a fresh "new translation"
  bell with NO release tag and NO manual version bump** — this is what replaces
  the release-tag "newness" signal for folder packs. The recipe MUST stay
  byte-identical to the notifier's `computeContentHash` (notifier emits the same
  `id@contentHash`); pinned by `TranslationCompatTests` (cross-impl value
  `67426f0ebcfec85f`). The packager (`TranslationService.ExportPackageAsync`)
  writes `contentHash` + `zip` (the zip filename) into the manifest and the
  Packager dialog's publish instructions now describe the folder path first
  (release stays as the legacy alternative). Don't reintroduce an inline `KeyOf`
  in `MainWindow` — use `TranslationCompat.KeyOf`.

- **A version-mismatched translation WARNS, it does NOT block — don't re-disable
  it.** A pack whose `compatibleWith` list doesn't include the installed mod
  version (`TranslationCompat.IsVersionBlocked`, `Models/TranslationPack.cs`) is
  surfaced as a *caution the user can override*, not a dead entry: the gear
  language menu item (`MainWindow.BuildLanguageMenuItem`) stays **enabled** in
  **amber + ⚠** (not disabled/grey), and the Mod Properties language card
  (`ModPropertiesDialog.BuildLanguageCard`) stays **clickable** showing
  **"Use anyway"** (`LangCardUseAnyway`) with a warning hint. Both route to
  `ApplyTranslationAsync` → `TranslationApplyDialog`, which is the real gate: it
  shows the ⚠ badge + the pack's declared compatible versions and **soft-blocks**
  (first click warns + flips the button to "Apply anyway"/`DlgLangApplyBtnForce`,
  second click proceeds), then does the deep MD5 hash recheck. Two HARD blocks
  stay: a pack for a **different mod** (`TargetModMatches`, would overwrite another
  mod's files) and the post-update auto-revert-to-English (`ReconcileAfterUpdate`).
  Don't reintroduce `IsEnabled = !incompatible` on the menu item or
  `clickable = !blocked` on the card — incompatibility is a warning, the apply
  dialog owns the confirmation. **Ordering of the language list is shared via
  `TranslationCompat.OrderForDisplay`** (active pack → compatible-with-installed-
  version → newest release → name), used by BOTH the Mod Properties Language tab
  (`LoadLanguage`) and the gear menu (`PopulateGameLanguageMenu`) so they match —
  "newest" is the pack id's position in the registry index (GitHub `/releases` is
  newest-first). Don't revert either to a plain `OrderBy(e => e.Name)`. Pinned by
  `TranslationCompatTests`.

- **The multiplayer fingerprint is LOCALIZATION-INVARIANT — applying a translation
  must NOT lock a player out of English lobbies.** `ModHashService` gates the lobby
  join on the SHA-256 of three files (`data\protoy.xml`, `techtreey.xml`,
  `stringtabley.xml`). But `stringtabley.xml` is exactly what a community translation
  overwrites, so hashing the LIVE file made a Spanish install and an English one on
  the same build produce different `CombinedHash`es → the lobby wrongly rejected the
  join ("apliqué español y no puedo jugar con peers en inglés"). String tables don't
  affect the simulation, so that's a FALSE mismatch. The fix: `FingerprintAsync`
  hashes the canonical English snapshot (`translations\_originals\`) for covered files
  via `TranslationService.ResolveHashableFile`, **exactly like
  `UpdateService.DetectCurrentVersionAsync` already does** for version detection. It's
  read-side only (the live files stay translated, so the player keeps their language
  IN multiplayer): no-op for English (snapshot == live); `protoy`/`techtreey` have no
  snapshot so they keep hashing the live file (a real OOS still mismatches); falls back
  to the live file when no snapshot exists; host and joiner compute it identically →
  symmetric. Pinned by `WarsOfLibertyLauncher.Tests/ModHashServiceTests`. **CAVEAT (not
  yet smoke-tested):** this fixes the LAUNCHER's lobby gate. AoE3 community evidence
  (CRC/version-mismatch and OOS are blamed on `proto`/`techtree`, and vanilla AoE3's
  cross-language MP works) indicates `age3y.exe`'s own LAN version match is centered on
  the simulation files, NOT the string tables — so the engine almost certainly does NOT
  independently block a translated player. Confirm with a Windows LAN smoke test (host
  EN + joiner ES on the same WoL build); the exact coverage of AoE3's CRC isn't publicly
  documented. If the engine DID gate on `stringtabley`, the fallback would be to swap the
  English file in only for the MP launch (which would sacrifice the in-game language).

- **The translator-facing packager lives in Settings → Packager tab, NOT the
  Game-language gear submenu, and it's globalised across mods.** The packaging
  dialog (`TranslationPackagerDialog`) used to be a `MenuItem` ("📦 Empaquetar
  mi traducción…") in the ActionPanel's `MenuGameLanguage` gear submenu and was
  hard-coded to the launcher's *active* mod — so packaging a Spanish pack for
  Improvement Mod while WoL was active forced a mod-switch first. The entry
  point moved to the **LauncherSettings → PACKAGER / EMPAQUETADOR** tab
  (`DlgLauncherSettingsSectionTranslations` — kept that key name for git-blame
  continuity even though the label is no longer "TRADUCCIONES"). It was renamed
  from "TRADUCCIONES" because users read that as a launcher-language switch; the
  tab only builds translation `.zip`s. It's a thin sidebar tab
  (icon `\xF2B7`) holding a header + description + "📦 Abrir empaquetador de
  traducciones" button that opens the dialog modally. The dialog's first form
  field is now a **"Mod a traducir" combo** populated from `ModRegistry.All`
  minus `IsStockGame` (translating the base game isn't in scope). Switching
  the combo: (a) re-instantiates `_translationService = new
  TranslationService(state.InstallPath)` for that mod (`_translationService` is
  a field, not a constructor arg), so `HasOriginalsSnapshot` / `OriginalsFolder`
  point at the picked mod's `<install>\translations\_originals\`; (b) refreshes
  the "current version" compatibility checkbox label from
  `config.GetState(profile.Id).LastKnownVersion` (or "?" when unknown, in which
  case the checkbox auto-disables); (c) updates the default output filename
  prefix from `wol-…` to `<modid>-…` (so a multi-mod translator's desktop
  doesn't get `wol-es.zip` clobbered by a later Improvement Mod export). The
  auto-suggest is opt-out: once the user types into `OutputBox` themselves the
  `_outputIsAutoSuggested` flag flips false and we stop overwriting their path
  — the `TextChanged` handler detects manual edits by comparing against
  `BuildSuggestedOutput()`. The dialog's chrome was also redone (it used to
  open with the white default WPF chrome) to match the rest of the launcher:
  `WindowStyle="None"` + custom `WindowChrome`, sticky footer, custom ✕ close,
  sectioned body (`SectionHeader` style: TARGET MOD / PACK IDENTITY / SOURCE
  FILES / COMPATIBILITY / OUTPUT). Constructor is `(LauncherConfig config)` —
  not `(TranslationService, string?)` like before; if you see a call passing
  the old args, it's a stale reference. Don't reintroduce the gear-menu entry:
  the global Settings location is now the canonical place.

- **The stock base game is a detect-only built-in profile
  (`ModProfile.IsStockGame`).** `ModRegistry._builtIn` now has TWO entries: WoL
  and `aoe3-tad` ("Age of Empires III: The Asian Dynasties"). The stock entry is
  modelled as `InstallType=InPlaceOverlay` + `UpdateMechanism=Manual` purely to
  reuse the existing detection + "Ready to play" UI paths, but `IsStockGame=true`
  is what makes it special: the launcher only *detects* the base game on disk —
  it never downloads / installs / updates / uninstalls it (it's the user's own
  legally-owned copy). **Safety-critical:** uninstall is a blanket recursive
  delete of the install folder, and a stock profile's "install folder" IS the
  user's real AoE3 directory, so a stray uninstall would wipe their game.
  `UninstallService.Plan` hard-refuses any `IsStockGame` profile (returns
  `NotAValidInstall`); the gear-menu handlers (`UninstallMenuItem_Click` /
  `MenuRepairInstall_Click` / `MenuVerifyFiles_Click`) early-return; and
  `ModPropertiesDialog` hides the Maintenance + Danger Zone sections.
  **Don't remove these guards.** Detection: because the launcher never wrote a
  saved `InstallPath` for it, multiplayer host/join (`MultiplayerTab.GetInstallPath`
  / `IsModInstalledLocally`) and the mod-fingerprint compute fall back to
  `AoE3Detector.FindInstallRoot()` (first detected AoE3 root containing `data\`);
  `ModHashService` then fingerprints the same TAD data files
  (`protoy/techtreey/stringtabley.xml`), so two stock players on the same game
  version match and can share a lobby. The host launch appends
  `OverrideAddress="<ip>"` exactly like the mods. Like WoL, the entry is mirrored in
  the catalog repo (`mods/aoe3-tad/mod.json`) for the public listing, but the
  built-in **shadows** it at runtime (built-in wins on id collision).

- **Mod icons come from two different places — don't assume the catalog.**
  Community mods and the stock game (`aoe3-tad`) get their icon from the
  catalog repo (`mods/<id>/icon.png` → `ModProfile.IconUrl`; INSTALLED/active
  mods additionally get it cached by `ModAssetCacheService` under
  `%LocalAppData%\AoE3ModLauncher\mod-assets\` → `LocalIconPath` — see the
  disk-cache-policy gotcha below). The
  first-party **WoL built-in sets BOTH**: an `IconUrl` pointing at the catalog
  (`mods/wol/icon.png`) AND `BannerImage` = the `WoL.ico` **embedded in the
  `.exe`** (a `<Resource>` in the `.csproj`). Resolution is centralized in
  **`ModProfile.ResolveIconSource()`**: `LocalIconPath` (the cached catalog
  icon, installed/active mods only) → `IconUrl` **painted live from the
  network** (skipped while `ConnectivityState.IsOffline`) → `BannerImage`
  (packed) → null = letter monogram — so the catalog icon wins when present
  and the embedded `WoL.ico` is the offline / 404 fallback — WoL's icon is
  editable from the catalog (commit `mods/wol/icon.png`, no recompile) yet
  always renders even with no network. (`ResolveBannerSource` /
  `ResolveHeroSources` / `ResolveScreenshotSources` are the sibling resolvers
  for the other roles; the banner one deliberately has NO packed fallback — a
  256px .ico stretched to 1200×300 looks broken. Pinned by
  `ModProfileResolverTests`.) That `IconUrl` is hardcoded on the built-in in
  `ModRegistry`; the catalog `mods/wol/mod.json` is shadowed at runtime
  (id-collision rule above), so a community PR editing that manifest can't
  redirect WoL's branding — the override is the raw `icon.png` the hardcoded
  URL points at. Every icon surface calls `ModProfile.ResolveIconSource()`
  (the old per-surface duplicates `ResolveModIcon`/`ResolveIconUri` are gone);
  `TryLoadBitmap` / `TryLoadTileImage` accept on-disk paths, `pack://` URIs
  **and http(s) URLs**. The resolved icon is painted on the dashboard hero
  (`DashboardIconHost`), the Workshop tiles / rows / detail header, the Mod
  Properties header (`HeaderIconHost`), the Create-room mod card **and its
  mod-dropdown items** (the latter via `ModProfileIconBrushConverter`, bound from
  each item's `Tag`; the item Content stays the name string so the combo's
  selection box shows just the name while the disc beside it paints the selected
  icon), the **rooms-browser room cards** (`ResolveRoomModIcon`, gold ★ fallback),
  and the install shortcut. **The desktop / Start-Menu shortcut (`.lnk`
  IconLocation) uses a DIFFERENT, stricter resolution than every surface above
  — and getting it wrong silently shows the stock-AoE3 exe icon (the original
  "el juego está sin icono del mod" bug).** A Windows `.lnk` IconLocation only
  renders `.ico` / `.exe` / `.dll`; a `.png` path is NOT rejected — it silently
  falls back to the **target exe's** embedded icon. The old `FindShortcutIcon`
  returned `LocalIconPath` (the cached **`icon.png`**) first, so every
  launcher-made WoL shortcut pointed at a PNG and showed the generic AoE3 icon,
  even though the real `WoL.ico` ships right there in the install folder.
  `FindShortcutIcon` (`NativeInstallService`) now resolves in this order, and
  the invariant **"never hand a `.png` to a `.lnk`"** must be preserved: (1) any
  `.ico` in the **install-folder root** (WoL/stock ship `WoL.ico`) — wins
  outright; (2) `LocalIconPath` **only if it is itself a `.ico`**; (3) else the
  cached PNG is wrapped into a real `<modid>-shortcut.ico` by
  `IconConverter.TryWritePngAsIco` (a dependency-free PNG→ICO container writer —
  Vista+ renders PNG-compressed icon frames, so the PNG bytes are embedded
  verbatim, no `System.Drawing`), written into the **install folder** so the
  manifest tracks it (clean uninstall) and it survives the "clear icons cache"
  button (which only wipes `mod-assets\`); (4) else null → the exe icon. The
  generated `.ico` is created during `CreateShortcuts`, which runs **before**
  `WriteManifest`, so `EnumerateInstalledItems` records it. Pre-existing broken
  shortcuts self-heal: `NativeInstallService.TryHealShortcutIcons` (kicked once
  per mod per session from `MainWindow.ApplyCheckResult`, off-thread, guarded by
  `_shortcutHealAttempted`) reads the manifest's `Shortcuts`, and for any `.lnk`
  whose IconLocation points at a non-`.ico` / missing file, re-points **only**
  the IconLocation at the resolved `.ico`. For WoL that's a pure re-point of the
  user-writable Desktop / Start-Menu `.lnk` to the already-present `WoL.ico` —
  no elevation, no re-download. Don't reintroduce a path that lets a PNG reach a
  shortcut's IconLocation.

- **A mod's detail panel has a screenshot/GIF gallery — and GIFs animate ONLY
  there, not in the icon/banner/hero.** A catalog `mod.json` may declare a
  `screenshots` array (`screenshot1..8.<ext>`, PNG/JPEG/**GIF**, ≤5 MB each, max
  8 — see the catalog repo's `CLAUDE.md`/`mod.schema.json`). It flows
  `ModCatalogManifest.Screenshots` → `ModCatalogEntry.ScreenshotUrls` (resolved
  through the same anti-traversal `ResolveAssetUrl` as icon/banner) →
  `ModProfile.ScreenshotUrls`, and — for INSTALLED mods only (the strictest
  tier of the disk-cache policy below: screenshots are the heavy role, up to
  8×5 MB) — is cached as `{modId}-shot-{i}{ext}`
  (5 MB cap) by `ModAssetCacheService.GetScreenshotPathsAsync`. **It's lazy and
  separate from the icon/banner fetch:** screenshots download only when the
  detail panel opens (`MainWindow.EnsureScreenshotsAsync`, its OWN per-session
  guard `_screenshotFetchAttempted`), never eagerly per card. A NON-installed
  mod's gallery paints live from the catalog URLs instead
  (`ModProfile.ResolveScreenshotSources`) — nothing written to disk; a remote
  GIF still animates (XamlAnimatedGif downloads remote URIs itself, at the
  cost of a re-download per selection). The UI
  (`ModsBrowser`: `DetailGalleryStrip` thumbnails + the big `DetailGalleryViewer`
  Image) is **collapsed when the mod has no screenshots** (zero regression for
  old manifests / built-ins). **GIF animation is confined to the large viewer
  on purpose:** the viewer is a WPF `Image`, so a `.gif` animates via
  `XamlAnimatedGif.AnimationBehavior.SetSourceUri` (the `XamlAnimatedGif` 2.3.2
  package, Apache-2.0, managed-only); thumbnails stay static because
  `TryLoadBitmap` decodes only frame 0. The banner/hero surfaces are
  `ImageBrush`, which `XamlAnimatedGif` can't animate, so a GIF there would look
  static anyway — hence the catalog schema forbids GIF outside `screenshots`.
  `ClearGallery`/`SelectGalleryShot` stop the animation on a non-GIF shot and on
  detail close. (Localized via the `WorkshopGalleryTitle` key, passed into
  `ModsBrowser` as the `GalleryTitleText` property — `ModsBrowser` doesn't import
  `Localization`, so strings reach it as properties set in `ApplyLanguage`.)

- **The mod-asset DISK cache is gated: only INSTALLED mods + the ACTIVE
  dashboard mod (+ the mod with an op in flight) get images written to
  `mod-assets\` — everything else paints LIVE from the catalog URL. Don't
  re-widen the gate; browsing the Workshop must not fill the disk.**
  `EnsureModAssetsAsync` early-returns on `!ShouldCacheAssetsToDisk(profile)`
  (installed ‖ active ‖ `_operatingModId`; install-detection via
  `IsProfileInstalledLocally`, the same rule `BuildModRowState` uses) and
  `EnsureScreenshotsAsync` on `!IsProfileInstalledLocally` (stricter —
  screenshots are the heavy role). **Both gates sit BEFORE the per-session
  `_assetFetchAttempted`/`_screenshotFetchAttempted` guards on purpose:** an
  ineligible mod must not consume its one attempt, so when it becomes eligible
  later in the session (installed/activated) the same call sites re-enter and
  the fetch actually runs. The eligibility TRIGGERS are: `BuildModCard` and
  `RefreshActiveModBanner` kick `EnsureModAssetsAsync` **unconditionally**
  (the old "only when the brush is null / asset missing" gates are gone —
  with live URL painting the brush is never null, so they'd never re-fire for
  a newly-installed mod; the internal gate + session guard keep it cheap),
  and `InstallAsync`'s success tail clears BOTH session guards for the
  installed mod (`service.Profile`, not the displayed one) and re-kicks.
  Non-eligible mods flow through the `ModProfile.Resolve*Source()` resolvers
  (see the icons gotcha) straight to `BitmapImage.UriSource = <https url>`,
  which WPF downloads asynchronously: **`Freeze()` on a still-downloading
  bitmap/brush THROWS and the image never renders — every loader must use
  `if (x.CanFreeze) x.Freeze()`** (an unfrozen brush repaints itself when the
  download completes, no `DownloadCompleted` handler needed), and
  `IgnoreImageCache` is set for LOCAL paths only (for remote URLs, WPF's
  in-memory per-URI cache is the per-session download dedupe — net8 WPF does
  NOT use WinINet, so there is no OS-level HTTP cache; between sessions a
  non-installed mod re-downloads its ~60 KB icon, the accepted cost). The
  memo caches (`s_tileImageCache`, `_roomModIconCache`) subscribe
  `DownloadFailed` to evict, so a transient network failure doesn't pin an
  empty brush for the whole session. A one-time-per-launch migration sweep
  (`PurgeNonEligibleModAssetsAsync`, called from the `Loaded` handler after
  the catalog refresh — that call is the policy's single opt-out point)
  `Clear()`s the cached assets old builds wrote for non-eligible mods; that's
  a deterministic policy delete, NOT a violation of the offline-safe rule
  below (which is about NETWORK errors). Offline UX: `Resolve*Source` skips
  the URL step while `ConnectivityState.IsOffline`, so a non-installed mod
  falls straight to packed icon / monogram instead of waiting on a download
  that will never finish.

- **The mod-asset cache is stale-while-revalidate: it reflects a catalog image
  being DELETED or REPLACED, and it's offline-safe — don't revert it to
  fetch-once.** Each cached asset has a sidecar `{modId}-{role}.meta` (JSON
  `{ url, etag }`). `ModAssetCacheService` has two tracks: (1) the FAST path
  (`GetIconPathAsync`/banner/hero/screenshots) — **no network** when the cached
  file's URL matches; an empty/null URL means "removed from the catalog" → purge
  the file + return null (UI falls to the monogram/gradient); a changed
  URL/extension downloads. (2) the BACKGROUND path
  (`RevalidateIconAsync`/banner/hero/screenshots, returning a
  `RevalidateOutcome`) — a conditional GET (`If-None-Match` with the stored
  ETag); a **304 is free** (`Unchanged`), a **200 means the modder replaced the
  image under the SAME file name** (`Replaced`) → re-download in place, a
  **404/410 means the file was DELETED at the source** (`Removed`) → purge.
  **The 404 case is load-bearing and easy to miss:** deleting `mods/wol/hero.jpg`
  from the repo does NOT null the URL — WoL's `HeroImageUrl` is a *hardcoded*
  built-in (`ModRegistry`), and even for a community mod the `mod.json` usually
  still declares `heroImage` after the file is gone — so the only signal is the
  asset URL returning 404. A 404 is treated as deletion (definitive: a successful
  connection saying "gone"; throttling returns 403/429, offline throws), in BOTH
  the fast path (covers a PRE-meta cached file with no `.meta` to revalidate) and
  the revalidation path (covers a meta'd cache); 5xx/transient/offline always
  keep the cached copy. On `Removed`, `EnsureModAssetsAsync` nulls the
  `Local*Path` so the UI drops to the fallback. `MainWindow.EnsureModAssetsAsync` runs both in order:
  Phase 1 resolves every role from disk (reconciling a now-null URL too — the old
  `isEmpty(LocalIconPath)` gate was removed so deletion is handled) and paints;
  Phase 2 revalidates and, on a replacement, calls `InvalidateTileImageCache`
  then repaints. **Two load-bearing invariants:** (a) **offline-safe — NEVER
  delete a cached file on a network error.** The fast-path download is
  download-FIRST, purge-stale-variants-AFTER a successful write (an existing user
  with no `.meta` yet, or any failed/offline fetch, keeps serving the cached
  image and self-heals the meta on the next online launch); the only deletes are
  an explicit null URL (confirmed deletion) or a confirmed 200 replacement.
  (b) **the bitmap loaders must `IgnoreImageCache` for LOCAL paths** —
  `TryLoadTileImage` (MainWindow, plus its in-memory `s_tileImageCache` memo,
  invalidated by `InvalidateTileImageCache`) and `TryLoadBitmap` (ModsBrowser)
  both set `BitmapCreateOptions.IgnoreImageCache` for non-http sources, or a
  same-name replacement would repaint WPF's stale per-URI cached bitmap
  (deliberately NOT set for http URLs — see the disk-cache-policy gotcha
  above). `BuildModCard` kicks `EnsureModAssetsAsync` unconditionally (gated
  internally) so a mod that dropped its ONLY image still purges the orphan.
  `PurgeRole`/`Clear` are
  anchored to the exact `{modId}-{role}.` prefix (so `Clear("wol")` can't sweep
  `wol-extra-*`); `GetScreenshotPathsAsync` purges surplus `shot-{i}` when a
  gallery shrinks; and `ModRegistry.ApplyMerged`'s `ClearVanishedAssets` wipes a
  community mod's assets when it leaves the catalog (built-ins/stock excluded).
  **Known gap:** replacing a **GIF** shown in the large viewer goes through
  `XamlAnimatedGif.SetSourceUri`, which has its own load path that
  `IgnoreImageCache` doesn't cover, so a same-name GIF replacement can look stale
  in the viewer until restart (thumbnails via `TryLoadBitmap` are fine).

- **Catalog + image edits show up WITHOUT a restart — three triggers, gated by
  `CheckUpdatesOnStartup`.** The catalog is cache-first with a 24h TTL and was
  historically fetched only once at startup; image revalidation ran once per mod
  per session (the `_assetFetchAttempted`/`_screenshotFetchAttempted` guards
  never cleared). So a maintainer editing the catalog couldn't see changes
  without relaunching. Now: (1) `ModRegistry.RefreshFromCatalogAsync` takes
  `bool force` — `force:true` skips BOTH the fresh and stale cache branches and
  re-fetches online now (used to bypass the 24h TTL). (2)
  `MainWindow.RevalidateVisibleAssetsAsync(activeOnly)` clears the fetch guards
  and re-invokes `EnsureModAssetsAsync` per profile so its Phase-2 conditional
  GETs re-run (detect 404→purge / 200→replace and repaint). Its loop over
  `ModRegistry.All` self-limits: the disk-cache gate inside
  `EnsureModAssetsAsync` filters non-installed mods, so the automatic triggers
  generate no traffic/disk for mods the user doesn't have. Three
  triggers: the **Workshop "Actualizar" button** (`force:true` + revalidate ALL
  mods, **unconditional** — explicit user action); **window `Activated`**
  (throttle 60s, revalidate all + a forced catalog refresh if ≥5 min stale); a
  **5-min `DispatcherTimer`** (revalidate only the ACTIVE mod each tick to
  minimize GETs; force a catalog re-fetch every 3rd tick ≈15 min). **The two
  AUTOMATIC triggers (timer + focus) early-return / don't start when
  `CheckUpdatesOnStartup` is false** — that setting means "metered / be silent",
  so they must not generate background traffic; only the manual button ignores
  it. **Quota math is load-bearing:** image revalidation hits `raw` (ETag 304,
  not rate-limited) so it's cheap and frequent; the catalog manifest fetch is one
  `api.github.com/.../contents/mods` call (**60/h per IP**), so a single shared
  throttle `_lastCatalogRefreshUtc` (≥5 min, seeded after the startup fetch so the
  first focus doesn't re-force) keeps forced re-fetches to ≈4-12/h. `5 min`
  aligns with `raw`'s CDN cache (~5 min to propagate a delete/replace), so polling
  faster wouldn't surface changes sooner. "Limpiar caché de iconos" in Launcher
  Settings now fires an `AssetsCleared` callback → `RevalidateVisibleAssetsAsync`
  so cleared images re-download live instead of staying monograms until restart.

- **User-data paths are DUAL-ROOT: the system Documents folder can be
  redirected (OneDrive Known Folder Move / moved to another drive) and the
  game's saves may live in EITHER root — don't collapse the resolution back
  to a single `GetFolderPath(MyDocuments)`.** The real-world shape (a German
  user's report, and the maintainer's own machine): `GetFolderPath(MyDocuments)`
  returns the REDIRECTED path (e.g. `...\OneDrive\Dokumente` — OneDrive uses
  localized real folder names), while saves written by the 2007 engine before
  the redirection still sit in the physical `%USERPROFILE%\Documents\My Games\`.
  `UserDataService.GetUserDataFolder` probes BOTH candidates
  (`GetCandidateUserDataFolders`: redirected first, physical second, deduped)
  via the pure `PickUserDataFolder` (first root whose folder EXISTS wins;
  neither → the redirected one, so new data follows the system convention —
  pinned by `UserDataRootTests`), logs divergence once per mod per session
  (`"User-data roots diverge..."` — the evidence a diagnostic bundle needs),
  `ListBackups` scans `<folder>.bak.*` in BOTH parents, and `RestoreBackup`
  falls back to a recursive COPY (source left in place) when the chosen backup
  lives on another volume and `Directory.Move` throws. Every consumer
  (pre-install alert, gear menu, Properties tab, MP replay finder) goes
  through `UserDataService` — don't rebuild the path by hand. The Properties
  USER DATA tab makes this visible on purpose: it shows the resolved path
  (Consolas), an amber warning when the OTHER root also holds files, the
  backup count + latest date on the Restore row (button disabled with a
  "no backups yet" hint instead of a surprise message box), and an inline
  result line after Backup/Restore — the `createBackup`/`restoreBackup`
  callbacks are `Func<string?>` (result text or null) because the main-window
  status bar sits BEHIND the non-modal dialog; the shared cores are
  `MainWindow.CreateUserDataBackupCore`/`RestoreUserDataCore` (the gear menu
  items call the same cores).

- **The notification bell (Steam-style) is a persistent, deduped history fed by
  detection hooks — NOT a second toast pipeline.** `Services/NotificationCenter.cs`
  is the UI-free backing store (testable: `NotificationCenterTests`): owns the
  `ObservableCollection<NotificationItem>` the bell panel binds to, mirrors it into
  `LauncherConfig.Notifications` (persisted, capped at `MaxItems=50`), exposes
  `UnreadCount` + `Changed`/`ItemAdded`/`ToastRequested` events, and applies the
  per-kind dedup. Three kinds (`NotificationKind`): **UpdateAvailable**,
  **UpdateFinished**, **NewTranslation**. **Dedup survives the 50-cap** via per-mod
  `ModState` fields — `NotifiedUpdateVersion` (one bell per (mod, latest-version))
  and `NotifiedTranslationKeys` (`id@version` set) — NOT the visible list, so an old
  item rolling off never re-bells. The bell UI lives in `MainWindow.xaml` **overlaid
  on `Grid.Row=0`** (title bar, right-aligned, `Margin=0,0,138,0` to clear the 3×46px
  caption buttons, `IsHitTestVisibleInChrome=True` like the brand button) — a
  `TitleBarButton` with a Segoe MDL2 bell (`\xEA8F`) + a red count badge; a `Popup`
  holds the panel (`MpSurface` card, `NotifRowButton`/`NotifLinkButton` styles).
  **Indicator: static red badge with the count; a one-shot ~1.3s `RotateTransform`
  shake (`PulseNotificationBell`) fires only on `ItemAdded`, never looping.** Opening
  the panel `MarkAllRead()`s (badge → 0, items stay). Click → `NavigateToNotification`
  (`LoadModProfile` + `SwitchTopTab(TopTab.Play)`; NewTranslation also opens
  `MenuGameLanguage`). **Detection hooks**: update-available in `ApplyCheckResult`
  (`MaybeNotifyUpdateAvailable`, skips pinned / unknown-version); update-finished
  REPLACES the old direct `ShowToast` in `ApplyAsync`'s success block (so one toast,
  not two — the toast now rides `ToastRequested → ShowToast`); new-translation in
  `RefreshTranslationIndexAsync` (`NotifyNewTranslations`, **seeds a silent baseline
  on first fetch** so a full catalog doesn't flood on first launch). **All installed
  mods**, not just the active one: `SweepInstalledModsForNotificationsAsync` runs
  `new UpdateService(_config, profile).CheckAsync()` + translations fetch for each
  installed non-active, non-stock mod — sequential, gated by `CheckUpdatesOnStartup`,
  fired once at startup + every 6th `_catalogPollTimer` tick (~30 min) to respect the
  GitHub API budget.
  **The bell has SEVEN kinds and its own reliability + UX rules (added later):**
  (0) **`Installed` is distinct from `UpdateFinished`.** A "fresh install" of WoL chains
  install → auto-continue update internally, so `ApplyAsync`'s success block used to always
  bell "Update complete" even for a from-scratch copy. Fix: an `InstallCompletion`
  (`None`/`Fresh`/`Copy`) enum is threaded `InstallAsync → MaybeAutoContinueUpdateAfterInstall
  → ApplyUpdateWithElevationCheckAsync → ApplyAsync`; at the raise site, a non-`None`
  context raises `NotificationCenter.RaiseInstalled` ("Installation complete" / "Copy
  installed", `Notif{Installed,CopyInstalled}{Title,Body}`) instead of `RaiseUpdateFinished`,
  and the genuine-update callers (leave it `None`) are unchanged. `MaybeAutoContinueUpdateAfterInstall`
  now returns bool; when it DIDN'T continue (payload already latest), `InstallAsync` raises
  the install bell itself with the installed version. `RaiseInstalled` is NOT deduped (an
  install is user-initiated + raised once; no reconciliation double-fires it). **Known
  limitation:** if the install needs elevation, the `--update-now` relaunch loses the
  context and that leg falls back to "Update complete" (covering it needs threading the
  context through the relaunch arg).
  (1) **Three extra `NotificationKind`s** beyond the original three — `LauncherUpdate`
  (raised in `CheckForLauncherUpdateInnerAsync` alongside the gold self-update pill,
  deduped by `LauncherConfig.NotifiedLauncherTag`; click → the self-update dialog),
  `Connectivity` (raised in `MainWindow.OnConnectivityChanged` on a real offline/online
  flip, deduped by `NotificationCenter._lastConnectivityOffline` so a flaky network
  doesn't spam; never fired for the initial online state), and `NewMod` (raised in
  `RefreshCatalogAsync → MaybeNotifyNewMods` for a community mod that newly appears in
  the catalog, deduped by `LauncherConfig.NotifiedCatalogModIds`; click → Workshop +
  `ModsBrowser.ShowDetail`). Each has a per-kind icon `DataTrigger` in the
  `NotificationList` template and en/es `Notif*` strings. **`MaybeNotifyNewMods` MUST
  seed a SILENT baseline on the first catalog fetch** (`SeedCatalogBaseline`, guarded by
  `LauncherConfig.CatalogBaselineSeeded`) or the whole existing catalog floods the bell
  on first launch — same pattern as the translation baseline; the stock game is excluded
  and the WoL built-in is caught by the baseline. (2) **"Update finished" is raised in
  TWO places** — the direct raise in `ApplyAsync`'s success block AND a **startup
  reconciliation** (`MainWindow.ReconcileUpdateFinishedNotification`, called from
  `ApplyCheckResult`): it compares the freshly-detected installed version against the
  per-mod `ModState.NotifiedInstalledVersion` (silent baseline the first time, bell only
  on a version ADVANCE via `LauncherUpdateService.TryParseSemVer` — now `internal` for
  reuse — else on any change). This is the backstop for the real bug: the WoL update
  usually applies in an ELEVATED `--update-now` relaunch, which persists the direct
  notification to the ELEVATING account's `%LocalAppData%` (a different admin's profile),
  so the user's normal session never saw it; the reconciliation re-raises it in the
  user's own session/config. Idempotent with the direct raise (that dedups on the visible
  list). Also, `--update-now` now runs `CheckAsync` even when `CheckUpdatesOnStartup` is
  off, so `_pendingDownloads` is populated and the elevated apply actually happens.
  Pinned by `NotificationCenterTests`. (3) **The bell POPUP follows the window.** A WPF
  `Popup` renders in its own HWND and only computes placement on open, so it didn't move
  when the window was dragged. `MainWindow` attaches `LocationChanged`/`SizeChanged →
  RepositionNotifPopup` (nudges `HorizontalOffset` to force a placement recompute against
  `NotificationBellButton`) ONLY while the popup is open, in the same `Opened`/`Closed`
  hooks that attach the outside-click/deactivate close handlers — don't move that wiring
  out of the open-scoped block.

- **The notification sweep above can read a CENTRAL FEED instead of polling
  GitHub per-mod — `Services/NotificationFeedService.cs`, served by a SEPARATE,
  now-deployed Oracle VM.** `SweepInstalledModsForNotificationsAsync` first tries a
  single cheap `GET /manifest` (ETag/304) against the **notifier feed** — a tiny
  Node/Fastify service that polls GitHub ONCE for everyone and publishes each mod's
  `latestVersion` + translation keys — and only falls back to the per-mod
  `UpdateService.CheckAsync()` + translation fetch when the feed is unreachable or
  returns bad JSON, so **the feed is never a single point of failure**. The launcher
  still does the version/translation diff + dedup LOCALLY (the feed reports
  availability only). The URL comes from `ResolveNotificationFeedUrl()`
  (`MainWindow.xaml.cs`): `LauncherConfig.NotificationFeedUrl` defaults to `""` →
  the **hardcoded built-in `https://wol-notify.duckdns.org/manifest`**; `"none"`
  opts out (always GitHub); any URL overrides. `NotificationFeedETag` is echoed as
  `If-None-Match` for the 304. **Infra:** the feed runs on its **OWN free Oracle VM**
  (public IP `129.213.160.55`, systemd `notifier`, nginx + Let's Encrypt),
  **deliberately separate** from the lobby backend (`wol-lobby.duckdns.org`) — same
  split rationale as that backend. Sources + the tested deploy runbook live in the
  **companion `notifier-server` repo** (`github.com/Gorgorito12/notifier-server`,
  its `DEPLOY.md`); it auto-discovers tracked mods from the same catalog
  (`Gorgorito12/aoe3-mods-catalog`), so the launcher's default just works with no
  config. (The manifest shape is a CONTRACT with `NotificationFeed` /
  `NotificationFeedMod`: adding fields is safe, renaming/removing `mods` /
  `latestVersion` / `translations` silently breaks every client → coordinate across
  both repos.)

- **The lobby room view (`LobbyWindow`) deliberately shows each datum once —
  don't "helpfully" re-add the removed fields.** `RenderRoomPanel`
  (`MultiplayerTab.xaml.cs`) fills it, and four duplications were stripped on
  purpose: (1) the title shows the room's **real name** — `CurrentLobbyTitle` is
  populated on create (from the dialog's `CreatedLobbyTitle`) and on join (from
  `LobbySummary.Title`), both threaded through new `title` params on
  `EnterHostedLobbyAsync` / `JoinLobbyAsync`. **That field was previously dead
  (only ever nulled), so the header always fell back** — if you see it reverting
  to a generic name, check those call sites still pass the title. A genuinely
  unnamed room falls back to `"{host}'s room"` (`Strings` key
  `MpRoomTitleFallback`; `MpRoomTitleGeneric` until the host is known) — **not**
  the raw lobby id, which already shows under the ROOM ID stat (that stat carries
  a 📋 `CopyRoomIdButton`, handled locally in `LobbyWindow.xaml.cs` — pure
  clipboard, no session round-trip); (2) there is **no HOST stat** — the roster's
  per-row badge is the canonical host marker; (3) the info card (`RoomInfoCard`)
  is **Mod + Password only** (the old "Connection" cell duplicated the P2P status
  in the meta subtitle and "Max players" duplicated the PLAYERS stat), collapsing
  entirely when neither field has data; (4) the PLAYERS stat reads `"1 / 8"` — or
  just `"1"` when the max is unknown — with no trailing "players" word. Capacity
  comes from a `_currentLobbyMaxPlayers` stash that **mirrors `_currentLobbyModId`**
  (set on create/join, cleared on leave) and is read as a fallback by
  `TryGetCurrentLobbyMaxPlayers`, because the HOST is absent from the browser
  snapshot (`_lastBrowserList`) it checks first; the Mod name resolves the same
  way (`_lastBrowserList` → `_currentLobbyModId` fallback) so the host sees the
  mod, not an em-dash. The two-method label refresh this view relies on is in the
  Localization bullet under Runtime conventions.

- **The players roster (`RenderRoomMembers` / `BuildMemberRow`) is host-first
  with open-slot placeholders — keep both, they're load-bearing UX.** Members
  sort host-first via a *stable* `OrderByDescending` (non-host members keep their
  join order); below them, one dimmed "`Esperando jugador…`" row
  (`BuildOpenSlotRow`, `Strings` key `MpRoomSlotOpen`) is emitted per unfilled
  slot up to the room capacity, so the list shows at a glance how many can still
  join (needs the `_currentLobbyMaxPlayers` capacity above — no max, no
  placeholders). Per row: the Host/Ready badges are localised
  (`MpRoomBadgeHost` → "Anfitrión", reused `MpRoomReady` → "Listo"), and a player
  who has readied up gets a subtle green row tint (`#223FB950`) on top of the
  small Ready pill. Relatedly, `MpDivider` was raised `#2C313A → #3A434F` in
  `Colors.xaml` so the lobby / MP cards stop blending into the near-black
  `BgBase` — a **global** brush change across every multiplayer surface (rooms
  table included), not a per-dialog recolour.

- **`LobbyWindow` is an INDEPENDENT top-level window with its own Windows taskbar
  button.** It's `WindowStyle="None"` + `WindowChrome` + **`ShowInTaskbar="True"`**
  and has **no `Owner`** (`OpenLobbyWindow` deliberately does NOT set one), so it
  gets its own taskbar entry next to the launcher, alt-tabs independently, can sit
  on another monitor, and is NOT hidden when the launcher minimizes. The title bar
  is the shared `Controls/TitleBar` (see its global bullet) with the full
  **minimise / maximise / close** trio (`ShowMinimize`/`ShowMaximize`/`ShowClose`
  all true); minimise is a plain `WindowState.Minimized`, which —
  *because* `ShowInTaskbar="True"` — goes to the **Windows taskbar button** (click
  it to restore), NOT a desktop stub. **`ShowInTaskbar="True"` is load-bearing:**
  the entire original "minimise pops a system menu" bug came from
  `ShowInTaskbar="False"`, where a chromeless minimise fell to the unstylable
  bottom-left desktop *stub* whose click opens the OS system menu
  (Restore/Move/Size/…) — "se ve así" + "no me tire un menú". Don't flip it back to
  False, and don't re-add an `Owner`. (History, each built then rejected before
  landing here: a glowing in-window "pill" minimise replacement; an in-tab "Sala"
  sub-tab; and removing minimise entirely. The accepted answer is "just a normal
  taskbar window".) `WindowStartupLocation` is `CenterScreen` (was `CenterOwner`,
  which needs the Owner we no longer set).
- **The TRAFFIC + CONNECTION metrics are the only REAL connection numbers, and
  both are OVERALL, not per-peer.** TRAFFIC (in-game overlay, `RefreshInGamePanel`)
  = the Radmin VPN adapter's `BytesSent + BytesReceived` *delta since match start*
  (`RadminVpnService.GetAdapterBytes`, baselined in `EnterInGamePhase` as
  `_matchBaselineBytes`) — it's the whole adapter, not this game or one peer, but
  during a match that's effectively the game; shows "—" when no 26.x Radmin
  adapter is up. CONNECTION is your general **INTERNET** latency — an ICMP
  round-trip to a public anycast resolver (`PingInternetRttMsAsync`: Cloudflare
  1.1.1.1, then Google 8.8.8.8), cached in `_connectionPingMs` and refreshed by a
  fire-and-forget `KickConnectionPing` (guarded by `_connectionPingInFlight`),
  colour-coded (<80 ms green / <200 amber / else red). That ONE value drives every
  "ping" in the multiplayer UI: the in-game CONNECTION stat, the lobby header
  CONNECTION stat (`RoomConnText` via `UpdateLobbyPing` on a `_lobbyPingTimer`),
  and the rooms-browser PING column (`RefreshRoomPingCells` on a `_roomsPingTimer`,
  updated **in place** so rows — and their Join buttons — aren't rebuilt). It is
  **your** internet latency, **not** a per-rival ping, so it's identical across all
  browser rows. (We deliberately dropped the earlier Radmin seed-peer ping: it
  needed a specific peer online AND you already on the VPN, so it usually showed
  "—".) **The in-game per-peer RTT column is now REAL (not a placeholder).** It
  used to be `…` because the launcher couldn't map a Discord login to a Radmin IP.
  That's solved end-to-end: each launcher reports its own Radmin IP (26.x) via the
  `set_radmin_ip` WS frame at `EnterInGamePhase` (NOT at join — the user often
  isn't on the VPN yet then; re-sent each tick if it changes, `MaybeReportRadminIp`);
  the backend stores it on the room member and broadcasts `member_net` + includes
  it in `room_state.members[x].radminIp`; `HandleMemberNet`/`HandleRoomState` save
  it on `RoomMemberEntry.RadminIp`; and `KickPeerPings` (off the 1s in-game tick,
  parallel, guarded by `_peerPingInFlight`) ICMP-pings every peer's Radmin IP via
  `PingPeerAsync` (a single-host clone of `PingInternetRttMsAsync`), storing the RTT
  on `RoomMemberEntry.PingMs`. `BuildInGamePeerRow` colours it green/amber/red on
  the same thresholds as CONNECTION so a laggy player stands out. The
  Radmin IP is validated server-side against `26.x` (a client can't inject an
  arbitrary host for everyone to ping), and it's only shared among that room's
  members — the same IP they already use to actually play (`OverrideAddress="<ip>"`).
  **Two fixes made this actually VISIBLE + gave the −1 case meaning (the "alucard
  no muestra el ping" report).** (1) **Layout:** the peer row used FIXED columns
  (name 180 + state 110 + rtt 80 + bytes ⭐ = 370 px) inside the ~284 px left panel
  with NO horizontal scroll, so the RTT + bytes columns were CLIPPED off the right
  edge — the ping was computed but never seen. `BuildInGamePeerRow` is now
  `[health-dot Auto] [name ⭐ ellipsis] [ping-or-status Auto]` (the always-zero bytes
  placeholder column was DROPPED), so the ping/status can't be pushed off-screen.
  Don't reintroduce fixed name/state widths there. (2) **Meaning of −1:** a peer's
  state is derived by the pure, testable `Services/Multiplayer/PeerNetHealth.Classify`
  (`PeerNetHealthTests`) → `PeerLinkState` {WaitingVpn, Online, Unstable, Lost},
  rendered as a coloured dot + text: grey **"Esperando VPN"** (no Radmin IP reported
  yet) vs a real **"NN ms"** vs amber **"…"** (transient miss) vs red **"Sin
  conexión"** (sustained ICMP silence past `LostThreshold`=5 consecutive 1-s probes).
  `RoomMemberEntry` gained `ConsecutiveFails`/`ConsecutiveOks`/`LastLinkState` (updated
  in `KickPeerPings`); `RefreshInGamePanel` posts a chat line ONLY on the Online↔Lost
  edge (`MpChatPeerLost`/`MpChatPeerReconnected`), debounced by the fail streak.
  **Load-bearing caveat:** the ICMP "Sin conexión" is INDICATIVE only — Radmin/Windows
  frequently block inbound ICMP echo while the game works fine, so it's a soft "no
  responde", NOT an authoritative disconnect; the authoritative "left" signal stays the
  server's `member_left` (`HandleMemberLeft`). Peer pinging + the health signal now also
  run in the LOBBY (pre-match): the `_lobbyPingTimer` tick calls
  `MaybeReportRadminIp`/`KickPeerPings`/`RefreshRosterHealthDots`, which recolours the
  roster's per-member dot (Tagged with the userId in `BuildMemberRow`) in place — the old
  always-green dot was static. Your own row is always green / "vos".

- **The rooms browser auto-refreshes its LIST on a quiet diff — separate from the
  PING timer above.** New / closed rooms now appear without pressing *Actualizar*:
  a dedicated `_roomsListTimer` (10 s, created in `StartQuotaPolling`, stopped in
  `OnVisibleChangedTabGate`'s not-visible branch alongside the other timers) calls
  `RefreshRoomsListAsync(quiet: true)`, gated to **MP-tab-visible + signed-in +
  `_activeSubtab == Subtab.Rooms`** so it never polls while the list is hidden
  (don't drop that subtab gate — it's the whole point of the resource budget). The
  `quiet` flag is load-bearing and does three things a full refresh doesn't: (1)
  skips the "Cargando…" skeleton; (2) compares a `BuildRoomsSignature` of the
  payload — id / status / players / private / title / mod / host per row, **in
  order, NOT ping** (ping is owned in place by `_roomsPingTimer`) — against
  `_lastRenderedRoomsSignature` and **returns without touching the visual tree when
  nothing changed**, so Join buttons, hover and scroll position survive; (3) on a
  network blip it keeps the last good list (logs only) instead of wiping it to the
  red error banner. The manual *Actualizar* button, sign-in, tab activation and
  leave-room still call `RefreshRoomsListAsync()` **non-quiet** (skeleton + error
  banner + always re-render); `SubtabRooms_Click` fires one *quiet* kick so
  returning to the subtab freshens at once. Cost is one `GET /lobbies` (a single
  small SQLite SELECT, ≤8 active lobbies) every 10 s **while actively browsing** —
  well under the backend's `llist` cap (60/min · 2000/day per IP, `rateLimit.ts`)
  and the 100k/day global budget. **The rooms list itself is REST-poll** — the
  lobby WebSocket (`/lobbies/:id/ws`) is per-room and only joined once you're
  *inside* a room, so a viewer sitting on the list has no per-room socket. A
  process-wide global WS channel DOES now exist (`/global/ws`, added for the
  global chat — see its bullet below), so the infra for real-time room push is
  in place; actually emitting `lobby_created/closed/updated` onto it is still
  deferred (the 10 s poll is enough at current scale).

- **The rooms browser is a TABLE with responsive columns — the action button
  isn't a plain always-"Join".** (Doc heads-up: an earlier revision of this
  bullet described a `WrapPanel` of cards whose "old table + column-header strip
  + zebra rows are gone" — that was **REVERTED**. The code is a table; trust it.)
  `BuildRoomCard` builds one full-width row per room into a `StackPanel`
  (`RoomsListPanel`), each a 6-column `Grid` aligned under the `ColHeader*` strip
  in `MultiplayerTab.xaml`: SALA, ANFITRIÓN, JUGADORES, PING, ESTADO, ACCIÓN.
  **Those six columns are STAR-sized with Min/Max, NOT fixed px** — fixed widths
  summed ~810px and, since the rooms list shares its row with the 380px
  global-chat column and the `RoomsListScroll` ScrollViewer **disables horizontal
  scroll**, the right-most ACCIÓN column (the Join/Re-enter button) got
  **clipped off-screen on a small / restored window** (it only showed fully when
  maximized — the reported bug). Stars always divide the available width so the
  row can't overflow; each column's `MaxWidth` replicates the old fixed look on a
  large window and its `MinWidth` (esp. ACCIÓN = 110) keeps the button fully
  visible when space is tight. **Keep the header strip and the `BuildRoomCard`
  column defs in lockstep, and don't revert either to fixed px** — that re-clips
  the button. The row shows: a **leading mod-icon disc** (the room's mod icon,
  resolved by `ResolveRoomModIcon` = cached catalog `icon.png` → built-in packed
  icon, cached per mod id and decoded once; **gold ★ fallback** when the mod
  ships no resolvable icon — so icon-less rooms look exactly as before), title
  (+ 🔒 if private), the mod name (chip), the host with an initial circle
  (`Anfitrión: <name>`), players, ping, a status cell, and the **ACCIÓN-column
  action button** whose caption + enabled-ness pick per room in this **priority
  order** (first match wins) — enabled Join / Re-enter use the
  `MpOutlineBlueButton` outline style, the disabled states use
  `MpSecondaryButton` (neutral):
  1. **room we're currently in** (`iAmInThisRoom` = `lobby.Id ==
     _session.CurrentLobbyId`) → **"Re-enter"** (`MpRoomReenter`, ES "Reingresar")
     wired to `OpenLobbyWindow()` (re-opens / Activates the lobby window) — never a
     Join for a room we're already inside;
  2. **our own room we're NOT session-tracked in** (`iAmHost`) → **disabled "Your
     room"** (`MpRoomYours`). This is matched by **host identity** — `lobby.Host.Id
     == me.Id` OR `lobby.Host.DiscordUsername == me.DiscordUsername` (case-
     insensitive) — **not** `CurrentLobbyId`, so it still holds after we closed the
     lobby window but the backend kept the room alive; re-joining your own room
     errors server-side, hence disabled;
  3. **in-game** (`status == "in_game"`) → **disabled "In game"**
     (`MpRoomStatusInGame`) — the room is locked;
  4. **full** (`CurrentPlayers >= MaxPlayers`) → **disabled "Full"** (`MpRoomFull`);
  5. **mod not installed locally** → **disabled "Join"** (`IsEnabled =
     modInstalled`); else → **enabled "Join"** → `JoinRoomButton_Click`.

  Status shows in the ESTADO column as a dot + label (`BuildStatusCell`):
  `Esperando` (waiting) or `En partida` (in game), localised via
  `MpRoomStatusWaiting` / `MpRoomStatusInGame`. (The disabled "In game" / "Full"
  action button — not a separate badge — is the actual join block; an unused
  `BuildRoomBadge` leftover from the reverted card design was removed.) The header also carries an
  `Actualizado hace X` timestamp (`RoomsUpdatedText` / `UpdateRoomsUpdatedLabel`,
  ticked by `_roomsPingTimer`), and the empty state is now localized
  (`MpRoomsEmptyTitle` / `MpRoomsEmptyBody` — they used to be hardcoded English).
  All of this keys off the backend
  reporting `status == "in_game"` once the host starts — the rooms browser has no
  other signal that a room you're *not* in has begun (the room WS is per-room, joined
  only from inside), so if in-game rooms never lock, check the backend is flipping
  the lobby status, not the launcher. How these
  captions refresh: `BuildRoomsSignature` (the quiet-diff key) includes status +
  player count + host, so In-game / Full / host changes repaint within ≤10 s while
  browsing. The **viewer-relative** bits (`iAmInThisRoom` / `iAmHost`) are
  deliberately **NOT** in the signature — they don't need to be, because they're
  recomputed on every render and the events that flip them also change the payload
  (create adds your row; join/leave moves a player count; **leave-room additionally
  forces a non-quiet `RefreshRoomsListAsync()`**), so a render happens regardless.
  Don't try to encode "is this my room" into the signature.

- **Global chat is a process-wide WebSocket room — separate from the per-lobby
  chat, and the launcher's first real server-push channel.** The Multiplayer
  tab's Rooms view is now TWO columns: active rooms (left card) + a persistent
  **"Chat global"** panel (right card; `GlobalChat*` x:Names in
  `MultiplayerTab.xaml` — a merged header `Chat global · ● N conectado`
  (`UpdateGlobalPresence`; the old separate `Canal general` label is gone),
  message list, composer). The client renders each message as a subtle rounded
  **bubble** and **dedupes the avatar/name for consecutive messages from the same
  author** (`_lastGlobalChatAuthor`, reset whenever the panel clears); Send is a
  compact paper-plane icon button (caption on its ToolTip). That's all cosmetic —
  the WS protocol + anti-spam below are untouched. Server side it's a single `GlobalChatRoom` **singleton** on
  the Node backend (`src/global/GlobalChatRoom.ts`, mounted at `/global/ws` in
  `index.ts`, held on `AppContext.globalChat`) — modelled on `LobbyRoom`'s
  broadcast / idle-kick / throttle but with **almost no DB**: membership IS
  "holds a valid JWT" (auth on the first `hello`), the **only** DB touch is one
  indexed `users.avatar_url` read per *connection* (cached on the
  `AttachedSocket`, not per message) so chat lines can carry the real Discord
  avatar, and history is a **capped in-memory ring** (lost on restart, by
  design). Wire protocol: client → `hello {token}` / `chat {body}` / `ping`;
  server → `global_state {history, online}` / `chat {line}` / `presence
  {online}` / `pong` / `error` — each `line` is
  `{id, userId, login, avatarUrl, body, at}`, and the client renders `avatarUrl`
  as a circular photo with the login **monogram as the fallback** when it's null
  or fails to load (panel width is a fixed 380 px column). Client side it
  **reuses the generic `LobbyWebSocket`** (SessionToken hello,
  `BuildWsUri(Api.BaseUri, "global/ws")`), but the socket is **owned by
  `MultiplayerTab`, NOT `MultiplayerSession`** (unlike `RoomSocket`) because its
  lifetime is gated on *tab-visible + signed-in*, not on being in a lobby — see
  `SyncGlobalChat` / `OpenGlobalChat` / `CloseGlobalChat` (open from
  `StartQuotaPolling` + `OnSessionStateChanged`; close from the
  `OnVisibleChangedTabGate` hide branch + the session swap in `Attach`). A user
  can be in the global chat AND a lobby at once (two sockets). The new
  `MultiplayerSession.SessionToken` getter exposes the JWT for the hello.
  **Why it's cheap on the 1 GB VM (the feasibility question that gated this
  build):** WS frames bypass the per-request daily budget (only the upgrade
  counts, once), and everything is bounded — `globalChatMaxConnections` (default
  = `maxConcurrentUsers` = 60), **one socket per user** (a second `hello` closes
  the first), in-memory `globalChatHistory` (100), per-user `globalChatMsgsPerMin`
  (20) + 500-char cap (all in `env.ts` / `.env.example`). Added RAM is
  single-digit MB; the binding limit stays the 60-user budget, not chat. **Don't
  switch the global chat to REST polling** — 60 users polling would blow the
  100k/day budget many times over, which is the whole reason it's WS.

- **Global chat anti-spam: slow-mode + auto-timeout (server-side, in
  `GlobalChatRoom.handleChat`).** On top of the 20/min cap, two more layers throttle
  abuse, all config-knobbed: (1) **slow mode** — a minimum gap between messages
  (`globalChatMinIntervalMs`, 1500 ms); a too-fast message is dropped, not the
  connection. (2) **auto-timeout** — slow-mode / rate trips are counted as
  *strikes* in a rolling minute (`registerViolation`); cross
  `globalChatTimeoutStrikes` (5) and the user is auto-muted for
  `globalChatTimeoutMs` (30 s), during which every message is dropped with the
  remaining seconds. The mute lives in a room-level `mutedUntilByUser` map keyed
  by **user id** (not socket), so reconnecting can't shed an active timeout
  (strikes stay per-socket — fine, the mute is the sticky part). No human moderator and no admin/role concept exists — these
  are purely automatic (manual mute/ban would need a new admin layer the backend
  doesn't have). The server emits distinct `error` codes (`chat_slow_mode` /
  `chat_rate_limited` / `chat_muted` / `chat_timeout` / `chat_too_long`); the
  launcher maps each to a localized hint shown above the composer
  (`GlobalChatNotice`, `ShowGlobalChatNoticeFor`, cleared on the next keystroke) —
  server error *messages* stay English, the client localizes by code. The check
  order in `handleChat` is **muted → length → slow-mode → per-minute**, and a
  slow-mode drop bails *before* incrementing the per-minute counter so it isn't
  double-penalized.

- **The launcher self-update (`LauncherUpdateService`) verifies before it
  swaps, and the swap is reversible — don't loosen either.** Detection is
  still **tag-based** (compare GitHub's `releases/latest` `tag_name` to
  `config.LastInstalledLauncherTag`, decoupled from AssemblyVersion except as
  the no-saved-tag fallback — see (1)), but the flow is now hardened in four
  load-bearing ways: (1) **SemVer guard** —
  `CheckAsync` only offers an update when the remote tag parses as a *strictly
  newer* SemVer than the installed one (`TryParseSemVer` strips a leading `v`
  and any `-rc`/`+commit` suffix). The guard compares the remote tag against an
  **effective current version**: the saved tag when present, else — for a binary
  that never self-updated in-app (a manual download from GitHub Releases, or a
  build run straight from `publish\`) — the binary's **stamped AssemblyVersion**
  (`EvaluateUpdate` → `FormatVersionTag`, `0.9.9.0` → `v0.9.9`). This closed a
  real bug: an empty saved tag used to fall through to prompt-on-any-difference,
  so a freshly-downloaded `v0.9.9` offered an "update" to `v0.9.9` and showed
  `current: —` (it hit every user the first time they opened the .exe). The
  fallback relies on `build-release.ps1 -Version` stamping the AssemblyVersion,
  so **release builds must pass `-Version`** (and use SemVer tags like `v0.9.8`)
  for both the "don't update backwards" guard and self-recognition to engage; a
  non-SemVer SAVED tag still keeps the prompt-on-difference fallback so a weird
  tag scheme can't silently hide an update. The decision lives in the pure,
  network-free `EvaluateUpdate` (saved-tag → dismissed-tag → SemVer guard),
  pinned by `WarsOfLibertyLauncher.Tests/LauncherUpdateServiceTests`.
  (2) **Integrity + authenticity verification** runs inside
  `DownloadUpdateAsync` *before* the binary is usable, and on failure deletes
  the downloaded `_new.exe` and throws `UpdateVerificationException` (the dialog
  shows a localized `DlgLauncherUpdateVerifyFailed*` message). **SHA-256**: the
  expected hash comes from GitHub's per-asset `digest` field, falling back to a
  `SHA256:`/`SHA-256:` line parsed out of the release **body**
  (`ExtractExpectedSha256`) — which is exactly the paste-ready line
  `build-release.ps1` now prints. The check is **deliberately tolerant**: a
  missing published hash logs a warning and *proceeds* (so pre-hash releases
  still self-update) rather than hard-blocking. **Authenticode**: the
  downloaded `.exe` must be signed by the **same `Subject` as the
  currently-running binary** (read at runtime via
  `X509Certificate.CreateFromSignedFile`, NOT hard-coded `CN=Gorgorito`, so a
  cert rotation needs no code change); if the running binary is itself unsigned
  it can't establish an expected signer and only warns. **Note this is a
  same-signer check, not a trust-chain validation** — the self-signed cert
  isn't in a CA chain; the guarantee is "same publisher as the binary the user
  already trusts and is running", which is the meaningful one for a
  self-updater. (3) **Atomic swap with rollback** — `RelaunchUpdated` renames
  `current → .old` then `new → current`; if the second move fails (AV lock,
  partial write) it **restores `.old → current`** and throws, so the user is
  never left with no executable at the launcher's own path. `CleanupOldVersion`
  (called early on startup) now also deletes an orphaned `_new.exe` from an
  aborted download, not just `.old`. (4) **Conditional fetch (ETag/304)** —
  `CheckAsync` sends `If-None-Match` with `config.LauncherUpdateETag` and
  returns `NoUpdate` on `304 Not Modified`, sparing the unauthenticated GitHub
  rate-limit (60 req/h per IP — a real concern behind shared NAT / Radmin). The
  ETag is threaded through **every** return path (including the `catch`, which
  preserves the cached value so a transient failure doesn't force a full fetch)
  and persisted by the caller in `MainWindow.CheckForLauncherUpdateInnerAsync`
  only when it changed. Returning `NoUpdate` on 304 is **correct, not a missed
  prompt**: after the first prompt the tag is always either installed or saved
  as `SkippedLauncherTag` (any dialog dismissal saves it), so the full path
  would also return `NoUpdate`; a genuinely new release changes GitHub's ETag →
  `200` → re-evaluated. Asset selection (`FindExeAsset`) prefers the exact name
  `Aoe3ModLauncher.exe`, falling back to the first `.exe` only when there's no
  exact match, and the `HttpClient` has a 15 s timeout so a slow GitHub doesn't
  stall the startup `WhenAll`. The dialog also surfaces the release `body` as a
  **"Novedades"/"What's new"** section (`ReleaseNotesSection`, collapsed when
  empty). **Publishing contract:** ship a SemVer-tagged release with the signed
  `Aoe3ModLauncher.exe` asset and paste the `SHA256:` line from
  `build-release.ps1` into the release notes (or rely on GitHub's `digest`).
  **(5) Letter versions (WoL-style: `v1.0.5a` < `v1.0.5b` < `v1.0.6`) are now
  understood, and a manually-downloaded binary recognises itself.** `TryParseSemVer`
  parses a trailing letter suffix into a base-26 rank (`LetterRank`: no letter = 0,
  `a`=1…`z`=26, `aa`=27) packed into `Version.Revision`, so `1.0.5 < 1.0.5a < 1.0.5b
  < 1.0.6` compares with plain numeric `Version` ordering — change `LetterRank` and
  you change the whole comparison. `EvaluateUpdate` gained a `currentInformationalTag`
  param and its **effective-current** priority is now `saved tag → informational tag
  → numeric AssemblyVersion`. The informational tag comes from the new
  `CurrentInformationalTag` (reads `AssemblyInformationalVersionAttribute`, strips
  SourceLink `+commit` metadata, never null — falls back to
  `FormatVersionTag(CurrentVersion)`), which `build-release.ps1` stamps with the full
  letter string (the numeric AssemblyVersion can't hold the letter). This closed the
  letter-suffix twin of the empty-saved-tag bug: a freshly-downloaded `v1.0.5a` with
  no saved tag used to offer to "update" to `v1.0.5a` (its AssemblyVersion read
  `1.0.5`); reading the informational tag lets it recognise itself. Pinned by the
  letter-version cases in `LauncherUpdateServiceTests`.
  **(6) The update prompt is now a persistent non-modal PILL, not an auto-modal.**
  The old flow auto-popped a modal on startup and `Cancel` saved `SkippedLauncherTag`,
  permanently silencing it. Instead `LauncherUpdatePill` (`MainWindow.xaml`, a gold
  pill in the title bar, `IsHitTestVisibleInChrome` so the drag handler doesn't eat
  the click) is shown on **every** launch a newer version exists — no permanent
  dismiss — pulses once per session (`PulseLauncherUpdatePill` /
  `StopLauncherUpdatePillPulse`) and on click (`LauncherUpdatePill_Click`) opens the
  existing download/restart dialog. Strings `LauncherUpdatePill` /
  `LauncherUpdatePillTooltip`.

## Architecture

WPF MVVM-lite single project. UI is thin; the **`Services/` layer is the
engine** and the UI binds to it.

- **`MainWindow` + `Controls/`** — the shell and tabs (`MainTabs`, `StatusCard`,
  `ProgressPanel`, `ActionPanel`, `ModsBrowser`, `MultiplayerTab`, `HeroBanner`).
  Most top-level `*Dialog.xaml` files are modals opened via `.ShowDialog()`
  (install, uninstall, self-update, user-data backup/restore, translations,
  Discord sign-in, create-lobby, etc.). The three exceptions are
  `LauncherSettingsDialog`, `ModPropertiesDialog` and `LobbyWindow`, which
  are non-modal + resizable + single-instance — see the dedicated bullet
  under Runtime conventions for the contract.
- **`Models/`** — plain schema/DTO types: `LauncherConfig` (`launcher-config.json`,
  lives next to the `.exe`), `UpdateInfo` (`UpdateInfo.xml` schema),
  `InstallManifest` (`install-manifest.json`, drives uninstall — and now also
  carries the `KeyFileHashes`/`FileHashes`/`EngineFileHashes` + `FileFingerprint`
  integrity data for verify/repair/version-recognition; see that gotcha),
  `ModProfile` / catalog types, and `Models/Multiplayer/` wire types.
- **`Services/`** — install pipeline (`NativeInstallService`, `InstallerService`,
  `FolderCloneService`), update orchestration (`UpdateService`,
  `UpdateInfoService`, `ArchiveService`, `DownloadService`), detection
  (`AoE3Detector`, `RegistryService`), hashing
  (`HashService` = MD5 + CRC32 + SHA-256), per-file install verification
  (`VerifyService`), self-update (`LauncherUpdateService`),
  Radmin VPN assist (`RadminVpnService` = registry + NIC probe,
  `RadminLogService` = `service.log` parser for network membership,
  `RadminAssistantService` = stage classifier the overlay binds to),
  uninstall, user data, translations, mod catalog, elevation, game launch.
  `Services/Multiplayer/` is the lobby client (`MultiplayerSession`,
  `LobbyApiClient`, `LobbyWebSocket`, `ModHashService`, `ReplayUploadService`).
- **`Styles/`** + `Localization/Strings.cs` — dark-only "dorado imperial" theme;
  all UI strings are EN/ES (diagnostic logs stay English on purpose). The
  dictionaries are merged app-wide in `App.xaml`: `Colors.xaml` (palette),
  `Buttons.xaml` (incl. the implicit global `Button` style — every bare button
  is themed by it, so there are no "white" buttons to chase), `Chrome.xaml`, and
  `Inputs.xaml` (implicit global `ComboBox`/`TextBox`/`CheckBox`/`RadioButton`
  styles). **Input theming is global — don't recolour inputs per-dialog.** A
  ComboBox in particular MUST be *retemplated*, not just recoloured: WPF's
  default ComboBox template paints its toggle + dropdown popup with the OS light
  theme and ignores `Background`, so colour-only styles leave a white dropdown
  (that was the original "language dropdown looks white" bug). The Multiplayer
  dialogs intentionally keep their own keyed *blue* input styles (CreateLobby's
  `MpFormCombo`/`MpFormTextBox`/`MpCheckBox`, applied explicitly — see
  `Colors.xaml`). To extend the global look in one dialog (e.g. add row spacing),
  use `BasedOn="{StaticResource {x:Type ComboBox}}"` instead of redefining the
  template.

- **Typography sizing is centralized in `App.xaml` as a semantic scale — new UI
  must bind to a token, NOT hardcode `FontSize`.** Seven role-named `sys:Double`
  resources (`FontSizeCaption=13`, `FontSizeBody=14`, `FontSizeBodyStrong=15`,
  `FontSizeSubtitle=16`, `FontSizeTitle=18`, `FontSizeHeading=24`,
  `FontSizeDisplay=34`) live next to `DisplayFont`/`BodyFont`. Bind via
  `FontSize="{StaticResource FontSizeBody}"` in XAML, or — in code-behind that
  builds elements — `(double)FindResource("FontSizeBody")` from an instance
  method (cache it; see `ModsBrowser`'s ctor `_fsCaption`/`_fsBody`/
  `_fsBodyStrong`) **or `(double)Application.Current.FindResource("FontSizeBody")`
  from a `static` builder/helper** (instance `FindResource` won't compile there —
  `MultiplayerTab`'s `BuildBadge`, `MpAlertOverlay`, and `MainWindow`'s static card
  builder all use the `Application.Current` form). `StaticResource` on purpose
  (app-lifetime constants — no runtime text-scale feature). **Floor is 13, body
  14 — calibrated to Steam's client** (its comfortable body ≈14px); 10-11px
  secondary text was unreadable on 125/150% displays (the original "text is too
  small" report). **The whole launcher is migrated** — every surface (dashboard
  chrome, Workshop, MultiplayerTab, LobbyWindow, all the `*Dialog`s, the shared
  `Buttons.xaml`/`Inputs.xaml` implicit + keyed styles) reads the tokens. **Two
  deliberate classes of non-adopter stay literal:** (1) the **dashboard hero
  subtree** (`HeroContentGrid` in `MainWindow.xaml`, ~lines 742-1019) — it scales
  as a unit via the shared `UiScale` transform, so its title/PLAY/progress sizes
  are hand-tuned literals (see the hero bullet), never per-element
  tokens; and (2) **icon glyphs / disc-geometry**: anything with
  `FontFamily="Segoe MDL2 Assets"` (chrome ✕/min/max/chevron/checkmark), large
  decorative symbols sized to a fixed square/circle (the lobby `⚔` at 28, the
  rooms `★` fallback, the `MpIconButton` style), and avatar/icon **monograms** (a
  single letter filling a fixed circle — Workshop card icon `18`, the MP avatar
  initials, the dashboard mod-icon letter). These are sized to their container or
  are pictographic, not typographic. **Don't tokenize a hero element or an
  icon-glyph, and don't hardcode a FontSize on ordinary text.**

### Three core flows (detailed diagrams in docs/ARCHITECTURE.md)

1. **Install** — detect AoE3 → download multi-part payload ZIP → clone AoE3 into
   a standalone mod folder → flatten Steam-layout `bin\` into root → overlay mod
   files (capturing per-file SHA-256 fingerprints during the copy) → shortcuts +
   uninstall registry entries + `install-manifest.json` (with the hash maps). After
   a fresh install that's recognised but behind the latest version,
   `MaybeAutoContinueUpdateAfterInstall` auto-continues into the update flow
   (`WolPatcher` only, valid install, pending patches, not pinned) so install→update
   is one click (`StatusContinuingUpdate`).
   **`InstallFolderDialog` hard-requires an AoE3 source: the OK/"Install" button
   stays disabled until `Aoe3SourcePath` is set** (auto-detected, picked via the
   in-dialog AoE3 Browse button, or *inferred live* when the chosen destination
   sits inside a folder that `AoE3Detector.LooksLikeAoE3`). The mod overlays a
   full AoE3 clone, so installing with no source would produce an unplayable
   mod-only folder — `NativeInstallService` still has a mod-only branch
   (`aoe3SourcePath == null`, the `InstallModOnlyAsync` path / the inline
   `isModOnly` weights in MainWindow), but it's now **unreachable from the
   dialog** and kept only as defensive plumbing. Don't re-enable a path that
   lets the dialog confirm without AoE3 (the old `InstallAoe3NotDetected` copy
   used to say "or the mod will install without copying AoE3 files" — that was
   misleading and was rewritten to "AoE3 is required").
   **Uninstall is a blanket recursive delete** of the install folder, gated only
   by a probe/manifest check that it looks like a mod install — it ignores the
   manifest's file list and has **no per-file base-game protection**. AoE3 base
   files survive only because `IsolatedFolder` mods are a separate clone; an
   `InPlaceOverlay` mod's underlying AoE3 files *would* be deleted. (The README's
   "hard-coded base-game protection" claim is false.) The lone hard-coded
   exception is the stock-game profile: `UninstallService.Plan` refuses any
   `IsStockGame` profile outright (its "install folder" is the user's real AoE3
   install — see the `IsStockGame` gotcha).
2. **Update** — 100% compatible with the original Java updater: fetch
   `UpdateInfo.xml`, MD5 three key files (`data/protoy.xml`, `techtreey.xml`,
   `stringtabley.xml`) to identify the installed version (falling back to the
   install manifest's baseline when the byte-faithful payload matches no UpdateInfo
   version — see the manifest-recognition gotcha), then download `.tar.xz`
   patches (resume + mirror fallback), CRC32-verify, back up, and extract — then
   re-fingerprint the touched files into the manifest so the patched install stays
   verifiable.
3. **Multiplayer** — Discord sign-in (JWT cached in config) → REST/WebSocket to a
   self-hosted Node/Fastify backend (`wol-lobby.duckdns.org`) for lobbies + chat,
   gated by a mod fingerprint (`ModHashService`) → players join a shared **Radmin
   VPN** network manually for the actual LAN; the host's game launch appends
   `OverrideAddress="<radmin-ip>"` plus skip-intro flags. Match-history/ELO and
   replay upload are scaffolded but not wired.

### Multi-mod profile system

Each mod is a `ModProfile` (branding, paths, payload URLs, update server).
`ModRegistry` holds built-in profiles (`_builtIn` — the two first-party entries:
WoL and the detect-only stock `aoe3-tad`) and merges in community mods fetched
from a remote catalog repo (`RefreshFromCatalogAsync`). **Do not add community
mods to `ModRegistry._builtIn`** — they go to the catalog repo (the in-app
"Publish my mod" wizard opens a PR there). The two first-party built-ins are the
deliberate exception; both are *also* mirrored in the catalog (`mods/wol/`,
`mods/aoe3-tad/`) for the public listing, and the built-in shadows the catalog
entry on id collision so a community PR can't redirect them.

Switching the active mod at runtime swaps `MainWindow._updateService` for a new
instance bound to the chosen profile (no process restart). `CheckAsync` results
and AoE3 detection are cached per session (`_checkResultCache`,
`_aoe3DetectedCache`) and invalidated on install/uninstall/update, so a
state-changing action forces a fresh check.

**`docs/MODDING.md` is the authoritative `mod.json` spec** — read it before
touching profile/catalog code. It defines install types (`IsolatedFolder` is
the default, `InPlaceOverlay`), update mechanisms (`GitHubReleases` recommended,
`WolPatcher` for the legacy `UpdateInfo.xml`+`.tar.xz` pipeline,
`DelegatedExternal`, `Manual`), and the tier-based auto-merge + SHA-256 security
model enforced by the catalog repo's CI. The JSON schema lives at
`aoe3-mods-catalog-template/schema/mod.schema.json`.

## Runtime conventions

- **Runtime-generated files live in `%LocalAppData%\AoE3ModLauncher\`, NOT next
  to the `.exe` — centralized in `Services/AppPaths.cs`.** `AppPaths.DataDir` is
  the per-user data dir (the same base `ModAssetCacheService`'s `mod-assets` cache
  uses); `ConfigFile` / `LogFile` / `TelemetryFile` / `SnapshotFile(name)` resolve
  the four generated files there. They USED to sit next to the `.exe` (via
  `AppContext.BaseDirectory`), which cluttered whatever folder the user ran it from.
  `App.OnStartup` calls `AppPaths.EnsureReady()` FIRST (before any config/log
  access) — it creates the dir and does a one-time migration: if the new
  `ConfigFile` doesn't exist but a legacy next-to-exe `launcher-config.json` does,
  it COPIES it over (not move — a rollback to an older build still finds its
  config). `LauncherConfig.Load()/Save()`, `DiagnosticLog` (log + `SaveSnapshot`),
  `MultiplayerTelemetry`, `UninstallService.ResetLauncherConfig`, and MainWindow's
  "View logs" all read through `AppPaths` now — don't reintroduce a raw
  `AppContext.BaseDirectory` path for these. The user opens this folder via
  **Launcher Settings → Maintenance → "Open data folder"**
  (`OpenDataFolderButton`). The self-update still writes `.old` / `_new.exe` NEXT
  to the running `.exe` (correct — that's the executable, not user data), and
  decoupling the config from the exe location actually makes self-update more
  robust (the new exe finds the config regardless). **Not an antivirus concern:**
  writing benign data to `%LocalAppData%` is the standard Windows pattern and
  doesn't touch the binary — unrelated to the single-file compression packer
  heuristic.
- **Logging:** call `DiagnosticLog.Write(...)` (or `WriteSection`). It's a
  non-blocking queued logger that resets at each launch and writes
  `launcher-debug.log`. Log messages are **always English** (they're for bug
  reports), even though the UI is localized. **Bug-report bundle:**
  `DiagnosticLog.ExportBundle(zipPath)` zips the shareable diagnostics — every
  top-level `*.log` and `*snapshot*` file in `AppPaths.DataDir`, copied to a temp
  **staging** folder first (so a concurrent log write can't corrupt the zip) — and
  it **NEVER includes `launcher-config.json`** (it holds the Discord session token;
  this privacy exclusion is load-bearing — `AppPaths.ConfigFileName` was made
  `internal` precisely so ExportBundle can name and skip it). The user triggers it
  from ModProperties' **"📤 Share diagnostics"** button (`shareDiagnostics`
  callback → `MainWindow.ShareDiagnostics`: Save dialog defaulting to the Desktop
  → `ExportBundle` → reveal in Explorer, ready to drag into Discord). Strings
  `ModPropShareDiagnostics*`. Separately, `MultiplayerTelemetry`
  appends a plaintext `multiplayer-events.log` next to the `.exe`. It is now
  **opt-in and OFF by default** (`LauncherConfig.MultiplayerTelemetryEnabled`,
  wired to `MultiplayerTelemetry.Enabled` in `MainWindow`'s ctor at startup and
  re-applied on `LauncherSettingsDialog` save) — a fresh install writes nothing
  until the user enables it in **Launcher Settings → Privacy**. Disclosed in
  `PRIVACY.md` (a SignPath Foundation OSS requirement: collected data must be
  both disclosed and disableable). The policy is also linked from the Discord
  sign-in dialog (`GitHubLoginDialog`) — the point where multiplayer data
  collection begins — and `LauncherConfig.PrivacyPolicyUrl` is the single source
  for that URL (used by both the settings button and the sign-in hyperlink).
- **Localization is mandatory for user-facing strings.** Add every UI string to
  the `Table` in `Localization/Strings.cs` with both `en` and `es` entries, and
  read it via `Strings.Get(key)` / `Strings.Format(key, args)` — never inline a
  literal in XAML/code. A missing key renders as the key itself (a visible
  signal). `Strings.SetLanguage` raises `LanguageChanged` for live refresh.
  The lobby window (`LobbyWindow`) splits its localisation across **two**
  methods in `MultiplayerTab.xaml.cs`: `ApplyLobbyStaticLabels()` for static
  labels (section/field headers, button captions, chat placeholder, copy
  tooltip, empty-chat hint) and `RenderRoomPanel()` for the dynamic,
  state-driven text (status line, player count, ready toggle, password value,
  title). Both run from `OpenLobbyWindow()` **before** `Show()` (so there's no
  English/empty flash on open) and again from `ApplyStrings()` on a mid-room
  `LanguageChanged`. The two match-phase overlays (`CountdownOverlay` /
  `InGameOverlay`) are localised the same way — their static labels (countdown
  label/hint/cancel, in-game title + the MATCH TIME / TRAFFIC / CONNECTION / ROOM
  stat headers) through `ApplyLobbyStaticLabels()`, and their state-driven text
  plus every `AppendChatSystem(...)` message through `Strings.Get` /
  `Strings.Format` — so the whole multiplayer surface is localised now.
  (Diagnostic logs stay English on purpose, as everywhere.) **Countdown +
  in-game layout (load-bearing) — the chat is ALWAYS reachable:** both
  match-phase surfaces are deliberately scoped so the right-hand chat panel
  stays visible and interactive through the whole flow (countdown AND live
  match), because the maintainer's explicit rule is "chat siempre
  accesible". Neither is a full-content scrim anymore (an earlier iteration
  had a top glowing bar + a full-room InGame cover; both are gone).
  **(1) The countdown is a single LIVE LINE INSIDE the chat.**
  `CountdownOverlay` (the name is kept so the code-behind visibility toggle
  + `CountdownLabel` / `CountdownNumber` wiring need no change — despite no
  longer being an overlay) is a `Border` sitting at **`Grid.Row="2"` of the
  chat panel's inner grid**, between the chat log (Row 1) and the input bar
  (Row 3). The row is `Height="Auto"`, so `Collapsed` = **0 px** (chat looks
  normal); during "Starting…" `ApplyMatchPhaseUi` flips it `Visible` and
  `UpdateCountdownTick` rewrites `CountdownNumber` in place (5→4→3…). It's a
  compact `⏱ + label + number` line — **no hint, no button** (the old
  `CountdownHint` TextBlock and its `ApplyLobbyStaticLabels` line were
  removed; don't re-add a `CountdownHint` reference or it won't compile).
  **If you add/remove a row in the chat grid, update these `Grid.Row`s** (the
  chat grid is now header=0 / log=1 / countdown=2 / input=3).
  **(2) The big left-column `StartButton` doubles as the countdown's
  Cancel.** During Starting, `ApplyMatchPhaseUi` swaps its Style to
  `MpDangerButton` (red), caption "✕ Cancel", shown + enabled for
  **everyone** (host AND joiner) so anyone can abort; `StartButton_Click`
  early-returns to `CancelCountdownByUser()` whenever
  `_matchPhase == Starting`. Outside the countdown, ownership returns to
  `RenderRoomPanel`, which **only touches `StartButton` while
  `_matchPhase == Lobby`** (blue "Start game", host-only) — load-bearing
  guard: without it a `room_state` refresh mid-countdown would stomp the red
  Cancel back to "Start game". The Style swap is safe because `StartButton`'s
  `Padding`/`FontSize`/`HorizontalContentAlignment` are **local XAML
  attributes** (precedence over Style setters), so the button keeps its size
  when the brush changes. There is **no separate countdown-cancel button or
  `OnCountdownCancel` callback anymore** — both removed; don't reintroduce
  them. **Do NOT re-add a `StartCountdownGlow()` call to `ApplyMatchPhaseUi`** —
  that method was **removed**; only a comment at `MultiplayerTab.xaml.cs` (next to
  the `StartCountdown` definition) documents why it must not come back.
  The in-chat `CountdownOverlay` Border uses a shared **frozen**
  `DynamicResource` (`MpBlue`) BorderBrush and has **no `Effect`**, so
  `StartCountdownGlow` (which animates the brush colour + a DropShadowEffect)
  threw `InvalidOperationException` on the frozen Freezable. Because the glow
  call sat in `ApplyMatchPhaseUi` *between* the overlay-Visibility line and
  the Start→Cancel button-swap — and inside the `StartCountdown` call chain
  *before* the tick timer started — that throw produced a nasty triple
  symptom: the countdown line appeared but **froze at the XAML-default
  number** (timer never armed), the Start button **never turned into
  Cancel**, and the **"starting in N" chat line never posted** (the throw
  unwound past it into `OnRoomFrame`'s catch). Fixed by removing the glow
  calls. If you ever want the chat line to glow, FIRST give its Border a
  **local unfrozen `SolidColorBrush`** + a `DropShadowEffect` (pill-glow
  recipe) — only then re-add the call.
  **(3) The `InGameOverlay` is CONFINED to the left column, not a full
  cover.** When the countdown hits 0 and AoE3 launches (`EnterInGamePhase`),
  the room controls must be blocked — but the chat stays live. So
  `InGameOverlay` is a `Border` at **`Grid.Column="0"` INSIDE the two-column
  body grid** (it's a sibling of the left column and the chat, NOT a
  top-level cover): its opaque `BgBase` fill sits over the roster +
  Ready/Start/Leave actions (blocking them by z-order — later child wins)
  while the chat in Column 2 is untouched and fully usable. To fit the 340 px
  column its stats were relaid out from a 4-wide row into a **2×2 grid**
  (MATCH TIME / TRAFFIC over CONNECTION / ROOM) with a stacked header; all
  the `InGame*` x:Names are preserved so `RefreshInGamePanel` needs no
  change. Don't move it back out to a top-level full-content cover — that
  re-hides the chat and breaks the "chat siempre accesible" rule.
  **(4) Countdown duration is 10 s, agreed by BOTH ends.** `game_countdown`'s
  handler does `durationMs = Math.Max(10000, durationMs)` and both offline-host
  fallbacks call `StartCountdown(10000)`; the chat-line's XAML default
  `CountdownNumber` is "10". The **backend's `LobbyRoom.COUNTDOWN_MS` is now also
  10000** (was 3000 — they MUST agree, because the abort-grace window below is
  measured off the same Start moment). Bump all together if you change it.
- **Host migration + abort-grace window — the lobby outlives its creator, and
  aborting a launched match is time-boxed.** Two coupled multiplayer rules added
  together; backend = `wol-launcher-lobby-node` (`src/lobbies/LobbyRoom.ts`,
  `rest.ts`), launcher = this repo — **they ship and deploy together** (new WS
  frames). Old clients ignore the new frames; a new launcher tolerates an old
  backend (degrades to no migration).
  **(a) Host migration (GameRanger-style).** When the host leaves, the backend no
  longer closes the lobby — it hands it to the next member by **JOIN ORDER ∩ LIVE
  (attached) socket** and only closes when nobody live remains. BOTH leave paths
  do it: REST `/leave` and the abrupt `ws.on('close')` (the crash/alt-F4 path that
  never hits `/leave`). CRITICAL: picking by `lobby_members.joined_at` ALONE would
  migrate to a **ghost** — abrupt closes don't sync the DB, so the table keeps rows
  for crashed players; you MUST intersect with the live `attached` set. The close
  path now also does the bookkeeping `/leave` used to (delete the leaver's row +
  recompute `current_players`) for ANYONE — a leftover row blocks that user's "1
  active lobby" guard and leaves `current_players` stuck (lobby reads full).
  `reassignHost` commits the DB `host_user_id` BEFORE broadcasting `host_changed`
  and is idempotent (guards `hostUserId === leavingUserId`) so the two paths racing
  is safe. Launcher: `HandleHostChanged` updates `_roomHostUserId` /
  `_isHostInCurrentRoom`, `RenderRoomPanel` (Lobby phase) hands the new host the
  Start button, chat shows `MpChatHostChanged`. Pinned by
  `scripts/test-host-migration.ts` (3-socket, abrupt-close, asserts no ghost).
  **(b) Abort-grace window.** Cancelling a match is **no longer host-only**: ANY
  member can abort for EVERYONE, but ONLY within the grace window — the countdown
  (`Starting`) plus **60 s after launch**. Server-authoritative: `handleCancelGame`
  checks `Date.now() - startedAtMs < COUNTDOWN_MS + 60000` (`startedAtMs` is
  in-memory from `handleStart`, NOT the DB `started_at`, to compare on one clock
  without date parsing); past it → `grace_window_closed`. Launcher mirrors the UX
  off `WithinAbortWindow` (local 60 s from `_matchTimerStartTicks`): the in-game
  button flips `MpInGameAbort` ("Abort match", any member) ↔ `MpInGameLeave`
  ("Leave", just you) each 1 s tick, and `EndMatchAsync(reason, sendCancel)` only
  sends `cancel_game` when within the window. Rationale (vs Voobly/GameRanger):
  the room migrates and the match continues for those who stay, so a host who is
  losing must NOT be able to kill everyone's game — abort is time-boxed to the
  start (a bad/desynced launch). To restrict abort to host-only later, it's a
  one-line guard in `handleCancelGame`.
  **(c) Kick.** The host can expel a member: `kick { user_id }` (host-only,
  validated in `LobbyRoom.handleKick`) sends the target a `kicked` frame then
  closes its socket — the existing `ws.on('close')` cleanup drops it from the
  roster for everyone (no new removal logic). **Simple kick, no ban list**: the
  target may re-join (to block re-join, add a per-room `Set<userId>` checked in
  `rest.ts` join). Launcher: a host-only ✕ button per roster row (`BuildMemberRow`,
  hidden on the host's own row, tracks `_isHostInCurrentRoom` so host migration
  keeps it correct) → confirm via `MpAlertOverlay` → `SendKickAsync`; the kicked
  client's `HandleKicked` closes the lobby window (disposing the socket, so no
  reconnect loop) and shows an `MpKicked*` notice. Pinned by the kick case in
  `scripts/test-host-migration.ts`.
- **Multiplayer alerts are themed in-window cards, NOT `MessageBox` — via
  the `MpAlertOverlay` helper.** `Controls/MpAlertOverlay.cs` is a static
  helper that injects a scrim + a centred card (MpSurface fill, two-tone
  rim, ⚠/ℹ glyph, title + body, `MpDangerButton`/`MpPrimaryButton` primary +
  `MpSecondaryButton` cancel) as the **last child of a host `Grid`**, and
  returns `Task<bool>` (true = primary/confirm/ack, false = cancel/Esc/
  scrim-click; a notice is OK-only and always resolves true). Two entry
  points: `ConfirmAsync` (two buttons) and `NoticeAsync` (one). It replaced
  **all** the multiplayer `MessageBox.Show` calls — the cancel-game confirm
  (the one from the screenshot, host = "cancel for everyone" danger / joiner
  = "leave the game"), hosted in `_lobbyWindow.LobbyRootGrid`; and the
  join/create/fingerprint/mod-mismatch/Radmin error notices, hosted in the
  tab's `TabRootGrid`. Both host grids are named in XAML for this. **The ONE
  remaining `MessageBox` is deliberate:** `ConfirmCloseDuringMatchAsync` runs
  synchronously from `MainWindow.OnClosing` via `task.Wait(...)`, so an
  in-window async overlay would deadlock the UI thread — it must stay a
  blocking modal. Don't "finish the job" by converting it. All alert strings
  are EN/ES `MpAlert*` / `MpConfirm*` / `MpNotice*` keys in `Strings.cs`.
  **Gotcha that already bit once:** the card builds its text purely from
  `Strings.Get(key)`, and a key that's MISSING from the `Strings.Table`
  renders as **the raw key** ("MpConfirmCancelHostTitle" shown literally in
  the card) — `Strings.Get` returns the key itself as its visible
  not-found signal, and the C# compiler can't catch it because the keys are
  plain string literals, so **the build stays green while the UI shows the
  key names.** When you add an `MpAlertOverlay` call with a new key, add the
  matching EN/ES entry to `Strings.cs` in the SAME change and actually run
  the app (or grep that every `Mp{Alert,Confirm,Notice}*` key used in
  `MultiplayerTab.xaml.cs` exists in `Strings.cs`) — a clean build is NOT
  proof the strings landed.
- **The dashboard hero title stacks `"Game: Subtitle"` names onto two lines.**
  Where `DashboardTitleText.Text` is set, the name renders as
  `DisplayName.ToUpperInvariant().Replace(": ", ":\n")` — so "Age of Empires III:
  The Asian Dynasties" shows as two lines (the colon stays on the first). Names
  without a "colon + space" (WoL, Improvement Mod) stay one line, and the hero
  copy column is capped at `MaxWidth=640` (down from 900) so text reads
  vertically instead of sprawling / clipping. Render-only — the canonical
  `DisplayName` is untouched.
- **WPF threading:** long-running work (download/install/check) is `async` and
  reports progress via `IProgress`/events; marshal UI updates back to the
  dispatcher. Periodic UI work uses `DispatcherTimer`.
- **HiDPI text crispness is set globally — don't add it per-XAML.**
  `App.OnStartup` registers a class handler for `Window.Loaded` that sets
  `UseLayoutRounding = true` plus the `TextOptions` trio
  (`TextFormattingMode.Display`, `TextRenderingMode.ClearType`,
  `TextHintingMode.Fixed`) on every `Window` instance. Without these, text on
  125% / 150% DPI displays (the modern default) renders visibly blurry because
  WPF positions elements at sub-pixel coordinates that ClearType smudges. The
  class handler catches every `Window` subclass uniformly, current and future,
  so **don't add these as XAML attributes on new Windows** — they're applied
  globally. Three legacy Windows (`MainWindow`, `RadminAssistantWindow`,
  `ModPropertiesDialog`) still carry redundant `TextOptions.*` XAML attributes
  from before this was centralised; harmless (the values match) but not the
  pattern to copy.
- **UI scaling is automatic and native — don't write DPI-detection code; route
  any window-size zoom through `UiScale` (its own bullet below).** The app
  declares **PerMonitorV2** DPI awareness (`app.manifest`
  → `<dpiAwareness>PerMonitorV2</dpiAwareness>`, wired via `<ApplicationManifest>`
  in the `.csproj`), and WPF measures everything in **DIPs** (1/96"), which are
  already relative to the display scale. So the entire UI — fonts, buttons, icons,
  margins, sidebar, cards, **and modals** — scales proportionally and crisply at
  100/125/150/175/200% with zero per-element code, and WPF recomputes the layout
  automatically on window resize (reflow) and on DPI/monitor change
  (`WM_DPICHANGED`, handled natively under PerMonitorV2). There is **no** manual
  scale picker and none is needed; the consistent base scale is the App.xaml
  typography tokens. **Web concepts do not apply** (this is not a WebView): there
  are no `rem`/`em`/`clamp`/`vw`/`vh`, no `devicePixelRatio` — the WPF
  equivalent of a "relative unit" is the DIP itself. **On TOP of DPI there is now
  a window-size *zoom* layer** (`Controls/UiScale.cs`, see its bullet): it
  generalises the old hero-only transform to every main surface (Library hero,
  Workshop, Multiplayer, the lobby, the settings/properties dialogs), shrinking
  content to fit windows smaller than each surface's default footprint (capped at
  1.0 so the default + larger render unchanged, floored at 0.82). DPI and the
  window-size zoom compose; neither needs per-element code. **DPI
  scaling is uniform** (it scales text and its container by the same factor), so
  DPI alone never clips text — the only real clip vector is a longer **localized**
  string (ES > EN) in a fixed-width single-line label. **Robustness convention:** a
  one-line `TextBlock` with dynamic/localized text living in a fixed-width context
  (a fixed `Width`/`ColumnDefinition`) must carry `TextTrimming="CharacterEllipsis"`
  so the worst case is an ellipsis, never a hard clip or overflow (e.g. the rooms-
  table `ColHeader*`, `RoomPasswordText`). Don't fix a fixed-`Height` **image**
  strip (e.g. `ModsBrowser`'s `DetailBanner`) to `Auto` — it holds no wrapping text
  and `Auto` would collapse it.
- **Window-size UI scaling is centralised in `Controls/UiScale.cs` — don't roll a
  per-view transform.** `UiScale.Attach(scaled, sizeSource, refW, refH, kind,
  origin)` installs a `ScaleTransform` on a FOREGROUND content root, driven by
  `sizeSource`'s footprint over a fixed reference, clamped to **`[0.82, 1.0]`**
  (never grows past design size: the default window and larger render at the
  crisp 1.0 ClearType path; only smaller windows shrink). Each caller passes its
  OWN default content footprint as the reference, so a default-sized window is
  exactly **1.0 — zero regression** vs the pre-scaler build; the scaler only ADDS
  "shrink to fit" below that. Crispness rides the hero's recipe
  (`SetTextCrispForScale`: Ideal/Grayscale/Animated below 1.0,
  Display/ClearType/Fixed at 1.0). **Two load-bearing rules:** (1) attach only to
  a foreground content root — NEVER a full-bleed background (it must keep filling
  the window) or an alert-overlay host; (2) `sizeSource` must be a container the
  transform does NOT resize (the element's parent / the window) — a
  `LayoutTransform` changes the scaled element's own ActualWidth, so
  `sizeSource == scaled` oscillates. `Kind.Render` (RenderTransform, no reflow) is
  the bottom-pinned **hero only**; everything else uses `Kind.Layout` (reflows,
  fills the slot, feeds the enclosing ScrollViewer). Wired: hero (`PlayView`, ref
  1500x760, Render); `ModsBrowser` + `MultiplayerTab` (sizeSource = the
  UserControl, ref 1100x604); `LobbyWindow` (ref 900x600);
  `LauncherSettingsDialog` / `ModPropertiesDialog` (their `Grid.Row=1` content,
  sized by the Window). `RadminAssistantWindow` + `CreateLobbyDialog` are
  `NoResize` → not scaled. `UiScale.Track(ContentHost, 1100, 604)` publishes
  `UiScale.Current` (the general content factor) for the two code-built popups
  (brand, mod-switch), which live in their own visual tree and read it via
  `ApplyPopupScale`; the gear `ContextMenu` + `ComboBox` dropdowns stay base-size
  (a transient menu over scaled content is an accepted minor mismatch).
- **Geometry tokens mirror the FontSize scale — bind a token, don't hardcode a
  size. They live in `Styles/Tokens.xaml`, merged FIRST in `App.xaml` (a
  load-bearing placement, see below).** The dictionary defines (all
  `StaticResource`): spacing `Thickness` tokens (`SpaceXs`..`SpaceXl`,
  `CardPadding`=14,12, `SectionPadding`=24,16); corner-radius `CornerRadius`
  tokens (`RadiusSm`=4 / `RadiusMd`=6 / `RadiusLg`=10, plus the two-tone popup-rim
  pairs `RadiusPopupInner/Outer`=6/7 and `RadiusPopupSubmenuInner/Outer`=4/5);
  icon-disc sizes (`DiscSizeSm/Md/Xxl`=24/36/72 with paired `DiscRadius*`); and
  button-padding `Thickness` tokens (`BtnPad{Compact,Default,Row,Roomy,Cta}`).
  Circular discs use the `IconDiscSm/Md/Xxl` Border styles (in `Buttons.xaml`) so
  radius can't drift from size/2. Same StaticResource rationale as the FontSize
  scale — the window scaler transforms the RENDER, not these VALUES, so static
  stays correct. **WHY a SEPARATE dictionary merged FIRST, not inline in App.xaml
  like the FontSize tokens (this bit the build once — a startup
  `XamlParseException` "cannot find resource 'RadiusMd'"):** a `{StaticResource}`
  used INSIDE a `ControlTemplate` body in a merged dictionary (the button / input
  corner radii in `Buttons.xaml` / `Inputs.xaml`) resolves only against
  dictionaries merged BEFORE it — it canNOT see `App.xaml`'s inline resources
  (those are added after the `MergedDictionaries` block, so a template-body lookup
  never finds them and throws at load). The FontSize tokens survive inline only
  because they're referenced exclusively from Setters (deferred) + direct element
  attributes, never from a merged-dict template body. Geometry tokens ARE used in
  template bodies, so they must be merged ahead of Buttons/Inputs (same reason
  Colors.xaml is merged early — its brushes resolve inside those templates too).
  If you add a token used in a template, put it in `Tokens.xaml`, not inline.
  Code-behind card builders (`ModsBrowser` rows, MP cards/badges) use numeric
  literals that MATCH these token values on purpose (a hot per-card path;
  `FindResource` per card would crash on a typo'd key). **Load-bearing exemptions
  stay literal:** the popup-rim radii are a paired set (outer = inner + 1, NOT
  folded into the Radius scale), the gold brand-popup rim keeps its own value, and
  the hero subtree / Segoe MDL2 glyph + monogram sizes / intentional fixed widths
  (chat 380, lobby left 340, hero icon 64, `RadminAssistant` 430x540) are NOT
  tokenised.
- **Maximize-respects-taskbar is set globally — don't roll your own per-Window.**
  The same `App.OnStartup` class handler that wires HiDPI crispness also
  installs a `WM_GETMINMAXINFO` WndProc hook on every Window whose
  `WindowStyle="None"`. Currently that's seven Windows: `MainWindow`,
  `LauncherSettingsDialog`, `ModPropertiesDialog`, `RadminAssistantWindow`,
  `LobbyWindow` (all `ResizeMode="CanResize"`, so they actually use the
  fix when the user maximises), plus `CreateLobbyDialog` and
  `TranslationApplyDialog` (both `ResizeMode="NoResize"`, so the hook
  attaches but never fires — listed for completeness so future contributors
  know the inventory). Without the hook, maximising
  the resizable ones would expand them over the **entire monitor rect
  including the Windows taskbar** — a classic side-effect of opting out
  of OS chrome via `WindowStyle="None"` + `WindowChrome`. The hook
  responds with the current monitor's *work area* (monitor minus taskbar),
  so maximise stops at the taskbar's edge and the system tray / clock stay
  visible. Works correctly on multi-monitor setups and side-mounted
  taskbars because `MonitorFromWindow` + `GetMonitorInfo` resolve per-HWND.
  The hook is no-op for Windows with native chrome (`WindowStyle=SingleBorderWindow`
  / `ThreeDBorderWindow`) — Windows already maximises those correctly on
  its own. Don't replicate the interop boilerplate in individual Windows.
- **Rounded window corners are set globally — don't roll your own per-Window.**
  The same `App.OnStartup` class handler also calls
  `DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND)` on
  every Window whose `WindowStyle="None"` — the same seven-window inventory
  as the maximize fix above. Chromeless WPF Windows paint hard 90-degree
  corners that look dated next to every native Windows 11 window (which the
  OS rounds automatically), so this call asks DWM to clip the window surface
  — and its drop shadow — to a rounded rectangle at the compositor. No
  `AllowsTransparency="True"` (which would kill hardware-accelerated
  rendering and Aero Peek), no XAML changes, no extra paint cost; the OS
  desktop shows through the corner cut-outs exactly like any native Win11
  window. **Graceful degradation:** the attribute id (33) is unknown on
  Windows 10 and earlier, so `DwmSetWindowAttribute` returns an error
  HRESULT and the call is a silent no-op (we ignore the return) — corners
  just stay square on legacy OSes. The `WindowChrome.CornerRadius` on
  `MainWindow` stays `0` on purpose: the DWM rounding clips at the OS layer
  *outside* the WPF render surface, so the chrome's own hit-test rectangle
  should remain square — setting both creates a ~1-2 px hit-test mismatch
  at the corners. Don't add per-XAML `CornerRadius` to the outer Border of
  any Window to fake this — DWM owns the outer shape now.
- **The custom title bar is ONE global component — `Controls/TitleBar`. Don't
  hand-roll a title bar per window.** Every launcher Window (all 16: MainWindow,
  LobbyWindow, the settings/properties sheets, the install/update/translations/
  maintenance/room dialogs — formerly some had a per-window dark bar and seven
  used the white OS chrome) now drops a single `<controls:TitleBar .../>` into
  `Grid.Row="0"` instead of building its own header. It is a **templated
  `ContentControl`** (NOT a UserControl — a UserControl owns a namescope, so
  naming elements inside consumer-supplied content throws MC3093; a ContentControl
  hosts named content like a `Button` does). The template lives in
  `Styles/Chrome.xaml` (implicit `Style TargetType="controls:TitleBar"`), the
  caption-button styles (`TitleBarButton` / `TitleBarCloseButton`) are there too,
  and ALL geometry — bar height, button size, glyph + title sizes, gutters — comes
  from the `TitleBar*` tokens in `Styles/Tokens.xaml`. **Secondary windows use a slim
  28px bar; the main launcher header is the deliberate EXCEPTION at 36px.**
  `TitleBarHeight=28` is the single height knob for every secondary window (change it
  once and they all follow); `TitleBarHeightMain=36` pins MainWindow's brand-bar
  header so the compact secondaries never shrink it. **Bar height and title size are
  SEPARATE tokens, but the rule INVERTED** — the bar is the fixed dimension now and the
  title fits IT: `TitleBarTitleSize=16` sits centred in the 28px bar (gold
  DisplayFont). The old "title size wins, the bar grows to hold a 24px title" rule is
  GONE (as is the earlier 18px-unify experiment). Button geometry is compact too
  (`TitleBarButtonWidth=40`, `TitleBarGlyphSize=8`); all four compact values are
  DPI-clean at 125/150% (whole pixels, no sub-pixel blur). The control exposes the
  geometry as DependencyProperties (`Height`, `ButtonWidth`, `GlyphSize`, `TitleSize`)
  whose defaults are the compact tokens, and the template TemplateBinds the buttons +
  title to them; **MainWindow opts out by setting
  `Height="{StaticResource TitleBarHeightMain}"` + `ButtonWidth="46"` + `GlyphSize="10"`
  locally** (local values beat the implicit-style setters). Per-window config is
  **only** via DependencyProperties — `Title`,
  `TitleIcon` (ImageSource), `Content` (extra bar content: MainWindow's brand
  dropdown button, ModProperties' version badge, PublishMod/Radmin subtitle),
  `ShowMinimize` / `ShowMaximize` / `ShowClose`, the geometry DPs above — plus the
  window's own `ResizeMode`/`SizeToContent`. **No window-name conditions anywhere**
  (MainWindow's height opt-out is a plain local property value, not a name check). Button
  policy (property-driven, no name checks): **resizable non-modal** windows
  (`MainWindow`, `LobbyWindow`, `LauncherSettingsDialog`, `ModPropertiesDialog`)
  show **min+max+close** — and those must be `ShowInTaskbar="True"` so minimize
  goes to a real taskbar button instead of the unstylable desktop stub (the same
  reason `LobbyWindow` is True; `LauncherSettingsDialog` was flipped False→True
  for this); **resizable modal wizards** (`PublishModDialog`,
  `TranslationPackagerDialog`) show **max+close, NO minimize** (a modal must not
  minimize away from its blocked owner); **NoResize / SizeToContent dialogs**
  (`CreateLobbyDialog`, install/update/translations-apply/uninstall/user-data/
  password/login, `RadminAssistantWindow`) are **close-only**. Maximize auto-hides
  when the window can't actually maximize (NoResize/CanMinimize or
  SizeToContent≠Manual), so a misconfig can't show a dead button. **WindowChrome is applied CENTRALLY**
  in `App.OnAnyWindowLoaded` (`ApplyWindowChrome`) to every `WindowStyle=None`
  window — CaptionHeight = the window's own bar-height token (the slim
  `TitleBarHeight` for secondaries, `TitleBarHeightMain` for MainWindow, branched
  by qualified type) so the whole bar is the native drag region,
  ResizeBorderThickness = 6 if resizable else 0 — so **no
  window declares `<WindowChrome>` in XAML anymore**, and drag / double-click-
  maximize / restore-on-drag / min-size / DPI / multi-monitor all come free and
  native (no DragMove/OnStateChanged code per window). The buttons wire to
  `SystemCommands.Minimize/Maximize/Restore/CloseWindow`; close = `Window.Close()`
  so each window's `Closing`/`Closed` cleanup (LobbyWindow leave-room, settings
  refresh, modal DialogResult=cancel) runs unchanged. Caption-button tooltips are
  localized (`TitleBar*` keys in `Strings.cs`). A new window needs zero chrome
  code: set `WindowStyle="None"`, add `<controls:TitleBar .../>` in Row 0, done.
  **MainWindow header alignment + crispness:** the brand button is the TitleBar's
  `Content` and carries NO own left `Padding` (and no stray `Grid.Column`), so it
  sits flush at the TitleBar content gutter (`TitleBarContentPadding.Left` = 12);
  the nav strip's `NavTabButton` uses **left** padding 12 to match (right padding
  22 for spacing), so the brand and the LIBRARY/WORKSHOP/MULTIPLAYER tabs share
  one consistent left gutter snug to the corner — don't reintroduce a per-button
  left margin to "fix" alignment, keep the shared 12 gutter. The main tabs are sized via the dedicated
  `NavTabTextSize` token (14 = `FontSizeBody`, Steam-like; icons 16) in a 38px nav
  strip row (the main header's title bar stays 36 via `TitleBarHeightMain`; only the
  secondary windows' bars are the slim 28). **Crispness:** the TitleBar template root sets
  `SnapsToDevicePixels="True"` + `UseLayoutRounding="True"` (both INHERITED) so the
  brand's bitmap icon + wordmark pin to whole device pixels — without it the icon
  landed on a sub-pixel X at 125/150% DPI (18 DIP × 1.25 = 22.5 px) and looked
  blurry. The blur is NOT from any transform (the header is never scaled —
  `UiScale.Attach` is hero-only, `Track` doesn't transform); don't "fix" it with
  scaling or by shrinking text.
  **Don't re-add a per-window `WindowChrome`, a per-window close/min/max style, or
  an `OnStateChanged` glyph swap — the component owns all of it.** (The two
  formerly-floating dialogs `CreateLobbyDialog` / `TranslationApplyDialog` were
  de-transparency'd — `AllowsTransparency` removed, opaque `BgBase`, DWM rounds the
  corners — so they're ordinary chrome windows like the rest.)
- **Settings / Properties / Lobby dialogs share a non-modal + resizable
  + single-instance pattern.** `ModPropertiesDialog`, `LauncherSettingsDialog`
  and `LobbyWindow` are all `WindowStyle="None"` + `ResizeMode="CanResize"`.
  **(Chrome update: the title bar itself is now the shared `Controls/TitleBar`
  component — WindowChrome is applied centrally and the old per-window
  `DialogCloseButton`/`TitleBarChromeButton` styles + `OnStateChanged` glyph swap
  are GONE; see the global TitleBar bullet above. The rest of this bullet — the
  non-modal / single-instance / DialogResult lifecycle — still holds.)**
  Title-bar buttons diverge via the TitleBar's `ShowMinimize`/`ShowMaximize`/
  `ShowClose` DPs: `LauncherSettingsDialog` and
  `ModPropertiesDialog` show only the close ✕ (they're settings sheets —
  minimise/maximise would be unusual there), while `LobbyWindow` shows the full
  **minimise / maximise / close** trio — it's a `ShowInTaskbar="True"`, ownerless,
  independent window (see its dedicated bullet above), so minimise goes to its
  own Windows taskbar button like any normal app window.
  The maximise glyph swap + the WM_GETMINMAXINFO maximise-respects-taskbar bound
  are handled by the TitleBar component + the App.OnStartup hook respectively.
  `LobbyWindow` keeps the rich room info in its own
  sub-header strip below the chrome: a `RoomTitleText` + `RoomMetaText`
  status/P2P meta line, then `PLAYERS` and `ROOM ID` stat blocks where the
  `ROOM ID` carries a `CopyRoomIdButton` (📋, flashes ✓ for ~1.4 s on copy —
  that handler is the lone bit of lobby UI logic that lives in
  `LobbyWindow.xaml.cs` directly rather than forwarding to a `MultiplayerTab`
  callback, since it's pure clipboard with no session coupling). The old
  `HOST` stat was dropped (the roster already badges the host), and the room
  info card was renamed `ROOM & NETWORK INFO` → `ROOM INFO` and trimmed from
  four cells to `RoomInfoCard` = Mod + Password only (Connection duplicated the
  header's P2P status, Max players duplicated the PLAYERS stat); the card
  collapses when neither field has data. The two settings dialogs use the shared
  `controls:TitleBar` like everything else. They add an **auto-width** left rail of
  `SidebarNavButton` buttons (from `Styles/Buttons.xaml`): the rail
  `ColumnDefinition` is `Width="Auto"` clamped to the `SidebarRailMin/MaxWidth`
  tokens, so WPF sizes it to the WIDEST nav item (this fixed "CATÁLOGO DE MODS"
  clipping at the old hardcoded 200px). Label size is its OWN token
  `SidebarNavTextSize` (16, the size the style was designed around — independent
  of `FontSizeBody` and of the rail width: the rail grows to fit the text, the
  text is never shrunk to fit the rail). The labels use the keyed
  `SidebarNavLabel` style (`TextTrimming=CharacterEllipsis`) so a label past the
  max ellipsises instead of clipping silently. The `Tag="active"` trigger is
  white text + **bold** + gold stripe (the original emphasis); because the rail is
  `Width="Auto"`, selecting the LONGEST label can widen the rail by a few px —
  an accepted minor trade-off for keeping the bold. A `SetActiveTab(button)` helper toggles `Tag="active"` on the
  chosen button while flipping `Visibility` on the matching content `StackPanel`;
  the gold right-rail accent is driven entirely by the style's
  `Tag="active"` trigger — no per-dialog colour code. Tab labels reuse the
  same uppercase section strings (`GENERAL`, `UPDATES`, etc.) the in-content
  section headers used before the refactor.
  `LobbyWindow` doesn't have sidebar tabs (single content view) but follows
  the same chrome and lifecycle. Its body XAML used to live as a Canvas
  overlay (`RoomPanel`) inside `Controls/MultiplayerTab.xaml`; the popup is
  gone and the entire lobby UI moved to `LobbyWindow.xaml`. The window
  exposes `Action` callback properties (`OnLeaveRoom`, `OnReady`,
  `OnSendChat`, etc.) that `MultiplayerTab` populates on construction; the
  XAML `Click="…"` handlers in the window are tiny forwarders, while the
  lobby business logic (rendering, chat send, match-phase transitions)
  stays in `MultiplayerTab.xaml.cs` and accesses the window's UI elements
  directly through `_lobbyWindow!.X` (the field-modifier-internal x:Name
  fields auto-generated for the Window are reachable across the same
  assembly). Every Render*/Apply* method guards on `if (_lobbyWindow == null)
  return;` because session events can fire after the window has already
  closed (host disconnect race, RoomLeft frame on the wire, etc.).
  All three dialogs are opened from their parents via `.Show()` (not
  `.ShowDialog()`) so the user can keep clicking the main window while
  they're open. **They are also all OWNERLESS — none sets `Owner = MainWindow`,
  and `WindowStartupLocation="CenterScreen"`.** This is load-bearing, not an
  oversight: a `WindowStyle="None"` window that is BOTH owned AND
  `ShowInTaskbar="True"` (WPF sets `WS_EX_APPWINDOW`) **minimizes its owner when
  it's closed** — that was the "closing Settings/Properties minimizes the
  launcher" bug. LobbyWindow was always ownerless (its own dedicated bullet);
  LauncherSettings + ModProperties were switched to match (the construction sites
  dropped `{ Owner = this }` and now call `dialog.Activate()` after `Show()` so the
  window still comes to front on first open). The trade-off — they alt-tab
  independently and the launcher can sit on top of them — is accepted (same as
  LobbyWindow). **Don't re-add `Owner` to any of the three**, and don't set
  `ShowInTaskbar="False"` to "fix" something (that reintroduces the desktop-stub
  minimize bug). That has three further implications: (1) **never set `DialogResult`
  in these dialogs** — it throws `InvalidOperationException` when the
  window wasn't shown modally; use a custom field (`ChangesSaved` on
  LauncherSettings) or nothing at all. (2) Callers track each dialog in
  a single-instance field (`_launcherSettingsDialog`, `_modPropertiesDialog`,
  `_lobbyWindow`) and either `Activate()` the existing window or `Close()`
  it before opening a new one, so re-clicking the gear / re-entering a
  room doesn't stack windows. The race-safety pattern is the same in all
  three: clear the field FIRST, then `Close()`; the `Closed` handler uses
  `ReferenceEquals` before nulling so a freshly-opened replacement doesn't
  get clobbered. (3) The post-dialog refresh runs on `dialog.Closed += …`
  instead of after the `ShowDialog()` call returns. For `LobbyWindow`,
  the `Closed` handler also triggers `_session.LeaveCurrentLobbyAsync()`
  if we're still session-tracked as InLobby/InGame, so closing the
  window is equivalent to "leave the room" regardless of dismiss path
  (✕, Esc, Alt+F4, our own `CloseLobbyWindow()`).
  **`ModPropertiesDialog`'s action buttons close the dialog SELECTIVELY,
  by design — don't make them uniform.** A handler closes the Properties
  window *only* when its flow lands on the **main window**: Verify / Repair
  (their progress runs on the main-window progress strip, which a non-modal
  Properties window would otherwise cover) and Uninstall (the mod is gone
  afterwards, so the open view would be stale). The path pickers (Change mod
  folder / Change AoE3 folder) and the backup/restore dialogs **used to
  close too, but no longer do** (the maintainer found the vanishing window
  jarring): those are modals that appear **on top** with nothing to uncover,
  so the handlers now **stay open and refresh the displayed paths / version /
  user-data state in place** via the public `RefreshData()` method
  (`LoadGeneral` + `LoadLocalFiles` + `LoadUserData`, deliberately NOT
  `LoadLanguage` so the language combo / active tab aren't disturbed).
  `RefreshData()` is called right after the callback returns (backup/restore
  callbacks are synchronous, so the final state is visible immediately); for
  the folder pickers the new path is written to config before the callback's
  `await CheckAsync()`, so the in-handler call shows the path and **MainWindow
  additionally calls `_modPropertiesDialog?.RefreshData()` after the async
  re-detection completes** (in `BrowseButton_Click` / `BrowseAoE3Button_Click`)
  so the re-detected version catches up. Handlers that merely shell out to
  Explorer/Notepad with **no covering window** — Open folder, Open AoE3
  folder, Open user-data folder, View logs — also **stay open** (they always
  did), because closing left the user staring at the main window unsure
  anything happened. **"Buscar actualizaciones" (`CheckUpdatesBtn_Click`) is the
  special case:** it is `async`, does NOT close, disables itself, shows a
  "Comprobando…" line, awaits the real check on the main window (the
  callback is a `Func<Task<UpdateService.CheckResult?>>`, not a fire-and-
  forget `Action`), then paints the outcome **inline** via `SetCheckResult`
  — accent "update available" / green "up to date" / red "failed" / neutral
  "not installed" (`ModPropChecking` / `ModPropUpdateAvailable` /
  `ModPropUpToDate` / `ModPropCheckFailed` / `ModPropCheckNotInstalled`
  strings) and re-runs `LoadGeneral()` to refresh the version labels. The
  earlier version closed the dialog and fired an `Action`, which is exactly
  the "I clicked Actualizar and nothing seems to happen, and the menu just
  closed" report — the close + lack of feedback were the bug, fixed in
  commit `21c0062`. `LauncherSettingsDialog` has **no** close-on-action
  controls at all (its "Limpiar caché/iconos/temporales" buttons report
  via an inline coloured hint and stay open; only Cancel / Guardar close)
  — so if a build still closes Launcher Settings on a checkbox/clear click,
  it predates this fix and needs a rebuild. None of these selective closes
  set `DialogResult` (same non-modal rule as above).
  When adding a new multi-section settings surface, copy this pattern
  instead of rebuilding navigation, chrome and tab visuals from scratch
  — the gear-menu modals (Aoe3Picker, CreateLobby, etc.) still use the
  default white WPF chrome and `.ShowDialog()` and are next in line for
  the same treatment.

## Conventions

- File-scoped namespaces, `Nullable` enabled, `ImplicitUsings` enabled.
- Doc-comments explain **why** code exists, not what it does. Don't add comments
  that restate the code.
- **DCO sign-off is required on every commit** — `git commit -s` (adds
  `Signed-off-by:`). PRs with unsigned commits get bounced.
- One topic per PR; keep refactors out of feature PRs. Apache-2.0 licensed.
