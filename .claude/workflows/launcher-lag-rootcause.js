export const meta = {
  name: 'launcher-lag-rootcause',
  description: 'Read-only investigation + adversarial verification of the launcher lag reported in a player diagnostic bundle',
  phases: [
    { title: 'Investigate', detail: '5 read-only investigators: layout loop, other feedback loops, room-open freeze + UI-thread I/O, update-gate ETag, room WS during freezes' },
    { title: 'Verify', detail: 'skeptics try to refute each investigator finding' },
  ],
}

const REPO = 'C:\\Users\\jsalas\\Downloads\\github\\AoE3-Mod-Launcher'

const RULES = `
HARD CONSTRAINTS (the session is in PLAN MODE):
- You are strictly READ-ONLY. Do NOT edit, create, move or delete any file. Do NOT run builds, tests, dotnet, msbuild, or anything that writes to disk or the network.
- You MAY read files, grep/glob, and run read-only git commands (git log/show/blame/diff) against the repo at ${REPO}.
- Windows WPF .NET 8 app. Main project: ${REPO}\\WarsOfLibertyLauncher (namespace WarsOfLibertyLauncher, assembly Aoe3ModLauncher). Tests: ${REPO}\\WarsOfLibertyLauncher.Tests (xUnit; STA helpers StaTestThread.Run, TestApplication.Ensure; never new Application()).
- Project docs worth reading for context: ${REPO}\\CLAUDE.md and ${REPO}\\.claude\\rules\\multiplayer.md (huge; grep them for the topic you need rather than reading whole).
- Cite code as path:line. Be concrete. Prefer evidence over speculation; mark anything you could not confirm.
`

