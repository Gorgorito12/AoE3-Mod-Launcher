# Salas · filas con relieve y color por grupo (diseño 104a)

La lista de salas de la pestaña Multijugador › Salas se ve plana: la fila apenas se distingue del panel, el único color es el punto del chip, y los títulos de columna son un renglón gris igual que la etiqueta de grupo que tienen debajo.

El cambio: cada fila pasa a ser una **ficha con relieve teñida con el color de su grupo**, los títulos de columna pasan a ser **una barra con fondo**, y las celdas de anfitrión, jugadores y ping ganan un dibujo (avatar, asientos, barras de señal). **No cambia** qué salas se muestran, cómo se agrupan ni qué hace cada botón.

Repo: `Gorgorito12/AoE3-Mod-Launcher`, rama `main`, proyecto WPF `WarsOfLibertyLauncher/`.

| Archivo del paquete | Qué contiene |
| --- | --- |
| `Salas filas.dc.html` | Prototipo, turno 104. **La elegida es 104a** (lista completa) y 104b (una sola sala en partida). 104c y 104d son alternativas descartadas: ignóralas. |
| `Salas v2.dc.html` | La pestaña completa (turno 86) con las filas de 104a ya puestas: partida rápida, lista, actividad y chat. Sirve para ver las proporciones. 86c muestra las filas provisionales de «Cargando». |
| `PartidaRapidaTarjeta.dc.html`, `support.js`, `mods/`, `design_handoff_workshop/` | Lo que necesitan los dos prototipos para abrirse. |

Abre los `.dc.html` en un navegador con `support.js` al lado (necesitan internet). Son **referencia de diseño**, no código para copiar. Los textos grises sobre cada maqueta y la nota del final son anotaciones.

> Los nombres de estilos y controles de abajo salen de una copia de `MultiplayerTab.xaml` que puede no ser la última. Si en `main` se llaman distinto o ya no existen, usa los equivalentes y dímelo.

---

## 1. Colores de grupo

Son los tres de estado que ya existen desde el diseño 66 (`MpRoomJoin*`, `MpRoomInGame*`, `MpRoomFull*` en `Styles/Colors.xaml`). Reutiliza esos pinceles si los valores coinciden; no declares duplicados.

| Grupo | Color | Texto en ese color | Refuerzo `K` |
| --- | --- | --- | --- |
| Puedes entrar | `#4FD68A` | `#7FE0A8` | 1 |
| En partida | `#55ACEE` | `#A9D6F7` | 1,3 |
| No disponibles ahora (llena, otra versión) | `#F4900C` | `#F5BD76` | 1 |

El azul lleva un 30 % más de opacidad en el tinte y en el borde porque sobre el fondo azul marino se nota menos que el verde y el ámbar.

Ninguna fila queda gris: las tres secciones llevan color.

## 2. La fila (`MpRoomCard`, la arma `BuildRoomCard`)

- Alto mínimo **72**, relleno **12 arriba y abajo, 14 a los lados**, radio **12**, separación entre filas **6** (como hoy).
- **Fondo, dos capas:**
  1. Degradado vertical de blanco: 9 % arriba → 4,5 % abajo.
  2. Encima, degradado horizontal del color del grupo: `28 % × K` en el borde izquierdo → `11 % × K` al 36 % del ancho → 0 % al 72 %.
- **Borde:** 1 px del color del grupo al `42 % × K`.
- **Filo de luz:** 1 px de blanco al 10 % pegado al borde superior, por dentro.
- **Sombra:** del color del grupo al 40 %, desplazada 10 px hacia abajo, difuminado 22, recogida 12 (solo asoma por debajo). Si un `DropShadowEffect` por fila pesa al desplazar la lista, omítela y dímelo: es lo menos importante.
- **Sala propia** (la tuya o en la que ya estás): el borde pasa a **1,5 px de blanco al 55 %**. El tinte sigue siendo el de su grupo. Sustituye al relleno y borde más claros que hoy pone `BuildRoomCard`.
- **Hover:** toda la fila un 10 % más clara.

## 3. Columnas (`RoomsTableLayout`)

Icono **48** · sala **1,6\*** · anfitrión **0,8\*** (mínimo 190) · jugadores **132** · ping **104** · acción **160**, con **16** de separación.

La columna de acción mide 160 en el prototipo porque los textos en español son largos («Revisar mi copia»). Si en `main` el ancho de esa columna y el alto del botón están fijados a propósito (el diseño 66 tomó solo los colores y dejó el tamaño), **respeta lo que haya** y dímelo.

## 4. Barra de títulos (`RoomsHeaderBand` / `RoomsHeaderStrip`)

Se conserva, fija sobre la lista (no se desplaza con las filas), pero deja de ser texto suelto:

- Barra de **32** de alto, radio **8**, fondo blanco al 6 %, borde de 1 px de blanco al 9 %.
- Texto **11,5 / 700**, mayúsculas, espaciado 0,06 em, color `#A8BCD2`.
- Mismo margen lateral (10) y relleno lateral (14) que las filas, y las mismas columnas, para que cada título caiga sobre su columna.
- **SALA** ocupa las columnas del icono y del nombre. **ANFITRIÓN**, **JUGADORES**, **PING**. La columna del botón no lleva título.
- Si los títulos siguen siendo botones de orden, se quedan como botones con su flecha dentro de la barra: solo cambia el dibujo.

