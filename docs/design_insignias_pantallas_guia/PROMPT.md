# Texto para pegar en Claude Code

Copia esta carpeta dentro del repo (p. ej. `docs/design_insignias_pantallas_guia/`), abre una terminal ahí, lanza `claude` y pega esto:

---

Lee `docs/design_insignias_pantallas_guia/README.md` completo antes de escribir código. Es la segunda parte de las **insignias de rango por edades**: dónde va la insignia en cinco pantallas (45a-e) y un popup que explica los rangos (46a-b).

**Depende del paquete `docs/design_insignias_rango/`**: la insignia como control y el método que calcula la edad. Si no están hechos, dímelo y empieza por ahí.

Hay dos prototipos de referencia: `Prototipo-pantallas.html` y `Prototipo-guia.html`. NO los copies: recrea en WPF/XAML con los estilos y controles que ya existen.

**No toques nada más:** ni los anchos de `RankingTableLayout`, ni la barra de Rooms, ni otras pantallas.

## Antes de escribir código, hazme un plan

1. **Datos de la edad fuera del ranking.** La fila de sala, el panel de la sala y el panel Jugadores solo traen el ELO. Dime qué hay hoy en `Models/Multiplayer/LobbyDtos.cs` y propón: o el servidor envía puesto/edad, o el launcher la saca del ranking que ya tiene cargado. **No inventes un campo.**
2. **Mide las filas reales** del panel Jugadores y de las tarjetas de Actividad en `MultiplayerTab.xaml.cs` (alto, avatar, tamaño del sufijo «ELO»). El diseño las sacó de capturas.
3. **Lee `WarsOfLibertyLauncher.Tests/AccountChipTests.cs`** y confírmame que la edad puede viajar en la llamada de `PushAccountChip` que ya hace `LoadStandingAsync`, sin añadir una llamada nueva. El test fija 3 apariciones.
4. **Dime cómo abrirías la guía desde Library o Workshop**, donde `TabRootGrid` no está en pantalla. El README pide cambiar a Multiplayer › Ranking y abrirla allí.
5. **Dime qué hace hoy pulsar un jugador** en el panel Jugadores o en el ranking. Si ya abre algo, la insignia no puede quitarle el clic.
6. Dime las claves nuevas de `Localization/Strings.cs` (guía, «How ranks work», «Pass N players to reach…», nombres de edad en inglés).
7. Commits pequeños, en este orden: 45a Clasificación → 45e tarjeta Ranking → 45b fila de sala → 45c y 45d paneles → 46a popup → 46b entradas.

## Reglas al implementar

- **La edad sale del puesto, nunca del rating impreso.** Un solo método para todo, incluido «el siguiente paso» de la guía.
- **Ninguna fila crece** por llevar insignia, en ninguna pantalla.
- **Las insignias de distinto tamaño van en una casilla de ancho fijo**, para que avatar y nombre queden en la misma x en todas las filas.
- **El bloque TOP 5 no estrecha las filas.** Comprueba que la cabecera y todas las filas tienen las columnas en la misma x.
- **En la fila de sala, la insignia ocupa el sitio del avatar** y HOST sigue en 152 px.
- **Community matches no lleva insignia** y cada partida es una línea que se corta con «…» al final, sin que desaparezca ninguna palabra.
- **El popup es una capa en `TabRootGrid`**, no una ventana. Se cierra con ✕, Esc y clic fuera.
- **«How ranks work» no puede parecerse a `MpScopeChip`.** Enlace en color de acción, o `MpSecondaryButton`.
- **Ningún botón nuevo en la barra de Rooms.**
- Cero cadenas literales: todo a `Localization/Strings.cs`.
- Señala cualquier punto del README que choque con la arquitectura, con `CLAUDE.md`, con `.claude/rules/multiplayer.md` o con un test, en vez de forzarlo.
- Compila y pasa los tests antes de dar cada commit por terminado.

Cuando tengas 45a y 45e, párate y enséñame una captura antes de seguir.
