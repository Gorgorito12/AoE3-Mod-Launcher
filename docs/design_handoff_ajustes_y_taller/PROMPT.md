# Texto para pegar en Claude Code

Copia esta carpeta dentro del repo (p. ej. `docs/design_ajustes_y_taller/`), abre una terminal ahí, lanza `claude` y pega esto:

---

Lee `docs/design_ajustes_y_taller/README.md` completo, y después las tres specs que indica. Es el rediseño de tres pantallas de este launcher WPF, para hacer en este orden:

1. **Ajustes del launcher** — `SPEC-1-ajustes-launcher.md` — `SettingsDialog.xaml.cs`
2. **Ajustes del mod** — `SPEC-2-ajustes-mod.md` — `ModPropertiesDialog.xaml.cs`
3. **Taller** — `SPEC-3-taller.md` — `Controls/ModsBrowser.xaml` + `.xaml.cs`

`Prototipo.html` es una referencia de diseño: NO lo copies ni lo integres. Recrea las pantallas en WPF/XAML con los estilos y controles que ya existen en `WarsOfLibertyLauncher/`.

**No toques ninguna otra pantalla.** Salas, Crear sala, Lobby, Partida en curso, Clasificación, Historial y Perfil quedan como están.

## Antes de escribir código, hazme un plan

1. Lee `SettingsDialog.xaml.cs`, `ModPropertiesDialog.xaml.cs` y `Controls/ModsBrowser.xaml(.cs)` — acota la lectura por regiones, son archivos grandes — y dime cómo está montada hoy cada pantalla.
2. **Antes de tocar la sección de complementos**, lee `.claude/rules/addons.md`, `Services/AddonRisk.cs`, `Services/AddonService.cs` y `Services/AddonOwnership.cs`, y confírmame que los estados que pide SPEC-2 (Cosmetic / MultiplayerRisk / Blocked / Conflict / HashMismatch, más los archivos omitidos por nombre) son los que el motor ya devuelve.
3. **Antes de tocar el Taller**, confirma en `CLAUDE.md` la regla de que el Taller nunca instala y que la insignia es estado en disco mientras el botón es pertenencia a la colección. Contrasta además la tabla de datos de SPEC-3 con `Models/ModCatalogManifest.cs`: dime campo por campo si existe en el esquema, si está relleno en `mods/*/mod.json` y si el launcher lo parsea.
4. Propón el trabajo en commits pequeños, uno por sección, empezando por Ajustes del launcher → General.
5. Señala cualquier punto de las specs que choque con la arquitectura real, con `docs/ARCHITECTURE.md` o con un test existente, en vez de forzarlo.

## Reglas al implementar

- **Haz los controles compartidos UNA vez.** El interruptor, la fila de ajuste, la tarjeta de grupo y la etiqueta de estado son los mismos en las tres pantallas; van a recursos compartidos, no copiados por ventana. Si acabas con dos estilos de interruptor, algo se hizo mal.
- **El contenido no se estira**: `MaxWidth` + `HorizontalAlignment="Left"`. Es el defecto que más se repite.
- **Ninguna etiqueta se parte en dos líneas ni se recorta**, tampoco traducida al inglés.
- **Los botones de acción van en columnas de ancho fijo**; el ancho no depende del texto.
- Cero cadenas literales: todo a `Localization/Strings.cs`, español e inglés. `DlgSettingsPreviewToasts` y `DlgSettingsPreviewToastsHint` salen hoy sin traducir: dalas de alta.
- Respeta los valores exactos de la tabla de Tokens del README. El dorado solo en los tres sitios que el README permite.
- Rutas, versiones, tamaños, fechas y hashes en monoespaciada, con elipsis.
- **No inventes datos ni endpoints.** Si un dato que la spec pide no existe hoy, dilo en el plan y omite ese elemento en vez de dejar un hueco vacío en la build.
- **No cambies el comportamiento de ningún ajuste**: esto es una reorganización de la UI. Las excepciones están dichas en cada spec (el asistente de Radmin pasa de casilla a desplegable de tres estados; las copias de seguridad pasan a lista).
- **No rehagas lo que el Taller ya tiene**: galería de capturas, aviso de compatibilidad, orden por estado y `BuildBannerGradient` ya existen en `ModsBrowser`. Reutilízalos.
- Añade o actualiza tests donde ya haya cobertura equivalente si cambias comportamiento observable. Compila y pasa los tests antes de darme cada commit por terminado.

Cuando acabes la primera sección (Ajustes del launcher → General), párate y enséñame una captura o el XAML resultante antes de seguir.
