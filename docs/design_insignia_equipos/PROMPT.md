# Texto para pegar en Claude Code

Copia esta carpeta dentro del repo (p. ej. `docs/design_insignia_equipos/`), abre una terminal ahí, lanza `claude` y pega esto:

---

Lee `docs/design_insignia_equipos/README.md` completo antes de escribir código. Hoy la insignia de rango junto al nombre es siempre la del ladder 1v1. Hay que añadir la **insignia de equipos** (dos escudos de la misma edad), decidir cuál sale en cada sitio según el modo de la sala, y añadir en el Perfil un selector para los sitios sin partida de por medio.

El prototipo de referencia es `Insignia de equipos.dc.html`, turno 51 (51a, 51b, 51c). NO lo copies: recrea en WPF/XAML **reutilizando el control de insignia que ya existe**. Edades, colores, luces y geometría no cambian. Los escudos del prototipo son estáticos a propósito.

**No toques nada más.** Ni el cálculo de la edad de 1v1, ni `RankingTableLayout`, ni las animaciones de la insignia.

## Antes de escribir código, hazme un plan

1. **Dime qué datos de equipos hay hoy.** Lee `Models/Multiplayer/LobbyDtos.cs`, `PlayerStanding.cs` y lo que alimenta la pestaña TEAMS de la Clasificación. Necesito saber si existe la posición **por jugador** en el ladder de equipos y si viaja en los DTO de sala, jugador, chat y cuenta. **No inventes campos**: si no existen, dime qué tendría que mandar el servidor.
2. **Confirma que el cálculo de la edad sirve tal cual** para el ladder de equipos con otra lista (puesto, no rating impreso).
3. **Dime de dónde sale el modo de cada sala** (`MatchModeView`, `MatchParticipantsView.FormatOf`) y qué pasa cuando no se conoce.
4. **Propón dónde se guarda la preferencia** (`best` / `1v1` / `teams`). Tiene que ser en el servidor, porque la ven los demás. Dime qué endpoint o DTO cambiaría.
5. **Propón cómo extender el control de insignia** con un modo `Teams` que añada el escudo trasero sin duplicar la geometría ni la luz.
6. Propón commits pequeños, en este orden: método puro que decide qué insignia mostrar (modo de la sala, preferencia, edades), con tests → modo Teams del control, estático → sala y fila de sala → chip de cuenta, chat y panel Jugadores → selector del Perfil → tooltip.

## Reglas al implementar

- **Un método puro, sin WPF**, que reciba el modo de la sala (o ninguno), la preferencia y las dos edades, y devuelva qué insignia mostrar. Tests para: sala 1v1, sala 2v2, modo desconocido, Highest con empate (gana 1v1), y Teams sin partidas de equipo.
- **Highest compara por edad**, no por puesto ni por ELO.
- **En una sala, el modo manda** sobre la preferencia, para todos los jugadores.
- **El escudo trasero no anima nunca** y reutiliza el recurso de geometría.
- **Sin partidas de equipo decididas:** escudo doble de Descubrimiento, y la opción Teams desactivada en el Perfil.
- Cero cadenas literales: todo a `Localization/Strings.cs`, en español e inglés.
- Si cambias la llamada de `PushAccountChip` en `LoadStandingAsync`, actualiza `AccountChipTests` en vez de saltártelos.
- Señala cualquier punto del README que choque con la arquitectura real, con `CLAUDE.md`, con `.claude/rules/multiplayer.md` o con un test existente, en vez de forzarlo.
- Compila y pasa los tests antes de darme cada commit por terminado.

Si el servidor no tiene todavía la posición de equipos por jugador, **para después del paso 1** y dime exactamente qué hay que pedir al backend antes de seguir.