const EVIDENCE = `
PLAYER DIAGNOSTIC BUNDLE (WoL-diagnostico-20261006-074213.zip), already analysed by the lead:
Machine: laptop 1366x768 desktop, 13.9in, DPI 1.00, WPF render tier 2 (hardware), window 1100x700 DIP, MultiplayerTab COMPACT layout, Windows es-ES, Radmin installed, WoL 1.2.0e installed. Players report "the launcher lags / is slow" repeatedly.

Sessions in the bundle:
- v1.0.15b (Oct 5 18:28, from Downloads\\Aoe3ModLauncher.exe): 586 UI stalls totalling 204 s; 567 UI OPs of System.Windows.Media.MediaContext.RenderMessageHandler (each 250-1200 ms) totalling 174 s.
- v1.0.15c (Oct 5 19:15, ~1 h session): 1123 UI stalls totalling 381 s; 1091 RenderMessageHandler UI OPs totalling 330 s (max 1297 ms). The pattern is a continuous stream of ~280-400 ms render frames, several per second, for minutes, while idle on the Multiplayer tab.
- v1.0.15f (Oct 6 07:36, self-updated, the LATEST release; it has the LayoutStormDetector/EffectsGovernor diagnostics). Lines verbatim:
  [07:36:46] EFFECTS REDUCED: drawing took 874 ms of every second (138 ms a frame) for 3 s with 5 badges animating — lights off for this session
  [07:36:53] LAYOUT STORM  0/s layout passes, 5/s redraws (2/s driven by animations), 1093 ms/s drawing (204 ms a frame) — slow frames, for 3 s, on screen — size changes: TextBlock@ActivityRecentList×3366, Border@ActivityRecentList×1683, Grid@ActivityRecentList×1683 — counters: RevealText.Evaluate +2142, InlineFlagFit.Apply +1071 — window shown (Normal), tab Multiplayer, mp Rooms, compact, rooms 0, activity Fixed, chat rows 10, players rows 6, room window none, focus CheckBox#CompetitiveCheck@CompetitiveBox — no active animation clocks
  [07:36:55] CreateRoom: dialog returned lobby id JP4X418Q (mod=wol), entering room ... EnterHostedLobbyAsync: InLobby state ... (competitive=True)
  [07:37:11] SLOW  MP RenderRoomsTab — 16109 ms on the UI thread
  [07:37:11] UI OP  16156 ms — WarsOfLibertyLauncher.Controls.MultiplayerTab.<OnSessionStateChanged>b__379_0  (priority Normal)
  [07:37:15] LAYOUT STORM over after 4 s
  [07:37:15] UI STALL  19735 ms — the dispatcher could not run for that long
  [07:37:19] UI OP  4297 ms — MediaContext.RenderMessageHandler (priority Render)
  [07:37:36] Room WS disconnected: server_close:4006
  [07:37:53] LAYOUT STORM  0/s layout passes, 5/s redraws (2/s driven by animations), 1270 ms/s drawing (246 ms a frame) — slow frames, for 26 s, nobody watching for 2 s of it — size changes: TextBlock@ActivityRecentList×36108, Border@ActivityRecentList×18054, Grid@ActivityRecentList×18054 — counters: RevealText.Evaluate +33980, InlineFlagFit.Apply +16983 — window shown (Normal), tab Multiplayer, mp Rooms, compact, rooms 0, activity Fixed, chat rows 11, players rows 6, room window none, focus TextBox#RoomTitleBox — active clocks: DoubleAnimationUsingKeyFrames 1.06s Forever ×1
- v1.0.15b again (Oct 6 07:38:40, from Desktop\\Aoe3ModLauncher.exe — a second, older copy the player also runs):
  startup: "UI OP 4734 ms — SynchronizationContextAwaitTaskContinuation (priority Send)"; "SLOW cards strip 234 ms", "SLOW workshop browser 734 ms" (both twice), "SLOW RefreshModCards (coalesced) 1000 ms".
  then a continuous stream of 250-500 ms RenderMessageHandler / AnimatedRenderMessageHandler UI OPs on the Multiplayer tab for minutes.
  [07:39:22] CreateRoom K33DM9JY ... [07:39:37] SLOW MP RenderRoomsTab — 15297 ms; UI OP 15359 ms OnSessionStateChanged; UI STALL 18656 ms.
  lobby open afterwards: UI STALLs of 600-900 ms every few seconds; "UI OP 797 ms — System.Windows.Threading.DispatcherTimer+<>c.<Restart>b__21_0 (priority Background)".
  "Taunt 11: playback failed — Se requiere Windows Media Player versión 10 o posterior." (x4)
  while exporting the diagnostics bundle: "UI STALL 9375 ms", "UI STALL 7360 ms".
  Self-update in this old copy: "Startup auto-update: saved tag 'v1.0.15f' contradicts the binary 'v1.0.15b'; using the binary." -> "GitHub returned 304 Not Modified; release unchanged." -> "Startup auto-update: not applying v1.0.15b -> v1.0.15b - NotAvailable." -> MainWindow: "saved tag 'v1.0.15f' does not match the running binary 'v1.0.15b' — trusting the binary, re-stamping the tag and dropping the cached ETag." -> "using the check the startup gate made." -> "nothing newer than 'v1.0.15b'". The redacted config AFTER this session still has launcherUpdateETag = W/"4f2b..." and lastInstalledLauncherTag = v1.0.15b. "StartupRegistration: registered '\\"C:\\Users\\Usuario\\Desktop\\Aoe3ModLauncher.exe\\" --minimized'".

How the diagnostics are produced (read Services/DiagnosticLog.cs ~330-560, Services/LayoutStormDetector.cs, Services/PerfCounters.cs):
- "layout passes" = count of LayoutUpdated events on the main window (DiagnosticLog.WatchLayoutOf). "size changes" = a class handler on FrameworkElement.SizeChangedEvent counting PER ELEMENT INSTANCE; the line shows the top 24 instances by count grouped by "Type@NearestNamedAncestor". The "counters:" list only names counters that MOVED that second (QueueActivityLayout and ApplyActivityLayout also have PerfCounters and did NOT appear).
- "UI OP" = one dispatcher operation that ran >= 250 ms; "UI STALL" = the dispatcher could not run a probe for that long.
`

const INVESTIGATION_SCHEMA = {
  type: 'object',
  properties: {
    summary: { type: 'string' },
    findings: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          id: { type: 'string' },
          title: { type: 'string' },
          severity: { type: 'string', enum: ['critical', 'high', 'medium', 'low'] },
          confidence: { type: 'string', enum: ['high', 'medium', 'low'] },
          evidence: { type: 'string' },
          root_cause: { type: 'string' },
          code_locations: { type: 'array', items: { type: 'string' } },
          proposed_fix: { type: 'string' },
          regression_test: { type: 'string' },
          risks: { type: 'string' },
        },
        required: ['id', 'title', 'severity', 'confidence', 'evidence', 'root_cause', 'code_locations', 'proposed_fix'],
      },
    },
    open_questions: { type: 'array', items: { type: 'string' } },
  },
  required: ['summary', 'findings'],
}

