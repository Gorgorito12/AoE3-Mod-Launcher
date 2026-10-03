# Botones secundarios con relleno propio

Hoy los botones secundarios de la ventana del mod (`ModPropertiesDialog`) y de los ajustes del launcher (`LauncherSettingsDialog`) son **solo un borde**: por dentro se ve el fondo de la tarjeta y se confunden con ella. Ejemplos: Open folder, Repair, Free space, Open, Change, Import, Refresh, Search for my install…, Close, Make active, Remove from list y Uninstall….

El cambio les da **un relleno propio, un poco más claro que la tarjeta**. No cambian la forma, el tamaño, el radio, la tipografía ni el lugar. **La variante elegida es 52b.**

Repo: `Gorgorito12/AoE3-Mod-Launcher`, rama `main`, proyecto WPF `WarsOfLibertyLauncher/`.

| Archivo del paquete | Qué contiene |
| --- | --- |
| `Botones secundarios.dc.html` | Prototipo, turno 52: cómo está hoy y las variantes 52a (suave), **52b (elegida)** y 52c (marcada). Ábrelo en un navegador con `support.js` al lado. Al pasar el ratón por un botón se ve su hover. |

El HTML es **referencia de diseño**, no código para copiar. Es de alta fidelidad: los colores de abajo son los finales.

---

## 1. Valores de 52b

Sobre la tarjeta `#12213a` (y sobre el pie `#16263e` en el caso de Close).

| Tipo | Relleno | Hover | Borde (1 px) | Texto |
| --- | --- | --- | --- | --- |
| Neutro (Open, Change, Free space, Repair, Import, Refresh, Remove from list…) | `#22385A` | `#2A446B` | `rgba(130,175,255,.28)` = `#4782AFFF` | `#E8EEF6` |
| Neutro en el pie (Close) | `#263E62` | `#2E4A74` | igual | `#E8EEF6` |
| Azul (Make active) | `#1B3B68` | `#21477C` | `rgba(47,127,224,.62)` = `#9E2F7FE0` | `#B8D5FA` |
| Rojo (Uninstall…) | `#33263C` | `#3D2A44` | `rgba(210,120,120,.55)` = `#8CD27878` | `#F3BCBC` |

- **Brillo superior:** 1 px de `rgba(255,255,255,.06)` (`#0FFFFFFF`) pegado al borde de arriba, por dentro. En WPF, un `Border` interior con `BorderThickness="0,1,0,0"` y el radio del botón menos 1 px. Si complica la plantilla, se puede omitir: es lo menos importante.
- **Pressed:** el color de hover, un 4 % más oscuro (o el relleno normal). Que no sea más claro que el hover.
- **Disabled:** el relleno normal con `Opacity` 0,5, como ya se haga hoy. No vuelvas al borde vacío.
- **Focus de teclado:** el anillo que ya exista, sin cambios.
- Radio, alto, padding y tipografía: **sin cambios**.

### Lo que NO cambia

- Los botones sólidos: Verify files (azul), Share diagnostics (turquesa) y los primarios del pie (`SetFooterPrimaryButton`).
- Los botones con estilo de enlace (`MpLinkButton`, p. ej. Cancel).
- Los toggles, ComboBox, campos de texto y chips.
- «Create backup» en User data ya tiene un relleno teñido. Déjalo como está, salvo que use uno de los estilos de abajo.

## 2. Dónde se cambia

Todo está en `Styles/Controls.xaml`, con los colores en `Styles/Colors.xaml`:

- `SetActionButton` (~402) es la base, con `SetActionButtonSm` y `SetActionButtonLg`. `InChangeButton` de `InstallFolderDialog` hereda de él.
- `SetFooterGhostButton` (~586): Close, Search for my install…
- `SetGhostButton` (~624), y de él `SetDangerOutlineButton` (~635, la variante roja) y `SetAccentOutlineButton` (~650, la variante azul).
- **No cambian:** `SetActionButtonPrimary`, `SetFooterPrimaryButton`, `SetSolidButton` ni `SetDiagButton` (el turquesa de Share diagnostics).
- Pinceles que existen hoy y pueden servir de referencia: `MpActionText #8CBCF5`, `MpActionRim #8C2F7FE0`, `MpActionSoftBg #292F7FE0`, `MpDestructiveText #D99A9A` y `MpDestructiveRim #4DC87878`.

El comentario de ~612 dice que la plantilla de `SetActionButton` usa `TemplateBinding`, así que en los estilos derivados basta con cambiar los Setters de `Background` y los triggers de hover y pressed.

**Lo ideal es cambiar solo esos estilos** y que las dos ventanas cambien a la vez. Si algún botón tiene `Background="Transparent"` escrito directamente en el XAML o en el código (`ModPropertiesDialog.xaml.cs` construye botones en tiempo de ejecución, p. ej. ~3020 y ~3035), ese valor local gana al estilo y hay que quitarlo.

Declara los colores **una sola vez como pinceles** (p. ej. `SetButtonFill`, `SetButtonFillHover`, `SetButtonFillAccent`, `SetButtonFillDanger`…) junto a los demás `Mp*`/`Ui*`, y que los estilos los referencien con `DynamicResource`.

## 3. Variantes descartadas, por si 52b no convence en pantalla

| | Neutro | Hover | Azul | Rojo | Borde |
| --- | --- | --- | --- | --- | --- |
| 52a suave | `#1A2C48` | `#213758` | `#16304F` | `#272338` | el de hoy |
| 52c marcado | `#2B4468` | `#34507A` | `#224A86` | `#47293C` | ninguno |
