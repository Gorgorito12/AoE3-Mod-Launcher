# Roadmap completo — AoE3 Mod Launcher (Wars of Liberty / Improvement Mod)

## Contexto

El launcher está en `v0.7.9`, ~20k líneas C#/XAML, escrito en .NET 8 + WPF. Ya cumple un rol sólido como reemplazo del actualizador Java de Wars of Liberty y soporta multi-mod (WoL + Improvement Mod). Tiene catálogo externo en GitHub con auto-merge, instalación nativa, traducciones comunitarias, self-update, EN/ES.

El pedido del usuario: hoja de ruta **grande y completa** para todo lo que se puede hacer, no solo multiplayer. Audiencia es el usuario solo (documento de trabajo interno, tono directo). Refactor a aplicar es **pragmático** — modularizar sin migrar a MVVM completo.

Objetivos transversales:
- **100% gratis** (sin tarjeta de crédito en cuenta CF; presupuestar para 50 DAU pico).
- **Compatible con lo que ya funciona** — no romper el flujo actual de WoL y IM.
- **Bajar la fricción para agregar mods nuevos** al ecosistema.
- **UI moderna** que reemplace la sensación de actualizador legacy.
- **Multijugador integrado** estilo Voobly/GameRanger.

---

## Estado actual (resumen)

| Área | Madurez | Notas |
|---|---|---|
| Install/update pipeline | 🟢 Sólido | `UpdateService` 752 L, `NativeInstallService` 772 L. Resumable, CRC32, backups. |
| Catálogo de mods | 🟡 Backend listo, sin UI | `ModRegistry` carga built-ins + GitHub. **No hay browser visual**. |
| Mecanismos install | 🟡 Limitado | Solo `IsolatedFolder` y `InPlaceOverlay`. Falta `StandardModsFolder` (Documents). |
| Mecanismos update | 🟢 3 funcionando | `WolPatcher`, `DelegatedExternal`, `GitHubReleases`. |
| UI/UX | 🟠 Funcional pero monolítica | `MainWindow.xaml.cs` 3998 L code-behind, sin ResourceDictionary global. Estilos duplicados en 9 diálogos. |
| Localización | 🟡 EN/ES hardcoded | `Strings.cs` 103 KB con diccionario en código. Hay que migrar a archivos externos. |
| Multijugador | 🔴 Inexistente | Backend ya pensado (CF + ZeroTier hosted, ver Track C). |
| Self-update launcher | 🟢 Funciona | `LauncherUpdateService`, tag-based. |
| Code signing | 🟡 Self-signed | Cert `CN=Gorgorito`, post-build target. SmartScreen sigue alertando. |
| CI/CD | 🔴 Manual | `build-release.ps1` local. **Sin GitHub Actions en el launcher** (sí en el catálogo). |
| Tests | 🔴 Cero | No hay proyecto de tests. |
| Crash reporting | 🔴 Solo local | `DiagnosticLog.cs` a archivo, sin envío remoto. |
| Discord RPC / Play time / News | 🔴 Inexistente | Tab "Noticias" existe pero sin backend. |

---

## Tracks del roadmap

Cada track es una unidad de trabajo independiente, con prioridad, esfuerzo y archivos afectados.

### 🅰️ Track A — UI/UX pragmática (alta prioridad, 4–5 semanas)

**Goal**: modernizar la primera impresión del launcher sin reescribir todo.

#### A1. Modularizar MainWindow.xaml.cs (3998 L → ~600 L)
- Extraer en UserControls:
  - `Views/HeroBanner.xaml` (mod selector + banner + accent)
  - `Views/StatusCard.xaml` (estado + versiones + AoE3 row)
  - `Views/ActionPanel.xaml` (Play / Update / Verify / Repair / Browse)
  - `Views/ProgressPanel.xaml` (overlay durante operaciones)
  - `Views/MainTabs.xaml` (Noticias / Changelog / Ayuda → expandible)
- MainWindow queda como "shell" que orquesta UserControls.
- Code-behind se queda en cada UserControl (no migramos a MVVM).
- Archivos: `MainWindow.xaml`, `MainWindow.xaml.cs` (refactor); nuevos en `Views/`.

