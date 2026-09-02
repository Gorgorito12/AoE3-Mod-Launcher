# Ajustes del mod (ventana por mod)

Rediseño de la ventana de ajustes **de cada mod**, distinta de los ajustes del launcher. Referencias visuales: **5a** (General), **5b** (Archivos), **5c** (Datos de usuario) y **5d** (Idioma y Complementos) en `Prototipo.html` — ábrelo en un navegador y haz zoom para leer los detalles.

Archivos del repo que cubre esta parte:

| Pantalla | Archivos |
| --- | --- |
| 5a General | `ModPropertiesDialog.xaml.cs` (pestaña GENERAL), `Services/UpdateService.cs`, `Models/ModState.cs` |
| 5b Archivos | `ModPropertiesDialog.xaml.cs` (LOCAL FILES), `Services/NativeInstallService.cs`, `Services/DiskSpaceService.cs`, `Services/InstallSnapshot.cs`, `Models/InstallManifest.cs` |
| 5c Datos de usuario | `ModPropertiesDialog.xaml.cs` (USER DATA), `Services/AppPaths.cs` |
| 5d Idioma y Complementos | `ModPropertiesDialog.xaml.cs` (LANGUAGE / ADDONS), `Services/AddonService.cs`, `Services/AddonRisk.cs`, `Services/AddonOwnership.cs`, `Services/AddonRegistry.cs`, `Services/AddonPaths.cs`, `Services/NsisExtractor.cs`, `Models/AddonManifest.cs`, `Models/LauncherConfig.cs`, `.claude/rules/addons.md` |

## Los tres defectos de fondo

1. **Todo se estira al ancho de la ventana.** En un monitor de 2540 px, «Añadir carpeta existente…» es un botón de 1100 px y las descripciones son líneas de 200 caracteres. Columna de contenido con `MaxWidth="620"` y `HorizontalAlignment="Left"`, padding 18/20.
2. **Todos los botones pesan lo mismo.** LOCAL FILES son 13 botones idénticos a ancho completo en 5 grupos: «Abrir carpeta» tiene el mismo peso visual que «Desinstalar mod». Los botones se dimensionan por su función: columna fija de 88 px (Abrir, Cambiar, Restaurar), 132 px (Verificar, Reparar, Liberar espacio), o al tamaño de su etiqueta cuando van en fila.
3. **Las secciones no comparten formato.** GENERAL y USER DATA son texto suelto; LANGUAGE y ADDONS usan tarjetas de 640 px; LOCAL FILES usa botones de ancho completo. Todas pasan al mismo patrón: etiqueta de grupo en mayúsculas + tarjeta con filas separadas por línea interior.

## Reglas transversales

- **Fila de ajuste**: título 12.5/600 `#e8eef6`; descripción 11.5/400 `#8ea4c0` `line-height 1.5`, una o dos líneas; acción a la derecha en columna fija. Rutas, tamaños, versiones y fechas en **monoespaciada** 11 px `#61779a`, con elipsis, nunca partidas en dos líneas.
- **Tarjeta de grupo**: radio 9, `#12213a`, borde `rgba(130,175,255,.11)`; filas separadas por `inset 0 -1px 0 rgba(130,175,255,.09)`, no por margen. Etiqueta encima: 10.5/600 `letter-spacing:.6px` `#61779a` en mayúsculas.
- **Casillas → interruptores** de 34×20 (radio 999, encendido `#2f7fe0` con pulgar blanco, apagado `rgba(255,255,255,.13)` con pulgar `#9fb3cd`). **El mismo estilo que en los ajustes del launcher** — un solo recurso compartido por las dos ventanas, no dos copias.
- **Los párrafos explicativos largos bajo una casilla desaparecen.** Lo que es una advertencia va en caja ámbar dentro de la tarjeta; lo que es descripción se acorta a una frase.
- **Menú lateral 206 px**, fondo `#16263e`. Entrada activa: `rgba(47,127,224,.16)` + barra izquierda de 2 px `#2f7fe0`, texto `#f0f5fb` 700. Inactiva `#b9c9de` 600. Ninguna etiqueta parte en dos líneas (`TextTrimming="None"`, sin ajuste); la pastilla de contador se separa con un espaciador flexible. Al pie, autor y web del mod.
- **Barra de título** (`#233648`, la misma que el launcher): icono del mod + nombre en serif dorado, pastilla de versión monoespaciada dorada, y **138 px reservados** al final para los botones nativos de Windows (46 px cada uno). Ningún contenido propio entra en esa zona.
- Los nombres de sección se acortan: LOCAL FILES → **Archivos**, USER DATA → **Datos**, ADDONS → **Complementos**

