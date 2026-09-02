---
description: Addon engine, risk gate, ownership record, Heaven download and NSIS extraction rules for the AoE3 Mod Launcher. Split out of CLAUDE.md so it only loads when working on the addon surface.
paths:
  - WarsOfLibertyLauncher/Services/Addon*.cs
  - WarsOfLibertyLauncher/Services/HeavenDownloader.cs
  - WarsOfLibertyLauncher/Services/NsisExtractor.cs
  - WarsOfLibertyLauncher/Models/AddonManifest.cs
  - WarsOfLibertyLauncher/ModPropertiesDialog.*
  - WarsOfLibertyLauncher.Tests/Addon*.cs
  - WarsOfLibertyLauncher.Tests/NsisExtractorTests.cs
  - WarsOfLibertyLauncher.Tests/HeavenDownloaderTests.cs
---

# Addon gotchas

Optional community addons (transparent UI, gun-smoke effects, building rotator) that
players currently copy into the mod folder by hand. **Update these invariants HERE**
when addon behaviour changes — same "document it as you change it" rule as
`CLAUDE.md`, different file.

Cross-cutting rules that merely *touch* addons stay in `CLAUDE.md`: the byte-faithful
install policy (`RemoveStaleBuildArtifacts`), the verify/repair contract, the
manifest re-fingerprint block, `AppPaths`, and the localization conventions.

Reference note: `AddonRisk.cs` cites *"the byte-faithful-install note in CLAUDE.md"* —
that note stays there, the cross-reference is intentional and bidirectional.

## Why an addon can't just be a file copy

Four systems already have opinions about what lives in an install, and an addon
walks into all four at once. This is the whole reason the feature has an engine
instead of a copy loop:

1. **Version detection** MD5s `data\protoy.xml`, `techtreey.xml` and
   `stringtabley.xml` to identify the build. Modify one and the install matches no
   known version, so the launcher queues the **entire patch chain** instead of an
   update.
2. **The multiplayer fingerprint** hashes those same three files into the
   `CombinedHash` the lobby validates, so a modified `protoy.xml` gets the player
   **rejected from every room**.
3. **Verify** compares every overlay file against `InstallManifest.FileHashes`, so
   addon files report corrupt — and **Repair**, which re-lays the whole overlay, then
   **wipes the addon** as a side effect of "fixing" it.
4. **Uninstall** deletes the install folder wholesale, so backups kept *inside* it are
   reclaimed for free — nothing to do.

Rules 1-2 are answered by `AddonRisk` (refuse), 3 by re-capturing fingerprints plus
`ReapplyAllAsync`, 4 by putting backups inside the install on purpose.

## The risk gate (`Services/AddonRisk.cs`)

- **`AddonRisk` is the safety core, and it is pure + WPF-free on purpose** (like
  `SafeUrl` / `PathDisplay`) so it is unit-testable off the STA thread. The
  **rejection** cases are the ones worth pinning — same philosophy as `SafeUrlTests`.

