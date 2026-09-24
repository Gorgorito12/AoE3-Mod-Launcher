# Local files, desinstalar y exclusión de antivirus

Tres ventanas relacionadas. Todas tienen que ver con la instalación del mod en disco:

| Orden | Parte | Prototipo | Archivos del repo |
| --- | --- | --- | --- |
| 1 | Pestaña LOCAL FILES (49a) | `Prototipo-archivos.dc.html` | `ModPropertiesDialog.xaml` + `.xaml.cs` (`LoadLocalFiles`, `LoadManageInstalls`, la fila de cada copia) · `Models/LauncherConfig.cs` (`RemoveInstall`, `RegisterInstall`) |
| 2 | Ventana de desinstalar (49b, 49d) | `Prototipo-archivos.dc.html` | `UninstallDialog.xaml` + `.xaml.cs` · `Services/UninstallService.cs` · `Tests/UninstallSafetyTests.cs` |
| 3 | Exclusión de antivirus (48a, 48b) | `Prototipo-antivirus.dc.html` | `AntivirusExclusionDialog.xaml` + `.xaml.cs` · `Controls/SupportLink.cs` |

Abre los dos prototipos en un navegador con `support.js` en la misma carpeta. Cada opción lleva su id (49a, 48b…) y un texto encima que explica qué cambia y por qué. **49c y 48c son notas sobre el código, no pantallas.**

Repo: `Gorgorito12/AoE3-Mod-Launcher`, rama `main`, proyecto WPF `WarsOfLibertyLauncher/`.

**Los HTML son referencia de diseño, no código para copiar.** Recrea en WPF/XAML con los estilos que ya existen. Todo el texto pasa por `Localization/Strings.cs`, en español e inglés.

---

## 1. LOCAL FILES (49a)

**Lo que se usa va arriba, sin tener que bajar.** Hoy el diagnóstico y Uninstall están al final de la página, después de Mantenimiento.

### Primera fila, dos tarjetas

- **Izquierda, COPIA ACTIVA:** icono, nombre de la carpeta, ruta acortada, versión y la frase «This is the copy that opens when you press Play». Acciones: `Open folder`, `Repair` y, alineado a la derecha, **`Uninstall…`** en rojo con borde, sin relleno. Uninstall actúa sobre `_service.InstallPath`, así que va junto a esa copia. **La sección «Danger zone» del final desaparece.**
- **Derecha, SOMETHING NOT WORKING?:** una frase («Start with Verify: it checks every file without downloading anything») y cuatro botones en rejilla de 2×2: `Verify files` (el único sólido), `View logs`, `Share diagnostics` y el enlace de Discord de `SupportLink`.

### COPIES OF THIS MOD

- Una línea que explica qué es una copia: carpetas separadas del mismo mod, y solo la activa se abre con Jugar.
- **Cuál es la activa, a primera vista:** círculo relleno, fondo azul suave, barra de 3 px a la izquierda, etiqueta `ACTIVE` y el texto «Opens with Play» a la derecha. Las inactivas llevan un círculo vacío.
- **Cada copia inactiva tiene tres acciones, con su nombre escrito:**
  - `Make active`: lo que hoy hace `SwitchToInstall`.
  - **`Remove from list`**: lo que hoy hace el `✕`. El código de `RemoveInstall` dice que solo quita el registro y *«does NOT touch files on disk»*, pero con una ✕ parece que borra. Escríbelo en el botón.
  - **`Uninstall…`, NUEVO**: desinstala esa copia. Ver la sección 2.
- Debajo, una línea que explica la diferencia: «Remove from list forgets the copy and keeps its files · Uninstall… removes that copy from your disk».
- **Los dos botones para añadir, como tarjetas con una línea de texto:**
  - `Install another copy`: «Downloads a fresh copy into a new folder. The copy you have stays as it is.»
  - `Add a folder you already have`: «Adds a copy that is already on your disk to the list. Nothing is downloaded.»

### Al final

