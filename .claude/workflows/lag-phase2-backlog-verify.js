export const meta = {
  name: 'lag-phase2-backlog-verify',
  description: 'Read-only: verify every item of launcher-lag-phase2-backlog.md against HEAD, re-run the cut adversarial review of the phase-1 commit, verify findings adversarially, and synthesize an ordered Phase 2 plan',
  phases: [
    { title: 'Investigate', detail: '6 groups: phase-1 review + release tooling, UI-thread polling, room open/startup, lists & text measuring, room socket, other real bugs' },
    { title: 'Verify', detail: 'adversarial verifiers per group (two lenses for the four riskiest)' },
    { title: 'Synthesize', detail: 'one ordered plan covering every backlog item' },
    { title: 'Critic', detail: 'every backlog line accounted for; spot-check the top items' },
  ],
}

const REPO = 'C:\\Users\\Jeison\\Documents\\GitHub\\AoE3-Mod-Launcher'
const BACKEND = 'C:\\Users\\Jeison\\Documents\\GitHub\\wol-launcher-lobby-node'
const BACKLOG = REPO + '\\launcher-lag-phase2-backlog.md'

const RULES = `
HARD CONSTRAINTS (the session is in PLAN MODE):
- You are strictly READ-ONLY. Do NOT edit, create, move or delete any file. Do NOT run builds, tests, dotnet, msbuild, npm, PowerShell scripts of the repo, or anything that writes to disk or the network. Do NOT extract archives to disk.
- You MAY read files, grep/glob, and run read-only git commands (git log/show/blame/diff) in ${REPO} and in the lobby backend repo ${BACKEND} (Node/Fastify/TypeScript, src/**).
- You MAY read zip diagnostic bundles in C:\\Users\\Jeison\\Downloads\\WoL-diagnostico-2026100*.zip WITHOUT extracting (PowerShell: Add-Type -A System.IO.Compression.FileSystem; $z=[IO.Compression.ZipFile]::OpenRead('<path>'); (New-Object IO.StreamReader($z.GetEntry('launcher-debug.log').Open())).ReadToEnd(); $z.Dispose()).
- Windows WPF .NET 8 app. Main project: ${REPO}\\WarsOfLibertyLauncher (namespace WarsOfLibertyLauncher, assembly Aoe3ModLauncher). Tests: ${REPO}\\WarsOfLibertyLauncher.Tests (xUnit; STA helpers StaTestThread.Run, TestApplication.Ensure; never new Application()).
- Project docs: ${REPO}\\CLAUDE.md and ${REPO}\\.claude\\rules\\multiplayer.md are huge — grep them for your topic rather than reading them whole. They hold invariants a fix must not break.
- Cite code as path:line. Read the actual code before concluding. Mark anything you could not confirm.
`

const CONTEXT = `
CONTEXT. The maintainer left a backlog note for "Phase 2" of the launcher lag work: ${BACKLOG}. READ IT FIRST, whole (it is short, Spanish). It was written on ANOTHER PC (paths like C:\\Users\\jsalas\\... in it do not exist here) on 2026-10-06 after commit 9057192 ("phase 1": InlineFlagFit hides flags with Visibility.Hidden; FitStackPanel arranges non-fitting rows at the panel's width; LAYOUT STORM NON-CONVERGING; diagnostics export awaited; self-update ETag stored with its release tag). The note's own rule: "verify each point against the current code before proposing, because the lines move".
HEAD is now 5af13da: 9057192 + the Rooms-page ranking card shows Discord avatars again + releases/v1.0.15h.md. A release v1.0.15h has been built from 5af13da but NOT yet tagged/published.
The finding codes in the note (A1-A8, B1-B11, C1-C14, D1-D6, E1-E11) come from an earlier investigation whose detail JSON is NOT available on this PC; work from the note's one-line descriptions plus the code.
The maintainer prefers a CONSERVATIVE scope: minimal, behaviour-preserving changes; nothing invasive; every change pinned by a test where a pure seam exists; docs (CLAUDE.md / .claude/rules/multiplayer.md) updated in the same change.
Diagnostic evidence from the player's bundle (v1.0.15f, laptop 1366x768, compact layout): "SLOW MP RenderRoomsTab — 16109 ms" after creating a room (OnSessionStateChanged), "UI STALL 19735 ms", in the lobby "UI STALLs of 600-900 ms every few seconds" and "UI OP 797 ms — DispatcherTimer Restart (priority Background)", "Room WS disconnected: server_close:4006" ~40 s after creating the room, "Taunt 11: playback failed — Se requiere Windows Media Player versión 10 o posterior" (x4), startup "UI OP 4734 ms — SynchronizationContextAwaitTaskContinuation (priority Send)" (old v1.0.15b copy), "UI STALL 9375 ms"/"7360 ms" while exporting diagnostics (old copy; the export was fixed in 9057192).
`

