# Base de diseño del launcher (obligatoria para todos los rediseños)

La referencia es `Biblioteca unificada.dc.html` (68a) y su ficha `Base de diseño.dc.html`. Todo menú, pestaña o diálogo nuevo del launcher sigue esta lógica. Respuestas al usuario en español (es-419, tuteo); textos de la interfaz en español e inglés.

## Estructura
- Pantallas a pantalla completa: fondo `#0f1c2e` con la imagen del mod (hero) o el resplandor de su color; capa de oscurecido a la izquierda/abajo; contenido encima en paneles de cristal.
- Ventanas y diálogos (Ajustes del mod y similares): la imagen del mod solo como franja arriba (~340 px), empezando en el borde superior detrás de la barra de título, que se funde con `#0f1c2e`, con el resplandor de su color debajo. La imagen a pantalla completa queda solo para la Biblioteca.
- Lista lateral a la izquierda (`clamp(320px,23cqw,460px)`, plegable a 72 px con cabecera fija y botón «/»); ficha o contenido a la derecha, alineado abajo cuando hay imagen.
- Sin topes de ancho en el layout: se reparte el espacio; el texto escala con `clamp()` y container queries (cqw). Probar a 1366, 1920 y 2560 px.
- Barra superior: título con tinte del color del mod; navegación MULTIJUGADOR · BIBLIOTECA; píldora de descargas global junto a «Conectado».

## Color
- Colores de estado FIJOS (nunca toman el color del mod): instalado/OK `#4fd68a`; actualización/info `#55ACEE`; error/detenido `#F4900C` (texto `#F5BD76`); favorito `#E6C06A`; destructivo `#c84a4a` (texto `#f0a6a6`); acción global `#2f7fe0`; versión: chip de cristal con icono de etiqueta verde `#7ee08b`.
- Color del mod (`accentColor` del catálogo, igualado en OKLCH a L 0,56–0,68 y C 0,11–0,16): selección, botón principal, filtro activo, barras de progreso, subrayado de la pestaña activa, tinte de la barra de título, barra de desplazamiento, enlaces «Leer más». Texto sobre el color: blanco u oscuro según contraste; texto en color: versión aclarada con contraste ≥ 5:1.
- Rellenos de selección con el color del mod (entrada activa, fila elegida, píldora o carta elegida): mezcla opaca del color con `#0f1c2e` al 58 %, nunca transparencia, para que el tono sea el mismo sobre la imagen y sobre el fondo liso.
- El color del mod solo va en lo que pertenece al mod que estás viendo. Menús y paneles globales (notificaciones, cuenta, descargas, ajustes del launcher) usan neutros; su barra de desplazamiento es `rgba(130,175,255,.28)`.
- Texto: `#f0f5fb` titulares · `#dce7f5`/`#c3d2e5` secundario · `#a8bcd2`/`#8C9CB1` atenuado · `#61779a` etiquetas.

## Cristal
- Niveles: Sólida `rgba(12,22,38,.86)` sin difuminado · Media (predeterminada) `rgba(10,19,33,.5)` + 16 px · Máxima `rgba(10,19,33,.28)` + 8 px. Borde interior `rgba(255,255,255,.08–.16)`.
- En WPF el difuminado se hace con una copia difuminada del hero recortada al panel; con efectos reducidos, fondos sólidos.
- Menús, popovers, avisos y paneles flotantes siguen el nivel de transparencia del usuario, con más difuminado porque van encima de texto: Sólida `rgba(12,22,38,.95)` + 18 px · Media `rgba(12,22,38,.74)` + 28 px · Máxima `rgba(12,22,38,.6)` + 32 px.
- El usuario lo elige en Ajustes › Interfaz › Apariencia (transparencia, imagen de fondo, color del mod, efectos, lista).

