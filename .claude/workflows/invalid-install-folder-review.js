export const meta = {
  name: 'invalid-install-folder-review',
  description: 'Adversarial review of the invalid-install-folder fix: four read-only lens reviewers, each followed by a skeptical verifier',
  phases: [
    { title: 'Review', detail: 'four read-only reviewers, one lens each' },
    { title: 'Verify', detail: 'one skeptic per reviewer tries to refute each finding' },
  ],
}

const REPO = 'C:/Users/Jeison/Documents/GitHub/AoE3-Mod-Launcher'

const CONTEXT = `
REPO: ${REPO} — Windows-only WPF .NET 8 launcher for Age of Empires III mods. Main project WarsOfLibertyLauncher/ (namespace WarsOfLibertyLauncher, assembly Aoe3ModLauncher), tests WarsOfLibertyLauncher.Tests/ (xUnit). Project rules are in ${REPO}/CLAUDE.md (huge — grep it).
STRICTLY READ-ONLY: do not modify, create or delete any file; no builds, no tests, no git state changes. Reading, grep, 'git diff' / 'git show HEAD:<path>' and read-only PowerShell probes of .NET behaviour (e.g. [IO.Path]::GetPathRoot('...')) are fine.

THE CHANGE UNDER REVIEW (uncommitted; see 'git -C ${REPO} diff' plus the new untracked files): the fix for "mods whose name is not a valid folder name download, then fail to install". Catalog mod 'aoe1-hd-edition' (displayName "Age of Empires 1: HD Edition") was installed into Path.Combine(aoe3, DisplayName); NTFS reads ':' as a stream separator, so after a 301 MB download Directory.CreateDirectory failed, and shortcuts were written as NTFS streams on a 0-byte 'Age of Empires 1' file. The approved plan is at C:/Users/Jeison/.claude/plans/pasted-content-id-7abf-sesi-n-vectorized-lantern.md — read it.
New files: WarsOfLibertyLauncher/Services/SafeFolderName.cs, InstallFolderRules.cs, InstallFolderSuggestion.cs, LegacyShortcuts.cs, WarsOfLibertyLauncher/Localization/InstallFolderProblemText.cs; tests SafeFolderNameTests.cs, InstallFolderRulesTests.cs, InstallFolderSuggestionTests.cs, LegacyShortcutsTests.cs, InstallFolderTextTests.cs. Modified: InstallFolderDialog.xaml.cs, MainWindow.xaml.cs, NativeInstallService.cs, SetupPathPatcher.cs (CR CR LF line endings — pre-existing), UninstallService.cs, UpdateService.cs, ModRegistry.cs, Localization/Strings.cs, tests (DialogXamlTests, SetupPathPatcherTests, UninstallSafetyTests, UserDataPayloadTests), CLAUDE.md, AGENTS.md, docs/MODDING.md.
User decisions: (1) the colon (and any forbidden char) is DROPPED: a run of forbidden chars plus surrounding whitespace -> one space; valid names must come back byte-identical; idempotent. (2) narrow legacy cleanup: only launcher-RECORDED .lnk paths whose file name contains ':', base file deleted only if 0 bytes.
Status: dotnet build -c Release succeeds; dotnet test passes 4464/4464 (baseline 4248 + 216 new).

OUTPUT: a JSON-ish markdown list of findings. For each: id, severity (critical/high/medium/low), file:line, the claim, concrete evidence (quote code; for .NET behaviour say how you verified), the failure scenario (inputs -> wrong result), and a suggested fix. Report ONLY things you believe are real defects or real gaps vs the requirements/plan — no style nits, no praise. If you find nothing in your lens, say so explicitly. End with what you did not check.
`

