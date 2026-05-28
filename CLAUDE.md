# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A native Windows desktop launcher (WPF, .NET 8) for *Age of Empires III*
total-conversion mods — currently Wars of Liberty and the Improvement Mod. It
replaces the legacy Java updater + Inno Setup installer with a single
self-contained `.exe` that installs, updates, verifies, and launches the mods,
plus a multiplayer matchmaking tab.

The repo also contains `aoe3-mods-catalog-template/` (a template for the
separate community mod-catalog GitHub repo, with `mod.schema.json` and PR
auto-merge Actions) and `docs/MODDING.md` (mod-authoring guide).

## Platform constraint (read first)

This is a **Windows-only** project: `net8.0-windows` + `UseWPF`. It **cannot be
built or run on Linux/macOS** — `dotnet build` fails off-Windows. The cloud
execution environment for this session is Linux, so you generally **cannot
compile, run, or smoke-test here**; reason about the code statically and rely
on the user to build/run on Windows. Say so explicitly rather than claiming a
change is verified.

## Build & run

All commands run from `WarsOfLibertyLauncher/`.

| Goal | Command |
| --- | --- |
| Dev build (framework-dependent, needs .NET 8 runtime) | `dotnet build -c Release` |
| Release single-file `.exe` (publish + sign + print SHA-256) | `.\build-release.ps1` (PowerShell, Windows-only) |
| Manual publish | `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish` |

- Dev build output: `bin/Release/net8.0-windows/Aoe3ModLauncher.exe`.
- Release output: `publish/Aoe3ModLauncher.exe` (~120 MB, self-contained). The
  `publish/` folder is git-ignored — release binaries go to GitHub Releases.
- `--update-now` is a launch argument that auto-resumes the update flow elevated
  (used after a UAC relaunch).
- Two publish scripts exist: `WarsOfLibertyLauncher/build-release.ps1` is the
  canonical one (cleans, publishes, signs, prints SHA-256). The root
  `publish.ps1` is an older alternative that also supports `-Tag` to create a
  `vX.Y.Z` git tag; its header comment claims the output is
  `WarsOfLibertyLauncher.exe`, but `<AssemblyName>` makes it
  `Aoe3ModLauncher.exe` — the comment is stale.

### No automated tests

There is no test project and no test runner. Verification is a manual smoke
test on Windows. (`CONTRIBUTING.md` links a `docs/ROADMAP.md#smoke-test` that no
longer exists — don't go looking for it.)

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
  network membership by tailing Radmin's own
  `%PROGRAMDATA%\Famatech\Radmin VPN\service.log` (English, tab-delimited,
  stable across Radmin VPN 2.x) for the `UPDATE\tYou joined/left network 'X'`
  events — that's how `RadminAssistantService.ProbeAsync` promotes its overlay
  checklist from `LoggedIn` → `InAoE3Network`. An ICMP ping to a known seed
  peer is the fallback signal when the log isn't readable (deleted, ACL'd,
  sandboxed account) (`Services/RadminVpnService.cs`,
  `RadminAssistantService.cs`, `RadminLogService.cs`). The launcher is
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
  Several code paths assume `AppContext.BaseDirectory` is the `.exe`'s own
  folder (where `launcher-config.json` and the debug log live). Turning that
  flag on would point it at a `%TEMP%` extract dir and lose user sessions. See
  the long comment in `WarsOfLibertyLauncher.csproj`.

- **Code signing is automatic but optional.** MSBuild `AfterBuild`/`AfterPublish`
  targets Authenticode-sign the binary with a self-signed `CN=Gorgorito` cert
  (thumbprint in `<SignCertThumbprint>`). They are Windows-only and silently
  no-op if the cert isn't present, so builds still succeed without it.

- **`third_party/**` and `native/**` are excluded from compile** in the
  `.csproj`. Those dirs don't currently exist; the excludes are defensive guards
  against duplicate-attribute build errors if vendored native code is re-added.

- **Two installer stacks coexist.** `NativeInstallService` is the live path
  (download multi-part ZIP → clone → flatten → overlay). `InstallerService` +
  `InstallProgressMonitor` are a legacy Inno-Setup flow (run a setup `.exe`
  silently). Confirm which one the UI actually calls before editing either.

- **`NativeInstallService.RemoveStaleBuildArtifacts` (WoL only) deletes shipped
  payload files** (`.xml.xmb`, `.bak`, stray `.rar`, …) after install *and* every
  update. It's a deliberate multiplayer LAN-hash-parity step, not cleanup — those
  deletions are load-bearing.

- **`LauncherConfig` is per-mod.** Real state lives in a `mods` dictionary of
  `ModState` keyed by mod id and selected by `activeModId`; the flat
  `modInstallPath` / `gameExecutable` / `activeTranslationId` fields are LEGACY,
  migrated into `mods[...]` on `Load()` (which also rewrites a retired
  `multiplayer.lobbyBaseUrl` and clears the session token). The README's flat
  config example is out of date. `Save()` is non-atomic and runs from background
  threads.

## Architecture

WPF MVVM-lite single project. UI is thin; the **`Services/` layer is the
engine** and the UI binds to it.

