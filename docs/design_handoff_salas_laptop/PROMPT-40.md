# Prompt para Claude Code: Community activity con tope fijo (turno 40)

Referencia visual: `salas-laptop-40.html` (abrir en navegador): **40a** sin salas, **40b** con muchas salas.

## Problema
Con pocas salas o ninguna, el panel abierto entra en el modo `RoomsActivityMode.Fill`: crece hasta ocupar toda la columna y `FitStackPanel` llena las tarjetas con 12 partidas y 15 puestos de ranking. Ocupa demasiado espacio. Además, la etiqueta dice solo "COMPETITIVE" y el formato (1v1 / 2v2) aparece a veces más adelante en la línea y a veces no aparece.

## Cambios

### 1. Panel de alto fijo, sin modo Fill
- **Quitar `RoomsActivityMode.Fill`.** El panel abierto mide siempre `RoomsActivityLayout.ExpandedHeight` (248 px), haya 0, 1 u 8 salas.
- La fila de salas es siempre `*`, y la de actividad es `Auto` con 248 px (abierta) o 44 px (franja plegada).
- Con pocas salas, el espacio sobrante queda en la lista de salas, sin rellenar. Las salas tienen prioridad.
- `Decide` queda reducido a dos casos: `Folded` y `Fixed`, más `None` si no hay datos. La regla por defecto de `IsExpanded` se mantiene.
- Ajustar los tests de `RoomsActivityLayout` que esperaban `Fill`.

### 2. Tope de elementos
- **COMMUNITY MATCHES:** como máximo **4** partidas. Se aplica el tope (`Take(4)`) **antes** de añadirlas al `FitStackPanel`, que se sigue usando para no cortar ninguna a medias si a 125 % de texto caben menos.
- **RANKING:** como máximo el **top 5**, con el mismo criterio.
- Si el jugador actual no está en el top 5, **no** se añade su fila al panel; su puesto se ve en "See all".
- "See all" abre las pestañas Ranking e historial completo, como ahora.

### 3. Etiqueta de modo con formato
- Línea 2 de cada partida: **"COMPETITIVE 1v1"**, **"COMPETITIVE 2v2"**, **"COMPETITIVE 3v3"**, **"COMPETITIVE FFA"**… (y "CASUAL 2v2", etc., si aplica), todo en el mismo estilo dorado SemiBold de ahora.
- El formato sale del número de participantes por equipo (`m.Participants`). Si no se puede determinar, se muestra solo "COMPETITIVE".
- **Quitar el "1v1" suelto** que hoy viene después de la etiqueta. Resto de la línea: `· resultado (si no hay ganador: "no result") · mapa · duración`.
- Ejemplos:
  - `COMPETITIVE 1v1 · vividlyAlberta · 35 min`
  - `COMPETITIVE 2v2 · no result · ESOC Manchac · 26 min`
- Corregir de paso el texto "no result read" → "no result" (clave de `Strings`).
- Usar la misma regla en la etiqueta de la franja plegada, si aparece.

### 4. PEAK HOURS
- "More people around 17:00–20:00" se corta a "1…" cuando la columna es estrecha. Cambiar a `TextWrapping="Wrap"` (máximo 2 líneas) en lugar de ellipsis, y lo mismo para "227 rooms in 30 days · your local time".

## Criterios de aceptación
- Con 0 salas, el panel abierto mide 248 px y la lista de salas ocupa el resto (como 40a).
- Con 8 salas, se ve igual que 40b.
- Nunca se muestran más de 4 partidas ni más de 5 puestos de ranking en el panel.
- Cada partida dice "COMPETITIVE NvN" y el formato no aparece repetido en la misma línea.
- El texto de PEAK HOURS no se trunca.
