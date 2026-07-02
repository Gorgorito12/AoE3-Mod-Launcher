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

**Install / Update / Verify** — native install pipeline (multi-part ZIP
download, AoE3 clone, mod overlay, shortcuts + registry) for Steam / GOG /
retail; resumable downloads with mirror fallback; CRC32-verified patches with
backup-before-overwrite. **Verify & Repair** runs a real per-file SHA-256
integrity pass against `install-manifest.json` and re-lays only a damaged
overlay (an intact install skips the multi-GB download). Uninstall is a guarded
delete of the mod's own clone — your base AoE3 is never touched, and the stock
game is hard-refused.

**Multi-mod & multi-install** — profile-based; the built-in mods (WoL,
Improvement Mod) merge with community mods from the Workshop catalog and switch
on the fly. A mod can have **several installs registered at once** (different
versions/folders); Mod Properties switches which is active.

**Mod presentation** — each mod brings its own icon, Workshop banner, full-bleed
**dashboard hero** (with rotating multi-image crossfade) and a screenshot/GIF
gallery; art up to **4K** (see [docs/MODDING.md](docs/MODDING.md)).

**Stock game (detect-only)** — the unmodded **AoE3: The Asian Dynasties**
(`aoe3-tad`) is detected and launchable (single-player + Radmin multiplayer) but
never installed, updated, or uninstalled — it's your own legally-owned copy.

**Path detection** — installs are found by **content** (probe file + marker),
never folder name: saved path → registry → content scan around the detected AoE3
root, with a manual override in Settings. AoE3 itself is found across all fixed
drives (Steam / GOG / retail).

**Offline mode** — installed mods stay **playable with no internet**. The update
check degrades gracefully (renders PLAY from local state) for **every** mod type;
connectivity is **observed, never probed** (so a proxy or the Radmin adapter
can't false-flag you). A "Sin conexión" chip appears and online-only actions grey
out until you reconnect — PLAY and all local actions keep working.

**Community translations** — language packs discovered automatically
(folder-committed or legacy GitHub releases), with a **version picker** to roll
back, one-click apply + restore-originals, and a built-in **Translation
Packager**.

**Notifications** — a Steam-style **bell** for update-available /
update-complete / new-translation / launcher-update / offline / new-mod events,
deduped and persisted. Fed by a small **central notification feed**
(`wol-notify.duckdns.org`) with automatic fallback to per-mod GitHub polling if
it's unreachable.

**Multiplayer** — a Voobly-style tab: Discord sign-in, a self-hosted Node/Fastify
lobby backend (rooms, in-room chat, global chat), real-time room state over
WebSocket, host migration, kick, a ~60 s match-abort window, and a **real**
per-peer ping. Game traffic rides user-managed **Radmin VPN**; joins are gated by
a mod-hash fingerprint. → full details in
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#multiplayer-internals).

**Self-update** — a startup check of GitHub Releases; the download is
**verified** (SHA-256 + same-signer Authenticode) before an **atomic swap with
rollback**, so a partial write never leaves you without an executable.

**User-data safety** — the launcher **never deletes** user data; at most it
renames `Documents\My Games\<Mod>\` to a timestamped backup after a fresh
install.

**Localization** — English + Spanish, switchable live from Launcher Settings
(diagnostic logs stay English for bug reports).

**Privilege handling** — runs un-elevated; prompts for UAC only when it needs to
write a protected location (the update flow can auto-resume elevated via
`--update-now`).

---

## Quick start (users)

Download the latest `Aoe3ModLauncher.exe` from the [Releases](../../releases)
page and run it. First launch may hit SmartScreen (the binary isn't yet signed
by a trusted cert) — see [INSTALL.md](WarsOfLibertyLauncher/INSTALL.md) for what
to click and how to verify the SHA-256.

## Quick build (developers)

Requires the **.NET 8 SDK** on Windows (this is a `net8.0-windows` + WPF
project — Windows only).

```powershell
cd WarsOfLibertyLauncher
dotnet build -c Release
```

For the single-file portable `.exe`, the release script, and the CI / SignPath
pipeline, see [docs/BUILDING.md](docs/BUILDING.md).

---

## Documentation

- **[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)** — how install / update /
  multiplayer work, the multiplayer internals, and the project structure.
- **[docs/CONFIGURATION.md](docs/CONFIGURATION.md)** — the `launcher-config.json`
  schema and per-mod state.
- **[docs/BUILDING.md](docs/BUILDING.md)** — dev build, single-file publish, and
  the release / signing pipeline.
- **[docs/MODDING.md](docs/MODDING.md)** — authoritative `mod.json` / catalog
  spec for mod authors.
- **[INSTALL.md](WarsOfLibertyLauncher/INSTALL.md)** — end-user install +
  SmartScreen guide.
- **[CONTRIBUTING.md](CONTRIBUTING.md)** — build / test, DCO sign-off, PR
  conventions.
- **[PRIVACY.md](PRIVACY.md)** · **[CODE_SIGNING_POLICY.md](CODE_SIGNING_POLICY.md)**
  · **[DISCLAIMER.md](DISCLAIMER.md)**

---

## Roadmap

**Shipped:** built-in multiplayer (Discord sign-in, self-hosted lobby backend,
real-time rooms + global chat, host migration, kick, match-abort window, real
per-peer ping) over Radmin VPN; mod-fingerprint join gating; detect-only stock
game; hardened verified self-update; offline mode; window-size UI scaling;
central notification feed + bell; a markdown news panel; a unit-test project.

**Next up:** wire match history / ELO + replay upload (the backend client
methods and endpoints exist; the in-launcher views still need to call them);
more mod profiles in the catalog; per-peer byte counters in the in-game overlay
(the per-peer *ping* is already live).

---

## License

Apache License 2.0 — see [`LICENSE`](LICENSE). Chosen over MIT for the explicit
patent grant from contributors; pull requests are accepted under the same
license via a DCO sign-off — see [`CONTRIBUTING.md`](CONTRIBUTING.md).

The mods themselves are the work of their respective teams (Wars of Liberty,
Improvement Mod, …) — this launcher is an unofficial alternative client and not
affiliated with them. *Age of Empires III* is a trademark of Microsoft
Corporation; see [`DISCLAIMER.md`](DISCLAIMER.md) for trademark and
third-party-component notes.

## Privacy & code signing

The launcher collects no analytics and runs no third-party trackers — it only
reaches the network to check for updates (which you can disable) and, once you
opt in by signing in with Discord, for multiplayer lobbies and chat. The optional
local telemetry log is **off by default**. See [`PRIVACY.md`](PRIVACY.md) for the
full detail.

Release binaries are Authenticode-signed. Free code signing is provided by
[SignPath.io](https://about.signpath.io), certificate by
[SignPath Foundation](https://signpath.org) — see the
[code signing policy](CODE_SIGNING_POLICY.md) for the team roles and the CI build
/ origin-verification model. Until SignPath approval, GitHub-release binaries
verify by the **SHA-256** printed in the release notes; local/dev builds use a
self-signed `CN=Gorgorito` cert (meaningful only on the build machine).
