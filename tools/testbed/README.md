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
| `idle`, `tray` | 60 s doing nothing, visible and in the tray |
| `workshop`, `multiplayer`, `settings`, `modsettings` | a screen left open |
| `tabs`, `scroll`, `resize` | the launcher made to work |

An A/B against an older commit: build it apart (`git archive <commit> WarsOfLibertyLauncher | tar -x`
in a scratch folder, `dotnet build -c Release`) and run the same scenario with `AOE3ML_TESTBED_EXE`
pointing at that exe, alternating the two builds so the machine's noise lands on both.

Run the same scenario with `--software` to reproduce a weak PC. Close Visual Studio's XAML tools
first: they force a frame every refresh.

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
