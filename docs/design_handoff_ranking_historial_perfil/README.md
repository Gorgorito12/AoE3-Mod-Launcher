# Clasificación · Historial · Perfil

Rediseño de las tres pestañas del Multijugador que quedaron sin estilo en el launcher. Referencias visuales: **3a** (Clasificación), **3b** (Historial) y **3c** (Perfil) en `Clasificacion Historial Perfil.html` — ábrelo en un navegador y haz zoom para leer los detalles.

Repo: `Gorgorito12/AoE3-Mod-Launcher`, rama `main`, proyecto WPF `WarsOfLibertyLauncher/`. Archivos afectados: `Controls/MultiplayerTab.xaml.cs` (+ su `.xaml`), la sección de pestañas Rooms/Friends/Profile/History/Ranking.

El HTML es una **referencia de diseño**, no código para copiar: recrea las pantallas en WPF/XAML con los estilos y controles que ya existen en el proyecto. Todo el texto pasa por `Localization/Strings.cs` (español e inglés); nada de cadenas literales en el XAML.

## Tokens

| Uso | Hex |
| --- | --- |
| Fondo de la pestaña | `#0f1c2e` |
| Barra de sub-pestañas / tarjeta | `#12213a` |
| Tarjeta atenuada (partida que no contó) | `#101d31` |
| Cabecera de perfil | degradado `#1b2e4c` → `#16263e` |
| Borde interior sutil | `rgba(130,175,255,.07–.15)` |
| Texto primario | `#e8eef6` · titulares `#f0f5fb` / `#f4f8fc` |
| Texto secundario | `#b9c9de` / `#c3d2e5` / `#dce7f5` |
| Texto atenuado | `#8ea4c0` → `#6d829d` → `#5f7592` |
| Etiqueta de sección | `#61779a` |
| Azul de acción | `#2f7fe0` · texto sobre oscuro `#8cbcf5` / `#8fb6ea` |
| Verde victoria | `#4fd68a` · texto `#8fe0b0` / `#7fdca6` |
| Rojo derrota | `#c8686e` · texto `#d99a9a` |
| Ámbar aviso / provisional | `#e6b455` · texto `#d8bd8a` / `#e0be7f` / `#f0dcae` |
| Gris sin resultado | `#4a5a72` |
| Oro puesto 1 | `#f4d9a0` |

**Tipografía**: UI en Segoe UI. Titulares y cifras grandes en serif (`Source Serif 4` en el prototipo; en WPF, Georgia o la serif que ya usa el título dorado). Datos numéricos (rating, deltas, récords, porcentajes) en **monoespaciada** — es lo que mantiene las columnas alineadas. Escala: 9.5 · 10.5 · 11 · 11.5 · 12 · 12.5 · 13 · 13.5 · 15 · 17 · 20 · 30 px. Etiquetas de sección 9.5–10.5 px, 600, `letter-spacing` .6 px, mayúsculas.

**Espaciado**: 3 · 4 · 6 · 7 · 8 · 9 · 10 · 11 · 12 · 13 · 14 · 16 · 18 px. **Radios**: 4 (etiquetas) · 7 (botones) · 8 (filas, celdas) · 9–10 (tarjetas) · 14 (avatar de perfil) · 999 (pastillas).

## Defecto común: el contenido se estira a todo el ancho

En un monitor de 2560 px, `ELO / DECIDED / %` acaban a más de 2000 px del nombre del jugador, y la tarjeta del historial estira el nombre del rival hasta el borde. Es ilegible: hay que mover la cabeza para relacionar una fila con su valor.

**Arreglo, en las tres pestañas**: el contenido va en un contenedor de **ancho máximo acotado y alineado a la izquierda** — 820 px en Clasificación e Historial, 900 px en Perfil (`MaxWidth` + `HorizontalAlignment="Left"`, padding lateral 14 px). El espacio sobrante queda a la derecha, vacío y en paz.

Además, en las tres: título de pestaña 17/700 serif arriba (`Clasificación`, `Historial`, y en Perfil el propio nombre), y **nada de tablas dibujadas como texto suelto** — cada tabla o lista es una tarjeta (radio 9, `#12213a`, borde `rgba(130,175,255,.11)`).

---

## 3a — Clasificación

- **Conmutador 1v1 / Equipos**: pastillas dentro de una cápsula `#12213a` (padding 3), activa `rgba(47,127,224,.28)` con borde `rgba(120,180,255,.35)`. Hoy `1v1` y `TEAMS` parecen dos etiquetas sin relación.
- Filtros a la derecha: mod y ventana temporal, como pastillas fantasma.
- **Columnas**: `44px · minmax(0,1fr) · 132px · 74px · 86px · 58px` → `#` · JUGADOR · RATING · DECID. · V-D · `%`. Cabecera 10/600 `letter-spacing:.6px` `#61779a` con línea inferior.
- `DECIDED` → **`DECID.`** y se añade la columna **V-D** (`8-5`): el número de decididas sin el desglose no dice nada.
- **Rating con barra comparativa**: cifra monoespaciada 13/600 + barra de 4 px cuya longitud es relativa al primer puesto. Deja ver la distancia entre puestos sin leer números.
- `%` con color: `#7fdca6` ≥50, `#e6b455` 30–49, `#d99a9a` <30.
- Puesto 1 en dorado `#f4d9a0` (serif 13/700); el resto `#cdd9e9`.
- **Tu fila** con fondo `rgba(47,127,224,.12)` y, cuando no estás en la parte visible de la lista, **fijada al pie de la tarjeta** con una línea superior azul. Así siempre sabes dónde estás sin buscar.
- Etiqueta `PROVISIONAL` (9.5/600 `#e0be7f` sobre `rgba(230,180,85,.14)`) junto al nombre cuando `rd` es alto.
- Al pie, una línea de 11 px explicando la regla + enlace «Cómo funciona el ELO».
- Con 2 jugadores en la tabla, esto ya se sostiene; no hace falta estado vacío especial.

