# Handoff 63 · Descargar grabaciones

Prototipo: `Grabaciones.dc.html` (abre con `ReplayRow.dc.html` y `support.js` en la misma carpeta). Variantes 63a–63f.

## Decisiones

- **El botón va en la columna derecha que la fila ya tiene**, debajo de la antigüedad. Esa columna ocupa las dos líneas y su ancho lo fija el texto de la antigüedad («7 d ago», «hace 7 d»), que siempre mide más que el botón. Los nombres no pierden ancho y la fila sigue midiendo 50 px.
- **Sin grabación, no hay botón.** Las casuales, las que no tienen resultado y las competitivas sin grabar dejan el hueco vacío. Las caducadas muestran el texto «caducada», sin hover ni clic.
- **El mod y la versión no van en la fila**: van en el tooltip y en el aviso.
- **Solo en Clasificación.** La fila compacta de Salas › Actividad de la comunidad no cambia.
- **Un año de partidas va en una vista nueva, «Partidas» / «Matches»**, la cuarta opción del selector de modo de Clasificación (1v1 · Equipos · Destacados · Partidas). El panel «Últimas partidas» sigue con las 30 más recientes y termina en «Todas las partidas →», que abre esa vista.

## Fila (ReplayRow)

| Elemento | Medida |
|---|---|
| Alto de fila | 50 px (padding 8 arriba y abajo; contenido 34) |
| Columnas | punto 6 · 9 · contenido `*` (MinWidth 0) · 9 · columna derecha Auto (MinWidth 44) |
| Punto | 6 × 6, radio 3, margen superior 6. Con resultado `#4fd68a`; sin resultado `#5d6f8a` |
| Línea 1 | 17 px de alto, Segoe UI 13. Nombres SemiBold `#f0f5fb` / `#dce7f5`, verbo `#8C9CB1`. Ambos nombres con TextTrimming CharacterEllipsis |
| Bandera | 18 × 12, radio 2, borde interior 1 px `rgba(255,255,255,.14)`, separación 6 |
| Línea 2 | 14 px de alto, Segoe UI 11 `#8C9CB1`, sangría 24, una línea con elipsis. Modo Bold: COMPETITIVE `#E6C06A`, CASUAL `#A8BCD2` |
| Separación entre líneas | 3 px |
| Columna derecha | antigüedad arriba (11 px, `#8C9CB1`, alineada a la derecha), botón abajo, alineado a la derecha. **La columna mide 38 px y baja 4 px dentro del padding inferior** (Margin 0,0,0,-4), de modo que quedan 4 px entre el texto (incluido el rabo de la «g» de «ago») y el botón. El botón queda a 4 px del borde inferior de la fila |

## Botón

Border de 20 × 20, CornerRadius 10. Icono Path de 10 × 11, trazo 1,6.

| Estado | Fondo / borde | Icono | Clic |
|---|---|---|---|
| Normal | `#2f7fe0` al 16 % | flecha ↓ `#8cbcf5` | descarga |
| Hover | `#2f7fe0` | flecha ↓ blanca | descarga |
| Descargando | anillo: pista `#2f7fe0` al 22 % y avance `#2f7fe0`; centro 14 px del color del panel | — | desactivado |
| Descargada | borde 1 px `#4fd68a` al 55 %, sin fondo | carpeta `#4fd68a` | muestra el archivo (`FileReveal.Reveal`, como en `MatchResultCard`) |
| Error | borde 1 px `#8C9CB1` al 60 % | flecha circular `#dce7f5` | reintenta |
| Caducada | sin botón: texto «expired» / «caducada», 10,5 px `#8C9CB1` | — | ninguno |

- **Descargando**: si la respuesta trae Content-Length, el anillo se llena según el avance. Si no, un arco del 25 % gira a 1 vuelta/s.
- **Descargada**: se recuerda por id de partida (índice local id → ruta). Si el archivo ya no existe en disco, el botón vuelve a Normal.
- **Caducada**: cuando la fila ya llega caducada, o cuando el clic recibe un 404 (en ese caso, también sale el aviso «Esta grabación ya no está»).

### Tooltip

Fondo `#1a2c48`, borde interior 1 px `rgba(130,175,255,.22)`, radio 6, padding 9 × 11, ancho máximo 250. Título SemiBold 12 `#f0f5fb`, texto 11,5 `#b9c9dc`.

| Estado | EN | ES |
|---|---|---|
| Normal / hover | **Download replay · {size}**<br>Saved to {mod}'s Savegame. Plays only with {mod} {version}. | **Descargar grabación · {size}**<br>Se guarda en Savegame de {mod}. Solo se reproduce con {mod} {version}. |
| Descargando | **Downloading…** | **Descargando…** |
| Descargada | **Already in your Savegame folder**<br>Click to show it. Open it in AoE3 › Load saved game. | **Ya está en tu carpeta Savegame**<br>Haz clic para mostrarla. Ábrela en AoE3 › Cargar partida grabada. |
| Error | **Couldn't download**<br>No connection to the server. Click to retry. | **No se pudo descargar**<br>Sin conexión con el servidor. Haz clic para reintentar. |

`{size}` con un decimal: «1.4 MB» / «1,4 MB».

## Aviso (AppToast)

Esquina inferior derecha, 360 px de ancho, padding 14 · 14 · 14 · 16, radio 8, fondo `#12213a`, borde interior 1 px `rgba(130,175,255,.22)`, sombra 0 12 32 al 45 %. Columnas: icono 24 · 12 · texto `*` · 12 · cerrar 16. Icono: círculo de 24 (radio 12). Título SemiBold 13,5 `#f0f5fb`; partida 12 `#8C9CB1`, una línea con elipsis; texto 12,5 `#c3d2e5`; botón 28 de alto, radio 6, `#2f7fe0`, texto SemiBold 12 blanco, margen superior 6.