- **`MainWindow` + `Controls/`** — the shell and tabs (`MainTabs`, `StatusCard`,
  `ProgressPanel`, `ActionPanel`, `ModsBrowser`, `MultiplayerTab`, `HeroBanner`).
  Top-level `*Dialog.xaml` files are the modals (install, uninstall, self-update,
  user-data backup/restore, translations, Discord sign-in, create-lobby, etc.).
- **`Models/`** — plain schema/DTO types: `LauncherConfig` (`launcher-config.json`,
  lives next to the `.exe`), `UpdateInfo` (`UpdateInfo.xml` schema),
  `InstallManifest` (`install-manifest.json`, drives uninstall), `ModProfile` /
  catalog types, and `Models/Multiplayer/` wire types.
- **`Services/`** — install pipeline (`NativeInstallService`, `InstallerService`,
  `FolderCloneService`), update orchestration (`UpdateService`,
  `UpdateInfoService`, `ArchiveService`, `DownloadService`), detection
  (`AoE3Detector`, `Aoe3DetectorService`, `RegistryService`), hashing
  (`HashService` = MD5 + CRC32 + SHA-256), self-update (`LauncherUpdateService`),
  Radmin VPN assist (`RadminVpnService` = registry + NIC probe,
  `RadminLogService` = `service.log` parser for network membership,
  `RadminAssistantService` = stage classifier the overlay binds to),
  uninstall, user data, translations, mod catalog, elevation, game launch.
  `Services/Multiplayer/` is the lobby client (`MultiplayerSession`,
  `LobbyApiClient`, `LobbyWebSocket`, `ModHashService`, `ReplayUploadService`).
- **`Styles/`** + `Localization/Strings.cs` — dark-only "dorado imperial" theme;
  all UI strings are EN/ES (diagnostic logs stay English on purpose).

### Three core flows (detailed diagrams in README.md)

1. **Install** — detect AoE3 → download multi-part payload ZIP → clone AoE3 into
   a standalone mod folder → flatten Steam-layout `bin\` into root → overlay mod
   files → shortcuts + uninstall registry entries + `install-manifest.json`.
   **Uninstall is a blanket recursive delete** of the install folder, gated only
   by a probe/manifest check that it looks like a mod install — it ignores the
   manifest's file list and has **no per-file base-game protection**. AoE3 base
   files survive only because `IsolatedFolder` mods are a separate clone; an
   `InPlaceOverlay` mod's underlying AoE3 files *would* be deleted. (The README's
   "hard-coded base-game protection" claim is false.)
2. **Update** — 100% compatible with the original Java updater: fetch
   `UpdateInfo.xml`, MD5 three key files (`data/protoy.xml`, `techtreey.xml`,
   `stringtabley.xml`) to identify the installed version, then download `.tar.xz`
   patches (resume + mirror fallback), CRC32-verify, back up, and extract.
3. **Multiplayer** — Discord sign-in (JWT cached in config) → REST/WebSocket to a
   self-hosted Node/Fastify backend (`wol-lobby.duckdns.org`) for lobbies + chat,
   gated by a mod fingerprint (`ModHashService`) → players join a shared **Radmin
   VPN** network manually for the actual LAN; the host's game launch appends
   `+OverrideAddress <radmin-ip>` plus skip-intro flags. Match-history/ELO and
   replay upload are scaffolded but not wired.

### Multi-mod profile system

Each mod is a `ModProfile` (branding, paths, payload URLs, update server).
`ModRegistry` holds built-in profiles (`_builtIn`) and merges in community mods
fetched from a remote catalog repo (`RefreshFromCatalogAsync`). **Do not add
community mods to `ModRegistry._builtIn`** — they go to the catalog repo (the
in-app "Publish my mod" wizard opens a PR there).

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

- **Files next to the `.exe`** resolve via `AppContext.BaseDirectory` (this is
  why the single-file publish keeps content unextracted — see gotchas).
  `LauncherConfig.Load()`/`Save()` is the only config accessor
  (`launcher-config.json`); the debug log and XML snapshots land in the same
  folder.
- **Logging:** call `DiagnosticLog.Write(...)` (or `WriteSection`). It's a
  non-blocking queued logger that resets at each launch and writes
  `launcher-debug.log`. Log messages are **always English** (they're for bug
  reports), even though the UI is localized. Separately, `MultiplayerTelemetry`
  appends a plaintext `multiplayer-events.log` next to the `.exe` — its opt-out
  isn't wired, so it always writes.
- **Localization is mandatory for user-facing strings.** Add every UI string to
  the `Table` in `Localization/Strings.cs` with both `en` and `es` entries, and
  read it via `Strings.Get(key)` / `Strings.Format(key, args)` — never inline a
  literal in XAML/code. A missing key renders as the key itself (a visible
  signal). `Strings.SetLanguage` raises `LanguageChanged` for live refresh.
- **WPF threading:** long-running work (download/install/check) is `async` and
  reports progress via `IProgress`/events; marshal UI updates back to the
  dispatcher. Periodic UI work uses `DispatcherTimer`.

## Conventions

- File-scoped namespaces, `Nullable` enabled, `ImplicitUsings` enabled.
- Doc-comments explain **why** code exists, not what it does. Don't add comments
  that restate the code.
- **DCO sign-off is required on every commit** — `git commit -s` (adds
  `Signed-off-by:`). PRs with unsigned commits get bounced.
- One topic per PR; keep refactors out of feature PRs. Apache-2.0 licensed.