#### A2. ResourceDictionary global
- Mover estilos compartidos (`DialogButton`, `PrimaryButton`, `SectionHeader`, `HintText`, scrollbars, tooltips) de `MainWindow.xaml` y los 9 diálogos a `Styles/Common.xaml`.
- Cargar como Merged Dictionary en `App.xaml`.
- Elimina ~2800 líneas de estilos duplicados.
- Archivos: `App.xaml`, nuevo `Styles/Common.xaml`, los 10 archivos XAML pierden la duplicación.

#### A3. Sistema de temas
- Tres modos: claro / oscuro / sistema.
- Variable `AccentColor` dinámica leída del mod activo (`ModProfile.AccentColor`) — ya existe el dato, falta usarlo en bindings.
- Setting nuevo en `LauncherConfig`: `Theme` (enum), `AccentColorOverride` (string, opcional).
- Archivos: `Models/LauncherConfig.cs`, `Styles/Themes/Dark.xaml`, `Styles/Themes/Light.xaml`, `LauncherSettingsDialog`.

#### A4. Pestañas principales reorganizadas
- Header con tabs: `Jugar` · `Mods` · `Multijugador` · `Noticias` · `Configuración`.
- "Jugar" = la vista actual (status + acciones).
- "Mods", "Multijugador" = nuevas (Tracks B y C).
- "Noticias" = el tab interno actual promovido a tab principal.
- Archivos: `MainWindow.xaml`, nuevo `Views/MainTabs.xaml`.

#### A5. News feed funcional
- Nuevo `Services/NewsService.cs`: fetch desde `news.json` en repo del catálogo (mismo repo de mods).
- Formato: `{ title, date, body (markdown), image, modIds: ["wol", null] }` (null = global).
- Cache 1 h, mostrar `last 10`. Markdown renderizado con `Markdig` NuGet (~200 KB, gratis).
- UI: lista de cards con imagen + texto recortado + "leer más" que expande.
- Archivos: nuevo `Services/NewsService.cs`, nuevo `Models/NewsItem.cs`, nuevo `Views/NewsTab.xaml`.

#### A6. Window state + last-used persistence
- Guardar posición, tamaño, último tab abierto, último mod activo en `LauncherConfig`.
- Restaurar en startup (con validación: pantalla puede haberse desconectado).
- Archivos: `Models/LauncherConfig.cs` (campos nuevos: `WindowX`, `WindowY`, `WindowWidth`, `WindowHeight`, `WindowMaximized`, `LastActiveTab`, `LastActiveModId`), `MainWindow.xaml.cs` (suscripción a `Closing`).

#### A7. Discord Rich Presence
- NuGet `DiscordRichPresence` (~50 KB, gratis, MIT).
- Mostrar: "Jugando Wars of Liberty v2.x" cuando se detecta `age3y.exe` corriendo (ya hay `_gameMonitorTimer`).
- Setting opt-in en `LauncherSettingsDialog`.
- Archivos: nuevo `Services/DiscordPresenceService.cs`, hook en `MainWindow.xaml.cs` donde corre `_gameMonitorTimer`.

#### A8. Animaciones suaves
- Storyboards XAML para transiciones entre tabs y aparición de paneles (no usar librerías externas).
- Loading skeleton para la lista de mods en lugar de spinner.
- Archivos: cada UserControl nuevo de Track A.

**Esfuerzo total Track A**: ~4–5 semanas.

---

### 🅱️ Track B — Mods más fáciles de agregar y consumir (3–4 semanas)

**Goal**: que el catálogo deje de tener 2 mods hardcoded y se vuelva un ecosistema vivo.

#### B1. Mod Browser dentro del launcher
- Tab `Mods` con grid de cards: icon, displayName, autor, descripción, accent color como borde.
- Filtros: instalado / no instalado, idioma, tipo (IsolatedFolder / Overlay / StandardMods).
- Click → vista detalle con banner grande, screenshots opcionales, "Instalar" / "Desinstalar".
- Reutiliza `ModCatalogService` (cache de 24 h ya implementado) y `ModAssetCacheService`.
- Archivos: nuevo `Views/ModBrowserTab.xaml`, nuevo `Views/ModDetailPage.xaml`.

