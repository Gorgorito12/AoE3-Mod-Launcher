# Clasificación ELO (v3)

Este documento **sustituye** a las versiones 54 y 54 v2. Parte de `design_handoff_ranking_historial_perfil` (3a-3c) y de las insignias por edades (`handoff_insignias_rango`, `handoff_insignia_equipos`).

Repo: `Gorgorito12/AoE3-Mod-Launcher`, rama `main`, proyecto WPF `WarsOfLibertyLauncher/`.

| Archivo | Qué contiene |
| --- | --- |
| `Clasificacion ELO v3.dc.html` | Prototipo, turno 55 (55a-55n). La sala de equipos (55h) es interactiva. Ábrelo en un navegador con `support.js` y `logo_wol/` al lado. |

El HTML es **referencia de diseño**: recréalo en WPF con los estilos y controles que ya existen. Todo el texto va en `Localization/Strings.cs`, en español (es-419, con tuteo) y en inglés. Los textos exactos están en el §10.

## 1. Reglas que refleja el diseño

- **Dos clasificaciones separadas: 1v1 y Equipos** (2v2 y 3v3 juntos). Cada una tiene su propio ELO, máximo, racha, posicionamiento e historial cara a cara.
- **La tabla se ordena por ELO**, de mayor a menor.
- **Posicionamiento:** 10 partidas puntuadas en 1v1 y 5 en Equipos. Mientras dura, el jugador va al final de la misma tabla, sin puesto ni insignia, con la fila apagada y su progreso.
- **Racha 🔥:** victorias seguidas. Se muestra desde 3. Se corta al perder o tras 14 días sin jugar una partida puntuada. La racha más larga queda guardada.
- **ELO provisional:** mientras dura el posicionamiento, el ELO lleva «?» (`1580?`) en la tabla, el perfil y la sala. El «?» va en `#e6b455`, con la misma fuente que la cifra. Tooltip: «ELO provisional: se ajusta durante el posicionamiento.»
- **Inactividad:** quien lleva 30 días sin jugar una partida puntuada en un modo **sigue en esa tabla con su puesto** y lleva la etiqueta INACTIVO. La etiqueta desaparece con su próxima partida puntuada.
- **Cuenta nueva:** algunas partidas de cuentas nuevas no puntúan (es una regla del servidor). El launcher solo muestra el motivo, nunca la duración.
- **Devolución de puntos:** si se sanciona por trampas a un rival contra el que perdiste, el servidor te devuelve los puntos y te avisa.
- **Antifarmeo:** cuando un jugador le gana varias veces seguidas al mismo rival, la partida vale menos para los dos:
  - de la 1.ª a la 2.ª victoria seguida: 100 %;
  - de la 3.ª a la 9.ª: 90 %, 80 %, 70 %, 60 %, 50 %, 40 % y 30 %;
  - desde la 10.ª: 20 % (es el mínimo).
  
  Vuelve al 100 % si el rival gana una, y recupera un 10 % por cada día sin jugar entre ellos. **No se aplica en torneos.**
- **Equipos en la sala:** en las salas 2v2 y 3v3, cada jugador elige Equipo 1 o Equipo 2, y el anfitrión puede mover a cualquiera. En una sala competitiva no se puede empezar hasta que los equipos estén completos y parejos. Si dentro del juego no eligen los mismos equipos, la partida no cuenta.
- **No inventar datos:** no hay duración de partida ni estadísticas internas del juego. Si falta un dato, esa parte no se muestra.

## 2. Tokens

