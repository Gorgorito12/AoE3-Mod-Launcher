export const meta = {
  name: 'repair-design',
  description: 'Design a safe, generic smarter mod-repair system and adversarially verify every item (read-only)',
  phases: [
    { title: 'Design', detail: '3 independent designs from different angles' },
    { title: 'Synthesize', detail: 'merge into one candidate work-item list' },
    { title: 'Verify', detail: '3 adversarial lenses per work item' },
    { title: 'Roadmap', detail: 'final ordered roadmap with verifier feedback applied' },
  ],
}

const ROOT = 'C:\\Users\\Jeison\\Documents\\GitHub\\AoE3-Mod-Launcher'
const MAPS = 'C:\\Users\\Jeison\\AppData\\Local\\Temp\\claude\\C--Users-Jeison-Documents-GitHub-AoE3-Mod-Launcher\\07f8f194-4852-4460-90c4-b7505c7386b7\\tasks\\wfxc7r55n.output'

const RULES = `STRICT RULES: READ-ONLY investigation (session is in plan mode). You MUST NOT edit/write/create/delete files, build, run tests, or change git state. Only Read/Grep/Glob and read-only shell. Repo: ${ROOT} (WarsOfLibertyLauncher\\ + WarsOfLibertyLauncher.Tests\\). Cite file:line for claims; verify in code rather than trusting the brief when it matters.
A full investigation of the current repair system already exists as JSON at: ${MAPS} (keys result.maps[] with area/summary/facts/invariants/gaps/opportunities/openQuestions, and result.critic). Read it with PowerShell, e.g. (Get-Content '<path>' -Raw | ConvertFrom-Json).result.critic, or .result.maps | where area -eq 'payload-sources'. Read at least the critic and the maps relevant to your task.`

