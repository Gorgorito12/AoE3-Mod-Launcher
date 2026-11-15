# 3 · Torneos: cada estado con su color (diseño 106)

Prototipo: `Torneos v3.dc.html`. Anterior: `Torneos v2.dc.html`. Captura de hoy: `uploads/torneo-hoy.png`. Los archivos están en la raíz del paquete.

Hoy, en la pestaña Torneos, todo lo que tiene color sale del mod: la selección, tu partido, la ronda en juego, el paso actual, el camino del campeón y la tarjeta del campeón. El resto es azul marino y gris. Con un mod rojo como Wars of Liberty, el campeón queda en un cartel rojo que parece un aviso de error, y un torneo terminado no tiene ni un color propio (`uploads/torneo-hoy.png`).

El cambio: **la estructura no se mueve; cambia de dónde sale el color.** El del mod se queda en lo que es tuyo o del mod. Lo demás usa colores de estado fijos, los mismos de Salas.

Repo: `Gorgorito12/AoE3-Mod-Launcher`, rama `main`, proyecto WPF `WarsOfLibertyLauncher/`.

| Archivo del paquete | Qué contiene |
| --- | --- |
| `Torneos v3.dc.html` | Prototipo, turno 106 (interactivo). Se abre en un torneo terminado de 4 jugadores con mod rojo, el caso de la captura. En la lista: «Copa de Octubre» (en curso, con partidos en juego, un ausente y un descalificado), «Liga 3v3 de la Guardia» (equipos), «Relámpago de Improvement» (inscripción). |
| `Torneos v2.dc.html` | El diseño anterior (turno 80), para comparar. La estructura es idéntica. |
| `uploads/torneo-hoy.png` | Captura del launcher hoy. |

Abre los `.dc.html` en un navegador con `support.js` al lado (necesitan internet). Son **referencia de diseño**, no código para copiar.

> Los nombres de clases de abajo salen de las notas del proyecto de diseño (`Controls/MultiplayerTab.xaml` › `TournamentsView`, `Services/Multiplayer/BracketLayout.cs`, `TournamentDetail`, `TournamentMatch`), no del código de hoy. Si en `main` se llaman distinto, usa los equivalentes y dímelo.

---

## 1. La regla

- **Color del mod:** solo la fila elegida de la lista, **tu partido** (casilla, tarjeta de arriba, «TÚ», tu camino por el cuadro) y el **botón principal**. Igual que hoy.
- **Todo lo demás:** colores de estado fijos. Nunca toman el color del mod.

| Color | Valor | Texto en ese color | Significa |
| --- | --- | --- | --- |
| Verde | `#4FD68A` | `#7FE0A8` | Abierto o ganado |
| Azul | `#55ACEE` | `#A9D6F7` | Se está jugando |
| Dorado | `#E6C06A` | `#F3D995` | Campeón, torneo terminado |
| Ámbar | `#F4900C` | `#F5BD76` | No se presentó |
| Rojo | `rgba(200,74,74,…)` | `#F0A6A6` | Descalificado |
| Neutro | `#8C9CB1` | `#C3D2E5` | Borrador, sin decidir |

Reutiliza los pinceles de estado que ya existan en `Styles/Colors.xaml` (los de Salas, `MpRoomJoin*` para la casilla verde). No declares duplicados.

**Animación:** el «punto que late» es opacidad 1 → 0,45 y escala 1 → 0,72, ciclo de 1,6 s. Con efectos reducidos se queda fijo.

## 2. Estado del torneo

Cambian dos colores respecto a hoy, para que signifiquen lo mismo que en Salas:

| Estado | Hoy | Nuevo |
| --- | --- | --- |
| Borrador | gris | gris (igual) |
| Inscripción abierta | azul | **verde** |
| Semillas | azul | azul (igual) |
| En curso | verde | **azul**, con el punto que late |
| Terminado | gris | **dorado** |

Ese color va en tres sitios:

- **Chip de estado** junto al nombre del mod (26 de alto): deja de ser cristal neutro. Fondo: el color al 20 % sobre `rgba(10,19,33,.72)`. Borde: el color al 55 %. Texto: el color de texto. Punto de 7.
- **Punto del subtítulo** en cada fila de la lista de torneos.
- **Filtros de la lista** (Inscripción · En curso · Terminados): un punto de 7 del color de su estado delante del texto. «Todos» no lleva punto.

