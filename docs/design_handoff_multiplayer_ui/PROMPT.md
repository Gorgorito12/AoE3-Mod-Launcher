# Texto para pegar en Claude Code

Abre una terminal en la carpeta del repo (`AoE3-Mod-Launcher`), copia esta carpeta de handoff dentro (por ejemplo en `docs/design_handoff_multiplayer_ui/`), lanza `claude` y pega esto:

---

Lee `docs/design_handoff_multiplayer_ui/README.md` completo antes de escribir código. Es el handoff de un rediseño de la pestaña Multijugador de este launcher WPF. El archivo `Launcher Multiplayer.dc.html` de esa carpeta es un prototipo HTML de referencia: NO lo copies ni lo integres, recrea las pantallas en WPF/XAML con los estilos y controles que ya existen en `WarsOfLibertyLauncher/`.

Implementa **solo las opciones 1a, 1d, 1e y 1f**. Las opciones 1b y 1c están descartadas: ignóralas por completo.

Antes de tocar nada, haz un plan y enséñamelo:

1. Lee `Controls/MultiplayerTab.xaml.cs`, `CreateLobbyDialog.xaml.cs`, `LobbyWindow.xaml.cs` y sus `.xaml`, y dime qué partes de la UI actual desaparecen (banner VPN permanente, fila de cuenta, columna MOD de la tabla, acordeón «Advanced details», bloque «So the match counts» con su «Don't show this again», paneles apilados de chat y jugadores) y qué se reubica.
2. Propón el orden de trabajo en commits pequeños, uno por pantalla, empezando por 1a.
3. Señala cualquier punto del README que choque con la arquitectura real o con `docs/ARCHITECTURE.md`, en vez de forzarlo.

Reglas al implementar:

- Cero cadenas literales en la UI: todo a `Localization/Strings.cs`, con español e inglés.
- Respeta los valores exactos de la sección Tokens del README (colores, alturas, radios, escala tipográfica).
- Las filas de sala son de UNA línea con recorte por elipsis y altura uniforme; el nombre del mod va en el subtítulo, no en una columna.
- El Lobby sigue siendo una `Window` aparte. No lo integres en la pestaña.
- No inventes campos de backend: usa los DTOs de `Models/Multiplayer/LobbyDtos.cs` que el README enumera. El ping se mide localmente por ICMP a la IP Radmin, como ya se hace en la sala.
- Mantén intacta la lógica de `RoomMatchState`, `MatchResultResolver` y `PlayerStanding`; el rediseño solo cambia cómo se presentan (win% sobre partidas decididas, «—» cuando no hay ninguna decidida, nunca 0 %).
- Añade o actualiza tests donde ya haya cobertura equivalente (`ChatTimeFormatTests`, `MatchContextTests`, `MatchResultResolverTests`) si cambias comportamiento observable.
- Compila y ejecuta los tests antes de darme cada commit por terminado.

Cuando acabes 1a, párate y enséñame una captura o el XAML resultante antes de seguir con 1d.
