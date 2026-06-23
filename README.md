# AoE3 Mod Launcher

A modern, native Windows launcher for **Age of Empires III** total-conversion
mods — currently shipping with profiles for **Wars of Liberty** and the
**Improvement Mod**.

Replaces the legacy Java updater and the Inno Setup installer with a single
self-contained `.exe`. Detects existing installations, downloads missing files
from the server, applies patches, and clones Age of Empires III into a
standalone mod folder so the mod runs side-by-side with the base game.

Also ships a **built-in multiplayer** tab — Discord-authed lobbies and chat on
a self-hosted Node/Fastify backend, plus an in-app assistant for **Radmin VPN**
(the user-managed virtual LAN the AoE3 mod community already uses), bringing
Voobly/GameRanger-style matchmaking back to AoE3 mods.

It also exposes the **stock, unmodded Age of Empires III: The Asian Dynasties**
as a built-in **detect-only** entry (`aoe3-tad`): the launcher locates an
existing install and runs it — single-player plus the same Radmin multiplayer
the mods use — but **never installs, updates, or uninstalls the base game**
(that's your own legally-owned copy).

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
- **Uninstall** — a recursive delete of the mod's install folder, gated by a
  probe/manifest check that the target really is a mod install. AoE3's base
  files stay safe because an `IsolatedFolder` mod (the default) is a separate
  *clone*, not your original game. The detect-only stock game (`aoe3-tad`) is
  hard-refused by `UninstallService.Plan` — its "install folder" is your real
  AoE3, so it can never be wiped.

### Multi-mod support
- Profile-based architecture — each mod (WoL, Improvement Mod, future ones)
  ships its own profile with branding, paths, payload URLs, and update server.
  Built-in profiles are merged with community mods fetched from a remote
  catalog repo (the Workshop tab); built-in entries win on id collisions.
- Mod selector switches the active profile on the fly; the rest of the UI
  re-skins itself to match.

### Mod presentation (icons, hero, gallery)
- Each mod brings its own art from the catalog: icon, Workshop banner, a
  full-bleed **dashboard hero**, and a screenshot/GIF gallery.
- **Rotating hero** — a mod can ship several hero images (`heroImages`); the
  dashboard cycles through them with an automatic crossfade.
- **High-res art** — images are validated by aspect ratio + a width range (not a
  fixed size), so mod art up to **4K** is supported (see `docs/MODDING.md` for the
  specs). Heroes/screenshots decode size-capped so 4K stays light on memory.
- The unmodded **Age of Empires III: The Asian Dynasties** appears as a
  built-in `aoe3-tad` profile. The launcher only **detects and launches** it —
  single-player, or the same Radmin multiplayer the mods use. It never
  installs, updates, or uninstalls the base game (every install / uninstall /
  repair / verify path is hard-guarded against `IsStockGame`), because that's
  your own legally-owned copy.

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
- Optional language packs the launcher discovers automatically. The recommended
  way to publish is by **committing files to a folder** in a translations repo —
  `translations/<id>/<version>/` on `main` — no GitHub release and no separate
  upload. (Legacy packs published as **GitHub releases** still work too; the
  launcher reads **both** sources.)
- **Version history**: each language keeps its versions as subfolders, so the
  Properties → Language tab shows a **version picker** — a player can roll back to
  an older translation (e.g. one that matches their installed mod version). The
  launcher flags incompatible versions but lets you apply them anyway.
- Apply a translation with one click; the launcher backs up the originals
  before overwriting and offers a "restore originals" button to undo.
- A built-in **Translation Packager** (Settings → Packager) builds a ready
  `translations/<id>/<version>/` folder from a translator's files — they just
  commit it. A content-hash baked into each pack means an improved version
  re-notifies players automatically, with no version/tag bookkeeping.

### Notification feed
The launcher's "update available" / "new translation" bells are fed by a small
**central notification feed** instead of every client polling GitHub per installed
mod. A tiny self-hosted Node + Fastify service polls GitHub **once for everyone**
and publishes a JSON manifest (each mod's latest version + translation keys) that
the launcher reads with a single cheap `ETag` / `304` request. The launcher still
does the version/translation **diff and dedup locally** — the feed only moves the
fetch off each client.

- **Deployed & live** at `https://wol-notify.duckdns.org/manifest` on its **own**
  Oracle Cloud VM, **separate** from the lobby backend, fronted by DuckDNS +
  Let's Encrypt. Sources and the full deploy runbook are in the companion
  **`notifier-server`** repo (`github.com/Gorgorito12/notifier-server`).
- **Never a single point of failure** — if the feed is down, unreachable, or
  returns bad JSON, the launcher automatically falls back to polling GitHub
  directly, exactly as before. The feed URL is configurable
  (`notificationFeedUrl` in `launcher-config.json`; `"none"` opts out), defaulting
  to the built-in endpoint above.

### Self-update
The launcher checks GitHub Releases for a newer version on startup. Updates are
**tag-based** (no need to bump assembly versions to publish a release) — the
launcher saves the GitHub release tag it was installed from, and prompts when
a strictly-newer SemVer tag appears upstream. A manually-downloaded build that
never self-updated in-app is recognised via its stamped AssemblyVersion, so it
won't offer to "update" to the version it already is.

The download is **verified before it's used**: the new `.exe` must match the
release's published **SHA-256** (from the asset digest or a `SHA256:` line in
the notes) and carry an **Authenticode signature from the same signer** as the
running binary; a failed check deletes the download and aborts. The swap itself
is **atomic with rollback** (rename current → `.old`, move new → current,
restore on failure), so a partial write never leaves you without an executable.
Checks use a conditional `ETag` / `304 Not Modified` request to spare GitHub's
unauthenticated rate limit.

The user can dismiss an update with "Later"; the launcher remembers that tag
and won't re-prompt until a different one is published.

### Multiplayer
A built-in **Multiplayer** tab that turns the launcher into a Voobly-style
matchmaking client. End-to-end flow: sign in with Discord → create or join a
room on the self-hosted lobby backend → both players join a shared **Radmin
VPN** network → the host starts the match and every client launches AoE3
pointed at the host over the Radmin LAN. The launcher is the *meta layer*
(sign-in, lobbies, chat, mod-hash gating); Radmin VPN carries the actual game
traffic.

- **Discord OAuth sign-in** via a device-flow-shaped API — the launcher opens
  the approval URL in the browser; no embedded webview, no one-time code to
  type. The session token (JWT) lives in `launcher-config.json` and is
  refreshed silently when it nears expiry.
- **Self-hosted lobby backend** — Node.js + Fastify on an Oracle Cloud VM,
  fronted by DuckDNS + Let's Encrypt at `wol-lobby.duckdns.org` (sources in the
  companion `wol-launcher-lobby-node` repo). Serves the rooms list, room state,
  in-room chat, and a process-wide global chat. It is **not** a Cloudflare
  Worker — configs that still point at the old Worker URL are auto-healed on
  load.
