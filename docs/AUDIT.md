# Auditoría de transparencia y seguridad · Transparency & Security Audit

> **La versión en inglés está más abajo, en este mismo documento.** ·
> *The English version is further down, in this same document.*

- **Fecha de la auditoría · Audit date:** 2026-09-09
- **Alcance · Scope:** el código del launcher en este repositorio (`WarsOfLibertyLauncher/`).
- **Método · Method:** revisión estática — leyendo el código, no confiando en la documentación.
- **Veredicto · Verdict:** **No se encontró comportamiento malicioso.** Las advertencias de
  antivirus son un **falso positivo** con causa conocida (ver §7). · **No malicious behaviour
  found.** The antivirus warnings are a **false positive** with a known cause (see §7).

---

## Español

> **¿Qué es esto?** Una auditoría **con referencias al código** del **AoE3 Mod Launcher**,
> escrita para que cualquiera —un jugador, un moderador o un analista de antivirus— pueda
> confirmar que el launcher **no es malware** y ver **exactamente** cómo se construyó,
> incluidos los modelos de IA usados. Cada afirmación apunta a un archivo real que puedes
> abrir tú mismo.

Este es el complemento técnico y detallado de la FAQ breve
[**IS-IT-A-VIRUS.md**](IS-IT-A-VIRUS.md). Si solo quieres "¿es seguro y cómo lo ejecuto?",
lee esa primero. Este documento es la evidencia que la respalda.

> **Sobre las cifras de este documento.** Las de abajo se midieron el día de la auditoría y el
> código sigue cambiando. No están aquí para que te las creas, sino para que las **repitas**:
> cada una viene con el comando que la produce (§2, §10). Si tu resultado no coincide con el
> texto, **el texto es lo viejo** — el repositorio es la fuente.

### Veredicto de un vistazo

| Pregunta | Hallazgo | Evidencia |
| --- | --- | --- |
| ¿Se ejecuta como administrador? | **No** — sin elevación (`asInvoker`); pide UAC solo para escribir en carpetas protegidas o registrar la clave privada de un mod | `app.manifest`, `ElevationService.cs` |
| ¿Keylogging / captura de entrada? | **Ninguno** | grep `SetWindowsHookEx` / `GetAsyncKeyState` = 0 |
| ¿Inyección en procesos? | **Ninguna** | grep `VirtualAllocEx` / `WriteProcessMemory` / `CreateRemoteThread` = 0 |
| ¿Toca o desactiva el antivirus? | **No** — toda mención a "Defender" es defensiva (manejo de falsos positivos) | `Strings.cs`, `PayloadFileBlockedException.cs` |
| ¿Ofuscación / empaquetado / cadenas ocultas? | **Ninguna** — sin ConfuserEx/Fody/Costura, sin URLs en base64 | grep = 0 |
| ¿Ejecución de código dinámico o remoto? | **No** — la única reflexión es el objeto COM `WScript.Shell` para accesos directos | `NativeInstallService.cs` |
| ¿Endpoints de red ocultos (C2)? | **Ninguno** — cada host es un literal en texto plano; solo se interpolan `repo`/`tag`/`id` | §3, `GitHubReleaseDownloader.cs` |
| ¿SDKs de analítica / rastreo? | **Ninguno** — 4 dependencias de código abierto conocidas, cero proveedores de telemetría | `.csproj` |
| Registro de telemetría local | **Opcional, desactivado por defecto**, nunca sale de tu PC | `LauncherConfig.cs`, `MultiplayerTelemetry.cs` |
| ¿Borra tus archivos? | Solo su propio clon del mod, con salvaguardas; **nunca** el juego base ni tus partidas | `UninstallService.cs`, `AoE3UserDataRedirect.cs` |
| ¿Cambios a nivel de máquina (HKLM)? | **Sí, dos, y ambos declarados** — ver §4. No son "por-usuario" | `NativeInstallService.cs`, `SetupPathPatcher.cs` |
| Código abierto y licencia | **Sí** — Apache-2.0, totalmente público | `LICENSE` |
| ¿Hecho con IA? ¿Qué modelo? | **Claude, de Anthropic**, en varias versiones — verificable en el historial de git | §2 |

---

### 1. Qué es el launcher (y qué NO es a propósito)

El AoE3 Mod Launcher es una aplicación de escritorio nativa de Windows (WPF, .NET 8) que
instala, actualiza, verifica y ejecuta mods de conversión total de *Age of Empires III*, con
una pestaña opcional de multijugador. En la fecha de esta auditoría son **~108.000 líneas de
C#** en **194 archivos** (131 de ellos bajo `Services/`), respaldadas por **146 archivos de
tests** — un proyecto de ingeniería real y mantenido, no una carcasa fina que esconde otra cosa.

Tiene **exactamente cuatro** dependencias externas, todas librerías de código abierto conocidas:
`SharpCompress` (extracción de archivos), `System.IO.Hashing` (CRC32),
`Hardcodet.NotifyIcon.Wpf` (el icono de bandeja) y `XamlAnimatedGif` (la galería de capturas).
**Ninguna** es un SDK de telemetría, analítica ni publicidad.

**Lo que NO hace** (confirmado por ausencia en el código — §6): sin keylogging, sin captura de
pantalla/cámara/micrófono, sin inyección en procesos, sin manipular el antivirus, sin
autoarranque oculto, sin criptominería, sin robo de credenciales, sin binarios de terceros
empaquetados. **No** descarga ni redistribuye *Age of Empires III*: debes tener una copia legal,
que el launcher clona localmente (`DISCLAIMER.md`).

### 2. Hecho con IA — qué modelos de Claude

Este launcher lo desarrolla su responsable, **Gorgorito12** (`jeisonso1997@gmail.com`), con
asistencia de programación en pareja de la IA **Claude, de Anthropic** (mediante *Claude Code*).
A lo largo del proyecto se han usado **varias versiones del modelo** —Opus 4.6, 4.7, 4.8 y Opus
5, incluidas sus variantes de contexto de 1 millón de tokens—, que es lo normal en meses de
trabajo: el modelo disponible cambia. **Ningún otro modelo o proveedor de IA** aparece en la
historia del proyecto: ni GPT, ni Copilot, ni Gemini, ni Llama.