| Uso | Valor |
| --- | --- |
| Fondo de pestaña | `#0f1c2e` |
| Tarjeta / tabla | `#12213a`, borde interior `rgba(130,175,255,.11)`, radio 9-10 |
| Fila o celda elevada | `#16263e`, radio 8-9 |
| Tarjeta que no contó | `#101d31` con franja `#4a5a72` |
| Cabecera de perfil | degradado `#1b2e4c` → `#16263e`, borde `rgba(130,175,255,.15)`, radio 10 |
| Separador de fila | `inset 0 -1px 0 rgba(130,175,255,.06)`; cabecera de tabla `.10` |
| Etiqueta de sección | 9.5-10.5 px, 600, espaciado .6 px, `#61779a` |
| Texto | primario `#e8eef6` / `#f0f5fb` · secundario `#c3d2e5` / `#9fb3cd` · atenuado `#8ea4c0` / `#6d829d` |
| Tu fila | fondo `rgba(47,127,224,.12)` + «TÚ» en 10/600 `#8cbcf5` |
| Equipo 1 | `#2f7fe0` (franja, botón activo, barra); título `#8cbcf5` |
| Equipo 2 | `#c0505a` (franja, botón activo, barra); título `#e6a0a6` |
| Victoria | `#4fd68a`; texto `#8fe0b0` / `#7fdca6` |
| Derrota | `#c8686e`; texto `#d99a9a` |
| Aviso | fondo `rgba(230,180,85,.08)`, borde `rgba(230,180,85,.30)`, icono `#e6b455`, título `#f0dcae`, texto `#d8bd8a` |
| Devolución | fondo `rgba(79,214,138,.08)`, borde `rgba(79,214,138,.30)`, icono `#4fd68a` sobre `rgba(79,214,138,.14)`, texto `#c8f0d8` |
| Etiqueta INACTIVO | fondo `rgba(130,175,255,.10)`, texto `#9fb3cd`, 9.5/600, espaciado .5, radio 4 · fila inactiva: nombre y ELO `#b9c9de`, barra `#4a6a96` |
| Porcentaje de antifarmeo | `#e6b455`, Consolas 600 |
| Racha | pastilla `rgba(255,140,60,.14)`, texto `#ffb27a`, Consolas 11/600, padding 3×6, radio 999 |
| Racha en una fila de posicionamiento | pastilla `rgba(255,140,60,.10)`, texto `#d9a07a` |
| Segmentos en la tabla | 3 px de alto, 2 px de separación · V `#4f9a72` · D `#9a5a5e` · pendiente `rgba(130,175,255,.14)` · jugada, vista por otros `#55667e` |
| Segmentos en el perfil | 8 px de alto, 3-4 px de separación, radio 3 · V `#4fd68a` · D `#c8686e` · pendiente `rgba(130,175,255,.12)` con borde `.18` |
| Etiquetas NO PUNTUADA / TORNEO | radio 4, 9.5/600, espaciado .5 · `rgba(130,175,255,.10)` con `#9fb3cd` / `rgba(47,127,224,.16)` con `#8cbcf5` |
| Tooltip | `#1b2d48`, borde `rgba(130,175,255,.2)`, sombra `0 6 18 rgba(0,0,0,.4)`, radio 7, padding 8×11 |

**Tipografía:** Segoe UI para la UI. Serif (la del título dorado) para titulares, puestos y el ELO grande. **Consolas para todas las cifras**: ELO, V-D, %, rachas, 6/10 y porcentajes.

## 3. 55a-55c · Clasificación

- Contenedor: `MaxWidth` 820, alineado a la izquierda. Título 17/700 serif, conmutador **1v1 / Equipos** y, a la derecha, «12 clasificados · 3 en posicionamiento».
- **Columnas:** `40 · minmax(0,1fr) · 140 · 64 · 52` → `#` · JUGADOR · ELO · V-D · `%`. Filas de 44 px y padding lateral de 14.
  - ELO: la cifra 13/600 y debajo una barra de 3 px proporcional al primer puesto.
  - El puesto 1 va en `#f4d9a0` serif 13/700; los demás en `#cdd9e9`.
- **Nombre:** insignia (18×21 en 1v1; escudo doble de 24×21 en Equipos), nombre 13/600 recortado con «…» y la racha. **La racha nunca se recorta.**
- **Tooltip de la racha:** «{n} victorias seguidas» y, debajo, «Se corta al perder o tras 14 días sin jugar una partida puntuada.»
- **Inactivos:** conservan su puesto. Llevan la etiqueta INACTIVO detrás del nombre, con el nombre y la cifra en `#b9c9de` y la barra en `#4a6a96`.
- **Posicionamiento: al final de la misma tabla, sin franja ni cabecera.** Filas de 54 px:
  - Sin puesto ni insignia, con un hueco del ancho de la insignia para que los nombres sigan alineados.
  - Nombre 13/500 `#9fb3cd`.
  - Columna ELO, en tres líneas: el ELO con «?» (`1490?`, Consolas 13/600 `#b9c9de`, con el «?» en `#e6b455`), «Posicionamiento 6/10» en 11 px `#8ea4c0` y los segmentos (10 en 1v1, 5 en Equipos). **Para los demás, los segmentos jugados van en gris `#55667e`**, sin distinguir victoria de derrota. Solo en tu propia fila llevan el color del resultado.
  - V-D y `%` como «—» para los demás. En tu propia fila se ve tu V-D.
  - Orden: de más a menos partidas jugadas; con empate, por nombre.
