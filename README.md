# AoE3 Mod Launcher

A modern, native Windows launcher for **Age of Empires III** total-conversion
mods — currently shipping with profiles for **Wars of Liberty** and the
**Improvement Mod**.

Replaces the legacy Java updater and the Inno Setup installer with a single
self-contained `.exe`. Detects existing installations, downloads missing files
from the server, applies patches, and clones Age of Empires III into a
standalone mod folder so the mod runs side-by-side with the base game.

Also ships a **built-in multiplayer** tab (v1.0) — GitHub-authed lobbies on a
Cloudflare Worker, P2P hole-punching, and a WinDivert-backed virtual LAN that
brings Voobly/GameRanger-style matchmaking back to AoE3 mods without asking
players to install a VPN.

> **100% compatible** with each mod's original update server — speaks the same
> `UpdateInfo.xml` format and processes the same `.tar.xz` patches as the Java
> updater, but without the Java runtime requirement.

---

## Features

### Install / Update / Verify
- **Native install pipeline** — downloads multi-part ZIPs, clones AoE3, applies
  the mod overlay, creates shortcuts and registry entries. Works for Steam,
  GOG, and retail layouts.
- **Resumable downloads** — HTTP Range requests, 30-minute per-file timeout,
  primary URL + SourceForge fallback. No more "timeout" errors on slow
  connections like the Java updater.
- **CRC32 verification** of every patch before applying.
- **Backup before overwrite** — patches first move existing files to a backup
  folder, so a failed update doesn't leave the install broken.
- **Verify & Repair** — scans the install for missing/corrupt files and offers
  to re-download.
- **Robust uninstall** — manifest-tracked removal with hard-coded protection
  against deleting AoE3 base game files (`age3y.exe`, `proto.xml`,
  `techtree.xml`, etc.).

### Multi-mod support
- Profile-based architecture — each mod (WoL, Improvement Mod, future ones)
  ships its own profile with branding, paths, payload URLs, and update server.
- Mod selector switches the active profile on the fly; the rest of the UI
  re-skins itself to match.

### Path detection
- 3-level fallback for finding existing mod installs:
  1. Saved path from `launcher-config.json`
  2. Windows registry (Inno Setup uninstall key)
  3. Disk scan across common drives (C:, D:, E:) for `age3y.exe`
- Equivalent fallback for AoE3: registry → walk up from mod folder → scan
  for Steam, GOG, and Microsoft Games install paths.
- Manual override via the Settings menu when auto-detection fails.