const ITEM = {
  type: 'object',
  properties: {
    id: { type: 'string', description: 'The backlog code(s) this covers, e.g. "C5" or "R-build-ps1"; NEW-xx for things not in the note' },
    backlog_claim: { type: 'string' },
    status: { type: 'string', enum: ['open', 'already-fixed', 'partially-fixed', 'claim-wrong', 'needs-decision'] },
    current_evidence: { type: 'string', description: 'What the code does TODAY, with path:line' },
    root_cause: { type: 'string' },
    proposed_fix: { type: 'string', description: 'Concrete, minimal, behaviour-preserving change: methods, threading, caching' },
    files: { type: 'array', items: { type: 'string' } },
    tests: { type: 'string', description: 'Pure seam + test name/idea, or why none is possible' },
    docs_to_update: { type: 'string' },
    risk: { type: 'string' },
    effort: { type: 'string', enum: ['S', 'M', 'L'] },
  },
  required: ['id', 'backlog_claim', 'status', 'current_evidence', 'proposed_fix', 'files', 'tests', 'risk', 'effort'],
}

const GROUP_SCHEMA = {
  type: 'object',
  properties: {
    summary: { type: 'string' },
    items: { type: 'array', items: ITEM },
    new_findings: { type: 'array', items: ITEM },
    unverified: { type: 'array', items: { type: 'string' } },
  },
  required: ['summary', 'items', 'new_findings', 'unverified'],
}

const VERDICT_SCHEMA = {
  type: 'object',
  properties: {
    verdicts: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          id: { type: 'string' },
          refuted: { type: 'boolean', description: 'true only if the status/root cause or the fix is wrong' },
          reasoning: { type: 'string' },
          corrections: { type: 'string' },
          fix_concerns: { type: 'string' },
        },
        required: ['id', 'refuted', 'reasoning'],
      },
    },
    missed: { type: 'array', items: { type: 'string' } },
    overall: { type: 'string' },
  },
  required: ['verdicts', 'overall'],
}

