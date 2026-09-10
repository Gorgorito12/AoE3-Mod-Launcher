# 1 · Publicar mi mod

Referencias visuales en `Prototipo.html`: **20a** (el chasis del asistente y el paso 1), **20b** (el paso 3, que es el que desborda) y **20c** (el paso 6). Son las tres el mismo diálogo, no alternativas.

| Qué | Archivos del repo |
| --- | --- |
| El asistente | `PublishModDialog.xaml` + `.xaml.cs` |
| Textos | `Localization/Strings.cs` — claves `PublishWizard*` (~2872-2960) y `PublishWizardIntro` (~3098) |
| El constructor del JSON | lo fija `WarsOfLibertyLauncher.Tests/BuildModJsonTests.cs` |
| El esquema | `schema/mod.schema.json` del repo del catálogo |
| Guía | `CONTRIBUTING.md:91`, `docs/MODDING.md` |

**Leí `PublishModDialog.xaml` completo; no leí el `.xaml.cs`.** Todo lo que sigue sobre estructura, nombres de campo y geometría sale del XAML. Lo que dependa de lógica —valores por defecto del desplegable, cuándo se muestran los `Error*`, qué valida `Next`— está marcado como no verificado al final.

El HTML es una **referencia de diseño**, no código para copiar. Todo el texto pasa por `Localization/Strings.cs` (español e inglés).

## El objetivo que el propio código declara, y que el paso 3 incumple

La ventana es `Width="760" Height="640"` (tu captura sale a 2540 px, o sea escalada). El comentario de `Window.Resources` dice:

> «Compact form chrome shared across every step. Keeps the wizard's form rows tight (40px target height) so all six steps fit in the 640px window **without needing nested scrollbars per panel**.»

El paso 3 tiene diez campos más una sección «Advanced» con dos áreas de texto de 54 px. En tu cuarta captura la etiqueta del desplegable sale **cortada por arriba**: el objetivo declarado está roto justo en el paso más complejo.

Ese es el criterio de este handoff. No es «hacer más bonito el paso 3»: es cumplir lo que el archivo ya se propuso.

## El hallazgo grande: tres pares de URL + SHA en dos pasos

Verificado en el XAML:

| Campos | Paso | Panel |
| --- | --- | --- |
| `FieldPayloadUrls` + `FieldPayloadSha256` | **3** | bajo `Step3AdvancedHeader` |
| `FieldWolPayloadZipUrls` + `FieldWolPayloadSha256` | **4** | `MechanismWolPanel` |
| `FieldGhExternalUrl` + `FieldGhExternalSha` | **4** | bajo `GitHubAdvancedHeader` |

Tres veces el mismo par conceptual —de dónde se descarga el paquete y su hash— repartido entre dos pasos. Y la ayuda del SHA está escrita **palabra por palabra igual** en los pasos 3 y 4: «One 64-hex SHA-256 per line, matching the URLs above in order. Strongly recommended — the launcher rejects a download whose hash doesn't match, blocking tampered payloads.» Se lee en tus capturas cuarta y quinta.

**La corrección:** los de `Step3Advanced` se van al paso 4, que es donde viven los otros dos pares y donde el usuario ya está pensando en descargas. El paso 3 se queda con la instalación local.

Antes de moverlos, **confirma en el `.xaml.cs` si los tres pares escriben a la misma rama del JSON o a ramas distintas**. Si son campos distintos del esquema con el mismo aspecto, hay que diferenciarlos por etiqueta en vez de fusionarlos, y decir en cada uno para qué mecanismo aplica.

## 20a — El chasis y el paso 1

**Los seis pasos se ven.** Hoy la única señal de progreso es `HeaderStep`, un `TextBlock` dentro de `TitleBar` que dice «Step 1 of 6». En seis pasos con ~40 campos, eso no dice qué queda. Riel horizontal bajo la barra de título: seis nodos con nombre corto (Identidad, Aspecto, Instalación, Actualizaciones, Textos, Revisar), check verde en los hechos, azul en el actual, aro vacío en los pendientes.