### Settings menu (gear icon)
The ⚙ button on the bottom-left opens a context menu organised into sections:
- **PATHS** — manage where the launcher looks for the mod and AoE3.
- **USER DATA** — open `Documents\My Games\<Mod>\`, create on-demand backup,
  restore from backup.
- **LANGUAGE** — game-language switcher (when the mod ships translations).
- **MAINTENANCE** — check for launcher updates, repair install, verify files.
- **ADVANCED** — view diagnostic logs.
- **DANGER** — uninstall the mod (with a styled confirmation dialog).

All paths are validated before saving (`age3y.exe` for AoE3, mod-specific
markers for the mod folder).

### User-data safety
After a fresh install, if the launcher detects existing user data under
`Documents\My Games\<Mod>\` from a previous (potentially newer) install, it
shows an alert offering to:
- **Back up and continue** — renames the folder to
  `<Mod>.bak.<timestamp>` so the freshly installed base can start clean.
- **Open folder** — launches Explorer to inspect the data manually.
- **Ignore** — proceeds at the user's own risk.

The launcher **never deletes** user data — the worst it does is rename a folder.

### Community translations
- Optional language packs published as GitHub releases, picked up
  automatically by the launcher.
- Apply a translation with one click; the launcher backs up the originals
  before overwriting and offers a "restore originals" button to undo.
- A built-in **Translation Packager** dialog helps translators build their
  own `.zip` from a folder of translated files.

### Self-update
The launcher checks GitHub Releases for a newer version on startup. Updates are
**tag-based** (no need to bump assembly versions to publish a release) — the
launcher saves the GitHub release tag it was installed from, and prompts when
a different tag appears upstream.

The user can dismiss an update with "Later"; the launcher remembers that tag
and won't re-prompt until a different one is published.

### Multiplayer (v1.0)
A built-in **Multiplayer** tab that turns the launcher into a Voobly-style
matchmaking client. End-to-end flow: sign in with GitHub → create or join a
room on a Cloudflare-hosted lobby Worker → P2P mesh hole-punches between
peers → a WinDivert-backed virtual LAN bridges AoE3's LAN broadcasts so the
game sees other players as real LAN hosts. No third-party VPN required.

- **GitHub OAuth sign-in** via Device Flow — no redirect URI, no popup
  browser embedding. The launcher prints a one-time code; users approve it
  at `github.com/login/device`. The session token (JWT, 7-day TTL) lives in
  `launcher-config.json` and gets refreshed silently when it nears expiry.
- **Lobby Worker** (Cloudflare Workers + Hono + D1 + KV + R2 + Durable
  Objects with hibernatable WebSockets). Hosts the rooms list, room state,
  in-room chat, replay uploads (R2), and Glicko-2 ELO ratings (D1). Sources
  live in the companion `wol-launcher-lobby-worker` repo.
- **Real-time room state** over WebSocket — chat / ready / member-join /
  game-started frames stream straight from the room's Durable Object.
  Auto-reconnect with exponential backoff so a brief network hiccup
  doesn't drop the player from the lobby.
- **P2P hole-punching** with STUN (RFC 5389) for NAT discovery + UDP
  simultaneous-send with magic prefixes (`WOLp` punch / `WOLg` game / `WOLk`
  keepalive). Symmetric-NAT users fall back to a Worker-relayed game
  channel (`game_relay`) so they're not locked out.
- **Virtual LAN over WinDivert** — captures AoE3's UDP broadcasts on the
  game's LAN-discovery range (2200–2500), forwards the payload over the P2P
  mesh, and re-injects each peer's packets locally with a stable
  `10.147.x.y` source address derived from a hash of their user id. AoE3
  sees a small set of distinct LAN hosts — same trick Voobly used, no VPN
  adapter to install.
- **Auto skip-intro + IP spoofing** for the game launch — when the host
  starts the match, AoE3 is spawned with the real (binary-verified) flags
  `+noIntroCinematics +disableESOProfile +dontDetectNAT +OverrideAddress
  <virtual-ip> +OverridePort 2300 +hostPort 2300`. Player sees the main
  menu in ~5 s instead of waiting through intro + ESO login, then clicks
  Multiplayer → LAN once and the room is there.
- **Mod fingerprint matching** — every room carries the SHA-256 of three
  AoE3 critical files (`protoy.xml`, `techtreey.xml`, `stringtabley.xml`)
  computed locally at create / join time. Joins with a mismatching hash
  are rejected by the Worker with `mod_mismatch` so no one ever joins a
  game they can't actually play.
- **Privacy toggles** — `relayOnly` mode never announces the user's
  public STUN endpoint, routing all game traffic through the Worker
  instead (~50 ms extra latency in exchange for not leaking the IP);
  the virtual IP shown to peers is always `10.147.x.y`, never the real
  one. The redesigned room view is built around blue accents with red
  reserved for destructive states (Leave room, errors, disconnects),
  and connection status (Connected / Reconnecting / Offline) lives in
  a header pill instead of leaking into the chat.

### Localization
English and Spanish, switchable from the top-right corner. All UI strings
(buttons, dialogs, status messages) are localized. Diagnostic logs stay in
English for bug reports.

### Privilege handling
The launcher runs un-elevated by default. When it needs to write to a
protected location (e.g. `C:\Program Files (x86)\...`) it prompts for UAC
elevation and relaunches itself with admin rights. Update flow can be
auto-resumed elevated via the `--update-now` argument.

### Code signing
Release builds are Authenticode-signed so Windows shows a publisher name
instead of "Unknown publisher" in SmartScreen. The current cert is a
self-signed `CN=Gorgorito` — see [INSTALL.md](WarsOfLibertyLauncher/INSTALL.md)
for what users will see and how to handle Smart App Control.

---

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
│  1. User signs in (GitHub Device Flow). Worker issues a    │
│     JWT; launcher caches it in launcher-config.json        │
├─────────────────────────────────────────────────────────────┤
│  2. Launcher fingerprints the active mod (SHA-256 of       │
│     protoy.xml / techtreey.xml / stringtabley.xml)         │
├─────────────────────────────────────────────────────────────┤
│  3. Host: POST /lobbies with title + mod hash + maxPlayers │
│     Joiner: GET /lobbies → list → POST /lobbies/<id>/join  │
│     Worker rejects join if local hash != lobby hash        │
├─────────────────────────────────────────────────────────────┤
│  4. Both ends open a WebSocket to the lobby's Durable      │
│     Object — receive room_state, chat, member_joined,      │
│     member_ready, game_started frames                      │
├─────────────────────────────────────────────────────────────┤
│  5. PeerMesh starts: STUN probe → broadcast public         │
│     endpoint via peer_announce → simultaneous-send hole    │
│     punch with WOLp magic prefix. Falls back to game_relay │
│     (Worker UDP-tunnel) on symmetric NAT                   │
├─────────────────────────────────────────────────────────────┤
│  6. VirtualLanService opens WinDivert handles. Capture     │
│     filter: udp 2200-2500 + broadcast/multicast. Each      │
│     captured AoE3 packet is forwarded to every peer over   │
│     the mesh; each incoming peer packet is re-injected     │
│     with a 10.147.x.y source IP (FNV-1a hash of user id)   │
├─────────────────────────────────────────────────────────────┤
│  7. Host presses Start → game_started broadcast → every    │
│     peer launches age3y.exe with                           │
│     +noIntroCinematics +disableESOProfile +dontDetectNAT   │
│     +OverrideAddress <virtual-ip> +OverridePort 2300       │
│     +hostPort 2300. Players click Multiplayer → LAN once;  │
│     AoE3 sees the host's broadcast as a normal LAN game    │
├─────────────────────────────────────────────────────────────┤
│  8. On game exit: launcher detects the freshest .age3yrec  │
│     under Documents\My Games\<Mod>\Savegame and offers it  │
│     for upload to R2 (linked to the match in D1)           │
└─────────────────────────────────────────────────────────────┘
```

