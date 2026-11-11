# Texto para pegar en Claude Code

Copia esta carpeta dentro del repo (p. ej. `docs/design_salas_filas/`), abre una terminal en la raíz del repo, lanza `claude` y pega esto:

---

Lee `docs/design_salas_filas/README.md` completo antes de escribir código. La lista de salas de Multijugador › Salas se ve plana: hay que convertir cada fila en una ficha con relieve teñida con el color de su grupo (verde si puedes entrar, azul en partida, ámbar si no está disponible), dibujar los títulos SALA · ANFITRIÓN · JUGADORES · PING como una barra con fondo, y darles dibujo a las celdas de anfitrión (avatar), jugadores (un punto por asiento) y ping (barras de señal). **El diseño elegido es 104a** de `Salas filas.dc.html`; `Salas v2.dc.html` lo muestra dentro de la pestaña completa. Son prototipos de referencia: NO copies su código, cambia los estilos y el código WPF que ya existen.

**No toques nada más.** Ni qué salas se listan, ni el orden, ni los grupos, ni qué botón sale en cada caso o lo que hace, ni la tarjeta de partida rápida, el chat o la actividad. No hay cambios de servidor.

## Antes de escribir código, hazme un plan

1. **Lee cómo se dibuja hoy la lista:** el estilo `MpRoomCard` (y `MpRoomCardCompact`), `BuildRoomCard` y `BuildRoomChip`, la banda de títulos (`RoomsHeaderBand` / `RoomsHeaderStrip`), `RoomsTableLayout` y la etiqueta de grupo. Dime cómo pinta hoy cada cosa y qué pinceles usa. Si algún nombre del README ya no existe en `main`, dime cuál es el equivalente.
2. **Compara los colores del README con `Styles/Colors.xaml`** (`MpRoomJoin*`, `MpRoomInGame*`, `MpRoomFull*`). Dime cuáles reutilizas y qué pinceles nuevos propones, con su nombre.
3. **Señala los choques** con decisiones que ya estén en el código o en `CLAUDE.md`: el ancho de la columna de acción y el alto del botón, los umbrales de color del ping, el modo compacto y cualquier test que fije medidas (p. ej. `RoomActionButtonTests`). En esos casos manda el código: dime qué propones en vez de forzar el README.
4. **Dime si la sala trae el avatar del anfitrión.** Si no lo trae, va la inicial; no añadas llamadas al servidor.
5. Propón commits pequeños: pinceles → ficha (fondo, borde, relieve) → barra de títulos y etiqueta de grupo → celdas (avatar, asientos, barras de ping) → chips y botón de fila → filas de «Cargando» → modo compacto.

## Reglas al implementar

- **Colores una sola vez, como pinceles con `DynamicResource`.** Nada de hex repetido en cada fila.
- **La barra de títulos y las filas comparten las mismas columnas**: cada título tiene que caer sobre su columna a 1366, 1920 y 2560 de ancho.
- **Nada inventado:** sin avatar, la inicial; sin ping medido, barras apagadas y «—».
- Si la sombra de color por fila pesa al desplazar la lista, quítala y dímelo.
- El punto del chip «En partida» late solo si los efectos no están reducidos.
- Los dos tooltips nuevos van en `Localization/Strings.cs`, en español (es-419) e inglés.
- Prueba con las tres transparencias (Sólida, Media, Máxima) y con una sola sala, con varias de cada grupo y con la lista vacía.
- Compila y pasa los tests antes de darme cada commit por terminado.

Cuando tengas la ficha y la barra de títulos, párate y enséñame una captura de la lista con salas de los tres grupos antes de seguir con las celdas.