- **Real-time room state** over WebSocket — chat / ready / member-join /
  game-started frames stream from the room, plus `host_changed` (migration),
  `member_net` (per-peer Radmin IP) and `kicked`, with auto-reconnect so a brief
  network hiccup doesn't drop the player from the lobby. The lobby opens in its
  **own independent window** with its own Windows taskbar button.
- **Host migration (GameRanger-style)** — if the host leaves (or crashes), the
  room is **not** torn down: it passes to the next member by join order, then the
  next, until nobody is left (only then does it close). The new host can start /
  cancel / kick like the original. Migration is backend-authoritative and skips
  "ghost" members whose socket already died, so it never hands the room to a
  player who isn't really there.
- **Per-player ping in-game** — the in-game overlay shows a **real** round-trip
  time to each peer (green / amber / red), not a placeholder. Each client reports
  its Radmin VPN IP over the lobby WebSocket at match launch; the server
  broadcasts it (`member_net`) and every client ICMP-pings the others, so you can
  spot who's lagging.
- **Match abort window** — for the first **~60 seconds** after launch, **any**
  member (not just the host) can abort the match for everyone — the safety valve
  for a bad / desynced start. After the window closes the match is left alone
  (a host who is merely losing can't kill everyone's game).
- **Kick** — the host can expel a member from the room (with a confirmation
  prompt). It's a **simple kick**: the player can re-join later; there's no ban
  list. Closing the kicked player's socket reuses the normal leave path, so every
  other roster updates itself.
