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
- Release output: `publish/Aoe3ModLauncher.exe` (~165 MB, self-contained). The
  `publish/` folder is git-ignored — release binaries go to GitHub Releases.
  **Single-file compression is OFF on purpose** (`EnableCompressionInSingleFile=false`
  in the `.csproj`, ~line 50): the self-extracting decompression was the #1
  trigger for Defender's `Win32/Injector` packer heuristic, which quarantined the
  `.exe`. That's why the binary is this big instead of the old ~120-130 MB.
  **Re-enable compression (`true`, recovers ~70 MB) only once the binary is signed
  by a REAL trusted cert (SignPath)** — a trust-valid signature suppresses the
  packer FP; the self-signed `CN=Gorgorito` cert does not. **That is still THE size
  lever; everything below is small change.**
- **Size, measured — don't re-litigate the dead ends.** The bundle is **98 % runtime**
  (`Aoe3ModLauncher.dll` is only ~4 MB of ~165), so optimising launcher code cannot
  move the number; only removing runtime pieces can. What was actually tried:
  **`SatelliteResourceLanguages=en;es` is IN and pays ~14.7 MB** (measured
  179.9 → 165.2) — a self-contained WPF publish ships 221 `.resources.dll` for 13
  languages (~15.5 MB) belonging to the FRAMEWORK's assemblies (UIAutomationClient,
  System.Xaml, WindowsBase…). Dropping them is free: they hold no launcher string
  (our UI is `Strings.cs`, compiled into the exe — it stays bilingual), only .NET's
  own exception/accessibility text, which falls back to the neutral English already
  embedded. `es` is kept because .NET picks satellites by `CurrentUICulture` (the
  WINDOWS language, not `Strings.Language`) and the player base is mostly
  Spanish-speaking. Dead ends, all verified: **`InvariantGlobalization` gains
  NOTHING** (no `icudt.dat` in the bundle — .NET uses the OS ICU on Windows);
  **`PublishTrimmed` is unsupported for WPF** on .NET 8 (it is also what makes the
  ~21 MB of WinForms — `System.Windows.Forms` + `.Design` + `.Primitives`, pulled in
  by the WindowsDesktop runtime pack even though this is pure WPF — unremovable);
  **framework-dependent** would cut ~150 MB but forces users to install the .NET
  runtime, killing the "one .exe and done" premise. `PublishReadyToRun=false` would
  recover ~30 MB but costs 1-2 s of cold start — deliberately kept ON.
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
**excludes the config**, overwrites a stale zip), `LauncherUpdateServiceTests`
(letter-version comparison + the informational-tag self-recognition fallback), and
`BackgroundStartupPlanTests` (`StartupRegistrationService.PlanStartup` — the
ON-by-default auto-start seed; `OptedOut_NeverReArms` is the one that matters, it
guards against silently re-enabling auto-start after the user turned it off), and
`SafeUrlTests` + `ModLinkTests` (the mod-supplied-url gate and the community-links
sanitisation — the REJECTION cases are the point, see the `SafeUrl` gotcha), and
`AppCompatLayerTests` (`AppCompatLayerService.Parse` — the `~` marker separating a layer
Windows applied itself from one the user set deliberately, which is the only thing gating
whether the launcher offers to remove it), and three that guard writes the launcher makes
into the player's own files: `GameRecordingPlanTests`
(`GameSettingsStore.PlanGameRecording` — the sibling of `BackgroundStartupPlanTests` for
game recording; `OptedOut_WritesFalseOnceAndNeverReArms` is its centrepiece, and
`EveryModIsSeededSeparately_NotJustTheFirst` is the one a single launcher-wide marker would
fail), `GameRecordingPurgeTests` (**the KEPT cases are the point** — a recording the player
renamed is never deleted and never counts against the budget), and `MatchResultResolverTests`
(who gets credited with winning a multiplayer match — `TheTwoScoresAlwaysSumToOne` is what
the backend validates against) and `ReplayParserTests` /
`ReplayMatchSelectionTests` (**the local player's slot comes from his AoE3 profile NAME,
never from the recording's outcome trailer** — reading it from the trailer's second field
meant that field is the LOSER in multiplayer, so the winner's own recording was rejected
and a match could only ever be rated when the host LOST;
`EachPlayerScoresTheOppositeFromTheSameBytes` and
`AcceptsTheRecordingOfWhoeverWon_NotOnlyTheLosers` are the regressions, and the details are
in `.claude/rules/multiplayer.md`), plus the two that guard the match lifecycle around a room that
goes away underneath it: `MatchContextTests` (the facts of a match, captured at launch —
`AClosedRoomCannotChangeTheAnswer` is the regression test for a real match that was never
reported because the host closed the room mid-game) and `RoomMatchStateTests`
(**`TheHostIsNeverOfferedIt`** — who may reopen their game without disturbing anyone, and what
leaving the room mid-match has to warn about). Two more cover the network edge of the WoL
pipeline, which had none at all: `DownloadRetryTests` (which download failures are retried —
the REJECTION cases are the point: a user's Cancel and a permission error must not be) and
`UpdateInfoServiceTests` (`ParseXml` + `IsUsable` — the well-formed-but-empty manifest is
the case that matters).

**`DialogXamlTests` is the guard for every window the smoke test never opens.** The
smoke-launch below opens `MainWindow` and nothing else, so the XAML of
`CreateLobbyDialog` and `LobbyWindow` — which is only parsed once a user signs in and
enters a room — would ship with a broken `{StaticResource}` unseen. It constructs each
window on an **STA thread** with the `Styles/*.xaml` dictionaries loaded by **explicit
`pack://` URIs** (App.xaml's own `Source` values are relative and resolve against the
entry assembly, which under a test host is the runner, not the launcher) and App.xaml's
INLINE resources — the FontSize scale and the font families — recreated by hand, since
they live in no dictionary file. It also builds `MatchResultCard`, which is assembled in
code and therefore checked by nothing at compile time; that test caught two tokens that
were never added, in a card that is only built when a real match ends. **Add a case here
whenever you add a window or a code-built card** — a green build is not evidence either
one loads.

Everything UI / install-pipeline still needs a **manual smoke test on Windows**.
Two cheap gates beyond a green build:
- **Smoke-launch** — a green build does NOT prove the app starts: a
  `{StaticResource}` that fails to resolve throws at *runtime*, not compile (this
  bit us once with `RadiusMd`). Run `dotnet
  bin/Release/net8.0-windows/Aoe3ModLauncher.dll` for ~10 s — it stays up
  (timeout-kill = OK) or prints the unhandled exception + stack. **The `.exe` now
  runs `asInvoker` (no UAC), so you can run it directly**; running the `.dll` via
  the dotnet host still works and keeps stdout in the current shell for capturing
  startup crashes. (Before the auto-start fix the `.exe` was
  `requireAdministrator` and needed the `.dll` route to avoid the UAC prompt — see
  the run-in-background gotcha.)
- **Install** — the installer can produce a broken-but-"successful" result
  (missing base game), so a real install needs an actual AoE3 + payload download;
  the integrity gate (below) is the in-process backstop.

## Design handoffs: follow the reference 100%

**When a design handoff is in play, match it exactly — colours, sizes, spacing,
weights, and what is present or absent.** Standing instruction from the maintainer,
after a first pass that kept the old bar background, kept icons the reference didn't
have, and substituted "close enough" values. The result read as a different design
and had to be redone. Assume every value in the reference is deliberate.

Two consequences worth stating, because they are what went wrong:
- **"I kept the existing X" is not a neutral choice.** Leaving the old title-bar
  background was the single biggest reason the bar didn't look like the mockup, and it
  was invisible in a diff because nothing changed.
- **Read the prototype, not only the README.** The prose spec omitted values the
  markup carries (the chip's muted `#a9cbb9` address text isn't in the token table).
  `docs/design_handoff_multiplayer_ui/Launcher Multiplayer.dc.html` has the real
  numbers; grep it for the element you're building.

**Deviate only where the platform makes fidelity impossible**, and then say so rather
than silently approximating. The two known cases so far: WPF's `TextBlock` has **no
letter-spacing** at all (faking it with thin spaces breaks `CharacterEllipsis` on
localized text), and a CSS `border-radius: 999px` is a clamped-to-50% idiom that WPF
renders as a distorted ellipse — a pill needs an explicit half-height radius. A house
rule is NOT such a case: the type scale's 13px floor was raised against the handoff's
11.5 twice and overruled twice, and 11.5 is what ships (see `MpLabelSize`).

## Important gotchas

- **AssemblyName ≠ RootNamespace, on purpose.** The shipped binary is
  `Aoe3ModLauncher.exe` (`<AssemblyName>`), but every file's namespace and
  `using` is `WarsOfLibertyLauncher` (`<RootNamespace>`). This mismatch is
  intentional — do not "fix" it by renaming namespaces.

- **Multiplayer / lobby / Radmin / global chat / Discord-announcement gotchas live in
  `.claude/rules/multiplayer.md`.** They load automatically when you work on the
  multiplayer surface (`MultiplayerTab`, `LobbyWindow`, `Services/Multiplayer/**`,
  `Radmin*`, `TauntService`, `AppToast`, `MpAlertOverlay`) instead of costing every
  session. **Update multiplayer invariants THERE, in the same change** — the
  "document it as you change it" rule is unchanged, only the file moved. Rules that
  merely touch multiplayer but apply more broadly stayed in this file.

- **Optional community ADDON gotchas live in `.claude/rules/addons.md`.** They load
  automatically when you work on the addon surface (`Services/Addon*`,
  `HeavenDownloader`, `NsisExtractor`, `Models/AddonManifest`, the ADDONS tab in
  `ModPropertiesDialog`) instead of costing every session. **Update addon invariants
  THERE, in the same change** — the "document it as you change it" rule is unchanged,
  only the file differs. Rules that merely touch addons but apply more broadly stay in
  this file. The short version, because it constrains code you may touch from
  elsewhere: an addon may **never** write the three identity files
  (`data\protoy.xml` / `techtreey.xml` / `stringtabley.xml` — version detection and the
  MP fingerprint both read them), applying one **re-captures** the touched files'
  fingerprints into the manifest so verify stays honest, and the two flows that re-lay
  the overlay (`UpdateService.ApplyUpdatesAsync`, `MainWindow.RepairInstallAsync`)
  **re-apply addons in their tails** — best-effort, so a cosmetic overlay can never
  fail an update.

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
  **`ExtractPayloadAsync` flattens a single WRAPPER folder** via
  `NativeInstallService.NormalizePayloadRoot` (internal, unit-tested `PayloadRootTests`):
  some mods package their overlay INSIDE one folder (e.g. the zip's only top-level entry
  is `Knights and Barbarians/`, with `data\`/`art\` under it). The overlay copy is
  relative to the returned root, so without this the mod files land one level too deep
  (`…\install\Knights and Barbarians\data`) and never merge over the cloned AoE3 → broken.
  `NormalizePayloadRoot` descends while a folder has EXACTLY one subdir and NO loose files
  (bounded 4 levels), so a normal FLAT payload (WoL/Improvement Mod ship several top-level
  dirs) is an immediate no-op. **It has a TWIN that must stay in step** — `ResolvePayloadPrefix`,
  which answers the same question from the zip's entry names for the direct-install path; see the
  `DirectPayloadInstall` bullet for why they can diverge and the differential test that proves
  they don't. Don't remove it — it's what makes wrapped community zips
  installable without repackaging.

- **Every download RETRIES transient network failures and RESUMES from the `.part` file —
  the retry lives in `DownloadService.DownloadFileAsync`, deliberately NOT at the call
  sites.** The failure it closes: WoL's payload is ~4 GB across three parts fetched in a
  loop by `DownloadAndConcatenatePartsAsync`, with **no mirror** (`WolPatcherSettings` has
  `UpdateInfoUrlAlt` but no payload alt, and the part loop calls `DownloadFileAsync`
  directly, not `DownloadWithFallbackAsync`). One dropped connection threw `IOException`
  straight out of the install — and because `MainWindow.InstallAsync`'s own 3-attempt loop
  catches only `InvalidDataException` (a corrupt ZIP), it hit the generic `catch (Exception)`
  and died with a raw `Error: …` after gigabytes had been downloaded. The resume machinery
  (`.part` + HTTP Range, incl. the 416 branch) already existed and worked; nothing ever
  re-entered it. **Why per-PART matters:** the retry sits inside `DownloadFileAsync`, so a
  blip on part 3 never re-fetches parts 1-2, and within a part it picks up at the byte it
  stopped on. Three details are load-bearing: (1) **`IsTransientDownloadFailure(ex,
  userCancelled)` takes the token state, not just the exception** — an `HttpClient` TIMEOUT
  and a user pressing Cancel are BOTH `TaskCanceledException`, so the type cannot tell them
  apart (the same trap `ConnectivityState.IsNetworkError` documents); retrying a Cancel
  would make the button take four attempts and a backoff to be obeyed. (2) It walks the
  inner-exception chain **twice, and the order is the point**: a `UnauthorizedAccessException`
  ANYWHERE wins over a transport error wrapped around it, so a read-only destination fails
  once instead of four times. (3) The budget is **two counters** — `MaxAttemptsWithoutProgress`
  (4) resets whenever an attempt advanced the `.part` file, so a multi-GB download over a
  flaky link may hiccup more than a handful of times, while `MaxTotalAttempts` (12) bounds a
  server that accepts the connection and drops it after a few bytes. Those two consts plus
  `RetryDelay` (2/4/8 s) are the dials. `DownloadWithFallbackAsync` now also **LOGS the
  primary's failure** before falling over — it was a bare `catch when`, so a mirror failing
  for every user was invisible in the diagnostic bundle (the download just appeared to work,
  from the alt). Note the outer `InvalidDataException` retry still wipes the whole temp via
  `CleanupTempPayload`, which stays correct: there we don't know WHICH part is corrupt.
  Pinned by `DownloadRetryTests` (the REJECTION cases are the point); `DownloadService` had
  no test before this.

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
  **The one sanctioned divergence from the canonical file set is an enabled community
  ADDON** — a deliberate, user-chosen overlay recorded in `<install>\addons\_owned.json`
  and reversible from it (see `.claude/rules/addons.md`). That is a user choice, not a
  strip: nothing here deletes, and the addon gate refuses any file this policy's
  reasoning protects.

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
  an update, a version switch, or an old manifest with no `FileHashes`. **Because the
  re-overlay rewrites every overlay file, it also wipes any enabled community ADDON —
  so `RepairInstallAsync`'s tail re-applies them** (`ReapplyAddonsAfterOverlayAsync`),
  BEFORE the success branch reports "repaired" so the state the user is told about is
  the final one. This was a real bug, not a hypothetical: a repair silently cost the
  user their addons as a side effect of "fixing" the install. Best-effort — see
  `.claude/rules/addons.md`.
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
  20 / Overlay 20 — a `DirectPayloadInstall` mod folds Extract into Overlay, so
  DL 60 / Overlay 40) and real `extractProgress`/`overlayProgress`/`phaseProgress`
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
  the copy** in `CopyPayloadToDestinationAsync` — or, for a `DirectPayloadInstall` mod, while the
  file is written by `ExtractPayloadToDestinationAsync` — (canonical **pre-translation** bytes,
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
  **That same block then re-applies the user's enabled ADDONS** (`ReapplyAddonsAsync`),
  deliberately AFTER the hash refresh — re-applying re-captures those files again with
  the addon's bytes, so the order is load-bearing; it's best-effort, so an addon can
  never fail an update (see `.claude/rules/addons.md`).
  **The same post-patch block ALSO re-stamps the version-KEY baseline** (`KeyFileHashes`
  + `manifest.Version`), which used to be a real bug: `KeyFileHashes` was written ONLY at
  install/repair, never after a patch, so a patched-to-current install kept the PRE-patch
  3-MD5 baseline → `RecognizeFromManifestData` computed `intact=false` → "live files
  drifted" → NO MATCH. So a genuine 1.2.0e install whose UpdateInfo was momentarily
  stale/unreachable read as unrecognized. The re-stamp computes the 3 MD5s the SAME way
  `DetectCurrentVersionAsync` reads them (`ComputeRecognitionKeyHashesAsync`: proto/tech
  live, stringtabley from `_originals` — NOT `NativeInstallService.ComputeKeyFileHashes`,
  which reads stringtabley LIVE and would drift under an active translation) and sets
  `KeyFileHashes` + `Version` TOGETHER to `LatestVersion.Ver` (never one without the
  other) so the baseline always matches what recognition later compares against. (Old
  drifted manifests self-heal on the next patch/Repair; the valid-install guard below
  covers them meanwhile.)
  (b) **`PruneMissingHashes` is mandatory after any deletion** (delete-list,
  `ApplyUpdateDeletions`): a fingerprint left for a deleted file makes verify report a
  false "missing" and granular Repair **resurrect** a file the pipeline intentionally
  stripped — inverting the strip. (See also the byte-faithful + delete-list bullets.)

- **A VALID install whose version can't be recognized NEVER gets offered a destructive
  from-scratch reinstall — `ApplyCheckResult` shows PLAY, not Install.** The failure it
  guards: WoL's UpdateInfo (`aoe3wol.com` over HTTP) intermittently returns a
  short/truncated body; `UpdateInfoService.FetchAsync` used the primary unless it THREW
  (a truncated body fails `LoadXml`), then fell to the ALT **without any version-count
  validation** — see the version-count bullet below, which closed that half. The old ALT
  was a SourceForge mirror **frozen at 1.0.9h**, so
  the fallback served an ancient UpdateInfo → a real 1.2.0e install matched no `<version>`
  → NO MATCH → (manifest baseline drifted, see above, so no rescue) → the install path was
  VALID but `CurrentVersion==null` with bogus pending downloads → the UI offered a **full
  reinstall** (users started a 4 GB re-download into `Wars of Liberty (2)`). Two fixes:
  (1) **`ModRegistry._builtIn` WoL UpdateInfo URLs: primary = `https://aoe3wol.com/updates/UpdateInfo.xml`,
  alt = `http://aoe3wol.com/updates/UpdateInfo.xml`.** aoe3wol's HTTP endpoint returns a
  truncated ~7 KB body (fails XML parse) — consistently — while HTTPS serves the correct
  complete file (47 versions, verified live), so HTTPS is the primary and HTTP the fallback
  (in case it recovers). The old alt was the ancient SourceForge mirror (frozen at 1.0.9h)
  — falling back to THAT is what made a valid 1.2.0e install read as unrecognized. (The
  built-in shadows the catalog, so this can't be fixed from `mod.json` — recompile only;
  the template `mod.json` was synced for consistency.) (2) **`ApplyCheckResult`'s
  `!versionKnown` branch is split on `result.IsValidInstall`**: a VALID-but-unrecognized
  install → `SetPrimaryAction(Play)` + neutral `StatusInstalledVersionUnknown` + **clears
  `_pendingDownloads`** (so nothing — e.g. a `--update-now` auto-apply — acts on the
  stale/downgrade "updates"); only a NON-valid install keeps the destructive Install CTA.
  **(3) The SAME guard is mirrored in `ModPropertiesDialog`** so the gear-menu dialog never
  contradicts the dashboard: `CheckUpdatesBtn_Click` shows `StatusInstalledVersionUnknown`
  (not "update available") when `result.IsValidInstall && result.CurrentVersion == null`,
  and `LoadGeneral` shows `ModPropVersionUnknown` ("installed — version not verified")
  instead of "(not installed)" when the version is null but `_service.InstallPath` is set.
  Without this, Properties said "An update is available — open the launcher to install it"
  while the dashboard showed Play → the user clicked "update" and nothing happened. Same
  philosophy as the offline fallback: a mod on disk stays playable regardless of a
  flaky/stale UpdateInfo; Repair/reinstall stays available via the gear menu but is never
  pushed, and every surface agrees.

- **An `UpdateInfo.xml` that PARSES but lists no versions counts as a FAILED fetch, not a
  successful one — `UpdateInfoService.IsUsable` — and the last validated manifest is kept
  on disk as a last resort.** This is the other half of the bullet above. `FetchAsync`
  only ever treated an EXCEPTION as failure, so a truncated body whose cut happens to land
  on an element boundary is still well-formed XML: it parsed, `ConnectivityState.ReportSuccess()`
  was reported, and `CheckCoreAsync` got `Versions = []` → `latest = null` → a perfectly
  good 1.2.0e install reads as matching no known version. Indistinguishable downstream
  from the failure that had users re-downloading 4 GB into `Wars of Liberty (2)`, and
  invisible in a diagnostic bundle because nothing errored. Now `FetchFromUrlAsync` throws
  `InvalidDataException` when `IsUsable` is false (versions count == 0), so **the ALT URL
  gets its turn** instead of the empty primary being accepted; if the ALT is empty too,
  `CheckAsync`'s existing catch degrades to `BuildOfflineResult`, which keeps PLAY and
  never proposes a reinstall. **The cache is deliberately NOT `DiagnosticLog.SaveSnapshot`'s
  `UpdateInfo-snapshot.xml`** — that one is a single shared file written even for the
  garbage bodies (which is exactly what makes it useful in a bundle), so reading it back
  would reintroduce the truncation this guards against. The validated copy is per-mod
  (`AppPaths.DataDir\updateinfo-cache-<modId>.xml`, keyed by the `cacheKey` argument
  `UpdateService` passes as `_profile.Id`), written only after a manifest passes
  `IsUsable`, and re-validated on read so a half-written cache can't do what a truncated
  download would have. **Risk direction, and why the cache is safe:** a stale manifest can
  only list FEWER patches than the live one, never invent one — `ComputePendingDownloads`
  filters by `MinReqDownload` and id. `IsUsable` and `ParseXml` are pure and pinned by
  `UpdateInfoServiceTests` (the empty-but-well-formed case is the point); before this the
  whole file had no test at all, making it the least-guarded link in the WoL version chain.

- **A STALE `translations\_originals\` snapshot used to brick an install — detection
  falls back to the LIVE stringtabley when the snapshot matches nothing, and re-syncs
  it. The refresh's guard is load-bearing.** `_originals` is only a COPY of the live
  file taken by `RefreshOriginalsSnapshot` at install/patch/pack time, so **a patch
  applied BY HAND** (copy-pasting the files — a workaround that circulates in the
  community) refreshes `data\` and leaves the snapshot on the OLD version. Since
  `DetectCurrentVersionAsync` hashes the snapshot on purpose (localization invariance),
  it then matches NOTHING — and that is not cosmetic, it makes the mod unusable in three
  ways at once, all from the one cause: (a) no version → the UI queues the **ENTIRE**
  patch chain (44 downloads from 1.0.13, 142 MB) instead of a real update, so "there's
  an update but it won't update"; (b) `ModHashService` hashes the same three files
  through the same snapshot → a HYBRID fingerprint matching no peer → **every lobby join
  is rejected as a version mismatch**; (c) the mod reads as unrecognized. Seen in the
  wild (user 69metal69): `proto=OK tech=OK` for 1.2.0e with an `_originals` stringtabley
  still at 1.2.0c2 — confirmed against his own `UpdateInfo.xml` snapshot. Fix: when the
  snapshot matched no version, retry the triple with the **live** file; on a match, use
  it and call `RefreshOriginalsSnapshot()` — which also repairs the MP fingerprint for
  free, since `ModHashService` reads that same snapshot (no change needed there).
  **The guard: only refresh when the live file MATCHES a known version** — that is what
  proves it is the canonical English one. With a translation applied the live file is
  the TRANSLATED one and matches nothing, so the fallback never fires and the snapshot
  is left alone; refreshing unconditionally would copy the translated file over the only
  canonical-English backup and permanently destroy BOTH revert-to-English and the
  fingerprint. The snapshot stays the PRIMARY source (that invariance is what lets a
  Spanish install play with English peers) — this is strictly a fallback for the case
  that currently just breaks. The **mirror case is deliberately NOT auto-healed**: live
  stale + snapshot correct (the game then shows an old version string) is left alone,
  because the live file may be NEWER than the snapshot — exactly what a hand-pasted
  patch produces — and auto-reverting would undo the user's own fix. Pinned by
  `StaleOriginalsFallbackTests`, where the **translation-applied case is the important
  one** (it's what protects the backup).

- **`IsolatedFolder` detection ALSO requires the base-game ENGINE at the folder root, not
  just the probe — a folder with only the mod's overlay is NOT an install.**
  `ModInstallProbe.Inspect` checks, for `ModInstallType.IsolatedFolder` only, that at least
  one engine DLL (`RockallDLL.dll`/`binkw32.dll`/`granny2.dll`/`deformerdlly.dll`) sits at the
  install root (new `ProbeOutcome.EngineMissing`, ranked between `MarkerMissing` and `Match`).
  An `IsolatedFolder` real install is a full AoE3 clone (bin\ flattened to root), so the engine
  is always there; a folder with the probe but no engine is a leftover **manual download of
  the mod's overlay** — the Napoleonic Era bug: the launcher adopted the user's stray
  `Napoleonic era\` folder (had `age3n.exe`, no engine, no manifest), read it as installed, and
  — being `GitHubReleases` with no known version — offered a bogus **Update** for a mod it never
  installed (the `ghUnknownInstalled` CTA). **Only `IsolatedFolder`:** an `InPlaceOverlay` mod
  installs INTO the base game, whose engine lives in `bin\`, not the install-path root, so the
  check is skipped for it. The engine list is the non-data `VerifyService.EngineCandidates`
  (NOT the data version-key files — a mod may ship its own, e.g. NE's `proton.xml` vs
  `protoy.xml`). Load-bearing: the install order (clone engine → flatten → overlay adds the
  probe LAST) means "probe without engine" never exists mid-install, so this can't reject an
  install in progress. Pinned by `ModInstallProbeTests` (engine-missing rejected, any single
  DLL satisfies, InPlaceOverlay unaffected); `ModInstallScannerTests`' fixtures lay the engine
  too. The stale `installPath` such an adopted folder left in the config self-heals — the next
  `ResolveInstallPath` rejects it as an invalid cached path.
  **That last sentence was FICTION until `UpdateService` was made to actually call this — it
  had its own, weaker copy of the rule, and the invariant above held nowhere on the
  resolution path.** `UpdateService.LooksLikeRealModInstall` used to be a local two-liner:
  require `InstallMarker` when the profile declares one, `return true` otherwise — **without
  touching the disk**. Napoleonic Era declares no marker, so every adoption path in that class
  (ctor cache, `forceInstallPath` step 0, cached path, registry, disk scan, broad scan)
  collapsed to "the probe file exists". The engine-less overlay folder above satisfied that,
  so the launcher reported it installed, showed PLAY, and `age3n.exe` died two seconds after
  every launch with nothing in the UI and a bare `Game exited.` in the log — the user's "I
  press play and it doesn't start". `CheckAsync`'s `valid` (which becomes
  `CheckResult.IsValidInstall` → `_modIsInstalled` → the PLAY-vs-INSTALL CTA) was looser
  still: probe file only. Both now go through `ModInstallProbe`. **Keep exactly one source of
  truth for "is this an install"** — a second opinion is how these drifted apart, and an empty
  `InstallMarker` must never again be read as "valid": no marker means *the probe is already
  exclusive*, not *skip the remaining checks*.
  **That rule is now STRUCTURAL, not just written down: `UpdateService.IsRealInstall` is a
  METHOD, where it used to be a two-call conjunction you had to remember to type.** The gate
  was `IsProfileInstalled(p) && LooksLikeRealModInstall(p)`, spelled out at six sites — and
  the second half had ALREADY gone missing at one of them (the `valid` recomputed after
  `BroadFallbackScan`). That site happened to be harmless, because the scan validates its own
  hits, but a conjunction that is correct only by luck at one of six sites is the same drift
  this bullet is about, one step from repeating. The first half was also redundant wherever a
  probe file IS declared, since `ModInstallProbe.Inspect` checks it too. `IsRealInstall` is
  `ModInstallProbe.LooksLikeModInstall` **plus one legacy allowance**: a profile declaring NO
  probe file must still pass `RegistryService.IsValidInstall` (the WoL `art\zulushield` marker),
  which is how pre-probe-file installs stay recognised — `Inspect` SKIPS the probe step for
  such a profile rather than failing it, so dropping that half would have silently widened
  the gate instead of narrowing it. `LooksLikeRealModInstall` is gone; don't reintroduce a
  second predicate beside `IsRealInstall`. Pinned by the no-probe-file cases in
  `ModInstallProbeTests`.

  **The engine requirement now covers `InPlaceOverlay` too — the old exemption was a hole,
  not a design.** It was written on the reasoning that an overlay's engine "lives in `bin\`,
  not the install root", which held only while overlays installed to the AoE3 ROOT. They now
  install to the folder the engine reads from (see the next bullet), so the exemption stopped
  protecting anything and started hiding the exact failure it sat next to: switching Napoleonic
  Era to `InPlaceOverlay` made its engine-less leftover folder pass again, by the other route.
  The two types differ only in WHERE the engine may be: `IsolatedFolder` requires it at the
  root exactly (a clone flattens `bin\` into the root), `InPlaceOverlay` accepts it at the path
  **or one level down in `bin\`** (`HasEngineNearby`) — a Steam overlay lands in `…\bin` where
  the engine is, other layouts keep `data\` at the root with the executables under `bin\`.
  Accepting both is what makes the check layout-independent instead of a guess.

- **`InPlaceOverlay` installs through `InstallModOnlyAsync` — the CLONE branch must never see
  it, because a clone stamps `clonedAoe3: true` and that flag is what tells Uninstall it may
  delete the whole folder.** `MainWindow.InstallAsync` computes `overlayInPlace`
  (`InstallType == InPlaceOverlay && !addNewSlot`) and routes on it; `addNewSlot` still clones,
  since two overlays can't share one game folder. The chain that makes this safe:
  `InstallModOnlyAsync` writes `WriteManifest(…, aoe3SourcePath: null, clonedAoe3: false, …)` →
  `UninstallService` takes the `overlayOnly` branch → only `OverlayNetNew` is removed and the
  player's AoE3 survives. **Caveat:** files the mod OVERWROTE aren't restored (Napoleonic Era
  overwrites exactly 1 of its 552); uninstall removes what was added, it doesn't revert what
  was replaced.
  **Why this is written so emphatically:** the type used to be ignored by the whole pipeline —
  the clone-vs-not decision hung on `aoe3SourcePath == null`, and `InstallFolderDialog`
  guaranteed it was never null, so `InstallModOnlyAsync` was dead code from `InstallAsync` and
  EVERY install stamped `clonedAoe3: true`. With the overlay destination pointed at the real
  game folder (which is where an overlay belongs), that combination would have cloned
  `directx\`/`msxml\` INTO the user's `bin\`, left a manifest there that makes
  `FolderCloneService` exclude `bin\` from every future clone (producing engine-less installs),
  and armed a **recursive delete of their AoE3** on uninstall. Two guards keep that from coming
  back: `InstallFolderDialog` still demands an AoE3 source for every CLONING install (the
  `requiresAoe3Source` flag only drops it for overlays), and `FindAoe3FolderConflict` refuses a
  destination that IS a detected `GameFolder`/`ModRoot` — skipped for overlays, since that is
  precisely where they go. The `…\Age Of Empires 3\<Mod Name>` convention stays allowed.
  **The overlay destination is the folder holding `data\`**, resolved with the same
  `GameLauncher.FindAoe3InstallRoot` the stock-game profile uses (parent of `bin\`, else the
  exe's folder — whichever has `data\`): `…\Age Of Empires 3\bin` on Steam, the root on a flat
  layout. `ModRoot` is one level above everything AoE3 loads.
  **Model choice for a non-UHC mod — the axis is WHERE THE EXE LOOKS, not whether the mod
  overwrites anything.** A non-UHC exe resolves its content through the registry `setuppath`,
  so any mod living in its own folder → `IsolatedFolder` + `privateSetupPath: true`, which
  gives it a key of its own and leaves vanilla's alone, both playable side by side. That is
  what BOTH shipped non-UHC mods use — Napoleonic Era and Struggle of Indonesia — even though
  only SoI overwrites base files. `InPlaceOverlay` (no key, install into the real AoE3) is
  still valid for a purely additive mod but **nothing ships on it**; see the fuller treatment
  in the UHC gotcha below before choosing it. The older `setupPathRedirect: true` solved the
  same problem with a junction and is mutually exclusive with vanilla while it runs — legacy,
  don't pick it for a new mod.
  **Applying UHC ourselves is not an option** — measured: WoL's UHC `age3y.exe` is 11,591,680
  bytes against the stock 11,598,648, a different *build* rather than a byte patch, so there is
  no "apply UHC" operation an installer could perform. It's done by the mod authors at build
  time.

- **A game that exits within `GameInstantExitSeconds` (8 s) of launching is reported as a
  FAILED start, not a short session.** `StartGameMonitor` only ever asked "is the pid still
  alive", so a game that couldn't start at all was indistinguishable from someone opening and
  quitting: the sole trace was `Game exited.` This is what let a broken install be launched
  over and over in silence. `MainWindow` stamps `_gameStartedUtc` beside `_gameProcessId` and
  `OnGameExited` warns (toast + status line, pointing at Verify/Repair) when the game died on
  its own inside that window. **`_stopRequestedByUser` is load-bearing**: `StopButton_Click`
  and `EnsureGameNotRunning`'s kill both set it, or pressing Stop — or agreeing to close the
  game before an install — would report the user's own action as a crash. The decision is made
  at the TOP of `OnGameExited`, before the bookkeeping it reads is cleared.

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
  ways: **`IsRealInstall` — the single gate every adoption route in that class
  goes through** (ctor cache fast-path, the forced/user-picked step 0, the saved
  path, the registry, the near-AoE3 disk scan, the broad scan, and the `valid`
  that becomes `CheckResult.IsValidInstall`), so a renamed install survives the
  re-check; and `IsolatedCandidates`' SECOND pass, which enumerates one
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
  **The SEARCH for that install was later made robust in WHERE it looks (never in
  WHAT counts — the marker gate above is untouched), via `Services/ModInstallScanner.cs`.**
  The gap: the auto-scan only looked one level inside/next to a detected AoE3 install,
  so WoL on an unrelated drive (`D:\Juegos\WoL`), nested (`…\AoE3\Mods\WoL`), or with
  AoE3 undetected read as "not installed". `ModInstallScanner.FindDeep(root, profile,
  maxDepth, ct, visited?)` is a bounded BFS (skips system dirs, per-dir IO swallow, lazy
  yield, `MaxDirsScanned` cap, cancellable) that delegates each folder to
  `ModInstallProbe.LooksLikeModInstall` — so it can NEVER promote vanilla AoE3 to WoL.
  `FindBroad`/`EnumerateLikelyRoots(includeDriveRoots)` scan a curated root set (every
  `AoE3Detector.FindAll` ModRoot/GameFolder/parent, Steam-library `steamapps\common`, and per
  fixed drive the Program Files variants + — **only when `includeDriveRoots`** — the bare drive
  root) with a shared visited-set. **The bare-drive-root crawl is gated because enumerating whole
  drives (`C:\`, `D:\`…) is a ransomware-adjacent behavioural signal for AV heuristics; the
  AUTOMATIC/passive scan opts OUT (`includeDriveRoots:false`) and the MANUAL/user-initiated search
  keeps it in** (see uses 2 vs 4 below). `FindBroad`/`FindDeep` also take a `maxDirs` cap (default
  `MaxDirsScanned`=20_000; the passive scan passes a lower 6_000). `IsBareDriveRoot` (internal,
  unit-tested) classifies `C:\` vs `C:\Program Files\`. Four wired uses:
  (1) `IsolatedCandidates` pass-2 now scans the AoE3 root/`bin\` **2 levels** deep (parent
  still 1) via `FindDeep` — catches `Mods\WoL`, still bounded to the AoE3 tree, still
  synchronous (cheap). (2) A **broad fallback** in `UpdateService.CheckAsync`
  (`BroadFallbackScan`) runs `FindBroad` **off the UI thread** (`Task.Run`) ONLY when the cheap
  resolution (cache / registry / near-AoE3 scan) failed, gated to isolated-folder + non-stock +
  has-content-signal, and **once per session per profile** (`s_broadScanAttempted`, static) so a
  not-installed user pays it at most once. **This automatic scan is CONSERVATIVE**
  (`includeDriveRoots:false`, `maxDirs:6000`): it does NOT crawl bare drive roots — it covers only
  near-AoE3 + Steam `steamapps\common` + Program Files — to keep the unprompted background scan off
  AV behavioural radar. A mod on a bare drive root (`D:\WoL`) is instead found via use (4) below. (3) The manual folder picker
  (`MainWindow.ResolvePickedModInstall`, used by "Change mod folder" + "Add existing
  folder") deep-scans the CHOSEN tree (maxDepth 4) after the shallow candidates miss —
  point at any reasonable ancestor and it's found. (4) A hero **"¿YA LO TIENES?" / "ALREADY
  INSTALLED?"** button (`DashboardSearchInstallButton`, shown next to Install only when an
  isolated-folder non-stock mod reads not-installed) + the ModProperties → LOCAL FILES
  **"Search for my install…"** button both call `MainWindow.SearchInstallAsync` →
  `FindBroad` off-thread with the **THOROUGH defaults** (`includeDriveRoots:true`, full cap — a
  broad scan is expected when the user explicitly asks), adopt the first hit (near-AoE3 roots
  first) + re-check. **This is the deliberate split: passive scan quiet, on-demand scan
  exhaustive.** Also:
  `RegistryService.FindInstallPath` now reads **HKCU** as well as HKLM (per-user Inno
  installs). **Load-bearing:** the broad scan NEVER relaxes the marker (no
  false-positive-on-vanilla), NEVER runs on the UI thread, and is bounded (no full-disk
  crawl) — and the PASSIVE variant additionally skips bare-drive-root enumeration. Pinned by
  `WarsOfLibertyLauncher.Tests/ModInstallScannerTests` (nested-found / maxDepth / system-dir-skip /
  no-marker-rejected / multiple / shared-visited / **maxDirs-cap / IsBareDriveRoot**). **Known
  simplifications (not yet done):** the multi-install case adopts the first hit + tells the
  user to pick a specific one via "Change mod folder" (no chooser dialog yet); the whole
  search UI needs a manual Windows click-test (build/unit-tests don't exercise it).
  **A manually-picked folder that content-validates is now ADOPTED DIRECTLY, and the
  picker is no longer a silent black box — both from a real diagnostic bundle.** The bug: a
  user pointed "Change mod folder" at a valid separate WoL install (`D:\Wars of Liberty`);
  the picker MATCHED it (probe + marker present), `BrowseButton_Click` wrote
  `st.InstallPath = resolved` + `Save()` + `CheckAsync()` — yet `ResolveInstallPath` read
  `state.InstallPath` back EMPTY (no "rejecting stale cache" log, which only fires on a
  NON-empty invalid path), so the mod stayed "not installed". Deterministic (2/2), and
  unexplained statically (`GetActiveState()` and `GetState(profile.Id)` resolve to the SAME
  `ModState`; `_config` is one shared reference). Two fixes plus instrumentation:
  (1) **`ModInstallProbe.Inspect(path, profile) -> ProbeOutcome`** {NotADirectory, ProbeMissing,
  MarkerMissing, Match} (ordered least→most install-like) reports WHICH check failed;
  `LooksLikeModInstall` is now a wrapper `=> Inspect(...) == Match`. `ResolvePickedModInstall`
  logs the chosen folder + each candidate's outcome + the deep-scan hit, and returns the
  best (closest) failure reason so the rejection message can name the MISSING signal — a
  marker-missing folder (looks like base AoE3 / an uninstalled overlay) shows
  `DlgInvalidFolderMarkerBody` ("missing `art\zulushield`… reinstall"), everything else the
  probe+marker list (`DlgInvalidFolderBody`). Pinned by `ModInstallProbeTests`.
  (2) **`forceInstallPath`**: the picked, already-validated path is threaded THROUGH the
  check — `MainWindow.CheckAsync(forceInstallPath)` (optional; skips the session cache
  fast-path when set) → `UpdateService.CheckAsync(..., forceInstallPath)` →
  `ResolveInstallPath(forced)`, whose new **step 0** adopts `forced` directly when it passes
  `IsRealInstall` (re-validated defensively; invalid falls
  through to normal resolution). So a valid manual pick is adopted **without depending on the
  cached-path read that was observed failing.** Both "Change mod folder" (`BrowseButton_Click`)
  and "Add existing folder" (`AddExistingCopy`, when there is NO active install — otherwise it
  registers an inactive copy as before) route through it. Invariant: **a manual pick that
  validates probe+marker is ALWAYS adopted.** (3) **Instrumentation kept**: `ResolveInstallPath`
  now logs, at the top of every check, the `_profile.Id` + the `state.InstallPath` it actually
  reads + `forced` + `hasMultiple`, and `BrowseButton_Click` logs a readback of the ids/state
  right after the write — so the next bundle pins WHY the normal config read returned empty
  (root cause of the paradox still open; the `forceInstallPath` fix unblocks the user
  regardless). Don't drop the top-of-`ResolveInstallPath` log or the forced step-0.

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
  **`FindAll` still misses a real AoE3 in a NON-STANDARD folder (name AND location)
  outside Steam — so there is a SEPARATE, opt-in deep content scan
  `AoE3Detector.FindAllDeep`, used ONLY by the install flow.** The real report: an
  AoE3 install can live at e.g. `…\Program Files (x86)\Microsoft Studios\Age of
  Empires III` (note "Studios", not the probed "Microsoft **Games**") — not a
  Steam library, maybe no registry entry — so `FindAll` returns nothing and a fresh
  install said "AoE3 not detected". `FindAllDeep(includeDriveRoots, ct, maxDirs)`
  runs the fast `FindAll` first, then a **bounded content BFS** for `age3y.exe`
  across the likely roots (Program Files variants + Steam `common`; bare drive
  roots only when `includeDriveRoots`), **reusing `ModInstallScanner`'s machinery**
  via a NEW `FindDeep(root, Func<string,bool> match, …)` overload (skip-list, depth,
  shared visited-set, dir cap, cancellation). **Load-bearing invariants:** (1) it is
  **NOT** folded into `FindAll` on purpose — `FindAll`/`FindInstallRoot` are called
  on 9 HOT paths (multiplayer per-render/join, cold start, stock-game fingerprint,
  `GameLauncher`, uninstall, `ModInstallScanner`), and a deep scan there would slow
  every one; `FindAllDeep` is install-only, off the UI thread. (2) The per-folder
  predicate `AoE3Detector.IsCleanAoE3Folder(dir)` requires `age3y.exe` (flat or
  `bin\`) **AND** a `data\` dir **AND** that the folder is **NOT a mod install** —
  it excludes `install-manifest.json` and **ANY** `ModRegistry.All` `InstallMarker`
  (WoL's `art\zulushield` + every catalog mod's marker, via
  `ModInstallProbe.MarkerExists`), so a WoL / mod folder (which also ships
  `age3y.exe`) is **never** offered as a clone source (that would produce a
  contaminated mod-on-mod install). This is GLOBAL, not WoL-specific — every
  `IsolatedFolder` mod that clones AoE3 benefits, since the base game is
  mod-agnostic. Two wired uses: (a) **automatic** — `MainWindow.InstallAsync` runs
  `FindAllDeep(includeDriveRoots:false)` (conservative: no drive-root crawl, ~6000
  dir cap — the AV-signal rule from the mod scanner) off-thread ONLY when the fast
  `FindAll()` returned nothing, pre-filling `aoe3SourcePath`; (b) **manual** — a
  "Buscar mi Asian Dynasties…" button in `InstallFolderDialog` (shown only when no
  source) runs `FindAllDeep(includeDriveRoots:true, maxDirs:20_000)` (exhaustive,
  user-initiated), the base-game analog of WoL's "¿YA LO TIENES?" search. Backstops
  if the scan ever picked a bad source: the existing `CountCloneableFiles`
  preflight + `InstallBaseGameMissingException` (0-file abort) + the 3-key-file
  `data\` verify. **`InstallAsync` ALSO reuses the durable manual AoE3 pin as a LAST
  resort** — after both `FindAll` and `FindAllDeep` come up empty, it tries
  `AoE3Detector.InstallationFromManualRoot(config.Aoe3ManualPath)` (returns an
  `Installation` only when the pinned folder passes `IsCleanAoE3Folder`;
  `ModRoot = the pin` is the clone source, correct for both the standard game-root and
  the atypical `…\bin` layout). So a user who already pointed "Change AoE3 folder" at a
  non-standard AoE3 doesn't have to re-find it for EVERY mod install (WoL copies +
  community mods) — the same durable-pin learning as the stock-game detection, applied
  to the install flow. It's a fallback (fires only when the scans found nothing), so
  zero regression, and the same `CountCloneableFiles` preflight + 0-file abort backstop
  it. Pinned by `AoE3DetectorTests` (clean-vs-mod predicate, non-standard
  folder found, WoL folder never returned, `InstallationFromManualRoot` flat/bin/mod/blank)
  + the `FindDeep` predicate overload cases.
  A machine that has ONLY a WoL (its `age3y.exe` bundled inside the marked WoL
  folder) and no separate clean AoE3 correctly finds nothing to clone — the existing
  WoL is recognized by the mod-detection path instead; this is intended, not a miss.

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

- **MainWindow is THREE rows: title bar (36) / nav (54) / content — and the tabs
  have now moved between them twice, so read this before moving them again.**
  Handoff 1a folded the separate nav strip INTO the title bar (five stacked bars
  down to two); the later header reference split it back out, because a chrome row
  carrying only a wordmark can afford to BE the drag handle. Row 0 is the shared
  `controls:TitleBar` holding the brand, the version chip, and — in the SAME grid,
  right-aligned — the self-update pill, the offline chip and the notification bell.
  Row 1 is `MainNav`, a plain `Border` with the three tabs (`TopTabBar`, still a
  named panel in MainWindow's namescope so `ApplyTopTabOrder` can clear and refill
  its `Children`) and, on the right, the connection capsule, a divider and the
  account block. Row 2 is the content.
  **The seam belongs to whichever bar sits directly above the content.** It was the
  nav strip's bottom border, then a TOP border on the content wrapper while the tabs
  lived in the bar, and it is the nav row's `BorderThickness="0,0,0,1"` again now —
  the content wrapper's was removed, since two 1px rules in different greys stack
  into a visible double line. Multiplayer draws its own sub-tab bar under this, but
  Library and Workshop have nothing, so the rule cannot simply be dropped. The title
  bar itself still has **no bottom border on purpose**; don't give it one.
  **Three things are coupled to this and are easy to break:**
  (1) the bar height must only ever be set through `TitleBarHeightMain`, because
  `App.ApplyWindowChrome` derives `WindowChrome.CaptionHeight` from the same token
  and the caption region *is* the drag region — the nav row's height is a SEPARATE
  token (`MainNavHeight`) for exactly that reason. Grow `TitleBarHeightMain` to
  cover both rows and the top of every tab starts dragging the window instead of
  switching tabs; leave it at 46 while the bar renders at 36 and you get the same
  bug in a 10px band. Silent either way.
  (2) **every interactive control in the bar needs `IsHitTestVisibleInChrome` set
  EXPLICITLY, and it must NOT be set on the content grid** — on the grid it makes
  the whole bar hit-testable and kills the drag. Nothing in the NAV row needs it at
  all: that row is below `CaptionHeight`, so it gets ordinary client hit-testing,
  hover and tooltips for free (which is what lets the connection capsule carry a
  tooltip at all — in the caption region one could never fire).
  (3) the right-hand affordances are **in the bar's own grid now**, not overlaid.
  That retired the hand-computed right margins this bullet used to warn about (bell
  148, offline chip 200, content grid 58, all derived from `ButtonWidth=46 x 3`):
  column 0 of the shared template ends exactly where the caption buttons begin, so
  right-aligned content lands beside them with no arithmetic. Don't reintroduce a
  hand-computed inset. The notification *popup* stays a sibling of the TitleBar —
  a `Popup` renders in its own HWND against its `PlacementTarget`, so it has no
  reason to live inside the bar.
  **The hit-test rule bit us and the symptom was bizarre, so it's worth the detail:**
  `WindowChrome.IsHitTestVisibleInChrome` IS declared `Inherits`, but that inheritance
  does **not** reach an element placed in a `ContentControl`'s `Content` — the content's
  inheritance parent is the ContentControl itself, not the template's `ContentPresenter`
  that carries the flag. So the nav tabs, which relied on it, were **dead**: a click on
  one landed in the caption region and dragged the window instead. What made it look
  like a state bug rather than a hit-test bug is that they came ALIVE right after
  opening the brand popup — a `StaysOpen=false` popup takes mouse capture, and while
  captured every click is routed as a normal client hit, bypassing the caption
  hit-test entirely. So "click the brand, then a tab works, then it's dead again"
  is the signature of this exact bug. It no longer applies to the tabs (they left the
  caption region) but it governs every control that stays in the bar.
  **`UiScale`'s references are tied to the chrome height, and getting them wrong looks
  like a font bug, not a layout one.** The chrome went 47px (bar 46 + content seam) to
  90 (36 + 54), so `ContentHost` at the default 700-tall window drops 653 → 610 and the
  three `1100, 604` references became `1100, 560` (`MainWindow.xaml.cs`,
  `ModsBrowser.xaml.cs`, `MultiplayerTab.xaml.cs`), the hero's `1500, 760` became
  `1500, 710`. A fresh install would have looked fine either way — the danger is that
  the window height is PERSISTED, so anyone with a saved height near the break-even
  lands at 0.98-0.999, and `UiScale.SetTextCrispForScale` switches at `< 0.999`: the
  whole Workshop and Multiplayer surface swaps `Display`/`ClearType` for
  `Ideal`/`Grayscale` and every glyph goes soft for a 1% size change. Re-tune these
  whenever a chrome row's height changes.
  **The launcher-wide ES/EN toggle that lived in the bar is GONE** — the language is
  changed in Launcher Settings, which was always the primary place;
  `LauncherConfig.LanguageExplicitlyChosen` is now written from there alone.
  **The active-tab marker follows the tabs, and has now
  changed twice:** a 2px underline (it worked because that edge *was* the chrome/
  content seam), then a filled pill once the tabs moved INSIDE the bar and there was
  no seam left to sit on, and a 2px `MpAction` underline again now that the nav row
  gives them a rule to land on. The 2px is reserved as `Transparent` at rest rather
  than added when active, and the active tab is deliberately **not** bolded — either
  would resize the button and shuffle its neighbours on every tab switch. One related guard keeps the **content
  below** from reading as if it invades the chrome: the content host is
  `ClipToBounds="True"` so nothing in any tab — the PlayView full-bleed
  background image, the hero gradients, the scaled hero block — can render *up*
  over the title bar; it's defensive (clips content only, never the
  chrome) and on its own a near-no-op, because the image is a `Border`
  background and is already clipped to its bounds. Separately, the dashboard
  hero is a **single-layer brush stretched
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
  Launcher ▾") brightens on hover/press/open like the nav tabs, on the
  `Chrome*` ramp: idle `ChromeTextDim` `#93A4B5` (7.31:1) → hover
  `ChromeTextBright` `#F0F5FB` (17.04:1) → pressed/open `#FFFFFF` (18.67:1).
  **The hover is one rung BELOW pure white on purpose** — pressed and open are
  already white, and the only other signal that the menu is open is the chevron
  flip, so collapsing hover into white would leave "open" indistinguishable from
  "the pointer is on it". It sat at `ChromeTextSoft` (11.79:1) until a screenshot
  taken WITH the pointer on the button turned out to be indistinguishable from one
  taken without it. For that
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
  not a glyph, so it can't follow `Foreground` — **so it does not illuminate at
  all, and that is deliberate.** It used to carry `Opacity` 0.7 → 1.0 on an
  `Image.Style` bound to the ancestor button's `IsMouseOver`/`Tag` (the Image
  lives in the button CONTENT, out of reach of template triggers); against the
  near-black bar that cost ~23% of the brightness of artwork already dark on its
  own (mean luminance 77/255 at render size), and the washed-out brand block was
  reported twice. The reference specifies a size and a colour for the mark and
  says nothing about opacity — the dimming was ours. The wordmark and chevron
  still answer the mouse, so the affordance survives without knocking the mark
  back at rest. The chevron flips ▾↔▴ and
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
  ~16 KB — trivial, and the right trade for a sharp brand mark. Don't drop it back
  to 64 to "save memory". **The Image is 16x16 as of the two-row header, and that
  SUPERSEDES the old "stays 20x20" rule** — the header reference specifies 16, the
  maintainer was shown the trade-off (a detailed raster logo loses detail at that
  size, and the identical "se ve en baja resolución" complaint was raised again) and
  chose fidelity to the reference. So a future report of the icon looking soft is a
  known, accepted cost, not a regression to fix — unless the maintainer revisits it.

- **A `ControlTemplate` trigger that paints a template element by `TargetName` CANNOT be
  overridden by a style derived from it — so a template meant to be a `BasedOn` base must
  not hardcode a state colour there.** The sibling of the local-value precedence trap
  documented for `TitleBarBrandButton`, and it has now cost three rounds of the wrong fix.
  A derived style can only set the CONTROL's own property; the base template's
  `TargetName` setter overrides the `{TemplateBinding}` that would carry it down, so the
  derived declaration compiles, reads as intent, and does nothing. Two live cases were
  found together: `MpFooterPrimary` (BasedOn `MpFooterGhost`) hovered to the ghost's dark
  `MpRowHighlight` instead of blue — the reported "the interface eats the Create button" —
  and `PrimaryButton`/`DangerButton` (BasedOn `DialogButton`), whose gold and red hovers
  were **dead in ~15 dialogs**: every confirm and every destructive button in the launcher
  hovered neutral grey while its style said otherwise. The fix in both: move the state off
  `TargetName` into each Style's own triggers, on the control's `Background`, which reaches
  the template through the `TemplateBinding`. Triggers merge base-first down the `BasedOn`
  chain and the last setter wins, so the derived style's state is the one that lands. A
  style that supplies its OWN `Template` is exempt — that is why `MpRoomActionGhost` and
  `MpRowJoinButton` legitimately keep a `TargetName` trigger. The implicit global `Button`
  style solves it a third way, by animating an overlay's `Opacity` rather than a colour.
  Pinned by `DialogXamlTests.NoDerivedStyleDeclaresAStateItsInheritedTemplateStomps`, which
  walks every style in `Styles/*.xaml` and asserts it examined a non-zero number of them.

- **Popup menus use a TWO-TONE "punched-out" rim — don't reduce it back to a
  single border.** The gear ContextMenu + its cascading submenu
  (`ActionPanel.xaml`'s `MoreMenu` template) and the dashboard mod-switch
  popup (`MainWindow.xaml.cs` `DashboardChangeModButton_Click`) all wrap a
  bright 2px inner border (`MenuBorder` = `#7C8794`) in a 1px near-black
  outer band (`MenuBorderOuter` = `#000000`), painted via the outer
  `Border`'s `Background` + `Padding="1"`. Together that's a 3px effective
  boundary that reads as a discrete card over **any** backdrop: the dark
  outer rim pops against bright surfaces (the hero image), and the bright
  inner line pops against dark ones. The first attempt was a
  single brighter brush at 2px (the `MpDivider` lift recipe `#2C313A →
  #3A434F` reapplied) — the maintainer reported "yo lo veo igual", so the
  recipe escalated to two-tone. The drop shadow lives on the OUTER band so
  it skirts the whole composite rim; don't move it to the inner Border or
  the shadow gets clipped behind the black band.
  **The brand popup used to be the documented EXCEPTION to this — a single
  gold `AccentBrush` border — and it no longer is.** That exception was
  written when the chrome was `BgSidebar` `#314556`; the title bar is
  `ChromeTitleBg` `#0C131B` now, and a warm gold rim over a cold near-black
  bar made the menu read as belonging to a different application. Reported
  as "no se ve bien". **When the chrome moves, the rules that justified
  themselves by naming the old chrome colour have to move with it** — that
  is the general lesson, and this bullet's own "lighter BgSidebar chrome"
  phrasing was a second instance of it.
  **All three menus that hang off the header — brand, MODS switcher,
  notifications — now share one recipe**: surface `ChromePopupBg` `#1C2A3A`,
  the two-tone rim, `RadiusPopupInner`/`Outer` (6/7), and a 20/4/0.6 shadow.
  They previously disagreed on every one of those axes AND with each other
  (`#1B2025` warm grey / `#314556`, literally the pre-redesign title-bar
  colour / `#0F1A2B`), which is why "align it with its siblings" had no
  single target. Text follows the chrome ramp: `ChromeTextStrong` for
  titles (12.3:1), `ChromeTextDim` for secondary (5.7:1) — measured, after
  a first pass left subtitles at 2.4-2.6:1 by stacking `Opacity = 0.85` on
  top of an already-faint brush. **Gold survives in exactly one place here,
  deliberately: the "AOE3 LAUNCHER" / "CAMBIAR JUEGO" caption** (7.5:1),
  which still marks these as the launcher's own menus without the frame
  fighting the bar. The gear ContextMenu is NOT part of this set — it hangs
  off the dashboard, not the header.

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
  1500, 710, Kind.Render, (0,1))` in `MainWindow`'s ctor (`Controls/UiScale.cs`,
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
  the whole section. **The manager list is ordered by install FOLDER (stable), NOT active-first
  — on purpose.** Active-first made a middle copy JUMP to the top when you Switched it (abrupt /
  ambiguous); a fixed order means only the gold highlight moves. `LoadManageInstalls` sorts rows
  by `r.Path` (Ordinal) before `DisambiguateLabels` + card build, and the Switch button stamps
  `_recentlyActivatedInstallId` before its `await` so the rebuilt list plays a one-shot gold-tint
  pulse on the now-active card (a `SolidColorBrush` `ColorAnimation` from `TintGoldHover` →
  `BgBase`, started on `card.Loaded`, ~450 ms EaseOut) — the "highlight moved here" cue. The flag
  is consumed + cleared inside `LoadManageInstalls` so a plain `RefreshData` doesn't re-animate.
  Don't revert the manager to active-first ordering. (The dashboard switcher POPUP
  `AppendInstallCopiesToModPopup` keeps its own active-first order — it's a quick selector, not
  the manager.)

- **The launcher opens on the SAVED active mod — and that only works because the catalog
  is primed from CACHE before the profile is resolved. Never pick the active profile before
  priming the registry.** `MainWindow`'s ctor resolves `_config.GetActiveProfile()` →
  `ModRegistry.Find(ActiveModId) ?? ModRegistry.Default`, but `RefreshFromCatalogAsync` runs
  much later (the `Loaded` handler's `Task.WhenAll`), so at ctor time `ModRegistry.All` was
  **built-ins only** (`wol` + `aoe3-tad`). A saved COMMUNITY mod id therefore never resolved,
  `Default` (WoL) was used, and **nothing reconciled the choice once the catalog landed** — so
  **the launcher could never open on a community mod at all**, only on the two built-ins. It was
  also completely SILENT: the fallback logged nothing, so `activeModId: "improvement-mod"` in the
  config next to `Active mod profile: 'wol'` in the log (one second before the catalog merged)
  was the only evidence, and the user just read it as "the launcher forgets". Three parts, all
  load-bearing: (1) **`ModRegistry.PrimeFromCache(repo)`** — synchronous, cache-only (no network,
  no `BackgroundRefreshAsync`), called in the ctor **before** `GetActiveProfile()`. It
  **ignores the cache TTL on purpose** (this pass resolves mod IDENTITY only; the normal refresh
  right after re-merges and owns staleness) and is safe w.r.t. `ClearVanishedAssets`, which only
  runs when a PREVIOUS merge existed — the prime is always the first. (2) **The fallback is
  LOGGED** when the saved id didn't resolve — that silence is what hid the bug. (3)
  **`MainWindow.ReconcileSavedActiveMod`**, called after the startup `WhenAll`, is the backstop
  for a COLD cache (fresh install / cleared cache): if the saved id resolves now and isn't what's
  displayed, it routes through `LoadModProfile` (the same path the switcher uses — don't
  re-implement the swap). No-op in the common case. Repo resolution is shared by
  `ModRegistry.ResolveCatalogRepo` (empty → `DefaultCatalogRepo`, `"none"` → null) so the prime and
  the refresh can't target different catalogs — a divergence there would make the prime read a
  cache the refresh never writes. Note `CheckUpdatesOnStartup=false` skips the whole `WhenAll`, so
  that session relies on the prime alone — fine, it needs no network.

- **The MODS switcher is ordered favourites → most-recently-played → alphabetical, and each row
  shows "Played X ago".** `ModState.LastPlayedUtc` (nullable; `null` = never played, and absent in
  old configs reads as that) is stamped by `MainWindow.MarkModPlayed` at the **two** launch sites:
  the dashboard PLAY (`ExecutePlay`, after `GameLauncher.Launch` returns — before the
  `CloseLauncherOnGameStart` early-return, or that path would never record it) and the
  **multiplayer** in-lobby launch, which stamps the **ROOM's** mod (it needn't be the displayed
  one). **Multiplayer stamps ONLY the play time, never `ActiveModId`** — moving the active mod
  mid-session would yank the dashboard and desync `_updateService` from every
  `_config.GetActiveState()` reader. `MarkModPlayed` saves **synchronously** (unlike the
  fire-and-forget save on a mod switch) because the dashboard launch can be followed immediately
  by a hard exit that would race a backgrounded write. The ordering rule lives in the pure
  `Services/ModOrdering.OrderForSwitcher` (pinned by `ModOrderingTests`) — favourite beats recency
  (an explicit user pin must outrank it), never-played sorts last, so a fresh install is plain
  alphabetical exactly as before. **Read `LastPlayedUtc` via `_config.Mods.TryGetValue`, NOT
  `GetState(id)`** — GetState CREATES the entry, so merely rendering the popup would write a blank
  `ModState` for every mod the user never touched. The recency text uses
  `RoomAgeFormat.Coarse` (single unit: "5 min" / "2 h" / "3 d"), a sibling of `Compact` — `Compact`
  keeps a second unit ("1 d 3 h") because a live room's exact age matters, which is noise here.
  The row is built by `MainWindow.BuildModSwitchRow` as a **`Grid`**
  (`Auto | * | Auto | Auto | Auto` — icon, name+subtitle, played-ago, active check,
  favourite star), not the horizontal `StackPanel` it used to be: the recency text has to sit
  hard right, and a horizontal StackPanel measures children with INFINITE width, which made the
  name's `CharacterEllipsis` inert (a long name grew the popup instead of trimming) — the same
  lesson as the rooms table. **Column 0 is the mod's ICON**, built by the shared
  `MainWindow.BuildModIconDisc` (extracted from `BuildModCard` so the switcher and the Workshop
  cards can't drift on the monogram fallback or the asset kick). It replaced a state glyph that
  was only ever checkmark-or-blank; **the active check moved to its own column beside the star**,
  where it reinforces the gold bold name rather than being the only signal, and inactive rows
  show nothing there — a placeholder glyph would read as a second, meaningless icon now that a
  real one leads the row.

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
  **"Install another copy" installs the snapshot into a NEW folder, then — WHEN SAFE — auto-switches
  to it and brings it fully current (Option A), reusing the ACTIVE-install update flow.** When
  there's already an active copy, the new folder is `RegisterInstall`ed (id captured as
  `newCopyInstallId`) with NO version stamped (see the version-recording rule below). Then the
  `keptCurrentActive` tail: if it's **safe** — `!_isGameRunning` (a switch is blocked mid-game) AND
  the copy's mod is still the displayed/active one (`service.Profile.Id == _updateService.Profile.Id`,
  i.e. the user didn't switch mods during the background install) — it `SwitchActiveInstallAsync(newCopyInstallId)`
  (which re-checks the copy → detects its REAL version + pending) then `MaybeAutoContinueUpdateAfterInstall(Copy)`
  patches it to latest. So a copy ends up ready like a first install, and `ApplyAsync` raises the final
  bell (the "Copy installed" bell here only fires when we did NOT continue into an update — mirrors the
  fresh-install tail). **If NOT safe** (game running, or the user switched mods), it falls back to the
  old behavior: bell "Copy installed" + `RefreshActiveModBanner`, you stay on your current copy, and the
  new one updates when you switch to it by hand. A first install with no prior active copy still becomes
  active via the normal path. **Why "switch first, THEN update" (not a background non-active update):**
  making the copy active means the whole tested update flow (progress, cancel, the `--update-now` UAC
  relaunch that re-resolves the ACTIVE install) targets the copy CORRECTLY — no new plumbing. The
  alternative (updating a non-active copy in place) was deliberately NOT done: it needs a UAC-relaunch
  target arg + a second concurrent op on a non-active path. **This is UI/async — smoke-test on Windows:**
  install a copy of WoL while NOT in a game → it auto-switches to the new copy and updates it to latest;
  install a copy WHILE PLAYING the active copy → it stays on your copy (no yank), bells "Copy installed",
  and the new copy shows Update when you switch to it. Don't reintroduce a global `_isBusy` gate on the
  visible buttons. **The cleanest end-state is still a CURRENT payload** — then the copy lands on latest
  with nothing to update and the auto-switch has no patch to run.
  **Two coupled correctness rules make the "switch to the copy → it shows Update" escape hatch
  actually work — both were bugs.** (a) **The copy is registered with NO version, not the ACTIVE
  copy's version.** `InstallAsync`'s `addNewSlot` branch used to stamp the new copy's
  `LastKnownVersion`/`keptCopyVersion` with `installVersion = ResolveInstallVersion(service)` —
  which is the *active* copy's detected version, NOT the new copy's. When the payload snapshot is
  an OLDER version than the active copy (a stale `WolPayload.zip`), the copy was registered as the
  newer version it doesn't have → switching to it read "already on that version" → **no Update CTA
  → stuck**. Fix: leave the copy's `LastKnownVersion` empty; the bell shows "?" and the first
  switch re-detects the real version. (b) **`SwitchActiveInstallAsync` invalidates
  `_checkResultCache[modId]` before reloading.** The session check-cache is keyed by MOD id
  (shared across copies), and `CheckAsync` short-circuits on it, so switching to a copy replayed
  the PREVIOUS copy's `CheckResult` (its version/Update state) instead of re-detecting the new
  copy's real version from its own `data\` MD5s. Dropping the cache entry on switch forces a full
  `DetectCurrentVersionAsync` on the new copy's path. Don't remove either — together they make a
  behind copy correctly surface its Update CTA (both when the Option-A auto-switch re-checks it and
  when the user switches by hand). **The cleanest end-state remains a CURRENT payload** — a fresh
  copy then lands on latest with nothing to update, so even the fallback "switch by hand" has no
  patch to run.

- **An antivirus quarantining a payload file mid-install surfaces an ACTIONABLE error, not a raw
  IOException — `PayloadFileBlockedException`.** Windows Defender's real-time protection sometimes
  false-positives on a WoL payload file (observed: `AI3\wolai.upl`) and deletes it from `%TEMP%`
  *while* `CopyPayloadToDestinationAsync`'s `File.Copy` is reading it → `IOException`
  "...contains a virus or potentially unwanted software...". The copy loop catches ONLY that case
  — by **HRESULT `0x800700E1` (ERROR_VIRUS_INFECTED) / `0x800700E2` (ERROR_VIRUS_DELETED)**, which
  is locale-independent (the message text is localized, so don't match on it) — and rethrows
  `Services.PayloadFileBlockedException(relPath)`. `MainWindow.InstallAsync` catches it BEFORE the
  generic handler, shows the SHORT localized `InstallDefenderBlocked` in the status line and opens
  **`AntivirusExclusionDialog`** with the exact folders to exclude. **No auto-retry** — the temp
  source is already quarantined, so a retry re-fails; the guidance is the fix. Don't widen the catch
  to all `IOException` (a real disk/sharing error must still surface its own message).
  **The dialog is opened AFTER the `finally`, via a `string? antivirusBlockedFile` local set in the
  catch** — showing a modal from inside the catch would block the UI with the install still flagged
  busy and the progress strip frozen mid-run, because the `finally` that clears both hasn't run yet.
  **This detection is KNOWN and PERMANENT, so the launcher also warns BEFORE installing.**
  `ModProfile.AntivirusFalsePositiveFile` (set to `AI3\wolai.upl` on the WoL built-in only) makes
  `MainWindow.InstallAsync` show the same dialog in its PREVENTIVE mode right after the install
  folder is picked and **before anything is downloaded** — warn-but-allow, exactly like the
  disk-space confirm next to it. That is the whole point: Defender quarantines the file mid-extract,
  `VerifyExtractIntact` aborts, and by then the user has already paid for the multi-GB download.
  `LauncherConfig.AntivirusNoticeAcknowledged` ("don't show again") is **launcher-wide, not per-mod**
  — an exclusion is a property of the machine — and is read ONLY by the preventive mode; the
  post-failure mode always shows, since anyone who hit it needs the paths regardless.
  **The advice names `AppPaths.InstallTempRoot` + the install folder, NOT `%TEMP%` wholesale** (the
  old string said to exclude all of `%TEMP%` and to "turn off real-time protection briefly" — both
  bad advice, and neither told the user the actual path). **All THREE writers of that folder derive
  from `InstallTempRoot`** — `NativeInstallService.TempDirectory`, `InstallerService.TempDirectory`
  and `UpdateService.ApplyUpdatesAsync` (which stages patch `.tar.xz` there, so the UPDATE flow is
  exposed to the same false positive an install is) — so the advice can't name a folder the code no
  longer writes to. Don't rebuild that path inline anywhere; a fourth copy that drifts makes the
  guidance wrong in silence. (Unrelated `Path.GetTempPath()` uses — `DeltaPatchService`,
  `DiagnosticLog`, `TranslationService`, `SelfInstallService`, `RadminVpnService` — create their own
  GUID-named scratch dirs and are deliberately NOT part of this.) **The launcher NEVER edits antivirus configuration** — no
  elevation, no `Add-MpPreference`: an installer that excludes itself from Defender is precisely the
  behaviour AV heuristics punish, which this project already fights over its own `.exe`. It only
  names the folders and copies them to the clipboard.
  **Correction to what this bullet used to claim:** the durable fix is NOT the SignPath signature —
  signing the launcher's `.exe` does nothing about Defender quarantining a WoL *data* file — and
  "report it to Microsoft" is already done: the detection predates the launcher entirely. `!MTB`
  verdicts come from re-trained ML models, so a correction can regress. Treat it as permanent and
  warn early; don't re-propose either as the fix.
  **That catch alone only covers the RACE (blocked mid-`File.Copy`) — the likelier, SILENT case is
  the AV quarantining the file AFTER a successful write, which threw nothing and produced an install
  missing it, permanently and invisibly.** `CopyPayloadToDestinationAsync` enumerates the DISK
  (`Directory.GetFiles(extractedFolder)`) with no expected-file list, so a vanished file is simply
  never copied; `WriteManifest` then records only what WAS copied, so the file stops being
  "expected" and **Verify reports the install intact forever** while Repair never restores it. That
  is the silent path a mid-game OOS traces back to (a missing AI file only loads once the match is
  under way — hence 8-40 min, not instant). Closed by three guards, all raising the SAME
  `PayloadFileBlockedException`: (1) `entry.ExtractToFile` now carries the same HRESULT catch as the
  copy (an AV block during EXTRACTION used to surface as a raw IOException); (2)
  `ExtractPayloadAsync` returns `PayloadExtract(Root, Written)` and `VerifyExtractIntact` re-checks
  every written path post-extract (fails fast, before the multi-minute clone); (3) the SAME check
  runs again at the top of `CopyPayloadToDestinationAsync` — **this is the one that matters**,
  because `InstallAsync`'s order is extract → **clone (~2 min on a real payload)** → flatten →
  overlay copy, so the extract sits in `%TEMP%` for MINUTES with the AV free to act. **Load-bearing:
  the expected set is built by COLLECTING the `destPath`s the extract loop actually writes — never
  reconstructed from `archive.Entries`.** Rebuilding it would have to re-derive the
  `NormalizePayloadRoot` wrapper rebasing, the directory entries (`entry.Name` empty) and the
  zip-slip rejects; get any subtly wrong and **every healthy install aborts with an antivirus
  message — worse than the bug**. The loop `continue`s past those before writing, so they can never
  enter the list. Existence-only (a quarantined file IS a deleted file), ~1 s per pass. This
  ENFORCES the byte-faithful invariant rather than conflicting with it: `RemoveStaleBuildArtifacts`
  stays the documented no-op, and nothing here deletes or strips. Pinned by `PayloadIntegrityTests`
  — where the **no-op tests are the important ones** (intact flat payload + wrapped payload with
  directory entries must NOT abort); if those ever fail, the guard is breaking real installs.
  (Caveat: the message names the antivirus, but "Clear temporary files"
  (`ModPropertiesDialog.ClearTempBtn_Click`) is NOT gated on `_isBusy`, so a user wiping `%TEMP%`
  mid-install would get the same, misattributed error — still better than today's silent
  zero-overlay install.)

- **WoL extracts its payload STRAIGHT into the install folder — `ModProfile.DirectPayloadInstall`
  — so no loose copy of the mod ever exists in `%TEMP%`. Read the honesty paragraph before you
  describe this to anyone as an antivirus fix.** The staged path writes every payload file
  **twice** (into `…\native-install\extracted\`, then again at the destination) and runs the whole
  AoE3 clone in between, so for WoL a loose `AI3\wolai.upl` sits in `%TEMP%` for MINUTES — and
  `%TEMP%` is the folder users report Defender acting on. `NativeInstallService.ExtractPayloadToDestinationAsync`
  replaces the `ExtractPayloadAsync` + `CopyPayloadToDestinationAsync` pair, returning the SAME
  `OverlayCaptureResult` so the delete-list strip, `ClassifyOverlay`, `ApplyUpdateDeletions`,
  `ApplyPrivateSetupPath`, `PruneMissingHashes` and `WriteManifest` are untouched. It covers
  install, repair AND update (both `InstallAsync` and `InstallModOnlyAsync` used that one pair).
  **Honesty:** antivirus scans a write wherever it lands, so a detection can still fire at the
  DESTINATION and raise the same `PayloadFileBlockedException`. What this buys is one write
  instead of two, no long-lived loose copy, and an exclusion list of one folder instead of two —
  **not** a way to hide the file. Don't "improve" it by renaming the file, staging it under
  another extension, or touching AV configuration; that is the evasion this project has always
  refused. Set on the WoL built-in only and deliberately **NOT** projected from a catalog
  `mod.json` — it reshapes the pipeline, so it is being run in on one known mod first.
  **Five things are load-bearing:**
  (1) **Order.** The extraction MUST run after the clone and `FlattenBinSubfolder` — `CloneAsync`
  copies with `overwrite:true`, so cloning over an already-extracted payload puts the base game's
  files back on top of the mod's. That is why `InstallAsync` now goes download → **validate zip**
  → clone → flatten → extract-to-destination.
  (2) **`ValidatePayloadZip` before the clone.** The staged path got the corrupt-download check
  for free by extracting first; extracting last would make a truncated download surface only
  AFTER the ~2 min clone, and `MainWindow`'s 3-attempt corrupt-payload retry would pay for that
  clone every time. Opening the archive reads its central directory, which IS the check.
  (3) **The wrapper rule is a TWIN, and the twins must agree.** `ResolvePayloadPrefix` reads the
  zip's ENTRY NAMES because there is no extracted folder to inspect, while `NormalizePayloadRoot`
  reads the FOLDER. They are kept in step by deriving the relative names from the same
  `GetFullPath(Combine(root, entry.FullName))` the staged path uses (so backslash separators,
  `./` prefixes and rooted/escaping entries normalise or drop identically) and by feeding the rule
  **FILE entries only** — the staged extraction skips directory entries BEFORE creating any
  directory, so an explicit empty `EmptyDir/` never reaches disk; counting it would see two
  top-level names where the disk sees one and the wrapper would not be stripped. Divergence is not
  cosmetic: the payload lands one level deep (the mod does nothing) and, for a `GitHubReleases`
  mod, `ApplyUpdateDeletions` would read every previously-shipped file as "no longer shipped" and
  delete it. Pinned by `DirectPayloadInstallTests.DirectExtraction_LandsTheSameFilesAsTheStagedPath`,
  a **differential** test over 10 zip shapes — the highest-value test in the change. It also pins
  the surprising case where a payload's only top-level folder is `data\`: that folder IS stripped,
  because the staged path strips it too.
  (4) **Hash + write in ONE pass, and the digest is recorded only after the stream CLOSES.**
  `IncrementalHash` (reset per entry) replaces extract → re-read → copy, so the install does about
  a third less disk I/O. Committing the digest while the file is still open would let a failed
  flush leave the manifest describing bytes that never reached disk — Verify would call that file
  corrupt forever and Repair would re-download gigabytes to "fix" it. The loop also stamps
  `LastWriteTime` from the zip entry: `ExtractToFile` did that and `File.Copy` preserved it, so
  without it every file would carry "now" and the install would visibly differ from a canonical
  peer's. And it honours `Pause`, which the staged EXTRACT never did (only the copy that followed
  it) — this is the long phase now.
  (5) **A half-written install can no longer be adopted — `ModInstallProbe.InstallInProgressMarker`.**
  This is the hazard the reorder creates, and it is nasty: a folder holding the clone plus part of
  the payload satisfies EVERY content signal (the clone supplies the probe file `data\stringtabley.xml`
  and the engine DLLs; the marker `art\zulushield` lands early in the payload), and
  `UpdateService.BroadFallbackScan` adopts the first content match it finds **without asking** — so
  an interrupted install could silently become "the install" and the launcher would offer PLAY on a
  mod missing most of its files. `InstallAsync` writes `.aoe3ml-install-in-progress` into the
  destination before the clone and deletes it **immediately before `WriteManifest`** (which
  enumerates the folder and would otherwise hand the marker to the uninstaller); `Inspect` returns
  the new `ProbeOutcome.InstallInProgress`. Written **only when the destination has no manifest
  yet**, so a failed reinstall over a working install can't strand it behind the marker. This also
  closes the pre-existing, smaller version of the same hole (a cancelled overlay copy). Pinned by
  the `InterruptedInstall_*` / `FinishedInstall_*` cases in `ModInstallProbeTests` — the
  finished-install one is the no-op that matters.
  **Temp lifecycle (all mods, not just WoL):** the `WolPayload.zip.00N` parts are deleted right
  after a successful `ConcatenateFilesAsync` and the combined zip right after a successful
  extraction. Nothing reads either again — `DownloadFileAsync` only ever resumes from a `.part`,
  never from a finished part file, so a retry re-fetches from byte 0 regardless. Peak `%TEMP%` for
  a WoL install goes from ~3× the payload (parts + combined zip + extracted tree, ~12 GB) to ~1×.
  **Net-new classification survives a retry — `OverlayBaseline`.** `existed` used to be read live
  from disk, so if an attempt died mid-extraction and MainWindow's corrupt-payload retry ran a
  second one over the same folder, everything the first attempt wrote read as pre-existing and
  dropped out of `FreshOnDisk` — and from there out of the manifest's `OverlayNetNew`, which is
  what `ApplyUpdateDeletions` uses to remove files a later release stopped shipping. Inert for WoL
  (uninstall removes the whole cloned folder, and `ApplyUpdateDeletions` never runs for
  `WolPatcher`) but NOT for anyone else: **every other mod is `GitHubReleases`**, where that list
  is live, and for an `InPlaceOverlay` mod an under-populated one would leave files behind in the
  player's own game folder on uninstall. `MainWindow.InstallAsync` now creates one
  `NativeInstallService.OverlayBaseline` **outside** the retry loop and threads it into both
  branches; the callee captures the destination's file set at the only correct moment — after the
  clone and flatten for `InstallAsync` (the base game IS the baseline), before writing anything
  for `InstallModOnlyAsync` — and `CaptureOnce` makes every later attempt reuse it. Passing none
  (as Repair does, having no retry loop) keeps the live check, so nothing else moves. Pinned by
  `DirectPayloadInstallTests.Baseline_KeepsNetNewIdenticalAcrossARetry_WhichLiveChecksDoNot`,
  whose **second half is the point**: without a baseline the second pass reports NOTHING as
  net-new, which is the bug written down as a test.
  **`ValidatePayloadZip` runs on the mod-only path too**, not just before the clone. Repair and
  update write over an install that WORKS; the staged path could not damage it (a bad zip died in
  `%TEMP%`), and validating first is what preserves that property — a corrupt download aborts
  before the first file is overwritten instead of halfway through.
  **Before making this the default for every mod**, two things: (a) let WoL ship one release on it
  — that is what the per-mod flag is for, and no other mod has an `AntivirusFalsePositiveFile` or a
  multi-GB multi-part payload, so their staged copy costs seconds and the urgency is low; (b)
  decide `InPlaceOverlay` deliberately. Nothing ships on that type today (Napoleonic Era and
  Struggle of Indonesia use `IsolatedFolder` + `privateSetupPath`, and the only `InPlaceOverlay`
  profile is the detect-only stock game), but its destination IS the player's real Age of Empires
  III folder, where a half-finished extraction is not recoverable by deleting the folder and the
  in-progress marker does not apply (that path goes through `InstallModOnlyAsync`). Either keep
  that type on the staged path or give it rollback-on-failure — `ArchiveService`'s `DeleteCreated`
  is the existing pattern to reuse, not a new one to invent.
  **Progress:** the merged step reports `InstallPhase.ModOverlay`, and the extract weight is set to
  **0** — which is what keeps the overlay handler's cumulative base (`download + extract + clone`)
  correct for the reordered pipeline with **no other arithmetic change**. Weights become DL 40 /
  Clone 25 / Overlay 30 / Finalize 5 (install), DL 50 / Overlay 45 / Finalize 5 (mod-only) and
  DL 60 / Overlay 40 (repair). Accepted cosmetic cost: `SpeedLabelKeyForPhase` maps `ModOverlay` to
  "💾 Copia" while it is really decompressing — accurate about writing to disk, and the status line
  says "🔧 Aplicando mod (n/m)".

- **`FolderCloneService.CloneAsync` returns files actually COPIED — keep `filesCopied++` INSIDE the
  try.** It used to sit after the try/catch, so a file skipped for `UnauthorizedAccessException`
  (Steam's read-only `_CommonRedist`) or a sharing violation (`0x80070020`, file locked by the Steam
  client) still counted. That made `Clone complete: N/N` mean **attempted/enumerated**, not copied —
  and it is not cosmetic: `InstallAsync` gates on `if (clonedFiles == 0) throw
  InstallBaseGameMissingException`, so **a clone where every file failed would return the full count
  and sail straight past the gate** that exists to stop a mod overlaying an empty base game. Skips
  are now counted separately (`skippedAccess`/`skippedLocked`) and reported in the summary — the
  same accounting `FlattenBinSubfolder` already does. **Skips stay NON-fatal on purpose**: the
  catches exist because some files are legitimately unreadable, and those installs work today. Don't
  make a skip fail the install; just count it honestly. Pinned by `CloneCountTests`.

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
  **A catalog `approvedReleaseTag` can go STALE SILENTLY, and the failure is total —
  `followLatest` is the fix, not a nicety.** A modder re-tagging their own releases is
  normal housekeeping, not a mistake: Napoleonic Era and Struggle of Indonesia were both
  catalogued at `1.0.0` and their authors later re-tagged to the mods' real version
  numbers (`2.1.7b`, `1.2`). `ResolveAssetAsync` fetches `/releases/tags/<approved>` with
  `GetFromJsonAsync`, so the now-missing tag 404s → throws → **every install of that mod
  fails for every player**, with nothing in the catalog looking wrong. It is invisible from
  this side: the manifest still validates, CI still passes, and the maintainer's own machine
  keeps working if the mod is already installed. Nothing in the launcher or the catalog CI
  checks that the approved tag still exists — a periodic catalog-side check would be the
  real guard, and doesn't exist yet. Meanwhile the mitigation is to have every
  GitHubReleases mod whose author publishes on their own schedule opt into `followLatest`,
  which reduces the pinned tag to an offline seed. What you give up is per-version review:
  a release that renames the executable or reshapes the payload makes `probeFile` / `marker`
  / `executable` stale and breaks installs unreviewed — bounded by the `CountCloneableFiles`
  preflight, the 0-file abort and the three-`data\`-file verify, all of which fail loudly.
  **A payload can be checked WITHOUT downloading it** (both mods above were, at ~450 MB and
  ~105 MB): range-GET the zip tail, find the EOCD, range-GET the central directory and read
  the names — the same trick `RemoteZipIndex` uses for version fingerprinting. Do that
  before trusting a manifest's `probeFile` / `marker` / `multiplayerProbeFiles` against a
  release nobody has installed yet.

- **`ActionPanelControl` (and everything else in `LegacyPlayContent`) is INVISIBLE
  — setting a control's `Visibility` there paints NOTHING on screen. Only what
  `SetPrimaryAction`/`Refresh*` MIRROR into the dashboard is real UI.**
  `MainWindow.xaml`'s `LegacyPlayContent` Grid is `Visibility="Collapsed"` and
  exists purely so the old code-behind keeps compiling/working (`ActionPanel`,
  `StatusCard`, `ProgressPanel`, `MainTabs`, `HeroBanner`); the cinema dashboard
  above it mirrors selected pieces. **A `Visibility = Visible` on a child of a
  Collapsed parent is a no-op** — that's how GitHubReleases mods ended up with NO
  reachable way to update: `ApplyCheckResult` dutifully set
  `ActionPanelControl.UpdateButton.Visibility = Visible` while the user only ever
  saw PLAY (reported for Improvement Mod sitting on `05.07.2026` with `19.07.2026`
  released, correctly resolved in the log and still unofferable). `SetPrimaryAction`
  mirrors ONLY the primary button into `DashboardPlayButton`/`DashboardPlayButtonText`;
  there is **no dashboard mirror for `UpdateButton`**. Fix: GitHubReleases now flips
  the PRIMARY CTA to `PrimaryAction.Update` when an update is on offer — the same
  rule WolPatcher already followed ("when patches are pending, the primary CTA IS
  update"). **Paired and mandatory:** `PlayButton_Click`'s `case PrimaryAction.Update`
  routes on the MECHANISM (`GitHubReleases` → `RepairInstallAsync(asUpdate:true)`,
  else `ApplyUpdateWithElevationCheckAsync`) — the latter applies `_pendingDownloads`,
  which is ALWAYS empty for GitHubReleases, so without the split the new CTA would
  do nothing. Gate on the mechanism, NOT on `GitHubUpdateAvailable()`: that predicate
  needs a known installed version and is false for the detected-install case that
  also raises this CTA. When adding any new affordance, mirror it into the dashboard
  — don't just unhide something in the legacy panel.
  **The same invisibility makes the primary CTA the ONLY reachable Stop, so
  `SetPrimaryAction` COERCES `Play`/`Update` to `Stop` while `_isGameRunning`.** The real
  `StopButton` is `Visibility="Collapsed"` (`Controls/ActionPanel.xaml`) inside the
  Collapsed `LegacyPlayContent`, and the sole route to `StopButton_Click` is
  `PlayButton_Click`'s `case PrimaryAction.Stop`. `ApplyCheckResult` sets `Play`/`Update`
  WITHOUT consulting `_isGameRunning`, and a `WolPatcher` check goes to the network — so its
  result routinely lands a second AFTER the launch, overwriting the Stop the monitor had
  just set. The old `Play` branch only consulted `_isGameRunning` for the LABEL, painting a
  disabled "PLAYING…" while leaving `_primaryAction = Play`, which made the Stop case
  unreachable and stranded the user with a running game and no way to close it (reported for
  Wars of Liberty; the stock game hid it because its `Manual` check resolves locally and
  finishes before you press Play). The coercion sits at the top of `SetPrimaryAction` — the
  one place no caller can bypass — mirroring the rule `UpdateGameUI` already encodes.
  `Update` is coerced too (its handler opens with `EnsureGameNotRunning()`, which asks to
  close the game anyway); `Install`/`Hidden` are left alone, since they can't legitimately
  co-occur with a running game and forcing them would mask a real state bug. `BtnPlaying`
  is now unreachable by construction. **Any new primary action must respect this** — an
  action that can be raised by a check completing mid-game will strand the user again.

- **The launched game is tracked by PID, not by executable name.** `GameProcessName()`
  derives from `ModProfile.GameExecutable`, and **WoL and the stock game both declare
  `age3y.exe`** (`ModRegistry`), so `GetProcessesByName("age3y")` cannot tell them apart.
  Consequences when it was the only signal: the 2 s exit monitor kept reading "still
  playing" while an unrelated AoE3 sat open, so the CTA stayed on Stop after the mod had
  closed; and Stop killed **every** `age3y.exe` on the machine, including copies the
  launcher never started. `GameLauncher.Launch` therefore returns
  `GameLaunchResult.ProcessId` — the pid from `StartReparented`, or `Process.Start(...)?.Id`
  on the fallback, `-1` when the OS hands back nothing usable (the elevated ShellExecute
  path). `MainWindow` stores it in `_gameProcessId` (cleared in `OnGameExited`) and both
  `StartGameMonitor`'s tick and `StopButton_Click` go through `TryGetLaunchedGameProcess()`.
  **The by-name sweep stays as the fallback for `ProcessId <= 0`** — don't delete it, it's
  what keeps the elevated launch working. `EnsureGameNotRunning` deliberately KEEPS the
  by-name sweep: its question is "is anything holding this folder open?", not "what did I
  launch?".

- **The follow-latest cache (`LastKnownLatestVersion` + `LatestReleaseETag`) is
  TIED to `ModState.LatestReleaseRepo` and discarded when the catalog moves a mod
  to a different repo — without that, a repo migration strands every existing user
  on the OLD version, silently.** The cached tag+ETag pair belongs to ONE repo but
  used to record only the values, never the source. When Improvement Mod moved from
  `papillo12/Improvement-Mod` to `mandosrex/AoE3ImpMod_New`, the stale ETag still
  MATCHED the old repo, so `GET /releases/latest` there answered **304** →
  `GetLatestReleaseTagAsync` returned a null tag → `CheckCoreAsync`'s fallback chain
  (fresh → cached → approved) served the **cached old-repo tag**, so the launcher
  reported `LatestVersion='Improvement-Mod'` while the catalog already said
  `19.07.2026`. Nothing errored and nothing was logged: indistinguishable from "no
  newer version". Fix: `LatestReleaseRepo` records where the pair came from; on a
  mismatch the whole pair is ignored (no `If-None-Match`, no cached-tag fallback)
  and the tag resolves fresh, mirroring `ModCatalogService.LoadFromCache`, which
  already discards its cache when `cache.Repo` != the active repo. An empty
  `LatestReleaseRepo` (pre-existing configs) reads as "doesn't match", which is the
  safe direction. **The 304 branch now LOGS** — it was the only path that returned
  without a trace, and that silence is what hid this. **Second half of the same
  incident, deliberately NOT fixed (known gap):** a catalog refresh does NOT rebuild
  the ACTIVE `UpdateService`'s `ModProfile`, so a launcher left running across a
  catalog change keeps querying the OLD `sourceRepo` until restarted; the cache fix
  bounds the damage (it can no longer get stuck on a wrong-repo tag) but seeing a
  migrated repo still needs a restart.

- **`GitHubReleases` mods now IDENTIFY the installed version by CRC-matching a few
  local files against each release's zip index, read remotely over HTTP range
  requests — no download, no modder cooperation.** This is the GitHubReleases
  answer to what WoL gets from `UpdateInfo.xml`'s per-version MD5s: a
  GitHubReleases version is otherwise just a string the launcher stamps when IT
  installs, so a mod detected on disk, updated by hand, or migrated to another repo
  has no usable version and can never be compared against the latest.
  `Services/RemoteZipIndex.cs` fetches the archive tail (~64 KB), finds the EOCD,
  then pulls the central directory (~250 KB for ~2200 entries) and parses
  `name → (CRC-32, size)`; `Services/ModVersionFingerprint.cs` indexes up to
  `MaxCandidates` (4) newest non-prerelease releases, keeps entries whose CRC
  DIFFERS across them (an identical file proves nothing), CRC-32s the **12
  smallest** such files locally (≤25 MB total) via `HashService.ComputeCrc32Async`,
  and `Decide` requires ≥60 % agreement AND a strict margin over the runner-up —
  "unknown" beats a wrong version. **Measured on the real Improvement Mod:** 2006
  common entries, only 15 discriminating, smallest 10.8 KB, verdict 15-0 correct.
  **Load-bearing details:** (a) range reads hit the **asset CDN**, not
  `api.github.com`, so they cost nothing against the 60/h limit — the only API call
  is `ListReleaseCandidatesAsync`, which reuses the SAME listing endpoint the
  version picker already calls; a ranged GET returns **206** and the github.com→CDN
  redirect preserves the Range header (verified: 100 bytes of a 1.19 GB asset).
  (b) The pass is **self-terminating**: it runs only when the stored version is
  empty or is not a real tag of the current repo, and persisting the identified tag
  makes that condition false — without that property it would re-fingerprint on
  every check for anyone merely behind. (c) A **single** indexable release returns
  null on purpose: with nothing to discriminate against, a match would only prove
  "some version of this mod". (d) **Zip64** (>4 GB payloads) is not parsed —
  `TryLocateCentralDirectory` returns false on the 0xFFFFFFFF sentinels and the
  whole chain degrades to the previous behaviour, like every other failure here.
  WoL/`WolPatcher` is untouched (it has MD5 detection) and the stock game never
  enters this path.

- **A DETECTED `GitHubReleases` install has NO version, and that used to mean it
  could never be updated — so a valid install with an unknown version is offered
  the Update button anyway.** A GitHubReleases mod's installed version is a plain
  string stamped ONLY when the launcher itself installs/updates/repairs it
  (`MainWindow` install tail + `RepairInstallAsync`'s post-verify tail); unlike
  WolPatcher there are no file hashes to deduce it from, and
  `UpdateService`'s `LastKnownVersion` write is circular for this mechanism
  (`current` IS `LastKnownVersion`), so it can never bootstrap itself. Any mod the
  launcher merely **finds** on disk — the content probe, `ModInstallScanner`, the
  "¿YA LO TIENES?" search, a manual folder pick — therefore keeps
  `LastKnownVersion == ""` forever. `ApplyCheckResult`'s GitHubReleases branch
  compares installed-vs-latest, so an empty installed version made `ghHasNewer`
  false and **hid the Update button permanently** (reported for a detected
  Improvement Mod: PLAY worked, a new release existed, nothing was ever offered).
  Fix: `ghUnknownInstalled` (valid install + empty `ghCur` + known `ghLatest`)
  also raises the Update CTA, with the neutral `StatusGhVersionUnknownCanUpdate`
  instead of "update available" — we don't know their copy is old, only that a
  release exists. **This does NOT violate the "never push a destructive
  reinstall" invariant**: for GitHubReleases the update is a re-overlay IN PLACE
  (`RepairInstallAsync(asUpdate:true)`), not a from-scratch install into a new
  folder, and PLAY stays the primary action. It is also **self-healing** — the
  repair tail stamps `LastKnownVersion` with the effective tag, so after one click
  the mod joins the normal follow-latest cycle and this branch stops firing. The
  **bell is deliberately NOT changed**: `MaybeNotifyUpdateAvailable` keeps its
  `versionKnown` gate, so a detected mod shows the button but never spams a
  notification.

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

- **A compat layer Windows pins on `age3y.exe` is what forces the UAC prompt on the stock
  game — the launcher DETECTS it (read-only) and offers to remove it on an explicit click,
  and never writes AppCompatFlags on its own.** This bullet supersedes an earlier one that
  said the launcher must never touch compat settings at all; **two of its factual claims were
  measured and found FALSE** (see below), so re-read this before "restoring" the old rule.
  **The chain, verified end to end on a reporting machine:** the manifest went
  `requireAdministrator` → `asInvoker` (`aa13bdd`, for the Run-key auto-start — see that
  bullet), so the launcher stopped running elevated and the game stopped INHERITING elevation;
  the stock profile launches the RAW `bin\age3y.exe`; Windows' Program Compatibility Assistant
  then pinned **`~ WINXPSP3`** on that exe under `HKCU\…\AppCompatFlags\Layers` on its own; that
  layer makes `CreateProcessW` fail with **740 `ERROR_ELEVATION_REQUIRED`**, so
  `DetachedProcessLauncher.StartReparented` returns `-1` and `GameLauncher` falls back to
  `UseShellExecute=true` — the only path that can elevate — which is what raises the UAC prompt.
  **The `~` prefix is PCA's signature**: it marks a layer Windows applied itself
  (`age3.exe`/`age3x.exe` on the same machine carry `WINXPSP3 RUNASADMIN` **without** it —
  those are the user's own, set years earlier). The launcher still has **ZERO code that WRITES
  AppCompatFlags unprompted**.
  **Two corrections to what this file used to assert.** (1) *"Clearing the layer doesn't help —
  the elevation is intrinsic"*: **false.** Removing the layer removes the UAC prompt, and the
  log flips from `Detached launch unavailable; falling back to Process.Start` to
  `Game launched detached (pid …)` — so the layer was ALSO silently defeating the explorer.exe
  re-parenting that keeps the game alive when the launcher is force-closed. That second cost is
  the reason this is worth fixing rather than tolerating. (2) *"the UAC dialog says 'Age of
  Empires III: The War Chiefs' because that's age3y.exe's embedded product-name"*: **false** —
  that string does not exist inside the binary (its `FileDescription` is "Age of Empires III
  Expansion 2"; "The WarChiefs" belongs to `age3x.exe`). The name comes from Windows' own shim
  database, which also fires a harmless built-in fix ("Age of Empires 3 Expansion 2",
  `{984d57c7-…}`) on EVERY launch including ones that need no elevation. Still cosmetic — it
  does launch Asian Dynasties.
  **What the launcher does now.** `Services/AppCompatLayerService.cs` reads the layer
  (`Probe`, pure parser `Parse` pinned by `AppCompatLayerTests`) and can delete it (`Remove`).
  `GameLauncher.Launch` returns a `GameLaunchResult` whose `NeededElevation` reflects the 740,
  and `MainWindow.MaybeOfferCompatLayerFix` opens `CompatibilityLayerDialog` explaining that
  **Windows** did this, with "Remove it" / "Open file properties" and a don't-ask-again flag
  (`LauncherConfig.CompatLayerNoticeAcknowledged`). **The old rule's spirit is what constrains
  it, and these guards are load-bearing:** nothing happens without an explicit click; only
  **HKCU** is written (never HKLM — needs admin and isn't this user's to undo); only the one exe
  that was launched; the removed value is logged verbatim so it can be restored by hand; and a
  layer WITHOUT the `~` marker (the user set it deliberately) or in HKLM hides the Remove button
  entirely. **The offer is deferred to `OnGameExited`**, never shown at launch — a modal over a
  game that is opening steals focus and can knock AoE3 out of fullscreen (same reasoning as the
  toast rule). On the CANCEL path it shows immediately, since nothing is running.
  **Trigger, unproven and now unmitigated:** what makes PCA pin the layer is not established —
  the event log only reaches back to the day it happened, so there is no "before" to compare
  against. Repeatedly `TerminateProcess`-ing a legacy game is a known PCA trigger, and a
  graceful-close attempt was built for exactly that; it was then **measured and removed**
  (AoE3 never honours WM_CLOSE, so it waited 5 s and killed the process anyway — see the
  `GameProcessCloser` bullet). So the launcher still terminates the game abruptly when the user
  presses Stop, and there is no mitigation in place — the detect-and-offer flow above is the
  whole answer, which is fine precisely because it is deterministic rather than a theory.
  **Don't re-add a SILENT compat write, and don't widen this past the one exe / HKCU /
  explicit-click shape.**
  **The genuine MP bug that WAS fixed:** the MP `LaunchAndWatch` fallback launched via
  `UseShellExecute=false` (needed for `Process.Exited`), which CANNOT elevate — so for an
  elevation-required exe `process.Start()` threw `Win32Exception` `ERROR_ELEVATION_REQUIRED`
  (740), the MainWindow launch hook swallowed it, and **the game silently never opened from
  multiplayer** (the dashboard `Launch` fallback already used `UseShellExecute=true`, so it at
  least prompted UAC and launched — the MP path was strictly worse). Now the fallback wraps
  `process.Start()` and, on a 740, RETRIES with `UseShellExecute=true` so the game launches
  (with the UAC Windows imposes). It only engages when BOTH the reparent AND the watched launch
  fail on elevation, so normal `asInvoker` mods (WoL/IM/SoI) keep the reparented watched launch
  untouched.
  **CORRECTION — this bullet used to call the lost exit watcher an acceptable trade ("the game
  starting matters more, and the caller gets a best-effort `Process`, enough for the last-played
  stamp"). That badly understated it.** `Process.Exited` was the ONLY trigger for
  `OnGameExitedAsync`, and the multiplayer path had no polling backstop — so losing it did not
  cost the tree-kill, it switched off **the entire post-match pipeline**, silently: no recording
  read, no match reported, no `game_ended` sent (the room and its Discord embed stuck "In game"),
  and `_matchContext` leaking into the next match. A real player's bundle showed **three launches
  and not one game-exit handler run**; had he hosted, none of his matches would have been
  reported at all. And it fed itself, because the offer to remove the compat layer causing the
  elevation hung off the DASHBOARD's exit handling, so a multiplayer-only player could never
  reach it. **What is actually lost now is only `Kill(entireProcessTree)`** —
  `Services/GameExitWatcher.cs` reports the exit by polling when the event cannot fire, and the
  compat offer reaches multiplayer via `MainWindow.OfferPendingCompatLayerFix`. See the
  exit-detection bullet in `.claude/rules/multiplayer.md`. `LaunchAndWatch` also returns a
  `WatchedLaunch` rather than a bare `Process?`, because a non-null process with no watcher on it
  is exactly what made this invisible for so long.

- **Declining the UAC prompt is a DECISION, not a failure — and `StartReparented` must log
  WHY it failed.** Two small gaps that together made the compat-layer bug above cost an hour
  instead of a glance. (1) `GameLauncher.Launch`'s fallback `Process.Start` sat outside any
  `try`, so clicking "No" on the UAC prompt threw `Win32Exception` 1223 (`ERROR_CANCELLED`)
  straight into `ExecutePlay`'s generic `catch`, which showed the framework's raw message in a
  red error dialog — telling the user something broke immediately after they chose that it
  shouldn't happen. It now raises `GameLaunchCancelledException` (same shape as
  `NsisExtractionException.DeclinedByUser`), caught BEFORE the generic handler: status line and
  log only, plus the compat-layer offer. The MP `LaunchAndWatch` elevated retry does the same.
  (2) `DetachedProcessLauncher` declares `SetLastError=true` on every P/Invoke and **never read
  `Marshal.GetLastWin32Error()`**, so "no explorer in this session", "OpenProcess denied" and
  "this exe demands elevation" all collapsed into one anonymous `-1` and a single
  `Detached launch unavailable` line. The `out int lastError` overload now names the failing
  step and its code, and calls out 740 explicitly. **Don't drop either** — the 740 is what tells
  `GameLauncher` that elevation (not some interop hiccup) cost the re-parenting, and it is the
  signal the compat-layer offer keys off. Note the first `InitializeProcThreadAttributeList`
  call is EXPECTED to fail (that's how it reports the buffer size); its error is deliberately
  not inspected.

- **Stopping the game KILLS it outright — `Services/GameProcessCloser.Stop`. Asking politely
  first was tried, measured, and removed; don't reintroduce it.** The class exists only to give
  the five kill sites one shared "kill, confirm with a short `WaitForExit`, log the pid"
  instead of the same try/catch copied five times: `MainWindow.StopButton_Click` +
  `EnsureGameNotRunning` (synchronous, as they always were — after a `Kill` the wait returns in
  milliseconds), and the three multiplayer ones (`MultiplayerTab` leave-room / `game_cancelled`
  / `EndMatchAsync`), which pass `killEntireTree: true` and run on `Task.Run` so a WS/UI
  callback can't block on the wait.
  **What was tried:** `CloseMainWindow()` → wait 5 s → `Kill()` only if refused, to avoid
  `TerminateProcess` (half-written saves, and the suspected PCA trigger behind the compat
  layer). **The log killed it:** across six stops, `"closed gracefully"` appeared **zero**
  times — four `"ignored the close request after 5000 ms"`, two `"has no main window to ask"`.
  AoE3 answers WM_CLOSE with its own in-game confirmation dialog, invisible while the launcher
  has focus, so the wait always expired. That made it strictly worse than doing nothing: five
  seconds of frozen UI (the Stop button calls this synchronously) **and** the same abrupt kill
  at the end, so the PCA mitigation never applied once. Every mod runs the same 2007 engine, so
  no profile will behave differently.

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
  emits the **newest** version's `id@contentHash` (one bell per new version).
  **Multi-repo caveat:** with several folder repos merged (below), "newest" here
  means the entry's top-level version, and for an id the DEFAULT repo also ships,
  `MergeFolderEntries` keeps the top-level fields (gear one-click apply + this
  notification key) from the **default** repo's entry ("mine is the default") — so
  a *newer* version of that same id coming from an EXTRA repo is NOT what the gear
  applies and does NOT re-bell here; it's reachable only via the Properties →
  Language version picker (labelled by source repo). Top-level `Version` can thus
  differ from `Versions[0]` in the collision case. The repos come from the profile's
  `Translations.FolderRepo` (new) + `.Repo` (legacy), resolved by
  `EffectiveTranslationsFolderRepos()` (PLURAL) + `EffectiveTranslationsRepo()`; the
  catalog `mod.json` `translations` block gained a `folderRepo` field. **The user can
  add MULTIPLE folder repos** (Settings → CATALOG & SOURCES, the "Translation sources"
  subsection — the standalone TRANSLATIONS tab was folded in here to avoid new players
  reading it as "where to get a translation"): the profile's own folder repo
  (the default, always first) PLUS `config.ExtraTranslationsFolderRepos` (a hand-added
  `owner/repo[]`), all fetched and merged. `config.CommunityTranslationsDisabled` (the
  "Disable" checkbox) turns everything off. `EffectiveTranslationsFolderRepos()` returns
  the default + extras (de-duped), or an EMPTY list when the profile doesn't participate
  (no Translations block) or translations are disabled — the participation gate means an
  added repo can't inject packs into a mod that opted out. The merge is
  `TranslationRegistryService.MergeFolderEntries` (pure, unit-tested): one entry per id;
  on an id collision the repos' versions are UNIONED into that entry's `Versions[]`
  (deduped by contentHash, each version keeps its `SourceRepo`, surfaced in the Mod
  Properties version picker labelled by repo), and the base/display+one-click-apply
  metadata comes from the DEFAULT repo's entry when it has that id (else the newest
  version's owner). `RefreshTranslationIndexAsync` + the notif sweep filter the merged
  index by `targetMod` so a foreign repo's packs don't pollute the active mod's menu.
  Each folder repo is fetched in its own try/catch so one bad/rate-limited repo doesn't
  blank the menu. The deprecated single-string `config.TranslationsFolderRepo` is migrated
  into the new fields on load (`MigrateTranslationsFolderRepo`). Pinned by
  `MultiTranslationRepoTests` + `TranslationMergeTests`. **The dedup /
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

- **The multiplayer fingerprint PROBE FILES are per-profile — a mod that ships its own
  `data\` files instead of the base `y` ones must declare them, or its lobby version gate
  is INERT.** `ModHashService.DefaultProbeFiles` is the three `y` files, but a mod like
  **Napoleonic Era** (`age3n.exe`) ships `data\proton.xml` + `data\techtreen.xml` (the `n`
  suffix) and does NOT overwrite the `y` files — those come from the AoE3 clone, so they're
  byte-identical for every NE player regardless of version. Fingerprinting the defaults there
  would make every NE install hash the same → two different versions could share a match and
  desync, undetected. `ModProfile.MultiplayerProbeFiles` (catalog `install.multiplayerProbeFiles`,
  a **tier-3 critical** field — it decides who can play with whom) declares the real files, and
  **the resolution lives INSIDE `ModHashService.ProbeFilesFor` / the 2-arg `FingerprintAsync`
  overload, NOT in the caller**, so every call site is correct at once (the one in `MainWindow`
  today and any added later). Empty list = the defaults, so WoL / IM / the stock game are
  byte-for-byte unchanged. Pinned by the per-profile cases in `ModHashServiceTests`. **Modelling
  gotcha for `IsolatedFolder` mods (learned the hard way with NE):** the AoE3 engine loads its
  `.bar` data from the **launch WORKING DIRECTORY**, not the registry `setuppath` — proven by
  WoL launching fine with `setuppath` pointing at an unrelated mod folder. So the launcher's
  existing "launch with `WorkingDirectory = mod folder`" is all these mods need; do NOT add a
  registry-`setuppath` redirect. NE's own probe file `age3n.exe` is exclusive to the mod (not in
  base AoE3), so its profile needs **no `marker`** (unlike WoL, whose probe `stringtabley.xml`
  ships in vanilla).

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
  **Don't remove these guards.** **Version display:** the stock game (Manual, detect-only)
  has NO launcher-tracked version by design, so `CurrentVersion` is always null — that is
  NOT a failure. Properties → GENERAL therefore shows `ModPropStockVersion`
  ("detected — ready to play") for a detected stock game, NOT the alarming
  `ModPropVersionUnknown` ("version not verified"), and **hides "Check for updates"**
  (`CheckUpdatesBtn`/`CheckUpdatesResult`) — the launcher doesn't manage the base game's
  updates. The dashboard already collapses the version chip when there's no version and
  shows `StatusStockReady`. Don't "fix" the null version by trying to detect a patch
  level (fragile — see the `AoE3Detector` no-SHA note). Detection: because the launcher never wrote a
  saved `InstallPath` for it, the stock game's install is resolved fresh each check
  by **`GameLauncher.FindAoe3InstallRoot(config)` — CONFIG-AWARE, not the bare
  `AoE3Detector.FindInstallRoot()`** — used by `UpdateService.ResolveInstallPath`'s
  stock branch (dashboard), `MainWindow.IsModInstalled`, and the multiplayer
  host/join + fingerprint callbacks. **Why config-aware is load-bearing (a real
  bug):** `AoE3Detector.FindInstallRoot()`/`FindAll()` only cover known folder-name
  variants (Steam/GOG/"Microsoft Games") and **never read `config.GameExecutable`**,
  so a non-standard install like
  `…\Microsoft Studios\Age of Empires III - Complete Collection` — even after the
  user pointed the "Change AoE3 folder" picker straight at its `age3y.exe` — read as
  "AoE3 not installed" for `aoe3-tad` (while the *general* "AoE3 found" badge, which
  DID go through `GameLauncher.FindAoe3Install`, worked — the two detections
  disagreed). `FindAoe3InstallRoot` derives the `data\`-containing root
  (`GameLauncher.DeriveAoe3RootFromExe`, pure + unit-tested `GameLauncherRootTests`:
  parent-of-`bin\` else the exe folder, whichever has `data\`) from
  `FindAoe3Install(config)`, which consults `config.GameExecutable` **and the durable
  `config.Aoe3ManualPath`** and every `FindAll` pass, falling back to the bare
  auto-scan. **`config.Aoe3ManualPath` is a SEPARATE durable field on purpose:**
  `config.GameExecutable` is a volatile launch cache **cleared on every mod switch**
  (`MainWindow` line ~1325, so the wrong game can't launch after a switch), so
  relying on it alone loses a manually-pointed non-standard AoE3 the moment you
  switch mods and back. The manual picker (`BrowseAoE3Button_Click`) writes BOTH:
  `GameExecutable` (volatile) and `Aoe3ManualPath` (the derived root, never cleared),
  and `GameLauncher.EnumerateCandidates` yields `Aoe3ManualPath\age3y.exe` +
  `\bin\age3y.exe` so the general finder survives switches too. **That candidate is
  GATED on `modInstallPath` being empty — it's a BASE-game resolver, NEVER for a mod
  launch.** `Aoe3ManualPath` is the *base* AoE3, which also ships `age3y.exe`; WoL is
  an isolated clone with its OWN `age3y.exe` and launches with an empty
  `GameExecutable` (relying on the walk-up from `modInstallPath`, step 2), so if the
  base path were preferred over the mod's own folder, PLAY would launch **vanilla AoE3
  instead of the mod** (the same filename-match hijack the `GameExecutable`
  clear-on-switch guards against). The stock game + the general `FindAoe3Install` badge
  both call with `modInstallPath: null`, so they still resolve the manual base; any mod
  launch passes its own folder and the base candidate is skipped. Pinned by
  `GameLauncherFindTests`. **The stock game still gets NO saved `ModState.InstallPath`**
  (resolved read-time only — preserving the uninstall-safety invariant above; don't
  "fix" detection by persisting a stock InstallPath). `ModHashService` then
  fingerprints the same TAD data files (`protoy/techtreey/stringtabley.xml`), so two
  stock players on the same game version match and can share a lobby. The host launch
  appends `OverrideAddress="<ip>"` exactly like the mods. Like WoL, the entry is
  mirrored in the catalog repo (`mods/aoe3-tad/mod.json`) for the public listing, but
  the built-in **shadows** it at runtime (built-in wins on id collision).

- **ANNOUNCEMENTS ride the notification feed into the bell, which inverts "join the Discord to
  see my announcements" into "the launcher tells you, and Discord has the detail".** A link the
  player has to remember to click only ever reaches the people who were already going to look.
  The pieces all existed: `NotificationFeedService`, the bell, and `SafeUrl`.
  **Publishing one is a COMMIT to `announcements.json` in this repo** — no deploy — which the
  notifier (`notifier-server`, a separate repo) republishes in the manifest every launcher
  already fetches. `NotificationKind.Announcement` is mod-less, like `LauncherUpdate` and
  `Connectivity`, so its branch in `NavigateToNotification` must sit ABOVE the
  `ModRegistry.Find(item.ModId)` guard. Its `TargetId` carries a URL rather than an id, because
  clicking it leaves the app; an empty one falls back to the Discord.
  **Three things are load-bearing, and each is a silent total failure if missed:**
  (1) **The server's `computeEtag` must hash the announcements.** Launchers read the feed with
  `If-None-Match`, so the ETag alone decides whether anybody ever sees a change — leave them out
  and every launcher gets a `304` forever and no announcement is ever delivered, with the service
  looking perfectly healthy. Pinned by the notifier's own `manifest.test.ts`.
  (2) **`SeedAnnouncementBaseline` on the first read**, or the day somebody installs the launcher
  they are handed the entire back catalogue at once — the same trap the catalog listing and the
  translation index each had to solve. **Seed it EVEN WHEN THE FEED CARRIES NONE**, and that half
  was a real bug: gating the seed on there being something to seed meant that, with
  `announcements.json` shipping empty, the marker was never written — so the FIRST announcement
  ever published ran the seed, was swallowed as "the backlog", and reached nobody. The unit tests
  pin the store's contract (an empty seed still sets the marker, and a later announcement bells)
  but they cannot catch this: the ordering lives in `MainWindow.MaybeNotifyAnnouncements`, which
  no test constructs. Read that method before changing it.
  (3) **A `DataTrigger` for the new kind in `MainWindow.xaml`'s badge style.** A kind with no
  trigger falls back to the default bell glyph in silence, so project news would read as an
  ordinary update.
  An `id` is permanent: changing one re-announces to everybody, reusing one silently suppresses
  the new item for everyone who saw the old. Pinned by the announcement cases in
  `NotificationCenterTests`.

- **The project's Discord has ONE owner on each side — `LauncherConfig.SupportDiscordUrl`
  (a `const`, like `PrivacyPolicyUrl`) and `Controls/SupportLink` — and WHERE it appears is
  the whole design.** Before this there was no route from the app to the project at all: no
  Discord, no repo link, no "report a bug". The word "Discord" appeared in the entire UI
  exactly once outside the sign-in flow, in a TOOLTIP telling the player to attach their
  diagnostics zip to a bug report there, with nothing to click.
  **A support link in an About box is the version that does not work** — it asks the player to
  remember it exists at the moment they least need it. So the pill lives where the launcher has
  just told somebody something went wrong and then stopped: `AntivirusExclusionDialog` (which
  hands them Windows Security's own menus and no way to ask), `CompatibilityLayerDialog`
  (whose no-remove branch answers "review it yourself in Properties"), `RadminAssistantWindow`
  (the "Help connecting" button, whose last step is "verify it yourself in Radmin"), and beside
  the diagnostics button that already promised Discord. Plus one always-reachable entry in the
  brand menu, which is the launcher's own menu.
  **`SupportLink.Build()` is a shared builder for a reason**: five copies of "glyph, label,
  tooltip, open" is five things that drift, and the one most likely to be lost is the tooltip —
  which carries the FULL url, the same anti-phishing measure the mod link pills make. It reuses
  `ModLinkPill` and `ModLink.GlyphFor(ModLinkType.Discord)` so it reads as the same kind of
  thing as a mod's own links, and it opens through `SafeUrl.TryOpen`.
  **Two moments are deliberately still missing, and both need a slot that does not exist**: an
  install failure (`ProgressPanel` collapses its action row, because install passes no `retry:`)
  and a multiplayer error (`MpAlertOverlay`'s card has no room for a second button). Half-fitting
  a link into either would be worse than the honest gap. Pinned by `SupportLinkTests` — a URL
  `SafeUrl` refuses would not break the build, would not throw, and would just silently do
  nothing in five places at once — and by `DialogXamlTests.SupportLink_Builds`, since a
  code-built control is checked by nothing at compile time.

- **EVERY url the launcher didn't author goes through `Services/SafeUrl.cs` — never
  `Process.Start` directly. That's the whole anti-abuse story for mod-supplied links.**
  Three sites used to shell out with a raw catalog/config string
  (`MainWindow.ModsBrowserView_OpenWebsiteRequested`, `MainWindow.OpenExternalUrl`,
  `ModPropertiesDialog.ValWebsite_Click`), and with `UseShellExecute = true` the shell
  runs whatever it is handed — a `file:///` URI, a UNC path, an `.exe`. The catalog
  schema's `^https?://` only guards the CI, and **CI is not the only door**: built-in
  profiles are hard-coded and never pass through it, and `launcher-config.json` is
  user-writable. So validation has to happen at OPEN time or it doesn't happen.
  `SafeUrl.IsAllowed` = absolute + scheme ∈ {http,https} + **empty `UserInfo`** (the
  `https://real-site.com@evil/` display trick) + non-empty host; `TryOpen` logs and
  returns false instead of throwing. The other ~25 `UseShellExecute = true` sites in
  the repo open local paths / `explorer.exe` / hardcoded URLs and are deliberately NOT
  routed. Pinned by `SafeUrlTests`.
  **A mod can declare community links (`links` in `mod.json`) — Discord / ModDB /
  forum / wiki / video — rendered as pills in the Workshop detail panel.** Model
  `Models/ModProfile.cs` (`ModLink`, `ModLinkType`, `ModLink.Sanitize`), DTO
  `ModLinkManifest` in `ModCatalogManifest.cs`, projection in
  `ModRegistry.ProjectToProfile`, UI `ModsBrowser.BuildDetailLinks`. Load-bearing:
  (1) **sanitised at PROJECTION, not at render** — HTTPS-only (stricter than
  `officialWebsite`, whose HTTP allowance is a legacy concession to `aoe3wol.com`;
  **don't fold the two fields together**), deduped, capped at `ModLink.MaxLinks` (4),
  label control-char-stripped then length-capped (stripping first, so control-char
  padding can't smuggle visible text past the cap), unknown `type` → `Other` rather
  than dropping the link. It repeats the CI's rules ON PURPOSE — same "CI isn't the
  only door" reasoning as above. (2) **The pill's tooltip is the full URL**
  (`TooltipHelper.Wrap`) — a label can claim anything, so showing the destination is
  the real anti-phishing measure; a Steam-style "you are leaving" interstitial was
  deliberately NOT built. (3) **No emoji and no brand logos** — the pills use GENERIC
  Segoe MDL2 system icons chosen per `type` by `ModLink.GlyphFor` (globe / speech bubble /
  camera …), plus a trailing `\uE8A7` marking that the link leaves the launcher. The rule
  is about never reproducing someone's LOGO, not about every link looking alike; it used to
  be one `\uE71B` for every type, which made the row read as static badges. `GlyphFor`'s
  `_ =>` returns the generic link glyph, so a type added later still renders an icon —
  pinned by `ModLinkTests`, which enumerates the enum rather than listing cases and asserts
  the glyphs sit in the Private Use Area (outside it they would render as literal
  characters). **The pill's colours come from the keyed `ModLinkPill` style in
  `Buttons.xaml`, NEVER from the instance:** `BuildLinkPill` used to set
  `Background`/`BorderBrush` locally, and a local value (precedence 3) beats
  `ControlTemplate.Triggers` (4-6), so every hover trigger was dead and the pills had no
  click affordance at all — the same precedence trap documented for `TitleBarBrandButton`.
  For the same reason the caption `TextBlock` must NOT set `Foreground` (the
  `ContentPresenter` propagates the button's, which is the only route hover has to reach
  it); the leading glyph DOES set one, so it stays gold on hover. (4) The section is
  **collapsed when `Links` is empty**, so every built-in and every pre-`links`
  manifest renders byte-for-byte as before. **(5) `OfficialWebsite` is FOLDED INTO the row
  as the first pill** (`ModLink.BuildDisplayList`, pure + tested), unless a `links` entry
  already carries that url. This inverted an earlier rule that SKIPPED such a link because
  the action bar had a "view mod page" button; that button was removed as redundant, and
  since the metadata "Website" line is plain text, the row became the only clickable route
  to the site — dropping the button without folding it in would have left every mod's
  website unreachable, and a mod that declares no `links` with nothing clickable at all.
  **The website must NOT go through `ModLink.Sanitize`:** that is HTTPS-only, while
  `OfficialWebsite` keeps a deliberate legacy HTTP allowance (WoL's is
  `http://aoe3wol.com/`), so sanitising it would silently delete the pill. The correct gate
  is `SafeUrl.IsAllowed`, which takes http and https — the same check that runs on open.
  The "Website" metadata row was also dropped: the pill's tooltip already shows the full
  url. **(6) `links` is
  the ONE field a catalog entry may contribute to a BUILT-IN — the single documented
  exception to the shadow rule, via `ModRegistry.ApplyBuiltInCosmeticOverlay`.** `wol` /
  `aoe3-tad` are constructed directly in `ModRegistry._builtIn` and never go through
  `ProjectToProfile`, and the built-in wins on id collision, so everything else in
  `mods/wol/mod.json` stays inert. Without an exception WoL could only get links by
  hardcoding them here — meaning a **new release for every Discord-invite change**. So
  `ApplyMerged`'s shadow branch calls the overlay before its `continue`: the entry is
  still never projected (it can't touch install paths, payload urls or the update
  mechanism), it just supplies `Links`. Safe because the field is cosmetic and defended
  twice — the catalog CI's per-mod ownership gate and `ModLink.Sanitize` on this side,
  which runs regardless of what CI did. **The assignment is UNCONDITIONAL, including for
  a manifest with no links** — `_builtIn` is a `static readonly` list and `ApplyMerged`
  copies the LIST, not the profiles, so the overlay mutates the singleton and survives
  every later merge; always assigning is what makes it idempotent, so dropping a link
  from the manifest drops it from the UI on the next refresh. An
  `if (manifest.Links != null)` guard would strand phantom links until restart (pinned by
  the `Overlay_*` cases in `ModLinkTests`). Keep the whitelist at exactly one field —
  widening it is what would put the shadow rule's security property back in play. Note
  the sibling asset rule is unchanged: `IconUrl` is still hardcoded on the built-in and
  points at the catalog's raw `icon.png`, which is the same "editable without a release"
  idea applied to assets rather than manifest data. Catalog side:
  `links` is in `classify_pr.py`'s `TIER_1_FIELDS`, so it inherits the per-mod
  **ownership gate** (only the mod's `maintainers` auto-merge it) — that gate plus the
  visible URL IS the abuse control; a per-type host allowlist was considered and
  rejected as maintenance the ownership gate already covers. The wizard's
  `PublishModDialog.ParseLinkLines` (`type|url` per line, pipe optional) drops what
  the schema would reject so a modder's first PR isn't red. Pinned by `ModLinkTests`
  + the `Links_*` cases in `BuildModJsonTests`.

- **The Workshop NEVER installs — it hands off to the Library, and that handoff has to be
  a real, enabled button. Adding a mod used to be a DEAD END, and it cost a real user the
  install.** Two orthogonal axes sit side by side in every Workshop row: the status badge
  is DISK state (`ModRowStatus.Installed`/`NotInstalled`) while the button is COLLECTION
  membership (`ModRowState.IsInUserCollection`). Nothing distinguished them, so "Add to my
  mods" on a not-installed mod read as the install button — and after pressing it the
  primary became a **disabled** "In my mods" pill while `BuildDetailMoreMenu` is empty by
  design, leaving Remove as the only clickable thing: the one action that undoes what the
  user just did. A user reported Improvement Mod as impossible to install from exactly
  this state. The fix, in `ModsBrowser`: a mod already in the collection (**including
  built-ins** — WoL and stock AoE3 *are* in the Library) shows an enabled
  **`OpenInLibraryRequested`** button in BOTH the row and the detail panel, wired in
  `MainWindow` to the same shape `NavigateToNotification` uses — `LoadModProfile(profile)`
  only when it isn't already active (it rebuilds `_updateService`), then
  `SwitchTopTab(TopTab.Play)`. **Load-bearing rules:** (a) the label is the SAME whether or
  not the mod is installed ("See in Library") — it names a destination, never an outcome,
  so it can't repeat the original confusion; (b) it must stay **enabled with a full-strength
  foreground** — `TextSecondary` is what made the old pill read as dead; (c) it does NOT
  install: the dashboard CTA already reads INSTALL for a mod that isn't on disk, so adding
  a second install trigger here would duplicate the state machine. The status badge carries
  a tooltip naming its axis (`ModsBrowserBadgeTooltip`) because that ambiguity is the root
  cause, not the button alone. Don't reintroduce a disabled pill in either slot.

- **"Remove from my mods" is a VISIBILITY toggle — it never deletes a file — and it is
  confirmed WHEN INSTALLED, from a secondary button, precisely because none of that is
  visible.**
  `LauncherConfig.RemoveUserMod` only drops the id from `UserModIds`; `_config.Mods[id]`
  (install path, `LastKnownVersion`, active translation, registered copies) is untouched,
  so re-adding from the Workshop restores the install with nothing re-downloaded. The
  problem it caused is perceptual, not data loss: an INSTALLED mod vanishes from the
  dashboard MODS popup while its multi-GB folder sits on disk, which reads as an
  uninstall — and nobody guesses re-adding brings it back. Three parts: (1) **one gate,
  ONE entry point** — only the detail panel's secondary `DetailRemoveButton` raises
  `RemoveFromCollectionRequested`, and confirming inside
  `MainWindow.ModsBrowserView_RemoveFromCollectionRequested` covers it; don't add a
  second check at the call site, and don't put Remove back on the row (it used to be the
  row button for every added mod, which made the destructive verb the most visible thing
  in the catalogue — see the Workshop→Library bullet). (2) **The prompt is gated on
  `IsProfileInstalledLocally` — a mod that isn't installed is removed outright.** That
  asymmetry is deliberate, not an oversight: nothing is at stake there, and confirming
  harmless actions is exactly what trains users to click through the prompt that matters.
  So `RemoveFromCollectionDialog` (themed, modeled on `SelfInstallPromptDialog`) has ONE
  unconditional body — it promises the files stay and shows the folder
  (`ResolveDisplayInstallPath`, which mirrors `IsProfileInstalledLocally`'s resolution
  order so the path can't contradict the badge that produced it). If you ever add a
  not-installed variant, move that gate too or it'll be dead code. **Cancel is
  `IsDefault`** — on a destructive confirm, Enter must not act. It shows the PATH, not a
  folder size: sizing ~40k files takes seconds and would need to leave the UI thread,
  while the path is free and is where you'd go look.
  (3) **The detail panel's PRIMARY button is a disabled "In my mods" status pill** (the
  same treatment as the built-in pill), with removal moved to the secondary
  `DetailRemoveButton`; the destructive action must not sit in the biggest slot. The
  dialog's copy is only honest as long as `RemoveUserMod` keeps its hands off
  `config.Mods` — that invariant is pinned by `UserModCollectionTests`, so if someone
  makes removal clear the per-mod state, those fail rather than the launcher silently
  lying to the user. Unrelated-but-adjacent: removing the ACTIVE mod doesn't make it
  disappear mid-session — the popup keeps it via the `|| p.Id == activeId` fallback in
  `BuildModSwitchRow`'s source list.

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

- **Sharing graphics/sound/hotkeys between mods GRAFTS XML SECTIONS — and the profile's real
  shape is NOT what an inspection suggests.** `Services/GameSettingsSync.cs` (pure, tested) +
  `GameSettingsStore.cs` (the disk side) carry a player's settings between mods. Two surfaces,
  both in **ModProperties → USER DATA**, because these settings live in the mod's own My Games
  folder and putting the decision on the mod's page is what makes the blast radius visible:
  an **"import from…"** picker (one-shot, explicit, `ImportFrom` goes straight between two
  profiles and never touches the shared copy) and a per-mod **`ModState.SyncGameSettings`**
  checkbox (**default false**) putting the mod in a shared GROUP — any member played updates the
  copy, any member launched applies it; a non-member is never read or written by the sync.
  **This replaced a launcher-wide `ShareGameSettingsAcrossMods` flag** — wrong on two counts:
  nobody finds a global checkbox (the very problem the request described), and it rewrote any
  mod's profile at launch with no clue on screen where that came from. The candidate rules live
  in the pure `GameSettingsStore.CanImportFrom` (installed, not stock, has a user-data folder,
  never the mod itself) so they are tested rather than buried in the dialog; `LoadGameSettings`
  is called ONCE from the ctor and deliberately NOT from `LoadUserData`, since `RefreshData()`
  re-runs that and would reset a half-made combo selection — the same reason `LoadVersions` sits
  outside it. Everything lives in ONE file per mod,
  `My Games\<mod>\Users3\<profile>.xml` (UTF-16; the active profile is named inside
  `Users3\LastProfile3.dat`). **Only `GameOptions` and `KeyMapGroups` travel** — the six
  `*GameSettings` blocks are last-used lobby setups and a civ from one mod means nothing in
  another, and the whole file would drag `UserInformation` too.
  **The trap, which cost a full wrong implementation: only `KeyMapGroups` is an element of its
  own name.** The rest are identical `<GameSettings Name="…">` siblings keyed by an ATTRIBUTE, so
  `FindSection` matches the `Name` attribute FIRST and only falls back to the element name.
  Matching by element name alone finds `KeyMapGroups` and nothing else — it shares nothing and
  reports success. It was written that way because **PowerShell's XML adapter returns the `Name`
  ATTRIBUTE from `$node.Name`**, so inspecting a real profile there prints a tree of neatly named
  sections that does not exist; the unit tests then encoded the same misreading and passed.
  `GameSettingsSyncTests`' fixture is copied from a real profile for exactly this reason — if you
  change it, diff it against one on disk first. Push happens LAST inside
  `GameLauncher.ApplyLaunchRedirects`, after the `My Games` junction, or a `userDataRedirect`
  mod's settings land in the wrong physical folder; capture happens in `MainWindow.OnGameExited`
  (using the profile captured AT LAUNCH — the user may switch mods mid-game) and
  `MultiplayerTab.OnGameExitedAsync`. Every path is best-effort, and `Graft` returns **null**
  rather than a partial document: this runs moments before the game starts, and the worst outcome
  would be a profile the game can no longer read.
  **The same file now also does SURGICAL single-setting writes — `ReadSetting` / `EnsureSetting`
  — and one setting is deliberately excluded from the sharing.** `EnsureSetting` keeps `Graft`'s
  contract exactly (**null when there is nothing to change**, so an already-correct profile is
  never rewritten on the launch path) and **never fabricates a `GameSettings` or `Settings`
  element**: a profile without that shape is not one we understand, and inventing structure risks
  a file the game refuses to load. A missing `Setting` IS created, since that is an absent value
  inside a shape we do recognise. `ReadSetting` exists because `EnsureSetting`'s null conflates
  "already correct" with "unreadable", and a caller that records having applied something must not
  confuse the two. Both look at **direct children only** — the same rule as `FindSection`, because
  `optionsoundlevel` in `GameOptions` sits beside a `civ` in `RandomMapGameSettings`.
  **The exclusion: `optionrecordgame` never travels between mods.** It lives inside `GameOptions`,
  which the sync grafts wholesale, so without this a capture would carry it into the shared copy
  and re-enable game recording over a player who had just turned it off — with the code that owns
  that preference correctly concluding it had already applied what was asked, so nothing could
  notice. `ExtractSections` strips it and **`Graft` carries the target's own value across**:
  omitting it alone would DELETE the setting, since a graft replaces the entire section. It cuts
  both ways on purpose — a mod where recording was switched off in-game must not export that over
  one where it is wanted. Recording is a launcher preference, not a setting the player asked to
  share; the rest of its story lives in `.claude/rules/multiplayer.md`.

- **WHICH `My Games` folder belongs to a mod is resolved by
  `UserDataService.ResolveFolderName(profile, config)` — the single source of truth.
  Never read `ModProfile.UserDataFolder` raw; that is what made three features
  invisible for most mods.** `userDataFolder` is a catalog field the mod's AUTHOR has
  to declare, and most don't: of the five shipped mods only WoL, Napoleonic Era and
  Struggle of Indonesia do. Improvement Mod never did, yet
  `Documents\My Games\AoE3 Improvement Mod` exists on every player's disk — so
  `ModPropertiesDialog.LoadGameSettings` collapsed its whole "Game settings" block,
  it could never appear as an import SOURCE for another mod
  (`GameSettingsStore.CanImportFrom` requires a folder), user-data backup/restore was
  inert, and `DiagnosticLog`'s bundle collected none of the game artifacts that make
  an OOS report diagnosable. The bug reads as "the feature was lost".
  **Order: declared → remembered (`ModState.UserDataFolder`) → discovered by name →
  learned from a launch.** Discovery (`MatchUserDataFolder`, pure + pinned by
  `UserDataDiscoveryTests`) normalizes both names (lowercase, alphanumerics only, a
  leading `aoe3`/`ageofempires3` dropped) and accepts an exact match or a folder that
  STARTS WITH the mod's name — which is what resolves `AoE3 Improvement Mod` →
  *Improvement Mod* and `Napoleonic Era Beta 2` → *Napoleonic Era*.
  **Three guards, and they are the point.** Choosing wrong writes one mod's
  graphics/hotkeys into ANOTHER mod's profile — silent, and not something a player
  could diagnose: (1) never the vanilla `Age of Empires 3` folder; (2) never a folder
  another profile already claims (`ClaimedFolderNames`) — one folder, one mod;
  (3) it must actually look like AoE3 data (`Users3\`, which is also the file the
  settings feature reads). **Ambiguity returns null on purpose** — no answer leaves
  the UI offering the section with nothing selected, which the user can fix, while a
  wrong answer corrupts another mod's settings. The rejection tests are the ones that
  matter, same philosophy as `SafeUrlTests`.
  **`MainWindow.LearnUserDataFolderFromLaunch` is what covers mods that don't exist
  yet**: on game exit, a mod still without a folder adopts the one written during the
  session — the game just told us, so no name heuristic is needed. It takes
  `launchedAtUtc` as a PARAMETER because `OnGameExited` clears `_gameStartedUtc` a few
  lines above the call (the same "decide before clearing the launch bookkeeping" rule
  the instant-exit detection follows); reading the field would make it always no-op.
  It skips `UserDataRedirect` mods, where the standard folder is junctioned at the
  mod's and the answer would be the junction rather than the destination.
  **Declaring `userDataFolder` in the catalog is still the exact, guess-free path and
  stays authoritative — the launcher just no longer DEPENDS on it.**

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
  (gear menu, Properties tab, MP replay finder) goes
  through `UserDataService` — don't rebuild the path by hand. **Backups are ON DEMAND
  only.** A `UserDataAlertDialog` used to open before every fresh install that found
  pre-existing data, offering to back it up; it was removed because it interrupted the
  user right after they had already committed to installing. `MainWindow.InstallAsync`
  still calls `LogPreExistingUserData()` at that point — detection and logging with no
  UI, kept because the log line is the only record that prior data existed at install
  time, which is what a diagnostic bundle needs when someone reports losing saves.
  Don't re-add an unprompted backup dialog to the install flow. The Properties
  USER DATA tab makes this visible on purpose: it shows the resolved path
  (Consolas), an amber warning when the OTHER root also holds files, the
  backup count + latest date on the Restore row (button disabled with a
  "no backups yet" hint instead of a surprise message box), and an inline
  result line after Backup/Restore — the `createBackup`/`restoreBackup`
  callbacks are `Func<string?>` (result text or null) because the main-window
  status bar sits BEHIND the non-modal dialog; the shared cores are
  `MainWindow.CreateUserDataBackupCore`/`RestoreUserDataCore` (the gear menu
  items call the same cores).

- **A mod can get an EXCLUSIVE My Games save folder via a launch-time JUNCTION
  redirect — `Services/AoE3UserDataRedirect.cs`, gated by
  `ModProfile.UserDataRedirect` (`install.userDataRedirect` in `mod.json`).** The
  AoE3 engine writes to the fixed `My Games\Age of Empires 3\`. WoL / Improvement
  Mod ship builds that already write to their OWN `My Games\<name>` folder (so they
  need nothing — verified: those folders exist on disk next to `Age of Empires 3`).
  But some mods (**King's Return**, `age3k.exe`) write to the SHARED
  `Age of Empires 3` folder, mixing saves with vanilla. For those, `GameLauncher`
  (both `Launch` and `LaunchAndWatch`, via `ApplyUserDataRedirect`) calls
  `EnsureRedirected(profile.UserDataFolder)` right before launch — it moves the real
  `Age of Empires 3` aside (once, to `Age of Empires 3 (AoE3 vanilla)`) and makes
  `Age of Empires 3` a directory JUNCTION to the mod's exclusive folder — and calls
  `EnsureDefault()` for every NON-redirect launch (vanilla / WoL / IM) to restore the
  real folder. `App.OnStartup` also calls `EnsureDefault()` (self-heal if a prior
  session was killed mid-play). **`MainWindow.OnGameExited` calls BOTH `EnsureDefault()`s
  too, and that hook is load-bearing — this bullet used to claim "no game-exit hook
  needed" and that claim was the bug.** Restoring only on the next launcher start / next
  other launch is not a bound at all: the launcher auto-starts minimised and lives in the
  tray, so it can stay open for days. Observed on a real machine — `My Games\Age of Empires 3`
  sat junctioned at Struggle of Indonesia for four hours with the launcher running, and any
  AoE3 opened outside the launcher in that window would have written its saves into the mod's
  folder. The dashboard `Launch` being fire-and-forget is irrelevant: the 2 s
  `StartGameMonitor` already detects the exit. **Safety is load-bearing:** it NEVER deletes a real folder —
  only moves it aside + creates/removes a junction, and the junction is removed with
  `Directory.Delete(std, recursive:false)` (drops ONLY the link, never the target);
  if an aside already exists it bails rather than clobber; every op is best-effort
  try/caught so it can never block a launch. Junctions use `mklink /J` (no elevation).
  **`EnsureDefault` restores ONLY a junction it owns, and the aside folder is the proof
  — this fixed a real latent bug in BOTH redirect services.** It used to delete ANY
  reparse point sitting at the target path and only afterwards restore, if an aside
  happened to exist. Two failures in one: redirecting saves (or a game folder) to
  another drive with a junction is something users do on their own, and such a link was
  deleted out from under them; and with no aside to put back, the path was left simply
  GONE. Only these services ever create `"<name> (AoE3 vanilla)"`, so requiring it both
  proves the link is ours and guarantees there is something to restore — one condition
  closing both holes. The link target is captured before the delete so a failed
  `Directory.Move` rolls the junction back rather than leaving the path missing. Pinned
  by the `EnsureDefault_LeavesAForeignJunctionAlone` cases in both test suites — those
  are the ones that matter.
  **CAVEAT:** this touches the user's My Games — it's clean on a NON-OneDrive Documents
  (junctions inside OneDrive-synced Documents are risky); the maintainer's machine is
  physical `C:\Users\…\Documents`. Pinned by `AoE3UserDataRedirectTests` (real→junction
  →restore preserves vanilla data + isolates the mod's; idempotent; no-vanilla case).
  **King's Return catalog note:** KR ships as an Inno `setup.exe` (the launcher CAN'T run
  installers — that flow was removed), so the catalog hosts the mod's overlay files
  (the installed `mod\` folder, flat, minus `_backupfiles\`/uninstaller — ~375 MB) as a
  **GitHubReleases** asset (`papillo12/Age-of-Empires-3-The-King-s-Return`, tag `1.0.0`).
  It installs as `IsolatedFolder`: the launcher CLONES AoE3 (for the engine — KR ships
  NO engine, only `age3k.exe`+`UHC.DLL`+`data\`(k-suffixed)+`art\`) and overlays KR on
  top, detected/launched by its EXCLUSIVE probe/exe `age3k.exe` (like IM's `age3m.exe`,
  no marker). **UNVERIFIED risk:** KR normally runs from `…\Age of Empires III\mod\` with
  the UHC loader pointing at a separate Steam AoE3; whether `age3k.exe`/UHC works from a
  launcher CLONE (engine in the same folder) needs a Windows smoke test — if it doesn't,
  fall back to `DelegatedExternal` (detect+launch a user-installed copy; `launcherCanInstall`
  = WolPatcher/GitHubReleases only, so that keeps Install/Repair off). Either way it keeps
  `userDataRedirect:true` (KR writes to the shared `Age of Empires 3` My Games regardless
  of install location — the folder name is engine-product-derived, not path-derived).

- **UHC decides the install model: a mod's `.exe` locates its `.bar`/data by the
  registry `setuppath` UNLESS it's UHC-patched (then it uses the working directory).
  This is the root rule behind why `IsolatedFolder` works for some mods and not
  others — get it wrong and the game either fails to load or launches VANILLA.** UHC
  ("Unofficial Hotfix / extension hack" applied onto the game exes — confirmed by
  modder Mandos/Improvement Mod) is what lets a mod run from ANY folder. WoL,
  Improvement Mod and ESOC ship UHC-patched exes (`age3y.exe`/`age3m.exe` read the
  launch **working directory**) → the launcher's `IsolatedFolder` clone-and-launch
  model works. A mod that ships the **stock** exe (no UHC) reads the registry
  `setuppath` (the TAD key `HKLM\…\Age of Empires 3 Expansion Pack 2\1.0\setuppath`,
  = the real `…\bin`), NOT the working dir → an isolated clone loads the base game's
  content, not the mod. **Empirically confirmed** (Napoleonic Era's `age3n.exe`: the
  isolated install said "Could not load DATAPN.BAR" until `setuppath` was pointed at
  its folder; Struggle of Indonesia's `age3y.exe` is byte-identical to the stock exe →
  no UHC). We CAN'T add UHC ourselves (needs a custom exe → forks multiplayer
  compatibility with the original mod's players). So NON-UHC mods use one of two models
  by whether they're ADDITIVE or REPLACEMENT:
  **(1) Additive non-UHC mods CAN go `InPlaceOverlay` into the real AoE3 — but this is
  NOT what Napoleonic Era ships, and the distinction is worth reading before you trust
  the model.** An additive mod brings its own exe + suffixed files that don't collide
  with the base `y` ones (NE: `age3n.exe`, `DataPN.bar`, `data\proton.xml`, `RMN\`,
  `AIN\`, `triggerN\`, `loading*n*.ddt`), so overlaying them where `setuppath` points
  makes the mod load with the registry **UNCHANGED** → TAD and the mod coexist, nothing
  global touched. That is the layout NE's official Inno installer produces, and it is why
  this was the original plan for NE. (In the Steam layout `…\bin` holds EVERYTHING —
  `data\`/`RM\`/`AI\`/`Sound\`/`loading*.ddt` are all inside `bin\`, so all of NE's files
  map into `bin\`.) Uninstall stays safe here: an `InPlaceOverlay` manifest is
  `ClonedAoe3=false`, so `UninstallService` removes only the mod's net-new files.
  **NE was nonetheless moved to `IsolatedFolder` + `privateSetupPath` (2) once the
  patcher existed**, and the catalog is the authority (`mods/napoleonic-era/mod.json`:
  `type: IsolatedFolder`, `privateSetupPath: true`). The reason is that additive-vs-
  replacement is the WRONG axis for this decision: what actually decides it is whether
  the exe resolves content through the **registry key** or through its **working
  directory**. A non-UHC exe reads the key, so the moment the mod lives in its own
  folder — for ANY reason, collisions or not — it needs a key of its own. Writing into
  the player's real AoE3 was the thing worth avoiding. So: **`InPlaceOverlay` is a valid
  model, but no shipped mod uses it today** — don't "fix" NE back to it on the strength
  of the paragraph above.
  **(2) Replacement non-UHC mods → `PrivateSetupPath` (patch the key name inside the mod's
  own exe). This SUPERSEDES the `SetupPathRedirect` junction described below, which is kept
  only so an install made by an older build still gets its `bin\` restored.**
  `Services/SetupPathPatcher.cs` (gated by `ModProfile.PrivateSetupPath` /
  `install.privateSetupPath`) rewrites the registry-key string inside the install's OWN copy
  of the executable to name a key belonging to the mod, and creates that key with
  `setuppath` = the install folder. Nothing global moves: vanilla keeps its key and its
  folder, both stay playable at once, and an abrupt exit leaves nothing to restore — which is
  the whole reason it replaced the junction (the maintainer rejected having `bin\` renamed
  while playing).
  **The flag is REFUSED outside `IsolatedFolder`, in BOTH `ModRegistry.ProjectToProfile`
  (dropped at projection, logged) and `NativeInstallService.ApplyPrivateSetupPath` (refused at
  use).** For an `InPlaceOverlay` the install folder IS the player's own AoE3, so the patcher
  would rewrite their BASE-GAME executable, break its signature and repoint vanilla at the mod's
  key — reachable from nothing worse than a catalogue typo, and not undoable by the player. The
  docs asserting the two are exclusive is not a guard; these two are.
  **The `displayName` is validated BEFORE the download** (`MainWindow.InstallAsync`, beside the
  elevation gate; `SetupPathPatcher.MaxModNameLength` = 33 → `StatusInstallSetupPathBadName`):
  the patch runs at the END of the install, so an unusable name would otherwise clone AoE3,
  download several GB and only then abort — reporting "the mod was NOT installed" with ten
  gigabytes sitting on disk. And `PatchExecutable` writes `<exe>.patching` then
  `File.Move(overwrite:true)` rather than rewriting in place: for a mod whose exe comes from the
  clone (SoI) a torn write is unrecoverable, because Repair only re-lays the overlay.
  **Measured on the real binaries, don't re-derive:** the key
  `Software\Microsoft\Microsoft Games\Age of Empires 3 Expansion Pack 2\1.0` occurs at
  **exactly 3 sites** — one ANSI (`8272016`) and two UTF-16 (`8009984`, `8626656`) — and is
  **72 chars**, the hard ceiling on the replacement (so `displayName` is capped at 33). Three
  OTHER keys (vanilla, WarChiefs) are deliberately left alone; the vanilla one is only 54
  chars, so patching those too would tighten the ceiling. **Four invariants:** (a) the private
  key must clone **every value** of the base key, not just `setuppath` — writing only the path
  makes the game demand the 25-character product key, because the licence check reads `pid` /
  `digitalproductid` / `doublehash` from the SAME key, and two of those are `REG_BINARY` so
  each value keeps its original kind; (b) the engine reads **HKLM only** — HKCU was tried and
  ignored, so writing the key needs elevation. **The key is written ONCE**: `EnsurePrivateKey`
  early-returns on `IsPrivateKeyCurrent` (read-only: key exists, `setuppath` matches, carries
  every value name the base key has), so the player is asked for admin at install and never
  again on a Repair/Update. That predicate is also what the elevation guard keys off —
  `MainWindow.NeedsAdminForPrivateKey`, consumed by `EnsureInstallWritableOrElevate(installPath,
  profile)`, which BOTH `InstallAsync` and both `RepairInstallAsync` sites call. **Keep that
  rule in the one helper**: it lived only in `InstallAsync` at first, and because a Steam
  library folder is writable the repair path sailed past the folder check and threw at the
  HKLM write — *after* the multi-GB download, as a raw non-localized .NET message. A permission
  failure now raises `SetupPathKeyAccessException` → `StatusPrivateKeyNeedsAdmin`, kept separate
  from `SetupPathPatchException` because the two ask the user for different things (run as
  admin vs. this exe is unsupported); (c) the 32-bit registry view is mandatory (the game is
  32-bit → its `HKLM\Software` is `WOW6432Node`, this launcher is x64); (d) `PatchExecutable`
  is **idempotent** — a Repair
  re-lays only the overlay, and for a mod whose exe comes from the AoE3 clone that file is
  never rewritten, so the step re-runs over an already-patched exe and treating that as a
  failure would abort every repair of exactly the mods this exists for. Applied in **all THREE**
  paths that write a manifest — `InstallAsync`, `InstallModOnlyAsync` and
  `ApplyGitHubDeltaAsync` — before `WriteManifest`, which records the key in
  `InstallManifest.PrivateSetupPathKey` so `UninstallService.RemoveRegistryEntries` takes it
  away; `RemovePrivateKey` refuses anything outside the AoE3 product root or naming a base
  game. It also **carries over the base game's EULA marker** (`FIRSTRUN` /
  `SystemInitialization` in HKCU) — a mod under its own product key otherwise re-prompts for
  a licence the player already accepted for this same game; it only ever propagates an
  acceptance that ALREADY exists, never fabricates one. Patching the exe **invalidates its
  Authenticode signature** and is an AV-sensitive edit, so it is done to the player's local
  copy and no modified binary is ever redistributed. **Don't "simplify" this by shipping a
  pre-patched exe in the mod's payload** — it was asked and it does not work: the patched exe
  is useless without its key, and the key CANNOT be packaged, because `setuppath` is that
  player's install folder and `pid`/`digitalproductid`/`doublehash` are that player's licence.
  A shipped exe with no key reproduces exactly the product-key dialog this feature exists to
  avoid, and would additionally mean redistributing Microsoft's executable (SoI's payload does
  not contain `age3y.exe` today — it comes from the player's own AoE3 clone). The flip side is
  the property that makes this correct: the mod's key is cloned from that machine's base-game
  key, so **the mod's licence state always equals vanilla's**. If the base key isn't found (unknown
  build), the install **aborts** (`SetupPathPatchException` → `StatusInstallSetupPathFailed`)
  rather than shipping a mod that silently loads vanilla. Pinned by `SetupPathPatcherTests`
  — the ABSENT case is the one that matters, since it is what makes a clean abort possible.
  **(2b, legacy) `SetupPathRedirect` (junction the setuppath folder).**
  Struggle of Indonesia ships the STOCK `age3y.exe` and REPLACES base TAD files
  (`data\civs.xml`, `homecity*.xml`, `protoy/techtreey/stringtabley.xml` with the
  Indonesian civs), so it can't be overlaid (would destroy vanilla) and it shares the
  exe+registry-key with vanilla TAD. It stays an `IsolatedFolder` clone; the new
  `Services/AoE3SetupPathRedirect.cs` (gated by `ModProfile.SetupPathRedirect` /
  `install.setupPathRedirect`) — a near-copy of `AoE3UserDataRedirect` — makes the
  folder `setuppath` points at (the real `bin\`) resolve to the mod's clone folder via
  a **directory junction** around launch, WITHOUT touching the registry (no admin): it
  reads `setuppath` from the registry, moves the real `bin\` aside once (to
  `bin\ (AoE3 vanilla)`), `mklink /J` `bin\` → the mod folder, launches (stock
  `age3y.exe` reads `setuppath=bin` = the junction = SoI content), and restores on the
  next non-redirect launch / `App.OnStartup` self-heal. `GameLauncher.ApplyLaunchRedirects`
  (renamed from `ApplyUserDataRedirect`, now takes `modInstallPath`) applies BOTH
  redirects on every launch. **Load-bearing:** NEVER deletes a real folder (junction
  removed with `recursive:false` = link only); bails if the aside already exists;
  best-effort try/caught; **stricter than the My Games variant — it refuses to junction
  when the setup folder doesn't already exist** (won't create a link where the base game
  should be); and `EnsureDefault` only removes a junction that has its `(AoE3 vanilla)`
  aside beside it — see the ownership-proof note in the My Games bullet above, which
  applies identically here (a user's own junction on `bin\` must survive untouched). While a replacement mod plays, the real `bin\` (base + NE) is aside, so a
  crash leaves it junctioned until the next launcher start heals it — accepted trade-off
  (the user chose this over per-launch-admin `setuppath` writes, which broke TAD when a
  manual restore failed). Pinned by `AoE3SetupPathRedirectTests`. **`ModInstallProbe`'s
  engine check still applies** (SoI is `IsolatedFolder` with the base engine DLLs); its
  probe (`data\stringtabley.xml`, shared with vanilla) needs a marker
  (`art\buildings\soi`, verified present). **Don't confuse the models:** additive →
  InPlaceOverlay (no registry, coexists), replacement → PrivateSetupPath (own registry key,
  coexists, one admin prompt at install) — with SetupPathRedirect as the legacy junction
  variant of the latter, mutually exclusive with vanilla while playing. **SoI's `mod.json` now
  declares `privateSetupPath`**, so `ApplyLaunchRedirects` sees the old flag off and calls
  `EnsureDefault()`, which is exactly what restores `bin\` for anyone coming from the old
  model. Note `AoE3SetupPathRedirect` therefore has no live consumer in the catalog and is
  kept solely for that migration — don't delete it while installs made by older builds exist.
  **Modder-facing surfaces (keep in sync with this gotcha):** `docs/MODDING.md §4` is the
  authoritative guide — §4.0 the UHC decision rule + the "copy the folder and run the exe"
  UHC test, §4.1 IsolatedFolder (UHC only), §4.2 InPlaceOverlay (additive non-UHC, NE),
  §4.3 `privateSetupPath` (stock-exe replacement, SoI — with `setupPathRedirect` noted as the
  legacy variant); §3.4 documents the
  `privateSetupPath`/`setupPathRedirect`/`multiplayerProbeFiles`/`userDataRedirect` fields;
  §9 has the "my mod won't open" runtime entry. (Known drift, pre-existing: the template
  schema at `aoe3-mods-catalog-template/schema/mod.schema.json` is missing
  `userDataRedirect`, `multiplayerProbeFiles` and both setup-path fields — the live catalog's
  schema is the current one.) The **"Publish my mod" wizard** (`PublishModDialog`, Step 3) no
  longer shows the raw `IsolatedFolder`/`InPlaceOverlay` combo — it asks ONE plain-language
  question ("how your mod installs and runs") with 3 options whose `Tag` encodes the type +
  a `+setupPathRedirect` suffix; `InstallTypeFromTag`/`SetupRedirectFromTag` split it in
  `ReadFormInput`, and `BuildModJson` emits `install.setupPathRedirect` only when true
  (pinned by `BuildModJsonTests`).

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
  shake (`PulseNotificationBell`) fires only on `ItemAdded`, never looping.**
  **Each row shows the ICON of the mod it is about, with the per-kind glyph demoted to
  a small badge on its corner** — the icon says which mod, the badge says what happened.
  It resolves from the `ModId` the item already carries, via `Controls/ModIconConverter`
  → `ModRegistry.Find` → `ModProfile.ResolveIconSource()` →
  `MainWindow.TryLoadTileImage` (`internal` for exactly this reason — a second icon
  cache would be a second thing `InvalidateTileImageCache` can miss). **The glyph
  fallback is load-bearing, not a leftover:** kinds with no mod (`LauncherUpdate`,
  `Connectivity`) resolve to null, and so do items whose mod has left the catalog —
  which is normal, because notifications PERSIST across sessions. `ModIconPresentConverter`
  (with `ConverterParameter=invert`) is what swaps the two; a `DataTrigger` can't do it
  because "the icon failed to load" isn't a value the row's bindings can see. Opening
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
  THREE places** — the direct raise in `ApplyAsync`'s success block (WolPatcher), the
  direct raise in `RepairInstallAsync` via `RaiseRepairFinishedBell` (the WHOLE
  GitHubReleases surface: update, version pick and repair all funnel through
  `InstallModOnlyAsync`), AND a **startup
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
  Pinned by `NotificationCenterTests`.
  **(2b) The GitHubReleases raise exists because the backstop alone is NOT enough, and its
  two rules are load-bearing.** The reconciliation only reacts to a version **ADVANCE**, so
  before this every operation that didn't raise the version was silent: a repair, a
  re-install of the same version, and a **downgrade** through the version picker (which
  additionally rewrites `NotifiedInstalledVersion` regardless). Reported for real —
  a maintainer downgraded Improvement Mod to `19.07.2026`, updated back to `24.07.2026`
  and got nothing, because the update path raised nothing directly and the backstop's
  raise was **deduped** against the `(mod, version)` item from the earlier update.
  So: (a) **the direct raise passes `bypassDedup: true`** — an action the user performed
  must answer even if that version already belled, the same reasoning that keeps
  `RaiseInstalled` undeduped; the BACKSTOP keeps its dedup because it runs on every check
  and has to stay idempotent. (b) **The direct raise must run BEFORE the post-operation
  `CheckAsync`** (it does: the raise is inside the success branch, the re-check fires after
  the `finally` via `else if (updated)`), so the backstop finds the fresh item and dedups
  itself out — reverse the order and both fire, since the direct one ignores dedup.
  Wording is picked per operation: an update gets `UpdateFinished`, while a version pick
  and a repair get **`Installed`** with their own strings — "Actualización completada"
  after a downgrade would be a lie, and the kind only drives icon/sound/click-target, all
  identical, so no new enum value is warranted. An `intact` repair (nothing damaged, no
  files re-laid) deliberately raises **nothing**. (3) **The bell POPUP follows the window.** A WPF
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
  (systemd `notifier`, nginx + Let's Encrypt) — **the IP is deliberately not recorded here**:
  the launcher depends on the DOMAIN alone, and a written-down address goes stale on the next
  migration, which has already happened once,
  **deliberately separate** from the lobby backend (`wol-lobby.duckdns.org`) — same
  split rationale as that backend. Sources + the tested deploy runbook live in the
  **companion `notifier-server` repo** (`github.com/Gorgorito12/notifier-server`,
  its `DEPLOY.md`); it auto-discovers tracked mods from the same catalog
  (`Gorgorito12/aoe3-mods-catalog`), so the launcher's default just works with no
  config. (The manifest shape is a CONTRACT with `NotificationFeed` /
  `NotificationFeedMod`: adding fields is safe, renaming/removing `mods` /
  `latestVersion` / `translations` silently breaks every client → coordinate across
  both repos.)

- **"A new room was created" fires a Windows notification via a BACKGROUND lobby
  poll (`MainWindow.PollNewRoomsAsync`), not a WS push.** There is no `lobby_created`
  push (the `/global/ws` channel is chat-only; emitting lobby events is deferred to
  the backend repo). So a MainWindow-owned `DispatcherTimer` (`_lobbyNotifyTimer`,
  **90 s** — process-wide, tab-independent) polls `_multiplayerSession.Api.ListLobbiesAsync()`
  and diffs lobby **`Id`s** (NOT `CreatedAt`, which is an unparsed string) against the
  session set `_knownLobbyIds`. The **first successful poll seeds `_knownLobbyIds`
  silently** (`_lobbyBaselineSeeded`) so existing rooms don't flood — only rooms
  created AFTER the poll starts notify. For each genuinely-new id it skips **your own
  room** (host id / DiscordUsername match) and rooms whose **mod isn't installed**
  (`MainWindow.IsModInstalled`, ported from `MultiplayerTab.IsModInstalledLocally` —
  the user chose "only joinable rooms"). **Room notifications DELIBERATELY do NOT go
  to the bell** (the user found room items cluttered the notification history) — the
  poll instead surfaces each genuinely-new room as a **Windows tray toast + a red dot**
  on the MULTIPLAYER nav tab and the Rooms subtab: it calls
  `NotificationCenter.TryMarkRoomNotified(id)` (persistent cross-restart dedup on
  `LauncherConfig.NotifiedRoomIds`, capped 500, **no** bell item — replaced the old
  `RaiseRoomCreated`) and, on first-seen, fires `ShowToast(...)` directly plus
  `SetMultiplayerTabDot(true)` (`MainWindow`, the `MultiplayerTabDot` ellipse on the
  `TopTabMultiplayer` button, cleared in `SwitchTopTab` when Multiplayer becomes
  active) and `MultiplayerView.SetNewRoomIndicator(true)` (`MultiplayerTab`, the
  `RoomsSubtabDot` on `SubtabRooms`, cleared when the user opens the Rooms subtab via
  `SubtabRooms_Click`/`ShowRooms`→`UpdateSubtabHighlights`; the `_hasNewRoomSignal`
  field keeps a repaint from stomping it). Both dots reuse the notification bell's
  badge recipe (7px `#E5484D` ellipse + a `DynamicResource` ring — `BgSidebar` on the
  nav tab, `MpSurface` on the subtab). Dedup is two-layer: the in-memory
  `_knownLobbyIds` (per session, seeds the silent baseline) + persisted
  `NotifiedRoomIds`. **`NotificationKind.RoomCreated` + its `KindBrush` /
  `NavigateToNotification`→`MultiplayerTab.ShowRooms()` handling are KEPT only for
  backward compat** — room items persisted by OLDER builds still render/click; no new
  room items are ever added. **Gating (load-bearing):** the tick early-returns unless
  `_config.NotifyNewRooms` (a dedicated Settings toggle, default true — now = toast +
  dot, no longer bell — separate from `ShowToastNotifications`) **AND**
  `_config.CheckUpdatesOnStartup` (metered/silent mode) **AND** the session is
  `SignedIn`. **Budget:** `/lobbies` is 60/min·2000/day per IP; 90 s ⇒ ~960/day — don't
  drop the interval (per-IP cap is shared behind NAT/Radmin). Detection lives ONLY in
  this poll (the MP tab's render poll doesn't raise it). Toast is still suppressed while
  the window is focused (existing `ShowToast` rule — fine, you'd see the room appear).
  **Future:** the efficient path is a backend `lobby_created` WS push + an app-level
  persistent `/global/ws`; that needs the separate backend repo, so the poll is the
  launcher-only MVP. **(Done — see the AppToast bullet below: `lobby_created` is now
  pushed over `/global/ws` for an INSTANT toast; this 90 s poll stays as the fallback,
  sharing dedup via `_knownLobbyIds` + `NotifiedRoomIds` so a room is announced once.)**

- **In-app "toasts" + multiplayer invites — `Controls/AppToast.cs` + a `/global/ws`
  invite/lobby-created protocol. A THIRD notification surface, distinct from the bell
  (persistent history) and the OS tray balloon.** `AppToast` is a small, NON-modal,
  auto-dismissing card that slides into the window's bottom-right and STACKS (host =
  `ToastHost` StackPanel in `MainWindow.xaml`, spans all rows, `Background=null` so only
  the cards catch clicks). Reuses `MpAlertOverlay`'s card look (MpSurface + two-tone rim +
  shadow) but has no scrim, times out (~9 s), and carries optional action buttons
  (Join/Ignore). Wired to `MultiplayerTab` via the `showAppToast` callback in
  `MultiplayerView.Attach`.
  **`MainWindow.ShowAppToast` ROUTES the card to the surface the user can actually see —
  ONE surface per event, never two.** Looking at the window → in-window (`ToastHost`)
  — **unless the card asks for `ToastOptions.PreferDesktop`, which skips that branch and
  goes to the desktop even with the launcher in front.** Exactly one card still appears;
  it just always appears in the same place. **The room INVITE sets it** (and so does the
  "muted" confirmation, which answers a button pressed on a desktop card): an invite
  expires and carries a Join worth pressing, and drawn inside the window it is missed from
  another tab or another monitor — which is what was reported. "A room opened" is ambient
  and keeps the normal routing. `PreferDesktop` does NOT override the suppression below,
  and the ORDER of the two checks is load-bearing: the suppression sits AFTER the
  in-window branch, so a launcher in the foreground during a game still draws in-window,
  which is harmless because the game is not the frontmost window then. Continuing:
  **game running (`_isGameRunning`) → nothing** (a topmost window can knock AoE3 out of
  full-screen, and a room invite is useless mid-match; the bell entry and sound still
  happen); otherwise (minimised, in the tray, buried) → **`Controls/DesktopToastWindow`**,
  a `Topmost`, `ShowInTaskbar=false` window pinned to the bottom-right of
  `SystemParameters.WorkArea` that hosts the SAME card. It reuses `AppToast` untouched
  because `AppToast.Show` takes any `Panel` and resolves brushes via `Application.Current`
  — it never depended on the main window. **Load-bearing:** `ShowActivated=false` (or the
  toast steals keystrokes from whatever the user is doing), `AllowsTransparency=true` (the
  deliberate exception to the repo's no-transparency rule for dialogs — the card has
  rounded corners and a shadow over an arbitrary desktop, and it is small and transient),
  and it closes itself once its panel empties so an invisible topmost window can't linger.
  **Seeing any of this without a second player: `MainWindow.PreviewNotificationToasts`**,
  reachable from Settings → Developer and from the **`--preview-toasts`** launch argument.
  A real invite needs another account on another machine, which makes "how does it look"
  almost untestable by waiting; the preview builds the same cards through the same routing
  — never by calling `DesktopToastWindow` directly, which would flatter it and hide the
  very bug worth finding — with inert buttons and a long dismiss so there is time to look.
  The ARGUMENT is what makes a screenshot scriptable; the Settings route needs clicks. To
  capture it: `EnumWindows` finds the window (UI Automation does not — it is
  `ShowInTaskbar=false`), but use a SCREEN capture, **not `PrintWindow`**: PrintWindow
  draws onto an opaque DC, so the transparent margin comes out black and cannot answer
  whether the real thing leaves a black box on the desktop. It does not — verified on
  screen, composited over another app.
  **It replaced a tray-balloon fallback** (`ShowToast`) that Windows drops easily and —
  the real defect — **cannot carry action buttons**, so a "new room" arrived with no way to
  Join. `ShowToast` still serves the BELL (`NotificationCenter.ToastRequested`); don't
  re-add it here. The Join action calls `BringToForeground()` FIRST, since the click can
  come from the desktop card with the window hidden in the tray.
  **Every early return in `OnNewRoomFromWs` LOGS** (own room / mod not installed / already
  announced) — the path used to be entirely silent, which made "the notification didn't
  appear" indistinguishable between a bug and the three deliberate filters; hosting the
  room yourself is the usual answer.
  **Three real-time frames over the always-on `/global/ws`** (owned by `MultiplayerTab`,
  sent via `LobbyWebSocket.SendAsync`): (1) **invite** — right-click a player in the
  Players panel → "Invite to my room" (`AttachInviteContextMenu`, enabled only while
  `_session.CurrentLobbyId != null`) sends `{type:"invite", target_user_id, lobby_id}`;
  the backend (`GlobalChatRoom.handleInvite`) VALIDATES the sender is a member of that
  lobby, rate-limits (`globalChatInvitesPerMin`), and routes `{type:"invite", from,
  lobbyId, roomName, modId}` to the target's one socket + acks the sender
  `invite_sent`. The recipient's `HandleInviteFrame` shows an AppToast whose Join runs
  the SAME `JoinByLobbyIdAsync` as the deep link. Invite errors (`invite_*`) surface as a
  toast, not the chat composer notice. **Receiver-side anti-spam (3 client-only gates in
  `HandleInviteFrame`, complement the backend's sender-side rate-limit + room-membership
  validation — no backend change):** each incoming invite is dropped SILENTLY (no toast, no
  `PlayConnect` sound, logged) when (a) the global opt-out `LauncherConfig.ReceiveInvites` is
  off (Settings → "Let players invite me…" / `DlgSettingsReceiveInvites`, default true), (b)
  the sender is in the session-only mute set `_ignoredInviters` (the toast's **"Mute"** action
  = the old no-op "Ignore" repurposed; adds the sender + shows a 🔕 confirm; NOT persisted, so
  a restart clears it — the global toggle is the durable opt-out), or (c) the sender is within
  the per-sender `InviteCooldownMs` (60 s) window tracked in `_inviteCooldownByUser` (kills a
  flood). All three key off a stable `senderKey` = `from.userId` (fallback `from.id`, fallback
  `from.login`) so a griefer's repeat invites collapse to one identity; the toast's ✕ /
  auto-dismiss still handle "ignore just this one". Don't persist the mute set or drop the
  cooldown; if the community grows, THEN lower `globalChatInvitesPerMin` / add backend
  strike-timeout (like the global chat), not now. (2) **lobby_created** — POST /lobbies broadcasts it
  (backend `announceLobbyCreated`, **skips private rooms**); `HandleLobbyCreatedFrame`
  hands it to `MainWindow.OnNewRoomFromWs`, which SHARES the poll's dedup (`_knownLobbyIds`
  + `NotifiedRoomIds.TryMarkRoomNotified`), gates (not my room, mod installed), shows the
  toast (Join), and sets the same tab/subtab dots — so the WS push and the 90 s poll never
  double-announce. (3) **invite_sent** — brief confirmation toast. **Load-bearing:** the
  `lobby_created` dedup MUST feed `_knownLobbyIds` (so the fallback poll skips the room)
  AND gate on `TryMarkRoomNotified`; don't split them. **Backend contract** (repo
  `wol-launcher-lobby-node`): `GlobalChatRoom` gained `handleInvite`/`announceLobbyCreated`
  + the `invite` dispatch case + `inviteWindowStart/inviteCount` on `AttachedSocket`;
  `rest.ts` POST /lobbies reads the host once and calls both `announceLobbyCreated`
  (in-app, all non-private) and the Discord webhook; `env.ts` `globalChatInvitesPerMin`
  (default 10). Forward-compatible: an old launcher ignores the new frames; an old backend
  answers the `invite` with `unknown_type` (swallowed). Deploy: `git pull` +
  `systemctl restart wol-lobby`.

