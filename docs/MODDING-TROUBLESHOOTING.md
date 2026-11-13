# Modder troubleshooting

> Something about your mod is not working in the launcher. Find the symptom
> below: each entry says why it happens, which `mod.json` field fixes it, and
> the real case that taught us.

[`MODDING.md`](MODDING.md) is the reference: every field, what it is and when to
set it. This page is the other way round, from what you see to the field.
Several fields only exist because a mod hit one of these problems, so the
background of each entry is worth reading before you decide you don't need it.

Problems with the catalog pull request itself (CI errors, auto-merge) are in
[`MODDING.md` §9](MODDING.md#9-common-errors-and-how-ci-catches-them-first).

**Contents**

1. [The mod won't open, or it opens the base game](#1-the-mod-wont-open-or-it-opens-the-base-game)
2. [The game asks for a product key](#2-the-game-asks-for-a-product-key)
3. [The launcher takes another folder for your mod, or uninstalls the wrong one](#3-the-launcher-takes-another-folder-for-your-mod-or-uninstalls-the-wrong-one)
4. [The mod shows the player's language, or the base game's data](#4-the-mod-shows-the-players-language-or-the-base-games-data)
5. [The mod won't start without its folders in My Games](#5-the-mod-wont-start-without-its-folders-in-my-games)
6. [Your saves mix with the base game's](#6-your-saves-mix-with-the-base-games)
7. [Two different versions get into the same match and go out of sync](#7-two-different-versions-get-into-the-same-match-and-go-out-of-sync)
8. [Installs fail for everybody after you renamed a release tag](#8-installs-fail-for-everybody-after-you-renamed-a-release-tag)
9. [The install succeeds but the mod does nothing](#9-the-install-succeeds-but-the-mod-does-nothing)
10. [GitHub refuses your release file](#10-github-refuses-your-release-file)
11. [An update broke the game by deleting a file](#11-an-update-broke-the-game-by-deleting-a-file)
12. [Matches on your mod are never rated](#12-matches-on-your-mod-are-never-rated)
13. [Field → symptom](#13-field--symptom)

---

## 1. The mod won't open, or it opens the base game

**Symptom.** `Could not load … .bar`, or the game starts and it is plain Age of
Empires III.

**Why.** The AoE3 engine finds its data (`.bar` files) through the registry
`setuppath` value, which points at the player's real `…\bin`, not through the
folder it is started from. Only an executable patched with **UHC** reads the
working directory instead. An `IsolatedFolder` install is a copy of the game in
a folder of its own, so a stock (non-UHC) executable there keeps loading the
base game's data from the real `…\bin`.

**Fix.** Answer the question in [`MODDING.md` §4.0](MODDING.md#40-first-will-your-mod-even-open-the-uhc-rule)
and pick the model it gives you:

- UHC executable → `IsolatedFolder` ([§4.1](MODDING.md#41-isolatedfolder--a-private-clone-of-aoe3)).
- Stock executable that only **adds** files → `InPlaceOverlay` ([§4.2](MODDING.md#42-inplaceoverlay--your-files-on-top-of-aoe3)).
- Stock executable that **replaces** base files → `IsolatedFolder` +
  `privateSetupPath: true` ([§4.3](MODDING.md#43-privatesetuppath--stock-exe-total-conversions)).

To test for UHC: copy your whole game folder somewhere else and start the mod's
`.exe`. If it opens normally, it has UHC.

**Don't** patch the executable with UHC yourself to make the copy work. It needs
a custom executable, and players on it can no longer play with players on the
original mod.

**Background.** Napoleonic Era's isolated install said "Could not load
DATAPN.BAR" until `setuppath` was pointed at its folder. Struggle of Indonesia's
`age3y.exe` is byte for byte the stock one. Both now use `privateSetupPath`.

**About `setupPathRedirect`.** It was the first answer to the same problem:
around launch the launcher turned the folder `setuppath` points at (the real
`…\bin`) into a junction to your mod's folder, and put it back afterwards. It
needed no administrator rights, but it renamed the player's `bin` while your mod
ran. Your mod and the base game could not run at the same time, and if the game
crashed the folder stayed renamed until the launcher next started. It still
works, and installs made with it are still put back correctly. Don't choose it
for a new mod.

---

## 2. The game asks for a product key

**Symptom.** Your mod starts and asks for the 25-character product key, while the
player's base game opens without asking.

**Why.** With `privateSetupPath` the launcher points the player's own copy of
your executable at a registry key of its own. That key holds the install folder
and the player's licence values (`pid`, `digitalproductid`, `doublehash`), which
the launcher copies from that machine's base game. An executable whose key does
not exist, or a key holding only the path, makes the game ask for the licence.

**Fix.** Don't ship an executable that is already patched in your payload. Let
the launcher patch the player's own copy at install. Then your mod's licence
always matches the player's base game: if their AoE3 opens without a key, so does
your mod.

**Background.** Shipping a patched executable looks like it saves the launcher a
step. It doesn't work: the patched executable is useless without its registry
key, and that key cannot be packaged, because its folder and licence values
belong to one player's machine. It would also mean redistributing Microsoft's
executable. The key must also be in `HKLM`: the engine ignores `HKCU`, and a key
written there produced exactly this prompt when it was tried.

---

## 3. The launcher takes another folder for your mod, or uninstalls the wrong one

**Symptom.** Another mod's folder shows up as an install of yours. Or uninstalling
your mod deletes a different mod.

**Why.** The launcher recognises an install by its content: `probeFile` must be
there, and `marker` too if you declared one. The folder name is never used. An
`IsolatedFolder` install is a copy of the player's AoE3 folder, so anything left
loose in that folder is copied into **every** mod folder on the machine, including
an old copy of your own executable.

**Fix.** Declare `install.marker`, even if your `probeFile` looks exclusive to
your mod. Use a **data** file your mod ships (for example `data\myproto.xml`),
not the executable. See [`MODDING.md` §3.4](MODDING.md#34-install--how-the-mod-is-installed).

**Background.** Napoleonic Era declared the probe `age3n.exe` and no marker,
because its own executable seemed exclusive. One leftover `age3n.exe` in a
player's AoE3 folder was copied into the folders of Wars of Liberty and Struggle
of Indonesia, and both then looked like Napoleonic Era installs. The launcher
deleted that player's Struggle of Indonesia (11,408 files) while reporting that it
was uninstalling Napoleonic Era.

This guide used to say a marker was only needed when the probe file also shipped
with the base game, and gave "your own `.exe`" as an example of when you could
skip it. That advice is what caused the incident. Today the launcher also refuses
a folder whose `install-manifest.json` names another mod, but a marker is still
the half only you can supply.

---

## 4. The mod shows the player's language, or the base game's data

**Symptom.** Menus come out in the player's language (Spanish for a Spanish
owner) while your own folder runs in English. Or units and techs behave like the
base game.

**Why.** AoE3 reads the compiled `data\<name>.xml.XMB` before the loose
`<name>.xml`. An `IsolatedFolder` install is a copy of the player's own game, so
if your mod ships modded `.xml` files without their compiled twins, the player's
compiled files sit on top of yours: in their language, with the base game's data.

**Fix.** It depends on how your mod is distributed, and the launcher cannot work
it out for you. Packaging drops every file that is identical to the base game, so
"I ship no such file" and "my file equals the base game's" look the same from
outside.

- **A complete game folder** that deliberately carries no compiled tables: set
  `install.supersedeCompiledXml: true`. A normal install of your mod has none of
  those files either, so the launcher removing them gives the same result as
  running your folder directly. It only removes a compiled file whose `.xml` you
  shipped and whose `.XMB` you did **not**, so if you ship both, nothing is
  touched.
- **An overlay onto the player's AoE3** (Wars of Liberty's case): leave it off.
  Every other player keeps the base game's compiled files, and removing them
  would make your install differ from all of them. The engine uses `.xml.xmb` for
  its LAN version check, so this caused version mismatches and out-of-syncs when
  it was tried for Wars of Liberty.

The publish wizard asks this as a plain question in step 3, under the install
type: *"My mod ships as a complete game folder, with no compiled .xml.XMB tables
of its own"*. It appears only for the two isolated-folder options, and ticking it
writes the field.

`package-mod-payload.ps1` lists the affected files in its report, so you find out
before you publish. If you **do** ship a compiled table, the packager keeps it
even when it matches the base game's, and the launcher then leaves it alone: its
presence in the payload is what says "the author has this one".

**Background.** Measured on *Knights and Barbarians*: seven files, including
`stringtable*` (a Spanish owner got Spanish menus while the author's folder runs
in English) and `protoy` / `techtreey`, which are **simulation** data. Deleting
them by hand fixed it.

---

## 5. The mod won't start without its folders in My Games

**Symptom.** On a fresh install the mod falls back to the stock maps, or does not
start at all, until certain folders exist under `Documents\My Games\<your mod>`.

**Why.** Some mods need that folder to have a shape before the first run, and the
game only creates its subfolders later.

**Fix.** Set `install.userDataPayload` to the name of a second asset on the same
release (for example `userdata.zip`) holding the folder skeleton, AI
personalities or a starter profile. It also needs `userDataFolder` and
`update.mechanism: GitHubReleases`. The launcher only **adds** what is missing: a
file the player already has is never overwritten, and an opt-in uninstall only
takes back files still identical to what it wrote. See
[`MODDING.md` §3.4](MODDING.md#34-install--how-the-mod-is-installed).

**Attach it to every release.** The launcher first looks for it on the release it
is installing. If it is not there, it falls back to your `approvedReleaseTag`,
then to the newest release that carries it (stable before prerelease). So
forgetting it once no longer breaks anyone, but the fallback is only written to
the diagnostic log, naming both tags, which is the only way you find out. Players
who already installed are unaffected: their seed is recorded in the install
manifest and never fetched again.

**Background.** *Knights and Barbarians* falls back to the stock maps and will
not start when its subfolders are missing. The fallback across releases exists
because the people a forgotten seed hurts are only the ones installing
afterwards, and nothing else would tell the author.

---

## 6. Your saves mix with the base game's

**Symptom.** Your mod's saves, recordings and profiles land in
`My Games\Age of Empires 3`, next to the base game's.

**Why.** Your executable writes to the shared folder instead of one of its own.
Wars of Liberty and Improvement Mod ship builds that already write to their own
folder; many mods don't.

**Fix.** Declare `userDataFolder` and set `install.userDataRedirect: true`. While
your mod runs, the launcher makes the shared folder point at yours, and puts the
real one back afterwards. Leave it off if your build already writes to its own
folder. A stock-executable total conversion ([§1](#1-the-mod-wont-open-or-it-opens-the-base-game))
usually needs it too. See [`MODDING.md` §3.6](MODDING.md#36-advanced-optional-fields).

**Background.** King's Return hardcodes `My Games\Age of Empires 3\Savegame` in
its executable, so its saves mixed with the base game's until it used the
redirect.

---

## 7. Two different versions get into the same match and go out of sync

**Symptom.** Players on different versions of your mod can join each other's
rooms, and the match desyncs.

**Why.** The launcher lets players into a room only when a fingerprint of three
data files matches: by default `data\protoy.xml`, `techtreey.xml` and
`stringtabley.xml`. If your mod ships its own data files instead of overwriting
those three, they come from the base game and are identical for every player,
whatever version of your mod they have.

**Fix.** Declare `install.multiplayerProbeFiles` with the files that really
change between your versions. Omit it for a normal mod. This is a critical field:
wrong values let two different versions share a match. See
[`MODDING.md` §3.4](MODDING.md#34-install--how-the-mod-is-installed).

**Background.** Napoleonic Era ships `data\proton.xml` and `data\techtreen.xml`
and never touches the `y` files, so without this field every Napoleonic Era
install had the same fingerprint.

---

## 8. Installs fail for everybody after you renamed a release tag

**Symptom.** Nobody can install your mod any more, yet the catalog entry still
validates and your own installed copy keeps working.

**Why.** Without `followLatest`, the launcher downloads exactly the release named
by `approvedReleaseTag`. If you delete or rename that tag on GitHub, that lookup
fails for every player. Nothing in the catalog looks wrong.

**Fix.** Opt into `update.github.followLatest`
([`MODDING.md` §5.1](MODDING.md#follow-latest-updategithubfollowlatest)). The
launcher then installs your newest stable release, and `approvedReleaseTag` is
only the fallback when GitHub cannot be reached. If you don't use it, update
`approvedReleaseTag` in the same change that renames the tag.

**Background.** Napoleonic Era and Struggle of Indonesia were listed at `1.0.0`;
their authors later renamed their tags to the mods' real version numbers (`2.1.7b`
and `1.2`), and every new install of both mods failed until the catalog caught
up.

---

## 9. The install succeeds but the mod does nothing

**Symptom.** The launcher reports a successful install, and the game is the base
game, or the mod's files sit one folder too deep.

**Why.** The launcher copies the player's AoE3, moves the copy's `bin\` up to the
install root, and only then lays your payload on top. A `bin\` inside your zip is
never moved: it lands at `<install>\bin\` and the game never reads it. A single
folder wrapping everything is moved up automatically, but nothing more complex.

**Fix.** Package the payload **flat**: `data\`, `art\`, `sound\`, your `.exe` at
the root of the zip. If your mod folder has a `bin\`, the zip root must be the
**contents** of `bin\`. Leave out `directx\`, `msxml\` and any `unins000.*`: the
copy of the game supplies the first two and the third does nothing here. See the
packaging tip in [`MODDING.md` §13](MODDING.md#packaging-tip).

`package-mod-payload.ps1` does this for you. Run it with `-ReportOnly` first and
read the report.

---

## 10. GitHub refuses your release file

**Symptom.** GitHub rejects the upload because the file is larger than 2 GB.

**Fix.** Split the zip into `payload.zip.001`, `payload.zip.002`, … (three digits,
numbered from `001` with no gaps) and upload them all. The launcher downloads
every part and joins them in number order before extracting. If a part is
missing, the launcher refuses the release and names the missing part.
`package-mod-payload.ps1` splits the file for you.

Also check that you are not shipping files identical to the base game: the
player's own copy already has them. Compare by SHA-256, not by size, and be
careful with `Sound\`, `avi\` and `Language\`. If your reference AoE3 is in a
different language from the one you built on, every voice line looks "changed".

---

## 11. An update broke the game by deleting a file

**Symptom.** After an update, the game fails because a base game file is missing.

**Why.** `delete.lst` **deletes**; it does not restore. A file your mod had
overwritten from the base game is removed outright, leaving a hole where the game
expects it.

**Fix.** Use `delete.lst` only for files that should stop existing. To return a
base game file to its original bytes, pack those original bytes in your zip so
the update overwrites it back. See
[`MODDING.md` §5.1](MODDING.md#removing-files-in-an-update-deletion).

---

## 12. Matches on your mod are never rated

**Symptom.** Competitive matches on your mod are stored with no result, so the
ladder never moves.

**Why.** A match is rated only when the launcher can read who won from the game's
recording, which it looks for in your mod's `My Games` folder.

**Fix.** Declare `userDataFolder`
([`MODDING.md` §3.6](MODDING.md#36-advanced-optional-fields)). Without it the
launcher finds the folder by name or learns it after a launch, and until then
your mod's matches have no result.

---

## 13. Field → symptom

| Field | Entry |
| --- | --- |
| `install.type`, `install.privateSetupPath`, `install.setupPathRedirect` | [1](#1-the-mod-wont-open-or-it-opens-the-base-game), [2](#2-the-game-asks-for-a-product-key) |
| `install.probeFile`, `install.marker` | [3](#3-the-launcher-takes-another-folder-for-your-mod-or-uninstalls-the-wrong-one) |
| `install.supersedeCompiledXml` | [4](#4-the-mod-shows-the-players-language-or-the-base-games-data) |
| `install.userDataPayload` | [5](#5-the-mod-wont-start-without-its-folders-in-my-games) |
| `install.userDataRedirect` | [6](#6-your-saves-mix-with-the-base-games) |
| `userDataFolder` | [5](#5-the-mod-wont-start-without-its-folders-in-my-games), [6](#6-your-saves-mix-with-the-base-games), [12](#12-matches-on-your-mod-are-never-rated) |
| `install.multiplayerProbeFiles` | [7](#7-two-different-versions-get-into-the-same-match-and-go-out-of-sync) |
| `approvedReleaseTag`, `update.github.followLatest` | [8](#8-installs-fail-for-everybody-after-you-renamed-a-release-tag) |
| Payload layout, split parts | [9](#9-the-install-succeeds-but-the-mod-does-nothing), [10](#10-github-refuses-your-release-file) |
| `delete.lst` | [11](#11-an-update-broke-the-game-by-deleting-a-file) |
