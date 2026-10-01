# Share diagnostics: más visible y fácil de encontrar en el buscador

Los jugadores no encuentran **Share diagnostics**, y es lo primero que pide el equipo en Discord. Este paquete hace dos cosas:

1. **Darle peso visual** en Local files y un acceso fijo desde cualquier sección de la ventana del mod (50a).
2. **Que el buscador lo encuentre** al escribir «diagnostico», «diagnóstico», «reporte», «error», «crash»…, en cualquier idioma de interfaz (50b, 50c).

Repo: `Gorgorito12/AoE3-Mod-Launcher`, rama `main`, proyecto WPF `WarsOfLibertyLauncher/`.

| Archivo del paquete | Qué contiene |
| --- | --- |
| `Local files.dc.html` | Prototipo. **Turno 50** arriba (50a, 50b, 50c). El turno 49, debajo, es el diseño de Local files que ya está en main: sirve de contexto y no cambia. Ábrelo en un navegador (necesita `support.js` y `WoL.ico` al lado). |

El HTML es **referencia de diseño**, no código para copiar. Recréalo en WPF/XAML con los estilos que ya existen. Todo el texto va en `Localization/Strings.cs` (español e inglés). El diseño es de alta fidelidad: los colores y las medidas de abajo son los finales.

| Qué | Archivos del repo |
| --- | --- |
| Tarjeta «Something not working?» | `ModPropertiesDialog.xaml` (~672, `ShareDiagnosticsBtn`, `ViewLogsBtn`, Verify, Discord) y `.xaml.cs` (~314, ~322, ~590, ~658-681: reparto en rejilla de View logs y Share diagnostics) |
| Acción de diagnóstico | `ModPropertiesDialog.xaml.cs`: `_shareDiagnostics` (Action inyectada en el constructor, ~135/174) y `ShareDiagnosticsBtn_Click` (~2553) |
| Buscador | `ModPropertiesDialog.xaml` (~194 `ModSearchBox`, ~197 `ModSearchPlaceholder`, ~330 `ModSearchNoResults`), `.xaml.cs` (~1988 `ModSearchBox_TextChanged`, ~2003 `SectionSearch.Apply`) |
| Motor de búsqueda | `Controls/SectionSearch.cs` (compartido con `LauncherSettingsDialog`) |

---

## 1. Por qué hoy no se encuentra (leído en `SectionSearch.cs`)

- **`TextOf` solo lee TextBlocks** del árbol lógico. El propio comentario lo dice: un Button cuyo Content es una cadena **no se lee**. `ShareDiagnosticsBtn.Content = Strings.Get("ModPropShareDiagnostics")` es una cadena, así que el botón es invisible para la búsqueda.
- **`Matches` busca la cadena entera.** Ya ignora mayúsculas y tildes (`IgnoreCase | IgnoreNonSpace`), pero «diagnostico» no está dentro de «diagnostics». Con la interfaz en inglés, quien escribe en español no encuentra nada.

## 2. 50a · Tarjeta «Something not working?» en dos pasos

Hoy es una rejilla 2×2 en la que Share diagnostics pesa lo mismo que View logs. Pasa a ser una columna con `gap` de 10 px:

1. **Cabecera** `SOMETHING NOT WORKING?`, sin cambios (10,5 px, semibold, espaciado 0,6, `#7d8b96`).
2. **Paso 1.** Una fila con el número `1` (Consolas 10,5 px bold, `#8fb6ea`) y el texto que ya existe, «Start with Verify: it checks every file without downloading anything.» (11,5 px, `#8ea4c0`). Debajo, el botón **✓ Verify files**, sólido azul `#2f7fe0`, a todo el ancho, 34 px de alto, radio 7.
3. **Paso 2: el bloque de diagnóstico.**
   - Contenedor: fondo `rgba(60,198,201,.09)`, borde interior de 1 px `rgba(60,198,201,.45)`, radio 9, padding 11×12, separación interna de 9 px.
   - Número `2` en `#7fe0e2`.
   - Título: **«Still failing? Share diagnostics»** (13 px semibold, `#e6fbfb`).
   - Texto: «Saves your logs and setup in one file. Post it on Discord so the team can see what went wrong.» (11,5 px, `#a9d6d8`).
   - Botón **⇪ Share diagnostics**, sólido turquesa `#3cc6c9`, texto `#032526` en bold 12,5 px, a todo el ancho, 36 px de alto, radio 7. Ejecuta `_shareDiagnostics`, igual que hoy.
4. **Fila secundaria.** Rejilla de dos columnas iguales con `gap` de 8: **View logs** y **Ask on Discord ↗**, con contorno (borde `rgba(130,175,255,.2)`, texto `#c3d2e5`), 30 px de alto. El botón negro de Discord que hay hoy pasa a ser de contorno.

**Por qué turquesa:** el azul ya es Verify, el dorado está reservado a la identidad del mod y el rojo es desinstalar. El turquesa solo se usa para el diagnóstico.

La lógica de `~658-681`, que reparte View logs y Share diagnostics en una o dos filas según el ancho, deja de hacer falta para Share diagnostics. Mantenla solo si View logs y Discord la necesitan.

La tarjeta de la izquierda (copia activa) y el resto de la página **no cambian**.

## 3. 50a · Acceso fijo en la barra lateral

