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

## 1.5. Try your manifest before you publish it

You do not have to open a PR to see how your mod will look. First you need
**developer mode**, and it is deliberately not a visible switch: open
**Settings** and **click the version number in the bottom-left rail seven times**
(the run resets if you pause for more than about a second and a half). An
**ADVANCED → DEVELOPER** block appears; the switch that keeps it on lives inside
it. Then **DEVELOPER → "Choose a mod.json…"** and pick your manifest from disk.

That block is also the only home of the translation packager and the
**delta-patch generator** (§5.1). It is hidden rather than merely off because a
visible "turn on developer mode" row told every player the tools were there;
none of it is a security boundary, just a door that stays shut for people who
have no use for it.

It appears in the catalog listing like any published mod — same parsing, same
projection, same icons and screenshots — except nothing is uploaded. Edit the file,
press **"Refresh catalog"**, and your changes show up immediately. Assets are read
from the folder the `mod.json` sits in, so keep `icon.png` and friends beside it
exactly as they will live in the catalog repo.

If the manifest can't be read, the launcher tells you **why** — the JSON error with
its line, or the missing required field. That is the point of trying it here: the
catalog CI only answers after you have opened the PR.

To stop using it, press **Remove** next to it in that same DEVELOPER section (or
open the mod's detail panel in the Workshop and press **"Stop using this file"**).
Either way it only forgets the path; your file is never modified or deleted.

Two limits worth knowing:

- A manifest whose `id` is `wol` or `aoe3-tad` is ignored — those are built into the
  launcher and no manifest may redirect them (see §6). Use a different `id` to test.
- This validates the **manifest**. Whether the mod actually *installs* still depends on
  the URLs in `install`/`update` pointing at something real and downloadable.

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
   GitHub subpanel (`sourceRepo`, `approvedReleaseTag`, the **"Enable incremental
   delta patches"** checkbox → `deltaPatches` (§5.1), and Advanced
   `externalAssetUrlTemplate` / `externalAssetSha256`), **and the `translations`
   block (`repo`, `coveredFiles`) — collected here, not on a separate step**.
   The wizard cannot set **`followLatest`** or **`maintainers`** yet; add both by
   hand to the JSON it gives you (see §3.5 and §6.3 — without `maintainers`
   naming you, the catalog CI will not auto-merge your own release PRs).
5. **Description & links** — `description.en`, `description.es`,
   `officialWebsite`, and `links` (one `type|url` per line).
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
| `links` | no | ≤ 4 entries, each `{type, url, label?}` | Community links (Discord, ModDB, forum, wiki, videos) shown as a row of pills in the launcher's Workshop detail panel. See §3.1.1. |

#### 3.1.1. Community links

Give players a way to reach your Discord or mod page from inside the launcher:

```json
"links": [
  { "type": "discord", "url": "https://discord.gg/your-mod" },
  { "type": "moddb",   "url": "https://www.moddb.com/mods/your-mod" },
  { "type": "video",   "url": "https://youtube.com/@your-mod", "label": "Trailers" }
]
```

- `type` — one of `website`, `discord`, `moddb`, `forum`, `wiki`, `video`,
  `other`. It only picks the default caption; the launcher never renders a brand
  logo. An unknown value degrades to `other` instead of hiding the link.
- `url` — **HTTPS only** (unlike `officialWebsite`, whose HTTP allowance is a
  legacy concession). Anything else is dropped.
- `label` — optional, ≤ 24 characters. Omit it and the launcher shows the type's
  name in the player's language ("Discord", "Foro", …).

Two things to know before you fill this in:

- **The launcher shows the full URL in the button's tooltip.** A label can claim
  anything, so the destination is always visible before the player clicks. Don't
  bother with a label that contradicts the url.
- **A link that just repeats `officialWebsite` is not rendered twice** —
  `officialWebsite` is folded into this same row as the first pill, so declaring it
  again just duplicates it. (There used to be a separate "view mod page" button; it
  was removed, and the website moved into the row precisely so it stays reachable.)

Editing `links` is a tier 1 (cosmetic) change, so once you're listed in
`maintainers` you can update your Discord invite without waiting for a review.

> **`links` also works for the two first-party entries — it's the only field that
> does.** `wol` and `aoe3-tad` are built into the launcher, and their built-in
> profile *shadows* the catalog manifest on id collision, so everything else in
> `mods/wol/mod.json` is ignored at runtime. `links` is the single whitelisted
> exception: it's cosmetic, it goes through the same sanitisation as any other
> mod's, and it's still covered by the ownership gate. That way a first-party
> Discord invite can change with a manifest edit instead of a new launcher
> release. Nothing else about a built-in can be set from the catalog.

The launcher re-validates every entry on its own side (HTTPS, no embedded
credentials, control characters stripped from the label, capped at 4) — so an
entry that slips past CI still can't reach a player.

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
| `type` | `IsolatedFolder` or `InPlaceOverlay`. **Read §4.0 first** — the wrong type is the top cause of "my mod won't open". Details in §4. |
| `defaultFolder` | Suggested path for the install dialog. Empty for `InPlaceOverlay` — the launcher uses the detected AoE3 path. |
| `probeFile` | Relative path the launcher checks to confirm the mod is installed at a given location (`File.Exists(install + probeFile)`). Pick something **unique** to your mod — `age3y.exe` exists in vanilla AoE3 too. ⚠ **Unique is not enough on its own; declare a `marker` too.** An `IsolatedFolder` install is a CLONE of the player's AoE3 folder, so anything sitting loose in that folder — including a stray leftover of *your own* executable from an older install — is copied into **every** mod folder on the machine. Napoleonic Era declared probe `age3n.exe` with no marker on the reasoning that its own exe was exclusive; one orphan `age3n.exe` in the AoE3 root made every cloned mod folder look like a Napoleonic Era install, and the launcher deleted a user's Struggle of Indonesia while reporting it was uninstalling Napoleonic Era. |
| `marker` | **Declare one.** Relative path (file **or** directory) unique to your mod and absent from vanilla AoE3. It is what lets the launcher detect your mod **by content in a folder with any name**, and what tells a real install apart from a folder that merely happens to hold your probe file. WoL uses `art\\zulushield`. ⚠ **"My probe is my own `.exe`, so I don't need one" is how a real incident happened** — see the warning under `probeFile`. Prefer a **data** file your mod ships (e.g. `data\\myproto.xml`) over the executable. |
| `executable` | Filename of the .exe that launches the game (`age3y.exe` for WoL, `age3m.exe` for Improvement Mod). The launcher looks for it inside the install folder. |
| `arguments` | Extra args the launcher appends when running. Usually empty. |
| `privateSetupPath` | *Optional, default false.* Set `true` for a **stock-exe replacement** total conversion (§4.3): at install the launcher points the player's own copy of your exe at a registry key of its own, so it loads your data instead of the base game's and both stay playable. Only meaningful with `type: IsolatedFolder`. Costs one admin prompt at install and caps `displayName` at 33 characters. Don't set it for a UHC mod or an additive `InPlaceOverlay` mod. |
| `setupPathRedirect` | *Optional, default false.* **Legacy** — the junction-based predecessor of `privateSetupPath` (§4.3). Still supported, but it renames the player's `bin` while your mod runs. Don't use it for a new mod. |
| `multiplayerProbeFiles` | *Optional.* Array of install-relative files that identify your mod's **version** for the multiplayer join check. Declare **only** if your mod ships its own data files instead of overwriting the base `y` files (`data\\protoy.xml` / `techtreey.xml` / `stringtabley.xml`) — e.g. Napoleonic Era's `data\\proton.xml` + `data\\techtreen.xml`. Omit for a normal mod: the launcher default is correct. This is a **critical** field — wrong values let two different versions share a match and desync. |
| `userDataRedirect` | *Optional, default false.* Set `true` if your mod writes saves to the **shared** `My Games\\Age of Empires 3` folder (instead of its own); the launcher junctions that folder at your `userDataFolder` around launch. A stock-exe replacement TC (§4.3) usually needs this too. |
| `userDataPayload` | *Optional.* Name of a **second asset on the same release** (e.g. `userdata.zip`) holding a small tree to seed into `Documents\\My Games\\<userDataFolder>` — the folder skeleton, AI personalities or a starter profile. Use it only if your mod **will not start** without those folders already there. The launcher only ADDS what is missing: a file the player already has is never overwritten, and uninstall (opt-in) can only take back files still byte-identical to what it wrote. Requires a non-empty `userDataFolder` — the destination has to be declared, never guessed — and `update.mechanism: GitHubReleases`. Different from `userDataRedirect`, which changes *where* the game writes; this decides *what must already be there*. **Attach it to every release.** The launcher looks for it on the release it is installing first; if it is not there it falls back to your `approvedReleaseTag`, and then to the newest release that does carry it (stable before prerelease). So forgetting once no longer breaks anyone — but the fallback is written to the diagnostic log naming both tags, which is the only way you find out, so treat it as a safety net rather than a workflow. Players who already installed are unaffected either way: their seed is recorded in the install manifest and is never re-fetched. |
| `supersedeCompiledXml` | *Optional, default false.* Remove the base game's compiled `data\<name>.xml.XMB` where your mod ships its own `<name>.xml` and no compiled twin. Set it **only** if your mod is distributed as a complete game folder carrying no compiled tables of its own; leave it off for a mod that installs over the player's AoE3. Only meaningful with `type: IsolatedFolder`. See the note under this table — getting it backwards causes LAN version mismatches. |

> **If you ship `data\*.xml` without their compiled `.XMB`, the player's own copies win — set
> `install.supersedeCompiledXml`.** Age of Empires III reads the compiled `data\<name>.xml.XMB`
> in preference to the loose `<name>.xml`, and an `IsolatedFolder` install is a **clone of the
> player's own game**. So a mod that ships modded `.xml` files and no compiled twins installs with
> THEIR compiled tables sitting on top of yours — in their language, with the base game's data.
> Measured on *Knights and Barbarians*: seven files, including `stringtable*` (a Spanish owner got
> Spanish menus while the author's own folder runs entirely in English) and `protoy`/`techtreey`,
> which are **simulation** data.
>
> **Which way to go depends on how your mod is distributed, and the launcher cannot work it out
> for you** — packaging drops whatever is identical to the base game, so "I ship no such file"
> and "my file equals the base game's" look the same from outside:
>
> - **A COMPLETE game folder** that deliberately carries no compiled tables (K&B's shape): set
>   `install.supersedeCompiledXml: true`. A canonical install of your mod has none of those files
>   either, so the launcher removing them reproduces exactly what running your folder directly
>   does. It only ever removes a compiled file whose `.xml` **you** shipped and whose `.XMB` you
>   did **not** — so if you ship both halves, nothing is touched.
> - **An overlay onto the player's AoE3** (Wars of Liberty's shape): leave it off. Every canonical
>   peer keeps the base game's compiled files, and removing them would diverge from all of them —
>   the engine uses `.xml.xmb` for its LAN version check, which is what caused version mismatches
>   and out-of-syncs when this was tried for WoL.
>
> `package-mod-payload.ps1` lists the affected files in its report, so you find out before you
> publish rather than from a player reporting the wrong language.

> **Where the initial payload actually comes from:** GitHubReleases mods get it from the
> release asset on `approvedReleaseTag` (or `externalAssetUrlTemplate`); WolPatcher mods get it
> from `update.wol.payloadZipUrls`. `install.payloadUrls`/`payloadSha256` are declared-but-unused
> at the moment, so a `Manual` mod that only sets `install.payloadUrls` can't be installed yet.

### 3.5. `update` — how files are kept up to date

```json
"update": {
  "mechanism": "GitHubReleases",
  "github": {
    "followLatest": true,
    "deltaPatches": true,
    "externalAssetUrlTemplate": "...",
    "externalAssetSha256": "..."
  }
}
```

`mechanism` is an enum with four values. Details in §5.

Everything under `github` is optional, and the two booleans are the ones worth
knowing about early — both are Tier 3, so they are reviewed once and then never
again:

- **`followLatest`** — track your newest stable GitHub release instead of the
  catalog-pinned tag, so an ordinary release needs no catalog PR at all (§5.1).
- **`deltaPatches`** — ship "only the changed files" patches, so you upload the
  full `.zip` once and patches thereafter, and players download only what moved
  (§5.1). Ignored for external-hosted mods, whose SHA is catalog-pinned.

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

### 4.0. First: will your mod even open? (the UHC rule)

Before picking a type, answer **one** question — it decides everything, and
getting it wrong is the single most common cause of **"my mod won't open"**:

> **Does your mod's `.exe` run from *any* folder, or only from where AoE3 was
> originally installed?**

The AoE3 engine normally finds its data (`.bar` files) through the **registry
`setuppath`** value (which points at the real `…\bin`), **not** the folder it's
launched from. A modding hack called **UHC** patches the `.exe` so it reads the
**working directory** instead, letting the game run from any folder. WoL,
Improvement Mod and ESOC ship UHC-patched exes; many other mods ship the **stock**
exe and do **not**.

**Test whether your mod has UHC:** copy your whole game folder somewhere else and
double-click the mod's `.exe`. If it opens normally → it has UHC. If it fails with
`Could not load … .bar` or opens vanilla → it does **not**.

Then pick your model:

| Your mod… | Declare |
| --- | --- |
| has a **UHC-patched** exe (runs from any folder) | `IsolatedFolder` (§4.1) |
| ships the **stock** exe and **ADDS** files (own suffixed exe/`.bar`, doesn't overwrite base) | `InPlaceOverlay` (§4.2) |
| ships the **stock** exe and **REPLACES** base game files | `IsolatedFolder` + `privateSetupPath: true` (§4.3) |

> **Why the additive case is the better deal when you qualify:** an `InPlaceOverlay` install
> copies your files into the folder the engine already reads, so there is no clone, no
> duplicated copy of the game, no junction, and the player can still run vanilla AoE3 while
> your mod is installed. Uninstalling removes only the files you added — but note it does
> **not** restore any base file you overwrote, so keep your payload strictly additive.
> `privateSetupPath` (§4.3) is the answer for replacement mods; it also leaves vanilla
> playable, at the cost of one administrator prompt at install.

If a stock-exe mod is installed as a plain `IsolatedFolder` clone, it will fail to
open or launch vanilla — because the engine keeps loading the base game's data from
the real `…\bin`, never your clone. §4.2 and §4.3 are the two fixes; §9 has the
troubleshooting entry.

> **Don't UHC-patch the exe yourself to "fix" this.** It needs a custom exe, which
> **forks multiplayer compatibility** — players on your custom exe can't play with
> players on the original mod. Use the models below instead.

### 4.1. `IsolatedFolder` — a private clone of AoE3

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

- Your exe is **UHC-patched** — it runs from any folder (WoL, Improvement Mod,
  ESOC). This is the load-bearing requirement: a **non-UHC** exe in an isolated
  clone loads vanilla data, not your mod (see §4.0). If yours is non-UHC, use §4.2
  (additive) or add `privateSetupPath` (§4.3, replacement).
- You want users to not feel the install is touching their AoE3.

Real example: `aoe3-mods-catalog-template/mods/wol/mod.json`.

### 4.2. `InPlaceOverlay` — your files on top of AoE3

Files are extracted **directly over the existing AoE3 install**. No
cloning; the mod and AoE3 share a folder.

Internal steps:

1. Detect AoE3.
2. Back up every file about to be overwritten.
3. Extract your payload on top.
4. Write `install-manifest.json` so an uninstall can revert.

Use it when:

- Your mod is **additive and non-UHC** — it ships its **own suffixed** exe and
  files (`age3n.exe`, `DataPN.bar`, `data\proton.xml`…) that **don't overwrite**
  the base game's. Because your files land in the real AoE3 (where `setuppath`
  already points), the stock engine finds them with **no registry tricks**, and
  your mod coexists with vanilla. This is what an additive mod's own installer does.
- Or your mod is a **lightweight patch/overhaul** touching few files.

> **No shipped mod uses this today.** Napoleonic Era is additive and was the
> obvious candidate, but it ships on §4.3 instead: writing into the player's real
> AoE3 is the part worth avoiding, and once your mod lives in its own folder a
> stock exe needs its own registry key regardless of whether it overwrites
> anything. Pick §4.2 only if installing *into* the base game is genuinely what
> you want.

**Uninstall is safe:** an in-place install's manifest records only your net-new
files, so uninstalling removes just those and leaves the base game intact. Declare
`payloadSha256` and ship your files in the folder layout they belong in (e.g. the
`.bar`/exe where the base `.bar` live), so they overlay onto the right place.

### 4.3. `privateSetupPath` — stock-exe total conversions

For a total conversion that ships the **stock `age3y.exe`** and **replaces** base
game data (so it can't just add files like §4.2 — it would destroy vanilla). It
stays an `IsolatedFolder` clone, and you add one flag:

```jsonc
"install": {
  "type": "IsolatedFolder",
  "privateSetupPath": true,
  "userDataRedirect": true,          // usually also needed (see §3.6 / userDataFolder)
  ...
}
```

**What it does.** The stock engine finds its `.bar`/data through a registry key
named inside the executable — the base game's — which is why an isolated clone
otherwise loads vanilla. At install time the launcher rewrites that name inside
**the player's own copy** of your exe so it points at a key belonging to your mod,
and creates that key with `setuppath` set to the install folder. Your mod and
vanilla then each read their own, so **both stay playable, including at the same
time**, and nothing has to be undone when the game closes. Real example: Struggle
of Indonesia (`mods/struggle-of-indonesia/mod.json`).

**What it costs you as a modder:**

- **One administrator prompt, at install only.** The key lives in `HKLM`; the engine
  ignores `HKCU` (measured — it falls back to demanding the product key). The
  launcher folds this into the permission prompt it may already be showing, so the
  player is asked once, before the download — and never again: the key is written
  once, so later repairs and updates need no elevation at all.
- **Your `displayName` must be at most 33 characters.** The replacement key has to
  fit in the 72-character slot the original occupies. A longer name fails the
  install rather than truncating.
- **Only the stock TAD executable is supported.** If the launcher doesn't find the
  base key in your exe, it **aborts the install** instead of leaving a mod that
  silently shows vanilla content. If your exe is UHC-patched you don't need this
  flag at all — use §4.1.
- The patch invalidates the executable's Authenticode signature. It is applied to
  the player's local copy only; no modified binary is distributed.

> **Don't ship a pre-patched exe in your payload.** It looks like it would save the
> launcher a step, and it doesn't work: the patched exe is useless without its
> registry key, and that key cannot be packaged — its `setuppath` is *that player's*
> install folder, and the licence values in it (`pid`, `digitalproductid`,
> `doublehash`) are *that player's* licence. A shipped exe with no key lands the
> player straight in the product-key dialog. It would also mean redistributing
> Microsoft's executable. Let the launcher patch the player's own copy; the key it
> creates is cloned from that machine's base game, so your mod's licence state always
> matches their vanilla — if their AoE3 opens without asking for a key, so does your
> mod.

> **Legacy: `setupPathRedirect`.** The earlier answer to the same problem: around
> launch the launcher junctioned the folder `setuppath` points at (the real `…\bin`)
> to your clone folder, and restored it afterwards. It needed no admin, but it
> renamed the player's `bin` while the mod ran — making your mod and vanilla
> mutually exclusive during a session, and leaving the folder renamed until the
> launcher next started if the game crashed. It still works, and installs made with
> it keep being restored correctly, but **don't choose it for a new mod.**

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
  changes which tag is targeted, not what's downloaded. The exception is
  delta patches (§ below): with those on you ship the full `.zip` only on a
  **baseline** release and later ones carry patches alone.
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
you can opt into **delta patches** so players download only the files that
changed — and so that **you stop re-uploading the whole mod on every release**.
It is a GitHub-native alternative to WoL's `WolPatcher` pipeline, with no
`UpdateInfo.xml` server to run.

**When to use it.** Big overlay + frequent small updates → worth it. Small mod
or rare updates → the full `.zip` is simpler; skip this. It's **opt-in and
purely additive**: nothing changes unless you turn it on and ship a patch.

**Requirements.**
- `update.mechanism` = `GitHubReleases`, payload hosted **on GitHub** (not an
  external `externalAssetUrlTemplate` CDN — those always use the full path).
- `"deltaPatches": true` inside `update.github` in your catalog `mod.json` (a
  Tier-3 change, reviewed once — see §6.3).

##### The whole thing in one page

If you read nothing else in §5.1, read this. The rest is the same story with the
reasoning attached.

1. **Once:** publish a release carrying your full overlay `.zip`. That release is
   your **baseline**. Turn on `deltaPatches`, and ideally `followLatest` too.
2. **Every release after that:** build your new overlay `.zip` locally as always,
   then open **Settings → ADVANCED → DEVELOPER → "Incremental patch generator"**
   (§1.5 explains how to reveal that block) and run it **once**, filling in:
   - previous release's `.zip` + its tag → the **incremental** patch;
   - baseline release's `.zip` + its tag, in the *Baseline* fields → the
     **cumulative** patch.
3. **Upload the files it wrote** — 2 with no baseline, 4 with one — to the **new**
   release. **Not the full `.zip`.** The zips you fed the tool were only read for
   comparison.
4. **Bump `approvedReleaseTag`** in the catalog, unless you use `followLatest`.

Three ways to get it wrong, none of which report an error — the launcher just
quietly downloads the whole mod, so you would never find out:

| Mistake | What happens |
|---|---|
| A tag that isn't exactly your real GitHub tag | The patch can't be matched to a version and is ignored |
| A patch uploaded to the release it comes **from** | Ignored — it belongs on the release it leads **to** |
| Patches in a separate "patches" repo | Never read; they must be on `sourceRepo`'s releases |

And one hard rule: **at least one release must always carry a full `.zip`.** A
new player can only start from one; patches cannot bootstrap an install.

##### The lifecycle, in three steps

**1. Your first publication — the baseline.** Create the release and upload the
full overlay `.zip`, exactly as you would without any of this. Nothing to patch
from yet. That release is now your **baseline**.

**2. Every release after that — patches only.**

1. Build your new full overlay `.zip` as always (you need it locally to diff
   against — you just won't be uploading it).
2. In the launcher: **Settings → ADVANCED → DEVELOPER → "Incremental patch
   generator" → Open** (see §1.5 for how to reveal that block; the *Packager*
   button beside it is the translation packager, a different tool). Give it:
   - the **previous** release's overlay `.zip` + its tag → produces the
     **incremental** patch, the smallest download for players who update every
     version;
   - the **baseline** release's overlay `.zip` + its tag (the optional
     "Baseline" fields) → produces the **cumulative** patch, which is what keeps
     a *fresh install* at two downloads no matter how many releases have gone by.
3. Create the GitHub release and upload the **four** files the tool wrote (two
   `.zip` + two `.json`). **No full `.zip`.**
4. Open the usual catalog PR bumping `approvedReleaseTag` (Tier 2, auto-merges),
   unless you use `followLatest`.

The launcher reads your whole release list in **one** API call — every tag, every
asset, every size — and works out the cheapest route for each player: one
cumulative patch, one incremental, a short chain of them, or the full `.zip`
when that is cheaper. You do not declare the route anywhere.

**3. When to publish a new baseline.** A cumulative patch grows with every
release. Once it approaches the size of the mod itself it has stopped saving
anybody anything — so publish that release **with the full `.zip` too** and treat
its tag as your new baseline from then on. The patch generator tells you when:
it compares the patch it just wrote against your full `.zip` and warns past
**half**. Nothing enforces it; if you ignore it the launcher simply starts
choosing the full download again.

Publishing that roll-up is an **ordinary release** — upload the full `.zip`
again, and that is the whole of it. There is no flag to set and nothing to
migrate. Ship the incremental from the previous version alongside it too, so
people who are up to date don't have to re-download the mod they already have.
From then on generate the cumulative against the **new** baseline; the very next
release needs only one patch, because its incremental and its cumulative are the
same file, and the generator skips the duplicate for you.

##### How the launcher tells a baseline from a patch

**By the file name, and nothing else.** `patch-*.zip` / `patch-*.json` are patch
machinery; **any other `.zip` is the full overlay**, and a release that carries
one *is* a baseline. That is the entire rule.

Two consequences worth having in mind:

- **Nothing is declared and nothing is stored.** The launcher re-derives this
  from your release list on every check, so it cannot go stale and there is
  never anything to migrate. Publish the roll-up and the next check simply sees
  a new baseline.
- **Several baselines coexist happily.** A new one doesn't retire the old one —
  it just adds a cheaper place to start from. The launcher weighs every release
  that carries a full `.zip` and picks whichever gives the cheapest total route,
  which for a fresh install is normally the newest.

> **Always keep at least one baseline.** A brand-new player can only start from
> a full `.zip`; patches cannot bootstrap an install. Delete *every* release
> that carries one and your mod becomes uninstallable.
>
> That is the real rule — it is not "keep your first release forever". Once
> you've published a newer baseline, the older one is safe to delete: fresh
> installs start from the new one, and anybody stranded on an older version is
> rescued from it. Intermediate patch-only releases are safe to delete too; the
> launcher just routes around them, at worst falling back to a full download.

##### Where the files go on GitHub

Two rules that are easy to get wrong, and neither one announces itself:

1. **Same repository.** The patches go on the releases of the repo your catalog entry names in
   `sourceRepo` — the same place your full `.zip` has always gone. **A separate "patches" repo is
   never read**, so nothing in it would ever be found.
2. **Each patch goes on the release it leads TO.** `patch-v1-to-v2.*` is uploaded to release
   `v2`, not to `v1`. A patch attached to the wrong release is ignored in silence: nothing breaks,
   the player just downloads the whole mod again — which is worse than an error, because you will
   not notice.

Concretely, over a few releases:

| Release | What you upload |
|---|---|
| `v1` — your baseline | `mod.zip` (the full overlay) |
| `v2` | `patch-v1-to-v2.zip` + `patch-v1-to-v2.json` |
| `v3` | `patch-v2-to-v3.*` (incremental) **and** `patch-v1-to-v3.*` (cumulative from the baseline) |
| `v4` | `patch-v3-to-v4.*` **and** `patch-v1-to-v4.*` |
| `v5` — new baseline, once the generator says so | `mod.zip` again, plus `patch-v4-to-v5.*` |
| `v6` | `patch-v5-to-v6.*` only — its incremental and its cumulative are the same file now |
| `v7` | `patch-v6-to-v7.*` **and** `patch-v5-to-v7.*` (cumulative from the NEW baseline) |

**Pair this with `followLatest: true`.** Without it the launcher targets whatever
`approvedReleaseTag` says, so every release needs a small catalog PR. With it the launcher follows
your newest stable release and `approvedReleaseTag` becomes just the seed for a first install with
no network.

**Try it before you publish anything.** Settings → ADVANCED → DEVELOPER → "Choose a `mod.json`…" loads a
manifest straight off your disk, so you can point one at your real repo, turn `deltaPatches` on,
and walk the whole flow against real releases without opening a catalog PR.

##### Removing a file in a patch

Exactly as in a full update: **leave it out of your new overlay `.zip`**. The
generator diffs the two zips and records it under `deleted` for you — you never
hand-write a delete list. The same limit applies as everywhere else: only files
your mod **added** are removed automatically; one that *overwrites* a base-game
file is never auto-deleted (that would leave a hole the engine expects), so to
revert a base file re-pack its original bytes or ship an explicit `delete.lst`.

One reason the cumulative patch is worth generating: its `deleted` list comes
from a **single** diff (baseline → new), so it is already the net result. A chain
of incrementals reaches the same place only because the launcher applies them in
order — deletions are order-dependent, since a later release may re-add what an
earlier one removed.

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

**Guarantees (why it's safe):**
- **Cheapest route, chosen for each player.** The launcher compares real byte
  sizes and picks; a tie always goes to the full `.zip`, because a full
  re-overlay also repairs an install that has quietly diverged and a patch does
  not. Chains are capped at **4** hops — a hop costs real time on a multi-gigabyte
  overlay regardless of how few bytes it moves.
- **Full fallback, always.** Any problem — a missing patch, a diverged install, a
  hash mismatch, a network hiccup, an external-hosted mod — falls back to the
  full download. Patching can never make an update *worse* than the full path,
  only faster when it works.
- **Every hop is committed before the next starts.** If a chain is interrupted
  halfway the install is left coherent at that intermediate version, still
  playable, and the next attempt simply plans a shorter route.
- **Byte-identical result.** After patching your install is identical to one that
  did the full download, so **multiplayer version-matching is unaffected**.
- **Hashes are optional but the tool always includes them** (extra verification;
  when absent the launcher trusts GitHub's CDN, exactly like the full `.zip`).

**Limitations:** at least one release must always carry the full `.zip` (see the
baseline rule above); external-hosted mods can't use patches; the arbitrary
version picker (Mod Properties) always uses the full path.

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
| **tier1** | Only: `displayName`, `subtitle`, `description`, `accentColor`, `author`, `officialWebsite`, `links`, `icon`, `banner`, `heroImage`, `screenshots` | **Auto-merge** after validation |
| **tier2** | Only: `approvedReleaseTag` (version bump) | **Auto-merge** after validation |
| **tier3** | Anything in: `id`, `sourceRepo`, `install.*`, `update.*`, `translations`, `maintainers`, **any field not listed above** (e.g. `userDataFolder`, `installProductGuid`), OR a first-time submission | Labelled `needs-manual-review` + comment; maintainer reviews manually |

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
  "id": "example-mod",
  "displayName": "Example Mod",
  "author": "Example Team",
  "accentColor": "#1f4e79",
  "icon": "icon.png",
  "banner": "banner.png",
  "description": {
    "en": "Example total conversion for AoE3.",
    "es": "Conversión total de ejemplo para AoE3."
  },
  "sourceRepo": "example-team/example-mod",
  "approvedReleaseTag": "v2.3.0",
  "userDataFolder": "Example Mod",
  "install": {
    "type": "IsolatedFolder",
    "probeFile": "age3n.exe",
    "executable": "age3n.exe",
    "privateSetupPath": true,
    "multiplayerProbeFiles": ["data\\proton.xml", "data\\techtreen.xml"]
  },
  "update": {
    "mechanism": "GitHubReleases",
    "github": {
      "externalAssetUrlTemplate": "https://cdn.example-mod.com/builds/example-{tag}.zip",
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

**Runtime — "my mod won't open" (`Could not load … .bar`, or it launches vanilla):**
this is almost always the wrong `install.type` for a **non-UHC** mod, not a CI/PR
problem. The engine is loading the base game's data from the real `…\bin` instead of
your clone. Read **§4.0** and switch to the model that matches your mod: additive →
`InPlaceOverlay` (§4.2), stock-exe replacement → `IsolatedFolder` +
`privateSetupPath: true` (§4.3).

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
- **Don't UHC-patch the game exe yourself to make an isolated clone work.**
  It needs a custom exe, which forks multiplayer compatibility (your players
  can't play with the original mod's players). If your mod ships the stock
  exe, use `InPlaceOverlay` (§4.2) or `privateSetupPath` (§4.3) instead — the
  launcher handles the "runs from any folder" problem for you.
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
| [`WarsOfLibertyLauncher/Services/GitHubReleaseDownloader.cs`](../WarsOfLibertyLauncher/Services/GitHubReleaseDownloader.cs) | Asset resolve + download (GitHubReleases). `PickAssetIndex` is the rule that keeps a `patch-*.zip` from being mistaken for your full overlay; `PickPayloadPartIndices` is the one that puts a `.zip.001`/`.002` set back together, in part order |
| [`WarsOfLibertyLauncher/Services/DeltaPatchService.cs`](../WarsOfLibertyLauncher/Services/DeltaPatchService.cs) | Delta patches (§5.1): the descriptor, the diff, `PatchAssetNaming` (the `patch-<from>-to-<to>` convention), and the pre-apply verification |
| [`WarsOfLibertyLauncher/Services/DeltaChainPlanner.cs`](../WarsOfLibertyLauncher/Services/DeltaChainPlanner.cs) | Works out each player's cheapest route — cumulative, incremental, a short chain, or the full `.zip`. Read this to see why a patch of yours was or wasn't taken |
| [`WarsOfLibertyLauncher/PatchGeneratorDialog.xaml.cs`](../WarsOfLibertyLauncher/PatchGeneratorDialog.xaml.cs) | The generator dialog itself |
| [`WarsOfLibertyLauncher/Services/UserDataPayloadService.cs`](../WarsOfLibertyLauncher/Services/UserDataPayloadService.cs) | `install.userDataPayload` (§3.4): seeds your `My Games` folder, copy-if-absent. The rule that a file the player already has is never overwritten lives here |
| [`package-mod-payload.ps1`](../package-mod-payload.ps1) | Builds the payload (flattens `bin\`, drops base-game files, splits over 2 GB) and `userdata.zip`. Run it with `-ReportOnly` first |
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
  `Documents\My Games\Age of Empires 3\Mods\`. Not implemented; no target date.
- AoE3: Definitive Edition support (detection and launch; full DE mod support is a
  later, larger effort). Not implemented — DE uses a different engine with no
  `age3y.exe`, so it is deliberately not detected today.
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

- `install.marker` — a file/dir unique to your mod (see §3.4 / §4). ⚠ **Declare
  one even when your probe file looks exclusive.** This bullet used to say a
  marker was needed *only* when the probe also shipped in vanilla, and gave "your
  own `.exe`" as the example of when you could skip it — that advice caused a real
  incident. An `IsolatedFolder` install clones the player's AoE3 folder, so one
  stray leftover of your exe in that folder propagates into every mod folder on
  the machine and they all start looking like your install. Prefer a data file
  your mod ships over the executable.
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

**⚠ If your mod folder has a `bin\`, the zip root must be the CONTENTS of `bin\`, not
`bin\` itself.** The launcher clones the player's AoE3 and flattens the *clone's* `bin\`
into the install root **before** laying your payload on top — so a `bin\` inside your zip
is never flattened. It just lands at `<install>in\` and your mod loads nothing, while
the install still reports success. Drop `directx\`, `msxml\` and any `unins000.*` too:
the clone supplies the first two and the third is meaningless here.

**Don't ship files that are identical to the base game.** The player's own AoE3 clone
already provides them; shipping them again bloats the download for everyone and
redistributes files that aren't yours to redistribute. Compare by SHA-256, not by size.
Careful with `Sound\`, `avi\` and `Language\`: if your reference AoE3 is a different
**language** from the one you built on, every voice line and cinematic will look
"changed" without your mod having touched any of it.

**Over 2 GB? Split it.** GitHub refuses a release asset larger than 2 GB. Upload the zip
split into `payload.zip.001`, `payload.zip.002`, … — three digits, contiguous from `001` —
and the launcher downloads every part and concatenates them before extracting. Upload the
**whole** set: a gap makes the launcher refuse the release and name the missing part.

The repo's `package-mod-payload.ps1` does all four of these (and builds `userdata.zip`),
printing a per-folder report of what it would ship plus the SHA-256 of every part. Run it
with `-ReportOnly` first and read the report before you upload anything.

---

**One-paragraph summary**: your mod enters the launcher through a PR
to the central catalog repo carrying a `mod.json` that describes
identity, install layout and update mechanism; CI validates the JSON
against a schema and auto-merges cosmetic changes and version bumps,
keeping the critical ones (URLs, hashes, install) under human review;
existing launchers see your mod automatically when their 24 h cache
expires, without you ever touching a line of the launcher itself.
