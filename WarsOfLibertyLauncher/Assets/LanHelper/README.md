# LAN helper

`dinput8.dll` is the EngineExt build of things-new (`Gorgorito12/things-new`, branch
`claude/lan-autostart`, target `EngineExt`, Win32 Release). The game imports `DINPUT8.dll`, so it
loads this file from its own folder and the file forwards every DirectInput call to the system's.
With `engine-ext\engine-ext.cfg` holding `lanAutoStart`, it reads the launcher's `+uhcLanHost` /
`uhcLanJoin="<address>"` / `uhcLanRules=N` and presses the LAN screen's own buttons. It writes no
byte of the game.

The launcher embeds it (`LanHelper.dinput8.dll`) and installs it only when the player agrees:
`Services/Multiplayer/LanHelperService.cs`.

To replace it: build things-new, copy the new `dinput8.dll` here, put its SHA-256 in
`LanHelperService.CurrentSha256` and keep the old hash in `KnownSha256` (that list is how an
installed older copy is recognised as ours and updated or removed).
`LanHelperServiceTests.TheEmbeddedDllIsTheOneTheHashNames` fails when the two disagree.
