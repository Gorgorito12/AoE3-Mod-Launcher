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

- **"The Create button gets consumed" was the HOVER, not the busy state — and it took three
  reports because the first two fixes chased the wrong state.** The real cause:
  `MpFooterPrimary` is `BasedOn` `MpFooterGhost`, so it inherited the ghost's
  `ControlTemplate`, whose `IsMouseOver` trigger painted the template Border **by
  `TargetName`** with `MpRowHighlight`. A trigger that targets a template element by name
  is unreachable from a derived style (see the general rule in `CLAUDE.md`), so pointing at
  the filled blue Create button replaced `#2F7FE0` with `#16263E` — the primary action went
  dark at the exact instant the user was about to click it. Proven from screenshot pixels:
  same button, `#2F7FE0` with the pointer away and `#16263E` with it over. Hover now lives
  in each Style's own triggers, on the BUTTON's `Background`; the primary uses
  `MpBlueHover` / `MpBluePressed`, the same pair `MpRoomActionPrimary` and `MpPrimaryButton`
  already use for an `MpAction` fill. Pinned by
  `DialogXamlTests.CreateButton_DoesNotInheritTheGhostButtonsHover`.
- **While a control is BUSY, say it with the WORD, not with opacity — and give the
  dialog somewhere to scroll.** Separate from the above, and a real improvement even though
  it was not what was being reported. `CreateLobbyDialog`'s footer disables BOTH buttons
  while the room is being created. That treatment went `Opacity 0.45` (both dissolved into
  the navy footer), then a two-step drop to `MpTextFaint` plus a rim at 9% alpha. The moment
  they go inert is the moment the user has pressed Create and is waiting to learn whether
  the click registered, so receding is the opposite of the message. Now: **Create keeps its
  fill, its edge AND white text** (its caption already changed to `MpCreateDialogCreating`,
  "Creando…" — that IS the state), and **Cancel keeps its `MpRimStrong` rim** and steps its
  text down exactly ONE notch, to `MpTextMuted`. Don't reintroduce a blanket `Opacity` on a
  busy control.
  **Paired with it:** the dialog is `SizeToContent="Height"` and this form only grows (the
  Record Game notice is permanent; the Radmin warning, password row, copy row and error
  line stack on top). With no ceiling it sizes itself taller than the screen and puts the
  footer past the bottom edge — the same complaint by a different mechanism. The body row
  is `*` with a `ScrollViewer` in it and the constructor clamps `MaxHeight` from
  `SystemParameters.WorkArea`, so the form scrolls instead. Keep all three: an `Auto` body
  row, a missing `MaxHeight`, or a removed `ScrollViewer` each re-opens it on its own.

  The launcher is
  the *meta layer* (sign-in, lobbies, chat, mod-hash gating) over a **self-hosted
  Node/Fastify backend at `wol-lobby.duckdns.org`** — **not** a Cloudflare
  Worker. Sign-in is **Discord OAuth** (a state flow shaped like device flow),
  **not** GitHub, yielding a JWT cached in `launcher-config.json`. **Match history
  IS wired, and so is the ELO: Glicko-2 lives in the backend
  (`src/elo/glicko2.ts`) and the launcher shows a rating in five places — the
  title-bar chip, the Profile tab, every roster line, the end-of-match card and
  the ladder card. Replay upload (`UploadAsync`) is still scaffolded with no live
  caller.** Authoritative source:
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