const VERDICT_SCHEMA = {
  type: 'object',
  properties: {
    verdicts: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          finding_id: { type: 'string' },
          refuted: { type: 'boolean' },
          reasoning: { type: 'string' },
          corrections: { type: 'string' },
          fix_concerns: { type: 'string' },
        },
        required: ['finding_id', 'refuted', 'reasoning'],
      },
    },
    overall: { type: 'string' },
  },
  required: ['verdicts', 'overall'],
}

const AREAS = [
  {
    key: 'layout-loop',
    verifiers: 2,
    prompt: `AREA A — THE NON-CONVERGING LAYOUT LOOP IN THE ROOMS PAGE'S COMMUNITY "RECENT MATCHES" CARD.
The v1.0.15f storm lines show ~700 size changes/second on the SAME ~24 element instances under ActivityRecentList (ratio TextBlock:Grid:Border = 2:1:1, InlineFlagFit.Apply ~= Grid count, RevealText.Evaluate ~= 2x Apply) with ZERO LayoutUpdated events and ~250 ms frames, while idle with no rooms. This persisted after EffectsGovernor turned all rank-badge lights off. Lead's hypothesis (VALIDATE OR REPLACE IT):
- Controls/InlineFlagFit.cs Apply(), wired in Controls/MultiplayerTab.xaml.cs BuildRankingMatchRow (~11081-11227: who.SizeChanged += Apply; who.Loaded += Apply), toggles flag pictures (Border 18x12 + margin 6, inside InlineUIContainer in the "who" TextBlock) between Visibility.Visible and Visibility.Collapsed. Visible<->Collapsed calls signalDesiredSizeChange (dirties layout) whereas Visible<->Hidden only sets VisualOpacity (layout-neutral). A SizeChanged handler that dirties layout makes WPF's LayoutManager.UpdateLayout loop again; if the decision flips between iterations the loop never converges, hits its ~153-iteration guard (bails WITHOUT raising LayoutUpdated and reschedules at Background), every frame -> exactly "0/s layout passes" + slow frames.
- Possible feedback path: collapsing a flag shrinks the row's desired width -> Grid star-column MinSize / UseLayoutRounding (set app-wide) in the ActivityStrip grid (columns *, gap, 2*, gap, 1.3*, see MultiplayerTab.xaml ~1117-1300 and LayOutActivityColumns ~14574) or inside the row Grid (Auto | * | Auto, the line-2 TextBlock spans columns 1-2) -> who.ActualWidth changes by ~1 px -> a borderline flag flips -> repeat. Note FitStackPanel (Controls/FitStackPanel.cs) arranges non-fitting children into Rect(0,0,0,0) and raises NaturalHeightChanged from MeasureOverride; RevealText (Controls/RevealText.cs) Evaluate runs on SizeChanged/Loaded and sets ToolTip.
Tasks:
1. Read InlineFlagFit.cs, RevealText.cs, FitStackPanel.cs, FitRowPanel.cs, MultiplayerTab.Compact.cs, BuildRankingMatchRow/AppendNameWithFlag (MultiplayerTab.xaml.cs ~11081-11380), the Rooms page XAML (MultiplayerTab.xaml ~1000-1300), RoomsActivityLayout (Services/Multiplayer). Determine the most likely exact feedback mechanism that makes the decision oscillate; identify whether anything OTHER than InlineFlagFit dirties layout from inside a size/layout callback in that subtree.
2. Evaluate the candidate fix: switch InlineFlagFit to Visibility.Hidden (layout-neutral; matches VisibleObjects' nominal-width model where hidden pictures keep their width), keeping write-only-on-change. Would it fully break the loop? Visual consequences vs Collapsed (text after a hidden flag no longer shifts left)? Hit-testing/tooltips of hidden flags? Interaction with RevealText.NominalWidth / CloneText. Also consider additional hardening (e.g., do not re-decide unless available width changed by > 1 px; never call it re-entrantly).
3. List every test and doc that would need updating (grep the Tests project for InlineFlagFit, Visibility.Collapsed assertions on flags, ACutLineWithFlagsRevealsWithItsFlags, ANarrowFourPlayerRowHidesTheFlagsPastTheCutAndStillReveals, InlineFlagFitTests; grep CLAUDE.md and .claude/rules/multiplayer.md for "collapses").
4. Design a REGRESSION TEST that would have caught this: e.g. build MultiplayerTab / the Rooms page with community recent matches including 2v2 rows with flags at the player's geometry (compact, ~1100x700 window) and sweep widths in 1-px steps, pump the dispatcher, assert layout converges (LayoutUpdated fires; InlineFlagFit.Apply count bounded). Look at existing tests (CompactRoomsLayoutTests, DialogXamlTests, RankingCivsAndHistoryTests, InlineFlagFitTests) for reusable fixtures and say exactly which to extend.
Return findings with ids like A1, A2...`,
  },
  {
    key: 'feedback-audit',
    verifiers: 0,
    prompt: `AREA B — AUDIT FOR OTHER SELF-SUSTAINING LAYOUT/RENDER FEEDBACK LOOPS AND CONTINUOUS UI-THREAD WORK.
The same class of defect (an event raised by layout — SizeChanged, LayoutUpdated, Loaded, a panel event fired from MeasureOverride/ArrangeOverride — whose handler changes a layout-affecting property, rebuilds children, calls Measure()/UpdateLayout() on in-tree elements, or queues work that changes sizes again) can exist elsewhere. On a slow laptop any loop like this pegs the UI thread.
Tasks:
1. Grep WarsOfLibertyLauncher for SizeChanged +=, LayoutUpdated +=, Loaded += in code-built UI, NaturalHeightChanged, .Measure(new Size, UpdateLayout(, CompositionTarget.Rendering, DispatcherTimer creation (and their Interval/priority), BeginAnimation/Storyboard forever loops, and panels that raise events from MeasureOverride/ArrangeOverride. For each candidate, judge whether it can feed back (changes something that changes its own trigger) or does continuous work while idle, and how expensive.
2. Pay special attention to: MultiplayerTab.Compact.cs (QueueActivityLayout/ApplyActivityLayout/RememberActivityHeights/MeasureMinCards/ApplyActivityFluid — note ApplyActivityLayout calls RoomsSectionHeader.Measure / RoomsEmptyState.Measure with infinite height on in-tree elements), FitStackPanel/FitRowPanel, RoomsTableLayout/ApplyRoomColumns, ApplyRankingSplit/ReflowRankingIfShapeChanged, ApplyPageSpacing/ApplyChatWidth, UiScale.Attach (Controls/UiScale.cs), RevealText, DeckTiles, rank badge, the timers on the multiplayer tab (rooms list 5 s, rooms ping 3 s, Radmin banner 3 s, lobby ping 2.5 s, activity age ticks) and what each tick does on the UI thread.
3. Rank the top risks with evidence. For anything with a concrete loop, propose a fix pattern.
Return findings with ids like B1, B2...`,
  },
  {
    key: 'room-open-freeze',
    verifiers: 1,
    prompt: `AREA C — WHY OPENING A ROOM FREEZES THE UI THREAD FOR 15-16 s (and the 600-900 ms stalls while the lobby is open, and the 7-9 s stalls while exporting the diagnostics bundle).
Evidence: after "CreateRoom ... EnterHostedLobbyAsync completed", "SLOW MP RenderRoomsTab — 16109 ms" (v1.0.15f) and "15297 ms" (v1.0.15b), as one dispatcher op from MultiplayerTab.OnSessionStateChanged (Controls/MultiplayerTab.xaml.cs ~1630 -> RefreshFromSession -> case Subtab.Rooms ~3907 -> RenderRoomsTab ~4013 -> OpenLobbyWindow ~20495 -> RenderRoomPanel ~4125 -> new LobbyWindow(...) + w.Show()). In the lobby: UI STALL 600-900 ms every few seconds and "UI OP 797 ms — DispatcherTimer+<>c.<Restart>b__21_0 (priority Background)" (new DispatcherTimer() defaults to Background priority).
Tasks:
1. Trace everything that runs synchronously on the UI thread in that op: LobbyWindow constructor/XAML, ApplyLobbyStaticLabels, RenderRoomPanel and everything it calls (RenderRoomMembers, rank badges, RefreshPreflightChecklist, RadminVpnService.GetStatus/TryGetAdapterIp/DescribeStateForLog, UserDataService.GetInGameName, KickConnectionPing, UpdateLobbyPing, MaybeReportRadminIp, MaybeReportInGameName, ModHashService/fingerprint, file reads, NetworkInterface enumeration, registry, Process enumeration, Ping), w.Show() and App.OnAnyWindowLoaded (App.xaml.cs — WindowChrome, DWM, HiDPI, UiScale), UiScale.Attach for LobbyWindow. Identify blocking I/O and expensive work.
2. Assess the hypothesis that most of the 16 s is the Rooms-page layout loop (Area A) being AMPLIFIED: WPF's LayoutManager is per dispatcher, so any synchronous layout (Window.Show, explicit Measure/UpdateLayout, ActualWidth after UpdateLayout, etc.) has to iterate the non-converging MainWindow subtree up to the loop guard each time. Estimate how many synchronous layout passes the open path triggers.
3. Inspect each lobby-time DispatcherTimer tick body (lobby ping 2.5 s: KickConnectionPing, UpdateLobbyPing, RefreshLobbyOpenAge, MaybeReportRadminIp, MaybeReportInGameName, KickPeerPings, RefreshRosterLiveCells; Radmin banner poll; rooms ping/list timers) for UI-thread I/O; identify which could take ~800 ms on a slow laptop; propose moving to background with cached results.
4. Look at MainWindow.ShareDiagnostics / TryWriteInstallSnapshot / DiagnosticLog.ExportBundle for UI-thread blocking (Task.Run(...).Wait/GetResult etc.).
5. Also: the startup "UI OP 4734 ms (priority Send)" in v1.0.15b — is it the remainder of App.OnStartup after the startup gate's await (MainWindow construction + show) — and is it already fixed in HEAD (v1.0.15f showed constructor finished +2106 ms)? Note anything still heavy at startup in HEAD.
Return findings with ids like C1, C2...`,
  },
  {
    key: 'update-gate',
    verifiers: 1,
    prompt: `AREA D — THE PLAYER IS STUCK ON AN OLD RELEASE: THE STARTUP AUTO-UPDATE ANSWERS A 304 WITH "NOTHING NEWER" WHEN THE SAVED TAG CONTRADICTS THE BINARY.
Evidence in the bundle (see above): the Desktop copy is v1.0.15b, the saved tag was v1.0.15f (written by the other copy's self-update), the gate detected the contradiction, substituted the binary's tag in memory, but still sent If-None-Match with the cached ETag (fingerprint of the v1.0.15f release response) -> 304 -> NoUpdate. MainWindow then dropped the ETag but CONSUMED the gate's check (handed over) and, because UpdateAvailable=false, re-saved the check's ResponseETag — undoing its own drop. Result: every later launch of that copy (saved tag now equals the binary) sends the same ETag, gets 304, concludes nothing is newer — stuck on v1.0.15b until a NEW release changes GitHub's ETag. That copy lacks the v1.0.15f performance fixes (EffectsGovernor, still list badges, clock fixes).
Tasks:
1. Read Services/StartupUpdateGate.cs (RunAsync ~72-148), Services/StartupUpdateState.cs, Services/LauncherUpdateService.cs (CheckAsync and its 304 path ~139-220, EvaluateUpdate, SavedTagContradictsBinary ~850-870, CurrentInformationalTag), MainWindow.xaml.cs CheckForLauncherUpdateInnerAsync (~9525-9640) and App.TakeStartupUpdateCheck. Confirm or refute each step of the chain above in HEAD code.
2. Design the minimal correct fix (e.g. the gate sends no ETag when it had to correct the saved tag; MainWindow does not persist a handed-over ResponseETag that came from a 304 after it decided to drop the ETag / or ignores the handed-over check when it detected a contradiction). Consider the more robust alternative of persisting the remote tag alongside the ETag so a 304 can still be compared with the running binary — weigh config-schema cost (StartupUpdateState writes surgical JSON; LauncherConfig must declare any new property or Save() drops it).
3. Specify unit tests in the style of the existing LauncherUpdateServiceTests / StartupUpdateStateTests / AutoUpdatePolicy tests that pin the regression (pure functions preferred; say which seam to extract if needed).
4. Also assess the two-portable-copies situation (Downloads\\Aoe3ModLauncher (1).exe = v1.0.15f, Desktop\\Aoe3ModLauncher.exe = v1.0.15b): StartupRegistration re-points the Run key to whichever copy ran last (SelfInstallService.ResolveAutoStartExe prefers a canonical install only if one exists). What practical advice should the maintainer give the player, and is there a cheap launcher-side mitigation?
Return findings with ids like D1, D2...`,
  },
  {
    key: 'room-socket',
    verifiers: 1,
    prompt: `AREA E — DID THE UI FREEZE KILL THE PLAYER'S ROOM? (WebSocket lifecycle vs the UI thread)
Timeline (v1.0.15f): room JP4X418Q created 07:36:55 (OpenRoomSocketAsync: WS started; EnterHostedLobbyAsync: InLobby state), then a 16 s UI op (OnSessionStateChanged -> RenderRoomsTab) and a 19.7 s UI stall ending ~07:37:15, then at 07:37:36 "Room WS disconnected: server_close:4006". The lobby backend is a separate repo (not available locally); per .claude/rules/multiplayer.md 4006 = lobby_closed and 4404 = lobby_not_found; the server deletes a member on socket close, has an empty-room close and a startup orphan sweep (grep multiplayer.md for "4006", "orphan", "empty-room", "hello", "idle-kick", "90 s").
Tasks:
1. Read Services/Multiplayer/LobbyWebSocket.cs, Services/Multiplayer/MultiplayerSession.cs (EnterHostedLobbyAsync, OpenRoomSocketAsync, hello handling, OnFrame), and MultiplayerTab's OnRoomFrame / OnRoomDisconnected / SyncRoomSocketSubscription. Determine whether connect, the hello send, the receive loop, the ping keep-alive, or reconnect logic depend on the UI thread (await continuations capturing the WPF SynchronizationContext without ConfigureAwait(false), Dispatcher.Invoke, timers on the dispatcher). Would a 16-20 s UI-thread block delay the hello / pong / receive processing?
2. From the client code and the docs, infer the plausible server-side reasons for a 4006 ~40 s after creation, and whether a delayed hello or missed ping during the freeze could cause it. Say clearly what can and cannot be concluded without the backend.
3. Propose concrete hardening (ConfigureAwait(false) on the socket pump/hello path, keeping network I/O off the UI thread, etc.) with code locations, and a test idea if feasible.
4. Secondary: TauntService playback fails with "Se requiere Windows Media Player versión 10 o posterior" (Windows N edition without the Media Feature Pack, MediaPlayer requires WMP). Read Services/TauntService.cs and SoundService.cs; propose a graceful fallback or a one-time log/notice, and whether MediaPlayer failing could itself cost UI time.
Return findings with ids like E1, E2...`,
  },
]

