# Tournaments · Torneos

> **La versión en español está más abajo, en este mismo documento.** ·
> *The Spanish version is further down, in this same document.*
>
> How the rating itself works is in [ELO.md](ELO.md). · *Cómo funciona la puntuación en sí
> está en [ELO.md](ELO.md).*

---

## English

### The short version

- A tournament is a **single-elimination bracket**: lose once and you are out. Any signed-in
  player can create one, in **MULTIPLAYER → Tournaments**.
- **Every match is a single game** (best of one).
- **Up to 16 places.** If places are left empty, the best seeds get a **bye** straight into the
  next round.
- **Seeds come from your 1v1 rating**, in its cautious form: a player with few games is placed
  lower until the system is sure of them.
- **You play your match in an ordinary competitive room** that only you and your opponent can
  enter. You open it from the bracket. Tick **"Record Game"**, as in every competitive match.
- **The bracket moves by itself when the game names a winner.** If the result cannot be read, the
  match stays pending and you can play it again.
- **Tournament games count for ELO at full value**: the anti-farm rule never applies and a crash
  never voids them. A walkover or a disqualification moves nobody's rating.
- **Nothing is decided by the clock**: no deadlines and no automatic forfeits. If somebody does
  not show up, the organiser decides the match.
- **Today only 1v1 tournaments can be played from the launcher.** 2v2 and 3v3 can be created,
  but teams cannot register yet — see
  [Team tournaments (2v2 and 3v3)](#team-tournaments-2v2-and-3v3).

### Before you play

- **Sign in with Discord** in the launcher. You need it to enter, to create a tournament and to
  play your matches.
- **Have the tournament's mod installed and active** (the mod selected in the **Play** tab). The
  launcher checks it before opening your room and stops you with **"Wrong mod"** if it is
  another one.
- **Same version of the mod as your opponent.** It is the same check as for any room: different
  files, no entry.
- **Radmin VPN**, as for any multiplayer match.
- **"Record Game" ticked** on the game's setup screen. Without a recording nobody knows who won,
  and the match stays pending.

### Where tournaments are

**MULTIPLAYER → Tournaments.** On the left, the list; on the right, the selected tournament with
its bracket and its list of entrants.

The list shows the 50 tournaments with the most recent activity, finished ones included. A
tournament that has not been published yet is only seen by the person who created it. Each card
shows its status, how many places are taken ("5 of 8 places") and, to its creator, how many
applications are waiting.

| Status | What it means |
|---|---|
| **Not published** | Just created. Only its creator sees it. |
| **Registration open** | You can enter. |
| **Registration closed** | Nobody else gets in. The organiser is preparing the seeds and the bracket. |
| **In progress** | The bracket is drawn and matches are being played. |
| **Finished** | It has a champion. |
| **Cancelled** | It was cancelled, by its creator or by the project's administrators. |
| **Abandoned** | Thirty days with no activity. It was archived: nobody is crowned and no rating changes. |

### Entering

Press **Enter** while registration is open. How you get a place depends on what the organiser
chose:

- **First come, first served.** You are **In** if there is a free place. If not, you are put on
  the waiting list (**Waiting**), in order of arrival.
- **The organiser accepts each one.** You appear as **Applied** until the organiser accepts you
  (**In**, or **Waiting** if the places are already full) or rejects you.

| Your status | What it means |
|---|---|
| **In** | You have a place. You will be in the bracket. |
| **Waiting** | On the waiting list. If a place frees up while registration is still open, the first one waiting gets it. |
| **Applied** | Waiting for the organiser to decide. |
| **Withdrew** | You left the tournament. |
| **Rejected** | The organiser said no. |
| **Disqualified** | The organiser removed you from a tournament in progress. |

Good to know:

- **Only players who are In when the bracket is drawn will play.** Whoever is still Waiting or
  Applied at that moment stays out.
- A place frees up for the waiting list **only while registration is open**. Once it is closed,
  places no longer move on their own.
- **You can withdraw** (**Withdraw**) until the bracket is drawn, and enter again if registration
  is still open. **After the bracket is drawn you can no longer withdraw**: tell the organiser.
- Your name in the bracket is the one you had **when you entered**. Changing it later does not
  change it there.
- The organiser can enter and play their own tournament.

### How the seeds are decided

Before drawing the bracket, the organiser presses **Seed**, and the launcher numbers the entrants
from strongest to weakest.

- Strength is your **1v1 rating minus twice its uncertainty** — the cautious estimate. That way a
  player with two lucky games does not pass a settled player, and the bye goes to whoever has
  earned it.
- If you have no rated 1v1 match, you count as an unknown player (1500 with the maximum
  uncertainty) and **you go to the bottom**.
- A tie goes to **whoever entered first**.
- It is your rating on the **whole 1v1 ladder**. The ladder is not split by mod, so it does not
  matter which mod you played your rated games on.
- The organiser can seed again as many times as they want before drawing the bracket.

### The bracket

- **Its size is the smallest power of two that fits everyone**: 2, 4, 8 or 16. That gives 1, 2, 3
  or 4 rounds.
- **Empty places are byes for the best seeds** (**BYE** on the card). Example: 6 entrants → a
  bracket of 8 → seeds 1 and 2 go straight to the semi-finals.
- **Order of the first round** (bracket of 8): 1 v 8, 4 v 5, 2 v 7, 3 v 6. **Seeds 1 and 2 can
  only meet in the final.**
- The rounds are called **FINAL**, **SEMI-FINALS**, **QUARTER-FINALS** and, before that,
  **ROUND 1**, **ROUND 2**…
- On every card: **1** and **0** for a match that was played; **W.O.** for a match the organiser
  awarded (walkover); **Disq.** for one won because the other side was disqualified; **BYE** for
  a pass. A slot that is still unknown reads "Winner of …" until that match ends. Your side
  carries the tag **YOU**.

### Playing your match

**Click your match in the bracket.** A bar appears above it with the two names, the round and the
button that applies:

| Button | When |
|---|---|
| **Open my room** | Both sides are known and nobody has opened the room yet. It creates the room and you are the host. |
| **Enter the room** | Your opponent already opened it. You go in. |
| **Back to my room** | You opened it and it is still open. |
| *(no button)* | The other side is not known yet, or the match is somebody else's. |

When your match is ready, the tournament's header and its card in the list say **"Your turn to
play"**.

What you need to know about the room:

- **If both of you press "Open my room", you both end up in the same room.**
- It is a **competitive room for two**, with a title the server writes:
  `Tournament name · R1 · Player A · Player B` (`Final` in the final).
- **Only the two players of that match can enter.** No spectators and no password. Anybody else
  is told *"That room belongs to a tournament match between two other players."*
- It is **not announced** in the global chat or on Discord.
- **Opening it closes any other room you were hosting.** And while your tournament room is open
  and the match is undecided, **you cannot create another room**: the server answers *"You still
  have a tournament match room open"*. Leave it first.
- Inside, **the usual rules of a competitive room apply**: the launcher reminds you about Record
  Game before starting, and the host stays until the result is sent.
- **Walking out loses.** In a 1v1 competitive room, leaving after five minutes of play and not
  coming back counts as a loss (see [ELO.md](ELO.md#walking-out-of-a-competitive-match)). That
  loss also moves the bracket.
- **A crash does not void a tournament match.** Outside tournaments the game can be voided if
  the loser's game crashes; here, never.

### After the game

- **If the recording names a winner, the bracket moves immediately**: the winner goes to their
  next match and both of you get a notification.
- **The bracket does not depend on the ELO.** A match the ladder does not rate (a recording
  already reported, times that do not add up, a brand-new account in a short match) still moves
  the bracket if it names a winner.
- **If the result cannot be read** (nobody recorded, the game was closed before the end), the
  match **stays pending** and the room closes. Open it again with **Open my room** and play it
  again. If one of you did record, that reading can still decide it a few minutes later, even
  with the room closed.
- **Your next opponent is not notified when your next match becomes ready.** Keep an eye on the
  bracket. They are notified when you open the room.
- In your **History** the match carries the **TOURNAMENT** tag and the tournament's name and
  round.
- **The recording is uploaded**, like any competitive match: the player who reports it (the
  host) uploads it if they have **Launcher settings → Games → "Share my competitive match
  recordings"** on. It is kept for a year and anyone can download it from History.

### Notifications

They arrive as a desktop notification. They are not shown while you are in a game, and if you
were offline you will not get them later: open the Tournaments tab and the bracket will be up to
date.

| Notification | What happened |
|---|---|
| **"Your opponent opened the room"** | Your match is waiting for you. Open the bracket and press **Enter the room**. |
| **"You won your tournament match"** | You go through to the next round. |
| **"Your tournament match is decided"** | It ended and you did not win. |
| **"You are in"** | The organiser accepted you and you have a place. |
| **"Your match has to be replayed"** | The organiser asked you to play an undecided match again. |
| **"A place freed up and it is yours"** | Check your status in the list: today this message also arrives when you were accepted without a free place, and then you are still **Waiting**. |

**There is no notification** when the bracket is drawn, when your next opponent is decided, when
the organiser awards a match, when somebody is disqualified or when the tournament is cancelled.

### The rating (ELO) in tournaments

- **It counts like any rated competitive match, always at full value.** The anti-farm rule never
  applies, because it is the bracket that decides who plays whom.
- It counts towards everything a rated match counts towards: your rating, your placement, your
  streaks and the month's highlights.
- **A crash never voids a tournament match.**
- **A walkover or a disqualification moves nobody's rating**: nobody played.
- **If a tournament is cancelled or abandoned, the games already played keep their rating.**
- Details in [ELO.md](ELO.md#tournaments).

### Running a tournament

#### Who can, and how many

- **Any signed-in player.** Whoever creates a tournament owns it: they can do everything to it
  and nothing to anybody else's.
- **Two tournaments in play per person at most** (not published, registration open or closed, or
  in progress), and **ten on the whole server**. Finished, cancelled or abandoned ones do not
  count.
- At most 2 tournaments created per minute and 10 a day. If you do many actions in a row you may
  have to wait a few seconds.

#### Creating it

**MULTIPLAYER → Tournaments → New.**

| Field | What it decides |
|---|---|
| **Mod** | The game it is played on. Every room in the bracket uses it, and players must have it active. |
| **Name** | From 3 to 80 characters. The launcher proposes "*Mod* tournament". |
| **Format** | 1v1, 2v2 or 3v3. **Today, choose 1v1** (see [Team tournaments](#team-tournaments-2v2-and-3v3)). |
| **Who gets in** | **First come, first served** (whoever arrives past the last place waits) or **I accept each one** (nobody takes a place until you accept them). |
| **Places** | 2, 4, 8 or 16. The line below tells you how many rooms the first round needs and how many rounds there are to the final. |

Sixteen is the ceiling for a reason: a first round of sixteen is eight rooms at the same time, half
of everything the server allows.

#### Step by step

The tournament is born **not published**. The big button always shows the next step, and the line
under it explains it:

1. **Open registration.** The tournament becomes visible to everybody.
2. **Close registration.** Nobody else gets in.
3. **Seed.** Assigns the seeds by rating (see [How the seeds are decided](#how-the-seeds-are-decided)).
4. **Draw the bracket.** The tournament goes to **In progress** and the first round can be played.

Under **⋯ (More actions)** you have **Reopen registration** (while it is closed, if somebody
arrives late) and **Seed** again (if you want to redo the seeds before drawing).

The bracket cannot be drawn with fewer than two entrants who are In, or while any of them has no
seed. If you reopen registration and somebody new comes in, **seed again**.

#### Managing the entrants

- In **I accept each one** mode, the applications appear in the **APPLICATIONS** group with
  **Accept** and **Reject**.
- **A place can only be given while registration is open.** If you accept somebody when there are
  no places left, or after closing it, they stay **Waiting** — and whoever is Waiting does not
  play. **Accept before closing.**
- On a waiting-list row, the creator of the tournament has **Give a place**.
- If somebody with a place withdraws while registration is open, the first one waiting moves up by
  themselves.

#### Co-organisers

- From the **⋯** on an entrant's row: **Make co-organiser**. In the launcher you can only choose
  somebody who appears in the list of entrants.
- A co-organiser can do **everything you can, except two things**: cancel the tournament, and
  appoint or remove co-organisers.
- They appear in the **CO-ORGANISERS** list, and from there you can remove them.

#### During the tournament

- **Decide a match by hand (walkover).** Select the match in the bracket and open the **⋯** of the
  bar: **"*Name* wins"**. You are asked to confirm. It counts as a walkover (**W.O.**): no game
  is recorded and nobody's rating moves. **It cannot be undone from the launcher.** It can only
  be done when both sides are known and the match is not decided yet.
- **Have them play it again.** In the same **⋯**: **"Have them play it again"**. For a match that
  is still undecided — usually one whose game had no readable result. Both players are told, and
  if a room is still open on it, it is closed so they can open a new one. Nothing that was already
  decided changes.
- **Disqualify.** From the **⋯** on the entrant's row, only while the tournament is in progress.
  Every pending match of theirs whose opponent is already known is given to that opponent
  (**Disq.**), so one disqualification can settle several matches at once. If their next opponent
  is not known yet, that match is given to whoever arrives. If both sides of a match end up
  disqualified, you have to decide it by hand.
- **There are no deadlines.** Nothing is decided automatically: you set the pace. If you want
  deadlines, announce them (for example on Discord) and apply them with walkovers.

#### What only the project's administrators can do

- **Undo a match that was already decided** (played or awarded). If a result is wrong, ask the
  administrators on the project's Discord. Undoing it in the bracket does not undo its rating:
  that is a separate decision.
- **Transfer a tournament** to another owner.

#### Cancelling

- In the **DANGER ZONE** at the bottom of the tournament, only for its creator.
- **There is no confirmation step: one click cancels it, and it cannot be undone.**
- The rooms still open for its matches are closed. **Games already played keep their rating.**

#### When a tournament is archived

- **Not published for 7 days** after being created: it is archived.
- **30 days without any activity** (entries, acceptances, seeds, reported matches, walkovers,
  disqualifications…): it goes to **Abandoned**. Nobody is crowned and no rating changes; the
  rooms still open for it are closed when the server archives it.
- From the moment it qualifies, it disappears from the list and stops counting towards your limit
  of two.

#### Announcing it

**The launcher never announces a tournament**: not on Discord and not in the global chat, and
neither are its rooms. Share the tournament's name in the community yourself so that people go and
enter.

### Team tournaments (2v2 and 3v3)

> **Today they cannot be played from the launcher.** The server already supports them and the
> launcher lets you create them, but **it has no way yet to register a team**: there is no screen
> to create saved teams or to choose a line-up. So:
>
> - In **"A captain enters a saved team"** and **"A captain picks a line-up when entering"**,
>   pressing **Enter** is refused.
> - In **"Everyone enters alone and I make the teams"**, the entries go in, but the launcher has
>   no way to put them together into teams, and a bracket of individual players in a team format
>   cannot be played.
>
> **Do not create team tournaments for now.** This section describes how they will work.

- Each slot in the bracket holds **a whole team**, and its line-up is **frozen when it enters**:
  if a saved team changes its players the next day, the tournament still remembers who it
  accepted.
- There are three ways of forming teams: a captain enters a **saved team**; a captain **picks the
  line-up** when entering; or **everybody enters alone** and the organiser puts the teams together
  before seeding.
- A **saved team** has a captain and up to **6 players** (so it can have substitutes); the captain
  invites, and invitations wait even if the person is offline. One person can own up to 5 teams.
  The captain cannot leave the team: they can disband it.
- **The sides inside the game have to match the bracket.** In the room each team picks its side,
  and in AoE3 the players of a team have to play together on one side. If the sides do not match,
  the match does not count and the bracket does not move. The launcher warns about it above the
  bracket.
- For the ELO it is the **team ladder**, and a team match needs the **other side's
  confirmation** to count (see [ELO.md](ELO.md#teams)). Seeds come from the **average** of the
  team members' cautious team rating.
- **The walk-out rule does not apply to teams**: if a team leaves, the match stays undecided and
  the organiser awards it.

### Limits at a glance

| What | Limit |
|---|---|
| Places | 2, 4, 8 or 16 (entrants, not players) |
| Tournament name | 3 to 80 characters |
| Games per match | 1 |
| Live tournaments per person | 2 |
| Live tournaments on the server | 10 |
| Creating tournaments | 2 per minute, 10 a day |
| Tournament actions | 40 per minute, 400 a day |
| Unpublished tournament is archived after | 7 days |
| Tournament with no activity is archived after | 30 days |
| Walking out loses (1v1) | after 5 minutes of play |
| Recording kept | 1 year |

### FAQ

**Do tournament games count for ELO?**
Yes, at full value and with no anti-farm. A walkover or a disqualification moves nobody's rating.

**Is the recording uploaded?**
Yes. A tournament room is competitive, so the player who reports the match uploads it (unless they
turned off "Share my competitive match recordings") and anybody can download it from History.

**My opponent does not show up. What do I do?**
Tell the organiser. They can award you the match (W.O.). The server never decides it on its own.

**The game ended and the match is still pending.**
The result could not be read: nobody recorded, or the game was closed before writing the end. Open
the room again from the bracket and play it again. If your opponent did record, wait a few minutes:
their reading can still decide it.

**I cannot create a room: "You still have a tournament match room open".**
You have your tournament room open with the match undecided. Leave it first.

**I cannot enter a room: "That room belongs to a tournament match between two other players".**
Tournament rooms only admit the two players of that match.

**It tells me "Wrong mod".**
The tournament is played on another mod. Select it in the **Play** tab and open the room again.

**Can I watch a tournament match?**
Not live: the rooms have no spectator seats. When it ends you can download the recording from
History.

**I won and the bracket did not move.**
Check that the match has a result in your History. If it says it could not be read, play it again.
If the result is there and the bracket still does not move, tell the organiser: they can award it
by hand.

**A notification said "A place freed up and it is yours" but I am still Waiting.**
That message also arrives when the organiser accepted you without a free place left. What counts
is your status in the list. If registration is still open, you will move up when a place frees up.

**The result of a match is wrong. Can it be changed?**
Not from the launcher. Only the project's administrators can undo a match that was already decided.

**Can I play in my own tournament?**
Yes. Organisers and co-organisers can enter like anybody else.

**The tournament disappeared from the list.**
It was archived (7 days unpublished or 30 days with no activity), or it dropped out of the 50 with
the most recent activity.

### Technical details

Everything here comes from the lobby server's code (`src/tournaments/` and `src/teams/` in the
backend, and `src/elo/` for the rating), and from the launcher's own checks.

- **Seed strength**: `rating − 2 × RD` on the 1v1 ladder (`default` mode) for 1v1, and on the team
  ladder for 2v2 and 3v3, where a team is the **mean** of its members. A player with no rating
  counts as 1500 with RD 500, i.e. a strength of 500. Ties: earlier entry, then the entrant's id.
- **First-round order**: built by mirroring, so every first-round pair adds up to the bracket size
  plus one (`1+8`, `4+5`, `2+7`, `3+6`). That guarantees every first-round match has at least one
  real entrant (no double byes) and that seeds 1 and 2 meet at the earliest in the final.
- **Advancing**: the bracket moves when the stored match has a winner and a loser (results 1 and
  0) and every player maps onto one of the two registered line-ups. A result of 0.5 means "could
  not be read", never a draw: the match stays pending. It is decided at three moments: the host's
  report, a late reading by the other player (1v1), and the other side's confirmation (teams).
- **Disqualification cascade**: the winner who arrives at a match whose rival is already
  disqualified wins it too (Disq.), and that can repeat up to the final. A match with both sides
  disqualified needs a person.
- **Places** are claimed one by one with a conditional update, so two people can never take the
  last place at the same time. A place can only be claimed while registration is open.
- **Waiting list**: strictly by order of entry.
- **Archiving**: evaluated on reading, not with a timer. A stale tournament stops being listed and
  stops counting towards the limits at once; the server marks it **Abandoned** the next time it
  starts.
- **Rooms**: created with `competitive` requested, one seat per player (2, 4 or 6) and no
  spectator seats, and with `announce: false`. The server keeps them competitive as long as the mod is on the ladder
  (today, every mod).

---

## Español

### Lo esencial

- Un torneo es un **cuadro de eliminación directa**: si pierdes una vez, quedas fuera. Cualquier
  jugador con la sesión iniciada puede crear uno, en **MULTIJUGADOR → Torneos**.
- **Cada partida del cuadro es una sola partida** (al mejor de una).
- **Hasta 16 plazas.** Si quedan plazas vacías, las mejores semillas **pasan directamente** a la
  siguiente ronda.
- **Las semillas salen de tu puntuación de 1v1**, en su versión prudente: alguien con pocas
  partidas queda más abajo hasta que el sistema está seguro de su nivel.
- **Juegas tu partida en una sala competitiva normal** en la que solo pueden entrar tú y tu rival.
  La abres desde el cuadro. Marca **«Record Game»**, como en toda partida competitiva.
- **El cuadro avanza solo cuando la partida dice quién ganó.** Si no se puede leer el resultado,
  la partida queda pendiente y se puede volver a jugar.
- **Las partidas de torneo cuentan para el ELO enteras**: nunca se aplica el antifarmeo y un fallo
  del juego nunca las anula. Una incomparecencia (W.O.) o una descalificación no mueve la
  puntuación de nadie.
- **Nada se decide por tiempo**: no hay plazos ni derrotas automáticas. Si alguien no aparece,
  quien organiza decide la partida.
- **Hoy solo se pueden jugar torneos 1v1 desde el launcher.** Los de 2v2 y 3v3 se pueden crear,
  pero los equipos todavía no pueden inscribirse: mira
  [Torneos por equipos (2v2 y 3v3)](#torneos-por-equipos-2v2-y-3v3).

### Antes de jugar

- **Inicia sesión con Discord** en el launcher. Lo necesitas para inscribirte, para crear un
  torneo y para jugar tus partidas.
- **Ten instalado y activo el mod del torneo** (el mod elegido en la pestaña **Jugar**). El
  launcher lo comprueba antes de abrir tu sala y te frena con **«Mod incorrecto»** si es otro.
- **La misma versión del mod que tu rival.** Es la misma comprobación que en cualquier sala: si
  los archivos son distintos, no se puede entrar.
- **Radmin VPN**, como en cualquier partida multijugador.
- **«Record Game» marcado** en la pantalla de configuración del juego. Sin grabación nadie sabe
  quién ganó y la partida queda pendiente.

### Dónde están los torneos

**MULTIJUGADOR → Torneos.** A la izquierda, la lista; a la derecha, el torneo elegido con su cuadro
y su lista de participantes.

La lista muestra los 50 torneos con actividad más reciente, también los terminados. Un torneo que
todavía no se publicó solo lo ve quien lo creó. Cada tarjeta indica su estado, cuántas plazas hay
ocupadas («5 de 8 plazas») y, a quien lo creó, cuántas solicitudes esperan respuesta.

| Estado | Qué significa |
|---|---|
| **Sin publicar** | Recién creado. Solo lo ve quien lo creó. |
| **Inscripción abierta** | Te puedes inscribir. |
| **Inscripción cerrada** | Ya no entra nadie más. La organización está preparando las semillas y el cuadro. |
| **En curso** | El cuadro está generado y se están jugando las partidas. |
| **Terminado** | Tiene campeón. |
| **Cancelado** | Se canceló, por quien lo creó o por los administradores del proyecto. |
| **Abandonado** | Treinta días sin actividad. Se archivó: no se corona a nadie y no cambia ninguna puntuación. |

### Inscribirte

Presiona **Inscribirme** mientras la inscripción esté abierta. Cómo consigues plaza depende de lo
que eligió la organización:

- **Por orden de llegada.** Quedas **Dentro** si hay una plaza libre. Si no, pasas a la lista de
  espera (**En espera**), por orden de llegada.
- **La organización acepta uno por uno.** Apareces como **Solicitado** hasta que te acepte
  (**Dentro**, o **En espera** si ya no quedan plazas) o te rechace.

| Tu estado | Qué significa |
|---|---|
| **Dentro** | Tienes plaza. Vas a estar en el cuadro. |
| **En espera** | Estás en la lista de espera. Si se libera una plaza mientras la inscripción sigue abierta, la ocupa el primero de la lista. |
| **Solicitado** | La organización todavía no decidió. |
| **Se retiró** | Saliste del torneo. |
| **Rechazado** | La organización dijo que no. |
| **Descalificado** | La organización te sacó de un torneo en curso. |

Conviene saber:

- **Solo juegan quienes están Dentro cuando se genera el cuadro.** Quien siga En espera o
  Solicitado en ese momento queda fuera.
- Una plaza se libera para la lista de espera **solo mientras la inscripción está abierta**. Una
  vez cerrada, las plazas ya no se mueven solas.
- **Te puedes retirar** (**Retirarme**) hasta que se genere el cuadro, y volver a inscribirte si
  la inscripción sigue abierta. **Una vez generado el cuadro ya no te puedes retirar**: avisa a la
  organización.
- Tu nombre en el cuadro es el que tenías **al inscribirte**. Si lo cambias después, ahí no cambia.
- Quien organiza también se puede inscribir y jugar su propio torneo.

### Cómo se deciden las semillas

Antes de generar el cuadro, la organización presiona **Sembrar** y el launcher numera a los
participantes del más fuerte al más débil.

- La fuerza es tu **puntuación de 1v1 menos dos veces su incertidumbre**: la estimación prudente.
  Así, alguien con dos partidas afortunadas no pasa delante de un jugador asentado, y el pase
  directo se lo lleva quien se lo ganó.
- Si no tienes ninguna partida 1v1 puntuada, cuentas como un jugador desconocido (1500 con la
  incertidumbre máxima) y **quedas al final**.
- Un empate lo gana **quien se inscribió antes**.
- Es tu puntuación en **toda la tabla de 1v1**. La tabla no se separa por mod, así que no importa
  en qué mod jugaste tus partidas puntuadas.
- La organización puede volver a sembrar todas las veces que quiera antes de generar el cuadro.

### El cuadro

- **Su tamaño es la menor potencia de dos en la que entran todos**: 2, 4, 8 o 16. Eso da 1, 2, 3
  o 4 rondas.
- **Las plazas vacías son pases directos para las mejores semillas** (**PASA** en la tarjeta).
  Ejemplo: 6 participantes → un cuadro de 8 → las semillas 1 y 2 van directo a semifinales.
- **Orden de la primera ronda** (cuadro de 8): 1 contra 8, 4 contra 5, 2 contra 7, 3 contra 6.
  **Las semillas 1 y 2 solo se pueden cruzar en la final.**
- Las rondas se llaman **FINAL**, **SEMIFINALES**, **CUARTOS** y, antes, **RONDA 1**,
  **RONDA 2**…
- En cada tarjeta: **1** y **0** para una partida jugada; **W.O.** para una partida que la
  organización dio por incomparecencia; **Desc.** para una ganada porque el otro lado fue
  descalificado; **PASA** para un pase directo. Un hueco que todavía no se conoce dice «Ganador
  de …» hasta que termine esa partida. Tu lado lleva la etiqueta **TÚ**.

### Jugar tu partida

**Haz clic en tu partida en el cuadro.** Aparece una barra encima con los dos nombres, la ronda y
el botón que corresponda:

| Botón | Cuándo |
|---|---|
| **Abrir mi sala** | Los dos lados se conocen y nadie abrió la sala todavía. Crea la sala y tú eres el anfitrión. |
| **Entrar en la sala** | Tu rival ya la abrió. Entras tú. |
| **Volver a mi sala** | La abriste tú y sigue abierta. |
| *(sin botón)* | El otro lado todavía no se conoce, o la partida no es tuya. |

Cuando tu partida está lista, la cabecera del torneo y su tarjeta en la lista dicen **«Te toca
jugar»**.

Lo que tienes que saber de la sala:

- **Si los dos presionan «Abrir mi sala», terminan en la misma sala.**
- Es una **sala competitiva para dos**, con un título que escribe el servidor:
  `Nombre del torneo · R1 · Jugador A · Jugador B` (`Final` en la final).
- **Solo pueden entrar los dos jugadores de esa partida.** No hay espectadores ni contraseña. A
  cualquier otro se le dice *«Esa sala es de una partida de torneo entre otros dos jugadores.»*
- **No se anuncia** en el chat global ni en Discord.
- **Abrirla cierra cualquier otra sala que estuvieras hospedando.** Y mientras tu sala de torneo
  esté abierta y la partida sin decidir, **no puedes crear otra sala**: el servidor responde que
  todavía tienes abierta una sala de partida de torneo (*«You still have a tournament match room
  open»*). Sal de ella primero.
- Dentro **valen las reglas de siempre de una sala competitiva**: el launcher te recuerda
  «Record Game» antes de empezar, y el anfitrión se queda hasta que se envía el resultado.
- **Irse es perder.** En una sala competitiva 1v1, irte después de cinco minutos de partida y no
  volver cuenta como derrota (mira
  [ELO.md](ELO.md#abandonar-una-partida-competitiva)). Esa derrota también mueve el cuadro.
- **Un fallo del juego no anula una partida de torneo.** Fuera de los torneos la partida se puede
  anular si el juego del perdedor falla; aquí, nunca.

### Después de la partida

- **Si la grabación dice quién ganó, el cuadro avanza enseguida**: el ganador pasa a su siguiente
  partida y a los dos les llega una notificación.
- **El cuadro no depende del ELO.** Una partida que la clasificación no puntúa (una grabación ya
  reportada, tiempos que no cuadran, una cuenta recién creada en una partida corta) igual mueve el
  cuadro si dice quién ganó.
- **Si no se puede leer el resultado** (nadie grabó, el juego se cerró antes del final), la
  partida **queda pendiente** y la sala se cierra. Vuelve a abrirla con **Abrir mi sala** y
  juéguenla otra vez. Si uno de los dos sí grabó, esa lectura todavía la puede decidir unos
  minutos después, aunque la sala ya esté cerrada.
- **A tu próximo rival no le llega ningún aviso cuando su siguiente partida queda lista.** Sigue el
  cuadro. Le llega un aviso cuando tú abres la sala.
- En tu **Historial** la partida lleva la etiqueta **TORNEO** y el nombre y la ronda del torneo.
- **La grabación se sube**, como en cualquier partida competitiva: la sube el jugador que reporta
  la partida (el anfitrión) si tiene activado **Ajustes del launcher → Partidas → «Compartir mis
  grabaciones de partidas competitivas»**. Se guarda un año y cualquiera la puede descargar desde
  el Historial.

### Avisos

Llegan como notificación de escritorio. No se muestran mientras estás en una partida, y si estabas
sin conexión no te llegan después: abre la pestaña Torneos y el cuadro estará al día.

| Aviso | Qué pasó |
|---|---|
| **«Tu rival abrió la sala»** | Tu partida te espera. Abre el cuadro y presiona **Entrar en la sala**. |
| **«Ganaste tu partida de torneo»** | Pasas a la siguiente ronda. |
| **«Tu partida de torneo está decidida»** | Terminó y no ganaste. |
| **«Estás dentro»** | La organización te aceptó y tienes plaza. |
| **«Hay que repetir tu partida»** | La organización pidió volver a jugar una partida sin decidir. |
| **«Se liberó una plaza y es tuya»** | Revisa tu estado en la lista: hoy este mensaje también llega cuando te aceptaron sin plazas libres, y entonces sigues **En espera**. |

**No hay aviso** cuando se genera el cuadro, cuando se define tu próximo rival, cuando la
organización da una partida por incomparecencia, cuando se descalifica a alguien ni cuando se
cancela el torneo.

### La puntuación (ELO) en los torneos

- **Cuenta como cualquier partida competitiva puntuada, siempre entera.** El antifarmeo nunca se
  aplica, porque es el cuadro el que decide quién juega contra quién.
- Cuenta para todo lo que cuenta una partida puntuada: tu puntuación, tu posicionamiento, tus
  rachas y los destacados del mes.
- **Un fallo del juego nunca anula una partida de torneo.**
- **Una incomparecencia (W.O.) o una descalificación no mueve la puntuación de nadie**: nadie jugó.
- **Si un torneo se cancela o se abandona, las partidas ya jugadas conservan su puntuación.**
- Los detalles están en [ELO.md](ELO.md#torneos).

### Organizar un torneo

#### Quién puede, y cuántos

- **Cualquier jugador con la sesión iniciada.** Quien crea un torneo es su dueño: puede hacerle
  todo y no puede tocar el de nadie más.
- **Dos torneos en juego por persona como máximo** (sin publicar, con la inscripción abierta o
  cerrada, o en curso) y **diez en todo el servidor**. Los terminados, cancelados o abandonados no
  cuentan.
- Como mucho 2 torneos creados por minuto y 10 al día. Si haces muchas acciones seguidas puede que
  tengas que esperar unos segundos.

#### Crearlo

**MULTIJUGADOR → Torneos → Nuevo.**

| Campo | Qué decide |
|---|---|
| **Mod** | El juego en el que se juega. Todas las salas del cuadro lo usan, y los jugadores lo tienen que tener activo. |
| **Nombre** | De 3 a 80 caracteres. El launcher propone «Torneo de *mod*». |
| **Formato** | 1v1, 2v2 o 3v3. **Hoy, elige 1v1** (mira [Torneos por equipos](#torneos-por-equipos-2v2-y-3v3)). |
| **Quién entra** | **Por orden de llegada** (quien llega después de la última plaza queda en espera) o **Yo acepto uno por uno** (nadie ocupa plaza hasta que tú lo aceptes). |
| **Plazas** | 2, 4, 8 o 16. La línea de abajo te dice cuántas salas necesita la primera ronda y cuántas rondas hay hasta la final. |

Dieciséis es el tope por una razón: una primera ronda de dieciséis son ocho salas a la vez, la
mitad de todo lo que permite el servidor.

#### Paso a paso

El torneo nace **sin publicar**. El botón grande siempre muestra el paso siguiente, y la línea de
abajo lo explica:

1. **Abrir inscripción.** El torneo se hace visible para todos.
2. **Cerrar inscripción.** Ya no entra nadie más.
3. **Sembrar.** Reparte las semillas por puntuación (mira
   [Cómo se deciden las semillas](#cómo-se-deciden-las-semillas)).
4. **Generar el cuadro.** El torneo pasa a **En curso** y ya se puede jugar la primera ronda.

En **⋯ (Más acciones)** tienes **Reabrir inscripción** (con la inscripción cerrada, si alguien
llega tarde) y **Sembrar** otra vez (si quieres rehacer las semillas antes de generar el cuadro).

El cuadro no se puede generar con menos de dos participantes Dentro, ni mientras alguno no tenga
semilla. Si reabres la inscripción y entra alguien nuevo, **vuelve a sembrar**.

#### Gestionar a los participantes

- En el modo **Yo acepto uno por uno**, las solicitudes aparecen en el grupo **SOLICITUDES** con
  **Aceptar** y **Rechazar**.
- **Solo se puede dar plaza mientras la inscripción está abierta.** Si aceptas a alguien cuando ya
  no quedan plazas, o después de cerrarla, queda **En espera**, y quien está En espera no juega.
  **Acepta antes de cerrar.**
- En una fila de la lista de espera, quien creó el torneo tiene **Dar plaza**.
- Si alguien con plaza se retira mientras la inscripción está abierta, el primero de la lista de
  espera sube solo.

#### Co-organizadores

- Desde el **⋯** de la fila de un participante: **Hacer co-organizador**. En el launcher solo
  puedes elegir a alguien que aparezca en la lista de participantes.
- Un co-organizador puede hacer **todo lo que haces tú, salvo dos cosas**: cancelar el torneo, y
  nombrar o quitar co-organizadores.
- Aparecen en la lista **CO-ORGANIZADORES**, y desde ahí los puedes quitar.

#### Durante el torneo

- **Decidir una partida a mano (incomparecencia).** Selecciona la partida en el cuadro y abre el
  **⋯** de la barra: **«Gana *nombre*»**. Se te pide confirmación. Cuenta como incomparecencia
  (**W.O.**): no se registra ninguna partida y no se mueve la puntuación de nadie. **No se puede
  deshacer desde el launcher.** Solo se puede hacer cuando se conocen los dos lados y la partida
  todavía no está decidida.
- **Que la repitan.** En el mismo **⋯**: **«Que la repitan»**. Para una partida que sigue sin
  decidirse, normalmente una cuya partida no tuvo un resultado legible. Se avisa a los dos, y si
  quedaba una sala abierta para ella se cierra para que puedan abrir una nueva. No cambia nada de
  lo ya decidido.
- **Descalificar.** Desde el **⋯** de la fila del participante, solo con el torneo en curso. Cada
  partida suya pendiente cuyo rival ya se conoce se le da a ese rival (**Desc.**), así que una
  descalificación puede resolver varias partidas de golpe. Si todavía no se conoce su próximo
  rival, esa partida se le da a quien llegue. Si en una partida los dos lados quedan
  descalificados, la tienes que decidir tú a mano.
- **No hay plazos.** Nada se decide solo: el ritmo lo marcas tú. Si quieres plazos, anúncialos
  (por ejemplo en Discord) y aplícalos con incomparecencias.

#### Lo que solo pueden hacer los administradores del proyecto

- **Deshacer una partida ya decidida** (jugada o dada por incomparecencia). Si un resultado está
  mal, pídeselo a los administradores en el Discord del proyecto. Deshacerla en el cuadro no
  deshace su puntuación: eso es una decisión aparte.
- **Pasar un torneo** a otro dueño.

#### Cancelar

- En la **ZONA DE PELIGRO** al pie del torneo, solo para quien lo creó.
- **No hay paso de confirmación: un clic lo cancela, y no se puede deshacer.**
- Se cierran las salas que sigan abiertas para sus partidas. **Las partidas ya jugadas conservan
  su puntuación.**

#### Cuándo se archiva un torneo

- **7 días sin publicar** desde que se creó: se archiva.
- **30 días sin ninguna actividad** (inscripciones, aceptaciones, semillas, partidas reportadas,
  incomparecencias, descalificaciones…): pasa a **Abandonado**. No se corona a nadie y no cambia
  ninguna puntuación; las salas que sigan abiertas se cierran cuando el servidor lo archiva.
- Desde el momento en que cumple el plazo desaparece de la lista y deja de contar para tu límite
  de dos.

#### Anunciarlo

**El launcher nunca anuncia un torneo**: ni en Discord ni en el chat global, y sus salas tampoco.
Comparte tú el nombre del torneo en la comunidad para que la gente vaya a inscribirse.

### Torneos por equipos (2v2 y 3v3)

> **Hoy no se pueden jugar desde el launcher.** El servidor ya los soporta y el launcher te deja
> crearlos, pero **todavía no tiene forma de inscribir un equipo**: no hay pantalla para crear
> equipos guardados ni para elegir una alineación. Por eso:
>
> - En **«Un capitán inscribe un equipo guardado»** y **«El capitán arma la alineación al
>   inscribirse»**, presionar **Inscribirme** da error.
> - En **«Cada uno se apunta solo y yo hago los equipos»**, las inscripciones entran, pero el
>   launcher no tiene forma de juntarlas en equipos, y un cuadro de jugadores sueltos en un formato
>   de equipos no se puede jugar.
>
> **No crees torneos por equipos por ahora.** Esta sección explica cómo van a funcionar.

- Cada hueco del cuadro contiene **un equipo entero**, y su alineación **se congela al
  inscribirse**: si un equipo guardado cambia de jugadores al día siguiente, el torneo sigue
  recordando a quién aceptó.
- Hay tres formas de armar los equipos: un capitán inscribe un **equipo guardado**; un capitán
  **arma la alineación** al inscribirse; o **cada uno se apunta solo** y la organización forma los
  equipos antes de sembrar.
- Un **equipo guardado** tiene un capitán y hasta **6 jugadores** (para poder tener suplentes); el
  capitán invita, y las invitaciones esperan aunque la persona esté desconectada. Una persona
  puede ser dueña de hasta 5 equipos. El capitán no puede salir del equipo: lo puede disolver.
- **Los bandos dentro del juego tienen que coincidir con el cuadro.** En la sala cada equipo elige
  su bando, y en AoE3 los jugadores de un equipo tienen que ir juntos en un mismo bando. Si los
  bandos no coinciden, la partida no cuenta y el cuadro no avanza. El launcher lo avisa encima del
  cuadro.
- Para el ELO es la **tabla de equipos**, y una partida de equipos necesita la **confirmación del
  otro bando** para contar (mira [ELO.md](ELO.md#equipos)). Las semillas salen del **promedio** de
  la puntuación prudente de equipos de sus integrantes.
- **La regla de abandono no se aplica a los equipos**: si un equipo se va, la partida queda sin
  decidir y la organización la da por incomparecencia.

### Los límites, de un vistazo

| Qué | Límite |
|---|---|
| Plazas | 2, 4, 8 o 16 (participantes, no jugadores) |
| Nombre del torneo | De 3 a 80 caracteres |
| Partidas por cruce | 1 |
| Torneos en juego por persona | 2 |
| Torneos en juego en el servidor | 10 |
| Crear torneos | 2 por minuto, 10 al día |
| Acciones de torneo | 40 por minuto, 400 al día |
| Un torneo sin publicar se archiva a los | 7 días |
| Un torneo sin actividad se archiva a los | 30 días |
| Irse es perder (1v1) | después de 5 minutos de partida |
| La grabación se guarda | 1 año |

### Preguntas frecuentes

**¿Las partidas de torneo cuentan para el ELO?**
Sí, enteras y sin antifarmeo. Una incomparecencia o una descalificación no mueve la puntuación de
nadie.

**¿Se sube la grabación?**
Sí. Una sala de torneo es competitiva, así que la sube el jugador que reporta la partida (salvo que
haya desactivado «Compartir mis grabaciones de partidas competitivas») y cualquiera la puede
descargar desde el Historial.

**Mi rival no aparece. ¿Qué hago?**
Avisa a la organización. Te puede dar la partida por incomparecencia (W.O.). El servidor nunca la
decide solo.

**La partida terminó y sigue pendiente.**
No se pudo leer el resultado: nadie grabó, o el juego se cerró antes de escribir el final. Vuelve a
abrir la sala desde el cuadro y juéguenla otra vez. Si tu rival sí grabó, espera unos minutos: su
lectura todavía la puede decidir.

**No puedo crear una sala: «You still have a tournament match room open».**
Tienes abierta tu sala de torneo con la partida sin decidir. Sal de ella primero.

**No puedo entrar en una sala: «Esa sala es de una partida de torneo entre otros dos jugadores».**
Las salas de torneo solo admiten a los dos jugadores de esa partida.

**Me dice «Mod incorrecto».**
El torneo se juega en otro mod. Elígelo en la pestaña **Jugar** y vuelve a abrir la sala.

**¿Puedo ver una partida de torneo?**
En directo, no: las salas no tienen plazas de espectador. Cuando termine puedes descargar la
grabación desde el Historial.

**Gané y el cuadro no se movió.**
Revisa en tu Historial que la partida tenga resultado. Si dice que no se pudo leer, vuelvan a
jugarla. Si el resultado está y el cuadro sigue sin moverse, avisa a la organización: la puede dar
a mano.

**Me llegó «Se liberó una plaza y es tuya» pero sigo En espera.**
Ese mensaje también llega cuando la organización te aceptó sin plazas libres. Lo que cuenta es tu
estado en la lista. Si la inscripción sigue abierta, subirás cuando se libere una plaza.

**El resultado de una partida está mal. ¿Se puede cambiar?**
Desde el launcher, no. Solo los administradores del proyecto pueden deshacer una partida ya
decidida.

**¿Puedo jugar en mi propio torneo?**
Sí. Quien organiza y los co-organizadores se pueden inscribir como cualquier otro.

**El torneo desapareció de la lista.**
Se archivó (7 días sin publicar o 30 días sin actividad), o quedó fuera de los 50 con actividad más
reciente.

### Los detalles técnicos

Todo lo de aquí sale del código del servidor de salas (`src/tournaments/` y `src/teams/` en el
backend, y `src/elo/` para la puntuación) y de las comprobaciones del propio launcher.

- **Fuerza para las semillas**: `puntuación − 2 × RD` en la tabla de 1v1 (modo `default`) para
  1v1, y en la tabla de equipos para 2v2 y 3v3, donde un equipo es el **promedio** de sus
  integrantes. Un jugador sin puntuación cuenta como 1500 con RD 500, es decir, una fuerza de 500.
  Empates: la inscripción más antigua, y luego el identificador del participante.
- **Orden de la primera ronda**: se construye por reflejo, así que cada pareja de la primera ronda
  suma el tamaño del cuadro más uno (`1+8`, `4+5`, `2+7`, `3+6`). Eso garantiza que cada partida
  de la primera ronda tenga al menos un participante real (no hay pases dobles) y que las semillas
  1 y 2 se crucen como pronto en la final.
- **Avance**: el cuadro se mueve cuando la partida guardada tiene un ganador y un perdedor
  (resultados 1 y 0) y todos los jugadores encajan en una de las dos alineaciones inscritas. Un
  resultado de 0,5 significa «no se pudo leer», nunca un empate: la partida queda pendiente. Se
  decide en tres momentos: el reporte del anfitrión, una lectura tardía del otro jugador (1v1) y la
  confirmación del otro bando (equipos).
- **Descalificación en cascada**: el ganador que llega a una partida cuyo rival ya está
  descalificado la gana también (Desc.), y eso se puede repetir hasta la final. Una partida con los
  dos lados descalificados necesita a una persona.
- **Las plazas** se reclaman una a una con una actualización condicional, así que dos personas
  nunca pueden quedarse con la última plaza a la vez. Una plaza solo se puede reclamar mientras la
  inscripción está abierta.
- **Lista de espera**: estrictamente por orden de inscripción.
- **Archivado**: se evalúa al leer, no con un temporizador. Un torneo caducado deja de listarse y
  de contar para los límites al instante; el servidor lo marca como **Abandonado** la próxima vez
  que arranca.
- **Salas**: se crean pidiendo `competitive`, una plaza por jugador (2, 4 o 6) sin plazas de
  espectador, y con `announce: false`. El servidor las mantiene competitivas mientras el mod esté en la clasificación
  (hoy, todos los mods).