const GOAL = `USER GOAL (the maintainer, in Spanish): "Make the mod repair system smarter. It must be SERIOUS: tell me what can be done WITHOUT BREAKING anything, and it must apply to ALL current mods AND future mods" (generic, driven by profile data, never by mod id).

CONDENSED BRIEF of what the investigation found (verified in code unless noted):
TODAY: Repair (MainWindow.RepairInstallAsync ~8006-8593) = if manifest has FileHashes, verify overlay (VerifyService.VerifyAgainstManifest, SHA-256 of everything); intact -> no download; ANY damage -> re-download the WHOLE payload (WoL 5.28 GB) and re-lay the whole overlay via NativeInstallService.InstallModOnlyAsync (no backup, no rollback, in-place FileMode.Create), then rewrite a brand-new manifest. Same method also serves GitHubReleases update, version pick, auto-continue, baseline rescue. Granular repair (RepairFilesAsync) existed 2 days (commit 64bd61d) and was removed in 8767ab2 — maintainer model "reinstale todo junto con las actualizaciones"; its technical flaws: still downloaded the full zip, returned early (skipped update chaining), null progress, didn't rewrite manifest, restored base-payload bytes over WoL-patched files (wrong version).
REAL BUGS in current repair (all verified):
 B1 InstallModOnlyAsync writes clonedAoe3:false + aoe3SourcePath:null -> after any repair/full GH update of an IsolatedFolder clone, Uninstall leaves the multi-GB clone behind; delta path carries them forward (NativeInstallService.cs:626 vs 794-797).
 B2 installLabel never passed -> repairing a COPY re-points the primary's desktop/StartMenu shortcuts + canonical Add/Remove key ({EB448764..}_is1, which WoL detection RegistryService.FindInstallPath reads) at the copy, and the copy's manifest gets the primary's ProductGuid -> uninstalling the copy deletes the primary's key. Also hits GH updates/delta of copies.
 B3 Plain GH repair re-lays EffectiveGitHubTag (approved/cached latest), not installed/pinned version; intact repair STAMPS LastKnownVersion=effective tag without laying it -> Update button disappears, false "update finished" bell, pin broken, possible DOWNGRADE (Improvement Mod installed 06.09.2026 > approved 19.07.2026 when followLatest cache empty).
 B4 Addon re-apply runs even on intact repair; AddonService.ReapplyAllAsync deletes backups/ownership first then backs up addon's OWN bytes -> original mod files lost permanently, addon can't be disabled cleanly; NSIS/missing-archive addons get orphaned.
 B5 Translation: plain repair re-lays English over covered files but ReconcileAfterUpdate only runs when asUpdate -> translation silently lost while UI says active (WoL main copy has ES-LA active on this machine). RefreshOriginalsSnapshot can poison _originals with translated bytes on paths that don't rewrite all covered files.
 B6 Verify vs Repair different checks: Verify reports engine/structural damage, its Retry -> Repair says "nothing to repair". WriteManifest re-baselines EngineFileHashes from disk -> blesses a corrupt engine file.
 B7 Repair offered/reachable for DelegatedExternal/Manual via Properties/Verify-Retry; global WoL-named payloadZipUrls/InstallerZipUrl override applies to ANY non-GH mod.
 B8 Plain repair damaged branch lacks the patch-only baseline rescue (GH deltaPatches).
 B9 No PayloadFileBlockedException handling in repair (antivirus loop re-downloads multi-GB forever); disk space check fixed 3 GiB (WoL really needs ~10.6 GB temp); UAC relaunch has no resume arg; offline -> UAC+space prompts before failing; EnsureGameNotRunning by process NAME only (age3y.exe shared by WoL copies/SoI/TAD; age3k.exe by K&B/King's Return) and only once before a multi-minute verify; unreadable (locked/AV-scanning) file counted as CORRUPT -> multi-GB redownload; progress panel with error text + Retry is inside Collapsed LegacyPlayContent -> user never sees verify details/Retry; success message overclaims ("all files verified") while recheck is a random 200-file zero-byte sample; Pause is a no-op for native downloads.
 B10 Non-transactional: interrupted repair/update leaves truncated/mixed files under old manifest; Manifest.Save non-atomic and write errors swallowed.
 B11 Retroactive clone cleanup (CloneFilesRemovedByPatches / SupersedeCompiledXml) never runs on intact repair or WolPatcher update -> old WoL installs keep 10 vanilla .XMB (vanilla proto/techtree!) and Repair can't fix them.
 B12 privateSetupPath: private HKLM key is per MOD not per install (copies overwrite each other's setuppath); clone-sourced exe (SoI age3y.exe) is in no hash map; renaming DisplayName makes re-overlay throw.
 B13 WoL-only structural verify layer (zulushield/AI3/.bar/sound) applied to ANY WolPatcher profile.
FACTS FOR A SMARTER DESIGN:
 - Range reads verified live (curl): GitHub CDN 302 -> SAS URL -> 206. WoL payload = ONE single-disk Zip64 archive split by bytes into 3 parts (1,992,294,400+1,992,294,400+1,298,797,955 = 5,283,386,755), 54,162 entries, CD 5.36 MB entirely in part 3, flat layout, deflate. IM and K&B = classic zips. RemoteZipIndex is single-URL, refuses Zip64, keeps no local-header offsets.
 - Proposed primitive: seekable HTTP-range Stream (block cache, 206-only, redirect-resolved URL cached) + composite stream mapping logical offsets across ordered parts, handed to System.IO.Compression.ZipArchive (which already reads WoL Zip64 in the installer) -> fetch only damaged entries. Unverified: .NET HttpClient Range across the 302; ZipArchive needs CanSeek.
 - Trust anchor must be the local manifest SHA-256 captured at a verified install (remote CRC32 is unauthenticated). BUT FileHashes != payload bytes for: privateSetupPath exe (patched), addon-owned files (re-captured with addon bytes), WoL files changed by .tar.xz patches after the payload, delta-hop files, translation-covered files (hashed vs _originals). Proposed: record a separate PayloadHashes / provenance map at canonical-lay time; a file is payload-restorable only when FileHashes[p]==PayloadHashes[p] and not owned by addon/translation/patcher.
 - Engine/clone files: only 7 EngineCandidates fingerprinted; Aoe3SourcePath recorded (until B1 erases it). Hash-proven restore from source roots (Aoe3SourcePath, Aoe3ManualPath, AoE3Detector.FindAll, root then bin\\) is possible; never for InPlaceOverlay/stock; never restore deliberately removed files (CloneFilesRemovedByPatches, superseded XMB, delete.lst).
 - Old manifests lack new fields; older launcher builds silently DROP unknown manifest/config fields (no JsonExtensionData) -> new fields must be optional, recomputable, absence = unknown.
 - No tests cover RepairInstallAsync/InstallModOnlyAsync/WriteManifest/VerifyInstallation; all logic lives in MainWindow code-behind. Pure extracted helpers are the tested ones.
 - Real catalog mods: wol (built-in, IsolatedFolder, WolPatcher, DirectPayload, 3-part Zip64, SHA-pinned, translations, CloneFilesRemovedByPatches), aoe3-tad (stock, detect-only), improvement-mod (GH followLatest, exe probe no marker), knights-and-barbarians-remastered (GH followLatest, supersedeCompiledXml, userDataPayload, userDataRedirect, previousIds), napoleonic-era (GH, privateSetupPath, MP probes n files), struggle-of-indonesia (GH, privateSetupPath, exe from clone, userDataRedirect). Unused-but-supported shapes a future mod may use: InPlaceOverlay, deltaPatches/patch-only, split .zip.NNN, external hosting+sha, community WolPatcher, translations on non-WoL.`

