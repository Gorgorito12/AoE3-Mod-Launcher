# Ajustes del launcher, Ajustes del mod y Taller

Rediseño de tres pantallas del AoE3 Mod Launcher, para implementar en este orden. Todas comparten una sola paleta y un solo juego de controles, y esa es la mitad del trabajo: hoy son tres pantallas que parecen de tres aplicaciones distintas.

Repo: `Gorgorito12/AoE3-Mod-Launcher`, rama `main`, proyecto WPF `WarsOfLibertyLauncher/`.

| Orden | Pantalla | Spec | Archivo principal del repo |
| --- | --- | --- | --- |
| 1 | Ajustes del launcher (4a-4d) | `SPEC-1-ajustes-launcher.md` | `SettingsDialog.xaml.cs` |
| 2 | Ajustes del mod (5a-5d) | `SPEC-2-ajustes-mod.md` | `ModPropertiesDialog.xaml.cs` |
| 3 | Taller (6a) | `SPEC-3-taller.md` | `Controls/ModsBrowser.xaml` + `.xaml.cs` |

`Prototipo.html` contiene las tres, en ese orden. Ábrelo en un navegador y haz zoom para leer los detalles; cada opción lleva su id (4a, 5b, 6a…) como etiqueta.

**El HTML es una referencia de diseño, no código para copiar.** Recrea las pantallas en WPF/XAML con los estilos y controles que ya existen en el proyecto. Todo el texto pasa por `Localization/Strings.cs` en español e inglés; nada de cadenas literales en el XAML.

---

## Decisión de color: una sola paleta

Esta ventana y los ajustes del launcher **comparten la paleta azul del launcher**. Antes eran dos sistemas distintos (azul frente a dorado sobre gris) y no había razón para que un interruptor fuese de un color en una ventana y de otro en la de al lado.

El dorado no desaparece: se reduce a **un único trabajo, la identidad del mod**, y solo aparece en tres sitios de toda la ventana:

1. El nombre del mod en la barra de título.
2. La pastilla de versión de la barra de título.
3. La cabecera del mod en General — su borde y la cifra de versión instalada.

Todo lo demás — interruptores, entradas activas del menú, botones, enlaces, estados — usa el azul `#2f7fe0` / `#8cbcf5` igual que el launcher. Verde, ámbar, rojo y violeta son los mismos que allí. Si añades una superficie nueva, sale de la tabla de tokens de abajo, no de un dorado nuevo.

---

## Controles compartidos

Las tres pantallas usan los mismos. Hazlos **una vez** como recursos compartidos y reutilízalos; no los copies por pantalla.

- **Interruptor** 34×20, radio 999. Encendido `#2f7fe0` con pulgar blanco de 16 px; apagado `rgba(255,255,255,.13)` con pulgar `#9fb3cd`. Sustituye a todas las casillas de las dos ventanas de ajustes.
- **Fila de ajuste**: título 12.5/600 `#e8eef6`, descripción 11.5/400 `#8ea4c0` con `line-height 1.5` (una o dos líneas), y el control a la derecha con `margin-top:1px` para alinearlo con la primera línea del título.
- **Tarjeta de grupo**: radio 9, fondo `#12213a`, borde `rgba(130,175,255,.11)`. Las filas se separan con `inset 0 -1px 0 rgba(130,175,255,.09)`, nunca con margen. Encima, la etiqueta del grupo en 10.5/600, `letter-spacing:.6px`, `#61779a`, mayúsculas.
- **Botón de acción en columna de ancho fijo** — 88 px (Abrir, Cambiar, Restaurar, Usar, Importar), 112-132 px (Verificar, Reparar, Comprobar, Actualizar) o 158 px en el Taller. El ancho no depende del largo de la etiqueta: si depende, la columna derecha queda en zigzag.
- **Etiqueta de estado**: 9-9.5/600, `letter-spacing:.4px`, radio 3-4, sobre un fondo del 14-20 % del color de su estado.
- **Caja de aviso ámbar**: fondo `rgba(230,180,85,.08)`, borde `rgba(230,180,85,.22)`, icono a la izquierda, texto 11-11.5/400 `#d8bd8a`. Para advertencias con consecuencia real, no para explicaciones largas.
- **Caja de peligro roja**: borde `rgba(200,120,120,.28-.5)` sin relleno, texto `#d99a9a`, botón fantasma al tamaño de su etiqueta.

## Reglas que valen para las tres

