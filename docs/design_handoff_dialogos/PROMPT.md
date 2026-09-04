# Texto para pegar en Claude Code

Copia esta carpeta dentro del repo (p. ej. `docs/design_dialogos/`), abre una terminal ahí, lanza `claude` y pega esto:

---

Lee `docs/design_dialogos/README.md` completo, y después las dos specs que indica. Es el rediseño de cuatro diálogos de este launcher WPF, en este orden:

1. **Crear sala y crear torneo** — `SPEC-1-crear-sala-y-torneo.md` — `CreateLobbyDialog`, `CreateTournamentDialog`, y el inicio de sesión de Discord en `GitHubLoginDialog`
2. **Asistente de Radmin** — `SPEC-2-asistente-radmin.md` — `RadminAssistantWindow`

`Prototipo.html` es una referencia de diseño: NO lo copies ni lo integres. Recrea las pantallas en WPF/XAML con los estilos y controles que ya existen en `WarsOfLibertyLauncher/`.

**No toques ninguna otra pantalla.** Salas, Lobby, Torneos, Clasificación, Taller, Estadísticas y los diálogos de ajustes quedan como están.

## Antes de escribir código, hazme un plan

1. Lee el resumen de clase de `CreateTournamentDialog.xaml.cs`. Es el criterio de todo este handoff: documenta las reglas que aplicó y el defecto que cada una reemplazó. Confírmame que las tres que cita el README están ahí.
2. Lee `CreateLobbyDialog.xaml.cs` en las líneas alrededor de 492, 636 y 747 y **dime con qué condición apaga `CreateButton`**. El README lo marca como no verificado y la spec de crear sala depende de saberlo.
3. Lee `CreateLobbyDialog.xaml` y dime su `Width=` y si usa el `TitleBar` compartido. La spec propone unificar el pie con el del torneo; necesito saber si parten del mismo control.
4. **Antes de tocar el pie del asistente de Radmin**, lee `Controls/SupportLink.cs` completo, incluida su documentación y el porqué del parámetro `captionSize`. Confírmame que la pastilla la comparten cinco ventanas y que la salida correcta es moverla, no reescribir su etiqueta.
5. Mide el ancho real de esa pastilla en la build antes de decidir el reparto del pie. La spec usa 354 px des-escalados de una captura, no medidos.
6. Dime qué claves de `Localization/Strings.cs` faltan y dame la lista.
7. Propón commits pequeños, empezando por el pie del asistente de Radmin — es el único defecto que el usuario ve como algo roto.

## Reglas al implementar

- **Un solo elemento sólido por diálogo, y es el que hace la cosa.** Cancelar es un enlace (`MpLinkButton`). Vale para crear sala, para el inicio de sesión de Discord y para el pie del asistente.
- **Ninguna ayuda puede contradecir la selección.** Todo texto derivado se recalcula en un único método, como el `Refresh()` del torneo. En particular el aviso de Record Game solo existe con la sala competitiva marcada, y sobra una de las dos copias que hay hoy.
- **Lo que no puede variar no se muestra.** El bloque de FORMATO de crear sala se colapsa mientras la casilla esté desmarcada, como hace `TeamSourceBlock` en 1v1. Un control segmentado nunca se queda sin segmento activo.
- **Un texto que se pide copiar se muestra entero**, envolviendo si hace falta. Nunca con elipsis. Vale para el nombre de red y para la URL de OAuth.
- **Mide cualquier fila con tres elementos** antes de darla por buena: las tres ventanas tienen ancho fijo. Recuerda que `overflow`/clip corta en la caja de relleno, no en la de contenido.
- **No cambies `SupportLink`.** Si estorba en un pie, el pie es el problema.
- Cero cadenas literales: todo a `Localization/Strings.cs`, español e inglés.
- Respeta los valores exactos de las tablas de Tokens de cada spec. Reutiliza los recursos compartidos del launcher; si creas un estilo nuevo (fila plegada, caja de cuentas), hazlo compartido, no por ventana.
- Señala cualquier punto de las specs que choque con la arquitectura real, con `CLAUDE.md`, con `.claude/rules/multiplayer.md` o con un test existente, en vez de forzarlo. `DialogXamlTests` ya cubre estos diálogos: si cambias comportamiento observable, actualiza o añade el test.
- Compila y pasa los tests antes de darme cada commit por terminado.

Cuando acabes el pie del asistente de Radmin, párate y enséñame una captura antes de seguir.
