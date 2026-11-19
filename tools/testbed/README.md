# Launcher testbed

Runs the real launcher beside your own, on the CORSAIR XENEON, so a change can be seen, pressed
and measured. Adapted from `tools/testbed.py` in `Unharcoded-AOE3- NEW`: small commands an agent
runs one at a time. A run passes on the log, UI Automation answers and frames somebody looked at.
Nothing here decides "looks right".

```
python -I tools/testbed/testbed.py build
python -I tools/testbed/testbed.py safety record
python -I tools/testbed/testbed.py launch --size 1366x768 -- --open-tab=workshop
python -I tools/testbed/testbed.py shot tools/testbed/out/workshop.png
python -I tools/testbed/testbed.py uia tree
python -I tools/testbed/testbed.py uia invoke -Id TopTabPlay
python -I tools/testbed/testbed.py log --grep "TAB |HARNESS BLOCKED"
python -I tools/testbed/testbed.py close
python -I tools/testbed/testbed.py safety check
```

`npm install` here once, for `reference.mjs`. It needs Edge and the network.

```
node tools/testbed/reference.mjs design_base_launcher "Inicio de sesion v2.dc.html" 97b out.png \
     --props '{"transparencia":"Media"}'
python -I tools/testbed/testbed.py sidebyside out.png tools/testbed/out/shot.png tools/testbed/out/cmp.png
```

## What keeps it safe

- **`AOE3ML_HARNESS=1`** needs a developer build and an `AOE3ML_DATA_DIR` that is not yours
  (`Services/HarnessMode.cs`). A published launcher ignores it.
- **In that mode** the launcher has its own mutex and pipe, writes no Run key or `wol-launcher://`,
  heals no junction, and every process it would start, plus install, repair, update, uninstall and
  game launch, becomes a `HARNESS BLOCKED <what>` log line. That line is how a button is proved.
- **`launch`** seeds a data folder from the public catalog cache and answers every first-run
  question. It never reads your config, which holds a Discord token.
- **Backups, restores and imports** run for real on `out/documents` (`AOE3ML_DOCUMENTS`), copies of a
  few of your files made once. **Repair** runs for real only on an install inside `out/sandbox`
  (`AOE3ML_SANDBOX`): `launch`, `close`, `python -I repair_fixture.py`, `launch --keep`. That script
  downloads a real mod, records its files and damages one.
- **A running game is never asked about or closed:** the launcher's check goes by name, and the
  `age3y.exe` on this machine can be another project's.
- **`close`** only touches the process it started, proved by its exe path.
- **`safety`** compares the Run key, `wol-launcher://`, your launcher's pid and the settings a stray
  run could move.

## Pictures