1. **El contenido no se estira.** Columna con `MaxWidth` y `HorizontalAlignment="Left"` — 620 px en las dos ventanas de ajustes. En un monitor de 2540 px, sin esto, una descripción es una línea de 200 caracteres y un botón mide media pantalla. Es el defecto que más se repite hoy.
2. **Ninguna etiqueta se parte en dos líneas ni se recorta.** Ni las entradas del menú lateral, ni las pastillas, ni las cabeceras de tabla — tampoco traducidas al inglés. Si no cabe, se ensancha el contenedor o se acorta la etiqueta; nunca se deja envolver, porque una fila más alta desalinea toda la columna.
3. **Rutas, versiones, tamaños, fechas y hashes en monoespaciada**, con elipsis cuando no caben. Es lo que mantiene las columnas alineadas y lo que hace legible un hash.
4. **Reserva 138 px al final de la barra de título** para los botones nativos de Windows (46 px cada uno). Ningún contenido propio entra en esa zona, o Windows lo pinta encima.
5. **Nada de jerga interna en la UI.** `Isolated folder` y `WoL patcher (UpdateInfo.xml)` se dicen en lenguaje de jugador.
6. **No inventes datos ni backend.** Cada spec lista los datos que pide; si alguno no existe hoy, dilo en el plan y omite ese elemento en vez de mostrarlo vacío.

## Tokens

| Uso | Hex |
| --- | --- |
| Fondo de contenido | `#0f1c2e` |
| Barra de título | `#233648` |
| Menú lateral | `#16263e` |
| Sub-barra de pestañas, tarjeta, panel | `#12213a` |
| Fila seleccionada o destacada | `#16263e` |
| Tarjeta atenuada / fila secundaria | `#101d31` |
| Campo, desplegable, cápsula segmentada | `#0d1828` |
| Borde interior | `rgba(130,175,255,.07–.20)` · seleccionada `rgba(47,127,224,.42)` |
| Azul de acción | `#2f7fe0` · texto sobre oscuro `#8cbcf5` / `#8fb6ea` / `#a8ccf5` |
| Dorado — SOLO identidad del mod | `#d9b26a` · pastilla `#c2a05a` sobre `rgba(217,178,106,.12)` · borde `rgba(217,178,106,.22)` |
| Texto primario | `#e8eef6` · titulares `#f0f5fb` / `#f4f8fc` |
| Texto secundario | `#b9c9de` / `#c3d2e5` / `#dce7f5` |
| Texto atenuado | `#8ea4c0` → `#6d829d` → `#61779a` → `#5f7592` |
| Verde OK / instalado | `#4fd68a` · texto `#8fe0b0` / `#7fdca6` sobre `rgba(53,196,111,.14)` |
| Ámbar aviso / provisional | `#e6b455` · texto `#d8bd8a` / `#e0be7f` / `#f0dcae` |
| Rojo peligro | `#c8686e` · texto `#d99a9a` / `#e8b3b3` |
| Violeta (privado, instalador, nuevo) | `#a06ef0` · texto `#cba7f2` sobre `rgba(160,110,240,.16)` |

**Tipografía.** UI en Segoe UI. Títulos de ventana y de sección, nombres de mod y cifras grandes en serif — `Source Serif 4` en el prototipo; en WPF, Georgia o la serif que ya usa el título dorado. Datos numéricos y técnicos en monoespaciada. Escala: 9 · 9.5 · 10.5 · 11 · 11.5 · 12 · 12.5 · 13 · 13.5 · 15 · 16 · 17 · 18 px. Etiquetas de sección 9.5-10.5 px, 600, `letter-spacing` .5-.7 px, mayúsculas.

**Espaciado**: 3 · 4 · 6 · 7 · 8 · 9 · 10 · 11 · 12 · 13 · 14 · 16 · 18 · 20 px. Padding de fila 11-13 / 14; de columna de contenido 18/20.

**Radios**: 3-4 (etiquetas) · 6-7 (botones, campos, filas del menú) · 8 (filas, cajas internas) · 9-11 (tarjetas, paneles) · 12-14 (iconos grandes) · 999 (interruptores, pastillas).

**Alturas**: barra de título 38-40 · sub-barra 48 · fila del menú ~34 · fila de lista ~86 · botón de acción 28-34 · botón primario 36-42 · campo 32-40 · interruptor 20.

## Archivos de este paquete

- `Prototipo.html` — las tres pantallas.
- `SPEC-1-ajustes-launcher.md`, `SPEC-2-ajustes-mod.md`, `SPEC-3-taller.md`.
- `AppIcon.png`, `WoL.ico` — iconos que usa el prototipo (ya están en el repo).
- `PROMPT.md` — texto listo para pegar en Claude Code.