- **"Help connecting" in the Rooms toolbar OPENS the Radmin assistant, and it is the
  only door to it once Radmin works** — the other one is "Show steps" INSIDE the red
  banner, and that banner collapses exactly when everything is fine. Retiring the
  permanent banner silently retired the assistant with it. `RadminHelpButton_Click`
  is the caller.
  **Three attempts, and the progression is the lesson — do not restart it.** The door
  was first the header's connection capsule (an action with no sign it was one:
  its entire affordance was a hand cursor and a rim shift between #22303E, 1.39:1, and
  #3A4B60, 2.09:1). Then a "?" button beside it — a sign with no hint of what it
  opened; reported back as "no se sabe que eso tiene una guía", and fairly, because the
  only way to find out was to hover, which nobody does for a thing they do not know
  exists. **A symbol says help exists but never says about what; only a word does
  both.** The "?" survives as a PREFIX to the label, matching its neighbours
  ("↻  Actualizar", "+  Crear sala") — a plain Unicode mark, not an emoji (banned in
  labels) and not an icon font (this row deliberately avoids pulling one).
  **In the Multiplayer tab, not the header**: Radmin only matters here, this is where
  someone goes when they cannot get online, and it costs the header nothing — which
  was the point of the redesign. The trade is that it is unreachable from Library and
  Workshop, which is correct rather than merely acceptable.
  **It follows the same Mode gate as "Show steps"** — hidden when
  `RadminAssistantMode == "Never"`, because that setting's own hint says the assistant
  is disabled and a visible way in would make it a lie. The header "?" ignored the
  mode, which was one more sign it was in the wrong place.
  **`OpenRadminAssistantWindow()` is gone.** It was public "so MainWindow could trigger
  it in the future", sat with zero callers for months, and was what made the missing
  door hard to spot. The handler calls `ShowRadminAssistant()` directly now; if an
  external caller ever needs one, add it back with that caller, not before.
  **Verified by invoking the button and watching the window list** — the assistant
  opens and stays open past 11 s with Radmin green, which is the `autoOpened` guard
  doing its job. Two measurement traps cost real time here and will again:
  **UI Automation's `RootElement` children query does NOT enumerate the assistant**
  (owned + `ShowInTaskbar=false`), so it reports the window as absent and looks exactly
  like the auto-close bug it is meant to disprove — sweep with Win32 `EnumWindows`
  instead. And **the Segoe MDL2 literals in `CopyNetworkBtn_Click` are real Private Use
  Area characters that print as empty strings** in terminals and greps; they read as
  `CopyBtnGlyph.Text = ""` in both branches and look like a lost-literal bug. They are
  `\ue73e` and `\ue8c8` and the code is correct — check `repr()` before "fixing" it.

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
  which the ROOMS-TOOLBAR "Help connecting" BUTTON now calls — see the next bullet)
  can summon it at ANY stage: with the checklist already green, `Refresh()`'s first tick
  (`_lastStage` starts at `-1`, so it always runs once) saw `InAoE3Network` and slammed
  the window shut ~1.2 s later. That's not just annoying — **once everything is green
  that window offers only the copy-network-name button and the "Open Radmin"
  shortcut** (this used to say the copy button was the only one; it is short by one), so the auto-close
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
  A = slot that LOST · B = NOT UNDERSTOOD, never use it · C = number of humans
  ```

  **A is the loser, and the rival readings die on one case:** a game the recorder WON
  reports A = the opponent's slot, so A is neither the winner nor the recorder. Confirmed
  on five games whose result was known independently — three skirmishes (two resigned,
  one won) and two real 1v1s between humans. The 1v1s were **predicted before the result
  was known** and then confirmed by the player who lost them, which is what separates
  this from fitting the data afterwards.

  **B was read as "the slot that RECORDED the file". That was WRONG, and it cost real
  players their rating for as long as the feature has existed. Never use B to identify
  anybody.** The evidence for it — "two files from the same player point at him in both,
  across different slots" — could not have decided the question: with ONE player on ONE
  machine, *the one who recorded it* and *the one who ended it* are the same person. Three
  multiplayer matches later captured from **both** players settle it:

  - the two players' copies of one match are **byte-identical for the last 64 bytes**, so
    nothing in there can name which machine wrote one;
  - in all six copies **B equals the loser**;
  - across both players' complete logs, **every confident reading ever recorded was
    `0,0`** — three of them, not one `1,0`. The system had never produced a win.

  What fits all eight observations is *the slot that ENDED the game*: the human who
  destroyed the AI in the singleplayer fixtures, the player who resigned in the
  multiplayer ones. **That is a hypothesis, and nothing depends on it.** The local
  player's slot comes from `ReplayParserService.FindPlayerSlot` — his AoE3 profile name,
  the one thing in the file that differs between the two players.

  **What it cost, concretely.** `LooksLikeThisMatch` gated on `recorderSlot == host.Slot`
  and `HostResultFrom` was fed the same field as the host's slot. In multiplayer that is
  the loser, so the gate passed only for whoever LOST, and the winner's own recording was
  thrown away with `is not this match`. **A match could be rated only when the HOST LOST**
  — roughly half of all 1v1s, silently. Of the three matches above, one had no trailer at
  all, one was hosted by the loser and rated, and the third was hosted by the winner and
  lost its rating to this.

  **Two of seven recordings have no trailer at all** — games that ended abnormally
  (a disconnect, a killed process). That is why `ReadOutcome` checks for the signature
  instead of assuming it, and why it returns a two-value confidence rather than a bool:
  a missing answer must not be readable as "it was a draw".

  **A third real sample, measured from a reported incident, refines that — and it is NOT
  "no trailer".** An 18-minute 1v1 (`ESOC_Iowa`, seed 29359) ends with the signature
  PRESENT but only **5 of the outcome block's 12 bytes** written: the last 32 bytes are
  `02 00 00 00 | 89 00 00 00 | 00x11 | FFx8 | 00 01 00 00 00`, against a healthy file's
  `02 00 00 00 | 81 00 00 00 | 00x12 | FFx8 | A B C`. The marker before the zeros changes
  from **`81` to `89`**, so the game reached the end by a different path. The container is
  intact (`declared == actual`), so this is not disk corruption — AoE3 wrote a shorter tail.
  `ReadOutcome` rejects it correctly, because it checks at exactly `len-32`.

  **Do NOT make the signature search tolerant.** The pattern `00x12 FFx8` occurs **3,773
  times** inside that one file — it is ordinary data in the command stream. The only thing
  that makes an occurrence the trailer is being at the very end; a parser that hunted for it
  would find thousands of false positives.

  **A THIRD shape, measured from a later report, and it is neither of the two above: the game
  wrote NO closing block at all.** An ESOC_Baja California 1v1 whose container is perfectly
  intact (declared size == actual, 17,609,494 bytes — so AoE3 finished writing the file) simply
  runs out of command-stream data and ends `FF x12`, four zero bytes, then four floats
  (-1, -1, -1, 1). The healthy closing block — `02 00 00 00 | 81 00 00 00 | 00x11 | FFx8 | A B C`
  — is nowhere near the end, and neither is Iowa's truncated `89` variant. `ReadOutcome` rejects
  it correctly, at exactly `len-32`.

  **This is the case that gives the tolerance rule its number.** That same file contains the
  `00x12 FFx8` pattern **4,253 times**, and the last occurrence sits 44 bytes from the end — close
  enough that a "search backwards a little" parser would find it and read `A = 0xFFFFFFFF` as a
  slot. So the temptation is concrete, not hypothetical, and the answer is still no.

  What ends a game this way is not established: the container is intact, so it is not corruption
  or a truncated write. Don't guess it from one sample.

  **A FOURTH shape, and this one is not damage at all — the block is PERFECT and the game simply
  wrote a byte after it.** Measured on the first match ever captured from BOTH machines (WoL
  1.2.0e, `ESOC_Fertile Crescent`, seed 13911). The two recordings carry the SAME outcome block —
  marker `81`, `A=2`, `B=2`, `C=2` — and the LOSER's copy has one extra trailing byte, putting its
  signature **33** bytes from the end against the winner's 32. `ReadOutcome` checked at exactly
  `len-32`, so it answered `Ambiguous` for a recording that says plainly who lost. Confirmed
  against the host's own reading of the same game: `Confident 1,0`.

  That is half the evidence for every match being thrown away by one byte — the loser's copy
  matters most, because he is the one who usually leaves first and whose confirmation is the
  cross-check the ladder leans on.

  **So `ReadOutcome` scans the last `MaxTrailingSlack` (512) bytes, nearest-to-the-end first, and
  VALIDATES each candidate instead of stopping at the first.** This started as `len-32` only, then
  8 bytes of slack for the loser's extra byte above — and 8 was still losing one competitive match
  in five.

  **The measurement that replaced the guess, prompted by a player whose 21-minute ranked game did
  not count.** His recording's block sat **135 bytes** from the end, reading `A=1 B=1 C=2` — a
  clean verdict naming him the loser. Over 20 readable 1v1 recordings the old bound decided
  **16**; the four it gave up on had valid blocks at **78, 79, 135 and 195**. The new rule decides
  **20 of 20 and changes none of the 16** — measured, not argued.

  **Widening alone would NOT have fixed it, and that is the half worth remembering.** In that same
  file the signature NEAREST the end is 12 bytes out and is rubbish (`A = 0xFFFFFFFF`, the Baja
  California shape all over again). `TrailerStart` returned only the nearest and `ReadOutcome` gave
  up when it failed, so the real block 123 bytes further back was never examined. Enumerating
  candidates is the other half.

  **What makes a window this wide safe is the validation, not the width — which is what the old
  version of this paragraph already said while the constant said otherwise.** Within the last 512
  bytes of those 20 files there are **26 candidates; exactly 20 pass, one per file, never two**,
  and the 6 rejected are unmistakable (`A = -1` with `C = 3212836864`, the float `-1.0` repeated).
  Two independent conditions do that work: **A must name a slot the header holds**, and **C should
  equal the human count**. C is a PREFERENCE, never a refusal — the two test fixtures are real
  skirmishes whose blocks say `C = 1` and the tests relabel their AI as human, so requiring it
  would reject genuine files over a doctored header. It settles the only case a wide window
  introduces: more than one candidate naming a real slot. **Do not raise 512 without repeating the
  measurement** — accidental-signature density is what bounds this and it grows with the window.

  **Never do this by trimming trailing zeros instead**: the block's last field is `02 00 00 00`, so
  it ENDS in three zeros and trimming eats the payload. Anchor on the signature, never on the end
  of the file. Pinned by `AGarbageSignatureNearerTheEndDoesNotHideTheRealBlock` (the reported
  shape), `ASignatureBeyondTheWindowIsOutOfReach` (the bound still exists),
  `TheCoherentBlockWinsOverTheNearerOne` (C beating proximity) and `AnExactTrailerIsPreferred…` (nearest still wins among the valid) — the rejections are the point.

  **And do not guess from a partial block.** Those 5 bytes read two ways: `00 01 00 00` =
  256 (no such slot), or `01 00 00 00` = slot 1 — the recorder, whose player states he WON
  that match. Guessing would have taken ~160 points from the winner.

  **`SignaturePresent == false` is how the caller tells the two apart.** It used to be
  carried as `RecorderSlot == -1`, which worked only while the local slot also came from
  the trailer; once the slot started coming from the player's NAME that sentinel became
  unreachable, so the flag is now explicit and separate from `Confidence`. That
  distinction is what feeds
  `LocalReadFailure.RecordingNoOutcome`, whose advice — leave the match to the main menu
  before closing AoE3 — is actionable where the generic ambiguous message has none.

  **That `loser == recorder` note used to live here as a "weak signal, n=3", with the guess
  that the loser's machine writes a complete trailer and the winner's often does not. Both
  halves are now answered, and the guess is REFUTED.** The `loser == recorder` part was
  never a signal at all — it is just B being the loser, restated. And a later incident
  captured from both sides shows **the trailer's presence is a property of the MATCH, not
  of the machine**: the `ESOC_Iowa` game above is missing its trailer in BOTH players'
  copies, identically, while the winner of the other two matches has complete trailers in
  both of his. So a winner's machine writes the trailer perfectly well.

  What survives is only that a conceded defeat is easy to read on the conceder's machine —
  which still makes the backend's "a liar can only give points away" rule sound, but is no
  longer a claim about who *can* read a recording.

  **Confident requires all three:** the exact signature, an A naming a slot the header
  has, and exactly two players. Past a 1v1 "X lost" doesn't name a winner on its own — the
  others may have lost too and nothing records the order. **The room state DOES identify every
  player now** (see the identity-bridge paragraph above), so what is left before a team game
  could be Confident is one measured fact: whether a team recording writes an outcome block at
  all. **Ambiguous must be reported as a draw**: this feeds a rating, and an invented winner
  takes points from someone with nothing on screen to explain it.

  **What is actually known about team recordings, measured over 33 unique files.** One human
  (skirmish): 4 of 4 carry a block. Two humans (1v1): **21 of 28**. Four humans (2v2): **0 of
  1** — and that single 2v2 is not evidence of anything, because its tail is byte-for-byte the
  same shape as a 1v1 that ended abnormally. **The "a team game writes a LONGER block"
  hypothesis is refuted**; don''t re-derive it.

  **READ THE ABOVE WITH THE SLACK FIX IN MIND — the "21 of 28" was largely OUR bug, not the
  game's.** Those counts were taken with `MaxTrailingSlack = 8`. Re-measured over 20 readable
  1v1s with the 512-byte scan, **20 of 20 carry a usable block**: the four that had looked
  blockless had one at 78, 79, 135 and 195 bytes. So "a quarter of 1v1s have no block" was the
  parser giving up, and the sentence that used that figure to excuse the 2v2 has lost its
  premise. **The single 2v2 was never re-measured** — the file is not to hand — so whether a team
  recording writes a block is still open, but it is now open for a DIFFERENT reason than it was.
  A 43-file folder collected to answer it turned out to contain no team game at all (19 readable,
  all 1v1; the other 10 were not recordings — a PNG, an `.exe`, four of zeroes).
  Two things that DID come out of those files: `A == B` in all 13 decided 1v1s (thirteen more
  confirmations that B is the loser, not the recorder), one block sits **5 bytes** short of the
  end (without the 8-byte slack that match would not have rated), and **`C` equals the human
  count in all 25 readings**.

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

  **The competitive confirmation is a NUDGE, not a guarantee — and it now escalates on
  evidence.** `ConfirmRecordGameAsync` is a dialog in the LAUNCHER: it ticks nothing inside AoE3
  and cannot see whether the player ticked it. Nothing can (see the two dead candidates above:
  the profile setting and `+RecordGame`). Anyone reading the code should know that before
  reasoning about what it guarantees, because the answer is nothing.
  **What it can do is stop repeating itself.** `Services/Multiplayer/RecordingMemory` (pure,
  `RecordingMemoryTests`) records, per mod, whether the last competitive match produced a
  recording; the next start then leads with the fact — "your last match wasn't recorded" —
  instead of the same instruction a third time. A reminder that reads identically every time
  stops being read; a statement about something that already happened does not.
  **Three rules are load-bearing:**
  (1) **A recording that exists but never finished writing its ending counts as RECORDED.** That
  player DID tick the box; what failed was closing AoE3 without returning to the main menu.
  Escalating there sends them to fix the one thing that was not broken, and the end-of-match card
  already names the real cause.
  (2) **`Evaluate` returns null — "learned nothing" — for a casual or non-reportable match**, and
  the caller must leave the memory untouched. Returning "recorded" there would let one friendly
  game in between quietly clear a warning that had been earned.
  (3) **It is per MOD** (`ModState.LastMatchHadNoRecording`, beside `GameRecordingApplied`),
  because `optionrecordgame` is per mod profile. It is written beside
  `MaybeReportMissingRecording` with the SAME inputs, so the two cannot disagree about whether
  this was a real host-side match.
  **ANSWERED: AoE3 writes the recording at the END of the match, so there is nothing to check
  while it is running, and `MaybeProbeRecordingStarted` has been DELETED.** This paragraph used
  to describe that probe — a one-shot log line 90 s into a competitive match, counting recordings
  newer than the launch — and promised that when the answer arrived it would either become a live
  check or be removed with the reason written down. It is the second.

  The evidence is a real player's bundle, and it is not the probe's own count: three competitive
  matches in one evening each produced a recording whose last-write time was **28, 42 and 54
  seconds BEFORE** the launcher analysed it, and that analysis runs the instant the game process
  exits. Three out of three, written as the match ended. So the idea this was gathering evidence
  for — warning a competitive host mid-match that nothing is being recorded, while there is still
  time to restart — is dead: at 90 seconds there is nothing to look at.

  The probe's own counts were consistent (0, 1, 0) and **the 1 has no explanation**: the only
  recording of that session carried a timestamp two minutes and twenty seconds EARLIER than the
  launch it was compared against. Written down rather than smoothed over. It does not move the
  conclusion — a file's timestamp is direct evidence, a count is not — but it is the loose end if
  this ever comes up again.

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

  **The identity gap WAS the blocker for scoring, and the room state now carries in-game
  names — which is exactly what this paragraph said it was waiting for.** The replay names
  players by their *AoE3 profile name* (`'69metal69'`); the backend needs the Discord
  `users.id`. Nothing in the file links them, and only the local player's own name is
  knowable, from `UserDataService.GetInGameName`. That determines a 1v1 completely (host won
  ⇒ the other lost) and says nothing about a team game.

  So every launcher now **publishes its own** name over the room socket —
  `LobbyWebSocket.SendSetInGameNameAsync` → `set_ingame_name` →
  `room_state.members[x].ingameName` + a `member_ingame_name` broadcast — copying
  `set_radmin_ip`/`member_net` line for line, **including the dedup guard reset on room
  ENTRY**: without it the second room of a session short-circuits on the unchanged name,
  never sends it to the new socket, and every team game from that room silently loses its
  teams. That precise bug already happened once with the Radmin IP.

  **`Services/Multiplayer/MatchTeamMap.cs` is the pure rule that joins the two**, and every
  clause in it is a refusal: all-or-nothing (one unmatched name refuses the WHOLE map,
  because a half-filled one puts a real person on the wrong side of a real match in somebody
  else's history), duplicate names refuse, a head count that disagrees with the recording
  refuses, `teamid = -1` on every slot refuses (that is what all fourteen measured 1v1s
  carry, and what an FFA carries), one team refuses, and a mix of real ids and -1 refuses.
  Null means "report no teams", which is what the launcher did for every match before this.
  Team ids are normalised to `0,1,2…` by lowest slot so both machines that could report one
  match agree on the numbers. Pinned by `MatchTeamMapTests` against the real 2v2 fixture.

  **The names are FROZEN into `MatchContext` at Start, not read when the match is reported** —
  and this is the difference between the feature working and never working. The player who
  leaves the room first is reliably the one who just lost, so by report time the live roster no
  longer holds their name; with the map being all-or-nothing, one missing name refuses the whole
  thing. Reading late would lose the teams of exactly the matches this exists for. Same rule, and
  the same reason, as the roster itself (`AClosedRoomCannotChangeTheAnswer`); pinned by
  `TheInGameNamesAreFrozenAtStart`. `Capture` also drops any name belonging to somebody outside
  the roster, since that would make the head count disagree with the recording's.

  **Guessing the link from the names is ruled out by measurement, not taste.** On one machine
  the same person is `Gorgorito12` on Discord and `Gorgorito` (WoL) / `gorgorito` (Improvement
  Mod) / `sdfs` (base game) — the profile is per MOD and none of the three equals the account.
  That is why the name is self-reported and why `MaybeReportInGameName` resolves it from the
  ROOM's mod, not the dashboard's.

  **This reverses the rejection recorded further down** (*"Rejected on purpose: comparing AoE3
  profile names… Don't re-propose it"*), and the distinction is worth keeping: that rejection
  was about `same_game`, where the **seed** does the same job with no name at all and is
  strictly better. For the team map there is no seed-shaped alternative — the name is the only
  link that exists — and the objections it raised (names unlike the account, blank or odd ones)
  are precisely why this self-reports and refuses rather than guesses.

  **Ambiguous must still report a draw — never an invented winner**, since this feeds a
  rating. Teams are recorded; **nothing about them rates yet** (see the ELO rules).

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
  passes `LooksLikeThisMatch`: host present (by name) and human count matching the room.
  There was a third condition — recorder slot = the host's — and it rejected the winner's
  own recording every time; see the trailer section. Nothing qualifying returns null, and
  no replay is always safer than the
  wrong one. `FindLatestReplay` survives for the chat line that names the saved file, where
  "the newest" is exactly right and no identity is needed.

  **Verified end to end on the maintainer's own disk, and both gates fire independently:**
  two strangers' replays in his `Savegame\` are refused for not containing him (the strong
  check), while `Code vs Nathan 2` — which reads `Confident` in isolation — is refused by
  ownership before its verdict can ever be reported. His own two recordings are accepted
  and then refused a result for being skirmishes. Redundant on purpose: each gate alone
  would have let one of those through.

- **A RECORDING'S NAME IS NOT AN IDENTITY — AoE3 calls them all `Record Game N` and RENUMBERS
  after every match, so the newest is always number 1. Never hand a player a file name and
  call it an answer.** Measured, not assumed: in one bundle three competitive matches in a
  single evening were each analysed from a file called `Record Game 1.age3Yrec`
  (`ESOC_Manchuria`, then `ESOC_High Plains`, then `ESOC_Tibet`), and by the time the bundle
  was taken those same three files were numbered 1, 2 and 3 in reverse order of play.

  **The bug that came out of it is the reason this is written down.** The launcher's ONLY
  statement about the file was the room-chat line `MpChatReplaySaved` ("Replay saved: {0} ({1}
  KB)."), so that player was told "Record Game 1" three times, and every one of those names had
  moved on to a different game before he went looking. He reported it as the launcher not
  recording at all. **It records fine** — the folder is `Savegame`, singular, next to `Users3`
  under `My Games\<mod>`, and every one of his matches was there, read and rated.

  Two fixes, and the split between them matters:
  - **The end-of-match card's REPLAY cell names the file and REVEALS it** — `explorer.exe
    /select`, via `Services/FileReveal.cs`, so the right one is selected among ten with
    interchangeable names. Opening the folder would not have answered the question. The cell was
    previously a fixed "not uploaded"; upload is still scaffolded with no caller, but the cell
    stopped being about the upload, so `MpResultReplayNone` now says "no recording".
  - **The chat line names the MAP as well** (`MpChatReplaySavedMap`, used when the recording
    gave one) and says outright that AoE3 will rename the file. The map is the only thing in
    that sentence that still identifies the match tomorrow.

  **The path has to be a FIELD, `_lastRecordingPath`, read at paint time** — the same rule, and
  the same trap, as `_lastLocalReadFailure`. The card's `_outcomeRebuilder` CAPTURES its
  `MatchReplayInfo`, so a reading that lands after the first paint (the early read, or the late
  correction) would repaint a card still holding the null it started with. It is **only ever
  assigned a real file and cleared only in `EnterInGamePhase`**: a later pass that finds nothing
  must not take away what an earlier one found, which is the same "a later pass may only improve
  the diagnosis" rule one paragraph up.

  **`FileReveal` falls back to the folder and never throws, and neither half is politeness.** A
  stored path goes stale two ways — the renumbering above, and `GameRecordingPurge` deleting
  automatic recordings past the newest ten — so by click time it routinely names nothing; and an
  exception there would take down the card it is drawn on. Its tooltip carries the FULL path,
  which is what a player needs when the launcher runs as another Windows account: in that same
  bundle the `.exe` sat on one user's desktop and every file it wrote went to a different user's
  Documents, so nothing in the player's own profile could ever have been found.

- **THE GAME'S EXIT IS NOW DETECTED TWO WAYS, and the second one exists because the first
  silently does not always happen.** `Process.Exited` was the ONLY trigger for
  `OnGameExitedAsync` — one call site — and the multiplayer path had no polling backstop, unlike
  the dashboard, which has had `MainWindow.StartGameMonitor` (a 2 s tick) all along.

  **What that cost, from a real bundle.** When AoE3 demands elevation — a Windows compatibility
  layer pinned on `age3y.exe` is enough — `GameLauncher.LaunchAndWatch` falls back to a
  ShellExecute launch, and a medium-integrity launcher cannot hold a handle on a
  higher-integrity child, so no event ever comes. The player's log showed **three launches and
  zero game-exit handling**: the recording never read, the match never reported,
  `game_ended` never sent (room and Discord embed stuck "In game"), `_matchContext` leaking into
  the next match, and — had he hosted — **nothing reported at all, ever**. Nothing warned him,
  because the failed launch still returned a `Process` object that read as success.

  **`Services/GameExitWatcher.cs`** now owns the answer: the event and a poll both feed it, an
  `Interlocked` guard makes it report **exactly once**, and the liveness probe is injected so
  every rule below is testable without launching anything (`GameExitWatcherTests`).
  **Three rules are load-bearing:**
  (1) **It never reports an exit before it has SEEN the game running.** On the elevated path the
  process does not exist while the UAC prompt is on screen, so the first ticks find nothing —
  and announcing that the match ended seconds before the game opened would be worse than the
  silence it replaces. `SignalExited` (the real event) skips this rule, because a handle on a
  real process is proof rather than an inference.
  (2) **A probe that throws is "don't know", never "it exited"** — the one mistake here moves
  somebody's rating while they are still playing. `ArmingTimeout` (2 min) stops a launch that
  never happened from leaving a timer running, and giving up is explicitly NOT an exit.
  (3) **The poll runs for EVERY multiplayer launch, not only the degraded one.** One way for the
  event to go missing has been found; assuming it is the only one is how this lasted as long as
  it did. A 2 s tick costs nothing beside a match that already refreshes at 1 Hz.

  **`LaunchAndWatch` returns `WatchedLaunch`, not a bare `Process?`** (`Services/GameLaunchResult.cs`)
  — `Process?`, `ProcessId`, `ExePath`, `NeededElevation`, `ExitWatcherAttached`, `Failed`. A
  non-null process with no watcher on it is precisely what made this invisible. `MultiplayerTab`
  gates on `Failed` rather than `process == null`, which also stops it telling a player whose
  elevated game IS opening that it could not be opened. Keeping the pid also rescues the rare
  case where the reparented launch works but the watcher attach loses a race, which used to
  return null and drop the callback with it.

  **`IsGameStillRunning` goes by PID first and only falls back to the NAME sweep** (no pid, or a
  process the integrity barrier will not let us inspect). The fallback's honest limit: WoL and
  the stock game both run `age3y.exe`, so with an unrelated AoE3 open it reads "still playing"
  and never fires — the same ambiguity `GameLaunchResult.ProcessId` documents. It never throws.

  **The compat-layer offer reaches multiplayer now** via `MainWindow.OfferPendingCompatLayerFix`,
  called after the multiplayer exit handler (game already closed, so no modal fights AoE3 for
  focus) and from the declined-UAC path, which used to be swallowed whole by a catch-all. Before
  this, the offer hung off the dashboard's exit handling alone, so a multiplayer-only player paid
  the UAC prompt on every launch and was never told a one-click fix existed. When the 740 fires,
  `LogCompatLayer` records WHICH layer it is — and records its ABSENCE too, which rules the layer
  out and points at an executable that demands admin on its own: a different problem, and one no
  bundle could distinguish before.

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

  **The host's slot comes from his NAME, never from the trailer — `FindPlayerSlot`.**
  This paragraph used to say the opposite ("the host's slot comes from the trailer, not
  from a second name match"), and that was the bug: the trailer's B field is the loser in
  multiplayer, so it resolved to the loser on both machines and `HostResultFrom` could
  only ever answer `0.0`. One rule now answers "which slot is ours", and both
  `LooksLikeThisMatch` and the caller go through it — they used to answer it two different
  ways, and the other one was wrong.

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
  so a folder of junk can't spin. And `ShouldRetry` fires when something was
  **unreadable** OR when **nothing was there at all** — but never for one that parsed cleanly
  and belongs to another game, which will parse identically in three seconds. Delays
  `0/1000/2500/5000 ms`.

  **The empty case used to be excluded, and that exclusion was a bug with a good reason
  behind it.** Requiring `Unreadable > 0` meant a recording that had not been CREATED yet got
  zero retries — the match reported all-draws instantly and a file that appeared a second
  later was never seen. It was excluded because waiting is pure latency for the MAJORITY of
  matches, which have no recording at all. **That reason is gone only because the report no
  longer waits for it**; if the search is ever moved back in front of the report, this branch
  has to go with it.

  **Runs on a background thread** (`Task.Run`): each candidate costs an inflate of a
  multi-megabyte file and this fires the instant the game closes, with the player looking
  at the launcher again.

  **Civ is deliberately still null.** The recording gives an INDEX; turning it into a name
  needs the mod's civ list, which Improvement Mod doesn't ship loose (it is inside the
  `.bar`) and whose ordering against `data\civs.xml` is plausible but **unconfirmed**
  (7 = `Indians`, 17 = `Italians` land on real civs, which is not proof). Sending the bare
  number would put a value nobody can interpret into everyone's history. `MapName` IS sent
  now, from `gamefilename` — the real map, not `gamemapname`, which is the POOL.

- **The report NEVER waits for the recording, and a reading that lands afterwards CORRECTS
  the match instead.** This inverts the order the previous bullet describes, so read them
  together.

  `OnGameExitedAsync` runs `AnalyseMatchReplayAsync` with **`firstPassOnly: true`** and
  reports whatever that one pass found. When the recording is there and readable — the good
  case, measured at under a second end to end (a real match logged its game-exit analysis and
  its `match_reported` in the same log second) — nothing changes. When it is not, the report
  goes out immediately with no result and `ContinueSearchingForResultAsync` runs the rest of
  the ladder BEHIND it.

  **The reason is a ratio, not a preference.** AoE3's per-match "Record Game" box comes up
  unticked every time, so most matches have no recording at all — putting the retries in front
  of the report makes the majority slower for the benefit of a few. It also widens the window
  in which `_matchContext` can be overwritten by a new match, which is the race the
  `ReferenceEquals` + `GameRestartedSince` guards in the exit `finally` exist for. Those guards
  are unchanged and must stay.

  **A late reading reaches the server through `TryConfirmMatchAsync`, not a second report.**
  Reporting is still host-only — N reporters insert N copies of one match. The confirm path
  gained `allowHost`, off by default because confirming your own report against itself proves
  nothing; it is passed true in exactly one case, when our own report went out with
  `unrated_reason == "no_decided_result"` and a reading turned up afterwards. That reading has
  never reached the server in any form.

  **A later pass may only IMPROVE the diagnosis.** `ContinueSearchingForResultAsync` refuses to
  overwrite a specific failure with `NoRecordingFound`, because that is the one message already
  known to be wrong, and putting it back would undo the whole point.

  **It stops early when there is nothing a recording could change**: a server reason of
  `not_1v1` / `mod_not_ranked` / `duplicate_recording` means reading one would spend seconds of
  disk to learn something nobody can act on.

- **The end-of-match card is REBUILT, not painted once — and never by re-entering
  `EnterResultPhase`.** The card used to be built at the instant the report arrived and frozen
  there. A player whose own AoE3 was still open therefore read "the match was not recorded" for
  as long as they left it open, while their recording sat on disk naming the winner — nine
  minutes, in the incident this came from.

  `_outcomeRebuilder` is a closure captured where the card is first painted (both the host's
  POST path and the guest's history path install one), and `RepaintMatchResult()` re-runs it.
  Everything that can change — `_lastLocalReadFailure` and `_lastLocalReadDetail` — is read at
  CALL time, so the rebuild picks it up. It is cleared in `ExitResultPhase` and
  `EnterInGamePhase`.

  **`EnterResultPhase` must NOT be used to refresh.** It clears `_roomMatchLive`, drops the
  process handle, kills the tick timer, stops the socket's reconnect and suppresses the leave
  confirm; running that again over an already-terminal state is a different bug.

- **Five things can stop a match being decided, and telling the player the wrong one is what
  loses their trust.** `LocalReadFailure` gained three values, and two of them exist because
  the generic "the match was not recorded — tick Record Game" was being shown when it was
  provably false:

  **`RecordingNotOurs`** — recordings were found and read PERFECTLY and none of them is this
  match (`Parsed > 0 && File == null`). This used to fall through to `NoRecordingFound`. The
  likeliest cause is named in the message because it fails **every** match until it is fixed:
  `LooksLikeThisMatch` requires the player's AoE3 `<OnlineName>` to appear among the
  recording's players, so a profile whose name differs from the one they play under never
  matches anything. `MpResultUnratedNotOurs` + the `LocalFailureDetail`, which carries the
  profile name we read against the names the recordings actually held — data, appended by the
  card, never translated.

  **`RecordingNoOutcome`** — the recording IS this match and the game never finished writing
  its ending (`!SignaturePresent`; see the trailer section). Actionable advice: leave the match
  to the main menu before closing AoE3.

  **`ReadPending`** — a WAIT, not a failure. Set in `HandleMatchReported` when the frame
  arrives while our own game is still running, and replaced when the reading lands. Read
  `_matchPhase == InGame` BEFORE calling `EnterResultPhase`, which sets it to `Result`.

- **The launcher tries to read the recording WITHOUT waiting for AoE3 to close —
  `TryEarlyReplayReadAsync` — and it may only ever improve the state.** Fired from
  `HandleMatchReported` when the match came back unrated and our game is still open. In the
  incident it would have turned nine minutes into seconds.

  **Whether AoE3 has finished — or even started — writing the file at that point is NOT
  established**, and it may hold it open with a lock. So a failure is discarded in SILENCE and
  every field it touched is restored: counting a locked file as "unreadable" would move the
  card from an honest "waiting" to a stated cause that is wrong, which is the exact failure
  being removed. Only a real result is kept. `_replayAnalysisInFlight` keeps this and the exit
  handler's own search from interleaving.

  **ANSWERED, and the answer says this early read almost never pays.** `replay-index.txt`
  against the game-exit line in the log was the test, and it came back: AoE3 writes the file at
  match END (see the probe paragraph above — three recordings, each written 28-54 s before the
  exit handler ran). So while our own game is still open there is usually nothing on disk to
  read, and this path plus the empty-folder retry are mostly dead weight.

  **They were nonetheless KEPT, and the reason is the shape of the failure, not sentiment.** The
  early read costs nothing when there is no file — one directory walk — discards a failure in
  silence by design, and still pays in the one case it was built for: a player whose own AoE3
  stays open for nine minutes after a match his opponent already reported. Removing it would
  save nothing measurable and would put that case back. Revisit it only with a bundle showing
  it firing uselessly and often.

- **The replay search window has a CEILING now, and it orders rather than rejects.**
  `FindMatchReplay`'s filter had only ever had a floor (newer than the launch), so a file
  written long after the match still qualified — not theoretical: the file the launcher
  analysed in the reported incident was named `bo3 siux vs alucard (3).age3Yrec`, a name its
  owner gave it while copying recordings to send over Discord, minutes after the match. That
  window widens further now that the search retries and can also run early.

  `preferBeforeUtc` (game exit + `ReplayWindowMargin`, two minutes) is the FIRST sort key:
  in-window candidates are judged first, out-of-window ones are still judged afterwards.
  **Never make it a filter.** The recording is finished as the game closes and its timestamp
  keeps moving while the retries run, so a tight ceiling would start discarding legitimate
  recordings — precisely the symptom this area exists to fix. Pinned by
  `TheWindowOrdersCandidates_ButNeverDiscardsThem`.

  Honest scope: it stops the careless case (an Explorer copy keeps the original write time and
  is excluded outright) and closes the window the other changes open. It does **not** stop
  anyone deliberate — the file's timestamp belongs to the machine's owner — and there is no
  clock inside the file to check it against (`gamehosttime` read 59 / 36 / 1507369 across the
  three real recordings; it is not wall-clock).

- **A late reading can DECIDE a match the server stored without one — backend
  `canUpgradeFromConfirmation`, and its anti-abuse clause is the load-bearing half.**
  This supersedes "it gates nothing" in the confirmation bullet above and the
  `0004_match_confirmations.sql` header, both of which say confirmations change nothing.

  The failure it closes had two shapes in one incident and the same cause in both: **only the
  host's reading counted.** In one match the host's recording had no outcome trailer; in the
  other the host found no recording at all and reported `map_name`, `game_seed` and
  `game_host_time` all null with both players on 0.5. Both times the OTHER player's recording
  named the winner correctly, their launcher read it, and `POST /matches/confirm` filed it as
  evidence and threw it away.

  `matches` now persists `unrated_reason`, `rated` and `decided_by` (migration `0006`), because
  the verdict used to be computed, returned and logged but never stored — so a row could not
  remember it was waiting for an answer. Every pre-migration row is NULL and therefore
  **ineligible by construction**: nothing here can re-rate history.

  **The rule, in order:** the stored reason must be exactly `no_decided_result`; the reading
  must name a winner; the confirmer must be in `roster_at_start`; and **you may concede your
  own defeat freely, but claim your own victory only when the fingerprint the reporter already
  stored matches yours.** That last clause does the work of a verification the server cannot
  perform — it never reads the recording, `result` is a number the client sends, and
  `replay_sha256` / `game_seed` are anti-duplicate keys rather than proof. It does not verify
  the claim; it removes the reason to invent one, since **a liar can only give points away**.
  It costs little coverage because the player who can read the recording is usually the one who
  lost — though note that is about who tends to concede, not about whose machine can read a
  recording; see the trailer section, where the stronger version of that claim is refuted.

  **When the row has no fingerprint, the confirmer's is ADOPTED** — which also gives the match
  the anti-duplicate protection it never had, since a recording-less report sends those as
  null. Refused if another match already claims that pair, and dropped without dropping the
  decision if the partial UNIQUE index still collides.

  **Double-rating is the worst outcome and the guard is a conditional UPDATE.** Both call sites
  (`POST /matches` after the report, `POST /matches/confirm` after tying) can fire for one
  match. The row is CLAIMED with `WHERE unrated_reason = 'no_decided_result'`; zero changes
  means somebody else got there and the call stops before touching Glicko. A read-then-write
  would not do — `applyMatch` awaits, and an await is where two requests interleave. If the
  rating then fails, the claim is rolled back rather than leaving a match marked rated with no
  ratings behind it.

  **The correction is announced over `/global/ws`, not the room socket** — by then the room has
  been closed for minutes. `GlobalChatRoom.announceMatchRated` sends `match_rated` to the two
  participants only (it carries their rating change), the launcher raises
  `NotificationKind.MatchRated`, and the bell dedups on the match id in
  `LauncherConfig.NotifiedRatedMatchIds` because that socket can deliver a frame twice and
  again after a restart. Best-effort: an offline player sees it in their History.

  **Deliberately NOT done: requiring the two readings to agree.** It would close the older and
  larger hole — a host can still lie in their own `result`, which predates all of this — but
  measured against the incident's three matches it would have rated **none**, including the one
  that works today. `match_confirmations.agreement` / `same_game` are now STORED rather than
  only logged (the log rotates and is gone) so that decision can be made with numbers; the
  query that answers it, including "how often does the second reading never arrive", is in
  `DEPLOY.md`.

  **Also deliberately not done: letting players declare the winner when nobody could read a
  recording.** It is the only route to full coverage and it was refused — the ELO moves on file
  evidence or not at all. A match neither player recorded stays undecided, permanently.

- **The diagnostic bundle describes the recordings — `replay-index.txt`.** The newest ten
  `.age3Yrec` under `Savegame\\`, one block each: name, size, mtime, then `gamefilename`, seed,
  host time, every player slot with its name and whether it is human, and the outcome trailer's
  verdict — including the last 16 bytes in hex when the signature is missing. **The files are
  never copied**, only read.

  It exists because answering the incident meant asking the player for his recordings over
  Discord and parsing them by hand, over hours. The same answer now travels in the bundle he
  already sends. The user-data listing also enumerates **directories** now: it was files-only,
  so a bundle sent specifically about a rating problem could not even show whether `Savegame\\`
  existed.

  `ShareDiagnostics` is `async` and hands the whole export to `Task.Run` — up to ten inflates
  of multi-megabyte files is not the UI thread's work.

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

  **One documented exception to "once per session", and it is an EVENT, not a timer.** A
  session that starts while the backend is down never gets a standing at all, and the only
  retry was a session-state change (`PushAccountChip` → `LoadStandingAsync` on a null cache).
  Measured: a launcher opened during a 502 outage logged four failed fetches and then showed
  no ELO under the player's name for the rest of the session, with the server back up the
  whole time. So `RefreshRoomsListAsync` now keeps a `_roomsFetchFailed` flag and, on the
  first fetch that SUCCEEDS after one that failed, re-requests the standing if it is still
  missing. **The trigger is the transition, never the poll** — hanging the request off the
  poll itself would fire it every few seconds for exactly as long as the server stayed down,
  which is what the 20/min · 500/day per-IP budget cannot pay for. The check sits ABOVE the
  quiet diff's early return on purpose: a poll that finds the rooms unchanged repaints
  nothing but still proves the backend answered.

  **The History row shows Win/Loss and NOTHING for 0.5 — the omission is the rule.** A 0.5
  means the result could not be read (no recording, a team game, a skirmish, or any match
  reported before this existed), so a "Draw" label would show all of them as drawn games
  that never happened. Every meta segment is likewise dropped when empty, so an old row
  renders exactly as it always did.

  **The row also NAMES who played and marks the winner, and that needed a backend change
  because nothing on the client could supply it.** A history row is the caller's OWN
  `match_participants` row joined to the match, so it could count heads and never name one —
  `_roomMembers` is live-room state that dies with the room, and `MatchContext.Participants`
  holds ids, not names. Both halves were already in the database, names in `users` and the
  win/loss in `match_participants.result`, and simply never joined. `attachParticipants`
  (`src/matches/rest.ts`) does it in **ONE extra query for the whole page** — fifty ids into a
  single `IN`, grouped in JS, never one query per match. That JOIN on `users` is **INNER**,
  unlike the two LEFT JOINs on `elo_ratings` this bullet insists on elsewhere: the FK is
  `ON DELETE CASCADE`, so a participant cannot outlive its user, and a LEFT could only add a
  nameless row nothing can render.

  **Client-side the rule is the pure `MatchParticipantsView`** (pinned by
  `MatchParticipantsViewTests`), and every rule in it is a refusal: a 0.5 gets **no ✓/✕** —
  the same omission as the badge, applied per player — a rating with either end missing shows
  **no delta rather than "+0"**, and the order is recomputed on the client rather than trusted
  from the server's `ORDER BY`, which a client cannot see change. **The head count and the
  names are alternatives, never both:** `MpHistoryPlayers` ("2 players") is dropped when
  participants arrive and kept when they don't, so a backend older than the field renders the
  row exactly as it always did — the same degradation `PlayerCount` itself already had.
  `BuildHistoryRow` also stopped hand-rolling `>= 0.999` / `<= 0.001` and goes through
  `MatchOutcomeView.Classify`, so the badge and the per-player lines can no longer answer the
  same number differently.

- **The History subtab is fed by a HOST-ONLY match report at game exit — don't
  re-add PER-PLAYER reporting.** (This bullet used to also forbid an ELO display.
  That half is obsolete: showing the rating is now the point. What stays banned is
  every player reporting the same match, which would insert it N times.) The
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
  by any gate still reports all-draws. `BuildHistoryRow` shows `mod [Win|Loss] [±ELO] ·
  map · duration · date` over one line per player (avatar, name, won/lost, their own ±ELO) —
  see the roster paragraph above for what it refuses to show. **The backend never needed a change for any of
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
  idempotency key for the backend to catch it with.
  **That last clause is now OUT OF DATE, and the exception it blocks has been carved
  out — read both together.** Migration `0005` added the UNIQUE index on
  `(game_seed, game_host_time)`, which IS an idempotency key: two reports of the same game
  collide and the second is stored `duplicate_recording` rather than rated twice. So in a
  COMPETITIVE room `HandleHostChanged` now calls the new `WithHostGained()` when the room
  promotes US mid-match. Without it the abandonment rule is one-sided — a guest who walks
  out is caught, while a HOST who closes his launcher produces no report at all, because his
  client was the only one that would have sent one — and a rule that catches one player and
  not the other is worse than no rule. Still one-way everywhere else. For the same reason there is
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

  **That rehydration was ALSO the one path through which host migration reached Discord,
  and it credited the wrong person.** In normal operation the embed keeps naming the
  CREATOR for the life of the room, and by three independent mechanisms:
  `notifyRoomChanged` accepts only `players`/`status`/`title`, `renderKey` does not
  include the host, and `reflectToDiscord` has no case for `host_changed`. But
  `rehydrate` rebuilt the state by joining `host_user_id` — the host NOW — so after a
  restart the author silently switched to whoever had inherited the room. Every deploy is
  a restart, so this was frequent, and it was reported as "the second player appears as
  the creator". Migration `0010` adds **`lobbies.created_by`**, written once at
  `POST /lobbies` and never updated (`reassignHost` mutates `host_user_id` in place, so
  nothing else remembers who opened the room), and the join is now
  `COALESCE(l.created_by, l.host_user_id)` — the fallback being exactly the old behaviour
  for rooms created before the column existed. **The three mechanisms above were left
  alone deliberately**: they are what keeps a host change from editing the embed at all.
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

- **A MATCH DOES NOT END WHEN OUR GAME CLOSES — it ends when the HOST reports it, and on a
  guest's machine that is always later. `_pendingResultContext` is what survives the gap.**
  This is the single cause behind three symptoms a real competitive match produced at once: no
  result shown, nothing said, and a room the launcher appeared to think was still playing.

  **The mechanism.** `OnGameExitedAsync`'s `finally` clears `_matchContext`, which is correct for
  what that field MEANS ("the match I will report") and wrong for receiving. The guest's game
  closes first — the player who lost leaves first — so by the time `match_reported` arrives,
  carrying every participant's result and rating change, the field is null. `HandleMatchReported`
  opened with two `return`s on that, **neither of which logged anything**, and the `4007` close
  behind it was gated on the same field, so it fell through to the generic reconnect. Measured on
  the real match: sixteen seconds between the two games closing, then ~230 reconnects to a deleted
  room over five minutes, stopped only by the player closing the window.

  **`_pendingResultContext` is deliberately a SECOND field, not a longer life for the first.**
  `_matchContext`'s lifetime is guarded by `ReferenceEquals` + `GameRestartedSince` for reasons
  that have their own bullet, and lengthening it would put the REPORT path — which works — at
  risk to fix the RECEIVE path. `ResultContext()` returns `_matchContext ?? _pendingResultContext`
  and is what every post-game reader now goes through. Cleared when the result lands, when the
  room is left, when a new match captures its own context in `EnterInGamePhase`, and by
  `ResultWaitCeilingSeconds` (120). **Every silent `return` on the frame path now logs its
  reason** — that silence is what made the fault take an afternoon to find, because a `return`
  with no log is indistinguishable from a frame that never arrived.

  **`MatchPhase.AwaitingResult` is the guest's visible half, and it is NOT `Result` renamed.**
  `EnterResultPhase` calls `StopReconnect`, and the socket it hangs up on is the one
  `match_reported` still has to arrive on — entering it early hangs up on the answer. The new
  phase shows the same `MatchResultOverlay` and collapses the same left column, and leaves the
  socket alone. The card says `MpResultWaitingHost` and, when `ShouldOfferRejoin` applies, offers
  the way back into the game: **the launcher cannot tell "my game closed because the match ended"
  from "my game crashed mid-match", so the card offers both readings rather than guessing one.**
  At the ceiling it becomes `ShowResultUnavailable`, which gained a "back to rooms" button —
  without one that panel is a dead end, since it covers the very column the Leave button is in.

  **Two terminal close codes stop the reconnect: `4404 lobby_not_found` and `4006 lobby_closed`.**
  Retrying a deleted room cannot succeed, and it did not slow down either, because
  `LobbyWebSocket` reset its backoff on a connection that ESTABLISHED — and these close
  immediately after the upgrade, so the exponential backoff never left its first step. Fixed at
  both levels: the codes are handled by name, and the backoff now only resets for a connection
  that lasted `StableConnectionMs` (5 s). Handling the codes is the better fix for the cases we
  can enumerate; the timer covers the ones we cannot.

  **The phase does not survive its room.** `HandleLobbyWindowClosed` resets `_matchPhase` when it
  is `Result` or `AwaitingResult`. Left set, it survives into the NEXT room the player opens,
  where `ApplyMatchPhaseUi` paints a fresh lobby with the result overlay up and the whole left
  column collapsed — no roster, no Ready, no Start, and nothing explaining why.

  **And a result that lands with no window is no longer discarded.** `ShowMatchResult` returned
  in silence when `_lobbyWindow` was null; it now falls back to a desktop toast plus a bell entry
  (`AnnounceResultWithoutAWindow`), deduped on the match id so a later `match_rated` frame cannot
  show the same match twice. The window is deliberately not reopened — the player closed it.

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

- **Nothing on the multiplayer surface may put an `Opacity` below 1 or an `Effect` on an
  ancestor of TEXT — both disable ClearType for every glyph underneath.** The rule was
  already written next to the room cards (`MultiplayerTab.xaml` ~:920, on why the card glow
  lives on a sibling underlay); two places had broken it, and together they are what a user
  reported as the multiplayer text looking "blurry and washed out".

  **`BuildRoomCard` dimmed a whole row with `Opacity = modInstalled ? 1.0 : 0.6`.** That is a
  composition layer over every label in the row: ClearType off, and the real contrast of
  `MpTextFaint` on `MpPanel` collapsing from 4.09:1 to about **2.1:1**. Blurry and washed out
  at once, from one line. The dimming now happens through the BRUSHES — both text roles step
  down one rung of the ramp — and the signal survives regardless, because the action button is
  already disabled for a mod you do not have.

  **`MpAlertOverlay` hung a `DropShadowEffect` on the Border whose child is the card whose
  children are the title and body.** Every confirm and notice dialog on the surface rendered
  its text softer than the text behind it. The shadow is now a SIBLING underlay beneath the
  card, sized off it by binding `ActualWidth`/`ActualHeight` because the card is
  content-sized — and it needs its own `host.Children.Remove` in `Close`, which a child would
  not have.

  A third, minor one survives on purpose: the invite chip's `Opacity` (`~:5836`) wraps a
  single Segoe MDL2 glyph, where there is no antialiasing quality to lose.

  **Both halves of this rule are now LAUNCHER-WIDE, and the shared styles multiplayer sits
  on top of changed with them — see the text-legibility bullet in `CLAUDE.md`.** The
  "colour, not an `Opacity` layer" rule got its own brush (`TextDisabled`, #989898) and
  replaced the disabled-state `Opacity` in the implicit `Button` style, `MpOutlineGoldButton`,
  `MpGhostButton`, `MpLinkButton`, `NotifLinkButton`, `SidebarColoredButton` and the
  `TextBox`/`CheckBox`/`RadioButton` templates — so several multiplayer controls stopped
  compositing their captions without anything in `MultiplayerTab` changing. The
  sibling-underlay treatment `MpAlertOverlay` uses spread the other way too, to
  `SidebarPrimaryButton` (PLAY), `AppToast` and the dashboard hero. Don't reintroduce a
  disabled `Opacity` in a shared style on the grounds that multiplayer looks fine — these
  styles are shared, and that is how the rule got broken here in the first place.

- **The bottom three rungs of the multiplayer text ramp were raised to clear 4.5:1, and the
  sizes were deliberately NOT touched.** `MpTextFaint` `#6D829D` (4.09:1 on `MpPanel`),
  `MpTextLabel` `#61779A` (3.54, and **3.04** on a hovered room row — the worst pairing on the
  surface) and `MpTextDim` `#5F7592` (3.41) all failed AA, and they are attached to exactly the
  SMALLEST tokens: the 9.5 section labels, the 10.5 pills, the 11.5 body. The least legible
  colours were carrying the least legible sizes.

  Now `#8C9CB1` / `#8394B1` / `#758AA5`. The hue is kept and so is the ORDER — Muted 6.30 >
  Faint 5.75 > Label 5.24 > Dim 4.55 — so the hierarchy still reads; only the floor moved.

  **The sizes stay where they are.** `MpLabelSize` 11.5 sits below the launcher's documented
  13-px floor on the maintainer's explicit call, raised and declined twice (see
  `Tokens.xaml:223-228`). Fixing contrast is how this surface gets more legible without
  reopening that.

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