En la tarjeta de acción de un torneo en inscripción, el rótulo «INSCRIPCIÓN ABIERTA · 9 DE 16» pasa de azul a verde `#7FE0A8`.

## 3. Barra de pasos

Hoy son cinco chips grises sueltos con un carácter delante (✓ ● ○). Pasan a ser **una sola cápsula** con los pasos unidos por una línea.

- **Cápsula:** alto mínimo 30, radio 15, fondo `rgba(10,19,33,.62)`, borde blanco al 10 %, relleno 6 / 13 / 6 / 8. Se oculta en ventanas bajas igual que hoy.
- **Cada paso:** círculo de 16 + etiqueta 11,5 / 600, separados 6. Entre pasos, una línea de 14 × 2 con 6 de margen a cada lado.

| Paso | Círculo | Etiqueta |
| --- | --- | --- |
| Cumplido | relleno verde, con un visto dibujado en `#0F1C2E` | `#DCE7F5` |
| Actual | fondo `rgba(10,19,33,.75)`, borde de 1,5 del color del estado, punto de 6 del mismo color (late si el torneo está en curso) | color de texto del estado |
| Pendiente | sin relleno, borde de 1,5 de blanco al 24 % | `#8C9CB1` |
| «Terminado», cuando el torneo terminó | relleno **dorado**, con visto | `#F3D995` |

- **Líneas:** verde al 70 % hasta el paso actual; blanco al 16 % a partir de ahí.
- El visto es un trazo, no una letra: `M4.5 10.5l3.8 3.8 7.2-8.3` en una caja de 20 × 20, extremos redondos.

## 4. Campeón: dorado

Nada del campeón usa el color del mod.

- **Degradado dorado del escudo:** 160°, `#FFF3CF` → `#E6C06A` al 52 % → `#8A6418`. La inicial va en `#3B2A06` (antes blanca).
- **Tarjeta de arriba:** fondo `#3E403B` (dorado mezclado al 78 % con `#0F1C2E`) con un resplandor dorado al 40 % desde la esquina superior izquierda que se apaga al 65 %. Borde de 1,5 dorado al 85 %. Halo `0 0 36` dorado al 30 %. Rótulo «CAMPEÓN» en `#F3D995`.
- **Casilla del campeón en el cuadro:** mismo fondo, con el resplandor (al 42 %) desde arriba al centro. Borde de 1,5 dorado al 85 %. Halo `0 0 38` dorado al 34 %.
- **Camino del campeón:** las líneas de 2 px que hoy van en el color del mod pasan a dorado al 90 %. Las casillas por las que pasó llevan borde dorado al 55 %. El título de columna «CAMPEÓN» va en `#F3D995`.
- **Su fila en la final:** tinte dorado de izquierda a derecha (24 % → 0 % al 80 %) y la casilla del resultado dorada (fondo dorado al 24 %, borde dorado al 75 %, número en `#F3D995`).
- **Participantes:** la fila del campeón lleva tinte dorado (20 % → 5 %) y borde dorado al 55 %; su estado «Campeón» pasa de verde a dorado.

## 5. El cuadro

**Resultado en casilla.** El `1` y el `0` sueltos pasan a una casilla de **18 × 18**, radio 5, Consolas 11,5 / 700:

| Caso | Fondo | Borde | Contenido |
| --- | --- | --- | --- |
| Ganó | `#1D4A37` | verde al 55 % | `1` blanco |
| Perdió | blanco al 5 % | blanco al 10 % | `0` en `#8C9CB1` |
| Ganó sin jugar (el rival no se presentó o fue descalificado) | `#1D4A37` | verde al 55 % | visto dibujado, blanco. Tooltip «Ganó sin jugar» |
| Ganó la final | dorado al 24 % | dorado al 75 % | `1` en `#F3D995` |

**Fila del ganador:** tinte verde de izquierda a derecha (16 % → 0 % al 80 %); el nombre sigue en blanco 700 y el del perdedor atenuado.

**Etiquetas** (18 de alto, radio 9, 9,5 / 700):

| Etiqueta | Fondo | Borde | Texto |
| --- | --- | --- | --- |
| AUSENTE | `rgba(244,144,12,.14)` | `rgba(244,144,12,.5)` | `#F5BD76` |
| DQ | `rgba(200,74,74,.18)` | `rgba(200,74,74,.55)` | `#F0A6A6` |
| PASA | blanco al 8 % | blanco al 14 % | `#C3D2E5` |