Se cierra solo a los 8 s, salvo el de error.

| Caso | Icono | EN | ES | Botón |
|---|---|---|---|---|
| Guardada | ✓ `#4fd68a` sobre verde al 16 % | **Replay saved**<br>Open it in AoE3 › Load saved game, with {mod} {version}. | **Grabación guardada**<br>Ábrela en AoE3 › Cargar partida grabada, con {mod} {version}. | Show in folder / Mostrar en la carpeta |
| Otra versión instalada | i `#8cbcf5` sobre `#2f7fe0` al 20 % | **Replay saved**<br>Played on {mod} {version} and you have {installed}. It may not play correctly. | **Grabación guardada**<br>Se jugó con {mod} {version} y tienes la {installed}. Puede que no se reproduzca bien. | ídem |
| Mod no instalado | i | **Replay saved**<br>Played on {mod}, which isn't in the launcher. Saved to its Savegame folder. | **Grabación guardada**<br>Se jugó con {mod}, que no está en el launcher. Se guardó en su carpeta Savegame. | ídem |
| Error | ! `#dce7f5` sobre `#8C9CB1` al 20 % | **Couldn't download the replay**<br>No connection to the server. Try again in a moment. | **No se pudo descargar la grabación**<br>Sin conexión con el servidor. Inténtalo de nuevo en un momento. | Retry / Reintentar |
| Caducó (404) | ! | **This replay is gone**<br>Replays are kept for one year and then deleted. The match stays in the list. | **Esta grabación ya no está**<br>Las grabaciones se guardan un año y después se borran solas. La partida sigue en la lista. | — |

La línea de partida es «{A} vs {B} · {mapa}». El segundo clic en una grabación descargada no muestra aviso.

Carpeta: `My Games\<mod>\Savegame`, la misma de la que el launcher ya lee las grabaciones. Si ya existe un archivo con ese nombre, no se sobrescribe: se añade « (2)».

## Vista Partidas

| Elemento | Medida |
|---|---|
| Barra de filtros | panel `#12213a`, radio 10, padding 10 × 12, separación 10, WrapPanel |
| Buscador | 32 de alto, radio 6, fondo `#0f1c2e`, borde 1 px `rgba(130,175,255,.22)` (con texto: `#2f7fe0`), ancho 340 (mín. 200). Lupa 13 px. ✕ para borrar. Busca con 300 ms de espera tras teclear |
| «Solo con grabación» | píldora de 32, **radio 16**, casilla de 14 (radio 3). Apagada: borde `rgba(130,175,255,.22)`. Encendida: fondo `#2f7fe0` al 18 %, borde `#2f7fe0`, casilla `#2f7fe0` con ✓ |
| Mod | ComboBox de 32, radio 6: «All mods» / «Todos los mods» y los mods con partidas |
| Contador | a la derecha, 12 `#8C9CB1` |
| Grupos | por mes, título SemiBold 11 `#c3d2e5` + «{n} partidas» 11 `#8C9CB1`. Cada grupo es un panel `#12213a`, radio 10, padding 2 × 16 |
| Columnas de filas | 1 por debajo de 900 px de ancho; 2 hasta 1900; 3 desde 1900. Separación 14. Orden por filas |
| Línea 2 | añade « · {mod}» al final (aquí se mezclan mods) |
| Antigüedad | «hace N h» / «hace N d» hasta 30 días; después «28 sep»; con más de un año, «sep 2025» |
| Cargar más | 34 de alto, radio 6, fondo `#2f7fe0` al 16 %, borde `#2f7fe0` al 45 %, texto SemiBold 12,5 `#dce7f5`, centrado. A su lado, «Mostrando {n} de {total}» |
| Fin de la lista | «That's every match for this search.» / «Esas son todas las partidas de esta búsqueda.» |
| Más de un año | grupo «OLDER THAN ONE YEAR» / «MÁS DE UN AÑO», título en `#8C9CB1`, nota «Replays are kept for one year and then deleted.» / «Las grabaciones se guardan un año y después se borran solas.» |

Textos EN / ES: «Search player…» / «Buscar jugador…» · «Only with replay» / «Solo con grabación» · «All mods» / «Todos los mods» · «{n} matches in the last 12 months» / «{n} partidas en los últimos 12 meses» · «Load 30 more» / «Cargar 30 más» · «Showing {n} of {total}» / «Mostrando {n} de {total}» · «All matches →» / «Todas las partidas →».

Sin resultados: título «No matches for “{q}” in the last 12 months» / «Ninguna partida de «{q}» en los últimos 12 meses» (con el filtro, «No matches with a replay…» / «Ninguna partida con grabación…»). Texto: «Check the spelling, or turn off “Only with replay” to see unrecorded matches too.» / «Revisa cómo se escribe el nombre, o quita «Solo con grabación» para ver también las partidas sin grabar.» Botón «Clear filters» / «Quitar filtros».

## Datos que necesita del servidor

- Por partida: `has_replay` (ya existe) y `replay_expires_at`. El launcher lo deduce así: si `has_replay` y todavía no venció, Normal; si `has_replay` y ya venció, Caducada; si no hay `has_replay`, sin botón. Además: `replay_size` (bytes), `mod` y `mod_version` de la partida. Sin `mod_version`, el tooltip y el aviso nombran solo el mod.
- Ruta paginada con cursor: `GET /matches?cursor=&limit=30&q=&replay=1&mod=` → `{ items, next_cursor, total }`. `total` es opcional: sin él, el contador se quita y queda solo «Cargar 30 más».
- Descarga: `GET /matches/{id}/replay`. Debe devolver Content-Length y 404 cuando la grabación ya caducó.
