# Diseño · El ELO de un jugador en posicionamiento solo lo ve él

> **Estado:** **implementado** en los dos repositorios (este launcher y el backend
> `wol-launcher-lobby-node`), pendiente de publicar. **Orden de despliegue: primero la versión del
> launcher, después el backend** — un launcher viejo lee un número omitido como «sin clasificar»,
> que es honesto pero pierde la etiqueta de posicionamiento.
>
> Lo que cambió respecto del diseño al implementarlo:
> - `LadderStanding` y `EloSnapshot` se quedan como `double` (ver §4, «DTOs y refresco»).
> - El memo de `/stats/community` es de 60 s, no de 5 min.
> - `GET /matches/history/:id` no leía `req.userId`; ahora sí.
> - La trampa de `RatingFor` (elegía la escalera de equipos solo si llegaba el número) se cerró
>   con `RatingForLadder`, que elige por la insignia si llegó cualquier dato de equipos.
> - Los textos están en `Localization/Strings.Placement.cs`.
> - La regla y sus trampas, para quien toque el código: `.claude/rules/multiplayer.md` §ELO V3,
>   viñeta «A RATING STILL BEING PLACED IS SHOWN TO ITS OWNER ONLY».
>
> La regla de puntuación en sí (Glicko, posicionamiento, insignias) está en [ELO.md](ELO.md); las
> reglas de implementación del multijugador, en `.claude/rules/multiplayer.md` §ELO V3.

---

## 1. La regla

> Mientras un jugador **P** está en posicionamiento en una escalera **L** — es decir,
> `isInPlacement(partidas_L, L)`: al menos 1 partida puntuada y menos de **10 en 1v1** o **5 en
> equipos** (`PLACEMENT_REQUIRED`, `src/elo/placement.ts` del backend) —, **ningún payload que
> pueda leer otro jugador lleva el ELO de P en L, su RD, ni nada que se derive de ellos**: ELO
> antes/después, delta, probabilidad de victoria ni promedio de equipo.
>
> P sigue viendo su propio número con «?». **Decide el estado actual**: cuando P termina el
> posicionamiento, todo vuelve a verse, también los números de sus partidas anteriores.

Precisiones:

- **Es por escalera.** P puede estar ya posicionado en 1v1 (se le ve el número) y en
  posicionamiento en equipos (no se le ve el de equipos), o al revés.
- **Cero partidas no es posicionamiento.** Quien no jugó ninguna partida puntuada sigue leyéndose
  «Sin clasificar», como hoy (`isInPlacement` exige más de cero; en el launcher,
  `RatingDisplay.IsUnrated`).
- **La insignia no cambia.** Quien posiciona ya lleva Descubrimiento (`ladder_rank = 0`) y la
  insignia sale del puesto, nunca del número (`RankAges`). No filtra nada.
- **Por qué «estado actual» y no «estado al jugar»**: es una sola pregunta por jugador y por
  respuesta (cuántas partidas puntuadas tiene hoy en esa escalera), que el servidor ya calcula en
  casi todos los payloads (`effectiveRatings` devuelve `games_played`). La alternativa — que los
  números de las 10 primeras partidas no se vean nunca — exigiría el índice de posicionamiento de
  cada participante en cada partida.

### Qué ven los demás en lugar del número

| Forma | Texto (es) | Texto (en) | Dónde |
|---|---|---|---|
| Larga | **En posicionamiento 3/10** | **In placement 3/10** | plantel de la sala, filas por equipos, tarjeta de resultado, Historial |
| Corta | **3/10** (atenuado) | **3/10** | celda de host en la lista de salas, panel de jugadores |
| Sin total | **En posicionamiento** | **In placement** | si el total requerido no llegó (servidor viejo y sin `/stats/community`) |
| Tooltip (siempre) | Su ELO se mostrará cuando termine el posicionamiento. | Their rating shows once they finish placement. | en las tres formas |

La Clasificación ya tiene su propia etiqueta y solo pierde el número: la fila de otro jugador pasa de
«1571? Posicionamiento 6/10» a «Posicionamiento 6/10» (`MpPlacementProgress`, que ya existe).

---

## 2. Dónde aparece hoy el ELO ajeno en el launcher

Inventario completo, verificado leyendo el código. Las líneas son aproximadas y se refieren a
`WarsOfLibertyLauncher/` (MT = `Controls/MultiplayerTab.xaml.cs`).

