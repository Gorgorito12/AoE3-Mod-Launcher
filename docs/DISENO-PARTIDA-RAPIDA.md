# Diseño · Partida rápida (matchmaking)

> **Estado:** **Fase 0 implementada** («unirse o crear» sobre las salas existentes, sin cambios en
> el backend), pendiente de publicar, con una **interfaz provisional** hecha con estilos y tokens
> existentes porque no hay handoff de diseño. Las Fases 1 y 2 quedan diseñadas para cuando la
> comunidad las justifique.
>
> Lo que cambió respecto del diseño al implementar la Fase 0 (el resto está como se describe):
> - **El título de la sala no es un texto traducido**: es un valor de red que leen todos, así que
>   es «{mod} · Quick match 1v1» para todos (`RoomTitleProposal.QuickMatch`, mismo criterio que
>   «Ranked»). §6.7 se consulta por ese marcador.
> - **Radmin**: aviso no bloqueante (toast con «Abrir el asistente»), sin «Buscar igual», porque la
>   búsqueda nunca se detiene; se mira la etapa base (`< LoggedIn`), no `InAoE3Network`, para no
>   leer los registros de Radmin en cada búsqueda.
> - **Rival encontrado**: un toast `PreferDesktop` «¡Rival encontrado!» con «Ver sala», no una
>   tarjeta con avatar e insignia. Sin chip «PARTIDA RÁPIDA» ni nota del host en la sala: el título
>   ya lo dice. Sin línea de actividad («N buscando ahora»): necesita el servidor.
> - Si la lista no trae la sala propia, el que espera **se queda** (la lista está desactualizada y
>   moverse con ella podría cruzar a dos que esperan).
> - Mientras se busca en un mod, el launcher no anuncia como «sala nueva» otras salas de ese mod:
>   unirse a mano a una más nueva desharía la convergencia.
> - Las reglas y sus trampas, para quien toque el código: `.claude/rules/multiplayer.md`, sección
>   «QUICK MATCH (phase 0)».
>
> Se apoya en el diseño de [ELO oculto en posicionamiento](DISENO-ELO-POSICIONAMIENTO.md): ninguna
> pantalla de este documento muestra el ELO de un jugador que está posicionando.

---

## 1. Qué es

El jugador presiona **«Buscar partida»** y el launcher lo empareja y arma la sala solo, en lugar de
crear una sala y esperar a ver si alguien la encuentra. Está pensado para **partidas competitivas
(puntuadas)**: toda partida rápida es competitiva.

---

## 2. El riesgo que manda: la comunidad es pequeña

Datos medidos (`.claude/rules/multiplayer.md`):

- **119 salas en 30 días**, unas 4 por día; la ventana pico de 3 horas reunió unas **25 salas en el
  mes** (§PEAK HOURS, ~5810).
- **40 partidas puntuadas en 30 días** cuando se midió (§Profile card, ~2924).
- **~14 mods** en el catálogo × **3 formatos** × **versiones** (la huella del mod tiene que ser
  idéntica para jugar juntos).
- Backend: una VM de 1 núcleo y 1 GB, `MAX_CONCURRENT_USERS` 60.

Con ese ritmo, la probabilidad de que **otra persona** busque el mismo mod, formato y versión en los
~10 minutos que alguien está dispuesto a esperar es de **un dígito porcentual**, incluso en hora pico.
Casi toda «clave de cola» tiene 0 o 1 personas. Una cola clásica — «espera aquí hasta que aparezca
alguien» — fallaría casi siempre, y una función que casi siempre falla se percibe rota.

El problema real no es el algoritmo de emparejamiento: es **que dos personas coincidan en el
tiempo**. De ahí los principios.

## 3. Principios (valen para todas las fases)

1. **No fragmentar.** La búsqueda aprovecha las salas competitivas que ya existen; nunca crea una
   población paralela.
2. **Esperar en segundo plano.** La búsqueda sobrevive a cambiar de pestaña, minimizar y pasar a la
   bandeja (el socket global ya vive en bandeja), con aviso de escritorio al encontrar rival.
