# Publicar mi mod, y dónde se instala

Dos diálogos, para hacer en este orden.

| Orden | Parte | Spec | Archivo principal |
| --- | --- | --- | --- |
| 1 | Publicar mi mod (20a-20c) | `SPEC-1-publicar-mi-mod.md` | `PublishModDialog` |
| 2 | Dónde se instala (19a, 19b) | `SPEC-2-donde-se-instala.md` | `InstallFolderDialog` |

`Prototipo.html` contiene los dos, en ese orden. Ábrelo en un navegador y haz zoom; cada opción lleva su id (20b, 19a…) como etiqueta.

Repo: `Gorgorito12/AoE3-Mod-Launcher`, rama `main`, proyecto WPF `WarsOfLibertyLauncher/`.

**El HTML es una referencia de diseño, no código para copiar.** Recréalos en WPF/XAML con los estilos y controles que ya existen. Todo el texto pasa por `Localization/Strings.cs` en español e inglés.

## Lo que une los dos

**Los dos son formularios que no caben en su propia ventana, y los dos lo dicen en su código.**

`PublishModDialog` declara que las filas se mantienen compactas «so all six steps fit in the 640px window without needing nested scrollbars per panel» — y el paso 3, con diez campos, corta la etiqueta de su desplegable. `InstallFolderDialog` mide 560 px y sus dos rutas no caben en el campo, cada una recortada por un extremo distinto.

En los dos casos la corrección es la misma familia: **acotar la columna, agrupar los campos por la pregunta que responden, y no mostrar lo que no puede aplicar todavía.**

## Reglas comunes

1. **El contenido no se estira.** `PublishModDialog` tiene un `Grid` sin `MaxWidth` dentro de un `ScrollViewer`, así que un campo para un id de veinte caracteres mide el ancho de la ventana. Columna acotada y `HorizontalAlignment="Left"`.
2. **Lo condicional se colapsa.** El marcador de contenido del paso 3 solo se necesita cuando el archivo testigo también existe en el juego base; `Aoe3Row` de `InstallFolderDialog` ya se colapsa así y es el modelo a seguir.
3. **Un texto que se pide leer o copiar se muestra entero.** Las dos rutas del instalador, y el `mod.json` del paso 6.
4. **Un solo elemento sólido por pantalla, y es el que hace la cosa.** El paso 6 tiene hoy cuatro acciones, dos de ellas doradas sólidas.
5. **Ninguna etiqueta se parte en dos líneas ni se recorta**, tampoco traducida al inglés.
6. **Rutas, ids, hashes y tamaños en monoespaciada**, y los ejemplos con **una sola barra invertida** (`data\napoleonic.xml`).
7. **No inventes datos.** Cada spec marca lo que es propuesta y no dato verificado.

## Lo que no comprobé

Para que no lo des por verificado:

- **No leí ninguno de los dos `.xaml.cs`.** Leí los dos `.xaml` completos y la documentación de `pendingSettingsImportFrom`. Todo lo que dependa de lógica —qué valida `Next`, el valor por defecto del desplegable de mecanismo, a qué rama del JSON escribe cada par de URL+SHA— está listado al final de la spec 1 como pendiente.
- **La validación en línea del asistente ya existe** en el XAML (diez `Error*` que nacen `Collapsed`). Lo que no sé es si `NextButton` la respeta.
- **El motivo por el que la copia de ajustes no puede pasar al instalar** lo deduzco del catálogo, no del consumidor del campo. Confírmalo antes de explicarlo en la UI.
- **Las cifras de recorte de 19b** (347 px visibles, 28 y 167 perdidos) están medidas en mi maqueta, no en la build.

## Archivos de este paquete

- `Prototipo.html` — los dos diálogos.
- `SPEC-1-publicar-mi-mod.md`, `SPEC-2-donde-se-instala.md`.
- `PROMPT.md` — texto listo para pegar en Claude Code.
