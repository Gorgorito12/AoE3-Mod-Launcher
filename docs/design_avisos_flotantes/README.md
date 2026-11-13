# Avisos flotantes · un solo dibujo para todos (diseño 105)

El aviso «Opponent found!» / «¡Rival encontrado!» sale feo (`uploads/aviso-rival-hoy.png`) por tres cosas:

1. **El icono es una letra.** El aviso recibe el carácter ⚔ y Windows lo pinta con su fuente de símbolos, en negro y sin recuadro: casi no se ve sobre el fondo oscuro.
2. **La tarjeta no tiene tono.** Es casi del mismo negro que lo de detrás y nada dice si es buena noticia o problema.
3. **El botón salió verde**, el color de «Unirse», cuando aquí ya estás dentro y lo que haces es ver tu sala.

Lo mismo les pasa a los demás avisos: se dibujaron por tandas con formas distintas y casi todos usan letras como icono (⚔ ⏹ ! i ✓ ✕ ▶). Este paquete define **un solo dibujo** para todos.

El cambio es **solo visual**: quién lanza cada aviso, sus textos, cuánto dura, cuándo va al escritorio y qué hace cada botón **no cambian**.

Repo: `Gorgorito12/AoE3-Mod-Launcher`, rama `main`, proyecto WPF `WarsOfLibertyLauncher/`.

| Archivo del paquete | Qué contiene |
| --- | --- |
| `Avisos flotantes.dc.html` | Prototipo, turno 105. **105a:** el aviso de rival encontrado como sale hoy, el nuevo dentro del launcher y el nuevo en el escritorio. **105b:** los quince avisos con el dibujo nuevo. |
| `uploads/aviso-rival-hoy.png` | Captura del aviso actual. |
| `support.js`, `mods/`, `design_handoff_workshop/` | Lo que necesita el prototipo para abrirse. |

Abre el `.dc.html` en un navegador con `support.js` al lado (necesita internet). Es **referencia de diseño**, no código para copiar. Los textos grises sobre cada aviso son anotaciones.

> Los nombres de clases y claves de abajo salen de las notas del proyecto de diseño, no del código de hoy. Si en `main` se llaman distinto, usa los equivalentes y dímelo.

Archivos del repo que toca, según esas notas: `Controls/AppToast.cs` (y `AppToast.ToastOptions`), `Controls/DesktopToastWindow.cs`, `Styles/Colors.xaml`. `Services/ToastRouting.cs` **no** cambia.

---

## 1. La tarjeta

- Ancho **420**, radio **14**. Relleno: 16 arriba, abajo y a la izquierda; 14 a la derecha (por la ✕).
- **Fondo, tres capas** (de abajo arriba):
  1. El cristal de los flotantes según la transparencia del usuario: Sólida `rgba(12,22,38,.95)` + difuminado 18 · Media `rgba(12,22,38,.74)` + 28 · Máxima `rgba(12,22,38,.6)` + 32. Es el que ya usan los flotantes; si `AppToast` ya lo aplica, no lo dupliques.
  2. Degradado vertical de blanco: 5 % arriba → 0 % al 60 % del alto.
  3. Resplandor del tono desde la esquina superior izquierda: elipse de 320 × 170 centrada en esa esquina, del tono al 22 % en el centro → 0 % al 72 % del radio.
- **Borde:** 1 px de blanco al 13 %. **Filo de luz:** 1 px de blanco al 8 % pegado arriba, por dentro. **Sombra:** `0 14 36` negra al 50 %.
- **Aviso importante** (solo los dos de «entró tu rival», que ya van al escritorio y duran 20 s): el borde pasa a 1 px **del tono al 55 %**, el filo de luz al 10 %, y se añade un **halo del tono** (difuminado 34, recogido 6, al 50 %).

## 2. Tonos

Los fijos de la base de diseño. No toman el color de ningún mod.

| Tono | Color | Texto y trazo en ese color | Cuándo |
| --- | --- | --- | --- |
| Verde | `#4FD68A` | `#7FE0A8` | Buena noticia, algo listo |
| Azul | `#55ACEE` | `#A9D6F7` | Información, invitaciones |
| Ámbar | `#F4900C` | `#F5BD76` | Problema o algo que falta |
| Neutro | `#8C9CB1` | `#C3D2E5` | La búsqueda se detuvo, sin más |

## 3. El icono: nunca una letra