const VERIFY_LENSES = [
  'CORRECTNESS lens: re-read the cited code yourself and check every causal step; look for a step that does not happen as claimed.',
  'WPF-SEMANTICS lens: check the claims about WPF layout/visibility/dispatcher/threading behaviour against your knowledge of the WPF (.NET 8 / PresentationFramework) source; challenge any that are wrong, and check the proposed fix cannot reintroduce the problem or break the feature it serves.',
]

function investigatorPrompt(area) {
  return `${RULES}\n${EVIDENCE}\n\n${area.prompt}\n\nBe thorough: read the actual code before concluding. Output via the StructuredOutput tool.`
}

function verifierPrompt(area, inv, lens) {
  return `${RULES}\n${EVIDENCE}\n\nYou are an ADVERSARIAL VERIFIER. Another agent investigated this area:\n---\n${area.prompt}\n---\nIts result (JSON):\n${JSON.stringify(inv, null, 2)}\n\nYour job: try hard to REFUTE each finding (root cause AND proposed fix). ${lens}\nFor each finding id return refuted=true only if you are confident the claim or its fix is wrong; put partial errors in 'corrections' and risks of the fix in 'fix_concerns'. Default to refuted=false only when you checked the code and it holds. Output via the StructuredOutput tool.`
}

const results = await pipeline(
  AREAS,
  (area) => agent(investigatorPrompt(area), {
    label: `investigate:${area.key}`,
    phase: 'Investigate',
    schema: INVESTIGATION_SCHEMA,
    agentType: 'Plan',
  }),
  async (inv, area) => {
    if (!inv) return { area: area.key, investigation: null, verdicts: [] }
    if (!area.verifiers) return { area: area.key, investigation: inv, verdicts: [] }
    const lenses = VERIFY_LENSES.slice(0, area.verifiers)
    const verdicts = await parallel(lenses.map((lens, i) => () => agent(verifierPrompt(area, inv, lens), {
      label: `verify:${area.key}:${i + 1}`,
      phase: 'Verify',
      schema: VERDICT_SCHEMA,
      agentType: 'Plan',
    })))
    return { area: area.key, investigation: inv, verdicts: verdicts.filter(Boolean) }
  },
)

log('All areas investigated and verified.')
return results.filter(Boolean)
