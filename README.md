# AoE3 Mod Launcher

A modern, native Windows launcher for **Age of Empires III** total-conversion
mods — currently shipping with profiles for **Wars of Liberty** and the
**Improvement Mod**.

Replaces the legacy Java updater and the Inno Setup installer with a single
self-contained `.exe`. Detects existing installations, downloads missing files
from the server, applies patches, and clones Age of Empires III into a
standalone mod folder so the mod runs side-by-side with the base game.

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
│
├── Localization/
│   └── Strings.cs                    EN/ES string table
│
├── Models/
│   ├── UpdateInfo.cs                 UpdateInfo.xml schema
│   ├── LauncherConfig.cs             launcher-config.json schema
│   └── InstallManifest.cs            <mod>-manifest.json (uninstall tracking)
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
    └── DiagnosticLog.cs              launcher-debug.log writer
```

---

## Roadmap

- **Radmin VPN integration** — auto-detect Radmin adapter IP, launch with
  `OverrideAddress` parameter for multiplayer over Radmin networks.
- **News panel** — surface patch notes and announcements from each mod's
  official site directly in the launcher.
- **More mod profiles** — extend the multi-mod system to other AoE3
  total-conversion mods.

---

## License

MIT (see `LICENSE.txt`).

The mods themselves are the work of their respective teams (Wars of Liberty,
Improvement Mod, …) — this launcher is an unofficial alternative client and
not affiliated with them.