3. **Convocar.** Buscar tiene que hacer ruido: los `lobby_created` ya llegan como toast de escritorio
   (con «Unirse») a quien tiene el mod instalado, y el embed de Discord ya existe.
4. **Mostrar actividad real.** Cuánta gente busca ahora y en qué horas hay más gente
   (`CommunityStatsView.PeakWindow` ya lo calcula).
5. **1v1 primero.** 2v2 y 3v3 necesitan 4-6 personas simultáneas; hoy eso no ocurre por azar.

## 4. Lo que la partida rápida no cambia

- **Radmin VPN y AoE3 siguen siendo manuales.** Cada launcher lanza el juego con **su propia** IP de
  Radmin (`OverrideAddress`, en `MultiplayerTab.BuildMultiplayerLaunchArgs`); todos tienen que estar
  en la red comunitaria fija **«Age of Empires III: The Asian Dynasties»**
  (`RadminVpnService.AoE3TadNetworkName`), y el launcher no puede unirse a una red por el jugador.
  Dentro de AoE3, el host crea la partida LAN y el resto entra por Multijugador → LAN; la línea de
  comandos no puede abrir esa pantalla (`multiplayer.md`, `doMPSetup`).
- **Radmin nunca bloquea.** Crear y unirse a salas no se condicionan a Radmin («Radmin-off
  messaging is INFORMATIONAL, never a blocker», `multiplayer.md` ~335), porque la detección tiene
  falsos negativos. Buscar partida sigue la misma regla.
- **La huella decide quién puede jugar con quién.** El servidor compara `mod_combined_hash` al unirse
  (`POST /lobbies/:id/join`, `mod_mismatch`); la partida rápida solo empareja huellas idénticas
  (`ModHashService`, copia activa del mod).
- **El host** es quien reporta el resultado (`POST /matches`) y, por convención, quien crea la
  partida LAN. Las reglas competitivas de hoy siguen intactas: confirmar «Record Game», retener al
  host hasta enviar el resultado, ascenso de host a mitad de partida, abandono 1v1 después de 5 min.

---

## 5. Flujo de UI (común a las fases)

Requiere un handoff de diseño para lo visual: la barra superior de Salas no tiene ancho libre
(necesita ~1045 px y una ventana de 900 ofrece 880; lo fija
`DialogXamlTests.TheRoomsTopBarFitsAtTheNarrowestWindow`), así que la entrada no puede ser un botón
más en esa barra. Este documento fija el comportamiento y los textos.

1. **Entrada.** Una tarjeta «Partida rápida» en Multijugador → Salas, con: el mod (el activo, igual
   que crear sala), el formato `1v1 | 2v2 | 3v3` (en el MVP solo 1v1 habilitado), el botón «Buscar
   partida» y una línea de actividad («1 buscando ahora · más gente entre 18:00 y 21:00»).
2. **Verificaciones previas, en orden; cada una con su acción**:
   1. Sesión de Discord → abre el inicio de sesión.
   2. Launcher al día (`LauncherUpdateGate`) → ofrece actualizar.
   3. Mod instalado y huella de la copia activa calculada (`ModHashService`).
   4. No estar en otra sala ni en partida (`JoinPrecheck`) → «Sal de la sala para buscar partida».
   5. (Fase 1) Sin penalización activa → dice hasta cuándo.
   6. **Radmin, informativo**: si `RadminAssistantService` no está en `InAoE3Network`, un aviso con
      «Abrir asistente» y «Buscar igual».
3. **Buscando.** Una píldora persistente en la barra de título, como la de actualización: «Buscando
   1v1 · WoL · 02:13 · Cancelar», y el mismo texto en el tooltip de la bandeja. Se puede cambiar de
   pestaña, minimizar o ir a la bandeja sin cortar la búsqueda. Lanzar un juego la cancela.
4. **Rival encontrado.** Tarjeta con el rival: avatar, nombre, insignia y ELO — o «En
   posicionamiento 3/10» si está posicionando —, formato y «Competitiva». En primer plano,
   `MpAlertOverlay.ShowContent`; fuera de primer plano, `MainWindow.ShowAppToast` con
   `ToastOptions.PreferDesktop` y sonido. Nunca durante una partida (ver §11).
