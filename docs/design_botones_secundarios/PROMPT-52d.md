# Texto para pegar en Claude Code: botones grandes de Local files (52d)

Pégalo tal cual en Claude Code. Es la continuación del turno 52, que ya está en main con la variante 52a.

---

En `ModPropertiesDialog.xaml`, los dos botones grandes de Local files, **`InstallNewCopyBtn`** (~819, «Install another copy») y **`AddExistingFolderBtn`** (~842, «Add a folder you already have»), se quedaron sin el relleno de 52a. Hoy son solo un borde sobre el fondo de la ventana. La referencia es `Botones secundarios.dc.html`, sección **52d**.

**Cambia solo esto:**

| Estado | Fondo | Borde (1 px) |
| --- | --- | --- |
| Normal | `UiButtonFill` (`#1A2C48`) | `rgba(130,175,255,.22)`, el mismo borde que los demás botones de 52a |
| Hover | `UiButtonFillHover` (`#213758`) | igual |
| Pressed | `UiButtonFillPressed` (`#203554`) | igual |
| Disabled | el fondo normal con `Opacity` 0,5, como el resto de 52a | |

- **Cuadro del icono:** un paso más claro para que siga destacando sobre el relleno. El de «+» pasa de `rgba(47,127,224,.16)` a `rgba(47,127,224,.26)`, con el glifo en `#A3C9F7`. El de la carpeta pasa de `rgba(130,175,255,.10)` a `rgba(130,175,255,.16)`, con el glifo en `#C9D7E8`. Declara estos colores como pinceles nuevos junto a `UiButtonFill*` en `Styles/Colors.xaml`, no como hex sueltos.
- **Texto:** el título en `#F0F5FB` y la descripción en `#9FB3CD`, un poco más claros que hoy porque el fondo también lo es. Si ya hay pinceles con esos valores, úsalos.
- Radio, padding, tamaño, tipografía y textos: **sin cambios**.

**Antes de tocar nada, dime:**
1. Qué estilo usan hoy esos dos botones y si lo comparte algún otro botón de la app. Si lo comparte, propón aplicar el cambio en el estilo, para que les llegue a todos, o en uno derivado solo para estos dos. No decidas tú.
2. Si tienen un `Background` local en XAML o en código que impediría que el cambio del estilo les llegue.

Compila y pasa los tests. Después enséñame una captura de Local files con los dos botones, en reposo y con el ratón encima.