- **Backend gaps the multiplayer redesign is waiting on** (documented, never faked).
  Four of the six are now CLOSED — see the ELO bullet below — and what remains is:
  per-room ping needs `radmin_ip` on `GET /lobbies` — without it the PING column shows
  YOUR latency, identical on every row, which is why sorting by it is a no-op. (The REPLAY
  cell used to be listed here too: `ReplayUploadService.UploadAsync` still has no live
  caller, but the cell stopped being about the upload — see the recording-file bullet.
  Closed: per-member ELO now rides on the room-state member object;
  PEAK HOURS and RANKING are fed by `GET /stats/community`; and `match_reported` carries
  the result, so the guest's **three** polls — this bullet used to say four — are now only
  a fallback for an old backend.)

- **The backend can REQUIRE a launcher version, and it refuses multiplayer ENTRY only.**
  `MIN_LAUNCHER_VERSION` (empty by default, so the check is off) turns away builds older than it
  from creating a room, joining one, and the room socket (`4010 launcher_too_old`). Reporting a
  match, the global chat, history and stats stay open on purpose: somebody who already played
  should still be able to report it, and somebody turned away should still be able to ask why.
  **Nothing outside multiplayer is ever blocked** — the self-update can fail for reasons the
  player cannot fix (antivirus, permissions, no network), and a launcher that does nothing at all
  is a worse outcome than an old one that still plays.
  **The launcher had to start reporting its version for any of this to be possible.** Both the
  REST client and the room socket sent a hardcoded `Aoe3ModLauncher/1.0` User-Agent — a literal,
  never the real build — so the server could not tell an old client from a new one. They now send
  `X-Launcher-Version: {LauncherUpdateService.CurrentInformationalTag}`, the same value the
  self-updater compares itself with, letter suffix and all.
  **Three things are load-bearing:**
  (1) **The comparison is duplicated on purpose and must stay in step.**
  `src/lib/launcherVersion.ts` mirrors `LauncherUpdateService.TryParseSemVer`, including the
  letter rank (`1.0.5 < 1.0.5a < 1.0.5b < 1.0.6`). They answer the same question from opposite
  ends, and if they disagree a player is told they are up to date and refused entry in the same
  breath. Both sides have tests; change one and change the other.
  (2) **A client that reports NO version fails the check** — it can only be a build from before
  clients reported one. That is correct and it is also the trap: set a minimum before the first
  reporting release has shipped and everybody is locked out at once. `admin.ts versions <tag>`
  answers "how many would this block" from `users.last_launcher_version` (migration `0009`)
  BEFORE you set it, which is the whole reason that column exists.
  (3) **The required version comes from the SERVER's answer** (`min_version` in the error
  payload), never from anything the launcher knows — it cannot know what a backend it predates
  requires. On the socket there is no payload, so the message falls back to the version-less
  wording rather than inventing a number.
  Being refused forces a self-update check with **no `If-None-Match`** and opens the dialog:
  the ordinary check is conditional, and a `304` would read as "no update" and leave the player
  told to update with nothing offering to do it. That forced path is the ONLY place the update
  dialog opens by itself — the pill stays non-invasive everywhere else, which is what it replaced
  a modal for.

