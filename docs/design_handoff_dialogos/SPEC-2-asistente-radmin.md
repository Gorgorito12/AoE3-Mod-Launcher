# 2 · Asistente de Radmin

Referencias visuales en `Prototipo.html`: **12a** (conectado), **12b** (en curso) y **12c** (el pie, antes y después).

| Qué | Archivos del repo |
| --- | --- |
| La ventana | `RadminAssistantWindow.xaml` + `.xaml.cs` |
| Estado y sondeo | `Services/RadminAssistantService.cs`, `Services/RadminVpnService.cs` |
| Preferencias | `Models/LauncherConfig.cs` — `RadminAssistantMode`, `RadminAssistantSkipped` |
| Enlace de ayuda | `Controls/SupportLink.cs` — **compartido, no lo toques** |
| Textos | `Localization/Strings.cs` — claves `RadAsst*` |
| Reglas | `.claude/rules/multiplayer.md` ~290-315 |

Geometría actual: `Height="540" Width="430"`, `WindowStyle="None"`, `ResizeMode="NoResize"`, `Topmost="True"`. El cuerpo es un `ScrollViewer` con `Margin="20,18,20,12"`; el pie es un `Border` con `Padding="20,12"`.

## 12c — El pie desborda. Empieza por aquí

Es el único defecto que el usuario ve como algo roto: en su captura, el rectángulo dorado del extremo derecho **es el botón Cerrar recortado**.

Las cuentas con la geometría real. El `Border` exterior de 1 px por lado y el `Padding="20,12"` dejan **430 − 2 − 40 = 388 px** de contenido. Dentro van tres cosas:

- la pastilla de `SupportLink` — **354 px**, medidos en la captura y des-escalados (la ventana sale a 531 px en vez de 430, factor 1,235)
- sus 12 px de `Margin="0,0,12,0"`
- el `MinWidth="100"` de `CloseBtn`

**354 + 12 + 100 = 466 en 388.** Sobran 78, pero el recorte no cae ahí: `overflow` corta en la caja de **relleno**, 428 px, así que los 20 de relleno derecho le devuelven al botón 40 px. Del botón asoman **42 px** y se pierden **58**.

Y la casilla desaparece por el reparto de columnas: está en la columna `*` entre dos `Auto`, así que es la única que puede encogerse, y se queda en cero. Es lo único que escribe `RadminAssistantSkipped`, o sea **lo único que impide que la ventana se abra sola cada vez**.

### La solución NO es acortar la etiqueta

`SupportLink.Build()` se usa en cinco sitios —el menú de marca, la fila de diagnóstico, el diálogo de antivirus, el de compatibilidad y este— y su texto sale de una sola clave, `SupportDiscordHelpLabel`. Acortarlo cambiaría los otros cuatro. Además su documentación defiende esa redacción a propósito: *«the wording is deliberately "need help?" rather than an invitation: every place this appears is a place where something has already gone wrong, so the link is an answer, not an advert»*.

**El defecto es de este pie, no del control, y ya hay precedente.** El parámetro `captionSize` existe porque en la fila de DIAGNÓSTICO la pastilla convivía con dos botones y *«that surplus width was also what pushed it off the edge of the card in Spanish»* — el mismo fallo, la misma causa. Se resolvió en el host, con la razón escrita: *«The size belongs to the HOST, not to this builder»*.

La misma lógica aquí. La pastilla está hecha para ir **sola en su línea** — su doc dice que así la usan tres de sus cuatro hosts — y este pie es el único que la metió en una fila con dos controles más. **Muévela al cuerpo**, entera, donde cabe con 34 px de holgura, y deja el pie con la casilla y `Cerrar`.

## 12a — Conectado

Con los cuatro pasos en verde, la ventana no debería seguir siendo un asistente.

Tu propio código ya dice cuál es su función en ese estado. El comentario que explica por qué no se autocierra cuando la abre el usuario dice que el botón de copiar la red *«is the ONLY thing the window is good for once the checklist is green»*. Pero el diseño entierra ese campo como tercero de cuatro tarjetas iguales y lo recorta a «Age of Empires III: The Asian Dy…» con `TextTrimming="CharacterEllipsis"`.