- **Feedback sounds are a tiny dependency-free layer — `Services/SoundService.cs`
  playing embedded WAVs via `SoundPlayer` — gated by one config toggle, and the
  "don't sound on history / on my own message" rules are load-bearing.** Five
  short synthesized WAVs (`Assets/Sounds/{chat,notify,connect,success,error}.wav`,
  shipped as `<Resource>` next to the icons) map to five categories: **Chat**
  ("blip"), **Notification** ("ding"), **Connect** ("pop", shared), **Success**
  (ascending G5→C6) and **Error** (descending A4→F4). **Categories are SEMANTIC,
  not one-per-event** — eight notification kinds mapped to eight tones would be
  indistinguishable, and the installed-mods sweep can raise several at once, so
  `MainWindow.SoundForNotification` groups them: `Installed`/`UpdateFinished` →
  Success, everything else → Notification (which IS the "available/heads-up" tone,
  reused rather than duplicated under a second name). **Error is NOT a notification
  kind** — failures leave no bell item, so it hangs off `ShowProgressError`, the
  single funnel every failed operation already goes through. A finished **uninstall**
  plays Success directly with **no bell entry** (the user asked for it and is watching;
  a history row would be noise — same reasoning that keeps room notifications out).
  **`NotificationCenter.ItemAdded` carries the `NotificationItem`** precisely so the
  sound can be chosen from `Kind`; it used to fire `EventArgs.Empty`, which is what
  forced one tone for everything. New sounds are synthesized to peak at 29490 (90 % of
  scale) like the existing three, or they land louder/quieter than their siblings.
  `SoundService` is
  static/UI-free: caches each WAV's bytes once (`Application.GetResourceStream`),
  and each `Play` spins a FRESH `SoundPlayer` over a new `MemoryStream` so sounds
  overlap and any thread can call it (no `MediaPlayer` — it needs a UI-thread
  Dispatcher + first-play latency; no NAudio). Every play is best-effort
  try/caught (audio must never kill the app), no-op when `!Enabled`, and
  **throttled per category** via `Environment.TickCount64` (Chat/Notify 300 ms,
  Connect/Success/Error 900 ms — presence altas cluster, and completions deserve to
  land cleanly). Resource path + throttle live in ONE table keyed by `SoundKind`, so
  adding a sound is a single entry rather than a field plus two switch arms that can
  disagree. `Enabled` is wired to
  `LauncherConfig.EnableSounds` (default true, independent of
  `ShowToastNotifications`/`NotifyNewRooms`) at MainWindow ctor startup + re-applied
  on `LauncherSettingsDialog` save (same pattern as `MultiplayerTelemetry.Enabled`);
  the toggle is the "Sounds" checkbox in Settings (`DlgSettingsSounds*` strings).
  **Event wiring (load-bearing rules):** (1) **Notification** — `PlayNotification`
  on `NotificationCenter.ItemAdded` (the single add path, already deduped), NOT
  gated on the toast's "user is looking" rule so it plays in-window too. (2)
  **Chat** — `PlayChat` only in the LIVE frame handlers (global `OnGlobalChatFrame`
  `case "chat"`, lobby `HandleChat`), NEVER inside `AppendGlobalChatLine` /
  `AppendChatLine` / `ReplayChatRing` / `RenderGlobalChatState` (those replay
  HISTORY on join and must stay silent), and skipped when the line's `userId` ==
  `_session.CurrentUser.Id` (no sound for your own message). (3) **Connect** —
  `PlayConnect` on `HandleMemberJoined` (someone joins your room, not you), in
  `MainWindow.PollNewRoomsAsync` (a new room appears), and in `ParseOnlineUsers`
  for a genuinely-new presence `userId`. The presence path is the noisy one:
  `_presenceSeenIds` + `_presenceBaselineSeeded` seed a SILENT baseline on the
  first frame (so connecting and receiving the whole roster doesn't machine-gun
  pops), then one pop per frame for a new non-self id (the Connect throttle
  smooths the rest). Don't move the chat sound into the shared append helpers, and
  don't drop the presence baseline seed — both re-introduce a sound flood.