const LENSES = [
  {
    key: 'pure-helpers',
    prompt: `LENS: correctness of the PURE helpers — SafeFolderName (From/Clean/IsValid/CheckSegment/IsReservedDeviceName/IsUsableDeclaredName/KnownLeaves), InstallFolderRules (Normalize/Validate/FindFileInTheWay/NearestExistingAncestor), InstallFolderSuggestion (Suggest/IsTrustedSavedPath/MakeUnique/UsableDefaultFolder/WithModFolder/TryFullPath), LegacyShortcuts (IsStreamPath/BaseFileOf/TryRemove/Extended), InstallFolderProblemText.KeyAndArgs.
Hunt for: inputs that break the identity property From(x)==x <=> IsValid(x) or idempotence (whitespace kinds, surrogates, NBSP, very long names, names that are only dots, names whose cleaned form becomes reserved, names with leading invalid chars); Validate accepting a path Windows cannot create or refusing one it can (UNC roots with/without trailing separator — probe [IO.Path]::GetPathRoot and TrimEndingDirectorySeparator on '\\\\server\\share', '\\\\server\\share\\', '\\\\server\\share\\x'; 'C:\\\\Games\\\\\\\\Mod'; segments '.'/'..'; '\\\\?\\UNC\\...'; a path like 'C:\\\\Games\\\\Mod\\\\' ; the drive check for lowercase letters and for UNC; the system-folder check); Normalize turning one valid path into a different one; IsDriveLetterPath edge cases; FindFileInTheWay/NearestExistingAncestor loops (termination on roots, UNC, relative input); Suggest returning a relative or invalid path in any branch; TryFullPath; WithModFolder with weird picks; LegacyShortcuts deleting anything that is not the recorded stream or its 0-byte base (e.g. a recorded path whose base is a DIRECTORY, a base that is a reparse point, a path with two colons like 'a:b:c.lnk', a stream path with '$DATA' type suffix), and whether File.Delete on '\\\\?\\C:\\\\...\\\\Base: Stream.lnk' really deletes just the stream.
Verify .NET behaviour claims with read-only PowerShell probes (pwsh uses .NET 8+; say which).`,
  },
  {
    key: 'ui-flow',
    prompt: `LENS: the install FLOW and the DIALOG — WarsOfLibertyLauncher/InstallFolderDialog.xaml.cs and MainWindow.xaml.cs (InstallAsync: the InstallFolderSuggestion.Suggest call, the dialog construction args, the new backstop right after the dialog, the addNewSlot PathEquals guard, probeFolder via NearestExistingAncestor, the privateSetupPath KeyNameFor check, the StatusInstallSuccessVerified args; ResolvePickedModInstall; BrowseAoE3Button_Click / BrowseButton_Click / AddExistingCopy trims; RepairInstallAsync's new HealLegacyShortcuts call).
Hunt for: ordering bugs (the dialog constructor sets FolderTextBox.Text which raises TextChanged -> ValidateInputs/UpdateDiskSpace — are all fields they read initialised by then? is _pathCheck null anywhere it is dereferenced?), any path where an invalid destination can still reach the first payload download (e.g. InPlaceOverlay branch, addNewSlot branch, a caller of InstallAsync other than the dialog flow, a drive that disappears between dialog and backstop), UI-thread blocking (FindFileInTheWay on a mapped network drive that is disconnected; Environment.GetLogicalDrives per keystroke), regressions for existing users (WoL copies '(2)…(5)', Knights and Barbarians whose saved path leaf is the defaultFolder leaf, in-place overlay into '...\\\\Age Of Empires 3\\\\bin' — does the new DriveRoot or SystemFolder rule refuse anything real?), the OK handler: does it still prompt the low-space confirm correctly, can DialogResult be set twice, does SelectedFolder ever become relative; the message shown via SetStatus+ShowProgressError before SetBusy (precedent?); localisation (any new user-visible English literal not in Strings; LocalizationGuard rule: XAML text needs x:Name); the HealLegacyShortcuts call placement in RepairInstallAsync (does it run on routes that returned early? on a cancelled repair? does Task.Run + COM WScript.Shell work off the UI thread? does it race with anything that writes the manifest?).`,
  },
  {
    key: 'pipeline-backcompat',
    prompt: `LENS: the install/uninstall/shortcut/registry PIPELINE and BACK-COMPAT with existing installs — NativeInstallService.cs (ShortcutStemFor; CreateShortcuts; the legacy RemoveRecorded calls before CreateShortcuts in InstallAsync and InstallModOnlyAsync; the delta path merge in ApplyGitHubDeltaAsync; HealLegacyShortcuts and MergeShortcutRecords; TryHealShortcutIcons skip; ApplyPrivateSetupPath KeyNameFor), SetupPathPatcher.cs (KeyNameFor, PrivateKeyFor now refusing ':' and '"'), UninstallService.cs (Plan refusing non-fully-qualified paths; IsDriveRoot treating 'C:'; DeleteShortcuts stream handling + FallbackShortcutNames), UpdateService.IsolatedCandidates pass 1, ModRegistry.ProjectToProfile userDataFolder guard.
Hunt for: anything that changes behaviour for an EXISTING install whose names are already valid (WoL and its copies, Hundred Days with key '...\\\\Hundred Days #2\\\\1.0', The Ottoman Empire #2, Age of Dynasties (Remake), Knights and Barbarians with AppName 'Knights and Barbarians', King's Return); whether PrivateKeyFor's new refusal can now throw somewhere it did not before for an existing install (PlanKey/ChooseKey/IsAllowedKey/PlanPrivateKeyWrite catch ArgumentException?); whether the three key-derivation sites really all use KeyNameFor (grep for every PlanKeyForInstall / PrivateKeyFor / ChooseKey caller); every OTHER caller of UninstallService.IsDriveRoot and Plan and whether the new behaviour breaks them (e.g. MultiInstallPhase1Tests, the uninstall dialog, copies); HealLegacyShortcuts: can it ever write a manifest for an install it does not own, rewrite a legacy wol-manifest.json into install-manifest.json, run concurrently with another manifest writer, lose fields on Save, or create shortcuts for a copy under the primary's name; the delta merge when CreateShortcuts returns nothing; whether a recorded non-stream shortcut under the old raw name could now be orphaned (e.g. a re-overlay of an install whose AppName was raw-valid but now cleaned differently — is that possible given identity rule?). Also check ModRegistry's guard does not drop a userDataFolder any shipped catalog mod uses (catalog cache at C:/Users/Jeison/AppData/Local/AoE3ModLauncher/catalog-cache.json).`,
  },
  {
    key: 'tests-docs',
    prompt: `LENS: TESTS and DOCS quality. Read every new/changed test (SafeFolderNameTests, InstallFolderRulesTests, InstallFolderSuggestionTests, LegacyShortcutsTests, InstallFolderTextTests, and the additions in DialogXamlTests, SetupPathPatcherTests, UninstallSafetyTests, UserDataPayloadTests).
Hunt for: a test that does not actually pin what its name/doc claims (would it still pass if the fix were reverted? — reason about it concretely against HEAD's code via 'git show HEAD:<path>'), tautologies, tests that depend on the machine (drive letters present, NTFS on %TEMP%, culture, the current Strings.Language, an AoE3 install on this PC — DialogXamlTests' documented trap: never pass an AoE3 source; destinations under a GUID; OK clicked only on a failing path), temp folders not cleaned up (especially a 'Foo ' file with a trailing space that only \\\\?\\ can delete), tests outside the [Collection("wpf-and-language")] that change Strings.Language, missing coverage of a requirement (the user asked for tests of: the helper incl. ':', reserved name, trailing dot, empty result; and recognition of an existing install with the RAW name).
DOCS: check the new CLAUDE.md bullet ("A mod's NAME is not a folder name", plus the "Five pin the folder a mod installs into" test paragraph and the King's Return correction), the AGENTS.md mirror, and docs/MODDING.md changes — every factual claim must match the code (file names, method names, behaviours, numbers like 255/33). Also check the new Spanish strings in Localization/Strings.cs (search WarnPathNotFullPath..StatusInstallFolderRefused and StatusInstallSuccessVerified) for es-419 tuteo (no voseo like 'Elegí', no peninsular like 'Pulsa'), correct accents, and that the English and Spanish say the same thing.`,
  },
]

