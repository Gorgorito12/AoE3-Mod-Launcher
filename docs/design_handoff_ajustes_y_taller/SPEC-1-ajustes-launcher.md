# Ajustes del launcher

Referencias visuales: **4a** (General), **4b** (Partidas), **4c** (Avanzado) y **4d** (Mods y actualizaciones) en `Prototipo.html` — ábrelo en un navegador y haz zoom para leer los detalles.

Archivo principal: `SettingsDialog.xaml.cs` (+ su `.xaml`).

## El cambio estructural: 7 secciones → 5

| Antes | Ahora |
| --- | --- |
| General | **General** (4a) |
| Interfaz | **Interfaz** — sin cambios de contenido |
| — | **Partidas** (4b) — nueva: recoge lo relativo a grabación, lanzamiento y red |
| Actualizaciones + Catálogo | **Mods y actualizaciones** (4d) |
| Mantenimiento + Privacidad + Desarrollador | **Avanzado** (4c) |

Mantenimiento, Privacidad y Desarrollador ocupaban tres entradas del menú para llenar media pantalla entre las tres. Actualizaciones y Catálogo se explicaban la una a la otra: el canal de actualización estaba separado del catálogo del que salen los mods.

## Reglas transversales (las cuatro secciones)

1. **El contenido no se estira.** Columna derecha con `MaxWidth="620"` y `HorizontalAlignment="Left"`, padding 18/20. Hoy las descripciones cruzan toda la pantalla y son ilegibles en un monitor ancho.
2. **Casillas → interruptores.** Once casillas seguidas con el mismo peso obligan a leerlas todas para encontrar una. Un `ToggleSwitch` de 34×20 (radio 999, encendido `#2f7fe0`, apagado `rgba(255,255,255,.12)`, pulgar 16 px blanco/`#9fb3cd`) se lee de un vistazo por su color.
3. **Agrupación en tarjetas con encabezado.** Cada grupo es una tarjeta (radio 9, `#12213a`, borde `rgba(130,175,255,.11)`) precedida de una etiqueta 10.5/600 `letter-spacing:.6px` `#61779a` en mayúsculas. Las filas dentro se separan con una línea interior `rgba(130,175,255,.08)`, no con margen.
4. **Fila de ajuste**: título 12.5/600 `#e8eef6`; descripción 11.5/400 `#8ea4c0` `line-height 1.5`, una o dos líneas como mucho; control a la derecha con `margin-top:1px` para alinearlo con la primera línea del título.
5. **Los botones de acción van en columna fija de 112 px**, centrados. Hoy el ancho del botón cambia con el largo del texto y la columna derecha queda en zigzag.
6. **Etiqueta RECOMENDADO** (9/600 `#8fe0b0` sobre `rgba(53,196,111,.14)`, radio 3) junto al título de los ajustes que no conviene desactivar. Sustituye a las explicaciones defensivas en la descripción.
7. **Buscador de ajustes** en la cabecera del menú lateral (alto 32, radio 7, `#0d1828`): filtra filas de todas las secciones y salta a la que corresponda.
8. **Pie**: «Los cambios se aplican al instante» + botón Cerrar. El par Cancelar/Guardar solo aparece cuando hay un cambio que requiere confirmación.
9. Menú lateral 216 px, fondo `#16263e`. Entrada activa: fondo `rgba(47,127,224,.16)` + barra izquierda de 2 px `#2f7fe0`, texto `#f0f5fb` 600. Inactiva `#b9c9de` 500. Puede llevar un punto ámbar (atención requerida) o una pastilla con un número (actualizaciones pendientes). Al pie, nombre y versión del launcher.
10. **Ninguna entrada del menú parte en dos líneas.** Las etiquetas van con `TextTrimming="None"` y sin ajuste de línea; el punto o la pastilla se separan con un espaciador flexible, no empujando al texto. Una entrada que se parte cambia la altura de esa fila y el menú deja de coincidir entre secciones. Si al traducir al inglés una etiqueta no cupiese, se ensancha el menú o se acorta la etiqueta — nunca se deja envolver.
11. **La columna de acciones de 112 px se mantiene alineada entre tarjetas adyacentes.** En las filas con menú `⋯` (mods instalados), ese icono va FUERA de los 112 px; en las filas sin menú se reserva igualmente su hueco de 14 px, para que el borde derecho de los botones caiga en la misma vertical en toda la sección.

## 4a — General

Cuatro grupos, en este orden:

- **Idioma** — fila suelta arriba, con desplegable. «Se aplica al instante».
- **INICIO** — arrancar con Windows en segundo plano (RECOMENDADO, explica que apareces conectado sin abrirlo), minimizar a la bandeja al cerrar, cerrar el launcher al empezar la partida.
- **AVISOS** — avisarme cuando alguien abre una sala (RECOMENDADO), dejar que me inviten, avisarme al terminar una actualización, y sonidos con un botón «Probar» junto al interruptor.
- **CONEXIÓN** — enlaces «Unirse» de Discord, y el asistente de Radmin VPN como desplegable (Automático / Siempre / Nunca) en vez de casilla: son tres estados, no dos.
- **Modo desarrollador** — fila suelta al final, sin tarjeta, con el texto en `#c3d2e5` atenuado. Al activarlo se despliega el bloque de herramientas de 4c.

## 4b — Partidas (sección nueva)

Las dos opciones de grabación son las que deciden si el ELO funciona, y hoy están perdidas en mitad del montón. Aquí abren la sección, con la explicación de la consecuencia real:

- **GRABACIÓN** — «Marcar Record Game automáticamente» (RECOMENDADO) y «Guardar una copia de la repetición». La descripción dice qué pasa si no: sin grabación no se puede determinar el ganador y la partida no cuenta para el ELO. Un aviso ámbar aparece si la opción está desactivada.
- **LANZAMIENTO** — ruta del ejecutable de AoE3 (monoespaciada con elipsis + botón «Cambiar» en la columna de 112 px), argumentos adicionales, y qué hacer al terminar la partida.
- **RED** — puerto P2P, medición de ping, y el aviso de que Radmin VPN debe estar activo.

## 4c — Avanzado

- **MANTENIMIENTO** — vaciar caché de iconos, borrar temporales (con el tamaño en monoespaciada, `318 MB`), abrir la carpeta de datos (ruta monoespaciada con elipsis), y versión del launcher con una pastilla verde `v2.4.1 al día` + botón «Comprobar».
- **Instalar en este PC** — caja azul destacada (`rgba(47,127,224,.09)`, borde `rgba(47,127,224,.24)`) con icono 28 px. Es la única acción de la sección que el usuario quiere encontrar, así que no va dentro de la lista gris.
- **PRIVACIDAD** — registro local de diagnóstico (interruptor, con la aclaración de que no se envía a ningún sitio) y política de privacidad con botón «Ver».
- **DESARROLLADOR** — bloque plegado con `▸` y el texto «Activa el modo desarrollador» a la derecha. Se despliega solo cuando el interruptor de 4a está encendido.
- `DlgSettingsPreviewToasts` y `DlgSettingsPreviewToastsHint` aparecen como claves sin traducir en la build actual: hay que darlas de alta en `Strings.cs`.

## 4d — Mods y actualizaciones

Orden según el flujo real: de dónde vienen los mods, cómo se actualizan, qué hay instalado.

- **Banner de actualización disponible** arriba, en azul, con el tamaño del parche, la fecha, un enlace «Ver cambios» y el botón «Actualizar». Solo aparece si hay algo pendiente.
- **ACTUALIZACIONES** — actualizar mods automáticamente (RECOMENDADO; la descripción explica la consecuencia: si tu versión no coincide con la del anfitrión no puedes entrar en su sala), descargar solo lo que cambió, canal Estable/Beta como segmentado, y límite de descarga como desplegable.
- **MODS INSTALADOS** — encabezado con el total (`3 · 14,2 GB`). Una fila por mod: icono 30 px, nombre, y una línea monoespaciada con `versión · tamaño` más **el estado de la huella** (punto de color + primeros 6 caracteres del `mod_combined_hash`). Acción a la derecha en la columna de 112 px: «Actualizar» (sólido) / «Al día» (texto atenuado) / «Reparar» (fantasma), más un `⋯` de 14 px para desinstalar, abrir carpeta y recalcular la huella.
  - Estado `HUELLA DISTINTA` en ámbar cuando los archivos se han tocado a mano. Debajo de la tarjeta, una línea de 11 px explica qué es la huella y qué hace «Reparar». Ni el tamaño ni la versión aparecen hoy en ninguna parte de la UI, y son lo primero que se pregunta.
- **CATÁLOGO** — origen (URL monoespaciada con elipsis + «Cambiar»), refrescar (con «Actualizado hace 12 min · 9 mods disponibles» + «Refrescar») y verificar la firma de las descargas (RECOMENDADO).

(Los tokens, la tipografía y el espaciado están en el README de la carpeta.)
