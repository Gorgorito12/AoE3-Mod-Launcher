# AoE3 Mod Launcher v1.0.10

Two new mods, community add-ons, settings you only configure once, and several fixes —
including three that could have cost you your Age of Empires III or your saved games.

## New mods

- **Napoleonic Era** and **Struggle of Indonesia** can now be installed and played from the
  launcher. Both ship the original Age of Empires III executable, which is why no launcher
  could run them before. Installing one asks for administrator **once**; updates and repairs
  never do, and your Age of Empires III stays untouched and playable alongside them.

## Community add-ons

- A new **ADDONS** tab in a mod's properties, with three Age of Empires Heaven add-ons — a
  transparent interface, gun smoke and weapon sounds, and a building placement rotator — ready
  to **download and enable in one click**. You can also add any add-on `.zip` from your PC.
- They **survive updates and repairs**: the launcher re-applies them afterwards. Disabling one
  puts back exactly the files it replaced.
- The launcher **refuses add-ons that would lock you out of multiplayer**, and warns you before
  enabling one that could desync a match — naming the exact files either way.
- Add-ons also work on a **plain, unmodded Age of Empires III**, not only on installed mods.

## Mods and Workshop

- **"See in Library".** After adding a mod from the Workshop, the button used to turn into a
  greyed-out pill, leaving "Remove" as the only thing you could click — one player reported
  being unable to install a mod because of it. It now takes you straight to where PLAY and
  INSTALL are.
- **Community links.** Mods can show their Discord, ModDB page, forum, wiki or videos on their
  Workshop page. Hovering one shows the full address before you click.
- **The game switcher** lists favourites first, then whatever you played most recently, shows
  each mod's icon and how long ago you played it.
- **The launcher opens on the mod you last used.** A community mod was silently discarded at
  startup and it always fell back to Wars of Liberty.
- **"Remove from my mods" now asks first** and explains that your files stay where they are —
  it never was an uninstall, but it looked like one.

## Game settings

- **Import graphics, sound volumes and hotkeys from another mod** in one click, instead of
  configuring every mod from scratch. Your saved games, home cities and profile are not touched.
- Or tick **"Share this mod's settings with the others"** on several mods and they all keep one
  common set — change it while playing any of them and the rest pick it up.

## Multiplayer

- **Notifications now appear on your desktop** when the launcher is minimised or in the tray,
  and the **Join** button on them actually works. The Windows tray balloon they replaced could
  not carry a button, so a "new room" alert arrived with no way to act on it. Nothing pops up
  while a game is running, so it can't knock you out of fullscreen.
- **The room list adapts to the window width** — it folds the host and mod into a second line
  instead of letting the columns drift out of alignment.
- **Fixed:** a multiplayer game refused to start when Windows demanded administrator for it.

## Protects your game

- **Uninstalling could delete your Age of Empires III.** If an older launcher had installed a
  mod straight into your game folder, uninstalling removed the whole folder, base game included.
  The launcher now refuses to delete a folder it recognises as a real Age of Empires III
  install, whatever its own records say — and it no longer lets you install a mod there in the
  first place.
- **Your saved games could end up inside a mod's folder.** Mods that share the base game's save
  folder get it redirected while they run, and that was only undone when the launcher restarted
  — which can be days, since it lives in the tray. It is now undone the moment you close the game.
- **Antivirus warning before the download, not after it.** Windows Defender is known to delete
  one of Wars of Liberty's files during install. The launcher now tells you beforehand, shows
  the exact folders to exclude and copies them to your clipboard. It never changes your
  antivirus settings itself.
- **Low disk space warning before any download** — installing, updating, repairing, applying a
  translation or grabbing an add-on. It names the drive that is short and lets you continue anyway.

## Fixed

- **"Play" could get stuck** on "PLAYING…" with no way to close the game. It now becomes Stop
  whenever a game is running, and Stop no longer closes unrelated copies of Age of Empires III.
- **Windows forcing administrator on your game.** Windows sets a compatibility flag on
  `age3y.exe` by itself, which is what makes it ask for permission every launch — reported as
  "the launcher broke my game". The launcher now explains it and removes it in one click.
- **A game that fails to start says so.** A mod that dies seconds after launching used to look
  exactly like a short session; you are now told, and pointed at Verify files.
- **Cancelling a Windows permission prompt** is a decision, not a crash — no more red error box.
- **A game that won't close** stops the install and tells you, instead of writing over files the
  game still has open.
- The notification bell now shows **each mod's icon**, and records version changes and repairs.
- **New sounds** for a finished install or update and for a failed operation.
- The backup dialog that **interrupted every fresh install** is gone. Backups are still there on
  demand, in the gear menu and in Properties → USER DATA.

## For mod authors

Settings → General has a **Developer mode** toggle that reveals the author tools, including
loading a `mod.json` straight from your PC to preview it in the Workshop — with a real error
message if it's malformed — before opening a pull request.

## Installing

Download `Aoe3ModLauncher.exe` and run it. Windows SmartScreen may warn you the first time —
[INSTALL.md](https://github.com/Gorgorito12/AoE3-Mod-Launcher/blob/main/WarsOfLibertyLauncher/INSTALL.md)
explains why and what to click, and
[IS-IT-A-VIRUS.md](https://github.com/Gorgorito12/AoE3-Mod-Launcher/blob/main/docs/IS-IT-A-VIRUS.md)
covers how to verify the download yourself.

If you already have the launcher, it will offer you this update on its own.

SHA256: CF34D3F18DE700E73A15BC60C25931595067AB1DD7091A778F5939D983EBCE15