No hace falta que nos creas: está en el historial público de commits. Estos comandos **no
prescriben un resultado**, te dicen qué mirar:

```bash
# Lista todos los trailers de coautoría de Claude, con su recuento.
# Verás varias versiones del modelo, no una sola.
git log --format=%b | grep -io "Co-Authored-By: Claude[^<]*" | sort | uniq -c

# Confirma que no se referencia ningún otro modelo/proveedor de IA (sin resultados):
git log --all --format="%B" | grep -iE "gpt|copilot|gemini|llama|codex"

# Reparto de autoría de los commits:
git log --format='%an' | sort | uniq -c | sort -rn
```

En la fecha de esta auditoría el repositorio tiene **373 commits**: 364 firmados por el
responsable humano y 9 por Claude. La asistencia de la IA se atribuye con los *trailers*
estándar `Co-Authored-By` y con los nombres de rama `claude/*` que usa *Claude Code*.

**Por qué esto es un punto de transparencia y no un problema de seguridad.** Quién (o qué)
*escribió* el código no cambia si es seguro — lo que importa es que sea **público, revisado,
probado y auditable de forma independiente**, que es justamente lo que este documento permite.
El código asistido por IA se somete al mismo estándar que cualquier otra contribución: cada
commit se firma bajo el [DCO](../CONTRIBUTING.md), lo revisa el responsable antes de fusionarlo,
y está cubierto por los tests.

### 3. Red — todos los destinos que contacta

El launcher hace peticiones HTTPS normales para buscar actualizaciones y —solo tras iniciar
sesión con Discord— para el multijugador. **Cada endpoint es un literal de cadena fijo en el
código**: el host nunca se construye a partir de datos descargados ni controlados por un
atacante; solo se interpolan los fragmentos `repo`/`tag`/`lobbyId` en una plantilla de host
fija. La única credencial que sale es tu token de sesión de Discord (un JWT), y se envía **solo**
al backend de lobby propio; todas las peticiones a GitHub son **anónimas**.

| Host | Propósito | Protocolo | ¿Envía identidad? | Código |
| --- | --- | --- | --- | --- |
| `api.github.com` | Autoactualización, catálogo, releases, traducciones | HTTPS | No (anónimo) | `LauncherUpdateService.cs`, `ModCatalogService.cs`, `GitHubReleaseDownloader.cs` |
| `raw.githubusercontent.com` | Iconos, arte, `mod.json`, `.zip` de traducciones, noticias | HTTPS | No | `ModRegistry.cs`, `ModAssetCacheService.cs`, `NewsService.cs` |
| `github.com/.../releases/download/...` | Payloads de mods (p. ej. `WolPayload.zip`) | HTTPS | No | `LauncherConfig.PayloadZipUrls` |
| `wol-lobby.duckdns.org` | Salas de multijugador + chat (**opcional**) | HTTPS + WSS | Sí — JWT de Discord, solo aquí | `MultiplayerConfig.LobbyBaseUrl`, `LobbyApiClient.cs`, `LobbyWebSocket.cs` |
| `wol-notify.duckdns.org` | Feed de avisos de actualización; opt-out con `"none"` | HTTPS | No | `NotificationFeedService.cs` |
| `aoe3wol.com` | `UpdateInfo.xml` heredado de WoL | HTTPS (con respaldo ⚠️ **HTTP**, ver §9) | No | `ModRegistry.cs` |
| `download.radmin-vpn.com`, `radmin-vpn.com` | Instalador / sitio de Radmin (acción tuya) | HTTPS | No | `RadminVpnService.MsiUrl` |
| `aoe3.heavengames.com` | Descargas opcionales de complementos | HTTPS | No | `HeavenDownloader.BaseUrl` |
| `1.1.1.1`, `8.8.8.8` | Ping de latencia (ICMP, no HTTP) | ICMP | No | `MultiplayerTab.xaml.cs` |

**Detalles del inicio de sesión.** Es **OAuth de Discord** (la clase se llama históricamente
`GitHubLoginDialog` pero funciona con Discord). El flujo no envía **ningún dato personal**: hace
un POST con cuerpo vacío y consulta con un identificador emitido por el servidor; el backend
devuelve un JWT y tu nombre de usuario/avatar de Discord, que se guardan localmente. Las demás
peticiones no llevan **ningún identificador de máquina ni huella digital**, solo un User-Agent
estático.

**Los enlaces externos** se abren únicamente a través de la compuerta `SafeUrl`
(`Services/SafeUrl.cs`, método `IsAllowed`): solo http/https, sin `UserInfo` incrustado (bloquea
la suplantación `https://real@malo/`), host no vacío; lo rechazado se registra y nunca se
ejecuta. El enlace profundo `wol-launcher://` solo acepta un `join/<id>` con un id de sala que
cumpla `^[A-Za-z0-9]{1,32}$` (`DeepLinkService.cs`).

### 4. Cambios en el sistema y persistencia

Todo cambio que el launcher hace en tu sistema es **declarado y reversible**, y la mayoría es
**por-usuario** (HKCU / tu perfil). **No hay tareas programadas, ni servicios de Windows, ni
WMI** en ningún lugar del código: una búsqueda de `schtasks`, `sc.exe`, `New-Service`, WMI,
`bcdedit` y `regsvr32` devuelve cero coincidencias en producción.

**Hay dos excepciones a "por-usuario", y conviene decirlas claramente porque una versión anterior
de este documento afirmaba que no existían:**

1. **La entrada de Agregar/Quitar programas** de un mod instalado se escribe en **HKLM** si el
   proceso puede, y cae a HKCU si no.
2. **Un mod que necesita clave de registro propia** (`privateSetupPath` — hoy Napoleonic Era y
   Struggle of Indonesia) hace que el launcher escriba una **clave HKLM persistente** (vista de
   32 bits) apuntando a su carpeta de instalación, clonando los valores de la clave del juego
   base. Esto **requiere elevación**, y por eso instalar ese mod pide UAC una vez. Es lo que
   permite que el mod y el juego original coexistan, y se elimina al desinstalar el mod
   (`SetupPathPatcher.cs`, `UninstallService.RemoveRegistryEntries`).