const GROUPS = [
  {
    key: 'phase1-review-and-release',
    verifiers: 2,
    prompt: `GROUP R — "PARA PUBLICAR EL ARREGLO" (release blockers) — plus the adversarial review that was cut short.
1. ADVERSARIAL CODE REVIEW of commit 9057192 (git show 9057192) — the phase-1 lag fix — and briefly of 5af13da (git show 5af13da, ranking-card avatars). The note says this review "was cut and did not finish". Hunt for REAL defects: logic errors, regressions, WPF layout/visibility semantics (Hidden vs Collapsed side effects: hit-testing, RevealText cloning of hidden flags, tooltips), FitStackPanel arrange at panel width (any child now visible or hit-testable that should not be? NaturalHeight accounting?), the ETag pair and the legacy-key migration (could any path send a stale ETag, persist the wrong pair, loop updates, or lose a real update? old copies sharing the config), StartupUpdateGate/StartupUpdateState surgical JSON writes, ShareDiagnostics async (re-entrancy, exceptions, UI touched off-thread), LayoutStormDetector/DiagnosticLog changes (cost, thread safety), and whether the new tests actually pin the behaviour (would they fail on the old code?). Report each real defect as an item with id R-REVIEW-n, status open, with evidence. Do NOT report style nits.
2. build-release.ps1 does not parse in Windows PowerShell 5.1: the note says the file has no BOM and line ~324 has an en/em dash inside a string; read as cp1252 one of its bytes is U+201D which PowerShell takes as a quote. VERIFY read-only (e.g. [IO.File]::ReadAllBytes first 3 bytes for a BOM; find every non-ASCII char and its line with Select-String or a byte scan). Also check the root publish.ps1 and any other .ps1 in the repo for the same problem. Fix proposal: save with UTF-8 BOM or replace the non-ASCII chars — say which is safer and every file affected.
3. Cosmetic: the log line "Launcher self-update: nothing newer than '—'" prints the saved tag (NoUpdate(lastInstalledTag)) instead of the running binary's tag. Locate it (LauncherUpdateService / MainWindow) and propose the minimal fix.
4. The code-signing cert note (CN=Gorgorito not in Cert:\\CurrentUser\\My on the other PC) — just confirm it is an environment matter, not code (status needs-decision or claim-wrong as appropriate).
5. The release step itself (tag above v1.0.15g, releases/vX.Y.Z.md + announcements.json): check releases/v1.0.15h.md exists and whether announcements.json already has a v1015h entry (it should NOT yet — it goes in after the GitHub release).`,
  },
  {
    key: 'ui-thread-polling',
    verifiers: 2,
    prompt: `GROUP P — PERFORMANCE ITEM 1 OF THE NOTE: UI-THREAD POLLING WITH MULTIPLAYER OPEN, EVEN MINIMIZED (B4, C5, C6, C7, E9).
The note: Radmin probe (registry, processes, NICs) every 3 s; the lobby tick every 2.5 s (NICs and the AoE3 profile); the in-game tick every 1 s; RadminAssistantService.ProbeAsync parsing ALL service*.log every 3 s. Proposed: a background snapshot with an in-flight guard (the MaybeRefreshPowerState pattern in RadminVpnService), cache FindInstallation, resolve the in-game name once per room.
For EACH timer/probe: locate it (file:line), list exactly what runs on the UI thread per tick today (RadminVpnService.GetStatus / DetectServiceRunning / TryGetAdapterIp / ReadAdapterCandidates / QuickSignature / DescribeStateForLog / IsAppRunning (process enumeration) / registry reads / FindInstallation; MultiplayerTab RefreshRadminBanner, MaybeReportRadminIp, MaybeReportInGameName -> UserDataService.GetInGameName file reads, KickPeerPings, RefreshRosterLiveCells, RefreshLobbyOpenAge, UpdateLobbyPing; the in-game 1 s tick RefreshInGamePanel -> GetAdapterBytes; RadminAssistantService.ProbeAsync and who calls it how often — the assistant window? the banner?), whether it keeps running while the window is minimized / in the tray / another tab is shown, and its likely cost on a slow laptop (NetworkInterface.GetAllNetworkInterfaces is slow with many virtual adapters; Process.GetProcesses; reading rotated multi-MB service logs).
Then design ONE shared background snapshot (e.g. RadminSnapshot refreshed off-thread with an in-flight guard, consumers read the cached value, the UI never blocks), and the in-game-name resolution once per room (keep the documented resend-until-the-server-confirms behaviour working: InGamePublishState). Respect documented invariants: OverrideAddress must use the gate-free adapter IP; banner semantics (IsServiceRunning requires app + power + adapter); state change logging (_lastRadminLogSig); the in-match TRAFFIC meter. Say how to pin it with pure tests.`,
  },
  {
    key: 'room-open-and-startup',
    verifiers: 2,
    prompt: `GROUP O — PERFORMANCE ITEMS 2 AND 7 OF THE NOTE: OPENING/CREATING A ROOM, STARTUP, AND THE DOUBLE RefreshFromSession (C1, C3, C4, C11, C9).
The note: opening/creating a room took 16 s in the bundle, largely amplified by the (now fixed) loop. To do: measure every step of OpenLobbyWindow (DiagnosticLog.Time + PerfCounters); move MaybeReportRadminIp, MaybeReportInGameName, KickConnectionPing and UpdateLobbyPing to a Background post after Show; declare UseLayoutRounding, TextOptions and WindowChrome in LobbyWindow.xaml because today App applies them on Loaded and that forces another measure; startup has a 4.7 s operation — post the first Radmin probe and MaybeAutoOpenAssistant at ApplicationIdle. Item 7: coalesce RefreshFromSession, which runs twice in a row when a room is created, and do not rebuild the account chip's badge if it did not change.
For each: locate the code today (MultiplayerTab OnSessionStateChanged/RefreshFromSession/RenderRoomsTab/OpenLobbyWindow/RenderRoomPanel, LobbyWindow.xaml(.cs), App.OnAnyWindowLoaded/ApplyWindowChrome and the HiDPI/TextOptions class handler, MainWindow startup path and where the first Radmin probe and MaybeAutoOpenAssistant run, PushAccountChip/SetAccountChip). Verify each claim (does App really set UseLayoutRounding/TextOptions/WindowChrome at Loaded for LobbyWindow, and would declaring them in XAML avoid a measure — or would App's handler still run and re-apply? WindowChrome is applied centrally by design per CLAUDE.md "WindowChrome is applied CENTRALLY in App.OnAnyWindowLoaded"; propose a way that keeps that central rule, e.g. App skipping work when values already match, or LobbyWindow pre-applying the same values before Show). Why does RefreshFromSession run twice on create, and how to coalesce safely (Dispatcher post with a pending flag)? What exactly is the 4.7 s startup op in HEAD (map with DiagnosticLog TIMING lines / StartupWork tests)? Propose minimal changes with tests (StartupWorkTests, DialogXamlTests, AccountChipTests counts exactly 3 PushAccountChip call sites — do not break it).`,
  },
  {
    key: 'lists-and-text',
    verifiers: 1,
    prompt: `GROUP L — PERFORMANCE ITEMS 3, 4, 5, 6 AND 8 OF THE NOTE (C8, B5, B8, B3, A2, B6, B7, B10, B11).
3 (C8): with a room open, fold the community activity and stop refreshing the rooms list and the age cells behind the room window.
4 (B5, B8): rebuild lists only when what they show changed (the community block every 60 s; the players panel on every presence frame); build only the rows that fit, not 12; update the quiet page's ticks (ping, "updated X ago", ages) in place and less often.
5 (B3): RevealText clones every cut line on every resize and doubled the cost of laying out 12 rows in the regression test; early-out with a layout key and coalesce SizeChanged/Loaded.
6 (A2): InlineFlagFit/RevealText MeasureOne measures in TextFormattingMode Ideal while the text is drawn in Display, so flags and ellipsis are decided with a few px of error; pass the TextFormattingMode like MiddlePathText does. Affects all of RevealText.
8: ApplyActivityLayout measures elements already in the tree (B6); QueueActivityLayout has no dead band and no fuse (B7); the diagnostics' own cost (B10, B11: LayoutStormDetector / DiagnosticLog size-change class handler, PerfCounters, the per-second tick).
For each: locate today's code, verify the claim, estimate the cost, and propose the minimal fix that cannot reintroduce a layout feedback loop (CLAUDE.md rule: from an element's own SizeChanged write only what cannot reach that element's size; a panel hides a child with a slot as wide as the panel). Respect the documented layout rules of the Rooms page (RoomsActivityLayout.Plan, FitStackPanel whole-row rule, 'the cards end at the fifth player', ActivityMatchesBuilt caps) and the tests that pin them (CompactRoomsLayoutTests, RoomsActivityLayoutTests, InlineFlagFitTests, RevealTextTests, DialogXamlTests). For the "build only the rows that fit" idea, check how FitStackPanel decides what fits and whether building fewer rows changes what is shown at any width.`,
  },
  {
    key: 'room-socket',
    verifiers: 2,
    prompt: `GROUP S — "SOCKET DE SALA" REAL BUGS (E3-E8, E11) plus ConfigureAwait (C13, E1).
The note's claims, verify EACH against today's code (Services/Multiplayer/LobbyWebSocket.cs, MultiplayerSession.cs, MultiplayerTab OnRoomFrame/OnRoomDisconnected/SyncRoomSocketSubscription/HandleLobbyWindowClosed/LeaveRoom paths, ModHashService):
 a) the tab subscribes to the NEW socket after Start() and can miss the first room_state;
 b) a late 4006/4404 from the PREVIOUS room calls StopReconnect() (which aborts) on the NEW room's socket — reachable by creating a room from inside another;
 c) leaving, and which socket each close belongs to, leave no trace in the log;
 d) if the server closes the room during the Lobby phase, a "zombie" room stays on screen;
 e) set_radmin_ip is marked as sent before the socket is open and not re-sent after a reconnect;
 f) it keeps reconnecting after our own /leave;
 g) an exception inside a handler kills reconnection for good.
Base fix proposed: guard every handler with ReferenceEquals(sender, _session.RoomSocket).
Also: missing ConfigureAwait(false) in ModHashService and LobbyWebSocket.SendRawAsync (C13, E1) — confirm and assess impact (deadlock risk vs UI-thread hops).
Use the backend repo ${BACKEND} (src/lobbies/LobbyRoom.ts, rest.ts, orphanSweep.ts) to confirm server semantics of close codes 4006 (lobby_closed), 4404, 4007, 4010, the hello handling and the empty-room close — and explain the player's 4006 ~40 s after creating a room while the UI thread was frozen ~20 s.
Respect the long documented history in .claude/rules/multiplayer.md (grep "StopReconnect", "4006", "4404", "zombie", "StableConnectionMs", "InGameNamePublishState", "set_radmin_ip", "_roomMatchLive", "AwaitingResult", "SyncRoomSocketSubscription"): several of these were deliberately designed (e.g. StopReconnect keeps the object so the result card survives). Propose minimal fixes and pure tests.`,
  },
  {
    key: 'other-real-bugs',
    verifiers: 1,
    prompt: `GROUP X — OTHER REAL BUGS FROM THE NOTE (D3, offline buttons, E10/C14, D5).
1 (D3): the startup update check — if it fails or takes more than 6 s, it is handed over as "nothing new", and the whole session stays with no update pill and with multiplayer open (the LauncherUpdateGate never closes it). Proposed: reject failed handovers so MainWindow asks on its own, and log "could not check". Verify in StartupUpdateGate.cs / App.TakeStartupUpdateCheck / MainWindow.CheckForLauncherUpdateInnerAsync / LauncherUpdateService.CheckAsync result shape; propose the minimal fix + pure test (StartupUpdateStateTests / LauncherUpdateServiceTests style).
2: "+ Create room" stays disabled after a while offline: SetOfflineMode disables it and its online branch never re-enables it; same for SignInButton. Verify in MultiplayerTab.SetOfflineMode / ApplyOfflineDisable / RefreshFromSession (CLAUDE.md says RefreshFromSession re-applies offline mode at its end) and propose the fix + test.
3 (E10, C14): taunts on Windows N without Media Player — each taunt creates a MediaPlayer that fails. Proposed: a per-session latch with one log line, and fall back to the chat blip. Verify in Services/TauntService.cs (how failure surfaces: MediaFailed event? exception? which thread?), SoundService, MultiplayerTab.HandleChat; respect taunt rules (per-sender throttle, no taunts from history replay, SoundService.Enabled gate). Classifier must not depend on localized text.
4 (D5): several loose copies of the launcher share the config, the Run key, wol-launcher:// and the mutex; the old one starts with Windows and takes over. Mitigations are missing. Read SelfInstallService / StartupRegistrationService / DeepLinkService.EnsureRegistered / App single-instance mutex / LauncherUpdateService (CLAUDE.md documents the canonical install, ResolveAutoStartExe, and the ETag-pair fix). Propose the cheapest SAFE mitigations (e.g. never let an OLDER binary re-point the Run key / URL scheme at itself when a NEWER or canonical copy is registered; log which copy is running) — conservative, no silent installs, no deleting user files.`,
  },
]

