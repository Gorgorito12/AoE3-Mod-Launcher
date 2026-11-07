# Sala de un cruce de torneo (LobbyWindow en modo torneo) · diseño 85a–85j

Referencia visual: `Sala de torneo v2 - referencia.html` (se abre en el navegador; 85a es interactivo y en Tweaks se cambian el tamaño, la transparencia, quién hospeda y si tu Radmin está listo). Sigue la base de diseño del launcher (Biblioteca unificada / Base de diseño).

## Qué hay que hacer

Es la misma `LobbyWindow`, no una ventana nueva. Si la sala tiene `tournament_match_id`, la ventana entra en modo torneo:

- Se ocultan Invitar, el código y su botón de copiar, la contraseña, las filas de hueco libre, el recuento «1 / 8» y el lápiz de renombrar. En un cruce solo entran sus jugadores (el servidor ya lo exige con `isEntrantMember`).
- La cabecera dice el torneo, quién juega contra quién, la ronda y el mod. Tras la partida, el título pasa a ser el resultado y aparece el bloque de ELO.
- El estado de la sala y el único botón principal van en el pie y cambian con cada paso.
- El chat, la conexión, el arranque, la cuenta atrás y el escalado (`UiScale`, ref 900×600) son los de siempre.

### Datos

- La sala solo trae `tournament_match_id`. La cabecera y «Qué está en juego» necesitan el `TournamentDetail` que ya carga la pestaña Torneos: nombre del torneo, `rounds_total`, la ronda del cruce, las cabezas de serie y el cruce siguiente (si su otro lado ya se decidió o es «quien gane A – B»). Reusa la caché de Torneos o pídelo al abrir la ventana.
- El ELO de quien está en posicionamiento no se muestra nunca: «En posicionamiento 4/10».
- El tiempo de espera sale de `created_at` de la sala.
- Para «puede darte la victoria», el nombre de quien organiza el torneo.

## Estructura y medidas

- Fondo `#0f1c2e`. Franja con la imagen del mod (hero) de 340 px desde el borde superior, detrás de la barra de título, fundida con `#0f1c2e`; debajo, el resplandor de su color al 20 %.
- Barra de título: 40 alto, color del mod mezclado al 22 % con `#0b1422`, uniforme. Sólida opaca · Media al 42 % + 10 px de difuminado · Máxima al 30 % + 10 px. Línea inferior `rgba(255,255,255,.06)`. Texto «Sala del cruce · {torneo}» con sombra 0 1 6 al 50 %. Icono del mod a 18 px.
- Cabecera (padding `clamp(16px,2.2cqw,30px) 28px 16px`):
  - Nombre del torneo en mayúsculas, 11/700, espaciado .06em, en el color del mod aclarado (contraste ≥ 5:1).
  - Título Source Serif 4 700, `clamp(26px,3.1cqw,40px)`: «Gorgo contra Cuzco» / «Ganaste contra Cuzco» / «Perdiste contra Cuzco».
  - Una sola fila con chip «Torneo · 1v1» (punto del color del mod), chip de versión (icono de etiqueta `#7ee08b`, nombre del mod en Segoe UI y número en Consolas) y las rondas «Dieciseisavos › Octavos › Cuartos › Semifinal › Final»: jugadas `#8C9CB1`, la del cruce en el color del mod 600, futuras `#61779a`.
  - A la derecha, «Ver cuadro» (pequeño, 32 alto, radio 7). Tras la partida se sustituye por el bloque de ELO: tarjeta de panel flotante (Sólida `rgba(12,22,38,.95)` + 18 px · Media `rgba(12,22,38,.74)` + 28 px · Máxima `rgba(12,22,38,.6)` + 32 px), radio 14, borde interior `rgba(255,255,255,.12)`; «TU ELO» 11/700; el número nuevo en Consolas `clamp(28px,3cqw,38px)`; la diferencia en un chip de 24 (verde `#7fe0a8` sobre `rgba(79,214,138,.14)` si sube; `#c3d2e5` sobre `rgba(255,255,255,.08)` si baja, nunca rojo); «Antes: 1383».
