# Texto para pegar en Claude Code

Copia esta carpeta dentro del repo (p. ej. `docs/design_mazo_tamano/`), abre una terminal ahí, lanza `claude` y pega esto:

---

Lee `docs/design_mazo_tamano/README.md` completo antes de escribir código. Es una **segunda pasada** sobre la sección «The community deck» de la pestaña Estadísticas, que ya está implementada: las bandas por porcentaje se quedan, lo que cambia es el tamaño de la carta y la franja de abajo.

`Prototipo.html` es una referencia de diseño: NO lo copies ni lo integres. Recréalo en WPF/XAML con los estilos y controles que ya existen en `WarsOfLibertyLauncher/`.

**No toques nada más de la pestaña.** El balance de civilizaciones, los enfrentamientos, los mapas más jugados, «cómo se mide» y las tarjetas de la derecha quedan como están.

## El criterio

El propio AoE 3 ya resolvió cómo se muestra un mazo, y su patrón es distinto del implementado: **icono de ~52 px con marco dorado, separación de ~5, el número en la esquina inferior derecha, ningún nombre debajo, y el nombre y la descripción en el globo al pasar por encima.** Ese es el modelo. Si dudas de un detalle, mira las tres capturas del inventario de cartas que acompañan al README.

## Antes de escribir código, hazme un plan

1. **Localiza `BuildDeckCardRow`** (o lo que construya hoy la tarjeta de carta) y dime cómo está montada: tamaño, si el nombre va debajo y de dónde sale el arte del icono.
2. **Dime si el arte de la carta está disponible** en los archivos del mod, y si el launcher ya lo carga en algún sitio. Si no hay arte, dime con qué se dibuja el icono hoy.
3. **Dime si el número de la carta (cantidad o recurso) es un dato separado** o viene dentro del nombre resuelto (`3 Cholos`, `Chests of 700 coin`). Si viene dentro, dime si conviene extraerlo o dejarlo solo en el globo. **No lo inventes con una expresión regular frágil** si el dato existe aparte.
4. **Dime qué texto de descripción hay disponible** para una carta en la tabla del mod, y con qué clave. El globo lo necesita; si no existe, el globo muestra solo nombre, era y el dato del launcher — y me lo dices en vez de dejarlo vacío.
5. **Dime cómo resolver el acceso sin ratón.** Con el nombre solo en el globo hace falta que el icono sea enfocable y que un clic o `Enter` abran el mismo contenido. Propón cómo, y revisa `ToolTipService.ShowDuration`, porque el globo tiene cinco o seis líneas.
6. Dime qué claves de `Localization/Strings.cs` quedan huérfanas al eliminar la franja de abajo.
7. Propón commits pequeños: primero el icono de 52 sin nombre, luego el globo, luego la franja.

## Reglas al implementar

- **Icono 52 × 52, radio 3, `gap` 5, y ningún nombre debajo.** Nada en la rejilla crece con el texto, así que las filas no se descuadran nunca.
- **El número va en la esquina inferior derecha del icono**, blanco en negrita con sombra, y solo si la carta lo tiene.
- **El nombre completo y la descripción van en el globo**, con el formato del juego: título, la era en cian, los efectos, y al pie el dato que añade el launcher (`in all 7 decks · 100 %`). **El texto lo aporta el mod**, no el launcher.
- **El globo se ancla al icono señalado** y se superpone a las filas de abajo, como en el juego. Solo el icono señalado lleva el resalte.
- **Fuera la franja de «tus mazos se comparten automáticamente».** Esa nota de privacidad va en Ajustes → Privacidad; que la muestra es pequeña ya lo dice la cabecera; y su botón era la única acción de toda la pestaña. **Se queda** la línea de los identificadores internos, porque explica algo visible.
- **No toques las bandas por porcentaje ni su denominador** («in 6 of the 7»): esa parte quedó bien.
- **No rompas lo que ya está bien:** `BuildDeckCountCell` no dibuja nada bajo el mínimo de muestra; las dos cadenas distintas para nombres sin resolver; el estado de la civilización elegida viviendo en la pestaña porque `RenderStatsTab` reconstruye todo en cada payload.
- Cero cadenas literales: todo a `Localization/Strings.cs`, español e inglés.
- Respeta los valores exactos de la tabla de Tokens. Reutiliza los recursos compartidos del launcher; si creas un estilo nuevo (icono de carta, globo), hazlo compartido.
- Señala cualquier punto del README que choque con la arquitectura real, con `CLAUDE.md`, con `.claude/rules/multiplayer.md` o con un test existente, en vez de forzarlo.
- Compila y pasa los tests antes de darme cada commit por terminado.

Cuando acabes el icono de 52 sin nombre, párate y enséñame una captura antes de seguir con el globo.
