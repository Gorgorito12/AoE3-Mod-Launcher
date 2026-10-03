# Texto para pegar en Claude Code

Copia esta carpeta dentro del repo (p. ej. `docs/design_elo/`), abre una terminal ahí, lanza `claude` y pega esto:

---

Lee `docs/design_elo/README.md` completo antes de escribir código. **Sustituye a cualquier versión anterior de este diseño.** Cubre:

- dos clasificaciones separadas, 1v1 y Equipos;
- posicionamiento de 10 partidas en 1v1 y 5 en Equipos, al final de la misma tabla;
- racha 🔥;
- antifarmeo por porcentaje;
- en la sala, la elección de Equipo 1 / Equipo 2, con probabilidad y bloqueo de Empezar;
- cuenta atrás con los equipos;
- tarjetas de resultado e historial;
- destacados del mes y su versión para Discord;
- ELO con «?» durante el posicionamiento, ELO mínimo, racha de derrotas más larga, etiqueta de inactividad (30 días), partidas no puntuadas por cuenta nueva y aviso de devolución de puntos.

El prototipo es `Clasificacion ELO v3.dc.html` (55a-55n). La sala de equipos, 55h, es interactiva. NO copies el HTML: recrea en WPF con lo que ya existe en `Controls/MultiplayerTab.xaml.cs`, `LobbyWindow`, el Historial y los controles de insignia.

## Antes de escribir código, hazme un plan

1. **Datos.** Revisa el §11 contra `Models/Multiplayer/LobbyDtos.cs` y `Services/Multiplayer/LobbyApiClient.cs`. Dime qué existe y qué hay que pedir al servidor. **No inventes campos ni valores por defecto**: si falta un dato, esa parte de la UI no se muestra.
2. **Sala de equipos.** Dime cómo se guarda hoy la sala y sus jugadores, y qué hace falta para el campo `team`, para que el anfitrión pueda mover jugadores y para bloquear Empezar.
3. **Posicionamiento contra PROVISIONAL.** Dime dónde se usa hoy PROVISIONAL o el `rd` alto, y cómo se sustituye sin romper los tests.
4. Lleva las 3 preguntas del §12 a una lista para el backend.
5. Propón commits pequeños, en este orden:
   1. DTO y vistas puras, con tests: tabla y posicionamiento, racha y caducidad, factor de antifarmeo y sus textos, motivo de bloqueo de Empezar, probabilidad, lista de nombres «Ana y Luis».
   2. Clasificación 55a-55c.
   3. Perfil 55d-55f.
   4. Sala 1v1 55g.
   5. Sala de equipos 55h.
   6. Cuenta atrás 55i.
   7. Resultado 55j.
   8. Historial 55k.
   9. Destacados 55l.
   10. Discord 55m.
   11. Devolución de puntos 55n.

## Reglas

- **Antifarmeo:** 100 % hasta la 2.ª victoria seguida contra el mismo rival; desde la 3.ª, 90 %, 80 %… hasta un mínimo del 20 % en la 10.ª. Vuelve al 100 % si el rival gana una y recupera un 10 % por cada día sin jugar entre ellos. **Nunca en torneos.** No se anuncia antes de la partida: solo en el resultado y en el Historial.
- **Textos sin acusar a nadie:** se dice qué pasó y qué lo devuelve a la normalidad.
- **Sala de equipos competitiva:** no se puede empezar hasta que los equipos estén completos y parejos. Se comprueba en este orden: sala incompleta, jugador sin equipo y equipos desiguales. El motivo siempre se muestra.
- **La probabilidad se recalcula** en cuanto alguien cambia de equipo. Es un entero entre 1 y 99.
- **Sin duración de partida** ni estadísticas internas del juego (la regla de cuenta nueva solo muestra el motivo).
- **Posicionamiento ajeno:** en la tabla, los demás solo ven el progreso en gris, sin victorias ni derrotas. El servidor no envía `placement_results` de otros jugadores.
- **ELO con «?»** durante el posicionamiento, en la tabla, el perfil y la sala.
- **Inactivos (30 días):** siguen en la tabla con su puesto y la etiqueta INACTIVO, y su perfil lo avisa.
- **Devolución de puntos:** nunca se nombra al rival sancionado.
- **Por debajo de 600 px:** la tabla pierde V-D y %, y las tarjetas del perfil y los destacados se apilan. Los nombres se recortan con «…», salvo en el recordatorio de la cuenta atrás, donde pasan a varias líneas. La racha nunca se recorta.
- **Cero cadenas literales:** todo a `Localization/Strings.cs` (es-419 con tuteo, y en), con singular y plural, y los ordinales en inglés con un helper.
- Señala cualquier choque con `CLAUDE.md`, con `.claude/rules/multiplayer.md` o con un test existente, en vez de forzarlo.
- Compila y pasa los tests antes de dar por terminado cada commit.

Si faltan campos del servidor, **para después del paso 1** y dame la lista exacta para el backend. Cuando tengas la Clasificación (55a) y la sala de equipos (55h) funcionando, enséñame capturas antes de seguir.
