# Complete Roadmap — AoE3 Mod Launcher (Wars of Liberty / Improvement Mod)

## Context

The launcher is at `v0.7.9`, ~20k lines of C#/XAML, written in .NET 8 + WPF. It already plays a solid role as a replacement for the Wars of Liberty Java updater and supports multi-mod (WoL + Improvement Mod). It has an external GitHub catalog with auto-merge, native installation, community translations, self-update, and EN/ES localization.

This is an internal working document. Tone is direct, opinionated, prioritized.

Cross-cutting goals:
- **100% free** (no credit card on the CF account; budget for 50 DAU peak).
- **Compatible with what already works** — do not break the current WoL and IM flow.
- **Lower the friction for adding new mods** to the ecosystem.
- **Modern UI** that replaces the legacy-updater feel.
- **Integrated multiplayer** in the style of Voobly / GameRanger.

Refactor stance: **pragmatic**. Modularize where it pays off; do not migrate to full MVVM. Keep code-behind where it works.

---

## Current state (summary)

| Area | Maturity | Notes |
|---|---|---|
| Install/update pipeline | 🟢 Solid | `UpdateService` 752 L, `NativeInstallService` 772 L. Resumable, CRC32, backups. |
| Mod catalog | 🟡 Backend ready, no UI | `ModRegistry` loads built-ins + GitHub. **No visual browser yet**. |
| Install mechanisms | 🟡 Limited | Only `IsolatedFolder` and `InPlaceOverlay`. Missing `StandardModsFolder` (Documents). |
| Update mechanisms | 🟢 3 working | `WolPatcher`, `DelegatedExternal`, `GitHubReleases`. |
| UI/UX | 🟠 Functional but monolithic | `MainWindow.xaml.cs` 3998 L code-behind, no global ResourceDictionary. Styles duplicated across 9 dialogs. |
| Localization | 🟡 EN/ES hardcoded | `Strings.cs` 103 KB with in-code dictionary. Needs migration to external files. |
| Multiplayer | 🔴 Nonexistent | Backend already planned (CF + hosted ZeroTier, see Track C). |
| Launcher self-update | 🟢 Working | `LauncherUpdateService`, tag-based. |
| Code signing | 🟡 Self-signed | Cert `CN=Gorgorito`, post-build target. SmartScreen still warns. |
| CI/CD | 🔴 Manual | Local `build-release.ps1`. **No GitHub Actions in the launcher** (catalog repo has them). |
| Tests | 🔴 None | No test project. |
| Crash reporting | 🔴 Local only | `DiagnosticLog.cs` to file, no remote upload. |
| Discord RPC / Play time / News | 🔴 Nonexistent | "News" tab exists but with no backend. |

---

## Roadmap tracks

Each track is an independent unit of work, with priority, effort, and affected files.

### 🅰️ Track A — Pragmatic UI/UX (high priority, 4–5 weeks)

**Goal**: modernize the launcher's first impression without rewriting everything.

#### A1. Modularize MainWindow.xaml.cs (3998 L → ~600 L)
- Extract into UserControls:
  - `Views/HeroBanner.xaml` (mod selector + banner + accent)
  - `Views/StatusCard.xaml` (state + versions + AoE3 row)
  - `Views/ActionPanel.xaml` (Play / Update / Verify / Repair / Browse)
  - `Views/ProgressPanel.xaml` (overlay during operations)
  - `Views/MainTabs.xaml` (News / Changelog / Help → expandable)
- MainWindow becomes a "shell" that orchestrates UserControls.
- Code-behind stays inside each UserControl (no MVVM migration).
- Files: `MainWindow.xaml`, `MainWindow.xaml.cs` (refactor); new files under `Views/`.

#### A2. Global ResourceDictionary
- Move shared styles (`DialogButton`, `PrimaryButton`, `SectionHeader`, `HintText`, scrollbars, tooltips) from `MainWindow.xaml` and the 9 dialogs to `Styles/Common.xaml`.
- Load as Merged Dictionary in `App.xaml`.
- Removes ~2800 lines of duplicated style XAML.
- Files: `App.xaml`, new `Styles/Common.xaml`, the 10 XAML files lose duplication.