- **Por debajo de 600 px de ancho (55b):** columnas `28 · minmax(0,1fr) · 92`. Desaparecen V-D y %. El posicionamiento muestra «1534?» y «2/5» en una línea, con los segmentos debajo (filas de 44 px).
- **55c · Nadie completó todavía el posicionamiento:** caja centrada con el título 13.5/600 y la explicación 12 `#8ea4c0`. Debajo, en la misma tabla, las filas de posicionamiento.
- **55c · Tabla vacía:** el aviso y el botón sólido **+ Crear sala**.

## 4. 55d-55f · Perfil

- **Cabecera:** avatar de 56, nombre 20/700 serif y la línea de estado por modo («Industrial en 1v1 · en posicionamiento en Equipos»).
- **Una tarjeta por modo** (`repeat(2, minmax(0,1fr))`, separación de 10, padding 14×15). Por debajo de 600 px se apilan. Cada tarjeta lleva:
  - Etiqueta del modo (10.5/600 `#8ea4c0`), la insignia si está clasificado y, a la derecha, «puesto 3 de 12» o «sin puesto todavía».
  - ELO 30/700 serif: `#f4f8fc` si está clasificado; `#c3d2e5` y con «?» en `#e6b455` si está en posicionamiento («1534?»).
  - **Clasificado:** debajo del ELO, «Máximo: 1720 · 12 ago» y «Mínimo: 1488 · 3 mar» en la misma fila, que pasa a dos líneas si no cabe (11.5 `#9fb3cd`, cifras en Consolas `#dce7f5`, 14 px de separación). Fecha corta «d mmm»; si es de otro año, «12 ago 2025».
  - Después, separadas por una línea, tres columnas (`repeat(3, minmax(0,1fr))`): RACHA ACTUAL (con un «i» de 13 px y su tooltip), RACHA DE VICTORIAS MÁS LARGA y RACHA DE DERROTAS MÁS LARGA, en Consolas 17/600 `#e8eef6`. Las etiquetas pueden ocupar dos líneas. La racha de derrotas no va en rojo.
  - **En posicionamiento:** «Posicionamiento 2/5», los segmentos de 8 px y «Faltan 3 partidas puntuadas para entrar en la tabla.» No se muestran el máximo, el mínimo ni las rachas.
  - **Sin partidas en ese modo:** solo el texto «Todavía no jugaste partidas puntuadas en {modo}. Tus primeras {n} son de posicionamiento.»
- **Racha actual:** «🔥{n}» en `#ffb27a` si n ≥ 3; el número en blanco si es 1 o 2; «—» en `#6d829d` si es 0. **Racha caducada:** «—» y debajo «Se cortó el 18 sep: 14 días sin jugar» (fecha de la última partida puntuada más 14 días).
- **Contra cada rival:** conmutador 1v1 / Equipos en la cabecera de la tarjeta. Columnas `minmax(0,1fr) · 56 · 150 · 86` y filas de 42 px.
  - Primera columna: «Contra {rival}».
  - Balance «7–4» en Consolas 13/600, con las victorias en `#8fe0b0` y las derrotas en `#d99a9a`. Se usa una raya «–» (U+2013), no un guion.
  - Barra de 5 px y última partida en tiempo relativo.
  - Orden: por número de partidas. Muestra 4 filas y el enlace «Ver los {n} rivales». En Equipos, cada jugador del equipo contrario cuenta como un rival.
- **55f · Sin partidas:** ELO «—» en `#6d829d` y «Todavía no jugaste contra nadie…».
- **55f · Inactivo (30 días):** la tarjeta del modo mantiene el puesto, el ELO y el máximo. Lleva un aviso ámbar: «Inactivo: llevas 30 días sin jugar una partida puntuada. Conservas tu puesto en la tabla.» Debajo, «Última partida puntuada: {fecha}».
- En el perfil de otro jugador, los textos van en tercera persona (§10).

## 5. 55g · Sala 1v1

- Debajo de los jugadores, una tarjeta `#12213a` con la frase «Tienes un **62 %** de probabilidad de ganar» (13 px; la cifra en Consolas 700 `#f4f8fc`) y una barra de 5 px `#2f7fe0`.
- La línea del rival muestra «tu balance 9–2» si ya se enfrentaron.
- Si el rival está en posicionamiento, su ELO lleva «?» («1490? ELO»).
- **El antifarmeo no se anuncia en la sala.** Solo se ve después, en la tarjeta de resultado y en el Historial.
- **Probabilidad:** P = 1 / (1 + 10^((ELO rival − ELO tuyo) / 400)), redondeada a un entero entre 1 y 99.

## 6. 55h · Sala de equipos (interactiva en el prototipo)