const VERIFY_SCHEMA = {
  type: 'object',
  properties: {
    verdicts: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          id: { type: 'string' },
          verdict: { type: 'string', enum: ['confirmed', 'refuted', 'uncertain'] },
          severity: { type: 'string', enum: ['critical', 'high', 'medium', 'low', 'none'] },
          evidence: { type: 'string' },
          fix: { type: 'string' },
        },
        required: ['id', 'verdict', 'severity', 'evidence', 'fix'],
      },
    },
  },
  required: ['verdicts'],
}

const results = await pipeline(
  LENSES,
  l => agent(CONTEXT + '\n' + l.prompt, { label: `review:${l.key}`, phase: 'Review', agentType: 'Explore' }),
  (review, l) => agent(CONTEXT + `
YOUR ROLE: SKEPTICAL VERIFIER for the '${l.key}' review below. For EACH finding, try hard to REFUTE it by reading the actual code (and read-only .NET probes where behaviour is claimed). Default to 'refuted' when the evidence does not hold up; 'confirmed' only when you can point at the exact code path and a concrete failing input; 'uncertain' when it depends on something you cannot check. Re-rate the severity honestly (a cosmetic issue is 'low'). Give the minimal correct fix for confirmed ones.

===== REVIEW (${l.key}) =====
${review}`, { label: `verify:${l.key}`, phase: 'Verify', agentType: 'Explore', schema: VERIFY_SCHEMA })
    .then(v => ({ key: l.key, review, verdicts: v ? v.verdicts : [] }))
)

return { results: results.filter(Boolean) }