#### A3. Theme system
- Three modes: light / dark / system.
- Dynamic `AccentColor` variable read from the active mod (`ModProfile.AccentColor`) — data already exists, bindings are missing.
- New settings in `LauncherConfig`: `Theme` (enum), `AccentColorOverride` (string, optional).
- Files: `Models/LauncherConfig.cs`, `Styles/Themes/Dark.xaml`, `Styles/Themes/Light.xaml`, `LauncherSettingsDialog`.

#### A4. Reorganized main tabs
- Header with tabs: `Play` · `Mods` · `Multiplayer` · `News` · `Settings`.
- "Play" = the current view (status + actions).
- "Mods", "Multiplayer" = new (Tracks B and C).
- "News" = today's internal tab promoted to a main tab.
- Files: `MainWindow.xaml`, new `Views/MainTabs.xaml`.

#### A5. Working news feed
- New `Services/NewsService.cs`: fetches from `news.json` in the catalog repo (same repo as mods).
- Format: `{ title, date, body (markdown), image, modIds: ["wol", null] }` (null = global).
- 1 h cache, show last 10. Markdown rendered with `Markdig` NuGet (~200 KB, free).
- UI: list of cards with image + clipped body + "read more" expand.
- Files: new `Services/NewsService.cs`, new `Models/NewsItem.cs`, new `Views/NewsTab.xaml`.

#### A6. Window state + last-used persistence
- Save position, size, last open tab, last active mod into `LauncherConfig`.
- Restore on startup (with validation: a monitor may have been disconnected).
- Files: `Models/LauncherConfig.cs` (new fields: `WindowX`, `WindowY`, `WindowWidth`, `WindowHeight`, `WindowMaximized`, `LastActiveTab`, `LastActiveModId`), `MainWindow.xaml.cs` (`Closing` subscription).

#### A7. Discord Rich Presence
- NuGet `DiscordRichPresence` (~50 KB, free, MIT).
- Show "Playing Wars of Liberty v2.x" when `age3y.exe` is detected (already have `_gameMonitorTimer`).
- Opt-in setting in `LauncherSettingsDialog`.
- Files: new `Services/DiscordPresenceService.cs`, hook into `MainWindow.xaml.cs` where `_gameMonitorTimer` runs.

#### A8. Smooth animations
- XAML storyboards for tab transitions and panel reveals (no external libs).
- Loading skeleton for the mod list instead of a spinner.
- Files: each new Track A UserControl.

**Total Track A effort**: ~4–5 weeks.

---

### 🅱️ Track B — Easier to add and consume mods (3–4 weeks)

**Goal**: make the catalog stop being 2 hardcoded mods and become a living ecosystem.

#### B1. In-launcher Mod Browser
- `Mods` tab with a grid of cards: icon, displayName, author, description, accent color as border.
- Filters: installed / not installed, language, type (IsolatedFolder / Overlay / StandardMods).
- Click → detail view with large banner, optional screenshots, "Install" / "Uninstall".
- Reuses `ModCatalogService` (24 h cache already implemented) and `ModAssetCacheService`.
- Files: new `Views/ModBrowserTab.xaml`, new `Views/ModDetailPage.xaml`.

#### B2. `StandardModsFolder` support
- New `ModInstallType.StandardModsFolder`: mod installs into `%USERPROFILE%\Documents\My Games\Age of Empires 3\Mods\<modName>\`.
- Activation: copy/symlink into the folder, write `Mods.xml` for AoE3 to register it.
- Launch: base AoE3 with `-mod:<modName>` arg.
- Covers the official AoE3 modding ecosystem (ESOC patch, etc.).
- Files: `Models/ModProfile.cs` (new enum value), `Services/NativeInstallService.cs` (new branch), `Services/GameLauncher.cs` (new args).

#### B3. AoE3: Definitive Edition support
- New `GameEdition` enum in `ModProfile`: `LegacyTAD` (current) / `DefinitiveEdition`.
- `Aoe3DetectorService` already probes registry → add DE paths (Steam app id `933110`).
- Different mods folder (`%USERPROFILE%\Games\Age of Empires 3 DE\<userId>\mods\`).
- Some mods exist on both versions; the manifest can declare `compatibleEditions: ["LegacyTAD", "DefinitiveEdition"]`.
- Files: `Models/ModProfile.cs`, `Models/ModCatalogManifest.cs`, `Services/Aoe3DetectorService.cs`.

#### B4. "Publish my mod" wizard
- New dialog accessible from the `Mods` button → `+ Publish my mod`.
- Form: ID, displayName, author, description EN/ES, accent color picker, install type, payload URLs.
- Validates against the local JSON schema (`aoe3-mods-catalog-template/schema/mod.schema.json` embedded as a resource).
- Generates `mod.json` and opens the browser at `https://github.com/Gorgorito12/aoe3-mods-catalog/new/main/mods/<id>/` with the content pre-filled (URL params).
- Files: new `PublishModDialog.xaml`, embedded schema resource.

