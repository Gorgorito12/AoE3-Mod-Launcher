# AoE3 Mod Launcher v1.0.9

The biggest release so far. Mod updating actually works now, the launcher no longer
nags for administrator rights, and it can start with Windows properly.

## Mod updates finally work for catalog mods

If you use **Improvement Mod** (or any other mod from the catalog), this is the headline.

- **The update button was invisible.** Catalog mods detected an available update
  correctly and then had no way to show it — the main button always said PLAY. It now
  turns into **UPDATE**, the same way Wars of Liberty already did.
- **The launcher can now tell which version you have.** Until now it only knew your
  version if it had installed the mod itself, so anyone who installed by hand, or whose
  mod moved to a different GitHub repository, was stuck with no update prompt forever.
  It now identifies your installed version by comparing a handful of small files against
  the release contents — **without downloading the release** (a few hundred KB instead of
  1 GB). From then on every new version shows up on its own.
- **Fixed:** when a mod moves to a new repository, users could silently stay on the old
  version — no error, no message, just nothing. That can't happen anymore.
- **Improvement Mod now follows the mod author's own repository**, so new releases reach
  you as soon as they are published. (This part is already live and needs no update.)

## No more administrator prompts

- The launcher **runs as a normal program now**. No UAC prompt every time you open it.
- It asks for permission **only at the moment it actually needs it** — installing,
  updating, repairing or uninstalling a mod inside a protected folder like
  `Program Files`. If your game lives in a Steam library or any normal folder, you will
  never see a prompt at all. Saying "No" simply cancels the action; nothing breaks.
- Detecting your game and verifying files never ask for permission — they only read.

## Start with Windows / run in the background

- **Start with Windows is now on by default**, announced once with a tray notification
  and turned off with a single click in Settings. This is what the previous point above
  unblocked: Windows silently refuses to auto-start programs that demand administrator,
  which is why it never worked before.
- Auto-start now points at the **stable installed copy**, so moving or deleting the
  `.exe` you downloaded no longer breaks it.
- **Double-clicking the launcher while it sits in the tray brings the window back**
  (the same behaviour as Steam or Discord). Previously nothing happened.

## Install and uninstall

- **First launch offers to install a stable copy** on your PC (opt-in, one dialog), so
  auto-start and shortcuts keep working no matter where you downloaded the file.
- **New: "Uninstall from this PC"** in Settings → Maintenance. Removes the launcher,
  its shortcuts, auto-start entry and link handler, and asks whether to keep or delete
  your settings. **It never touches your installed mods** — those are uninstalled
  separately.
- **Fixed:** installing a stable copy from certain builds copied an incomplete program
  that failed to start with no error message.

## Multiplayer

- **You can rename a room after creating it** (host only). Everyone in the room sees the
  new name, and the Discord announcement updates itself.
- **Chat messages now show the date**, not just the time — `Yesterday 19:03`,
  `15 Jul 19:03` — so older messages stop looking recent and midnight no longer scrambles
  the order.
- **Rooms show how long they have been open**, both in the room list and inside the room.
- **Private rooms are clearly marked** with a badge everywhere, and the create-room dialog
  now explains that a private room is not announced (on Discord or in-app) and needs its
  password to join.

## Language

- **The launcher now starts in your Windows display language** (Spanish or English) until
  you choose one yourself in Settings. Previously it always started in English, even on a
  Spanish system.
- The first-run install prompt now matches the launcher's theme instead of appearing as a
  plain white Windows dialog.

## Fixes

- Title-bar tooltips (minimise / maximise / close) displayed empty boxes instead of words.
- Repairing, updating a catalog mod, picking a specific version and applying a translation
  now request permission properly when the mod folder is protected, instead of failing.
- The launcher now records diagnostic details that were previously missing, so update
  problems can actually be traced from a shared log.

---

**Note for server admins:** renaming rooms requires the lobby backend to be updated as
well. Without it, the rest of this release works normally and renaming simply does nothing.
