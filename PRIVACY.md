# Privacy Policy

_Last updated: 2026-06-12_

The **AoE3 Mod Launcher** is a free, open-source desktop application. This
document describes exactly what data it stores, what data leaves your computer,
and how to turn each of those off. It is written to be honest about the code as
it actually behaves — if you find a discrepancy between this policy and what the
launcher does, please [open an issue](https://github.com/Gorgorito12/Updater/issues).

## Summary (TL;DR)

- **No analytics, no ad networks, no third-party tracking SDKs.** Nothing about
  your usage is sold or shared for advertising.
- **By default the launcher only talks to the internet to check for updates**
  (the launcher itself, mod patches, the mod catalog, translations and news).
  You can turn that off.
- **Multiplayer is opt-in.** Nothing related to multiplayer leaves your computer
  until you choose to sign in with Discord.
- **The local telemetry log is OFF by default** and never leaves your computer
  even when enabled.

## What the launcher stores on your computer

These files live next to the launcher executable (or in your local app data) and
are **never uploaded anywhere by the launcher**:

- **`launcher-config.json`** — your settings and, once you sign in to
  multiplayer, the session token issued by the lobby server. Treat this token
  like a password; anyone with the file could act as your multiplayer session
  until it expires.
- **`launcher-debug.log`** — a local diagnostic log (reset on each launch) that
  records what the launcher did, in English, to help debug problems. It stays on
  your machine unless **you** choose to attach it to a bug report.
- **`multiplayer-events.log`** — the optional local telemetry log (see below).
  Off by default.
- **Cached mod assets** (icons, catalog data) under
  `%LocalAppData%\AoE3ModLauncher\`.

You can clear caches and temporary files at any time from **Launcher Settings →
Maintenance**.

## What leaves your computer, and when

### 1. Update, catalog, translation and news checks

On startup (and when you press refresh) the launcher makes ordinary HTTPS
requests to:

- **GitHub** — to check for a newer launcher version, fetch the community mod
  catalog, list translations, and read the news feed.
- **Mod servers** (e.g. `aoe3wol.com`, SourceForge, GitHub Releases) — to read
  version manifests and download mod payloads you ask it to install.

As with any web request, the remote server sees your IP address. The launcher
sends no personal identifiers in these requests. **You can disable all
startup network activity** with *Launcher Settings → Updates → "Check for
updates on startup"*.

### 2. Multiplayer (opt-in — requires Discord sign-in)

Multiplayer is handled by a **self-hosted lobby server** (the maintainer's own
Node.js/Fastify deployment). Nothing below happens unless you open the
Multiplayer tab and sign in.

- **Discord sign-in (OAuth).** When you authorise the app, the lobby server
  receives your Discord **account id, username and avatar** and issues a session
  token that is cached locally in `launcher-config.json`. The launcher does not
  see your Discord password.
- **Lobbies and chat.** When you create or join a room, your display name, the
  room's mod, and your chat messages are sent to the lobby server so other
  players in the room (and, for the global chat, other signed-in users) can see
  them. Global chat history is kept only in the server's memory and is lost when
  the server restarts.
- **Mod fingerprint.** A hash of your installed mod's data files is sent so the
  server can match you only with players on the same mod version. It does not
  identify you or expose your file paths.
- **IP address.** As with any online service, the lobby server sees your IP
  address (used for rate-limiting and basic abuse prevention).

To stop sharing this data, simply do not sign in — or sign out, which clears the
cached session token.

### 3. Radmin VPN (third-party, for in-game traffic)

The actual in-game network uses **Radmin VPN by Famatech**, which you install and
manage yourself. The launcher only *assists* (it can detect, help install, and
launch the Radmin client, and copy a network name to your clipboard); it does
**not** bundle Radmin and cannot join a network on your behalf. Your use of
Radmin VPN is governed by **Famatech's own privacy policy and terms**, not this
one.

## Local telemetry log (opt-in, off by default)

The launcher can keep a small local file, `multiplayer-events.log`, with plain
event counters such as "a sign-in was attempted", "a lobby was joined", or "a
rate-limit error occurred". It contains **no message contents and no personal
data**, uses **no network and no third-party service**, and **never leaves your
computer**. Its only purpose is to help you and the maintainer diagnose
multiplayer issues if you choose to share it in a bug report.

This log is **disabled by default**. You can enable or disable it at any time in
**Launcher Settings → Privacy → "Enable local telemetry log"**.

## Third-party services

When you use the relevant features, these third parties may process data under
their own privacy policies:

- **Discord** — sign-in / identity. <https://discord.com/privacy>
- **GitHub** — update, catalog, translation and news hosting.
  <https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement>
- **Famatech Radmin VPN** — the virtual LAN for in-game traffic.
  <https://www.radmin-vpn.com/>
- **Mod distribution servers** (aoe3wol.com, SourceForge) — mod payload
  downloads, under their respective policies.

## Children

The launcher is not directed at children and does not knowingly collect data
from anyone under the age required to hold a Discord account in their
jurisdiction.

## Changes to this policy

This policy may change as the launcher evolves. Material changes will be
reflected in this file in the repository, with the "Last updated" date above.

## Contact

Questions or concerns: please
[open an issue](https://github.com/Gorgorito12/Updater/issues) on the project
repository.
