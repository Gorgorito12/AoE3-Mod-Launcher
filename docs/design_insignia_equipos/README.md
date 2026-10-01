# Insignia de rango en equipos

Hoy la insignia que se ve junto al nombre siempre es la del ladder **1v1**. Este paquete añade la **insignia de equipos**, define en qué sitio sale cada una y permite al jugador elegir cuál se ve en los sitios sin partida de por medio.

Se apoya en el sistema de insignias por edades que ya existe (paquete `handoff_insignias_rango`). Las edades, los colores, las capas de luz y la geometría del escudo **no cambian**.

Repo: `Gorgorito12/AoE3-Mod-Launcher`, rama `main`, proyecto WPF `WarsOfLibertyLauncher/`.

| Archivo del paquete | Qué contiene |
| --- | --- |
| `Insignia de equipos.dc.html` | Prototipo, **turno 51**: 51a forma de la insignia, 51b qué insignia sale en cada sitio, 51c selector en el Perfil (interactivo). Ábrelo en un navegador con `support.js` al lado. |

El HTML es **referencia de diseño**, no código para copiar. Recréalo en WPF/XAML con el control de insignia que ya existe. Todo el texto va en `Localization/Strings.cs` (español e inglés). Los escudos del prototipo son **estáticos** a propósito: las animaciones son las del control actual.

| Qué | Archivos del repo (a confirmar) |
| --- | --- |
| Control de insignia y cálculo de la edad | lo que se creó con `handoff_insignias_rango` (método puro tipo `AgeFor(position, decidedMatches)`) |
| Clasificación, pestañas 1v1 / TEAMS | `Controls/MultiplayerTab.xaml.cs` (`BuildRankingHeader`, `BuildLeaderboardRow`), `Services/Multiplayer/RankingTableLayout.cs` |
| Fila de sala | `Controls/MultiplayerTab.xaml.cs` (`BuildRoomCard`), `Services/Multiplayer/MatchModeView.cs`, `MatchParticipantsView.FormatOf` |
| Panel de jugadores de la sala | `LobbyWindow.xaml(.cs)` |
| Chip de cuenta | `MainWindow` (`PushAccountChip`, `LoadStandingAsync`), `WarsOfLibertyLauncher.Tests/AccountChipTests.cs` |
| Datos | `Models/Multiplayer/LobbyDtos.cs` (`LeaderboardRow`, DTO de sala y jugador), `Services/Multiplayer/PlayerStanding.cs` |
| Perfil | `Controls/MultiplayerTab.xaml.cs` (pestaña Profile) |

---

## 1. 51a · La forma: dos escudos

- **1v1:** un escudo, igual que hoy.
- **Equipos:** dos escudos de la **misma edad y color**. Delante va el escudo normal, con el puesto dentro. Detrás asoma otro escudo, desplazado a la izquierda, más pequeño y más oscuro.
- La edad de equipos se calcula **igual que la de 1v1** (puesto en el ladder, no rating impreso), pero con la posición en el **ladder de equipos**. Mismo método, otra lista.

### Medidas (escudo delantero de 24×28, el del panel de jugadores)

| Pieza | Valor |
| --- | --- |
| Caja total | 31 × 28 (7 px más ancha que la de 1v1) |
| Escudo trasero | 21 × 24, en x 0 e y 3, opacidad 0,8, velo `rgba(6,12,20,.85)`: casi solo se ve el filo de color |
| Escudo delantero | 24 × 28, en x 7 e y 0. Es el control de insignia normal, con su luz |
| Separación | detrás del delantero, una silueta del color del fondo, 1,5 px mayor por la izquierda y 1 px por arriba y abajo, para que los dos escudos no se fundan |

Escala lo mismo para los otros tamaños: 17 px en la fila de sala, 20 px en listas y 48 px en grande (en el prototipo, la caja de 48 px mide 62 × 56). El escudo trasero es aproximadamente el 87 % del delantero y se desplaza a la izquierda un 29 % del ancho del delantero.

**La luz solo va en el escudo delantero.** El trasero no anima nunca, así que el coste es el mismo que el de una insignia de 1v1.

La geometría es la misma de siempre: `polygon(6% 0%, 94% 0%, 100% 15%, 96% 60%, 50% 100%, 4% 60%, 0% 15%)`. Que el trasero reutilice el recurso, no una copia.

### Texto

