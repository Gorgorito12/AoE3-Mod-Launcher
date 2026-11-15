# 1 · Botón «Update launcher» en dorado (diseño 110)

Prototipo: `Botón actualizar launcher.dc.html`. Captura de hoy: `uploads/boton-actualizar-hoy.png`.

## El problema

El botón es azul translúcido sobre una barra azul marino, al lado de «Conectado», que tiene la misma forma. Se lee como un estado más, no como algo que hay que hacer, y los jugadores no se enteran de que hay versión nueva. Además, la flecha es el carácter `↑`.

## El cambio

Es el botón de la barra de pestañas (MULTIJUGADOR · BIBLIOTECA), en `MainWindow.xaml`. **Solo cambia su estilo.** Pasa a ser la única pieza sólida y dorada de la barra.

| Propiedad | Valor |
| --- | --- |
| Alto | 30 (el mismo de «Conectado») |
| Radio | 15 |
| Relleno | 0 · 14 · 0 · 11 (arriba, derecha, abajo, izquierda) |
| Flecha ↔ texto | 7 |
| Letra | Segoe UI 12,5 / 600, `#2B1F06` |
| Fondo | degradado vertical `#F6DF9E` → `#E6C06A` al 55 % → `#D2A54A` |
| Borde interior | 1 px, `rgba(255,243,207,.55)` |
| Filo de luz | 1 px arriba, blanco al 45 % |
| Halo | anillo exterior de 3 px de `#E6C06A` al 16 %, más resplandor de 18 px de `#E6C06A` al 45 % |

**El halo respira:** en un ciclo de 2,4 s (entrada y salida suaves), el resplandor pasa de 18 px al 45 % a 8 px al 18 %, y el anillo del 16 % al 6 %. Con efectos reducidos se queda fijo en el valor de reposo.

| Estado | Fondo | Halo |
| --- | --- | --- |
| Reposo | el de la tabla | respira |
| Cursor encima | `#FBEAB8` → `#EDCB7C` al 55 % → `#DDB25A`; borde interior al 70 % | anillo al 26 %, resplandor de 24 px al 60 %, fijo |
| Pulsado | `#D9B55E` → `#C4973D`; borde interior al 40 % | sin halo; sombra interior `0 2 4` de `rgba(60,40,5,.35)` |
| Foco de teclado | el de reposo | en vez del anillo dorado: 2 px de `#0F1C2E` y, por fuera, 2 px de blanco al 85 % |

**La flecha** es un trazo, no el carácter `↑`: en una caja de 14 × 14 dibujada a 13, `M7 11.8V2.6M3.1 6.4 7 2.5l3.9 3.9`, grosor 2, extremos y uniones redondos, del color del texto (`#2B1F06`).

## Reglas

- Nunca toma el color del mod: es el mismo dorado con cualquier mod.
- El dorado significaba favorito y campeón. Se añade este uso: «hay una versión nueva del launcher». **Las actualizaciones de un mod siguen en azul** (`#55ACEE`), para que no se confundan con esta.
- Colores como pinceles con `DynamicResource`.

## Lo que NO cambia

- El texto («Update launcher» / «Actualizar launcher»), el sitio, cuándo aparece y lo que hace al pulsarlo.
- El botón de la pantalla que cierra el multijugador hasta actualizar (`UpdateGateOverlay` en `MultiplayerTab.xaml`): sigue con `MpPrimaryButton`.
- El diálogo de actualizar.

## Textos nuevos

Ninguno.

## Orden sugerido y capturas

Un commit. Al terminar, dos capturas de la barra con el botón visible: con Wars of Liberty y con Hundred Days (mod dorado), para comprobar que destaca en las dos.

En el prototipo, 110d es una alternativa más discreta (cristal dorado en vez de sólido). **No la implementes**: está solo como referencia.
