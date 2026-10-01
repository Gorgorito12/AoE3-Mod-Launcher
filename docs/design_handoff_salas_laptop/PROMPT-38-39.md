# Prompt para Claude Code: Multiplayer › Rooms en pantallas de laptop (turnos 38 y 39)

Esto **sustituye** lo pedido en el turno 36. Corrige la implementación actual (`MultiplayerTab.Compact.cs`, `Services/CompactLayout.cs`, `MultiplayerTab.xaml`, `TitleBar.xaml(.cs)`).

Referencias visuales (abrir en navegador):
- `salas-laptop-38-39.html`: **38a** (pocas salas), **38b** (muchas salas), **39a** (actividad plegada) y **39b** (después de pulsar Show activity).
- `referencia-38b.png`: captura del resultado esperado con la actividad abierta.

## Principio
En pantallas pequeñas la interfaz debe ser **fiel a la versión grande**: los mismos bloques, en el mismo orden y con el mismo contenido. Lo único que cambia entre tamaños es **cuánto cabe** en cada bloque (alturas un poco menores, menos filas visibles, scroll), **nunca qué bloques se ven**.

## Correcciones sobre lo implementado

### 1. Recuperar las tres barras superiores (no fusionar ni ocultar)
La implementación del turno 36 metió MULTIPLAYER / LIBRARY / WORKSHOP en la barra de título y quitó contenido. Hay que revertirlo. En modo compacto se mantienen las tres barras, solo con menos alto:

- **Barra de título, 34 px** (`#0b1524`): icono 16 px · "AoE3 Mod Launcher ▾" · chip de versión `v1.0.14` · espacio flexible arrastrable · botón "↻ Update v1.0.14s" (24 px de alto, `#e6c06a`, conserva el número de versión) · campana · controles de ventana de 44 px cada uno.
- **Navegación principal, 42 px** (`#0e1a2c`, línea inferior 1 px `rgba(130,175,255,.08)`): MULTIPLAYER / LIBRARY / WORKSHOP (700/600 12px, letter-spacing .6px, padding 0 18px; la activa con subrayado de 2 px `#2f7fe0`) · espacio flexible · chip "Connected ▾" · avatar 26 px + "gorgorito_12" + segunda línea "Colonial · 1383 ELO" (10.5px `#8fb6ea`).
- **Sub-barra Rooms, 44 px** (`#12213a`): Rooms / Tournaments / Ranking / Statistics · buscador 300×32 ("Search room, mod, player or paste a code") · refresh 32×32 · "+ Create room".

Con WORKSHOP en su propia barra desaparece el solape con el botón Update y el chip de usuario.

### 2. La actividad nunca se oculta ni se superpone
Hoy, `PlaceActivityStrip` pliega la actividad siempre que `CompactLayout.IsCompact` es verdadero, y al desplegarla la pone en `ActivityOverlayHost`, que tapa la lista. Hay que cambiarlo:

- **No usar `ActivityOverlayHost`** en ningún estado. La actividad siempre va en línea, en `ActivityInlineHost`, debajo de la lista y dentro de la columna central.
- La columna central es un Grid de 2 filas con 14 px de separación:
  - **Pocas salas (38a):** la fila de salas es `Auto` (mide lo que ocupan sus filas) y la de actividad es `*`, que llena todo el alto restante. No quedan huecos vacíos.
  - **Muchas salas (38b / 39b):** la fila de salas es `*` con `ScrollViewer` interno, y la de actividad tiene **248 px** de alto.
  - **Regla:** si la lista deja menos de 248 px para la actividad, se pasa al modo "muchas salas".
- La decisión depende del **contenido**, no solo del tamaño de ventana. Se recalcula al cambiar el número de salas o al redimensionar la ventana.

