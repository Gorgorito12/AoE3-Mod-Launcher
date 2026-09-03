# What a `.age3Yrec` actually yields

A reference for what can be read out of an AoE3 recording, what is read today, and — the part that
keeps costing people time — which fields look like data and are not.

**Everything here is measured**, over 17 real Wars of Liberty recordings (tournament 1v1s, games
against the AI, and one four-human game) plus one recording cross-checked field by field against
its own `.personality` file. Where a claim rests on a single file it says so.

The format itself — container, settings dictionary, command stream, outcome trailer — is documented
in the `.age3Yrec` section of `.claude/rules/multiplayer.md`. This file is the *inventory*.

---

## 1. What the launcher reads today

`Services/Multiplayer/ReplayParserService` uses **14 of the file's 370 header keys**:

| key | surfaces as | used for |
|---|---|---|
| `gamefilename` | `MapName` | the map actually played, stored on every match |
| `gamemapname` | `MapPool` | the pool it came from (`ESOC Maps`) |
| `gamename` | `GameName` | the room's name |
| `gamenumplayers` | `PlayerCount` | the head count every gate checks against |
| `gamerandomseed` | `RandomSeed` | `same_game` — proves two readings are the same match |
| `gamehosttime` | `HostTime` | stored beside the seed; not part of any verdict |
| `gameplayerNname` | `ReplayPlayer.Name` | finding our own slot, by AoE3 profile name |
| `gameplayerNciv` | `Civilization` | the civ index, 1-based into `civs.xml` |
| `gameplayerNteamid` | `TeamId` | the team map, when the recording carries real teams |
| `gameplayerNtype` | `SlotType` | human / AI / empty |
| `gameplayerNexplorername` | `ReplayPlayer.Explorer` | named on a local match card |
| `gameplayerNhclevel` | `HomeCityLevel` | named on a local match card |
| `gameplayerNhcfilename` | `HomeCityFile` | which deck that player brought — the NAME only |
| *(the file's tail)* | `ReplayOutcome` | who lost, how many humans, whether the block exists |

Plus, from the command stream: nothing yet. See §3.

---

## 2. What else is there and IS information

These vary across the 17 recordings, which is what separates a field from a decoration.

| field | distinct values / 17 | what it is |
|---|---|---|
| `gameplayerNhclevel` | 25 | each player's home city level |
| `gameplayerNhcfilename` | 20 | **the deck's file name** — `sp_Beijing_homecity.xml` |
| `gameplayerNhomecityname` | 20 | the city the player named |
| `gameplayerNexplorername` | 23 | the explorer's name |
| `gameplayerNcolor` | 8 | player colour |
| `gameplayerNaipersonality` | 3 | which AI played (`wolMenelik`) |
| `gamefilenameext` | 12 | the map's SCRIPT file (`NEWBarbary.xs`) |
| `gamefilecrc` | 11 | the map's checksum — would catch an altered map |
| `gamedifficulty` | 2 | AI difficulty |
| `gameallowcheats`, `gamecontentid`, `gameteamlock` | 2 each | room settings |

**`hcfilename` is the load-bearing one.** It is what ties a recording to the deck the player
brought, and the launcher already reads those files
(`Services/HomeCityDeckService`) — see §6 for why that pairing matters and where it stops.

---

## 3. What the command stream yields

The framing is validated against the file's own declared command count: **144,563 of 144,685
records across 79 recordings agree exactly**, so every command is found. What each command MEANS is
a different matter, and this is where the honest number lives. Measured over 11 tournament games,
61,309 commands:

```
distinct command types                    73
types whose meaning can be justified       2-3
types still unidentified                  ~70, and they are 25.4% of all commands
```

**The 74.6% covered by the identified types flatters it**: type 1 alone — order-to-position, which
carries two float32 map coordinates and is unmistakable — is 69% of every command in the game. **By
TYPE, under 5% is understood.** Types 7 and 9 are unit selection (lists of entity handles, reordered
between rows). Everything else is a number with a name nobody has earned.

What can be taken from it today, per player:

- **how many commands each player issued** — a real activity measure. A tournament human issues
  1,400-1,700 per game; the AI in a skirmish issues 13,711.
- **a histogram over the 73 types**, and each type's timing. Types 1-4 begin at second zero; types
  14-35 only after minute six. That is enough to date the phases of a match without knowing what
  any of them are.

**Two cautions.** A command is **repeated across consecutive records**, up to eight times for one
action, so a raw count is not a count of actions — deduplicate first. And **field `+12` is NOT a
proto id**: that claim was made and withdrawn the same day. In one game its small values resolved to
plausible Chinese buildings, but nine of them arrive as a consecutive run at a single instant, which
is an enumeration, and in another game the same field gives `Skulls` and `TurkeyScout`. The trap
generalises: **a consecutive run of ids resolves to a coherent-looking group in any table ordered by
faction**, so "the names suit this player's civ" is not evidence until you have checked the values
are not sequential.

---

## 4. The traps

Fields whose names promise something their values do not deliver. Each was constant, or
inconsistent, across all 17.

- **`gamefreeforall` does NOT identify a free-for-all.** It reads `True` on some 1v1s and `False`
  on others that are identical, and **`False` on the four-human free-for-all**. Use the `teamid`s;
  they are what `MatchTeamMap` already keys on.
- **Constant in all 17, therefore carrying no information:** `gamestartwithtreaty` (always `True`,
  including in plain skirmishes), `gamestartingage` (always 0), `gamespeed`, `gamemapsize`,
  `gamenorush`, `gamekoth`, `gametrademonopoly`, `gamerestrictpause`, `gamenoblockade`,
  `gamenomadstart`, `gameteambalanced`, `gameteamsharepop`, `gameteamshareres`, `gamehiddencards`,
  `gamepickcardsfirst`, `gamelatency`, `gamehandicapmode`, `gamemapvisibility`, `gamemapresources`,
  `gamerecordgame`, `gamerestored`, `gametype`, `gamemodetype`, `homecitylevelmin/max`, and every
  `gamecampaign*`. **Storing one would put a label on everybody's match history that means
  nothing** — "Treaty" on every game, for instance.
- **Per player, always empty or zero:** `rating`, `rank`, `winratio`, `totalxp`, `powerrating`,
  `clan`, `handicap`, `avatarid`, `hclocation`, `questid`, `queststatus`, `status`. These belong to
  the original ESO service, which has been dead for years.

**One caveat on "constant", stated because it bounds the whole section.** The corpus is 16
1v1s and one four-player game, so a field that only moves with the head count cannot show movement
here — `gamenumplayers` and `gamecurplayers` are constant for that reason and are NOT traps. The
list above is fields that stayed constant while the map, the civilizations, the players and the
game mode all changed around them, which is a different thing.

**The general rule this section exists for: a field that never varies is not data.** Two of these
(`gamestartwithtreaty`, `gamestartingage`) were already written up after nearly being stored; the
list above is the rest of them, measured at the same time.

---

## 5. Is this everything?

**No — it is everything currently READABLE, which is not the same thing.** The file contains the
whole match: AoE3 replays a recording by re-running the simulation from the command stream, so
every build order, every attack, every shipment is in there by construction. What is missing is the
decoding.

| layer | state |
|---|---|
| container (`l33t` + zlib) | fully read, and the declared size double-checks it |
| settings header | **370 of 370 keys** parsed; §1, §2 and §4 are the complete inventory |
| outcome trailer | fully read — who lost, how many humans, whether it was written at all |
| command stream | every command FOUND (144,563 of 144,685 records agree with the file's own count) |
| command MEANING | **2-3 of 73 types** |

So the honest summary is: **the header is finished, the command stream is opened but not read.**
Almost certainly among those ~70 unidentified types are attacking, tributing, resigning,
garrisoning, changing formation, assigning gatherers — and sending a card.

**One thing in that list is different, and it is worth keeping straight:** the unidentified types
are undecoded, which is a matter of effort. The CARD a shipment names is not there at all — see §6.
Those two are not the same kind of "cannot".

---

## 6. What cannot be obtained, and why

**Which cards a player played.** Not a gap in the search — a property of the format, established
from the engine's own scripting API:

```
aiHCDeckPlayCard(bestCard);                // bestCard = i, the loop index over the deck
aiHCDeckGetCardTechID(gDefaultDeck, i);    // and THIS is what turns an index into a tech
```

A card is played **by deck slot**. The game never transmits a card identifier, which is why six
separate id spaces were searched and all six came back empty (card DBID, proto DBID as a card, tech
index, `DisplayNameID`, `RolloverTextID`, and a bit-packed field). The recording **names the deck's
file** (`hcfilename`) and does not carry its contents: searching a real 25-card deck through an
inflated recording finds at most 5 of 25 within 250 bytes as ids, and overlaps 20% — chance level —
as tech indices.

So the chain is **slot (in the stream) + that player's home city file (on their disk) = card**. A
spectator sees cards because every client holds every player's deck for the match. From a
stranger's recording it is impossible; from your own it would still need the slot field, which has
not been found.

**How many cards were sent** is also unavailable. `<ships>` in a `.personality` file is the closest
figure and it is unproven — it sits among six-figure resource totals and plausibly counts shipment
points earned rather than cards played, and it only exists for games against an AI.

**Which deck a player used** stops at the file NAME. The contents are on that player's machine.

**And the file name does not even name the DECK — only the city.** A home city file holds
several decks (a real one holds two), and the recording has exactly four per-player home city
keys — `hclocation`, `hclevel`, `hcfilename`, `homecityname` — none of which identifies one. The
file itself is no help: a `<deck>` carries only a `name`, a `gameid` and its cards, with no
active marker, and two decks of a city can share the same `gameid`, so not even the game mode
separates them. `LastHomeCityY.xml` is a byte-for-byte copy of the city last used, which says
the city and not the deck.

⚠ **The false positive worth knowing before you search for the cards yourself.** Looking for a
real 25-card deck's internal names inside three inflated recordings finds **35 of 35, in all
three**. That is not the deck: of 200 cards taken at random from the 4,419 that player does NOT
run, **200 of 200** are also there. What the file carries is the game's entire tech-name table.
It is §3's trap in a different costume — there a consecutive run of ids resolved to a coherent
group in any faction-ordered table; here a whole vocabulary makes any subset of it look present.
**A hit means nothing until the control says the misses are absent.**

**What this leaves, and what the launcher does with it:** the home CITY per player, which IS
knowable for everyone in the match and is now reported with the result (`home_city`, migration
0012 in the lobby backend). For the viewer's own cards there is no reading of the past at all,
only a recording of the present: `Services/DeckSnapshotStore` keeps a copy of the deck files
when a match ends, so matches from then on can show what was brought.

---

## 7. If you want one of §2's fields stored

**Three of §2 are wired up now** — `explorername`, `hclevel` and `hcfilename` — and the reason is
worth stating, because it is the same reason they were left alone before. They are still marginal
for a match HISTORY, where a row is a result. They are the whole substance of the LOCAL match list
(ModProperties → STATISTICS), where a game between people has no result to report most of the time
and no statistics at all ever: who played, as whom, with which explorer and which deck is what
makes such a row a game somebody played rather than two names and a map. Nothing is sent to the
server, so none of it needed a column.

The rest stays unwired, deliberately: `color` and `aipersonality` say nothing a player wants, and
the one that would have hardened existing behaviour — `gamefreeforall`, to make `MatchTeamMap`'s
free-for-all refusal direct instead of inferred — turned out to be the trap in §4.

Adding one is small: a field on `ReplayHeader`, read in `ParseHeader` beside its neighbours, and
then wherever it is to be shown or reported. The cost is not the parsing; it is that anything sent
to the server needs a column and a migration, and anything shown to a player needs a string in both
languages.