| # | Superficie | Dónde | Fuente en el cable | Señal de posicionamiento hoy | Qué se ve hoy de otro jugador |
|---|---|---|---|---|---|
| 1 | Lista de salas, celda del host | MT `BuildRatingText` (~3840), usado en `BuildRoomCard` (~17941) | `GET /lobbies` → `host.rating`, `rating_team`, `in_placement(_team)` | flag del servidor | «1571? ELO» |
| 2 | Lista de salas, subtítulo cuando la columna Host se cae por ancho | MT ~17809-17819 | idem (siempre el de 1v1) | **la ignora** | «Nombre 1571 ELO», sin «?» (ya contradice a #1) |
| 3 | Plantel de sala 1v1/casual (y espectadores de salas por equipos) | MT `MemberDetailLine` (~3453), refrescado por `RefreshRosterLiveCells` | `room_state.members` (camelCase) y `member_joined` (snake_case) | derivada localmente; los flags del servidor se descartan porque `RoomMemberEntry` no los guarda | «1490? ELO · 38 ms · 1v1 · …» |
| 4 | Filas de sala por equipos | `Controls/MultiplayerTab.RoomTeams.cs` `TeamRowDetail` (~293) ← `TeamRatingOf` (~328) | idem, `ratingTeam` | derivada localmente | «1534? · …» |
| 5 | «Prom. ELO» de cada equipo | RoomTeams `TeamAverage` (~343), en la cabecera (~172) | calculado en el launcher | **ninguna**: promedia también a quien posiciona | en un 2v2, promedio × 2 − compañero = ELO oculto |
| 6 | Probabilidad por equipos | RoomTeams `BuildTeamOddsCard` (~582) | `room_odds` y `room_state.odds` | ninguna | «Equipo 1: 57 %» |
| 7 | Probabilidad 1v1 | MT `BuildOneVOneOdds` (~3544) | idem | ninguna | «Tienes un 62 %…»: con tu ELO se despeja el del rival |
| 8 | Panel de jugadores | MT `RenderPlayersPanel` (~17078; número ~17213, vía `BuildRatingText`) | `presence` / `global_state` → `onlineUsers[]` | flag del servidor | «1571? ELO» |
| 9 | Clasificación, filas en posicionamiento | `Controls/MultiplayerTab.Ranking.cs` `BuildPlacementRow` (579; número 608) | `GET /stats/community` → `leaderboard_placement(_team)` | contadores + `IsViewer` | «1571? Posicionamiento 6/10» |
| 10 | Historial, delta de cada rival o compañero | MT `BuildHistoryPlayerRow` (~14043; delta ~14129) | `GET /matches/history/:id` → `participants[].rating_before/after` | **ninguna por participante** | «+242» |
| 11 | Tarjeta de resultado, celda RIVAL | `Controls/MatchResultCard.cs` (418-422); modelo en MT `BuildOutcome` (~22431) | `POST /matches` → `rating_changes[]`; `match_reported` → `participants[]` | está en el cable (`placement_played`/`placement_required`) pero `BuildOutcome` solo copia los del propio jugador | «Pedro 1571» |

### Verificado sin cambios

- **Filas clasificadas** de la Clasificación y la **tarjeta «Clasificación»** de Salas: solo listan
  gente que ya terminó el posicionamiento.
- **Destacados** (página y tira de Salas): el servidor ya excluye a quien posiciona
  (`stats/highlights.ts`: la subida cuenta solo partidas tras el posicionamiento, ~216-222; la
  sorpresa se salta partidas con alguien posicionando, ~341).
- **Chat global e insignias**: solo edad y puesto. **Guía de rangos**: solo el número propio.
- **Overlays de partida, cuenta atrás por equipos, toasts** (invitación, `lobby_created`): sin ELO.
- **Torneos**: solo el número de siembra (el servidor siembra por `rating − 2·rd`; es un orden, no un
  número).
- **Perfil**: hoy solo existe el propio (`OpenProfileWindow()` no recibe usuario).
- **Chip de cuenta**: es el propio.
- **Discord**: ni el anuncio de sala ni el post mensual llevan el ELO de quien posiciona.

### Fugas solo de cable (no se dibujan, pero viajan)

- `GET /matches/elo/:id` es público y devuelve el ELO de cualquiera.
- `recent_matches[]` de `/stats/community` y `GET /matches` llevan `participants[].rating_*`.

---

## 3. Contrato del backend — la capa que de verdad oculta

El launcher solo puede ocultar lo que dibuja; mientras el número viaje, cualquiera con un proxy lo
lee. **El servidor deja de enviarlo.**

### Codificación: omitir la clave

Se **omite la clave** (en JS, `undefined`, que `JSON.stringify` descarta). **Nunca `null`** en un
campo que un launcher viejo declare como `double` no anulable — `PlacementRow.Rating/Rd`,
`LadderStanding.Rating/Rd`, `EloSnapshot.Rating` —: System.Text.Json lanzaría una excepción y se
perdería la respuesta entera (el mismo fallo que tumbó el Historial en 1.0.13l, ver el comentario de
`rated` en `GET /matches/history`). Donde el launcher ya tolera `null` se indica.

Se mantienen siempre `games_played*`, `in_placement*` y los contadores de posicionamiento: son lo
que permite escribir «3/10».

### Payloads

| Payload | Archivo (backend) | Audiencia | Cambio |
|---|---|---|---|
| `GET /lobbies` → `host` | `src/lobbies/rest.ts` (~196) | pública, `Cache-Control: public, max-age=5` | sin `rating`/`rd` si `in_placement`; sin `rating_team`/`rd_team` si `in_placement_team`. El DTO (`LobbyHost`) es anulable: `null` también vale |
| `presence` / `global_state` → `onlineUsers[]` | `src/global/GlobalChatRoom.ts` (`onlineUsers`, ~728) | broadcast a todos los sockets | ídem (`rating`/`rd`, `ratingTeam`/`rdTeam`). El launcher lee solo valores numéricos: `null` es seguro |
| `room_state.members` | `src/lobbies/LobbyRoom.ts` (~578) | `send` a un solo socket | **redacción por destinatario**: los miembros distintos del destinatario que posicionan van sin `rating`/`rd` (o `ratingTeam`/`rdTeam`) |
| `member_joined` | `LobbyRoom.ts` (~592) | broadcast que excluye al que entra | sin `rating`/`rd` del que entra si posiciona (él no recibe este frame) |
| `room_odds` y `room_state.odds` | `src/lobbies/roomTeams.ts` (`roomOdds`) | broadcast | si **algún jugador** de la sala posiciona en la escalera de la sala: `teams: null, players: null` y el campo **nuevo** `hidden_reason: "placement"` |
| `GET /stats/community` → `leaderboard_placement(_team)` | `src/stats/rest.ts` (`placementLadder`, 697-698) | pública + memo de 60 s, sin quien-mira | sin `rating`/`rd` para todos; el propio jugador saca su número de `/matches/elo` |
| `GET /matches/elo/:id` | `src/matches/rest.ts` (~2437) | pública; ya calcula `self = req.userId === userId` | si `!self` y posiciona en L: sin `ladders.L.rating`/`rd` ni los campos planos de L (`rating`/`rd` para 1v1, `rating_team`/`rd_team` para equipos). `rating_peak`/`rating_low` ya son null durante el posicionamiento |
| `GET /matches/history/:id` | `matches/rest.ts` (~2322) | pública; antes no leía `req.userId`, **ahora sí** (como `/matches/elo`) | participantes distintos de quien consulta que posicionan **hoy** en la escalera de esa fila: sin `rating_before`/`rating_after`. La fila del sujeto, igual si sujeto ≠ quien consulta. **Nuevos** `participants[].in_placement`, `placement_played`, `placement_required` (estado actual) |
| `recent_matches[]` y `GET /matches` | `stats/rest.ts`, `matches/browse.ts` | públicas | misma redacción de participantes (no se dibuja, pero viaja) |
| `match_reported` | `matches/rest.ts` (`rooms.publish`, ~2134) | todos los sockets de la sala | **por destinatario**: sin `rating_before/after` ni `rd_before/after` de los otros participantes que **sigan** posicionando tras la partida (`placement_played < placement_required`). Se mantienen sus `placement_*` |
| respuesta de `POST /matches` → `rating_changes[]` | ídem | el host | igual que `match_reported`, con el host como destinatario |

**Sin cambio:** `match_rated` (cada jugador recibe solo sus números), `matches_changed` (ids), el
post mensual y los anuncios de Discord.

`match_reported` y `room_state` hoy se arman una vez para todos; la redacción por destinatario
implica construir el frame por socket (en la sala hay como mucho 8, así que el costo es trivial).

### Test del backend

Un `THE ONE THAT MATTERS: a placing player's rating never reaches another player`, que recorra
`GET /lobbies`, `onlineUsers`, `room_state`, `member_joined`, `roomOdds`, `placementLadder`,
`/matches/elo` sin `self`, `/matches/history` y `match_reported` con un jugador posicionando y otro
que consulta, y verifique que no aparece ninguna de las claves. Y el caso espejo: el propio jugador
sigue recibiendo su número en `/matches/elo` con `self` y en su `match_rated`.

---

## 4. Segunda capa en el launcher

Oculta con lo que ya llega, así que funciona también contra un backend viejo.

### Una sola puerta: `Services/Multiplayer/RatingDisplay.cs`

Hoy `LookOf(rd, gamesPlayed, inPlacement)` devuelve `Number`, `Provisional` o `Unrated`, sin saber
quién mira. Se añade:

```csharp
public enum Look { Number, Provisional, Unrated, HiddenPlacement }

/// isViewer: el jugador dibujado es quien mira.
public static Look LookFor(bool isViewer, double? rd, int? gamesPlayed, bool? inPlacement)
    => IsUnrated(rd, gamesPlayed) ? Look.Unrated
     : inPlacement == true ? (isViewer ? Look.Provisional : Look.HiddenPlacement)
     : Look.Number;

/// «En posicionamiento 3/10», o sin total cuando required no se conoce (null o 0).
public static string PlacementLabel(int? played, int? required, bool shortForm);
```

`LookOf` queda como `LookFor(isViewer: true, …)`. **Las 11 superficies pasan por `LookFor`**;
hoy la Clasificación (`MultiplayerTab.Ranking.cs` ~606) y el Perfil (`MultiplayerTab.ProfileModes.cs`
~550) dibujan su «?» a mano, y así es como #2 acabó sin «?».

### La señal «este jugador posiciona»

En este orden:

1. El flag del servidor (`in_placement` / `inPlacement` y sus versiones `_team`).
2. Si falta: `PlacementView.InPlacement(gamesPlayed, CommunityStatsView.PlacementRequiredFor(...))`.
3. Si tampoco hay total (no cargó `/stats/community`): sin señal → se dibuja como hoy.

Cambios para que la señal llegue: `RoomMemberEntry` gana `InPlacement` e `InPlacementTeam`
(hoy `WsRoomMemberFlags` los trae y se pierden al copiar en `HandleRoomState`), y `member_joined`
lee `in_placement(_team)` (hoy no lo lee).

### Por superficie

| # | Cambio |
|---|---|
| 1, 8 | `BuildRatingText` recibe `isViewer`; con `HiddenPlacement` dibuja la forma corta «3/10» atenuada con el tooltip |
| 2 | usa la misma regla que #1 (cierra el bug del «?» ausente) |
| 3, 4 | `MemberDetailLine` y `TeamRowDetail` escriben la forma larga en lugar de «1490? ELO»; la fila propia no cambia |
| 5 | el promedio de un equipo con un jugador que posiciona se oculta para todos («Prom. ELO —» con tooltip) |
| 6, 7 | si algún jugador de la sala posiciona en esa escalera, o llega `hidden_reason`, la tarjeta se reemplaza por una línea |
| 9 | la fila de otro jugador pierde el número y el «?»; queda «Posicionamiento 6/10» y los segmentos grises |
| 10 | con `participants[].in_placement == true` y participante ≠ quien mira: sin delta, forma larga. Sin el campo (servidor viejo), como hoy |
| 11 | `BuildOutcome` copia `PlacementPlayed`/`PlacementRequired` del rival; si sigue posicionando: «Pedro · 4/10», con «Pedro · en posicionamiento 4/10» en el tooltip (la tarjeta vive en la columna de 352 px de la sala y la forma larga se cortaba justo en la cuenta) |

### DTOs y refresco

- `PlacementRow.Rating`/`Rd` pasan a `double?`, para tolerar la clave omitida. **Corregido al
  implementar:** `LadderStanding` y `EloSnapshot` se quedan como `double`, porque el launcher solo
  pide **su propia** posición (`LoadStandingAsync` → `GetEloAsync(CurrentUser.Id)`, el único
  llamador), y la propia nunca se omite. Si algún día se pide la de otro jugador, esos campos tienen
  que pasar a `double?` en el mismo cambio.
- `BuildRoomsSignature` (MT ~14632) y `PlayersPanelKey` (MT ~17063) incluyen el estado de
  posicionamiento: hoy solo un cambio de puesto o de insignia repinta la fila, y la etiqueta «3/10»
  quedaría vieja.

### Strings nuevos (`Localization/Strings.Placement.cs`, es-419 con tuteo)

| Clave | en | es |
|---|---|---|
| `MpEloHiddenPlacement` | In placement {0}/{1} | En posicionamiento {0}/{1} |
| `MpEloHiddenPlacementNoCount` | In placement | En posicionamiento |
| `MpEloHiddenPlacementTip` | Their rating shows once they finish placement. | Su ELO se mostrará cuando termine el posicionamiento. |
| `MpOddsHiddenPlacement` | Win chances show once nobody in the room is in placement. | La probabilidad de victoria aparece cuando nadie en la sala está en posicionamiento. |
| `MpTeamAvgHiddenPlacementTip` | Hidden while a player on this team is in placement. | Oculto mientras un jugador de este equipo esté en posicionamiento. |

La forma corta reutiliza `MpPlacementProgressShort` («{0}/{1}») y la Clasificación,
`MpPlacementProgress`.

### Tests

- `RatingDisplayTests`: la matriz completa de `LookFor` (propio/ajeno × sin partidas / posicionando /
  posicionado / servidor viejo sin flag). Hoy `LookOf` no tiene test directo.
- `THE_ONE_THAT_MATTERS_AnotherPlayersPlacementRatingNeverReachesTheScreen`, sobre las filas reales
  (#1, #3, #4, #8, #9, #11) y los derivados (#5-#7): con un jugador posicionando que no es quien
  mira, ningún `TextBlock` contiene su número.
- El caso espejo: la fila propia sigue mostrando «1490?».
- Ajustar los que fijan el comportamiento actual: `EloPreviewTests` (~300-371: la probabilidad 1v1
  y la sala por equipos con Bai Yu Feng posicionando), `RankBadgeContextTests` (~80-100),
  `RoomBadgesTests` (~146-149), `RankBadgeChoiceTests` (~190-192).

---

## 5. Compatibilidad y orden de despliegue

| | Backend actual | Backend con este contrato |
|---|---|---|
| **Launcher actual** | lo de hoy | no ve números ajenos de quien posiciona (null u omitido); solo las filas de posicionamiento de la Clasificación leerían «0?», y la pestaña Multijugador está tapada por `LauncherUpdateGate` en un launcher desactualizado |
| **Launcher con la segunda capa** | oculta con los flags que ya llegan; Historial (#10) igual que hoy hasta tener `participants[].in_placement` | comportamiento completo |

**Orden: primero el launcher, después el backend.** Así nunca hay una versión publicada del launcher
que dibuje «0?», y el `LauncherUpdateGate` (cada versión es obligatoria para el multijugador) cierra
la ventana en la que conviven un launcher viejo y el backend nuevo.

---

## 6. Al implementar, actualizar en el mismo cambio

- `docs/ELO.md`, **las dos mitades**: «Placement» / «Posicionamiento» dice hoy que el número con «?»
  se ve «everywhere it appears»; pasa a «solo tú lo ves; los demás ven En posicionamiento 3/10», y
  la probabilidad deja de mostrarse con alguien posicionando. Ídem en los detalles técnicos.
- `.claude/rules/multiplayer.md` §ELO V3: una viñeta nueva con la regla, la única puerta
  (`LookFor`) y la codificación «omitir, nunca null en un double».
- Las escenas de `--demo-elo` (`EloDemoData`): `room1v1` y `roomteams` con un rival posicionando, y
  `ranking` con filas de otros sin número.

## 7. Fuera de alcance

- `LauncherConfig.ShowMyElo` («mostrar mi ELO a otros») está guardado y nadie lo lee. El mismo
  mecanismo, con `hidden_reason: "private"`, serviría para implementarlo más adelante.
- El chip de cuenta propio no muestra «?» mientras posicionas; es coherente añadirlo, pero no es
  parte de esta regla.
- La siembra de torneos sigue usando el ELO de todos (incluido quien posiciona); es un orden, no un
  número visible.