**La intro baja de 60 palabras a una línea.** `IntroText` va hoy en un `Border` con `BorderBrush="{DynamicResource AccentBrush}"` y `Margin="0,0,0,14"`, así que empuja el formulario unos 130 px hacia abajo justo donde hay que empezar a escribir. Se queda una frase —qué produce el asistente y que aquí no se instala nada— y el resto tras un botón «Cómo funciona» en la cabecera, con el estado recordado.

**El campo deja de medir 2500 px.** El `ScrollViewer` tiene `Padding="32,18"` y dentro un `Grid` sin `MaxWidth`, así que los `FieldInput` se estiran a la ventana. Columna con `MaxWidth="620"` y `HorizontalAlignment="Left"`.

**El id muestra la ruta que va a crear.** `HintId` dice hoy «Used as the folder name under /mods/». En su lugar, bajo el campo: `Creará mods/napoleonic-era/mod.json`, en monoespaciada, derivado en vivo. Responde «es el nombre de la carpeta» sin gastar una frase, y anticipa dónde va a aterrizar el PR.

**«Atrás» no se pinta en el paso 1.** En tu primera captura está visible y activo. No hay atrás.

**Las etiquetas «(optional)» salen del texto.** Hoy están dentro de la propia cadena de la etiqueta —`Author (optional)`, `Subtitle (optional)`— lo que obliga a traducir el paréntesis y hace la etiqueta más larga que el dato. Pasa a una marca gris aparte, con un solo estilo. Ojo: eso significa **cambiar las cadenas** de `Strings.cs`, no solo el XAML.

## 20b — El paso 3

**Los campos se agrupan por la pregunta que responden**, en vez de ser una lista plana de diez:

- **Cómo se instala** — tipo (`FieldInstallType`) + carpeta propuesta (`FieldDefaultFolder`)
- **Cómo se reconoce** — archivo testigo (`FieldProbeFile`) + el marcador, condicional
- **Cómo se lanza** — ejecutable (`FieldExecutable`) + argumentos (`FieldArguments`)
- **Datos del jugador** — `FieldUserDataFolder`
- **Avanzado**, colapsado — `FieldInstallProductGuid` y lo que quede

**El marcador de contenido se colapsa tras una casilla.** Su propia ayuda dice que solo se necesita «when your probe file also exists in the base game»: es una respuesta condicional y hoy se pregunta siempre, con la ayuda más larga del paso (unas 45 palabras). Casilla: «Ese archivo también existe en el AoE 3 original» → aparece el campo. Es el patrón que `CreateTournamentDialog` ya usa con `TeamSourceBlock`.

**Una línea de ayuda por campo.** Las de este paso tienen entre 12 y 45 palabras. La referencia a `MODDING.md §4` pasa a enlace, no a texto corrido dentro de la cursiva.

**Nada de valores de ejemplo con doble barra.** El ejemplo es `data\napoleonic.xml`, una sola barra, igual que en `HintProbeFile`.

## 20c — El paso 6

**Presenta igual un mod.json de cuatro campos y uno completo.** El de tu captura tiene `id`, `displayName`, `install.type` y `update.mechanism`, y nada más: sin archivo testigo, sin ejecutable, sin descripción, sin icono. El asistente te deja pulsar «Open PR on GitHub» con eso, y su propio `NextStepsBody` avisa de que los mods nuevos pasan **revisión manual** — así que el rechazo llega en GitHub, días después, no aquí.

**Empieza por lo que falta**, en caja ámbar, con una pastilla por hueco que dice a qué paso volver. **No bloquea** —publicar un mod mínimo es legítimo— pero deja de ser una sorpresa. Deriva la lista del esquema del catálogo, no de una lista escrita a mano: `schema/mod.schema.json` es la fuente, y si un campo es obligatorio ahí, aquí es un hueco.

**Hoy hay cuatro acciones compitiendo:** `CopyJsonButton` y `OpenPrButton` en el cuerpo (el segundo con `Background="{DynamicResource AccentBrush}"`), más `CancelButton` y `NextButton`/Finish en el pie (el segundo también con `AccentBrush`). Dos sólidos doradas en la misma pantalla. La acción de verdad sube al pie como **único sólido**, «Copiar» se queda como botón pequeño junto a la cabecera del JSON, y **«Finish» desaparece**: abrir el PR *es* terminar.