- **Global chat** — a process-wide WebSocket channel (`/global/ws`) shown beside
  the rooms list: a presence count, message history, and server-side anti-spam
  (per-minute cap + slow-mode + auto-timeout on repeated strikes).
- **Radmin VPN transport** — game traffic rides the community's existing Radmin
  VPN LAN (the `26.0.0.0/8` range; AoE3's stock LAN discovery finds peers on
  it). The launcher only **assists**: detect / install / launch the Radmin GUI
  and copy the network name to the clipboard for manual paste — it **cannot
  join a network programmatically**. It detects whether you're already in a
  network by parsing Radmin's `service.log` (plus its rotated `service (N).log`
  backups), with an ICMP ping to a seed peer as the fallback signal.
- **Mod fingerprint matching** — every room carries the SHA-256 of three AoE3
  critical files (`protoy.xml`, `techtreey.xml`, `stringtabley.xml`) computed
  locally at create / join time. Joins with a mismatching hash are rejected
  (`mod_mismatch`) so no one ever joins a game they can't actually play. Two
  stock-game players on the same game version match the same way and can share
  a lobby.
- **Auto skip-intro on launch** — when the host starts the match, AoE3 is
  spawned with `OverrideAddress="<host-radmin-ip>"` plus skip-intro flags, so
  players reach the menu quickly, then click Multiplayer → LAN once and the
  game is there.
- **Match history / ELO and replay upload are scaffolded but not yet wired** —
  the backend client methods (`ReportMatchAsync`, replay `UploadAsync`) and
  endpoints exist, but nothing in the UI calls them yet.

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