#### B2. Soporte de `StandardModsFolder`
- Nuevo `ModInstallType.StandardModsFolder`: mod se instala en `%USERPROFILE%\Documents\My Games\Age of Empires 3\Mods\<modName>\`.
- Activación: copiar/symlink al folder, escribir `Mods.xml` para que AoE3 lo registre.
- Lanzamiento: AoE3 base con `-mod:<modName>` arg.
- Cubre el ecosistema oficial de modding AoE3 (ESOC patch, etc.).
- Archivos: `Models/ModProfile.cs` (nuevo enum value), `Services/NativeInstallService.cs` (rama nueva), `Services/GameLauncher.cs` (args nuevos).

#### B3. Soporte de AoE3: Definitive Edition
- Nuevo `GameEdition` enum en `ModProfile`: `LegacyTAD` (actual) / `DefinitiveEdition`.
- `Aoe3DetectorService` ya busca registry → agregar las rutas de DE (Steam app id `933110`).
- Distinto folder de Mods (`%USERPROFILE%\Games\Age of Empires 3 DE\<userId>\mods\`).
- Algunos mods existen en ambas versiones; el manifest puede declarar `compatibleEditions: ["LegacyTAD", "DefinitiveEdition"]`.
- Archivos: `Models/ModProfile.cs`, `Models/ModCatalogManifest.cs`, `Services/Aoe3DetectorService.cs`.

#### B4. Wizard "Publicar mi mod"
- Diálogo nuevo accesible desde el botón `Mods` → `+ Publicar mi mod`.
- Form: ID, displayName, autor, descripción EN/ES, accent color picker, install type, payload URLs.
- Valida contra el schema JSON local (`aoe3-mods-catalog-template/schema/mod.schema.json` embebido como recurso).
- Genera el `mod.json` y abre el navegador en `https://github.com/Gorgorito12/aoe3-mods-catalog/new/main/mods/<id>/` con el contenido pre-rellenado (URL params).
- Archivos: nuevo `PublishModDialog.xaml`, recurso embebido del schema.

#### B5. Dependencias entre mods (opcional, baja prioridad)
- Campo nuevo en `mod.json`: `dependencies: [{ id: "esoc-patch", version: ">=1.5" }]`.
- El launcher instala dependencias antes que el mod principal.
- Útil para mods que requieren ESOC patch o Sandbox.
- Archivos: `Models/ModCatalogManifest.cs`, `Services/NativeInstallService.cs` (resolución topológica).

#### B6. Mejorar el flow Tier 2 del catálogo
- En el repo de catálogo, el workflow `auto-merge.yml` valida tag bump.
- Agregar validación: que el tag exista en `sourceRepo` y tenga al menos un asset `.zip`.
- Esto evita "tag fantasma" que se mergea y rompe usuarios.
- Archivos: `aoe3-mods-catalog-template/.github/scripts/classify_pr.py` (extender), o nuevo `validate_release_tag.py`.

**Esfuerzo total Track B**: ~3–4 semanas (sin B5 ni B6 que pueden esperar).

---

### 🅲 Track C — Multijugador (6–9 semanas)

**Goal**: lobby + chat + matchmaking + red virtual estilo Voobly, todo gratis, dimensionado para 50 DAU pico.

#### C1. Backend en Cloudflare (1–2 semanas)
- Cuenta Cloudflare **sin tarjeta** (físicamente no pueden cobrar).
- Worker + Durable Objects + D1 + R2.
- Schema D1: `users`, `friends`, `games`, `replays`, `bans`, `usage_today`.
- Endpoints REST mínimos: `/auth/github`, `/me`, `/games` (CRUD), `/friends`, `/replays`.
- WebSocket Hibernation API para chat lobby y eventos de salas (cero gasto cuando idle).
- Repo nuevo: `wol-launcher-lobby-worker` (separado del launcher).

