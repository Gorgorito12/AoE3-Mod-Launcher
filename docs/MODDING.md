# Modder integration guide

> How to get **your AoE3 mod** listed in the launcher, installed,
> updated and uninstalled — without anyone touching the launcher's
> source code.

This guide describes the full contract between a mod and the launcher.
It assumes you already have a working build of your mod — files in a
folder, ideally a `.zip` published somewhere — and want to be listed
officially. With that in hand, an afternoon is enough.

---

## 1. The picture in one diagram

```
   your_mod (your repo / your CDN)      aoe3-mods-catalog (central repo)
   ┌──────────────────────────┐         ┌──────────────────────────────┐
   │ payload .zip / releases  │◀────────│ mods/<your-id>/              │
   │ UpdateInfo.xml (optional)│         │   ├─ mod.json   ← manifest   │
   │                          │         │   ├─ icon.png                │
   └──────────────────────────┘         │   └─ banner.png              │
                                        └────────────┬─────────────────┘
                                                     │
                                          24 h cache │ raw.githubusercontent
                                                     ▼
                                        ┌──────────────────────────────┐
                                        │ Launcher (Aoe3ModLauncher)   │
                                        │  · ModCatalogService fetch   │
                                        │  · ModRegistry merge         │
                                        │  · UI: mod card + install    │
                                        └──────────────────────────────┘
```

Key points:

- **You don't touch the launcher's code.** Your mod enters the
  ecosystem through a pull request to the central catalog repo
  `Gorgorito12/aoe3-mods-catalog`, NOT to the `Updater` repo. The
  launcher fetches the catalog every 24 h and surfaces new entries
  automatically.
- **One file decides everything: `mod.json`.** That manifest describes
  your mod, where it installs, how it updates, how it runs. The rest
  of the flow is derived from it.
- **The mod binary lives wherever you want** (GitHub Releases, your own
  CDN, SourceForge, …) — the catalog only stores metadata + URLs.
- **CI in the catalog validates your PR** against the schema and checks
  the icon/banner specs. Cosmetic changes auto-merge; critical ones go
  through human review (the "tier" system, §6).

---

## 2. Three ways to publish

Pick whichever feels most comfortable; the end result is the same PR.

### 2.1. In-app wizard — the recommended path

In the launcher: **Mods tab → "Publish my mod"** button. A 6-step
wizard asks for every schema field with inline validation:

1. **Identity** — `id`, `displayName`, `author`, `subtitle`.
2. **Look & feel** — `accentColor`, `icon`, `banner`.
3. **Install** — `type`, `defaultFolder`, `probeFile`, `executable`,
   `arguments`, `marker`, plus an *Advanced* section (`installProductGuid`,
   `payloadUrls`, `payloadSha256`, `userDataFolder`).
4. **Updates** — `mechanism` and its dependent fields: the WoL subpanel
   (`updateInfoUrl`, `updateInfoUrlAlt`, `payloadZipUrls`, `payloadSha256`), the
   GitHub subpanel (`sourceRepo`, `approvedReleaseTag`, and Advanced
   `externalAssetUrlTemplate` / `externalAssetSha256`), **and the `translations`
   block (`repo`, `coveredFiles`) — collected here, not on a separate step**.
5. **Description & website** — `description.en`, `description.es`,
   `officialWebsite`.
6. **Review** — preview of the generated `mod.json`. Two buttons:
   **Copy JSON** (copy to clipboard) and **Open PR on GitHub** (opens
   `https://github.com/Gorgorito12/aoe3-mods-catalog/new/main` with
   the path `mods/<your-id>/mod.json` and the content pre-populated).

Advantage: impossible to invent a field or break a regex — the wizard
uses the same expressions as the schema and warns before you submit.

### 2.2. Direct pull request

If you'd rather work in your editor:

```
git clone https://github.com/Gorgorito12/aoe3-mods-catalog
cd aoe3-mods-catalog
mkdir -p mods/<your-id>
$EDITOR mods/<your-id>/mod.json
cp ~/your-mod/icon.png    mods/<your-id>/icon.png
cp ~/your-mod/banner.png  mods/<your-id>/banner.png
git checkout -b add-<your-id>
git add mods/<your-id>
git commit -s -m "Add <your-id> to catalog"
git push origin add-<your-id>
```

Point `$schema` in your `mod.json` at the schema in the repo to get
IntelliSense in VS Code:

```json
"$schema": "https://raw.githubusercontent.com/Gorgorito12/aoe3-mods-catalog/main/schema/mod.schema.json"
```

### 2.3. Fork + edit on github.com

**Fork** `Gorgorito12/aoe3-mods-catalog`, open your fork, **Add file →
Create new file**, type the path `mods/<your-id>/mod.json`, paste your
JSON and open a PR. Upload the assets in follow-up commits (**Add file
→ Upload files**, target `mods/<your-id>/`).

---

## 3. Anatomy of `mod.json`

Full schema at
[`aoe3-mods-catalog-template/schema/mod.schema.json`](../aoe3-mods-catalog-template/schema/mod.schema.json).
What follows is the same content in the order you usually fill it in,
with the real constraints the schema enforces.

### 3.1. Identity

| Field | Required | Constraints | Notes |
|---|---|---|---|
| `id` | yes | `^[a-z][a-z0-9-]{1,30}$` | Must match the folder name under `/mods/`. It's the *primary key* — changing it later breaks existing installs. Pick well. |
| `displayName` | yes | 1–50 chars | What shows on the launcher card. Uppercase, spaces and accents are allowed. |
| `subtitle` | no | ≤ 50 chars | Small line under the title (e.g. *"AoE3:TAD overhaul"*). |
| `author` | no | ≤ 100 chars | Team or author name. |
| `officialWebsite` | no | `^https?://` | Opened in the user's browser, nothing is downloaded from here. HTTP allowed for legacy sites; HTTPS preferred. |

