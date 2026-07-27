# Wars of Liberty Launcher v1.0.10

Two new mods, a community add-on system, and a handful of fixes — including two that
could cost you your Age of Empires III install.

---

## Two new mods: Napoleonic Era and Struggle of Indonesia

Both are total conversions that ship the **original** Age of Empires III executable, which
is why they never worked from a launcher before: the game finds its content through a
Windows registry entry that points at your base game, so a mod installed in its own folder
would just load plain Age of Empires III.

The launcher now gives each of these mods **a registry entry of its own**, pointing at its
own folder. Your Age of Empires III keeps its own, untouched — so the mod and the base game
both stay playable, and nothing has to be undone when you close the game.

Installing one of these two asks for administrator **once**, at the start. Never again:
repairs and updates don't need it.

## Community add-ons

A new **ADDONS** tab in a mod's properties, with the three Age of Empires Heaven add-ons
ready to download and enable in one click. Add-ons survive updates and repairs — the
launcher re-applies them afterwards — and disabling one puts back exactly what it replaced.

An add-on can never touch the files that identify your mod's version, so enabling one can't
break multiplayer.

## Community links on a mod's page

Mods can now show their Discord, ModDB page, forum or wiki as buttons on their Workshop
page. Hovering one shows you the full address before you click.

---

## Fixes

**Uninstalling could delete your Age of Empires III.** If you had a mod installed straight
into your game folder by an older version of the launcher, uninstalling it removed the whole
folder — base game included. The launcher now refuses to delete a folder it recognises as a
real Age of Empires III install, whatever its own records claim.

**Your saved games could end up inside a mod's folder.** Mods that share the base game's
save folder get it redirected while they run. That redirect was only undone when the
launcher restarted — and the launcher is built to sit in the tray for days. It is now undone
the moment you close the game.

**"Play" could get stuck.** With Wars of Liberty, the button could stay on "PLAYING…" with
no way to close the game. The play button now becomes Stop whenever a game is running.

**A game that fails to start now says so.** A mod that dies two seconds after launching used
to look exactly like a short session. The launcher now tells you it failed and points you at
Verify files.

**Cancelling the Windows administrator prompt is no longer an error.** Declining it is a
decision, not a crash, and it no longer shows a red error box.

**A game that won't close no longer gets ignored.** If the launcher can't close a running
game before installing, it now stops and tells you, instead of writing over files the game
still has open.

Plus: the launcher no longer kills unrelated copies of Age of Empires III when you press
Stop, and it detects a mod installed in a folder with any name.

---

## Installing

Download `Aoe3ModLauncher.exe` and run it. Windows SmartScreen may warn you the first time —
see [INSTALL.md](WarsOfLibertyLauncher/INSTALL.md) for why and what to click.

If you already have the launcher, it will offer you this update by itself.

SHA256: <paste the hash printed by build-release.ps1 here>