const LENSES = [
  'CORRECTNESS lens: re-read the cited code yourself and check every claim, every status and every causal step; look for a status that is wrong (already fixed, or never true), a cost that is cached, or a fix that breaks a documented invariant (grep CLAUDE.md / .claude/rules/multiplayer.md for the code it touches).',
  'WPF-THREADING & SAFETY lens: check claims about dispatcher priorities, SynchronizationContext capture, await continuations, layout/visibility semantics, DispatcherTimer behaviour and WebSocket lifecycles against real .NET 8 WPF behaviour; check each fix cannot deadlock, touch UI objects off-thread, reorder something that must stay ordered, lose a frame/result, or reintroduce a layout feedback loop.',
]

function groupPrompt(g) {
  return `${RULES}\n${CONTEXT}\n\n${g.prompt}\n\nReturn one item per backlog code you own (status already-fixed / claim-wrong are valid and useful), plus new_findings for real problems you discover that the note missed. Output via the StructuredOutput tool.`
}

function verifierPrompt(g, inv, lens) {
  return `${RULES}\n${CONTEXT}\n\nYou are an ADVERSARIAL VERIFIER. Another agent handled this group:\n---\n${g.prompt}\n---\nIts result (JSON):\n${JSON.stringify(inv, null, 2)}\n\nTry hard to REFUTE each item (its status, root cause AND proposed fix). ${lens}\nrefuted=true only if you are confident it is wrong; partial errors go in corrections, fix risks in fix_concerns, and real problems it missed in missed. Output via the StructuredOutput tool.`
}

