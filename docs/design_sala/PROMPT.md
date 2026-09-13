# Texto para pegar en Claude Code

Copia esta carpeta dentro del repo (p. ej. `docs/design_sala/`), abre una terminal ahí, lanza `claude` y pega esto:

---

Lee `docs/design_sala/README.md` completo antes de escribir código. Es el arreglo del panel de jugadores de la ventana de sala (`LobbyWindow`) de este launcher WPF, más varias correcciones de texto y de reparto de espacio en la misma ventana.

`Prototipo.html` es una referencia de diseño: NO lo copies ni lo integres. Recréalo en WPF/XAML con los estilos y controles que ya existen en `WarsOfLibertyLauncher/`. **22b y 22a son la misma ventana**: 22b es el detalle del panel (hoy frente a propuesta) y 22a la ventana entera.

**No toques ninguna otra pantalla.** Salas, Crear sala, Torneos, Clasificación, Estadísticas, Taller, el asistente de Radmin y los diálogos de ajustes quedan como están.

## Antes de escribir código, hazme un plan

Este handoff se diseñó desde una captura, no leyendo el código. Empieza por ahí:

1. **Lee `LobbyWindow.xaml` y su code-behind** y dime cómo se construye el panel `PLAYERS` y **de dónde sale su alto**. Si es un `ScrollViewer` con `Height` fijo, quitar ese alto puede ser casi todo el arreglo — dímelo antes de rehacer nada.
2. **Dime si `WsRoomState` trae el número de plazas de la sala**, no solo los jugadores presentes. Lo necesito para dibujar las plazas libres. Si no lo trae, confírmame que se puede derivar del formato (1v1 → 2, 2v2 → 4, 3v3 → 6).
3. **Dime si el ELO del rival está disponible en el lobby.** En la captura solo se ve el propio.
4. **Dime si la regla de los cinco minutos aplica en 2v2 y 3v3** o solo en 1v1. El texto de la nota y la etiqueta del botón de salir dependen de eso.
5. Dime qué claves de `Localization/Strings.cs` faltan o cambian. Ojo: la frase de los mods idénticos **modifica una cadena existente**, no solo el XAML.
6. Propón commits pequeños, empezando por el alto del panel.

## Reglas al implementar

- **El panel de jugadores crece con sus plazas.** Sin alto fijo. Tope a las ocho de AoE 3; por encima desplaza, pero mostrando **filas enteras**, nunca media línea. Hoy una fila necesita 48 px y el panel le da 28, así que la línea del ELO sale partida.
- **La plaza libre es una fila**, no una ausencia: «Waiting for an opponent», el código debajo y un botón `Copy`. La acción va donde está el problema, no en la cabecera de la ventana.
- **El estado va en su propia columna**, `flex:none`, alineado a la derecha. Hoy `Host` y `waiting` se solapan porque comparten flujo. Lo único que se recorta con elipsis es el nombre.
- **La cabecera del panel dice la cuenta** (`PLAYERS · 1 OF 2`).
- **Ninguna frase interpola un recuento en una plantilla partida.** «Identical mods across all 1 players» se rompe en singular.
- **Una casilla es una tarea; una regla es una nota.** Que salir después de cinco minutos cuente como derrota no se marca. La casilla se reserva para el *Record Game*.
- **Los botones nombran su consecuencia.** «Leave room · counts as a loss», con confirmación.
- **Nada de jerga en la UI**: «P2P LAN ready» y «Copy · Wars of Liberty (2)» se dicen en lenguaje de jugador.
- **El diseño tiene que aguantar a 900×600**, que es la referencia de escalado de `LobbyWindow` (`.xaml.cs:149`). Nada de anchos fijos que solo cuadren al tamaño de la captura.
- **Reserva 138 px al final de la barra de título** para los botones nativos de Windows.
- Cero cadenas literales: todo a `Localization/Strings.cs`, español e inglés. ELO, código, ping y horas en monoespaciada.
- Respeta los valores exactos de la tabla de Tokens. Reutiliza los recursos compartidos del launcher; si creas un estilo nuevo (fila de jugador, fila de plaza libre), hazlo compartido.
- Señala cualquier punto del README que choque con la arquitectura real, con `CLAUDE.md`, con `.claude/rules/multiplayer.md` o con un test existente, en vez de forzarlo.
- Compila y pasa los tests antes de darme cada commit por terminado.

Cuando acabes el panel de jugadores, párate y enséñame una captura antes de seguir con el resto.
