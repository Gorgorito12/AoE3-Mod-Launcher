# Multiplayer rating (ELO) · La puntuación (ELO) del multijugador

> **La versión en español está más abajo, en este mismo documento.** ·
> *The Spanish version is further down, in this same document.*
>
> **Short link to share:** pin this page and link it whenever somebody asks why
> their match didn't count. · *Fija esta página en tu servidor de Discord y
> enlázala cuando alguien pregunte por qué su partida no sumó.*

---

## English

### The short version

- **There are two ladders: 1v1 and Teams** (2v2 and 3v3 share one). What happens in one never
  touches the other.
- **Everybody starts at 1500**, and the system starts out unsure of you. The less sure it is,
  the more each match moves you.
- **Your first matches place you**: 10 in 1v1, 5 in Teams. Until then your rating carries a
  **"?"** and you are not on the table yet.
- **The table is ordered by rating**, highest first. Thirty days without a rated match marks you
  **INACTIVE**, but you keep your place.
- **Competitive rooms on every mod count, the base game included, all on the same ladder**, and
  only with a recording that says who won. Tick **"Record Game"** on the game's setup screen,
  every match.
- **Beating the same opponent over and over is worth less and less** (from the third win in a
  row). That is the anti-farm rule; the result card tells you when it applied.
- **In Teams you pick your side in the room**, and you have to play the same sides in the game.
- **If a cheater is banned, you get back the points you lost to them.** The launcher tells you
  once.
- **There are no resets.** One continuous ladder, no seasons. Not playing never lowers your
  rating.
