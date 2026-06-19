# AoE3 Mod Launcher — translations repo (template)

A dedicated repo that hosts **community translation packs as files on `main`**.
The launcher reads it directly (no GitHub releases needed). Point a mod at it via
`translations.folderRepo` in its `mod.json` in the
[mods catalog](https://github.com/Gorgorito12/aoe3-mods-catalog).

## Structure

```
translations/
  <id>/                     # id = the pack id (es, es-419, fr, …)
    translation.json        # the manifest (includes contentHash + zip)
    <id>.zip                # the translated files
schema/
  translation.schema.json   # the translation.json format (for editors / CI)
```

One folder per pack. The folder name should match the pack's `id`.

## How to publish a pack

1. In the launcher, open **Settings → Packager**, pick your mod and source files,
   and export. You get a `translation.json` + a `.zip`.
2. Create `translations/<id>/` here and **commit both files** into it (push to
   `main` or open a PR). That's it — no release, no separate asset upload.
3. Players see the pack the next time their launcher refreshes the language list.

## Updating / improving a pack

Re-export with the Packager and **commit the new `translation.json` + `.zip` over
the old ones** in the same `translations/<id>/` folder. The packager bakes a
`contentHash` into the manifest; because the bytes changed, the launcher treats it
as a **new** translation and re-notifies users — **no version bump, no tag**. Same
mod version, improved pack: just commit over it.

## How the launcher finds packs

- Lists `translations/` via the GitHub Contents API, reads each
  `translations/<id>/translation.json` via the raw CDN, and downloads the `.zip`
  from `raw.githubusercontent.com/<owner>/<repo>/main/translations/<id>/<zip>`.
- Dedups / notifies on `id@contentHash`. The same key is computed by the central
  notifier service, so a pack alerts once across the whole community.
- The MD5 hashes in `translation.json` are the real compatibility check at apply
  time; `compatibleWith` is the translator's tested-versions hint.

## Notes

- Legacy packs published as **GitHub releases** still work (the launcher reads both
  in dual mode), so you can migrate gradually.
- `schema/translation.schema.json` documents every field. Don't hand-write the
  hashes — let the Packager compute them.

Apache-2.0.