const ITEMS = {
  type: 'object',
  properties: {
    angle: { type: 'string' },
    philosophy: { type: 'string' },
    items: { type: 'array', items: { type: 'object', properties: {
      id: { type: 'string' }, title: { type: 'string' },
      problem: { type: 'string', description: 'What is wrong / missing today, with evidence' },
      change: { type: 'string', description: 'Concrete change: which methods/classes, new pure functions, data fields' },
      files: { type: 'array', items: { type: 'string' } },
      reuse: { type: 'string', description: 'Existing functions/utilities to reuse, with paths' },
      generality: { type: 'string', description: 'How it applies to every mod shape and future mods; what it is keyed on' },
      safetyGuards: { type: 'string', description: 'Why it cannot break anything; fail-closed fallback' },
      tests: { type: 'string' },
      dependsOn: { type: 'array', items: { type: 'string' } },
      effort: { type: 'string', enum: ['S', 'M', 'L', 'XL'] },
      needsMaintainerDecision: { type: 'string', description: 'Empty if none' },
    }, required: ['id','title','problem','change','files','reuse','generality','safetyGuards','tests','dependsOn','effort','needsMaintainerDecision'] } },
  },
  required: ['angle', 'philosophy', 'items'],
}

const ANGLES = [
  { key: 'safety-first', prompt: 'ANGLE: SAFETY FIRST. Prioritise never losing user data, never writing unproven bytes, transactional behaviour, fixing the current bugs before adding cleverness, and fail-closed fallbacks to today\'s full re-overlay. Every smart path must be optional and provably equivalent to the full path.' },
  { key: 'incremental', prompt: 'ANGLE: INCREMENTAL / SHIPPABLE. Design a sequence of small, independently shippable PRs, each valuable on its own and each reversible. Prefer extracting pure, unit-testable planners out of MainWindow before behaviour changes. Minimise risk per release; state what each step unlocks.' },
  { key: 'generic-future', prompt: 'ANGLE: GENERIC FOR ALL AND FUTURE MODS. Design the repair around a data-driven model: a per-file provenance/ownership model (payload, patch, delta, addon, translation, patcher, clone/engine, launcher artifact) and a pure resolver that picks the correct source for each damaged file, working for every combination of InstallType x UpdateMechanism x payload shape (single, split, Zip64, external, patch-only) x feature flags, including shapes no mod uses yet. Nothing keyed on mod id. Explain how a future mod gets smart repair with zero code.' },
]