5. **Sala.** Antes de entrar: `BringToForeground()` + pestaña Multijugador + `ShowRooms()`, y después
   `JoinByLobbyIdAsync` → `JoinLobbyCoreAsync`, con todas sus compuertas (cambio de mod activo,
   huella, ya-en-otra-sala, launcher viejo). El orden importa: la ventana de sala solo se abre desde
   `RenderRoomsTab`. La sala lleva el chip «PARTIDA RÁPIDA» y la nota «Hospeda X: crea la partida en
   AoE3 → Multijugador → LAN; los demás se unen desde ahí». Desde aquí, el flujo competitivo de
   siempre: Listo → sala llena y todos listos → `MaybeAutoStartOnAllReady` del host (que pregunta
   `ConfirmRecordGameAsync`) → `start_game` → cuenta atrás del servidor (5 s) → juego → reporte del
   host → tarjeta de resultado.
6. **Después.** La tarjeta de resultado gana el botón «Buscar otra», que repite la búsqueda con los
   mismos parámetros.

### Textos propuestos (es-419 con tuteo / en)

| Uso | es | en |
|---|---|---|
| Título de la tarjeta | Partida rápida | Quick match |
| Botón | Buscar partida | Find match |
| Actividad | {0} buscando ahora · más gente entre {1} y {2} | {0} searching now · busiest between {1} and {2} |
| Píldora | Buscando {0} · {1} · {2} | Searching {0} · {1} · {2} |
| Cancelar | Cancelar | Cancel |
| Aviso de Radmin | Radmin VPN no parece estar en la red de AoE3. Puedes buscar igual, pero para jugar necesitas esa red. | Radmin VPN doesn't seem to be on the AoE3 network. You can search anyway, but you need that network to play. |
| Botones del aviso | Abrir asistente · Buscar igual | Open assistant · Search anyway |
| Ya en una sala | Sal de la sala para buscar partida. | Leave your room to find a match. |
| Esperando en la sala (Fase 0) | Buscando rival… Tu sala está anunciada; te avisamos cuando alguien entre. | Looking for an opponent… Your room is announced; we'll let you know when someone joins. |
| Encontrado | ¡Rival encontrado! | Opponent found! |
| Nota del host | Hospeda {0}: crea la partida en AoE3 → Multijugador → LAN; los demás se unen desde ahí. | {0} hosts: create the game in AoE3 → Multiplayer → LAN; the others join from there. |
| Título de la sala creada | {mod} · Quick match 1v1 (igual en los dos idiomas: es un valor de red) | {mod} · Quick match 1v1 |
| Tras la partida | Buscar otra | Find another |
| Fin por tiempo (Fase 0) | Dejamos de buscar después de 30 minutos. | We stopped searching after 30 minutes. |
| Otra versión | Hay {0} jugadores buscando con otra versión de {1}. | {0} players are searching with another version of {1}. |
| Aceptar / rechazar (Fase 1) | Aceptar · Rechazar | Accept · Decline |

---

## 6. Fase 0 — MVP: «unirse o crear» sobre las salas existentes

**Cero cambios en el backend.** Una versión del launcher.

### 6.1 Datos

`GET /lobbies` (`LobbyApiClient.ListLobbiesAsync`) ya trae todo: `mod_id`, `mod_combined_hash` (hoy
se deserializa y nadie lo lee), `competitive`, `max_players`, `spectator_slots`, `current_players`,
`spectators_present`, `status`, `is_private`, `tournament_match_id`, `created_at` y el host con su
ELO (oculto si posiciona).

### 6.2 Algoritmo

Lógica pura en un servicio nuevo, `Services/Multiplayer/QuickMatchPlanner.cs`, testeable sin WPF:

- **Candidata**: `competitive`, `status == "open"`, sin contraseña, sin torneo, mismo `mod_id` **y**
  misma `mod_combined_hash`, asientos de juego del formato (`RoomFormats.PlayingSeats`), al menos un
  asiento de jugador libre (`RoomFormats.SeatsOf`), y host ≠ yo.
