# La puntuación (ELO) del multijugador · Multiplayer rating (ELO)

> **Enlace corto para compartir:** fija esta página en tu servidor de Discord y
> enlázala cuando alguien pregunte por qué su partida no sumó. · *Pin this page
> and link it whenever somebody asks why their match didn't count.*

---

## Español

### Lo esencial

- **Todos empiezan en 1500.** No es un número de adorno: es el punto de partida real
  desde el que se calcula tu primera partida.
- **Solo puntúan las partidas de una sala competitiva, de Wars of Liberty, uno contra uno
  y con grabación.** Al crear una sala hay una casilla, **Sala competitiva**: si no la
  marcas, la partida se juega igual y **queda en tu historial**, pero no mueve la
  puntuación de nadie. Es a propósito — así una partida de prueba no te cuesta puntos.
- **En una sala competitiva, abandonar cuenta como derrota** pasados los primeros
  5 minutos. Ver [Abandonar una partida competitiva](#abandonar-una-partida-competitiva).
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

**Ese aviso es un recordatorio, no una garantía.** La casilla está dentro del juego: el
launcher no puede marcarla por ti ni comprobar si la marcaste. Lo que sí hace es
**acordarse**: si tu última partida competitiva se quedó sin grabación, el aviso de la
siguiente empieza diciéndotelo, en vez de repetirte el mismo texto.

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

### Abandonar una partida competitiva

Pasados los primeros **5 minutos**, si te vas de una partida competitiva y no vuelves,
cuenta como **derrota** y tu rival se lleva la victoria. Es la regla de siempre en cualquier
sistema de clasificación, y la casilla te lo advierte antes de que crees la sala.

- **Una desconexión cuenta igual.** Desde fuera no hay forma de distinguir un corte de luz
  de alguien que cierra el juego para no perder puntos, y fingir que sí la hay sería
  mentirte.
- **Solo si el otro se queda.** Si se caen los dos —lo típico cuando se corta la conexión
  del anfitrión y se lleva la sala por delante— no gana nadie: queda sin resultado.
- **Una grabación que diga quién ganó siempre manda.** El abandono solo decide las partidas
  que se habrían quedado sin resultado, nunca cambia una que ya lo tenía.
- **Hace falta que la partida se haya grabado.** Sin una grabación de por medio el abandono
  no decide nada, y tampoco puede repetirse una y otra vez entre los mismos dos jugadores:
  las dos cosas están para que nadie se invente partidas y farmee puntos.
- **Antes de los 5 minutos no pasa nada.** Ahí lo normal es que la partida empezara mal
  —mapa equivocado, opciones mal puestas— y no que alguien esté huyendo.

Si crees que una se decidió mal, escribe por Discord: se puede revisar y deshacer.

### Cuando una partida no puntúa

El servidor decide si una partida cuenta y **dice el motivo**; el launcher te lo muestra
tal cual en la tarjeta del final. Estos son todos los casos:

| Lo que ves | Qué pasó | Qué hacer |
|---|---|---|
| «Esta partida **SÍ se grabó**, pero el juego se cerró antes de terminar de escribir el final» | La grabación existe y es la correcta, pero le falta el desenlace | **Sal de la partida hasta el menú principal antes de cerrar AoE3.** Es el arreglo más útil de esta lista |
| «Se encontraron grabaciones, pero **en ninguna apareces** entre los jugadores» | El nombre de tu perfil de AoE3 no coincide con el que usas al jugar | Verifica el nombre de tu perfil. Mientras no cuadre, **falla en todas tus partidas** |
| «La partida terminó con tu AoE3 **todavía abierto**» | No es un fallo: tu grabación aún no se ha leído | Cierra el juego. El launcher la lee y **la partida todavía puede contar** |
| «La partida no se grabó, así que no hay forma de saber quién ganó» | No apareció ninguna grabación de esa partida | Marcar «Record Game» antes de la próxima |
| «La grabación no dice quién ganó» | Se leyó, pero no nombra un ganador utilizable | Nada que arreglar |
| «Solo las partidas uno contra uno cuentan para el ELO» | Fue una partida por equipos | Nada: una grabación nombra a un perdedor, y eso no dice quién ganó un 2v2 |
| «Esta sala no era competitiva» | La sala se creó sin marcar **Sala competitiva** | Marca la casilla al crear la sala. La partida queda en tu historial igual |
| «Este mod todavía no tiene clasificación» | Hoy solo Wars of Liberty puntúa | Nada. Los demás mods siguen guardando historial |
| «Los tiempos de esta partida no cuadran» | Duró menos de **3 minutos**, o los relojes no cuadran | Nada, salvo jugar partidas de verdad |
| «Alguien de este reporte no estaba en la sala cuando empezó la partida» | Alguien entró después de que empezara | Que estén todos en la sala antes de empezar |
| «Esta grabación ya se había reportado» | Esa misma partida ya se había contado | Nada: si fue real, ya está en tu historial |
| «Esta partida se reportó sin sala» | Llegó un reporte sin sala a la que asociarlo | Poco frecuente. Juega desde una sala del launcher |

En todos los casos, **la partida queda guardada en tu historial**. Lo único que no ocurre
es el cambio de puntuación.

### Una partida puede puntuar más tarde

El resultado se guarda **al instante**, sin esperar a la grabación; la lectura del archivo
sigue por detrás. Así que una partida que aparece «sin resultado» **no está cerrada**: si la
lectura llega después — la tuya al cerrar AoE3, o la de tu rival — la partida se puntúa
igual, aunque la sala ya no exista.

Cuando eso pasa te llega una notificación: **«Se puntuó una partida tuya»**. No hay que
repetir nada ni reclamar nada.

### Si tu rival grabó y tú no

Al terminar una partida, **quien no es el anfitrión manda también su propia lectura de la
grabación**, automáticamente y sin que tengas que hacer nada.

Sirve para rescatar exactamente un caso: la partida se guardó **sin poder leer quién
ganó**, y el otro jugador sí tenía una grabación legible. Entonces esa lectura decide la
partida **después**, y la puntuación se aplica igual.

Hay un límite para que nadie se invente resultados: **reconocer tu propia derrota se
acepta siempre; que tu grabación te dé la victoria solo cuenta si el servidor ya tenía
guardada la huella de esa misma partida** y coincide con la tuya.

Esto **solo** rescata "no se pudo leer quién ganó", y **nunca cambia una partida que ya
tenía resultado**. Una partida por equipos, un mod sin clasificación o unos tiempos que no
cuadran no se rescatan de ninguna forma.

No es teórico: así se recuperaron las partidas que se habían perdido por el fallo que
arregló la v1.0.12e.

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

**Jugué una partida entera y no me sumó nada. ¿Por qué?**
Lo más probable es que la sala no fuera competitiva. Desde la v1.0.13 solo puntúan las
partidas de una sala creada con la casilla **Sala competitiva** marcada; el resto se juega
igual y queda en tu historial, pero no mueve puntos. La tarjeta del final te dice cuál de
los motivos fue.

**Se me cortó internet a mitad de una partida competitiva y perdí puntos. ¿Es normal?**
Sí, y es a propósito. Pasados los primeros 5 minutos, irte de una partida competitiva
cuenta como derrota — y desde fuera no hay manera de distinguir un corte real de alguien
que cierra el juego para no perder. Es la regla en cualquier sistema de clasificación. Si
crees que se decidió mal, escribe por Discord: se puede revisar y deshacer.

**Mi juego me pide permisos de administrador y no se me reporta ninguna partida.**
Era un fallo, arreglado en la v1.0.13. Windows le pone a veces un modo de compatibilidad a
`age3y.exe` por su cuenta, y eso hacía que el launcher no se enterara de que habías cerrado
el juego: no se leía la grabación, no se reportaba nada y, si eras el anfitrión, **ninguna
de tus partidas llegaba a existir**. Ahora el launcher te ofrece quitar ese modo al terminar
la partida. A mano: clic derecho en `age3y.exe` → Propiedades → Compatibilidad, y desmarca
lo que esté marcado.

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

**Gané varias partidas como anfitrión y no me contaron. ¿Se perdieron?**
No. Hasta la v1.0.12e había un fallo por el que una partida solo contaba si el anfitrión
**perdía** — afectaba a cerca de la mitad de los 1v1. Está arreglado, y **esas partidas ya
volvieron**: se recuperaron desde la lectura del rival y están en tu historial con los
puntos aplicados.

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
- **Only matches in a competitive room, on Wars of Liberty, one-on-one, with a recording,
  count.** Creating a room has a **Competitive room** checkbox: leave it unticked and the
  match plays exactly the same and **stays in your history**, it just doesn't move anyone's
  rating. That is deliberate — a practice game shouldn't cost you points.
- **In a competitive room, walking out counts as a loss** after the first five minutes. See
  [Walking out of a competitive match](#walking-out-of-a-competitive-match).
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

**That reminder is a nudge, not a guarantee.** The box lives inside the game: the launcher
can neither tick it for you nor see whether you did. What it can do is **remember**: if your
last competitive match ended with no recording, the next reminder leads with that instead of
repeating the same text.

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

### Walking out of a competitive match

After the first **five minutes**, leaving a competitive match and not coming back counts as
a **loss**, and your opponent is credited with the win. It is the standard rule in any
rating system, and the checkbox warns you before you create the room.

- **A disconnection counts the same.** From the outside there is no way to tell a power cut
  from somebody closing the game to dodge a loss, and pretending otherwise would be lying to
  you.
- **Only if the other player stayed.** If you both drop — typically when the host's
  connection dies and takes the room with it — nobody wins: it ends with no result.
- **A recording that names a winner always wins.** Walking out only decides matches that
  would have ended with no result; it never changes one that already had it.
- **The match has to have been recorded.** With no recording involved, walking out decides
  nothing — and it cannot happen over and over between the same two players either. Both
  exist so nobody can invent matches and farm points.
- **Before the five minutes, nothing happens.** That early, the usual reason is a match that
  started wrong — the wrong map, the wrong settings — not somebody running away.

If you think one was decided wrongly, say so on Discord: it can be reviewed and undone.

### When a match doesn't count

The server decides whether a match counts and **says why**; the launcher shows you that
reason verbatim on the end-of-match card. These are all the cases:

| What you see | What happened | What to do |
|---|---|---|
| "This match **WAS** recorded, but the game closed before it finished writing the ending" | The recording exists and is the right one, but it has no ending | **Leave the match to the main menu before closing AoE3.** The most useful fix on this list |
| "Recordings were found, but **none of them has you** among its players" | Your AoE3 profile name isn't the one you actually play under | Check your profile name. Until it matches, **every match of yours fails** |
| "The match ended while your AoE3 was **still open**" | Not a failure: your recording hasn't been read yet | Close the game. The launcher reads it and **the match can still count** |
| "The match was not recorded, so nobody can tell who won" | No recording of that match turned up | Tick "Record Game" before the next one |
| "The recording does not say who won" | It was read, but names no usable winner | Nothing to fix |
| "Only one-on-one matches count towards the rating" | It was a team game | Nothing: a recording names one loser, which says nothing about who won a 2v2 |
| "This room wasn't a competitive one" | The room was created without ticking **Competitive room** | Tick the box when you create the room. The match stays in your history either way |
| "This mod has no ladder yet" | Today only Wars of Liberty is rated | Nothing. Other mods still record history |
| "The times reported for this match don't add up" | It ran under **3 minutes**, or the clocks disagree | Nothing, other than playing real games |
| "Someone in this report was not in the room when the game started" | Somebody joined after it began | Have everyone in the room before starting |
| "This recording had already been reported" | That same match had already been counted | Nothing: if it was real, it is already in your history |
| "This match was reported without a room" | A report arrived with no room to tie it to | Uncommon. Play from a launcher room |

In every case, **the match is still saved to your history**. The only thing that doesn't
happen is the rating change.

### A match can be rated later

The result is saved **immediately**, without waiting for the recording; reading the file
carries on in the background. So a match showing "no result" **is not closed**: if a reading
turns up afterwards — yours when you close AoE3, or your opponent's — the match is rated all
the same, even once the room is gone.

When that happens you get a notification: **"A match of yours was rated"**. Nothing to replay,
nothing to claim.

### If your opponent recorded and you didn't

When a match ends, **whoever is not the host also sends their own reading of the
recording**, automatically, with nothing for you to do.

It rescues exactly one case: the match was stored **without a readable winner**, and the
other player did have a readable recording. That reading then decides the match
**afterwards**, and the rating applies as normal.

There is a limit so nobody can invent results: **you may concede your own defeat freely;
your recording only grants you a victory if the server already had a fingerprint of that
same match stored** and it matches yours.

This **only** rescues "who won could not be read", and it **never overturns a match that
already had a result**. A team game, an unranked mod or times that don't add up cannot be
rescued at all.

Not hypothetical: it is how the matches lost to the bug fixed in v1.0.12e were recovered.

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

**I played a whole match and got nothing. Why?**
Most likely the room wasn't competitive. Since v1.0.13 only matches in a room created with
the **Competitive room** box ticked are rated; everything else plays the same and stays in
your history, but moves no points. The end-of-match card tells you which reason applied.

**My internet dropped mid-match in a competitive room and I lost points. Is that right?**
Yes, and it is deliberate. After the first five minutes, leaving a competitive match counts
as a loss — and from the outside there is no way to tell a real outage from somebody closing
the game to avoid losing. It is the rule in any rating system. If you think one was decided
wrongly, say so on Discord: it can be reviewed and undone.

**My game asks for administrator permission and none of my matches get reported.**
That was a bug, fixed in v1.0.13. Windows sometimes applies a compatibility mode to
`age3y.exe` on its own, and that stopped the launcher noticing you had closed the game: the
recording was never read, nothing was reported and, if you were the host, **none of your
matches ever existed**. The launcher now offers to remove that mode when the match ends. By
hand: right-click `age3y.exe` → Properties → Compatibility, and untick whatever is ticked.

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

**I won several matches as host and they didn't count. Are they lost?**
No. Until v1.0.12e there was a bug where a match only counted if the host **lost** — it
affected around half of all 1v1s. It is fixed, and **those matches are back**: they were
recovered from the opponent's reading and are in your history with the points applied.

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