Mantenimiento (archivos temporales) y Carpetas (carpeta de AoE3 y «Can't find your installation?»). Se usan menos.

### Lo que no cambia

- El nombre de cada copia sigue siendo el de su carpeta, porque se quitó el renombrado.
- La ruta se sigue acortando por el medio con `PathDisplay.CompactPathMiddle`.
- Se mantiene el destello en la fila al activar una copia (`_recentlyActivatedInstallId`).
- **En el juego base (`IsStockGame`) se sigue ocultando todo:** copias, Mantenimiento, Uninstall y el nuevo Uninstall de las copias.

---

## 2. Desinstalar (49b, 49d)

### «Uninstall…» en una copia reutiliza la ventana de desinstalar

**No crees un segundo camino para borrar.** Para una copia inactiva:

1. Pide a `UninstallService` el plan de la **carpeta de esa copia**, no el de `_service.InstallPath`.
2. Abre `UninstallDialog` con ese plan.
3. Si termina bien, llama a `RemoveInstall(id)`.

La protección ya está en el plan: con `NotAValidInstall`, la ventana no borra nada.

**Antes de nada, confírmame** en `UninstallSafetyTests` que el plan también se niega a borrar si la carpeta es la de AoE3 o la contiene. Por eso mismo se oculta hoy Uninstall en el juego base. Si no está cubierto, añade ese test antes de conectar el botón.

### 49b · La ventana

- **Título de la barra: solo «Uninstall».** Hoy «Uninstall Wars of Liberty» sale en la barra y otra vez debajo, más grande. Debajo va la pregunta con el nombre de la copia: «Uninstall Wars of Liberty (2)?».
- **Tarjeta de la carpeta con borde rojo:** la etiqueta «THIS FOLDER IS DELETED», la ruta completa en monoespaciada y cortando en las barras, y `65,057 files · 4,130 folders` con separador de miles. **Hoy sale `65057`: usa formato `N0`.**
- **El aviso de que AoE3 no se toca sale una sola vez**, en una franja verde dentro de la tarjeta. Hoy lo dicen `DescriptionText` y `AoE3SafeNoteText`. Quita uno de los dos.
- **ALSO REMOVE, con una explicación en cada casilla:**
  - Shortcuts: «The desktop and Start menu shortcuts to this copy.»
  - Windows installed-apps entry: «Its line in Settings → Apps.» Hoy dice «(Add/Remove Programs)», que es jerga.
  - Saved games and profiles, sin marcar por defecto: la carpeta exacta en Documents y el consejo de dejarla sin marcar para conservar las partidas. **Sigue oculta cuando `UserDataFileCount` es 0**, como hoy.
- **«Reset launcher settings» va aparte**, en su propia tarjeta con la nota «Not needed to uninstall».
- **Pie:** `Cancel` como enlace y `Uninstall` en rojo sólido, que es el único sólido.

### 49d · Cuando la carpeta no es el mod (`NotAValidInstall`)

Hoy aparece el panel rojo, **pero las casillas y el botón Uninstall siguen visibles, solo desactivados**. En este modo:

- Oculta las casillas, la sección ALSO REMOVE y el botón Uninstall.
- Titular: «This folder doesn't look like Wars of Liberty».
- Qué se comprobó: el archivo que falta, con el `probeFile` que ya recibe la ventana, y «Nothing was deleted».
- La ruta.
- Qué hacer: «use Remove from list and add the right folder again».
- La única acción es `Close`.

`NothingToDo` recibe el mismo tratamiento, con su propio texto.

### Lo que hay que confirmar

- **`OptResetConfig`:** ¿reinicia la configuración de **todo el launcher** o solo la de este mod? Si es la de todo el launcher, dímelo: probablemente no debería estar en la ventana de desinstalar un mod.
- **«Your other copies are not touched»:** hoy `UninstallDialog` no sabe si hay otras copias. Si no se le pasa ese dato, la frase se queda en «Age of Empires III is not touched». No la escribas sin saberlo.

---

## 3. Exclusión de antivirus (48a, 48b)

Un solo diálogo con dos modos, como ya hace el código: **preventivo** (`ShowNotice`, antes de instalar) y **bloqueado** (`ShowBlocked`, después de que el antivirus borró el archivo).

### Lo que está mal hoy

- **Los tres botones salen dorados.** Copy y Cancel llevan `Background="#3a3d44"`, pero usan `SidebarPrimaryButton`, que no aplica ese fondo. Cancel pesa lo mismo que Continue. Cancel tiene que usar el estilo de enlace, no el primario con otro color encima.
- **El icono es un candado.** El comentario dice «shield glyph», pero `&#xE72E;` en Segoe MDL2 es el candado, y un candado da a entender «todo seguro». Usa el escudo: ámbar en el modo preventivo, rojo en el bloqueado.
- **Las rutas se parten a mitad de nombre** («Age Of / Empires 3»), porque `TextWrapping="Wrap"` corta en los espacios. Para que corte en las barras, inserta U+200B (espacio de ancho cero) después de cada `\` **solo en el texto que se ve**. `_clipboardText` ya está separado, así que lo que se copia no cambia.
- **El nombre del archivo se parte en la prosa** («AI3 / \wolai.upl»). `BodyText` es un solo `TextBlock`. Pártelo en tres: titular, etiqueta en monoespaciada con el archivo y un párrafo.

### 48a · Modo preventivo

- Escudo ámbar y el titular «Windows Defender may delete one of this mod's files». Debajo, la etiqueta con el archivo y «known false positive · already reported», y un párrafo corto.
- **Tarjeta FOLDERS TO EXCLUDE:** el botón `Copy both` va en la cabecera de la tarjeta, **junto a lo que copia**, no en el pie. Dos filas con su rótulo (`Download`, `Mod folder`) y la ruta completa.
- **Las instrucciones, en cuatro pasos numerados** dentro de la misma tarjeta, en vez de la frase con flechas de `DlgAntivirusHowTo`.
- `SupportLink` en su propia línea, **sin cambios**.
- **Pie:** la casilla «Don't show this again» a la izquierda, `Cancel` como enlace y `Continue install` como único botón sólido.

### 48b · Modo bloqueado

- Escudo rojo y el titular «Windows Defender removed a mod file». El texto empieza por **«Nothing is broken.»**
- La misma tarjeta de carpetas y pasos. El botón de copiar muestra el estado `✓ Copied` durante `CopiedFlash`.
- Sin casilla y sin Cancel, **igual que hoy**: el código los oculta en este modo a propósito.
- Pie: solo `Close`.

### Lo que hay que confirmar

- **Los rótulos `Download` y `Mod folder`** son cadenas nuevas.
- **`Open Windows Security ↗`**, opcional, abriría `windowsdefender://threatsettings`. No choca con la regla del código de no tocar nunca la configuración del antivirus, porque solo abre la pantalla. **Pruébalo en Windows 10 y 11 antes de añadirlo.** Si no funciona en los dos, no lo pongas.
- Si falta la carpeta de instalación (`hasInstallFolder` en falso), la fila «Mod folder» se oculta, como hoy.

---

## Tokens

Los mismos del resto del launcher (paleta navy).

| Uso | Hex |
| --- | --- |
| Fondo | `#0f1c2e` |
| Barra de título | `#233648`, título en dorado `#f4d9a0` |
| Tarjeta | `#12213a` · destacada o activa `#16263e` |
| Campo o ruta | `#0d1828` |
| Borde interior | `rgba(130,175,255,.08–.20)` · activa `rgba(47,127,224,.42)` |
| Azul de acción | `#2f7fe0` · texto `#8cbcf5` / `#8fb6ea` |
| Rojo de desinstalar | sólido `#b04a52` · con borde, sin relleno: texto `#e8a6a6`, borde `rgba(200,120,120,.45–.5)` |
| Ámbar de aviso | `#e6b455` · texto `#f0dcae` sobre `rgba(230,180,85,.12)` |
| Verde | `#4fd68a` · texto `#a9d9bb` sobre `rgba(53,196,111,.08)` |
| Texto | titular `#f4f8fc` · primario `#e8eef6` · cuerpo `#b9c9de` · atenuado `#8ea4c0` → `#6d829d` → `#5f7592` |

**Tipografía.** UI en Segoe UI; los titulares en serif. **Rutas, versiones, nombres de archivo y recuentos en monoespaciada.** Etiquetas de sección 10.5 SemiBold con `letter-spacing:.6px`.

**Regla de etiqueta.** Ninguna etiqueta de botón, rótulo ni título de sección se parte en dos líneas ni se recorta, tampoco en inglés. Lo único que se recorta con elipsis es la ruta acortada de cada copia.