- **Dos columnas,** «EQUIPO 1» y «EQUIPO 2», cada una con una franja de 3 px a la izquierda y «ELO medio {n}» a la derecha.
- **Jugador en posicionamiento:** su ELO lleva «?» («1534? · tú · anfitriona»). Ese ELO cuenta para el medio y la probabilidad igual que el de los demás.
- **Fila de jugador:** avatar de 24, nombre 13/500 recortado y, debajo, el ELO en Consolas 11 («1612 · tú · anfitriona»). A la derecha, el **selector 1 | 2**: fondo `#0b1526`, padding 2 y radio 7, con botones de 26×24 y radio 5. El activo va en el color de su equipo con texto blanco; el inactivo, transparente con `#a8bcd2`.
  - Cada jugador solo puede tocar su propio selector. El anfitrión puede tocar todos.
  - En las filas que no puede tocar, el selector se muestra solo como indicador (sin cursor ni hover).
- **SIN EQUIPO:** caja con borde `rgba(130,175,255,.18)` para los jugadores que todavía no eligieron. Desaparece cuando no queda nadie.
- **Probabilidad:** «Equipo 1: 58 % · Equipo 2: 42 %», con una barra partida de 6 px (azul / rojo). Se calcula con la fórmula del §5 usando el ELO medio de cada equipo y **se recalcula en cuanto alguien cambia de equipo**. Si un equipo está vacío, se muestra «—» y «Aparece cuando los dos equipos tengan jugadores.»
- **Aviso fijo en las salas competitivas de equipos:** «Si no eligen los mismos equipos dentro del juego, la partida no contará».
- **Botón Empezar:** sólido `#2f7fe0` cuando se puede. Desactivado: fondo `rgba(47,127,224,.22)`, texto `#7f93ad` y cursor no permitido. El motivo va a su izquierda, en 12 px `#f0dcae`, y se comprueba en este orden:
  1. sala incompleta: «Faltan jugadores: {n} de {total}.»;
  2. alguien sin equipo: «Falta que {nombres} elija equipo.» (en plural, «elijan»);
  3. equipos desiguales: «Los equipos no están parejos ({a} contra {b}).»

  Cuando todo está bien: «Todo listo. Al empezar verán una cuenta atrás con los equipos escritos.» (en `#9fb3cd`).
- En una sala casual de equipos se puede empezar con equipos desiguales, y no se muestran ni la probabilidad ni el aviso.

## 7. 55i · Cuenta atrás (equipos)

- Ventana `#16263e`, radio 12, sombra `0 10 28 rgba(0,0,0,.4)`. Círculo de 52 px con un anillo de 3 px `#2f7fe0` y el número en serif 24/700. Debajo, «ABRIENDO EL JUEGO» y el título «Elijan estos equipos en el juego».
- Recordatorio en una caja `#0f1c2e`, 13 px: «**Equipo 1:** Ana y Luis · **Equipo 2:** Pedro y Sara. Elijan esto en el juego.» Los nombres de los equipos van en sus colores. En 3v3 se usa «Ana, Luis y Marta». **No se recorta:** el texto pasa a varias líneas (`overflow-wrap:anywhere`).
- Pie: «Si no coinciden, la partida no contará.» y el botón **Cancelar**.

## 8. 55j · Tarjeta de resultado · 55k · Historial

**Tarjeta** (330 px, radio 10, franja izquierda de 4 px):

| Caso | Delta | Texto |
| --- | --- | --- |
| Normal | `+14` · `1598 → 1612` | Si hay racha ≥ 3: pastilla 🔥N y «{n} victorias seguidas» |
| Antifarmeo | `+5` y `40 %` en `#e6b455` | «**+5 (40 %):** 8.ª victoria seguida contra este rival. Vuelve a la normalidad si él gana una, o se recupera un 10 % por cada día sin jugar entre ustedes.» |
| Equipos que no coincidieron | `—`, fondo `#101d31`, franja `#4a5a72`, etiqueta NO PUNTUADA | «La partida no contó: los equipos del juego no coincidieron con los de la sala (en el juego: Ana y Pedro contra Luis y Sara).» |
| Cuenta nueva | `—`, fondo `#101d31`, franja `#4a5a72`, etiqueta NO PUNTUADA | «Esta partida no contó para el ELO (cuenta nueva y partida muy corta).» |
| Posicionamiento completado | delta normal, fondo degradado `#1b2e4c` → `#12213a` | Insignia nueva de 30×35, «¡Posicionamiento completado!» (13.5/600) y «Entras en la tabla en el puesto 7.» |

