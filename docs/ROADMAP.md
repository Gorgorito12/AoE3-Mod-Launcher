# AoE3 Mod Launcher — Roadmap

> From **v0.7.9** to **v1.0**: a modern launcher, a living mod ecosystem, and integrated multiplayer.

## North star

- **Free at launch** — 50 DAU target, Cloudflare free tier, no credit card on file.
- **Never break what already works** — current Wars of Liberty and Improvement Mod flows stay green.
- **Lower the friction for adding new mods.**
- **Modern UI** that retires the legacy-updater feel.
- **Integrated multiplayer** in the spirit of Voobly / GameRanger.

**Refactor stance:** pragmatic. Modularize where it pays off, keep code-behind where it works, no full MVVM migration.

## Where we are (v0.7.9)

The launcher already works well as an updater: install/update pipeline, mod registry with GitHub catalog, native installation, EN/ES localization, self-update. The two real gaps: the UI feels dated (`MainWindow.xaml.cs` is 3998 lines and styles are duplicated across 11 XAML files), and the catalog has no visual browser — adding a mod still means hand-editing JSON. Multiplayer is nonexistent.

---

## 🎨 v0.8 — Modern UI · *Month 1–2*

**Goal:** the first impression stops feeling like a 2010 updater.

- Split `MainWindow` into 5 UserControls: `HeroBanner`, `StatusCard`, `ActionPanel`, `ProgressPanel`, `MainTabs`. One UserControl per commit, smoke test after each.
- Global `Styles/Common.xaml` merged dictionary — removes ~2800 duplicated lines across the 11 XAML files.
- Theme system: light / dark / system, accent driven by the active mod's `AccentColor`.
- Top-level tabs: **Play · Mods · Multiplayer · News · Settings**.
- News feed reading `news.json` from the catalog repo, markdown-rendered with 1 h cache.
- Window-state persistence (size, position, last tab, last mod).
- Discord Rich Presence (opt-in).

**Biggest risk in the whole roadmap.** The `MainWindow` refactor touches 3998 lines. Mitigation is procedural: one UserControl at a time, no parallel changes, run the smoke test below before each commit.

## 📦 v0.9 — Living mod ecosystem · *Month 3*

**Goal:** the catalog stops being "two hardcoded mods" and becomes a browsable ecosystem.

- **Mod Browser** tab: grid of cards (icon, accent border, author, description), filters, detail view, install/uninstall in-place. Reuses `ModCatalogService` and `ModAssetCacheService`.
- **`StandardModsFolder` install type**: mods drop into `Documents\My Games\Age of Empires 3\Mods\…` and AoE3 picks them up natively. Opens the door to ESOC patch and the wider AoE3 mod scene.
- **AoE3: Definitive Edition** — detection + launch only. Full DE mod compatibility is a separate, larger effort and stays deferred.
- **"Publish my mod" wizard**: form → validates against the embedded JSON schema → generates `mod.json` → opens a pre-filled PR in the catalog repo.

## 🌐 v1.0 — Integrated multiplayer · *Month 4–6 (realistic: 7–9)*

**Goal:** Voobly-style lobby, free, sized for 50 DAU peak.

- **Cloudflare Worker** backend (Durable Objects + D1 + R2) in a new repo `wol-launcher-lobby-worker`. GitHub OAuth, hibernatable WebSocket for chat and room events, REST for everything else. Auth token validated on every WS message, not just on connect.
- **ZeroTier Central** virtual network — one network per game room, created and destroyed on demand. **Free TURN (`openrelay.metered.ca`) integrated from day 1**, so symmetric-NAT users don't silently fail at launch.
- **Multiplayer tab** with subtabs: Rooms · Friends · Profile · History. Per-room chat + persistent global chat. Glicko-2 ELO. Replays uploaded to R2 with a 5 MB cap.
- **Hard caps and visible quota bar**: 60 concurrent users, 8 active games, 30 chat msg/min, daily counter. Clear "server full (free tier)" message instead of silent degradation.

**Plan B if the free tier breaks:** €4/mo VPS keeps the service alive. The service survives, the "free at launch" goal does not — that's an explicit tradeoff, not a fallback to discover later.

---

## 🛠️ Continuous track (interleaved with every release)

Not a milestone — these ship a piece at a time alongside the v0.8 / v0.9 / v1.0 work.

