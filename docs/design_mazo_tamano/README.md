# El mazo de la comunidad, al tamaño del juego

Segunda pasada sobre la sección «The community deck» de la pestaña Estadísticas, ya implementada. Referencias visuales en `Prototipo.html`: **26a** (la sección corregida), **26b** (el coste del tamaño de tarjeta) y **26c** (la franja de abajo, eliminada).

Repo: `Gorgorito12/AoE3-Mod-Launcher`, rama `main`, proyecto WPF `WarsOfLibertyLauncher/`.

| Qué | Archivos del repo |
| --- | --- |
| La sección | `Controls/MultiplayerTab.xaml.cs` — `BuildDecksSection` y sus ayudantes (`BuildDeckCivGroup`, `BuildDeckCardRow`, `BuildDeckCountCell`, `BuildDeckTailRow`) |
| El agregado | `Services/Multiplayer/DeckStatsView.cs` |
| Nombres de carta | el resolutor que alimenta `names.Resolved`; `Services/ModStringTable.cs` |
| Textos | `Localization/Strings.cs` — `MpStatsCommunityDecks*`, `MpStatsDecks*` |

**Esto se diseñó desde cinco capturas del usuario**: tres del inventario de cartas del propio AoE 3 y dos de la pestaña Estadísticas ya implementada. No releí el código en esta pasada.

El HTML es una **referencia de diseño**, no código para copiar. Todo el texto pasa por `Localization/Strings.cs` (español e inglés).

## El criterio: el juego ya lo resolvió

Las tres capturas del inventario de cartas de AoE 3 muestran el patrón, y es distinto del implementado:

- **Icono de ~52 px**, marco dorado, cuadrado con esquinas de radio pequeño.
- **Separación de ~5 px**, en filas apretadas.
- **El número en la esquina inferior derecha** del icono, en negrita blanca con sombra.
- **Ningún nombre debajo.**
- **El nombre y la descripción viven en el globo** al pasar por encima: título, la era en cian («This Card can be sent in Age 1»), y los efectos en líneas con viñeta.

En la primera captura, «Advanced Wonders» explica en cinco líneas qué hace la carta. Ese es el sitio del texto.

## El cambio principal: 52 px y sin nombre debajo

La implementación actual usa tarjetas grandes con el nombre a dos líneas debajo del arte. Tres consecuencias:

1. **Cuatro veces el área por carta** (104 × 118 frente a 52 × 52).
2. **El coste es vertical.** En la cuarta captura la sección ocupa cerca de 900 px y empuja el final de la página por debajo del borde inferior, así que hay que desplazarse para llegar a la última banda. La barra lateral no se desplaza: está entera en pantalla en las dos capturas, al lado de la sección.
3. **El nombre debajo era lo que impedía la descripción**: gastaba las dos líneas que el juego reserva para el globo, y además se corta igual («Recycling Railroad Tracks», «Campaña de Restauración»). Gastaba el espacio de la descripción para dar un nombre incompleto.

Con las 34 cartas de Peruvians: **cinco filas y ~590 px** frente a **cuatro filas y ~240**. Unos 350 px menos de alto — medidos en las maquetas, no en la build.

### La rejilla

- Icono **52 × 52**, radio 3, `gap` 5, con marco de 1 px `rgba(196,166,112,.5)`.
- **El número en la esquina**, 10 px, 700, blanco con `text-shadow`, solo cuando la carta lo tiene (cantidades y recursos: `3 Cholos` → 3, `Chests of 700 coin` → 700).
- **Sin nombre debajo.** Nada crece con el texto, así que las filas no se descuadran nunca.
- Las bandas por porcentaje se quedan tal cual están: son lo que funcionaba.

### El globo

