# Wars of Liberty Launcher

A modern, native Windows launcher for **Wars of Liberty**, the Age of Empires III mod.

Replaces the legacy Java updater and the Inno Setup installer with a single
self-contained `.exe`. Detects existing installations, downloads missing files
from the server, applies patches, and clones Age of Empires III into a
standalone WoL folder so the mod runs side-by-side with the base game.

> **100% compatible** with the official Wars of Liberty server — speaks the
> same `UpdateInfo.xml` format and processes the same `.tar.xz` patches as the
> Java updater, but without the Java runtime requirement.

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

### Path detection
- 3-level fallback for finding existing WoL installs:
  1. Saved path from `launcher-config.json`
  2. Windows registry (Inno Setup uninstall key)
  3. Disk scan across common drives (C:, D:, E:) for `age3y.exe`
- Equivalent fallback for AoE3: registry → walk up from WoL folder → scan
  for Steam, GOG, and Microsoft Games install paths.
- Manual override via the gear (⚙) menu when auto-detection fails.

### Settings menu (gear icon)
The ⚙ button on the bottom-left opens a context menu with:
- 📁 **Select Wars of Liberty folder** — point the launcher at an existing WoL
  install in a non-standard location.
- 🎮 **Select Age of Empires III folder** — point the launcher at an AoE3
  install when auto-detection misses it.
- 🗑 **Uninstall mod** — manifest-tracked removal with a styled confirmation
  dialog.

All paths are validated before saving (`age3y.exe` for AoE3, `art\zulushield`
marker for WoL).

### User-data safety
After a fresh install, if the launcher detects existing WoL data under
`Documents\My Games\Wars of Liberty\` from a previous (potentially newer)
install, it shows an alert offering to:
- **Back up and continue** — renames the folder to
  `Wars of Liberty.bak.<timestamp>` so the freshly installed 1.0.15d base can
  start clean.
- **Open folder** — launches Explorer to inspect the data manually.
- **Ignore** — proceeds at the user's own risk.

The launcher **never deletes** user data — the worst it does is rename a folder.

### Self-update
The launcher checks GitHub Releases for a newer version on startup. Updates are
**tag-based** (no need to bump assembly versions to publish a release) — the
launcher saves the GitHub release tag it was installed from, and prompts when
a different tag appears upstream.

The user can dismiss an update with "Later"; the launcher remembers that tag
and won't re-prompt until a different one is published.

### Localization
English and Spanish, switchable from the top-right corner. All UI strings
(buttons, dialogs, status messages) are localized. Diagnostic logs stay in
English for bug reports.

### Privilege handling
The launcher runs un-elevated by default. When it needs to write to a
protected location (e.g. `C:\Program Files (x86)\...`) it prompts for UAC
elevation and relaunches itself with admin rights. Update flow can be
auto-resumed elevated via `--update-now` argument.

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
│  4. Download WoL payload ZIP (3 parts, ~4 GB total)        │
│     Concatenate into single ZIP, extract to temp           │
├─────────────────────────────────────────────────────────────┤
│  5. Clone AoE3 → destination (skips destination if it      │
│     lives inside source, to avoid recursion)               │
├─────────────────────────────────────────────────────────────┤
│  6. Flatten `bin\` to root (Steam layout) and remove the   │
│     redundant `bin\` subfolder afterwards (~3.7 GB saved)  │
├─────────────────────────────────────────────────────────────┤
│  7. Overlay WoL mod files on top of cloned AoE3            │
├─────────────────────────────────────────────────────────────┤
│  8. Create Start Menu + Desktop shortcuts                  │
├─────────────────────────────────────────────────────────────┤
│  9. Write uninstall registry entries (HKLM if admin,       │
│     HKCU otherwise) and `wol-manifest.json`                │
├─────────────────────────────────────────────────────────────┤
│ 10. Verify install (required dirs + .bar archive sizes)    │
├─────────────────────────────────────────────────────────────┤
│ 11. If existing user data detected → show backup alert     │
└─────────────────────────────────────────────────────────────┘
```

## How update works

