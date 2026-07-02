# AoE3 Mod Launcher — translations repo (template)

A dedicated repo that hosts **community translation packs as files on `main`**.
The launcher reads it directly (no GitHub releases needed). Point a mod at it via
`translations.folderRepo` in its `mod.json` in the
[mods catalog](https://github.com/Gorgorito12/aoe3-mods-catalog).

## Structure

```
translations/
  <id>/                       # id = the language pack (es, es-419, fr, …)
    <version>/                # one subfolder per version (1.2, 1.1, …)
      translation.json        # the manifest (contentHash + zip + date)
      <zip>                   # the translated files (name from the manifest's
                              # "zip" field; the packager defaults to <id>.zip)
schema/
  translation.schema.json     # the translation.json format (for editors / CI)
```

One folder per language; inside, one subfolder per version (the version history).
Single-version `translations/<id>/translation.json` (no version subfolder) also
works.

## How to publish a pack

1. In the launcher, open **Settings → Packager**, pick your mod and source files,
   and export. It builds a ready `translations/<id>/<version>/` folder.
2. **Commit that folder** here (push to `main` or open a PR). No release, no
   separate asset upload.
3. Players see it the next time their launcher refreshes the language list.

## Adding a new version

Re-export with the Packager (bump the version) and **commit the new
`translations/<id>/<version>/` subfolder** — never touch the old ones. The launcher
groups them into one menu entry with a version picker (latest 10), so users can
roll back; the newest drives the menu/notification. (Committing over a single
folder also works if you prefer one live version.)

## How the launcher finds packs

- Reads the whole tree in ONE call (Git Trees API) and each
  `translations/<id>[/<version>]/translation.json` via the raw CDN; downloads the
  `.zip` from `raw.githubusercontent.com/<owner>/<repo>/main/translations/<id>/<version>/<zip>`.
- Dedups / notifies on `id@contentHash` of the NEWEST version. The same key is
  computed by the central notifier service, so a pack alerts once across the
  community.
- The MD5 hashes in `translation.json` are the real compatibility check at apply
  time; `compatibleWith` is the translator's tested-versions hint.

## Notes

- Legacy packs published as **GitHub releases** still work (the launcher reads both
  in dual mode), so you can migrate gradually.
- `schema/translation.schema.json` documents every field. Don't hand-write the
  hashes — let the Packager compute them.

Apache-2.0.