- **`shot`** uses `PrintWindow`, which works while the window is covered and falsifies antialiasing.
- **`shot --screen`** copies the screen and refuses if another window is on top. Use it to judge text.
- **A blank white window with `PERF 0 redraws` in the log is the GPU, not the launcher:** a game in
  exclusive full screen (another project's `age3y.exe`, which is never closed) leaves WPF's hardware
  path drawing nothing. `check --software` renders the same layout; measure `perf` only with no game
  running.
- **Edge and WPF never match pixel for pixel.** WPF has no letter-spacing and the blur is emulated,
  so `sidebyside`'s percentage is a guide. Geometry is asserted in xUnit; the picture is for looking.

## Performance

`perf <scenario> --label baseline` measures one scenario in a new process each run, because
`EFFECTS REDUCED` lasts a session. `compare current.json baseline.json` fails on a broken budget.

| Scenario | What it does |
|---|---|
| `coldstart` | five starts, for `window shown` |
| `coldstart-checks` | three starts of 20 s WITH the update checks on (`launch --checks`), so the start's network work is in the measure |
| `idle`, `tray` | 60 s doing nothing, visible and in the tray |
| `workshop`, `multiplayer`, `settings`, `modsettings` | a screen left open |
| `tabs`, `scroll`, `resize` | the launcher made to work |
| `soak`, `soaktray` | 30 min switching tabs, and 30 min in the tray: what only time shows (a leak, a creeping CPU); read the per-second rows, not the summary alone |
| `pill`, `tournaments` | 60 s on the update pill breathing and on a running 32-place bracket (its beating dots) |

**A looping light runs only while the launcher is the foreground application**, and `perf` does not
bring it to the front: measured that way, the pill and the bracket show 0 redraws a second, which
proves nothing. Raise the window first (the `SetForegroundWindow` trick `shot --top` uses) when the
question is what an animation costs. Measured so on 2026-11-14, hardware rendering: the bracket
2.2 % CPU, the Rooms page 2.5 %, the pill 0.8 %. The `redraws` figure counts dispatcher render
operations, about 1.5 per frame, so a 30-fps light reads ~44 a second; compare it against another
screen, never against 30.

An A/B against an older commit: build it apart (`git archive <commit> WarsOfLibertyLauncher | tar -x`
in a scratch folder, `dotnet build -c Release`) and run the same scenario with `AOE3ML_TESTBED_EXE`
pointing at that exe, alternating the two builds so the machine's noise lands on both.

Run the same scenario with `--software` to reproduce a weak PC. Close Visual Studio's XAML tools
first: they force a frame every refresh.

## Two accounts: a whole multiplayer match on one machine

A local lobby server, two or three launchers each signed in as its own test account, and a
simulated game that lasts a set time and leaves a REAL recording, so everything after the game
(reading the recording, the host's report, the guest's confirmation, both civilizations, the
result card, rematch) runs the production code. Nothing leaves the machine: the server's Discord
and recording-storage settings are emptied, and it runs on a throwaway database in `out/server`.

```
python -I testbed.py server start --fresh       # Node 20 through npx (nothing installed), port 8799
python -I testbed.py --as a token --name "El Taita"
python -I testbed.py --as b token --name skibidi
python -I testbed.py --as c token --name Miron   # a third account, to watch the chat's room card
python -I testbed.py --as a launch --mp --half first --software
python -I testbed.py --as b launch --mp --half second --software
python -I testbed.py --as c launch --mp --half third --software
python -I testbed.py players <recording>         # who is in it, by slot
python -I testbed.py --as a fakegame <recording> --slot 1
python -I testbed.py --as b fakegame <recording> --slot 2
python -I testbed.py --as a uia invoke -Id CreateRoomButton   # then Segment:OneVOne, CreateButton
python -I testbed.py --as b uia invoke -Id RoomAction:<lobby id>
python -I testbed.py --as a uia invoke -Id ReadyButton        # and b: the countdown starts by itself
python -I testbed.py admin match:show <match id>              # the backend's own view
python -I testbed.py close --all && python -I testbed.py server stop
```

Every per-run command takes `--as`: `log`, `shot`, `uia`, `windows`, `close`. Without it the
testbed is exactly what it was. How it stays inert outside: the launcher reads
`AOE3ML_LOBBY_URL` and `AOE3ML_FAKE_GAME` only in harness mode (plain http to this machine for the
first, a recording dropped only inside the run's own Documents for the second), and
`scripts/dev-token.ts` refuses unless the server runs with `DEV_AUTH_BYPASS=true`.

`scenario_watch.py` plays one more ranked 1v1 while c opens "See match" from its Rooms row, and
leaves `out/watch-playing.png` and `out/watch-result.png`. `launch -- --open-profile=<user id>`
(harness only) opens another player's profile once signed in, since its links are text that UI
Automation cannot press.

**The simulated game lasts 190 s**: a match shorter than three minutes is not reported, by the
launcher or by the server. `fakegame` names the account's AoE3 profile after that slot of the
recording (read from the header, not from the file name), which is how the launcher finds itself
in the file. One recording per match: the server flags a recording reported twice.

**What only two real PCs over Radmin prove** (a checklist for the maintainer):

1. Both on the AoE3 Radmin network; each room row shows the other's ping.
2. The room's automatic-entry card is green on both; the first room asked once about the helper.
3. The host's log says `+uhcLanHost`, the guest's `uhcLanJoin="<host's 26.x>"` and
   `LAN auto-start enabled in <cfg>`.
4. The host lands in the hosted LAN lobby with Record Game ticked (competitive room), and the guest
   connects by Direct IP unaided, also when the guest's game starts first.
5. With the card's switch off, no `uhcLan` argument is passed and entering by hand works.
6. A real match: the result card on both, both civilizations in History, the recording uploaded.
7. A rematch between two real accounts; the invitation reads as the rematch.
8. Walking out after five minutes of a competitive 1v1, and separately a host who closes the
   launcher mid-match.

## Done means

1. The markup of the prototype was read, not only its README.
2. Each variant was compared side by side, at 1366x768, 1920x1080 and maximized, in the three
   transparencies, in both languages, and with reduced effects.
3. A frame from the middle of the video was looked at.
4. `uia tree` lists every interactive control and each one was pressed, its effect checked.
5. `dotnet test` is green and its total did not drop.
6. `compare` passes against the baseline.
7. `safety check` passes.

The report says what works, what was tested, what was not, and what is inferred.