### 3.2. Look & feel

| Field | Constraints | Physical specs |
|---|---|---|
| `accentColor` | `^#[0-9a-fA-F]{6}$` | Card border, badges, and the synthetic banner gradient when no banner is supplied. |
| `icon` | filename ending in `.png` | **Square (1:1), width 256–1024 px, PNG with alpha, ≤ 1 MB.** Validated in CI by aspect + width range (a non-square fails). |
| `banner` | filename `.png/.jpg/.jpeg` | **4:1 aspect, width 1200–4800 px** (e.g. 1200×300, 2400×600, 4800×1200), **≤ 2 MB.** |
| `heroImage` | filename `.png/.jpg/.jpeg` | **16:9 aspect, width 1920–3840 px (1080p up to 4K), ≤ 5 MB** (use JPEG for 4K — a 4K PNG can be 10 MB+). Dashboard background; keep the subject in the **right half** (left is covered by the title + PLAY button). |
| `heroImages` | array of filenames | **Rotating dashboard heroes (2–6).** Each follows the `heroImage` spec. When 2+ are listed the dashboard cycles them with a crossfade (~7 s each); takes precedence over `heroImage`. |
| `screenshots` | array of filenames `.png/.jpg/.jpeg/.gif` | **Workshop gallery (max 8). No fixed dimensions, ≤ 5 MB each.** Animated GIFs allowed **here only**. |

> Dimensions are validated by **aspect ratio + a width range**, not a single exact size — so any resolution up to 4K passes as long as the shape is right.

> **Auto-merge note:** icon, banner, single hero and screenshot edits all
> auto-merge (tier 1) as long as the files use the conventional names
> (`icon.png`, `banner.*`, `hero.*`, `screenshot1..8.*`). The one exception
> is **rotating `heroImages`**: their filenames are free-form, so they are
> not on the auto-merge asset whitelist and require human review (tier 3) —
> see §6.3.

The files live in `mods/<your-id>/` next to `mod.json`. The launcher
resolves `icon: "icon.png"` to
`https://raw.githubusercontent.com/Gorgorito12/aoe3-mods-catalog/main/mods/<your-id>/icon.png`
and caches them on disk (`ModAssetCacheService`).

The cache is **stale-while-revalidate**: if you **replace** an icon/banner
(same filename, new bytes), launchers pick up the new image on their next
refresh (a conditional `ETag` request detects the change) — they don't keep
the old one forever. If you **delete** an asset from the catalog, the cached
copy is purged (a `404` is treated as a definitive removal). So editing your
artwork in the catalog repo is enough; users don't need to clear anything by
hand. (Cached files are only kept on a transient network error, never on a
clean `404`.)

### 3.3. Multilingual descriptions

```json
"description": {
  "en": "Total-conversion mod for AoE3, set in 19th-century colonial wars.",
  "es": "Mod de conversión total para AoE3, ambientado en guerras coloniales del siglo XIX."
}
```

Keys are ISO 639-1 codes. The launcher picks one based on UI language
and falls back to `en` if the user's language isn't present. 500 chars
max per language.

### 3.4. `install` — how the mod is installed

```json
"install": {
  "type": "IsolatedFolder",
  "defaultFolder": "C:\\Program Files (x86)\\My Mod",
  "probeFile": "data\\stringtable.xml",
  "marker": "data\\mymod_marker.xml",
  "executable": "age3m.exe",
  "arguments": ""
}
```

| Field | Meaning |
|---|---|
| `type` | `IsolatedFolder` or `InPlaceOverlay`. Details in §4. |
| `defaultFolder` | Suggested path for the install dialog. Empty for `InPlaceOverlay` — the launcher uses the detected AoE3 path. |
| `probeFile` | Relative path the launcher checks to confirm the mod is installed at a given location (`File.Exists(install + probeFile)`). Pick something **unique** to your mod — `age3y.exe` exists in vanilla AoE3 too. If you can't (your mod patches a base-game file rather than adding a new one), declare a `marker` as well. |
| `marker` | *Optional.* Relative path (file **or** directory) that is unique to your mod and absent from vanilla AoE3. When set, the launcher detects your mod **by content in a folder with any name** (the install folder no longer has to be named after the mod) and uses it to tell a real install apart from the base game. Needed only when `probeFile` is shared with AoE3 — WoL uses `art\\zulushield`, because its probe `data\\stringtabley.xml` also ships in vanilla. |
| `executable` | Filename of the .exe that launches the game (`age3y.exe` for WoL, `age3m.exe` for Improvement Mod). The launcher looks for it inside the install folder. |
| `arguments` | Extra args the launcher appends when running. Usually empty. |
| `payloadUrls` | Array of HTTPS URLs for the initial install zip (multi-part `.zip.001`, `.002`, … listed in order). **Reserved — the current launcher does NOT read this.** It's schema-valid and the publish wizard collects it, but the install pipeline sources the initial payload from the **`update` block** instead (a GitHubReleases release asset / `externalAssetUrlTemplate`, or `update.wol.payloadZipUrls`). Declare your payload there. |
| `payloadSha256` | Parallel array to `payloadUrls` with each part's SHA-256. **Also reserved / not verified today** (the launcher doesn't consume `payloadUrls`). For an actually-enforced hash, use `update.github.externalAssetSha256` (§5.1). |