Quien pierde con el antifarmeo ve: «**−4 (40 %):** {rival} te ganó 8 veces seguidas. Vuelve a la normalidad si le ganas una, o se recupera un 10 % por cada día sin jugar entre ustedes.»

**Historial** (fichas de 3b; la segunda línea es «{mod} · {mapa} · {hora}», **sin duración**):
- Con antifarmeo: «+5» en Consolas 17/600 y, al lado, «· 40 %» en 11.5 `#e6b455`.
- No puntuada: fondo `#101d31`, franja `#4a5a72`, etiqueta NO PUNTUADA, delta «—», y el motivo en 11.5 `#d8bd8a`. Motivos:
  - «Los equipos del juego no coincidieron con los de la sala.»
  - «No se pudo leer el resultado: Record Game estaba desactivado.»
  - «Sala casual: no mueve el ELO.»
  - «Esta partida no contó para el ELO (cuenta nueva y partida muy corta).»
- Torneo: etiqueta TORNEO; la segunda línea es «{torneo} · {ronda} · {hora}». Nunca lleva porcentaje.

**55n · Devolución de puntos**
- **Notificación** (una sola vez, en las notificaciones del Multijugador): tarjeta `#16263e` de 360 px, radio 10. A la izquierda, el icono «↺» en un círculo de 30 px. Arriba, «NOTIFICACIÓN · 1V1 · hace 2 h». El texto, en 13 px `#e8eef6`: «Recuperaste 34 puntos: un rival contra el que perdiste fue sancionado por hacer trampas.». A la derecha, «+34» en Consolas 17/600 `#8fe0b0` y «1578 → 1612».
- **Banner en el perfil,** encima de la tarjeta del modo afectado, con los colores de devolución del §2, el mismo texto y el botón **Entendido**, que lo cierra para siempre.
- **No se nombra al rival sancionado.** Si hubo varias partidas, todo se suma en un solo aviso.

## 9. 55l · Destacados · 55m · Discord

- Tarjeta bajo la lista de salas: «DESTACADOS DE {MES}» y «hasta hoy». A la derecha, el enlace «Ver {mes anterior}». Tres celdas `#16263e`, cada una con avatar de 26 px, nombre recortado, cifra y una línea de contexto.
- **Quién más subió:** ELO al final del mes menos ELO al principio, **por modo**. Se muestra el mayor de los dos, con el modo en el contexto («en Equipos · 12 partidas»). Exige un mínimo de partidas puntuadas en ese modo (lo da el servidor). No entra nadie en posicionamiento.
- **Más partidas:** partidas puntuadas de los dos modos.
- **Mejor racha del mes:** la racha más larga conseguida dentro del mes, con su modo.
- **Estado vacío:** «El mes recién empieza…» y el enlace al mes anterior. Por debajo de 600 px, las celdas se apilan.
- **Discord:** imagen de 1200 × 675 con fondo `radial-gradient(120% 90% at 0% 0%, #1d3354, #0f1c2e 60%)`, logo `wol-logo-icono-izquierda.png` a 120 px de alto, título 62/700 serif y tres tarjetas. El prototipo está a escala 0,8. Se genera el día 1 con el mes cerrado. El texto del mensaje está en el §10.

## 10. Textos (Strings.cs)