- Fondo casi negro, borde dorado de 1 px, ancho ~322.
- **Nombre** 12/600 · **era en cian** `#63d0d8` · **efectos** en `#cfd6de` · separador · y al pie, en monoespaciada, **el dato que añade el launcher**: `in all 7 decks · 100 %`.
- **El texto sale de la tabla de textos del mod**, la misma fuente que resuelve los nombres. El launcher no lo escribe: en el prototipo el cuerpo es una línea estructural que lo dice.
- Se ancla al icono señalado y se superpone a las filas de abajo, como en el juego. En el prototipo sale pegado a la tercera carta del 100 %, y el resalte azul es **solo** de esa carta.

**Accesibilidad:** con el nombre solo en el globo, hace falta una vía sin ratón. Que el icono sea enfocable y que `Enter` o un clic abran el mismo panel; y `ToolTipService.ShowDuration` largo, porque el globo del juego tiene cinco o seis líneas.

## 26c — La franja de abajo, fuera

Ocupaba una franja de ancho completo para decir tres cosas, y **ninguna es de esta sección**:

- *Que tus mazos se comparten solos y dónde apagarlo* es una nota de privacidad. Su sitio es **Ajustes → Privacidad**, donde el usuario va cuando le importa. Anunciarlo bajo una rejilla de cartas no da opción: solo ocupa sitio.
- *Que diez es poca muestra* **ya lo dice la cabecera** («from 7 decks for Peruvians»), así que la franja lo repetía.
- *El botón «Open Settings»* era la única acción de toda la pestaña de Estadísticas, compitiendo con el contenido por la atención.

**Lo que sí se queda** es la nota de los identificadores internos, porque explica algo que se ve en pantalla: «Some of these cards are not in this mod's files, so they are shown by their internal identifier.»

## Lo que NO cambia

- **Las bandas por porcentaje**, con su denominador al lado («in 6 of the 7»). Esa parte quedó bien.
- **`BuildDeckCountCell` no dibuja nada** bajo el mínimo de muestra: ni guion ni «0 %».
- **Las dos cadenas distintas** para nombres sin resolver (todos / algunos).
- **El estado de la civilización elegida** vive en la pestaña, porque `RenderStatsTab` reconstruye la página entera en cada payload.

## Tokens

| Uso | Hex |
| --- | --- |
| Fondo de la pestaña | `#0f1c2e` |
| Contenedor de la rejilla | `#0d1828`, borde `rgba(130,175,255,.09)` |
| Marco de icono | `rgba(196,166,112,.5)` · señalado `rgba(140,188,245,.9)` + halo `rgba(47,127,224,.5)` |
| Globo: fondo | `#080b10` · borde `rgba(196,166,112,.55)` · separador `rgba(196,166,112,.22)` |
| Globo: texto | título `#f2f5f8` · era `#63d0d8` · cuerpo `#cfd6de` · pie `#8ea4c0` |
| Porcentaje de banda | consenso `#8cbcf5` · resto `#b9c9de` |
| Texto atenuado | `#8ea4c0` → `#6d829d` → `#5f7592` |

**Tipografía.** UI en Segoe UI. Porcentaje de banda 13 px monoespaciada 700. Número en la esquina 10 px 700. Conteos y el pie del globo en monoespaciada.

**Medidas.** Icono **52 × 52**, radio 3 · `gap` 5 · relleno del contenedor 14 · separación entre bandas 13 · globo ancho 322, relleno 9/11.

## Datos

- Las cartas de 26a son las **reales** de la quinta captura: Peruvians, 7 mazos, bandas de 8 / 6 / 8 / 12 al 100 / 86 / 71 / 57 %. Los números de esquina se han puesto donde el nombre de la carta los implica.
- **El cuerpo del globo es estructural**, no una descripción real: el texto tiene que venir de la tabla del mod.
- Los 350 px de diferencia de 26b están **medidos en las maquetas**, no en la build.

## Archivos de este paquete

- `Prototipo.html` — 26a, 26b y 26c.
- `PROMPT.md` — texto listo para pegar en Claude Code.
