# Transparency & Security Audit · Auditoría de transparencia y seguridad

> **What is this?** An independent, code-cited audit of the **AoE3 Mod Launcher**,
> written so that anyone — a player, a moderator, or an antivirus analyst — can
> confirm the launcher is **not malware** and can see **exactly** how it was built,
> including the AI models used. Every claim below points at a real file and line
> you can open yourself.
>
> **¿Qué es esto?** Una auditoría independiente y **con referencias al código** del
> **AoE3 Mod Launcher**, escrita para que cualquiera —un jugador, un moderador o un
> analista de antivirus— pueda confirmar que el launcher **no es malware** y ver
> **exactamente** cómo se construyó, incluidos los modelos de IA usados. Cada
> afirmación apunta a un archivo y una línea reales que puedes abrir tú mismo.

- **Audit date · Fecha de la auditoría:** 2026-07-24
- **Scope · Alcance:** the launcher source in this repository (`WarsOfLibertyLauncher/`).
- **Method · Método:** static source review — reading the code, not trusting the docs.
- **Verdict · Veredicto:** **No malicious behaviour found.** The antivirus warnings
  are a **false positive** with a known cause (see §7). · **No se encontró
  comportamiento malicioso.** Las advertencias de antivirus son un **falso positivo**
  con causa conocida (ver §7).

This is the deep, technical companion to the short FAQ
[**IS-IT-A-VIRUS.md**](IS-IT-A-VIRUS.md). If you just want "is it safe and how do I
run it", read that one first. This document is the evidence behind it. · Este es el
complemento técnico y detallado de la FAQ breve
[**IS-IT-A-VIRUS.md**](IS-IT-A-VIRUS.md). Si solo quieres "¿es seguro y cómo lo
ejecuto?", lee esa primero. Este documento es la evidencia que la respalda.

---

## Verdict at a glance · Veredicto de un vistazo