- **Orden**: primero por cercanía de ELO cuando los dos números se ven (si el host posiciona, la
  distancia es desconocida y va detrás), después por antigüedad. **No se excluye nadie por ELO**:
  cualquiera puede entrar desde la lista a una sala competitiva, así que un filtro aquí no daría
  control real, y con 0-1 candidatas lo único que haría es dejar a alguien sin partida.
- **Hay candidata** → `JoinLobbyCoreAsync`. Si falla porque se llenó (`lobby_full`) o desapareció
  (`not_found`), se vuelve a planificar.
- **No hay** → `CreateLobbyAsync` (competitiva, 2 asientos de juego, título «{mod} · Quick match 1v1»)
  + `EnterHostedLobbyAsync`. La sala **se anuncia sola**: `lobby_created` a cada launcher con el mod
  (toast de escritorio con «Unirse»), aparece en la lista y sale el embed de Discord. El jugador
  espera dentro, con el aviso «Buscando rival…».

### 6.3 Convergencia: dos personas que buscan a la vez acaban juntas

Si A y B presionan el botón a la vez, los dos ven la lista vacía y cada uno crea su sala. Para que no queden dos
salas solitarias:

- Mientras espera **solo** en su sala rápida, el launcher vuelve a mirar `GET /lobbies` a los ~3 s de
  crearla, después cada 20 s, y cada vez que llega un `lobby_created`.
- Si hay una sala **compatible y más antigua** que la suya, sale de la propia y entra en esa. «Más
  antigua» es un **orden total**: `created_at` y, a igualdad, `id` (las fechas tienen resolución de
  segundos).
- Solo se mueve el dueño de la sala más nueva; el dueño de la más antigua se queda. Por eso converge:
  todos los que esperan solos acaban en la mínima.
- Carreras benignas: si al moverse la sala destino ya se llenó, se vuelve a planificar; si alguien
  entra en la sala que se abandona justo antes de que se cierre, hereda el host (lo que ya hace el
  servidor) y puede esperar o salir.

### 6.4 Cancelar y terminar

- **Cancelar** = salir de la sala (`LeaveCurrentLobbyAsync`); estando solo, el servidor la cierra.
- **Fin por tiempo**: a los 30 minutos sin rival, sale de la sala y avisa («Dejamos de buscar…»).
  Evita salas abandonadas por alguien que se fue del PC.
- **Rival encontrado** = llega un `member_joined` a la sala rápida: sonido (ya existe), toast si la
  ventana no está al frente, y la búsqueda termina. Desde ahí, el flujo normal de la sala.

### 6.5 Coste y límites

- Llamadas: un `GET /lobbies` al empezar, uno a los 3 s y uno cada 20 s mientras espera solo; un
  `POST /lobbies` o un `join` por intento. Muy por debajo de los límites del backend
  (`src/middleware/rateLimit.ts`: crear y unirse 20/min y 200/día por IP; listar 120/min).
- Archivos (cuando se implemente): `QuickMatchPlanner.cs` + `QuickMatchPlannerTests`, un parcial
  `Controls/MultiplayerTab.QuickMatch.cs` (tarjeta, verificaciones previas, bucle de convergencia,
  enganche a `lobby_created`), la píldora en `MainWindow`, los textos en `Localization`, un caso en
  `DialogXamlTests` para la tarjeta, y una sección en `.claude/rules/multiplayer.md`.

### 6.6 Lo que la Fase 0 no da

- Aceptar/rechazar antes de entrar.
- Penalización por irse antes de empezar (igual que hoy en cualquier sala).
- Control de ELO sobre quien entra a tu sala desde la lista.

### 6.7 Cómo medirla

Las salas rápidas se reconocen por el título en `lobbies`: termina en «· Quick match 1v1» en todos
los idiomas (`RoomTitleProposal.IsQuickMatch`), y `started_at` / `roster_at_start` dicen si llegaron
a jugarse. Basta una consulta del operador; no hace falta telemetría nueva:

