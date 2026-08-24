---
description: Multiplayer, lobby, Radmin VPN, global chat and Discord-announcement rules for the AoE3 Mod Launcher. Split out of CLAUDE.md so it only loads when working on the multiplayer surface.
paths:
  - WarsOfLibertyLauncher/Controls/MultiplayerTab.*
  - WarsOfLibertyLauncher/Controls/AppToast.cs
  - WarsOfLibertyLauncher/Controls/MpAlertOverlay.cs
  - WarsOfLibertyLauncher/LobbyWindow.*
  - WarsOfLibertyLauncher/CreateLobbyDialog.*
  - WarsOfLibertyLauncher/RenameRoomDialog.*
  - WarsOfLibertyLauncher/RadminAssistantWindow.*
  - WarsOfLibertyLauncher/Services/Multiplayer/**
  - WarsOfLibertyLauncher/Services/Radmin*.cs
  - WarsOfLibertyLauncher/Services/TauntService.cs
---

# Multiplayer gotchas

These moved out of `CLAUDE.md` verbatim (nothing was reworded). **Update them HERE**
when a multiplayer invariant changes — same rule as before, different file.

Cross-cutting rules that merely *touch* multiplayer deliberately stayed in `CLAUDE.md`:
the `config.GameExecutable` shared-exe trap, the notification bell + new-room poll, the
`wol-launcher://` deep link / single-instance mutex, feedback sounds, the localization
-invariant MP fingerprint, and the shared localization + dialog-chrome conventions.

- **The README's multiplayer story is aspirational, and the original CLAUDE.md
  wording was itself stale — here is the verified reality.** The README describes
  P2P UDP hole-punching, STUN, and a WinDivert virtual LAN; **none of that code
  exists** (no `PeerMesh`/`VirtualLanService`/`WinDivertNative`). Game traffic
  rides **user-managed Radmin VPN** (its 26.0.0.0/8 LAN; AoE3's stock LAN
  discovery finds peers). The launcher only *assists* with Radmin — detect /
  install / launch its GUI and copy the network name to the clipboard for manual
  paste; it **cannot join a network programmatically**. It DOES detect current
  network membership by parsing Radmin's own
  `%PROGRAMDATA%\Famatech\Radmin VPN\service.log` **plus every rotated
  backup** `service (N).log` in that directory (English, tab-delimited,
  stable across Radmin VPN 2.x) for `UPDATE\tYou joined/left network 'X'`
  events — that's how `RadminAssistantService.ProbeAsync` promotes its overlay
  checklist from `LoggedIn` → `InAoE3Network`. Reading only `service.log`
  silently fails the morning Radmin rotates the file at ~1 MB (the live log
  starts empty even though the user is still session-tracked in a network);
  `RadminLogService` enumerates `service*.log` in the directory, sorts by
  `LastWriteTimeUtc` ascending so newer events overwrite older ones in the
  same dict, and combines the result. An ICMP ping to a known seed peer is
  the fallback signal when no log file is readable (deleted, ACL'd, sandboxed
  account) (`Services/RadminVpnService.cs`, `RadminAssistantService.cs`,
  `RadminLogService.cs`). **"Radmin is running" (`RadminStatus.IsServiceRunning`,
  the green banner + the `LoggedIn` stage) requires THREE things, because the 26.x
  adapter alone is a false positive: (1) the GUI process `RvRvpnGui.exe` ALIVE
  (`IsAppRunning`), (2) the VPN NOT powered off per the log
  (`RadminLogService.GetPowerState() != Off`), and (3) an Up 26.x adapter.**
  `DetectServiceRunning` early-returns `(false,null)` when either (1) or (2) fails.
  The background service `RvControlSvc` auto-starts at boot and keeps the adapter Up
  with its STATIC 26.x identity IP regardless of the app OR the power toggle — so an
  adapter-only check read "running · IP" both for a CLOSED Radmin and for an OPEN but
  **"Desconectado"** (powered-off) Radmin (two reported false positives). Neither a
  service-state check (the service is always up) nor the network-membership parser
  (`GetActiveNetworkMemberships` — powering off logs `Switched Off` but NOT `You left
  network`, so membership goes STALE) can tell. The reliable live signal is the log's
  own power toggle: `RadminLogService.GetPowerState()` reads the newest `service*.log`
  (newest-first, same rotation/UTF-16-BOM/share rules as `ScanOneLog`) and the pure,
  unit-tested `DeterminePowerState` scans lines newest→oldest — `Switched Off` ⇒ Off,
  `Switched On`/`Connected to server` ⇒ On (ignoring the transient `Disconnected from
  server` and per-peer `Connected to <id>/'name'` lines), `Unknown` when unreadable.
  Only a POSITIVE `Off` blocks (Unknown falls back to app+adapter, no false alarm).
  The log read is cached + refreshed OFF the UI thread (`MaybeRefreshPowerState`,
  ~2s throttle + in-flight guard, mirroring `KickConnectionPing`) so the 3s banner
  poll does no UI-thread IO. **The banner's non-green "not ready" state is now RED**
  (same palette as NotInstalled: bg `#3d1f1f`, glyph `!`) — it covers closed AND
  powered-off; the model is traffic-light (red = not ready, green = connected). Don't
  "simplify" `DetectServiceRunning` back to adapter-only, and don't gate green on the
  stale membership parser. (`GetAdapterBytes`, the in-match traffic meter, stays
  adapter-only on purpose — it measures real bytes, and the app is in the tray during
  a match.) Pinned by `RadminPowerStateTests`. **The create-room dialog
  (`CreateLobbyDialog`) surfaces a NON-BLOCKING amber warning (`RadminWarning`,
  `MpCreateDialogRadminWarning`) when `RadminVpnService.GetStatus().IsServiceRunning`
  is false at open** — the host can still create the room (peers just can't join until
  Radmin is on); it never disables Create and a probe failure just hides the warning.
  The launcher is
  the *meta layer* (sign-in, lobbies, chat, mod-hash gating) over a **self-hosted
  Node/Fastify backend at `wol-lobby.duckdns.org`** — **not** a Cloudflare
  Worker. Sign-in is **Discord OAuth** (a state flow shaped like device flow),
  **not** GitHub, yielding a JWT cached in `launcher-config.json`. **Match history
  IS now wired (unranked "match log"); ELO is not surfaced and replay upload
  (`UploadAsync`) is still scaffolded with no live caller.** Authoritative source:
  the `MultiplayerSession.cs` class doc-comment + `LobbyApiClient.cs`. Scattered
  `WinDivert` / `PeerMesh` / `n2n` / `ZeroTier` mentions are historical comments.
  **Trust the code over both the README and stale comments here.**

- **The game-launch `OverrideAddress` injection binds to the Radmin ADAPTER IP,
  NOT the readiness-gated `RadminStatus.AdapterIp` — and a launch that can't bind
  it WARNS in the chat instead of failing silently.** The MP launch
  (`MultiplayerTab.BuildMultiplayerLaunchArgs`) appends `OverrideAddress="<26.x>"`
  so AoE3's LAN discovery binds to the Radmin NIC. It USED to gate that on
  `RadminVpnService.GetStatus().IsServiceRunning` (which requires the GUI process
  ALIVE + power ON + adapter Up), so a joiner whose Radmin app was merely CLOSED
  launched with **no** `OverrideAddress` → AoE3 auto-picked the first NIC
  (VirtualBox/wifi) → never saw the host's LAN game — a **silent** failure (a real
  diagnostic bundle: user DeLos, `extraArgs='+noIntroCinematics +disableESOProfile
  +dontDetectNAT'`, no OverrideAddress). Fix: bind to
  `RadminVpnService.TryGetAdapterIp()` — a NEW helper that enumerates the 26.x
  Radmin NIC WITHOUT the app/power gates (the background `RvControlSvc` keeps the
  adapter Up with its static 26.x identity IP even when the app is closed or
  "Desconectado", so the IP is readable and worth injecting regardless of the
  banner). `DetectServiceRunning` now calls the same helper AFTER its gates
  (banner semantics unchanged — zero regression). `BuildMultiplayerLaunchArgs`
  LOGS the outcome both ways (`OverrideAddress injected 26.x=<ip>` /
  `OverrideAddress OMITTED — no 26.x Radmin adapter Up`) so the next bundle is
  diagnosable. `LaunchActiveModGame` surfaces two chat warnings, keyed off whether
  the flag actually went in: NO `OverrideAddress` (no 26.x adapter at all) →
  strong `MpChatRadminNoAdapter`; flag present but `IsServiceRunning == false`
  (Radmin closed / powered off) → soft `MpChatRadminNotReady` ("bound your IP but
  Radmin isn't active — connect it"). Don't re-gate the injection on
  `IsServiceRunning`, and don't bind to `RadminStatus.AdapterIp` (null unless the
  full gate passes). The injection FORM is untouched (`OverrideAddress="<ip>"`, no
  `+`, double quotes — verified in-game; see the launch-args doc-comment).
  **Radmin state is now LOGGED (on change + at launch) and the "not ready" banner
  shows the adapter IP even while Radmin is off** — because a bundle where "Radmin
  was open but wasn't recognized" gave ZERO clue why (`GetStatus()` was never
  logged, so nothing recorded WHICH gate — GUI process / power / adapter — rejected
  it). `RadminVpnService.DescribeStateForLog()` composes a one-line English summary
  of every sub-signal (`installed=… app=running|NOT-running(Rv procs: …) power=On|Off|Unknown
  adapter=<26.x|none> serviceRunning=…`); when the GUI process isn't detected it
  lists the running `Rv*` process names (`ListRunningRvProcessNames`) — that's what
  would surface a Radmin version whose GUI binary isn't the exactly-matched
  `RvRvpnGui.exe`. `RefreshRadminBanner` writes it to the diagnostic log **only on
  a state CHANGE** (guarded by `_lastRadminLogSig`, so the 3 s poll stays quiet but
  records every transition), and `BuildMultiplayerLaunchArgs` appends it to the
  launch line so the launch instant is captured. Separately, the RED "not
  ready" banner branch (`!IsServiceRunning`) now shows the 26.x IP via
  `TryGetAdapterIp()` (`MpRadminNotConnectedBodyIp`) when the adapter has one — the
  launcher already sees the user's Radmin IP even when the banner is red. The
  detection gate (`IsServiceRunning`, `RvRvpnGui.exe` process name) was
  deliberately NOT relaxed — get the log first; a confirmed process-name mismatch
  in a future bundle is a separate, targeted fix.
  **Radmin-off messaging is INFORMATIONAL, never a blocker — creating and JOINING
  rooms are NOT gated on Radmin.** Joining (`JoinLobbyCoreAsync`) is gated only by
  the mod fingerprint; Create is never disabled. So a room is created AND joinable
  with Radmin off, and the game auto-injects the 26.x IP regardless — Radmin's
  tunnel is only needed for actual in-game peer connectivity. The old
  `CreateLobbyDialog` warning ("other players won't be able to join until you turn
  Radmin on") was FALSE and scared testers off; it's now an ℹ info note chosen by
  `RadminVpnService.TryGetAdapterIp()`: IP present → `MpCreateDialogRadminInfo`
  (room created, IP `{0}` injected automatically, connect Radmin to play); IP
  absent → `MpCreateDialogRadminWarning` (install/enable Radmin to play). Same
  softening on the two launch chat lines (`MpChatRadminNoAdapter` /
  `MpChatRadminNotReady`). Don't reword these back to imply Radmin blocks
  create/join, and don't add a Radmin gate to the join path.

- **The nav-row connection capsule OPENS the Radmin guide, and it is the only door
  to it once Radmin works.** The guide's other entry point is the "Show steps" button
  INSIDE the red banner — and that banner collapses exactly when everything is fine.
  So retiring the permanent banner in favour of the capsule silently retired the guide
  with it: `MultiplayerTab.OpenRadminAssistantWindow()` sat with **zero callers**, its
  own comment saying it was exposed "so MainWindow could trigger it in the future".
  Nothing could open the window in the one state the `autoOpened` rule below exists to
  serve. `MainWindow.ConnectionChip_Click` is that caller.
  **Two things this depends on.** The capsule had to become a `Button` whose template
  draws the same `Border`, so nothing moved a pixel; and it works from ANY tab because
  `ShowRadminAssistant` needs only MultiplayerTab's `_config` (set in `Attach` at
  startup) and `Window.GetWindow(this)`, both of which resolve while the tab is
  collapsed. It lives BELOW `CaptionHeight`, so it is ordinary client area — no chrome
  flag, and its tooltip actually fires. That tooltip always names the guide and only
  adds the address when there is one: the capsule is clickable either way, and a hint
  that appeared only sometimes would vanish exactly when the VPN is what is wrong.
  **Verified by invoking it and watching the window list** — the guide opens and stays
  open past 15 s with Radmin green, which is the `autoOpened` guard doing its job.
  Note for whoever checks this next: **UI Automation's `RootElement` children query does
  NOT enumerate the assistant** (owned + `ShowInTaskbar=false`), so it reports the window
  as absent and looks exactly like the auto-close bug it is meant to disprove. Use a
  Win32 `EnumWindows` sweep over the process instead; that sees it.

- **The banner's network-name copier and numbered instructions are GONE, not hidden.**
  `RadminNetworkNamePanel` / `RadminNetworkNameBox` / `RadminCopyNameButton` /
  `RadminInstructionsText` were orphaned in May when that content moved into
  `RadminAssistantWindow`: no code path ever set them Visible again,
  `RadminInstructionsText.Text` was never assigned at all despite a comment claiming
  `RefreshRadminBanner()` filled it, and the copy button's handler was unreachable.
  They were removed rather than left `Collapsed` — a control nobody can reach reads as
  a feature that exists. The strings only they used went too. **This was never a
  casualty of the redesign**, which is worth knowing because it looks like one.

- **`RadminAssistantWindow` auto-closes at `InAoE3Network` ONLY when the launcher
  opened it — the `autoOpened` ctor flag is load-bearing, don't drop it back to an
  unconditional close.** The auto-open path (`MultiplayerTab.MaybeAutoOpenAssistant`)
  fires *exclusively* while Radmin is NOT ready (`if (snap.Stage >= RadminStage.LoggedIn)
  return;` — "don't teach someone something that already works"), so a window we pushed
  reaching `InAoE3Network` means the tutorial finished and the ~1.2 s close is a
  celebration. The **"Show steps" button** (and the public `OpenRadminAssistantWindow`,
  which the NAV-ROW CONNECTION CAPSULE now calls — see the next bullet)
  can summon it at ANY stage: with the checklist already green, `Refresh()`'s first tick
  (`_lastStage` starts at `-1`, so it always runs once) saw `InAoE3Network` and slammed
  the window shut ~1.2 s later. That's not just annoying — **once everything is green
  the ONLY thing that window offers is the copy-network-name button**, so the auto-close
  destroyed the exact reason to open it. Rule: *they opened it, they close it* —
  `ShowRadminAssistant(bool autoOpened = false)` defaults to manual so every
  user-initiated entry point is safe by construction, and only
  `MaybeAutoOpenAssistant` passes `true`. Deliberately NOT smarter (e.g. "auto-close a
  manual window only if it wasn't green on open"): that is unpredictable — it would
  close sometimes and not others depending on Radmin's state at open time.

- **The `.age3Yrec` format, measured — don't re-derive it, and don't trust the
  community references.** `ReplayParserService` reads the recorded game AoE3 writes to
  `My Games\<mod>\Savegame\` when a match ends. Container:

  ```
  0  "l33t"                  magic (the same four bytes AoE2 recorded games use)
  4  uint32 LE               decompressed size
  8  zlib stream (78 9C)
  ```

  The declared size matched the real one **exactly** on both files tested, so verifying
  it is a free corruption check — do it before parsing offsets that a truncated file
  would invalidate. Inside is a settings dictionary:

  ```
  [uint32 nChars][key UTF-16LE][uint32 type][value]
  type 1 = float (4B) · 2 = int (4B) · 5 = bool (1B) · 9 = string ([uint32 nChars][UTF-16LE])
  ```

  **All four types are load-bearing and a partial table fails SILENTLY, not loudly.**
  With only int+string the walk dies on `gamerestored` — the 3rd key, a bool — which
  still returns the map and player count (they come earlier) and **zero players**. That
  is what the first run of these tests did. A real 2v2 has 370 keys: 159 int, 150
  string, 35 bool, 26 float, then `0xFFFFFFFF` terminates.

  **`civ` is a raw index, not a name, and the index is per-mod** — civ 8 in Struggle of
  Indonesia is not civ 8 in Wars of Liberty. The parser returns the number; resolving it
  needs that mod's own civ list. Don't map it to base-game names.

  **The outcome IS in the file — at the very end, not in the header.** An earlier pass
  concluded it wasn't, having looked for text markers and header keys; that was wrong.
  A normally-finished recording ends with:

  ```
  [00 × 12] [FF × 8] [A: uint32] [B: uint32] [C: uint32]
  A = slot that LOST · B = slot that RECORDED the file · C = number of humans
  ```

  **A is the loser, and the rival readings die on one case:** a game the recorder WON
  reports A = the opponent's slot, so A is neither the winner nor the recorder. Confirmed
  on five games whose result was known independently — three skirmishes (two resigned,
  one won) and two real 1v1s between humans. The 1v1s were **predicted before the result
  was known** and then confirmed by the player who lost them, which is what separates
  this from fitting the data afterwards. B was likewise predicted and held: two files
  from the same player point at him in both, across different slots.

  **Two of seven recordings have no trailer at all** — games that ended abnormally
  (a disconnect, a killed process). That is why `ReadOutcome` checks for the signature
  instead of assuming it, and why it returns a two-value confidence rather than a bool:
  a missing answer must not be readable as "it was a draw".

  **Confident requires all three:** the exact signature, an A naming a slot the header
  has, and exactly two players. Past a 1v1 "X lost" doesn't name a winner — the others
  may have lost too and nothing records the order — so team games stay draws until the
  room state can identify every player. **Ambiguous must be reported as a draw**: this
  feeds a rating, and an invented winner takes points from someone with nothing on
  screen to explain it.

  **The community references are worse than they look:** the official forum thread on
  the format is someone *requesting* documentation, and the AoE3:DE parser on GitHub
  declares **no license** (all rights reserved) *and* reads a different format —
  273-byte fixed header + raw DEFLATE, not `l33t` + zlib. Everything above came from
  reading real files.

  **THE GAME DOES NOT RECORD BY DEFAULT, and that — not the identity gap — is why almost
  every stored match is a 0.5.** Measured, not assumed: `optionrecordgame` was `false` in
  ALL FIVE installed mods' profiles on the maintainer's machine, and had never once been
  `true`. Without a recording there is nothing to read and the match silently becomes "no
  result", so this outranks everything else in this section. The setting lives in
  `<GameSettings Name="GameOptions">` → `<Settings Version="53">` →
  `<Setting Name="optionrecordgame">`, as lowercase `true`/`false` plain text with **no type
  attribute** (the typed value table belongs to the `.age3Yrec` container, not to profile
  XML — don't carry it over). Read from a real profile; do not guess it.

  **The launcher enables it once per mod, and the whole design is about not doing it
  twice.** `GameSettingsStore.PlanGameRecording` (pure, `GameRecordingPlanTests`) keys off
  the **per-mod** `ModState.GameRecordingApplied` (`bool?`) against the launcher-wide
  `LauncherConfig.EnableGameRecording` (default true). Three rules, each load-bearing:
  (1) **per-mod, because the profile is** — five mods means five separate files, so the
  single launcher-wide "seeded" marker that would mirror `BackgroundDefaultSeeded` seeds
  whichever mod is launched first and silently skips the other four; (2) `applied == wants`
  ⇒ do nothing, **even when the game has changed the setting since** — AoE3 rewrites the
  whole profile on exit, so writing every launch would permanently override a player who
  turned recording off inside the game; (3) the marker is set **only after the profile was
  actually reached**, which deliberately INVERTS the `BackgroundDefaultSeeded` precedent — a
  Run-key failure is a machine policy that will fail identically forever, but a missing
  `Users3\` is a legitimate *not yet* (the game creates it on first run) and marking it done
  would mean that mod never records at all. Opting out writes `false` once per mod, then
  stops. Hooked as the **last** step of `GameLauncher.ApplyLaunchRedirects`.

  **That hook must stay after the `ApplyTo` graft, and `optionrecordgame` must stay out of
  the shared copy — two halves of the same trap.** `GameSettingsSync.SharedSections`
  includes `GameOptions`, which the sync grafts **wholesale**, and the recording setting
  lives inside it. So (a) writing before the graft has the graft overwrite it moments later,
  and (b) `CaptureFrom` would carry `true` into `shared-profile.xml`, and after an opt-out
  the next launch of any sync-group mod would graft it straight back — with the planner
  correctly concluding it had already applied what the player asked for, so **nothing could
  ever notice the opt-out undoing itself.** `ExtractSections` therefore strips it, and
  `Graft` **carries the target profile's own value across** rather than merely omitting it:
  omitting alone would DELETE the setting, since a graft replaces the whole section. Both
  directions are pinned (`ExtractSections_LeavesGameRecordingBehind`,
  `Graft_NeverOverwritesGameRecording`).

  **MEASURED: writing the profile does NOT make multiplayer record. AoE3's per-match "Record
  Game" box is an independent control.** The box lives on the game's own setup screen
  (`mpsetup-recordCheckButton` in `data/uiMPGameSetupPage.xml`; single-player
  `SPS-CheckRecordGame`), and it came up **unchecked with `optionrecordgame=true` already on
  disk before the game started** — the precondition that matters, and one that took two
  attempts to establish, because AoE3 only writes the profile when it EXITS: the first test
  looked conclusive but had the setting reaching disk five seconds after the screenshot. So
  the box has to be ticked by hand, per match, and the profile write alone does not fix
  multiplayer. **Second thing that test settled, in the other direction: the game PRESERVES
  our written value** across a launch/exit cycle, so the launcher's write is stable and is not
  fought.

  **So the host is reminded before every launch, and only an explicit
  `LauncherConfig.GameRecordingReminderMuted` stops it.** It was first gated on "a recording
  was read, so it must be working" — and that is exactly what the measurement above kills: if
  the box resets each match, that rule goes quiet after the first success and lets every match
  afterwards go unrecorded in silence, the reminder vanishing precisely when it is needed.
  **Never re-derive that gate from a match that happened to record**; one success says nothing
  about the next. Host only (his recording is the one whose result is read), a chat line rather
  than a toast (the lobby window is on screen at that instant and the game is about to take
  over), and the string itself names where to switch it off, since a chat line cannot carry a
  button. Paired with it, `MaybeReportMissingRecording` reads the profile BACK after a match
  that should have been recorded and names the actual cause: still `true` ⇒ the per-match box,
  `false` ⇒ the game overwrote us on exit. Toast first, chat second — a successful report
  closes the room and tears the lobby window down, so a chat line can vanish milliseconds
  after it is written.

  **There is NO way to enable it automatically. Both candidates were tested and both failed —
  don't re-derive them.** (1) The profile's `optionrecordgame`, above. (2) **`+RecordGame` as a
  launch argument: tested twice and dead.** It was the strong candidate — `RecordGame` is a
  registered engine config (no-help-text block, beside `BroadenMPSearchOption`) and this
  launcher already proves `+` cvars work, since it injects
  `+noIntroCinematics +disableESOProfile +dontDetectNAT` on every multiplayer launch. Launching
  `age3y.exe +noIntroCinematics +RecordGame` left the multiplayer setup box unchecked; and
  because an unchecked box does not prove the config was ignored — the engine could plausibly
  read the config at match start while the checkbox tracks separate UI state — a **LAN game was
  hosted and played through with that argument and no box ticked, and no recording was
  written**. That second run is the one that settles it: the argument has no effect on
  recording at all. **Test the multiplayer path, not a skirmish** — the two setup screens carry
  DIFFERENT controls (`mpsetup-recordCheckButton` vs `SPS-CheckRecordGame`), so a skirmish
  result would not have generalised. A LAN game can be hosted and started solo by filling the
  other slot with an AI, so this needs no second player. `Startup\user.cfg` was never tried and
  is not worth it: it needs elevation (the install lives in Program Files), it is per mod copy,
  and it breaks byte-faithfulness. **Mind the two argument mechanisms** if you ever revisit
  this — they already cost two flip-flops with `OverrideAddress`: `+name` is a console cvar,
  `Name="value"` is a config assignment, and they are not interchangeable.

  So the per-match box is ticked by hand or the match has no result. That is why the reminder
  fires every launch rather than once — and why it is not only a chat line.

  **The primary surface is a standing band in the lobby's left column, `RecordReminderBand`,
  sitting immediately above the Start button.** Three placement facts are load-bearing.
  (a) **Not in the chat**: the chat auto-scrolls and keeps 500 rows, so the line posted at launch
  is gone by the next message — which is exactly how a competitive match gets played unrecorded.
  (b) **Not inside `RoomInfoCard`**: that card collapses as a whole when the room has no mod name,
  no password and no extra copy (`RenderRoomPanel` ~:2634), so a notice parked in it would vanish
  silently for those rooms. (c) It is `Grid.Column="0"`, which `InGameOverlay` covers during a
  match, so it hides itself for the match with no code at all — visible in Lobby and during the
  countdown, which is the correct lifetime.
  **Everyone sees it, worded differently** (`MpRecordBandHost` / `MpRecordBandGuest`): only the
  host can tick the box, but if he forgets, his opponent loses the result too, and hosts rotate.
  The wording picks itself in `RenderRoomPanel` beside the `RenameRoomButton` host gate, so a
  **host migration re-words it for free** — put it anywhere else and it goes stale. Static title
  and dismiss caption live in `ApplyLobbyStaticLabels` so a mid-room language switch catches them.
  **Nothing on that Border may be animated**: its brushes come from `DynamicResource` and are
  frozen, and animating one throws — the trap that froze the countdown line once.
  The chat line survives alongside it, promoted to `ChatSeverity.Warning` (both non-Info branches
  in `AppendChatRow` had been dead code): the band is ambient, the line is the nudge at the moment
  you leave for the game.
  **Verifying the lobby XAML needs more than the usual smoke test** — it only opens `MainWindow`,
  and this window's XAML is not parsed until someone signs in and enters a room, so a bad
  `{StaticResource}` here would ship unseen. Construct `LobbyWindow` on an STA thread with the
  `Styles/*.xaml` dictionaries loaded by explicit `pack://` URIs (App.xaml's own `Source` values
  are relative and resolve against the entry assembly).

  **Recording costs ~1.4 MB a game forever, so `GameRecordingPurge` cleans up — but only
  after files the GAME named.** Measured on real recordings: 765 KB – 2.1 MB.
  `IsAutoNamed` matches `^Record Game \d+$` exactly (the game's own
  `cStringRecordGameFileName` + a number); a **renamed** recording is never deleted and never
  counts against the `KeepNewest` budget either, because renaming one is the player saying
  they care. Nothing newer than the launch time is touched, so the match still being read
  can't be swept up by its own cleanup. Top level of `Savegame\` only — deliberately unlike
  the recursive search used to FIND a recording, since looking further is harmless and
  deleting further is not. Note the auto-name comes from a string table, so a localized
  install may write something else and the purge simply does nothing there: the safe
  direction.

  **The identity gap is the blocker for scoring, not the parsing.** The replay names
  players by their *AoE3 profile name* (`'69metal69'`); the backend needs the Discord
  `users.id`. Nothing links them. Only the host's own name is knowable, and it comes from
  `UserDataService.GetInGameName` — which determines a 1v1 completely (host won ⇒ the
  other lost) but not a team game, so anything beyond 1v1 stays a draw until the room
  state carries in-game names. **Ambiguous must report a draw — never an invented
  winner**, since this feeds a rating.

  **The in-game name is NOT in `LastProfile3.dat`.** That file holds the active profile's
  FILE name, which is the stock `NewProfile3` on all five installs checked — the same
  string for everyone. Used as an identity it would match every player against the same
  placeholder: no crash, just results quietly attributed to whoever was checked first.
  The real name is `<OnlineName>` inside `Users3\<profile>.xml` (fallback
  `optionskirmishnickname`). The chain is `LastProfile3.dat` → the profile FILE → the
  name inside it, and `GameSettingsStore` shares the first step rather than keeping its
  own copy. **It is per MOD, not per machine** — the same person is `Gorgorito` in one
  and `gorgorito` in another — so every comparison against it is case-insensitive.

  **Never trust "the newest replay" alone.** Replays other people send you live in the
  same `Savegame\` folder — that is where the game looks for them — timestamped when they
  were copied. On a real disk two of someone else's games sat eleven minutes newer than
  the player's own, so a match played in between selected a stranger's file, whose result
  would have been reported for two people who never played it. `FindMatchReplay` walks
  candidates newest-first (capped at 5, each costs an inflate) and takes the first that
  passes `LooksLikeThisMatch`: host present, human count matching the room, recorder slot
  = the host's. Nothing qualifying returns null, and no replay is always safer than the
  wrong one. `FindLatestReplay` survives for the chat line that names the saved file, where
  "the newest" is exactly right and no identity is needed.

  **Verified end to end on the maintainer's own disk, and both gates fire independently:**
  two strangers' replays in his `Savegame\` are refused for not containing him (the strong
  check), while `Code vs Nathan 2` — which reads `Confident` in isolation — is refused by
  ownership before its verdict can ever be reported. His own two recordings are accepted
  and then refused a result for being skirmishes. Redundant on purpose: each gate alone
  would have let one of those through.

- **The result is read BEFORE the match is reported, and that order is load-bearing.**
  `OnGameExitedAsync` runs `AnalyseMatchReplayAsync` first, then hands its
  `MatchReplayInfo?` to `TryReportMatchAsync`: the analysis needs the room's head count to
  tell our recording from a downloaded one, and the report is what consumes the result. The
  old order (report first) existed to stop a missing user-data folder from skipping the
  report via an early `return`; that `return` is gone — the analysis returns null instead,
  and null reports the same all-draws it always did.

  **`_matchContext` is cleared in `OnGameExitedAsync`'s `finally`, NOT in the
  report's.** It used to live in `TryReportMatchAsync`'s `finally`, which sits *below* that
  method's early-return guards — so on a **joiner**, who leaves at the very first of
  them (not the host), the snapshot was never cleared at all and survived into
  the next match. Same for a host whose game was too short or had too few players. It
  belongs to the MATCH, and `OnGameExitedAsync` is the end of the match on every
  client, host or not. Don't move it back. That `finally` carries **two** guards —
  `ReferenceEquals` (a whole new match may have started during the recording retries) and
  `GameRestartedSince()` (a REOPENED game deliberately keeps the same instance, so the
  reference matches and clearing would still be wrong).

  **The host's slot comes from the trailer, not from a second name match.**
  `LooksLikeThisMatch` only accepts a recording when `recorderSlot == host.Slot`, so after
  acceptance `RecorderSlot` IS the host's slot; with no trailer it is −1 and
  `HostResultFrom` returns null, which is consistent because a missing trailer is
  Ambiguous anyway.

  **`Services/Multiplayer/MatchResultResolver` is where the recording's verdict meets the
  room**, and it refuses unless the room had exactly 2 participants and the host is among
  them — with three or more, "the host scored X" leaves everyone else's score a guess. It
  was a private, untested method on `MultiplayerTab`; it is now a pure sibling of
  `PlayerStanding` taking a plain `double?` so nothing WPF crosses the boundary, because
  this is the one line where a mistake silently moves rating points between two real
  people. It returns a `HostResultDecision(Result, Reason)` rather than logging — the
  caller logs the reason, so "it refused" and "it refused for the right cause" are separate,
  testable claims. `ParticipantResult(hostResult, isHost)` owns the `1.0 - x` mirror, pinned
  by `TheTwoScoresAlwaysSumToOne` — which is exactly the `sum == N/2` the backend validates.
  The per-recording decision upstream is still the pure `ReplayParserService.HostResultFrom`.

  **`LooksLikeThisMatch` fails CLOSED on an unknown head count.** `expectedHumans <= 0` now
  rejects; it used to *skip* the check, quietly reducing three gates to two at the moment
  there was least to go on. Paired with that, `AnalyseMatchReplayAsync` has an explicit
  announce-only branch for an empty roster — mirroring its no-in-game-name one — so the
  `MpChatReplaySaved` chat line naming the file doesn't disappear as a side effect.
  (`ReplayMatchSelectionTests.AHumanCountOfZeroConfirmsNothing` is the inverted test; it
  used to assert the opposite.)

  **The search RETRIES, and only for the one reason worth waiting on.** It runs the instant
  the game process dies, so the recording is often still being flushed: it fails to parse
  and the match silently becomes a draw. Two coupled fixes — `FindMatchReplay` now returns
  `ReplaySearch(File, Parsed, Unreadable)` and its callback a
  `CandidateVerdict{Match,NotOurs,Unreadable}`, so `MaxCandidatesExamined` (5) counts files
  that actually PARSED rather than files opened (a few half-written ones used to spend the
  whole budget and hide the real recording behind it), bounded by `MaxCandidatesOpened` (12)
  so a folder of junk can't spin. And `ShouldRetry` only fires when something was
  **unreadable** — one that parsed cleanly and belongs to another game will parse identically
  in three seconds. Delays `0/1000/2500/5000 ms`, so a match with no recording at all still
  reports at once and the worst case is ~8.5 s on the path that is wrong today.

  **Runs on a background thread** (`Task.Run`): each candidate costs an inflate of a
  multi-megabyte file and this fires the instant the game closes, with the player looking
  at the launcher again.

  **Civ is deliberately still null.** The recording gives an INDEX; turning it into a name
  needs the mod's civ list, which Improvement Mod doesn't ship loose (it is inside the
  `.bar`) and whose ordering against `data\civs.xml` is plausible but **unconfirmed**
  (7 = `Indians`, 17 = `Italians` land on real civs, which is not proof). Sending the bare
  number would put a value nobody can interpret into everyone's history. `MapName` IS sent
  now, from `gamefilename` — the real map, not `gamemapname`, which is the POOL.

- **The Profile tab shows a SERVER-side standing, and the win rate divides by DECIDED games —
  never by games played.** Everything on that tab lives in the backend's `elo_ratings` table
  (Glicko, `src/elo/glicko2.ts`); the launcher stores only a per-session copy. `GET
  /matches/elo/:userId` (client: `LobbyApiClient.GetEloAsync`, DTO `EloSnapshot`) gained
  `wins`/`losses` — a `SUM(CASE …)` over `match_participants` counting only `result >= 0.999`
  and `<= 0.001`. **`wins + losses` deliberately does NOT equal `games_played`:** a 0.5 means
  the outcome could not be read, and that is the MAJORITY of stored rows, so dividing by
  `games_played` would report **3 %** for someone who won 3 of their 4 decided games. The rule
  lives in the pure `Services/Multiplayer/PlayerStanding.WinPercent` (pinned by
  `PlayerStandingTests`), which returns **null** when nothing has been decided — the profile
  then shows no rate at all rather than a 0 %, the same refusal as the History badge. An older
  backend sends neither field, which deserializes to 0/0 and lands on that same silence with no
  special case. **Fetched once per session, never on a timer** (`_cachedStanding`, dropped on
  sign-out and after a report moves the rating): the endpoint allows 20/min · 500/day **per
  IP**, and that IP is shared behind NAT or an active Radmin network. A failed or offline fetch
  leaves the lines BLANK — the 1500 the server hands new players must never be shown as if it
  were earned. Verified against the live backend: today's deployment answers without the tally
  and the rate is correctly hidden.

  **The History row shows Win/Loss and NOTHING for 0.5 — the omission is the rule.** A 0.5
  means the result could not be read (no recording, a team game, a skirmish, or any match
  reported before this existed), so a "Draw" label would show all of them as drawn games
  that never happened. Every meta segment is likewise dropped when empty, so an old row
  renders exactly as it always did.

- **The History subtab is fed by a HOST-ONLY, unranked match report at game
  exit — don't re-add per-player reporting or an ELO/win-loss display.** The
  Multiplayer → History tab (`RefreshHistoryAsync`/`BuildHistoryRow`) was fully
  built but empty forever because nothing called `ReportMatchAsync`. Now
  `MultiplayerTab.TryReportMatchAsync` (invoked from `OnGameExitedAsync`, AFTER
  `AnalyseMatchReplayAsync` — see the result-wiring bullet below) posts the
  finished match. **Load-bearing rules:**
  (1) **host-only** (now `ctx.IsHost`, decided when the match STARTED — see the
  `MatchContext` bullet below) — `OnGameExitedAsync`
  fires on EVERY player's client and the backend inserts a `match_participants`
  row for each participant (so every player's own `GET /matches/history/:userId`
  returns it), so a single host report fills everyone's history; without the gate
  you get N duplicate matches. (2) The participant list is a SNAPSHOT taken at
  match START (`MatchContext.Participants`, captured in `EnterInGamePhase` from
  `_roomMembers.Keys`), NOT the roster at exit — the honest
  "who was in the room when we launched" (AoE3 never tells the launcher who
  actually entered the LAN game). (3) It uses the **`lobby_id`-present** branch of
  `POST /matches`, which the backend host-validates AND **closes the room**
  (`status='closed'` + `finalizeRoom` Discord webhook) — the maintainer's "close
  the room when the match ends" choice; the backend WS close (`4007
  match_reported`) tears down the lobby window for everyone. (4) **`result=0.5` is
  now the FALLBACK, not the design** — a clean human 1v1 reports the real winner
  (result-wiring bullet below); a team game, an unreadable recording or one refused
  by any gate still reports all-draws. `BuildHistoryRow` shows `mod [Win|Loss] ·
  N players · map · duration · date`. **The backend never needed a change for any of
  this** — an earlier note here claimed it forced 0.5; it does not. `POST /matches`
  has always taken `p.result` per participant, validated the sum against N/2, fed it
  to Glicko via `applyMatch`, and returned `result`/`map_name`/`rating_*` from
  `GET /matches/history/:userId`. The all-draws came from the LAUNCHER. No redeploy.
  (5) **Anti-noise gates**: skip when the snapshot has < 2 players or
  the match ran < 3 min (an opened-and-closed AoE3). The whole call is best-effort
  non-fatal (offline / 404 room-GC'd / 403 host-mismatch swallowed with a log).
  Backend: `GET /matches/history/:userId` gained a `player_count` subquery →
  `MatchHistoryRow.PlayerCount` (0 on an old backend → the "N players" chip is
  hidden). **Resource cost is negligible** (1 POST per match, 1 GET per tab open,
  <1 MB per 1000 matches — no sockets/timers), which was the user's concern.
  **Known limitations:** a host crash = no report (match lost from all histories);
  lobby membership ≠ guaranteed in-game; no map/civs. (A host who merely CLOSES the
  room mid-match is no longer one of these — that used to lose the match and is what
  `MatchContext` fixed.) Server change lives in the
  sibling repo `wol-launcher-lobby-node` (`src/matches/rest.ts`) and needs a
  redeploy (`git pull` + `systemctl restart wol-lobby`) for `player_count`.
  **Observability (load-bearing for diagnosis):** `TryReportMatchAsync` LOGS the
  reason for every skip (`not host` / `< 2 participants` / `< 3 min` / missing
  lobby+mod) and, on a real attempt, surfaces the outcome VISIBLY —
  "Match recorded" on success (`MpChatMatchRecorded`) and, on
  failure, the HTTP status + code (`MpChatMatchNotRecorded`, e.g. `HTTP 404 ·
  http_error`). **Both go through `AnnounceMatchOutcome`, NOT `AppendChatSystem`
  directly, and that is load-bearing:** `AppendChatRow` returns in silence when
  `_lobbyWindow == null`, and a SUCCESSFUL report closes the room, which tears the
  lobby window down — so the success line was being written to a window that had
  just disappeared. The helper falls back to a toast when there is no window (same
  lesson `MaybeReportMissingRecording` already encodes one method further down).
  This exists because a match that didn't record used to look
  identical (silent) whether it was skipped or failed — "nothing happened" was
  undiagnosable (the recurring confusion: creating a room records nothing, because
  the report only fires from `OnGameExitedAsync` when the GAME process exits, not
  on room creation). The failure chat line is genuinely visible because the room
  stays OPEN on failure (only success closes it). **Two testable halves:** the
  DISPLAY half is verifiable SOLO with no code — `GET /matches/history/:userId`
  has no ≥2 check (that lives only in POST), so seeding one `matches` +
  `match_participants` row with your own `users.id` renders the row; the REPORT
  half needs 2 real Discord users + a >3 min game. `MatchContext.Participants`
  keys are backend `users.id` (the room_state member dict is keyed by the JWT
  `sub`), so the `match_participants` FK is satisfied.

- **The facts of a match are CAPTURED at launch and CONSUMED at exit —
  `Services/Multiplayer/MatchContext.cs`. Nothing on the post-match path may ask the
  ROOM what just happened.** The incident: geaf hosted, closed the room while the
  game was still running, and the match never reached anyone's history. One action,
  two independent consequences, all in the same log second —
  `SyncRoomSocketSubscription` (socket → null) cleared the roster, **killed the game**
  and set `_isHostInCurrentRoom = false`, and the `OnGameExited` that its own kill
  triggered then arrived to find the state gone and logged
  `skipped — not host of this room`. (The other consequence, a recording with no
  outcome trailer because the process was killed, is CORRECT and not fixable — a
  match cut short has no winner.) So `_matchParticipantSnapshot` and
  `_matchStartedAtUtc` were **deleted** (not kept alongside — two lifetimes, one of
  which a teardown clears, is the bug) in favour of one immutable `_matchContext`
  captured in `EnterInGamePhase` and read by `OnGameExitedAsync` /
  `AnalyseMatchReplayAsync` / `TryReportMatchAsync` / `MaybeReportMissingRecording`.
  Same shape as `MainWindow._settingsSyncProfile` ("the profile captured AT LAUNCH").
  **The property that matters is negative: `CanReport` has no parameter and no field
  through which live room state can enter** — pinned by
  `MatchContextTests.AClosedRoomCannotChangeTheAnswer`. Four things stay LIVE on
  purpose and carry comments: the `game_ended` send (it operates on a live room),
  `_session.Api` and `_computeModFingerprint` (dependencies of the POST, not facts of
  the match), and every other `_isHostInCurrentRoom` read, which is UI.
  **The guard this INTRODUCED:** with the context surviving a teardown, a host who
  lost the socket would now report — and so would the player the room promoted, so
  `HandleHostChanged` calls `WithHostLost()`. **One way only**: losing the role
  silences us, gaining it does NOT arm us (the old host may be disconnected and never
  receive the frame that would have silenced them). A false negative costs one history
  row; a false positive corrupts two people's rating, and `ReportMatchRequest` has no
  idempotency key for the backend to catch it with. For the same reason there is
  **no retry without `lobby_id`** on a 404: that branch only checks you are among the
  participants, so it would downgrade a host-validated report to an unvalidated one on
  the strength of an HTTP code.

- **A player whose AoE3 closes while the ROOM keeps playing gets "Abrir el juego" —
  and it is GUESTS ONLY, by protocol, not by taste.** Reported: close the game
  mid-match and you are stuck — `ExitInGamePhase` drops you to the Lobby phase where
  `StartButton` is host-only, so a guest had nothing to press, and leaving the room to
  re-join is refused by the backend with `Conflict('Lobby already in game.')`
  (`rest.ts`) until the match ends. `RejoinGameButton` (LobbyWindow row 4, between
  Start and Leave) calls `RejoinGame` → `LaunchActiveModGame()` +
  `EnterInGamePhase(process, resume: true)` — **exactly what the countdown does when
  it expires, and no frame is sent to the server**: the room is already `in_game`
  there, and the launch args carry the player's OWN Radmin address, so AoE3 finds the
  host's LAN game over the VPN unchanged. Nobody else can tell.
  **Never the host**, because when the host's game exits the launcher sends
  `game_ended`, the room reverts to `open` and everyone must relaunch — which is what
  their own Start button does; they only get a caption change to "Volver a abrir el
  juego" while `_roomMatchLive` (via `StartButtonCaption()`, read by BOTH writers of
  that button or they drift). The rule is the pure
  `Services/Multiplayer/RoomMatchState.ShouldOfferRejoin`, pinned by
  `RoomMatchStateTests`. **Three load-bearing details:**
  (1) **`_roomMatchLive` is a THIRD lifetime**, distinct from `_matchPhase` ("am I
  playing") and `_matchContext` ("the match I will report"): set in `StartCountdown`
  (the one point every member passes, including one whose launch fails), cleared by
  `game_cancelled`, by the socket teardown, and by the host's own successful
  `SendGameEndedAsync` — the server excludes the sender from that broadcast, so
  nobody else will tell them.
  (2) **`resume: true` must NOT re-stamp `_matchTimerStartTicks` /
  `MatchContext.StartedAtUtc` / `_matchBaselineBytes`.** `WithinAbortWindow` is
  "InGame and < 60 s since the tick stamp", so re-stamping would REOPEN the
  abort window twenty minutes in and the overlay button would go back to reading
  "Abort match" — which cuts the match for everyone — plus MATCH TIME would reset to
  00:00 and the reporter would measure the duration from the relaunch.
  (3) **`GameRestartedSince()`** (`_matchPhase == InGame`) guards the two things the
  PREVIOUS game's exit handler does after its awaits (the recording retries take up to
  ~8.5 s): sending `game_ended`, which would make the server broadcast
  `game_cancelled` to everyone EXCEPT us and kill the AoE3 the others had just
  reopened; and clearing `_matchContext`, which a resume deliberately keeps (so the
  `ReferenceEquals` guard alone is not enough — both are needed).

- **Leaving a room mid-match now CONFIRMS, and the guard lives in
  `LobbyWindow.OnClosing`, not `Closed`.** Leaving used to be the only destructive
  multiplayer action with no confirmation at all — it kills every player's AoE3 in
  silence, which is the root cause of the incident above; only closing the LAUNCHER
  ever asked. `Closed` runs with the window already going, too late, so the guard is
  an `OnClosing` override using **cancel-then-reclose** (it is synchronous and
  `MpAlertOverlay.ConfirmAsync` is awaited), plus `SuppressLeaveConfirm()` which
  `CloseLobbyWindow()` calls for every PROGRAMMATIC close (kicked, signed out, tab
  teardown) — those must never ask. `LeaveRoomButton_Click` calls the same confirm as
  its first line (that button IS reachable during the countdown, when `InGameOverlay`
  is not covering the column yet) and then suppresses the window's own, so the
  question is asked exactly once. Which text appears comes from the pure
  `RoomMatchState.WarnOnLeave`: a running game of our own outranks a running room
  (host → "closes it for everyone", guest → "closes yours"), and only once ours is
  already closed does `RoomStillPlayingCannotRejoin` ("you will not be able to come
  back") become the point. **Host-vs-guest wording reads `_matchContext?.IsHost`, not
  the live flag** — if the room has already collapsed the live one says `false` and
  the host would be shown the mild version of what they are about to do.
  **`MainWindow.OnClosing` stays a `MessageBox`** (it does `task.Wait(10 s)` on the
  confirm; an awaited in-app overlay needs the UI thread that `Wait` is holding, so it
  would freeze for ten seconds and then refuse to close) — comment on both sides.

- **AoE3 taunts in the LOBBY chat — `Services/TauntService.cs`. A message whose body
  is JUST a number (1..33) plays that taunt for everyone in the room, each in THEIR
  launcher's language. Nothing is sent over the wire and the backend is untouched.**
  The `"11"` already travels as an ordinary lobby chat message, so every client
  detects it in `MultiplayerTab.HandleChat` and plays it from its OWN embedded set.
  **That indirection is the whole point**: shipping the audio would force ONE language
  on everybody, which is exactly what the feature must not do. Load-bearing rules:
  (1) **The parse is strict — digits ONLY** (`TauntService.TryParseTaunt`, pure +
  pinned by `TauntServiceTests`): `"11"` is a taunt, `"gg 11"`/`"11 gg"`/`"11!"` are
  ordinary chat. A looser "contains a number" rule would blast taunts at the room
  during normal conversation — the explicit ask was "just the number, not something
  that ends up appending the number". (2) **The hook lives in `HandleChat` (the LIVE
  frame) and nowhere else** — history replays via `ReplayChatRing` → `AppendChatLine`
  without passing through it, so joining a room whose backlog has numbers does NOT
  machine-gun taunts (the same reason the chat blip lives there). (3) It fires for
  YOUR OWN line too (AoE3 plays your own taunt; the server echoes it back, which is
  why the blip has to filter on `UserId`) and `return`s before the blip — the taunt
  IS the sound. (4) **The throttle is PER-SENDER (`userId`), never global**: a global
  one would make two players taunting within the window collapse into one — you'd hear
  A's `5` and silently lose B's `20`. That breaks the feature it is meant to protect.
  (5) **Both language sets are embedded** (`Assets/Taunts/{en,es}/NNN.mp3`, ~2 MB,
  globbed in the `.csproj`) — NOT read from `<install>\Sound\taunts\`: the WoL payload
  ships the **Spanish** set only (verified 33/33 by hash against a canonical install;
  English matches only 5/33 — the five with no speech: Wololo/Laugh/Charge/Laugh
  Redux/Zing), so English exists nowhere on disk. **The filenames are English in BOTH
  sets** (`011 Are You Ready.mp3`) — the language is ONLY the folder they came from,
  so never infer it from a name; they are renamed to `NNN.mp3` because the number is
  the key and the originals carry spaces/apostrophes that make poor `pack://` URIs.
  (6) **`MediaPlayer`, not `SoundPlayer`** — taunts are MP3 and SoundPlayer decodes
  WAV only (converting would cost ~9-20 MB vs ~2 MB). SoundService's "no MediaPlayer"
  rule targets background-thread sounds; taunts come off `HandleChat`, already on the
  UI thread. Two MediaPlayer traps are handled and must stay: it **cannot open a
  `pack://` URI** (hence `EnsureOnDisk` materialising the resource once into
  `%LocalAppData%\AoE3ModLauncher\taunts\<lang>\`, which is not a convenience), and an
  unreferenced instance **gets GC'd mid-playback and the audio cuts off** (hence the
  `s_playing` list, cleared on `MediaEnded`/`MediaFailed`). Gated by
  `SoundService.Enabled`, so turning off "play sounds" also turns off taunts.
  **Testing note:** a test host cannot resolve `pack://` (WPF's Application never
  initialises, so the scheme is unregistered and `new Uri` throws "Invalid port
  specified") — `TauntServiceTests` reads the assembly's `.g.resources` table directly
  to pin that all 33×2 files are embedded, since a missing one is silent at runtime.

- **The BACKEND announces rooms to Discord CHANNEL(s) via webhook, with LIVE
  message editing — separate from the in-app bell above, and launcher-
  independent.** In `wol-launcher-lobby-node`, `src/lobbies/discordAnnounce.ts` is
  a small **stateful module** (in-memory `Map<lobbyId, RoomAnnounceState>` as a hot
  cache, **backed by the persisted `lobbies.discord_targets`** — see the
  restart/rehydration bullet below; it USED to be memory-only and that was a real
  bug). `POST /lobbies` (`src/lobbies/rest.ts`, after
  `ctx.rooms.getOrCreate`) fire-and-forgets `announceLobbyCreated`, which POSTs an
  embed to **every configured webhook** with `?wait=true` to capture each
  `message_id`. Then the message is **edited in place** as the room changes:
  `LobbyRoom.broadcast` (the single choke point every WS state change passes
  through, `src/lobbies/LobbyRoom.ts`) calls `notifyRoomChanged` on
  `member_joined`/`member_left` (live player count) and `game_countdown`/
  `game_cancelled` (status → In game / back to Waiting); edits are **debounced ~2 s**
  so a burst of joins is one edit (well within Discord's per-webhook edit rate
  limit). On room close, `finalizeRoom` edits the message to **"Closed"** (grey) and
  keeps it as history — hooked at the four `status='closed'` sites (`rest.ts`
  close-prior-rooms loop + host-leave-no-successor, `LobbyRoom.handleDisconnectCleanup`,
  `matches/rest.ts` match-reported). The embed is **embellished**: host avatar+name
  as author (`users.avatar_url`), room name as title, **mod icon as thumbnail**
  (`raw.githubusercontent.com/Gorgorito12/aoe3-mods-catalog/main/mods/<modId>/icon.png`
  — unknown mod 404s and Discord just omits it), fields Mod/Players/Status +
  **Opened/Lasted uptime**, **color by status** (gold/green/grey); no stray emojis.
  **The uptime field is a LIVE relative timestamp at ZERO server cost:** while the
  room is active the 4th field is `Opened: <t:<unixSeconds>:R>` — Discord's native
  relative-time markdown, which each CLIENT renders as "5 minutes ago" and updates
  live on its own (localised per viewer), so there is NO polling / periodic edit /
  timer on the backend (the constant timestamp is absent from `renderKey`, so it
  triggers no extra flushes). On close, `buildEmbed` swaps it to a STATIC
  `Lasted: <formatDuration(now-createdAt)>` (compact `1h 5m`/`12m`/`45s`) so a closed
  room shows its final duration frozen instead of an ever-growing "opened N ago".
  Don't "improve" the live counter by editing the message on a timer — that would
  burn the per-webhook edit rate limit + CPU for what Discord already does client-side.
  **`renderKey` is `players|status|title`, and the title being in it is
  load-bearing.** `notifyRoomChanged` DISCARDS an edit whose key is unchanged
  (that's what coalesces a burst of joins), so any embed field that can change
  while a room is open MUST be in the key. The title can: the host renames a live
  room via the `rename_room` WS frame (below). Drop `title` from the key and the
  rename updates every launcher but **silently never reaches Discord** — the
  worst kind of bug here, since nothing errors.
  **Multi-channel:**
  `DISCORD_WEBHOOK_URL` is a **comma-separated list** (parsed by `urlListEnv` in
  `env.ts` into `config.discordWebhookUrls: string[]`), so several channels/servers
  stay in sync; `configure(config, app.log)` in `index.ts` stashes cfg+logger so the
  broadcast/close paths can post without threading `ctx`. **Gated + safe:** no-op
  when the list is empty (NOT in `env.ts`'s hard-fail list) or the room is
  **private** (private rooms are never announced), never awaited (no latency to the
  201), and every fetch swallows its own errors (a Discord/rate-limit failure can't
  break room creation or the WS broadcast). All fixed text is **English on purpose**
  (community-facing, mirrors the server's English logs); the only variable text is
  the player-typed room name. Pretty mod name from the hardcoded `MOD_LABELS`
  (`wol`/`improvement-mod`/`aoe3-tad`), fallback to the raw `mod_id`. **Optional
  ROLE PING:** the create POST adds `content: "<@&<id>>"` +
  `allowed_mentions:{parse:[],roles:[id]}` so a "Players"/"Jugadores" role gets
  notified (the mention MUST be in `content`, not the embed; the `allowed_mentions`
  restriction stops a room name from @everyone-ing). It's ONLY on the create POST —
  the PATCH edits never re-ping. **The ping is PER-SERVER: `DISCORD_PLAYERS_ROLE_ID`
  is a comma list aligned POSITIONALLY with `DISCORD_WEBHOOK_URL`** (`roleIdListEnv`
  in `env.ts` keeps empty slots as placeholders — unlike `urlListEnv` which drops
  them — so the alignment holds; `announceLobbyCreated` builds the payload PER webhook
  via `roleIdFor(i)`). So `webhook[i]` pings `roleId[i]`; an empty / `"none"` slot
  skips that server's ping (a role belongs to ONE server, so each server must name its
  own role). The role id **DEFAULTS to the WoL community "Players" role at index 0,
  hardcoded in `env.ts`** (a role id is a public identifier, not a secret — same
  pattern as the other hardcoded server defaults), so it works with no config. Example
  for two servers: `DISCORD_WEBHOOK_URL=<wol>,<server2>` +
  `DISCORD_PLAYERS_ROLE_ID=1088344884882194563,1087729644989579374` (WoL first, same
  order). Keep the two lists in the SAME order and don't leave empty webhook slots
  (`urlListEnv` drops them, shifting the alignment).
  Deploy: `git pull` + set the comma-separated `DISCORD_WEBHOOK_URL`
  [+ optional `DISCORD_PLAYERS_ROLE_ID`] in `.env` + `systemctl restart wol-lobby`;
  **the `0002` migration auto-applies at startup** (`db.migrate` runs every unseen
  `migrations/*.sql` in lexicographic order and tracks them in `_migrations`), no
  `npm install` (`undici` already ships), no launcher rebuild. Deferred: a full
  Discord bot (a persistent gateway would blow the 1 GB VM's RAM).

- **The Discord announcement SURVIVES a server restart — message ids are persisted
  and the state REHYDRATES from the DB; and orphaned rooms are swept.** The
  message ids used to live only in `discordAnnounce`'s process-local `Map`, on the
  theory that "rooms are ephemeral and restarts rare — accepted trade-off". That
  trade-off was wrong in practice and produced a permanent ghost (the reported bug:
  an embed still reading `🟢 Open · 1/8 · hace 35 minutos` long after the room had
  closed and the server had been restarted). Two independent faults, both fixed:
  **(1) `finalizeRoom` no-op'd after a restart.** It did `rooms.get(id)` → miss →
  early `return`, so the embed was never edited to Closed. This silently broke even
  the paths that ALREADY called it — notably the "creating a room closes my prior
  one" loop (`rest.ts`). Fix: **migration `0002_lobby_discord_targets.sql`** adds a
  nullable `lobbies.discord_targets` (JSON `[{"w":"<webhookId>","m":"<messageId>"}]`),
  `announceLobbyCreated` persists it, and the internal **`ensureState`** rehydrates
  the whole `RoomAnnounceState` from `lobbies` JOIN `users` on a cache miss (every
  embed field already lived on those rows; only the ids needed the column). So any
  close path — and a REVIVED room's live edits — work across a restart.
  **Load-bearing details:** only the webhook's **id** is stored, never the token
  (the id is the public half of `.../webhooks/<id>/<token>`; the token is re-paired
  from `cfg.discordWebhookUrls` at edit time), so a leaked/backed-up DB can't post
  to the channel — don't "simplify" this by storing the whole URL. `notifyRoomChanged`
  / `finalizeRoom` keep their **synchronous** signatures (6 call sites, one on the WS
  broadcast path) and rehydrate fire-and-forget; a `rehydrating` in-flight map stops
  two concurrent closes from double-PATCHing. `normaliseSqliteTimestamp` exists
  because `datetime('now')` yields `'YYYY-MM-DD HH:MM:SS'` with no zone, which
  `Date.parse` reads as LOCAL — without it the rehydrated "Opened `<t:unix:R>`"
  drifts by the host's UTC offset.
  **(2) Nothing ever closed rooms orphaned by the restart** (there was no startup
  sweep), so the `lobbies` row stayed `open` forever and the room also lingered as
  joinable in every launcher's browser. Fix: `src/lobbies/orphanSweep.ts` —
  `sweepOrphanLobbies` closes each candidate lobby with **no attached sockets** (row
  → `closed`, `lobby_members` deleted, `ctx.rooms.close`, `finalizeRoom` → embed to
  Closed), then one `ctx.globalChat.refreshPlayers()` (the players panel derives
  status from `lobbies JOIN lobby_members`, so without it everyone keeps reading "in
  a room"). It also re-checks each candidate's status and skips one already
  `closed` — a candidate can close normally DURING the grace window, and re-closing
  would stomp its real `closed_at` and re-edit its embed.
  **The candidate list is a SNAPSHOT taken at startup (`snapshotOrphanCandidates`),
  never a re-query when the timer fires** — this is load-bearing. The sweep's intent
  is "lobbies from the PREVIOUS process"; re-querying "everything still open" at
  T+90 s would also match a room created by the CURRENT process at ~T+85 s, whose
  host has its 201 but whose WS hasn't attached yet (zero sockets, very much alive)
  → a brand-new room killed seconds after creation. Lobby ids are random, so the
  snapshot is exact.
  **The `ORPHAN_SWEEP_GRACE_MS` (90 s) delay is load-bearing — do NOT sweep at
  startup directly.** A restart kills every socket but NOT the room: the launcher's
  `LobbyWebSocket` auto-reconnects (backoff to 30 s), `GET /lobbies/:id/ws` rebuilds
  the room via `rooms.getOrCreate` from the lobby row, and `hello` re-admits the
  member against `lobby_members` (which survives). **Rooms genuinely revive**, so an
  immediate sweep would close live rooms out from under the players sitting in them;
  90 s = the client's max backoff plus margin. The predicate is "no sockets" applied
  uniformly, **including `in_game`**: an in-game room WITH sockets is people actually
  playing (the match is P2P over Radmin and needs no backend), so closing it would
  tear down their lobby window for nothing. Pinned by
  `scripts/test-discord-restart.ts` (fake webhook HTTP server + a re-imported module
  to simulate the fresh process: persistence, token-not-stored, post-restart
  finalize, sweep closes an orphan, **sweep leaves a revived room open**).

- **The lobby room view (`LobbyWindow`) deliberately shows each datum once —
  don't "helpfully" re-add the removed fields.** `RenderRoomPanel`
  (`MultiplayerTab.xaml.cs`) fills it, and four duplications were stripped on
  purpose: (1) the title shows the room's **real name** — `CurrentLobbyTitle` is
  populated on create (from the dialog's `CreatedLobbyTitle`) and on join (from
  `LobbySummary.Title`), both threaded through new `title` params on
  `EnterHostedLobbyAsync` / `JoinLobbyAsync`. **That field was previously dead
  (only ever nulled), so the header always fell back** — if you see it reverting
  to a generic name, check those call sites still pass the title. A genuinely
  unnamed room falls back to `"{host}'s room"` (`Strings` key
  `MpRoomTitleFallback`; `MpRoomTitleGeneric` until the host is known) — **not**
  the raw lobby id, which already shows under the ROOM ID stat (that stat carries
  a 📋 `CopyRoomIdButton`, handled locally in `LobbyWindow.xaml.cs` — pure
  clipboard, no session round-trip). **The title is RENAMEABLE mid-room by the
  host** via a ✏ `RenameRoomButton` beside it (mirrors the 📋; opens
  `RenameRoomDialog`, a `PasswordPromptDialog` clone with a TextBox). Three rules:
  (a) **the client never paints the new name itself** — it sends the `rename_room`
  WS frame and waits for the server's `room_renamed` broadcast
  (`HandleRoomRenamed` → `MultiplayerSession.SetCurrentLobbyTitle` +
  `RenderRoomPanel` + a chat line), which is sent with `exclude: null` so host and
  peers can never disagree and a REJECTED rename can't leave the host showing a
  name nobody else has; (b) the 3-80 length rule is the SERVER's (identical to
  room creation in `rest.ts`) — the dialog repeats it only for instant feedback,
  and the backend also gates host-only + a 2 s per-room throttle; (c) button
  visibility is set in `RenderRoomPanel` (not once at open) so a **host migration**
  moves it to the new host. `room_state` does NOT carry the title — that's why the
  dedicated frame exists; don't "simplify" by expecting a room_state refresh to
  deliver it. The rooms-browser row needs nothing (`BuildRoomsSignature` already
  includes the title, so the 5 s quiet refresh repaints it) and Discord follows via
  `renderKey` (see the webhook bullet). (2) there is **no HOST stat** — the roster's
  per-row badge is the canonical host marker; (3) the info card (`RoomInfoCard`)
  is **Mod + Password only** (the old "Connection" cell duplicated the P2P status
  in the meta subtitle and "Max players" duplicated the PLAYERS stat), collapsing
  entirely when neither field has data; (4) the PLAYERS stat reads `"1 / 8"` — or
  just `"1"` when the max is unknown — with no trailing "players" word. Capacity
  comes from a `_currentLobbyMaxPlayers` stash that **mirrors `_currentLobbyModId`**
  (set on create/join, cleared on leave) and is read as a fallback by
  `TryGetCurrentLobbyMaxPlayers`, because the HOST is absent from the browser
  snapshot (`_lastBrowserList`) it checks first; the Mod name resolves the same
  way (`_lastBrowserList` → `_currentLobbyModId` fallback) so the host sees the
  mod, not an em-dash. The two-method label refresh this view relies on is in the
  Localization bullet under Runtime conventions. **The PLAYERS numerator is derived
  from the roster in ONE place (`RefreshRoomPlayerCount`, called from BOTH
  `RenderRoomPanel` AND the end of `RenderRoomMembers`) so the stat can never lag
  the roster.** It used to be set inline only in `RenderRoomPanel`, but the
  incremental `member_joined`/`member_left` handlers call `RenderRoomMembers`
  (rebuild roster) WITHOUT `RenderRoomPanel`, so a join grew the roster to 2 while
  the stat stayed the stale `"1 / 8"` from the last `room_state` (the reported
  "somos dos pero figura 1" bug). Since every room frame calls `RenderRoomMembers`,
  refreshing the count there keeps stat == `_roomMembers.Count` always. (The
  `room_state` WS frame carries NO count field — only the `members` roster — so
  `_roomMembers.Count` is the sole in-room source; the DB `current_players` is a
  SEPARATE fact surfaced only in the rooms-browser LIST.)

- **The players roster (`RenderRoomMembers` / `BuildMemberRow`) is host-first
  with open-slot placeholders — keep both, they're load-bearing UX.** Members
  sort host-first via a *stable* `OrderByDescending` (non-host members keep their
  join order); below them, one dimmed "`Esperando jugador…`" row
  (`BuildOpenSlotRow`, `Strings` key `MpRoomSlotOpen`) is emitted per unfilled
  slot up to the room capacity, so the list shows at a glance how many can still
  join (needs the `_currentLobbyMaxPlayers` capacity above — no max, no
  placeholders). Per row: the Host/Ready badges are localised
  (`MpRoomBadgeHost` → "Anfitrión", reused `MpRoomReady` → "Listo"), and a player
  who has readied up gets a subtle green row tint (`#223FB950`) on top of the
  small Ready pill. Relatedly, `MpDivider` was raised `#2C313A → #3A434F` in
  `Colors.xaml` so the lobby / MP cards stop blending into the near-black
  `BgBase` — a **global** brush change across every multiplayer surface (rooms
  table included), not a per-dialog recolour.

- **The big Ready button is refreshed from the ROSTER (`RefreshReadyButton`, called
  by `RenderRoomMembers`), and turns green ONLY via the `Tag="ready"` trigger in the
  `MpReadyButton` style.** It took BOTH halves to fix "la opción de marcar como listo
  funciona, pero el botón grande no se pone verde", and either alone is useless:
  **(1) Nothing refreshed the button on a ready toggle.** The label + `Tag` used to be
  set inline in `RenderRoomPanel` ALONE — but neither `ReadyButton_Click` nor the
  server's `member_ready` echo (`HandleMemberReady`) calls RenderRoomPanel; both only
  call `RenderRoomMembers`. So readying up tinted your roster row and left the button
  frozen on "○ Marcar listo" until an unrelated full `room_state` frame (a join, a
  host change) happened by. This is the SAME shape as the "roster 2, stat 1/8" bug,
  and it has the same fix: `RefreshReadyButton` is derived from `_roomMembers` and
  called from `RenderRoomMembers` (which EVERY room frame runs), exactly like
  `RefreshRoomPlayerCount`. Don't move it back inline into `RenderRoomPanel`.
  **(2) The `Tag="ready"` trigger didn't exist** in `Styles/Buttons.xaml`, while the
  style's own comment described a green active state — so even a correctly-set Tag
  painted nothing. ALL the colour lives in the style; the code-behind only sets the
  Tag. A green build proves nothing here (both halves compile fine either way).
  **Trigger ORDER is load-bearing** (last active trigger wins per property):
  `IsMouseOver` → `Tag="ready"` (solid `#15803D` + `MpStatusReady` border + white
  text) → the ready+hover `MultiTrigger` (`#16A34A`, so a ready button still reacts
  to the mouse) → `IsEnabled=False` LAST so disabled always overrides. It also only
  works because `LobbyWindow.xaml` sets **no local `Background`/`Foreground`** on
  the button — a local value beats every `ControlTemplate` trigger (the same WPF
  precedence trap documented for the title-bar brand button).

- **`LobbyWindow` is an INDEPENDENT top-level window with its own Windows taskbar
  button.** It's `WindowStyle="None"` + `WindowChrome` + **`ShowInTaskbar="True"`**
  and has **no `Owner`** (`OpenLobbyWindow` deliberately does NOT set one), so it
  gets its own taskbar entry next to the launcher, alt-tabs independently, can sit
  on another monitor, and is NOT hidden when the launcher minimizes. The title bar
  is the shared `Controls/TitleBar` (see its global bullet) with the full
  **minimise / maximise / close** trio (`ShowMinimize`/`ShowMaximize`/`ShowClose`
  all true); minimise is a plain `WindowState.Minimized`, which —
  *because* `ShowInTaskbar="True"` — goes to the **Windows taskbar button** (click
  it to restore), NOT a desktop stub. **`ShowInTaskbar="True"` is load-bearing:**
  the entire original "minimise pops a system menu" bug came from
  `ShowInTaskbar="False"`, where a chromeless minimise fell to the unstylable
  bottom-left desktop *stub* whose click opens the OS system menu
  (Restore/Move/Size/…) — "se ve así" + "no me tire un menú". Don't flip it back to
  False, and don't re-add an `Owner`. (History, each built then rejected before
  landing here: a glowing in-window "pill" minimise replacement; an in-tab "Sala"
  sub-tab; and removing minimise entirely. The accepted answer is "just a normal
  taskbar window".) `WindowStartupLocation` is `CenterScreen` (was `CenterOwner`,
  which needs the Owner we no longer set).

- **The TRAFFIC + CONNECTION metrics are the only REAL connection numbers, and
  both are OVERALL, not per-peer.** TRAFFIC (in-game overlay, `RefreshInGamePanel`)
  = the Radmin VPN adapter's `BytesSent + BytesReceived` *delta since match start*
  (`RadminVpnService.GetAdapterBytes`, baselined in `EnterInGamePhase` as
  `_matchBaselineBytes`) — it's the whole adapter, not this game or one peer, but
  during a match that's effectively the game; shows "—" when no 26.x Radmin
  adapter is up. CONNECTION is your general **INTERNET** latency — an ICMP
  round-trip to a public anycast resolver (`PingInternetRttMsAsync`: Cloudflare
  1.1.1.1, then Google 8.8.8.8), cached in `_connectionPingMs` and refreshed by a
  fire-and-forget `KickConnectionPing` (guarded by `_connectionPingInFlight`),
  colour-coded (<80 ms green / <200 amber / else red). That ONE value drives every
  "ping" in the multiplayer UI: the in-game CONNECTION stat, the lobby header
  CONNECTION stat (`RoomConnText` via `UpdateLobbyPing` on a `_lobbyPingTimer`),
  and the rooms-browser PING column (`RefreshRoomPingCells` on a `_roomsPingTimer`,
  updated **in place** so rows — and their Join buttons — aren't rebuilt). It is
  **your** internet latency, **not** a per-rival ping, so it's identical across all
  browser rows. (We deliberately dropped the earlier Radmin seed-peer ping: it
  needed a specific peer online AND you already on the VPN, so it usually showed
  "—".) **The in-game per-peer RTT column is now REAL (not a placeholder).** It
  used to be `…` because the launcher couldn't map a Discord login to a Radmin IP.
  That's solved end-to-end: each launcher reports its own Radmin IP (26.x) via the
  `set_radmin_ip` WS frame — sent on room ENTRY (`OpenLobbyWindow`) and at
  `EnterInGamePhase`, re-sent each tick if it changes (`MaybeReportRadminIp`); the
  backend stores it on the room member and broadcasts `member_net` + includes
  it in `room_state.members[x].radminIp`; `HandleMemberNet`/`HandleRoomState` save
  it on `RoomMemberEntry.RadminIp`; and `KickPeerPings` (off the 1s in-game tick,
  parallel, guarded by `_peerPingInFlight`) ICMP-pings every peer's Radmin IP via
  `PingPeerAsync` (a single-host clone of `PingInternetRttMsAsync`), storing the RTT
  on `RoomMemberEntry.PingMs`. `BuildInGamePeerRow` colours it green/amber/red on
  the same thresholds as CONNECTION so a laggy player stands out. The
  Radmin IP is validated server-side against `26.x` (a client can't inject an
  arbitrary host for everyone to ping), and it's only shared among that room's
  members — the same IP they already use to actually play (`OverrideAddress="<ip>"`).
  **Two fixes made this actually VISIBLE + gave the −1 case meaning (the "alucard
  no muestra el ping" report).** (1) **Layout:** the peer row used FIXED columns
  (name 180 + state 110 + rtt 80 + bytes ⭐ = 370 px) inside the ~284 px left panel
  with NO horizontal scroll, so the RTT + bytes columns were CLIPPED off the right
  edge — the ping was computed but never seen. `BuildInGamePeerRow` is now
  `[health-dot Auto] [name ⭐ ellipsis] [ping-or-status Auto]` (the always-zero bytes
  placeholder column was DROPPED), so the ping/status can't be pushed off-screen.
  Don't reintroduce fixed name/state widths there. (2) **Meaning of −1:** a peer's
  state is derived by the pure, testable `Services/Multiplayer/PeerNetHealth.Classify`
  (`PeerNetHealthTests`) → `PeerLinkState` {WaitingVpn, Online, Unstable, Lost},
  rendered as a coloured dot + text: grey **"Esperando VPN"** (no Radmin IP reported
  yet) vs a real **"NN ms"** vs amber **"…"** (transient miss) vs red **"Sin
  conexión"** (sustained ICMP silence past `LostThreshold`=5 consecutive 1-s probes).
  `RoomMemberEntry` gained `ConsecutiveFails`/`ConsecutiveOks`/`LastLinkState` (updated
  in `KickPeerPings`); `RefreshInGamePanel` posts a chat line ONLY on the Online↔Lost
  edge (`MpChatPeerLost`/`MpChatPeerReconnected`), debounced by the fail streak.
  **Load-bearing caveat:** the ICMP "Sin conexión" is INDICATIVE only — Radmin/Windows
  frequently block inbound ICMP echo while the game works fine, so it's a soft "no
  responde", NOT an authoritative disconnect; the authoritative "left" signal stays the
  server's `member_left` (`HandleMemberLeft`). Peer pinging + the health signal now also
  run in the LOBBY (pre-match): the `_lobbyPingTimer` tick calls
  `MaybeReportRadminIp`/`KickPeerPings`/`RefreshRosterHealthDots`, which recolours the
  roster's per-member dot (Tagged with the userId in `BuildMemberRow`) in place — the old
  always-green dot was static. Your own row is always green / "tú".
  **Two load-bearing rules keep the reported IP consistent with the game — both were the
  SAME "Esperando VPN despite the game working" bug.** (1) `MaybeReportRadminIp` reads
  `RadminVpnService.TryGetAdapterIp()` (the GATE-FREE 26.x enumeration), NOT
  `GetStatus().AdapterIp` (null unless the full readiness gate passes: GUI `RvRvpnGui.exe`
  alive + power ≠ Off + adapter Up). It MUST match what `OverrideAddress` binds at launch —
  else a user whose Radmin GUI is merely CLOSED (background `RvControlSvc` keeps the adapter
  Up) launches bound to the correct NIC yet is reported to everyone as `WaitingVpn`
  "Esperando VPN" the whole match (real bundle: `serviceRunning=False adapter=26.58.19.45`,
  game played ~30 min fine). This is the exact class of bug the `OverrideAddress` injection
  already fixed; don't re-gate the report on `GetStatus`. "Esperando VPN" now means only
  "no 26.x adapter at all". **Semantics nuance:** a player with the adapter Up but Radmin
  *powered off* now shows red "Sin conexión" (peers' ICMP gets no reply through the dead
  tunnel) instead of grey "Esperando VPN" — genuinely unreachable, so honest. (2) The dedup
  guard `_lastReportedRadminIp` is reset **on every room ENTRY** (`OpenLobbyWindow`, plus an
  immediate `MaybeReportRadminIp()` there to kill the ~2.5 s pre-first-Tick flicker), not
  only in `EnterInGamePhase`. The guard is per-launcher-session, so without the entry reset
  a user entering a SECOND room with an unchanged IP would `Equals`-short-circuit → never
  `set_radmin_ip` to the new socket → stuck "Esperando VPN" in room #2 (100% reproducible:
  create a room, leave, join another). Don't drop either the entry reset or the immediate
  report.

- **The rooms browser auto-refreshes its LIST on a quiet diff — separate from the
  PING timer above.** New / closed rooms now appear without pressing *Actualizar*:
  a dedicated `_roomsListTimer` (5 s, created in `StartQuotaPolling`, stopped in
  `OnVisibleChangedTabGate`'s not-visible branch alongside the other timers) calls
  `RefreshRoomsListAsync(quiet: true)`, gated to **MP-tab-visible + signed-in +
  `_activeSubtab == Subtab.Rooms`** so it never polls while the list is hidden
  (don't drop that subtab gate — it's the whole point of the resource budget). The
  `quiet` flag is load-bearing and does three things a full refresh doesn't: (1)
  skips the "Cargando…" skeleton; (2) compares a `BuildRoomsSignature` of the
  payload — id / status / players / private / title / mod / host per row, **in
  order, NOT ping** (ping is owned in place by `_roomsPingTimer`) — against
  `_lastRenderedRoomsSignature` and **returns without touching the visual tree when
  nothing changed**, so Join buttons, hover and scroll position survive; (3) on a
  network blip it keeps the last good list (logs only) instead of wiping it to the
  red error banner. The manual *Actualizar* button, sign-in, tab activation and
  leave-room still call `RefreshRoomsListAsync()` **non-quiet** (skeleton + error
  banner + always re-render); `SubtabRooms_Click` fires one *quiet* kick so
  returning to the subtab freshens at once. Cost is one `GET /lobbies` (a single
  small SQLite SELECT, ≤8 active lobbies) every 5 s **while actively browsing** —
  12 req/min (under the `llist` 60/min per-IP cap, `rateLimit.ts`); the 2000/day
  per-IP cap is only approached after hours of continuous browsing (accepted for a
  fresher list). (Was 10 s; dropped to 5 s on request.) **The rooms list itself is REST-poll** — the
  lobby WebSocket (`/lobbies/:id/ws`) is per-room and only joined once you're
  *inside* a room, so a viewer sitting on the list has no per-room socket. A
  process-wide global WS channel DOES now exist (`/global/ws`, added for the
  global chat — see its bullet below), so the infra for real-time room push is
  in place; actually emitting `lobby_created/closed/updated` onto it is still
  deferred (the 5 s poll is enough at current scale).

- **Discord avatars, the room-roster "peek", and the top-bar "players online" count
  (added together).** (1) **Avatars everywhere, via one helper.** `MultiplayerTab.
  BuildAvatarDisc(name, avatarUrl, size)` = a circular disc: the real Discord photo
  (`Ellipse` + `ImageBrush(BitmapImage(uri))`) over a coloured-hash monogram fallback
  (`HostMonogramBrush` + initial) — used by the roster (`BuildMemberRow`, ALL members
  now, not just "me"), the rooms-list HOST cell (`BuildRoomCard`), and the peek popup.
  The `avatar_url` had to be plumbed through the backend: `GET /lobbies` host object
  (`rest.ts` — `u.avatar_url AS host_avatar`) and the room WS `room_state`/
  `member_joined` members (`LobbyRoom.ts` — read `users.avatar_url` in the same
  membership query on `hello`, carry it on `MemberEntry`, and preserve it across the
  `member_ready` reconstruction via `{...existing}`). Launcher DTOs gained the field:
  `LobbyHost.AvatarUrl`, `WsRoomMemberFlags.AvatarUrl`, `RoomMemberEntry.AvatarUrl`,
  and `LobbyMember`/`LobbyDetail` (below). Legacy rooms without the field → monogram
  fallback (no break). (2) **Peek a room's roster WITHOUT joining.** `GET /lobbies/:id`
  is a PUBLIC endpoint returning the full roster (`members[]` with avatar/name/ready/
  role) — no join, no WS. `LobbyApiClient.GetLobbyByIdAsync` → `LobbyDetail`; the
  rooms-list PLAYERS cell is clickable (hand cursor + hover underline + tooltip) and
  opens a single-instance `Popup` (`_peekPopup`, `ChromePopups.Track`, deferred Closed-
  clear to toggle instead of reopen) showing each member via `BuildAvatarDisc` + host/
  ready badges. Works for full/private/in-game rooms (read-only). (3) **Top-bar "N
  players online" now reads the LIVE global-chat presence** (`_lastGlobalOnline`, fed
  by the `/global/ws` `presence`/`global_state` frames via `UpdateGlobalPresence`) via
  `UpdateTopBarCounts()`, so it matches the chat's "N connected" (real connected users)
  instead of the old `/quota` `players.active` (in-lobby count) that made it disagree.
  "N active rooms" stays from `/quota`; presence falls back to the `/quota` count until
  the first presence frame. **These need the backend redeploy for avatars** (the list/
  WS changes); the peek + top-bar count are launcher-only. (4) **The right column is now
  SPLIT 50/50 — global chat on top, a LIVE PLAYERS panel on the bottom, categorized by
  status: 🟢 In game / 🟡 In a room / ⚪ In launcher** (GameRanger-style). This REPLACED an
  earlier clickable "N players online" chip/popup (that pill + `OnlinePlayers_Click` +
  `_onlinePopup` are GONE — don't reintroduce them; the top-bar "N players online" is a
  plain static count again). The split is a `Grid` at `Grid.Column="2"` with rows
  `["*",12,"*"]`: row 0 = the existing chat card, row 2 = a new Players card cloning the
  same glow+`MpSurface`/`MpCardBorder`/`RadiusLg` chrome (`PlayersPanelTitle` header +
  `PlayersPanel` StackPanel in a `PlayersScroll` ScrollViewer). **Per-user status is
  backend-computed and pushed LIVE.** The `presence` / `global_state` frames carry
  `onlineUsers: [{userId, login, avatarUrl, status}]` alongside the `online` count;
  `GlobalChatRoom.onlineUsers()` is now **async** — it runs ONE bounded query
  (`SELECT lm.user_id, l.status FROM lobby_members lm JOIN lobbies l ON l.id=lm.lobby_id
  WHERE l.status IN ('open','locked','in_game')`, ≤ maxActiveGames×lobbyMaxPlayers rows on
  indexed columns) and maps each connected user: `in_game`→`in_game`, `open`/`locked`→
  `in_room`, absent→`idle`. `broadcastPresence` is async; `this.ctx` is stashed in
  `handleConnection` for the DB handle. **Live updates come from `GlobalChatRoom.refreshPlayers()`**
  (public, **debounced ~1.5s**, self-swallowing) called on every room-state change: the
  lobby paths reach it via a module stash `attachGlobalChat(globalChat)` (wired in
  `index.ts`) — `LobbyRoom.reflectToDiscord` (member_joined/left, game_countdown→in_game,
  game_cancelled→open) + `handleDisconnectCleanup` + `rest.ts` create/leave. No polling.
  Launcher: `_globalOnlineUsers` widened to `(userId, login, avatarUrl, status)`;
  `ParseOnlineUsers` reads `status` (missing→`idle`) and calls `RenderPlayersPanel()`,
  which clears `PlayersPanel` and emits the 3 status sections (dot + `<label> · N` header
  via `MpPlayersInGame`/`MpPlayersInRoom`/`MpPlayersInLauncher`, dots `MpStatusInGame`/
  `MpStatusFull`/`TextSecondary`) with one `BuildAvatarDisc` row per player (own row tagged
  "· you" via `_session.CurrentUser`). Empty (old backend / no presence) → `MpOnlinePlayersEmpty`.
  **This DOES need the backend redeploy** (the `status` field + `refreshPlayers` hooks); with
  an old backend the panel just shows everyone under "In launcher" (or empty).

- **The rooms browser is a TABLE with responsive columns — the action button
  isn't a plain always-"Join".** (Doc heads-up: an earlier revision of this
  bullet described a `WrapPanel` of cards whose "old table + column-header strip
  + zebra rows are gone" — that was **REVERTED**. The code is a table; trust it.)
  `BuildRoomCard` builds one full-width row per room into a `StackPanel`
  (`RoomsListPanel`), each a **7-column** `Grid` aligned under the
  `RoomsHeaderStrip` `ColHeader*` strip in `MultiplayerTab.xaml`: **ROOM, MOD,
  HOST, PLAYERS, PING, STATUS, ACTION** (the MOD chip is now its OWN column,
  split out of the ROOM cell — mockup-driven redesign). **Rows are flush TABLE
  rows, not floating cards, but each row has its OWN fill so a room stands out
  against the panel** (revived per-room colour): the `MpRoomCard` style fills with
  `MpRoomRowBg` (#16243A, a navy band distinct from the card `MpSurface` #0F1A2B +
  header band #0C1626), a 1px `MpDivider` bottom border, and an `MpRoomRowHover`
  (#1B2D49) hover, compact padding `14,9,14,9` (≈46px rows). Don't set it back to
  `Transparent` — the fill is the "a created room is visible" feature. **The two cards (rooms + chat)
  got a PREMIUM navy treatment** (mockup-driven): the tab background is a navy
  gradient (`MpTabBackground`), each card has a blue-tinted border (`MpCardBorder`)
  + `RadiusLg` corners + a soft blue outer **glow via a separate underlay Border**
  (a `DropShadowEffect` is on the underlay, NEVER on the content card — an Effect
  on the card kills ClearType on all its text). The MP palette (`Mp*` brushes in
  `Colors.xaml`) was retuned to a **deep-navy premium set** app-wide across MP
  (LobbyWindow, CreateLobbyDialog, buttons): `MpTabBackground` #0E1B2E→#07111F,
  `MpSurface` #0F1A2B, `MpSurfaceAlt` #182740, `MpRoomRowBg` #16243A / hover
  #1B2D49, `MpCardBorder` #403B82F6 (blue ~25% alpha), `MpDivider` #22344F,
  `MpTableHeader` #94A3B8, `MpBlue` #3B82F6 / hover #2563EB / pressed #1D4ED8,
  status Waiting #22C55E (green) / InGame #3B82F6 (blue) / Full #F59E0B / Locked
  #8B5CF6, ping #22C55E/#F59E0B/#EF4444. **The Waiting/InGame status dots are
  deliberately GREEN/BLUE (not the reverse) to match the Discord webhook's embed
  colours** (`discordAnnounce.ts`: open=green `0x22c55e`, in_game=blue `0x3b82f6`)
  so the same room reads the same colour in the launcher table and the webhook —
  green = open/joinable, blue = in progress. Because `MpStatusInGame` also tinted
  the ✓ "Listo/Ready" pill green, flipping it to blue would have turned that pill
  blue; so Ready now uses its own `MpStatusReady` (#22C55E green). Don't re-swap
  these back or the launcher and webhook will disagree again. The top-bar header Border is `Transparent` (blends into
  the navy gradient) with an `MpDivider` bottom rule; the Radmin banner's connected
  state is green #123C2B + a low-alpha green border (set in `RefreshRadminBanner`);
  the "Radmin VPN" NAT badge colours are set in `RenderNatBadge` (#182740/#94A3B8).
  `MpStatusOffline` was deliberately left alone (it leaks to the title-bar offline
  chip + PatchGeneratorDialog). **The seven columns
  are STAR-sized with Min/Max, NOT fixed px** — fixed widths overflowed a small
  window, and since the `RoomsListScroll` ScrollViewer **disables horizontal
  scroll**, overflow clips off-screen. Weights/mins:
  **all columns are proportional with NO MaxWidth** (except none) so on a wide
  window they grow TOGETHER and the "air" distributes evenly (symmetric) instead
  of ROOM absorbing all slack — the earlier `4*`-no-max ROOM produced a giant gap
  before MOD. Weights/mins (title is **single-line `CharacterEllipsis`**, compact
  rows): ROOM `2.3*` min120, MOD `1.05*` min58, HOST `1.35*` min66,
  PLAYERS `0.62*` min46, PING `0.62*` min48, STATUS `0.9*` min60,
  ACTION `0.95*` min100. Mins sum ≈498 so the 7 fit the min window (~714px table
  region) without clipping ACTION. **The ACTION button is `HorizontalAlignment=Center`
  + MinWidth 96 / MaxWidth 130** so it stays compact/centred (not stretched) now
  that its column has no cap. **ROOM and HOST cells are
  `Grid{Auto,*}` (disc in col0, text in col1), NOT horizontal StackPanels** — a
  horizontal StackPanel measures children with infinite width so wrap/ellipsis
  never fire; the text must live in a bounded `*` column.
  **The columns live ONCE, in `Services/RoomsTableLayout.All`, which both the header strip and
  `BuildRoomCard` read — the old "keep the two lists in lockstep" comment is gone because the
  two lists are gone. Don't re-add literal `ColumnDefinition`s to either side, and don't revert
  to fixed px.**
  **That single-source claim was FALSE in practice for a while, and the way it failed is worth
  keeping.** `ApplyRoomColumns` skips its work when the resolved set matches `_roomColumns` —
  but that field is SEEDED with `RoomsTableLayout.All`, which is exactly what `Resolve()`
  returns at any comfortable width. So on a wide window the very FIRST call decided nothing had
  changed and returned before building the header's `ColumnDefinitions` and re-indexing the
  header labels. The header silently kept the XAML design-time placeholder — then a stale
  SEVEN-column set including MOD and STATUS, columns that no longer exist — while the rows used
  the real five, so every row rendered ~500px right of its own heading. Two things now prevent
  it: a separate `_roomColumnsApplied` flag, so the first application can never be skipped
  (the sets matching on the first call is the NORMAL case, not an edge case), and the XAML
  placeholder now mirrors `RoomsTableLayout.All` exactly, so even a skipped apply would render
  correctly. The lesson generalises: **a "these two cannot drift" invariant is only real if the
  code that enforces it always runs** — and a guard that compares against a seeded value will
  silently no-op on the path where the seed is already right. `RoomsTableLayout.Resolve(width)` also DROPS columns as the card
  narrows — ping → host → mod, never Room/Players/Action — and `Hidden(resolved)` tells
  `BuildRoomCard` which values to fold into the room's sub-line instead, so nothing is lost at
  any width. `ApplyRoomColumns` (hooked to `RoomsHeaderStrip.SizeChanged`) re-renders **only
  when the resolved SET changes**, not per resize tick, or dragging the window edge rebuilds
  every row per pixel. Pinned by `RoomsTableLayoutTests`.
  **Every cell must stay BOUNDED, and this is not cosmetic.** The header labels (label + sort
  arrow), the mod chip, the ping bars and the status badge were all horizontal StackPanels, so
  their `TextTrimming` never fired and they drew straight over the next column — visible at a
  1355 px window, not just a small one, and much worse in Spanish (`JUGADORES` vs `PLAYERS`,
  `ANFITRIÓN` vs `HOST`). They are now `Grid [*][Auto]` (text in the star column) or wrapped by
  `WrapCell`, and the header grid + each row grid set `ClipToBounds` as a backstop. If you add a
  cell, don't put text straight into a horizontal StackPanel. Left inset is now **30px** (ScrollViewer pad 16 + row
  padding 14; the flat row has no side border) — the header strip (wrapped in a
  subtle `#141C2C` band Border with a bottom `MpDivider` divider) has a `30,7,30,7`
  margin that matches it. **Header columns 0–5 are clickable SORT buttons**
  (`MpColHeaderButton`, label + a `SortArrow*` glyph: `⇅` idle, `↑`/`↓` active),
  wired to `RoomHeader_Click` → toggles asc/desc, sets `_roomsSort`/`_roomsSortAsc`,
  `UpdateSortArrows()`, then `RerenderRoomsFromCache()` (re-orders from
  `_lastBrowserList`, NO network). Sort is applied render-side by `ApplyRoomSort`
  (stable `OrderBy`, `Reverse` for desc; ROOM/MOD/HOST by name, PLAYERS by count,
  STATUS by Waiting<Full<InGame rank, PING is a no-op since your latency is the
  same for every row); `BuildRoomsSignature` stays in SERVER order so the quiet
  5s auto-refresh diff is stable and doesn't lose the chosen sort. ACTION (col 6)
  is a plain centered label, not sortable. A **footer** (`RoomsShowingCount`,
  `MpRoomsShowingCount` = "Showing N rooms") shows the count — **no pagination**
  (the list scrolls). **The layout is ~78/22** — the rooms table col is `3*`, the
  global-chat column is FLEXIBLE (`*` MinWidth 280 / MaxWidth 300) — so the table
  fills ~78%;
  `SyncHeaderScrollbarGutter` (hooked to `RoomsListScroll.ScrollChanged`) bumps the
  header's right margin by `SystemParameters.VerticalScrollBarWidth` when the vbar
  shows so the header tracks the rows. The row shows: a **leading mod-icon disc**
  (the room's mod icon, resolved by `ResolveRoomModIcon` = cached catalog
  `icon.png` → built-in packed icon, cached per mod id and decoded once; **gold ★
  fallback** when the mod ships no resolvable icon), the title (with a purple
  **"Privada" chip** beside it when private, + small muted sub-lines under it: an
  optional "not installed" note and a **live "open for X"** counter — how long the
  room has been open, ticked in place by `RefreshRoomAgeCells` on the ~3 s rooms
  ping timer, registered in `_roomAgeCells`; the open time is parsed from
  `LobbySummary.CreatedAt` via the pure `Services/RoomAgeFormat.cs`
  (`ParseCreatedUtc` handles SQLite's zone-less UTC + ISO; `Compact` →
  "5 min"/"1 h 20 min", `RoomAgeFormatTests`). The lobby window shows the same
  counter in its header meta line (`RenderRoomPanel` appends an "open for X" Run,
  `RefreshLobbyOpenAge` on the ~2.5 s lobby ping timer; the open time is
  `_currentLobbyCreatedUtc`, mirroring `_currentLobbyMaxPlayers` — set to now on
  create / parsed from the joined summary, cleared on leave)), the **MOD chip**
  (own column), the host with
  a **name-colored** monogram circle (`HostMonogramBrush`, hashed palette + white
  initial), players, ping, a status cell, and the **ACTION-column
  action button** whose caption + enabled-ness pick per room in this **priority
  order** (first match wins) — enabled Join / Re-enter use the
  `MpOutlineBlueButton` outline style, the disabled states use
  `MpSecondaryButton` (neutral):
  1. **room we're currently in** (`iAmInThisRoom` = `lobby.Id ==
     _session.CurrentLobbyId`) → **"Re-enter"** (`MpRoomReenter`, ES "Reingresar")
     wired to `OpenLobbyWindow()` (re-opens / Activates the lobby window) — never a
     Join for a room we're already inside;
  2. **our own room we're NOT session-tracked in** (`iAmHost`) → **disabled "Your
     room"** (`MpRoomYours`). This is matched by **host identity** — `lobby.Host.Id
     == me.Id` OR `lobby.Host.DiscordUsername == me.DiscordUsername` (case-
     insensitive) — **not** `CurrentLobbyId`, so it still holds after we closed the
     lobby window but the backend kept the room alive; re-joining your own room
     errors server-side, hence disabled;
  3. **in-game** (`status == "in_game"`) → **disabled "In game"**
     (`MpRoomStatusInGame`) — the room is locked;
  4. **full** (`CurrentPlayers >= MaxPlayers`) → **disabled "Full"** (`MpRoomFull`);
  5. **mod not installed locally** → **disabled "Join"** (`IsEnabled =
     modInstalled`); else → **enabled "Join"** → `JoinRoomButton_Click`.

  Status shows in the STATUS column as a **colored dot + label**
  (`BuildStatusCell(label, RoomStatusKind)`) with FOUR kinds by priority
  **In Game > Full > Private > Waiting**: **In Game** (green `MpStatusInGame`, bold
  label), **Full** (amber `MpStatusFull`), **Private** (purple `MpStatusLocked`,
  string `MpRoomStatusLocked` = "Private"/"Privada", shown for `IsPrivate` rooms
  that aren't in-game/full), **Waiting** (blue `MpStatusWaiting`). **A purple "Privada"
     CHIP next to the room NAME marks every private room ALWAYS** (`BuildRoomCard`
     title row: a `Grid{*, Auto}` with the name in `*` (ellipsizes) + a
     `BuildRoomChip(MpRoomStatusLocked, low-alpha-purple #228B5CF6, MpStatusLocked)`
     in `Auto`) — needed because the purple Private DOT only shows at Waiting rank
     (In Game / Full outrank it), so an in-game/full private room would otherwise
     show no hint it's private. (An earlier 🔒-emoji prefix on the name was rejected —
     it looked ugly; the chip is the accepted form.) **The purple
  Private dot is COSMETIC — the ACTION stays an enabled `Join`** because private
  rooms ARE joinable (the click handler prompts for the password via
  `PasswordPromptDialog`); do NOT turn it into a disabled "Locked" (that would
  break joining private rooms). `StatusRank` is untouched (private sorts at
  Waiting rank — acceptable). **There is no "Watch"/spectate action** (the mockup
  had one; removed on request) — an in-game room shows the disabled "En partida"
  button. The header also carries an
  `Actualizado hace X` timestamp (`RoomsUpdatedText` / `UpdateRoomsUpdatedLabel`,
  ticked by `_roomsPingTimer`), and the empty state is now localized
  (`MpRoomsEmptyTitle` / `MpRoomsEmptyBody` — they used to be hardcoded English).
  All of this keys off the backend
  reporting `status == "in_game"` once the host starts — the rooms browser has no
  other signal that a room you're *not* in has begun (the room WS is per-room, joined
  only from inside), so if in-game rooms never lock, check the backend is flipping
  the lobby status, not the launcher. How these
  captions refresh: `BuildRoomsSignature` (the quiet-diff key) includes status +
  player count + host, so In-game / Full / host changes repaint within ≤5 s while
  browsing. The **viewer-relative** bits (`iAmInThisRoom` / `iAmHost`) are
  deliberately **NOT** in the signature — they don't need to be, because they're
  recomputed on every render and the events that flip them also change the payload
  (create adds your row; join/leave moves a player count; **leave-room additionally
  forces a non-quiet `RefreshRoomsListAsync()`**), so a render happens regardless.
  Don't try to encode "is this my room" into the signature.

- **Presence is ALWAYS-ON while signed in — the global-chat/`/global/ws` socket is
  deliberately NOT gated on tab/window visibility, so a launcher in the background
  (other tab, or minimised to the tray) still shows the user as "connected" to
  everyone (GameRanger-style).** This was the whole point of the "run in background"
  work. `MultiplayerTab.SyncGlobalChat` gates ONLY on `_session.Status == SignedIn`
  (NOT `IsVisible`); `OnVisibleChangedTabGate`'s not-visible branch stops the
  pollers (quota/rooms/radmin) but **does NOT** `CloseGlobalChat()`; and `Attach`
  calls `SyncGlobalChat()` unconditionally so it connects at startup for a cached
  valid session regardless of the active tab. **Load-bearing details:** (1) the 30 s
  ping keep-alive is a background `Task.Delay` loop in `LobbyWebSocket` (NOT
  UI-gated), so a tray socket survives the backend's 90 s idle-kick — don't move the
  ping onto a Dispatcher timer that pauses when hidden. (2) With the socket open in
  the background, `AppendGlobalChatRow` **caps `GlobalChatPanel.Children` to 200
  rows** (ring buffer) — otherwise chat rows accumulate unbounded in a hidden panel
  (slow leak). (3) Only the presence socket is always-on; the pollers stay
  visibility-gated. (4) Presence needs a valid cached JWT to auto-connect at
  startup; an expired token means no presence until re-login. **Backend capacity:**
  `globalChatMaxConnections` was decoupled from `MAX_CONCURRENT_USERS` (60) and
  defaulted to **200** (`env.ts` / `.env.example`), because every running launcher
  now holds a persistent socket — size it to the online installed base, not active
  lobby players. ~15 MB RAM at 150 idle sockets on the 1-core/1GB VM (nginx
  terminates TLS → Node sees plain WS); the full-list presence frame is O(N²) bytes
  but debounced ~1.5 s + event-driven, so it's fine to ~150 and strains the single
  vCPU only past ~300-500 (where you'd switch to delta presence). Don't re-gate the
  presence socket on visibility, and don't drop the 200-row render cap.

- **Global chat is a process-wide WebSocket room — separate from the per-lobby
  chat, and the launcher's first real server-push channel.** The Multiplayer
  tab's Rooms view is now TWO columns: active rooms (left card) + a persistent
  **"Chat global"** panel (right card; `GlobalChat*` x:Names in
  `MultiplayerTab.xaml` — a merged header `Chat global · ● N conectado`
  (`UpdateGlobalPresence`; the old separate `Canal general` label is gone),
  message list, composer). The client renders each message as a subtle rounded
  **bubble** and **dedupes the avatar/name for consecutive messages from the same
  author** (`_lastGlobalChatAuthor`, reset whenever the panel clears); Send is a
  compact paper-plane icon button (caption on its ToolTip). **The header stamp
  shows the DATE, not just the time**, so old messages don't read as recent (and
  the midnight wrap-around stops looking out of order): today → `HH:mm`, yesterday
  → `Ayer HH:mm`, older → `15 jul HH:mm` (with the year if it's a different one),
  via the pure/WPF-free **`Services/ChatTimeFormat.cs`** (unit-tested
  `ChatTimeFormatTests`; month names follow `Strings.Language`, NOT the OS locale),
  with the full date+time on the timestamp's hover tooltip. A message on a NEW day
  **forces a fresh dated header even for the same author** (`_lastGlobalChatDate`,
  reset with `_lastGlobalChatAuthor` at both panel-clear sites) so a same-author
  run crossing midnight can't hide the date. `AppendGlobalChatRow` is the single
  choke point for both live messages and the history replay, so this covers both.
  That's all cosmetic — the WS protocol + anti-spam below are untouched. Server side it's a single `GlobalChatRoom` **singleton** on
  the Node backend (`src/global/GlobalChatRoom.ts`, mounted at `/global/ws` in
  `index.ts`, held on `AppContext.globalChat`) — modelled on `LobbyRoom`'s
  broadcast / idle-kick / throttle but with **almost no DB**: membership IS
  "holds a valid JWT" (auth on the first `hello`), the **only** DB touch is one
  indexed `users.avatar_url` read per *connection* (cached on the
  `AttachedSocket`, not per message) so chat lines can carry the real Discord
  avatar, and history is a **capped in-memory ring** (lost on restart, by
  design). Wire protocol: client → `hello {token}` / `chat {body}` / `ping`;
  server → `global_state {history, online}` / `chat {line}` / `presence
  {online}` / `pong` / `error` — each `line` is
  `{id, userId, login, avatarUrl, body, at}`, and the client renders `avatarUrl`
  as a circular photo with the login **monogram as the fallback** when it's null
  or fails to load (the chat column is FLEXIBLE — `*` MinWidth 280 / MaxWidth 300,
  ~22% of a ~78/22 split with the rooms table `3*`; see the rooms-table bullet).
  Client side it
  **reuses the generic `LobbyWebSocket`** (SessionToken hello,
  `BuildWsUri(Api.BaseUri, "global/ws")`), but the socket is **owned by
  `MultiplayerTab`, NOT `MultiplayerSession`** (unlike `RoomSocket`) because its
  lifetime is gated on *tab-visible + signed-in*, not on being in a lobby — see
  `SyncGlobalChat` / `OpenGlobalChat` / `CloseGlobalChat` (open from
  `StartQuotaPolling` + `OnSessionStateChanged`; close from the
  `OnVisibleChangedTabGate` hide branch + the session swap in `Attach`). A user
  can be in the global chat AND a lobby at once (two sockets). The new
  `MultiplayerSession.SessionToken` getter exposes the JWT for the hello.
  **Why it's cheap on the 1 GB VM (the feasibility question that gated this
  build):** WS frames bypass the per-request daily budget (only the upgrade
  counts, once), and everything is bounded — `globalChatMaxConnections` (default
  = `maxConcurrentUsers` = 60), **one socket per user** (a second `hello` closes
  the first), in-memory `globalChatHistory` (100), per-user `globalChatMsgsPerMin`
  (20) + 500-char cap (all in `env.ts` / `.env.example`). Added RAM is
  single-digit MB; the binding limit stays the 60-user budget, not chat. **Don't
  switch the global chat to REST polling** — 60 users polling would blow the
  100k/day budget many times over, which is the whole reason it's WS.

- **Global chat anti-spam: slow-mode + auto-timeout (server-side, in
  `GlobalChatRoom.handleChat`).** On top of the 20/min cap, two more layers throttle
  abuse, all config-knobbed: (1) **slow mode** — a minimum gap between messages
  (`globalChatMinIntervalMs`, 1500 ms); a too-fast message is dropped, not the
  connection. (2) **auto-timeout** — slow-mode / rate trips are counted as
  *strikes* in a rolling minute (`registerViolation`); cross
  `globalChatTimeoutStrikes` (5) and the user is auto-muted for
  `globalChatTimeoutMs` (30 s), during which every message is dropped with the
  remaining seconds. The mute lives in a room-level `mutedUntilByUser` map keyed
  by **user id** (not socket), so reconnecting can't shed an active timeout
  (strikes stay per-socket — fine, the mute is the sticky part). No human moderator and no admin/role concept exists — these
  are purely automatic (manual mute/ban would need a new admin layer the backend
  doesn't have). The server emits distinct `error` codes (`chat_slow_mode` /
  `chat_rate_limited` / `chat_muted` / `chat_timeout` / `chat_too_long`); the
  launcher maps each to a localized hint shown above the composer
  (`GlobalChatNotice`, `ShowGlobalChatNoticeFor`, cleared on the next keystroke) —
  server error *messages* stay English, the client localizes by code. The check
  order in `handleChat` is **muted → length → slow-mode → per-minute**, and a
  slow-mode drop bails *before* incrementing the per-minute counter so it isn't
  double-penalized.

- **Auto-start requires a FULL room, not just "everyone present is ready".**
  `MaybeAutoStartOnAllReady` (`MultiplayerTab`) fires `BeginHostStart` — the SHARED
  host-start flow the manual Start button also calls — when EVERY `_roomMembers`
  entry is `.Ready` (host included), gated **host-only** (one start),
  Lobby-phase-only, `_roomMembers.Count >= 2` (no solo auto-launch), **and the room
  is FULL**, once per ready-up via `_autoStartInFlight` (reset in `ExitInGamePhase`).
  Called from `HandleMemberReady` / `HandleRoomState` / `ReadyButton_Click`. The
  manual "Start game" button still works and never checked ready state, so it stays
  the host's deliberate early/force-start.
  **The full-room gate is a bug fix, don't drop it:** "everyone present is ready"
  launched a 6-slot room the moment the 3 players in it readied up, stranding the
  other 3 with no way for the host to wait. Capacity comes from
  `TryGetCurrentLobbyMaxPlayers` — the SAME resolution behind the "3 / 6" stat and
  the roster's open-slot rows (`_lastBrowserList` → the `_currentLobbyMaxPlayers`
  stash, since the host is absent from the browser snapshot) — so the gate can never
  contradict what the host is looking at. **An UNKNOWN capacity must NOT auto-start**
  (`!TryGetCurrentLobbyMaxPlayers(out var max) || max <= 0` returns): without that
  guard a max of 0 makes `Count >= max` trivially true and it would fire MORE eagerly
  than the bug it replaces — the existing `Count < 2` guard doesn't cover it.

- **Host migration + abort-grace window — the lobby outlives its creator, and
  aborting a launched match is time-boxed.** Two coupled multiplayer rules added
  together; backend = `wol-launcher-lobby-node` (`src/lobbies/LobbyRoom.ts`,
  `rest.ts`), launcher = this repo — **they ship and deploy together** (new WS
  frames). Old clients ignore the new frames; a new launcher tolerates an old
  backend (degrades to no migration).
  **(a) Host migration (GameRanger-style).** When the host leaves, the backend no
  longer closes the lobby — it hands it to the next member by **JOIN ORDER ∩ LIVE
  (attached) socket** and only closes when nobody live remains. BOTH leave paths
  do it: REST `/leave` and the abrupt `ws.on('close')` (the crash/alt-F4 path that
  never hits `/leave`). CRITICAL: picking by `lobby_members.joined_at` ALONE would
  migrate to a **ghost** — abrupt closes don't sync the DB, so the table keeps rows
  for crashed players; you MUST intersect with the live `attached` set. The close
  path now also does the bookkeeping `/leave` used to (delete the leaver's row +
  recompute `current_players`) for ANYONE — a leftover row blocks that user's "1
  active lobby" guard and leaves `current_players` stuck (lobby reads full).
  `reassignHost` commits the DB `host_user_id` BEFORE broadcasting `host_changed`
  and is idempotent (guards `hostUserId === leavingUserId`) so the two paths racing
  is safe. Launcher: `HandleHostChanged` updates `_roomHostUserId` /
  `_isHostInCurrentRoom`, `RenderRoomPanel` (Lobby phase) hands the new host the
  Start button, chat shows `MpChatHostChanged`. Pinned by
  `scripts/test-host-migration.ts` (3-socket, abrupt-close, asserts no ghost).
  **(b) Abort-grace window.** Cancelling a match is **no longer host-only**: ANY
  member can abort for EVERYONE, but ONLY within the grace window — the countdown
  (`Starting`) plus **60 s after launch**. Server-authoritative: `handleCancelGame`
  checks `Date.now() - startedAtMs < COUNTDOWN_MS + 60000` (`startedAtMs` is
  in-memory from `handleStart`, NOT the DB `started_at`, to compare on one clock
  without date parsing); past it → `grace_window_closed`. Launcher mirrors the UX
  off `WithinAbortWindow` (local 60 s from `_matchTimerStartTicks`): the in-game
  button flips `MpInGameAbort` ("Abort match", any member) ↔ `MpInGameLeave`
  ("Leave", just you) each 1 s tick, and `EndMatchAsync(reason, sendCancel)` only
  sends `cancel_game` when within the window. Rationale (vs Voobly/GameRanger):
  the room migrates and the match continues for those who stay, so a host who is
  losing must NOT be able to kill everyone's game — abort is time-boxed to the
  start (a bad/desynced launch). To restrict abort to host-only later, it's a
  one-line guard in `handleCancelGame`.
  **(b2) Natural game-exit resets the room — `game_ended` (host, NO grace window).**
  The grace-gated `cancel_game` only reverts `in_game → open` inside 65 s AND only
  when the user aborts; when the HOST's game process just EXITS, nothing told the
  backend, so the room (and the Discord embed) stayed stuck **"In game"** forever —
  the reported bug ("started a match, closed it, Discord still In game"). Fix: on
  `OnGameExitedAsync` the HOST sends `game_ended` **only when the match wasn't
  reported+closed** (`TryReportMatchAsync` now returns a bool — a real ≥2-player /
  ≥3-min match still REPORTS + CLOSES the room via `POST /matches` as before; a
  solo/short/failed report falls through). `LobbyRoom.handleGameEnded` (host-only,
  idempotent on `startedAtMs`, **no grace check** — a host's own game ending is
  always legitimate, and the launcher only sends it on a real process exit, not a
  spammable button) sets `status='open'` + `startedAtMs=null` and broadcasts a
  `game_cancelled {reason:'ended'}` **excluding the sender** (the host already left
  the in-game phase locally) → `reflectToDiscord` maps it to status `open` →
  Discord "Waiting" + `refreshPlayers`, and any peer still in-game returns to the
  lobby (chat line `MpChatHostEndedMatch`). No game-exit process watch is needed
  (the dashboard launch is fire-and-forget). Forward-compatible: an old backend
  answers `game_ended` with `unknown_type` (swallowed → room stays as today until
  deploy). Deploy: `git pull` + `systemctl restart wol-lobby`.
  **(c) Kick.** The host can expel a member: `kick { user_id }` (host-only,
  validated in `LobbyRoom.handleKick`) sends the target a `kicked` frame then
  closes its socket — the existing `ws.on('close')` cleanup drops it from the
  roster for everyone (no new removal logic). **Simple kick, no ban list**: the
  target may re-join (to block re-join, add a per-room `Set<userId>` checked in
  `rest.ts` join). Launcher: a host-only ✕ button per roster row (`BuildMemberRow`,
  hidden on the host's own row, tracks `_isHostInCurrentRoom` so host migration
  keeps it correct) → confirm via `MpAlertOverlay` → `SendKickAsync`; the kicked
  client's `HandleKicked` closes the lobby window (disposing the socket, so no
  reconnect loop) and shows an `MpKicked*` notice. Pinned by the kick case in
  `scripts/test-host-migration.ts`.

- **Multiplayer alerts are themed in-window cards, NOT `MessageBox` — via
  the `MpAlertOverlay` helper.** `Controls/MpAlertOverlay.cs` is a static
  helper that injects a scrim + a centred card (MpSurface fill, two-tone
  rim, ⚠/ℹ glyph, title + body, `MpDangerButton`/`MpPrimaryButton` primary +
  `MpSecondaryButton` cancel) as the **last child of a host `Grid`**, and
  returns `Task<bool>` (true = primary/confirm/ack, false = cancel/Esc/
  scrim-click; a notice is OK-only and always resolves true). Two entry
  points: `ConfirmAsync` (two buttons) and `NoticeAsync` (one). It replaced
  **all** the multiplayer `MessageBox.Show` calls — the cancel-game confirm
  (the one from the screenshot, host = "cancel for everyone" danger / joiner
  = "leave the game"), hosted in `_lobbyWindow.LobbyRootGrid`; and the
  join/create/fingerprint/mod-mismatch/Radmin error notices, hosted in the
  tab's `TabRootGrid`. Both host grids are named in XAML for this. **The ONE
  remaining `MessageBox` is deliberate:** `ConfirmCloseDuringMatchAsync` runs
  synchronously from `MainWindow.OnClosing` via `task.Wait(...)`, so an
  in-window async overlay would deadlock the UI thread — it must stay a
  blocking modal. Don't "finish the job" by converting it. All alert strings
  are EN/ES `MpAlert*` / `MpConfirm*` / `MpNotice*` keys in `Strings.cs`.
  **Gotcha that already bit once:** the card builds its text purely from
  `Strings.Get(key)`, and a key that's MISSING from the `Strings.Table`
  renders as **the raw key** ("MpConfirmCancelHostTitle" shown literally in
  the card) — `Strings.Get` returns the key itself as its visible
  not-found signal, and the C# compiler can't catch it because the keys are
  plain string literals, so **the build stays green while the UI shows the
  key names.** When you add an `MpAlertOverlay` call with a new key, add the
  matching EN/ES entry to `Strings.cs` in the SAME change and actually run
  the app (or grep that every `Mp{Alert,Confirm,Notice}*` key used in
  `MultiplayerTab.xaml.cs` exists in `Strings.cs`) — a clean build is NOT
  proof the strings landed.

- **A match that ENDS has its own phase — `MatchPhase.Result` — and without it the lobby
  window was a zombie.** A reported match closes the room on the backend, which shuts the
  socket with `4007 match_reported` (`ctx.rooms.close` in `matches/rest.ts`). Nothing in
  the launcher reacted to that: `LobbyWebSocket` treats a server close like a dropped
  connection and re-enters its backoff loop (1→30 s, forever), so the window survived with
  a dead chat, live buttons and a socket pointing at a room that no longer exists. That
  zombie WAS the de-facto end-of-match state; the phase makes it deliberate.
  **Three things happen on entry and each is load-bearing:** (1) **`_roomMatchLive` is
  cleared** — on the reported path nothing else does it, because the `game_ended` branch
  that normally would is skipped precisely *because* the report already closed the room;
  left set, `RoomMatchState.WarnOnLeave` tells the player they "will not be able to come
  back" while they are looking at their own result. (2) **`LobbyWebSocket.StopReconnect()`
  cancels the retries but KEEPS the object** — disposing it, or nulling
  `MultiplayerSession.RoomSocket`, raises the state change that runs
  `RenderRoomsTab`'s else-branch → `CloseLobbyWindow()`, which would tear down the very
  window the card lives in. (3) `SuppressLeaveConfirm()`, since there is no longer a room
  to warn about leaving. **The trigger is the 4007 itself**, handled in
  `OnRoomDisconnected`, so host and guest reach the phase through one line; it is gated on
  having been in a match because **4007 is also the kick code** and a kick has already
  closed the window via its own frame. The host ALSO enters from its own POST returning —
  both signals arrive and whichever is first wins, so entering at both points makes the
  host's card deterministic rather than a race. `GameRestartedSince()` stays
  `_matchPhase == InGame`, so the Result phase correctly reads as "no game running".

- **The result card's numbers come from the POST for the host and from the HISTORY for
  everyone else — and `TryReportMatchAsync` must keep returning its boolean unchanged.**
  `POST /matches` answers with `ReportMatchResponse.RatingChanges`, a `rating_before` /
  `rating_after` per participant, which the launcher used to discard; capturing it gives
  the host their delta and the opponent's new rating for zero extra requests. It is
  returned as a `record` whose `ClosedRoom` flag has the OLD boolean's exact meaning,
  because `OnGameExitedAsync` decides from it whether to send `game_ended` — that
  semantic must not drift. A **guest gets no frame carrying the result**, so
  `ResolveGuestResultAsync` polls `GET /matches/history/:userId` and matches by mod +
  start time (±120 s) via the pure `MatchHistoryMatcher` — never "the newest row", which
  would be a different match of theirs. Three attempts (0 / 6 / 15 s) then a terminal
  "check History" line; **never a timer beyond that** (20/min · 500/day per IP, shared
  behind NAT or a Radmin network), and skipped outright when the match was not reportable
  anyway (solo / under three minutes), where there is no row to find.
  **`MatchOutcomeView` holds the three refusals** and the card only paints them: a 0.5 is
  `NoResult` and **never a draw** (it is what the backend stores when the outcome could
  not be read, which is most rows); an unknown rating yields a **null delta, not "+0"**;
  and `PlayerStanding.WinPercent` returning null shows an em dash, **never 0 %**.
  **"Rematch" is deliberately not wired**: it must complete `LeaveCurrentLobbyAsync` on
  the closed room BEFORE creating the next one or it collides with the backend's "one
  active lobby" guard, and getting that sequence wrong strands the player in neither room.

- **The in-match RECORDING cell says "requested", never "active" — the one deliberate
  deviation from the design reference.** The handoff asks for a green "activa" read from
  `GameRecordingPlan`. That plan answers a narrower question (should the launcher write
  `optionrecordgame` into this mod's profile), and it is MEASURED above that the profile
  setting does not drive recording in multiplayer: AoE3's per-match "Record Game" box
  does, it comes up unticked every match, and both ways of setting it automatically were
  tried and failed. A green "active" would therefore be a claim the launcher cannot
  support in the one place where being wrong costs the player their rating — they read
  "active", skip the box, and the match counts for nobody. `RecordingIndicator.Classify`
  (pure, tested) returns Requested / Off / Unknown; the cell keeps the reference's
  position, size and colours, and only the wording drops from a statement about the game
  to one about the launcher's own setting, with a tooltip naming the real checkbox.

- **The lobby's "BEFORE YOU START" checklist replaced the record-reminder band, but
  `LauncherConfig.GameRecordingReminderMuted` is NOT gone.** The two-item checklist cannot
  be silenced — it costs two lines instead of seven, which is what made the old band worth
  silencing — but that config field still gates the launch-time chat line and has its own
  Settings checkbox, so deleting it breaks `LauncherSettingsDialog`. **The first item's tick
  is honest but not verified per member**: everyone present passed `POST /lobbies/:id/join`,
  which rejects a mismatched `mod_combined_hash`, so the claim follows from their being
  there; the room-state frame carries no per-member hash, so a truly verified tick needs a
  backend change. **The second never ticks, by design.**

- **Removing the roster's health dot would have broken the live ping SILENTLY.**
  `RefreshRosterHealthDots` found its target by walking each row for an `Ellipse` with a
  string `Tag`; the redesign states the link in words on the row's second line and drops
  the dot, so that walk would have found nothing, thrown nothing, and left every ping
  frozen at whatever it read when the row was built. It is now `RefreshRosterLiveCells`
  and the `Tag` sits on the second line's `TextBlock`. **Any future change to the roster's
  visual shape has to move that Tag with it** — the update is by structure, not by name,
  which is exactly why the failure is quiet. The rating segment of that line is **omitted
  entirely when unknown**, the same refusal `PlayerStanding` makes: the 1500 the server
  hands new players must never be rendered as if it were earned.

- **Backend gaps the multiplayer redesign is waiting on** (documented, never faked):
  per-room ping needs `radmin_ip` on `GET /lobbies` — without it the PING column shows
  YOUR latency, identical on every row, which is why sorting by it is a no-op; per-member
  ELO in the lobby roster needs `rating` on the room-state member object (or a batch
  `/matches/elo?ids=`), since 8 single-user calls do not fit in 20/min; the rooms view's
  PEAK HOURS and RANKING cards need an activity-by-hour endpoint and a top-N that do not
  exist; a `match_reported` frame carrying the rating changes would replace the guest's
  four polls with zero; and the REPLAY cell reads "not uploaded" because
  `ReplayUploadService.UploadAsync` still has no live caller.