#### B5. Mod dependencies (optional, low priority)
- New field in `mod.json`: `dependencies: [{ id: "esoc-patch", version: ">=1.5" }]`.
- Launcher installs dependencies before the main mod.
- Useful for mods that require ESOC patch or Sandbox.
- Files: `Models/ModCatalogManifest.cs`, `Services/NativeInstallService.cs` (topological resolution).

#### B6. Improve catalog Tier 2 flow
- In the catalog repo, the `auto-merge.yml` workflow validates tag bumps.
- Add validation: that the tag exists in `sourceRepo` and has at least one `.zip` asset.
- Prevents "ghost tags" from being merged and breaking users.
- Files: `aoe3-mods-catalog-template/.github/scripts/classify_pr.py` (extend), or new `validate_release_tag.py`.

**Total Track B effort**: ~3–4 weeks (without B5 or B6, which can wait).

---

### 🅲 Track C — Multiplayer (6–9 weeks)

**Goal**: lobby + chat + matchmaking + virtual network, Voobly-style, all free, sized for 50 DAU peak.

#### C1. Cloudflare backend (1–2 weeks)
- Cloudflare account **without a credit card** (physically cannot be charged).
- Worker + Durable Objects + D1 + R2.
- D1 schema: `users`, `friends`, `games`, `replays`, `bans`, `usage_today`.
- Minimal REST endpoints: `/auth/github`, `/me`, `/games` (CRUD), `/friends`, `/replays`.
- Hibernatable WebSocket API for lobby chat and room events (zero cost while idle).
- New repo: `wol-launcher-lobby-worker` (separate from the launcher).

#### C2. Virtual network via ZeroTier Central (1–2 weeks)
- Free ZeroTier Central API: unlimited networks, 25 nodes per network.
- **1 ZeroTier network created on demand per game room** (max 8 AoE3 players → well under the limit).
- Worker holds `ZT_API_TOKEN` (secret), exposes `/games/{id}/network` that creates the network and returns `network_id` + `assigned_ip`.
- WPF client downloads the official ZeroTier binary (free, silent MSI), runs `zerotier-cli join <id>`, waits for `25.x.x.x` IP.
- After the match ends: the host destroys the network.

#### C3. TURN/relay fallback (0 weeks initially)
- Start **without our own TURN**. If ~10% of players hit symmetric NAT, they'll notice — no magic fix at launch.
- If it becomes a problem: use a free public TURN (e.g. `openrelay.metered.ca` has a generous free plan) before self-hosting.
- If the problem grows and it's worth it, spin up `coturn` on a Hetzner CX11 VPS (€3.79/month) — not free, but the last resort.

#### C4. WPF client — Multiplayer tab (2–3 weeks)
- Internal subtabs: `Rooms` · `Friends` · `Profile` · `History`.
- Rooms: filterable list (map, ranked, ping), "Create room" button.
- Room view: 8 slots, color, civilization, map, room chat, "Ready", "Start" (host only).
- Persistent global chat in sidebar.
- Friends: list, presence, DMs.
- Profile: Glicko-2 ELO (computed in Worker), games, replays.
- Files: `Views/MultiplayerTab.xaml` with subtabs, `Services/LobbyClient.cs` (WebSocket + REST), `Services/ZeroTierService.cs`.

#### C5. Game launch flow (1 week)
- Host creates room → Worker reserves a slot + creates ZT network.
- Peers join → receive `network_id` + virtual IPs of the rest.
- Host clicks "Start" → Worker emits `start` event with the host's virtual IP.
- Each client: launches AoE3 (`game.exe -nointro`) with the host's virtual IP as "Direct IP Connect".
- Launcher keeps the WebSocket open during the match only for chat/admin (not for game traffic).