#### C2. Red virtual con ZeroTier Central (1–2 semanas)
- API gratis de ZeroTier Central: redes ilimitadas, 25 nodos por red.
- **1 red ZeroTier creada on-demand por sala de juego** (máximo 8 jugadores AoE3 → muy lejos del límite).
- Worker tiene el `ZT_API_TOKEN` (secret), expone `/games/{id}/network` que crea la red y devuelve `network_id` + `assigned_ip`.
- Cliente WPF baja el binario oficial de ZeroTier (gratis, MSI silencioso), `zerotier-cli join <id>`, espera IP `25.x.x.x`.
- Al terminar la partida: el host destruye la red.

#### C3. TURN/relay fallback (0 semanas inicial)
- Empezar **sin TURN propio**. Si ~10% de jugadores caen en NAT simétrica, lo notan, no hay solución mágica al inicio.
- Si se vuelve problema: usar un TURN público gratis (ej. `openrelay.metered.ca` tiene plan free generoso) antes de auto-hostear.
- Si el problema crece y compensa, levantar un `coturn` en VPS Hetzner CX11 (€3.79/mes) — no es gratis pero es el último recurso.

#### C4. Cliente WPF — pestaña Multijugador (2–3 semanas)
- Subtabs internos: `Salas` · `Amigos` · `Perfil` · `Historial`.
- Salas: lista filtrable (mapa, ranked, ping), botón "Crear sala".
- Vista de sala: 8 slots, color, civilización, mapa, chat de sala, "Listo", "Comenzar" (solo host).
- Chat global persistente en sidebar.
- Friends: lista, presencia, DMs.
- Perfil: ELO Glicko-2 (cálculo en Worker), partidas, replays.
- Archivos: `Views/MultiplayerTab.xaml` con subtabs, `Services/LobbyClient.cs` (WebSocket + REST), `Services/ZeroTierService.cs`.

#### C5. Game launch flow (1 semana)
- Host crea sala → Worker reserva slot + crea red ZT.
- Peers join → reciben `network_id` + IPs virtuales del resto.
- Host clickea "Comenzar" → Worker emite evento `start` con la IP virtual del host.
- Cada cliente: lanza AoE3 (`game.exe -nointro`) con la IP virtual del host como "Direct IP Connect".
- Launcher mantiene el WebSocket abierto durante la partida solo para chat/admin (no para tráfico de juego).

#### C6. ELO, replays, anti-cheat liviano (continuo)
- ELO: Glicko-2 calculado en Worker tras cada partida (host reporta resultado, peers confirman).
- Replays: `.aoe3rec` subido a R2 (10 GB gratis), descargable desde perfil. Cap 5 MB/replay.
- Anti-cheat: hash del `age3y.exe` reportado al join; partidas con hashes distintos quedan unranked.

#### C7. Caps y circuit breakers
- `MAX_CONCURRENT_USERS = 60`, `MAX_ACTIVE_GAMES = 8`, `MAX_CHAT_MSG_PER_MIN = 30`, `MAX_REPLAY_SIZE_MB = 5`.
- Contador diario en D1 (`usage_today`): cuando llega a 80% gasta, rechaza features no-críticas; 95% rechaza nuevos logins.
- Mensaje claro en UI: "Servidor lleno (free tier). Intentá en unos minutos."
- Barra superior visible: "🟢 Cuota: 34% · 14 online · 2/8 partidas".

**Esfuerzo total Track C**: ~6–9 semanas.

---

### 🅳 Track D — Infraestructura / DX (continuo, 2–3 semanas iniciales)

**Goal**: profesionalizar el ciclo de release y dar paz mental.

#### D1. GitHub Actions: build + sign + release
- Workflow nuevo en `.github/workflows/release.yml` del launcher.
- Trigger: push de tag `v*`.
- Steps: `dotnet publish` → firma con cert (almacenado como GitHub secret) → crea release → sube `.exe` + SHA-256.
- Manual `build-release.ps1` queda como respaldo local.
- Archivos: nuevo `.github/workflows/release.yml`, ajuste menor en `WarsOfLibertyLauncher.csproj` para CI-aware signing.

#### D2. Tests unitarios
- Proyecto nuevo `WarsOfLibertyLauncher.Tests` con xUnit.
- Cobertura prioritaria: parseo de `UpdateInfo.xml`, computación de chain de patches, validación de schema de mod manifest, hashing.
- No apuntamos a 100% coverage; apuntamos a regresiones de lógica crítica.
- Archivos: nuevo proyecto, integrado al `.sln`.

