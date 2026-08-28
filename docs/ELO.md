# La puntuación (ELO) del multijugador · Multiplayer rating (ELO)

> **Enlace corto para compartir:** fija esta página en tu servidor de Discord y
> enlázala cuando alguien pregunte por qué su partida no sumó. · *Pin this page
> and link it whenever somebody asks why their match didn't count.*

---

## Español

### Lo esencial

- **Todos empiezan en 1500.** No es un número de adorno: es el punto de partida real
  desde el que se calcula tu primera partida.
- **Solo puntúan las partidas de Wars of Liberty, uno contra uno, y con grabación.**
  Cualquier otra cosa **se guarda igual en tu historial**, pero no mueve la puntuación
  de nadie.
- **Tienes que marcar «Record Game» en la pantalla de configuración de AoE3, cada
  partida.** Es lo único que hay que hacer a mano.
- **Cuando una partida no puntúa, el launcher te dice por qué** en vez de callarse.
- **No hay temporadas ni castigo por no jugar.** Si desapareces seis meses, vuelves con
  la misma puntuación que dejaste.

### Marca «Record Game». Cada partida.

Age of Empires III **vuelve a desmarcar esa casilla en cada partida**. Está comprobado:
no hay forma de dejarla puesta desde fuera, ni desde el perfil ni con argumentos de
arranque. Por eso el launcher te lo recuerda en la sala antes de empezar, y te avisa
después si la partida se jugó sin grabar.

