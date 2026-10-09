---
name: launcher-lag-phase2-backlog
description: "AoE3-Mod-Launcher, lag: fase 1 publicada (v1.0.15h); fase 2 hecha en el árbol sin commitear (2026-10-07), con lo aplazado y las pruebas manuales que faltan"
metadata:
  node_type: memory
  type: project
  originSessionId: 7cbbec67-e108-42c8-972f-af8e9b608f8f
  modified: 2026-10-06T20:37:46.496Z
---

**Fase 2 (2026-10-07): hecha en el árbol, SIN commitear.** Se siguió el plan de 40 pasos (lotes A-F). Build Release con 0 errores y suite completa en verde: 3924 tests. Todo lo de las listas de abajo está hecho, salvo lo que se nombra como pendiente.
- **Lote A (próxima versión):** `Save` best-effort en el ETag; petición de release extraída (`BuildLatestReleaseRequest`); `NON-CONVERGING` por segundo; los `.ps1` en ASCII (arregla PowerShell 5.1); "nothing newer than" con el tag del binario; entrada `v1015h` en `announcements.json`.
- **Lote B (errores reales):** botones al volver online; handlers del socket de sala solo para el socket actual (`RoomSocketEvents.Route`); enganche síncrono; IP de Radmin confirmada por el servidor; comprobación fallida ≠ "nada nuevo" y re-check al volver online; píldora y bloqueo vuelven online; 4404/4006 y 4002/4004 en Lobby inactivo sueltan la sala con aviso; cerrar en partida sin bloquear 10 s; taunts sin WMP caen al blip; `wol-launcher://` sigue la copia instalada; rastro de la salida y del socket; sin reconexión tras el propio `/leave`; eventos aislados por suscriptor.
- **Lote C (diagnóstico y sondeos):** `UI OP` nombra el timer, su intervalo y el GC; ticks cronometrados; `main.Show()` cronometrado; sonda del asistente de Radmin fuera del hilo de UI; chip de conexión sin recorrer NICs; nombre in-game una vez por sala; `FindInstallation` cacheado, banner a ApplicationIdle, sin sondeo minimizado; un solo recorrido de NICs para el tráfico.
- **Lote D (sala y arranque):** `OpenLobbyWindow` cronometrado y pintado antes de `Show`; ruta de crear sala (host, aviso de downgrade, abre desde cualquier subpestaña); pasadas de sesión coalescidas e insignia del chip solo si cambia; sondas de entrada tras el primer frame; ventana de sala vestida antes del primer layout; auto-abrir del asistente a ApplicationIdle y solo visible.
- **Lote E (listas y texto):** `MeasureOne` en el modo del bloque; `RevealText`/`InlineFlagFit` con ancho cacheado (el revelado se conserva por referencia); 6 filas de partidas en vez de 12; celdas de ping en el sitio; tick de celdas solo en Salas; la tarjeta de comunidad y el panel de jugadores solo se repintan si cambia lo que muestran (`ActivityPaintKey`, `PlayersPanelKey`); el panel de jugadores se repinta al cambiar cualquiera de los dos tamaños de escalera; lista y celdas en pausa detrás de una ventana de sala (`RoomsPageRefresh`); aviso vacío medido al ancho interior de la tarjeta.
- **Lote F:** fingerprint fuera del hilo de UI; contexto del storm solo para la línea que se escribe.

**Aplazado a propósito (no hacer sin datos):** 24b/25/26b (snapshot de Radmin en segundo plano: esperar un bundle con un tick de Radmin ≥150 ms), B7 (refutado), NEW-POLL-05 (leer los logs de Radmin desde la cola), NEW-OPEN-04/05, NEW-SOCK-3 (salir desde la bandeja durante el envío: primero una prueba manual), `ConfigureAwait` en todo `LobbyApiClient`, reconstrucción de la subpestaña Ranking, carrera de hello en el backend, ráfaga del momento de lanzar, dedupe del avatar, zombi en fase Starting, plegar la actividad con sala abierta (C8), guard de versión en el Run key (D5 M2). Los IDs A1, A3-A8, B1, B2, B9, C2, C10, C12, D1, D2, D4, D6 y E2 no estaban en esta nota y no se cubrieron.

**Pruebas manuales pendientes (ningún test las cubre):** cerrar durante una partida → cierra al instante; crear sala desde otra sala → la nueva recibe `room_state`; desconectar y reconectar la red → vuelven "+ Crear sala" e "Iniciar sesión"; arrancar sin red → "could not check" y re-check al volver; el servidor cierra una sala inactiva → aviso y la ventana se cierra; `--minimized` con Multijugador como primera pestaña → el asistente no aparece en la bandeja; smoke-launch (no se pudo: el launcher instalado estaba abierto).

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
