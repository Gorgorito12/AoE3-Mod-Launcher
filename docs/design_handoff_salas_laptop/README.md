# Handoff: Multiplayer › Rooms en pantallas de laptop (turno 36)

## Overview
En ventanas de laptop (~1280×720 – 1440×900) la pestaña **Multiplayer › Rooms** gasta casi todo el alto en cabeceras y en "Community activity"; la lista de salas solo recibe ~120 px de 906 (cabe 1 sala). Este cambio introduce un **modo compacto** que da a la lista de salas casi todo el alto: caben 6 salas a la vista.

Hay dos variantes para la actividad de la comunidad. **Implementa una sola** (pregunta al usuario cuál si no lo indica):
- **36a** – La actividad se pliega a una franja de 44 px bajo la lista, desplegable.
- **36b** – La actividad pasa a una pestaña **Activity** en el panel lateral (junto a Chat y Players).

## About the Design Files
`salas-laptop-36.html` es una **referencia de diseño en HTML** (mockup estático), no código de producción. Recréalo en el entorno existente del launcher usando sus componentes, estilos y patrones actuales. Los datos de salas/chat del mockup son de ejemplo.

## Fidelity
**Alta fidelidad** en layout, medidas y jerarquía. Colores y tipografía siguen la paleta actual del launcher; si el código ya tiene tokens equivalentes, úsalos en lugar de los hex de abajo.

## Cuándo se activa
- Modo compacto cuando la ventana mide **< ~900 px de alto o < ~1500 px de ancho**.
- Por encima, se mantiene el layout actual sin cambios.
- Debe reaccionar en vivo al redimensionar la ventana.

## Cambios comunes (36a y 36b)

### 1. Una sola fila de cabecera (ahorra ~66 px)
Barra de título de **40 px** (`#0b1524`, borde inferior `inset 0 -1px 0 rgba(130,175,255,.08)`), izquierda → derecha:
- Icono 18×18 (radio 5) + "AoE3 Mod Launcher" (Segoe UI 600 12.5px `#dce7f5`) + chip versión `v1.0.14` (500 10.5px `#8ea4c0`, borde 1px `rgba(130,175,255,.2)`, radio 4).
- Pestañas principales **MULTIPLAYER / LIBRARY / WORKSHOP** dentro de la barra de título: 700/600 11.5px, letter-spacing .6px, padding 0 12px, alto completo 40px. Activa: `#fff` + subrayado inferior 2px `#2f7fe0`. Inactivas: `#93a7c3`.
- Espaciador flexible (la zona libre sigue siendo arrastrable para mover la ventana).
- Botón actualización: alto 26, radio 6, fondo `#e6c06a`, texto `#2a1d05` 600 11.5px, "↻ Update v1.0.14s".
- Chip conexión: alto 26, píldora, fondo `rgba(53,196,111,.1)`, borde `rgba(53,196,111,.3)`, punto 7px `#4fd68a`, "Connected" `#8fe0b0` 600 11.5px + "▾". **La IP ya no se muestra en línea**: va dentro del desplegable.
- Campana de notificaciones.
- Usuario: avatar 24px + nombre 600 12px `#e4ecf6` + ELO en chip (600 10.5px `#8fb6ea`, fondo `rgba(47,127,224,.16)`, radio 4). Se elimina la segunda línea "Colonial · 1383 ELO" (el rango sigue disponible en hover/menú).
- Controles de ventana 44px de ancho cada uno.

### 2. Sub-barra de Rooms más ligera (46 px)
Fondo `#12213a`, borde inferior `inset 0 -1px 0 rgba(130,175,255,.1)`, padding 0 12px, gap 10px.
- Sub-pestañas: Rooms (activa: 600 13px `#fff`, fondo `rgba(255,255,255,.08)`, radio 7, padding 8px 12px) · Tournaments · Ranking · Statistics (500 13px `#93a7c3`).
- Buscador 300×32, radio 7, fondo `#0d1828`, borde `rgba(130,175,255,.16)`. Placeholder: **"Search room, mod, player or paste a code"**.
  - **El campo "room code" separado + botón enviar desaparecen.** Si el texto pegado/escrito coincide con el formato de código de sala (p. ej. `SJMD9J6W`), mostrar como primer resultado "Join room SJMD9J6W" y unirse con Enter.
- **Refresh** pasa a botón de icono 32×32 (↻), tooltip "Refresh".
- **"Help connecting" sale de esta barra** → opción dentro del desplegable del chip "Connected" (junto a la IP).
- Botón primario "+ Create room": 32 alto, padding 0 15px, `#2f7fe0`, 600 12.5px blanco.

### 3. Lista de salas
Contenedor principal: padding 12px, gap 12px, fondo `#0f1c2e`. Columna central `flex:1`, panel lateral **300 px** (antes ~350).