- **The protected list is SOURCED from `UpdateService`'s constants
  (`ProtoRelativePath` / `TechRelativePath` / `StrRelativePath`), never re-typed.**
  That is load-bearing: re-typing the three paths would let the block list drift from
  the paths detection and the fingerprint actually read, and the drift would be
  silent. A third subsystem keys off the same files — `stringtabley.xml` is snapshot
  into `translations\_originals\`, which both version detection and the MP fingerprint
  read *through*, so an addon writing it collides with the canonical-English copy.

- **Four levels, and `MultiplayerRisk` exists because of an asymmetry that is easy to
  miss.** `Blocked` (writes one of the three identity files) → refused outright.
  `MultiplayerRisk` → **warned, not blocked**. `Cosmetic` (art/sound/UI) → applies
  freely. `Empty` → nothing applicable to write. The asymmetry: **the fingerprint
  covers three files, not the simulation.** An addon editing any *other* `data\` file
  passes the lobby check and can still desync the match — the launcher cannot detect
  that later, so it has to say so up front.

- **`.xmb` is its own warned tier (`VersionMatchFiles`), reported SEPARATELY from
  `data\` changes, because the symptoms differ.** AoE3 hashes `.xmb` for its own LAN
  version check, so replacing them can stop the match from **starting** with peers who
  don't have the addon; a `data\` change **desyncs one already under way**. Don't
  merge the two lists — the warning has to name which failure the user is buying.
  Warned rather than blocked because the exact coverage of AoE3's CRC isn't publicly
  documented, and refusing outright would reject legitimate art packs on an assumption.
  (The gun-smoke addon replaces 77 `.xmb` files — this tier is not hypothetical.)

- **Matching is on the path TAIL (`EndsWith('\\' + p)`), not the full path**, so a
  protected file is caught no matter how many wrapper folders the packager nested it
  under — `MyAddon\data\protoy.xml` is the normal shape, not the exception.
  **Over-blocking is the correct direction to fail here:** a false positive costs one
  addon somebody applies by hand, a false negative costs a player silently locked out
  of every lobby.

- **Executables and author documentation are NEVER written into a game folder, and the
  skipped entries are reported BY NAME.** Real addons ship them: the building-rotator
  archive carries a **UPX-packed PE32**, a PDF and a screenshot next to the one config
  file that does the work. The launcher has no business silently placing a third-party
  binary — least of all a packed one, which is the exact heuristic that got this
  project's own `.exe` quarantined by Defender. Naming them matters: *"1 file skipped"*
  is useless when the addon then doesn't work, while naming `Building Rotator.exe`
  tells the user (or its author) exactly what was left out.

Pinned by `AddonRiskTests`.

## Wrapper stripping and path shape (`Services/AddonPaths.cs`)

- **A single wrapper folder is stripped (`AddonPaths.StripCommonRoot`), and the naive
  version of this rule is wrong.** Packing an overlay inside one folder is normal — the gun-smoke addon ships all 197
  files under `AO3/`. Extracted verbatim that lands in `<install>\AO3\art\…`, which
  the game never reads: **the addon doesn't fail, it silently does nothing**, with
  nothing in the UI suggesting why.

- **The subtlety the tests caught: a shared root is only a wrapper when it is NOT one
  of the game's own folders (`GameRootFolders`).** An addon shipping only art files has
  `art/` as its single common root, and stripping that would scatter every file into
  the install root — breaking an addon that was fine. `AO3/` is a wrapper, `art/` is a
  destination, and the folder name is the only signal available before anything is
  extracted. Stripping also requires that **every** entry share the root with nothing
  loose beside it.

- **This mirrors `NativeInstallService.NormalizePayloadRoot`** (same problem for mod
  payloads) but works from the **zip's entry names, before anything is written**,
  rather than on an already-extracted folder.

- **`AddonPaths.Normalize` emits FORWARD slashes, and this is load-bearing and
  invisible to the type system.** `NativeInstallService.RecaptureHashes` converts its
  input with `Replace('\\','/')` and matches it against `InstallManifest.OverlayFiles`,
  so a backslash path **silently fails to match**: the fingerprint never lands in
  `FileHashes`, verify keeps calling the addon's files corrupt, and Repair wipes the
  addon — **the exact failure this whole subsystem exists to prevent**. Caught by the
  fingerprint test; don't "tidy" the separators.

Pinned by `AddonPathsTests`.

## Ownership record (`Services/AddonOwnership.cs`)

- **The authoritative record is a SIDECAR — `<install>\addons\_owned.json` — NOT the
  install manifest, and the reason is a trap worth keeping.** Addons apply to any AoE3
  install, **including the player's own unmodded copy**, and that copy has no
  `install-manifest.json` because the launcher never installed it. Requiring one made
  every addon fail there.

- **Writing a manifest into the real game folder to "fix" that would be worse than the
  bug.** `AoE3Detector.IsCleanAoE3Folder` treats the presence of
  `install-manifest.json` as *"this is a mod install, not a clone source"* — so the
  launcher would quietly stop offering the player's own AoE3 as the base for installing
  new mods. A separate record avoids the question entirely.

- **The manifest is still used WHERE IT EXISTS** (it is what keeps "Verify files"
  honest about a modded install) — it is just no longer *required*. Both
  `ApplyCoreAsync` and `DisableAsync` guard with `InstallManifest.TryLoad(...) != null`.

- **The migration from `InstallManifest.AddonFiles` is NOT optional.** Addons enabled
  before this change recorded their files only there; without absorbing them they
  become **impossible to disable** — the launcher would show them on with no idea which
  files to restore. A corrupt `_owned.json` also falls through to the manifest rather
  than making an install unmanageable.

- **Reverting is RECORDED, never derived.** The record owns the file list per addon
  because the zip may be gone, or a newer version of it may ship a different file set
  than what is actually on disk. Files **with** a backup are restored; files the addon
  **added** (no backup) are deleted.

Pinned by `AddonOwnershipTests`.

## Apply / disable / re-apply (`Services/AddonService.cs`)

- **The order in `ApplyCoreAsync` is load-bearing: risk check → conflict check → BACK
  UP → extract → record → re-capture.** Backing up before extracting is what makes
  disabling reversible; re-capturing **last** is what stops verify from calling the
  result corrupt. Backups live at `<install>\addons\_originals\<id>\`, **inside the
  install on purpose** (mirroring `translations\_originals\`): uninstall reclaims them
  for free and they travel with a moved install.

- **Two addons may not own the same file — the second is refused (`Conflict`).**
  Whoever disabled second would restore the FIRST one's file as the "original", and
  there is no merging overlay binaries.

- **`allowMultiplayerRisk` never bypasses `Blocked`.** It is set only after the user
  confirmed a `MultiplayerRisk` addon; `Blocked` is unconditional.

- **A declared `includeOnly` list is EXHAUSTIVE and overrides the extension rules**,
  because the reviewer who wrote it already decided. Null (an imported archive, which
  has no manifest) falls back to the automatic skip rules. Declaring the list is what
  makes a catalog PR auditable: the reviewer sees precisely which game files the addon
  writes **before any player runs it**.

- **Path traversal is rejected** — an entry resolving outside the install root is never
  legitimate, logged and dropped.

- **`ReapplyAllAsync` DISCARDS the backups first, and this is the least obvious rule
  here.** After a re-overlay the files on disk are the NEW version's, but
  `addons\_originals\` still holds the PREVIOUS version's bytes. Re-applying without
  clearing them leaves a backup that, when the addon is later disabled, **restores the
  old version's file over the new one** — a silent downgrade that verify would then
  *bless*, because re-capturing makes the manifest agree with whatever is on disk.
  Taking a fresh backup from the freshly-laid files is the only correct order.

- **Re-apply reuses the files the addon owned LAST time as its include list**, captured
  **before** the record entry is cleared. Those already went through the declared list
  or the skip rules on first apply, so this reproduces the same set — and keeps a
  skipped executable skipped on the second pass — without needing the catalog manifest
  at that point. It also passes `allowMultiplayerRisk: true`: the user accepted the
  risk when they enabled it, and re-prompting mid-update isn't possible.

- **Re-apply is wired into the TWO flows that re-lay overlay files**, and both are
  **best-effort by construction** — a cosmetic overlay failing to come back must never
  turn a successful update into a failed one:
  - `UpdateService.ApplyUpdatesAsync` → `ReapplyAddonsAsync`, **after** the post-patch
    hash refresh (re-applying re-captures those files again with the addon's bytes).
  - `MainWindow.RepairInstallAsync` → `ReapplyAddonsAfterOverlayAsync`, **before** the
    success branch reports "repaired", so the state the user is told about is final.
    **The repair case is the one that actually bit** — a repair re-lays the whole
    overlay, so without this it wipes every addon as a side effect of "fixing" the
    install.

- **`AddonStore` copies the archive into `%LocalAppData%\AoE3ModLauncher\addons\`
  rather than remembering where the user's file was.** `ReapplyAllAsync` needs the
  archive again after **every** update and repair; the user's copy lives in Downloads
  and they delete it, move it, or reinstall Windows — and then the first update
  silently loses their addons with nothing explaining why. It also unifies the two
  sources, so re-apply never has to know whether an addon came from the catalog or an
  import. Ids are content-derived (`local-<sha12>`) so re-importing the same archive is
  recognised instead of stacking duplicates. `Remove` runs when the user **removes** an
  imported addon, not when they merely **disable** it — disabling is reversible and
  re-enabling needs the archive back.

Pinned by `AddonServiceTests`.

## Downloading from AoE3 Heaven (`Services/HeavenDownloader.cs`)

- **The two-step flow, and the correction worth remembering.** An earlier round
  concluded the launcher *could not* download from Heaven and that this was a technical
  impossibility — that was wrong, and it was wrong because the test used an aged-out
  token copied from a browser. The site simply serves downloads in two steps:
  `showfile.php?fileid=N` renders a page carrying a per-file token in an inline handler
  (`get_file('1932','caf2c858…')`), and `getfile.php?id=N&dd=1&s=<token>` returns the
  archive. Verified end to end: `application/zip`, 1,055,693 bytes.

- **The token ROTATES, so it must be read fresh on every download** — a link pasted
  from a browser stops working. `ParseToken` is **pure and separately tested against a
  saved copy of the real page** (`Tests/Fixtures/heaven-showfile-1932.html`), because
  that regex is what breaks the day Heaven changes their markup, and a unit test is the
  only way to notice without reaching the network.

- **Validation is by ZIP MAGIC BYTES (`PK\x03\x04`), not Content-Type and not the
  status code.** Every failed attempt while building this returned a perfectly valid
  **HTTP 200 serving an HTML interstitial**; writing that out as a `.zip` produces a
  corrupt addon whose error surfaces much later, far from the cause.

- **The User-Agent identifies the launcher rather than impersonating a browser**
  (verified the site accepts it). If it is ever refused, prefer asking them over
  spoofing — the catalog re-host path exists precisely so this scrape stays optional.

Pinned by `HeavenDownloaderTests`.

## NSIS installers (`Services/NsisExtractor.cs`)

- **Running the installer is the SAFER option here, not the reckless one — and the
  alternative it replaces is not "do nothing".** The transparent-UI addon is
  distributed only as an NSIS self-extractor whose payload is ordinary game content (25
  `data\ui*.xml.xmb` layouts + 11 `art\ui\ingame\*.ddt` textures). Parsing NSIS
  in-process was rejected (no existing dependency handles it — SharpCompress covers
  zip/rar/7z/tar/gzip — and a hand-written parser for untrusted input is a poor trade
  for one addon). Running it **against the game folder** was rejected too: the files
  would land with no backup and no manifest entry, so verify would call them corrupt
  and Repair would wipe them. The real alternative was telling the player to run the
  same installer themselves, against their game, with no way to undo it. Running it
  into a **scratch folder** and applying the result through the normal path inverts all
  of that.

- **The scratch destination is ASSERTED to sit under `AppPaths.DataDir`, not assumed.**
  The entire safety argument rests on the installer writing somewhere disposable.

- **`BuildArguments` is pure because NSIS's rules here fail SILENTLY when wrong** — the
  installer runs, ignores the destination, and writes wherever it defaults to. `/D=`
  **must be last, must NOT be quoted, and must have no trailing separator.** Unquoted is
  what lets it contain spaces at all: NSIS reads everything after `/D=` to the end of
  the command line. **This is why the caller must use `ProcessStartInfo.Arguments`, not
  `ArgumentList`**, which would add quotes of its own.

- **`UseShellExecute = true` is required, not a style choice.** Some of these installers
  declare `requireAdministrator`, and with `UseShellExecute = false` Windows refuses to
  start them outright ("The requested operation requires elevation") because the
  launcher deliberately runs `asInvoker`. ShellExecute lets Windows show its own consent
  prompt. Note `CreateNoWindow` does not apply in this mode; `WindowStyle` does.

- **A declined UAC prompt (`Win32Exception` native code 1223 / ERROR_CANCELLED) is a
  DECISION, not a failure** — hence `NsisExtractionException.DeclinedByUser`. Reporting
  it as an error tells the user something broke right after they chose that it
  shouldn't happen.

- **A 2-minute timeout then kills the process**: a custom installer page can ignore
  `/S` and sit waiting for a human, and without it the launcher waits forever. Exiting
  successfully but writing **nothing** is also treated as a failure (it probably ignored
  the destination).

- **`ApplyFromFolderAsync` shares the ENTIRE core with the archive path** — same risk
  gate, same conflict check, same backups, same re-capture. Only where the bytes come
  from differs. A second implementation would be a second place for the safety rules to
  drift.

Pinned by `NsisExtractorTests`.

## The offered addons (`Services/AddonRegistry.cs`)

- **Hard-coded, for the same reason `ModRegistry` keeps built-in mod profiles:** it
  works on a cold start with no catalog fetch and — more to the point here — **without
  waiting on the authors' permission to re-host their files**. A catalog-backed list can
  merge in later without touching the UI.

- **Every entry's contents were read from the real archive rather than assumed, and each
  surprise changed the design** — that is why the per-entry comments exist:
  - `heaven-1932` (building rotator) ships a UPX-packed executable, a PDF and a
    screenshot next to the startup configs, so it declares `IncludeOnly` with all three
    `startup/game*.con` variants (the engine reads the one matching its executable:
    `game`/`gamex`/`gamey` ↔ vanilla / WarChiefs / TAD).
  - `heaven-3730` (gun smoke) keeps all 197 files under a wrapper folder — **this is
    the entry that exposed the missing wrapper stripping** — and replaces 77 `.xmb`
    files, so it lands in `MultiplayerRisk` and the user must confirm.
  - `heaven-1656` (transparent UI) is a single `.exe` that is an NSIS self-extractor, so
    it is `AddonPackaging.NsisInstaller`. A plain checkbox would have been a lie.

- **`AddonManifest` (catalog `addons/<id>/addon.json`) pins a REQUIRED SHA-256**, since
  an addon writes into the player's game folder and "whatever that URL serves today" is
  not good enough; it is verified before a single file is extracted
  (`AddonApplyStatus.HashMismatch`). It is **deliberately NOT part of
  `ModCatalogManifest`**: addons overlay the stock AoE3 files every mod clones, so they
  apply to any mod rather than belonging to one, and duplicating them per `mod.json`
  would mean editing three manifests to fix one URL.

## Config surface

- `ModState.EnabledAddons` — **per install**: what *should* be on. Sits beside
  `ActiveTranslationId` because it is the same kind of state (a user choice that
  modifies files inside ONE install and must be re-applied after an update/repair).
- `LauncherConfig.ImportedAddons` — **launcher-wide on purpose**: these overlay the
  stock AoE3 files every mod clones, so one import is usable by every install. Caches
  the risk level at import time so rendering the tab doesn't reopen every archive; the
  **authoritative** check still runs inside `ApplyAsync`, which re-reads the zip.
- `InstallManifest.AddonFiles` — now the **legacy/migration** source (see
  `AddonOwnership`), still written for modded installs.

⚠ **Two stale doc-comments in `Models/LauncherConfig.cs`**, both overtaken by later
commits — correct them if you touch that file, and don't trust them meanwhile:
`EnabledAddons` still claims the manifest's `addonFiles` is "the authoritative record"
(it is `_owned.json` now), and `ImportedAddons` still claims "the launcher cannot fetch
them" (`HeavenDownloader` does).

## UI

The **ADDONS tab** is the fifth tab of `ModPropertiesDialog` (gear → Mod Properties). Every route goes
through `AddonService`, so the risk gate, the backup and the manifest re-capture apply
**no matter which button was pressed** — don't add a path that writes addon files
directly.

### The 5d layout, and the one rule that governs every figure on it

`SPEC-2` §5d rebuilt this tab. It is **two groups**, because the two sources are not the same
promise: `AddonCardList` holds `AddonRegistry.All` (checked against a pinned SHA-256 before a
byte is written, belonging to THIS install) and `ImportedAddonList` holds
`LauncherConfig.ImportedAddons` (whatever the user pointed at, copied into the launcher's own
folder, offered to every mod). Stacking them made those look like one kind of thing. Import
lives in the IMPORTED group's header, not at the bottom of the page — and `AddonsEmptyHint`
belongs to that group ALONE, because the catalog group is never empty and "no addons yet"
under a list of three was simply wrong.

Each card states, in this order: the name with its state chips (`ACTIVE`, then the risk chip,
then `INSTALLER`), one line of description, a mono line of figures, and a notice box per
consequence — amber for "this can break a match", red for "the launcher will not do this",
neutral for "here is how this one is delivered". An enabled addon is marked by its `ACTIVE`
chip and a brighter rim at **1 px**, never a border that grows to 2 and shifts the card's
contents by a pixel.

⚠ **Every figure on this screen is READ, never estimated.** `FactsFor` opens the archive,
counts its entries and runs `AddonRisk.Assess` over them; the counts and the risk chip come
from that and from nothing else. Three consequences, all deliberate:

1. **An addon nobody has downloaded yet shows its row and NO numbers.** There is no archive to
   read, so there is nothing true to say about what it writes. Do not fall back to what
   `AddonRegistry` declares — the registry records what was true when it was written, and the
   file is what will actually be extracted. It is the same reason `DownloadAndEnableAsync`
   already assesses the DOWNLOAD rather than the registry entry.
2. **An NSIS entry returns null from `FactsFor` on purpose.** Its archive holds the installer,
   not the files the installer produces, so counting its entries would report a figure about
   the wrong thing. It still gets its `INSTALLER` chip and its notice, both from
   `AddonEntry.Packaging`, which is declared and therefore known before any download.
3. **`_addonFacts` is cleared at the top of `LoadAddons`.** A download that just landed makes
   an archive readable that was not there when the tab opened, and the card should then show
   the figures rather than keep the empty ones it was drawn with.

**"EN CONFLICTO" from the reference is NOT implemented, and that is the same rule.** A
conflict is only known at apply time (`AddonApplyStatus.Conflict` + `ConflictingAddonId`,
computed from the ownership record); nothing stores a standing verdict, and a chip that
guessed one would be wrong exactly when it mattered.

For an imported addon the cached `ImportedAddon.Risk` is the FALLBACK, not the source: the
archive is re-read when it is still in the store, so a launcher update that changes the risk
rules is reflected without a re-import.

⚠ **`ImportedAddon.RiskFiles` must include `VersionMatchFiles`.** It used to be
`BlockingFiles.Concat(SimulationFiles)` only, so an addon that is `MultiplayerRisk` PURELY
because of its `.xmb` entries stored an empty list — and the card that exists to name the
offending files had nothing to name, warning in the abstract about exactly the case
`AddonRiskAssessment` separates out in order to be concrete about. Fixed in
`ImportAddonBtn_Click`; keep the three lists together if you touch it.

Blocked and multiplayer-risk addons **name the files that caused the verdict**: *"this
addon is dangerous"* is unactionable, while *"it replaces `data\protoy.xml`"* tells the
user, or its author, exactly what to change. Strings follow the repo's rules — EN/ES,
`es` in neutral Latin-American **tuteo**, no emoji in labels.