## Componentes (medidas)
- Botón principal: 52 alto, radio 10, 16/600, fondo color del mod + sombra de su color. Uno solo por vista.
- Secundario: 52 alto, cristal. Icono solo (Ajustes ⚙): 52×52 con tooltip. Pequeños: 32 alto, radio 7.
- En ventanas y diálogos, los botones secundarios y pequeños llevan fondo sólido `#1a2c48` con borde `rgba(130,175,255,.22)`, nunca transparente.
- Barra de título de ventanas y diálogos: 40 alto, un solo color uniforme en todo el ancho (sin degradado) y línea inferior `rgba(255,255,255,.06)`. Color: globales `#0b1422`; de un mod, su color mezclado al 22 % con `#0b1422`. Sigue el nivel de transparencia para fundirse con la imagen o el resplandor de detrás: Sólida opaca · Media al 42 % + 10 px de difuminado · Máxima al 30 % + 10 px. Su texto lleva una sombra suave (0 1 6 al 50 %) para leerse sobre la imagen.
- Entrada activa de un menú lateral: solo relleno suave (color del mod al 20 %, o neutro en menús globales) y texto blanco. Sin barra lateral ni borde de color.
- Chips de estado y versión: 24 alto, radio 12, 11/600, cristal; se distinguen por el punto o icono.
- Píldoras (filtros, enlaces): 30 alto, radio 15. Controles de barra: 34 alto, radio 8.
- Menú contextual y popovers: `rgba(12,22,38,.95)` + 18 px, radio 10–12, filas 32 alto; destructivo al final, separado y en rojo.
- Avisos flotantes: 420 de ancho, apilados abajo a la derecha; título en 1 línea y texto en 2 como mucho; acciones abajo a la derecha, la principal en el extremo, 32 alto y mínimo 96 de ancho.
- Confirmación: velo `rgba(5,10,18,.72)`, tarjeta 440 `#12213a` radio 12, botón destructivo `#c84a4a`.
- Diálogo pequeño (confirmación, aviso, un campo; ficha 103): barra de 40, icono de tono de 32 junto al titular (azul `#55ACEE` info · ámbar `#F5BD76` cuidado · rojo `#f0a6a6` destructivo · verde `#4fd68a` hecho), titular Source Serif 19/1,3, texto 13/1,55, pie con «Cancelar» sólido a la izquierda del principal. Un solo botón principal; Esc y ✕ equivalen a Cancelar; Enter al principal salvo si es destructivo. Ancho 440 sin campo · 480 con campo o detalle técnico (caja `#0d1828` en Consolas) · 520 con lista (filas de 44). Alto al contenido. Un tercer botón va a la izquierda del pie, sólido y separado.
- Tarjeta sobre velo dentro de la ventana cuando la pregunta nace de lo que se ve ahí y la respuesta no lleva a otra parte (abandonar, salir, quitar a alguien); ventana aparte cuando el aviso llega de fuera, pide escribir o debe sobrevivir a que se cierre la vista. Misma medida y tono que el diálogo pequeño, sin barra.
- El tono lo decide el botón principal: `#c84a4a` si no se deshace; `#2f7fe0` si es del launcher; color del mod si es de un mod (y la barra se tiñe). Nunca dos botones de color.
- Menús y popovers: filas de 32 con texto 13/1,4, sin iconos salvo puntos de estado, etiquetas de grupo 10,5 mayúsculas `#61779a`, separador 1 px, destructivo al final en `#f0a6a6`. Los de barra (conexión, cuenta) llevan cabecera con avatar o punto y una línea de estado; nunca color de mod. Abren junto a lo que los invocó; si no caben, se voltean, nunca se recortan.
- Sin MessageBox de Windows: todo aviso pasa por el diálogo pequeño.
- Progreso: tarjeta de cristal, barra 8 px del color del mod, %, velocidad, tiempo, Pausar/Cancelar; también barra fina en la fila de la lista y píldora global.
- Tipografía: Segoe UI; títulos Source Serif 4 700; números y versiones en Consolas.
- Texto en una línea recortado con puntos suspensivos (`overflow:hidden` + `nowrap`; en WPF `TextTrimming`): interlineado mínimo 1,4, nunca 1,2–1,3, porque el recorte se come los trazos bajos de g, p, j, y.

## Reglas de interacción
- El clic derecho es un atajo: nunca es el único sitio de una acción y no cambia la selección; solo «Jugar» navega.
- Nada que parezca funcionar sin hacer nada: lo que no tiene datos no se muestra («Leer más» solo si el texto está cortado; selector de copias solo con 2+ copias; capturas solo si existen).
- No inventar datos: si el launcher no guarda un dato, se dice que hay que empezar a guardarlo.
- Al entregar algo para Claude Code: texto listo para pegar + HTML de referencia.