phase('Design')
const designs = (await parallel(ANGLES.map(a => () =>
  agent(`${RULES}\n\n${GOAL}\n\n${a.prompt}\n\nProduce a complete design as work items. Include both (a) fixes for the current bugs that any smarter repair depends on, and (b) the smarter capabilities (verify once/one verdict, per-file source resolution, range-download of only damaged entries, hash-proven engine restore, retroactive cleanup, transactional writes, UX surfacing). Be concrete about methods and data. Mark anything that conflicts with the maintainer's "reinstale todo junto con las actualizaciones" model as needing a decision.`,
    { label: `design:${a.key}`, phase: 'Design', schema: ITEMS })))).filter(Boolean)
log(`${designs.length} designs`)

phase('Synthesize')
const MERGED = {
  type: 'object',
  properties: {
    architecture: { type: 'string', description: 'The unified target architecture in prose: components, data model, flow' },
    items: ITEMS.properties.items,
    rejectedIdeas: { type: 'array', items: { type: 'object', properties: { idea: { type: 'string' }, why: { type: 'string' } }, required: ['idea','why'] } },
    decisions: { type: 'array', items: { type: 'string' }, description: 'Questions only the maintainer can answer, with a recommended default each' },
  },
  required: ['architecture', 'items', 'rejectedIdeas', 'decisions'],
}
const merged = await agent(`${RULES}\n\n${GOAL}\n\nYou are the synthesizer. Three independent designs follow. Merge them into ONE coherent target architecture and ONE deduplicated list of 12-20 work items (not more), each self-contained enough to be verified in isolation, with stable ids (R1, R2, ...), correct dependsOn, and effort. Take the safest correct version of each idea; graft the best ideas from each angle. Drop or fold duplicates. Explicitly list rejected ideas with why. Check claims against code where designs disagree.\n\nDESIGNS:\n${JSON.stringify(designs, null, 1)}`,
  { label: 'synthesize', phase: 'Synthesize', schema: MERGED })
if (!merged) return { designs }
log(`${merged.items.length} work items to verify`)

phase('Verify')
const VERDICT = {
  type: 'object',
  properties: {
    lens: { type: 'string' },
    verdict: { type: 'string', enum: ['keep', 'modify', 'drop'] },
    breaks: { type: 'array', items: { type: 'object', properties: {
      scenario: { type: 'string', description: 'Concrete mod shape / state -> what goes wrong' }, evidence: { type: 'string' } },
      required: ['scenario', 'evidence'] } },
    requiredGuards: { type: 'array', items: { type: 'string' } },
    notes: { type: 'string' },
  },
  required: ['lens', 'verdict', 'breaks', 'requiredGuards', 'notes'],
}
const LENSES = [
  { key: 'data-safety', q: 'DATA-SAFETY & INVARIANTS. Try hard to find a way this item loses or corrupts user data, writes unproven bytes, resurrects deliberately deleted files, breaks an existing documented invariant (CLAUDE.md, .claude/rules/addons.md, .claude/rules/multiplayer.md), breaks multiplayer fingerprint parity or byte-faithfulness, touches the player\'s real AoE3 (InPlaceOverlay/stock), or leaves a half-written install. Check the actual code paths it touches.' },
  { key: 'generality', q: 'GENERALITY ACROSS ALL MOD SHAPES. Walk the item through EVERY shape: wol (WolPatcher, Zip64 3-part, patched after payload, translations, CloneFilesRemovedByPatches), improvement-mod (GH followLatest, exe probe no marker), knights-and-barbarians-remastered (supersedeCompiledXml, userDataPayload, userDataRedirect, previousIds), napoleonic-era & struggle-of-indonesia (privateSetupPath, SoI exe from clone), stock aoe3-tad, additional COPIES (OtherInstalls), old manifests without FileHashes, manifest-less detected installs, and future shapes (InPlaceOverlay, deltaPatches patch-only, split .zip.NNN, external hosting+sha, community WolPatcher, DelegatedExternal/Manual). Find the shape where it misbehaves or where it silently only works for WoL. Anything keyed on a mod id is a failure.' },
  { key: 'feasibility-compat', q: 'FEASIBILITY & COMPATIBILITY. Is it implementable as described in this codebase (check the APIs it assumes exist: ZipArchive seek behaviour, HttpClient range+redirect, manifest serialization, WPF threading)? Is it compatible with old manifests/configs and with OLDER launcher builds that drop unknown JSON fields? Is the effort estimate honest? Does it need a maintainer decision that the item hides? Is there a simpler way to get most of the value?' },
]
const verified = await parallel(merged.items.map(it => () =>
  parallel(LENSES.map(l => () =>
    agent(`${RULES}\n\n${GOAL}\n\nTARGET ARCHITECTURE (context):\n${merged.architecture}\n\nWORK ITEM UNDER REVIEW:\n${JSON.stringify(it, null, 1)}\n\nYOUR LENS: ${l.q}\n\nBe adversarial: default to finding problems. verdict=keep only if you could not find a real break; modify if it is sound with the guards you list; drop if it is unsafe or not worth it.`,
      { label: `verify:${it.id}:${l.key}`, phase: 'Verify', schema: VERDICT })))
  .then(vs => ({ item: it, verdicts: vs.filter(Boolean) }))))
