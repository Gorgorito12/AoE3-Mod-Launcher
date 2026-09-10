# Texto para pegar en Claude Code

Copia esta carpeta dentro del repo (p. ej. `docs/design_publicar_instalar/`), abre una terminal ahí, lanza `claude` y pega esto:

---

Lee `docs/design_publicar_instalar/README.md` completo, y después las dos specs que indica. Es el rediseño de dos diálogos de este launcher WPF, en este orden:

1. **Publicar mi mod** — `SPEC-1-publicar-mi-mod.md` — `PublishModDialog`
2. **Dónde se instala** — `SPEC-2-donde-se-instala.md` — `InstallFolderDialog`

`Prototipo.html` es una referencia de diseño: NO lo copies ni lo integres. Recréalos en WPF/XAML con los estilos y controles que ya existen en `WarsOfLibertyLauncher/`. En la parte 1, **20a, 20b y 20c son tres pasos del mismo diálogo**, no alternativas.

**No toques ninguna otra pantalla.** Salas, Torneos, Clasificación, Estadísticas, Taller, el asistente de Radmin y los diálogos de ajustes quedan como están.

## Antes de escribir código, hazme un plan

Las specs se escribieron leyendo los dos `.xaml`, pero **no el code-behind de ninguno**. Empieza por ahí:

1. **Lee `PublishModDialog.xaml.cs`** y dime tres cosas:
   - **Qué valida `NextButton`.** El XAML ya tiene diez `Error*` que nacen `Collapsed`, así que la validación en línea está montada; quiero saber si `Next` la respeta o si se puede llegar al paso 6 con cualquier cosa.
   - **El valor por defecto de `FieldMechanism`.** En el XAML los items van `WolPatcher, GitHubReleases, DelegatedExternal, Manual` — el legado primero — mientras `HintMechanism` recomienda GitHubReleases para mods nuevos, y la captura del usuario muestra WolPatcher seleccionado. Si el orden o el `SelectedIndex` hacen que el legado sea el defecto, **cámbialo**.
   - **A qué rama del JSON escribe cada uno de los tres pares de URL+SHA** (`FieldPayloadUrls`/`FieldPayloadSha256` en el paso 3, `FieldWolPayloadZipUrls`/`FieldWolPayloadSha256` y `FieldGhExternalUrl`/`FieldGhExternalSha` en el paso 4). De eso depende si se fusionan o solo se reagrupan. Contrástalo con `BuildModJsonTests.cs`.
2. **Confirma el objetivo que la spec 1 usa como criterio:** el comentario de `Window.Resources` de `PublishModDialog.xaml` dice que los seis pasos deben caber en la ventana de 640 px sin scroll anidado. Mídelo en el paso 3 y dime cuánto se pasa.
3. **Lee `InstallFolderDialog.xaml.cs`** y dime cómo se rellenan hoy los dos campos de ruta, y si el de destino se recalcula al cambiar el de origen.
4. **Deriva la lista de «lo que falta» del paso 6 del esquema del catálogo** (`schema/mod.schema.json`), no de una lista escrita a mano. Dime qué campos son obligatorios ahí.
5. **Confirma o descarta el motivo** de que la copia de ajustes no pueda hacerse al instalar (la spec 2 lo deduce, no lo verifica). Si no se confirma, la UI dice solo el cuándo y omite el por qué.
6. Dime qué claves de `Localization/Strings.cs` faltan o cambian. Ojo: sacar «(optional)» de las etiquetas **modifica cadenas existentes**, no solo el XAML.
7. Propón commits pequeños, empezando por el chasis del asistente (columna acotada + riel de pasos).

## Reglas al implementar

- **El contenido no se estira.** `PublishModDialog` tiene un `Grid` sin `MaxWidth` dentro del `ScrollViewer`: acótalo a 620 y alinéalo a la izquierda. Es el defecto que más se repite en este proyecto.
- **El paso 3 se agrupa por la pregunta que responde** —cómo se instala, cómo se reconoce, cómo se lanza, datos del jugador, avanzado— y el marcador de contenido se colapsa tras una casilla, porque su propia ayuda dice que solo aplica cuando el testigo existe en el juego base. Usa el patrón de `TeamSourceBlock` en `CreateTournamentDialog`.
- **El paso 6 empieza por lo que falta**, con el paso al que volver, y **no bloquea**: publicar un mod mínimo es legítimo. Hoy un mod.json de cuatro campos se presenta igual que uno completo, y el rechazo llega en la revisión manual de GitHub.
- **Un solo sólido en el paso 6.** «Finish» desaparece: abrir el PR *es* terminar.
- **En el instalador, las dos filas de ruta miden exactamente lo mismo.** La spec 2 avisa de un error concreto que cometí en la maqueta: dibujar la anidación como un carril dentro de la segunda fila le roba ancho a su campo y rompe la simetría. La relación va en la etiqueta.
- **Ruta en reposo = raíz + tramo elidido + última carpeta.** Nunca se pierde la raíz ni la última carpeta. `Aoe3PathTextBox` es `IsReadOnly`, así que ese solo cambia por «Cambiar…».
- **Los ejemplos con una sola barra invertida.** `data\napoleonic.xml`, no `data\\napoleonic.xml`.
- **No rompas lo que estos diálogos ya hacen bien:** `Aoe3Row` se colapsa en instalación superpuesta y `CopySettingsRow` solo aparece si hay otro mod del que copiar.
- Cero cadenas literales: todo a `Localization/Strings.cs`, español e inglés.
- Respeta los valores exactos de las tablas de Tokens. Reutiliza los recursos compartidos del launcher; si creas un estilo nuevo (riel de pasos, campo de ruta elidida), hazlo compartido.
- Señala cualquier punto de las specs que choque con la arquitectura real, con `CLAUDE.md` o con un test existente, en vez de forzarlo. `BuildModJsonTests` y `DialogXamlTests` cubren esto: si cambias comportamiento observable, actualiza o añade el test.
- Compila y pasa los tests antes de darme cada commit por terminado.

Cuando acabes el chasis del asistente, párate y enséñame una captura antes de seguir con el paso 3.
