# AoE3 Mod Launcher — translations repo (template)

A dedicated repo that hosts **community translation packs as files on `main`**.
The launcher reads it directly (no GitHub releases needed). Point a mod at it via
`translations.folderRepo` in its `mod.json` in the
[mods catalog](https://github.com/Gorgorito12/aoe3-mods-catalog) — that makes it the
mod's **official** source. Any other repo works too: players can follow it from the
mod's **Language tab → Translation sources**, and its packs are shown as unofficial.

## Structure

```
translations/
  <id>/                       # id = the language pack (es, ES-LA, fr, pt-br, …)
    <version>/                # one folder per version: <mod version>-r<N>, e.g. 1.2.0e-r1
      translation.json        # the manifest (contentHash + zip + date + targetMod)
      <zip>                   # the pack, under the name the manifest's "zip" field
                              # records (the Packager writes it; <id>.zip if absent)
schema/
  translation.schema.json         # the translation.json format (for editors / CI)
  translations-index.schema.json  # the translations-index.json format (see below)
scripts/
  validate_translations.py    # the checks CI runs — runnable locally too
covered-files.json            # which files each mod lets a pack replace (CI checks paths)
.github/workflows/validate.yml
```

**The layout is fixed.** The launcher only reads
`translations/<id>/<version>/translation.json`; a manifest anywhere else is ignored
(and written to the player's `launcher-debug.log`), so a misplaced pack is simply
invisible. The folder names must be the manifest's `id` and `version`.

## Versions: `<mod version>-r<N>`

Name each version after the mod version it was made for, plus a revision:
`1.2.0e-r1`, then `1.2.0e-r2` for a corrected pack, then `1.2.0f-r1` when the mod
updates. The Packager proposes the next free one for you. This is what players see in
the version picker ("1.2.0e-r2 · for 1.2.0e"), and it means two different packs can
never share a label — "1.1 for 1.2.0d" and "1.1 for 1.2.0e" used to look identical.

## How to publish a pack

1. In the launcher, open **Settings → Packager**, pick your mod and source files,
   and export. It builds a ready `translations/<id>/<version>/` folder and warns if
   that version already exists.
2. **Commit that folder** here (push to `main` or open a PR). No release, no
   separate asset upload. CI checks it (see below).
3. Players see it the next time their launcher refreshes the language list — at
   startup, every ~30 minutes, or when they open the Language tab — and the bell
   tells them a new version is out.

## Adding a new version

Re-export with the Packager and **commit the new `translations/<id>/<version>/`
folder** — never touch the old ones. A published version is something players
already have and can roll back to; a fix is a new version (`-r2`, `-r3`…). The
launcher groups your versions into one card with a version picker (latest 10).

## Not on GitHub? Publish an index anywhere

A translator can also publish from **Google Drive, Dropbox, a gist or their own
website** with a single `translations-index.json` (format:
`schema/translations-index.schema.json`):

```json
{
  "name": "Traducciones de Juan",
  "translations": [
    {
      "id": "ES-LA", "name": "Español", "author": "Juan", "targetMod": "wol",
      "version": "1.2.0e-r1", "compatibleWith": ["1.2.0e"],
      "zip": "https://drive.google.com/file/d/<file id>/view?usp=sharing",
      "sha256": "<64 hex>", "size": 1249053, "contentHash": "18ca36a3d84d2352",
      "date": "2026-09-30T00:00:00Z"
    }
  ]
}
```

- **Let the Packager write it:** after exporting, paste the zip's download link and
  press **"Add to my index…"** — it adds the version (with the SHA-256, which is
  required) to your existing file, or creates one.
- **Keep the same link.** Players add your index's link once; every version you add
  to that file then shows up for them. So replace the file, don't upload a new one:
  on Drive use *Manage versions → Upload new version*, on Dropbox overwrite the file
  with the same name, on a gist edit it. Share the file as "anyone with the link".
- `sha256` and `targetMod` are required, and the zip link must be a direct file:
  Google Drive and Dropbox share links are converted automatically, but **Mega,
  MediaFire, OneDrive and folder links can't be used**. On Drive, Dropbox or a gist
  give the full zip link; a relative path only works on an ordinary website or here.
- Players can follow you with one click from a
  `wol-launcher://add-source?url=<your index link, URL-encoded>` link (the launcher
  asks them to confirm and shows the full address).

A repo can carry a `translations-index.json` at its root too; CI then checks that
every relative zip it lists exists here and matches its SHA-256.

## What the launcher accepts from a pack

The launcher treats every pack as untrusted input, so a pack has to follow these
rules or it is refused (the reason is written to the player's `launcher-debug.log`):

- **The zip is flat.** `translation.json` and the translated files sit at the zip's
  root, with no folders. Anything else in the zip is ignored.
- **Only the mod's covered files are applied.** Each `files[].path` must be one of
  the files the mod declares in its `translations.coveredFiles` (for Wars of
  Liberty: `data/stringtabley.xml` and `data/unithelpstringsy.xml`). Any other path
  — another game file, `..`, an absolute path — is refused and never written.
- **Every file matches its hash.** Each file in the zip must have the MD5 its
  `translatedHash` records, and the pack's content hash must be the one it was
  listed with. A pack that doesn't is refused as damaged — so never edit a zip by
  hand without re-exporting.
- **It names its mod.** `targetMod` must be the mod's id. It may be empty only in
  the mod's own official repo (older packs); anywhere else the pack is refused.
- **The id is a plain folder name.** Letters, digits, `.`, `_` and `-`, starting
  with a letter or digit, at most 64 characters (`es`, `ES-LA`, `pt-br`). The same
  goes for the version folder, which may also use `+`.
- **Size limits.** The zip may be at most 64 MB, and at most 128 MB once extracted
  (64 MB per file). Real packs are around 1–2 MB.

The Packager already produces packs that follow every one of these rules.

## CI

`.github/workflows/validate.yml` runs `scripts/validate_translations.py` on every
pull request and push. It fails when:

- a file is outside `translations/<id>/<version>/`, or a folder name isn't the
  manifest's id / version (version: for new folders);
- a manifest breaks `schema/translation.schema.json`, lists a file the mod doesn't
  cover (`covered-files.json`), lacks a `translatedHash`, or declares a
  `contentHash` its files don't produce;
- the zip is missing, isn't the manifest's `zip`, isn't flat, holds files the
  manifest doesn't list, exceeds the limits, or a file's MD5 doesn't match;
- two versions of one id share a label or the same content;
- a **published** file (one already on the base branch) was changed or deleted.
  Ship a fix as a new version. For a one-time reorganisation a maintainer adds the
  **`allow-modify-published`** label to the pull request.

Run it locally before pushing:

```
python scripts/validate_translations.py --base origin/main
```

## Moving an older layout to this one

Repos that started with other folder shapes (`translations/<id>/translation.json`,
or `translations/historial/<mod version>/<version>/…`) move each pack to
`translations/<id>/<mod version>-r<N>/` with `git mv`, set the outer
`translation.json`'s `version` to the new folder name, and merge that pull request
with the `allow-modify-published` label. The zips don't change, so players' active
packs keep being recognised (they're identified by content hash, not by name).

## Notes

- Legacy packs published as **GitHub releases** still work (the launcher reads a
  mod's releases repo too), so you can migrate gradually.
- `schema/translation.schema.json` documents every field. Don't hand-write the
  hashes — let the Packager compute them.

Apache-2.0.
