# Texto para pegar en Claude Code

Copia esta carpeta dentro del repo (p. ej. `docs/design_botones_secundarios/`), abre una terminal ahí, lanza `claude` y pega esto:

---

Lee `docs/design_botones_secundarios/README.md` completo antes de escribir código. Los botones secundarios de `ModPropertiesDialog` y `LauncherSettingsDialog` hoy son solo un borde, y por dentro se ve el fondo de la tarjeta. Hay que darles un relleno propio un poco más claro. **La variante elegida es 52b**, con los valores de la tabla del README. El prototipo es `Botones secundarios.dc.html`: NO lo copies, cambia los estilos WPF que ya existen.

**No toques nada más.** Ni el tamaño, el radio, el padding o la tipografía de los botones, ni los botones sólidos (Verify, Share diagnostics, `SetFooterPrimaryButton`), ni los de enlace (`MpLinkButton`), ni toggles, ComboBox o campos.

## Antes de escribir código, hazme un plan

1. **Lee los estilos.** Están en `Styles/Controls.xaml`: `SetActionButton` (~402), `SetFooterGhostButton` (~586), `SetGhostButton` (~624), `SetDangerOutlineButton` (~635) y `SetAccentOutlineButton` (~650). Dime cómo pintan hoy el fondo, el hover, el pressed y el disabled, y qué botones de las dos ventanas usa cada uno.
2. **Haz la lista de botones afectados** en las dos ventanas, agrupada por estilo. Señala cualquier botón con `Background` local (en XAML o en código, p. ej. `ModPropertiesDialog.xaml.cs` ~3020 y ~3035) que impediría que el cambio del estilo le llegue.
3. **Dime qué otras ventanas usan esos mismos estilos** (`InstallFolderDialog` mediante `InChangeButton` y cualquier otra). Cambiarán también; confírmame que está bien antes de seguir.
4. **Propón los pinceles nuevos** (nombres y dónde se declaran) junto a los `Mp*`/`Ui*` existentes.
5. Propón commits pequeños: pinceles → estilo neutro → variantes azul y roja → limpieza de fondos locales.

## Reglas al implementar

- **Solo cambian el fondo, el hover, el pressed y el borde.** Todo lo demás se queda igual.
- **Colores una sola vez, como pinceles con `DynamicResource`.** Nada de hex repetido en cada estilo.
- **Pressed nunca más claro que hover. Disabled conserva el relleno con opacidad 0,5**, sin volver al borde vacío.
- El brillo superior de 1 px es opcional: si complica la plantilla, omítelo y dímelo.
- Señala cualquier punto del README que choque con la arquitectura real, con `CLAUDE.md` o con un test existente, en vez de forzarlo.
- Compila y pasa los tests antes de darme cada commit por terminado.

Cuando tengas el estilo neutro aplicado, párate y enséñame una captura de Local files y de Launcher settings → Mods and updates antes de hacer las variantes azul y roja.