- Cabecera de lista (24 px): "Active rooms" 600 14px `#e8eef6` + contador píldora + a la derecha "Updated just now" 400 11px `#5f7592`.
- Cabecera de columnas: 600 10px, letter-spacing .6px, `#5f7592`, margen superior 8px. ROOM · HOST · PLAYERS ⇅ · PING ⇅.
- Grid de columnas (cabecera y filas): `34px minmax(0,1fr) 190px 78px 52px 92px`, gap 0 12px, padding 0 12px.
- **Fila de sala: 52 px de alto** (antes 76), radio 8, fondo `#12213a`, borde `inset 0 0 0 1px rgba(130,175,255,.08)`, separación 6px.
  - Icono del mod 30×30 radio 7.
  - Nombre: 600 13.5px `#eef3f9`, una línea con ellipsis.
  - Segunda línea (margen 4px): etiqueta modo (700 9.5px; COMPETITIVE `#e6c06a` sobre `rgba(230,192,106,.14)`, CASUAL `#8fb6ea` sobre `rgba(47,127,224,.14)`, radio 3) + "Mod · Mapa" 400 11px `#8ea4c0` con ellipsis.
  - Host: avatar/insignia 22px + nombre 500 12.5px `#cdd9e9` (ellipsis) + ELO monoespaciado 11px `#8ea4c0`.
  - Players: "4/4" 600 12.5px + barras de ocupación (3px alto, 10px ancho o 7px si >4 plazas, gap 3; ocupada `#2f7fe0`, libre `rgba(130,175,255,.18)`).
  - Ping 600 12px: <60 ms `#4fd68a`, <120 `#e6c06a`, resto `#e07a5f`.
  - Acción 30px alto, radio 7: **Join** (fondo `rgba(47,127,224,.2)`, borde `rgba(90,160,255,.4)`, texto `#dce9fb`) o **In game** (sin fondo, borde `rgba(130,175,255,.16)`, texto `#8ea4c0`).
- La lista ocupa todo el alto disponible y hace scroll interno; no debe quedar hueco entre la lista y lo de abajo.

### 4. Panel lateral (300 px)
Radio 8, fondo `#12213a`, borde `rgba(130,175,255,.11)`. Pestañas arriba (padding 7px, 600/500 12px, activa con fondo `rgba(255,255,255,.08)` radio 6) con contadores en píldora. Chat: mensajes con avatar 24px, nombre 600 12px, hora 10.5px `#5f7592`, texto 12.5px/1.45 `#b9c9de`; separadores de día; respuestas rápidas; input 34px + botón enviar 34×34.

## Variante 36a — franja de actividad
- Debajo de la lista, gap 10px, **franja de 44 px** (radio 8, fondo `#12213a`, borde `rgba(130,175,255,.08)`, padding 0 14px, gap 14px, separadores verticales 1×20 `rgba(130,175,255,.14)`):
  `COMMUNITY` (600 10px .6px `#61779a`) · mini-gráfico de horas (14 barras de 3px, 18px alto; horas pico `#2f7fe0`, resto `rgba(47,127,224,.4)`) + "Busiest **17:00–20:00**" · "**111** matches · 30 d" · punto verde + "**Kaiser** beat **El Taita** · 36 min ago" (ellipsis, es el elemento que se encoge) · "#1 **Aluclown** 1605" · a la derecha "Show activity ▴" (600 11.5px `#8fb6ea`).
- Texto 400 11.5px `#8ea4c0`, valores en negrita 600 `#cdd9e9`.
- **"Show activity"** despliega hacia arriba el bloque completo actual (Peak hours / Community matches / Ranking) superpuesto sobre la parte baja de la lista; "Hide activity ▾" lo pliega. Recordar el estado (persistido).
- Panel lateral: Global chat · Players (sin cambios funcionales).

## Variante 36b — pestaña Activity
- La columna central es **solo** la lista de salas (sin bloque de actividad).
- Panel lateral con tres pestañas: **Chat** (contador de no leídos cuando no está activa) · **Players** (9) · **Activity**.
- Contenido de Activity (padding 14px, gap 18px entre bloques), cabeceras 600 10px .6px `#61779a` con "See all" 500 11px `#8fb6ea`:
  1. PEAK HOURS: barras 34px alto a todo el ancho + "More people around **17:00–20:00**" 12px `#b9c9de` + "221 rooms in 30 days · your local time" 10.5px `#5f7592`.
  2. COMMUNITY MATCHES: 3 entradas de 2 líneas (12px / 10.5px, ellipsis) con tiempo a la derecha; punto verde si hay resultado, gris si no.
  3. RANKING: top 5, filas de 20px (posición, avatar 20px, nombre 12px, ELO monoespaciado 11.5px). Posiciones 1–3 en `#e6c06a`.
- Llegada de un mensaje mientras Activity está abierta → incrementa el contador de Chat.

## State
- `isCompact` (derivado del tamaño de ventana, con listener de resize).
- 36a: `activityExpanded: boolean` (persistido).
- 36b: `sideTab: 'chat' | 'players' | 'activity'`, `unreadChat: number` (se resetea al abrir Chat).
- Buscador: detección de código de sala en el texto.

## Design tokens
- Fondos: `#0b1524` (título), `#12213a` (barras/tarjetas), `#0f1c2e` (contenido), `#0d1828` (inputs).
- Texto: `#eef3f9`, `#e8eef6`, `#cdd9e9`, `#b9c9de`, `#8ea4c0`, `#6d829d`, `#5f7592`, `#61779a`.
- Acento `#2f7fe0`; enlaces `#8fb6ea`; éxito `#4fd68a`; aviso/competitivo `#e6c06a`; error `#e07a5f`.
- Bordes: `rgba(130,175,255,.08–.2)` como `inset box-shadow` de 1px.
- Radios: 3, 4, 6, 7, 8. Alturas de control: 26 (título), 30 (filas), 32 (barra), 34 (chat).
- Fuente: Segoe UI / system-ui; números de ELO en Consolas/monospace.

## Files
- `salas-laptop-36.html` — mockups 36a y 36b a 1280×720 (abrir en navegador).
- `AppIcon.png` — icono usado en la barra de título del mockup.
