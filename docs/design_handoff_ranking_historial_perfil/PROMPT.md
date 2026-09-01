# Texto para pegar en Claude Code

Copia esta carpeta dentro del repo (p. ej. `docs/design_ranking_historial_perfil/`), abre una terminal ahí, lanza `claude` y pega esto:

---

Lee `docs/design_ranking_historial_perfil/README.md` completo antes de escribir código. Es el rediseño de tres pestañas del Multijugador de este launcher WPF: **Clasificación (Ranking), Historial (History) y Perfil (Profile)**. Hoy están sin estilo: texto suelto sobre fondo vacío y columnas estiradas a todo el ancho de la pantalla.

El archivo `Clasificacion Historial Perfil.html` es un prototipo de referencia: NO lo copies ni lo integres. Recrea las pantallas en WPF/XAML con los estilos y controles que ya existen en `WarsOfLibertyLauncher/`. Las opciones están rotuladas 3a (Clasificación), 3b (Historial) y 3c (Perfil).

**No toques ninguna otra pantalla.** Salas, Crear sala, Lobby y Partida en curso quedan como están.

Antes de tocar nada, haz un plan y enséñamelo:

1. Lee la sección de `Controls/MultiplayerTab.xaml.cs` (+ su `.xaml`) que renderiza las pestañas Ranking, History y Profile, y dime cómo está montado el layout actual de cada una.
2. Confirma de dónde sale cada dato: `EloSnapshot` (rating, `rd`, wins, losses), `MatchHistoryRow` (`rating_before`, `rating_after`, `result`, mapa, mod, duración, participantes) y el endpoint de ranking para el puesto y el total de jugadores.
3. Propón un commit por pestaña, empezando por Clasificación.
4. Señala cualquier punto del README que choque con la arquitectura real o con `docs/ARCHITECTURE.md`, en vez de forzarlo.

Reglas al implementar:

- **El contenido no se estira**: contenedor con `MaxWidth` (820 px en Clasificación e Historial, 900 px en Perfil) y `HorizontalAlignment="Left"`. Es el defecto principal: hoy el ELO acaba a 2000 px del nombre.
- Cero cadenas literales: todo a `Localization/Strings.cs`, español e inglés.
- Respeta los valores exactos de la sección Tokens (colores, tamaños, radios, escala tipográfica). Los datos numéricos van en monoespaciada.
- Ninguna tabla dibujada como texto suelto: cada tabla o lista es una tarjeta con su cabecera de columnas.
- **No inventes campos de backend.** «Rival habitual» y «Mapa más jugado» se calculan en cliente sobre el historial ya descargado.
- Respeta las reglas de porcentaje: con menos de 6 partidas decididas **no se muestra el porcentaje de victorias** (se muestra el récord V-D). Nunca un «0 %» por una única derrota. Si no hay ninguna decidida, «—», nunca 0 %.
- Las partidas sin grabación (`result == 0.5`) se muestran en gris como «Sin resultado / no contó», con el motivo real. Nunca como empate.
- Mantén intacta la lógica de `PlayerStanding` y `MatchResultResolver`; esto solo cambia la presentación.
- Añade o actualiza tests donde ya haya cobertura equivalente si cambias comportamiento observable.
- Compila y ejecuta los tests antes de darme cada commit por terminado.

Cuando acabes Clasificación, párate y enséñame una captura o el XAML resultante antes de seguir con Historial.