**Corrección:** en el prototipo anterior la etiqueta **DQ** salía en la fila del que ganó. Va en la fila del **descalificado**, igual que AUSENTE va en la del que no se presentó. Comprueba cómo está en el código y dímelo.

**Casilla de un partido:**

| Caso | Fondo | Borde y halo |
| --- | --- | --- |
| En juego | tinte azul (20 % → 4 % al 72 %) sobre `#12213A` | borde de 1,5 azul al 80 %, halo azul `0 0 18` recogido 2 al 50 %. Punto azul de 7 arriba a la derecha, que late (antes verde y fijo) |
| Tu partido | como hoy (color del mod) | como hoy, más un halo del color del mod `0 0 18` recogido 2 al 50 % |
| Elegida (clic) | — | como hoy; en un torneo terminado, el anillo de 2 px es dorado |
| El resto | `#12213A` | blanco al 10 %, más un filo de luz de 1 px de blanco al 5 % arriba |

**Ronda en juego:** el título de su columna pasa del color del mod a `#A9D6F7`, con un punto azul de 6 que late delante. Su píldora de salto lleva borde azul al 70 % y punto azul.

**Cuadro pequeño:** con 4 jugadores, la casilla del campeón es más alta que el cuadro y se corta por arriba contra la fila de títulos. Cuando el alto del cuadro sea menor que 200, se centra dentro de 200.

## 6. Panel del partido (a la derecha)

- Las mismas casillas de resultado, a **22 × 22** con radio 6, y las mismas etiquetas.
- Fila del ganador: tinte verde (18 % → 5 %) y borde verde al 50 %. En la final de un torneo terminado, dorado (22 % → 6 %, borde al 60 %).
- Línea de estado: «Jugándose ahora» en `#A9D6F7` con punto azul que late (antes verde). «Terminado · ganó …» con punto verde, dorado si es la final.

## 7. Leyenda del pie

Usa las mismas piezas que el cuadro, no texto gris: el recuadro de «Tu partido» (color del mod), el punto azul de «Jugándose ahora», las casillas `1` verde y `0` gris para «ganó / perdió», una raya dorada para «Camino del campeón» (solo si el torneo terminó), y las etiquetas AUSENTE, DQ y PASA con su color, cada una con su explicación al lado.

## 8. Lo que NO cambia

- La disposición: lista, cabecera, tarjeta de acción, pestañas Cuadro · Participantes · Organizar, zoom, búsqueda, saltos de ronda.
- Qué se muestra en cada estado y lo que hace cada botón.
- La selección de la lista, tu partido y el botón principal siguen con el color del mod.
- El servidor: no hay campos nuevos.

## 9. Textos nuevos

Dos: «Camino del campeón» (leyenda) y «Ganó sin jugar» (tooltip). Van en `Localization/Strings*.cs`, en español (es-419) e inglés.

## 10. A tener en cuenta

En un mod cuyo color ya es dorado (Hundred Days, The Asian Dynasties), «tu partido» se parece al dorado del campeón. No coinciden en pantalla: uno sale durante el torneo y el otro al final. No hay que hacer nada; si en una captura confunde, dímelo.

## 11. Antes de tocar este bloque

1. Dime, para cada cosa con color de la pestaña (casilla de un partido, tarjeta y casilla del campeón, pasos, chip de estado, leyenda), de qué pincel sale hoy.
2. Haz la lista de todos los sitios donde Torneos usa el color del mod y sepárala en dos: los que se quedan con él (selección, tu partido, botón principal) y los que pasan a un color de estado.
3. Comprueba dónde sale hoy la etiqueta DQ: en la fila del que ganó o en la del descalificado.
4. «Inscripción abierta» pasa a verde y «En curso» a azul. Cámbialos en todos los sitios a la vez (chip, punto de la lista, filtros, pasos) para que no queden mezclados.

## 12. Orden sugerido y capturas

Commits: pinceles → estado del torneo (chip, punto de la lista, filtros) → barra de pasos → campeón en dorado (tarjeta, casilla, camino, participantes) → casillas del cuadro (resultado, fila ganadora, etiquetas, partido en juego, ronda en juego) → panel del partido → leyenda → cuadro pequeño.

Cuando tengas el estado del torneo, la barra de pasos y el campeón en dorado, párate y enséñame dos capturas antes de seguir con el cuadro: un torneo terminado de Wars of Liberty y uno en curso. Al terminar el bloque, las mismas dos con el cuadro completo.
