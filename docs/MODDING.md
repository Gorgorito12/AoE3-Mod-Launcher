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

1. **Identity** — `id`, `displayName`.
2. **Look & feel** — `accentColor`, `icon`, `banner`.
3. **Install** — `type`, `defaultFolder`, `probeFile`, `executable`.
4. **Updates** — `mechanism` and its dependent fields.
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
  "executable": "age3m.exe",
  "arguments": "",
  "payloadUrls": ["https://..."],
  "payloadSha256": ["aabbcc..."]
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
| `payloadUrls` | Array of HTTPS URLs for the initial install zip. If the mod ships in parts (`.zip.001`, `.002`, …) list them in order — the launcher concatenates and then extracts. |
| `payloadSha256` | **Strongly recommended.** Parallel array to `payloadUrls` with each part's SHA-256. If you declare it and a download doesn't match, the launcher aborts — defends against payload tampering. |

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
| `approvedReleaseTag` | Approved release tag for `GitHubReleases`. Bumping this is the normal way to ship a new version (auto-merge, §6). |
| `installProductGuid` | Stable Add/Remove Programs key (`HKLM\…\Uninstall\<here>`). If you have a pre-existing installer with its own GUID, put it here to stay compatible. Otherwise omit and the launcher derives `<id>_launcher`. |
| `userDataFolder` | Folder name under `Documents\My Games\<here>\` where your mod stores saves/replays. When set, the launcher enables the pre-install backup prompt and exposes "Open / Create backup / Restore backup" in the gear menu. Omit if your mod reuses vanilla AoE3's user-data folder. |
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
3. Flatten `bin\` to the root (Steam layout) and delete the now-empty
   `bin\` afterwards (~3.7 GB saved).
4. Extract your payload on top.
5. Write shortcuts, registry entry and `<id>-manifest.json`.

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
4. Write `<id>-manifest.json` so an uninstall can revert.

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
> Packager** to build a pack; it produces a `translation.json` + a `.zip`. Then
> just **commit both files to `translations/<id>/` on the `main` branch** of your
> `translations.folderRepo` (push or open a PR) — no GitHub release, no separate
> asset upload. The launcher discovers folder packs via the Contents API and keys
> them by a **content hash** baked into `translation.json`, so an **improved** pack
> (re-export with new files, commit over the old one) automatically re-notifies
> users — you don't bump any version or tag. Releases on `translations.repo` still
> work too (dual mode), so existing packs keep showing while you migrate.

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

The launcher lists the mod, lets the user install it (if you declared
`install.payloadUrls`) and never tries to update it. Useful for demos,
prototypes, or mods whose update story isn't decided yet.

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

| Field | Mandatory when | Strongly recommended when |
|---|---|---|
| `install.payloadSha256` | never | always, when you declare `payloadUrls` |
| `update.wol.payloadSha256` | never | always, when you declare `payloadZipUrls` |
| `update.github.externalAssetSha256` | **always**, when `externalAssetUrlTemplate` is set | n/a |

The launcher verifies the hash after download and aborts on mismatch.
Without a hash, the launcher trusts the host (GitHub Releases, the
mod's own site). **With** a hash, the launcher catches tampering even
if the host was compromised after the PR was approved.

### 6.3. Tier system — what auto-merges and what doesn't

The `classify_pr.py` script classifies every PR by which fields it
touches:

| Tier | Fields modified | Action |
|---|---|---|
| **invalid** | Files outside `/mods/`, multiple mods at once, malformed JSON, unknown filenames | PR is blocked with an explanatory comment |
| **tier1** | Only: `displayName`, `subtitle`, `description`, `accentColor`, `author`, `officialWebsite`, `icon`, `banner` | **Auto-merge** after validation |
| **tier2** | Only: `approvedReleaseTag` (version bump) | **Auto-merge** after validation |
| **tier3** | Anything in: `id`, `sourceRepo`, `install.*`, `update.*`, `translations`, OR a first-time submission | Labelled `needs-manual-review` + comment; maintainer reviews manually |

What this means for you as a modder:

- **Your first submission is always tier 3.** Expect to wait for
  review.
- **Changing icon / banner / text** later: auto-merge within minutes.
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
  class has a hardcoded list for WoL only (an offline fallback). Your
  mod goes in the catalog. Editing `ModRegistry` directly would mean
  your mod needs a new launcher release to appear — the opposite of
  what the system is designed for.
- **Don't upload the mod payload to the catalog.** The catalog repo
  holds only metadata (`mod.json` + small assets). The binary lives in
  GitHub Releases / your CDN.
- **Don't declare `payloadSha256` without actually computing the hash.**
  A placeholder hash means the launcher refuses to install for every
  user.
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
| [`WarsOfLibertyLauncher/Services/GitHubReleasesInstallService.cs`](../WarsOfLibertyLauncher/Services/GitHubReleasesInstallService.cs) | Update flow (GitHubReleases) |
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

**One-paragraph summary**: your mod enters the launcher through a PR
to the central catalog repo carrying a `mod.json` that describes
identity, install layout and update mechanism; CI validates the JSON
against a schema and auto-merges cosmetic changes and version bumps,
keeping the critical ones (URLs, hashes, install) under human review;
existing launchers see your mod automatically when their 24 h cache
expires, without you ever touching a line of the launcher itself.
