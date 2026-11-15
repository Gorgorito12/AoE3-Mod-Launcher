# 2 · Diálogo «Nuevo torneo»: ningún texto cortado (diseño 108)

Prototipo: `Nuevo torneo v3.dc.html` (página) y `NuevoTorneo2.dc.html` (el diálogo). Anterior: `Nuevo torneo v2.dc.html` y `NuevoTorneo.dc.html`.

En el prototipo: 108a interactivo en español; 108b en inglés con Struggle of Indonesia (el caso que falla hoy); 108c 3v3; 108d inglés, 2v2 y nombre demasiado corto. En Tweaks se cambia el idioma.

## El problema

En `TournamentDialog.xaml`, «Quién entra» son dos casillas de ancho fijo con el texto en una sola línea, y la elegida pasa a negrita. El diseño se midió en español. En inglés, «First come, first served» es más largo, y en negrita crece otro poco: se sale de su casilla y el borde lo tapa.

De paso: la columna derecha va cargada (inscripción, plazas y tres cajas de cifras) y la izquierda se queda corta.

## La regla nueva, para todo el diálogo

1. **Lo elegido cambia de relleno y de borde, nunca de `FontWeight` ni de `FontSize`.** El texto mide lo mismo elegido o no. Todas las etiquetas de opción van en 600.
2. **Las opciones con frase van una por fila**, a todo el ancho, con su explicación dentro y `TextWrapping="Wrap"`.
3. **Ninguna casilla tiene `Height` fijo:** `MinHeight`, y crece con su texto.

| Estado de una opción | Fondo | Borde | Texto |
| --- | --- | --- | --- |
| Elegida | color del mod mezclado al 58 % con `#0F1C2E` | 1,5 del color del mod al 95 %, más sombra `0 2 12` del color del mod al 28 % | blanco; explicación `#DCE7F5` |
| Sin elegir | `#1A2C48` | 1 de `rgba(130,175,255,.22)` | `#DCE7F5`; explicación `#A8BCD2` |

## Disposición

Ancho del diálogo: **780** (antes 760). Dos columnas iguales con 12 de separación; las dos tarjetas tienen **la misma altura**. Relleno del cuerpo 16 · 22 · 16; 12 entre bloques.

**Columna izquierda, «el torneo»:** Mod · Nombre · Formato · Plazas.
**Columna derecha, «la inscripción»:** Quién entra · Quién lo ve · y, pegado abajo, «Abrir la inscripción al crearlo».

Debajo de las dos columnas, a todo el ancho y en este orden:

1. **Solo en 2v2 y 3v3:** la tarjeta «Cómo se forman los equipos».
2. La franja de resumen (las tres cifras).
3. Los botones del pie.

Alto resultante: unos 550 en 1v1 y unos 700 en 2v2 y 3v3. Tiene que caber en 1366 × 768.

## Pieza por pieza

**Filas de opción** («Quién entra», «Quién lo ve», «Cómo se forman los equipos»). Relleno 8 × 12, radio 9, 6 de separación entre filas. A la izquierda, un círculo de 16; a 10, el texto: título 13 / 600 y, 2 más abajo, la explicación en 12. Los dos con `TextWrapping="Wrap"`.

- Círculo sin elegir: aro de 1,5 de `rgba(130,175,255,.5)`.
- Círculo elegido: relleno del color del mod con un aro interior blanco de 5 (queda un punto del color del mod en el centro).

«Quién entra» y «Quién lo ve» apilan sus dos filas. «Cómo se forman los equipos» pone sus tres opciones en tres columnas iguales, con 6 de separación y la misma altura; la tarjeta lleva relleno 12 × 16.

**Formato** (1v1 · 2v2 · 3v3): tres casillas iguales, `MinHeight` 50, relleno 7 × 6, radio 8; arriba el formato en 14 / 600 y debajo la descripción en 11, centrados y con `TextWrapping="Wrap"`.

**Plazas** (2 · 4 · 8 · 16 · 32 · 64): seis casillas iguales de 36 de alto, Consolas 13 / 600. Debajo se queda la nota «Si se llenan las plazas, los siguientes quedan en espera.»

**Franja de resumen.** Sustituye a las tres cajas de cifras. Una sola caja a todo el ancho: relleno 7 × 14, radio 9, fondo del color del mod al 10 %, borde del color del mod al 35 %. Dentro, las tres cifras en línea (22 entre ellas, y saltan de línea si no caben): número en Consolas 14 / 700 blanco y, a 6, el texto en 12,5 `#DCE7F5`. Los textos son los de hoy: «jugadores» (en equipos, «jugadores (8 equipos de 3)»), «rondas hasta la final», «salas a la vez en la primera ronda».

**Botones del pie.** Sin ancho fijo: `MinWidth` 112 (Cancelar) y 136 (principal), alto 32, y si no caben saltan de línea. El aviso de nombre corto ocupa el espacio libre a la izquierda y puede bajar de línea.

**Trazos en vez de caracteres:** la ✕ de cerrar, la flecha del selector de mod (gira 180° al abrirse) y el visto del mod elegido en la lista.

## «Quién lo ve»

La app ya tiene «WHO SEES IT: Public / Private»; el diseño anterior no lo incluía. Aquí va como dos filas de opción con explicación:

| Opción | Explicación (es) | Explicación (en) |
| --- | --- | --- |
| Público / Public | Sale en la lista de torneos de todos. | Listed in everyone's tournament list. |
| Privado / Private | No sale en la lista: se entra con el código. | Not listed: people join with the code. |

**Comprueba en el código qué hace exactamente «Privado»** antes de escribir estas dos frases. Si no funciona así, dime cómo funciona y propón el texto.

## Textos

- **Nuevos:** las dos explicaciones de «Quién lo ve».
- **Cambia uno:** las explicaciones de «Quién entra» son las líneas que hoy salen debajo del segmentado. La de «Con aprobación» se queda igual («Cada inscripción espera a que la aceptes.»). La de «Por orden de llegada» repetiría el título, así que pasa a «Entran según se apuntan, hasta llenar las plazas.» / «They get in as they sign up, until the slots are full.»
- **Los demás no cambian:** usa los que ya hay en `Localization/Strings*.cs`. El inglés del prototipo es orientativo; el bueno es el de la app.

## Lo que NO cambia

- Qué campos hay, sus valores por defecto y lo que se envía al crear.
- El selector de mod, el contador y el aviso del nombre, el interruptor y el texto del botón principal («Crear y abrir inscripción» / «Crear como borrador»).
- Las tres transparencias y la franja con la imagen del mod.

## Orden sugerido y capturas

Commits: estilo de opción sin cambio de grosor → filas de opción para «Quién entra» y «Quién lo ve» → reparto de columnas → franja de resumen → fila de equipos → trazos y botones del pie.

Al terminar, tres capturas: en inglés con «First come, first served» elegido; en español 3v3; y en inglés 2v2 con el nombre de dos letras.
