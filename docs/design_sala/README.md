# La sala: el panel de jugadores tapado

Referencias visuales en `Prototipo.html`: **22b** (el panel de jugadores, hoy y propuesta, medidos lado a lado) y **22a** (la ventana entera con el espacio repartido). Las dos son la misma ventana, no alternativas — 22b es el detalle del defecto principal.

Repo: `Gorgorito12/AoE3-Mod-Launcher`, rama `main`, proyecto WPF `WarsOfLibertyLauncher/`.

| Qué | Archivos del repo |
| --- | --- |
| La ventana | `LobbyWindow.xaml` + `.xaml.cs` |
| Estado de la sala | `Services/Multiplayer/MultiplayerSession.cs`, `Models/Multiplayer/LobbyDtos.cs` (`WsRoomState`) |
| Textos | `Localization/Strings.cs` |

**Esto se diseñó desde una captura de tu build, no leyendo `LobbyWindow`.** Verifica la estructura antes de implementar. El único dato que comprobé en el repo en una sesión anterior: `LobbyWindow.xaml.cs:149` dice `ref 900x600` como base del escalado del contenido, así que el diseño tiene que aguantar a ese tamaño y no solo al de la captura.

El HTML es una **referencia de diseño**, no código para copiar. Todo el texto pasa por `Localization/Strings.cs` (español e inglés).

## El defecto: el panel tiene alto fijo y su contenido no cabe

En una sala 1v1 con un jugador dentro, el panel `PLAYERS` muestra una barra de desplazamiento y **corta la segunda línea del único jugador**: se lee «gorgorito_12» y debajo «1383 ELO · you» partido por la mitad.

Las cifras, medidas en la maqueta: la fila necesita **48 px** —nombre 20, línea de ELO 16, relleno— y el panel le da **28**.

Dos consecuencias peores que el recorte:

1. **La segunda plaza no existe.** En una sala de dos, la plaza libre no se dibuja en ninguna parte, así que nada indica que falta alguien ni qué hacer al respecto. El código de la sala está arriba, en la cabecera, desconectado de la plaza que hay que llenar.
2. **`Host` y `waiting` se solapan.** Van las dos en el mismo flujo horizontal, sin columna propia para el estado, así que se pisan en el borde derecho de la fila.

### La corrección

- **El panel crece con sus plazas.** Sin alto fijo: tantas filas como plazas tenga la sala. Tope a las **ocho** de AoE 3; por encima de eso sí desplaza, pero mostrando **filas enteras**, nunca media línea.
- **La plaza libre es una fila**, con borde discontinuo: «Waiting for an opponent» y debajo «share the code 5Q8HB6HM», con un botón `Copy` a la derecha. Así la acción está donde está el problema.
- **El estado tiene su propia columna**, alineado a la derecha (`waiting` en ámbar, `ready` en verde). La etiqueta `HOST` se queda junto al nombre.
- **La cabecera del panel dice la cuenta**: `PLAYERS · 1 OF 2`. Hoy ese dato solo está en la tarjeta de arriba.

Estructura de la fila: avatar 26 · columna flexible con nombre + `HOST` arriba y `1383 ELO · you` abajo en monoespaciada · estado o acción a la derecha, `flex:none`. El nombre se recorta con elipsis; el estado nunca.

## Lo demás que la captura deja ver

**El reparto del espacio.** La columna izquierda comprime lo que el jugador mira mientras el chat vacío se queda con ~740 px y una línea en cursiva centrada. El panel de jugadores es lo primero que se mira en un lobby: dale el alto que necesita antes que al hueco del chat.

**«Identical mods across all 1 players».** La frase interpola el recuento y se rompe en singular. Reescríbela para que no dependa del número: «Everyone here has the same mod». Si necesitas el recuento, usa formas plurales completas, no una plantilla partida.

**Dos de las tres casillas no son tareas.** Que abandonar la sala después de cinco minutos cuente como derrota **no es algo que marques**: es una regla. Pasa a nota con icono de información. La casilla se reserva para lo único accionable, el *Record Game*, y su texto dice por qué importa: el ganador se lee de la grabación, así que sin ella la partida no cuenta para nadie.

**«Copy · Wars of Liberty (2)» es jerga.** Dice que los ajustes se copiarán de esa instalación: «Settings — copied from your Wars of Liberty install».

**«Leave room» no nombra su consecuencia.** En una sala competitiva pasada de los cinco minutos, salir es perder. El botón lo dice: «Leave room · counts as a loss», y pide confirmación.

**«P2P LAN ready» es jerga**: «player-to-player network ready».

**El chat vacío cuenta un hecho, no un silencio.** Que la sala se acabe de abrir sí se puede contar —«You opened this room. It is listed in Multiplayer with a COMPETITIVE · 1v1 badge»— más una línea gris de «Nobody has written yet» y tres frases de arranque pulsables.

## Tokens

Los mismos del resto del launcher.

| Uso | Hex |
| --- | --- |
| Fondo de la ventana | `#0f1c2e` |
| Barra de título | `#233648` (título en dorado `#f4d9a0`) |
| Panel | `#12213a` |
| Fila del jugador presente / cabecera | `#16263e` |
| Campo | `#0d1828` |
| Borde interior | `rgba(130,175,255,.07–.20)` |
| Borde de sala competitiva | `rgba(217,178,106,.32)` · insignia `#d9b26a` sobre texto `#1c2734` |
| Azul de acción | `#2f7fe0` · texto `#8cbcf5` / `#8fb6ea` |
| Verde presente / hecho | `#4fd68a` · texto `#8fe0b0` / `#7fdca6` sobre `rgba(53,196,111,.18)` |
| Ámbar esperando / requerido | texto `#e0be7f` / `#d8bd8a` / `#f0dcae`, borde de casilla `rgba(230,180,85,.5)` |
| Rojo de abandonar | texto `#d99a9a` · borde `rgba(200,120,120,.28)` |
| Texto | titular `#f0f5fb` · primario `#e8eef6` · cuerpo `#b9c9de` · atenuado `#8ea4c0` → `#6d829d` → `#61779a` → `#5f7592` |

**Tipografía.** UI en Segoe UI; el nombre de la sala en serif. **ELO, código, ping, cifras de cabecera y horas en monoespaciada.** Etiqueta de panel 10.5 SemiBold `letter-spacing:.6px`; nombre de jugador 12.5 SemiBold; línea de ELO 11.

**Medidas.** Barra de título 38 con **138 px reservados** al final para los botones nativos · relleno 14 · columna izquierda 452 fija, chat flexible · **fila de jugador 48 de alto útil** · avatar 26 · botón primario 42 · secundarios 34 · acción de fila 28.

**Regla de etiqueta.** Ni la etiqueta del panel, ni `HOST`, ni el estado, ni las cifras de cabecera se parten en dos líneas ni se recortan, tampoco traducidas al inglés. Lo único que se recorta con elipsis es el nombre del jugador y el de la sala.

## Datos

Antes de implementar, verifica y dime:

- **Cómo se construye hoy el panel de jugadores** y de dónde sale su alto. Si es un `ScrollViewer` con `Height` fijo, quitar el alto puede ser todo el arreglo.
- **Si `WsRoomState` trae el número de plazas** además de los jugadores presentes. Hace falta para dibujar las plazas libres; si no lo trae, sale del formato (1v1 → 2, 2v2 → 4, 3v3 → 6).
- **Si el ELO del rival está disponible en el lobby.** En la captura solo aparece el propio.
- **Si la regla de los cinco minutos aplica en 2v2 y 3v3** o solo en 1v1. El texto de la nota depende de eso.
