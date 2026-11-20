# Notificaciones · el panel de la campana (diseño 120)

`Notificaciones campana.dc.html` tal como salió del proyecto de diseño, con la captura de la que
parte (`uploads/notificaciones-hoy.png`, 120a). Abrirla necesita `support.js`, que va en esta
misma carpeta. Las imágenes de los mods y el icono del launcher que usa la lámina no se copiaron:
sin ellas los marcos se ven con el icono vacío.

## Qué se construyó (120b, 120c y 120d)

- **Dónde se abre.** El borde derecho del panel es el borde derecho de la campana, a 6 de la barra
  de título, con una punta bajo la campana. Si la ventana es tan estrecha que se saldría por la
  izquierda, se corre lo justo para quedar a 12 del borde. La posición de la campana se mide en la
  ventana cada vez (`MainWindow.PlaceNotificationPanel`); la regla sin ventana está en
  `Services/NotificationPlacement.cs`.
- **Cuánto mide.** Lo que mide su contenido. Crece hasta 12 del fondo de la ventana y entonces solo
  la lista se desplaza; la cabecera, los filtros y el pie no se mueven
  (`Controls/NotificationPanel.cs`).
- **El icono del launcher** en los avisos de versión nueva y de novedades
  (`NotificationView.IsAboutTheLauncher`). «Sin conexión» y los destacados del mes conservan el
  círculo.
- **Marcas y engranaje dibujados**, no caracteres. El engranaje lleva el tooltip
  «Notification settings» / «Configuración de notificaciones».
- **Sin avisos:** la campana dibujada y «Sin notificaciones».

Para verlo con avisos de ejemplo, sin tocar el historial: `--preview-dialog=bell:b`, `bell:c` o
`bell:d` (solo en el banco de pruebas).

## Dónde no se siguió al pie de la letra, y por qué

- **Sin desenfoque detrás del panel.** WPF no lo tiene dentro de su propia ventana; el panel
  conserva el cristal flotante que ya tenía.
- **«Configuración de notificaciones»** en vez de «Ajustes de notificaciones»: el launcher dice
  Configuración en todas partes.
- **La estrella** de los destacados del mes es nuestra: la lámina no tiene ese aviso. Va rellena,
  porque con trazo de 1,9 una estrella de diez píxeles es una mancha.
- **La punta** desaparece si la ventana empujó el panel tanto que la campana ya no queda encima.
- **Sin interletrado** en el encabezado de día (WPF no lo tiene).
- La ✕ de cada aviso sigue siendo el carácter de antes: la lámina dice que no cambia.
