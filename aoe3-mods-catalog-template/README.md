# aoe3-mods-catalog template

This folder is **scaffolding for a separate GitHub repo** — `Gorgorito12/aoe3-mods-catalog` (or whatever you call it). It is not meant to live inside the launcher repo. Copy these files into a fresh repo when you're ready.

---

## What's inside

```
aoe3-mods-catalog-template/
├── .github/
│   ├── scripts/
│   │   ├── classify_pr.py          PR change classifier (tier1/2/3/invalid)
│   │   └── validate_images.py      Icon/banner/hero/screenshot spec checks
│   └── workflows/
│       └── auto-merge.yml          Orchestrates classify → validate → decide
└── schema/
    └── mod.schema.json             JSON Schema every mod.json is checked against
```

The workflow runs on every PR. It classifies the diff into one of four buckets:

| Tier | What changed | Action |
|---|---|---|
| **invalid** | Touched files outside `/mods/`, multiple mods at once, or unknown filenames | PR is blocked with an explanatory comment |
| **tier1** | Only cosmetic fields (`displayName`, `description`, `accentColor`, icon, banner, single hero, screenshots) — image files must use the conventional names (`icon.png`, `banner.*`, `hero.*`, `screenshot1..8.*`) | Auto-merge after schema + image validation passes |
| **tier2** | Only `approvedReleaseTag` bumped (and maybe tier1 alongside) | Auto-merge after validation passes |
| **tier3** | Critical fields (`install.*`, `update.*` — including `followLatest` —, `sourceRepo`, `id`, `translations`, rotating `heroImages`, or first-time mod submission) | Labelled `needs-manual-review`; you approve manually |

The intent is: **you only see the PRs that actually need a human decision**. Cosmetic edits and version bumps merge themselves.

---

## One-time setup checklist

After copying this folder into a new repo:

### 1. Create the repo
- Make it public (so the launcher can pull `mod.json` files via `raw.githubusercontent.com` without auth).
- Initialise with the contents of this template.

### 2. Branch protection on `main`
Settings → Branches → Add rule for `main`:
- ✅ **Require a pull request before merging**
- ✅ **Require status checks to pass before merging**
  - Add the workflow job names as required checks (after the workflow runs once and registers them):
    - `Validate and auto-merge / Classify changes`
    - `Validate and auto-merge / Validate manifest and assets`
