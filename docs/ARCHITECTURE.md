# Architecture & internals

How the launcher is put together and how its three core flows work. For the
`mod.json` / catalog spec see [`MODDING.md`](MODDING.md); for the config schema see
[`CONFIGURATION.md`](CONFIGURATION.md); for the build/release pipeline see
[`BUILDING.md`](BUILDING.md). The high-level feature list lives in the
[README](../README.md).

## How install works

```
┌─────────────────────────────────────────────────────────────┐
│  1. Detect AoE3 install (registry / disk scan / manual)    │
├─────────────────────────────────────────────────────────────┤
│  2. Show install dialog with detected AoE3 + dest folder   │
│     User can change either path manually before continuing │
├─────────────────────────────────────────────────────────────┤
│  3. Permission check on destination → UAC prompt if needed │
├─────────────────────────────────────────────────────────────┤
│  4. Download mod payload ZIP (multi-part, GBs total)       │
│     Concatenate into single ZIP, extract to temp           │
├─────────────────────────────────────────────────────────────┤
│  5. Clone AoE3 → destination (skips destination if it      │
│     lives inside source, to avoid recursion)               │
├─────────────────────────────────────────────────────────────┤
│  6. Flatten `bin\` to root (Steam layout) and remove the   │
│     redundant `bin\` subfolder afterwards (~3.7 GB saved)  │
├─────────────────────────────────────────────────────────────┤
│  7. Overlay mod files on top of cloned AoE3                │
├─────────────────────────────────────────────────────────────┤
│  8. Create Start Menu + Desktop shortcuts                  │
├─────────────────────────────────────────────────────────────┤
│  9. Write uninstall registry entries (HKLM if admin,       │
│     HKCU otherwise) and `<mod>-manifest.json`              │
├─────────────────────────────────────────────────────────────┤
│ 10. Verify install (required dirs + .bar archive sizes)    │
├─────────────────────────────────────────────────────────────┤
│ 11. If existing user data detected → show backup alert     │
└─────────────────────────────────────────────────────────────┘
```

## How update works

```
┌─────────────────────────────────────────────────────────────┐
│  1. Detect mod install (config / registry / disk scan)     │
├─────────────────────────────────────────────────────────────┤
│  2. Download UpdateInfo.xml from the mod's update server   │
│     (with mirror fallback)                                 │
├─────────────────────────────────────────────────────────────┤
│  3. MD5 three key files:                                   │
│       data\protoy.xml                                      │
│       data\techtreey.xml                                   │
│       data\stringtabley.xml                                │
│     Compare with XML table to identify installed version   │
├─────────────────────────────────────────────────────────────┤
│  4. Determine pending patches (everything from             │
│     `minreqdownload` upwards)                              │
├─────────────────────────────────────────────────────────────┤
│  5. For each patch:                                        │
│       a. Download .tar.xz with resume + URL fallback       │
│       b. Verify CRC32                                      │
│       c. Back up files about to be overwritten             │
│       d. Extract over the install                          │
│       e. Apply file-deletion list                          │
│       f. Open post-update page if URL is provided          │
└─────────────────────────────────────────────────────────────┘
```

## How multiplayer works

```
┌─────────────────────────────────────────────────────────────┐
│  1. User signs in with Discord (device-flow-shaped).        │
│     The backend issues a JWT; launcher caches it in         │
│     launcher-config.json                                    │
├─────────────────────────────────────────────────────────────┤
│  2. Launcher fingerprints the active mod (SHA-256 of        │
│     protoy.xml / techtreey.xml / stringtabley.xml)          │
├─────────────────────────────────────────────────────────────┤
│  3. Host:   POST /lobbies  (title + mod hash + max)         │
│     Joiner: GET /lobbies -> list -> POST .../join           │
│     Backend rejects join if local hash != room hash         │
├─────────────────────────────────────────────────────────────┤
│  4. Both ends open a WebSocket to the room -> receive       │
│     room_state, chat, member_joined, member_ready,          │
│     game_started frames (auto-reconnect on hiccup)          │
├─────────────────────────────────────────────────────────────┤
│  5. Both players join the SAME Radmin VPN network by        │
│     hand. The launcher detects/installs/launches            │
│     Radmin and copies the network name -- it cannot         │
│     join for you. AoE3's stock LAN discovery then           │
│     finds peers on the 26.x LAN.                            │
├─────────────────────────────────────────────────────────────┤
│  6. Host presses Start -> game_started broadcast ->         │
│     every client launches age3y.exe with skip-intro         │
│     flags + OverrideAddress="<host-radmin-ip>".             │
│     Players click Multiplayer -> LAN once; the host         │
│     shows up as a normal LAN game.                          │
├─────────────────────────────────────────────────────────────┤
│  7. In-game overlay tracks match time + Radmin adapter      │
│     traffic + a REAL per-player ping (each client reports   │
│     its Radmin IP, peers ICMP-ping each other). The host    │
│     can kick a member; if the host leaves, the room         │
│     migrates to the next joiner. For ~60 s after launch     │
│     any member can abort a bad start for everyone. (Match   │
│     history / ELO + replay upload are scaffolded, not yet   │
│     wired.)                                                  │
└─────────────────────────────────────────────────────────────┘
```

