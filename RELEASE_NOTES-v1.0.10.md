# AoE3 Mod Launcher v1.0.10

Two new mods, community add-ons, and two fixes that could cost you your Age of Empires III.

## New

- **Napoleonic Era** and **Struggle of Indonesia** can now be installed and played from the
  launcher. Both ship the original Age of Empires III executable, which is why no launcher
  could run them before. Installing one asks for administrator **once**; updates and repairs
  never do. Your Age of Empires III stays untouched and playable.
- **Add-ons.** A new **ADDONS** tab in a mod's properties, with the three Age of Empires
  Heaven add-ons ready in one click. They survive updates and repairs, and disabling one puts
  back exactly what it replaced.
- **Mod links.** Mods can show their Discord, ModDB page, forum or wiki on their Workshop
  page. Hovering a link shows the full address before you click.
- **Low disk space warning** before any download — installing, updating, repairing, applying a
  translation or grabbing an add-on. It tells you which drive is short and lets you continue
  anyway.

## Fixed

- **Uninstalling could delete your Age of Empires III.** If an older launcher had installed a
  mod straight into your game folder, uninstalling it removed the whole folder, base game
  included. The launcher now refuses to delete a folder it recognises as a real Age of
  Empires III install, whatever its own records say.
- **Your saved games could end up inside a mod's folder.** Mods that share the base game's
  save folder get it redirected while they run, and that redirect was only undone when the
  launcher restarted — which can be days, since it lives in the tray. It is now undone the
  moment you close the game.
- **"Play" could get stuck.** With Wars of Liberty the button could stay on "PLAYING…" with no
  way to close the game. It now becomes Stop whenever a game is running.

Also: a mod that dies seconds after launching is reported as a failed start instead of looking
like a short session, cancelling the Windows administrator prompt is no longer treated as an
error, and pressing Stop no longer closes unrelated copies of Age of Empires III.

## Installing

Download `Aoe3ModLauncher.exe` and run it. Windows SmartScreen may warn you the first time —
[INSTALL.md](https://github.com/Gorgorito12/AoE3-Mod-Launcher/blob/main/WarsOfLibertyLauncher/INSTALL.md)
explains why and what to click, and
[IS-IT-A-VIRUS.md](https://github.com/Gorgorito12/AoE3-Mod-Launcher/blob/main/docs/IS-IT-A-VIRUS.md)
covers how to verify the download yourself.

If you already have the launcher, it will offer you this update on its own.

SHA256: CF34D3F18DE700E73A15BC60C25931595067AB1DD7091A778F5939D983EBCE15