| Question · Pregunta | Finding · Hallazgo | Evidence · Evidencia |
| --- | --- | --- |
| Does it run as Administrator? · ¿Se ejecuta como administrador? | **No** — un-elevated (`asInvoker`); asks for UAC only to write protected folders · sin elevación; pide UAC solo para escribir en carpetas protegidas | `app.manifest:37`, `ElevationService.cs:98` |
| Any keylogging / input capture? · ¿Keylogging / captura de entrada? | **None** · Ninguno | grep `SetWindowsHookEx`/`GetAsyncKeyState` = 0 |
| Any process injection? · ¿Inyección en procesos? | **None** · Ninguna | grep `VirtualAllocEx`/`WriteProcessMemory`/`CreateRemoteThread` = 0 |
| Does it touch / disable antivirus? · ¿Toca o desactiva el antivirus? | **No** — every "Defender" mention is defensive (handling false positives) · toda mención a "Defender" es defensiva | `Strings.cs:4772`, `PayloadFileBlockedException.cs` |
| Obfuscation / packing / hidden strings? · ¿Ofuscación / empaquetado / cadenas ocultas? | **None** — no ConfuserEx/Fody/Costura, no base64-decoded URLs · Ninguna | grep = 0 |
| Dynamic / remote code execution? · ¿Ejecución de código dinámico o remoto? | **No** — the only reflection is the `WScript.Shell` COM object for `.lnk` shortcuts · la única reflexión es el objeto COM `WScript.Shell` para accesos directos | `NativeInstallService.cs:1769-1772` |
| Hidden network endpoints (C2)? · ¿Endpoints de red ocultos (C2)? | **None** — every host is a plaintext literal; only `repo`/`tag`/`id` are interpolated · cada host es un literal en texto plano | §3, `GitHubReleaseDownloader.cs:87` |
| Analytics / tracking SDKs? · ¿SDKs de analítica / rastreo? | **None** — 4 well-known OSS dependencies, zero telemetry vendors · Ninguno | `.csproj` (SharpCompress, System.IO.Hashing, Hardcodet.NotifyIcon.Wpf, XamlAnimatedGif) |
| Local telemetry log · Registro de telemetría local | **Opt-in, OFF by default**, never leaves your PC · opcional, **desactivado por defecto**, nunca sale de tu PC | `LauncherConfig.cs:1022`, `MultiplayerTelemetry.cs` |
| Does it delete your files? · ¿Borra tus archivos? | Only its own mod clone, with guards; **never** the base game or user saves · solo su propio clon del mod, con salvaguardas; **nunca** el juego base ni tus partidas | `UninstallService.cs:73-129`, `AoE3UserDataRedirect.cs:81-103` |
| Open source & license · Código abierto y licencia | **Yes** — Apache-2.0, fully public · Sí | `LICENSE`, [repo](https://github.com/Gorgorito12/AoE3-Mod-Launcher) |
| Built with AI? Which model? · ¿Hecho con IA? ¿Qué modelo? | **Claude Opus 4.8** (Anthropic), verifiable in git history · verificable en el historial de git | §2, `git log` trailers |

---

## 1. What the launcher is — and what it deliberately is *not* · Qué es el launcher (y qué NO es a propósito)

**EN.** The AoE3 Mod Launcher is a native Windows desktop app (WPF, .NET 8) that
installs, updates, verifies and launches *Age of Empires III* total-conversion mods,
with an optional multiplayer tab. It is **~60,700 lines of C#** across **117 source
files** and **70 service classes**, pinned by **58 test files** — a real,
maintained engineering project, not a thin wrapper around something hidden. It has
**exactly four** third-party dependencies, all mainstream open-source libraries:
`SharpCompress` (archive extraction), `System.IO.Hashing` (CRC32),
`Hardcodet.NotifyIcon.Wpf` (the tray icon), and `XamlAnimatedGif` (the screenshot
gallery). **None** of them is a telemetry, analytics, or advertising SDK.

**ES.** El AoE3 Mod Launcher es una aplicación de escritorio nativa de Windows (WPF,
.NET 8) que instala, actualiza, verifica y ejecuta mods de conversión total de
*Age of Empires III*, con una pestaña opcional de multijugador. Son **~60.700 líneas
de C#** en **117 archivos** y **70 clases de servicio**, respaldadas por **58
archivos de tests** — un proyecto de ingeniería real y mantenido, no una carcasa
fina que esconde otra cosa. Tiene **exactamente cuatro** dependencias externas,
todas librerías de código abierto conocidas (arriba). **Ninguna** es un SDK de
telemetría, analítica ni publicidad.

**What it does NOT do · Lo que NO hace** (confirmed by absence in the code — §6):
no keylogging, no screen/webcam/mic capture, no process injection, no antivirus
tampering, no hidden autostart-then-hide, no cryptomining, no credential theft, no
bundled third-party binaries. It does **not** download or redistribute *Age of
Empires III* itself — you must own a legal copy, which the launcher clones locally
(`DISCLAIMER.md`).

---

## 2. Built with AI — which Claude models · Hecho con IA — qué modelos de Claude

**EN.** This launcher was developed by its maintainer, **Gorgorito12**
(`jeisonso1997@gmail.com`), with AI pair-programming assistance from **Anthropic's
Claude** (via *Claude Code*). The specific model used is **Claude Opus 4.8**
(including its 1-million-token-context variant). **No other AI model or provider**
appears anywhere in the project's history — no GPT, Copilot, Gemini, Llama, or
others.

You do not have to take our word for this — it is recorded in the public commit
history and you can verify it yourself:

```bash
# Every Claude co-authorship trailer in the repo (all say "Claude Opus 4.8"):
git log --format="%(trailers:key=Co-Authored-By)" | grep -i claude | sort | uniq -c

# Confirm no other AI model/provider is referenced anywhere in history:
git log --all --format="%B" | grep -iE "gpt|copilot|gemini|llama|codex"   # → no results
```

At the time of this audit the human maintainer accounts for **62 of 63** commits;
the AI assistance is attributed via standard `Co-Authored-By: Claude Opus 4.8`
commit trailers and the `claude/*` working-branch naming that *Claude Code* uses.

**Why this is a transparency point, not a security concern.** Who or what *wrote*
the code does not change whether it is safe — what matters is that the code is
**public, reviewed, tested, and independently auditable**, which is exactly what
this document demonstrates. AI-assisted code is held to the same standard as any
other contribution: every commit is signed off under the project's
[DCO](../CONTRIBUTING.md), reviewed by the maintainer before merge, and covered by
the test suite. This audit itself was produced the same way and is verifiable line
by line.

**ES.** Este launcher fue desarrollado por su responsable, **Gorgorito12**
(`jeisonso1997@gmail.com`), con asistencia de programación en pareja de la IA
**Claude, de Anthropic** (mediante *Claude Code*). El modelo usado es **Claude Opus
4.8** (incluida su variante de contexto de 1 millón de tokens). **Ningún otro modelo
o proveedor de IA** aparece en la historia del proyecto — ni GPT, ni Copilot, ni
Gemini, ni Llama, ni otros.

No hace falta que nos creas: está registrado en el historial público de commits y
puedes comprobarlo tú mismo con los comandos de arriba (`git log`). En el momento de
esta auditoría, el responsable humano firma **62 de 63** commits; la asistencia de la
IA se atribuye con los *trailers* estándar `Co-Authored-By: Claude Opus 4.8` y con
los nombres de rama `claude/*` que usa *Claude Code*.

**Por qué esto es un punto de transparencia, no un problema de seguridad.** Quién (o
qué) *escribió* el código no cambia si es seguro — lo que importa es que el código sea
**público, revisado, probado y auditable de forma independiente**, que es justamente lo
que demuestra este documento. El código asistido por IA se somete al mismo estándar
que cualquier otra contribución: cada commit se firma bajo el
[DCO](../CONTRIBUTING.md) del proyecto, lo revisa el responsable antes de fusionarlo,
y está cubierto por los tests. Esta misma auditoría se produjo así y es verificable
línea por línea.

---

## 3. Network — every destination it contacts · Red — todos los destinos que contacta

**EN.** The launcher makes ordinary HTTPS requests to check for updates and, only
after you opt in by signing in with Discord, to run multiplayer. **Every endpoint is
a hardcoded string literal** — the host is never built from downloaded or
attacker-controlled data; only the `repo`/`tag`/`lobbyId` fragments are interpolated
into a fixed host template. The only credential ever sent outbound is your Discord
session token (a JWT), and it is sent **only** to the self-hosted lobby backend; all
GitHub requests are **anonymous** (User-Agent only, no token).

**ES.** El launcher hace peticiones HTTPS normales para buscar actualizaciones y —solo
tras iniciar sesión con Discord— para el multijugador. **Cada endpoint es un literal
de cadena fijo en el código**: el host nunca se construye a partir de datos
descargados ni controlados por un atacante; solo se interpolan los fragmentos
`repo`/`tag`/`lobbyId` en una plantilla de host fija. La única credencial que sale es
tu token de sesión de Discord (un JWT), y se envía **solo** al backend de lobby
propio; todas las peticiones a GitHub son **anónimas**.

| Host | Purpose · Propósito | Protocol · Protocolo | Sends identity? · ¿Envía identidad? | Code · Código |
| --- | --- | --- | --- | --- |
| `api.github.com` | Launcher self-update, mod catalog, releases, translations · autoactualización, catálogo, releases, traducciones | HTTPS | No (anonymous) | `LauncherUpdateService.cs:36`, `ModCatalogService.cs:89`, `GitHubReleaseDownloader.cs:87` |
| `raw.githubusercontent.com` | Icons, hero art, `mod.json`, translation `.zip`, news feed · iconos, arte, `mod.json`, traducciones, noticias | HTTPS | No | `ModRegistry.cs:655`, `ModAssetCacheService.cs:559`, `NewsService.cs:53` |
| `github.com/.../releases/download/...` | Mod payloads (e.g. `WolPayload.zip`) · descargas de mods | HTTPS | No | `LauncherConfig.cs:1160-1162` |
| `wol-lobby.duckdns.org` | Multiplayer lobbies + chat (**opt-in**) · multijugador (**opcional**) | HTTPS + WSS | Yes — Discord JWT, only here · sí, solo aquí | `LobbyApiClient.cs:64`, `LobbyWebSocket.cs:216` |
| `wol-notify.duckdns.org` | "Update available" notification feed; opt-out with `"none"` · feed de avisos; opt-out con `"none"` | HTTPS | No | `MainWindow.xaml.cs:11195`, `NotificationFeedService.cs:61` |
| `aoe3wol.com`, `master.dl.sourceforge.net` | Legacy WoL `UpdateInfo.xml` · `UpdateInfo.xml` heredado de WoL | ⚠️ **HTTP** (see §8) | No | `ModRegistry.cs:691-692`, `LauncherConfig.cs:761-766` |
| `download.radmin-vpn.com`, `radmin-vpn.com` | Radmin VPN installer / site (user action) · instalador / sitio de Radmin (acción del usuario) | HTTPS | No | `RadminVpnService.cs:82,479` |
| `aoe3.heavengames.com` | Optional add-on downloads · descargas opcionales de add-ons | HTTPS | No | `HeavenDownloader.cs:34` |
| `1.1.1.1`, `8.8.8.8` | Latency ping (ICMP, not HTTP) · ping de latencia (ICMP) | ICMP | No | `MultiplayerTab.xaml.cs:6528` |

**Sign-in details · Detalles de inicio de sesión.** Sign-in is **Discord OAuth**
(the class is historically named `GitHubLoginDialog` but is Discord-backed —
`GitHubLoginDialog.xaml.cs:16`). The flow sends **no personal data**: it POSTs an
empty body and polls with a server-issued handle (`LobbyApiClient.cs:90,118`); the
backend returns a JWT + your Discord username/avatar, cached locally. All other
requests carry **no machine IDs and no fingerprints** — only a static User-Agent.

**External links** are opened only through the `SafeUrl` gate (`Services/SafeUrl.cs`):
http/https only, no embedded `UserInfo` (blocks `https://real@evil/` spoofing),
non-empty host; rejects are logged, never executed (`SafeUrl.cs:31-77`). The custom
`wol-launcher://` deep link only ever honours a `join/<id>` with a strict
`^[A-Za-z0-9]{1,32}$` lobby id (`DeepLinkService.cs:31`).

---

## 4. System changes & persistence · Cambios en el sistema y persistencia

**EN.** Every change the launcher makes to your system is **per-user** (HKCU / your
profile — no machine-wide changes), **disclosed**, and **reversible**. There are
**no scheduled tasks, no Windows services, and no WMI** anywhere in the code — a
codebase-wide search for `schtasks`, `sc.exe`, `New-Service`, WMI, `bcdedit`,
`regsvr32` returns zero production hits. Persistence is limited to two removable
HKCU entries.

**ES.** Todo cambio que el launcher hace en tu sistema es **por-usuario** (HKCU / tu
perfil — nada a nivel de máquina), **declarado** y **reversible**. **No hay tareas
programadas, ni servicios de Windows, ni WMI** en ningún lugar del código. La
persistencia se limita a dos entradas HKCU que puedes quitar.

| Change · Cambio | Where · Dónde | Default · Por defecto | Reversible? | Code · Código |
| --- | --- | --- | --- | --- |
| Auto-start ("Run with Windows") | **HKCU** `...\CurrentVersion\Run`, value `Aoe3ModLauncher` | **ON** — but announced by a one-time tray notice, and cannot silently re-arm after opt-out · **activado** — pero avisado con notificación única, no puede reactivarse solo | Yes — Settings checkbox · sí | `StartupRegistrationService.cs:41,180` |
| Deep-link scheme | **HKCU** `Software\Classes\wol-launcher` | Registered on start · registrado al inicio | Yes — subtree deleted · sí | `DeepLinkService.cs:27,86` |
| Uninstall entry (Add/Remove Programs) | HKLM if possible, **else HKCU** | Only during a mod install (your action) · solo al instalar un mod | Yes | `NativeInstallService.cs:1963-2001` |
| Desktop / Start-Menu shortcuts | User profile · perfil de usuario | On install · al instalar | Yes — removed on uninstall · sí | `NativeInstallService.cs:1644`, `SelfInstallService.cs:293` |
| Directory junctions (save/setup redirect) | `My Games`, game `setuppath` | Only while a redirect-mod plays · solo mientras juega un mod con redirección | Yes — auto-undone next launch · sí | `AoE3UserDataRedirect.cs`, `AoE3SetupPathRedirect.cs` |

**Auto-start honesty · Honestidad sobre el autoarranque.** Auto-start is **ON by
default**. This is a deliberate choice (like Steam/Discord/OneDrive) and is
implemented **safely**: it uses only the per-user HKCU Run key — *never* a Scheduled
Task or Service, precisely to keep the antivirus-persistence signal low
(`app.manifest:19-24`, `StartupRegistrationService.cs:18-28`). The first time it is
enabled the launcher shows a **one-time tray balloon** telling you
(`MainWindow.xaml.cs:2630`), and the write is keyed to a seed marker so that once you
turn it off it **can never silently turn itself back on**
(`StartupRegistrationService.cs:76-97`) — a code invariant, pinned by a unit test.

**File-deletion safety · Seguridad al borrar archivos.** The junction redirects
**never delete a real folder** — they move the real folder aside once and remove only
the *link* (`recursive:false`), and bail rather than clobber if an "aside" already
exists (`AoE3UserDataRedirect.cs:74-103`). Uninstall **refuses the stock base game
outright** (`UninstallService.cs:73-79`) and, for a mod overlaid onto your real AoE3,
deletes **only the mod's own net-new files, never a directory or your saves**
(`UninstallService.cs:97-118`). The launcher's own self-uninstall explicitly "**never
touches installed mods**" (`SelfInstallService.cs:357`).

**Process launching.** The game is launched **re-parented under `explorer.exe`**
(`DetachedProcessLauncher.cs:97-161`) so that force-closing the launcher in Task
Manager doesn't kill the game — a robustness feature, not stealth. The only `cmd.exe`
uses are `mklink /J` for junctions and a deferred self-delete script during the
launcher's *own* uninstall (`SelfInstallService.cs:325-386`), both targeting only the
launcher's own folders.

---

## 5. Privacy & data handling · Privacidad y manejo de datos

**EN.** No analytics, no ad networks, no third-party tracking (`PRIVACY.md:13`). By
default the only network activity is update/catalog/translation/news checks, which
you can fully disable in *Settings → Updates* (`PRIVACY.md:64`). Multiplayer is
opt-in — nothing leaves your PC until you sign in with Discord (`PRIVACY.md:17`). The
optional local telemetry log (`multiplayer-events.log`) is **OFF by default**
(`LauncherConfig.cs:1022`), contains only event counters (no message contents, no
personal data), and **never uses the network**.

The multiplayer session token lives in
`%LocalAppData%\AoE3ModLauncher\launcher-config.json`. The "Share diagnostics" bundle
**deliberately excludes that config file** so your token can't leak in a bug report —
enforced in code (`DiagnosticLog.cs:192-221`) and pinned by a unit test
(`DiagnosticLogTests.cs:63`).

**ES.** Sin analítica, sin redes de anuncios, sin rastreo de terceros
(`PRIVACY.md:13`). Por defecto, la única actividad de red son las comprobaciones de
actualización/catálogo/traducciones/noticias, que puedes desactivar por completo en
*Configuración → Actualizaciones* (`PRIVACY.md:64`). El multijugador es opcional:
nada sale de tu PC hasta que inicias sesión con Discord (`PRIVACY.md:17`). El registro
de telemetría local opcional está **desactivado por defecto**
(`LauncherConfig.cs:1022`), solo contiene contadores de eventos (sin contenido de
mensajes ni datos personales) y **nunca usa la red**.

El token de sesión del multijugador está en `launcher-config.json`. El paquete
"Compartir diagnósticos" **excluye a propósito ese archivo** para que tu token no se
filtre en un reporte de error — garantizado en el código y fijado con un test.

Full detail: [**PRIVACY.md**](../PRIVACY.md).

---

## 6. No malware behaviour — absence confirmed · Sin comportamiento de malware — ausencia confirmada

**EN.** A security review looks not only at what the code *does* but at what a piece
of malware *would* do and confirms it is **absent**. Targeted searches across the
whole source tree returned **zero matches** for every one of these:

**ES.** Una revisión de seguridad no solo mira lo que el código *hace*, sino lo que
*haría* un malware, y confirma su **ausencia**. Búsquedas dirigidas en todo el árbol
de código devolvieron **cero coincidencias** para cada uno de estos:

| Malware technique · Técnica de malware | Searched for · Se buscó | Result · Resultado |
| --- | --- | --- |
| Anti-debug / anti-analysis | `IsDebuggerPresent`, `CheckRemoteDebuggerPresent`, `NtQueryInformationProcess` | **None · Ninguno** |
| Process injection · Inyección | `VirtualAllocEx`, `WriteProcessMemory`, `CreateRemoteThread`, `SetWindowsHookEx`, `VirtualProtect` | **None · Ninguna** |
| AV / telemetry tampering · Sabotaje de AV | `AmsiScanBuffer`, ETW patching, `Set-MpPreference`, `DisableRealtimeMonitoring`, `ExclusionPath` | **None · Ninguno** |
| Obfuscation tooling · Ofuscadores | ConfuserEx, Dotfuscator, Fody, Costura; base64-decoded URLs | **None · Ninguna** |
| Remote / dynamic code · Código remoto | `Assembly.Load`, `Reflection.Emit`, `FromBase64String` feeding execution | **None · Ninguno** |
| Keylogging / input capture · Keylogging | `GetAsyncKeyState`, `keybd_event`, `WH_KEYBOARD` | **None · Ninguno** |
| Process / window hiding · Ocultar procesos | task-manager hiding, hidden persistence | **None · Ninguno** |

The only reflection in the app is `Activator.CreateInstance` on the standard
`WScript.Shell` COM object to fix `.lnk` shortcut icons
(`NativeInstallService.cs:1769-1772`) — a benign, well-known Windows pattern. Elevation
posture is conservative (`asInvoker`, elevate-on-demand), deliberately avoiding the
stronger persistence signal of a service or scheduled task
(`app.manifest:7-35`).

**Historical note.** The project *once* bundled a native Detours-based hook DLL /
injector (an old approach to LAN multiplayer); it was **removed**, and there are no
`native/` or `third_party/` binaries in the tree today — the `.csproj` `Compile
Remove` globs are defensive leftovers only (`.csproj:213-223`). The scary-sounding
`Win32/Injector` name some antivirus engines show is the **packer heuristic** from
§7, **not** injection code that exists in this repository.

---

## 7. Why antivirus flags it anyway (and it's still clean) · Por qué el antivirus lo marca igual (y sigue limpio)

**EN.** The launcher ships as **one large (~165 MB) self-contained `.exe`** that
bundles the whole .NET runtime, so users don't have to install anything. To a
signature-less **"packer" heuristic**, a single executable that carries a big opaque
payload *looks* structurally like the way malware crypters hide code — so it can be
flagged as `Win32/Injector`, `Wacatac`, `ML.Attribute`, etc. That is a **"this
resembles something"** verdict, not **"this is malware."**

The single biggest trigger was **single-file compression**, which unpacks assemblies
to a `%TEMP%` cache at first launch (self-extracting behaviour). So the project
**turned compression OFF on purpose** — that's why the binary is ~165 MB instead of
~120 MB (`.csproj:69-83`). This trades size for a much smaller AV footprint. Add the
usual factors — an **unsigned / unknown publisher**, **low reputation** (a new binary
with few downloads), and normal-but-suspicious-in-isolation actions (it downloads
files, writes a Run key, elevates on demand) — and a first-run SmartScreen/Defender
warning is expected for *any* small open-source tool.

**ES.** El launcher se distribuye como **un único `.exe` grande (~165 MB)**
autocontenido que incluye todo .NET para que no tengas que instalar nada. Para un
**heurístico de "empaquetador"** sin firma, un ejecutable con un payload grande y
opaco *se parece* estructuralmente a cómo los *crypters* de malware esconden código —
por eso puede marcarse como `Win32/Injector`, `Wacatac`, `ML.Attribute`, etc. Es un
veredicto de **"esto se parece a algo"**, no de **"esto es malware."**

El mayor disparador era la **compresión de archivo único**, que descomprime los
ensamblados a una caché en `%TEMP%` en el primer arranque (comportamiento
autoextraíble). Por eso el proyecto **desactivó la compresión a propósito** —por eso
pesa ~165 MB y no ~120 MB (`.csproj:69-83`). Sumado a lo habitual —**editor sin firma
/ desconocido**, **reputación baja** y acciones normales-pero-sospechosas-aisladas—,
un aviso de SmartScreen/Defender en el primer arranque es esperable para *cualquier*
herramienta open-source pequeña.

Full user-facing explanation + how to get past the warning:
[**IS-IT-A-VIRUS.md**](IS-IT-A-VIRUS.md) · [**INSTALL.md**](../WarsOfLibertyLauncher/INSTALL.md).

---

## 8. Integrity: builds, signing & verified updates · Integridad: compilación, firma y actualizaciones verificadas

**EN.**

- **Public CI build.** Official releases are built in **GitHub Actions on a clean
  `windows-latest` runner** with read-only permissions
  (`.github/workflows/release.yml:32-35`), triggered by a version tag. **Unit tests
  run before** the shippable build (`release.yml:70-72`), and the `.exe`'s SHA-256 is
  printed to the run log and the release notes (`release.yml:98-109`).
- **Verify your download.** Every release publishes the `.exe`'s SHA-256; compare it
  with `Get-FileHash Aoe3ModLauncher.exe -Algorithm SHA256`. Match = your file is
  byte-for-byte what CI built.
- **Verified self-update.** Before swapping itself, the launcher verifies the
  downloaded binary's **SHA-256** and its **Authenticode signer** against the running
  binary, deleting the download on any failure, then does an **atomic swap with
  rollback** (`LauncherUpdateService.cs:232-332`).
- **Verified mod payloads.** Each downloaded payload part is checked against a
  **catalog-pinned SHA-256** (`NativeInstallService.cs:782-813`), and `.tar.xz`
  patches against a **CRC32** from the update manifest (`UpdateService.cs:800-843`);
  zip extraction is **clamped to the install root** to block path-traversal
  (`ArchiveService.cs:194-204`).

**ES.**

- **Compilación en CI pública.** Los releases oficiales se construyen en **GitHub
  Actions, en un runner limpio `windows-latest`** con permisos de solo lectura,
  disparados por una etiqueta de versión. **Los tests corren antes** de la compilación
  final, y la SHA-256 del `.exe` se imprime en el registro y en las notas del release.
- **Verifica tu descarga.** Cada release publica la SHA-256 del `.exe`; compárala con
  `Get-FileHash Aoe3ModLauncher.exe -Algorithm SHA256`. Si coincide, tu archivo es
  exactamente el que compiló el CI.
- **Autoactualización verificada.** Antes de reemplazarse, el launcher verifica la
  **SHA-256** y el **firmante Authenticode** del binario descargado contra el binario
  en ejecución, borra la descarga ante cualquier fallo, y hace un **intercambio
  atómico con reversión**.
- **Payloads de mods verificados.** Cada parte descargada se coteja con una **SHA-256
  fijada en el catálogo**, y los parches `.tar.xz` con un **CRC32** del manifiesto; la
  extracción de zips está **limitada a la carpeta de instalación** para bloquear
  path-traversal.

**Signing status — the honest version · Estado de la firma — la versión honesta.**
Release binaries are *intended* to be Authenticode-signed by **SignPath Foundation**
(free code signing for open source). As of this audit that application is
**pending / not yet live**: the CI signing job is **gated and currently skipped**
until the SignPath project is approved (`release.yml:131`). Until then, **CI releases
are unsigned** and verified by their **published SHA-256**, and local/developer builds
use a **self-signed `CN=Gorgorito`** certificate that proves integrity **only on the
build machine** and does *not* suppress the antivirus warning
(`.csproj:69-82`, `docs/IS-IT-A-VIRUS.md:158-162`).

Details: [**CODE_SIGNING_POLICY.md**](../CODE_SIGNING_POLICY.md) ·
[**docs/BUILDING.md**](BUILDING.md).

---

## 9. Honest caveats & known gaps · Salvedades honestas y puntos abiertos

A credible audit lists what is *not* perfect. None of the below is malicious; they are
disclosed so you can judge for yourself. · Una auditoría creíble enumera lo que *no*
es perfecto. Nada de lo siguiente es malicioso; se declara para que juzgues tú mismo.

1. **Trusted signature is not live yet.** SignPath is applied-for, not active, so
   today's releases are CI-unsigned (verify by SHA-256). · La firma de confianza aún
   no está activa; verifica por SHA-256.
2. **Not a bit-for-bit reproducible build.** The build is source-determined and CI
   normalises paths (`ContinuousIntegrationBuild=true`), but ReadyToRun + an embedded
   runtime is not guaranteed byte-identical across environments; the integrity
   guarantee is the **published SHA-256**, not a reproducible-build attestation. · No
   es una compilación reproducible bit a bit; la garantía es la SHA-256 publicada.
3. **Self-update signer check is "same-signer," not "trusted-chain,"** and is
   **skipped if the running binary is unsigned** — so for today's CI-unsigned builds
   it effectively relies on the SHA-256 check (`LauncherUpdateService.cs:306-331`). ·
   La verificación de firmante en la autoactualización es "mismo firmante", no "cadena
   de confianza", y se omite si el binario en ejecución no está firmado.
4. **CRC32 on `.tar.xz` patches is a corruption check, not cryptographic** — the
   strong control on the payload path is the catalog-pinned SHA-256. · El CRC32 de los
   parches es un control de corrupción, no criptográfico.
5. **Two legacy endpoints use plaintext HTTP** (`aoe3wol.com` and SourceForge
   `UpdateInfo.xml`) — the mod *payload* itself downloads over HTTPS from GitHub. ·
   Dos endpoints heredados usan HTTP en texto plano; el *payload* del mod se descarga
   por HTTPS desde GitHub.
6. **Lobby and notification-feed URLs are user-editable in the config,** so a
   hand-modified `launcher-config.json` could repoint those two services (both remain
   http/https; the feed falls back to GitHub). · Las URLs del lobby y del feed son
   editables en el config.
7. **Multiplayer backend repos are separate** (`wol-launcher-lobby-node`,
   `notifier-server`) and are not part of this checkout, so their license/visibility
   can't be confirmed from here. · Los repos del backend de multijugador son separados
   y no pueden confirmarse desde aquí.
8. **Two documentation inaccuracies noted for the maintainer** (not security issues):
   `CODE_SIGNING_POLICY.md` reads as if SignPath were already live (it is pending),
   and a stale `.csproj` comment referenced `requireAdministrator` while the manifest
   correctly declares `asInvoker`. · Dos imprecisiones de documentación anotadas para
   el responsable (no son problemas de seguridad).

---

## 10. Verify it yourself · Compruébalo tú mismo

You do **not** have to trust this document. · **No** tienes que confiar en este
documento.

1. **Read the code.** Everything is public:
   <https://github.com/Gorgorito12/AoE3-Mod-Launcher>. Open any `file:line` cited above.
2. **Scan on VirusTotal** (~70 engines at once). Heuristic/generic names
   (`Injector`, `Wacatac`, `ML.Attribute`) mean "looks like", not "is".
3. **Verify the SHA-256** of your download against the release notes
   (`Get-FileHash Aoe3ModLauncher.exe -Algorithm SHA256`).
4. **Build it yourself** from source with the .NET 8 SDK on Windows — you get the same
   launcher (see [IS-IT-A-VIRUS.md](IS-IT-A-VIRUS.md)).
5. **Confirm the AI models** with `git log` (see §2).
6. **Found something genuinely malicious?**
   [Open an issue](https://github.com/Gorgorito12/AoE3-Mod-Launcher/issues) or a
   [security advisory](https://github.com/Gorgorito12/AoE3-Mod-Launcher/security/advisories/new)
   — it's reviewed in the open.

---

*See also · Ver también:* [IS-IT-A-VIRUS.md](IS-IT-A-VIRUS.md) ·
[SECURITY.md](../SECURITY.md) · [PRIVACY.md](../PRIVACY.md) ·
[CODE_SIGNING_POLICY.md](../CODE_SIGNING_POLICY.md) ·
[BUILDING.md](BUILDING.md) · [DISCLAIMER.md](../DISCLAIMER.md)

*This audit reflects the repository state on its audit date. Code moves; re-run the
searches in §6 and the checks in §10 against the current source to confirm. · Esta
auditoría refleja el estado del repositorio en su fecha. El código cambia; vuelve a
correr las búsquedas del §6 y las comprobaciones del §10 sobre el código actual.*