Recuadro de **44 × 44**, radio **11**, arriba a la izquierda. Tres clases:

- **Pictograma** (sucesos y estados): un trazo de **1,7** con extremos y uniones redondos, sin relleno, dibujado a 24 × 24, en el color de texto del tono. Recuadro: el tono al 16 % con borde de 1 px del tono al 45 %. En neutro: blanco al 7 % con borde de blanco al 16 %.
- **Icono del mod** (el aviso es de una sala o de un mod concretos): la imagen del mod llenando el recuadro, borde de blanco al 16 %, y una **insignia** de 20 de diámetro abajo a la derecha (sobresale 5), rellena del tono, con anillo de 2 px `#101D31` y un pictograma de 12 en `#0F1C2E` con trazo 2,8.
- **Escudo del equipo** (invitaciones a equipo): como se dibuje hoy el escudo; en el prototipo, las iniciales en 14 / 700 blanco sobre `#4F6A8A`.

Los pictogramas van como `Geometry` en recursos, no como texto. Datos de cada uno, en una caja de **20 × 20**:

| Nombre | Datos del trazo |
| --- | --- |
| `swords` | `M3.5 3.5l9.5 9.5M10.5 15.5l5-5M13 13l3.5 3.5M16.5 3.5L7 13M4.5 10.5l5 5M7 13l-3.5 3.5` |
| `check` | `M4.5 10.5l3.8 3.8 7.2-8.3` |
| `plus` | `M10 4.5v11M4.5 10h11` |
| `stop` | `M6.5 6.5h7v7h-7z` |
| `clock` | `M10 3.5a6.5 6.5 0 1 0 0 13a6.5 6.5 0 0 0 0-13M10 6.8V10l2.3 1.6` |
| `warn` | `M10 4.5v6.5M10 14.6v.4` |
| `info` | `M10 9v6.2M10 5.2v.4` |
| `offline` | `M3.2 8.3a10 10 0 0 1 13.6 0M6 11.2a6 6 0 0 1 8 0M10 14.4v.3M4.2 3.6l11.6 13` |
| `up` | `M10 16V4.5M5.5 9L10 4.5 14.5 9` |
| `download` | `M10 3.5v8.5M6.3 8.6L10 12.3l3.7-3.7M4.5 16h11` |

**La ✕ de cerrar** también es un trazo: `M2 2l8 8M10 2l-8 8` en una caja de 12 × 12, trazo 1,5 `#A8BCD2`, dentro de un botón de **24 × 24** con radio 6 (al pasar el ratón, fondo blanco al 10 %). Tooltip «Cerrar».

## 4. Texto

Columnas: icono 44 · texto (el resto) · ✕ 24, con 14 de separación.

- **Título:** 14,5 / 600 `#F4F8FC`, una línea con puntos suspensivos (interlineado 1,4).
- **Línea de contexto** (opcional; hoy solo la usan las grabaciones: «Aluclown vs Geaf_Argento · ESOC Indonesia»): 12 `#A8BCD2`, una línea con puntos suspensivos.
- **Cuerpo:** 13 / 1,45 `#C3D2E5`, dos líneas como mucho.
- Separación entre líneas de texto: 3.

## 5. Botones

Abajo a la derecha, a 13 del texto. **32** de alto, **mínimo 96** de ancho, relleno lateral 14, radio 7, 12,5 / 600, separados 8, el principal en el extremo derecho.

| Clase | Fondo | Borde / sombra | Texto | Para |
| --- | --- | --- | --- | --- |
| Azul | `#2F7FE0` | sombra `0 4 14` de su color al 40 % | blanco | El aviso te pide actuar: Ver sala, Aceptar, Abrir el asistente, Mostrar en la carpeta |
| Verde | `#1D4A37` (`MpRoomJoin`) | borde `rgba(79,214,138,.55)` | blanco | **Solo «Unirse»** |
| Sólido | `#1A2C48` | borde `rgba(130,175,255,.22)` | `#DCE7F5` | Todo lo demás: Ignorar, Ahora no, Rechazar, Ver, Jugar, Reintentar, Ver equipo |

**Uno solo de color por aviso.** Ningún botón lleva letras-símbolo delante («▶ Jugar» pasa a «Jugar»).

## 6. Barra de tiempo (nuevo)

Es lo único que no es solo dibujo.