> **Where the initial payload actually comes from:** GitHubReleases mods get it from the
> release asset on `approvedReleaseTag` (or `externalAssetUrlTemplate`); WolPatcher mods get it
> from `update.wol.payloadZipUrls`. `install.payloadUrls`/`payloadSha256` are declared-but-unused
> at the moment, so a `Manual` mod that only sets `install.payloadUrls` can't be installed yet.

### 3.5. `update` — how files are kept up to date

```json
"update": {
  "mechanism": "GitHubReleases",
  "github": { "externalAssetUrlTemplate": "...", "externalAssetSha256": "..." }
}
```

`mechanism` is an enum with four values. Details in §5.

### 3.6. Advanced optional fields

| Field | When to use |
|---|---|
| `sourceRepo` | `owner/repo` of your GitHub repository. **Required** if `update.mechanism = GitHubReleases`. Informational for other mechanisms. |
| `approvedReleaseTag` | Approved release tag for `GitHubReleases`. Bumping this is the normal way to ship a new version (auto-merge, §6) — unless your mod opts into `update.github.followLatest` (§5.1), in which case it stays as the first-install seed + API-failure fallback and you rarely touch it again. |
| `installProductGuid` | Stable Add/Remove Programs key (`HKLM\…\Uninstall\<here>`). If you have a pre-existing installer with its own GUID, put it here to stay compatible. Otherwise omit and the launcher derives `<id>_launcher`. |
| `userDataFolder` | Folder name under `Documents\My Games\<here>\` where your mod stores saves/replays. When set, the launcher enables the pre-install backup prompt and exposes "Open / Create backup / Restore backup" in the gear menu. Omit if your mod reuses vanilla AoE3's user-data folder. |
| `install.userDataRedirect` | `true` **only** if your mod writes to the SHARED `My Games\Age of Empires 3\` folder (it doesn't ship a build that already isolates its saves like WoL / Improvement Mod do). The launcher then junction-redirects the standard folder to your `userDataFolder` while your mod runs, and restores the real vanilla folder otherwise — so your saves don't mix with vanilla. Requires a non-empty `userDataFolder`. Leave it off (default) if your build already writes to its own `My Games` folder. |
| `translations` | `{ "repo": "owner/repo", "folderRepo": "owner/repo", "coveredFiles": [...] }` so the launcher lists community translations. `folderRepo` hosts packs as **files** under `translations/<id>/` on main (recommended); `repo` hosts them as **releases** (legacy). The launcher reads BOTH (dual mode). Only meaningful if your mod uses the same overlay scheme as WoL (files under `data\`). See §8.x below. |

---

## 4. Install types (`install.type`)

### 4.1. `IsolatedFolder` — the default choice

The launcher **clones the entire AoE3 install** into a new folder and
overlays your mod on top. Result: the user's original AoE3 stays
untouched, your mod lives in isolation, and both can coexist.

Internal steps:

1. Detect AoE3 (Steam / GOG / retail).
2. Clone the AoE3 folder to `defaultFolder`.
3. Flatten `bin\` to the root (Steam layout) by copying its contents up,
   then delete `bin\` (reclaims ~3.7 GB of duplicated files).
4. Extract your payload on top.
5. Write shortcuts, registry entry and `install-manifest.json`.

Use it when:

- Your mod is a **total conversion** (WoL, Napoleonic Era, …).
- You want users to not feel the install is touching their AoE3.
- Your executable is different from vanilla (e.g. `age3y.exe`, `age3m.exe`).

Real example: `aoe3-mods-catalog-template/mods/wol/mod.json`.

### 4.2. `InPlaceOverlay` — on top of AoE3

Files are extracted **directly over the existing AoE3 install**. No
cloning; the mod and AoE3 share a folder.

Internal steps:

1. Detect AoE3.
2. Back up every file about to be overwritten.
3. Extract your payload on top.
4. Write `install-manifest.json` so an uninstall can revert.

Use it when:

- Your mod is a **lightweight patch/overhaul** touching few files.
- It's acceptable that the user's vanilla AoE3 ends up modified (they
  understand that going back to vanilla means uninstalling).

**Caveat:** this mode modifies the user's AoE3. Declare
`payloadSha256` and list exactly which files your mod ships — the
uninstaller uses that to clean up.

---

## 5. Update mechanisms (`update.mechanism`)

> **Notifications are automatic — you don't configure anything.** Once your mod is
> in the catalog with `update` filled in (and, optionally, `translations`), the
> launcher's notification bell tells **every** user when you ship a new version or a
> new translation. You don't touch any server or notification setting: a small
> central service reads your `mod.json` from the catalog and figures out your latest
> version (from your GitHub releases for `GitHubReleases`, or your `UpdateInfo.xml`
> for `WolPatcher`) and your published translations (from your `translations.repo`
> **and** `translations.folderRepo`). Publish to the catalog and you're done — see
> §6 for how a version bump ships.

> **Publishing a translation (the simple way).** Use the launcher's **Settings →
> Packager** to build a pack; it produces a ready
> `translations/<id>/<version>/` folder. Then **commit that folder on the `main`
> branch** of your `translations.folderRepo` (push or open a PR) — no GitHub
> release, no separate asset upload. The launcher discovers folder packs via the
> Git Trees API and keys them by a **content hash** baked into `translation.json`.
> Each export is a **new version subfolder**, so a history accumulates append-only:
> the launcher groups versions of one language into a single menu entry with a
> **version picker** (latest 10), uses the newest for the menu/notification, and
> lets users roll back to an older one. (Committing over a single
> `translations/<id>/translation.json` also works if you want one live version.)
> Releases on `translations.repo` still work too (dual mode), so existing packs
> keep showing while you migrate.

Decision tree:

```
Does your mod ship versions as GitHub Releases?
├─ yes ───────────────────────────────▶ GitHubReleases
└─ no
   ├─ Do you have UpdateInfo.xml + incremental .tar.xz patches?
   │   └─ yes ────────────────────────▶ WolPatcher
   ├─ Do you have your own external updater that runs with the game?
   │   └─ yes ────────────────────────▶ DelegatedExternal
   └─ none ────────────────────────────▶ Manual