### 3. Show activity / Hide activity (39a ↔ 39b)
- **Plegado (39a):** franja de 44 px bajo la lista (radio 8, `#12213a`). Contenido, en este orden: `COMMUNITY` · mini-gráfico de 24 h + "Busiest **17:00–20:00**" · última partida ("**Geaf_Argento** beat **aoe** · 3 h") · "**114** matches · 30 d" · a la derecha, el botón **"Show activity ▴"** (28 px de alto, fondo `rgba(47,127,224,.16)`, borde `rgba(90,160,255,.45)`, texto `#dce9fb`).
  - En la franja, cada segmento tiene **ancho fijo**: nada se recorta a "E…". Hoy la última partida es la única columna en estrella de `FillActivityBar`, y por eso es la que se recorta. Si falta espacio, se ocultan segmentos enteros por prioridad: primero "114 matches", luego la última partida.
- **Desplegado (39b):** el panel **crece hacia arriba** de 44 a 248 px, ocupa **todo el ancho de la columna central** (alineado con los bordes de las filas de salas) y la lista se encoge con scroll. El chat y las barras superiores no se mueven.
  - Cabecera: "Community activity" (600 14px `#e8eef6`). A la derecha, "**114** matches (30 d) · **26** players (7 d) · Most played: **ESOC Malaysia**" y después **"Hide activity ▾"** como **enlace de texto** (600 11px `#8fb6ea`, sin caja), para no aumentar el alto de la cabecera.
- Se recuerda el estado elegido por el usuario (persistido). Por defecto: desplegado si caben ≥ 248 px; si no, plegado.

### 4. Tarjetas de la actividad
Tres tarjetas lado a lado en un Grid con columnas `0.8* / 1.5* / 1*`, gap 10 px, que ocupan todo el alto restante del panel. Cada una: radio 8, fondo `#12213a`, borde `rgba(130,175,255,.09)`, padding 12px 14px. Títulos en 600 10px, letter-spacing .6px, `#61779a`; "See all" en 500 11px `#8fb6ea`.

- **PEAK HOURS:** las 24 barras quedan abajo y se estiran a todo el ancho de la tarjeta (34 px de alto con 248 px de panel; más altas si el panel crece). Eje "0h / 12h / 23h" en monoespaciado 9.5px. Debajo, "More people around **17:00–20:00**" (12.5px) y "224 rooms in 30 days · your local time" (10.5px `#5f7592`).
- **COMMUNITY MATCHES:** cada partida ocupa **2 líneas fijas**, sin saltos de línea.
  - Línea 1: punto de 6 px (`#4fd68a` si hay resultado, `#4a5d78` si no) + jugadores con **bandera** de 14×10 y nombre, separados por "vs" o "beat" + tiempo a la derecha.
  - Línea 2: "**COMPETITIVE** (`#c9a752`) · resultado/modo · mapa · duración" con ellipsis.
  - El **nombre de la civilización no va en línea**: va en el tooltip de la bandera. Así se arreglan las tarjetas de 10–12 líneas.
  - Se muestran las partidas que quepan completas, **nunca una cortada a medias**: 4 con 248 px, más si hay sitio.
- **RANKING:** filas de 30 px (posición con el top 3 en `#e6c06a`, avatar de 22 px, nombre con ellipsis, ELO en monoespaciado 12px). Se muestran las que quepan completas (5 con 248 px, 9 en 38a).

### 5. Lista de salas (sin cambios respecto a 36, se confirma)
Filas de 54 px. Columnas `34px / * / 200px / 78px / 52px / 92px`. Cabecera "Active rooms" + contador + "Updated just now". Cabecera de columnas ROOM ⇅ · HOST · PLAYERS ⇅ · PING ⇅. Con muchas salas, scroll interno.

### 6. Panel lateral
Chat de 300 px, sin cambios en ningún estado.

## Criterios de aceptación
- A 1380×860 con 1 sala, se ve como **38a**: tres barras, una sala y la actividad llenando el resto, sin huecos.
- A 1380×860 con 8 salas, se ve como **39a** al estar plegada y como **39b** / `referencia-38b.png` al pulsar Show activity.
- Siempre se ven las tres barras superiores con todo su contenido, y nada se solapa con WORKSHOP.
- Ningún elemento se superpone a la lista de salas.
- Ninguna partida ni fila del ranking aparece cortada a medias, y ningún texto de la franja se recorta a "E…".
- Al redimensionar la ventana en vivo cambia cuánto cabe en cada bloque, nunca qué bloques se ven.