- Cuerpo (padding `0 28px 16px`, separación 12/16):
  - Menos de 1400 px de ancho: dos columnas, `clamp(300px,32cqw,420px)` y el chat. A la izquierda, «El cruce» arriba y debajo «Para que cuente» y «Qué está en juego»; lo que no quepa se desplaza.
  - Desde 1400 px: tres columnas, `clamp(320px,26cqw,440px)` · `clamp(280px,23cqw,400px)` · chat.
  - Paneles de cristal: Sólida `rgba(12,22,38,.86)` · Media `rgba(10,19,33,.5)` + 16 px · Máxima `rgba(10,19,33,.28)` + 8 px; radio 14; borde interior `rgba(255,255,255,.1)`. Etiquetas de sección 11/700 `#61779a`, espaciado .06em. Barra de desplazamiento en el color del mod al 55 %.
- **El cruce.** Filas de 32 px de avatar: nombre 14/600 `#f0f5fb`, etiquetas «tú · anfitrión · capitán» 12 `#8C9CB1`, estado a la derecha en la misma línea (punto de 7 px); debajo, cabeza de serie «#10» y ELO en Consolas. En 3v3, una cabecera por equipo: nombre, «#1» y «Bando 1». Nota al pie del panel (12 `#8C9CB1`).
- **Para que cuente.** Icono de información `#55ACEE`. Con «¿Dónde está?» (en el color del mod) se abre la ayuda de Record Game que ya existe (`OnRecordHelp`). Tras una partida que no cuenta, el borde pasa a `rgba(244,144,12,.6)`.
- **Qué está en juego / Resultado.** Líneas con punto de 7 px: la siguiente ronda en el color del mod, quedar fuera en `#61779a` y el ELO en `#55ACEE`.
- **Chat.** Cabecera «CHAT DE LA SALA» con «Limpiar» (pequeño); líneas del sistema 12.5 `#8C9CB1`; mensajes con nombre 13/600 y hora en Consolas 11 `#61779a`; campo de 34 y «Enviar» de 34, radio 8.
- **Pie.** Padding `14px 28px 16px`, fondo Sólida `rgba(12,22,38,.94)` · Media `rgba(10,19,33,.62)` · Máxima `rgba(10,19,33,.42)`, línea superior `rgba(255,255,255,.08)`. A la izquierda, el estado: punto de 8 px, título 15/600 y, si hace falta, reloj en Consolas 17; debajo, una línea de 13 `#a8bcd2` (dos como mucho). A la derecha, los botones juntos (separación 10), con el principal en el extremo.
- Botones: principal 52 alto, radio 10, 16/600, color del mod + sombra `0 6px 20px` de su color al 35 %, texto blanco u oscuro según contraste (uno solo por vista). Secundario 52, sólido `#1a2c48`, borde `rgba(130,175,255,.22)`. «Abandonar la partida»: secundario con borde `rgba(200,74,74,.55)` y texto `#f0a6a6`.
- Confirmaciones: velo `rgba(5,10,18,.72)`, tarjeta de 440 `#12213a`, radio 12; título en Source Serif 4 19; botones de 32 alto y mínimo 96 de ancho; destructivo `#c84a4a`.
- Aviso flotante (85j): 420 de ancho, abajo a la derecha, nivel de panel flotante, radio 12; icono del mod a 36; título en una línea y texto en dos como mucho; «Ver sala» de 32 en azul de acción global `#2f7fe0`. Va en neutros porque puede salir encima de cualquier pantalla.
- Tipografía: Segoe UI; títulos Source Serif 4 700; ELO, cabezas de serie, versiones, horas y relojes en Consolas.

## Estados (85a recorre los de 1v1)