| Cambio | Dónde | Por defecto | ¿Reversible? | Código |
| --- | --- | --- | --- | --- |
| Autoarranque ("Ejecutar con Windows") | **HKCU** `...\CurrentVersion\Run`, valor `Aoe3ModLauncher` | **Se pregunta en el primer arranque**, antes de escribir nada, y no vuelve a preguntar ni a activarse solo | Sí — casilla en Configuración | `StartupRegistrationService.cs` |
| Esquema de enlace profundo | **HKCU** `Software\Classes\wol-launcher` | Registrado al inicio | Sí — se borra el subárbol | `DeepLinkService.cs` |
| Entrada de desinstalación | **HKLM** si se puede, **si no HKCU** | Solo al instalar un mod (acción tuya) | Sí | `NativeInstallService.cs` |
| Clave de registro privada de un mod | **HKLM** (32 bits) | Solo para mods `privateSetupPath`; pide UAC una vez | Sí — se borra al desinstalar el mod | `SetupPathPatcher.cs` |
| Accesos directos de escritorio / menú Inicio | Perfil de usuario | Al instalar | Sí — se quitan al desinstalar | `NativeInstallService.cs`, `SelfInstallService.cs` |
| Uniones de directorio (redirección de guardado/setup) | `My Games`, `setuppath` del juego | Solo mientras juega un mod con redirección | Sí — se deshacen al siguiente arranque | `AoE3UserDataRedirect.cs`, `AoE3SetupPathRedirect.cs` |

**Honestidad sobre el autoarranque.** El autoarranque **se pregunta**, en el primer arranque, en
un diálogo que nombra la clave de registro que añadiría y dónde deshacerlo
(`BackgroundConsentDialog`). No se escribe nada antes de que respondas — ni la clave, ni un
borrado. Antes venía activado por defecto y se avisaba después con un globo en la bandeja; era
defendible, pero era un aviso, no un consentimiento, y a un globo no se le puede responder.
**"Sí" es la recomendación y nada más:** cerrar la ventana, pulsar Escape o hacer clic en la X
cuentan como no.

Cuando se activa usa solo la clave Run de HKCU —*nunca* una tarea programada ni un servicio—
precisamente para mantener baja la señal de persistencia ante los antivirus. Respondas lo que
respondas, la respuesta se registra contra un marcador, así que **la pregunta se hace exactamente
una vez** y, una vez que has dicho que no o lo has desactivado, **no puede volver a activarse
solo** (`StartupRegistrationService.PlanStartup` / `PlanAnswer`) — es una invariante del código,
fijada por `BackgroundStartupPlanTests`.

**Qué hace realmente un arranque en segundo plano.** Con el autoarranque activado, un arranque al
iniciar sesión abre directo a la bandeja y: busca actualizaciones del launcher y del mod,
refresca el catálogo y el índice de traducciones, abre la conexión de presencia del chat global
(esto es lo que hace que tus amigos te vean conectado: es la función, no un efecto secundario) y
consulta la lista de salas cada 90 segundos para avisarte cuando alguien abre una partida. **No**
descarga el panel de noticias ni revalida imágenes mientras la ventana está oculta; ambas cosas
esperan a que la abras. Toda esa actividad de red está gobernada por los ajustes ya existentes
`checkUpdatesOnStartup` y `notifyNewRooms`.

**Seguridad al borrar archivos.** Las redirecciones por unión **nunca borran una carpeta real**:
mueven la carpeta real a un lado una vez y quitan solo el *enlace*, y se detienen en vez de
sobrescribir si ya existe una copia apartada. La desinstalación **rechaza de plano el juego
base** y, para un mod superpuesto sobre tu AoE3 real, borra **solo los archivos nuevos del propio
mod, nunca un directorio ni tus partidas guardadas**. La autodesinstalación del launcher
explícitamente **nunca toca los mods instalados** (`SelfInstallService.cs`).

**Lanzamiento de procesos.** El juego se lanza **reparentado bajo `explorer.exe`**
(`DetachedProcessLauncher.cs`) para que cerrar el launcher a la fuerza desde el Administrador de
tareas no mate la partida — es robustez, no sigilo. Los únicos usos de `cmd.exe` son `mklink /J`
para las uniones y un script de autoborrado diferido durante la desinstalación del *propio*
launcher, y ambos apuntan solo a carpetas del launcher.

### 5. Privacidad y manejo de datos

Sin analítica, sin redes de anuncios, sin rastreo de terceros. Por defecto, la única actividad de
red son las comprobaciones de actualización/catálogo/traducciones/noticias, que puedes desactivar
por completo en *Configuración → Actualizaciones*. El multijugador es opcional: nada sale de tu
PC hasta que inicias sesión con Discord. El registro de telemetría local opcional
(`multiplayer-events.log`) está **desactivado por defecto**, solo contiene contadores de eventos
(sin contenido de mensajes ni datos personales) y **nunca usa la red**.

El token de sesión del multijugador vive en
`%LocalAppData%\AoE3ModLauncher\launcher-config.json`. El paquete "Compartir diagnósticos"
**excluye a propósito ese archivo** para que tu token no se filtre en un reporte de error —
garantizado en el código (`DiagnosticLog.ExportBundle`) y fijado por un test
(`DiagnosticLogTests`).

Detalle completo: [**PRIVACY.md**](../PRIVACY.md).

### 6. Sin comportamiento de malware — ausencia confirmada

Una revisión de seguridad no solo mira lo que el código *hace*, sino lo que *haría* un malware, y
confirma su **ausencia**. Búsquedas dirigidas en todo el árbol devolvieron **cero coincidencias**
para cada uno de estos:

| Técnica de malware | Se buscó | Resultado |
| --- | --- | --- |
| Anti-depuración / anti-análisis | `IsDebuggerPresent`, `CheckRemoteDebuggerPresent`, `NtQueryInformationProcess` | **Ninguno** |
| Inyección en procesos | `VirtualAllocEx`, `WriteProcessMemory`, `CreateRemoteThread`, `SetWindowsHookEx`, `VirtualProtect` | **Ninguna** |
| Sabotaje de AV / telemetría | `AmsiScanBuffer`, parcheo de ETW, `Set-MpPreference`, `DisableRealtimeMonitoring`, `ExclusionPath` | **Ninguno** |
| Ofuscadores | ConfuserEx, Dotfuscator, Fody, Costura; URLs en base64 | **Ninguna** |
| Código remoto / dinámico | `Assembly.Load`, `Reflection.Emit`, `FromBase64String` alimentando ejecución | **Ninguno** |
| Keylogging / captura de entrada | `GetAsyncKeyState`, `keybd_event`, `WH_KEYBOARD` | **Ninguno** |
| Ocultar procesos / ventanas | ocultarse del administrador de tareas, persistencia oculta | **Ninguno** |