- **COMPETITIVE ROOMS are the gate on the whole ladder now: a room created without the box
  ticked stores its match and scores nothing.** `lobbies.competitive` (migration `0007`) is
  set once at creation and never again — a host who could tick it after seeing he had won
  would be choosing his own rating — and `ratabilityReason` refuses everything else with
  `not_competitive`, checked second, right after `mod_not_ranked`.
  **`RatabilityInput.roomIsCompetitive` is `boolean | null` and the null is load-bearing:**
  it means "there was no room to ask", and only an explicit `false` refuses. Collapse the two
  and a report with no `lobby_id` answers `not_competitive` instead of `no_lobby` — a worse
  message and, worse, a false one. Pinned by the `no_lobby` case in `ratability.test.ts`.
  **A competitive room also DECLARES A FORMAT — 1v1 / 2v2 / 3v3 — and the format is its SIZE.**
  Ticking the box reveals a segmented row; picking one sets the seat count (2 / 4 / 6) and locks
  the player-count row while the box stays ticked. **The format is DERIVED from
  `(competitive, max_players)` by the pure `Services/Multiplayer/RoomFormats`, never sent** —
  which is why `POST /lobbies` now also requires a competitive room to be 2, 4 or 6 seats and
  downgrades it to casual otherwise. Without that clamp a patched client could create a
  competitive room of 8 and leave a match whose format nothing can name.
  **`RoomFormat.Unknown` exists so that such a room is never read as 1v1** — a fallback would
  hand it the abandonment rule and a place on the 1v1 ladder on a guess. And `Casual` is not
  "1v1 by default" either: a two-seat casual room's size says nothing about how it will be
  played, which is the whole claim the competitive flag exists to prevent.
  **The price, paid knowingly: format and size are now married.** The day a competitive room
  wants spectator seats this stops deriving and has to become a real column on `lobbies` —
  which is a migration plus the nine hops `competitive` already travels.
  **The declared format is a PROMISE the report keeps.** It is frozen into `MatchContext` like
  `IsCompetitive`, and `RoomFormats.TeamsAgreeWithFormat` drops the recording's teams when they
  contradict it: a room created as 2v2 and actually played 1v3 would otherwise write
  real-but-wrong sides into four people's history with nothing downstream able to tell. A room
  that declared nothing (casual, unknown) cannot be contradicted, which is how a casual team
  game still shows its sides.
  **In the dialog the note under the box is format-driven and says opposite things:** the 1v1
  forfeit clause, or — now that team games rate — which ladder a team match scores on and what
  evidence it needs. It used to be one fixed string toggled by `_maxPlayers > 2`, which also
  described a casual room, one with no rating to miss out on.

  **The box is GOLD, and it used to wear the private-room purple.** It reused `MpPrivateCheck`
  and the whole `MpPrivate*` family wholesale, so ticking "competitive" looked exactly like
  ticking "private" — and the room it produced then wore a gold `MpCompetitiveBg` badge in the
  rooms list. The badge was right and the box was wrong: "gold rather than a fifth hue" was
  already the decision, taken where the badge is defined. The rest of the family
  (`MpCompetitiveSoftBg` / `Rim` / `FieldRim` / `Title` / `Sub`) is the same alphas over the
  same `#C9A227`. **`MpCompetitiveCheck` is a COPY of `MpPrivateCheck`, not a `BasedOn`** —
  the state colours live on `TargetName` setters inside that template and a derived style
  cannot override those, the precedence trap that left dead hover states in fifteen dialogs.
  And the tick inside the filled box is `MpCompetitiveInk` `#261900`, not white: gold is a
  light fill and white on it is about 2.5:1, the same call PLAY already makes.
  **The launcher asks; the server decides.** `POST /lobbies` accepts `competitive` and CLAMPS
  it to false for a mod outside `rankedModIds` — and now also for a size no format names — then
  echoes the effective value on the 201.
  `CreateLobbyDialog` reads `CreatedLobbyIsCompetitive` from the RESPONSE, never from its own
  checkbox, and says so in the room when the two differ
  (`MpCreateDialogCompetitiveDowngraded`) — a silent downgrade would leave the host playing
  as if his rating were on the line when it is not. **Never work out which mods are ranked in
  the launcher**; that is the same rule as clause (1) below, and the echo is what makes
  obeying it free.
  **The badge is derived from the boolean, never from words in the title** — anyone can type
  "competitive" into a room name, and a badge a stranger can forge is worth less than none. It
  is painted in `BuildRoomCard`'s title row (sharing column 1 with the private chip, so the
  name keeps the ellipsis) and beside `RoomTitleText` in the lobby — **not** inside
  `RoomInfoCard`, which collapses as a whole when the room has no mod name, password or extra
  copy and would take the badge with it, silently. The Discord embed gains a `Mode` field ONLY
  when true, so a casual room's embed is byte-for-byte what it was; it does **not** go in
  `renderKey` (the value cannot change while the room lives) but it **is** read back in
  `rehydrate`, or the first edit after a restart drops it.
  **Three protections ride on the flag, and each is shaped the way it is for a reason.**
  (a) **Record Game is confirmed before EVERY competitive start**, from `BeginHostStart` — the
  choke point both the button and `MaybeAutoStartOnAllReady` pass through, because gating only
  the button would let the commoner path skip it. Every match, not once per room: AoE3's box
  comes up unticked every time and the launcher cannot tick it, so "they were told once" is
  worth nothing by the third game.
  (b) **`RoomMatchState.HoldLeave` shuts the Leave button** — for BOTH players in a competitive
  room now, from the moment the game closes until the result is settled, capped at
  `ResultGraceSeconds` (30). It used to be host-only, on the reasoning written into its own
  comment: *"a guest leaving costs nothing"*. That is true about CORRECTNESS and is exactly where
  it fails about EXPERIENCE — a guest's leaving costs nobody the report, and costs the guest the
  only sight he will get of his own result. **The two holds mean different things and the
  difference is load-bearing: the host's is correctness, the guest's is information. So the
  guest's ceiling must never be raised.** A host waits on his own machine reading his own
  recording; a guest waits on WHEN THE OTHER PLAYER CLOSES HIS GAME, which can be minutes or
  never. Holding somebody on something a third party controls is how a player gets trapped in a
  room. Pinned by `TheGuestIsReleasedAtTheCeilingEvenThoughNothingArrived`.
  **This is not politeness.** `matches/rest.ts` refuses a report from anyone who is no longer
  `lobbies.host_user_id`, and leaving hands that role straight to the opponent via
  `reassignHost` — so walking out in those seconds destroys the result for both players,
  silently. It covers the READ as well as the send: the report goes out in under a second, but
  the correction that names the winner arrives up to ~15 s later through the confirm path, so
  a hold that ended at the report would protect the wrong half. Released on completion, on
  failure, or at the cap — whichever comes first — and `NeedsLeaveConfirm` includes it, or
  the window's close button walks straight past a question the Leave button asks.
  (c) **`IsMatchActive` covers the same window**, so closing the LAUNCHER during it asks too
  (`MpCloseDuringResultBody`, a different message: nothing is running to be cut short, and
  `EndMatchAsync` must not fire for a match that is over).
  **The flag travels in `MatchContext`, never read live.** Everything above runs after the
  game closed, when the room may be gone — reading `_currentLobbyIsCompetitive` there would
  reintroduce exactly the bug `AClosedRoomCannotChangeTheAnswer` pins.
  **It also buys patience:** `ReplayRetryDelaysCompetitiveMs` (~16.5 s of delay, sized to stay
  inside the 30 s hold — change one, look at the other) and `MaxCandidatesOpenedCompetitive`
  (24). The short ladder exists because almost no match is recorded, so waiting taxes the
  majority; a competitive room inverts that ratio by construction, since the host has just
  confirmed Record Game. `MaxCandidatesExamined` stays at 5 either way — it counts recordings
  that PARSED, and a sixth real one holds no sixth answer.