```

### 5.1. `GitHubReleases` — recommended for new mods

The launcher pins to a **release tag** on your repo (`sourceRepo`).
When you ship v1.1, you open a PR to the catalog that **only** changes
`approvedReleaseTag: "v1.0"` → `"v1.1"`. That's a "Tier 2" change and
auto-merges (§6).

```json
"sourceRepo": "youruser/your-mod",
"approvedReleaseTag": "v1.0",
"update": { "mechanism": "GitHubReleases" }
```

By default the launcher downloads **the first `.zip` asset** on the
release tag. If you want to host the payload outside GitHub Releases
(your own CDN, S3, …) but keep the tag as the version marker, declare:

```json
"update": {
  "mechanism": "GitHubReleases",
  "github": {
    "externalAssetUrlTemplate": "https://your-cdn.com/your-mod-{tag}.zip",
    "externalAssetSha256": "aabbcc...64-hex"
  }
}
```

The literal `{tag}` is replaced with `approvedReleaseTag` at download
time. **`externalAssetSha256` is mandatory** when you set the template
— the launcher refuses to install from an external host without a
hash, because GitHub no longer underwrites the authenticity.

#### Follow latest (`update.github.followLatest`)

If bumping `approvedReleaseTag` per release feels like overhead, opt
into **follow-latest**: the launcher resolves your mod's latest version
from `GET /repos/{sourceRepo}/releases/latest` — the same mechanism the
launcher uses for its own self-update — and offers/installs it with
**no catalog PR per version**. You publish a release, users get it.

```json
"sourceRepo": "youruser/your-mod",
"approvedReleaseTag": "v1.0",
"update": {
  "mechanism": "GitHubReleases",
  "github": { "followLatest": true }
}
```

Rules and trade-offs:

- **Stable releases only.** The `/releases/latest` endpoint excludes
  drafts and prereleases by definition, so marking a release as a
  *pre-release* on GitHub keeps it away from users until you promote it.
- **`approvedReleaseTag` is still required.** It seeds a first install
  when the launcher has never resolved your latest (e.g. offline) and is
  the fallback whenever the GitHub API is unreachable. Keep it pointing
  at a known-good version; you don't need to bump it every release.
- **Keep shipping the full `.zip` on every release** — follow-latest
  changes which tag is targeted, not what's downloaded. Delta patches
  (§ below) compose: ship `patch-<from>-to-<to>.zip`/`.json` on the new
  release and single-hop updates apply the delta toward the latest.
- **Not available with external hosting** (`externalAssetUrlTemplate`):
  the catalog-pinned SHA-256 only covers the approved tag, so other tags
  can't be verified. The flag is ignored in that case.
- **Security trade-off:** your releases skip the per-version catalog
  approval gate. Enabling the flag is a Tier 3 catalog change (reviewed
  once by a human); after that, whatever you release is what users get —
  which is why the flag is opt-in per mod rather than the default.

#### Removing files in an update (deletion)

Each release `.zip` is your mod's **complete overlay**. When the user
updates, the launcher extracts the new `.zip` on top of their install,
adding and overwriting files. To **remove** files an old version shipped
but the new one shouldn't, there are two ways — and you can use both:

1. **Automatic (net-new files).** If you stop shipping a file that *you*
   added (one that did **not** exist in the base game), the launcher
   deletes it automatically on update. You don't declare anything — just
   leave it out of the new `.zip`. Files that **overwrite** a base-game
   file are never auto-deleted (removing one would leave a hole the engine
   expects → broken game), so those stay until you say otherwise.

2. **Explicit (`delete.lst`).** Ship a plain-text `delete.lst` at the root
   of your `.zip` with **one relative path per line** (`#` starts a
   comment). The launcher deletes exactly those paths, then removes the
   `delete.lst` itself. Use this for files you can't express as "net-new
   I dropped" — e.g. removing a folder you no longer use.

   ```
   # delete.lst — remove files this version drops
   data/old_unit.xml
   art/legacy/banner.tga
   ```

> ⚠️ **`delete.lst` DELETES — it does not revert.** Listing a file your
> mod *overwrote* from the base game **removes** it, leaving a hole where
> the game expects it → broken install. `delete.lst` is only for files
> that should stop existing. To return a base-game file to its original
> (vanilla) bytes, **re-pack those original bytes in your `.zip`** so the
> launcher overwrites it back — never list it in `delete.lst`.

Deletions are backed up before they run, so a failed update rolls back.
None of this applies to Wars of Liberty, which uses its own
`WolPatcher` delete-list pipeline (§5.2).

#### Incremental delta patches (optional)

By default every `GitHubReleases` update re-downloads the **whole** overlay
`.zip` (see the trade-off above). If your overlay is large and you patch often,
you can opt into **incremental delta patches** so returning users download only
the files that changed — a GitHub-native alternative to WoL's `WolPatcher`
pipeline, with no `UpdateInfo.xml` server to run.

**When to use it.** Big overlay + frequent small updates → worth it. Small mod
or rare updates → the full `.zip` is simpler; skip this. It's **opt-in and
purely additive**: nothing changes unless you turn it on and ship a patch.

**Requirements.**
- `update.mechanism` = `GitHubReleases`, payload hosted **on GitHub** (not an
  external `externalAssetUrlTemplate` CDN — those always use the full path).
- `"deltaPatches": true` inside `update.github` in your catalog `mod.json` (a
  Tier-3 change, reviewed once — see §6.3).