```sql
SELECT date(created_at) AS dia,
       COUNT(*) AS salas,
       SUM(started_at IS NOT NULL) AS jugadas
  FROM lobbies
 WHERE title LIKE '%Quick match 1v1'
 GROUP BY dia ORDER BY dia;
```

---

## 7. Fase 1 — Cola en el servidor (1v1)

Cuando la Fase 0 muestre que hay coincidencias (ver §10), la cola del servidor añade aceptar/
rechazar, emparejamiento por ELO y penalizaciones. Todo lo de abajo es **contrato propuesto** para
`wol-launcher-lobby-node`.

### 7.1 Modelo

- Un singleton en memoria, `Matchmaker`, en `AppContext` (igual que `globalChat`). Los tickets están
  atados al socket global: cerrar el socket es salir de la cola, con 15 s de gracia para una
  reconexión. Un reinicio del servidor vacía la cola; el launcher, al reconectar, vuelve a entrar
  solo y lo dice.
- **Sin temporizador periódico** (regla de la casa: el servidor no tiene barridos periódicos;
  `sweepOrphanLobbies` corre una vez al arrancar). El emparejamiento corre **por eventos**: alta,
  baja, resolución de una propuesta y un `mm_ping` cada 15 s de quien busca. Los plazos son un
  `setTimeout(...).unref()` de un disparo por propuesta y por sala, el mismo patrón que
  `LobbyRoom.probeLiveness`.

### 7.2 Emparejamiento

- **Clave**: (`mod_id`, `mod_combined_hash`, formato). Solo competitivas (y `isRankedMod`).
- **Ventana de ELO**, calculada perezosamente: `150 + 100 · ⌊espera / 30 s⌋`, **sin límite a los
  2 minutos** (con esta comunidad, cualquier rival es mejor que ninguno). Quien posiciona entra con
  la ventana máxima desde el principio, porque su número todavía es inestable; su número nunca se
  envía a nadie.
- **Par elegido**: el de menor |ΔELO| que quepa en la ventana de los dos; a igualdad, el más
  antiguo.
- **Host**: quien declaró Radmin en `InAoE3Network` (`radmin_ready`), luego quien no marcó «prefiero
  no hospedar», luego quien lleva más tiempo en cola.

### 7.3 Aceptar y armar la sala

1. `mm_found` a cada jugador, con 20 s para `mm_accept`.
2. Todos aceptan → el servidor llama a
   `createLobby(ctx, hostId, { title, modId, modCombinedHash, maxPlayers: 2, askedCompetitive: true,
   announce: false, matchmakingMatchId })` — la única puerta para crear salas, que ya usan los
   torneos — y envía `mm_room_ready`.
3. Cada launcher entra por `JoinByLobbyIdAsync`, **el host incluido**, exactamente como en torneos
   (`OpenTournamentMatchAsync` hace eso en sus dos ramas). El servidor lo admite: `ALREADY_IN_LOBBY_SQL`
   excluye la propia sala.
4. Alguien rechaza o deja vencer el plazo → la propuesta se cancela; quien no aceptó recibe una falta
   (§9) y los demás vuelven a la cola **conservando su antigüedad**.
5. Si alguien no entra a la sala en 90 s, la sala se cierra, falta para el ausente y vuelta a la
   cola para el resto.

La sala lleva `lobbies.matchmaking_match_id` (migración) y `POST /lobbies/:id/join` aplica la misma
lista blanca que los torneos aplican con `isEntrantMember`: solo los emparejados
(`not_match_participant`). `POST /matches` marca `matches.origin = 'matchmaking'` para estadísticas.

### 7.4 Contrato

Todo por el socket global (`/global/ws`), salvo lo indicado.