## 5. Etiqueta de grupo

Punto de **7** del color del grupo · texto **11,5 / 700** en mayúsculas en el color de texto del grupo · la cuenta de salas en Consolas 11,5 `#8C9CB1` · una línea de 1 px de blanco al 9 % que llena el resto del ancho. Separación entre elementos 8; relleno 2/6 la primera y 10/6/2 las siguientes.

## 6. Celdas

**Icono del mod:** 48×48, radio 10, borde interior de blanco al 16 % y sombra `0 4 12` negra al 35 %. Tooltip con el nombre del mod.

**Sala:** nombre **15 / 600** `#F0F5FB`, una línea con puntos suspensivos (interlineado 1,4). Debajo, los chips, a 6 de separación.

**Chips** (22 de alto, radio 11, texto 11 / 600, punto de 6):

| Chip | Fondo | Borde | Texto | Punto |
| --- | --- | --- | --- | --- |
| Normal (Competitiva, Casual, Con contraseña, Observadores) | blanco 7 % | blanco 14 % | `#DCE7F5` | Competitiva `#E6C06A` · Casual `#8C9CB1` · resto `#A8BCD2` |
| En partida · N min | `#55ACEE` al 18 % | `#55ACEE` al 50 % | `#A9D6F7` | `#55ACEE`, **late** (opacidad 1 → 0,4 y escala 1 → 0,7, ciclo de 1,6 s). Con efectos reducidos, fijo. |
| Otra versión | `#F4900C` al 12 % | `#F4900C` al 45 % | `#F5BD76` | `#F4900C` |
| Tu sala / Estás dentro | `#DCE7F5` sólido | ninguno | `#0F1C2E` | `#2F7FE0` |

**Anfitrión:** círculo de **30** (`#1A2C48`, borde `rgba(130,175,255,.3)`) con la inicial en 12 / 700 `#DCE7F5`. Si la sala ya trae el avatar de Discord del anfitrión, va ahí; **si no lo trae, no se inventa ni se pide al servidor**: se queda la inicial. A la derecha, el nombre **13,5 / 600** `#F0F5FB` (y «tú» en 12 `#A8BCD2` si es tu sala) y debajo `1630 ELO` o `Posicionamiento 3/10` en 12 `#A8BCD2`, con el número en Consolas `#DCE7F5`.

**Jugadores:** `1/2` en Consolas **14 / 700** `#F0F5FB`. Debajo, a 7, **un punto de 8 por asiento** con 4 de separación: relleno `#DCE7F5` si está ocupado, solo contorno de 1,5 px de blanco al 36 % si está libre. Tooltip «1 de 2 jugadores».

**Ping:** cuatro barras de **3** de ancho y **5, 8, 11 y 14** de alto, alineadas abajo, con 2 de separación; a 8, los ms en Consolas **12,5 / 600**.

| Ping | Barras encendidas | Color de barras | Color del texto |
| --- | --- | --- | --- |
| hasta 60 ms | 4 | `#4FD68A` | `#7FE0A8` |
| hasta 100 ms | 3 | `#4FD68A` | `#7FE0A8` |
| hasta 150 ms | 2 | `#DCE7F5` | `#DCE7F5` |
| más de 150 ms | 1 | `#F4900C` | `#F5BD76` |
| sin medir | 0 | — | «—» en `#8C9CB1` |

Las barras apagadas son blanco al 16 %. Si el launcher ya tiene sus propios umbrales de color para el ping, **manda el código**: adapta el número de barras a esos umbrales y dímelo.

## 7. Botón de la fila

34 de alto, radio 8, 13 / 600, relleno lateral 16, alineado a la derecha.

| Caso | Fondo | Borde | Texto |
| --- | --- | --- | --- |
| Unirse | `#1D4A37` (`MpRoomJoin`) | `rgba(79,214,138,.55)` | blanco |
| Ver sala (tu sala o en la que estás) | `#2F7FE0` + sombra `0 4 14` de su color al 35 % | — | blanco |
| Ver partida, Entrar a mirar, Instalar mod, Revisar mi copia | blanco 8 % | blanco 18 % | `#DCE7F5` |
| Llena (estado, sin hover ni cursor de mano) | `#3D2C17` (`MpRoomFull`) | `rgba(244,144,12,.5)` | `#F5BD76` |

## 8. Filas provisionales de «Cargando»

Misma forma que la fila nueva: 72 de alto, radio 12, las mismas columnas, cuadrado de 48 con radio 10 en el sitio del icono. Ver 86c en `Salas v2.dc.html`.

## 9. Lo que NO cambia

- Qué salas se listan, el orden, los tres grupos y sus textos.
- Qué botón sale en cada caso y lo que hace.
- La tarjeta de partida rápida, el chat, la actividad de la comunidad y la barra de subpestañas.
- El servidor: no hay campos nuevos.

## 10. Sin dibujar

**El modo compacto** (`MpRoomCardCompact`, ventanas bajas) no está dibujado con el tinte. Propuesta: la misma ficha con alto mínimo **60**, relleno **8/14** e icono de **40** con radio 9, que es lo que se probó en 104d y cabe con las dos líneas. Enséñame una captura antes de darlo por bueno.

## 11. Textos nuevos

Solo los tooltips: «{0} de {1} jugadores» y «Ping sin medir». Van en `Localization/Strings.cs`, en español (es-419) e inglés.