| Clave sugerida | es (es-419) | en |
| --- | --- | --- |
| `MpRankCountSummary` | {0} clasificados · {1} en posicionamiento | {0} ranked · {1} in placement |
| `MpRankColElo` / `ColWL` | ELO / V-D | ELO / W-L |
| `MpRankYouTag` | TÚ | YOU |
| `MpPlacementProgress` | Posicionamiento {0}/{1} | Placement {0}/{1} |
| `MpRankFootRule` | Sin número y con «?»: en posicionamiento ({0} partidas puntuadas). 🔥 marca 3 o más victorias seguidas. INACTIVO: sin partidas puntuadas en {1} días; conserva su puesto. | No number and a «?»: in placement ({0} rated matches). 🔥 marks 3 or more wins in a row. INACTIVE: no rated match in {1} days; keeps their place. |
| `MpRankHowElo` | Cómo funciona el ELO | How ELO works |
| `MpStreakTipTitle` | {0} victorias seguidas | {0} wins in a row |
| `MpStreakTipBody` | Se corta al perder o tras 14 días sin jugar una partida puntuada. | Ends when you lose or after 14 days without a rated match. |
| `MpRankEmptyPlacementTitle` | Nadie completó todavía el posicionamiento | Nobody has finished placement yet |
| `MpRankEmptyPlacementBody` | Los primeros puestos aparecen cuando alguien juegue sus {0} partidas puntuadas. | The first ranks appear once someone plays their {0} rated matches. |
| `MpRankEmptyTitle` | Todavía nadie jugó una partida puntuada | Nobody has played a rated match yet |
| `MpRankEmptyBody` | Crea una sala competitiva y activa Record Game para que el resultado cuente. | Create a competitive room and turn on Record Game so the result counts. |
| `MpModeOneVsOne` / `ModeTeams` | 1v1 / Equipos | 1v1 / Teams |
| `MpProfileStatusRanked` / `StatusPlacement` | {0} en {1} / en posicionamiento en {0} | {0} in {1} / in placement in {0} |
| `MpProfileRankOf` / `NoRankYet` | puesto {0} de {1} / sin puesto todavía | rank {0} of {1} / no rank yet |
| `MpProfilePeak` | Máximo: {0} · {1} | Peak: {0} · {1} |
| `MpProfileStreak` | RACHA ACTUAL | CURRENT STREAK |
| `MpProfileLongestWin` / `LongestLoss` | RACHA DE VICTORIAS MÁS LARGA / RACHA DE DERROTAS MÁS LARGA | LONGEST WIN STREAK / LONGEST LOSING STREAK |
| `MpProfileLow` | Mínimo: {0} · {1} | Low: {0} · {1} |
| `MpProfileStreakTipTitle` | Racha actual: {0} victorias seguidas | Current streak: {0} wins in a row |
| `MpProfileStreakTipBody` | Se corta al perder o tras 14 días sin jugar una partida puntuada. La racha más larga queda guardada. | Ends when you lose or after 14 days without a rated match. Your longest streak is kept. |
| `MpProfileStreakExpired` | Se cortó el {0}: 14 días sin jugar | Ended {0}: 14 days without playing |
| `MpPlacementLeft` / `LeftOne` | Faltan {0} partidas puntuadas para entrar en la tabla. / Falta 1 partida puntuada para entrar en la tabla. | {0} more rated matches to enter the table. / 1 more rated match to enter the table. |
| `MpProfileModeEmptyYou` / `Other` | Todavía no jugaste partidas puntuadas en {0}. Tus primeras {1} son de posicionamiento. / Todavía no jugó partidas puntuadas en {0}. | You haven't played rated matches in {0} yet. Your first {1} are placement. / No rated matches in {0} yet. |
| `MpProfileNoMatchesYou` / `Other` | Todavía no jugaste partidas puntuadas. Tu primera partida en cada modo empieza su posicionamiento. / Todavía no jugó partidas puntuadas. | You haven't played rated matches yet. Your first match in each mode starts its placement. / No rated matches yet. |
| `MpH2HTitle` / `H2HSub` | Contra cada rival / partidas puntuadas | Against each opponent / rated matches |
| `MpH2HRow` | Contra {0} | Against {0} |
| `MpH2HRowTip` | Contra {0}: {1}–{2} | Against {0}: {1}–{2} |
| `MpH2HSeeAll` | Ver los {0} rivales | See all {0} opponents |
| `MpH2HEmptyYou` / `Other` | Todavía no jugaste contra nadie. Aquí verás tu balance con cada rival. / Todavía no jugó contra nadie. | You haven't played anyone yet. Your record against each opponent will show here. / No opponents yet. |
| `MpRoomPlayers1v1` / `PlayersTeams` | JUGADORES · COMPETITIVA 1V1 / JUGADORES · COMPETITIVA {0} · {1} DE {2} | PLAYERS · COMPETITIVE 1V1 / PLAYERS · COMPETITIVE {0} · {1} OF {2} |
| `MpRoomRecord` | tu balance {0}–{1} | your record {0}–{1} |
| `MpWinProb1v1` | Tienes un {0} % de probabilidad de ganar | You have a {0}% chance to win |
| `MpWinProbTeams` | Equipo 1: {0} % · Equipo 2: {1} % | Team 1: {0}% · Team 2: {1}% |
| `MpWinProbTeamsNote` | Según el ELO medio de cada equipo. Cambia al mover a alguien. | Based on each team's average ELO. Updates when someone moves. |
| `MpWinProbTeamsEmpty` | Aparece cuando los dos equipos tengan jugadores. | Shows once both teams have players. |
| `MpTeam1` / `Team2` / `NoTeam` | EQUIPO 1 / EQUIPO 2 / SIN EQUIPO | TEAM 1 / TEAM 2 / NO TEAM |
| `MpTeamAvg` | ELO medio {0} | Avg. ELO {0} |
| `MpTeamEmpty` | Nadie todavía | Nobody yet |
| `MpTeamHostTag` | anfitrión / anfitriona | host |
| `MpTeamsMismatchWarn` | Si no eligen los mismos equipos dentro del juego, la partida no contará | If you don't pick the same teams in the game, the match won't count |
| `MpStart` | Empezar | Start |
| `MpStartBlockedPrefix` | No puedes empezar todavía: | You can't start yet: |
| `MpStartBlockedPlayers` | faltan jugadores ({0} de {1}). | players missing ({0} of {1}). |
| `MpStartBlockedNoTeam` / `NoTeamPl` | falta que {0} elija equipo. / falta que {0} elijan equipo. | {0} still has to pick a team. / {0} still have to pick a team. |
| `MpStartBlockedUneven` | los equipos no están parejos ({0} contra {1}). | teams aren't even ({0} vs {1}). |
| `MpStartReady` | Todo listo. Al empezar verán una cuenta atrás con los equipos escritos. | All set. When you start, everyone sees a countdown with the teams. |
| `MpCountdownLabel` | ABRIENDO EL JUEGO | OPENING THE GAME |
| `MpCountdownTitle` | Elijan estos equipos en el juego | Pick these teams in the game |
| `MpCountdownTeams` | Equipo 1: {0} · Equipo 2: {1}. Elijan esto en el juego. | Team 1: {0} · Team 2: {1}. Pick this in the game. |
| `MpCountdownFoot` | Si no coinciden, la partida no contará. | If they don't match, the match won't count. |
| `MpCancel` | Cancelar | Cancel |
| `MpResultWin` / `Loss` | Victoria / Derrota | Victory / Defeat |
| `MpResultVs` | contra {0} · {1} | vs {0} · {1} |
| `MpResultStreak` | {0} victorias seguidas | {0} wins in a row |
| `MpResultFarmWin` | {0} ({1} %): {2}.ª victoria seguida contra este rival. Vuelve a la normalidad si él gana una, o se recupera un 10 % por cada día sin jugar entre ustedes. | {0} ({1}%): {2} win in a row against this opponent. It goes back to normal if they win one, or recovers 10% for each day you two don't play. |
| `MpResultFarmLoss` | {0} ({1} %): {2} te ganó {3} veces seguidas. Vuelve a la normalidad si le ganas una, o se recupera un 10 % por cada día sin jugar entre ustedes. | {0} ({1}%): {2} has beaten you {3} times in a row. It goes back to normal if you win one, or recovers 10% for each day you two don't play. |
| `MpResultTeamsMismatch` | La partida no contó: los equipos del juego no coincidieron con los de la sala (en el juego: {0} contra {1}). | The match didn't count: the in-game teams didn't match the room's (in the game: {0} vs {1}). |
| `MpResultPlacementDone` | ¡Posicionamiento completado! | Placement complete! |
| `MpResultPlacementRank` | Entras en la tabla en el puesto {0}. | You enter the table at rank {0}. |
| `MpHistUnrated` | NO PUNTUADA | UNRATED |
| `MpHistTournament` | TORNEO | TOURNAMENT |
| `MpHistFarmPct` | · {0} % | · {0}% |
| `MpHistReasonTeams` | Los equipos del juego no coincidieron con los de la sala. | The in-game teams didn't match the room's. |
| `MpHistReasonNoResult` | No se pudo leer el resultado: Record Game estaba desactivado. | The result couldn't be read: Record Game was off. |
| `MpHistReasonCasual` | Sala casual: no mueve el ELO. | Casual room: doesn't move ELO. |
| `MpHistNoResult` | Sin resultado | No result |
| `MpEloProvisional` | {0}? | {0}? |
| `MpEloProvisionalTip` | ELO provisional: se ajusta durante el posicionamiento. | Provisional ELO: it settles during placement. |
| `MpRankInactiveTag` | INACTIVO | INACTIVE |
| `MpInactiveNotice` / `Other` | Inactivo: llevas 30 días sin jugar una partida puntuada. Conservas tu puesto en la tabla. / Inactivo: lleva 30 días sin jugar una partida puntuada. | Inactive: you haven't played a rated match in 30 days. You keep your place in the table. / Inactive: no rated match in 30 days. |
| `MpInactiveLast` | Última partida puntuada: {0} | Last rated match: {0} |
| `MpResultNewAccount` / `MpHistReasonNewAccount` | Esta partida no contó para el ELO (cuenta nueva y partida muy corta). | This match didn't count for ELO (new account and a very short match). |
| `MpRefundNotifLabel` | NOTIFICACIÓN · {0} · {1} | NOTIFICATION · {0} · {1} |
| `MpRefundBody` | Recuperaste {0} puntos: un rival contra el que perdiste fue sancionado por hacer trampas. | You got {0} points back: an opponent you lost to was penalized for cheating. |
| `MpRefundDismiss` | Entendido | Got it |
| `MpHlTitle` / `HlSub` | DESTACADOS DE {0} / hasta hoy | {0} HIGHLIGHTS / so far |
| `MpHlSeePrev` | Ver {0} | See {0} |
| `MpHlTopGain` / `TopGainSub` | QUIÉN MÁS SUBIÓ / en {0} · {1} partidas | BIGGEST CLIMB / in {0} · {1} matches |
| `MpHlMostGames` / `MostGamesSub` | MÁS PARTIDAS / partidas puntuadas | MOST MATCHES / rated matches |
| `MpHlBestStreak` / `BestStreakSub` | MEJOR RACHA DEL MES / victorias seguidas en {0} | BEST STREAK OF THE MONTH / wins in a row in {0} |
| `MpHlEmpty` | El mes recién empieza. Los destacados aparecen cuando haya al menos {0} partidas puntuadas este mes. | The month has just started. Highlights appear once there are at least {0} rated matches this month. |