- **ABANDONING a match after five minutes counts as a defeat — 1v1 ONLY, and the launcher spent
  a while promising it to everybody. The one rule in the
  project that moves rating from an ABSENCE of evidence, so read the brakes before touching
  it.** The exploit: the player who is losing closes his launcher, the game never writes an
  ending to the recording, the report goes down as "nobody won", and he keeps his rating.
  Nothing in the file can fix that; the only witness is the server, which was holding his
  socket.
  It is defensible because it is the universal convention of competitive ladders — a
  disconnect is a loss — and because the host agrees to it in writing before the room exists
  (`MpCreateDialogCompetitiveForfeit`).

  **It is 1v1 ONLY, and the launcher used to promise it to team rooms as well.** `decideByAbandon`
  refuses anything but two participants, so in a 2v2 or 3v3 room the create-room hint and the
  lobby's "before you start" item were both threatening a forfeit the server never carries out.
  Both now ask `RoomFormats.AbandonmentApplies(format)` — which is true for `OneVOne` and nothing
  else, deliberately including `Casual` and `Unknown`, because a rule written as "not 1v1" fires
  on those too. The forfeit clause was moved OUT of `MpCreateDialogCompetitiveHint` into its own
  string for exactly this reason: the two clauses that stayed (confirm Record Game, cannot leave
  until the result is sent) are true for every format, since both key off the competitive flag
  alone. **It is not defensible as a default**, which is why it
  only ever applies to a competitive room.
  **Detection is server-side and never claimed by a client.** `handleDisconnectCleanup` writes
  `lobby_abandons` (migration `0008`) in the SAME batch as the membership cleanup, via
  `INSERT ... SELECT ... WHERE id = ? AND status = 'in_game' AND competitive = 1` — one
  statement, no extra round trip on a hot path, and it asks the authoritative row rather than
  the room object's in-memory `startedAtMs`, which a restart would have cleared while the game
  carried on. The row is deleted the moment that user says hello again.
  **The decision is the pure `src/elo/abandon.ts`, evaluated lazily inside `POST /matches`** —
  no timer, which matters because **there is no periodic sweep in this server**
  (`sweepOrphanLobbies` runs once, at startup). It only ever runs when ratability said
  `no_decided_result`: **a recording that names a winner always outranks an inference.**
  **The brakes, and each closes a specific hole:** exactly two participants; the report must
  have carried a recording (`replay_sha256`/`game_seed`) — without it farming is "open a room,
  wait out the timer, alt-F4, repeat" with no game played, and requiring one puts every such
  match under the existing anti-duplicate index; at most one abandonment-decided match **per
  pair per 24 h** (`PAIR_COOLDOWN_MS`), because real disconnections are scattered and farming
  is repetitive; the walkout must be at least `RECONNECT_GRACE_SECONDS` (90) old; and **the
  walkout itself must be at least `Config.competitiveAbandonSeconds` into the match**
  (`COMPETITIVE_ABANDON_SECONDS`, default 300 — policy, tuned with a restart like
  `rankedModIds`). **Both players gone is a draw**, since the usual cause is the host's
  connection dying and taking the room with it.

  **That last brake used to measure the REPORT instead of the walkout, and it forfeited people
  the rule was written to protect.** The check was `nowMs - startedAtMs`, where `nowMs` is when
  the host's `POST /matches` lands — i.e. when the HOST closed his game. So the question it
  actually asked was "had the room's match been going five minutes by the time the host
  reported", which says nothing whatsoever about when the person being forfeited left. Measured
  on the incident that surfaced it: a player left at **4:40**, the host kept his game open and
  reported at **~15 min**, and the rule read fifteen minutes and took **176 points**. He would
  have been forfeited leaving at thirty seconds just the same, and the create-room hint he
  agreed to says the opposite in as many words. `lobby_abandons.disconnected_at` was already
  stored and already read — it was simply never compared against the start.

  Each walkout is now judged on **its own timestamp**, against the two limits separately, and
  the refusal names WHICH one applied (`reason` is what the server logs and what `admin.ts
  match:show` prints — it is the only account anyone disputing a forfeit will ever get; "the
  game only ran 900s" would have been false twice over). **The new check REPLACES the old one
  rather than joining it**: a walkout ≥ 300 s in whose report arrived ≥ 90 s later implies
  `now - started ≥ 390 s`, so keeping both would leave a condition that can never fire. A
  negative `secondsIntoMatch` — clocks disagreeing about a room that started after somebody
  left it — lands in the too-early branch, which is the safe direction. Pinned by
  `a walkout inside the first five minutes is not rescued by a long match`; its sibling
  `a walkout past the threshold still decides, promptly` is what stops the fix from quietly
  disarming the rule.

  **The other half of that incident was that the guest had never been told the rule.** It was
  written in exactly one place — `MpCreateDialogCompetitiveHint`, the create-room dialog, seen
  only by the HOST. Whoever JOINS gets the competitive badge, whose tooltip says "this match
  counts towards the rating", and nothing more. So the launcher now states it as the **third
  item of the lobby's "BEFORE YOU START" card** (`PreflightAbandonRow` / `PreflightAbandonText`,
  `MpPreflightAbandon`), shown only in a competitive room.
  **Why that card and not a chat line:** the chat auto-scrolls and keeps 500 rows, so a line
  posted on entry is gone before the match starts — the same reason the Record Game reminder
  stopped being only a chat line. The card lives in `Grid.Column="0"`, which `InGameOverlay`
  covers and `ApplyMatchPhaseUi` collapses during the match, so it hides itself for free; and
  `RefreshPreflightChecklist` runs from `RenderRoomPanel`, so a host migration re-evaluates it.
  It reads the LIVE `_currentLobbyIsCompetitive`, which is correct **here and only here** — a
  pre-match surface shown while the room exists, the same field and method that already drive
  the badge two lines away. The "never read it live" rule belongs to the post-match path, where
  the room may be gone (`AClosedRoomCannotChangeTheAnswer`).
  **The wording mirrors the create-room hint word for word, deliberately**, so the guest reads
  the same rule the host agreed to. "Five minutes" is spelled out in BOTH strings and the number
  the server actually enforces is `COMPETITIVE_ABANDON_SECONDS` — **three places, change them
  together.**
  **Why 90 seconds, and it is NOT mainly about reconnecting.** Closing the launcher the moment
  a match ends is normal behaviour and drops the socket exactly like a rage-quit does; only
  WHEN tells them apart, and the window has to span the gap to the host's report, which
  stretches while the retry ladder runs. (The reconnect reasoning still applies — the socket
  retries with backoff up to 30 s — but note that an abrupt close deletes the `lobby_members`
  row, so a dropped player usually cannot re-enter the room at all; do not lean on that path.)
  **What it deliberately does NOT cover, and don't widen it on a hunch:** the game is launched
  re-parented under `explorer.exe` so it survives the launcher being force-closed, so a player
  CAN close the launcher and keep playing, and that is scored as an abandonment. It is narrow
  — the match must also have ended with no readable outcome — and it is why protection (c)
  above warns before the launcher closes.
  `matches.decided_by = 'abandon'` is a SENTINEL, not a user id (every other writer stores the
  player whose late reading decided it; uuids cannot collide with the word). `admin.ts
  match:show` prints it, the room's mode and any walkouts — the first question anyone asks
  about a match that did not score.

