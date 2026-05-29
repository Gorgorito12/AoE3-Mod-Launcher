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
  feature.

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
  reach for a local `Foreground`.

- **The cinema dashboard hero scales with the window — via one transform, not
  per-element font sizes.** The PlayView "Layer 4" Grid (title + description +
  version chip + action row + progress strip) is wrapped in a `ScaleTransform`
  named `HeroScaleTransform` and driven by `UpdateHeroScale()` in code-behind.
  The whole block scales as a unit so proportions stay fixed: **1:1 ("current
  size") when the window is maximized, shrinking down to a floor of
  `HeroMinScale` (0.65)** as the window gets smaller. The scale is
  *(PlayView content footprint) ÷ (maximized footprint)*, where the maximized
  footprint is `SystemParameters.WorkArea` **minus 96 px** of chrome — that 96
  is the title bar (`Grid.Row=0`, 40) + nav strip (`Grid.Row=1`, 56); **if you
  change those RowDefinition heights, update the `- 96` in `UpdateHeroScale`
  too** (they're coupled by hand, not computed). Three deliberate, non-obvious
  choices: (1) it's a **`RenderTransform` (not `LayoutTransform`) with
  `RenderTransformOrigin="0,1"`** so the block shrinks in place pinned to its
  bottom-left corner without reflowing or nudging the gradient/background layers
  behind it; (2) it's hooked to **`PlayView.SizeChanged`, not the window's** —
  switching tabs collapses PlayView to a 0-size (guarded no-op) and switching
  back grows it from 0 to its real size, a size change that recomputes the scale
  even though the window never resized (the window's own `SizeChanged` would
  miss that); (3) it caps at 1.0 so it never grows *past* the maximized look on
  huge monitors. Don't add per-XAML `FontSize` scaling, a `Viewbox`, or DPI
  tweaks to make the hero responsive — route everything through
  `HeroScaleTransform`. **Text crispness is coupled to the global HiDPI setup:**
  `App.OnStartup` renders all text in `TextFormattingMode=Display` (pixel-
  snapped) + `Fixed` hinting + `ClearType` — razor-sharp at 1:1 but **blurry
  once the scale transform shrinks the glyphs** (they're rasterised for the
  pre-transform pixel grid, then squashed). So `UpdateHeroScale` flips the named
  `HeroContentGrid` subtree to `Ideal` formatting + `Animated` hinting +
  `Grayscale` rendering whenever scale < 1.0 (WPF's mode for text under a
  transform), and restores the `Display`/`ClearType`/`Fixed` trio at exactly 1.0
  so the maximized hero stays pixel-crisp. Don't hard-set static `TextOptions`
  on the hero subtree — the toggle owns them. (Reference is the **primary**
  monitor's work area, so on a multi-monitor setup with mismatched resolutions a
  window maximized on a *secondary* monitor won't land exactly at 1.0 — a known,
  minor cosmetic edge case.)

- **`LauncherConfig` is per-mod.** Real state lives in a `mods` dictionary of
  `ModState` keyed by mod id and selected by `activeModId`; the flat
  `modInstallPath` / `gameExecutable` / `activeTranslationId` fields are LEGACY,
  migrated into `mods[...]` on `Load()` (which also rewrites a retired
  `multiplayer.lobbyBaseUrl` and clears the session token). The README's flat
  config example is out of date. `Save()` is non-atomic and runs from background
  threads.

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
  `+OverrideAddress` exactly like the mods. Like WoL, the entry is mirrored in
  the catalog repo (`mods/aoe3-tad/mod.json`) for the public listing, but the
  built-in **shadows** it at runtime (built-in wins on id collision).

- **Mod icons come from two different places — don't assume the catalog.**
  Community mods and the stock game (`aoe3-tad`) get their icon from the
  catalog repo (`mods/<id>/icon.png` → `ModProfile.IconUrl` →
  `ModAssetCacheService` caches it under
  `%LocalAppData%\AoE3ModLauncher\mod-assets\` → `LocalIconPath`). The
  first-party **WoL built-in is different**: it ships `WoL.ico` **embedded in
  the `.exe`** (a `<Resource>` in the `.csproj`) and references it via
  `BannerImage` (a `pack://` URI), NOT `IconUrl`. So WoL needs no catalog
  upload, and dropping an `icon.png` into the catalog's `mods/wol/` is
  **ignored** — the built-in shadows the catalog entry (same id-collision rule
  as above) and sets `BannerImage` in code. Resolution order is
  `LocalIconPath` (if the cached file exists) → `BannerImage` (packed) → null =
  letter monogram, via `ResolveModIcon` (MainWindow) and `ResolveIconUri`
  (ModsBrowser); `TryLoadBitmap` / `TryLoadTileImage` accept **both** on-disk
  paths and `pack://` URIs. The resolved icon is painted on the dashboard hero
  (`DashboardIconHost`), the Workshop tiles / rows / detail header, the Mod
  Properties header (`HeaderIconHost`), the Create-room mod card, and the
  install shortcut. To move WoL's icon to the catalog like the others, set
  `IconUrl` on the WoL built-in in `ModRegistry` — editing only the catalog
  `mod.json` won't do it.

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

### Three core flows (detailed diagrams in README.md)

1. **Install** — detect AoE3 → download multi-part payload ZIP → clone AoE3 into
   a standalone mod folder → flatten Steam-layout `bin\` into root → overlay mod
   files → shortcuts + uninstall registry entries + `install-manifest.json`.
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
- **Maximize-respects-taskbar is set globally — don't roll your own per-Window.**
  The same `App.OnStartup` class handler that wires HiDPI crispness also
  installs a `WM_GETMINMAXINFO` WndProc hook on every Window whose
  `WindowStyle="None"`. Currently that's six Windows: `MainWindow`,
  `LauncherSettingsDialog`, `ModPropertiesDialog`, `RadminAssistantWindow`,
  `LobbyWindow` (all `ResizeMode="CanResize"`, so they actually use the
  fix when the user maximises), plus `CreateLobbyDialog` (`ResizeMode="NoResize"`,
  so the hook attaches but never fires — listed for completeness so
  future contributors know the inventory). Without the hook, maximising
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
- **Settings / Properties / Lobby dialogs share a non-modal + resizable
  + single-instance pattern.** `ModPropertiesDialog`, `LauncherSettingsDialog`
  and `LobbyWindow` all pair a custom dark `WindowChrome`
  (`WindowStyle="None"` + 30–40 px caption + 6 px `ResizeBorderThickness` for
  edge-drag, recipe replicated as the `DialogCloseButton` local style).
  Title-bar buttons diverge: `LauncherSettingsDialog` and
  `ModPropertiesDialog` show only the close ✕ (they're settings sheets —
  minimise/maximise would be unusual there), while `LobbyWindow` adds the
  full minimise / maximise / close trio via the `TitleBarChromeButton`
  style (neutral hover) + `DialogCloseButton` (red hover) — matching what
  a user expects from a regular OS window since the lobby is a
  long-running interactive surface that warrants alt-tab + maximise. The
  maximise glyph swaps via `OnStateChanged` in code-behind (Segoe MDL2
  `0xE922` ↔ `0xE923`); the App.OnStartup WM_GETMINMAXINFO hook handles
  the maximise-respects-taskbar bound. `LobbyWindow` also uses a smaller
  30 px caption + tighter padding because the rich room info (room code,
  HOST / PLAYERS / ROOM ID stats) lives in its own sub-header strip below
  the chrome; the two settings dialogs keep the taller 40 px caption since
  their title is the only thing in the chrome row. The two settings dialogs add a 200-px left rail of
  `SidebarNavButton` buttons (from `Styles/Buttons.xaml`) and a
  `SetActiveTab(button)` helper that toggles `Tag="active"` on the chosen
  button while flipping `Visibility` on the matching content `StackPanel`;
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
  they're open. That has three implications: (1) **never set `DialogResult`
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