| Dirección | Frame / ruta | Campos |
|---|---|---|
| C→S | `hello` | + `features: ["matchmaking"]`. El launcher ya manda `features` (`LobbyWebSocket.HelloFeatures`); `GlobalChatRoom` hoy no lo lee y tendría que guardarlo por socket, como hace `LobbyRoom` |
| C→S | `mm_join` | `mod_id`, `mod_combined_hash`, `format` (`"1v1"`), `radmin_ready`, `host_ok` |
| C→S | `mm_leave` | — |
| C→S | `mm_ping` | — (cada 15 s mientras busca) |
| C→S | `mm_accept` / `mm_decline` | `match_id` |
| S→C | `mm_queued` | `ticket_id`, `mod_id`, `format`, `queued_at`, `searching` (misma clave) |
| S→C | `mm_status` | `elapsed_s`, `searching`, `searching_total`, `searching_other_version`, `window` (solo la propia) |
| S→C | `mm_found` | `match_id`, `format`, `accept_deadline_ms`, `you_are_host`, `players[]`: `user_id`, `display_name`, `avatar_url`, `ladder_rank`, `badge_mode`, `rating`/`rd` (**omitidos si posiciona**), `games_played`, `in_placement` |
| S→C | `mm_accept_progress` | `match_id`, `accepted`, `total` |
| S→C | `mm_cancelled` | `match_id?`, `reason` (`declined`, `timeout`, `player_left`, `room_failed`, `restart`), `requeued` |
| S→C | `mm_room_ready` | `match_id`, `lobby_id`, `host_user_id` |
| S→C | `mm_error` | `code`: `not_supported`, `rate_limited`, `penalty` (+ `until`), `in_room`, `in_game`, `mod_not_ranked`, `launcher_too_old`, `banned`, `bad_request` |
| REST | `GET /matchmaking/status` | pública, `Cache-Control: public, max-age=10`: `{ enabled, queues: [{ mod_id, format, searching }] }`. **404 = servidor sin cola → el launcher se queda en la Fase 0** |
| REST | `GET /lobbies` | + `matchmaking: true` en la sala: el launcher muestra el chip y oculta «Unirse» a quien no fue emparejado |

**Base de datos (migraciones):** `matchmaking_matches` (id, mod, huella, formato, estado, host,
`lobby_id`, fechas, motivo de cancelación), `matchmaking_match_players` (la lista blanca, que tiene
que sobrevivir a un reinicio), `matchmaking_penalties` (§9), `lobbies.matchmaking_match_id`,
`matches.origin`.

### 7.5 Compatibilidad

- **Launcher nuevo + servidor viejo**: `GET /matchmaking/status` da 404 (o `mm_join` recibe
  `unknown_type`) → el launcher usa la Fase 0. Nada se rompe.
- **Launcher viejo + servidor nuevo**: nunca envía `mm_join`; la sala de partida rápida aparece en su
  lista como una sala más y, si intenta entrar, recibe `not_match_participant` (el mismo trato que hoy
  recibe ante una sala de torneo). El `LauncherUpdateGate` hace que esto sea raro.

### 7.6 Carga

Decenas de tickets como mucho; emparejar es O(n²) por clave con n de un dígito. Memoria y CPU
despreciables frente al presupuesto de la VM.

---

## 8. Fase 2 — Equipos y extras

- **2v2 y 3v3 en solitario**: con 4 o 6 tickets de la misma clave, el reparto se elige por fuerza
  bruta (3 repartos posibles para 4 jugadores, 10 para 6) minimizando |promedio₁ − promedio₂|. Los
  lados quedan **bloqueados** como en los torneos (`teams_locked` en `room_state`; el servidor
  sienta a cada uno y congela `teams_at_start`).
- **Grupos**: dos o tres amigos buscan juntos como un bloque.
- **Presencia `searching`**: el panel de jugadores muestra «Buscando 1v1 · WoL» junto al nombre, con
  un «Buscar también» — prueba social, la mejor herramienta contra las colas vacías.
- **Aviso opcional en Discord**: «X busca partida 1v1 de WoL», con enlace para unirse.
- **Host por conectividad medida** y **evitar emparejar cuentas con el mismo hash de IP**.

---

## 9. Anti-abuso

