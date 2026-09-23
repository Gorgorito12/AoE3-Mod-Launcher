# Insignias en las pantallas, y guía de rangos

Segunda parte de las insignias de rango. Explica dónde va la insignia en las cinco pantallas donde aparece un jugador (**45**), y cómo es el popup que explica los rangos y desde dónde se abre (**46**).

**Depende del paquete anterior, `handoff_insignias_rango`.** Allí están la insignia en sí (forma, colores, luz, chispas) y la regla de la edad. Si todavía no está implementado, hazlo primero. Este paquete solo usa ese control.

Repo: `Gorgorito12/AoE3-Mod-Launcher`, rama `main`, proyecto WPF `WarsOfLibertyLauncher/`.

| Archivo del paquete | Qué contiene |
| --- | --- |
| `Prototipo-pantallas.html` | 45a Clasificación · 45b fila de sala · 45c panel de la sala · 45d panel Jugadores · 45e Actividad de la comunidad |
| `Prototipo-guia.html` | 46a el popup · 46b sus tres entradas |

Los dos HTML son **referencia de diseño**, no código para copiar. Recrea en WPF/XAML con los estilos que ya existen. Todo el texto pasa por `Localization/Strings.cs` (español e inglés). Los textos de la guía están en inglés, como el resto del launcher.

| Qué | Archivos del repo |
| --- | --- |
| Clasificación | `Controls/MultiplayerTab.xaml.cs` (`BuildRankingHeader`, `BuildLeaderboardRow`), `Services/Multiplayer/RankingTableLayout.cs`, `Tests/RankingTableLayoutTests.cs` |
| Fila de sala | `MultiplayerTab.xaml.cs` (`BuildRoomCard`) |
| Panel de la sala | `LobbyWindow.xaml` + `.xaml.cs` |
| Panel Jugadores y Actividad | `MultiplayerTab.xaml` (columna Global chat / Players, estilo `MpActivityCard`), y sus constructores en `MultiplayerTab.xaml.cs` |
| Capa del popup | `MultiplayerTab.xaml` → `TabRootGrid` (ya aloja `MpAlertOverlay`) |
| Bloque de cuenta | `MainWindow`, `PushAccountChip` / `LoadStandingAsync` en `MultiplayerTab.xaml.cs`, `Tests/AccountChipTests.cs` |
| Datos | `Models/Multiplayer/LobbyDtos.cs` (`LeaderboardRow`) |

---

## La regla de siempre

**La edad sale del puesto en la tabla, nunca del rating impreso.** La tabla ordena por `rating − 2·rd`; por eso AleReis tiene 1720 y va 4.º. La calcula un solo método puro (ver el paquete anterior), y lo usan las cinco pantallas y la guía.

---

## 45 · Dónde va la insignia

### 45a · Clasificación

- Sustituye al número de la columna # (44 px) y lleva dentro el puesto del servidor. 24 px; 28 px la del 1.º.
- **Bloque TOP 5 (43g):** las cinco primeras filas en un panel un tono más claro, con cabecera «TOP 5». **Sin margen lateral:** el marco va con borde interior. Si estrecha las filas, las columnas se desalinean con la cabecera.
- **No toques los anchos de columna:** se reparten como dice `RankingTableLayout`.

### 45b · Fila de sala

- En la celda HOST, la insignia (17 px) **ocupa el sitio del avatar**. La columna sigue en 152 px. Con avatar e insignia a la vez no cabe.

### 45c · Panel de la sala

- Insignia (24 px) junto al avatar. La segunda línea añade la edad en texto: `1383 ELO · you · Colonial`. El hueco vacío no lleva insignia.

### 45d · Panel Jugadores (ventana principal)

- Entre el avatar y el nombre, a 19 px. **La fila no crece.** Aquí caben avatar e insignia a la vez, porque el panel no tiene columnas fijas.
- **Quien no tiene partidas decididas («Unrated») lleva la insignia de Descubrimiento**, gris y sin luz, en lugar de ninguna.
- La columna del panel mide 250-270 px (`MultiplayerTab.xaml`). **El alto de fila, el avatar y el sufijo «ELO» los saqué de una captura**, no del código: confírmalos en el constructor del panel antes de colocar la insignia.

### 45e · Actividad de la comunidad