- **The Discord room announcement carries a "Join" DEEP LINK that opens the
  launcher and AUTO-JOINS the room — and this made the launcher SINGLE-INSTANCE.**
  The webhook embed (`discordAnnounce.ts`) now includes a `description` with
  `▶ [Join in the launcher](https://wol-lobby.duckdns.org/j/<lobbyId>)` (only while
  the room is joinable — dropped on `closed`). **Discord can't linkify a custom
  scheme**, so the link is HTTPS → a backend **bounce route `GET /j/:id`**
  (`index.ts`, public, no DB, validates `^[A-Za-z0-9]{1,32}$`) returns a tiny HTML
  page that redirects the browser to `wol-launcher://join/<id>`. The launcher
  registers that scheme per-user in `App.OnStartup` via
  `Services/DeepLinkService.EnsureRegistered()` (HKCU `Software\Classes`,
  idempotent, self-healing exe path — mirrors `StartupRegistrationService`);
  `DeepLinkService.TryParseJoin` treats the URI as UNTRUSTED (any web page can fire
  it) and only ever yields a validated lobby id (pinned by `DeepLinkServiceTests`).
  **Single-instance (NEW behaviour):** a deep link fired while the launcher is open
  must route into the RUNNING instance, not spawn a second window. `App.OnStartup`
  takes a per-session `Mutex` (`Local\WarsOfLibertyLauncher.SingleInstance.v1`); the
  SECOND launch forwards its intent over a **named pipe**
  (`WarsOfLibertyLauncher.DeepLink.v1`) to the primary and `Shutdown()`s WITHOUT
  showing a window; the PRIMARY runs a background pipe-server loop that marshals to
  the dispatcher. The pipe carries one of two payloads: a valid lobby id →
  `DispatchJoin` (raises `App.JoinRequested`), or the sentinel `__show__` →
  `DispatchShow`. **A plain relaunch of the .exe while the launcher sits in the
  tray now RESTORES the window** (the Steam/Discord behaviour): the second instance,
  with no deep link, forwards `__show__` (UNLESS it carried `--minimized` — a
  duplicate auto-start stays in the tray), and the primary calls the new public
  `MainWindow.BringToForeground()` → `ShowFromTray()`. Both dispatch paths now go
  through `BringToForeground()` (not a bare `Activate()`), which `Show()`s a window
  hidden to the tray — a bare `Activate()` can't un-hide a `Hide()`'d window, so a
  deep link arriving while the launcher was tray-hidden previously never surfaced it.
  The `ShowCommand`/`__show__` sentinel is deliberately an INVALID lobby id (the
  underscores) so it can't collide with a real join payload; `--minimized` is
  detected once in `OnStartup` and shared by the primary path (`StartMinimized`) and
  the second-instance branch. **Load-bearing: `StartupUri` was REMOVED from `App.xaml`** so
  the guard can suppress a second window — the primary creates `new MainWindow()`
  itself in `OnStartup` (don't re-add `StartupUri`; `ShutdownMode=OnMainWindowClose`
  still works because we set `Application.MainWindow`). A COLD-START link (this
  launch WAS the click) is stashed in `App.PendingJoinLobbyId` and drained by
  `MainWindow`'s `Loaded` handler. Both paths call `MainWindow.HandleJoinDeepLink`
  → `SwitchTopTab(Multiplayer)` + `MultiplayerTab.JoinByLobbyIdAsync(id)`, which
  ensures sign-in (opens `GitHubLoginDialog` if needed), resolves the `LobbySummary`
  from `Api.ListLobbiesAsync()` (no get-by-id endpoint), and runs the SHARED
  `JoinLobbyCoreAsync(LobbySummary)` (the extracted core of `JoinRoomButton_Click` —
  same mod-install / fingerprint / password / mod-mismatch gates). **Scope caveat:**
  only works for users who already have the launcher installed (scheme registered) +
  signed in + the room's mod installed — it's for re-joining, not onboarding. The
  mutex fails OPEN (a policy/ACL failure runs a normal instance rather than refusing
  to start). Registering a URI scheme is a standard pattern (`steam://`,
  `discord://`) but can add SmartScreen/AV friction on the unsigned binary — best
  paired with the SignPath signature.