Los ordinales en inglés (`8th`, `2nd`, `3rd`…) salen de un helper. En español, «8.ª». En las listas de nombres se usa «Ana y Luis» / «Ana, Luis y Marta», y en inglés «Ana and Luis» / «Ana, Luis and Marta».

**Mensaje de Discord**:

```
🏆 Destacados de {mes}
📈 Quién más subió: {nombre}, +{n} ELO en {modo} en {m} partidas puntuadas
⚔️ Más partidas: {nombre}, {n} partidas puntuadas
🔥 Mejor racha: {nombre}, {n} victorias seguidas en {modo}
{total} partidas puntuadas este mes. La clasificación completa está en el launcher, pestaña Clasificación.
```

```
🏆 {Month} highlights
📈 Biggest climb: {name}, +{n} ELO in {mode} over {m} rated matches
⚔️ Most matches: {name}, {n} rated matches
🔥 Best streak: {name}, {n} wins in a row in {mode}
{total} rated matches this month. The full ranking is in the launcher, Ranking tab.
```

## 11. Datos

**Existe hoy** (`LobbyDtos.cs`): `EloSnapshot` (rating, rd, wins, losses), `LeaderboardRow` (rank, rating, rd, wins, losses) y `MatchHistoryRow` (result, rating_before, rating_after, participantes, mapa, mod).