La única reflexión en la aplicación es `Activator.CreateInstance` sobre el objeto COM estándar
`WScript.Shell` para arreglar los iconos de los accesos directos `.lnk`
(`NativeInstallService.cs`) — un patrón de Windows benigno y conocido. La postura de elevación es
conservadora (`asInvoker`, elevar bajo demanda), evitando a propósito la señal de persistencia
más fuerte que supondría un servicio o una tarea programada.

**Nota histórica.** El proyecto *llegó a* incluir una DLL nativa de enganche basada en Detours
(un enfoque antiguo para el multijugador LAN); se **eliminó**, y hoy no hay binarios `native/` ni
`third_party/` en el árbol — los `Compile Remove` del `.csproj` son restos defensivos. El nombre
alarmante `Win32/Injector` que muestran algunos antivirus es el **heurístico de empaquetado** del
§7, **no** código de inyección presente en este repositorio.

### 7. Por qué el antivirus lo marca igual (y sigue limpio)

El launcher se distribuye como **un único `.exe` grande (~165 MB)** autocontenido que incluye
todo .NET para que no tengas que instalar nada. Para un **heurístico de "empaquetador"** sin
firma, un ejecutable con un payload grande y opaco *se parece* estructuralmente a cómo los
*crypters* de malware esconden código — por eso puede marcarse como `Win32/Injector`, `Wacatac`,
`ML.Attribute`, etc. Es un veredicto de **"esto se parece a algo"**, no de **"esto es malware"**.

El mayor disparador era la **compresión de archivo único**, que descomprime los ensamblados a una
caché en `%TEMP%` en el primer arranque (comportamiento autoextraíble). Por eso el proyecto
**desactivó la compresión a propósito** —por eso pesa ~165 MB y no ~120 MB—. Sumado a lo habitual
—**editor sin firma / desconocido**, **reputación baja** y acciones
normales-pero-sospechosas-aisladas (descarga archivos, escribe una clave Run, eleva bajo
demanda)—, un aviso de SmartScreen/Defender en el primer arranque es esperable para *cualquier*
herramienta open-source pequeña.

Explicación completa y cómo pasar el aviso:
[**IS-IT-A-VIRUS.md**](IS-IT-A-VIRUS.md) · [**INSTALL.md**](../WarsOfLibertyLauncher/INSTALL.md).

### 8. Integridad: compilación, firma y actualizaciones verificadas

- **Compilación en CI pública.** Los releases oficiales se construyen en **GitHub Actions, en un
  runner limpio `windows-latest`** con permisos de solo lectura, disparados por una etiqueta de
  versión. **Los tests corren antes** de la compilación final, y la SHA-256 del `.exe` se imprime
  en el registro y en las notas del release (`.github/workflows/release.yml`).
- **Verifica tu descarga.** Cada release publica la SHA-256 del `.exe`; compárala con
  `Get-FileHash Aoe3ModLauncher.exe -Algorithm SHA256`. Si coincide, tu archivo es exactamente el
  que compiló el CI.
- **Autoactualización verificada.** Antes de reemplazarse, el launcher verifica la **SHA-256** y
  el **firmante Authenticode** del binario descargado contra el binario en ejecución, borra la
  descarga ante cualquier fallo, y hace un **intercambio atómico con reversión**
  (`LauncherUpdateService.cs`).
- **Payloads de mods verificados.** Cada parte descargada se coteja con una **SHA-256 fijada en el
  catálogo** (`NativeInstallService.cs`), y los parches `.tar.xz` con un **CRC32** del manifiesto
  (`UpdateService.cs`); la extracción de zips está **limitada a la carpeta de instalación** para
  bloquear *path-traversal* (`ArchiveService.cs`).

**Estado de la firma — la versión honesta.** Los binarios de release están *pensados* para ir
firmados con Authenticode por **SignPath Foundation** (firma gratuita para código abierto). En la
fecha de esta auditoría esa solicitud sigue **pendiente**: el trabajo de firma del CI está
**condicionado y hoy se omite** hasta que el proyecto se apruebe. Hasta entonces, **los releases
de CI van sin firmar** y se verifican por su **SHA-256 publicada**, y las compilaciones locales
usan un certificado **autofirmado `CN=Gorgorito`** que prueba la integridad **solo en la máquina
de compilación** y *no* suprime la advertencia del antivirus.

Detalles: [**CODE_SIGNING_POLICY.md**](../CODE_SIGNING_POLICY.md) · [**BUILDING.md**](BUILDING.md).

### 9. Salvedades honestas y puntos abiertos

Una auditoría creíble enumera lo que *no* es perfecto. Nada de lo siguiente es malicioso; se
declara para que juzgues tú mismo.

1. **La firma de confianza aún no está activa.** SignPath está solicitado, no activo, así que los
   releases de hoy van sin firmar por CI (verifica por SHA-256).
2. **No es una compilación reproducible bit a bit.** Está determinada por el código y el CI
   normaliza rutas (`ContinuousIntegrationBuild=true`), pero ReadyToRun con un runtime incrustado
   no garantiza ser idéntico byte a byte entre entornos; la garantía de integridad es la
   **SHA-256 publicada**, no una atestación de compilación reproducible.
3. **La verificación de firmante en la autoactualización es "mismo firmante", no "cadena de
   confianza"**, y **se omite si el binario en ejecución no está firmado** — así que para los
   binarios sin firmar de hoy se apoya efectivamente en la comprobación de SHA-256.
4. **El CRC32 de los parches `.tar.xz` es un control de corrupción, no criptográfico** — el
   control fuerte en la ruta del payload es la SHA-256 fijada en el catálogo.
5. **Queda un respaldo en HTTP en texto plano** para el `UpdateInfo.xml` heredado de WoL: el
   principal es HTTPS y el alternativo del mismo host es HTTP, usado solo si el primero falla. El
   *payload* del mod se descarga siempre por HTTPS desde GitHub.