- **Want the exact numbers?** [Technical details](#technical-details) has the formulas, every
  constant and the exact rules, at the end of the English half.

### Points and uncertainty

Your rating is two things: a number (your ELO) and how sure the system is about it.

- **The less sure it is, the bigger the swings.** A newcomer can move a hundred points or more in
  one match. Someone with fifty matches moves a handful. It is the same rule in both cases, not a
  cap that kicks in later.
- **Every match makes it surer.** That is why your swings shrink as you play.
- **Time without playing makes it less sure.** If you come back after months away, your first
  matches move you more than usual for a while, so you settle quickly wherever you are now.
- **Your rating never drops on its own.** Not playing only makes the next match move you more,
  in either direction.

How much a match moves you also depends on who you play: beating someone rated well above you
pays a lot, beating someone well below you pays little. And there are two safety rails: no single
match can move anyone more than 700 points, and nobody goes below 400.

**How many points, in practice.** Approximate values, computed with the server's own rating code
— the server does the exact maths for every match:

| Situation | You win | You lose |
|---|---|---|
| Your very first match (both players new) | +242 | −242 |
| Your second match | ~ +128 | ~ −128 |
| After about 5 matches | ~ +51 | ~ −51 |
| After about 10 matches | ~ +30 | ~ −30 |
| Settled (about 30 matches), against someone at your level | ~ +12 | ~ −12 |
| Very settled (about 100 matches) | ~ +7 | ~ −7 |
| Heavy favourite (1700 vs 1300) | +2 | −23 |
| Clear underdog (1300 vs 1700) | +23 | −2 |
| Favourite (1600 vs 1400) | +6 | −19 |
| Settled, against a newcomer | ~ +7 | ~ −7 |
| Back after 6 months away | ~ +35 | ~ −35 |
| Back after a year away | ~ +56 | ~ −56 |

The table is for a 1v1 between players who play about one rated match a day, with no anti-farm
discount. The favourite and underdog rows are between two settled players (about 30 matches
each). The formulas behind every row are in [Technical details](#technical-details).

Three things that surprise people and are correct:

- **Your first matches move enormously, on purpose.** A newcomer starts with the widest
  uncertainty there is, and each match narrows it. By ten matches the swings are around ±30; by
  thirty, around ±12.
- **Beating the favourite pays about ten times more** than winning what was expected of you. A
  1700 who beats a 1300 gains 2; losing that same match costs 23.
- **It is not zero-sum.** In the same match one player can gain 7 points while the other loses
  236 — a settled player against a newcomer. Each side moves by how sure the system is about
  *their* level, not by what happened to the other.

**Why it sometimes only moves a few points.** The system stores how sure it is about your rating
(the *RD*, see [Technical details](#technical-details)). Playing about once a day, it settles
around 50 after a hundred matches and never quite reaches its floor of 45, because every day away
adds a little uncertainty back. That is why a veteran's match moves single digits and a returning
player's moves dozens: the same rule, not a cap that kicks in later.

**Win probability.** In a competitive room the launcher shows each side's chance of winning. The
**server** works it out from both sides' ratings and how sure it is about each — the launcher only
displays it. It is a guide, not a promise: two settled players 100 points apart are 63 % against
37 %, 400 points apart 90 % against 10 %.

### Placement

Your first **10 rated matches in 1v1** (or **5 in Teams**) are placement matches.

- During placement your rating shows with a **"?"** (for example **1580?**) everywhere it
  appears, and your profile shows how many you have left.
- You are **not on the table yet**: your rank badge is **Discovery**. The ranking lists players in
  placement separately, at the bottom.
- The matches count exactly like any other: they move your rating, they count for streaks and the
  anti-farm rule applies to them too.
- When you finish, the result card tells you **which place you entered the table at**.

### The table and inactive players

- **The table is ordered by rating**, highest first. The number you see beside a name is the
  number that decides the order.
- **Rank badges** (the shield beside a name) depend on your place: the top of the table wears the
  highest age, the rest follow by share of the table. See [Your rank badges](#your-rank-badges).
- **Thirty days without a rated match** marks you **INACTIVE** in the table. You keep your rating
  and your place; one rated match removes the tag.
- **The win percentage only appears from 5 decided matches onwards.** With fewer it would not be
  a rate: a 0 % off a single loss says nothing about anybody. The decided count is always shown.
- The ranking lives in the **Ranking** tab; the **Rooms** tab shows the top five in the
  community panel, with a **See all** link.

### Your rank badges

You have **one badge per ladder**, and each comes from your place on that ladder.

- **The 1v1 badge is one shield; the Teams badge is two shields** of the same age. While you are
  still placing on a ladder, its badge is **Discovery**.
- **Inside a room, the room decides**: a 1v1 room shows everybody's 1v1 badge, and a 2v2 or 3v3
  room everybody's Teams badge.
- **Everywhere else you choose**: in the chat, the online players list, your account and casual
  rooms. On your **Profile**, under **Badge next to your name**, pick **Highest** (the default),
  **1v1** or **Teams**. *Highest* shows whichever badge has the higher age; if both are the same
  age, the 1v1 one. **Teams** unlocks once you have played a team match.
- **The rating beside a badge belongs to that ladder**: next to a Teams badge you see your team
  rating.
- **How ranks work**, in your account menu, explains every age, with a 1v1 tab and a Teams tab.

### Teams

2v2 and 3v3 score on the **Teams** ladder, which is separate from 1v1.

- **You choose your side in the room.** Each player picks **1** or **2**; the host can move
  anybody. Players who haven't picked wait in a **No team** box.
- **A competitive team room only starts when it is complete**: every seat taken, everyone on a
  team, and the same number on both sides. If not, the **Start** button stays locked and says why.
- **Play the same sides in the game.** Teams are set inside Age of Empires III too. If the sides
  the game recorded don't match the room's, **the match doesn't count**, and the result card says
  which sides the game had.
- **Everyone's change comes from the two teams' averages.** Your own certainty decides how much
  you move: a player still placing moves more than a teammate with a hundred matches.
- **It needs the other side's confirmation.** A team match is rated when someone on the other
  team also reads the recording and both readings agree. Nothing for you to do — one of them
  closing the game is enough. Until then it shows as pending.

### Streaks

- From **3 wins in a row** a 🔥 appears beside your name in the table, with the number.
- A streak **ends when you lose**, or after **14 days without a rated match**.
- Your **longest win streak** and your **longest losing streak** are kept on your profile.

### Anti-farm

Beating the same opponent again and again is worth less and less. It stops two accounts from
feeding each other points, and it costs nothing to normal players, who change opponents.

- **The 1st and 2nd win in a row** over the same opponent count in full.
- **From the 3rd** each one is worth a little less: 90 %, 80 %, 70 %… down to **20 %** from the
  10th win in a row.
- **It goes back to normal** the moment the other player wins one, and it recovers a step for
  every full day the two of you go without playing each other.
- **Both players are affected**: the winner gains less and the loser also loses less.
- **In Teams** it applies only when the **exact same line-up** plays again (the same players on
  each side).
- It applies **during placement too**, and **never in tournaments**.
- It is **not announced in the room**. Afterwards, the result card and your History show the
  percentage that applied (for example "+5 · 40 %").

### Tick "Record Game". Every match.

Age of Empires III **unticks that box again every match**, and nothing outside the game can make
it stick. So the launcher reminds you in the room before you start, and tells you afterwards if the
game was played without recording.

With no recording nobody knows who won — not the launcher, not the server. **If neither player
records, the result is lost for both.** If only one of you did, it can still be saved: see
[If your opponent recorded and you didn't](#if-your-opponent-recorded-and-you-didnt).

The launcher only deletes old recordings it created itself; any you renamed are never touched.

### Matches that don't count

The server decides whether a match counts and **says why**; the launcher shows that reason on the
end-of-match card and in your History. In every case **the match stays in your history** — only
the rating change doesn't happen.

| What you see | What happened | What to do |
|---|---|---|
| "Casual room: doesn't move ELO." | The room was created without ticking **Competitive room** | Tick the box when you create the room |
| "This mod wasn't on the ladder when the match was played" | An older match: only Wars of Liberty was rated back then | Nothing. Every mod counts now |
| "The result couldn't be read: Record Game was off." | No recording of that match turned up | Tick "Record Game" before the next one |
| "This match WAS recorded, but the game closed before it finished writing the ending" | The recording has no ending | **Leave the match to the main menu before closing AoE3** |
| "Recordings were found, but none of them has you among its players" | Your AoE3 profile name isn't the one you play under | Check your profile name |
| "The in-game teams didn't match the room's." | The sides played weren't the ones chosen in the room | Pick the same sides in the game as in the room |
| "This match didn't count for ELO (new account and a very short match)." | An account under 7 days old, a match under 10 minutes, and both players on the same network | Nothing. Real games count normally |
| "The result is in, but the team ladder also needs a reading from the other side" | A team match your side has reported | Nothing. It counts when the other side confirms |
| "The times reported for this match don't add up" | It ran under 3 minutes, or the clocks disagree | Nothing, other than playing real games |
| "Someone in this report was not in the room when the game started" | Somebody joined after it began | Have everyone in the room before starting |
| "This recording had already been reported" | That match had already been counted | Nothing |
| "The loser's game crashed… this match was voided automatically" | Windows recorded the fault | Nothing. Forgiven once a day per player |

### Walking out of a competitive match

In a **1v1** competitive room, after the match has been going **five minutes** (counted from
when the host pressed Start), leaving and not coming back counts as a **loss**.

- **Leaving means dropping the connection**: leaving the room, or **quitting** the launcher.
  **Hiding it to the tray with the ✕ is not leaving.** If the launcher closes on its own,
  reopening it picks the match back up.
- **Closing only the game is not leaving either.** What decides then is your opponent's recording,
  which names the loser.
- **If you both drop, nobody wins.**
- **A recording that names a winner always wins** over this rule.
- It does not apply to team rooms.

### If the game crashes

When the game really crashes, Windows records it, and the launcher checks it. If the player who
crashed was losing, the match is **voided**: nobody gains or loses points. This is forgiven
**once a day per player**; from the second crash in 24 hours it counts as a loss again. Closing
the game from Task Manager is not a crash. Tournament matches are never voided.

### A match can be rated later

The result is saved straight away and the recording is read in the background. A match showing
"no result" is **not closed**: if a reading turns up afterwards — yours when you close the game,
or your opponent's — it is rated all the same, even after the room is gone. You get a
notification when that happens.

### If your opponent recorded and you didn't

When a match ends, the player who isn't the host also sends their own reading of the recording,
automatically. If the match had been stored without a winner and the other player's recording
names one, that reading decides it. To stop invented results: **you can always concede your own
defeat; your recording only gives you a win when it matches the match already stored.**

### Points given back

If a player is **banned for cheating**, everyone who lost rated matches to them **gets those
points back**.

- You get **one notification**, and a banner on your profile above the card of the affected
  ladder, until you press **Got it**.
- If you lost several matches to them, it is all **added up in one notice**.
- **The banned player is never named.**

### No resets

There is **one continuous ladder**. There are no seasons and no date on which everybody starts
again. Your rating, your place and your record carry on for as long as you play.

When this system arrived, every rated match already played was recalculated with these rules
once, so your rating and your placement already reflect them.

### Tournaments

A tournament game rates like any other competitive game, **always at full value** — the anti-farm
rule never applies there, because the bracket decides who plays whom. A walkover or a
disqualification moves nobody's rating. A team tournament match needs the other side's
confirmation, like any team match.

### Monthly highlights

There are seven:

- who climbed the most (with the ladder; only players with at least 5 rated matches that month
  after finishing placement);
- who won the most rated matches (a tie goes to whoever played fewer);
- who played the most rated matches;
- the best streak of the month;
- the best win rate (only players with at least 10 rated matches that month);
- the biggest upset: the win against the side with the most rating above;
- the civilization of the month (the most picked one, at least 3 times).

On the **Rooms** tab, the **Community activity** block shows three of them — who climbed the
most, who played the most and the best streak — beside the community's own figures (matches,
players, the most played map). **See September** shows last month's instead.

On the **Ranking** tab, **Highlights** (beside 1v1 and Teams) shows all seven in depth: the top
five of each, with how much they climbed, how many they won, and so on. The climb and the streak
can be seen for 1v1 or for Teams.

Players still in placement don't count for the climb, and a match where someone was still in
placement is not an upset. On the 1st, the previous month's climb, most matches and best streak
are also posted to Discord.

### Where you see your rating

- On **your account**, top right.
- On your **Profile**: one card per ladder, with your peak and low, your streaks and how you do
  against each opponent.
- In a room's **player list**, and in the **rooms table** next to the host.
- In the **online players** panel and the **Ranking** tab.
- On the **result card** when a match ends, and on every **History** row.

### FAQ

**Why does my rating have a "?"?**
You are still placing: 10 rated matches in 1v1, 5 in Teams. It goes once you finish.

**I won and only got a few points. Why?**
Either your opponent was rated well below you, or you have many matches and the system is already
sure of you — or the anti-farm rule applied (the card says so, with the percentage).

**I haven't played in months. Did I lose points?**
No. Your rating never drops on its own. Your next matches just move you more than usual for a
while.

**Why does it say INACTIVE next to my name?**
Thirty days without a rated match. You keep your place; one rated match removes it.

**I played a whole match and got nothing.**
The end-of-match card and your History say why. Most often: the room wasn't competitive, or
nobody ticked "Record Game".

**What about draws?**
There are none. A match whose result couldn't be read is **not a draw**; it simply moves nobody's
rating.

**Can I report the same match twice?**
No. The server recognises the recording and the match; the second attempt doesn't count.

**Is my recording uploaded anywhere?**
No. It is read **on your own PC**. Only the result and a few values about the match travel to the
server, never the file.

**My profile counts more "decided" matches than "rated" ones. Why?**
They count different things. **Rated** matches are the ones that moved your rating. **Decided**
matches are every match where a winner is known, including ones that didn't count for some other
reason (a casual room, an older match of a mod that wasn't on the ladder yet). Decided is always
equal or higher.

### Technical details

Everything in this section comes from the server's own rating code (`src/elo/` in the lobby
backend), and every number in this document was computed with that same code. If a constant
changes there, this section changes with it.

#### The engine

**Glicko-2** (Mark Glickman, 2013), run the way Lichess runs it. On each ladder you have three
values:

- **rating** — the number you see;
- **RD** (rating deviation) — how unsure the system is about your rating; smaller means surer;
- **volatility** (σ) — how erratic your results have been.

| Constant | Value | What it does |
|---|---|---|
| Starting rating | `1500` | What a player with no rated match is worth |
| Starting RD | `500` | The widest uncertainty there is |
| Starting volatility | `0.09` | |
| τ (system constant) | `0.75` | How fast volatility is allowed to change |
| RD range | `45` – `500` | Clamped after every match |
| Volatility cap | `0.1` | |
| Uncertainty regrowth | `0.21436` rating periods per day | How fast RD grows while you don't play |
| Rating floor | `400` | Nobody goes below it |
| Maximum change in one match | `±700` | |

1v1 and Teams each keep their own three values; they never mix. Every mod shares the same two
ladders.

**RD grows with time, not with matches.** Just before a match, your RD is grown by the time since
your last rated match on that ladder:

```
φ  = RD / 173.7178
t  = 0.21436 × days since your last rated match on this ladder
φ* = √(φ² + t·σ²)              then back to the RD scale, clamped to 45–500
```

An RD of 68 (about 30 matches played) becomes 78 after 30 days away, 96 after 90, 118 after 180,
154 after a year and 207 after two. That is why a returning player moves more for a while. After
the match there is no extra growth (Glickman's step 6 is skipped), so the RD the server shows is
exactly the one your next match starts from.

#### One match, step by step

Ratings are moved onto the Glicko-2 scale, the update is Glickman's, and the result is moved back:

```
μ    = (rating − 1500) / 173.7178          φ = RD / 173.7178   (RD after the time growth above)

g(φ) = 1 / √(1 + 3·φ² / π²)
E    = 1 / (1 + exp(−g(φ_opp) · (μ − μ_opp)))       your expected score
v    = 1 / (g(φ_opp)² · E · (1 − E))
Δ    = v · g(φ_opp) · (s − E)                       s = 1 if you won, 0 if you lost
σ′   = new volatility (Glickman's step 5, Illinois algorithm, τ = 0.75), at most 0.1
φ′   = 1 / √(1/φ² + 1/v)
μ′   = μ + φ′² · g(φ_opp) · (s − E)

raw new rating = 1500 + 173.7178 · μ′
new RD         = 173.7178 · φ′               clamped to 45–500
```

Then three limits, always in this order:

```
change = (raw new rating − old rating) × anti-farm factor
change = clamp(change, −700, +700)
final  = max(400, old rating + change)
```

The anti-farm factor scales the rating change only: your RD and volatility update in full. The
cap is not theoretical — a brand-new player who beats a settled 2400 would gain far more than 700
on the raw formula, and gets +700.

#### Win probability

The server sends each side's chance; the launcher only displays it.

```
P(A wins) = 1 / (1 + exp(−g(√(φA² + φB²)) · (μA − μB)))
```

`μA` and `μB` are each side's mean rating, `φA` and `φB` each side's combined RD (see
[Teams, exactly](#teams-exactly)). The result is rounded to a whole percent and kept between 1 %
and 99 %.

| Match (both settled, RD ≈ 68) | Favourite's chance |
|---|---|
| 1550 vs 1500 | 57 % |
| 1600 vs 1500 | 63 % |
| 1700 vs 1500 | 75 % |
| 1700 vs 1300 | 90 % |

With two newcomers (RD 500), 1600 vs 1500 is only 56 %: uncertainty pulls every prediction
towards 50 %.

#### Teams, exactly

2v2 and 3v3 share the Teams ladder, and a team match is rated side against side:

- your **expected score** comes from **your side's mean rating** against **the other side's mean
  rating**, so teammates face the same odds;
- the other side's uncertainty is the **root mean square** of its RDs, `√(mean of RD²)` — not the
  plain mean, which under-weights one newcomer among veterans, and not `√(ΣRD²)/n`, which shrinks
  with team size and would read differently in a 2v2 and a 3v3 on the same ladder;
- **how far you move** comes from your **own** RD and volatility.

A 2v2 in which side A wins:

| Player | Before | RD | Change |
|---|---|---|---|
| A, newcomer | 1500 | 500 | +236 |
| A, veteran | 1600 | 68 | +12 |
| B, veteran | 1550 | 68 | −9 |
| B, veteran | 1550 | 68 | −9 |

Both A players won the same match and move the same way, but the newcomer moves twenty times more.
The B players lose less than a usual settled loss (−12) because side A's combined RD is large,
√((500² + 68²) / 2) ≈ 357: the system cannot be sure how strong side A really was.

**Evidence.** A team match is rated only when, besides the side that reported it, at least one
player of the **other** side has sent a reading that agrees, from the same game (the same recording
fingerprint). Three readings from the winning side are one claim made three times; one from the
losing side is what makes it believable. The sides in the recording must also be the ones frozen
in the room when the host pressed Start, or the match is stored as a teams mismatch.

#### Anti-farm, exactly

| Wins in a row by the same side over the same opponent | Factor |
|---|---|
| 1st, 2nd | 100 % |
| 3rd | 90 % |
| 4th | 80 % |
| 5th | 70 % |
| 6th | 60 % |
| 7th | 50 % |
| 8th | 40 % |
| 9th | 30 % |
| 10th and later | 20 % |

- "The same opponent" means the same **matchup**: the same ladder and exactly the same players on
  each side, in any order.
- The run drops **one step for every full 24 hours** the two go without playing each other (never
  below the 1st), and starts again the moment the other side wins one.
- **Both sides** are scaled by the same factor. Between two settled, equal players: at 100 % the
  winner gets +12.5 and the loser −12.5; at 40 %, +5 and −5.
- Only the rating change is scaled; the RD still drops as after any match.
- Tournament games are invisible to it: they never count towards a run, never break one, and are
  always at 100 %.

#### Ranking, placement and badges, exactly

- **Placement:** you enter a ladder's table after **10 rated matches in 1v1** or **5 in Teams**.
  Your rating moves from the very first one; placement only decides whether you are *shown* in
  the table.
- **Order:** by rating, highest first.
- **Inactive:** more than **30 days** since your last rated match on that ladder. A label, not a
  penalty: you keep your rating and your place.
- **Badges** come from your **position**, as a share of the table: **Sovereign** the top 10 %,
  **Imperial** down to 25 %, **Industrial** down to 45 %, **Fortress** down to 70 %, **Colonial**
  the rest. Each cut is rounded **up** and is at least one place wide, so a small table still fills
  every age. With 18 players: 1–2 / 3–5 / 6–9 / 10–13 / 14–18. **Discovery** means "not on the
  table yet".
- **Streaks:** wins in a row on one ladder, broken by a loss or by more than **14 days** between
  two rated matches. The 🔥 shows from 3.
- **Win percentage:** wins ÷ (wins + losses), shown from **5 decided matches**.

#### When a match is rated, exactly

The server checks in this order and stops at the first check that fails — that one is the reason
you see:

1. **The mod is on the ladder.** Every mod is, the base game included.
2. **The room was created competitive.**
3. **A shape with a readable winner:** a 1v1, or two equal sides of 2 or 3. A free-for-all or
   uneven sides are not rated.
4. **There was a room**, and **everyone in the report was in it** when the game started.
5. **The times add up:** the match lasted at least **3 minutes**; the reported duration is within
   **2 minutes** of the start and end times; the start is no more than **5 minutes** in the future;
   the report is at most **7 days** old.
6. **Somebody won:** a result of 1 or 0 for some player (stored as ≥ 0.999 or ≤ 0.001). A 0.5
   means "could not be read", never a draw.

After those, four more gates: the same recording can never rate twice (it is recognised by its
fingerprint); the **new-account** rule withholds the rating when **all three** hold — one of the two
accounts is under **7 days** old, the match lasted under **10 minutes**, and the two opponents share
a network (compared through a one-way hash of the address, kept 30 days; see `PRIVACY.md`),
teammates never count; the sides played must match the room's; and a team match waits for the
other side's reading.

#### Walking out, exactly

Only in a **1v1** competitive room, and only when the recording could not name a winner — a
recording that does always wins. A walkout decides the match when **all** of these hold:

- the player's connection to the room dropped at least **5 minutes** after the host pressed Start,
  never came back, and had been gone for at least **90 seconds** when the result came in (the
  launcher reconnects on its own; a blip is not a departure);
- the other player stayed;
- some reading of that match carries a **recording fingerprint** — with no real recording
  anywhere nothing is decided, which is what stops matches being invented by opening a room and
  waiting;
- the same two players haven't had another match decided this way in the last **24 hours**.

Closing only the game never decides anything: in a 1v1 both games end together, so the timing looks
the same after a dodge and after an ordinary ending.

#### Crashes, exactly

A crash voids a rated 1v1 when the **loser's** game crashed and the launcher verified it with four
signals:

1. Windows logged an **Application Error** (event 1000) for the game inside the match's time
   window;
2. the loser's own recording has **no ending**;
3. the launcher did not stop the game itself;
4. the exit code, when it can be read, is a **failure status** (`0xC0000000` or above, except
   `0xFFFFFFFF`, which is what a forced kill leaves).

It only ever voids — nobody wins on a crash — **once per player per 24 hours**, never in a
tournament. If the winner crashes, nothing changes.

#### Matches decided after the fact

- A match stored with no result can be decided later by **either player's own reading**, even after
  the room is gone. You can always concede your own defeat; a reading only gives you a **win** if
  its recording fingerprint matches the one already stored for that match.
- If the host never reported at all (they quit everything mid-match), the server can **found**
  the match from the players' own readings. Conceding your defeat is enough; claiming a win needs a
  second witness — the server must have seen your opponent walk out past the thresholds above. A
  founded match is marked as an inference and is **undone** if a fingerprinted recording of that
  game later says the opposite.

#### Refunds and recalculation

- When a player is banned with a refund, the points you lost to them in rated matches come back,
  summed per ladder, in one notice.
- Every rating is **deterministic**: the server can replay the whole history from the stored
  matches and reach exactly the same numbers. That is how every past match was recalculated once
  when this system arrived.

---

## Español

### Lo esencial

- **Hay dos tablas: 1v1 y Equipos** (2v2 y 3v3 comparten una). Lo que pasa en una no toca la otra.
- **Todos empiezan en 1500**, y al principio el sistema no está seguro de tu nivel. Cuanto menos
  seguro está, más te mueve cada partida.
- **Tus primeras partidas te posicionan**: 10 en 1v1 y 5 en Equipos. Mientras tanto tu puntuación
  lleva un **«?»** y todavía no estás en la tabla.
- **La tabla se ordena por ELO**, de mayor a menor. Treinta días sin una partida puntuada te marcan
  como **INACTIVO**, pero conservas tu puesto.
- **Cuentan las salas competitivas de todos los mods, también el juego base, en la misma
  tabla**, y solo con una grabación que diga quién ganó. Marca **«Record Game»** en la pantalla
  de configuración del juego, en cada partida.
- **Ganarle una y otra vez al mismo rival vale cada vez menos** (desde la tercera victoria
  seguida). Es el antifarmeo; la tarjeta de resultado te avisa cuando se aplicó.
- **En Equipos eliges tu lado en la sala**, y tienes que jugar los mismos equipos dentro del juego.
- **Si sancionan a un tramposo, recuperas los puntos que perdiste contra él.** El launcher te avisa
  una vez.
- **No hay reinicios.** Una sola tabla continua, sin temporadas. No jugar nunca te baja la
  puntuación.
- **¿Quieres los números exactos?** [Los detalles técnicos](#los-detalles-técnicos) tienen las
  fórmulas, todas las constantes y las reglas exactas, al final de esta mitad en español.

### Puntos e incertidumbre

Tu puntuación son dos cosas: un número (tu ELO) y cuánta confianza le tiene el sistema.

- **Cuanta menos confianza, más grandes los cambios.** Alguien nuevo puede moverse cien puntos o
  más en una partida. Alguien con cincuenta partidas se mueve muy poco. Es la misma regla en los
  dos casos, no un tope que aparece después.
- **Cada partida le da más confianza.** Por eso los cambios se achican a medida que juegas.
- **El tiempo sin jugar le quita confianza.** Si vuelves después de meses, tus primeras partidas
  te mueven más de lo normal durante un tiempo, para que te acomodes rápido a tu nivel actual.
- **Tu ELO nunca baja solo.** No jugar solo hace que la próxima partida te mueva más, para arriba o
  para abajo.

Cuánto te mueve una partida también depende de contra quién juegas: ganarle a alguien con mucho
más ELO paga mucho; ganarle a alguien con mucho menos paga poco. Y hay dos límites de seguridad:
ninguna partida puede mover a nadie más de 700 puntos, y nadie baja de 400.

**Cuántos puntos, en la práctica.** Valores aproximados, calculados con el propio código de
puntuación del servidor; el cálculo exacto de cada partida lo hace el servidor:

| Situación | Si ganas | Si pierdes |
|---|---|---|
| Tu primera partida (los dos jugadores nuevos) | +242 | −242 |
| Tu segunda partida | ~ +128 | ~ −128 |
| Después de unas 5 partidas | ~ +51 | ~ −51 |
| Después de unas 10 partidas | ~ +30 | ~ −30 |
| Asentado (unas 30 partidas), contra alguien de tu nivel | ~ +12 | ~ −12 |
| Muy asentado (unas 100 partidas) | ~ +7 | ~ −7 |
| Gran favorito (1700 contra 1300) | +2 | −23 |
| Claro desvalido (1300 contra 1700) | +23 | −2 |
| Favorito (1600 contra 1400) | +6 | −19 |
| Asentado, contra un recién llegado | ~ +7 | ~ −7 |
| De vuelta después de 6 meses sin jugar | ~ +35 | ~ −35 |
| De vuelta después de un año sin jugar | ~ +56 | ~ −56 |

La tabla es para un 1v1 entre jugadores que juegan más o menos una partida puntuada por día, sin
descuento de antifarmeo. Las filas de favorito y desvalido son entre dos jugadores asentados (unas
30 partidas cada uno). Las fórmulas detrás de cada fila están en
[Los detalles técnicos](#los-detalles-técnicos).

Tres cosas que sorprenden y son correctas:

- **Tus primeras partidas mueven muchísimo, a propósito.** Alguien nuevo empieza con la mayor
  incertidumbre posible, y cada partida la achica. A las diez partidas los saltos andan por ±30; a
  las treinta, por ±12.
- **Ganarle al favorito paga unas diez veces más** que ganar lo que se esperaba de ti. Un 1700 que
  le gana a un 1300 suma 2; perder esa misma partida le cuesta 23.
- **No es de suma cero.** En la misma partida uno puede sumar 7 puntos y el otro perder 236: un
  jugador asentado contra alguien nuevo. Cada uno se mueve según lo seguro que esté el sistema de
  *su* nivel, no según lo que le pasó al otro.

**Por qué a veces solo se mueven unos pocos puntos.** El sistema guarda cuánta confianza le tiene a
tu puntuación (el *RD*, mira [Los detalles técnicos](#los-detalles-técnicos)). Jugando más o menos
una vez por día, se asienta alrededor de 50 después de cien partidas y nunca llega del todo a su
mínimo de 45, porque cada día sin jugar le devuelve un poco de incertidumbre. Por eso la partida de
un veterano mueve pocos puntos y la de alguien que vuelve mueve decenas: es la misma regla, no un
tope que aparece después.

**Probabilidad de victoria.** En una sala competitiva el launcher muestra la probabilidad de ganar
de cada lado. La calcula el **servidor** con la puntuación de los dos lados y la confianza que le
tiene a cada uno; el launcher solo la muestra. Es una orientación, no una promesa: dos jugadores
asentados con 100 puntos de diferencia quedan 63 % contra 37 %, y con 400 puntos, 90 % contra 10 %.

### Posicionamiento

Tus primeras **10 partidas puntuadas en 1v1** (o **5 en Equipos**) son de posicionamiento.

- Durante el posicionamiento tu puntuación aparece con un **«?»** (por ejemplo **1580?**) en todos
  lados, y tu perfil muestra cuántas te faltan.
- **Todavía no estás en la tabla**: tu insignia es **Descubrimiento**. La clasificación muestra a
  los jugadores en posicionamiento aparte, al final.
- Las partidas cuentan como cualquier otra: mueven tu puntuación, cuentan para las rachas y el
  antifarmeo también se aplica.
- Cuando terminas, la tarjeta de resultado te dice **en qué puesto entras en la tabla**.

### La tabla y los inactivos

- **La tabla se ordena por ELO**, de mayor a menor. El número que ves al lado de cada nombre es el
  que decide el orden.
- **Las insignias de rango** (el escudo junto al nombre) dependen de tu puesto: lo más alto de la
  tabla lleva la edad más alta y el resto sigue según su parte de la tabla. Mira
  [Tus insignias de rango](#tus-insignias-de-rango).
- **Treinta días sin una partida puntuada** te marcan como **INACTIVO** en la tabla. Conservas tu
  puntuación y tu puesto; una partida puntuada quita la etiqueta.
- **El porcentaje de victorias aparece a partir de 5 partidas decididas.** Con menos no sería un
  porcentaje: un 0 % por una sola derrota no dice nada de nadie. El número de partidas decididas se
  muestra siempre.
- La clasificación está en la pestaña **Clasificación**; la pestaña **Salas** muestra los cinco
  primeros en el panel de la comunidad, con el enlace **Ver todo**.

### Tus insignias de rango

Tienes **una insignia por cada tabla**, y cada una sale de tu puesto en esa tabla.

- **La insignia de 1v1 es un escudo; la de Equipos son dos escudos** de la misma edad. Mientras
  todavía estás en posicionamiento en una tabla, su insignia es **Descubrimiento**.
- **Dentro de una sala, decide la sala**: una sala 1v1 muestra la insignia de 1v1 de todos, y una
  sala 2v2 o 3v3 la de Equipos de todos.
- **En todo lo demás eliges tú**: en el chat, la lista de jugadores conectados, tu cuenta y las
  salas casuales. En tu **Perfil**, en **Insignia junto a tu nombre**, elige **La más alta** (la
  opción por defecto), **1v1** o **Equipos**. *La más alta* muestra la insignia de mayor edad; si
  las dos tienen la misma edad, la de 1v1. **Equipos** se habilita cuando ya jugaste una partida en
  equipo.
- **La puntuación junto a una insignia es la de esa tabla**: al lado de la insignia de Equipos ves
  tu puntuación de equipos.
- **Cómo funcionan los rangos**, en el menú de tu cuenta, explica cada edad, con una pestaña de
  1v1 y otra de Equipos.

### Equipos

Las partidas 2v2 y 3v3 puntúan en la tabla de **Equipos**, separada de la de 1v1.

- **Eliges tu lado en la sala.** Cada jugador elige **1** o **2**; el anfitrión puede mover a
  cualquiera. Quien todavía no eligió espera en la caja **Sin equipo**.
- **Una sala competitiva de equipos solo empieza cuando está completa**: todos los asientos
  ocupados, todos con equipo y la misma cantidad en cada lado. Si no, el botón **Empezar** queda
  bloqueado y dice por qué.
- **Jueguen los mismos equipos dentro del juego.** Los equipos también se eligen dentro de Age of
  Empires III. Si los lados que grabó el juego no coinciden con los de la sala, **la partida no
  cuenta**, y la tarjeta de resultado dice qué equipos tenía el juego.
- **El cambio de cada uno sale de la media de los dos equipos.** Tu propia confianza decide cuánto
  te mueves: alguien en posicionamiento se mueve más que un compañero con cien partidas.
- **Necesita la confirmación del otro lado.** Una partida de equipos puntúa cuando alguien del otro
  equipo también lee la grabación y las dos lecturas coinciden. No tienes que hacer nada: basta con
  que uno de ellos cierre el juego. Mientras tanto aparece como pendiente.

### Rachas

- Desde **3 victorias seguidas** aparece un 🔥 junto a tu nombre en la tabla, con el número.
- La racha **se corta cuando pierdes**, o después de **14 días sin una partida puntuada**.
- Tu **racha de victorias más larga** y tu **racha de derrotas más larga** quedan guardadas en tu
  perfil.

### Antifarmeo

Ganarle una y otra vez al mismo rival vale cada vez menos. Así dos cuentas no pueden regalarse
puntos, y a un jugador normal, que cambia de rival, no le cuesta nada.

- **La 1.ª y la 2.ª victoria seguidas** contra el mismo rival cuentan enteras.
- **Desde la 3.ª** cada una vale un poco menos: 90 %, 80 %, 70 %… hasta **20 %** desde la 10.ª
  victoria seguida.
- **Vuelve a la normalidad** en cuanto el otro jugador gana una, y se recupera un paso por cada día
  completo que pasen sin jugar entre ustedes.
- **Afecta a los dos jugadores**: el que gana suma menos y el que pierde también pierde menos.
- **En Equipos** se aplica solo cuando se repite **exactamente el mismo enfrentamiento** (los mismos
  jugadores en cada lado).
- Se aplica **también durante el posicionamiento** y **nunca en los torneos**.
- **No se avisa en la sala.** Después, la tarjeta de resultado y tu Historial muestran el porcentaje
  que se aplicó (por ejemplo «+5 · 40 %»).

### Marca «Record Game». Cada partida.

Age of Empires III **desmarca esa casilla en cada partida**, y nada fuera del juego puede dejarla
marcada. Por eso el launcher te lo recuerda en la sala antes de empezar, y te avisa después si se
jugó sin grabar.

Sin grabación nadie sabe quién ganó: ni el launcher ni el servidor. **Si ninguno de los dos graba,
el resultado se pierde para ambos.** Si solo uno grabó, todavía se puede salvar: mira
[Si tu rival grabó y tú no](#si-tu-rival-grabó-y-tú-no).

El launcher solo borra las grabaciones viejas que creó él; las que renombraste no se tocan nunca.

### Partidas que no cuentan

El servidor decide si una partida cuenta y **dice por qué**; el launcher muestra ese motivo en la
tarjeta de resultado y en tu Historial. En todos los casos **la partida queda en tu historial**:
solo no se mueve la puntuación.

| Lo que ves | Qué pasó | Qué hacer |
|---|---|---|
| «Sala casual: no mueve el ELO.» | La sala se creó sin marcar **Sala competitiva** | Marca la casilla al crear la sala |
| «Este mod no estaba en la clasificación cuando se jugó la partida» | Una partida antigua: en ese momento solo puntuaba Wars of Liberty | Nada. Ahora cuentan todos los mods |
| «No se pudo leer el resultado: Record Game estaba desactivado.» | No apareció ninguna grabación de esa partida | Marca «Record Game» antes de la próxima |
| «Esta partida SÍ se grabó, pero el juego se cerró antes de terminar de escribir el final» | La grabación no tiene final | **Sal de la partida al menú principal antes de cerrar AoE3** |
| «Se encontraron grabaciones, pero en ninguna apareces entre los jugadores» | Tu nombre de perfil de AoE3 no es con el que juegas | Revisa tu nombre de perfil |
| «Los equipos del juego no coincidieron con los de la sala.» | Los lados que se jugaron no eran los elegidos en la sala | Elijan en el juego los mismos equipos que en la sala |
| «Esta partida no contó para el ELO (cuenta nueva y partida muy corta).» | Una cuenta de menos de 7 días, una partida de menos de 10 minutos y los dos jugadores en la misma red | Nada. Las partidas reales cuentan con normalidad |
| «El resultado ya está, pero la clasificación de equipos necesita además una lectura del otro bando» | Una partida de equipos que tu lado ya informó | Nada. Cuenta cuando el otro lado confirme |
| «Los tiempos de esta partida no cuadran» | Duró menos de 3 minutos, o los relojes no coinciden | Nada, salvo jugar partidas de verdad |
| «Alguien de este reporte no estaba en la sala cuando empezó la partida» | Alguien entró después de empezar | Que todos estén en la sala antes de empezar |
| «Esta grabación ya se había reportado» | Esa partida ya se había contado | Nada |
| «El juego del perdedor se cerró por un fallo… esta partida se anuló automáticamente» | Windows registró el fallo | Nada. Se perdona una vez al día por jugador |

### Abandonar una partida competitiva

En una sala competitiva **1v1**, cuando la partida ya lleva **cinco minutos** (contados desde que
el anfitrión presionó Empezar), irse y no volver cuenta como **derrota**.

- **Irse es cortar la conexión**: salir de la sala o **cerrar del todo** el launcher. **Ocultarlo
  en la bandeja con la ✕ no es irse.** Si el launcher se cierra solo, al abrirlo de nuevo retoma la
  partida.
- **Cerrar solo el juego tampoco es irse.** Ahí decide la grabación de tu rival, que dice quién
  perdió.
- **Si se van los dos, no gana nadie.**
- **Una grabación que dice quién ganó siempre manda** sobre esta regla.
- No se aplica a las salas de equipos.

### Si el juego se cierra por un fallo

Cuando el juego falla de verdad, Windows lo registra y el launcher lo comprueba. Si quien sufrió el
fallo iba perdiendo, la partida se **anula**: nadie gana ni pierde puntos. Se perdona **una vez al
día por jugador**; desde el segundo fallo en 24 horas vuelve a contar como derrota. Cerrar el juego
desde el Administrador de tareas no es un fallo. Las partidas de torneo nunca se anulan.

### Una partida puede puntuar más tarde

El resultado se guarda enseguida y la grabación se lee en segundo plano. Una partida que dice «sin
resultado» **no está cerrada**: si después aparece una lectura —la tuya al cerrar el juego, o la de
tu rival— igual puntúa, aunque la sala ya no exista. Te llega una notificación cuando pasa.

### Si tu rival grabó y tú no

Al terminar una partida, el jugador que no es anfitrión también envía su lectura de la grabación,
automáticamente. Si la partida se había guardado sin ganador y la grabación del otro dice quién
ganó, esa lectura la decide. Para que nadie invente resultados: **siempre puedes reconocer tu
propia derrota; tu grabación solo te da una victoria si coincide con la partida ya guardada.**

### Devolución de puntos

Si **sancionan a un jugador por hacer trampas**, todos los que perdieron partidas puntuadas contra
él **recuperan esos puntos**.

- Recibes **una notificación**, y un aviso en tu perfil, encima de la tarjeta de la tabla afectada,
  hasta que presionas **Entendido**.
- Si perdiste varias partidas contra él, **todo se suma en un solo aviso**.
- **Nunca se nombra al jugador sancionado.**

### No hay reinicios

Hay **una sola tabla continua**. No hay temporadas ni una fecha en la que todos empiezan de nuevo.
Tu puntuación, tu puesto y tu historial siguen mientras juegues.

Cuando llegó este sistema, todas las partidas puntuadas ya jugadas se recalcularon una vez con
estas reglas, así que tu puntuación y tu posicionamiento ya las tienen en cuenta.

### Torneos

Una partida de torneo puntúa como cualquier otra partida competitiva, **siempre entera**: el
antifarmeo nunca se aplica, porque el cuadro decide quién juega contra quién. Una victoria por
incomparecencia o una descalificación no mueve la puntuación de nadie. Una partida de torneo por
equipos necesita la confirmación del otro lado, como cualquier partida de equipos.

### Destacados del mes

Son siete:

- quién más subió (con la tabla; solo quienes jugaron al menos 5 partidas puntuadas ese mes
  después de terminar el posicionamiento);
- quién ganó más partidas puntuadas (si empatan, quien jugó menos);
- quién jugó más partidas puntuadas;
- la mejor racha del mes;
- el mejor porcentaje de victorias (solo quienes jugaron al menos 10 partidas puntuadas ese mes);
- la mayor sorpresa: la victoria contra el lado con más ELO de ventaja;
- la civilización del mes (la más elegida, al menos 3 veces).

En la pestaña **Salas**, el bloque **Actividad de la comunidad** muestra tres — quién más subió,
quién jugó más y la mejor racha — junto a las cifras de la comunidad (partidas, jugadores, el
mapa más jugado). **Ver septiembre** muestra los del mes anterior.

En la pestaña **Clasificación**, **Destacados** (junto a 1v1 y Equipos) muestra los siete a
fondo: los cinco primeros de cada uno, con cuánto subieron, cuántas ganaron, etcétera. La subida
y la racha se pueden ver en 1v1 o en Equipos.

Los jugadores en posicionamiento no cuentan para la subida, y una partida en la que alguien
seguía en posicionamiento no cuenta como sorpresa. El día 1 también se publican en Discord la
subida, las más partidas y la mejor racha del mes anterior.

### Dónde ves tu puntuación

- En **tu cuenta**, arriba a la derecha.
- En tu **Perfil**: una tarjeta por tabla, con tu máximo y tu mínimo, tus rachas y cómo te va contra
  cada rival.
- En la **lista de jugadores** de una sala, y en la **tabla de salas** junto al anfitrión.
- En el panel de **jugadores conectados** y en la pestaña **Clasificación**.
- En la **tarjeta de resultado** al terminar una partida, y en cada fila del **Historial**.

### Preguntas frecuentes

**¿Por qué mi puntuación tiene un «?»?**
Todavía estás en posicionamiento: 10 partidas puntuadas en 1v1, 5 en Equipos. Desaparece cuando
terminas.

**Gané y sumé pocos puntos. ¿Por qué?**
O tu rival tenía mucho menos ELO que tú, o ya tienes muchas partidas y el sistema está seguro de tu
nivel, o se aplicó el antifarmeo (la tarjeta lo dice, con el porcentaje).

**Hace meses que no juego. ¿Perdí puntos?**
No. Tu ELO nunca baja solo. Solo que tus próximas partidas te moverán más de lo normal durante un
tiempo.

**¿Por qué dice INACTIVO junto a mi nombre?**
Treinta días sin una partida puntuada. Conservas tu puesto; una partida puntuada lo quita.

**Jugué una partida entera y no sumé nada.**
La tarjeta de resultado y tu Historial dicen por qué. Lo más común: la sala no era competitiva, o
nadie marcó «Record Game».

**¿Y los empates?**
No hay. Una partida cuyo resultado no se pudo leer **no es un empate**; simplemente no mueve la
puntuación de nadie.

**¿Puedo reportar la misma partida dos veces?**
No. El servidor reconoce la grabación y la partida; el segundo intento no cuenta.

**¿Se sube mi grabación a algún lado?**
No. Se lee **en tu propia PC**. Al servidor solo viajan el resultado y algunos datos de la partida,
nunca el archivo.

**Mi perfil cuenta más partidas «decididas» que «puntuadas». ¿Por qué?**
Cuentan cosas distintas. Las **puntuadas** son las que movieron tu ELO. Las **decididas** son todas
las partidas en las que se supo quién ganó, incluidas las que no puntuaron por algún otro motivo
(una sala casual, una partida vieja de un mod que todavía no estaba en la clasificación). Las
decididas siempre son iguales o más.

### Los detalles técnicos

Todo lo de esta sección sale del propio código de puntuación del servidor (`src/elo/` en el backend
del lobby), y todos los números de este documento se calcularon con ese mismo código. Si allí cambia
una constante, esta sección cambia con ella.

#### El motor

**Glicko-2** (Mark Glickman, 2013), usado como lo usa Lichess. En cada tabla tienes tres valores:

- **puntuación** — el número que ves;
- **RD** (desviación de la puntuación) — cuánta duda tiene el sistema sobre tu puntuación; más
  bajo significa más seguro;
- **volatilidad** (σ) — qué tan irregulares fueron tus resultados.

| Constante | Valor | Para qué sirve |
|---|---|---|
| Puntuación inicial | `1500` | Lo que vale alguien sin partidas puntuadas |
| RD inicial | `500` | La mayor incertidumbre posible |
| Volatilidad inicial | `0.09` | |
| τ (constante del sistema) | `0.75` | Qué tan rápido puede cambiar la volatilidad |
| Rango del RD | `45` – `500` | Se ajusta a ese rango después de cada partida |
| Tope de la volatilidad | `0.1` | |
| Recuperación de la incertidumbre | `0.21436` períodos por día | Qué tan rápido crece el RD mientras no juegas |
| Puntuación mínima | `400` | Nadie baja de ahí |
| Cambio máximo en una partida | `±700` | |

1v1 y Equipos guardan cada uno sus tres valores; nunca se mezclan. Todos los mods comparten las
mismas dos tablas.

**El RD crece con el tiempo, no con las partidas.** Justo antes de una partida, tu RD crece según
el tiempo desde tu última partida puntuada en esa tabla:

```
φ  = RD / 173.7178
t  = 0.21436 × días desde tu última partida puntuada en esta tabla
φ* = √(φ² + t·σ²)              y de vuelta a la escala del RD, ajustado a 45–500
```

Un RD de 68 (unas 30 partidas jugadas) pasa a 78 después de 30 días sin jugar, a 96 después de 90,
a 118 después de 180, a 154 después de un año y a 207 después de dos. Por eso quien vuelve se mueve
más durante un tiempo. Después de la partida no hay ningún crecimiento extra (se omite el paso 6
de Glickman), así que el RD que muestra el servidor es exactamente con el que empieza tu próxima
partida.

#### Una partida, paso a paso

Las puntuaciones pasan a la escala de Glicko-2, se aplica la actualización de Glickman y el
resultado vuelve a la escala normal:

```
μ    = (puntuación − 1500) / 173.7178      φ = RD / 173.7178   (el RD después del crecimiento por tiempo)

g(φ) = 1 / √(1 + 3·φ² / π²)
E    = 1 / (1 + exp(−g(φ_rival) · (μ − μ_rival)))   tu resultado esperado
v    = 1 / (g(φ_rival)² · E · (1 − E))
Δ    = v · g(φ_rival) · (s − E)                     s = 1 si ganaste, 0 si perdiste
σ′   = volatilidad nueva (paso 5 de Glickman, algoritmo Illinois, τ = 0.75), como mucho 0.1
φ′   = 1 / √(1/φ² + 1/v)
μ′   = μ + φ′² · g(φ_rival) · (s − E)

puntuación nueva (sin límites) = 1500 + 173.7178 · μ′
RD nuevo                       = 173.7178 · φ′          ajustado a 45–500
```

Después vienen tres límites, siempre en este orden:

```
cambio = (puntuación nueva sin límites − puntuación anterior) × factor de antifarmeo
cambio = limitar(cambio, −700, +700)
final  = máximo(400, puntuación anterior + cambio)
```

El factor de antifarmeo solo escala el cambio de puntuación: tu RD y tu volatilidad se actualizan
completos. El tope no es teórico: alguien completamente nuevo que le gana a un 2400 asentado
sumaría mucho más de 700 con la fórmula sin límites, y suma +700.

#### Probabilidad de victoria

El servidor envía la probabilidad de cada lado; el launcher solo la muestra.

```
P(gana A) = 1 / (1 + exp(−g(√(φA² + φB²)) · (μA − μB)))
```

`μA` y `μB` son la puntuación media de cada lado, `φA` y `φB` el RD combinado de cada lado (mira
[Equipos, en detalle](#equipos-en-detalle)). El resultado se redondea a un porcentaje entero y queda
siempre entre 1 % y 99 %.

| Partida (los dos asentados, RD ≈ 68) | Probabilidad del favorito |
|---|---|
| 1550 contra 1500 | 57 % |
| 1600 contra 1500 | 63 % |
| 1700 contra 1500 | 75 % |
| 1700 contra 1300 | 90 % |

Con dos jugadores nuevos (RD 500), 1600 contra 1500 es solo 56 %: la incertidumbre acerca toda
predicción al 50 %.

#### Equipos, en detalle

El 2v2 y el 3v3 comparten la tabla de Equipos, y una partida de equipos se puntúa lado contra lado:

- tu **resultado esperado** sale de **la puntuación media de tu lado** contra **la puntuación media
  del otro lado**, así todos los compañeros tienen las mismas probabilidades;
- la incertidumbre del otro lado es la **raíz media cuadrática** de sus RD, `√(media de RD²)`; no
  la media simple, que le da poco peso a un jugador nuevo entre veteranos, ni `√(ΣRD²)/n`, que se
  achica con el tamaño del equipo y daría resultados distintos en 2v2 y en 3v3 dentro de la misma
  tabla;
- **cuánto te mueves** sale de **tu propio** RD y tu volatilidad.

Un 2v2 en el que gana el lado A:

| Jugador | Antes | RD | Cambio |
|---|---|---|---|
| A, nuevo | 1500 | 500 | +236 |
| A, veterano | 1600 | 68 | +12 |
| B, veterano | 1550 | 68 | −9 |
| B, veterano | 1550 | 68 | −9 |

Los dos jugadores de A ganaron la misma partida y se mueven en la misma dirección, pero el nuevo se
mueve veinte veces más. Los de B pierden menos que en una derrota normal entre asentados (−12)
porque el RD combinado del lado A es grande, √((500² + 68²) / 2) ≈ 357: el sistema no puede estar
seguro de qué tan fuerte era realmente el lado A.

**Pruebas.** Una partida de equipos solo puntúa cuando, además del lado que la informó, al menos un
jugador del **otro** lado envió una lectura que coincide, del mismo juego (la misma huella de la
grabación). Tres lecturas del lado ganador son la misma afirmación repetida tres veces; una del lado
perdedor es la que la hace creíble. Además, los lados de la grabación tienen que ser los que quedaron
fijados en la sala cuando el anfitrión presionó Empezar; si no, la partida se guarda como «equipos
que no coinciden».

#### Antifarmeo, en detalle

| Victorias seguidas del mismo lado contra el mismo rival | Factor |
|---|---|
| 1.ª y 2.ª | 100 % |
| 3.ª | 90 % |
| 4.ª | 80 % |
| 5.ª | 70 % |
| 6.ª | 60 % |
| 7.ª | 50 % |
| 8.ª | 40 % |
| 9.ª | 30 % |
| 10.ª y siguientes | 20 % |

- «El mismo rival» significa el mismo **enfrentamiento**: la misma tabla y exactamente los mismos
  jugadores en cada lado, en cualquier orden.
- La racha baja **un paso por cada 24 horas completas** que los dos pasan sin jugar entre ellos
  (nunca por debajo de la 1.ª), y vuelve a empezar en cuanto el otro lado gana una.
- **Los dos lados** se escalan con el mismo factor. Entre dos jugadores asentados y parejos: al
  100 % el ganador suma +12.5 y el perdedor −12.5; al 40 %, +5 y −5.
- Solo se escala el cambio de puntuación; el RD baja igual que después de cualquier partida.
- Las partidas de torneo no existen para esta regla: nunca suman a una racha, nunca la cortan y
  siempre valen el 100 %.

#### Clasificación, posicionamiento e insignias, en detalle

- **Posicionamiento:** entras en la tabla después de **10 partidas puntuadas en 1v1** o **5 en
  Equipos**. Tu puntuación se mueve desde la primera; el posicionamiento solo decide si *apareces*
  en la tabla.
- **Orden:** por puntuación, de mayor a menor.
- **Inactivo:** más de **30 días** desde tu última partida puntuada en esa tabla. Es una etiqueta,
  no una sanción: conservas tu puntuación y tu puesto.
- **Insignias:** salen de tu **puesto**, como parte de la tabla: **Soberano** el 10 % de arriba,
  **Imperial** hasta el 25 %, **Industrial** hasta el 45 %, **Fortalezas** hasta el 70 %,
  **Colonial** el resto. Cada corte se redondea **para arriba** y ocupa al menos un puesto, así una
  tabla pequeña igual llena todas las edades. Con 18 jugadores: 1–2 / 3–5 / 6–9 / 10–13 / 14–18.
  **Descubrimiento** significa «todavía no estás en la tabla».
- **Rachas:** victorias seguidas en una tabla; se cortan con una derrota o con más de **14 días**
  entre dos partidas puntuadas. El 🔥 aparece desde 3.
- **Porcentaje de victorias:** victorias ÷ (victorias + derrotas), se muestra a partir de **5
  partidas decididas**.

#### Cuándo puntúa una partida, en detalle

El servidor revisa en este orden y se detiene en lo primero que falla; ese es el motivo que ves:

1. **El mod está en la clasificación.** Lo están todos, también el juego base.
2. **La sala se creó como competitiva.**
3. **Una forma con un ganador que se pueda leer:** un 1v1, o dos lados iguales de 2 o 3. Un todos
   contra todos o lados desparejos no puntúan.
4. **Hubo una sala**, y **todos los del reporte estaban en ella** cuando empezó la partida.
5. **Los tiempos cuadran:** la partida duró al menos **3 minutos**; la duración informada está a
   menos de **2 minutos** de las horas de inicio y fin; el inicio no está más de **5 minutos** en el
   futuro; el reporte tiene como mucho **7 días**.
6. **Alguien ganó:** un resultado de 1 o 0 para algún jugador (guardado como ≥ 0.999 o ≤ 0.001). Un
   0.5 significa «no se pudo leer», nunca un empate.

Después de eso hay cuatro filtros más: la misma grabación nunca puntúa dos veces (se reconoce por su
huella); la regla de **cuenta nueva** deja la partida sin puntuar cuando se cumplen **las tres
cosas** a la vez: una de las dos cuentas tiene menos de **7 días**, la partida duró menos de **10
minutos** y los dos rivales comparten red (se compara con un hash de un solo sentido de la
dirección, que se guarda 30 días; mira `PRIVACY.md`), y los compañeros de equipo nunca cuentan; los
lados jugados tienen que coincidir con los de la sala; y una partida de equipos espera la lectura
del otro lado.

#### Abandono, en detalle

Solo en una sala competitiva **1v1**, y solo cuando la grabación no pudo decir quién ganó: una
grabación que lo dice siempre manda. Un abandono decide la partida cuando se cumple **todo** esto:

- la conexión del jugador con la sala se cortó al menos **5 minutos** después de que el anfitrión
  presionara Empezar, no volvió, y llevaba al menos **90 segundos** caída cuando llegó el resultado
  (el launcher se reconecta solo; un corte de un momento no es irse);
- el otro jugador se quedó;
- alguna lectura de esa partida tiene **huella de grabación**: sin una grabación real en ningún
  lado no se decide nada, y eso es lo que impide inventar partidas abriendo una sala y esperando;
- esos dos jugadores no tuvieron otra partida decidida así en las últimas **24 horas**.

Cerrar solo el juego nunca decide nada: en un 1v1 los dos juegos terminan juntos, así que los
tiempos se ven iguales después de una huida y después de un final normal.

#### Fallos del juego, en detalle

Un fallo anula un 1v1 puntuado cuando el juego del **perdedor** se cerró por un fallo y el launcher
lo comprobó con cuatro señales:

1. Windows registró un **Application Error** (evento 1000) del juego dentro del tiempo de la
   partida;
2. la grabación del propio perdedor **no tiene final**;
3. el launcher no cerró el juego por su cuenta;
4. el código de salida, cuando se puede leer, es un **estado de fallo** (`0xC0000000` o mayor,
   salvo `0xFFFFFFFF`, que es lo que deja un cierre forzado).

Solo anula —nadie gana por un fallo—, **una vez por jugador cada 24 horas**, y nunca en un torneo.
Si el que falla es el ganador, no cambia nada.

#### Partidas decididas después

- Una partida guardada sin resultado se puede decidir después con **la lectura de cualquiera de los
  dos jugadores**, aunque la sala ya no exista. Siempre puedes reconocer tu propia derrota; una
  lectura solo te da una **victoria** si la huella de su grabación coincide con la que ya estaba
  guardada para esa partida.
- Si el anfitrión nunca informó la partida (lo cerró todo en medio de la partida), el servidor puede
  **crearla** a partir de las lecturas de los jugadores. Reconocer tu derrota alcanza; reclamar una
  victoria necesita un segundo testigo: el servidor tiene que haber visto a tu rival irse pasando los
  límites de arriba. Una partida creada así queda marcada como deducida y se **deshace** si después
  aparece una grabación con huella de esa partida que dice lo contrario.

#### Devoluciones y recálculo

- Cuando se sanciona a un jugador con devolución, los puntos que perdiste contra él en partidas
  puntuadas vuelven, sumados por tabla, en un solo aviso.
- Cada puntuación es **determinista**: el servidor puede volver a calcular toda la historia a partir
  de las partidas guardadas y llegar exactamente a los mismos números. Así se recalcularon una vez
  todas las partidas anteriores cuando llegó este sistema.