---

## Building

Requires **.NET 8 SDK** on Windows.

### Quick build (development)

```powershell
cd WarsOfLibertyLauncher
dotnet build -c Release
```

Output: `bin\Release\net8.0-windows\Aoe3ModLauncher.exe` (framework-dependent,
needs .NET 8 runtime on the machine that runs it).

### Single-file portable .exe (recommended for distribution)

Use the included PowerShell script — it cleans previous output, runs `dotnet
publish` with the right flags, signs the binary with the local code-signing
cert, and prints the path / size / SHA-256 / signature status:

```powershell
cd WarsOfLibertyLauncher
.\build-release.ps1
```

Output: `WarsOfLibertyLauncher\publish\Aoe3ModLauncher.exe` (~74 MB, fully
self-contained — no .NET install required on the target machine).

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

### Manual publish (without the script)

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

---

## Distributing a release

1. Run `.\build-release.ps1` and copy the SHA-256 hash it prints.
2. Create a new release on GitHub.
3. Attach `publish\Aoe3ModLauncher.exe` as a release asset.
4. Paste the SHA-256 in the release notes so users can verify the download.
5. Link to [`INSTALL.md`](WarsOfLibertyLauncher/INSTALL.md) (or copy its
   content) so users know what to do if SmartScreen / Smart App Control
   blocks the binary on first launch.