- Donde hay una segunda línea bajo el nombre, se añade el modo: `1612 ELO · Teams · Imperial` (o `· you ·` en medio si es el propio jugador, como hoy). En una sala de equipos, el ELO de esa línea también es el de equipos.
- **Tooltip de las dos insignias:** título «Teams rank · Imperial» o «1v1 rank · Industrial» y, debajo, «#2 in the teams ladder · 1v1: Industrial #5». Siempre nombra la otra insignia.
- Sin partidas de equipo decididas: escudo doble de **Descubrimiento**, sin color ni luz, como en 1v1.

## 2. 51b · Qué insignia sale en cada sitio

| Dónde | Insignia |
| --- | --- |
| Sala 1v1 y su fila en la lista | 1v1, automática |
| Sala 2v2, 3v3 o 4v4 y su fila en la lista | Equipos, automática, para todos los jugadores |
| Clasificación, pestaña 1v1 / pestaña TEAMS | La de la pestaña |
| Chip de cuenta, chat global, panel Jugadores, cabecera del Perfil | **La que haya elegido el jugador** (§3) |

El modo de la sala sale de lo que ya usa `MatchModeView.Label` o `MatchParticipantsView.FormatOf`. Si no se conoce el formato, usa la elección del jugador en vez de adivinar. Es la misma regla que ya sigue `MatchModeView`: si no se sabe, no se inventa.

## 3. 51c · Selector en el Perfil

Bloque en la pestaña Profile (tarjeta `#12213a`, borde `rgba(130,175,255,.11)`, radio 10, padding 14×15):

- Título: **«Badge next to your name»** (12,5 px semibold, `#e8eef6`).
- Texto: «In chat, the Players list and your account. Inside a room, the badge always matches the room's mode.» (11,5 px, `#8ea4c0`).
- Selector segmentado con el mismo estilo que el selector 1v1 / TEAMS de la Clasificación: fondo `#0d1828`, padding 3, opción activa `#233a5c` con texto blanco, inactiva con texto `#a8bcd2`, 32 px de alto. Tres opciones:
  - **Highest** (por defecto): la de mayor edad. Si empatan, la de 1v1.
  - **1v1**
  - **Teams**: desactivada si no hay partidas de equipo decididas, con el tooltip «Play a team match to get this badge».
- **OTHERS SEE:** vista previa con avatar, insignia, nombre y, a la derecha, «Teams · Imperial» o «1v1 · Industrial». Cambia al momento.
- Debajo, dos casillas con las dos clasificaciones: `1V1` Industrial · #5 · 1566 ELO, y `TEAMS` Imperial · #2 · 1612 ELO. El nombre de la edad va en el color claro de su edad.

**La elección se guarda en el servidor** y viaja con el jugador, para que los demás la vean. Guardarla solo en `LauncherConfig` no sirve.

## 4. Lo que hay que confirmar antes de empezar

- **¿Envía el servidor la posición de cada jugador en el ladder de equipos?** La pestaña TEAMS de la Clasificación existe, pero no está comprobado si `LeaderboardRow` o el DTO del jugador traen el puesto de equipos. Si el ladder de equipos es por equipo y no por jugador, avísame: el diseño supone que es por jugador.
- **¿Dónde se guarda la preferencia?** Hace falta un campo en el perfil del servidor (p. ej. `badge_mode: "best" | "1v1" | "teams"`) y que viaje en los DTO que usan el chat, el panel Jugadores y el chip de cuenta.
- **Desempate de Highest:** se compara por edad, no por puesto ni por ELO. Con la misma edad gana 1v1.

## 5. Textos nuevos (Strings.cs)

| Clave sugerida | en | es |
| --- | --- | --- |
| `MpBadgeModeTitle` | Badge next to your name | Insignia junto a tu nombre |
| `MpBadgeModeBody` | In chat, the Players list and your account. Inside a room, the badge always matches the room's mode. | En el chat, la lista de jugadores y tu cuenta. Dentro de una sala, la insignia siempre es la del modo de la sala. |
| `MpBadgeModeBest` / `1v1` / `Teams` | Highest / 1v1 / Teams | La más alta / 1v1 / Equipos |
| `MpBadgeModeTeamsLocked` | Play a team match to get this badge | Juega una partida en equipo para conseguir esta insignia |
| `MpBadgeOthersSee` | OTHERS SEE | LOS DEMÁS VEN |
| `MpBadgeTipTitle` | {0} rank · {1} | Rango {0} · {1} |
| `MpBadgeTipBody` | #{0} in the {1} ladder · {2}: {3} #{4} | #{0} en el ladder {1} · {2}: {3} #{4} |
| `MpModeTeams` | Teams | Equipos |