6. **Las URLs del lobby y del feed de avisos son editables en el config,** así que un
   `launcher-config.json` modificado a mano podría reapuntar esos dos servicios (ambos siguen
   siendo http/https; el feed cae de vuelta a GitHub).
7. **Los repos del backend de multijugador son separados** (`wol-launcher-lobby-node`,
   `notifier-server`) y no forman parte de este checkout, así que su licencia/visibilidad no puede
   confirmarse desde aquí.
8. **Las dos escrituras en HKLM** (§4) son cambios a nivel de máquina, no por-usuario. Están
   declaradas, piden UAC y se revierten al desinstalar, pero conviene saberlo antes de instalar un
   mod que use `privateSetupPath`.

### 10. Compruébalo tú mismo

**No** tienes que confiar en este documento.

1. **Lee el código.** Todo es público: <https://github.com/Gorgorito12/AoE3-Mod-Launcher>. Abre
   cualquier archivo citado arriba.
2. **Analízalo en VirusTotal** (~70 motores a la vez). Los nombres heurísticos/genéricos
   (`Injector`, `Wacatac`, `ML.Attribute`) significan "se parece", no "lo es".
3. **Verifica la SHA-256** de tu descarga contra las notas del release
   (`Get-FileHash Aoe3ModLauncher.exe -Algorithm SHA256`).
4. **Compílalo tú mismo** desde el código con el SDK de .NET 8 en Windows — obtienes el mismo
   launcher (ver [IS-IT-A-VIRUS.md](IS-IT-A-VIRUS.md)).
5. **Confirma los modelos de IA** con `git log` (ver §2).
6. **Repite las cifras** de §1 y §2 con los comandos del §2; si no coinciden con el texto, el
   texto está viejo.
