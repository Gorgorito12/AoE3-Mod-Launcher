# Disclaimer

The **AoE3 Mod Launcher** is an unofficial, fan-made tool. It is **not**
developed, endorsed, sponsored or supported by Microsoft, Ensemble Studios,
World's Edge, Forgotten Empires, Tantalus Media, Skybox Labs, or any of the
mod teams whose work this launcher helps install.

## Trademarks

*Age of Empires III* is a trademark of **Microsoft Corporation**. All
trademarks referenced by this project — including but not limited to *Age of
Empires*, *Wars of Liberty*, *Improvement Mod*, *ESOC Patch* — are the
property of their respective owners. Their inclusion here is descriptive use
to identify the games and mods this launcher works with; it does not imply
affiliation or endorsement.

## What this launcher does and does not redistribute

- **It does not redistribute Age of Empires III itself.** To use any mod
  installed by this launcher you must own a legitimate copy of *Age of
  Empires III* (retail, Steam, GOG, or Microsoft Games). The launcher
  detects an existing install and clones from it locally — it never
  downloads the base game.
- It **does** download mod payloads from each mod's official servers (or
  the mirrors those projects designate, e.g. SourceForge for Wars of
  Liberty) using exactly the same URLs and formats their own updaters use.

## Multiplayer

The integrated multiplayer feature relies on:

- **A self-hosted [Node.js](https://nodejs.org/) + [Fastify](https://fastify.dev/)
  backend** for the meta layer (Discord sign-in, lobbies, chat, matchmaking).
  It runs on the maintainer's own server; the source lives in a companion
  repository.
- **[Radmin VPN](https://www.radmin-vpn.com/)** (by Famatech) for the in-game
  traffic — a free, user-managed virtual LAN so AoE3's original LAN code can
  find peers across the public internet. The launcher only **assists** with
  Radmin (it can detect, help install, and launch the Radmin client, and copy
  a network name to the clipboard); it does **not** bundle Radmin and cannot
  join a network on the user's behalf. Radmin VPN is subject to Famatech's own
  terms of use.

Sign-in uses **Discord OAuth**; the launcher stores only the session token the
backend issues. ESO (Microsoft's original online service for AoE3) was shut
down in 2014; this launcher does not connect to it and is not a replacement for
it provided by Microsoft.

## No warranty

The launcher is provided **"AS IS"** under the Apache License 2.0 without
warranty of any kind. Mods modify game files; while the launcher backs up
overwritten files and refuses to delete AoE3's base data, you use it at
your own risk. We recommend keeping a backup of any save games or custom
content you care about before installing a mod.

## Reporting issues

If you are a rights holder and believe this project misrepresents your work
or infringes your trademarks, please open a GitHub issue or contact the
maintainers directly so the concern can be addressed.