## 3b — Historial

- Cuatro celdas de resumen arriba (RATING con delta, DECIDIDAS, SIN CONTAR, MÁS JUGADO), `flex:1`, radio 8.
- Filtros: Todas · Puntuadas · Sin contar.
- **Agrupación por día**: separador con la fecha en 9.5/600 `letter-spacing:.7px` `#5f7592` + línea. Hoy la fecha va dentro de cada tarjeta (`29/8/2026 7:09 p.m.`), lo que la repite en cada fila.
- **Tarjeta de partida**: franja vertical de 4 px a la izquierda — `#c8686e` derrota, `#4fd68a` victoria, `#4a5a72` sin resultado. Dentro:
  - Línea 1: resultado 13.5/600 (`Derrota`) + `contra {rival}` 12/400 `#8ea4c0`. Sustituye a las dos pastillas `Loss` / `-117` de la implementación actual, que dicen lo mismo dos veces.
  - Línea 2: `{mod} · {mapa} · {duración} · {hora}` 11/400 `#6d829d`.
  - A la derecha, el delta como cifra protagonista: 17/600 monoespaciada, y debajo `1500 → 1383` en 10.5 `#6d829d`.
  - Bloque de jugadores separado por una línea interior: avatar 22 px, nombre, resultado en columna de 56 px, delta en columna de 48 px alineada a la derecha. Columnas fijas para que los deltas queden en vertical.
  - Acciones: «Ver repetición» (fantasma) y «Revancha» (fantasma azul), solo en partidas puntuadas.
- **Partida que no contó**: fondo `#101d31`, franja gris, título «Sin resultado» + etiqueta `NO CONTÓ`, delta `—`, y una caja ámbar con el motivo real y el enlace «Ver cómo». No se muestra `0.5` ni «empate».
- Estado vacío: caja igual a la de Salas — «Todavía no has jugado ninguna partida multijugador» + botón `+ Crear sala`.

## 3c — Perfil

La versión actual son cuatro líneas de texto sobre fondo vacío, y **«0 % wins» con una sola partida jugada transmite algo falso**. Estructura nueva:

- **Cabecera** (radio 10, degradado `#1b2e4c → #16263e`, borde `rgba(130,175,255,.15)`, padding 16/18): avatar 56 px radio 14; nombre 20/700 serif + etiqueta `PROVISIONAL`; línea secundaria `@usuario · se unió en {mes} · {mod}`. A la derecha: `RATING 1v1` en 9.5/600, la cifra en **30/700 serif** con el delta al lado, y `puesto 7 de 18` debajo.
- **Evolución del rating**: `Polyline` sobre los `rating_after` de `MatchHistoryRow` en orden, trazo 2 px `#2f7fe0`, punto final coloreado según el último delta. Con menos de 2 puntos, muestra «Aún no hay suficientes partidas para dibujar la curva» en lugar de una línea plana.
- **Récord**: `0-1` en 20/600 monoespaciada + «en 1 decidida», y **seis segmentos** de progreso (uno por partida decidida hasta salir de provisional) — llenos con el color del resultado. Debajo, en palabras: «Faltan **5 partidas decididas** para que el rating deje de ser provisional».
- **`0 % wins` desaparece como cifra suelta.** Con menos de 6 decididas el porcentaje no se muestra; se muestra el récord `V-D`. Nunca un 0 % gigante por una derrota.
- Tres celdas: PARTIDAS TOTALES (`3`, con «1 decidida · 2 sin contar»), MAPA MÁS JUGADO, RIVAL HABITUAL (avatar + nombre + balance contra él).
- **Últimas partidas**: tres filas compactas con franja de color de 3 px, título, mapa y hora, delta a la derecha y antigüedad en columna fija de 52 px. Enlace «Ver historial completo».
- Al ver el perfil de **otro** jugador, la cabecera gana dos botones: «Invitar a la sala» (sólido) y «Añadir a amigos» (fantasma).

## Datos

Todo sale de lo que ya existe: `EloSnapshot` (rating, `rd`, wins, losses) y `MatchHistoryRow` (`rating_before`, `rating_after`, `result`, mapa, mod, duración, participantes). El puesto y el total de jugadores, del endpoint de ranking. «Rival habitual» y «Mapa más jugado» se calculan en cliente sobre el historial ya descargado — no hace falta endpoint nuevo.