- ✅ **Require branches to be up to date before merging**
- ❌ **Require approvals** — set to **0** (the workflow gates merges via required status checks instead, so manual approval isn't needed for tier1/2)
- ✅ **Restrict who can push to matching branches** — only your account
- ✅ **Do not allow bypassing the above settings** — even you go through PRs

### 3. Allow auto-merge
Settings → General → Pull Requests → ✅ **Allow auto-merge**.

### 4. Allow Actions to manage PRs
Settings → Actions → General → **Workflow permissions**:
- ✅ **Read and write permissions** (or scope to what `permissions:` already declares in the yml)
- ✅ **Allow GitHub Actions to create and approve pull requests** — needed so the workflow can comment / label

### 5. Add `CODEOWNERS`
Create `.github/CODEOWNERS`:

```
# Default — only you can land changes outside /mods/
*                          @your-username

# Each mod's folder is owned by its author. Required for the
# "modder iterates on their carpeta without bothering you" flow.
# (The two first-party entries — wol and the detect-only aoe3-tad —
# are mirrored here for the public listing; the launcher's built-ins
# shadow them at runtime, so a PR can't redirect their behaviour.)
/mods/wol/                 @your-username
/mods/aoe3-tad/            @your-username
/mods/improvement-mod/     @your-username

# Add new modders here as you accept their first PR:
# /mods/napoleonic-era/     @autor_napoleonic
```

### 6. Add `CONTRIBUTING.md` for modders
A short doc telling them:
- The folder structure (`mods/<id>/{mod.json, icon.png, banner.png}` plus optional `hero.png/.jpg` / `screenshot1..8.*`)
- The image specs (icon: 1:1, 256–1024 px PNG ≤1 MB; banner: 4:1, 1200–4800 px PNG/JPG ≤2 MB; hero: 16:9, 1920–3840 px PNG/JPG ≤5 MB; screenshots ≤5 MB). Dimensions validate by aspect + width range, so any size up to 4K passes.
- The schema URL to point their editor at
- **Every image must be DECLARED in `mod.json` with the exact filename** (`"icon": "icon.png"`, `"heroImage": "hero.png"`, …) — uploading the file alone does nothing, and a declared name that doesn't match the real file (e.g. `hero.jpg` declared, `hero.png` uploaded) resolves to a 404 in the launcher
- That cosmetic and release-bump PRs auto-merge, including single hero and screenshots (their conventional names are on `classify_pr.py`'s `ALLOWED_ASSETS` whitelist) — **except rotating `heroImages`**: their filenames are free-form, so those PRs land in tier 3 (manual review) even though `validate_images.py` fully validates them

The validation workflow + schema make this template enforce most of the rules automatically; CONTRIBUTING.md is mostly for ergonomics.

---

## How the auto-merge logic works (sequence)

```
PR opened/updated
       │
       ▼
┌────────────────┐
│   classify     │   git diff vs base
│ (Python script)│
└──────┬─────────┘
       │
       ├─── invalid ──▶ comment + fail workflow ─▶ branch protection blocks merge
       │
       ▼
┌────────────────┐
│   validate     │   ajv validate + Pillow image checks
└──────┬─────────┘
       │   (only runs if classify said tier1/2/3)
       ▼
   ┌───┴───────────────────────┐
   │                           │
tier1 / tier2 ──▶ auto_merge   tier3 ──▶ request_review
   │                           │
   ▼                           ▼
 gh pr merge --auto        label + comment
   │                           │
 status checks pass            (you approve manually)
   │                           │
 PR squash-merges              │
                               ▼
                        you click merge
```

If any step fails, the PR stays open with status checks red. The author can push fixes; the workflow re-runs from scratch on the new diff.

---

## Tweaking the tier rules

The single source of truth for what counts as tier 1/2/3 is at the top of `.github/scripts/classify_pr.py`:

```python
TIER_1_FIELDS = {"displayName", "subtitle", "description", "accentColor",
                 "author", "officialWebsite", "icon", "banner",
                 "heroImage", "screenshots"}
TIER_2_FIELDS = {"approvedReleaseTag"}
TIER_3_FIELDS = {"id", "sourceRepo", "install", "update", "translations"}
```

Asset files are gated separately by `ALLOWED_ASSETS` (same script): only the
conventional names (`icon.png`, `banner.*`, `hero.*`, `screenshot1..8.*`,
`mod.json`) can ride a tier-1 PR — any other file in a mod folder forces
tier 3. If you add a schema field that references new asset files, add their
exact names there too, or asset-only PRs stop auto-merging.

**Be conservative when reclassifying down (3 → 2 or 2 → 1).** Anything that controls what the launcher executes or downloads must stay in tier 3 — that's the security boundary.

Adding new schema fields? Add them to one of the three sets here, otherwise the script falls through and labels the PR tier 3 (safe default).

---

## Limitations / caveats

- **The workflow doesn't approve PRs.** It enables auto-merge; the actual merge happens because branch protection requires status checks (not approvals). If you DO want approvals required, you'll need a separate bot account or a GitHub App, since `GITHUB_TOKEN` cannot approve PRs by design.
- **First-time mod submissions are always tier 3.** The script forces this regardless of what fields the manifest declares — a maintainer must vet new authors. After the first merge, subsequent PRs from the same modder follow normal classification.
- **The classifier reads the diff, not the contents of the new mod.json alone.** A PR that only changes `accentColor` from `#ff0000` to `#ff0001` is tier 1; a PR that "rewrites" the same `accentColor` value (no actual change) doesn't trigger anything. This matters because some clients write a no-op diff when the file is touched but content is unchanged — those are no-ops by construction.
- **Image validation is by aspect ratio + width range, not exact size.** An icon passes if it's square (±2%) and 256–1024 px wide; banner/hero likewise validate shape + range (±3%). A wrong shape or an out-of-range width fails; to loosen the tolerances, edit the spec tuples at the top of `validate_images.py`.
- **Only DECLARED images are validated.** `validate_images.py` checks the files referenced by `mod.json`; an uploaded file that nothing declares is ignored by both the validator and the launcher (see the CONTRIBUTING point above — every image must be declared with its exact filename).
