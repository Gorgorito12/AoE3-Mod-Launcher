---
name: launcher-lag-phase2-backlog
description: "AoE3-Mod-Launcher, lag (2026-10-06): fase 1 hecha y confirmada por testers; qué falta (rendimiento, errores reales, release), dónde está cada cosa y el arreglo propuesto"
metadata:
  node_type: memory
  type: project
  originSessionId: 7cbbec67-e108-42c8-972f-af8e9b608f8f
  modified: 2026-10-06T20:37:46.496Z
---

**Estado (2026-10-06).** La fase 1 está hecha y commiteada en `main`: commit `9057192` (2026-10-06 14:19, ya en origin), hecho por el usuario. Falta el release oficial. Arregló:
- el bucle de layout que no convergía en la tarjeta de actividad de Salas: `InlineFlagFit` oculta con `Hidden`, y `FitStackPanel` maqueta las filas que no caben al ancho del panel;
- la etiqueta `NON-CONVERGING` en `LAYOUT STORM`;
- la exportación del diagnóstico, que ya no bloquea la UI;
- el ETag del auto-update, guardado junto al tag que identifica (`launcherReleaseETag` + `launcherReleaseTag`).

Se repartió un build de prueba 1.0.15g sin firmar (SHA-256 `9CDF5C13…0986392`) y los testers confirmaron que el lag se fue.

**Para publicar el arreglo**
- **Repetir la revisión adversarial del cambio de la fase 1.** Se cortó y no terminó. Ahora el cambio es el commit `9057192` (`git show 9057192`); el script lo revisaba con `git diff` sin commitear, así que hay que apuntarlo a ese commit. Script: `C:\Users\jsalas\.claude\projects\C--Users-jsalas-Downloads-github-AoE3-Mod-Launcher\7cbbec67-e108-42c8-972f-af8e9b608f8f\workflows\scripts\review-lag-fix-diff-wf_ae98830c-b49.js`.
- **Etiquetar el oficial por encima de `v1.0.15g`** (p. ej. `v1.0.15h`). Con la misma etiqueta, las copias de prueba no se actualizan a él. Las notas van en `releases/vX.Y.Z.md`, más una entrada en `announcements.json` (ver `docs/BUILDING.md`).
- **`build-release.ps1` no parsea en Windows PowerShell 5.1.** El archivo no tiene BOM y la línea ~324 lleva un guion largo dentro de una cadena. Leído como cp1252, uno de sus bytes es U+201D, que PowerShell toma como comilla. Arreglo: guardarlo con BOM o cambiar el guion. En la sesión se usó una copia temporal con BOM.
- **El certificado `CN=Gorgorito` del `.csproj` no está en `Cert:\CurrentUser\My` de este PC**, así que los builds locales salen sin firmar.

**Rendimiento (fase 2), de mayor a menor**
1. **Sondeos en el hilo de la UI** con Multijugador abierto, aun minimizado (B4, C5-C7, E9):
   - Radmin (registro, procesos, NICs) cada 3 s;
   - el tick del lobby cada 2.5 s (NICs y perfil de AoE3);
   - in-game cada 1 s;
   - `RadminAssistantService.ProbeAsync`, que parsea todos los `service*.log` cada 3 s.

   Arreglo: pasarlos a un snapshot en segundo plano con guarda in-flight (el patrón `MaybeRefreshPowerState` de `RadminVpnService`), cachear `FindInstallation` y resolver el nombre in-game una vez por sala.
2. **Abrir o crear sala** (C1, C3, C4, C11). En el bundle tardaba 16 s, en buena parte amplificados por el bucle ya arreglado.
   - Medir cada paso de `OpenLobbyWindow` (`DiagnosticLog.Time` + `PerfCounters`).
   - Mover `MaybeReportRadminIp`, `MaybeReportInGameName`, `KickConnectionPing` y `UpdateLobbyPing` a un post en Background después de `Show`.
   - Declarar `UseLayoutRounding`, `TextOptions` y `WindowChrome` en `LobbyWindow.xaml`. Hoy se aplican en Loaded y fuerzan otro measure.
   - Arranque: hay una operación de 4.7 s. Postear la primera sonda de Radmin y `MaybeAutoOpenAssistant` en ApplicationIdle.