#### C6. ELO, replays, lightweight anti-cheat (continuous)
- ELO: Glicko-2 computed in the Worker after each match (host reports the result, peers confirm).
- Replays: `.aoe3rec` uploaded to R2 (10 GB free), downloadable from profile. Cap 5 MB/replay.
- Anti-cheat: hash of `age3y.exe` reported on join; games with mismatched hashes stay unranked.

#### C7. Caps and circuit breakers
- `MAX_CONCURRENT_USERS = 60`, `MAX_ACTIVE_GAMES = 8`, `MAX_CHAT_MSG_PER_MIN = 30`, `MAX_REPLAY_SIZE_MB = 5`.
- Daily counter in D1 (`usage_today`): at 80% spent, reject non-critical features; at 95%, reject new logins.
- Clear UI message: "Server full (free tier). Try again in a few minutes."
- Visible top bar: "🟢 Quota: 34% · 14 online · 2/8 games".

**Total Track C effort**: ~6–9 weeks.

---

### 🅳 Track D — Infrastructure / DX (continuous, 2–3 weeks initial)

**Goal**: professionalize the release cycle and earn peace of mind.

#### D1. GitHub Actions: build + sign + release
- New workflow at `.github/workflows/release.yml` for the launcher.
- Trigger: tag push `v*`.
- Steps: `dotnet publish` → sign with cert (stored as a GitHub secret) → create release → upload `.exe` + SHA-256.
- The manual `build-release.ps1` stays as a local fallback.
- Files: new `.github/workflows/release.yml`, minor `.csproj` tweak for CI-aware signing.

#### D2. Unit tests
- New project `WarsOfLibertyLauncher.Tests` using xUnit.
- Priority coverage: `UpdateInfo.xml` parsing, patch chain computation, mod manifest schema validation, hashing.
- Not aiming at 100% coverage; aiming at regression prevention on critical logic.
- Files: new project added to the `.sln`.