#### D3. Crash reporting opt-in
- **Sentry free tier** (5k errors/mo, suficiente para 50 DAU).
- Setup en `App.xaml.cs` con handler global `DispatcherUnhandledException` + `AppDomain.CurrentDomain.UnhandledException`.
- Opt-in en `LauncherSettingsDialog`. Default: off.
- Si off: solo log local (lo de hoy).
- Archivos: `App.xaml.cs`, `Services/CrashReporter.cs` nuevo, setting en `LauncherConfig`.

#### D4. Telemetría opt-in (postergar, opcional)
- **PostHog free tier** (1M events/mo) o evitar del todo.
- Track: `launcher_started`, `mod_installed`, `game_launched`, `update_applied`.
- Solo si el usuario lo decide más adelante. **Default: postergar**.

#### D5. Code signing — evaluar EV o Azure Trusted Signing
- Self-signed actual sigue dando warnings de SmartScreen.
- **Azure Trusted Signing**: ~$10/mes, soporta CI sin cert local, mata warnings de SmartScreen. **Recomendado si hay presupuesto mínimo**.
- Alternativa gratis: documentar bien en INSTALL.md cómo agregar el cert auto-firmado a `TrustedPublisher`.
- Sin código, solo decisión.

#### D6. Auto-changelog desde commits
- GitHub Action que en cada tag genera el changelog desde commits desde el tag anterior (formato Conventional Commits opcional).
- Hace el body del release automáticamente.
- Archivos: `.github/workflows/release.yml` (mismo workflow de D1).

#### D7. SmartScreen sample submission automation
- Después de cada release, script PowerShell que sube el .exe a Microsoft Defender Sample Submission API.
- Reduce el tiempo en que el binario está "unknown publisher".
- Archivos: `.github/workflows/release.yml` (step extra).

**Esfuerzo total Track D**: ~2–3 semanas para D1+D2+D3, resto continuo.

---

### 🅴 Track E — Calidad de vida (continuo, sin urgencia)

#### E1. PlayTime tracking
- `Services/PlayTimeService.cs`: hookea `GameLauncher` start/stop, persiste en `LauncherConfig.Mods[id].PlayTimeHours` + `LauncherConfig.Mods[id].LastPlayed`.
- Mostrar en `StatusCard`: "Jugaste 12h esta semana".
- Archivos: nuevo service, extender `Models/ModState.cs`.

#### E2. Achievements locales
- 100% local, sin servidor. JSON con definiciones de logros.
- Ejemplos: "Primera victoria", "10 horas jugadas", "Probaste 3 mods".
- Notificación in-app (toast).
- Archivos: nuevo `Services/AchievementService.cs`, `Models/Achievement.cs`.

#### E3. Notificaciones granulares
- Hoy: toast on/off global.
- Mejorar: por evento (update disponible, partida lista, amigo conectado).
- Archivos: extender `LauncherConfig` con `NotificationPrefs` dict.

#### E4. Background update checker
- Hoy: check on startup.
- Agregar: timer cada N horas para chequear (configurable).
- Archivos: `Services/UpdateService.cs`, `MainWindow.xaml.cs`.

#### E5. Keyboard shortcuts
- `Ctrl+1..5` para cambiar de tab.
- `F5` para refresh catálogo.
- `Ctrl+L` para abrir log.
- Archivos: `MainWindow.xaml.cs` (InputBindings).

---

### 🅵 Track F — Localización (2 semanas)

#### F1. Migrar Strings.cs a archivos externos
- De 103 KB hardcoded → archivos JSON por idioma en `Localization/` (`en.json`, `es.json`).
- `Strings.cs` queda solo como API que lee de los JSON.
- Permite hot-reload (cambiar idioma sin recompilar).
- Archivos: `Localization/Strings.cs` (refactor), nuevos `Localization/Strings.en.json`, `Localization/Strings.es.json`.

#### F2. Sumar idiomas
- PT-BR (gran comunidad AoE3 en Brasil), FR, DE, RU.
- Cada idioma es un PR de la comunidad agregando `Strings.<lang>.json`.