const reviewed = verified.filter(Boolean)
const tally = reviewed.map(r => `${r.item.id}: ${r.verdicts.map(v => v.verdict).join('/')}`).join(', ')
log(`verdicts: ${tally}`)

phase('Roadmap')
const ROADMAP = {
  type: 'object',
  properties: {
    executiveSummary: { type: 'string', description: 'For the maintainer, plain language: what repair will become, why it is safe, how it covers all/future mods' },
    phases: { type: 'array', items: { type: 'object', properties: {
      name: { type: 'string' }, goal: { type: 'string' },
      items: { type: 'array', items: { type: 'object', properties: {
        id: { type: 'string' }, title: { type: 'string' }, change: { type: 'string' },
        files: { type: 'array', items: { type: 'string' } }, reuse: { type: 'string' },
        guards: { type: 'string', description: 'All required guards from the verifiers, merged' },
        tests: { type: 'string' }, effort: { type: 'string' } },
        required: ['id','title','change','files','reuse','guards','tests','effort'] } } },
      required: ['name','goal','items'] } },
    dropped: { type: 'array', items: { type: 'object', properties: { id: { type: 'string' }, why: { type: 'string' } }, required: ['id','why'] } },
    decisions: { type: 'array', items: { type: 'object', properties: {
      question: { type: 'string' }, options: { type: 'string' }, recommendation: { type: 'string' } },
      required: ['question','options','recommendation'] } },
    verification: { type: 'string', description: 'How to verify end to end on Windows: build, tests, manual smoke per mod shape' },
    residualRisks: { type: 'array', items: { type: 'string' } },
  },
  required: ['executiveSummary', 'phases', 'dropped', 'decisions', 'verification', 'residualRisks'],
}
const roadmap = await agent(`${RULES}\n\n${GOAL}\n\nYou produce the FINAL roadmap. Apply every verifier finding: an item with any 'drop' vote must be either dropped or rewritten so that the drop reason no longer applies (say which); every 'modify' guard must be folded into the item's guards. Order into phases where each phase is independently shippable and never leaves the launcher worse than today; bug fixes that later phases depend on come first. Keep only items that survived. Be concrete (methods, files, pure functions, tests).\n\nARCHITECTURE:\n${merged.architecture}\n\nSYNTHESIZER DECISIONS:\n${JSON.stringify(merged.decisions)}\n\nREJECTED IDEAS:\n${JSON.stringify(merged.rejectedIdeas)}\n\nITEMS WITH VERDICTS:\n${JSON.stringify(reviewed, null, 1)}`,
  { label: 'roadmap', phase: 'Roadmap', schema: ROADMAP })

return { merged, reviewed, roadmap }