## Multiplayer internals

A built-in **Multiplayer** tab turns the launcher into a Voobly-style matchmaking
client. The launcher is the *meta layer* (sign-in, lobbies, chat, mod-hash gating);
**Radmin VPN** carries the actual game traffic.

- **Discord OAuth sign-in** via a device-flow-shaped API — the launcher opens the
  approval URL in the browser; no embedded webview, no one-time code to type. The
  session token (JWT) lives in `launcher-config.json` and is refreshed silently when
  it nears expiry.
- **Self-hosted lobby backend** — Node.js + Fastify on an Oracle Cloud VM, fronted by
  DuckDNS + Let's Encrypt at `wol-lobby.duckdns.org` (sources in the companion
  `wol-launcher-lobby-node` repo). Serves the rooms list, room state, in-room chat, and
  a process-wide global chat. It is **not** a Cloudflare Worker — configs that still
  point at the old Worker URL are auto-healed on load.
- **Real-time room state** over WebSocket — chat / ready / member-join / game-started
  frames stream from the room, plus `host_changed` (migration), `member_net` (per-peer
  Radmin IP) and `kicked`, with auto-reconnect so a brief network hiccup doesn't drop
  the player from the lobby. The lobby opens in its **own independent window** with its
  own Windows taskbar button.
- **Host migration (GameRanger-style)** — if the host leaves (or crashes), the room is
  **not** torn down: it passes to the next member by join order, then the next, until
  nobody is left (only then does it close). The new host can start / cancel / kick like
  the original. Migration is backend-authoritative and skips "ghost" members whose
  socket already died, so it never hands the room to a player who isn't really there.
- **Per-player ping in-game** — the in-game overlay shows a **real** round-trip time to
  each peer (green / amber / red), not a placeholder. Each client reports its Radmin VPN
  IP over the lobby WebSocket at match launch; the server broadcasts it (`member_net`)
  and every client ICMP-pings the others, so you can spot who's lagging.
- **Match abort window** — for the first **~60 seconds** after launch, **any** member
  (not just the host) can abort the match for everyone — the safety valve for a bad /
  desynced start. After the window closes the match is left alone (a host who is merely
  losing can't kill everyone's game).
- **Kick** — the host can expel a member from the room (with a confirmation prompt).
  It's a **simple kick**: the player can re-join later; there's no ban list. Closing the
  kicked player's socket reuses the normal leave path, so every other roster updates
  itself.
- **Global chat** — a process-wide WebSocket channel (`/global/ws`) shown beside the
  rooms list: a presence count, message history, and server-side anti-spam (per-minute
  cap + slow-mode + auto-timeout on repeated strikes).
