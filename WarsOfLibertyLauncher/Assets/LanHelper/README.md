# LAN helper

`avifil32.dll` is the launcher's own build of things-new's lanAutoStart steps (`Gorgorito12/things-new`,
target `LanHelper`, Win32 Release, `src/lanhelper.cpp`; built 2026-11-20 in `Unharcoded-AOE3- NEW`).
The game imports `AVIFIL32.dll`, so it loads this file from its own folder, and the file forwards
every call to the system's. It reads the launcher's `+uhcLanHost` / `uhcLanJoin="<address>"` /
`uhcLanRules=N` and presses the LAN screen's own buttons.

It is a file of its own on purpose. It has no cfg, writes no byte of the game and hooks nothing of
Direct3D, so it sits beside a mod's own `UHC.dll` and a mod's own `dinput8.dll` whatever they bring.
Without an action on the command line it starts nothing: a game opened by hand is the game without
this file. When a cfg of the folder already turns `lanAutoStart` on, it leaves the entry to that DLL.

Until launcher v1.1.0 the helper was `dinput8.dll` (things-new's EngineExt, 0.4.0) plus two marked
lines in `engine-ext\engine-ext.cfg`. That name and that cfg are the mod's now. The launcher takes
the earlier pair away when it installs this file, and only when the DLL's hash is in
`LanHelperService.LegacySha256`; the real 0.4.0 file is kept as a test fixture
(`WarsOfLibertyLauncher.Tests/Fixtures/lan-helper-0.4.0-dinput8.dll`).

The launcher embeds it (`LanHelper.avifil32.dll`) and installs it only when the player agrees:
`Services/Multiplayer/LanHelperService.cs`.

To replace it: build things-new (`cmake --build build --config Release --target LanHelper`), copy
the new `avifil32.dll` here, put its SHA-256 in `LanHelperService.CurrentSha256` and keep the old
hash in `KnownSha256` (that list is how an installed older copy is recognised as ours and updated
or removed). `LanHelperServiceTests.TheEmbeddedDllIsTheOneTheHashNames` fails when the two disagree.
