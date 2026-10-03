# Texto para pegar en Claude Code: guía de rangos con pestaña de equipos (53)

Copia esta carpeta dentro del repo (p. ej. `docs/design_guia_rangos_equipos/`), abre una terminal ahí, lanza `claude` y pega esto:

---

La guía de rangos (`Controls/RankGuideCard.cs`, con los datos de `Services/Multiplayer/RankGuideView.cs`) solo explica el ladder **1v1**. Hay que añadirle una pestaña **Teams** con un selector **1v1 / Teams** en la cabecera. El prototipo es `docs/design_guia_rangos_equipos/Guia de rangos equipos.dc.html`: **53a** es la pestaña Teams y **53b** son las reglas. NO copies el HTML: recrea en WPF con lo que ya existe.

**No toques nada más.** La pestaña 1v1 se queda como hoy, salvo el selector y el pie («on the 1v1 ladder»). No cambies `RankAges`, `RankBadgeChoice` ni la Clasificación.

## Qué tiene la pestaña Teams (53a)

- **El mismo `RankGuideView`, construido con la lista de equipos:** posición propia en el ladder de equipos, tamaño de ese ladder y nombres por posición. Las franjas salen de `RankAges` sobre esa lista. Las de 53a (1.º, 2.º, 3.º-4.º… con 9 jugadores) son solo un ejemplo: **no escribas franjas a mano.**
- **Todas las insignias son el escudo doble** (`BadgeKind.Team` del control `RankBadge`): la de la cabecera, la del aviso de «Pass 1 player to reach…» y las de las seis filas.
- **Cabecera:** «You are Fortress · 5th of 9 · 1455», con el ELO de equipos.
- **Selector** a la derecha del título, antes de ✕. Usa el mismo estilo segmentado que el 1v1 / TEAMS de la Clasificación: fondo `#0B1526`, opción activa `#233A5C` en blanco e inactiva `#A8BCD2`.
- **Título de la lista:** «THE SIX AGES · TEAMS». **Título de las explicaciones:** «HOW THE TEAM RANKING WORKS».
- **Explicaciones de Teams.** La 2 se reutiliza tal cual de 1v1; las otras tres son nuevas o ajustadas:
  1. Your team age comes from your place in the team table, not from your rating. 2v2 and 3v3 share this table.
  2. *(la de 1v1 sobre el rating confirmado, sin cambios)*
  3. Only competitive team matches with a readable result count. Turn on Record Game before you start.
  4. In a team room everyone wears this badge. Elsewhere, choose in Profile which one others see.
- **Pie:** «9 players on the team ladder» y el botón **Open team ranking**, que abre la Clasificación directamente en la pestaña TEAMS.

## Reglas (53b)

1. **Abre en la pestaña de la insignia que se pulsó.** Desde un escudo doble, en Teams. Desde el chip de cuenta, en la pestaña de la insignia que se muestra ahí (la de `BadgeMode`). La pestaña elegida **no se guarda**.
2. **Sin partidas de equipo decididas:** escudo doble de Descubrimiento en la cabecera, con «You are Discovery · play a competitive team match to join». El aviso usa el mismo criterio que ya usa 1v1 para entrar en la tabla, con el número que pida el servidor. No se resalta ninguna fila.
3. **Servidor sin ladder de equipos** (edad de equipos desconocida): **sin selector**, y la guía queda exactamente como hoy.
4. Cero cadenas literales: todo a `Localization/Strings.cs`, en español e inglés. Reutiliza las claves `MpGuide*` que valgan y crea solo las que falten.

## Antes de escribir código, hazme un plan

1. Dime de dónde saldrán, en el launcher, la posición propia en equipos, el tamaño del ladder de equipos y los nombres por posición. ¿Lo trae ya lo que alimenta la pestaña TEAMS de la Clasificación? **No inventes campos.**
2. Dime cómo cambiará la firma de `RankGuideCard.Build` para recibir las dos vistas, o una vista y el modo, y desde dónde se abre hoy la guía, para pasarle la pestaña inicial.
3. Propón las claves de `Strings.cs` nuevas.
4. Propón commits pequeños: `RankGuideView` para equipos con tests → selector y pestaña Teams → pestaña inicial según la insignia → caso sin partidas de equipo y caso de servidor antiguo.

Compila y pasa los tests antes de dar por terminado cada commit. Cuando tengas la pestaña Teams con datos reales, para y enséñame una captura.