- **THE PREREQUISITE for auto-start: the launcher runs `asInvoker` (un-elevated).
  A Run-key registration can NEVER auto-start a `requireAdministrator` binary — this
  was the real, forensically-confirmed reason "Start with Windows" did nothing.**
  Windows processes the HKCU `Run` key (and the Startup folder) at logon with the
  user's NORMAL, non-elevated token and **silently skips** any entry whose target
  requires elevation — no UAC prompt, no error, no launch. So under the old
  `app.manifest` `requireAdministrator` the whole chain below could be perfect (Run
  value present + pointing at a good exe, `StartupApproved` enabled, config seeded)
  and Windows still launched nothing. Two earlier rounds of fixes (point the Run key
  at the stable canonical copy; copy the whole framework-dependent folder so that
  copy is runnable — see the self-install bullet) were **necessary but not
  sufficient**; the manifest was the blocker. The fix flipped the manifest to
  `asInvoker` (Steam model): the client runs un-elevated so the Run key actually
  fires for everyone, there's no UAC on daily use, and privileged operations
  (any WRITE into a protected install folder) elevate ON DEMAND via
  `ElevationService.CanWriteTo` + `RelaunchElevated`. **Every write path is
  guarded** (a protected NON-Steam folder — e.g. retail/GOG AoE3 in Program Files —
  is the case that needs it; a Steam-library or user-chosen folder is already
  writable and never prompts): install (`InstallAsync:8797`), WoL-patch update
  (`ApplyUpdateWithElevationCheckAsync`, uses `--update-now` auto-resume), uninstall
  (`UninstallMenuItem_Click`), and — via the shared `MainWindow.EnsureInstallWritableOrElevate`
  helper — **Repair / GitHubReleases update / version-pick** (`RepairInstallAsync`, guard
  placed right before each `InstallModOnlyAsync` write so an intact repair that skips the
  write never prompts) and **applying a translation** (`ApplyTranslationAsync`, before the
  `data\`/`_originals\` write). Detection/recognition is READ-only (probes, marker,
  `data\*.xml`, HKLM/HKCU reads, `FindAllDeep`/`ModInstallScanner`, "Change folder"),
  so it never needs admin in any folder. The elevate-on-demand relaunch has NO
  auto-resume except the WoL update path — the user re-triggers the action in the
  elevated instance (same as install/uninstall). The only ways to auto-start an *elevated* app silently are a Scheduled
  Task (highest-privilege logon trigger — the Voobly model) or a Service; both are
  heavier and a stronger AV persistence signal on an unsigned binary, so they were
  deliberately rejected. **Don't restore `requireAdministrator`** — it re-breaks
  Run-key auto-start (and everything below becomes inert again).
- **"Run in background" is ONE toggle that bundles auto-start + minimise-to-tray
  + start-to-tray, it is now ON BY DEFAULT, and the default is real only because a
  MARKER-gated one-time seed writes the Run key — auto-start opens straight to the
  tray via a `--minimized` arg, not the config.** The single `StartWithWindowsCheck`
  in Launcher Settings ("Start with Windows in the background" / "Iniciar con Windows
  en segundo plano") drives all three config flags together on save:
  `StartWithWindows` + `MinimizeToTray` + `StartMinimized` (**all three now default
  `true`**). **(The old standalone MinimizeToTray checkbox is NO LONGER
  collapsed — it was re-surfaced and rewired to the independent `CloseToTray` flag;
  see the close-to-tray bullet below. It is NOT part of this bundle anymore.)**
  `StartupRegistrationService.Apply(enabled, startMinimized)`
  appends **`--minimized`** to the HKCU Run-key command when `startMinimized` — so
  the **Windows-login** launch opens to the tray, while a **manual double-click**
  (no arg) still shows the window. `App.OnStartup` parses `--minimized` into
  `App.StartMinimized`, pre-sets `WindowState=Minimized` before `Show()` (avoids a
  flash), and `MainWindow`'s Loaded handler calls `HideToTray()` when it's set. The
  self-update preserves the exe path, so the registered auto-start survives updates.
  Don't split the toggle back into separate checkboxes (the user asked for one), and
  keep `--minimized` on the registration side (not a config field) so manual launches
  aren't affected.
  **The default-ON mechanics — three coupled facts, none optional:**
  (1) **Flipping the config defaults alone does NOTHING visible.** `LoadFromConfig`
  sets the checkbox from `StartupRegistrationService.IsRegistered()` — the **REGISTRY**,
  not the config — and the Run key is only ever written by `Apply`. A config default of
  `true` with no Run key yields the *identical* unchecked checkbox plus a silent
  divergence (`MinimizeToTray=true` keeps the tray icon resident while the checkbox
  reads off). This is the trap: the reported "why does it appear to activate?" was
  exactly the registry and the config agreeing on OFF.
  (2) **The seed is keyed off `LauncherConfig.BackgroundDefaultSeeded`, NEVER off "the
  Run key is missing".** `MainWindow`'s ctor (right before the `EnableJoinLinks` line —
  the mirror pattern) calls the pure `StartupRegistrationService.PlanStartup(seeded,
  startWithWindows, alreadyRegistered)` → `BackgroundStartupPlan(SeedNow, Register,
  ShowNotice)`, then executes it: on `SeedNow` it sets the marker **FIRST** (so a failed
  registry write — managed-PC policy, AV — can't leave the seed retrying every launch),
  **FORCES** the three flags true, saves, and `Apply`s. Keying it off the missing key
  would silently re-enable auto-start the launch after an opt-out — **a default that
  won't stay off is malware behaviour**, and it's the single worst bug this feature
  could ship. Pinned by `BackgroundStartupPlanTests.OptedOut_NeverReArms`.
  (3) **The seed FORCES the flags instead of reading them, and that's what reaches
  EXISTING users** — their config has a persisted `startWithWindows:false` that
  overrides the new `= true` default at deserialisation, so reading it would seed
  nobody. A pre-existing `false` is treated as "never chose" (the toggle used to default
  off), not "declined". Once seeded, the flag is obeyed literally and forever;
  `Apply(plan.Register)` runs every launch, which self-heals the registered exe path
  (the portable binary moves) and clears a stale key after an opt-out.
  **The Run key targets the STABLE installed copy when one exists, NOT the volatile
  running exe — this closed a confirmed "auto-start did nothing" bug.** The self-heal
  used to write `Environment.ProcessPath`, so launching a portable/dev build
  (`publish\`, `bin\Debug\`, a moved download) re-pointed the key at THAT path — and
  if it was gone by the next Windows login (a `build-release.ps1` that wiped `publish\`,
  a `dotnet clean`, a re-downloaded exe), login silently launched nothing (forensically
  confirmed: Run key → deleted `publish\` exe, no auto-start session logged, no crash).
  Both `Apply` call sites (the ctor self-heal at `MainWindow` ~249 and the Settings save
  `SaveButton_Click` step 1b) now pass `exePathOverride: SelfInstallService.ResolveAutoStartExe()`,
  whose pure core `SelectAutoStartExe` (unit-tested `AutoStartTargetTests`) returns
  `CanonicalExe` when `File.Exists(CanonicalExe)` else `Environment.ProcessPath`. So once
  a stable copy exists (the user did "Install on this PC", or accepted the opt-in prompt
  below), running ANY volatile build heals the key back to the durable canonical path
  instead of clobbering it. **This does NOT auto-install** — it only PREFERS an existing
  canonical copy, so a not-installed portable user is byte-for-byte unchanged. Dev
  trade-off (accepted): login opens the canonical copy (possibly an older build than the
  one you run by hand); to change what auto-starts, re-run "Install on this PC" from the
  build you want. **Enabling the toggle from a portable exe with NO canonical copy yet
  OFFERS an opt-in self-install** (`SaveButton_Click`: `MessageBox` YesNo, keys
  `DlgSettingsBgInstallPrompt{Title,Body}` / `DlgSettingsBgInstallFailed`) — Yes runs
  `SelfInstallService.Install()` and the Run key then points at the canonical copy; No
  registers the portable path as before (fragile, their choice; a failed install falls
  back the same way). The prompt is explicit consent, so it respects SelfInstallService's
  OPT-IN contract — don't turn it into a silent auto-copy.
  **Two registry facts that mislead:** Task Manager's Startup tab **disables without
  deleting** our Run value (it writes `Explorer\StartupApproved\Run`), so a TM-disabled
  entry still reads as registered by `IsRegistered()` and the checkbox shows checked —
  deliberately not parsed (Windows honours its own disable no matter what we write). And
  because `Apply` re-runs each launch, a Run value deleted by an external cleaner (NOT
  via our checkbox, which sets the flag) does come back — that's the same self-heal
  contract `DeepLinkService.EnsureRegistered` has, and the flag remains the opt-out.
  **Why default-ON is defensible despite the old AV rule it replaces:** the observed
  Defender FP (`Win32/Injector`) came from the **compression packer**, not the Run key —
  no FP was ever traced to auto-start; Steam/Discord/Epic/OneDrive all ship a Run key;
  and the project already ships a default-ON HKCU write (`EnableJoinLinks`). The risk is
  **speculative, not measured**. What survives from the old rule is the part worth
  keeping — **it is never SILENT**: the seed fires a one-time tray balloon
  (`MainWindow.MaybeShowBackgroundSeedNotice`, `_pendingBackgroundSeedNotice`,
  `TrayBackgroundSeed{Title,Body}`) naming the change and where to undo it. **Don't
  remove that balloon** — it is what makes the default legitimate rather than sneaky.
  (SignPath remains the durable mitigation for the whole unsigned-binary AV class.)
  **The Settings save no longer discards `Apply`'s return value.** It runs in
  `SaveButton_Click` **step 1b, BEFORE any `_config` mutation** (same "validate first,
  don't half-apply" shape as the catalog-repo check), and a `false` shows the red
  `DlgLauncherSettingsStartupFailed` hint + switches to the General tab + returns
  without closing. Discarding it produced a self-contradicting silent failure: config
  saying "on" while the registry-backed checkbox came back unchecked, unexplained.
  **The label/hint say what the toggle DOES (start with Windows) — don't revert them to
  the presence claim.** The old copy ("stay shown as connected even without the window
  open") described what the user ALREADY had: presence rides an always-on `/global/ws`
  socket gated only on `SignedIn`, and `CloseToTray` defaults on — so it read as "isn't
  this already happening?", which is what made the toggle look broken. Same trap in the
  tooltip: "closing the window keeps it running" belongs to `CloseToTray`, a different
  checkbox.

- **Closing the window (X / Alt+F4) hides the launcher to the tray by default —
  governed by `LauncherConfig.CloseToTray` (default TRUE), with a one-click opt-out —
  and EVERY intentional `Application.Current.Shutdown()` MUST set the static
  `MainWindow.HardExitRequested = true` first, or it hides instead of quitting.**
  `MainWindow.OnClosing` intercepts a bare user close when `!HardExitRequested &&
  _config.CloseToTray` → `e.Cancel = true; HideToTray()` + a ONE-TIME onboarding
  balloon (`MaybeShowClosedToTrayHint`, guarded by `ClosedToTrayHintShown`, fires the
  `TrayClosedHint*` balloon directly regardless of `ShowToastNotifications`). The tray
  icon's → **Exit** (`RequestHardExit`) is the way to fully quit; the minimize button
  is untouched (it never hits `OnClosing`, still goes to the taskbar). **`HardExitRequested`
  is STATIC on purpose** because two real-exit paths live OUTSIDE MainWindow —
  `LauncherUpdateDialog` (self-update relaunch) and `SelfInstallService` (self-install
  relaunch) — and must signal a hard exit before their `Shutdown()`; without it the
  relaunch would hide the old instance to the tray and the new copy would hang on the
  single-instance mutex. The invariant "set `HardExitRequested` before any intentional
  `Shutdown()`" covers all six real-exit sites: tray Exit + game-start-close (via
  `RequestHardExit`), brand-menu Exit, the two elevated relaunches (install/update),
  self-update, self-install. (`App.OnStartup`'s second-instance `Shutdown()` is exempt —
  it runs before a MainWindow exists.) `CloseToTray` is **INDEPENDENT of the "Run in
  background" bundle** (it adds NO registry/persistence, so no new AV signal) and is
  toggled by its own re-surfaced "Minimize to tray on close" checkbox
  (`MinimizeToTrayCheck`, wired to `_config.CloseToTray` in `LauncherSettingsDialog`
  load/save — NOT to `MinimizeToTray`); `MinimizeToTray` now only feeds the bundle +
  `UpdateTrayIconVisibility` keepResident (which also ORs in `CloseToTray`). Don't
  re-collapse the checkbox, don't fold `CloseToTray` back into the run-in-background
  toggle, and don't add a `Shutdown()` without the hard-exit flag.

- **The portable single-file exe can self-install to a stable location —
  `Services/SelfInstallService.cs`, opt-in, NOT a return to Inno Setup.** The launcher
  ships as one signed self-contained exe; that's fragile for "run in background"
  (a Downloads-folder exe that gets moved/deleted breaks auto-start). `SelfInstallService`
  copies the running exe to `%LocalAppData%\Programs\Aoe3ModLauncher\Aoe3ModLauncher.exe`,
  creates Start-Menu + Desktop shortcuts (reusing `NativeInstallService.CreateShortcutFile`,
  now `internal`; the exe is its own `.lnk` icon source), then relaunches from there and
  shuts down the portable instance. **Single-instance handoff:** the relaunched child
  carries `SelfInstallService.FromInstallArg` (`--from-install`) and `App.OnStartup`, when
  it sees that arg and the mutex is still held by the exiting parent, **waits up to 5 s**
  (`WaitOne`, treating `AbandonedMutexException` as acquired) instead of quitting as a
  duplicate — without this the relaunch aborts. The self-update keeps swapping in place at
  the installed path. Exposed via a "Install on this PC" button in Launcher Settings →
  Maintenance, hidden when already `IsInstalled()`. Don't reintroduce an installer
  toolchain — this is the deliberate lightweight alternative.
  **Whether the install ALSO enables "run in background" (auto-start) is governed by the
  SINGLE GENERAL toggle `StartWithWindowsCheck`, NOT a separate install-time checkbox.**
  There USED to be a second, pre-checked `InstallRunInBackgroundCheck` next to the install
  button — it was REMOVED because it duplicated / contradicted the GENERAL "Ejecutar en
  segundo plano" toggle (two competing controls for the same setting, shown in disagreement).
  Now `SelfInstallButton_Click` reads `StartWithWindowsCheck.IsChecked`: if on, it enables
  the three background flags + `_config.Save()` + registers auto-start — and MUST pass
  `StartupRegistrationService.Apply(..., exePathOverride: SelfInstallService.CanonicalExe)`
  because it still runs from the PORTABLE exe, so `Environment.ProcessPath` (the default)
  would register the wrong path. If the toggle is off, the install registers NOTHING (no
  Run-key). **AV note (REVISED — the toggle now defaults ON):** auto-start is no longer
  opt-in, so "default OFF = no Run-key persistence at all" is dead. What replaced it is
  "never SILENT": the one-time seed announces itself with a tray balloon and the toggle
  stays one visible click away — see the run-in-background bullet for why default-ON is
  defensible (the observed Defender FP was the compression packer, not the Run key). By
  the time the user reaches this button the seed has already registered auto-start, so
  the `exePathOverride` here is what re-points the Run key from the portable exe to the
  installed copy. The durable fix for the whole self-signed-binary AV class is still the
  SignPath trusted signature. Don't re-add a second run-in-background checkbox.
  **The auto-start Run key now PREFERS this canonical copy whenever it exists, and the
  toggle OFFERS this install opt-in when it doesn't** (see the run-in-background bullet's
  Run-key-target paragraph): `SelfInstallService.ResolveAutoStartExe()` (pure core
  `SelectAutoStartExe`, pinned by `AutoStartTargetTests`) returns `CanonicalExe` when it's
  on disk else `Environment.ProcessPath`, and BOTH background `Apply` call sites (ctor
  self-heal, Settings save) feed it through `exePathOverride`. So a stable copy protects
  auto-start from being clobbered by a volatile launch, and enabling the toggle from a
  never-installed portable exe prompts (opt-in, `MessageBox` YesNo) to create one. This
  keeps SelfInstallService's "nothing runs automatically" contract intact — the copy is
  only ever made by an explicit "Install on this PC" click OR an explicit Yes on that
  prompt, never silently.
  **A THIRD entry point makes this automatic for players who never open Settings: a
  ONE-TIME first-launch offer (`MainWindow.MaybeOfferSelfInstall`, called from the Loaded
  handler right before `MaybeShowBackgroundSeedNotice`).** Auto-start is default-ON but a
  portable exe the player later moves/deletes silently breaks the Run key — the exact
  `publish\` bug the maintainer hit — and a player won't think to run "Install on this
  PC". So the first time we're running a portable build with auto-start on, the launcher
  OFFERS the same install: gated on `!App.StartMinimized && _config.StartWithWindows &&
  !SelfInstallService.CanonicalLooksRunnable() && !_config.SelfInstallPromptShown` (a
  VISIBLE/manual launch — never a `--minimized` tray launch, where a modal over a
  tray-hidden window would be wrong; auto-start on; no runnable canonical copy yet; not
  offered before). It sets `SelfInstallPromptShown = true` + `Save()` **FIRST** (same
  rationale as the seed marker — a failed/declined attempt must never re-nag), then a
  `MessageBox` YesNo reusing the SAME strings as the Settings prompt
  (`DlgSettingsBgInstallPrompt{Title,Body}` / `DlgSettingsBgInstallFailed` — zero new
  strings; that copy already reads "runs from this .exe, auto-start breaks if you move/
  delete it, install a stable copy?"). Yes → `SelfInstallService.Install()` → on success
  `RelaunchInstalledAndExit()` (hard-exits; the canonical instance self-heals the Run key
  to the canonical exe via its ctor `Apply(ResolveAutoStartExe())`), on failure a warning
  and stay portable; No → stay portable. It returns true when it showed the dialog, and
  the Loaded handler then sets `_pendingBackgroundSeedNotice = false` so the tray seed
  balloon doesn't ALSO fire — first-launch onboarding is one popup, not two. If a runnable
  canonical copy already exists (the maintainer, a returning installed user) it stays
  silent (auto-start is already durable). Still opt-in — an explicit Yes — so the
  "nothing installs itself" contract holds. Don't key the offer off the missing Run key
  (that's the `SelfInstallPromptShown` marker's job), and don't drop the `!StartMinimized`
  gate (no modal over the tray).
  **The offer is a THEMED dialog, not the white OS `MessageBox` — and the first-run UI
  language is auto-derived from the Windows display language.** The onboarding used to pop
  the raw Windows `MessageBox`, which (a) clashed with the dark "dorado imperial" theme and
  (b) showed in ENGLISH — the config default `Language="en"` didn't look at the OS, so a
  mostly-Spanish audience's very first screen was English. Two coupled fixes: **(1) the UI
  language FOLLOWS the Windows display language until the user overrides it in Settings.**
  `MainWindow.ApplyStartupLanguage()` (called in the ctor where `Strings.SetLanguage` used to
  be, ~:309): while `LauncherConfig.LanguageExplicitlyChosen` is false, it re-derives
  `Language = DefaultLanguageForCulture(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)`
  every launch (pure, unit-tested `DefaultLanguageTests`: `"es"` → `"es"`, else → `"en"` — only
  two ship), saves if it changed, then `Strings.SetLanguage`. This applies to **fresh AND
  existing configs** (an existing config predates the flag → deserializes false → follows the
  OS), which is the whole point — a "fresh-config only" version was tried first and DIDN'T
  work, because every existing user's config already said `en` so it never applied.
  `LanguageExplicitlyChosen` flips to true ONLY in `LauncherSettingsDialog.SaveButton_Click`
  and ONLY when the picked language actually differs from the current one (saving Settings
  without touching the language must not silently lock it); after that the launcher holds the
  user's pick and stops following the OS. There is NO first-run popup (an earlier
  `FirstRunLanguageDialog` chooser was removed — the ask was "just start in the Windows
  language", silently). Use `CurrentUICulture` (the Windows DISPLAY language), not
  `CurrentCulture` (regional format); the detected culture is logged for diagnosis. Don't move
  the derivation back into `Load()` (it's centralized at startup so it covers existing configs
  and is idempotent), and don't drop the "only on actual change" guard on the explicit-choice
  flag. **(2) `SelfInstallPromptDialog`** — the themed version of the install offer (modeled on
  `PasswordPromptDialog`: `WindowStyle=None` + shared `controls:TitleBar` + `BgBase`, modal
  `ShowDialog` so `DialogResult` is valid), reusing the SAME Tarea-H strings
  (`DlgSettingsBgInstallPrompt{Title,Body}`) plus button labels
  `DlgSettingsBgInstallPrompt{Yes,No}`; `MaybeOfferSelfInstall` shows it (`ShowDialog() ==
  true` = install) instead of the `MessageBox`. The rare install-FAILURE path keeps its
  `MessageBox` (edge case). Don't re-add a first-run language popup. **Existing-user
  transition:** they get all of this via the normal self-update (the exe is swapped in place
  at their current path); their next manual launch follows the OS display language (unless
  they'd picked one in Settings) and shows the themed install offer in that language.
  **`Install()` copies the WHOLE build folder for a framework-dependent build, not just
  the exe — and auto-start only registers a canonical copy that's actually RUNNABLE.**
  A confirmed second cause of "auto-start launched nothing": `Install()` used to
  `File.Copy` only the exe, on the assumption it's always the self-contained single-file
  build. But self-installing from a dev `bin\Debug\`/`bin\Release\` build (a ~0.29 MB
  **framework-dependent apphost** that needs `Aoe3ModLauncher.dll` + the dependency DLLs
  beside it) copied only the stub → a canonical copy that fails before .NET starts (no
  log, no crash). Fix: the copy logic is the testable seam
  `SelfInstallService.CopyPayload(sourceExe, destDir)` — framework-dependent (a sibling
  `Aoe3ModLauncher.dll` sits next to the exe) copies the whole output folder recursively
  (`CopyDirectory`: exe + DLLs + `*.runtimeconfig.json` + `*.deps.json` + `runtimes\`);
  self-contained single-file (no sibling DLL, e.g. `publish\`) copies just the exe as
  before. Paired with it, `ResolveAutoStartExe` now gates on the pure
  `SelfInstallService.CanonicalRunnable(exeExists, siblingDllExists, exeLength)` (via
  `CanonicalLooksRunnable`): a canonical exe counts only when it exists AND (a sibling DLL
  is present OR the exe is ≥ `SelfContainedMinBytes`=50 MiB — the apphost is ~0.29 MB, the
  single-file ~165 MB, so it separates cleanly). A present-but-broken apphost is NOT
  registered — auto-start falls back to the running exe, and the Settings toggle's opt-in
  prompt fires (its gate is `!CanonicalLooksRunnable()`, so a broken copy re-offers the
  install). Pinned by `AutoStartTargetTests` (`CanonicalRunnable` cases + `CopyPayload`
  FD-copies-all / single-file-copies-only-exe). Self-update caveat (dev-only): it swaps
  the single self-contained exe in place, so upgrading a framework-dependent canonical
  install leaves stale sibling DLLs the single-file exe just ignores — harmless.
  **The counterpart is `SelfInstallService.UninstallAndExit(removeUserData)` — a clean
  "Uninstall from my PC" for the self-installed copy (there is no MSI/Inno, so the app
  never appears in Windows "Add or remove programs").** In-process it removes the
  auto-start Run key (`StartupRegistrationService.Apply(enabled:false)`), the
  `wol-launcher://` deep-link scheme (`DeepLinkService.EnsureUnregistered`) and the
  Desktop + Start-Menu shortcuts (`RemoveShortcuts`, keyed to the shared `ShortcutName`
  const so it deletes exactly what `CreateShortcuts` wrote) — none of which touch the
  running exe. The install FOLDER can't be deleted while its exe runs, so a detached
  `cmd` (pure, testable `BuildDeferredDeleteScript(pid, canonicalDir, dataDir?)`: waits
  on the PID via `tasklist`, delays with `ping` NOT `timeout` — `timeout` needs a console
  stdin and fails detached — then `rmdir /s /q`, self-deletes with `del "%~f0"`) does it
  after the hard-exit. **Load-bearing invariants:** (1) it NEVER touches installed MODS
  (WoL / AoE3) — those are the user's own game folders, uninstalled separately via
  `UninstallService`; (2) `removeUserData` is an explicit CHOICE surfaced as a YesNoCancel
  confirm (`DlgLauncherSettingsUninstallConfirmBody`): Yes = uninstall + delete
  `%LocalAppData%\AoE3ModLauncher` (settings/logs/cache), No = uninstall but KEEP settings
  (the safe default, so a reinstall keeps preferences), Cancel = nothing — "keep" is the
  reason `dataDir` is nullable in the script; (3) the `DangerButton` "Uninstall from my
  PC" row in Launcher Settings → Maintenance shows ONLY when `IsInstalled()` (the exact
  counterpart of the "Install on this PC" row, which hides then) — a portable exe has no
  "installation" to remove, you just delete the file. On success it hard-exits
  (`HardExitRequested = true` so the close-to-tray intercept doesn't hide+lock the exe);
  a failed script write/launch returns false WITHOUT exiting so the UI reports it (the
  already-done Run-key/shortcut removals are a harmless partial state). Pinned by
  `AutoStartTargetTests` (`BuildDeferredDeleteScript`: waits-on-pid + self-deletes;
  null dataDir keeps user data; non-null removes both).

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
  styles). **A TextBox applies its OWN `Padding` — never bind it onto
  `PART_ContentHost` as well.** The shared template did
  (`Margin="{TemplateBinding Padding}"`), which counted it TWICE: measured on the
  chat composer, `Padding="11"` put the caret 22.4 DIP in, not 11. That doubling is
  why placeholder TextBlocks overlaid on these boxes never lined up — every call site
  was tuning a number that was silently multiplied, so the "fixes" were compensations
  for a bug, and an audit that read the template literally computed the wrong offsets
  for all five pairs. The idiom IS correct on a `ContentPresenter` (ComboBox, Button),
  which does not self-apply Padding; it is only wrong on a TextBox's content host.
  **A ~2 px residual is the platform floor** — `TextBoxView` has a small inherent
  inset that a sibling `TextBlock` does not, and every correctly-built field in the
  launcher shows exactly that much; don't chase it with a hand-tuned compensation.
  Give the placeholder the SAME `FontSize` as the box, or the text jumps the moment
  you type even with the insets right.
  **The focus ring is `TextBoxFocusBrush`, not a local `BorderBrush`.** The template's
  focus trigger targets its inner Border BY NAME, so it beats anything the call site
  sets — a field cannot opt out of the gold on its own. The multiplayer surfaces
  override that key to blue in their own resources.
  **Input theming is global — don't recolour inputs per-dialog.** A
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
   files (capturing per-file SHA-256 fingerprints during the copy — or, for a
   `DirectPayloadInstall` mod like WoL, extracting the payload straight onto the
   clone in one pass instead, with no `%TEMP%` staging) → shortcuts +
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
   **`InstallFolderDialog` also carries a low-disk-space WARNING (warn-but-allow,
   never a block), and Repair has a smaller one.** `Services/DiskSpaceService.cs`
   (pure + unit-tested, `DiskSpaceServiceTests`) holds the conservative,
   network-free estimate: `EstimateInstallRequirement(cloneBytes) = max(0,cloneBytes)
   + InstallExtraAllowanceBytes` (4 GiB fixed for payload download + extraction +
   overlay + headroom), `SafeFreeSpace(path)` (→ -1 on error, never throws),
   `IsShort(free,req)` (-1 is NEVER short — don't cry wolf when unmeasured). The
   dominant, variable cost — the AoE3 clone (~10 GB) — is measured EXACTLY via the
   new `FolderCloneService.CountCloneableBytes` (same enumeration + exclusions as
   `CountCloneableFiles`, `Sum(f=>f.Length)`); everything else is the fixed
   allowance (no HEAD/API calls). `InstallFolderDialog` measures the clone
   OFF-THREAD (`MeasureCloneSizeAsync`, cached per source, "Calculating…" transient)
   whenever the AoE3 source changes, then `UpdateDiskSpace()` compares free space on
   BOTH the destination drive (against the full estimate) and, if `%TEMP%` is on a
   different volume, the temp drive (against the payload allowance) — amber warning
   line + `_spaceWarning`; on OK, `_spaceWarning` triggers a Yes/No confirm but never
   blocks. Repair (`MainWindow.ConfirmRepairSpaceOk`) uses `RepairAllowanceBytes`
   (3 GiB — repair re-overlays only, NO clone) and is checked **right before each
   `InstallModOnlyAsync` re-download** (NOT at the top) so a "plain repair intact"
   (no download) never false-warns; declining throws `OperationCanceledException`
   (caught as a clean cancel). Strings `DiskSpace{Calculating,WarningLine,ConfirmTitle,
   ConfirmInstallBody,ConfirmRepairBody,ConfirmDownloadBody}`. Load-bearing: the estimate
   is deliberately conservative (exact clone + fixed slack), unknown free space never
   warns, and it NEVER blocks — it's a protective heads-up for low-space users.
   **EVERY download the launcher makes is now covered — seven sites — and the shared decision
   lives in `DiskSpaceService.Check(destPath, destRequired, tempPath, tempRequired)`, with the
   shared PROMPT in `Controls/DiskSpacePrompt.ConfirmOrCancel`. The reason `Check` exists as ONE
   function is the same-volume case.** When both paths resolve to the
   same volume root the two requirements are **ADDED**, not compared separately: two
   independent checks each pass on a drive with 4 GB free and 3 GB needed on each side,
   and the operation still fills the disk — which is exactly what the two hand-rolled
   callers that predate it could not get right. It returns a `DiskSpaceShortfall(Drive,
   Required, Free)` so the message can **NAME the volume** ("3 GB short on C:" is
   actionable when the user was looking at D:), or null when there's room **or when a
   volume is unmeasurable** (the `IsShort` rule — never cry wolf). Unknown on one side
   must still fall through to the other; pinned. Wired at three sites, all
   warn-but-allow through the shared `MainWindow.ConfirmLowSpace`: (1) **the WoL patch
   chain** (`ConfirmUpdateSpaceOk`, at the TOP of `ApplyAsync`) — the heaviest download
   the launcher does and it had NO check at all; it spans two volumes (the `.tar.xz`
   stage in `AppPaths.InstallTempRoot` while each patch's rollback backup is written
   INSIDE the install folder) and the requirement is **exact, not estimated** —
   `DownloadInfo.Size` is already in the manifest, temp doubled for the extraction. It
   sits in `MainWindow`, not `UpdateService`, because a service must not open dialogs.
   (2) **Repair**, which used to check the install folder — where a repair barely writes
   — and now checks `InstallTempRoot` too (`OverlayHeadroomBytes` 1 GiB on the install
   side, the full allowance on temp); with the game on D: and `%TEMP%` on a small C:
   there was previously no warning at all. (3) **The launcher self-update**
   (`LauncherUpdateDialog`, before the download) against a THIRD volume nothing else
   looks at — the one holding the running `.exe`, via the new public
   `LauncherUpdateService.GetPendingUpdatePath()`; size exact (GitHub reports it) and
   **doubled**, since the new binary sits beside the old one until the swap. (4) **The
   GitHubReleases DELTA patch** — see the paragraph below, it was a BUG not a gap. (5) **The
   Radmin MSI** (`RadminVpnService.InstallSilentAsync`) — it already read `ContentLength` to
   drive its progress bar and never compared it to anything; returning false from its
   `confirmSpace` lands on the documented "fall back to the browser download page", which is
   where someone who cancelled for lack of space should end up. (6) **Translations**
   (`TranslationApplyDialog`) — `%TEMP%` once, install TWICE (extracted into
   `translations\<id>\` AND copied over `data\`). (7) **Addons** (`HeavenDownloader`, checked at
   the `ResponseHeadersRead` point before the body is pulled) — `AppPaths.DataDir` for the
   archive + NSIS unpack, install for the extracted files + their `_originals` backups.
   **Two rules the four new sites share:** a size of 0 (no `Content-Length`, or the
   translation registry's folder path which hard-codes `Size = 0`) means unmeasurable and
   **never warns** — the same `IsShort` rule, needing no special case because `Check` already
   returns null for `required <= 0`; and declining is the USER's choice, so it must not be
   reported as a failure (the addon path needed its own `catch (OperationCanceledException)` →
   neutral `AddonCancelled`, or the generic handler painted it red).
   **The delta patch was a genuine HOLE, not a missing nicety, and the shape is worth
   remembering.** `RepairInstallAsync` calls `ConfirmRepairSpaceOk` only inside its
   `if (!deltaApplied)` branch, so a delta that SUCCEEDED downloaded, backed up and extracted
   with the space check never having run — the guard existed and the code simply walked past
   it. The fix can't live at the call site: the download is INSIDE
   `DeltaPatchService.TryPrepareAsync`, so a caller-side check could only run after the bytes
   were already written. Hence the `confirmSpace` callback, invoked once `payloadAsset` resolves
   (its `.Size` was always there and simply unused, so this costs no extra request) and BEFORE
   the download. **Declining THROWS `OperationCanceledException` rather than returning false**:
   returning false means "no usable delta" and would fall through to the FULL path, which needs
   MORE room — offering that to someone who just declined for lack of space is nonsense.
   It is also **the one requirement here that is an estimate**: the descriptor records per-file
   hashes but no size, so only the compressed figure is known, hence the documented
   `DeltaTempFactor` (3) / `DeltaInstallFactor` (2) — temp is charged more because it holds the
   zip AND the rollback backup while the install only receives the extracted result. A `size`
   field in the descriptor would make it exact. `DiskSpaceServiceTests` pins that every factor
   is **> 0**, because a 0 makes `required` zero, which `Check` reads as "nothing to check" and
   silently disables that flow's warning with a green build and no visible symptom.
   Still deliberately NOT covered: `upd_backup_<id>` (`UpdateService`) is written INSIDE the
   install folder and its size isn't measured — `ConfirmUpdateSpaceOk` approximates it with the
   compressed download size, a refinement of an existing check rather than a gap.
   **Uninstall is a blanket recursive delete** of the install folder, gated only
   by a probe/manifest check that it looks like a mod install — it ignores the
   manifest's file list and has **no per-file base-game protection**. AoE3 base
   files survive only because `IsolatedFolder` mods are a separate clone; an
   `InPlaceOverlay` mod's underlying AoE3 files *would* be deleted. (The README's
   "hard-coded base-game protection" claim is false.) The lone hard-coded
   exception is the stock-game profile: `UninstallService.Plan` refuses any
   `IsStockGame` profile outright (its "install folder" is the user's real AoE3
   install — see the `IsStockGame` gotcha).
   **The blanket delete is gated by the pure `UninstallService.ShouldRemoveOverlayOnly`, where a
   real AoE3 root VETOES the manifest — it does not merely stand in for it** (pinned by
   `UninstallSafetyTests`). It used to read `manifest != null ? !ClonedAoe3 : IsUserAoe3Root(path)`,
   so `IsUserAoe3Root` was consulted ONLY when there was no manifest — leaving the worst case
   open, because older builds stamped `clonedAoe3: true` on EVERY install (see the
   `InPlaceOverlay` gotcha), including overlays laid straight into the player's game folder.
   Anyone still carrying such a manifest lost their Age of Empires III to one Uninstall click.
   The no-manifest branch is deliberately unchanged: a launcher-made clone whose manifest went
   missing still gets a normal folder removal, which is the correct uninstall for it.
   **Uninstall ELEVATES ON DEMAND:** since
   the launcher runs `asInvoker`, deleting an install under a protected folder
   (Program Files) needs admin, so `MainWindow.UninstallMenuItem_Click` probes
   `ElevationService.CanWriteTo(InstallPath)` before it starts and, if it can't
   write, prompts (`DlgElevationRequired*`) → `RelaunchElevated()` → hard-exit, the
   SAME guard install uses (a Steam-library folder is usually already user-writable,
   so this won't fire for it). The registry-cleanup step is best-effort per hive
   (`RemoveRegistryEntries` swallows an `UnauthorizedAccessException` on HKLM), so a
   non-elevated cleanup of an old HKLM entry just logs and continues.
2. **Update** — 100% compatible with the original Java updater: fetch
   `UpdateInfo.xml`, MD5 three key files (`data/protoy.xml`, `techtreey.xml`,
   `stringtabley.xml`) to identify the installed version (falling back to the
   install manifest's baseline when the byte-faithful payload matches no UpdateInfo
   version — see the manifest-recognition gotcha), then download `.tar.xz`
   patches (resume + retry + mirror fallback), CRC32-verify, back up, and extract — then
   re-fingerprint the touched files into the manifest so the patched install stays
   verifiable.
3. **Multiplayer** — Discord sign-in (JWT cached in config) → REST/WebSocket to a
   self-hosted Node/Fastify backend (`wol-lobby.duckdns.org`) for lobbies + chat,
   gated by a mod fingerprint (`ModHashService`) → players join a shared **Radmin
   VPN** network manually for the actual LAN; the host's game launch appends
   `OverrideAddress="<radmin-ip>"` plus skip-intro flags. Match history is wired as
   a host-only match report (players + duration + mod + date + map, and — for a
   clean human 1v1 whose recording can be read — the real winner; see the
   History-subtab and result-wiring gotchas in `.claude/rules/multiplayer.md`).
   Everything the recording can't answer stays a 0.5 draw, which the History row
   renders as no badge at all rather than as "Draw". Replay UPLOAD remains
   scaffolded/not surfaced.
   **Rating is opt-in per ROOM**: only a room created with the *competitive* box
   ticked can score, and in one the launcher confirms Record Game before every
   start, holds the host in the room until the result is sent, and looks harder for
   the recording. Walking out of one after five minutes is a forfeit, decided by the
   server. Both the competitive gate and the abandonment rule have their own bullets
   in `.claude/rules/multiplayer.md` — read them before touching either; the second
   is the only rule in the project that moves rating from an absence of evidence.
   ("Host-only" above now has one narrow, documented exception: the player the room
   promotes mid-match in a competitive room, or a host could dodge by closing his
   launcher.)

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

**A `mod.json` can also be loaded straight off DISK — Settings → DEVELOPER → "Choose a
mod.json…" — so a manifest can be tried before it is published.** That tab is gated by
**`LauncherConfig.DeveloperMode`** (default false), which also holds the translation
packager and the delta-patch generator — all three already told the reader "normal users
can ignore this section", so they were costing every user a tab. **The toggle lives in
GENERAL, not in the tab it governs** (inside it, it could never be switched back on), and
`SetActiveTab` falls back to GENERAL when the tab is hidden while displayed, or the content
pane goes blank. **Developer mode gates the TOOLS, never the content: local manifests stay
merged when it is off** — a test mod can be INSTALLED, and dropping it from the listing
would orphan that install. The Workshop's header button was REMOVED (its copy promised a
different, never-implemented feature: registering an already-installed folder); the row
badge and the detail panel's "stop using this file" stay. `LauncherConfig.LocalCatalogModPaths`
stores only PATHS (`ModCatalogService.LoadLocalEntry` re-reads the file on every merge,
which is what makes "edit + Refresh catalog" show the change; caching a copy would defeat
the feature). Three rules are load-bearing: (1) **they are injected INSIDE
`ModRegistry.ApplyMerged`, never at its call sites** — it is reached from four (cache
prime, fresh cache, stale cache, cold fetch), so per-caller injection means a local mod
that appears or vanishes depending on how the refresh entered; (2) **a local entry
replaces a CATALOG entry of the same id** (you copy an existing manifest to edit it, so
seeing the published one instead would be useless) but **built-ins still win over both** —
that is the security property above, so a local `wol` is ignored and logged; (3) assets
resolve to files beside the `mod.json` through `ResolveLocalAsset`, which repeats
`ResolveAssetUrl`'s traversal guard — **being on disk does not make a manifest trusted**,
and without it a declared `../../secret.png` would be read and rendered. A manifest that
stops loading mid-session is skipped with a log line, never auto-dropped: the user is
editing it exactly when that happens. Rejections must NAME the cause
(`LocalManifestException` carries the JSON line / missing field) — learning the manifest
is wrong before opening the PR is the whole point, since the catalog CI only answers
after. Removing forgets the path and touches no file. Pinned by `LocalManifestTests`
(the rejection + traversal cases are the point); documented for modders in
`docs/MODDING.md` §1.5.

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

**Per-mod OWNERSHIP gate in the catalog CI (governance, not consumed by the
launcher).** The catalog's `classify_pr.py` now decides auto-merge by WHO opens
the PR, not just WHAT changed: each `mod.json` carries a `maintainers` array of
GitHub logins (read from the BASE manifest, so a PR can't self-authorize), plus a
repo-wide `REPO_MAINTAINERS` set. An **owner** of a mod (its maintainer or a repo
maintainer) auto-merges ANY change to THEIR folder — including `install`/`update`
download URLs (a deliberate full-autonomy trust grant); a **non-owner** editing a
mod's cosmetic/release fields is BLOCKED (`classify` exits 1 → the required check
fails), and a non-owner's critical/unknown change goes to manual review. This
closed the "anyone could change any mod's look & feel" hole. `maintainers` is a
tier-3 field (no self-granting), the workflow pins the classifier to the base ref
(a fork can't rewrite the gate), and it passes `PR_AUTHOR`. **The launcher IGNORES
`maintainers`** (`ModCatalogManifest` deserializes with S.T.Json's default
skip-unknown, so the field is safe to ship in live manifests). Keep the catalog
repo (`aoe3-mods-catalog`) and this template's `classify_pr.py`/`auto-merge.yml`/
`mod.schema.json` in sync (only `REPO_MAINTAINERS` differs — real `gorgorito12`
vs template `your-username`). Owner-fork auto-merge additionally needs the repo's
"send write tokens to fork PRs" toggle; the ownership BLOCK works regardless.

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
- **Crash capture is a global net — an unhandled exception is PERSISTED, and
  UI-thread throws are survived, not fatal.** `App.OnStartup` subscribes the three
  global hooks (`DispatcherUnhandledException`, `AppDomain.CurrentDomain.UnhandledException`,
  `TaskScheduler.UnobservedTaskException`) → each calls `DiagnosticLog.WriteCrash(source, ex)`,
  which writes a timestamped **`crash-<yyyyMMdd-HHmmss>.log`** in `AppPaths.DataDir`
  (full `ex.ToString()` + version/OS), mirrors a marker into the debug log, and
  `Flush()`es. The dispatcher (UI-thread) handler sets **`e.Handled = true`
  (log-and-survive)** — most UI-thread throws leave the app usable, so staying up
  beats a hard crash; the AppDomain handler just logs (terminal), and the task one
  `SetObserved()`s. **Why this exists:** before it, a crash left ZERO trace — no
  global handler wrote anything, `launcher-debug.log` is truncated each launch, and
  there was no crash file, so a user-reported crash was undiagnosable (a shared
  diagnostic bundle showed a clean session because the crash was in a PRIOR,
  overwritten run). Two coupled changes make a crash survive into the next bundle:
  (1) `crash-*.log` is **persistent** (not rotated; capped at the newest 5 via
  `PruneOldCrashLogs`) and (2) `DiagnosticLog.Reset()` now **rotates** the old
  `launcher-debug.log` → `launcher-debug.prev.log` (one generation) BEFORE
  truncating, so even a hard/native kill that never trips the .NET net leaves the
  crashed run's full log behind. **`ExportBundle` needs no change** — both
  `crash-*.log` and `launcher-debug.prev.log` end in `.log`, so its existing glob
  picks them up. Load-bearing: `WriteCrash` must never throw (it runs while the app
  may be dying — whole body try/caught). Specific hardening that pairs with the net:
  the game-monitor `DispatcherTimer.Tick` (`MainWindow`, every 2 s on the UI thread
  for the whole game session) and the `Process.Exited` handlers
  (`GameLauncher.LaunchAndWatch`, thread-pool) are now individually try/caught +
  the monitor disposes the `Process[]` it enumerates — the monitor tick was the
  strongest crash vector (an unguarded throw there killed the launcher WHILE the
  game ran, since the game is launched detached and survives).
- **Logging:** call `DiagnosticLog.Write(...)` (or `WriteSection`). It's a
  non-blocking queued logger that **rotates** at each launch (`Reset()` moves the
  prior `launcher-debug.log` to `launcher-debug.prev.log`, then truncates) and
  writes `launcher-debug.log`. Log messages are **always English** (they're for bug
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
  `ModPropShareDiagnostics*`. **The bundle ALSO folds in the active mod's GAME
  user-data OOS/sync artifacts — this is what makes an in-game OUT-OF-SYNC report
  diagnosable.** `ShareDiagnostics` resolves
  `UserDataService.GetUserDataFolder(profile.UserDataFolder)` (`My Games\<folder>`;
  null for the stock game / any profile with no managed user-data folder → no-op)
  and passes it as `ExportBundle`'s 3rd param `gameUserDataDir`. `ExportBundle` then
  copies that folder's **top-level** small OOS/sync/text-log files into a
  `game-userdata/` subfolder and writes a `game-userdata-listing.txt` snapshot of
  the whole top level (so we can learn the real AoE3 dump names even when nothing
  matched). The include rule is the pure, unit-tested
  `DiagnosticLog.ShouldIncludeGameFile(name, size)`: name matches `*oos*`/`*sync*`/
  `*.txt`/`*.log` (case-insensitive) AND size ≤ `GameFileMaxBytes` (2 MiB), capped at
  `GameFileMaxCount` (40) files — so recorded games (`.age3Yrec`), savegames
  (`.age3Ysav`), configs and binaries are never swept. It's **READ-ONLY** (copies to
  staging, never touches the game folder) and whole-operation best-effort (a failure
  can't abort the bundle). **Why:** a sim desync (the "OOS at 8 min" report) is
  written by the GAME, not the launcher log, and file-mismatch OOS shows "within
  seconds" (see `ModFingerprint`) — so a mid-game OOS is a simulation desync the
  3-file MP fingerprint can't catch, and these artifacts are the only in-bundle
  evidence. Recorded games (ideal for replay-diffing) stay a manual ask — too large
  to auto-bundle. Pinned by `DiagnosticLogTests`.
  **The bundle also carries `install-snapshot.txt` — an INTEGRITY summary of the
  active install (`Services/InstallSnapshot.cs`, written by
  `MainWindow.TryWriteInstallSnapshot` right before `ExportBundle`).** It exists
  because a real report — "the launcher says 1.2.0e but the game's menu shows
  1.2.0c2" — was **unclosable from a bundle**: the log only ever carried the
  `_originals` MD5, never the LIVE `data\stringtabley.xml`, which is the file the
  game actually reads for that string (`_locID='18459'` / `cStringAoMVersion`), and
  nothing recorded whether a manifest-tracked file was simply GONE. It reports:
  manifest version + counts, `KeyFileHashes` baseline **vs the live MD5s** (drift is
  what makes recognition distrust the baseline), live **vs** `_originals`
  stringtabley, the active translation, the engine files, and **how many
  manifest-tracked files are missing**. Three rules are load-bearing: (1) the file
  NAME must keep containing `snapshot` — `ExportBundle`'s `*.log`/`*snapshot*` glob
  is the only thing that stages it, so a rename silently drops it from every bundle
  (pinned in `DiagnosticLogTests`); (2) the missing-file sweep is **existence-only
  (`File.Exists`), never hashing** — a real verify re-reads multi-GB and takes
  minutes while the user waits, and a quarantined file IS a deleted file, so
  existence already catches the Defender class this is for (measured ~2.1 s over
  43k paths on a real WoL install); (3) `TryWriteInstallSnapshot` wraps the call in
  **`Task.Run`** — it runs on the UI thread and blocking on a Task whose awaits
  capture the WPF SynchronizationContext **deadlocks**; unit tests would NOT catch
  it (no context there). The config stays excluded (token) — never add it.
  `DetectCurrentVersionAsync` additionally logs the live stringtabley MD5 alongside
  the snapshot one when they differ, and `ResolveInstallPath` logs the active
  translation id/version (`ModState.ActiveTranslationId`, which the bundle otherwise
  has no way to see since the config is excluded). Pinned by `InstallSnapshotTests`.
  Separately, `MultiplayerTelemetry`
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
  **The `es` register is NEUTRAL LATIN-AMERICAN (es-419): TUTEO** (`tú`, `tienes`,
  `haz clic`) — **never voseo** (`vos`, `tenés`, `Descargá`, `Instalalo`) and
  **never peninsular** (`Pulsa`, `Ajustes`, `ordenador`, `fichero`, `rellenar`).
  Prefer `Configuración` over `Ajustes`, `PC` over `equipo` (the computer sense —
  `PublishFieldAuthorHint` legitimately means *team*), `aquí` over `acá`, `clic`
  over `click`, `Verificar`/`Consultar` over `Comprobar`. This is a real, twice-repeated
  drift: the table shipped MIXED (a large voseo minority inside a tuteo majority) until
  it was unified, because the maintainer writes Rioplatense — so a new string in voseo
  reintroduces exactly the bug that was fixed. Note the trap that made the mix invisible:
  **the tuteo majority is what a Rioplatense ear misreads as "Spanish from Spain"**, even
  though `instálalo`/`inténtalo` are standard in MX/CO/PE; the actual peninsular tells were
  only ~5 strings. **Audit with STRUCTURAL patterns, never a word list** — voseo is
  generative, so every closed list leaks (a "complete" 45-line inventory missed `Descargá`,
  `empezá`, `Mantené`, `Indicale`, `subila`, `cancelala`, `Apagalo`, `Reanudalas`…). Use
  the **Grep tool (ripgrep)**, NOT bash `grep`: under locale C, `á` is multibyte so `\b`
  **fails after an accented final vowel** (`\busá\b` never matches `usá`). Two patterns,
  eyeballed — the false positives are a closed, recognisable set:
  `\b\w+(ás|és|ís)\b` (FPs: `más`, `después`, `través`, `inglés`, `estás`/`Estás` = valid
  tuteo, and tuteo futures `podrás`/`recibirás`) and `\b\w+[áéí]\b` (FPs: `está`, `aquí`,
  `así`, `qué`, `caché`, `Sé` = tuteo of *ser*, and 3rd-person futures `será`/`aparecerá`).
  Beware: filtering `-rás$` to drop futures also **eats legitimate voseo** whose stem ends
  in `r` (`borrás`, `enterás` are present, not future). Unaccented enclitics
  (`Instalalo` → `Instálalo`) are invisible to BOTH patterns and need their own
  sweep — and they come in three separate classes, each needing its own grep, or
  you will miss one: accusative `-alo/-ala/-elo/-ela` (`Apagalo`, `subila`,
  `Reanudalas`), reflexive `-ate/-ete/-ite` (`conectate`, `unite`), and dative
  `-ale/-ele/-ile` (`Indicale` — this one hid the longest, because the first two
  sweeps don't cover it). FPs for the dative are the plural nouns
  (`portapapeles`, `temporales`, `perfiles`). Mind the tilde moving with the
  stress (`Mantené` → `Mantén`, `Desactivalo` → `Desactívalo`).
  **This includes hover TOOLTIPS — they are localized too, never hardcoded.** A
  newcomer-onboarding pass gave the interactive controls in Settings, the dashboard
  mod buttons, the gear menu / Mod Properties, and the Workshop a clear label + a
  benefit-first hint + a hover tooltip with the detail. The pattern is a local
  `static void SetTip(FrameworkElement el, string key) => el.ToolTip = Strings.Get(key);`
  helper inside each dialog's `ApplyLanguage`/`ApplyStrings` (see
  `LauncherSettingsDialog` and `ModPropertiesDialog`), with keys named `*Tip`
  (`DlgLauncherSettings*Tip`, `TipCta*`/`TipGear*` dashboard, `TipMp*` Mod
  Properties, `TipWs*` Workshop). The dashboard primary CTA tooltip is **dynamic**
  (set per-state in `SetPrimaryAction`: Play/Install/Update/Stop). Some MainWindow
  tooltips were previously **hardcoded English** ("Mod actions", "Pause/Resume",
  "Cancel") — those were migrated to localized keys; don't reintroduce a literal
  tooltip. `ModsBrowser` doesn't import the Localization layer, so its tooltips are
  fed as **properties/methods** from MainWindow (`RefreshCatalogTooltip`,
  `SetFilterTooltips(...)`, `*Tooltip` setters) — same pattern as its labels.
  Adding a new control ⇒ add its `*Tip` string + a `SetTip`/property call.
  **No emojis in labels** — the maintenance/packager button labels used to carry
  💾/📂/📦/🧩 prefixes; those were removed on request. Don't re-add emoji to labels.
  **Tooltips WRAP — the dark ToolTip style is APP-WIDE (`App.xaml`), not
  `MainWindow.Window.Resources`.** It had to move: a `ContextMenu` (the gear menu)
  renders in a SEPARATE popup that resolves implicit styles against
  `Application.Resources`, NOT a Window's — so MainWindow's old `MaxWidth` never
  reached the gear tooltips and long descriptions clipped to one line (reported
  bug). Two coupled rules: (1) **string tooltips must be wrapped by the caller** via
  `TooltipHelper.Wrap(text)` (a `TextBlock{TextWrapping=Wrap, MaxWidth=340}`) —
  `TextBlock.TextWrapping` is NOT an attached property, so it can't be forced on the
  template's `ContentPresenter`; every `SetTip` helper (Settings, Mod Properties) and
  every direct `X.ToolTip = <string>` (dashboard buttons, `ModsBrowser` setters) go
  through `Wrap`. (2) **rich-content tooltips** (`MainWindow.BuildMenuTooltip`, the
  gear menu) set `MaxWidth` + `TextWrapping=Wrap` on their OWN `TextBlock`s. The gear
  `MenuItem` style also sets `ToolTipService.Placement="Right"` so the description
  shows beside the item instead of covering the menu. Don't re-add an implicit
  `ToolTip` style to a single Window, and don't assign a raw long string to
  `.ToolTip` (use `TooltipHelper.Wrap`).
  (3) **`TooltipHelper.Wrap` pins a LOCAL `FontFamily`/`FontSize` on its TextBlock —
  load-bearing, and the ToolTip style's font setters are NOT enough on their own.** A
  WPF `ToolTip` INHERITS its font from the control it's attached to. The title-bar
  caption buttons (`TitleBarButton` in `Chrome.xaml`) use `FontFamily="Segoe MDL2
  Assets"` — an ICON font with no letter glyphs — at the tiny `GlyphSize` (8-10px), so
  a raw-string tooltip on min/max/close rendered its WORDS ("Maximizar"/"Restaurar")
  as missing-glyph boxes ("tofu"). Setting `FontFamily` on the app-wide `ToolTip`
  *style* did NOT fix it (font inheritance into the ToolTip's CONTENT is unreliable —
  the owner's icon font still leaked through); the reliable fix is a **LOCAL**
  `FontFamily`/`FontSize` on the content TextBlock (a local value beats any
  inheritance). So `Wrap` sets them (`BodyFont` / `FontSizeBody`, via
  `TryFindResource`), and `TitleBar.RefreshChrome` routes its caption-button tooltips
  through `Wrap` instead of assigning the raw string. Rule: a tooltip on a control
  that carries an icon font MUST go through `Wrap` (or bring its own TextBlock with a
  local font); don't assign a raw string there. The `ToolTip` style keeps its font
  setters as a harmless default for non-Wrapped tooltips.
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
  top-level cover), while the chat in Column 2 is untouched and fully usable.
  **It no longer relies on covering that column — `ApplyMatchPhaseUi` COLLAPSES
  `LobbyLeftColumn` for the `InGame` and `Result` phases.** Covering is not
  hiding, and the difference showed: the left column is one `*` row over three
  `Auto` rows, so a short window shrinks the star to zero and the `Auto` rows
  lay themselves out PAST the bottom of the grid — WPF does not clip — where the
  overlay, sized to the cell, is not covering anything. The Ready/Leave pair drew
  in a band under "Abort match" (reported). Collapsing leaves nothing to leak,
  through the overflow, the overlay's rounded corners, or a hairline seam, and
  stops rendering a panel nobody can see for the length of a match; the column
  also carries `ClipToBounds="True"` for the same overflow in the LOBBY phase,
  where no overlay is up. Renaming `LobbyLeftColumn` would break the hiding
  SILENTLY — pinned by a `NotNull` on it in `DialogXamlTests`. To fit the 340 px
  column its stats were relaid out from a 4-wide row into a **2×2 grid**
  (MATCH TIME / TRAFFIC over CONNECTION / ROOM) with a stacked header; all
  the `InGame*` x:Names are preserved so `RefreshInGamePanel` needs no
  change. Don't move it back out to a top-level full-content cover — that
  re-hides the chat and breaks the "chat siempre accesible" rule.
  **(4) Countdown duration is SERVER-DRIVEN (no launcher floor) — the backend's
  `LobbyRoom.COUNTDOWN_MS` is the single source of truth — and the countdown
  auto-starts when everyone is ready.** `game_countdown`'s handler OBEYS the server's
  `duration_ms` as-is (`StartCountdown(durationMs)`, no `Math.Max(10000,…)` floor — only
  StartCountdown's 500 ms sanity floor), so redeploying the backend changes the countdown
  everywhere with no launcher change. The backend `LobbyRoom.COUNTDOWN_MS` is set to
  **5000** in the SEPARATE repo `wol-launcher-lobby-node` (`src/lobbies/LobbyRoom.ts`) →
  a live room counts **5 s once that backend is redeployed** (`git pull` +
  `systemctl restart wol-lobby`); an OLD backend still sending 10000 counts 10 s (the
  launcher obeys either). The `game_countdown` missing-`duration_ms` default (10000) and
  the two offline-host fallbacks (`StartCountdown(10000)`) are LOCAL safety values only
  (the backend always sends `duration_ms`); the chat-line's XAML default `CountdownNumber`
  is "10" (a placeholder repainted on the first tick to the real remaining seconds).
  Backend + launcher stay coupled by the abort-grace window (`COUNTDOWN_MS + 60s`).
  (A launcher-side 5 s CAP was tried and reverted — the maintainer wanted the countdown
  tied to the server value, i.e. controlled from the backend, not forced by the client.)
  Bump the launcher default + XAML + backend `COUNTDOWN_MS` together if
  you change the duration.
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
  Display/ClearType/Fixed at 1.0).
  **The top of the range is a DEAD BAND — `SnapToOneAbove` (0.97) — and removing it
  re-opens a real bug.** Because the crispness recipe flips at `scale < 0.999`, the
  clamp alone put that decision two pixels away from a normal window resize: the
  Multiplayer tab is `min(W/1100, H/560)` against a window that is 1100 wide by default,
  so the WIDTH term sat at exactly 1.0 with ~2 px of slack while the height had ~50.
  Narrowing the window **three pixels** turned ClearType off for every glyph on the tab —
  reported as the text looking blurry — to buy a 0.2% shrink that fits no extra content,
  since these surfaces are star-column + scroll. Anything ≥ 0.97 now snaps flat to 1.0;
  below it the transform still does its job. Pinned by `UiScaleTests` (the
  `AHairNarrowerThanTheReferenceStaysAtOne` cases are the regression).
  **Measured and REJECTED, so don't re-propose it:** switching the formatting mode to
  `Ideal` + `Auto` hinting at fractional DPI (125/150%), on the theory that `Display`
  snaps glyph advances to a non-integer device grid. Captured both builds at the same
  1100x700 window on a 125% display and measured the text band: `Display` scored a mean
  edge gradient of **2.260** with **3.63%** partially-covered pixels against `Ideal`'s
  **2.176 / 3.74%** — i.e. Display is slightly SHARPER, not blurrier. The theory was
  plausible and wrong. **Two load-bearing rules:** (1) attach only to
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
- **Launcher-wide text legibility: the rules that came out of measuring it, and the two
  outright bugs the measurement found.** The palette itself was fine — `TextPrimary`
  14.55:1, `TextSecondary` 8.63:1, the nav tabs 6.59:1 — so nothing was repainted
  wholesale. The failures were concentrated, and the size of every string was left alone
  on the maintainer's call (no text was enlarged; nothing reflows).

  **(a) Disabled state is a COLOUR, never an `Opacity` layer — `TextDisabled` (#989898).**
  Eight styles each wrote their own `#666`/`#888`/`#aaa` AND stacked an `Opacity` of
  0.4-0.5 on the Border that contains the caption. `#666` alone is 2.26:1 on `BgNeutral`;
  with the layer it lands at **1.57:1**, i.e. you cannot read what the button you cannot
  press says — and the layer turned ClearType off for the caption as a bonus. WCAG exempts
  inactive controls, so this is not a compliance box; it is that the state was illegible.
  One brush now, 4.51 / 5.69 / 6.13:1 on the three surfaces it lands on, and it still reads
  as disabled because it is flat grey against near-white enabled text. This is the SAME
  rule the multiplayer notes already state for a BUSY control ("say it with the word, not
  with opacity"); it is now launcher-wide. The `ComboBox` was already right by accident —
  its `Opacity` falls on an empty sibling Border — and that is the shape to copy.

  **(b) `PrimaryButton` painted WHITE ON GOLD: 1.94:1, and 1.65:1 on hover.** The worst
  pairing in the launcher, on the button every dialog confirms with. The fix was already in
  the codebase: PLAY uses the same gold with `#261900` at **8.86:1**. Gold is a LIGHT fill
  and wants dark text. Also fixed: the bell popup's "·" separator was painted with
  `MpDivider`, a BORDER brush, used as a foreground — **1.16:1**, invisible.

  **(c) The halo-sibling pattern now covers PLAY and the toasts.** An `Effect` on an
  ancestor of text disables ClearType for every glyph underneath (the rule written next to
  the multiplayer room cards). `SidebarPrimaryButton` hung a permanent `DropShadowEffect`
  on the Border wrapping the **PLAY** caption — the largest button in the app, its text
  composited all the time — and `AppToast` did the same over every toast's title and body.
  Both now put the glow on a sibling underlay (`halo` / `shell`) and keep the content
  Border clean. When you retarget this, move the `IsPressed` and `IsEnabled=False` Effect
  setters to the halo too, or the triggers put the Effect straight back on the text.

  **(d) The hero's two `TextBlock.Effect`s became a scrim, and the first attempt at that
  scrim left a visible seam.** The 48px title and the 14px description each carried their
  own `DropShadowEffect`. That shadow was NOT decoration — the copy sits over whatever hero
  art a mod ships — so it was replaced, not dropped: a `Rectangle` behind the copy block,
  sized off it by binding and blown outward with a `ScaleTransform` (no layout maths, and a
  transform on a Rectangle costs nothing because there is no text under it). Measured on a
  real capture, contrast went **11.98 -> 13.35:1** and the right half of the image is
  untouched (mean luminance delta 0.0).
  **The lesson worth keeping: a `RadialGradientBrush` whose transparent stop lies OUTSIDE
  its own bounds ends on a hard cut.** The first version used Center 0.42 / RadiusX 0.72,
  so the fill was still ~30% opaque at the Rectangle's right edge and drew a soft vertical
  seam across the sky. Centre 0.5 + radius 0.5 puts the zero stop exactly on the edge
  midpoints; the scrim then has to be about twice the block for its core to still cover the
  text. Verified by scanning for the largest column-to-column luminance jump in a strip of
  sky: identical (5.88 at x=563) before and after, i.e. no discontinuity introduced.
  **The sharpness half of this only pays MAXIMIZED, and that is worth knowing before
  re-litigating it:** at the default 1100x700 the hero subtree is already under
  `SetTextCrispForScale`'s sub-1.0 branch, so ClearType is off there for an unrelated
  reason and removing the Effect changed the fringing not at all (17.8 -> 16.5). At scale
  1.0 the same paragraph measures **121.5**. So the Effect removal buys contrast at every
  size and sharpness only at 1.0.

  **(e) THE BUG WORTH REMEMBERING: `ClearValue` restores a STYLE setter, and there was no
  style setter — so "Ready for operations" rendered BLACK ON BLACK at 1.05:1.**
  `RefreshIdleProgressPanel` reset the dashboard progress strip with
  `DashboardProgressLabel.ClearValue(TextBlock.ForegroundProperty)`, with a comment saying
  it "falls back to the style setter". Their gold is a LOCAL value written on the element in
  `MainWindow.xaml`, and this app has no implicit `TextBlock` style — so `ClearValue`
  removed the only thing painting them and let `Foreground` fall back to WPF's default,
  **Black**, on the darkest corner of the hero. The icon and label are now ASSIGNED
  `AccentBrush` instead; the ProgressBar keeps its `ClearValue` because its gradient really
  does come from `ShimmerProgressBar`. Contrast **1.05 -> 10.30:1**.
  **How it hid for so long, and how to catch the next one:** the strip renders, the string
  is right, UI Automation reports the element with its text and a normal bounding box, and
  nothing throws — the text is simply the same colour as what is behind it. It was found by
  measuring a screenshot, and confirmed by temporarily painting the label pure red and
  finding **zero red pixels anywhere in the window**, which is what proved the local value
  was gone rather than merely dark. Before using `ClearValue` to "reset" a brush, check
  whether the value you expect to reappear is a style setter or the XAML attribute you just
  erased.

  **(f) How this was measured, and where the metric is invalid.** Contrast is computed from
  real screen captures (sRGB linearisation, `(L1+0.05)/(L2+0.05)`), never eyeballed, and
  never with `PrintWindow` — it draws onto an opaque DC and cannot be used to judge
  antialiasing. ClearType leaves colour fringes on glyph edges and greyscale AA does not, so
  the mean `|R-B|` over edge pixels is a usable proxy for "is ClearType on here". **It is
  only valid for GREY text on a GREY background:** over the hero photo, or on the gold
  title, R and B differ by the colours themselves and the number means nothing (the gold
  title scores 103 with the Effect still attached). The capture harness gates on
  `WindowFromPoint` at the window centre returning OUR pid before it saves anything — an
  earlier run without that gate captured an unrelated window that happened to be on top.

  **(g) The transparent popups are UNCHANGED, and here is the number the decision needs.**
  `AllowsTransparency="True"` makes a window layered and WPF turns ClearType off for all of
  its content. That covers the bell popup, the brand and mod-switch popups, every `ComboBox`
  dropdown and the tooltips. Measured, same grey-on-dark text family, same capture: inside
  the bell popup the mean edge `|R-B|` is **20.8-32.7**; in the ordinary window it is
  **70.1-103.1** — about a 3x difference, so they really are losing it. It was NOT changed,
  because the transparency is what buys the rounded corners and the two-tone rim, and
  dropping it squares them off. That is a look decision, not a correctness one, so it is
  the maintainer's to make. All of that popup text passes AA as it stands (12.34 / 6.64 /
  5.70 / 5.70:1).

  **Left alone on purpose:** enlarging any text; the ~21 hardcoded sub-13px `FontSize`
  literals (mostly multiplayer, each with a token holding the identical value — tidiness,
  not legibility); the gear `ContextMenu`, which lives inside the `Collapsed`
  `LegacyPlayContent` and is unreachable; and `WarnBrush`, a brush key that does not exist
  and always falls through to a literal (`ModPropertiesDialog.xaml.cs:896`) — a real bug,
  but not a contrast one. **Also spotted and not fixed:** `DashboardProgressLabel` shows
  the ENGLISH "Ready for operations" while the rest of the strip is Spanish, so the idle
  label is populated before the language is applied and never refreshed.

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