#### F3. In-launcher translation tool para el propio launcher
- Similar al `TranslationPackagerDialog` actual (que es para el juego), pero para los strings del launcher.
- Permite a un usuario editar EN strings → ver resultado → exportar `.json` para PR.
- Archivos: nuevo `LauncherTranslationDialog.xaml`.

---

### 🅶 Track G — Seguridad / robustez (sin urgencia, evaluar caso por caso)

#### G1. Firma ed25519 opcional del `mod.json`
- Autor del mod firma su `mod.json` con clave privada; clave pública en su perfil de GitHub.
- Launcher verifica antes de instalar.
- Defensa contra cuentas de modder comprometidas.
- Archivos: `Services/ModCatalogService.cs` (validación), `Models/ModCatalogManifest.cs` (campo `signature`).

#### G2. Mirror system extendido
- Hoy: primary URL + SourceForge fallback.
- Sumar: R2 mirror (gratis 10 GB), IPFS opcional.
- Archivos: `Services/DownloadService.cs`, `Models/ModCatalogManifest.cs` (campo `mirrors[]`).

#### G3. Delta updates (binary diff)
- En lugar de descargar archivos completos, descargar bsdiff entre versiones.
- Solo vale la pena si los mods grandes empiezan a tener updates frecuentes.
- Archivos: nuevo `Services/DeltaUpdateService.cs`, ajustes en `UpdateService.cs`.

#### G4. Quarantine integration
- En vez de descargar a `%TEMP%`, descargar a un folder cuarentena que Defender escanea antes de extraer.
- Archivos: `Services/DownloadService.cs`.

---

### 🅷 Track H — Comunidad / engagement (continuo)

#### H1. News feed por mod (ya en A5)
- Cubierto en Track A5.

#### H2. Discord webhook integration
- En el WoL Discord, los usuarios pueden ver cuándo alguien crea partida pública.
- Worker postea a un webhook configurado.
- Archivos: solo Worker, no toca el launcher.

#### H3. Botones a comunidad
- En el banner del mod, botones rápidos: Discord, Reddit, Foro oficial, redes sociales.
- Datos vienen del `mod.json` (campo nuevo `community: { discord, reddit, forum, twitter }`).
- Archivos: `Models/ModCatalogManifest.cs`, `Views/HeroBanner.xaml`.

#### H4. Sistema de feedback in-app
- Botón "Reportar problema" → diálogo con: descripción, adjuntar `launcher-debug.log` opcional, enviar a un endpoint del Worker o crear issue de GitHub vía API.
- Archivos: nuevo `FeedbackDialog.xaml`, endpoint `/feedback` en Worker.

#### H5. Banners de torneos/eventos
- Campo opcional en `news.json`: `pinned: true, banner: url, callToAction: { label, url }`.
- Muestra arriba del tab "Noticias" como banner promocional.

---

### 🅸 Track I — Calidad de código (continuo, sin urgencia)

Compatible con el refactor pragmático del usuario (no full MVVM).

#### I1. Consolidar duplicaciones
- 3 copias de `GitHubRelease` / `GitHubAsset` (en `GitHubReleaseDownloader`, `LauncherUpdateService`, `TranslationRegistryService`).
- Unificar en `Models/GitHub/`.

#### I2. Interfaces para servicios críticos (mockables)
- `IUpdateService`, `IInstallerService`, `IModCatalogService`, `IDownloadService`.
- Facilita tests sin migrar a DI completa.

#### I3. Refactorizar servicios grandes
- `UpdateService.cs` (752 L) → separar en `UpdateOrchestrator` + `PatchApplier` + `VersionDetector`.
- `NativeInstallService.cs` (772 L) → separar en `InstallOrchestrator` + `Aoe3Cloner` + `ModOverlay`.
- `TranslationService.cs` (621 L) → ya es bastante focused, dejarlo.

#### I4. Architecture docs
- Diagrama de servicios y data flow en `docs/architecture.md`.
- No es para "vender" el proyecto sino para vos mismo dentro de 6 meses.

---

### 🅹 Track J — Platform expansion (muy futuro, opcional)

#### J1. AoE3: DE soporte completo (ya en B3)
- Cubierto en Track B.