| Estado | Pie: título · línea | Botones |
|---|---|---|
| Solo, esperando al rival (anfitrión) | «Esperando a {rival}» + tiempo desde `created_at` · «{rival} ve la sala en Torneos. Si no llega, {organizador}, que organiza el torneo, puede darte la victoria.» | Salir de la sala · **Estoy listo** |
| Rival dentro | «{rival} entró» / «Estás en la sala de {rival}» / «{rival} está listo» / «Estás listo · falta {rival}» · «La partida empieza sola cuando los dos estén listos.» | Salir de la sala · **Estoy listo** (o «Ya no estoy listo», secundario) |
| Los dos listos (anfitrión) | Confirmación «Antes de empezar» (el `ConfirmRecordGameAsync` de hoy) | Cancelar · **Empezar** |
| Cuenta atrás | «La partida empieza en» + número · anfitrión: «Cuando se abra AoE3, crea la partida en Multijugador → LAN y marca «Record Game».»; resto: «… únete a la partida de {rival} desde Multijugador → LAN.» | Cancelar |
| En partida | «En partida» + reloj · anfitrión: «Al terminar, tu launcher envía el resultado: no lo cierres hasta entonces.»; resto: «Al terminar, el launcher de {rival} envía el resultado.» | Abandonar la partida (con confirmación) |
| Ganaste | Cabecera «Ganaste contra {rival}» + ELO · pie «El cuadro ya avanzó» · «Juegas {ronda} contra {siguiente}.» | Salir de la sala · **Ver cuadro** |
| Perdiste | Cabecera «Perdiste contra {rival}» + ELO · pie «El cuadro ya avanzó» · «Puedes seguir el resto del torneo en Torneos.» | Salir de la sala · **Ver cuadro** |
| No cuenta (sin grabación) | «La partida no cuenta todavía» en `#F5BD76`, punto `#F4900C` · texto de `MpTournamentReportUndecided` | Salir de la sala · **Estoy listo** |
| 3v3 enviada, sin confirmar | «Resultado enviado · falta la confirmación», punto `#55ACEE` · texto de `MpTournamentReportAwaiting` | Salir de la sala · **Ver cuadro** |
| Tu Radmin no está listo (solo / rival dentro) | «Tu Radmin VPN no está listo» en `#F5BD76` · «Los demás no te ven en la red hasta que lo esté. Puedes marcarte listo igual, pero lo necesitas para jugar.» Tu fila va en gris («Esperando VPN») | Salir de la sala · Abrir el asistente · **Estoy listo** |

Estados de fila: Listo (punto `#4fd68a`, texto `#7fe0a8`) · No está listo (`#61779a`, `#a8bcd2`) · No ha entrado (punto y avatar huecos) · Esperando VPN (fila al 60 %) · En partida · Pasa · Eliminado.

## Reglas

- Radmin nunca bloquea: el aviso informa y lleva al asistente.
- Salir antes de empezar no pide confirmación: se puede volver a abrir la sala desde Torneos. Con la partida en marcha, sí (`NeedsLeaveConfirm` / `ConfirmLeave`).
- Uno solo principal por vista. «Ver cuadro» lleva a Torneos con el cuadro del torneo y el cruce a la vista.
- La tarjeta de resultado de un cruce no ofrece «Buscar otra»: hoy `OffersFindAnother` solo mira que la partida sea 1v1.
- El aviso flotante de 85j sale con `ShowAppToast` y `PreferDesktop`, como el «¡Rival encontrado!» de la partida rápida, cuando llega `member_joined` y la ventana de la sala no está al frente. «Ver sala»: `BringToForeground()` y activar la ventana de la sala.
- Probar a 900×600, 1180×760, 1440×900 y maximizada a 1920×1080 (añade el caso a `DialogXamlTests`).

## Textos nuevos (es-419 con tuteo / en)

