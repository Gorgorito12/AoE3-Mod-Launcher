# Texto para pegar en Claude Code

Copia esta carpeta dentro del repo (p. ej. `docs/design_share_diagnostics/`), abre una terminal ahí, lanza `claude` y pega esto:

---

Lee `docs/design_share_diagnostics/README.md` completo antes de escribir código. Los jugadores no encuentran **Share diagnostics** en la ventana del mod (`ModPropertiesDialog`). Hay que hacerlo más visible y que el buscador lo encuentre al escribir «diagnostico» o palabras parecidas, en español o en inglés.

El prototipo de referencia es `Local files.dc.html`, **turno 50** (50a, 50b, 50c). El turno 49, que va debajo, es lo que ya está en main. NO copies el HTML: recrea en WPF/XAML con los estilos que ya existen.

**No toques nada más.** Ni la tarjeta de la copia activa, ni la lista de copias, ni otras secciones del diálogo, ni `LauncherSettingsDialog` (aunque comparte `SectionSearch`, su comportamiento no debe cambiar salvo por leer `Keywords` si las tiene).

## Antes de escribir código, hazme un plan

1. **Confirma el diagnóstico de 50c.** Lee `Controls/SectionSearch.cs` y dime si es verdad que `TextOf` no lee el Content de texto plano de `ShareDiagnosticsBtn`, y que por eso hoy ni «Share diagnostics» lo encuentra. Dime también si la tarjeta «Something not working?» tiene filas con estilo `SetRow`/`SetActionRow` o es un hijo directo sin filas, porque eso decide dónde va `Keywords`.
2. **Propón `SectionSearch.Keywords`** como propiedad adjunta, y cómo la lee `TextOf` sin cambiar `Matches`. Las palabras de los dos idiomas se cargan siempre.
3. **Dime cómo encaja la lista de resultados** (50b) con `ModSearchBox_TextChanged` y `SectionSearch.Apply`: dónde se dibuja (Popup o capa del propio diálogo) y cómo se reúnen los elementos con `Keywords`.
4. **Dime qué pasa con la lógica de ~658-681** de `ModPropertiesDialog.xaml.cs`, que reparte View logs y Share diagnostics por ancho, cuando Share diagnostics sale de esa rejilla.
5. **Comprueba que la caja del carril no ensancha el carril**, que ya está en una columna Auto con el ancho fijado por la URL del pie.
6. Propón commits pequeños, en este orden: `Keywords` con sus tests → tarjeta en dos pasos → caja fija en el carril → lista de resultados con anillo de resaltado.

## Reglas al implementar

- **Turquesa `#3cc6c9` solo para el diagnóstico.** No lo uses en nada más.
- **La caja del carril ejecuta la misma `_shareDiagnostics`** que el botón. No navega a Local files.
- **`Matches` no cambia.** Las palabras clave se suman al texto que se compara; no metas lógica de prefijos ni de sinónimos en `Matches`.
- **Palabras clave de los dos idiomas siempre**, sea cual sea el idioma de la interfaz.
- **Anillo de resaltado de unos 3 s** al abrir un resultado. Sin animaciones del sistema (`SystemParameters.ClientAreaAnimation`), sin transición.
- Cero cadenas literales: todo a `Localization/Strings.cs`, en español e inglés.
- Añade tests de `SectionSearch` que comprueben que «diagnostico», «diagnóstico», «reporte» y «crash» encuentran el botón con la interfaz en inglés y en español.
- Señala cualquier punto del README que choque con la arquitectura real, con `CLAUDE.md` o con un test existente, en vez de forzarlo.
- Compila y pasa los tests antes de darme cada commit por terminado.

Cuando tengas la tarjeta en dos pasos y la caja del carril, párate y enséñame una captura antes de hacer la lista de resultados.