#### J2. Linux via Wine wrapper
- Distribución como AppImage que usa Wine para correr el launcher + AoE3.
- Mercado pequeño pero presente.

#### J3. macOS
- Muy futuro, AoE3 no corre nativo en Mac. Probablemente nunca.

---

## Secuencia recomendada

Asumiendo trabajo de tiempo parcial (~10–15 h/semana):

```
Mes 1–2:   Track A1–A4 (modularizar UI, ResourceDictionary, tabs, tema)
Mes 2:     Track A5–A8 (news, window state, Discord RPC, animaciones)
Mes 3:     Track B1–B3 (mod browser, StandardModsFolder, DE soporte)
Mes 3:     Track B4 (publish wizard)
Mes 4–5:   Track C1–C2 (backend CF + ZeroTier integration)
Mes 5–6:   Track C4–C5 (UI multiplayer + game launch flow)
Mes 6:     Track C6–C7 (ELO, replays, caps)
Continuo:  Track D (CI, tests, crash report) — se mete entre tracks
Cuando haya:  Track F (i18n PT-BR, etc.) — depende de community
Diferido:  Tracks E, G, H, I, J
```

**Hito 1 (Mes 2)**: UI moderna shippable → release `v0.8`.
**Hito 2 (Mes 3)**: Mod browser funcional → release `v0.9`.
**Hito 3 (Mes 6)**: Multijugador beta → release `v1.0`.

---

## Archivos críticos a modificar (consolidado)

### Refactor mayor
- `WarsOfLibertyLauncher/MainWindow.xaml` (3998 L → ~600 L) — A1
- `WarsOfLibertyLauncher/MainWindow.xaml.cs` — A1
- `WarsOfLibertyLauncher/App.xaml` — A2

### Models extendidos
- `WarsOfLibertyLauncher/Models/LauncherConfig.cs` — A3, A6, E1, E3
- `WarsOfLibertyLauncher/Models/ModProfile.cs` — B2, B3
- `WarsOfLibertyLauncher/Models/ModCatalogManifest.cs` — B3, B5, G1, G2, H3
- `WarsOfLibertyLauncher/Models/ModState.cs` — E1

### Services nuevos
- `Services/NewsService.cs` — A5
- `Services/DiscordPresenceService.cs` — A7
- `Services/LobbyClient.cs` — C4
- `Services/ZeroTierService.cs` — C2
- `Services/PlayTimeService.cs` — E1
- `Services/AchievementService.cs` — E2
- `Services/CrashReporter.cs` — D3

### UserControls / Views nuevos
- `Views/HeroBanner.xaml` — A1
- `Views/StatusCard.xaml` — A1
- `Views/ActionPanel.xaml` — A1
- `Views/ProgressPanel.xaml` — A1
- `Views/MainTabs.xaml` — A4
- `Views/NewsTab.xaml` — A5
- `Views/ModBrowserTab.xaml` — B1
- `Views/ModDetailPage.xaml` — B1
- `Views/MultiplayerTab.xaml` — C4

### Diálogos nuevos
- `PublishModDialog.xaml` — B4
- `FeedbackDialog.xaml` — H4
- `LauncherTranslationDialog.xaml` — F3

### Recursos
- `Styles/Common.xaml` — A2
- `Styles/Themes/Dark.xaml` — A3
- `Styles/Themes/Light.xaml` — A3
- `Localization/Strings.en.json` — F1
- `Localization/Strings.es.json` — F1

### Infra
- `.github/workflows/release.yml` — D1, D6, D7
- Proyecto `WarsOfLibertyLauncher.Tests/` — D2
- Repo externo `wol-launcher-lobby-worker` — C1

### Reutilizar (existente, no romper)
- `Services/UpdateService.cs` — solo agregar `IUpdateService` interface (I2)
- `Services/NativeInstallService.cs` — extender para `StandardModsFolder` (B2)
- `Services/ModCatalogService.cs` — extender con firmas (G1) y mirrors (G2)
- `Services/ModRegistry.cs` — sin cambios, sigue siendo source of truth
- `Services/GameLauncher.cs` — extender args para mods de standard folder (B2)
- `Services/Aoe3DetectorService.cs` — extender para AoE3 DE (B3)
- `Services/TranslationService.cs` — sin cambios
- `Services/LauncherUpdateService.cs` — refactor para reusar `GitHubReleaseDownloader` (I1)