| Uso | es | en |
|---|---|---|
| Barra de título | Sala del cruce · {0} | Match room · {0} |
| Título | {0} contra {1} | {0} vs {1} |
| Ganaste / Perdiste | Ganaste contra {0} · Perdiste contra {0} | You beat {0} · You lost to {0} |
| Chip | Torneo · {0} | Tournament · {0} |
| Ver cuadro | Ver cuadro | View bracket |
| Secciones | EL CRUCE · PARA QUE CUENTE · QUÉ ESTÁ EN JUEGO · RESULTADO · CHAT DE LA SALA | THE MATCH · FOR IT TO COUNT · WHAT'S AT STAKE · RESULT · ROOM CHAT |
| Nota 1v1 | Solo pueden entrar los dos jugadores del cruce: no hay plazas libres ni código que compartir. | Only the two players of this match can join: there are no free seats and no code to share. |
| Nota 3v3 | Los bandos y las alineaciones los fija el cuadro: si se juega con otros, la partida no cuenta. | The bracket sets the sides and line-ups: a game played with others does not count. |
| Para que cuente | Antes de empezar en AoE3, marca «Record Game». El resultado se lee de la grabación: sin ella, el cruce no avanza. | Before you start in AoE3, tick "Record Game". The result is read from the recording: without it, the match does not move on. |
| Enlace | ¿Dónde está? | Where is it? |
| En juego (rival pendiente) | Quien gane juega {0} contra quien gane {1} – {2}. | The winner plays the {0} against the winner of {1} – {2}. |
| En juego (rival conocido) | Quien gane juega {0} contra {1}. | The winner plays the {0} against {1}. |
| En juego (final) | Quien gane se lleva el torneo. / El equipo que gane se lleva el torneo. | The winner takes the tournament. / The team that wins takes the tournament. |
| Eliminación | Quien pierda queda eliminado. | The loser is out. |
| ELO | Cuenta para el ELO de los dos. / Cuenta para el ELO de los {0}. | It counts for both players' rating. / It counts for the rating of all {0}. |
| 3v3 confirmar | Para que el cuadro avance, un jugador del otro equipo confirma el resultado en su launcher. | For the bracket to move on, a player of the other team confirms the result in their launcher. |
| Resultado | Pasas a {0}. · {0} queda fuera del torneo. · Quedas fuera de {0}. · {0} pasa a {1}. | You go through to the {0}. · {0} is out of the tournament. · You are out of {0}. · {0} goes through to the {1}. |
| Esperando | Esperando a {0} | Waiting for {0} |
| Esperando (línea) | {0} ve la sala en Torneos. Si no llega, {1}, que organiza el torneo, puede darte la victoria. | {0} sees the room in Tournaments. If they don't show up, {1}, who runs the tournament, can give you the win. |
| Listo sin rival | Estás listo · falta que entre {0} | You're ready · waiting for {0} to join |
| Rival dentro | {0} entró · Estás en la sala de {0} · {0} está listo · Estás listo · falta {0} | {0} joined · You're in {0}'s room · {0} is ready · You're ready · waiting for {0} |
| Arranque | La partida empieza sola cuando los dos estén listos. / … cuando los {0} estén listos. | The game starts by itself when you're both ready. / … when all {0} are ready. |
| Cuenta atrás | La partida empieza en | The game starts in |
| Cuenta atrás (anfitrión) | Cuando se abra AoE3, crea la partida en Multijugador → LAN y marca «Record Game». | When AoE3 opens, create the game in Multiplayer → LAN and tick "Record Game". |
| Cuenta atrás (resto) | Cuando se abra AoE3, únete a la partida de {0} desde Multijugador → LAN. | When AoE3 opens, join {0}'s game from Multiplayer → LAN. |
| En partida | En partida | In game |
| En partida (anfitrión) | Al terminar, tu launcher envía el resultado: no lo cierres hasta entonces. | When it ends, your launcher sends the result: don't close it until then. |
| En partida (resto) | Al terminar, el launcher de {0} envía el resultado. | When it ends, {0}'s launcher sends the result. |
| Después | El cuadro ya avanzó · Juegas {0} contra {1}. · Puedes seguir el resto del torneo en Torneos. | The bracket has moved on · You play the {0} against {1}. · You can follow the rest of the tournament in Tournaments. |
| Bloque de ELO | TU ELO · Antes: {0} | YOUR RATING · Before: {0} |
| No cuenta (título) | La partida no cuenta todavía | The game doesn't count yet |
| Sin confirmar (título) | Resultado enviado · falta la confirmación | Result sent · waiting for confirmation |
| Radmin | Tu Radmin VPN no está listo · Los demás no te ven en la red hasta que lo esté. Puedes marcarte listo igual, pero lo necesitas para jugar. | Your Radmin VPN isn't ready · The others can't see you on the network until it is. You can still mark yourself ready, but you need it to play. |
| Botones | Estoy listo · Ya no estoy listo · Salir de la sala · Cancelar · Abandonar la partida | I'm ready · Not ready · Leave room · Cancel · Leave the game |
| Antes de empezar | Antes de empezar · Cuando se abra AoE3, crea la partida en Multijugador → LAN y marca «Record Game». El resultado del cruce se lee de la grabación: sin ella, no cuenta. · Cancelar · Empezar | Before you start · When AoE3 opens, create the game in Multiplayer → LAN and tick "Record Game". The match result is read from the recording: without it, it doesn't count. · Cancel · Start |
| Abandonar | ¿Abandonar la partida? · La partida del cruce está en marcha. Si sales después de los primeros 5 minutos, cuenta como abandono: pierdes el cruce y {0} pasa a {1}. · Seguir jugando · Abandonar | Leave the game? · The match game is under way. If you leave after the first 5 minutes, it counts as abandoning: you lose the match and {0} goes through to the {1}. · Keep playing · Leave |
| Estados de fila | Listo · No está listo · No ha entrado · Esperando VPN · En partida · Pasa · Eliminado | Ready · Not ready · Not here yet · Waiting for VPN · In game · Through · Out |
| Cabeza de serie (tooltip) | Cabeza de serie | Seed |
| Aviso flotante | {0} entró en la sala del cruce · {1} · {2}. Márcate como listo para empezar. | {0} joined your match room · {1} · {2}. Mark yourself ready to start. |
| Chat del sistema | Abriste la sala del cruce. {0} la ve en Torneos, en su cruce. · Entraste en la sala del cruce que abrió {0}. · {0} entró en la sala. · {0} está listo. · Los dos están listos. La partida empieza en 5 segundos. · Partida en marcha. | You opened the match room. {0} sees it in Tournaments, on their match. · You joined the match room {0} opened. · {0} joined the room. · {0} is ready. · You're both ready. The game starts in 5 seconds. · Game under way. |

Se reutilizan: `MpTournamentReportUndecided`, `MpTournamentReportAwaiting`, `QmRadminOpen` («Abrir el asistente»), `QmFoundView` («Ver sala»), `MpTeamsCaptain`, `MpTeamsYou`.

## Por confirmar en el código antes de implementar

1. Qué pasa al abandonar un cruce en marcha. El texto supone la regla de las partidas competitivas (abandono después de 5 minutos) y que el rival pasa de ronda.
2. Si, después de una partida que no cuenta, la sala sigue abierta para repetirla con «Estoy listo».
3. Cómo confirma el resultado un jugador del otro equipo en 3v3. El diseño solo muestra la espera; si la confirmación necesita una acción, falta diseñarla.

## Archivos

`LobbyWindow.xaml(.cs)`, la parte de sala de `Controls/MultiplayerTab.xaml.cs` (modo torneo, `OffersFindAnother`, aviso de `member_joined`), `Localization` (claves nuevas en un parcial nuevo, p. ej. `Strings.TournamentRoom.cs`, unido como los demás), `DialogXamlTests` (900×600) y `.claude/rules/multiplayer.md`.