- **Tarjeta Ranking:** la insignia sustituye al número. **Cada insignia va en una casilla fija de 26 px**, aunque la del 1.º sea más grande. Si no, el avatar y el nombre del 1.º quedan 3 px desplazados respecto a los demás.
- **Sin bloque TOP 5:** la tarjeta entera ya es el top 5. Basta con el acento del 1.º.
- **Community matches no lleva insignia**, a propósito: cada línea ya tiene nombre, bandera y civilización por jugador. Cada partida sigue en **una sola línea**, como hoy. Si no cabe, se corta con «…» al final del texto y no desaparece ninguna palabra.
- Relleno y radio de las tarjetas: los de `MpActivityCard` (12,9 · radio 8).

---

## 46 · Guía de rangos

### 46a · El popup

De arriba abajo:

1. **Tu rango:** tu insignia, tu edad, tu puesto y tu rating («Colonial · 11th of 14 · 1383»).
2. **El siguiente paso**, en una línea con la insignia de la edad siguiente: «Pass 1 player to reach Fortress. Pikilic is 10th». Sale del mismo método que calcula la edad: cuántos puestos hay hasta el primero de la edad siguiente. Si eres Soberano, dice que defiendas el puesto. Si eres Descubrimiento, que juegues tu primera partida competitiva.
3. **Las seis edades**, de la más alta a la más baja, cada una con su insignia animada, los puestos que abarca, quién la tiene ahora y **cuántos jugadores** («1 player», «4 players»). Tu fila va marcada.
4. **Cómo funciona**, en cuatro frases sin fórmulas:
   - La edad sale del puesto, no del rating.
   - La tabla usa un rating confirmado: con pocas partidas está por debajo del número que ves, así que nadie se pone primero con una victoria suelta.
   - Solo cuentan las partidas competitivas con resultado leído. Hay que activar Record Game.
   - Soberano es de quien va primero, y pasa a otro si pierde el puesto.
5. **Pie:** «Open ranking» lleva a la tabla completa.

**Implementación:**
- **Capa sobre la pestaña, no ventana aparte.** Va en `TabRootGrid`, igual que las tarjetas de `MpAlertOverlay`.
- Se cierra con ✕, con Esc y pulsando fuera.
- Ancho máximo 620 px. Con muchas edades o pantalla baja, desplaza solo la lista, no la cabecera.
- Las insignias de la guía usan el mismo control que el resto, no una copia.

### 46b · Dónde se abre

Tres entradas. **Ningún botón nuevo en la barra de Rooms:** tu XAML dice que esa fila no tiene ancho libre, y el test `TheRoomsTopBarFitsAtTheNarrowestWindow` existe para impedirlo.

1. **Pestaña Ranking:** el enlace «How ranks work», junto a «30 days». **Va en color de acción y sin borde.** «30 days» es un `MpScopeChip`, que es un `Border` a propósito para no parecer un filtro. Si el enlace tuviera la misma pastilla, no se sabría cuál se pulsa. Si prefieres un botón, usa `MpSecondaryButton`, nunca la forma del chip.
2. **Bloque de cuenta, arriba a la derecha:** «1383 ELO» pasa a mostrar tu insignia, tu edad y tu rating. Al pulsarlo se abre la guía con tu fila marcada. Dos condiciones:
   - **El bloque vive en `MainWindow`** y se ve también en Library y Workshop, donde la capa de `TabRootGrid` no está en pantalla. Desde ahí, al pulsarlo el launcher **cambia a Multiplayer › Ranking y abre la guía allí**. No montes una segunda capa en `MainWindow`.
   - **`AccountChipTests.NothingElseRendersTheChip` fija 3 apariciones de `PushAccountChip(`:** la declaración, la llamada del estado de sesión y la que repinta cuando llega el rating (`LoadStandingAsync`). **La edad viaja en esa tercera, ampliando lo que recibe.** No añadas una llamada nueva o el test fallará, y con razón.
3. **Cualquier insignia** (salas, paneles, ranking) abre la guía al pulsarla, con cursor de mano y sin más cambio visual.

---

## Lo que hay que confirmar antes de empezar

- **De dónde sacan la edad las salas, el panel de la sala y el panel Jugadores.** Hoy solo traen el ELO. O el servidor envía el puesto o la edad en los DTO, o el launcher la saca del ranking que ya tiene cargado. **No inventes un campo:** di qué hay hoy en `LobbyDtos.cs`.
- **Alto de fila, avatar y sufijo «ELO» del panel Jugadores y de las tarjetas de Actividad.** Los constructores están en `MultiplayerTab.xaml.cs`, que no pude abrir por tamaño.
- **Qué hace hoy pulsar un jugador en el panel Jugadores**, si hace algo. Si ya abre algo, la insignia no puede robarle el clic.