**El bloque «What happens after you publish» se reduce.** Hoy son cinco pasos numerados más dos párrafos, uno con una URL cruda de GitHub. Se queda en tres líneas y un enlace; el párrafo sobre publicar una actualización no pertenece a la primera publicación — va en la guía, o en el paso 4 junto al mecanismo.

## Tokens

Los mismos del resto del launcher.

| Uso | Hex |
| --- | --- |
| Fondo del cuerpo | `#0f1c2e` |
| Barra de título | `#233648` (título en dorado `#f4d9a0`) |
| Riel de pasos y pie | `#12213a` |
| Campo, desplegable, bloque JSON | `#0d1828` |
| Borde interior | `rgba(130,175,255,.11–.20)` · campo con foco `rgba(47,127,224,.55)` a 2 px |
| Azul de acción | `#2f7fe0` · texto `#8cbcf5` / `#8fb6ea` |
| Verde hecho / válido | `#4fd68a` · texto `#8fe0b0` sobre `rgba(53,196,111,.18)` |
| Ámbar de lo que falta | `#e6b455` · texto `#d8bd8a` / `#e0be7f` / `#f0dcae` sobre `rgba(230,180,85,.08)` |
| Texto | titular `#f4f8fc` · primario `#e8eef6` · cuerpo `#b9c9de` · atenuado `#8ea4c0` → `#6d829d` → `#5f7592` |

**Tipografía.** UI en Segoe UI; los títulos de paso en serif. **Rutas, ids, hashes, nombres de archivo y el JSON en monoespaciada.** Etiqueta de campo 11.5 SemiBold; ayuda 10.5; etiqueta de sección 10.5 SemiBold `letter-spacing:.6px`.

**Medidas.** Ventana 760 × 640 · columna de contenido 620 · barra de título 38 · riel 41 · campo 34 · botón del pie 34 con `MinWidth` 120 · nodo del riel 18.

**Regla de etiqueta.** Ninguna etiqueta, marca de «opcional» ni nombre del riel se parte en dos líneas ni se recorta, tampoco en inglés. El riel usa un espaciador flexible entre nodos que absorbe la holgura.

## Lo que no verifiqué

- **Qué valida `NextButton`.** No leí el `.xaml.cs`. Los `Error*` existen en el XAML (`ErrorId`, `ErrorDisplayName`, `ErrorAccent`, `ErrorIcon`, `ErrorBanner`, `ErrorExecutable`, `ErrorPayloadSha256`, `ErrorSourceRepo`, `ErrorWebsite`, `ErrorGhExternalSha`) y nacen `Collapsed`, así que **la validación en línea ya está montada**. Lo que no sé es si `Next` la respeta.
- **El valor por defecto del mecanismo.** En el XAML los `ComboBoxItem` van `WolPatcher, GitHubReleases, DelegatedExternal, Manual` — el legado primero — mientras `HintMechanism` dice que GitHubReleases es el recomendado para mods nuevos, y tu captura muestra WolPatcher seleccionado. **Si el orden o el `SelectedIndex` hacen que el legado sea el defecto, cámbialo**: pon el recomendado primero y por defecto.
- **Si los tres pares de URL+SHA escriben al mismo sitio del JSON.** Decide con eso si se fusionan o solo se reagrupan.
- **El campo de enlaces.** `FieldLinks` es un `TextBox` con `TextWrapping="NoWrap"` y sintaxis `type|url` a mano, hasta 4 entradas, con los tipos de un conjunto cerrado (website, discord, moddb, forum, wiki, video, other). Un conjunto cerrado pide filas con desplegable + campo, pero eso cambia el parseo: dime si merece la pena.
- **`FieldDescriptionEs` no tiene ayuda propia** (solo hay un `HintDescription`, tras el inglés) y no dice si es opcional, aunque todos los demás campos opcionales lo dicen. Aclara cuál de las dos es.