phase('Investigate')
const results = await pipeline(
  GROUPS,
  (g) => agent(groupPrompt(g), {
    label: `investigate:${g.key}`,
    phase: 'Investigate',
    schema: GROUP_SCHEMA,
    agentType: 'Plan',
  }),
  async (inv, g) => {
    if (!inv) return { group: g.key, investigation: null, verdicts: [] }
    const lenses = LENSES.slice(0, g.verifiers)
    const verdicts = await parallel(lenses.map((lens, i) => () => agent(verifierPrompt(g, inv, lens), {
      label: `verify:${g.key}:${i + 1}`,
      phase: 'Verify',
      schema: VERDICT_SCHEMA,
      agentType: 'Plan',
    })))
    return { group: g.key, investigation: inv, verdicts: verdicts.filter(Boolean) }
  },
)
const reviewed = results.filter(Boolean)
const empty = GROUPS.filter(g => !reviewed.some(r => r.group === g.key && r.investigation)).map(g => g.key)
if (empty.length) log(`No investigation came back for: ${empty.join(', ')}`)

phase('Synthesize')
const PLAN_SCHEMA = {
  type: 'object',
  properties: {
    summary: { type: 'string', description: 'Plain language for the maintainer: what Phase 2 does, in what order, why it is safe' },
    steps: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          order: { type: 'integer' },
          title: { type: 'string' },
          backlog_ids: { type: 'array', items: { type: 'string' } },
          why: { type: 'string', description: 'What the player feels / which evidence it explains' },
          change: { type: 'string' },
          files: { type: 'array', items: { type: 'string' } },
          reuse: { type: 'string' },
          guards: { type: 'string', description: 'Invariants to keep, merged from verifiers and docs' },
          tests: { type: 'string' },
          docs: { type: 'string' },
          effort: { type: 'string', enum: ['S', 'M', 'L'] },
        },
        required: ['order', 'title', 'backlog_ids', 'why', 'change', 'files', 'guards', 'tests', 'docs', 'effort'],
      },
    },
    release_checklist: { type: 'array', items: { type: 'string' } },
    already_fixed_or_wrong: { type: 'array', items: { type: 'object', properties: { id: { type: 'string' }, note: { type: 'string' } }, required: ['id', 'note'] } },
    deferred: { type: 'array', items: { type: 'object', properties: { id: { type: 'string' }, why: { type: 'string' } }, required: ['id', 'why'] } },
    decisions: { type: 'array', items: { type: 'object', properties: { question: { type: 'string' }, options: { type: 'string' }, recommendation: { type: 'string' } }, required: ['question', 'options', 'recommendation'] } },
    verification: { type: 'string' },
  },
  required: ['summary', 'steps', 'release_checklist', 'already_fixed_or_wrong', 'deferred', 'decisions', 'verification'],
}
const plan = await agent(`${RULES}\n${CONTEXT}\n\nYou are the SYNTHESIZER. Below are six verified group investigations of the Phase 2 backlog. Produce ONE ordered implementation plan:\n- EVERY backlog code in the note (A2, B3, B4, B5, B6, B7, B8, B10, B11, C1, C3, C4, C5, C6, C7, C8, C9, C11, C13, C14, D3, D5, E1, E3-E8, E9, E10, E11, plus the release items and the cosmetic log line) must appear exactly once: in a step, in already_fixed_or_wrong, or in deferred with a reason.\n- Keep only what survived verification; a refuted item is dropped or rewritten so the refutation no longer applies (say which). Fold every correction and fix concern into guards.\n- Phase-1 review defects (R-REVIEW-n) that survived come FIRST: they block the v1.0.15h release.\n- Then order by what players feel and by safety: real bugs that lose state (room socket, update check, offline buttons), then UI-thread polling, then room open/startup, then lists/text, then minor.\n- Each step must be independently shippable, minimal and behaviour-preserving, with a pure-seam test where possible, and must name the CLAUDE.md / .claude/rules/multiplayer.md bullets to update in the same change.\n- release_checklist: what must happen before tagging v1.0.15h (build-release.ps1 fix if verified, re-review, notes, announcement after the release, etc.).\n\nVERIFIED GROUPS:\n${JSON.stringify(reviewed, null, 1)}`,
  { label: 'synthesize', phase: 'Synthesize', schema: PLAN_SCHEMA, agentType: 'Plan' })

phase('Critic')
const critique = plan ? await agent(`${RULES}\n${CONTEXT}\n\nYou are the COMPLETENESS CRITIC. Read the backlog note ${BACKLOG} line by line and map EVERY bullet to a plan step, an already-fixed/wrong entry, or a deferred entry — list any bullet that is missing or mis-mapped. Then spot-check the 4 highest-priority steps against the code: feasible as written? breaks a documented invariant? is the test meaningful (would it fail on today's code)? Return plain text: the mapping table, then the spot-check notes, then any step you would add or change, with the same level of detail.\n\nPLAN:\n${JSON.stringify(plan, null, 1)}`,
  { label: 'critic', phase: 'Critic', agentType: 'Plan' }) : null

return { plan, critique, reviewed }