Output: `WarsOfLibertyLauncher\publish\Aoe3ModLauncher.exe` (~120 MB, fully
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

Per-mod state lives under a **`mods` dictionary keyed by mod id**, with the
launcher-wide `activeModId` selecting the current one — so switching mods never
cross-contaminates install paths or translations. The flat `modInstallPath` /
`gameExecutable` / `activeTranslationId` fields are **legacy**: migrated into
`mods[...]` on load and kept only for backward compatibility.

```json
{
  "activeModId": "wol",
  "mods": {
    "wol": {
      "installPath": "C:\\Program Files (x86)\\Wars of Liberty",
      "activeTranslationId": "",
      "lastKnownVersion": "1.0.4",
      "lastKnownLatestVersion": "1.0.4"
    }
  },
  "userModIds": [],
  "favoriteModIds": [],
  "radminAssistantMode": "Auto",
  "updateInfoUrl": "http://aoe3wol.com/updates/UpdateInfo.xml",
  "updateInfoUrlAlt": "http://master.dl.sourceforge.net/project/wars-of-liberty/Patches/UpdateInfo.xml",
  "startWithWindows": false,
  "closeLauncherOnGameStart": false,
  "minimizeToTray": false,
  "showToastNotifications": true,
  "checkUpdatesOnStartup": true,
  "openPostUpdatePages": true,
  "language": "en",
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
    "lobbyBaseUrl": "https://wol-lobby.duckdns.org",
    "sessionToken": "",
    "sessionExpiresAt": 0,
    "cachedUser": null
  }
}
```

`multiplayer.lobbyBaseUrl` points at the self-hosted Node/Fastify backend.
Configs written by older launchers that still carry the retired Cloudflare
Worker URL (or a local `127.0.0.1` / `*.trycloudflare.com` dev URL) are
auto-healed to this value on load, and the stale session token is cleared so
the user is prompted to sign in again with Discord.

`modsCatalogRepo` is empty by default (use the built-in
`Gorgorito12/aoe3-mods-catalog`); set it to `"none"` to skip the catalog fetch
entirely, or to a specific `owner/repo` to point at a fork or private test
catalog.

---

## Project structure

Repo-level layout:

```
Updater/
├── WarsOfLibertyLauncher/         The launcher (WPF, net8.0-windows) — see below
├── WarsOfLibertyLauncher.Tests/   xUnit tests for pure logic (sibling project)
├── docs/MODDING.md                Authoritative mod.json / catalog spec for modders
├── aoe3-mods-catalog-template/    Template for the separate community catalog repo
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
    ├── NativeInstallService.cs       Full install pipeline (live path)
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

---

## Roadmap

Shipped:

- **Built-in multiplayer** — Discord sign-in, a self-hosted Node/Fastify
  lobby backend (rooms, room state, in-room chat), real-time room state
  over WebSocket, and a process-wide global chat. Game traffic rides
  user-managed **Radmin VPN**, which the launcher detects / installs /
  launches and guides the user through joining.
- **Mod-fingerprint gating** — rooms carry the SHA-256 of the three
  critical AoE3 files; mismatched joins are rejected so no one lands in
  an unplayable game.
- **Live room control** — **host migration** (the room passes to the next
  joiner if the host leaves, GameRanger-style), a **match abort window**
  (any member can abort a bad start for ~60 s after launch) and **kick**
  (the host can expel a member, simple/re-joinable).
- **Per-peer connection stats** — the in-game overlay shows a real
  per-player ping: each client reports its Radmin IP over the lobby
  socket (`member_net`) and peers ICMP-ping each other.
- **Detect-only stock game** — the unmodded AoE3: The Asian Dynasties
  (`aoe3-tad`) is detected and launchable (single-player + Radmin
  multiplayer) without ever being modified.
- **Hardened self-update** — SemVer guard, SHA-256 + Authenticode
  verification before an atomic swap-with-rollback, conditional ETag
  fetch.
- **Window-size UI scaling** — the whole UI scales to fit smaller
  windows on top of native per-monitor DPI; typography + geometry are
  centralised as semantic tokens.
- **News panel** — markdown news feed pulled from the mod catalog repo.
- **Unit-test project** — `WarsOfLibertyLauncher.Tests` pins the
  pure-logic regressions (sibling-exclusion, install parity, content
  install-probe, self-update evaluation).

Next up:

- **Wire match history / ELO + replay upload** — the backend client
  methods and endpoints exist; the in-launcher views (match history,
  ELO ladder, replay browser) still need to call them.
- **More mod profiles** — extend the catalog with other AoE3
  total-conversion mods.
- **Per-peer byte counters** — the in-game per-player *ping* is live, but
  the bytes column is still a placeholder; true per-flow counters would
  need traffic accounting the launcher doesn't collect yet.

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

## Privacy

The launcher collects no analytics and runs no third-party trackers. It only
reaches the network to check for updates (which you can disable) and — once you
opt in by signing in with Discord — for multiplayer lobbies and chat. The
optional local telemetry log is **off by default**. See [`PRIVACY.md`](PRIVACY.md)
for the full detail.

## Code signing

Release binaries are Authenticode-signed. Free code signing provided by
[SignPath.io](https://about.signpath.io), certificate by
[SignPath Foundation](https://signpath.org). See the
[code signing policy](CODE_SIGNING_POLICY.md) for the team roles and the
CI build / origin-verification model.