En el carril de la ventana del mod, encima del pie «Wars of Liberty Team / aoe3wol.com» (12 px por encima):

- Caja: fondo `rgba(60,198,201,.1)`, borde interior de 1 px `rgba(60,198,201,.38)`, radio 8, padding 10×11.
- Línea 1: `PROBLEMS?` (10 px semibold, espaciado 0,6, `#7fe0e2`).
- Línea 2, 6 px por debajo: `⇪` (`#7fe0e2`) seguido de **Share diagnostics** (12,5 px semibold, `#e6fbfb`).
- Toda la caja es un botón que ejecuta la misma `_shareDiagnostics`. **No** navega a Local files.
- Se ve desde todas las secciones.

**Cuidado:** el carril está en una columna Auto y su ancho ya lo fija la URL del pie (ver la nota de simetría en `github.md`). Esta caja no debe ensancharlo: ponle `TextTrimming` o un ancho máximo igual al del carril.

## 4. 50b · Buscador

### Palabras clave (obligatorio)

Crea una propiedad adjunta `SectionSearch.Keywords` (string) y haz que `TextOf` añada su valor al texto del elemento que la tenga. Así `Matches` no cambia y sus tests siguen valiendo.

- Las palabras van en `Strings.cs`, una clave por elemento. **Se cargan siempre las de los dos idiomas**, sea cual sea el idioma de la interfaz.
- `TextOf` debe leer la propiedad en cualquier descendiente, incluidos los Buttons, y no solo en TextBlocks.

| Elemento | es | en |
| --- | --- | --- |
| `ShareDiagnosticsBtn` y la caja del carril | diagnóstico diagnósticos informe reporte error fallo | diagnostics diagnose report error crash bug support |
| `ViewLogsBtn` | registros diagnóstico error | logs log error |

Revisa si el resultado es una fila (`SetRow`/`SetActionRow`…) o un hijo directo sin filas. Si la tarjeta «Something not working?» no tiene filas, la propiedad debe estar dentro de ella para que `FilterPanel` la encuentre.

### Lista de resultados (capa nueva)

Hoy `SectionSearch.Apply` abre la primera sección con resultados y filtra. Por encima de eso:

- **Lista desplegable** bajo `ModSearchBox`, del ancho de la caja de búsqueda o algo más ancha (330 px en el prototipo), con fondo `#1b2d48`, borde `rgba(130,175,255,.22)`, radio 9 y sombra `0 10 28 rgba(0,0,0,.45)`. Encima del contenido.
- **Muestra hasta 5 elementos** con `Keywords` que coincidan. Cada uno tiene icono de 26×26, título (12,5 px semibold) y la ruta «Local files › Something not working? · *palabra que coincidió*» (10,5 px). La palabra va en `#7fe0e2` en el resultado de diagnóstico.
- **El primero está seleccionado** (fondo `rgba(60,198,201,.14)` y borde `rgba(60,198,201,.4)` en el de diagnóstico). Se recorre con ↑↓, se abre con ↵ o clic y se cierra con Esc. Pie: «↑↓ to move · ↵ to open».
- **Al abrir un resultado:** se activa su sección, se mantiene el filtro de hoy y el elemento lleva un anillo de 4 px `rgba(60,198,201,.22)` durante unos 3 s, que luego se desvanece. Respeta `SystemParameters.ClientAreaAnimation`: sin animaciones, el anillo aparece y desaparece sin transición.
- La cabecera de la sección dice «2 results for «diagnostico»» (11,5 px, `#8ea4c0`).

Si la lista se complica, **las palabras clave bastan por sí solas**: con ellas, la búsqueda actual ya lleva a Local files y deja la tarjeta a la vista. Haz primero las palabras clave y luego la lista.

## 5. Valores

| Token | Valor |
| --- | --- |
| Turquesa sólido | `#3cc6c9` (texto encima `#032526`) |
| Turquesa claro (texto) | `#7fe0e2` |
| Título sobre turquesa | `#e6fbfb` |
| Texto secundario sobre turquesa | `#a9d6d8` |
| Fondo del bloque | `rgba(60,198,201,.09)`; carril `.10`; resultado `.14` |
| Borde del bloque | `rgba(60,198,201,.45)`; carril `.38`; resultado `.40` |
| Azul Verify | `#2f7fe0` |
| Fondos | ventana `#0f1c2e` · tarjeta `#12213a` · carril `#16263e` |
| Fuente | Segoe UI; números en Consolas |

## 6. Textos nuevos (Strings.cs)

| Clave sugerida | en | es |
| --- | --- | --- |
| `ModPropDiagStep2Title` | Still failing? Share diagnostics | ¿Sigue fallando? Comparte el diagnóstico |
| `ModPropDiagStep2Body` | Saves your logs and setup in one file. Post it on Discord so the team can see what went wrong. | Guarda tus registros y tu configuración en un archivo. Publícalo en Discord para que el equipo vea qué falló. |
| `ModPropRailProblems` | PROBLEMS? | ¿PROBLEMAS? |
| `ModSearchResultsCount` | {0} results for «{1}» | {0} resultados para «{1}» |
| `ModSearchHint` | ↑↓ to move · ↵ to open | ↑↓ para moverte · ↵ para abrir |
| `SearchKw_ShareDiagnostics_es/en`, `SearchKw_ViewLogs_es/en` | ver tabla de §4 | |