- **Radmin VPN transport** — game traffic rides the community's existing Radmin VPN LAN
  (the `26.0.0.0/8` range; AoE3's stock LAN discovery finds peers on it). The launcher
  only **assists**: detect / install / launch the Radmin GUI and copy the network name
  to the clipboard for manual paste — it **cannot join a network programmatically**. It
  detects whether you're already in a network by parsing Radmin's `service.log` (plus
  its rotated `service (N).log` backups), with an ICMP ping to a seed peer as the
  fallback signal.
- **Mod fingerprint matching** — every room carries the SHA-256 of three AoE3 critical
  files (`protoy.xml`, `techtreey.xml`, `stringtabley.xml`) computed locally at create /
  join time. Joins with a mismatching hash are rejected (`mod_mismatch`) so no one ever
  joins a game they can't actually play. Two stock-game players on the same game version
  match the same way and can share a lobby.
- **Auto skip-intro on launch** — when the host starts the match, AoE3 is spawned with
  `OverrideAddress="<host-radmin-ip>"` plus skip-intro flags, so players reach the menu
  quickly, then click Multiplayer → LAN once and the game is there.
- **Match history / ELO and replay upload are scaffolded but not yet wired** — the
  backend client methods (`ReportMatchAsync`, replay `UploadAsync`) and endpoints exist,
  but nothing in the UI calls them yet.

## Project structure

Repo-level layout:

```
Updater/
├── WarsOfLibertyLauncher/         The launcher (WPF, net8.0-windows) — see below
├── WarsOfLibertyLauncher.Tests/   xUnit tests for pure logic (sibling project)
├── docs/                          MODDING / ARCHITECTURE / CONFIGURATION / BUILDING
├── aoe3-mods-catalog-template/    Template for the separate community catalog repo
├── aoe3-translations-template/    Template for the community translations repo
├── README.md  CONTRIBUTING.md  DISCLAIMER.md  PRIVACY.md  CODE_SIGNING_POLICY.md  CLAUDE.md  LICENSE
```

The launcher project (`Aoe3ModLauncher.exe` ships as the assembly name; every
namespace is `WarsOfLibertyLauncher`):

```
WarsOfLibertyLauncher/
├── MainWindow.xaml(.cs)              Main shell (title bar, nav tabs, dashboard hero)
├── App.xaml(.cs)                     WPF entry point (global HiDPI / chrome setup)
├── build-release.ps1                 Release build script (publish + sign + hash)
├── INSTALL.md                        End-user install / SmartScreen guide
│
├── InstallFolderDialog.xaml(.cs)     Install destination + AoE3 source picker
├── UninstallDialog.xaml(.cs)         Uninstall confirmation + options
├── LauncherUpdateDialog.xaml(.cs)    Self-update prompt + "what's new"
├── UserDataAlertDialog.xaml(.cs)     Documents user-data backup prompt
├── UserDataRestoreDialog.xaml(.cs)   Restore previously backed-up user data
├── TranslationApplyDialog.xaml(.cs)  Apply a community translation
├── TranslationPackagerDialog.xaml(.cs) Build a translation .zip from a folder
├── CreateLobbyDialog.xaml(.cs)       Create-a-room modal (Multiplayer)
├── GitHubLoginDialog.xaml(.cs)       Multiplayer sign-in dialog (Discord OAuth;
│                                     filename kept for git-blame continuity)
├── PasswordPromptDialog.xaml(.cs)    Password prompt for joining private rooms
├── PublishModDialog.xaml(.cs)        Publish a mod profile to the catalog repo
├── LauncherSettingsDialog.xaml(.cs)  Launcher settings (non-modal, sidebar tabs)
├── ModPropertiesDialog.xaml(.cs)     Per-mod properties / maintenance (non-modal)
├── LobbyWindow.xaml(.cs)             Independent lobby room window (own taskbar btn)
├── RadminAssistantWindow.xaml(.cs)   Radmin VPN connect-steps assistant overlay
│
├── Controls/
│   ├── MultiplayerTab.xaml(.cs)      Multiplayer UI (rooms browser + global chat)
│   ├── ModsBrowser.xaml(.cs)         Workshop: catalog of installable mods
│   ├── StatusCard.xaml(.cs)          Mod status card
│   ├── ProgressPanel.xaml(.cs)       Download / install progress
│   ├── ActionPanel.xaml(.cs)         Action buttons + gear menu
│   ├── HeroBanner.xaml(.cs)          Dashboard hero banner
│   ├── MainTabs.xaml(.cs)            Right-pane tabs (News / Changelog / Help)
│   ├── MpAlertOverlay.cs             Themed in-window alert cards (not MessageBox)
│   ├── TitleBar.xaml.cs              Shared title-bar component (template in Chrome.xaml)
│   ├── ChromePopups.cs               Single-open coordinator for hand-built popups
│   └── UiScale.cs                    Window-size zoom (ScaleTransform) helper
│
├── Styles/
│   ├── Tokens.xaml                   Spacing / radius / disc geometry tokens
│   ├── Colors.xaml                   Dark "dorado imperial" palette (incl. Mp*)
│   ├── Buttons.xaml                  Button styles (implicit + Dialog/Primary/Mp*)
│   ├── Inputs.xaml                   Retemplated ComboBox / TextBox / CheckBox
│   └── Chrome.xaml                   Window-chrome / title-bar styling
│
├── Localization/
│   └── Strings.cs                    EN/ES string table
│
├── Models/
│   ├── UpdateInfo.cs                 UpdateInfo.xml schema
│   ├── LauncherConfig.cs             launcher-config.json schema (per-mod state)
│   ├── InstallManifest.cs           <mod>-manifest.json (uninstall tracking)
│   ├── ModProfile.cs                 Mod profile model (branding/paths/update)
│   ├── ModCatalogManifest.cs         Remote catalog mod.json wire type
│   ├── ModCatalogCache.cs            On-disk cache of the fetched catalog
│   ├── GitHubReleasesSettings.cs     GitHubReleases update-mechanism config
│   ├── NewsItem.cs                   News-feed item
│   ├── TranslationPack.cs            Community translation descriptor
│   └── Multiplayer/
│       └── LobbyDtos.cs              Lobby REST DTOs / WebSocket frame types
│
└── Services/
    ├── AppPaths.cs                   %LocalAppData%\AoE3ModLauncher\ path resolver
    ├── ConnectivityState.cs          Observed offline/online signal (offline mode)
    ├── HashService.cs                MD5 + CRC32 + SHA-256
    ├── RegistryService.cs            Detect mods via registry
    ├── AoE3Detector.cs               Disk scan for AoE3 (Steam/GOG/retail)
    ├── ModInstallProbe.cs            Content-based install detection (probe+marker)
    ├── DownloadService.cs            HTTP with resume + URL fallback
    ├── GitHubReleaseDownloader.cs    Resolve + download a GitHubReleases asset
    ├── SpeedTracker.cs               Download-speed measurement
    ├── UpdateInfoService.cs          Parse UpdateInfo.xml
    ├── ArchiveService.cs             .tar.xz extraction + local delete-list
    ├── UpdateService.cs              Update flow orchestrator
    ├── VerifyService.cs              Per-file integrity verify (manifest hashes)
    ├── NativeInstallService.cs       Full install pipeline (live path)
    ├── DetachedProcessLauncher.cs    Launch the game re-parented (out of our tree)
    ├── InstallerService.cs           Legacy Inno-Setup install orchestrator (vestigial)
    ├── FolderCloneService.cs         AoE3 → mod clone (+ pre-flight count)
    ├── InstallBaseGameMissingException.cs  Clone-count integrity gate
    ├── UninstallService.cs           Manifest-tracked removal (refuses stock game)
    ├── UserDataService.cs            Documents\My Games\<Mod>\ backup/restore
    ├── ModRegistry.cs                Built-in profiles + catalog merge
    ├── ModCatalogService.cs          Fetch / parse the remote catalog repo
    ├── ModAssetCacheService.cs       Cache catalog icons/banners locally
    ├── IconConverter.cs              PNG → .ico writer for shortcuts
    ├── NewsService.cs                Fetch the catalog news.json feed
    ├── NotificationCenter.cs         Bell history store (dedup + persistence)
    ├── NotificationFeedService.cs    Central wol-notify feed client (ETag/304)
    ├── TranslationService.cs         Apply / remove community translations
    ├── TranslationRegistryService.cs Discover translations on GitHub
    ├── LauncherUpdateService.cs      GitHub Releases self-update (verified swap)
    ├── ElevationService.cs           UAC / admin-rights helpers
    ├── StartupRegistrationService.cs Run-at-login registry key
    ├── GameLauncher.cs               Launch age3y.exe with the right args
    ├── RadminVpnService.cs           Radmin detect / install / launch + NIC probe
    ├── RadminLogService.cs           Parse service.log (+ rotated backups)
    ├── RadminAssistantService.cs     Stage classifier the overlay binds to
    ├── DiagnosticLog.cs              launcher-debug.log writer
    │
    └── Multiplayer/
        ├── MultiplayerSession.cs     Top-level state: auth + rooms + lobby socket
        ├── LobbyApiClient.cs         REST client for the Node/Fastify backend
        ├── LobbyWebSocket.cs         WebSocket client w/ auto-reconnect
        ├── ModHashService.cs         SHA-256 fingerprint of the 3 critical files
        ├── ReplayUploadService.cs    Find + upload .age3yrec (scaffolded)
        └── MultiplayerTelemetry.cs   multiplayer-events.log writer
```