- **What scores, what does not, and who decides — the ELO rules.** The short version:
  **only Wars of Liberty, only 1v1, only with a readable recording.** The long version is
  worth reading before touching any of it, because every clause below was a bug first.

  **(1) The SERVER decides, and says WHY.** `POST /matches` answers `rated` plus an
  `unrated_reason`, and the launcher only renders it (`MatchOutcomeView.UnratedNoteKey`).
  This is not tidiness. The policy used to live on both sides, and they drifted: the card
  told the player "no contó para el ELO de nadie" while the backend was busy feeding that
  exact match to Glicko as a draw between everyone. **Never re-derive "did this count" in
  the launcher** — it cannot know which mods have a ladder, and the day that list changes
  it would be wrong again.

  **(2) An unrated match is STORED, never rejected.** The history is a record of what was
  played; rating is a separate judgement about it. The reasons are `mod_not_ranked`,
  `not_competitive`, `not_1v1`, `no_decided_result`, `no_lobby`,
  `participants_not_in_lobby`, `implausible_timing` and `duplicate_recording`, each with its
  own string — because
  "tick Record Game" is the right advice for a missing recording and useless for a team
  game, and sending someone to fix what was never the problem is worse than saying nothing.

  **(3) 1v1 comes for free, and always did.** `MatchResultResolver.ResolveHostResult`
  refuses anything but exactly two participants (a recording names ONE loser, which says
  nothing about the other three), so a team game has only ever reported all-0.5. What was
  missing was the server declining to rate that — which is why team games silently moved
  ratings for months. The server now requires exactly two participants itself, which also
  stops a patched launcher claiming a decided three-player match.

  **(3b) A team game is RECORDED with its real sides**, joined to accounts through the in-game
  names the room publishes (see the identity-bridge paragraph in the `.age3Yrec` section). The
  launcher used to send `team = 0` for everybody. The backend needed nothing for it:
  `match_participants.team` has existed since `0001_initial.sql` and `attachParticipants` already
  selected and emitted it — it was simply always 0.

  History groups the sides only when `MatchParticipantsView.HasTeams` is true, i.e. two or more
  distinct teams. **A 1v1 and every pre-existing row report team 0 for everybody, so they render
  exactly as before** — that equivalence is the property to protect, and it is pinned by
  `AOneVersusOneHasNoTeamsToDraw`.

  **(3c) TEAM GAMES NOW RATE, on a separate ladder. This bullet used to describe the plan; it
  now describes what shipped, and the differences from the plan are the interesting part.**
  `elo_ratings` carried `mode TEXT NOT NULL DEFAULT 'default'` with `PRIMARY KEY (user_id, mode)`
  and `idx_elo_rating (mode, rating DESC)` from day one — `0001_initial.sql` says in as many
  words that it exists for *"per-mode ratings later (1v1 / team / FFA)"* — so `mode = 'team'`
  cost **no migration**. 2v2 and 3v3 **share** it: team games are rare and the community is
  small, and splitting a scarce category leaves both halves permanently provisional against the
  leaderboard's `rd <= 110` + 3 decided matches. Everything that DISPLAYS a rating still shows
  the 1v1 one — the chip, the rooms list, the players panel, the roster — so switching this on
  could not touch the ladder that has history. The Ranking subtab is the only surface that shows
  both, behind a selector.

  **`ReadOutcome` was NOT changed, and that is the good news the plan did not expect.** It
  already hands back `LoserSlot` for a match of more than two players — it only refuses to name
  a *winner*, which is a slot and genuinely does not exist for a side. So `Confidence` still
  means "a clean 1v1 verdict" for every existing caller, and the team path reads
  `SignaturePresent` + `LoserSlot` instead. **Naming one loser names a whole SIDE**, and the
  other side is what is left; that is the entire idea and it needs no new bytes out of the file.

  **What DID change, in order:** `MatchResultResolver.ResolveTeamResults` (new, pure) turns the
  loser's slot into every player's score by joining it to the team map through the same in-game
  names, refusing on three sides, uneven sides, an AI, a slot nobody claims, or a duplicate name;
  `ratability.ts`'s `not_1v1` became `matchShape`, which keeps that answer for a free-for-all and
  for sides that do not pair up; and **`glicko2.ts`'s round-robin, which was actively wrong** —
  it decided each pairing by comparing `result`, so two teammates with the same score were fed to
  Glicko as a **draw between themselves** (measured: 1900+1100 beating 1500+1500 gave the 1100
  **+354** and the 1900 **−36**). `areOpponents` now skips same-side pairs, and a match with no
  sides answers true for every pair, so 1v1 pairing is byte-for-byte what it always was.

  **`ParticipantResult` is untouched and still `1.0 - hostResult`.** It is right for a 1v1 and
  the team path simply does not go through it — pinned by
  `TheHostsOwnTeammateIsNeverMarkedALoser`, which is the bug that would otherwise mark the host's
  own partner a loser.

  **Evidence required: at least one reading from EACH side, agreeing, on the same game**
  (`teamEvidenceMet`, pure and tested). Not "N readings for an NvN": what has to be prevented is
  a side lying about its own result, and only a witness from the other side breaks that — three
  readings from the winning team are one claim three times. **The host's report IS his side's
  reading**, so what the rule looks for is one agreeing confirmation from the opposing side,
  which makes the achievable minimum two readings for any format. `same_game` must be an explicit
  `'true'`: `'unknown'` means one side had no seed, and accepting it would let readings of
  DIFFERENT games corroborate each other by coincidence. Being this strict is affordable
  precisely because the ladder starts empty — a slow fill costs nobody a rating they already had,
  which was not true when the same question was asked of 1v1.

  **The sequencing consequence, and it is the genuinely new machinery: a team match is NOT rated
  when it is reported.** It is stored with `rated = 0` and the new, TEMPORARY reason
  `awaiting_confirmation`, and `maybeRateAwaitingTeamMatch` releases it when the witness arrives.
  Checked from BOTH directions because the order is not guaranteed — the side that just lost
  leaves first, so their confirmation routinely lands before the host has reported at all. The
  row is CLAIMED with a conditional `UPDATE ... WHERE unrated_reason = 'awaiting_confirmation'`,
  the same guard that stops the late-reading path rating one match twice, and a failure puts the
  row back rather than leaving it marked rated with no ratings behind it. It never touches
  `match_participants.result`: unlike its sibling it decides nothing, it only releases a result
  that was already read.

  **`teamEvidenceForMatch` recomputes `agreement` and `same_game` in memory rather than reading
  the stored columns, and that is load-bearing.** At report time `tieConfirmations` has not run
  yet, so those columns are still NULL for confirmations that arrived first — which is the COMMON
  ordering. Reading the columns there would report "no evidence" for exactly the case the feature
  exists to serve.

  **The guest must compute his own side too, or none of this ever fires.** `TryConfirmMatchAsync`
  sent `replay?.HostResult`, which is always null in a team match — so every confirmation of a
  2v2 would have been a 0.5, landed as `inconclusive`, and the evidence rule could never be
  satisfied by anybody. Report and confirmation now share one `ResolveTeamResults` helper, which
  is also what stops two honest players contradicting each other over a file they both read
  correctly.

  **Migration `0010` added `matches.rating_mode`** — which ladder a match moved — because
  `rating_before`/`rating_after` on `match_participants` could not otherwise be attributed once
  two ladders existed. NULL means a row from before it, all of which were `'default'`; readers
  must treat NULL as that, not as unknown. The leaderboard's win/loss tally is now scoped by it
  too, or a player's 1v1 record would quietly be padded with their team wins.

  **`scripts/admin.ts`'s `recomputeLadder` was a live bomb and is defused.** Its
  `UPDATE elo_ratings SET …` had **no `WHERE mode`**, and it had no team guard, so the first
  operator ladder replay after a team match was stored would have flattened both ladders and fed
  the team match through the old round-robin. It now carries each match's `rating_mode` through
  the replay. The reset statement still has no `WHERE`, deliberately — it replays BOTH — and the
  comment says so, because narrowing this function to one ladder without narrowing that statement
  would silently wipe the other.

  **The one thing NOT known, and it decides whether any of this ever fires: whether a team
  recording writes an outcome block at all.** The old figures (1v1 21/28, four humans 0 of 1) were
  taken with the 8-byte trailer bound, and **that bound turned out to be the reason most of the
  1v1 misses were misses** — re-measured with the 512-byte scan it is 20 of 20. The single 2v2 has
  never been re-read, so it is still one sample, and now a doubtful one. A 43-file folder
  collected for exactly this question held no team game at all (19 readable, all 1v1; the other
  10 were not recordings — a PNG, an `.exe`, four of zeroes). The design is safe either way:
  with no block the match reports 0.5 for everyone and stays unrated, which is what
  every team game did before. `TryReportMatchAsync` logs `loserSlot` / `teams` / `sides` for every
  team match so the first real ones answer it — **do not remove that line until they have.**

  **Abandonment was deliberately NOT extended to team games.** `AbandonmentApplies` is still
  `OneVOne` only and `abandon.ts` keeps its own `!== 2`. The rule exists for when no recording can
  decide, and in a team game it would take rating from two or three people for one person's
  dropped connection — while a team match that finishes is decided by the recordings anyway.

  **(4) `games_played` counts RATED matches**, not played ones — a consequence of (1)
  that made two launcher strings lie until they were reworded (`MpProfileGames`,
  `MpProfileProvisional`).

  **(5) The anti-replay fingerprint is the SHA-256 of the recording FILE**
  (`ReportMatchRequest.ReplaySha256` → `matches.replay_sha256`, partial UNIQUE index).
  Not the game's contents (map + player names): a rematch on the same map with the same
  people is a different match and must still score, and a content-derived identity could
  not tell them apart. Free side effect: it is the idempotency key `POST /matches` never
  had. **What it does NOT stop is two colluding accounts** — that would need the loser's
  client to corroborate, or the server to parse the file. Both were considered and left out.

  **(6) Participants are checked against the roster frozen at Start**
  (`lobbies.roster_at_start`, written by `LobbyRoom.handleStart`), **not against
  `lobby_members`** — leaving a room DELETES that row, and the player most likely to leave
  first is the one who just lost, so live membership would reject most real matches while
  catching almost nothing. The question worth asking is "did these people play", and Start
  is when it has an answer.

  **(7) A rating is shown wherever a player's name appears — and "provisional" is
  NOT part of it.** This reverses an earlier rule of mine, so read the reason before
  reinstating it.

  The old rule withheld the server's starting 1500 (`rd > 110`) on the grounds that
  showing it passes a placeholder off as earned skill, and labelled it "provisional"
  where it did appear. **The flaw: every player who has not played is on exactly 1500.**
  A number everybody starts from, shown to everybody, claims nothing about anyone —
  while hiding it left the rating blank in the chip, the room roster and everywhere
  else, which is what actually got reported, twice.

  So `RatingDisplay.ShouldShow(double? rating)` is now simply "is there one", and the
  rating appears in five places: the title-bar chip, the Profile tab, the room roster,
  the rooms table (beside the host) and the global players panel.

  **The refusal that SURVIVES is the one that was always the point: a null rating paints
  nothing.** Null is not somebody's 1500, it is not knowing — the state the app was in
  the day the backend answered 502 to every rating fetch — and putting a number there
  would be the actual invention. Pinned by `RatingDisplayTests`.

  **Two deliberate exceptions.** The RANKING card keeps its `rd <= 110` +
  `wins + losses >= 3` filter: showing 1500 next to a name informs, but ordering a
  league table of people who never played does not — they would all tie, in an
  arbitrary order. And the Profile tab plus the end-of-match note still say
  "provisional", because there the word explains something real (you have no rated
  matches; that swing was large) rather than qualifying a bare number.

  **WHO fills in the starting 1500 is the SERVER, and the client must never do it.**
  This is what the reversal above actually cost, and it took a second report to find:
  the rule was applied in the launcher and NOT in the backend, which kept three copies
  of the old one. `GET /lobbies`, the presence frame and the room roster all sent `null`
  for a player with no `elo_ratings` row, and after the ratings reset that is EVERY
  player — so the rooms table and the Players panel stayed blank while the chip read
  1500, because `GET /matches/elo/:userId` had always synthesised the default. Two
  endpoints of the same server disagreeing about the same person.

  The server fills it in because **the server is the only side that can tell the two
  cases apart**: it ran the query, so it knows the difference between "no row, therefore
  unrated, therefore 1500" and "I could not answer". The launcher cannot — a missing
  field looks identical to an older backend or a failed fetch, so substituting there
  would paint 1500 for both and that IS the invention the surviving rule forbids.
  `src/elo/glicko2.ts` settles it: `row?.rating ?? DEFAULT_RATING` is what already
  RATES an unrated player's first match, so refusing to SHOW that same number was the
  incoherence. The defaults are exported from there (`DEFAULT_RATING` / `DEFAULT_RD` /
  `DEFAULT_VOLATILITY`) and used at all four sites, because typing 1500 out per site is
  how they came to disagree.

  **`null` on the wire now has ONE meaning: no answer.** `GlobalChatRoom.onlineUsers`
  keeps a `ratingsKnown` flag for exactly that — a user missing from the map after a
  SUCCESSFUL query is unrated and gets the default, while a query that THREW leaves
  everybody null, because it told us nothing about anyone. Collapsing those two would
  either hide every rating again or invent one for a lookup that failed.

  **The two `LEFT JOIN`s stay, for a reason unrelated to the default.** In
  `lobbies/rest.ts` an inner join would make **every room whose host has no rating row
  vanish from the rooms list** — worse than a missing number, and silent. In
  `LobbyRoom`'s hello that same query **is the membership check**, so an inner join
  would throw everyone with no rating out of the room with `4004 not_in_lobby` — right
  after a reset, everyone.

  **The RANKING is untouched by all of this** because it selects `FROM elo_ratings`
  (`stats/rest.ts`), i.e. real rows only: a synthesised default cannot reach it, so
  someone who never played still does not appear in the table.

  **(8) The reset.** `scripts/reset-elo.ts` emptied `elo_ratings` and nulled every
  `rating_before`/`rating_after`, because those numbers were produced by the bug in (1).
  `matches` and `match_participants.result` were untouched — the history is whole. It is a
  script and NOT a migration on purpose: a migration is remembered in the `_migrations`
  table of the database it ran against, so restoring the backup and starting up would
  re-run it and delete the ratings just restored. Rollback is the `.backup` file named in
  `DEPLOY.md`.

  **(9) `match_reported` is published BEFORE `rooms.close`, and that order is
  load-bearing.** The room closing is how the match used to end for the guest, who has no
  recording and so polled their own history three times over fifteen seconds hoping the
  row had been written. The frame carries every participant's **`result`** — not just the
  ratings, which is the trap: without the result the guest's card would say "no result"
  even when they had won. `OnRoomDisconnected` gained a `_matchPhase != Result` guard, or
  the close arriving behind the frame re-enters the result phase and fires the very polls
  this removed. `ResolveGuestResultAsync` stays as the old-backend fallback.

  **(10) The whole community strip is ONE endpoint** (`GET /stats/community`), and so is the
  RANKING subtab, and so is the team ladder. The budget is per IP and shared behind a Radmin
  NAT, so a second route would cost double for nothing — every card and every table added
  since rides the same payload and the same 60 s server-side cache, and costs **no extra
  request**. The limit asks for the server's maximum (50) because one payload feeds a
  three-row summary and a full table; asking twice for two sizes would double it for nothing.
  `rank` comes from the server and **must not be renumbered** client-side.

  **It is re-fetched at most once a MINUTE, and it used to be once a SESSION.** The old
  `_activityLoaded` bool meant a player never saw their own place change without restarting
  the launcher — untenable the moment a second ladder arrived, since the first thing anyone
  does after a rated match is look. `_activityFetchedUtc` + `ActivityMaxAge` is exactly the
  backend's own memo duration, so a refresh inside that window would have been answered from
  memory anyway. **Change one and change the other.**

  **The RANKING subtab is where the whole table lives; the strip keeps showing three.** A
  strip that grew with the league would push the rooms list off the screen the week it
  filled — which is why the strip is capped at 3 and not because three is interesting. The
  subtab carries the 1v1/TEAM selector, and **the TEAM button is hidden when the payload has
  no `leaderboard_team` at all**: null there means an older backend with no team ladder,
  which is not the same as one nobody has qualified for yet, and offering a tab that can only
  ever be empty is worse than not offering it. The selector reuses the `SubTab` style rather
  than `MpSegment`, which lives in `CreateLobbyDialog` and is not reachable from this file.
  **The five column widths are now written in THREE places** — the strip's XAML header,
  `BuildLeaderboardRow`, and `BuildRankingHeader` — and must be changed together. Peak hours is bucketed
  from `lobbies.created_at` — rooms OPENED, which is what the card's wording says, not
  matches played — sent in UTC and shifted to local by `CommunityStatsView.ToLocalHours`;
  below `MinSampleRooms` the card hides rather than dressing four rooms up as a finding.

  **THE STRIP MUST NOT DEPEND ON THE VIEWER'S OWN HISTORY, and it used to — which made a
  panel headed "community activity" invisible to anyone who had not played.**
  `RefreshActivityStripAsync` opened by fetching the caller's match history and `return`ed
  when it was empty, so the ladder and the peak hours — community data, present all along —
  **were never even requested**. Community stats are fetched FIRST now and each card reports
  whether it drew anything; the strip is shown when any of them did.

  **A hidden card has to give up its COLUMN and its DIVIDERS too — `LayOutActivityColumns`.**
  This is the non-obvious half and it is what made the middle third look broken: a star
  column keeps its share of the width whatever its child does, so collapsing a card left a
  third of the strip reserved and blank, and the two dividers were unconditional, so that
  blank third came framed by two rules. The XAML comment promising that "an absent card
  reads as *not yet*" was only true of the card, never of the space it left. The left rule
  is `recent && (middle || peak)`, not `recent && middle` — with the middle card gone it is
  what separates the two that remain.

  **The middle third is the community's NUMBERS, with the ladder under them.** Stacked
  rather than given a column each: a fourth column leaves every one of them too narrow for a
  player name at the smallest window this tab supports. The numbers are `totals` —
  `matches` in a window, `players` seen in a shorter one, and the most-played map — and each
  window travels WITH its figure, so a card that hardcoded "30 days" cannot start lying the
  day that constant moves. **`totals: null` is not zeros**: an older backend reports nothing
  and the card is not drawn, while a genuine 0 IS shown, because a quiet month is a fact and
  an unwelcome fact is still worth knowing. Both windows are measured against
  **`matches.created_at`, the server-stamped column, never `started_at`**, which the client
  sends and one wrong clock would skew — the same reasoning that already makes the histogram
  read `lobbies.created_at`. It costs a scan of `matches` (`created_at` is not indexed),
  which is no worse than the histogram's existing scan of `lobbies`.

  **The ladder now explains its own emptiness instead of vanishing.** `rd <= 110` +
  `wins + losses >= 3` means that after a ratings reset **nobody** qualifies for weeks, and
  the card hid itself — which is where the blank third came from. It shows the requirement
  instead, from the server's own `min_decided`, and **only when that is greater than zero**:
  an older backend has no such field, it deserializes to 0, and "you get in with 0 decided
  matches" is both wrong and impossible (`CommunityStatsView.RequiredDecided`). The table is
  capped at 3 rows, not 5, so the strip does not lurch taller the week the ladder fills.

  **RECENT MATCHES is the COMMUNITY's, and says who won.** It used to be the viewer's own
  history under that heading. The server sends the last few matches from `matches` ordered by
  `created_at` with the participants attached by **the same `attachParticipants` the history
  endpoint uses** — one query for the whole page, never one per match. The sentence "X beat
  Y" is written by `CommunityStatsView.Describe`, which is built on `MatchParticipantsView`
  and therefore inherits its refusals: **only a two-player match with one winner and one
  loser is described**, so the 0.5 that most stored matches carry names nobody, and a team
  game names nobody either. Everything else keeps the old shape — mod, map, "didn't count".
  A backend with no `recent_matches` falls back to the viewer's history **under the old
  heading**, because calling that "community matches" would be a lie.

  **This strip is the ONE place in the tab that went up to the type scale's 13 floor**
  (`MpActivityTitleSize` / `MpActivityBodySize` / `MpActivityHeadlineSize`). The rest of
  multiplayer keeps the handoff's 10.5/11.5 — see `MpLabelSize`, ratified twice — and these
  are separate tokens precisely so the two decisions stay separable and neither leaks into
  the other. The ladder's fixed column widths were widened in the same change (`26 / * / 56
  / 72 / 48`), because "DECIDIDAS" does not fit 62 at 13 px; **the XAML header and
  `BuildLeaderboardRow` carry that list twice and must be changed together.**

  **(11) Only the HOST measures. The opponent's score is an inference, not a
  measurement** — `MatchResultResolver.ParticipantResult` is literally
  `1.0 - hostResult`. Correct for a 1v1, where there is exactly one winner, but it means
  the server gets ONE reading of something two machines can read.

  Both of them already read it. `OnGameExitedAsync` runs on **every** client and
  `AnalyseMatchReplayAsync` is not gated on the host, so the guest finds their own
  recording, validates it against their own slot, and reaches an independent verdict.
  **Watch the names there:** `hostName` is `GetInGameName` — *this machine's* profile —
  and `hostSlot` is `FindPlayerSlot(header, hostName)`, so `MatchReplayInfo.HostResult`
  means "the result of the player THIS machine belongs to", which on a guest's PC is the
  guest. (It used to say `outcome.RecorderSlot`, which is what made both machines read the
  loser and answer `0.0`.) They read
  the other way round and are documented in place rather than renamed, because
  `ResolveHostResult` / `HostResultFrom` in the tested pure service are named to match
  and renaming half the set would be worse than leaving it.

  That second reading is now SENT — `TryConfirmMatchAsync` → `POST /matches/confirm` →
  `match_confirmations`. **It began as evidence that gated nothing, and it no longer is:**
  a reading may now DECIDE a match the server stored without a result, under the rule in the
  late-reading bullet above. What has not changed is that it cannot overturn a decided one,
  and that reporting is still host-only. Reporting stays host-only (N reporters would insert N
  copies of one match). The server compares it with `compareReadings` and STORES the
  verdict in `match_confirmations.agreement` / `same_game` — it used to only write a log
  line, and the log rotates; `inconclusive` when either side is 0.5, because "nobody could
  read it" is not "they contradict each other" and merging the two would make the data
  measure the wrong thing. The table is keyed by `(lobby_id, user_id)` and NOT by match id, because the
  guest usually leaves the game before the host and their confirmation routinely arrives
  before the match row exists; the lobby row always exists, since those are never deleted.

  **What this is for, and what it is not.** With the roster gate in (6), a cheater
  already needs a second account that really joined and played — and if they control
  both, they control both readings, so **this does nothing against two colluding
  accounts**. What it catches is the likelier thing in a small community: **a host who
  plays a real opponent, loses, and reports a win.** Deciding whether to actually
  REQUIRE agreement is deferred on purpose until the data says how often the second
  reading arrives at all; `DEPLOY.md` has the query, and "no confirmation" is the number
  that decides it.

  **(12) The MATCH is identified by `gamerandomseed` + `gamehosttime`, and matching
  player NAMES was considered and rejected.** Two keys the `.age3Yrec` settings
  dictionary has carried all along (`ReplayHeader.RandomSeed` / `HostTime`; the parser
  already walked the whole dictionary, they are simply surfaced now). The seed is what
  makes every machine generate the same map, so both players of one game carry it and
  two different games do not.

  Measured on six real recordings before any of this was written: six different seeds,
  including **two back-to-back games by the same host, host clocks fifteen apart, seeds
  22235 and 15346**. Neither a player name nor a timestamp separates that pair. Pinned
  against the real fixture in `ReplayParserTests` (seed `21427`, clock `1310758`) — the
  numbers are read out of a genuine file, not invented to match a reading of the format.

  It buys two things: (a) the server can tell whether the host and their opponent read
  the **same** match, which is what makes the second reading in (11) a real cross-check
  rather than two opinions about possibly different games; and (b) an anti-reuse key
  that identifies the **game** rather than the **file**, so re-packing a recording no
  longer slips past the SHA-256 in (5).

  **Rejected on purpose FOR THIS CHECK: comparing AoE3 profile names.** Publishing each
  player's in-game name in the room and checking the other human in the recording against it
  was the obvious design and is a worse one *here* — profile names are frequently nothing like
  the Discord account, changing them in AoE3 barely works, and a player with a blank or odd
  name would silently stop being verifiable. The seed needs no name at all. Don't re-propose it
  **for `same_game`**.

  **Note the scope, because the room DOES publish those names now** (`set_ingame_name`, see the
  identity-bridge paragraph in the `.age3Yrec` section). That is for the TEAM MAP, where there
  is no seed-shaped alternative — the name is the only link between a recording slot and a
  person — and where the objections above become the design rather than an argument against it:
  the name is self-declared instead of guessed, it is resolved per MOD, and anything odd makes
  the map refuse outright rather than produce a wrong answer. `same_game` still uses the seed
  and must keep using it.

  **Load-bearing details:** the pair is indexed together and **never the seed alone**
  (largest value seen is 32747, about 15 bits — alone it would collide across unrelated
  matches); a 0 or absent value is stored as **NULL, never 0**, and the unique index is
  partial, so a scenario or an unreadable field can never block a legitimate report; and
  **`game_host_time` is recorded but takes no part in the verdict — though the reason for that
  has now been answered, and the answer is that it COULD.** A match captured from both machines
  has `gamerandomseed` **13911** and `gamehosttime` **1310730** in both copies, identical. So the
  clock is a property of the GAME rather than of the machine, and `same_game` could use the pair.
  It still uses the seed alone, because one two-machine sample is enough to remove a doubt and not
  enough to tighten a gate that would silently stop rating matches if it were wrong. Widen it when
  a second capture agrees. The original wording follows, for the record:

  Only one side of
  each match was available to measure, so whether the guest's recording carries the same
  clock is plausible and unproven — `same_game` is decided on the seed alone until a
  two-machine test settles it. `DEPLOY.md` has the query that does.