Con todo en verde, la ventana **es** eso:

1. **Confirmación compacta** — disco verde, «Estás en la red AoE3», y debajo tu IP de Radmin en monoespaciada.
2. **El nombre de la red completo**, en su tarjeta, con `Copiar` y una línea diciendo que ya está en el portapapeles. Deja que envuelva en dos líneas antes que recortarlo: es lo que el usuario ha venido a buscar.
3. **Los cuatro pasos plegados a un renglón** — «Los cuatro pasos están completos» con un enlace «Ver pasos» que los despliega.
4. La pastilla de ayuda en su línea, y el pie con casilla y `Cerrar`.

**Los 120 px de vacío son estructurales.** `Height="540"` con `NoResize` no se arregla quitando contenido: pon `SizeToContent="Height"` como hacen `CreateLobbyDialog` y `CreateTournamentDialog`, y la ventana medirá lo que necesite en cada estado. Ojo con `Window_Loaded`: ancla la ventana con `Left = area.Right - ActualWidth - 20`, así que recalcula la posición **después** del pase de medida, o el anclaje abajo-derecha se descuadra al cambiar de alto.

Quita también el subtítulo de la barra de título (`HeaderSubtitleText`). Es copy de onboarding —«Te guiamos. La mayoría de la gente…»— que sigue en pantalla cuando ya estás conectado, y además no cabe: en 430 px, con el título y el botón de cerrar, se recorta siempre. Si ese texto importa al principio, va en el cuerpo y solo en los estados iniciales.

## 12b — En curso

Hoy las cuatro tarjetas miden lo mismo en todos los estados, así que nada indica dónde estás.

- **Los pasos hechos se plegan a un renglón**: check verde de 18 px, el resultado en una línea («Sesión iniciada · IP 26.162.244.170») y el número a la derecha.
- **El paso activo es el único abierto**, con borde azul y su acción dentro.
- **El paso pendiente queda atenuado**, con el aro vacío.
- **Barra de progreso de cuatro segmentos** arriba, con «Paso 3 de 4».

**El botón de reabrir Radmin sale a tamaño completo en un paso terminado.** `ApplyStage` pone `Step1OpenBtn.Visibility = Visibility.Visible` en **las dos** ramas — la de `NotInstalled` y la de todo lo demás. Cuando el paso 1 está hecho, ese botón pasa a enlace pequeño dentro del renglón plegado: hecho, no urgente.

**«Jugándose ahora» no puede ser una fila más.** Si el indicador de estado añade una tercera fila a la celda, esa celda crece y desalinea la columna. Va como punto de color dentro de la fila existente.

## Tokens

Los mismos del resto del launcher.

| Uso | Hex |
| --- | --- |
| Fondo del cuerpo | `#12213a` |
| Barra de título | `#233648` (título en dorado `#f4d9a0`) |
| Pie | `#16263e` |
| Tarjeta interior | `#0f1c2e` |
| Paso activo | `#16263e` con borde `rgba(47,127,224,.42)` |
| Borde interior | `rgba(130,175,255,.09–.20)` |
| Azul de acción | `#2f7fe0` · texto `#8cbcf5` / `#8fb6ea` |
| Verde hecho | `#4fd68a` sobre `rgba(53,196,111,.16–.18)` |
| Texto | primario `#f0f5fb` · cuerpo `#b9c9de` · atenuado `#8ea4c0` → `#6d829d` → `#5f7592` → `#4f6485` |

**Medidas.** Barra de título 38 · relleno del cuerpo 18/20/12 · relleno del pie 12/20 · botón `Cerrar` 100×32 · pastilla de ayuda 354×34 · check de paso plegado 18 · disco de confirmación 34 · segmento de progreso 4 de alto.

**Tipografía.** UI en Segoe UI; el titular de confirmación en serif. IPs, nombre de red y numeración de pasos en monoespaciada — el nombre de red mantiene `Consolas`, que es lo que usa hoy.