Sin grabación nadie sabe quién ganó — ni el launcher, ni el servidor. **Si no graba
ninguno de los dos, el resultado se pierde para los dos.** Si graba solo uno, todavía se
puede salvar: mira [Si tu rival grabó y tú no](#si-tu-rival-grabó-y-tú-no).

El launcher deja la grabación activada en la configuración del mod y **borra solo las
grabaciones antiguas que él mismo generó**. Las que hayas renombrado no se tocan nunca.

### Cuántos puntos ganas o pierdes

No hay una cantidad fija. Lo que se mueve depende de tres cosas:

1. **Cuántas partidas llevas.** Cuantas menos, más se mueve.
2. **La diferencia de puntuación entre los dos.** Ganar lo que se esperaba paga poco;
   ganarle a alguien mejor paga mucho.
3. **Cuántas partidas lleva tu rival.** Ganarle a alguien de nivel todavía desconocido
   mueve menos.

Valores **orientativos** — el cálculo exacto lo hace el servidor:

| Situación | Ganas | Pierdes |
|---|---|---|
| Tus primeras partidas | ~ +160 a +175 | ~ −160 a −175 |
| Después de unas 5 partidas | ~ +26 | ~ −26 |
| Ya asentado, contra alguien de tu nivel | ~ +10 | ~ −10 |
| Muy asentado, con muchas partidas encima | ~ +3 | ~ −3 |
| Gran favorito (1700 contra 1300) | +2 | −19 |
| Claro desfavorecido (1300 contra 1700) | +19 | −2 |
| Asentado, contra un recién llegado | ~ +7 | ~ −7 |

Tres cosas que sorprenden y son correctas:

- **Tus primeras partidas mueven muchísimo, a propósito.** El sistema te está ubicando.
  Después de unas cuantas, los saltos caen a diez puntos o menos y ahí se quedan.
- **Ganarle al favorito paga unas diez veces más** que ganar lo que ya se esperaba de ti.
  Y perder contra quien debías ganar cuesta caro: +2 si ganas, −19 si pierdes.
- **No es de suma cero.** En la misma partida, uno puede sumar 7 puntos y el otro perder
  175. Cada jugador se mueve según lo seguro que esté el sistema de *su* nivel, no según
  lo que le pasó al otro.

### Por qué a veces solo se mueven 3 puntos

Porque el sistema no guarda solo tu puntuación: guarda también **cuánta confianza tiene
en ella**.

Un recién llegado no tiene ninguna confianza detrás, así que cada partida lo mueve
cientos de puntos hasta encontrar su sitio. Un jugador con cincuenta partidas ya está
ubicado, y una sola partida no debería cambiar eso — así que se mueve poco. Es la misma
regla en los dos casos, no un límite que aparezca luego.

Mientras esa confianza sea baja, el launcher llama a tu puntuación **provisional**. Deja
de serlo sola, jugando.

**No jugar no cambia nada.** No hay decaimiento por inactividad: si te vas un año y
vuelves, tienes la misma puntuación y los mismos saltos pequeños que dejaste.

### Cuando una partida no puntúa

El servidor decide si una partida cuenta y **dice el motivo**; el launcher te lo muestra
tal cual en la tarjeta del final. Estos son todos los casos:

| Lo que ves | Qué pasó | Qué hacer |
|---|---|---|
| «La partida no se grabó, así que no hay forma de saber quién ganó» | Nadie encontró una grabación de esa partida | Marcar «Record Game» antes de la próxima |
| «La grabación no dice quién ganó» · «La grabación de esta partida no se pudo leer» | Sí se grabó, pero el archivo se cortó o no nombra un ganador utilizable | Suele pasar si el juego se cerró de golpe. Nada que arreglar |
| «No se pudo leer el nombre de tu perfil de AoE3» | Sin nombre de perfil no hay forma de encontrarte entre los jugadores de la grabación | Abre AoE3 una vez y asegúrate de que tu perfil tiene nombre |
| «Solo las partidas uno contra uno cuentan para el ELO» | Fue una partida por equipos | Nada: una grabación nombra a un perdedor, y eso no dice quién ganó un 2v2 |
| «Este mod todavía no tiene clasificación» | Hoy solo Wars of Liberty puntúa | Nada. Los demás mods siguen guardando historial |
| «Los tiempos de esta partida no cuadran» | Duró menos de **3 minutos**, o los relojes no cuadran | Nada, salvo jugar partidas de verdad |
| «Alguien de este reporte no estaba en la sala cuando empezó la partida» | Alguien entró después de que empezara | Que estén todos en la sala antes de empezar |
| «Esta grabación ya se había reportado» | Esa misma partida ya se había contado | Nada: si fue real, ya está en tu historial |
| «Esta partida se reportó sin sala» | Llegó un reporte sin sala a la que asociarlo | Poco frecuente. Juega desde una sala del launcher |

En todos los casos, **la partida queda guardada en tu historial**. Lo único que no ocurre
es el cambio de puntuación.

### Si tu rival grabó y tú no

Al terminar una partida, **quien no es el anfitrión manda también su propia lectura de la
grabación**, automáticamente y sin que tengas que hacer nada.

Sirve para rescatar exactamente un caso: la partida se guardó **sin poder leer quién
ganó**, y el otro jugador sí tenía una grabación legible. Entonces esa lectura decide la
partida **después**, y la puntuación se aplica igual.

Hay un límite para que nadie se invente resultados: **reconocer tu propia derrota se
acepta siempre; que tu grabación te dé la victoria solo cuenta si el servidor ya tenía
guardada la huella de esa misma partida** y coincide con la tuya.

Esto **solo** rescata "no se pudo leer quién ganó". Una partida por equipos, un mod sin
clasificación o unos tiempos que no cuadran no se rescatan de ninguna forma.

### La clasificación

Está al final de la pestaña **Salas**, en la tira **Actividad de la comunidad**, junto a
**Últimas partidas** y **Horas punta**.

Para aparecer hacen falta dos cosas:

- **Al menos 3 partidas con un resultado decidido.**
- **Que tu puntuación haya dejado de ser provisional**, es decir unas cuantas partidas
  puntuadas encima.

No es un castigo: una tabla llena de gente que no ha jugado estaría toda empatada en 1500
y en un orden arbitrario. Se ordena por puntuación, muestra los diez primeros y se
actualiza cada minuto más o menos.

**Horas punta** cuenta **cuándo se abren salas**, no cuándo se juega, y lo muestra en tu
hora local sobre los últimos 30 días.

### Dónde ves tu puntuación

- En **tu cuenta**, arriba a la derecha.
- En la pestaña **Perfil**, con tus partidas puntuadas y tu porcentaje de victorias.
- En la **lista de jugadores** de una sala.
- En la **tabla de salas**, junto al anfitrión.
- En el **panel de jugadores conectados**.
- Al terminar una partida, en la **tarjeta de resultado**, con cuánto cambió.
- En cada fila del **Historial**, con el cambio de esa partida.

### Preguntas frecuentes

**¿Y los empates?**
No existen. Una partida cuyo resultado no se pudo leer **no es un empate** — por eso la
fila del historial no muestra nada en vez de decir "Empate". Un empate que nunca ocurrió
es peor que un hueco.

**En mi perfil pone "12 partidas puntuadas" pero "8V-6D de 14 decididas". ¿Por qué no
cuadran?**
Porque cuentan cosas distintas. **Puntuadas** son las que movieron tu puntuación.
**Decididas** son todas las partidas en las que se supo quién ganó, incluidas las de mods
sin clasificación y las que por algún motivo no puntuaron. Las decididas siempre son
iguales o más.

**¿Bajo de puntuación si dejo de jugar?**
No. Nada baja solo.

**¿Por qué se reinició mi puntuación en su momento?**
Se reinició una vez, a propósito. Antes las partidas cuyo resultado no se podía leer se
contaban como empate, y eso ensuciaba la puntuación de todo el mundo. Al arreglarlo se
partió de cero. **El historial de partidas no se borró.**

**¿Puedo reportar la misma partida dos veces para sumar el doble?**
No. El servidor guarda una huella del archivo de grabación y otra del identificador
interno de la partida; la segunda vez se detecta y no cuenta.

**¿Se sube mi grabación a algún sitio?**
No. La grabación se lee **en tu propia PC**; al servidor solo viajan el resultado y esas
huellas, que son números y no contienen el archivo.

---

## English

### The short version

- **Everybody starts at 1500.** Not a placeholder: it is the real starting point your
  first match is calculated from.
- **Only Wars of Liberty matches, one-on-one, with a recording, count.** Anything else is
  **still saved to your history**, it just doesn't move anyone's rating.
- **You have to tick "Record Game" on the AoE3 setup screen, every match.** It is the one
  thing you do by hand.
- **When a match doesn't count, the launcher tells you why** instead of staying quiet.
- **There are no seasons and no penalty for not playing.** Disappear for six months and
  you come back on the rating you left.

### Tick "Record Game". Every match.

Age of Empires III **unticks that box again every match**. This has been tested: there is
no way to make it stick from outside — not from the profile, not with launch arguments.
So the launcher reminds you in the room before you start, and tells you afterwards if the
game was played without recording.

With no recording nobody knows who won — not the launcher, not the server. **If neither
player records, the result is lost for both.** If only one of you did, it can still be
saved: see [If your opponent recorded and you didn't](#if-your-opponent-recorded-and-you-didnt).

The launcher keeps recording enabled in the mod's settings and **deletes only the old
recordings it created itself**. Any you renamed are never touched.

### How many points you win or lose

There is no fixed amount. What moves depends on three things:

1. **How many matches you have played.** The fewer, the bigger the swing.
2. **The rating gap between you.** Winning what was expected pays little; beating someone
   better pays a lot.
3. **How many matches your opponent has played.** Beating someone whose level is still
   unknown moves less.

**Indicative** values — the server does the exact maths:

| Situation | You win | You lose |
|---|---|---|
| Your first matches | ~ +160 to +175 | ~ −160 to −175 |
| After about 5 matches | ~ +26 | ~ −26 |
| Settled, against someone at your level | ~ +10 | ~ −10 |
| Very settled, many matches in | ~ +3 | ~ −3 |
| Heavy favourite (1700 vs 1300) | +2 | −19 |
| Clear underdog (1300 vs 1700) | +19 | −2 |
| Settled, against a newcomer | ~ +7 | ~ −7 |

Three things that surprise people and are correct:

- **Your first matches move enormously, on purpose.** The system is placing you. After a
  handful, the swings drop to ten points or less and stay there.
- **Beating the favourite pays about ten times more** than winning what was already
  expected of you. And losing to someone you should beat is expensive: +2 if you win,
  −19 if you lose.
- **It is not zero-sum.** In the same match one player can gain 7 points while the other
  loses 175. Each side moves by how sure the system is of *their* level, not by what
  happened to the other.

### Why it sometimes only moves 3 points

Because the system doesn't only store your rating — it also stores **how confident it is
in it**.

A newcomer has no confidence behind them, so every match moves them hundreds of points
until they land somewhere. A player with fifty matches is already placed, and one game
shouldn't change that — so they move very little. It is the same rule in both cases, not
a cap that kicks in later.

While that confidence is low, the launcher calls your rating **provisional**. It stops
being provisional on its own, by playing.

**Not playing changes nothing.** There is no inactivity decay: leave for a year and you
return on the same rating, with the same small swings.

### When a match doesn't count

The server decides whether a match counts and **says why**; the launcher shows you that
reason verbatim on the end-of-match card. These are all the cases:

| What you see | What happened | What to do |
|---|---|---|
| "The match was not recorded, so nobody can tell who won" | No recording of that match was found | Tick "Record Game" before the next one |
| "The recording does not say who won" · "The recording could not be read" | It did record, but the file was cut short or names no usable winner | Usually happens when the game was closed abruptly. Nothing to fix |
| "Your AoE3 profile name could not be read" | Without a profile name there is no way to find you among the players in the recording | Open AoE3 once and make sure your profile has a name |
| "Only one-on-one matches count towards the rating" | It was a team game | Nothing: a recording names one loser, which says nothing about who won a 2v2 |
| "This mod has no ladder yet" | Today only Wars of Liberty is rated | Nothing. Other mods still record history |
| "The times reported for this match don't add up" | It ran under **3 minutes**, or the clocks disagree | Nothing, other than playing real games |
| "Someone in this report was not in the room when the game started" | Somebody joined after it began | Have everyone in the room before starting |
| "This recording had already been reported" | That same match had already been counted | Nothing: if it was real, it is already in your history |
| "This match was reported without a room" | A report arrived with no room to tie it to | Uncommon. Play from a launcher room |

In every case, **the match is still saved to your history**. The only thing that doesn't
happen is the rating change.

### If your opponent recorded and you didn't

When a match ends, **whoever is not the host also sends their own reading of the
recording**, automatically, with nothing for you to do.

It rescues exactly one case: the match was stored **without a readable winner**, and the
other player did have a readable recording. That reading then decides the match
**afterwards**, and the rating applies as normal.

There is a limit so nobody can invent results: **you may concede your own defeat freely;
your recording only grants you a victory if the server already had a fingerprint of that
same match stored** and it matches yours.

This **only** rescues "who won could not be read". A team game, an unranked mod or times
that don't add up cannot be rescued at all.

### The ranking

It lives at the bottom of the **Rooms** tab, in the **Community activity** strip, next to
**Recent matches** and **Peak hours**.

Two things are needed to appear:

- **At least 3 matches with a decided result.**
- **A rating that is no longer provisional** — a handful of rated matches behind it.

It isn't a punishment: a table full of people who never played would all be tied on 1500,
in an arbitrary order. It is sorted by rating, shows the top ten, and refreshes roughly
every minute.

**Peak hours** counts **when rooms are opened**, not when games are played, and shows it
in your local time over the last 30 days.

### Where you see your rating

- On **your account**, top right.
- On the **Profile** tab, with your rated matches and win rate.
- In a room's **player list**.
- In the **rooms table**, next to the host.
- In the **online players** panel.
- On the **result card** when a match ends, with how much it changed.
- On every **History** row, with that match's change.

### FAQ

**What about draws?**
There are none. A match whose result couldn't be read **is not a draw** — which is why the
history row shows nothing instead of saying "Draw". A draw that never happened is worse
than a gap.

**My profile says "12 rated matches" but "8W-6L of 14 decided". Why don't they match?**
They count different things. **Rated** are the ones that moved your rating. **Decided**
are every match where a winner was known, including those on unranked mods and ones that
didn't count for some other reason. Decided is always equal or higher.

**Do I lose rating for not playing?**
No. Nothing drops on its own.

**Why was my rating reset back then?**
It was reset once, deliberately. Matches whose result couldn't be read used to be counted
as draws, which polluted everybody's rating. Fixing that meant starting over. **Match
history was not deleted.**

**Can I report the same match twice to double up?**
No. The server stores a fingerprint of the recording file and another of the match's
internal id; the second attempt is detected and doesn't count.

**Is my recording uploaded anywhere?**
No. The recording is read **on your own PC**; only the result and those fingerprints — 
numbers, not the file — travel to the server.

---

*Ver también · See also: **[INSTALL.md](../WarsOfLibertyLauncher/INSTALL.md)** ·
[IS-IT-A-VIRUS.md](IS-IT-A-VIRUS.md) · [PRIVACY.md](../PRIVACY.md)*