---

## Verificación end-to-end

Cada track tiene su forma de validar.

### Track A — UI
- Smoke test manual: abrir launcher, cambiar entre tabs, switch tema, ver news, lanzar juego, verificar Discord status.
- Verificar que diálogos viejos (Aoe3Picker, InstallFolder, etc.) siguen visualmente correctos tras extraer ResourceDictionary.

### Track B — Mods
- Instalar/desinstalar WoL e IM desde el browser (regresión).
- Crear un mod de prueba en `StandardModsFolder`, ver que aparece en AoE3.
- Detección dual de AoE3 TAD + DE (probar con ambas instalaciones).
- Usar el wizard de publicar, validar el JSON contra el schema, abrir el PR.

### Track C — Multiplayer
- 2 cuentas, 2 PCs con NAT distintos, crear sala, join, ver chat, lanzar partida, conectarse, jugar 5 min, terminar, ver replay.
- Test de NAT simétrica: deshabilitar UPnP en router, probar conexión (espera que falle hasta tener TURN).
- Test de caps: scripts que simulan 100 conexiones, ver que el Worker rechaza a partir de 60.
- Test de cuota diaria: forzar contador a 80%, ver mensaje en UI.

### Track D — Infra
- Push de tag `v0.8.0` dispara workflow, genera release con .exe firmado y SHA-256.
- Tests corren en CI, fallan PRs que rompen lógica de versioning.
- Forzar excepción no manejada, ver que aparece en Sentry (si opt-in).

### Track F — i18n
- Cambiar idioma a PT-BR, ver que todos los strings cambian sin reiniciar.

---

## Decisiones diferidas (que el usuario tiene que tomar en algún momento)

1. **EV cert vs self-signed**: pagar ~$10/mes de Azure Trusted Signing o aguantar SmartScreen warnings. Postergable hasta que la cantidad de instaladores nuevos lo justifique.
2. **Telemetría opt-in**: D4. Sumar PostHog o evitar del todo. Por defecto, no implementar.
3. **Soporte AoE3 DE**: si el público real es legacy TAD (más probable para WoL), B3 es nice-to-have, no must.
4. **Achievements**: E2 es 100% nice-to-have, no agrega valor competitivo.
5. **Mod dependencies (B5)**: implementar solo si llega un mod que las necesite.
6. **Linux/Mac (J2/J3)**: improbable que valga el esfuerzo. Diferir indefinidamente.
7. **Donaciones**: no se incluye paywall ni botón de Ko-fi en el roadmap. Decisión del usuario si suma cuando crezca.

---

## Riesgos / loose ends

- **MainWindow refactor (A1)**: 3998 líneas tocadas. Riesgo de regresiones. Mitigación: hacer en pasos pequeños, una sección por commit, smoke test después de cada paso.
- **Multiplayer + ZeroTier requiere admin local**: el cliente ZT corre como servicio. UAC prompt al instalarlo. Documentar.
- **Cloudflare free tier puede cambiar reglas**: hoy DO está en free, mañana podría no. Mitigación: diseño tal que el Worker pueda apagarse y solo P2P directo siga funcionando degradado.
- **ZeroTier Central podría cambiar reglas de API o cerrar gratis**: improbable a corto plazo, pero plan B = self-hostear el controller (cuesta una VPS €4/mes).
- **Audiencia real**: el roadmap asume ~50 DAU. Si la comunidad es de 5 personas, Tracks C completo es overkill. Reevaluar después de Track A+B según engagement.

---

## Qué NO está en este roadmap (intencional)

- Reescribir el launcher en Electron / Tauri / otra cosa.
- Monetización, suscripciones, currencies.
- Modding tools dentro del launcher (editores, asset packers).
- Voice chat (Discord ya lo cubre).
- Matchmaking automático con ELO (ranked queue) — postergado, requiere masa crítica.
- Ladder global, torneos automatizados.
- AoE3 DE soporte completo de mods (la API de mods de DE es distinta y menos abierta — solo detección y launch).