#### D3. Opt-in crash reporting
- **Sentry free tier** (5k errors/mo, plenty for 50 DAU).
- Setup in `App.xaml.cs` with global handlers `DispatcherUnhandledException` + `AppDomain.CurrentDomain.UnhandledException`.
- Opt-in toggle in `LauncherSettingsDialog`. Default: off.
- If off: local log only (today's behavior).
- Files: `App.xaml.cs`, new `Services/CrashReporter.cs`, setting in `LauncherConfig`.

#### D4. Opt-in telemetry (defer, optional)
- **PostHog free tier** (1M events/mo), or avoid altogether.
- Track: `launcher_started`, `mod_installed`, `game_launched`, `update_applied`.
- Only if the user decides to enable it later. **Default: defer**.

#### D5. Code signing — evaluate EV or Azure Trusted Signing
- Current self-signed still triggers SmartScreen warnings.
- **Azure Trusted Signing**: ~$10/month, supports CI without a local cert, kills SmartScreen warnings. **Recommended if even a minimal budget is available**.
- Free alternative: document in INSTALL.md how to add the self-signed cert to `TrustedPublisher`.
- No code, just a decision.

#### D6. Auto-changelog from commits
- GitHub Action that generates the changelog on each tag from commits since the previous tag (optional Conventional Commits format).
- Auto-populates the release body.
- Files: `.github/workflows/release.yml` (same workflow as D1).

#### D7. SmartScreen sample submission automation
- After each release, a PowerShell script uploads the .exe to Microsoft Defender Sample Submission API.
- Reduces the window during which the binary appears as "unknown publisher".
- Files: `.github/workflows/release.yml` (extra step).

**Total Track D effort**: ~2–3 weeks for D1+D2+D3, the rest continuous.

---

### 🅴 Track E — Quality of life (continuous, no urgency)

#### E1. Play time tracking
- `Services/PlayTimeService.cs`: hooks into `GameLauncher` start/stop, persists into `LauncherConfig.Mods[id].PlayTimeHours` + `LauncherConfig.Mods[id].LastPlayed`.
- Show in `StatusCard`: "Played 12h this week".
- Files: new service, extend `Models/ModState.cs`.

#### E2. Local achievements
- 100% local, no server. JSON with achievement definitions.
- Examples: "First victory", "10 hours played", "Tried 3 mods".
- In-app toast notification.
- Files: new `Services/AchievementService.cs`, `Models/Achievement.cs`.

#### E3. Granular notifications
- Today: global toast on/off.
- Improve: per-event (update available, match ready, friend online).
- Files: extend `LauncherConfig` with a `NotificationPrefs` dict.

#### E4. Background update checker
- Today: check on startup.
- Add: timer every N hours (configurable).
- Files: `Services/UpdateService.cs`, `MainWindow.xaml.cs`.

#### E5. Keyboard shortcuts
- `Ctrl+1..5` to switch tabs.
- `F5` to refresh the catalog.
- `Ctrl+L` to open the log.
- Files: `MainWindow.xaml.cs` (InputBindings).

---

### 🅵 Track F — Localization (2 weeks)

#### F1. Migrate Strings.cs to external files
- From 103 KB hardcoded → per-language JSON files in `Localization/` (`en.json`, `es.json`).
- `Strings.cs` stays only as an API reading from the JSON.
- Allows hot-reload (change language without recompiling).
- Files: `Localization/Strings.cs` (refactor), new `Localization/Strings.en.json`, `Localization/Strings.es.json`.

#### F2. Add languages
- PT-BR (large AoE3 community in Brazil), FR, DE, RU.
- Each language is a community PR adding `Strings.<lang>.json`.

#### F3. In-launcher translation tool for the launcher itself
- Similar to the current `TranslationPackagerDialog` (which is for the game), but for launcher strings.
- Lets a user edit EN strings → see the result → export `.json` for a PR.
- Files: new `LauncherTranslationDialog.xaml`.

---

### 🅶 Track G — Security / robustness (no urgency, case by case)

#### G1. Optional ed25519 signature for `mod.json`
- Mod author signs their `mod.json` with a private key; public key in their GitHub profile.
- Launcher verifies before installing.
- Defense against compromised modder accounts.
- Files: `Services/ModCatalogService.cs` (validation), `Models/ModCatalogManifest.cs` (`signature` field).

#### G2. Extended mirror system
- Today: primary URL + SourceForge fallback.
- Add: R2 mirror (free 10 GB), optional IPFS.
- Files: `Services/DownloadService.cs`, `Models/ModCatalogManifest.cs` (`mirrors[]` field).

#### G3. Delta updates (binary diff)
- Instead of downloading full files, download bsdiffs between versions.
- Only worth it if large mods start releasing frequent updates.
- Files: new `Services/DeltaUpdateService.cs`, tweaks in `UpdateService.cs`.

#### G4. Quarantine integration
- Instead of downloading to `%TEMP%`, download to a quarantine folder that Defender scans before extraction.
- Files: `Services/DownloadService.cs`.

---

### 🅷 Track H — Community / engagement (continuous)

#### H1. Per-mod news feed (already in A5)
- Covered in Track A5.

#### H2. Discord webhook integration
- In the WoL Discord, users can see when someone creates a public room.
- Worker posts to a configured webhook.
- Files: Worker only, no launcher changes.

#### H3. Community buttons
- In the mod banner, quick buttons: Discord, Reddit, Official forum, social media.
- Data comes from `mod.json` (new `community: { discord, reddit, forum, twitter }` field).
- Files: `Models/ModCatalogManifest.cs`, `Views/HeroBanner.xaml`.

#### H4. In-app feedback system
- "Report a problem" button → dialog with: description, optional `launcher-debug.log` attachment, sent to a Worker endpoint or as a GitHub issue via API.
- Files: new `FeedbackDialog.xaml`, `/feedback` endpoint in Worker.

#### H5. Tournament/event banners
- Optional field in `news.json`: `pinned: true, banner: url, callToAction: { label, url }`.
- Shown at the top of the "News" tab as a promotional banner.

---

### 🅸 Track I — Code quality (continuous, no urgency)

Compatible with the user's pragmatic refactor stance (no full MVVM).

#### I1. Consolidate duplications
- 3 copies of `GitHubRelease` / `GitHubAsset` (in `GitHubReleaseDownloader`, `LauncherUpdateService`, `TranslationRegistryService`).
- Unify into `Models/GitHub/`.

#### I2. Interfaces for critical services (mockable)
- `IUpdateService`, `IInstallerService`, `IModCatalogService`, `IDownloadService`.
- Enables tests without a full DI migration.

#### I3. Refactor large services
- `UpdateService.cs` (752 L) → split into `UpdateOrchestrator` + `PatchApplier` + `VersionDetector`.
- `NativeInstallService.cs` (772 L) → split into `InstallOrchestrator` + `Aoe3Cloner` + `ModOverlay`.
- `TranslationService.cs` (621 L) → already fairly focused, leave it.

#### I4. Architecture docs
- Service and data-flow diagram in `docs/architecture.md`.
- Not for "selling" the project — for your future self in 6 months.

---

### 🅹 Track J — Platform expansion (very far future, optional)

#### J1. Full AoE3: DE support (already in B3)
- Covered in Track B.

#### J2. Linux via Wine wrapper
- Distribute as an AppImage that uses Wine to run the launcher + AoE3.
- Small but present market.

#### J3. macOS
- Very far future, AoE3 doesn't run natively on Mac. Probably never.

---

## Recommended sequence

Assuming part-time work (~10–15 h/week):

```
Month 1–2:  Track A1–A4 (modularize UI, ResourceDictionary, tabs, theme)
Month 2:    Track A5–A8 (news, window state, Discord RPC, animations)
Month 3:    Track B1–B3 (mod browser, StandardModsFolder, DE support)
Month 3:    Track B4 (publish wizard)
Month 4–5:  Track C1–C2 (CF backend + ZeroTier integration)
Month 5–6:  Track C4–C5 (multiplayer UI + game launch flow)
Month 6:    Track C6–C7 (ELO, replays, caps)
Continuous: Track D (CI, tests, crash report) — interleaved between tracks
When ready: Track F (PT-BR i18n, etc.) — depends on community
Deferred:   Tracks E, G, H, I, J
```

**Milestone 1 (Month 2)**: shippable modern UI → release `v0.8`.
**Milestone 2 (Month 3)**: functional mod browser → release `v0.9`.
**Milestone 3 (Month 6)**: multiplayer beta → release `v1.0`.

---

## Critical files to modify (consolidated)

### Major refactor
- `WarsOfLibertyLauncher/MainWindow.xaml` (3998 L → ~600 L) — A1
- `WarsOfLibertyLauncher/MainWindow.xaml.cs` — A1
- `WarsOfLibertyLauncher/App.xaml` — A2

### Extended models
- `WarsOfLibertyLauncher/Models/LauncherConfig.cs` — A3, A6, E1, E3
- `WarsOfLibertyLauncher/Models/ModProfile.cs` — B2, B3
- `WarsOfLibertyLauncher/Models/ModCatalogManifest.cs` — B3, B5, G1, G2, H3
- `WarsOfLibertyLauncher/Models/ModState.cs` — E1

### New services
- `Services/NewsService.cs` — A5
- `Services/DiscordPresenceService.cs` — A7
- `Services/LobbyClient.cs` — C4
- `Services/ZeroTierService.cs` — C2
- `Services/PlayTimeService.cs` — E1
- `Services/AchievementService.cs` — E2
- `Services/CrashReporter.cs` — D3

### New UserControls / Views
- `Views/HeroBanner.xaml` — A1
- `Views/StatusCard.xaml` — A1
- `Views/ActionPanel.xaml` — A1
- `Views/ProgressPanel.xaml` — A1
- `Views/MainTabs.xaml` — A4
- `Views/NewsTab.xaml` — A5
- `Views/ModBrowserTab.xaml` — B1
- `Views/ModDetailPage.xaml` — B1
- `Views/MultiplayerTab.xaml` — C4

### New dialogs
- `PublishModDialog.xaml` — B4
- `FeedbackDialog.xaml` — H4
- `LauncherTranslationDialog.xaml` — F3

### Resources
- `Styles/Common.xaml` — A2
- `Styles/Themes/Dark.xaml` — A3
- `Styles/Themes/Light.xaml` — A3
- `Localization/Strings.en.json` — F1
- `Localization/Strings.es.json` — F1

### Infra
- `.github/workflows/release.yml` — D1, D6, D7
- `WarsOfLibertyLauncher.Tests/` project — D2
- External repo `wol-launcher-lobby-worker` — C1

### Reuse (existing, do not break)
- `Services/UpdateService.cs` — only add `IUpdateService` interface (I2)
- `Services/NativeInstallService.cs` — extend for `StandardModsFolder` (B2)
- `Services/ModCatalogService.cs` — extend with signatures (G1) and mirrors (G2)
- `Services/ModRegistry.cs` — no changes, remains source of truth
- `Services/GameLauncher.cs` — extend args for standard-folder mods (B2)
- `Services/Aoe3DetectorService.cs` — extend for AoE3 DE (B3)
- `Services/TranslationService.cs` — no changes
- `Services/LauncherUpdateService.cs` — refactor to reuse `GitHubReleaseDownloader` (I1)

---

## End-to-end verification

Each track has its own way to validate.

### Track A — UI
- Manual smoke test: open launcher, switch tabs, toggle theme, view news, launch the game, verify Discord status.
- Verify that old dialogs (Aoe3Picker, InstallFolder, etc.) still render correctly after extracting the ResourceDictionary.

### Track B — Mods
- Install/uninstall WoL and IM from the browser (regression).
- Create a test mod in `StandardModsFolder`, verify it appears in AoE3.
- Dual detection of AoE3 TAD + DE (test with both installs).
- Use the publish wizard, validate the JSON against the schema, open the PR.

### Track C — Multiplayer
- 2 accounts, 2 PCs behind different NATs, create a room, join, see chat, start a match, connect, play 5 minutes, end, view the replay.
- Symmetric-NAT test: disable UPnP on the router, attempt connection (expect it to fail until TURN is in place).
- Caps test: scripts simulating 100 connections, verify the Worker rejects above 60.
- Daily-quota test: force the counter to 80%, see the UI message.

### Track D — Infra
- A `v0.8.0` tag push triggers the workflow and produces a release with a signed .exe and SHA-256.
- Tests run in CI, PRs that break versioning logic fail.
- Force an unhandled exception, verify it shows up in Sentry (if opted in).

### Track F — i18n
- Switch language to PT-BR, verify all strings change without a restart.

---

## Deferred decisions (the user has to make these at some point)

1. **EV cert vs self-signed**: pay ~$10/month for Azure Trusted Signing or live with SmartScreen warnings. Defer until the volume of new installs justifies it.
2. **Opt-in telemetry**: D4. Add PostHog or skip entirely. Default: do not implement.
3. **AoE3 DE support**: if the real audience is legacy TAD (more likely for WoL), B3 is nice-to-have, not must-have.
4. **Achievements**: E2 is 100% nice-to-have, no competitive value.
5. **Mod dependencies (B5)**: only implement when a mod actually needs them.
6. **Linux/Mac (J2/J3)**: unlikely to be worth the effort. Defer indefinitely.
7. **Donations**: no paywall or Ko-fi button included in the roadmap. User's call if it makes sense once it grows.

---

## Risks / loose ends

- **MainWindow refactor (A1)**: 3998 lines touched. Regression risk. Mitigation: small steps, one section per commit, smoke-test after each step.
- **Multiplayer + ZeroTier requires local admin**: the ZT client runs as a service. UAC prompt on install. Document this.
- **Cloudflare free tier rules can change**: today DOs are free, tomorrow maybe not. Mitigation: design so the Worker can be turned off and direct P2P still works in a degraded mode.
- **ZeroTier Central could change API rules or kill free tier**: unlikely short-term, but plan B = self-host the controller (≈€4/month VPS).
- **Real audience**: the roadmap assumes ~50 DAU. If the community is 5 people, full Track C is overkill. Re-evaluate after Tracks A+B based on engagement.

---

## What is NOT in this roadmap (intentional)

- Rewriting the launcher in Electron / Tauri / anything else.
- Monetization, subscriptions, currencies.
- Modding tools inside the launcher (editors, asset packers).
- Voice chat (Discord already covers it).
- Automatic ELO-based matchmaking (ranked queue) — deferred, requires critical mass.
- Global ladder, automated tournaments.
- Full AoE3: DE mod support (DE's mod API is different and less open — only detection and launch).