6. (Optional) Submit the `.exe` to
   [Microsoft Defender Sample Submission](https://www.microsoft.com/en-us/wdsi/filesubmission)
   — Microsoft analysis improves Smart App Control reputation in 1–3 days.

---

## Configuration

A `launcher-config.json` file is created next to the `.exe` on first run.
Most fields auto-populate; edit only when you need to override defaults
(custom server URLs, non-standard install paths, alternate payload mirrors).

```json
{
  "updateInfoUrl": "http://aoe3wol.com/updates/UpdateInfo.xml",
  "updateInfoUrlAlt": "http://master.dl.sourceforge.net/project/wars-of-liberty/Patches/UpdateInfo.xml",
  "modInstallPath": "",
  "gameExecutable": "",
  "gameArguments": "",
  "startWithWindows": false,
  "closeLauncherOnGameStart": false,
  "minimizeToTray": false,
  "showToastNotifications": true,
  "checkUpdatesOnStartup": true,
  "openPostUpdatePages": true,
  "language": "en",
  "theme": "dark",
  "newsUrl": "https://raw.githubusercontent.com/Gorgorito12/aoe3-mods-catalog/main/news.json",
  "payloadZipUrls": [
    "https://github.com/papillo12/Updater/releases/download/updater/WolPayload.zip.001",
    "https://github.com/papillo12/Updater/releases/download/updater/WolPayload.zip.002",
    "https://github.com/papillo12/Updater/releases/download/updater/WolPayload.zip.003"
  ],
  "defaultInstallFolder": "C:\\Program Files (x86)\\Wars of Liberty",
  "officialWebsite": "http://aoe3wol.com/",
  "lastInstalledLauncherTag": "",
  "skippedLauncherTag": "",
  "translationsRepo": "papillo12/translations",
  "modsCatalogRepo": "",
  "multiplayer": {
    "lobbyBaseUrl": "https://wol-launcher-lobby.workers.dev",
    "sessionToken": "",
    "sessionExpiresAt": 0,
    "cachedUser": null,
    "virtualAdapterEnabled": false,
    "relayOnly": false
  }
}
```

The `multiplayer.lobbyBaseUrl` points at the Cloudflare Worker deployment.
For local Worker development the launcher can point at a `cloudflared`
quick tunnel URL (`https://*.trycloudflare.com`) so the desktop client and
the Worker can talk without deploying to production.

`relayOnly` forces all game traffic through the Worker (~50 ms extra
latency) so peers never see the user's public STUN endpoint.
`virtualAdapterEnabled` is reserved for the optional Microsoft Loopback
Adapter path; the default WinDivert path doesn't need it.

---

## Project structure

```
WarsOfLibertyLauncher/
├── MainWindow.xaml(.cs)              Main UI (sidebar, mod selector, status, progress)
├── App.xaml(.cs)                     WPF app entry point
├── build-release.ps1                 Release build script (publish + sign)
├── INSTALL.md                        End-user install / SmartScreen guide
│
├── Aoe3PickerDialog.xaml(.cs)        Pick AoE3 install when multiple are detected
├── InstallFolderDialog.xaml(.cs)     Install destination + AoE3 picker
├── UninstallDialog.xaml(.cs)         Uninstall confirmation + options
├── LauncherUpdateDialog.xaml(.cs)    Self-update prompt
├── UserDataAlertDialog.xaml(.cs)     Documents user-data backup prompt
├── UserDataRestoreDialog.xaml(.cs)   Restore previously backed-up user data
├── RestoreBackupDialog.xaml(.cs)     Restore from a patch-time backup
├── TranslationApplyDialog.xaml(.cs)  Apply a community translation
├── TranslationPackagerDialog.xaml(.cs) Build a translation .zip from a folder
├── CreateLobbyDialog.xaml(.cs)       Create-a-room modal (Multiplayer)
├── GitHubLoginDialog.xaml(.cs)       GitHub Device Flow sign-in dialog
├── PasswordPromptDialog.xaml(.cs)    Password prompt for joining private rooms
├── PublishModDialog.xaml(.cs)        Publish a mod profile to the catalog repo
├── LauncherSettingsDialog.xaml(.cs)  Settings menu (paths / language / advanced)
│
├── Controls/
│   ├── MultiplayerTab.xaml(.cs)      Full multiplayer UI (rooms list + room popup)
│   ├── ModsBrowser.xaml(.cs)         Catalog of installable mods
│   ├── StatusCard.xaml(.cs)          Mod status card on the Play tab
│   ├── ProgressPanel.xaml(.cs)       Download / install progress
│   └── ActionPanel.xaml(.cs)         Right-rail action buttons
│
├── Styles/
│   ├── Colors.xaml                   Dark theme palette (incl. Mp* multiplayer colors)
│   ├── Colors.Light.xaml             Light theme overrides
│   ├── Buttons.xaml                  Shared button styles (Dialog/Primary/Mp*)
│   └── Chrome.xaml                   Window-chrome / title-bar styling
│
├── Localization/
│   └── Strings.cs                    EN/ES string table
│
├── Models/
│   ├── UpdateInfo.cs                 UpdateInfo.xml schema
│   ├── LauncherConfig.cs             launcher-config.json schema
│   ├── InstallManifest.cs            <mod>-manifest.json (uninstall tracking)
│   └── Multiplayer/
│       └── LobbyDtos.cs              Wire types for the lobby Worker (DTOs / WS frames)
│
└── Services/
    ├── HashService.cs                MD5 + CRC32
    ├── RegistryService.cs            Detect mods via registry
    ├── AoE3Detector.cs               Disk scan for AoE3 (Steam/GOG/retail)
    ├── Aoe3DetectorService.cs        Higher-level AoE3 detection facade
    ├── DownloadService.cs            HTTP with resume + URL fallback
    ├── SpeedTracker.cs               Download-speed measurement
    ├── UpdateInfoService.cs          Parse UpdateInfo.xml
    ├── ArchiveService.cs             .tar.xz extraction with backup
    ├── UpdateService.cs              Update flow orchestrator
    ├── NativeInstallService.cs       Full install pipeline
    ├── InstallerService.cs           Install orchestrator
    ├── InstallProgressMonitor.cs     Progress reporting during install
    ├── FolderCloneService.cs         AoE3 → mod clone with progress
    ├── UninstallService.cs           Manifest-tracked removal
    ├── UserDataService.cs            Documents\My Games\<Mod>\
    ├── ModRegistry.cs                Mod profile catalogue
    ├── TranslationService.cs         Apply / remove community translations
    ├── TranslationRegistryService.cs Discover translations on GitHub
    ├── LauncherUpdateService.cs      GitHub Releases self-update
    ├── ElevationService.cs           UAC / admin-rights helpers
    ├── GameLauncher.cs               Launch age3y.exe with the right args
    ├── DiagnosticLog.cs              launcher-debug.log writer
    │
    └── Multiplayer/
        ├── MultiplayerSession.cs     Top-level state: auth + rooms + mesh + vlan
        ├── LobbyApiClient.cs         REST client for the Cloudflare Worker
        ├── LobbyWebSocket.cs         Hibernatable WS client w/ auto-reconnect
        ├── ModHashService.cs         SHA-256 fingerprint of the 3 critical files
        ├── NatTypeDetector.cs        STUN-based NAT classification
        ├── ReplayUploadService.cs    Find + upload .age3yrec to R2
        ├── DirectPlayService.cs      DirectPlay capability probe (legacy LAN)
        ├── VirtualAdapterService.cs  Optional Microsoft Loopback Adapter helper
        └── P2P/
            ├── PeerMesh.cs           UDP hole-punching + per-peer channel mgmt
            ├── VirtualLanService.cs  WinDivert capture / inject bridge
            ├── WinDivertNative.cs    WinDivert P/Invoke surface
            └── PacketRewriter.cs     IPv4 / UDP packet builder for inject path
```

---

## Roadmap

Done in v1.0:

- **Built-in multiplayer** — GitHub OAuth sign-in, Cloudflare Worker
  matchmaking, P2P hole-punching, WinDivert virtual LAN, Voobly-style
  IP spoofing. Replaced the old "use Radmin VPN" plan with a no-VPN
  flow that doesn't ask players to install a third-party adapter.
- **News panel** — markdown news feed pulled from the mod catalog repo.

Next up:

- **Auto-host / auto-join via UI automation** — AoE3 has no CLI flag to
  jump straight into "Host" or "Join" from the LAN screen (verified
  against the `age3y.exe` strings). A SendInput-based menu driver would
  bring the room-to-game experience down to zero clicks once the host
  presses Start.
- **More mod profiles** — extend the multi-mod system to other AoE3
  total-conversion mods.
- **Friends + ELO ladder UI** — the Worker already serves Glicko-2
  ratings; the in-launcher views (Friends tab, Profile ELO graph,
  global ladder) are still placeholders in v1.0.
- **Replay browser** — surface uploaded `.age3yrec` files in the
  History tab with one-click download / re-watch.

---

## License

Apache License 2.0 — see [`LICENSE`](LICENSE).

Apache 2.0 was chosen over MIT for the explicit patent grant from
contributors. Pull requests are accepted under the same license via a DCO
sign-off — see [`CONTRIBUTING.md`](CONTRIBUTING.md).

The mods themselves are the work of their respective teams (Wars of Liberty,
Improvement Mod, …) — this launcher is an unofficial alternative client and
not affiliated with them. *Age of Empires III* is a trademark of Microsoft
Corporation; see [`DISCLAIMER.md`](DISCLAIMER.md) for trademark and
third-party-component notes.
