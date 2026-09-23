# Texto para pegar en Claude Code

Copia esta carpeta dentro del repo (p. ej. `docs/design_insignias_rango/`), abre una terminal ahí, lanza `claude` y pega esto:

---

Lee `docs/design_insignias_rango/README.md` completo antes de escribir código. Es un sistema nuevo de **insignias de rango por edades de AoE 3** (Descubrimiento, Colonial, Fortalezas, Industrial, Imperial y Soberano para el 1.º) para este launcher WPF. Se muestra en la Clasificación, en la fila de sala y en el panel de jugadores de la sala.

Hay dos prototipos de referencia: `Prototipo-insignias.html` (la insignia y sus variantes) y `Prototipo-pantallas.html` (cómo queda en las tres pantallas). NO los copies ni los integres: recrea en WPF/XAML con los estilos y controles que ya existen. La marca de TOP 5 elegida es la de **43g** (bloque de honor); 43f y 43h son alternativas descartadas.

**No toques nada más.** Ni los anchos de columna de `RankingTableLayout`, ni la barra de rating, ni las banderas, ni ninguna otra pantalla.

## Antes de escribir código, hazme un plan

1. **Confirma la regla principal.** Lee `Services/Multiplayer/RankingTableLayout.cs` y confírmame que la tabla se ordena por `rating − 2·rd` y no por el rating impreso. La edad tiene que salir de ese orden (el puesto del servidor), nunca del rating que se ve. Si no, AleReis (1720, 4.º) saldría con más rango que los tres que tiene delante.
2. **Dime de dónde sacará la edad la fila de sala y la sala.** Hoy solo traen el ELO del anfitrión o del jugador, no su puesto. Propón si el servidor debe enviar la edad (o el puesto) en los DTO, o si el launcher la saca del ranking que ya tiene cargado. **No inventes un campo**: dime qué hay hoy en `LobbyDtos.cs`.
3. **Dime qué criterio usa hoy el launcher para decidir quién entra en la Clasificación** (partidas decididas, `ProvisionalRd`), para que Descubrimiento use el mismo y no un número nuevo.
4. **Propón dónde vive el cálculo de la edad:** un método puro sin WPF, con tests al estilo de `RankingTableLayoutTests`, que usen las tres pantallas.
5. Dime qué claves de `Localization/Strings.cs` hacen falta (los seis nombres de edad, «TOP 5») en español e inglés.
6. Propón commits pequeños, en este orden: el cálculo de la edad con sus tests → la insignia como control reutilizable, primero estática → la luz y las animaciones → Clasificación con el bloque TOP 5 → fila de sala → panel de la sala.

## Reglas al implementar

- **La edad sale del orden de la tabla, nunca del rating impreso.** Soberano es el puesto 1 del servidor, sin renumerar.
- **Un solo control de insignia** para las tres pantallas, que reciba edad, número a mostrar y tamaño. La geometría del escudo se declara una vez en recursos y la comparten placa, facetas y filo.
- **La luz sube con el rango y Descubrimiento no tiene ninguna.** Respeta la tabla de capas y duraciones del README. El reflejo es lento y más lento cuanto más alto el rango.
- **El golpe del número coincide con el paso del reflejo por el centro.** Los picos están medidos (12,4 %, 15,7 % y 18,9 % del ciclo). Si cambias una curva, vuelve a medir: no lo pongas a mitad de recorrido.
- **Ningún escudo repite patrón de chispas.** Sortea posición, tamaño y tiempo con una semilla derivada del puesto o del id del jugador, **nunca de un contador**: `RenderStatsTab` reconstruye la pestaña en cada payload y el patrón no puede cambiar en cada refresco.
- **El bloque TOP 5 no puede estrechar las filas.** Sin margen lateral, con el marco por dentro. Comprueba que la cabecera y las 14 filas tienen las columnas en la misma x.
- **En la fila de sala, la insignia ocupa el sitio del avatar** y la columna HOST sigue en 152 px.
- **Nada de `Clip`** en el contenedor de la insignia.
- **Numeral en monoespaciada con cifras alineadas.** Nada de Georgia.
- **Si el sistema tiene las animaciones desactivadas**, la insignia se queda quieta con sus colores.
- Cero cadenas literales: todo a `Localization/Strings.cs`.
- Señala cualquier punto del README que choque con la arquitectura real, con `CLAUDE.md`, con `.claude/rules/multiplayer.md` o con un test existente, en vez de forzarlo.
- Compila y pasa los tests antes de darme cada commit por terminado.

Cuando tengas la insignia estática de los seis rangos, párate y enséñame una captura antes de meter la luz y las animaciones.