- **3 px** de alto, pegada al borde inferior de la tarjeta y recortada por su radio. Carril: blanco al 7 %. Relleno: el color del tono.
- Empieza llena y se vacía hacia la izquierda, de forma lineal, en lo que dura el aviso.
- **Se detiene con el ratón encima** y sigue al quitarlo. Si hoy el aviso no pausa su cierre con el ratón encima, dímelo antes de añadirlo.
- Los avisos que no se cierran solos no la llevan.
- Con efectos reducidos no se anima (se queda llena).
- Si complica `AppToast` más de la cuenta, se deja fuera y me lo dices: el resto del diseño no depende de ella.

## 7. En el escritorio (`DesktopToastWindow`)

La misma tarjeta, con dos diferencias:

- Fondo **`#12213A` sólido**, sin difuminado (es una ventana sobre el escritorio). El degradado blanco y el resplandor del tono se mantienen.
- Una línea arriba que dice de quién es: icono del launcher de **14** y «AoE3 Mod Launcher» en 11,5 `#A8BCD2`, a 11 del borde superior; el bloque del icono y el texto empieza 10 más abajo.

## 8. Los quince avisos

Textos, duración y destino son los que ya tiene el código; aquí solo se asigna tono, icono y clase de botón.

| Aviso | Origen | Tono | Icono | Botones (principal al final) | Importante |
| --- | --- | --- | --- | --- | --- |
| ¡Rival encontrado! | 96e · `QmFound*` · 20 s · escritorio | Verde | `swords` | Ver sala (azul) | **Sí** |
| «{rival} entró en la sala del cruce» | 85j · torneo · 20 s · escritorio | Verde | `swords` | Ver sala (azul) | **Sí** |
| «{jugador} abrió una sala» | 70b · `RoomCreated` · 8 s | Verde | Mod + insignia `plus` | Ignorar · Unirse (verde) | No |
| «{jugador} te pide la revancha» | 87m | Azul | Mod + insignia `swords` | Ahora no · Unirse (verde) | No |
| Invitación a un equipo | 83l · `TeamInviteNotice` | Azul | Escudo | Rechazar · Ver · Aceptar (azul) | No |
| «Te uniste a {equipo}» | 83l | Verde | `check` | Ver equipo | No |
| Búsqueda: nadie entró en 30 min | 96e · `QmTimedOut` · 15 s · escritorio | Neutro | `clock` | — | No |
| Búsqueda detenida: abriste un juego | 96e · `QmStoppedByGame` · 6 s | Neutro | `stop` | — | No |
| Salas con otra versión del mod | 96e · `QmOtherVersion` · 12 s | Azul | `info` | — | No |
| Radmin VPN no está listo | 96e · `QmRadmin*` · 15 s | Ámbar | `warn` | Abrir el asistente (azul) | No |
| Sin conexión con el servidor | 70b · no se cierra solo | Ámbar | `offline` | Reintentar | No |
| «{mod} se actualizó» / se instaló | 70b · 8 s | Verde | Mod + insignia `check` | Jugar | No |
| «Recuperaste {n} puntos» | 70c · `RatingRefund` · 8 s | Verde | `up` | — | No |
| Grabación guardada | 63c · 8 s | Verde | `download` | Mostrar en la carpeta (azul) | No |
| No se pudo descargar la grabación | 63c · no se cierra solo | Ámbar | `warn` | Reintentar | No |

Las variantes de «Grabación guardada» con aviso de versión (se jugó con otra versión, mod no instalado) van en **azul** con `info`, como en 63c.

**Si en el código hay avisos que no están en esta tabla**, no los dejes con el dibujo viejo: aplícales la misma tarjeta, propón tono e icono con el criterio de la sección 2 y enséñamelos.

## 9. Lo que NO cambia

- Cuándo sale cada aviso, a quién, cuánto dura y si va al escritorio (`ToastRouting`, «No molestar», «Mostrar avisos flotantes»).
- Los textos, salvo quitar las letras-símbolo de los botones.
- Dónde se apilan (abajo a la derecha) y la separación entre ellos.
- La campana y su panel (70a), los menús, los popovers y los diálogos pequeños (103).

## 10. Sustituye a

Los dibujos de avisos de los turnos 63c (360 de ancho, radio 8, botón a la izquierda), 70b, 83l, 85j, 87m y 96e. Si hay código que siga esas medidas, pasa a estas.