```
┌─────────────────────────────────────────────────────────────┐
│  1. Detect WoL install (config / registry / disk scan)     │
├─────────────────────────────────────────────────────────────┤
│  2. Download UpdateInfo.xml from aoe3wol.com               │
│     (fallback to SourceForge mirror)                       │
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

---

## Building

Requires **.NET 8 SDK** on Windows.

### Quick build

```powershell
cd WarsOfLibertyLauncher
dotnet build -c Release
```

### Single-file portable .exe (recommended for distribution)

Use the included PowerShell script:

```powershell
# Use whatever <Version> is in the .csproj
.\publish.ps1

# Override version (also bakes it into the assembly)
.\publish.ps1 0.6.6

# Override version AND create a matching git tag
.\publish.ps1 0.6.6 -Tag
```

The output is at `publish\WarsOfLibertyLauncher.exe` (~155 MB self-contained).

### Manual publish

```powershell
dotnet publish WarsOfLibertyLauncher\WarsOfLibertyLauncher.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o publish
```

---

## Configuration

A `launcher-config.json` file is created next to the `.exe` on first run:

```json
{
  "updateInfoUrl": "http://aoe3wol.com/updates/UpdateInfo.xml",
  "updateInfoUrlAlt": "http://master.dl.sourceforge.net/project/wars-of-liberty/Patches/UpdateInfo.xml",
  "modInstallPath": "",
  "gameExecutable": "",
  "gameArguments": "",
  "openPostUpdatePages": true,
  "language": "en",
  "payloadZipUrls": [
    "https://github.com/papillo12/Updater/releases/download/updater/WolPayload.zip.001",
    "https://github.com/papillo12/Updater/releases/download/updater/WolPayload.zip.002",
    "https://github.com/papillo12/Updater/releases/download/updater/WolPayload.zip.003"
  ],
  "defaultInstallFolder": "C:\\Program Files (x86)\\Wars of Liberty",
  "officialWebsite": "http://aoe3wol.com/",
  "lastInstalledLauncherTag": "",
  "skippedLauncherTag": ""
}
```

Most fields auto-populate. Edit only when you need to override defaults
(custom server URLs, non-standard install paths, alternate payload mirrors).

---

## Project structure

```
WarsOfLibertyLauncher/
├── MainWindow.xaml(.cs)            Main UI
├── InstallFolderDialog.xaml(.cs)   Install destination + AoE3 picker
├── UninstallDialog.xaml(.cs)       Uninstall confirmation + options
├── LauncherUpdateDialog.xaml(.cs)  Self-update prompt
├── UserDataAlertDialog.xaml(.cs)   Documents user-data backup prompt
├── publish.ps1                     Build script
│
├── Models/
│   ├── UpdateInfo.cs               UpdateInfo.xml schema
│   ├── LauncherConfig.cs           launcher-config.json schema
│   └── InstallManifest.cs          wol-manifest.json (uninstall tracking)
│
└── Services/
    ├── HashService.cs              MD5 + CRC32
    ├── RegistryService.cs          Detect WoL via registry
    ├── AoE3Detector.cs             Disk scan for AoE3 (Steam/GOG/retail)
    ├── DownloadService.cs          HTTP with resume + URL fallback
    ├── UpdateInfoService.cs        Parse UpdateInfo.xml
    ├── ArchiveService.cs           .tar.xz extraction with backup
    ├── UpdateService.cs            Update flow orchestrator
    ├── NativeInstallService.cs     Full install pipeline
    ├── FolderCloneService.cs       AoE3 → WoL clone with progress
    ├── UninstallService.cs         Manifest-tracked removal
    ├── UserDataService.cs          Documents\My Games\Wars of Liberty
    ├── LauncherUpdateService.cs    GitHub Releases self-update
    ├── ElevationService.cs         UAC / admin-rights helpers
    ├── GameLauncher.cs             Launch age3y.exe with the right args
    └── DiagnosticLog.cs            launcher-debug.log writer
```

---

## Roadmap

- **Radmin VPN integration** — auto-detect Radmin adapter IP, launch with
  `OverrideAddress` parameter for multiplayer over Radmin networks.
- **Community translations** — optional language packs with backup/restore.
- **News panel** — surface patch notes and announcements from the official
  site directly in the launcher.

---

## License

MIT (see `LICENSE.txt`).

The Wars of Liberty mod itself is the work of the Wars of Liberty team —
this launcher is an unofficial alternative client and not affiliated with them.