Esta es la sección donde el diseño tiene que seguir al motor, no al revés. El comportamiento real está documentado en `.claude/rules/addons.md` e implementado en `Services/Addon*.cs`; la UI solo tiene que **mostrar los estados que el motor ya calcula**, sin inventarse ninguno.

**Los cuatro niveles de riesgo de `AddonRisk` se ven, y nombran los archivos que los causan.** «Este complemento es peligroso» no sirve de nada; «reemplaza `data\protoy.xml`» le dice al jugador —o a su autor— exactamente qué pasa.

| `AddonRiskLevel` | Cómo se ve | Acción |
| --- | --- | --- |
| `Cosmetic` | etiqueta `COSMÉTICO` azul | interruptor normal |
| `MultiplayerRisk` | etiqueta `RIESGO MULTIJUGADOR` ámbar + caja ámbar con el motivo | botón «Activar igual…» (pide confirmación) |
| `Blocked` | etiqueta `BLOQUEADO` roja + caja roja nombrando el archivo | sin control: es incondicional |
| `Empty` | no se ofrece | — |

**Los dos motivos de `MultiplayerRisk` se explican por separado, porque el síntoma es distinto.** No los juntes en una frase genérica:

- **Archivos bajo `data\`** (`SimulationFiles`): la huella que valida la sala no los cubre, así que el jugador **entra bien y la partida se desincroniza a mitad**.
- **Archivos `.xmb`** (`VersionMatchFiles`): AoE3 los usa para su propia comprobación de versión en LAN, así que puede que la partida **no llegue a empezar** con quien no tenga el complemento.

El humo de pólvora reemplaza 77 `.xmb` y la interfaz transparente escribe 25 archivos dentro de `data\`: son casos reales del catálogo, no hipótesis. La caja ámbar lleva un enlace «Ver los N archivos» que despliega la lista completa.

**`Conflict` es un estado propio y hay que mostrarlo.** Dos complementos no pueden poseer el mismo archivo: el segundo se rechaza. La fila lo dice nombrando **el archivo y el otro complemento** («ya pertenece a Interfaz transparente»), porque la salida es desactivar ese primero.

**Los archivos omitidos se nombran.** El launcher nunca escribe ejecutables ni documentación en la carpeta del juego (`AddonRisk.ExecutableExtensions` / `DocumentExtensions`). «1 archivo omitido» es inútil cuando el complemento luego no funciona; nombrar `Building Rotator.exe` dice qué faltó.

**Dos grupos, porque el estado vive en dos sitios distintos:**

- **DEL CATÁLOGO** — `ModState.EnabledAddons`, **por instalación**. El interruptor de una fila afecta a esta instalación, no a las demás.
- **IMPORTADOS** — `LauncherConfig.ImportedAddons`, **de todo el launcher**: se importan una vez y están disponibles en cualquier mod. El subtítulo del grupo lo dice con esas palabras. Su id es `local-<sha12>`, mostrado en monoespaciada.

**Otros detalles que salen del motor:**

- El instalador NSIS (interfaz transparente) lleva etiqueta `INSTALADOR` y una nota de que **Windows pedirá confirmación**: se ejecuta en una carpeta temporal y se aplica el resultado. Un interruptor a secas sería mentira.
- Los del catálogo verifican su **SHA-256** antes de escribir nada (`AddonApplyStatus.HashMismatch`); si falla, la fila lo dice en rojo en vez de un error genérico.
- La intro de la sección menciona que los complementos **se vuelven a aplicar solos tras cada actualización y reparación**, y que al desactivar uno se restauran los originales de `addons\_originals\`. Es lo que responde «¿perderé esto al actualizar?» sin que el jugador tenga que preguntarlo.
- «Añadir desde archivo…» vive en la cabecera del grupo IMPORTADOS, que es donde aterriza lo que añade.