**The recipe (per new release):**

1. Build your new full overlay `.zip` exactly as always — **you still upload
   this** (fresh installs and everyone who skipped a version need it).
2. In the launcher: **Launcher Settings → Packager → "Generate patch"**. Pick
   the **old** release's overlay `.zip`, your **new** overlay `.zip`, and type
   the two tags (`from` = previous release tag, `to` = new tag). It writes
   `patch-<from>-to-<to>.zip` (only the changed/added files) and
   `patch-<from>-to-<to>.json` (the descriptor, with hashes filled in for you).
3. Create the GitHub release for the new tag and upload **three** assets: the
   full `.zip` **+** the `patch-*.zip` **+** the `patch-*.json`.
4. Open the usual catalog PR bumping `approvedReleaseTag` (Tier 2, auto-merges).
   Setting `deltaPatches: true` is a one-time change.

Result: a user on the previous version downloads the small patch; everyone else
(fresh install, or who skipped versions) downloads the full `.zip` — the
launcher decides automatically.

**The descriptor** (`patch-*.json`, written by the tool — you don't hand-edit it):

```json
{
  "fromTag": "v1.0",
  "toTag": "v1.1",
  "payload": "patch-v1.0-to-v1.1.zip",
  "payloadSha256": "…",
  "changed": [ { "path": "data/protoy.xml", "fromSha256": "…", "sha256": "…" } ],
  "deleted": [ "data/old_unit.xml" ]
}
```

**Deletions are automatic.** `deleted` is computed from the diff (files your old
overlay had and the new one doesn't) — you don't hand-write a delete list like
WoL's `_delete.lst`. The launcher only removes files your mod *added* (net-new);
a file that overwrote a base-game file is never auto-deleted (that would leave a
hole). To revert a base file to vanilla, re-pack its original bytes in the new
overlay (same rule as §5.1's `delete.lst`).

**Guarantees (why it's safe):**
- **Single-hop.** A delta only applies when you're updating from the
  *immediately-previous* version. Skipped versions → full download.
- **Full fallback, always.** Any problem — no patch on the release, a diverged
  install, a hash mismatch, a network hiccup, an external-hosted mod — silently
  falls back to the full download. A delta can never make an update *worse* than
  today, only faster when it works.
- **Byte-identical result.** After a delta your install is identical to one that
  did the full update, so **multiplayer version-matching is unaffected**.
- **Hashes are optional but the tool always includes them** (extra verification;
  when absent the launcher trusts GitHub's CDN, exactly like the full `.zip`).

**Limitations:** always upload the full `.zip` too; external-hosted mods can't
use deltas; the arbitrary version picker (Mod Properties) always uses the full
path.

### 5.2. `WolPatcher` — for mods already running the legacy pipeline

What Wars of Liberty uses: an `UpdateInfo.xml` on the mod's server
lists versions, each with an incremental `.tar.xz` patch.

```json
"update": {
  "mechanism": "WolPatcher",
  "wol": {
    "updateInfoUrl": "http://your-mod.com/updates/UpdateInfo.xml",
    "updateInfoUrlAlt": "http://mirror.example.com/UpdateInfo.xml",
    "payloadZipUrls": ["https://github.com/.../payload.zip.001", "...002"],
    "payloadSha256": ["...", "..."]
  }
}
```

The launcher:

1. Hashes `data\protoy.xml`, `data\techtreey.xml`,
   `data\stringtabley.xml` to identify the installed version.
2. Applies every pending patch from `minreqdownload` upwards.
3. Verifies CRC32 of each patch before applying.
4. Backs up files before overwriting.

`UpdateInfo.xml` format reference: see
`WarsOfLibertyLauncher/Models/UpdateInfo.cs`.

### 5.3. `DelegatedExternal` — your mod has its own updater

The launcher stays out of the way: installs the initial payload, and
on each play session runs your `.exe` — if it spawns its own updater
(Improvement Mod's `age3m.exe` style), that's your problem now.

```json
"update": { "mechanism": "DelegatedExternal" }
```

### 5.4. `Manual` — no automated updates

The launcher lists the mod and never tries to update it. Useful for demos,
prototypes, or mods whose update story isn't decided yet. **Note:** the launcher
currently has no automatic install path for a pure `Manual` mod — it sources the
initial payload from the `update` block (GitHubReleases asset or
`update.wol.payloadZipUrls`), and `install.payloadUrls` is not consumed yet
(§3.4). So `Manual` today means "listed, not auto-installed/updated"; pick
`GitHubReleases` if you want the launcher to install and update your mod.

```json
"update": { "mechanism": "Manual" }
```

---

## 6. Security model

Three layers:

### 6.1. Schema validation (CI)

`ajv validate` runs on every PR against `schema/mod.schema.json`. It
rejects manifests with unknown fields (`additionalProperties: false`),
non-matching regexes, exceeded lengths, schemeless URLs, and so on.

### 6.2. SHA-256 hashes

| Field | Mandatory when | Enforced today? |
|---|---|---|
| `install.payloadSha256` | never | **No** — `install.payloadUrls` isn't consumed (§3.4), so this is reserved. |
| `update.wol.payloadSha256` | never | **Not yet** — the WoL catalog SHA isn't wired through to the download verifier. Safe to declare (future-proof), but don't rely on it as a guarantee today. |
| `update.github.externalAssetSha256` | **always**, when `externalAssetUrlTemplate` is set | **Yes** — verified after download; a mismatch aborts the install. |

The only hash the launcher **enforces today** is
`update.github.externalAssetSha256` (external-host GitHubReleases). It verifies
the download and aborts on mismatch — catching tampering even if the host was
compromised after the PR was approved. For plain GitHub-Releases assets the
launcher trusts GitHub's CDN (no hash needed). The other two SHA fields are
declared-but-not-verified for now (see the caveats above).

### 6.3. Tier system — what auto-merges and what doesn't

The `classify_pr.py` script classifies every PR by which fields it
touches:

| Tier | Fields modified | Action |
|---|---|---|
| **invalid** | Files outside `/mods/`, multiple mods at once, malformed JSON, unknown filenames | PR is blocked with an explanatory comment |
| **tier1** | Only: `displayName`, `subtitle`, `description`, `accentColor`, `author`, `officialWebsite`, `icon`, `banner`, `heroImage`, `screenshots` | **Auto-merge** after validation |
| **tier2** | Only: `approvedReleaseTag` (version bump) | **Auto-merge** after validation |
| **tier3** | Anything in: `id`, `sourceRepo`, `install.*`, `update.*`, `translations`, OR a first-time submission | Labelled `needs-manual-review` + comment; maintainer reviews manually |

What this means for you as a modder:

- **Your first submission is always tier 3.** Expect to wait for
  review.
- **Changing icon / banner / hero / screenshots / text** later:
  auto-merge within minutes, as long as the image files use the
  conventional names on the asset whitelist (`icon.png`, `banner.*`,
  `hero.*`, `screenshot1..8.*`).
- **Rotating `heroImages` are the exception**: their filenames are
  free-form, so the field and its files are not on the auto-merge
  whitelist and land in tier 3 (human review). CI still *validates*
  them, it just doesn't auto-merge.
- **Shipping a new version** (bumping `approvedReleaseTag`):
  auto-merge.
- **Changing URLs, hashes, or `install.*`**: human review, always. This
  is by design — it controls what the launcher downloads.

---

## 7. End-to-end flow: from zero to published

```
                                      ┌────────────────────────────┐
                                      │ Your repo / CDN already    │
                                      │ hosts the payload          │
                                      └─────────────┬──────────────┘
                                                    │
                                                    ▼
┌─────────────────────────┐    ┌────────────────────────────────────┐
│ In-app wizard or your   │    │ Compute SHA-256 of each payload    │
│ editor (write mod.json) │───▶│   certutil -hashfile payload.zip   │
│                         │    │   SHA256                           │
└─────────────────────────┘    └─────────────┬──────────────────────┘
                                             │
                                             ▼
                               ┌────────────────────────────────────┐
                               │ PR to Gorgorito12/aoe3-mods-catalog│
                               │   mods/<your-id>/mod.json          │
                               │   mods/<your-id>/icon.png          │
                               │   mods/<your-id>/banner.png        │
                               └─────────────┬──────────────────────┘
                                             │
                                             ▼
                               ┌────────────────────────────────────┐
                               │ CI: classify → validate            │
                               │   first submission → tier 3        │
                               └─────────────┬──────────────────────┘
                                             │
                                             ▼
                               ┌────────────────────────────────────┐
                               │ Maintainer reviews and merges      │
                               └─────────────┬──────────────────────┘
                                             │
                                             ▼
                               ┌────────────────────────────────────┐
                               │ Next catalog refresh in users'     │
                               │ launchers (≤24 h cache).           │
                               │ Your mod shows up in the UI.       │
                               └────────────────────────────────────┘
```

Once merged, you don't need to do anything else: existing launchers
will see your mod automatically when their cache expires
(`ModCatalogService.CacheTtl = 24h`).

---

## 8. Real-world examples

### 8.1. Total conversion with WolPatcher (WoL)

```json
{
  "$schema": "https://raw.githubusercontent.com/Gorgorito12/aoe3-mods-catalog/main/schema/mod.schema.json",
  "id": "wol",
  "displayName": "Wars of Liberty",
  "subtitle": "Launcher",
  "accentColor": "#c8102e",
  "author": "Wars of Liberty Team",
  "officialWebsite": "http://aoe3wol.com/",
  "description": {
    "en": "Total-conversion mod for AoE3, set in 19th-century colonial wars.",
    "es": "Mod de conversión total para AoE3, ambientado en las guerras coloniales del siglo XIX."
  },
  "install": {
    "type": "IsolatedFolder",
    "defaultFolder": "C:\\Program Files (x86)\\Wars of Liberty",
    "probeFile": "data\\stringtabley.xml",
    "executable": "age3y.exe",
    "arguments": ""
  },
  "update": {
    "mechanism": "WolPatcher",
    "wol": {
      "updateInfoUrl": "http://aoe3wol.com/updates/UpdateInfo.xml",
      "updateInfoUrlAlt": "http://master.dl.sourceforge.net/project/wars-of-liberty/Patches/UpdateInfo.xml",
      "payloadZipUrls": [
        "https://github.com/papillo12/Updater/releases/download/updater/WolPayload.zip.001",
        "https://github.com/papillo12/Updater/releases/download/updater/WolPayload.zip.002",
        "https://github.com/papillo12/Updater/releases/download/updater/WolPayload.zip.003"
      ]
    }
  },
  "translations": {
    "repo": "papillo12/translations",
    "coveredFiles": [
      "data\\stringtabley.xml",
      "data\\unithelpstringsy.xml"
    ]
  }
}
```

### 8.2. Overhaul with GitHubReleases (Improvement Mod)

```json
{
  "$schema": "https://raw.githubusercontent.com/Gorgorito12/aoe3-mods-catalog/main/schema/mod.schema.json",
  "id": "improvement-mod",
  "displayName": "Improvement Mod",
  "subtitle": "AoE3:TAD overhaul",
  "accentColor": "#3a8cd9",
  "description": {
    "en": "Overhaul mod for AoE3:TAD. The launcher clones your AoE3 install into a separate folder and overlays the latest release on top — your original AoE3 stays untouched.",
    "es": "Mod de mejora para AoE3:TAD. El launcher clona AoE3 en una carpeta separada y aplica encima la última release — tu AoE3 original queda intacto."
  },
  "sourceRepo": "papillo12/Improvement-Mod",
  "approvedReleaseTag": "Improvement-Mod",
  "install": {
    "type": "IsolatedFolder",
    "defaultFolder": "C:\\Program Files (x86)\\Improvement Mod",
    "probeFile": "age3m.exe",
    "executable": "age3m.exe",
    "arguments": ""
  },
  "update": { "mechanism": "GitHubReleases" }
}
```

### 8.3. New mod on GitHub with hashes and an external CDN

```json
{
  "$schema": "https://raw.githubusercontent.com/Gorgorito12/aoe3-mods-catalog/main/schema/mod.schema.json",
  "id": "napoleonic-era",
  "displayName": "Napoleonic Era",
  "author": "Napoleonic Era Team",
  "accentColor": "#1f4e79",
  "icon": "icon.png",
  "banner": "banner.png",
  "description": {
    "en": "Napoleonic-era total conversion for AoE3.",
    "es": "Conversión total ambientada en la era napoleónica para AoE3."
  },
  "sourceRepo": "napo-team/napoleonic-era",
  "approvedReleaseTag": "v2.3.0",
  "userDataFolder": "Napoleonic Era",
  "install": {
    "type": "IsolatedFolder",
    "defaultFolder": "C:\\Program Files (x86)\\Napoleonic Era",
    "probeFile": "data\\protonapoleonic.xml",
    "executable": "age3y.exe"
  },
  "update": {
    "mechanism": "GitHubReleases",
    "github": {
      "externalAssetUrlTemplate": "https://cdn.napoleonic-era.com/builds/napoleonic-{tag}.zip",
      "externalAssetSha256": "5d41402abc4b2a76b9719d911017c592aa8f1b9c2b4e8a3f1e0c9b8a7f6e5d4c"
    }
  }
}
```

---

## 9. Common errors (and how CI catches them first)

| PR symptom | Cause | Fix |
|---|---|---|
| `ajv: id should match pattern "^[a-z]…"` | `id` has uppercase, spaces or odd characters | Use only `a-z0-9-`, must start with a letter |
| `ajv: install.type should be one of …` | Typo in the enum (e.g. `"isolated"`) | `IsolatedFolder` or `InPlaceOverlay`, case-sensitive |
| `ajv: additionalProperties` | You added a field the schema doesn't know | Remove it, or propose adding it to the schema in a separate PR |
| `validate_images: icon … aspect …` | Icon isn't square or is outside 256–1024 px | Make it 1:1 within the width range |
| `validate_images: … exceeds limit` | Image is over its weight cap (icon 1 MB / banner 2 MB / hero & screenshots 5 MB) | Compress, or use JPEG for 4K |
| PR marked **invalid** | You touched something outside `/mods/<your-id>/`, or more than one mod at once | One PR per mod. Schema changes go in a separate PR |
| PR not auto-merging even though you only changed `displayName` | First-time submission — always tier 3 for safety | Wait for the maintainer; later cosmetic PRs auto-merge |
| Launcher doesn't show my mod after merge | 24 h cache hasn't expired yet | Delete `%LocalAppData%\AoE3ModLauncher\catalog-cache.json` to force a refresh |

---

## 10. What you **don't** need to do (and people often try)

- **Don't edit `WarsOfLibertyLauncher/Services/ModRegistry.cs`.** That
  class hardcodes only the two first-party built-ins — WoL and the
  detect-only stock game (`aoe3-tad`) — as an offline fallback. Your
  mod goes in the catalog. Editing `ModRegistry` directly would mean
  your mod needs a new launcher release to appear — the opposite of
  what the system is designed for.
- **Don't upload the mod payload to the catalog.** The catalog repo
  holds only metadata (`mod.json` + small assets). The binary lives in
  GitHub Releases / your CDN.
- **Don't declare `update.github.externalAssetSha256` without actually
  computing the hash.** That's the one hash the launcher enforces — a
  placeholder or wrong value means it refuses to install for every user.
  (The `install.payloadSha256` / `update.wol.payloadSha256` fields aren't
  verified today — see §6.2 — so a bad value there is silently ignored, which
  is arguably worse; only set them to real hashes.)
- **Don't reuse someone else's `id`.** Even though the schema regex
  doesn't forbid it, CI rejects the PR if `mods/<id>` already exists
  and you're not its CODEOWNER.
- **Don't put uppercase or spaces in `id`** "because it looks nicer".
  `displayName` is the user-facing string; `id` is a technical
  identifier.

---

## 11. Code references

If you want to understand what the launcher does with your `mod.json`:

| File | Role |
|---|---|
| [`WarsOfLibertyLauncher/Models/ModCatalogManifest.cs`](../WarsOfLibertyLauncher/Models/ModCatalogManifest.cs) | DTO that maps 1:1 to `mod.json` |
| [`WarsOfLibertyLauncher/Services/ModCatalogService.cs`](../WarsOfLibertyLauncher/Services/ModCatalogService.cs) | Catalog fetch + cache |
| [`WarsOfLibertyLauncher/Services/ModRegistry.cs`](../WarsOfLibertyLauncher/Services/ModRegistry.cs) | Projection to `ModProfile` and merge with built-ins |
| [`WarsOfLibertyLauncher/Models/ModProfile.cs`](../WarsOfLibertyLauncher/Models/ModProfile.cs) | Runtime model the rest of the launcher uses |
| [`WarsOfLibertyLauncher/Services/NativeInstallService.cs`](../WarsOfLibertyLauncher/Services/NativeInstallService.cs) | Initial install pipeline |
| [`WarsOfLibertyLauncher/Services/UpdateService.cs`](../WarsOfLibertyLauncher/Services/UpdateService.cs) | Update flow (WolPatcher) |
| [`WarsOfLibertyLauncher/Services/GitHubReleaseDownloader.cs`](../WarsOfLibertyLauncher/Services/GitHubReleaseDownloader.cs) | Asset resolve + download (GitHubReleases) |
| [`aoe3-mods-catalog-template/schema/mod.schema.json`](../aoe3-mods-catalog-template/schema/mod.schema.json) | Authoritative schema |
| [`aoe3-mods-catalog-template/.github/scripts/classify_pr.py`](../aoe3-mods-catalog-template/.github/scripts/classify_pr.py) | Tier classifier |

---

## 12. What if I need something the schema doesn't support?

Open an issue on the launcher repo describing the use case. The schema
is versioned deliberately: adding a new field is a coordinated change
across the launcher, the schema and the tier classifier. The good
news: once accepted, every mod can use it.

Typical cases that have come up on the roadmap:
- `StandardModsFolder` install type for mods that drop into
  `Documents\My Games\Age of Empires 3\Mods\` (target v0.9).
- AoE3: Definitive Edition support (target v0.9 — detection and
  launch; full DE mod support is a later, larger effort).
- `assetNamePattern` for `GitHubReleases` when the first `.zip` on a
  release isn't the right one.

---

## 13. What every catalog mod gets automatically

The launcher treats your mod the same way it treats the first-party ones —
almost everything is **generic, not WoL-specific**. Two lists: what you get for
free, and what a single `mod.json` field switches on.

### Free, app-wide — you declare nothing

Once your mod is in the catalog and a user installs it, it inherits all of this
with **zero extra config**:

- **Feedback sounds** — a chat blip, a notification ding, and a "someone
  connected" pop (a global on/off in Settings).
- **Notification bell + toasts** — "update available", "installed", "update
  finished", and "new translation" fire for *your* mod, including a background
  sweep of installed-but-not-active mods.
- **Offline mode** — the mod stays playable with no internet; the update check
  never hard-fails, and the UI renders PLAY from local state.
- **Multiplayer** — Discord sign-in, lobbies + global chat, the Radmin VPN
  assistant, the room-mismatch mod fingerprint (localization-invariant), the
  Discord room webhook + `wol-launcher://join/<id>` deep link, and unranked
  match history.
- **Copy management** — "install another copy", switch/remove copies, the
  "already installed?" search, all with content-based detection.
- **Content-based install detection** — the mod is recognised by its files, not
  its folder name, so a user can rename/move it (see `install.marker` below).
- **Verify + Repair** — per-file integrity check and a re-overlay repair, driven
  by the per-file hashes captured in `install-manifest.json` at install time.
- **Diagnostics** — the "Share diagnostics" bundle (logs + game OOS/sync
  artifacts, never the config/session token).
- **Low-disk-space warning**, **desktop/Start-Menu shortcuts with a real
  `.ico`**, **HiDPI/rounded-window chrome**, **EN/ES localization of the shell**,
  and **base-AoE3 detection** (including a manually-pinned, non-standard AoE3
  folder, reused for your install too — no need to re-find it per mod).

### Switched on by one `mod.json` field

- `install.marker` — a file/dir unique to your mod, needed **only** when your
  `install.probe` file also ships in vanilla AoE3 (see §3.4 / §4). If your probe
  is already exclusive (e.g. your own `.exe`), you don't need a marker.
- `update.github.followLatest` — track the modder's newest stable GitHub release
  instead of the catalog-pinned tag (§5.1).
- `update.github.deltaPatches` — ship optional "changed-files-only" incremental
  patches with a guaranteed full fallback (§5.1).
- `translations.folderRepo` — let the community publish translations for your mod
  (folder-based, with a per-language version picker).
- `icon` / `banner` / `heroImage` / `heroImages` / `screenshots` — branding: the
  dashboard hero (single or rotating), Workshop tiles, and a detail gallery that
  animates GIFs (§3.2).

### What stays first-party-specific (you do NOT inherit — and shouldn't)

- WoL's `WolPatcher` + `UpdateInfo.xml` + `.tar.xz` pipeline and its
  `aoe3wol.com` server, plus its `art\zulushield` marker and legacy Inno-Setup
  registry entry — those are WoL's identity. **Use `GitHubReleases` and declare
  your own `install.marker`.**
- The **stock `aoe3-tad`** entry is a special *detect-only* built-in (no version
  tracked, never installed/updated/uninstalled). Community mods are the opposite:
  they DO carry a tracked version and a real update mechanism.

### Packaging tip

Package your payload **flat** — put `data\`, `art\`, `sound\`, your `.exe`, etc. at
the **root** of the `.zip`, not inside a wrapper folder. The launcher overlays the
zip's contents onto the cloned AoE3, so a flat layout merges correctly. As a
convenience the launcher DOES auto-flatten a zip whose only top-level entry is a
single folder (so `MyMod/data/…` still works), but a flat zip is the reliable shape
and avoids surprises.

---

**One-paragraph summary**: your mod enters the launcher through a PR
to the central catalog repo carrying a `mod.json` that describes
identity, install layout and update mechanism; CI validates the JSON
against a schema and auto-merges cosmetic changes and version bumps,
keeping the critical ones (URLs, hashes, install) under human review;
existing launchers see your mod automatically when their 24 h cache
expires, without you ever touching a line of the launcher itself.
