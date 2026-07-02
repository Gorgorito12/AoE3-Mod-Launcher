# Configuration

A `launcher-config.json` file is created in **`%LocalAppData%\AoE3ModLauncher\`**
on first run (alongside the diagnostic log, snapshots, and telemetry — all
runtime data lives there, not next to the `.exe`; a legacy next-to-exe config is
migrated over automatically). Most fields auto-populate; edit only when you need
to override defaults (custom server URLs, non-standard install paths, alternate
payload mirrors). The example below is a **partial excerpt** — the real file
carries more fields (window geometry, notification history, ETags, etc.).

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
      "activeTranslationVersion": "",
      "pinnedVersion": "",
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
  "notificationFeedUrl": "",
  "multiplayerTelemetryEnabled": false,
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

`notificationFeedUrl` is empty by default (use the built-in
`wol-notify.duckdns.org` feed); set it to `"none"` to always poll GitHub
directly, or to your own feed URL. `multiplayerTelemetryEnabled` is `false` by
default — the opt-in local `multiplayer-events.log` (see [`PRIVACY.md`](../PRIVACY.md)).