**Nuevo, del servidor** (todo por modo):

| Campo (sugerido) | Para qué |
| --- | --- |
| `placement_played`, `placement_required` (10 / 5), `placement_results` (solo al propio jugador) | tabla y perfil |
| `streak_current`, `streak_best`, `loss_streak_best`, `streak_ended_at` | 🔥, celdas de racha, racha caducada |
| `rating_peak`, `rating_peak_at` | «Máximo: 1720 · 12 ago» |
| `rating_low`, `rating_low_at` | «Mínimo: 1488 · 3 mar» |
| `inactive`, `last_rated_at` (los inactivos siguen en la tabla) | etiqueta INACTIVO y aviso del perfil |
| `head_to_head[]`: rival, wins, losses, last_at | Contra cada rival |
| sala: `team` por jugador (1, 2 o ninguno) y quién es el anfitrión | 55h |
| resultado e historial: `elo_factor`, `farm_streak`, `unrated_reason` (`teams_mismatch`, `no_result`, `casual`, `new_account_short`), `ingame_teams`, `tournament` | 55j-55k |
| resultado: `placement_completed`, `entered_rank` | 55j |
| `refunds[]`: puntos, modo, fecha, rating_before/after (sin el rival) | 55n |
| `monthly_highlights`: periodo, total, top_gain (+modo), most_games, best_streak (+modo), mínimos | 55l-55m |

## 12. Para confirmar con el servidor

1. **Probabilidad:** ¿se usa la fórmula Elo del §5 con el ELO (medio)? Si el servidor tiene en cuenta el `rd` (Glicko), la cifra debe venir del servidor.
2. **Antifarmeo en equipos:** ¿se aplica por pareja de jugadores, o no se aplica?
3. **Recuperación del 10 % por día:** ¿se cuenta en días naturales (UTC) o en periodos de 24 h desde la última partida entre ellos?