7. **¿Encontraste algo realmente malicioso?**
   [Abre una incidencia](https://github.com/Gorgorito12/AoE3-Mod-Launcher/issues) o un
   [aviso de seguridad](https://github.com/Gorgorito12/AoE3-Mod-Launcher/security/advisories/new)
   — se revisa en abierto.

---

## English

> **What is this?** A **code-cited** audit of the **AoE3 Mod Launcher**, written so that anyone
> — a player, a moderator, or an antivirus analyst — can confirm the launcher is **not malware**
> and can see **exactly** how it was built, including the AI models used. Every claim below
> points at a real file you can open yourself.

This is the deep, technical companion to the short FAQ
[**IS-IT-A-VIRUS.md**](IS-IT-A-VIRUS.md). If you just want "is it safe and how do I run it",
read that one first. This document is the evidence behind it.

> **About the numbers in this document.** The figures below were measured on the audit date and
> the code keeps moving. They are not here to be believed but to be **repeated**: each comes with
> the command that produces it (§2, §10). If your result disagrees with the text, **the text is
> what is stale** — the repository is the source.

### Verdict at a glance

| Question | Finding | Evidence |
| --- | --- | --- |
| Does it run as Administrator? | **No** — un-elevated (`asInvoker`); asks for UAC only to write protected folders or register a mod's private key | `app.manifest`, `ElevationService.cs` |
| Any keylogging / input capture? | **None** | grep `SetWindowsHookEx` / `GetAsyncKeyState` = 0 |
| Any process injection? | **None** | grep `VirtualAllocEx` / `WriteProcessMemory` / `CreateRemoteThread` = 0 |
| Does it touch / disable antivirus? | **No** — every "Defender" mention is defensive (handling false positives) | `Strings.cs`, `PayloadFileBlockedException.cs` |
| Obfuscation / packing / hidden strings? | **None** — no ConfuserEx/Fody/Costura, no base64-decoded URLs | grep = 0 |
| Dynamic / remote code execution? | **No** — the only reflection is the `WScript.Shell` COM object for `.lnk` shortcuts | `NativeInstallService.cs` |
| Hidden network endpoints (C2)? | **None** — every host is a plaintext literal; only `repo`/`tag`/`id` are interpolated | §3, `GitHubReleaseDownloader.cs` |
| Analytics / tracking SDKs? | **None** — 4 well-known OSS dependencies, zero telemetry vendors | `.csproj` |
| Local telemetry log | **Opt-in, OFF by default**, never leaves your PC | `LauncherConfig.cs`, `MultiplayerTelemetry.cs` |
| Does it delete your files? | Only its own mod clone, with guards; **never** the base game or user saves | `UninstallService.cs`, `AoE3UserDataRedirect.cs` |
| Any machine-wide (HKLM) changes? | **Yes, two, both disclosed** — see §4. These are not per-user | `NativeInstallService.cs`, `SetupPathPatcher.cs` |
| Open source & license | **Yes** — Apache-2.0, fully public | `LICENSE` |
| Built with AI? Which model? | **Anthropic's Claude**, across several versions — verifiable in git history | §2 |

---

### 1. What the launcher is — and what it deliberately is *not*

The AoE3 Mod Launcher is a native Windows desktop app (WPF, .NET 8) that installs, updates,
verifies and launches *Age of Empires III* total-conversion mods, with an optional multiplayer
tab. As of this audit it is **~108,000 lines of C#** across **194 source files** (131 of them
under `Services/`), pinned by **146 test files** — a real, maintained engineering project, not a
thin wrapper around something hidden.

It has **exactly four** third-party dependencies, all mainstream open-source libraries:
`SharpCompress` (archive extraction), `System.IO.Hashing` (CRC32), `Hardcodet.NotifyIcon.Wpf`
(the tray icon), and `XamlAnimatedGif` (the screenshot gallery). **None** of them is a telemetry,
analytics, or advertising SDK.

**What it does NOT do** (confirmed by absence in the code — §6): no keylogging, no
screen/webcam/mic capture, no process injection, no antivirus tampering, no hidden
autostart-then-hide, no cryptomining, no credential theft, no bundled third-party binaries. It
does **not** download or redistribute *Age of Empires III* itself — you must own a legal copy,
which the launcher clones locally (`DISCLAIMER.md`).

### 2. Built with AI — which Claude models

This launcher is developed by its maintainer, **Gorgorito12** (`jeisonso1997@gmail.com`), with AI
pair-programming assistance from **Anthropic's Claude** (via *Claude Code*). **Several model
versions** have been used over the project's life — Opus 4.6, 4.7, 4.8 and Opus 5, including
their 1-million-token-context variants — which is simply what happens across months of work as
the available model changes. **No other AI model or provider** appears anywhere in the project's
history — no GPT, Copilot, Gemini, or Llama.

You do not have to take our word for this — it is recorded in the public commit history. These
commands **do not prescribe an answer**; they tell you where to look:

```bash
# List every Claude co-authorship trailer with its count.
# You will see several model versions, not one.
git log --format=%b | grep -io "Co-Authored-By: Claude[^<]*" | sort | uniq -c

# Confirm no other AI model/provider is referenced anywhere in history (no results):
git log --all --format="%B" | grep -iE "gpt|copilot|gemini|llama|codex"

# Commit authorship split:
git log --format='%an' | sort | uniq -c | sort -rn
```

As of this audit the repository has **373 commits**: 364 authored by the human maintainer and 9
by Claude. AI assistance is attributed via standard `Co-Authored-By` commit trailers and the
`claude/*` working-branch naming that *Claude Code* uses.

**Why this is a transparency point, not a security concern.** Who or what *wrote* the code does
not change whether it is safe — what matters is that the code is **public, reviewed, tested, and
independently auditable**, which is exactly what this document enables. AI-assisted code is held
to the same standard as any other contribution: every commit is signed off under the project's
[DCO](../CONTRIBUTING.md), reviewed by the maintainer before merge, and covered by the test suite.

### 3. Network — every destination it contacts

The launcher makes ordinary HTTPS requests to check for updates and, only after you opt in by
signing in with Discord, to run multiplayer. **Every endpoint is a hardcoded string literal** —
the host is never built from downloaded or attacker-controlled data; only the
`repo`/`tag`/`lobbyId` fragments are interpolated into a fixed host template. The only credential
ever sent outbound is your Discord session token (a JWT), and it is sent **only** to the
self-hosted lobby backend; all GitHub requests are **anonymous**.

| Host | Purpose | Protocol | Sends identity? | Code |
| --- | --- | --- | --- | --- |
| `api.github.com` | Launcher self-update, mod catalog, releases, translations | HTTPS | No (anonymous) | `LauncherUpdateService.cs`, `ModCatalogService.cs`, `GitHubReleaseDownloader.cs` |
| `raw.githubusercontent.com` | Icons, hero art, `mod.json`, translation `.zip`, news feed | HTTPS | No | `ModRegistry.cs`, `ModAssetCacheService.cs`, `NewsService.cs` |
| `github.com/.../releases/download/...` | Mod payloads (e.g. `WolPayload.zip`) | HTTPS | No | `LauncherConfig.PayloadZipUrls` |
| `wol-lobby.duckdns.org` | Multiplayer lobbies + chat (**opt-in**) | HTTPS + WSS | Yes — Discord JWT, only here | `MultiplayerConfig.LobbyBaseUrl`, `LobbyApiClient.cs`, `LobbyWebSocket.cs` |
| `wol-notify.duckdns.org` | "Update available" notification feed; opt-out with `"none"` | HTTPS | No | `NotificationFeedService.cs` |
| `aoe3wol.com` | Legacy WoL `UpdateInfo.xml` | HTTPS (with an ⚠️ **HTTP** fallback, see §9) | No | `ModRegistry.cs` |
| `download.radmin-vpn.com`, `radmin-vpn.com` | Radmin VPN installer / site (user action) | HTTPS | No | `RadminVpnService.MsiUrl` |
| `aoe3.heavengames.com` | Optional add-on downloads | HTTPS | No | `HeavenDownloader.BaseUrl` |
| `1.1.1.1`, `8.8.8.8` | Latency ping (ICMP, not HTTP) | ICMP | No | `MultiplayerTab.xaml.cs` |

**Sign-in details.** Sign-in is **Discord OAuth** (the class is historically named
`GitHubLoginDialog` but is Discord-backed). The flow sends **no personal data**: it POSTs an empty
body and polls with a server-issued handle; the backend returns a JWT + your Discord
username/avatar, cached locally. All other requests carry **no machine IDs and no fingerprints**
— only a static User-Agent.

**External links** are opened only through the `SafeUrl` gate (`Services/SafeUrl.cs`, method
`IsAllowed`): http/https only, no embedded `UserInfo` (blocks `https://real@evil/` spoofing),
non-empty host; rejects are logged, never executed. The custom `wol-launcher://` deep link only
ever honours a `join/<id>` with a strict `^[A-Za-z0-9]{1,32}$` lobby id (`DeepLinkService.cs`).

### 4. System changes & persistence

Every change the launcher makes to your system is **disclosed** and **reversible**, and most of it
is **per-user** (HKCU / your profile). There are **no scheduled tasks, no Windows services, and no
WMI** anywhere in the code — a codebase-wide search for `schtasks`, `sc.exe`, `New-Service`, WMI,
`bcdedit`, `regsvr32` returns zero production hits.

**There are two exceptions to "per-user", and they are worth stating plainly because an earlier
version of this document claimed there were none:**

1. **A mod's Add/Remove Programs entry** is written to **HKLM** when the process can, falling back
   to HKCU when it cannot.
2. **A mod that needs a registry key of its own** (`privateSetupPath` — today Napoleonic Era and
   Struggle of Indonesia) makes the launcher write a **persistent HKLM key** (32-bit view) pointing
   at its install folder, cloning the values of the base game's key. This **requires elevation**,
   which is why installing such a mod asks for UAC once. It is what lets the mod and the original
   game coexist, and it is removed when the mod is uninstalled (`SetupPathPatcher.cs`,
   `UninstallService.RemoveRegistryEntries`).

| Change | Where | Default | Reversible? | Code |
| --- | --- | --- | --- | --- |
| Auto-start ("Run with Windows") | **HKCU** `...\CurrentVersion\Run`, value `Aoe3ModLauncher` | **Asked on the first launch, before anything is written** — and cannot re-ask or re-arm after you decline | Yes — Settings checkbox | `StartupRegistrationService.cs` |
| Deep-link scheme | **HKCU** `Software\Classes\wol-launcher` | Registered on start | Yes — subtree deleted | `DeepLinkService.cs` |
| Uninstall entry (Add/Remove Programs) | **HKLM** if possible, **else HKCU** | Only during a mod install (your action) | Yes | `NativeInstallService.cs` |
| A mod's private registry key | **HKLM** (32-bit view) | Only for `privateSetupPath` mods; asks for UAC once | Yes — removed when the mod is uninstalled | `SetupPathPatcher.cs` |
| Desktop / Start-Menu shortcuts | User profile | On install | Yes — removed on uninstall | `NativeInstallService.cs`, `SelfInstallService.cs` |
| Directory junctions (save/setup redirect) | `My Games`, game `setuppath` | Only while a redirect-mod plays | Yes — auto-undone next launch | `AoE3UserDataRedirect.cs`, `AoE3SetupPathRedirect.cs` |

**Auto-start honesty.** Auto-start is **asked for**, on the first launch, in a dialog that names
the registry key it would add and where to undo it (`BackgroundConsentDialog`). Nothing is written
before you answer — not the key, and not a deletion either. It used to be ON by default and
announced afterwards by a one-time tray balloon; that was defensible but it was a notice, not
consent, and a balloon cannot be answered. **Yes is the recommendation and nothing more:** closing
the window, pressing Escape or clicking the X all count as no.

When enabled it uses only the per-user HKCU Run key — *never* a Scheduled Task or Service,
precisely to keep the antivirus-persistence signal low. Whichever way you answer, the answer is
recorded against a seed marker, so **the question is asked exactly once** and, once you have said
no or turned it off, it **can never silently turn itself back on**
(`StartupRegistrationService.PlanStartup` / `PlanAnswer`) — a code invariant, pinned by
`BackgroundStartupPlanTests`.

**What a background launch actually does.** With auto-start on, a logon launch opens straight to
the tray and: checks for a launcher update and a mod update, refreshes the mod catalogue and the
translation index, opens the global-chat presence connection (this is what makes friends see you
as connected — it is the feature, not a side effect), and polls the room list every 90 seconds so
it can notify you when somebody opens a game. It does **not** fetch the news panel or revalidate
card images while the window is hidden; both wait until you actually open it. All of the network
activity above is governed by the existing `checkUpdatesOnStartup` and `notifyNewRooms` settings.

**File-deletion safety.** The junction redirects **never delete a real folder** — they move the
real folder aside once and remove only the *link*, and bail rather than clobber if an "aside"
already exists. Uninstall **refuses the stock base game outright** and, for a mod overlaid onto
your real AoE3, deletes **only the mod's own net-new files, never a directory or your saves**. The
launcher's own self-uninstall explicitly **never touches installed mods**
(`SelfInstallService.cs`).

**Process launching.** The game is launched **re-parented under `explorer.exe`**
(`DetachedProcessLauncher.cs`) so that force-closing the launcher in Task Manager doesn't kill the
game — a robustness feature, not stealth. The only `cmd.exe` uses are `mklink /J` for junctions
and a deferred self-delete script during the launcher's *own* uninstall, both targeting only the
launcher's own folders.

### 5. Privacy & data handling

No analytics, no ad networks, no third-party tracking. By default the only network activity is
update/catalog/translation/news checks, which you can fully disable in *Settings → Updates*.
Multiplayer is opt-in — nothing leaves your PC until you sign in with Discord. The optional local
telemetry log (`multiplayer-events.log`) is **OFF by default**, contains only event counters (no
message contents, no personal data), and **never uses the network**.

The multiplayer session token lives in
`%LocalAppData%\AoE3ModLauncher\launcher-config.json`. The "Share diagnostics" bundle
**deliberately excludes that config file** so your token can't leak in a bug report — enforced in
code (`DiagnosticLog.ExportBundle`) and pinned by a unit test (`DiagnosticLogTests`).

Full detail: [**PRIVACY.md**](../PRIVACY.md).

### 6. No malware behaviour — absence confirmed

A security review looks not only at what the code *does* but at what a piece of malware *would* do
and confirms it is **absent**. Targeted searches across the whole source tree returned **zero
matches** for every one of these:

| Malware technique | Searched for | Result |
| --- | --- | --- |
| Anti-debug / anti-analysis | `IsDebuggerPresent`, `CheckRemoteDebuggerPresent`, `NtQueryInformationProcess` | **None** |
| Process injection | `VirtualAllocEx`, `WriteProcessMemory`, `CreateRemoteThread`, `SetWindowsHookEx`, `VirtualProtect` | **None** |
| AV / telemetry tampering | `AmsiScanBuffer`, ETW patching, `Set-MpPreference`, `DisableRealtimeMonitoring`, `ExclusionPath` | **None** |
| Obfuscation tooling | ConfuserEx, Dotfuscator, Fody, Costura; base64-decoded URLs | **None** |
| Remote / dynamic code | `Assembly.Load`, `Reflection.Emit`, `FromBase64String` feeding execution | **None** |
| Keylogging / input capture | `GetAsyncKeyState`, `keybd_event`, `WH_KEYBOARD` | **None** |
| Process / window hiding | task-manager hiding, hidden persistence | **None** |

The only reflection in the app is `Activator.CreateInstance` on the standard `WScript.Shell` COM
object to fix `.lnk` shortcut icons (`NativeInstallService.cs`) — a benign, well-known Windows
pattern. Elevation posture is conservative (`asInvoker`, elevate-on-demand), deliberately avoiding
the stronger persistence signal of a service or scheduled task.

**Historical note.** The project *once* bundled a native Detours-based hook DLL / injector (an old
approach to LAN multiplayer); it was **removed**, and there are no `native/` or `third_party/`
binaries in the tree today — the `.csproj` `Compile Remove` globs are defensive leftovers only.
The scary-sounding `Win32/Injector` name some antivirus engines show is the **packer heuristic**
from §7, **not** injection code that exists in this repository.

### 7. Why antivirus flags it anyway (and it's still clean)

The launcher ships as **one large (~165 MB) self-contained `.exe`** that bundles the whole .NET
runtime, so users don't have to install anything. To a signature-less **"packer" heuristic**, a
single executable that carries a big opaque payload *looks* structurally like the way malware
crypters hide code — so it can be flagged as `Win32/Injector`, `Wacatac`, `ML.Attribute`, etc.
That is a **"this resembles something"** verdict, not **"this is malware."**

The single biggest trigger was **single-file compression**, which unpacks assemblies to a `%TEMP%`
cache at first launch (self-extracting behaviour). So the project **turned compression OFF on
purpose** — that's why the binary is ~165 MB instead of ~120 MB. This trades size for a much
smaller AV footprint. Add the usual factors — an **unsigned / unknown publisher**, **low
reputation** (a new binary with few downloads), and normal-but-suspicious-in-isolation actions (it
downloads files, writes a Run key, elevates on demand) — and a first-run SmartScreen/Defender
warning is expected for *any* small open-source tool.

Full user-facing explanation + how to get past the warning:
[**IS-IT-A-VIRUS.md**](IS-IT-A-VIRUS.md) · [**INSTALL.md**](../WarsOfLibertyLauncher/INSTALL.md).

### 8. Integrity: builds, signing & verified updates

- **Public CI build.** Official releases are built in **GitHub Actions on a clean
  `windows-latest` runner** with read-only permissions, triggered by a version tag. **Unit tests
  run before** the shippable build, and the `.exe`'s SHA-256 is printed to the run log and the
  release notes (`.github/workflows/release.yml`).
- **Verify your download.** Every release publishes the `.exe`'s SHA-256; compare it with
  `Get-FileHash Aoe3ModLauncher.exe -Algorithm SHA256`. Match = your file is byte-for-byte what CI
  built.
- **Verified self-update.** Before swapping itself, the launcher verifies the downloaded binary's
  **SHA-256** and its **Authenticode signer** against the running binary, deleting the download on
  any failure, then does an **atomic swap with rollback** (`LauncherUpdateService.cs`).
- **Verified mod payloads.** Each downloaded payload part is checked against a **catalog-pinned
  SHA-256** (`NativeInstallService.cs`), and `.tar.xz` patches against a **CRC32** from the update
  manifest (`UpdateService.cs`); zip extraction is **clamped to the install root** to block
  path-traversal (`ArchiveService.cs`).

**Signing status — the honest version.** Release binaries are *intended* to be
Authenticode-signed by **SignPath Foundation** (free code signing for open source). As of this
audit that application is **pending / not yet live**: the CI signing job is **gated and currently
skipped** until the SignPath project is approved. Until then, **CI releases are unsigned** and
verified by their **published SHA-256**, and local/developer builds use a **self-signed
`CN=Gorgorito`** certificate that proves integrity **only on the build machine** and does *not*
suppress the antivirus warning.

Details: [**CODE_SIGNING_POLICY.md**](../CODE_SIGNING_POLICY.md) · [**BUILDING.md**](BUILDING.md).

### 9. Honest caveats & known gaps

A credible audit lists what is *not* perfect. None of the below is malicious; they are disclosed
so you can judge for yourself.

1. **Trusted signature is not live yet.** SignPath is applied-for, not active, so today's releases
   are CI-unsigned (verify by SHA-256).
2. **Not a bit-for-bit reproducible build.** The build is source-determined and CI normalises
   paths (`ContinuousIntegrationBuild=true`), but ReadyToRun + an embedded runtime is not
   guaranteed byte-identical across environments; the integrity guarantee is the **published
   SHA-256**, not a reproducible-build attestation.
3. **Self-update signer check is "same-signer," not "trusted-chain,"** and is **skipped if the
   running binary is unsigned** — so for today's CI-unsigned builds it effectively relies on the
   SHA-256 check.
4. **CRC32 on `.tar.xz` patches is a corruption check, not cryptographic** — the strong control on
   the payload path is the catalog-pinned SHA-256.
5. **A plaintext HTTP fallback remains** for WoL's legacy `UpdateInfo.xml`: the primary is HTTPS
   and the same host's alternate is HTTP, used only if the first fails. The mod *payload* itself
   always downloads over HTTPS from GitHub.
6. **Lobby and notification-feed URLs are user-editable in the config,** so a hand-modified
   `launcher-config.json` could repoint those two services (both remain http/https; the feed falls
   back to GitHub).
7. **Multiplayer backend repos are separate** (`wol-launcher-lobby-node`, `notifier-server`) and
   are not part of this checkout, so their license/visibility can't be confirmed from here.
8. **The two HKLM writes** (§4) are machine-wide rather than per-user. They are disclosed, prompt
   for UAC and are reverted on uninstall, but they are worth knowing about before installing a mod
   that uses `privateSetupPath`.

### 10. Verify it yourself

You do **not** have to trust this document.

1. **Read the code.** Everything is public: <https://github.com/Gorgorito12/AoE3-Mod-Launcher>.
   Open any file cited above.
2. **Scan on VirusTotal** (~70 engines at once). Heuristic/generic names (`Injector`, `Wacatac`,
   `ML.Attribute`) mean "looks like", not "is".
3. **Verify the SHA-256** of your download against the release notes
   (`Get-FileHash Aoe3ModLauncher.exe -Algorithm SHA256`).
4. **Build it yourself** from source with the .NET 8 SDK on Windows — you get the same launcher
   (see [IS-IT-A-VIRUS.md](IS-IT-A-VIRUS.md)).
5. **Confirm the AI models** with `git log` (see §2).
6. **Re-measure** the §1 and §2 figures with the commands in §2; if they disagree with the text,
   the text is stale.
7. **Found something genuinely malicious?**
   [Open an issue](https://github.com/Gorgorito12/AoE3-Mod-Launcher/issues) or a
   [security advisory](https://github.com/Gorgorito12/AoE3-Mod-Launcher/security/advisories/new)
   — it's reviewed in the open.

---

*Ver también · See also:* [IS-IT-A-VIRUS.md](IS-IT-A-VIRUS.md) ·
[SECURITY.md](../SECURITY.md) · [PRIVACY.md](../PRIVACY.md) ·
[CODE_SIGNING_POLICY.md](../CODE_SIGNING_POLICY.md) ·
[BUILDING.md](BUILDING.md) · [DISCLAIMER.md](../DISCLAIMER.md)

*Esta auditoría refleja el estado del repositorio en su fecha. El código cambia; vuelve a correr
las búsquedas del §6 y las comprobaciones del §10 sobre el código actual. · This audit reflects
the repository state on its audit date. Code moves; re-run the searches in §6 and the checks in
§10 against the current source to confirm.*
