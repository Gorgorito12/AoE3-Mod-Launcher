# Texto para pegar en Claude Code

Copia esta carpeta dentro del repo (p. ej. `docs/design_simetria/`), abre una terminal ahí, lanza `claude` y pega esto:

---

Lee `docs/design_simetria/README.md` completo antes de escribir código.

El problema: **la ventana de ajustes de cada mod se reparte el espacio de forma distinta según el mod.** No es el nombre — es la URL del pie del carril lateral.

`ModPropertiesDialog.xaml` pone el carril en una `ColumnDefinition Width="Auto"` con `MinWidth="{StaticResource SetModRailWidth}"` (206), y su pie contiene `RailAuthorText` y `RailSiteText` **sin `TextTrimming` ni `TextWrapping`**. Una columna `Auto` mide lo que pide su hijo más ancho, y ese hijo es una URL que sale del `mod.json`. Resultado medido en tres mods, devuelto a DIP: el carril mide **200, 296 y 336 px**, y sus URLs tienen **19, 42 y 49 caracteres**. El orden coincide exactamente. El contenido, que es la columna `*`, se queda con 699, 603 y 563: **136 px de diferencia entre un mod y otro**.

`Prototipo.html` muestra las tres ventanas a escala real. Es una referencia de diseño: NO lo copies ni lo integres.

## Antes de escribir código, hazme un plan

1. **Reproduce la medida.** Abre los ajustes de Wars of Liberty y los de Knights and Barbarians y dime el `ActualWidth` del `Border` del carril en cada uno. Si no coinciden, el diagnóstico está confirmado.
2. **Lee el comentario de la tarjeta de cabecera** en `ModPropertiesDialog.xaml` — el que empieza «TWO THIRDS AND ONE THIRD, always». Describe este mismo defecto y cómo se resolvió allí, y dice que esa tarjeta era «the one place a long name or a long status could rearrange». Confírmame que el carril es un segundo sitio donde pasa lo mismo.
3. **Lee el comentario del carril en `LauncherSettingsDialog.xaml`**, el que explica la regla «MinWidth-not-Width». Quiero que el arreglo **preserve esa intención**: el carril debe seguir creciendo si el usuario sube el tamaño de texto, porque los rótulos de navegación son cadenas del launcher. Lo que no puede es crecer por un dato del mod.
4. **Busca el mismo patrón en el resto del proyecto.** Cualquier contenedor `Auto` o `SizeToContent` que tenga dentro, directa o indirectamente, un texto que venga del `mod.json`, del catálogo o del servidor. Dame la lista antes de tocar nada; me interesa más la lista que el arreglo puntual.
5. Propón commits pequeños, empezando por el carril.

## La regla

> **El ancho de un contenedor del launcher nunca puede depender de un dato del mod.**

Los rótulos de navegación, los títulos de sección y las etiquetas de botón son cadenas del launcher: acotadas, traducidas por ti, conocidas en tiempo de diseño. Que un contenedor mida a partir de ellas está bien. Los nombres de mod, autores, URLs, rutas, versiones y estados vienen de fuera: esos **se acotan y se recortan dentro del espacio que les toca**, nunca lo definen.

## Reglas al implementar

- **El arreglo del carril es sacar el texto del mod de la medida, no fijar el carril.** Da a los dos `TextBlock` del pie un `MaxWidth` igual al ancho útil del carril en su tamaño de referencia, más `TextTrimming="CharacterEllipsis"`. Así el carril sigue midiendo a partir de sus rótulos de navegación —que es lo que la regla `MinWidth` quiere— y la URL deja de participar.
- **Considera mostrar solo el dominio** en vez de la URL entera. En un pie de 10 px nadie lee `https://www.moddb.com/mods/knights-and-barbarians`; `moddb.com` dice lo mismo y el enlace sigue funcionando. Si lo haces, que sea el dominio, no una elipsis a mitad de palabra.
- **El nombre en la barra de título trunca.** Hoy «Age of Empires III: The Asian Dynastie» se mete por debajo de los iconos superpuestos de la barra.
- **No toques lo que ya está resuelto:** la cabecera (`Auto / 2* / * MaxWidth 200`) y el `MaxWidth="260"` del desplegable de versión están bien. El desplegable recortaba por falta de sitio en su contenedor, no por su cap: se arregla solo al devolverle los 136 px.
- **Añade un test a `DialogXamlTests`** que instancie el diálogo con un nombre, un autor y una URL absurdamente largos y verifique que el ancho del carril **no cambia** respecto a un mod de datos cortos. Sin ese test esto vuelve: el defecto solo se ve comparando dos mods, y nadie abre dos mods seguidos.
- **Defecto aparte, misma captura:** en el juego base la tarjeta «Stay on this version» aparece sin interruptor y sin descripción, pero **con el aviso ámbar** sobre quedarse atrás en actualizaciones — para algo que no tiene actualizaciones. Si se oculta el control, se oculta su advertencia. La caja está dentro del mismo `Border` que el `ToggleButton`, así que es un cambio de una línea.
- Cero cadenas literales nuevas: todo a `Localization/Strings.cs`, español e inglés.
- Compila y pasa los tests antes de darme cada commit por terminado.

Cuando tengas la lista del punto 4, párate y enséñamela antes de arreglar nada.