- **Build & release.** `release.yml` GitHub Action: tag push → `dotnet publish` → sign → release with SHA-256. Local `build-release.ps1` stays as fallback.
- **Tests.** New `WarsOfLibertyLauncher.Tests` (xUnit). Priorities: `UpdateInfo.xml` parsing, patch chain computation, manifest schema validation, hashing. Regression prevention, not coverage chasing.
- **Crash reporting.** Sentry free tier, opt-in, default off. Today's local log stays when disabled.
- **Code signing.** Self-signed today triggers SmartScreen. Stay self-signed until install volume justifies ~$10/mo Azure Trusted Signing. Meanwhile, document the "add to `TrustedPublisher`" workaround in INSTALL.md.
- **Localization.** Migrate the 106 KB hardcoded `Strings.cs` to per-language JSON (`Localization/Strings.<lang>.json`). Then welcome community PRs for PT-BR, FR, DE, RU.
- **Code hygiene.** Consolidate the 3 duplicate `GitHubRelease` definitions; add interfaces for the 4 critical services (`IUpdateService`, `IInstallerService`, `IModCatalogService`, `IDownloadService`) so they're mockable. No big-bang refactor.

## ✨ Beyond v1.0 (no commitment)

Picked up only if the community pulls for them: per-mod news, Discord webhook for new rooms, community buttons on mod banners, in-app feedback dialog, tournament banners, play-time tracking, local achievements, granular notifications, keyboard shortcuts, in-launcher translation tool, ed25519-signed mod manifests, R2/IPFS mirrors, delta updates, Linux via Wine, full AoE3: DE mod support.

---

## Smoke test (run after every UI commit during v0.8)

1. Launcher opens cold without errors.
2. Switch between Wars of Liberty and Improvement Mod — banner and accent update.
3. "Verify files" starts and completes.
4. Switch language EN ↔ ES without restart.
5. Switch tabs Noticias / Changelog / Ayuda.
6. Open the More menu — every section renders.
7. Minimize to tray and restore.
8. Close the launcher with no operation in flight.

## Multiplayer smoke test (run before each v1.x release)

Needs two Windows PCs, both with the active mod installed at the same version.

1. **Backend deployed.** `wol-launcher-lobby-worker` reachable at the URL in
   `launcher-config.json` → `multiplayer.lobbyBaseUrl`. `GET /health` returns 200
   with `version` matching the deploy.
2. **Cold first run.** On a PC without ZeroTier installed, open Multiplayer →
   the ZeroTier bootstrap card appears. Click *Install ZeroTier* → UAC prompt →
   MSI installs silently → card disappears within ~15 s.
3. **GitHub sign-in.** Click *Sign in with GitHub* → device code dialog →
   *Open browser* fires the verification URL → approve → dialog closes and
   `@login` shows in the top bar.
4. **Create a room.** *Create room* → fingerprint of the mod shown read-only,
   title editable. Submit → ZeroTier joins the new network → room view appears
   with the host in the players list.
5. **Join from PC #2.** Same sign-in flow. The newly-created room shows in
   the list. Click *Join* → mod fingerprint check passes → room view appears
   on PC #2 with both players visible on both sides.
6. **Chat.** Send a message from each PC; the other one shows it within ~1 s
   with sender + timestamp.
7. **Ready toggle.** Click *Ready* on PC #2; PC #1 sees the green dot.
8. **Mod-mismatch path.** Edit a critical mod file on PC #2 (e.g. add a
   space to `protoy.xml`), try to join again → join is rejected with
   `mod_mismatch`; `multiplayer-events.log` increments `mp_mod_mismatch`.
9. **Quota bar.** Header reads `1/60 players · 1/8 active rooms`. Refresh
   resets correctly after both PCs leave the room.
10. **Sign out + restart launcher.** The signed-in state and saved
    `multiplayer.sessionToken` clear from `launcher-config.json`. Cold restart
    lands on the sign-in gate.

## Open decisions

1. **EV cert vs self-signed.** Pay for Azure Trusted Signing or live with SmartScreen. Defer until install volume justifies it.
2. **Telemetry.** PostHog free tier or skip entirely. Default: skip.
3. **Full AoE3: DE mod support.** Detection ships in v0.9; full mod compatibility is a separate decision once the DE audience materializes.
4. **Donations.** No Ko-fi / Patreon planned. Reconsider only if real costs appear (TURN VPS, signing cert).

## What we're *not* doing (intentional)

- Rewriting in Electron / Tauri / anything not WPF.
- Monetization, paywalls, virtual currencies.
- Modding tools inside the launcher (editors, asset packers).
- Voice chat — Discord already covers it.
- Automatic ranked matchmaking, global ladder, automated tournaments.
- macOS support.

## Risks worth naming

- **`MainWindow` refactor (v0.8).** 3998 lines moved. One UserControl per commit, smoke test in between, no shortcuts.
- **Free-tier dependency (v1.0).** Cloudflare DOs and ZeroTier Central both need to stay free. If either goes paid, plan B is the €4/mo VPS — service survives, "free" goal does not.
- **ZeroTier needs admin rights.** UAC prompt on first install. Some users will refuse. Document up front.
- **Audience size assumption.** Roadmap assumes ~50 DAU. If the real community is 5 people, v1.0 multiplayer is overkill — re-evaluate after v0.9 based on engagement.