3. **Con una sala abierta**, plegar la actividad y no refrescar detrás la lista de salas ni las celdas de edad. (C8)
4. **Listas** (B5, B8):
   - reconstruirlas solo si cambia lo que muestran (la comunidad cada 60 s, los jugadores en cada frame de presencia);
   - construir solo las filas que caben, no 12;
   - actualizar los tics de la página quieta (ping, "actualizado", edades) en el sitio y con menos frecuencia.
5. **`RevealText`.** Hoy clona cada línea cortada en cada resize, y duplicaba el costo de maquetar 12 filas en el test de regresión. Arreglo: early-out con una clave de layout y coalescer `SizeChanged`/`Loaded`. (B3)
6. **`MeasureOne` mide en modo Ideal y el texto se dibuja en Display**, así que banderas y elipsis se deciden con unos px de error. Arreglo: darle el `TextFormattingMode` como hace `MiddlePathText`. Afecta a todo `RevealText`. (A2)
7. **Coalescer `RefreshFromSession`**, que corre dos veces seguidas al crear sala, y no reconstruir la insignia del chip de cuenta si no cambió. (C9)
8. **Menores:**
   - `ApplyActivityLayout` mide elementos ya en el árbol (B6);
   - `QueueActivityLayout` no tiene banda muerta ni fusible (B7);
   - falta `ConfigureAwait(false)` en `ModHashService` y en `LobbyWebSocket.SendRawAsync` (C13, E1);
   - el costo propio del diagnóstico (B10, B11).

**Errores reales (no son de lag)**
- **Comprobación de actualización al arrancar.** Si falla o pasa de 6 s, se entrega como "nada nuevo", y la sesión entera queda sin píldora y con multijugador abierto. Arreglo: rechazar los handovers fallidos para que `MainWindow` pregunte por su cuenta, y registrar "could not check". (D3)
- **"+ Crear sala" queda deshabilitado tras un rato sin conexión.** `SetOfflineMode` lo deshabilita y su rama online nunca lo vuelve a habilitar; lo mismo pasa con `SignInButton`.
- **Socket de sala** (E3-E8, E11):
  - la pestaña se engancha al socket nuevo después de `Start()` y puede perder el primer `room_state`;
  - un 4006/4404 tardío de la sala anterior llama `StopReconnect()`, que aborta, sobre el socket de la sala nueva; se alcanza creando sala desde dentro de otra;
  - la salida, y a qué socket pertenece cada cierre, no dejan rastro en el log;
  - si el servidor cierra la sala durante Lobby, queda una sala "zombi" en pantalla;
  - `set_radmin_ip` se marca como enviado antes de abrir el socket y no se reenvía tras reconectar;
  - sigue reconectando después del propio `/leave`;
  - una excepción en un handler corta la reconexión para siempre.

  Arreglo base: guardar cada handler con `ReferenceEquals(sender, _session.RoomSocket)`.
- **Taunts en Windows N, sin Media Player.** Cada taunt crea un `MediaPlayer` que falla. Arreglo: un latch por sesión con una sola línea de log, y caer al blip del chat. (E10, C14)
- **Varias copias sueltas del launcher** comparten config, clave Run, `wol-launcher://` y el mutex. La vieja arranca con Windows y se adueña. Faltan mitigaciones. (D5)
- **Cosmético:** `Launcher self-update: nothing newer than '—'` imprime el tag guardado (`NoUpdate(lastInstalledTag)`), no el del binario.

**Detalle completo.** La evidencia, las ubicaciones y el arreglo propuesto de cada hallazgo (A1-A8, B1-B11, C1-C14, D1-D6, E1-E11) están en `C:\Users\jsalas\.claude\projects\C--Users-jsalas-Downloads-github-AoE3-Mod-Launcher\7cbbec67-e108-42c8-972f-af8e9b608f8f\workflows\wf_482be0ed-765.json`, en el campo `result[].investigation.findings`. Es parte de la transcripción y puede borrarse cuando se limpie; lo esencial está arriba.

**Why:** el usuario pidió dejarlo apuntado para otra sesión. La fase 1 ya resolvió el lag de los testers, y esto se dejó fuera a propósito.

**How to apply:** ante "fase 2", "lo que faltó" o "publicar el arreglo", partir de esta lista y verificar cada punto contra el código actual antes de proponer, porque las líneas se mueven.

Related: [[aoe3-launcher-mantener-claude-md]], [[no-commits-user-pushes]], [[launcher-scope-conservador]], [[red-mantenedor-umbrella]].
