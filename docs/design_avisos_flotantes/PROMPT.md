# Texto para pegar en Claude Code

Copia esta carpeta dentro del repo (p. ej. `docs/design_avisos_flotantes/`), abre una terminal en la raíz del repo, lanza `claude` y pega esto:

---

Lee `docs/design_avisos_flotantes/README.md` completo antes de escribir código, y mira `uploads/aviso-rival-hoy.png` para ver cómo sale hoy el aviso de «Opponent found!». Los avisos flotantes (`AppToast` y `DesktopToastWindow`) se ven mal: el icono es un carácter que Windows pinta en negro, la tarjeta no tiene tono y el botón sale verde donde no toca. Hay que darles **un solo dibujo a todos**: iconos vectoriales sobre un recuadro del color del estado, resplandor del tono en la tarjeta, una regla única de botones y una barra de tiempo. El diseño es el turno 105 de `Avisos flotantes.dc.html` (105a el aviso de rival encontrado, 105b los quince avisos). Es un prototipo de referencia: NO copies su código, cambia el código WPF que ya existe.

**El cambio es solo visual.** No toques quién lanza cada aviso, sus textos, cuánto dura, cuándo va al escritorio ni lo que hace cada botón. `ToastRouting`, «No molestar» y los ajustes de avisos se quedan como están. Tampoco toques la campana, los menús ni los diálogos pequeños.

## Antes de escribir código, hazme un plan

1. **Lee cómo se dibuja hoy un aviso:** `AppToast` (y `ToastOptions`), `DesktopToastWindow` y los estilos que usen. Dime cómo recibe hoy el icono cada aviso (¿un carácter?, ¿una imagen?), cómo pinta el fondo y los botones, y si el cierre automático se pausa con el ratón encima.
2. **Hazme la lista de todos los sitios que lanzan un aviso flotante** y compárala con la tabla de la sección 8 del README. Dime cuáles coinciden, cuáles faltan en el código y cuáles hay en el código que no están en la tabla; para estos últimos, propón tono e icono.
3. **Propón cómo pasa cada aviso su tono, su icono y la clase de sus botones** (por ejemplo, un enum de tono y otro de pictograma en `ToastOptions`), de modo que ningún llamador vuelva a pasar un carácter como icono.
4. **Dime qué pinceles y qué `Geometry` nuevos declaras** y cuáles reutilizas de `Styles/Colors.xaml` (`MpRoomJoin*` para «Unirse», el azul global, los de estado).
5. **Señala los choques** con el código, con `CLAUDE.md` o con tests que fijen medidas o colores de los avisos. En esos casos dime qué propones en vez de forzar el README.
6. Propón commits pequeños: pinceles y pictogramas → tarjeta (fondo, borde, resplandor) → icono (pictograma, icono de mod con insignia, escudo) → texto y ✕ → botones → aviso importante → escritorio → barra de tiempo → repaso de todos los llamadores.

## Reglas al implementar

- **Ningún icono como texto.** Ni en el recuadro, ni en la ✕, ni delante del texto de un botón.
- **Colores y trazos una sola vez**, como recursos con `DynamicResource`.
- **Un solo botón de color por aviso:** azul cuando el aviso pide actuar, verde solo para «Unirse», sólido para el resto.
- **Borde y halo de color solo en los dos avisos de «entró tu rival».** Los demás llevan borde neutro.
- La barra de tiempo es lo último. Si el aviso hoy no pausa su cierre con el ratón encima, o si complica `AppToast` más de la cuenta, para y dímelo antes de añadirla.
- En el escritorio no hay difuminado: fondo sólido.
- Respeta las tres transparencias (Sólida, Media, Máxima) y los efectos reducidos (sin animación de la barra).
- Si cambias el texto de algún botón para quitarle un símbolo, hazlo en `Localization/Strings*.cs`, en español e inglés.
- Compila y pasa los tests antes de darme cada commit por terminado.

Cuando tengas la tarjeta, el icono y los botones, párate y enséñame capturas de tres avisos antes de seguir: «Opponent found!» dentro del launcher, el mismo en el escritorio, y uno ámbar con botón (Radmin o sin conexión).