| Riesgo | Fase 0 | Fase 1 |
|---|---|---|
| Rechazar o no responder | no aplica | 1 falta |
| Aceptar y salir antes de empezar | igual que hoy | 2 faltas |
| Irse durante la partida | reglas competitivas actuales (abandono 1v1 tras 5 min, fundación por el servidor) | ídem |
| Spam de búsquedas | límites de crear/unirse del backend | `mm_join` 10/min por usuario; un ticket por usuario |
| Buscar en sala, en partida, baneado o con launcher viejo | `JoinPrecheck` / `LauncherUpdateGate` | además, el servidor lo rechaza (`in_room`, `in_game`, `banned`, `requireLauncherVersion`) |
| Farmeo entre amigos | anti-farm del servidor (descuenta victorias repetidas sobre el mismo rival) y `new_account_short` | ídem; Fase 2 evita el mismo hash de IP |
| Filtrar el ELO de quien posiciona | la tarjeta y la sala siguen la regla de [C1](DISENO-ELO-POSICIONAMIENTO.md) | `mm_found` omite su número; `mm_status` solo da la ventana propia |

**Faltas (Fase 1)**: tabla `matchmaking_penalties` (`user_id`, `strikes`, `last_strike_at`,
`locked_until`), con decaimiento de 1 falta cada 6 h calculado perezosamente. Bloqueos: 1.ª falta,
solo aviso; 2.ª, 2 minutos; 3.ª, 10 minutos; desde la 4.ª, 30 minutos.

En una comunidad tan chica **se repetirán los mismos emparejamientos**; es esperable y no es un
fallo. El anti-farm ya existe precisamente para eso.

---

## 10. Riesgos y cómo se mitigan

1. **Colas vacías** (el principal): Fase 0 sobre salas, espera en segundo plano, convocatoria por
   `lobby_created` y Discord, actividad visible y solo 1v1.
2. **Versiones distintas del mismo mod**: dos jugadores de WoL con huellas distintas nunca se
   emparejan. La Fase 0 puede contar las salas compatibles en todo salvo la huella y decir «Hay N
   jugadores buscando con otra versión de WoL», que empuja a actualizar.
3. **Alguien busca y se va del PC**: la sala rápida espera con su dueño ausente. Mitigan el fin por
   tiempo (30 min) y el «Listo» de siempre: nadie arranca una partida sin que el host esté listo.
4. **Jugadores sin Radmin en la red**: se forma la partida y no pueden verse en AoE3. El aviso previo
   es informativo, y la sala ya muestra a cada par en gris («Esperando VPN») mientras no anuncia su IP
   (`set_radmin_ip` / `member_net`).
5. **Ruido en Discord**: cada sala rápida de la Fase 0 publica el embed, como cualquier sala; al
   ritmo actual no es un problema. Las salas de la Fase 1 nacen con `announce: false`.
6. **Reinicio del servidor (Fase 1)**: la cola se pierde; el launcher vuelve a entrar solo. Las
   salas ya creadas las cierran los mecanismos de siempre (barrido al arrancar, cierre sin sockets).

## 11. Huecos actuales a cerrar antes o junto con la Fase 0

Encontrados al mapear el flujo; existen hoy, con o sin partida rápida:

- **`_isGameRunning` solo lo activa el PLAY del panel** (`MainWindow.StartGameMonitor`, llamado
  únicamente desde `ExecutePlay`). El lanzamiento multijugador (`MainWindow` ~497-560) no lo activa,
  así que `ShowAppToast` **no suprime** los toasts de escritorio durante una partida multijugador,
  aunque su comentario dice que sí. La partida rápida necesita esa supresión.
- **El «Unirse» del toast de invitación** llama a `JoinByLobbyIdAsync` sin `BringToForeground()` ni
  cambiar de pestaña, a diferencia del toast de sala nueva; con el launcher en la bandeja, la ventana
  de sala puede no aparecer porque solo se abre desde `RenderRoomsTab`.

## 12. Cuándo pasar de fase

- **A la Fase 1** cuando haya coincidencias que una cola aprovecharía. Señales medibles con la
  consulta de §6.7: al menos ~2 salas rápidas por día durante dos semanas, o salas rápidas
  compatibles abiertas **a la vez** (dos personas esperando cada una en su sala sin encontrarse), o
  que la comunidad pida aceptar/rechazar y penalizaciones.
- **A la Fase 2** cuando haya, con regularidad, 4 o más personas buscando el mismo mod a la vez.
