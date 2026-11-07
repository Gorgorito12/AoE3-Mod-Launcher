# Rediseño con la base de la Biblioteca unificada, segunda tanda (80–103)

Todo lo que falta por implementar del rediseño con la base de la Biblioteca unificada (68a): torneos y salas (80–88), la clasificación con edades (93), los ajustes posteriores a la tanda 95–102 (ya en main) y los diálogos pequeños, menús y popovers (103). Las reglas comunes están en el paquete anterior (`docs/design_base_launcher/BASE.md`); aquí va cada pantalla: qué cambia, en qué archivos del repo y qué necesita código nuevo o confirmación.

**Los prototipos.** Abre cada `.dc.html` en un navegador, con `support.js` y las carpetas `mods/` y `design_handoff_workshop/` al lado. Son interactivos; la transparencia (y en algunos el tamaño) se cambia en Tweaks. Son referencia de diseño: recréalos en WPF/XAML con los estilos y controles que ya existen.

**Los textos.** Los textos en español que no salen de `Localization/Strings.cs` son propuestas (el archivo supera el límite de lectura). Si ya hay una clave equivalente, usa la suya. Todo texto nuevo va en español (es-419, tuteo) e inglés.

**Una regla de la base que apareció en esta tanda.** Texto en una línea recortado con puntos suspensivos (`TextTrimming`) lleva interlineado mínimo 1,4; con 1,2–1,3 el recorte se come los trazos bajos de g, p, j, y.

## Orden sugerido

1. **103 Diálogos pequeños, menús y popovers**: primero, porque todas las demás pantallas usan sus confirmaciones y menús.
2. **Salas:** 88 Crear sala · 87 Sala · 86 Salas (con la tarjeta de partida rápida).
3. **Torneos:** 81 Nuevo torneo · 82 Inscribirse · 83 Mis equipos · 85 Sala de torneo · 80 Torneos.
4. **93 Clasificación.**
5. **Ajustes de 95–102** (ya implementadas; solo lo que cambió después).

## Cambios de código y servidor

Todo lo que no es solo XAML. El launcher debe funcionar con y sin el dato nuevo del servidor: si falta, se oculta lo que lo necesita.

### Servidor (API de multijugador)

- **`GET /teams/mine` debe devolver también las invitaciones que envió el capitán** (pendientes, con fecha y a quién). Hoy solo devuelve las recibidas. Sin esto, Mis equipos (83) no puede mostrar «Invitado · hace 2 h» ni cancelar una invitación.
- **Sala de torneo (85): `TournamentDetail` tiene que poder resolverse desde la sala.** La sala solo trae `tournament_match_id`; para la cabecera (torneo, ronda, quién contra quién) y «Qué está en juego» (contra quién sigue el ganador) hace falta el torneo y el cruce siguiente. O el servidor añade `tournament_id` y `next_match` a la sala, o el launcher reutiliza el `TournamentDetail` que ya carga Torneos (válido mientras la pestaña lo tenga en memoria).
- **Ver una partida (99d): el organizador debe poder escribir en el chat de un cruce.** Hoy el servidor bloquea con tres puertas (`in_game`, cruce, plazas de observador). Hasta que exista la excepción para `tournament.organizer_id`, el chat va en solo lectura (99a–99c).
- **Partida rápida (96): ¿la sala de partida rápida admite `POST /lobbies/{id}/invite`?** Si no, el botón «Invitar» se oculta mientras buscas.
- **Sala (87) y Sala de torneo (85), por confirmar en el servidor:** si en una sala casual el anfitrión puede empezar sin que todos estén listos (el diseño supone que no); a quién pasa la sala cuando el anfitrión se va (el diseño supone que al siguiente que entró); si desde Salas se puede entrar como observador a una partida en marcha; qué pasa al abandonar un cruce en marcha (el diseño lo cuenta como derrota) y si, tras una partida que no cuenta, la sala sigue abierta para repetirla.
- **Estadísticas (98): `GET /stats/community`, `/stats/civs`, `/stats/matchups`, `/stats/decks` filtrados por mod y modo (1v1 / equipos).** La cabecera los da por hecho. Si el servidor no filtra por modo, el selector de modo se oculta.
- **Estadísticas (98): `UnratedTopReason` solo se nombra si es `no_decided_result`**; para otras causas, la fila dice «No contaron» sin motivo.

### Launcher (C#)

- **`BaseDialog` (103):** un control nuevo del que heredan todos los diálogos pequeños: barra de 40 con `GlassTheme`, icono de tono (`Tone`: Info · Caution · Destructive · Done · None), titular, texto, zona de contenido (`Detail` en Consolas, campo, lista, progreso, casilla), pie con `LeftButton` opcional, Cancelar y principal. Esc y ✕ = Cancelar; Enter = principal salvo destructivo. Heredan: ThemedConfirmDialog, BackgroundConsentDialog, CompatibilityLayerDialog, RemoveFromCollectionDialog, CrossUserAccountDialog, StartupUpdateWindow, UserDataRestoreDialog, PasswordPromptDialog, RenameRoomDialog, CreateTeamDialog, PlayerPickerDialog.
- **`BaseMenu` (103):** estilo único para `ContextMenu` y `Popup` con `GlassTheme.Popover` (Sólida .95 + 18 · Media .74 + 28 · Máxima .6 + 32), filas de 32, separador, destructivo al final. Lo usan el ⋯ del mod (ModsBrowser, ActionPanel), `LibraryMenu`, el popover de conexión y el de cuenta (`ChromePopups`). Voltear si no cabe, nunca recortar.
- **`MpAlertOverlay` (103m):** toma las medidas del diálogo pequeño (tarjeta 440, icono de tono, botones de 32 con nowrap), pierde el borde doble `MpDivider` y el glifo ⚠ grande.
- **Los nueve `MessageBox.Show` desaparecen:** InstallFolderDialog (1), LauncherSettingsDialog (mod.json inválido, relanzar), ModPropertiesDialog (4), PublishDialog (1). Pasan a ThemedConfirmDialog con el texto que ya tienen y el detalle técnico en `Detail`.
- **Crear sala (88):** guardar el último tipo de sala usado (`Settings.LastRoomKind`) para abrir el diálogo con él. Hoy no se guarda.
- **Salas (86):** la tarjeta de partida rápida sustituye a `QuickMatchButton`; «Revisar mi copia» compara `mod_combined_hash` con la copia activa (como `OtherVersionCount`); la lista se ordena por lo que puedes hacer (Unirse · Ver sala · Ver partida · Instalar mod · Revisar mi copia · Llena).
- **Sala (87):** la lista de jugadores crece con las plazas (sin desplazamiento propio; los huecos de más se resumen en una línea); `EloPreview` en competitiva; en equipos `MpTeamAvg` (oculta si alguien posiciona), `MpWinProbTeams`, bando elegible y motivos de `StartGate` en el pie.
- **Torneos (80):** cuadro de 32+ con desplazamiento en dos direcciones, rondas fijas arriba, salto por ronda, «Ir a mi partido» y zoom; Participantes dice en qué ronda cayó cada uno (sale de `BracketLayout`, sin dato nuevo).
- **Nuevo torneo (81):** plazas hasta 32 y 64; «Abrir la inscripción al crearlo» cambia el botón a «Crear y abrir inscripción» / «Crear como borrador».
- **Inscribirse (82):** una sola ventana sustituye al selector de jugadores + inscripción (dos ventanas seguidas hoy).
- **Clasificación (93):** podio y agrupación por edad con lo que ya trae `PlayerStanding`; «Tu posición» calcula distancia en ELO con los vecinos de la misma lista. Sin dato nuevo.
- **Partida rápida (96), ya en main:** el pie lleva `WindowSpinner` mientras busca, también con Radmin sin encender.
- **Perfil (95), ya en main:** sin «Cargar más partidas» (`/matches/history` no pagina). `MinDecidedForPercent` = 5.
- **Generar parche (101), ya en main:** `GeneratePatchAsync` acepta `CancellationToken`; el botón «Cancelar» mientras genera lo usa.

### Textos nuevos (Strings.cs, es + en)

103: Seguir en la sala · Abandonar · Instalar más tarde · Mostrar · Invitar a {0} · No volver a preguntar.  
99: Solo lectura · Esperando a {0} · Terminada hace {0} min · Escribe como organizador… · Ves el chat pero no puedes escribir: el servidor no deja entrar a quien no juega. · Si {0} no entra a tiempo, decide la partida desde aquí o desde el cuadro.  
101: Comparando {0}… · Escribiendo el parche incremental… · Escribiendo el parche acumulado… · {0} de {1} archivos · Mostrar en la carpeta · No se pudo generar el parche.  
86–88, 80–85: los de cada sección; los del prototipo son propuestas si no existe clave equivalente.

## Pruebas para todas

1366×768, 1920×1080 y 2560×1440; las tres transparencias (Sólida, Media, Máxima); efectos reducidos. Los diálogos también en su tamaño mínimo (Perfil 780×480, Partida rápida 900×600).

## Dudas acumuladas (confirmar antes de implementar)

- `PlayerStanding.MinDecidedForPercent` es 5 (confirmado en main). `SignInFailure.Classify` detecta el enlace caducado por `device_flow_timeout` / `expired_token` o un 404 durante la espera. `DeltaPatchService.GeneratePatchAsync` ya acepta `CancellationToken`. `GET /matches/history` no pagina: no hay «Cargar más».
- `/stats/community`: ¿acepta mod y modo? La cabecera de 98 los da por hecho.
- Inicio de sesión: el enlace caducado llega como una `LobbyApiException` más; hace falta mirar su `Code` (p. ej. `expired_token`) para mostrar 97d.
- Partida rápida: ¿la sala de partida rápida admite «Invitar»? Si no, el botón se oculta mientras buscas.
- Generar parche: «Cancelar» mientras genera necesita un `CancellationToken` en `DeltaPatchService.GeneratePatchAsync`.
- Empaquetar traducción: los textos `DlgPackager*` del prototipo son propios; si ya existen, usa los de `Strings.cs`.
- Ver una partida: 99d (chat funcional para el organizador) solo se activa cuando el servidor exima al organizador de las tres puertas (`in_game`, cruce y plazas de observador).
- Sala (87): si en una sala casual el anfitrión puede empezar sin que todos estén listos (el diseño supone que no); a quién pasa la sala cuando el anfitrión se va; si desde Salas se puede entrar como observador; si «Revancha» ya invita al rival a la sala nueva.
- Sala de torneo (85): qué pasa al abandonar un cruce en marcha y si, tras una partida que no cuenta, la sala sigue abierta para repetirla. `OffersFindAnother` no debe ofrecer «Buscar otra» en un cruce.
- Crear sala (88): si el launcher guarda el último tipo de sala usado; si no, hay que empezar a guardarlo.
- Mis equipos (83): `teams/mine` hoy no devuelve las invitaciones que envió el capitán; hace falta ese dato.

---

## 103 · Diálogos pequeños, menús y popovers

**Prototipo:** `Diálogos pequeños v2.dc.html` · **Reglas:** `BASE.md` (sección Componentes) y `Base de diseño.dc.html` (8b)  
**Archivos del repo:** ThemedConfirmDialog, BackgroundConsentDialog, CompatibilityLayerDialog, RemoveFromCollectionDialog, CrossUserAccountDialog, StartupUpdateWindow, UserDataRestoreDialog, PasswordPromptDialog, RenameRoomDialog, CreateTeamDialog, PlayerPickerDialog (.xaml/.cs), Controls/MpAlertOverlay.cs, Controls/ChromePopups.cs, ModsBrowser y ActionPanel (menú ⋯), LibraryMenu, MainWindow (popovers)

### Qué cambia

Un solo chasis, `BaseDialog`, para todo diálogo pequeño: barra de 40, icono de tono de 32 junto al titular (azul info · ámbar cuidado · rojo destructivo · verde hecho; sin icono cuando no es un aviso, como el progreso de actualización o un campo), titular Source Serif 19/1,3, texto 13/1,55, pie con Cancelar sólido a la izquierda del principal. Ancho 440 sin campo · 480 con campo o detalle · 520 con lista; alto al contenido. El tono lo decide el botón principal: #c84a4a si no se deshace; #2f7fe0 si es del launcher; color del mod si es de un mod (y la barra se tiñe al 22 %). Nunca dos botones de color; un tercero va a la izquierda del pie.

- **103a–103c** ThemedConfirmDialog en sus tres tonos; sustituye a los 9 MessageBox. 103c es RemoveFromCollectionDialog: diálogo de un mod con botón rojo fijo.
- **103d** BackgroundConsentDialog con la casilla «No volver a preguntar» en el cuerpo.
- **103e** CompatibilityLayerDialog: «Instalar más tarde» a la izquierda del pie.
- **103f** CrossUserAccountDialog: un solo botón, la ruta en la caja de detalle.
- **103g** StartupUpdateWindow: sin icono, barra de progreso, un botón secundario; si falla, tono cuidado con «Reintentar».
- **103h** UserDataRestoreDialog: lista de copias (fecha y tamaño en Consolas), la elegida con relleno azul; sin emoji.
- **103i–103k** PasswordPrompt («Mostrar» dentro del campo), RenameRoom (contador 22/40 dentro del campo), CreateTeam (nombre y etiqueta en un campo doble; global, azul).
- **103l** PlayerPickerDialog a 520: filas de 44 con casillas; los que no se pueden invitar siguen a la vista, atenuados y con el motivo; el botón dice a cuántos invita.
- **103m** MpAlertOverlay: tarjeta sobre velo con las medidas del diálogo pequeño, sin barra. Se usa cuando la pregunta nace de lo que ves en esa ventana (abandonar, salir, quitar a alguien).
- **103n–103p** menú ⋯ del mod (Jugar · Ajustes · Abrir carpeta · Buscar actualización · separador · Quitar de la colección en rojo), popover de conexión (cabecera con punto y latencia; Reconectar · Cambiar servidor… · lista de servidores con punto y ms) y popover de cuenta (avatar, nombre, ELO; Ver mi perfil · Ajustes · Mis equipos · separador · Cerrar sesión). Siguen la transparencia del usuario con más difuminado; nunca color de mod.

### Variantes

103a info · 103b cuidado con detalle · 103c destructivo de mod · 103d casilla · 103e tres botones · 103f un botón · 103g progreso · 103h lista · 103i–103k campo · 103l selector · 103m tarjeta sobre velo · 103n–103p menús.

---

## 88 · Crear sala

**Prototipo:** `Crear sala v2.dc.html`  
**Archivos del repo:** CreateLobbyDialog.xaml(.cs), Services/Multiplayer/RoomFormats.cs

### Qué cambia

Gemelo de Nuevo torneo (81): ventana del mod elegido (franja con su imagen, barra teñida), tarjetas de cristal en dos columnas y, abajo, Cancelar y el único botón principal. Los campos son los del código: mod, copia (solo con 2 o más; `CopyHintReadonly` se oculta), nombre con dos sugerencias (`Suggest1`, `Suggest2`), contraseña con `PasswordRevealButton`, tipo en una sola fila (Casual · 1v1 · 2v2 · 3v3, sustituye a `CompetitiveCheck` + `CompetitiveFormat`), plazas, observadores (restan plazas, `ObserversHint/ObserversCost`) y el estado de Radmin (`MpCreateDialogRadminInfo/Warning`). La huella del mod se ve junto a la copia (`MpCreateDialogFingerprintOk/Loading/Failed` con el hash en tooltip); sin ella el botón dice que no se puede crear. Errores en el pie (`MpCreateDialogTitleTooShort`, `MpCreateDialogNoFingerprint`) y «Creando…» mientras trabaja. El formato competitivo fija las plazas como `RoomFormats.Resolve`. El diálogo abre con el último tipo que usaste.

---

## 87 · Sala

**Prototipo:** `Sala v2.dc.html` (estructura y medidas: `SALA-DE-TORNEO-85.md`)  
**Archivos del repo:** LobbyWindow.xaml(.cs), Controls/MultiplayerTab.EloPreview.cs, Services/Multiplayer/StartGate

### Qué cambia

La misma LobbyWindow de 85 sin el modo torneo. Lo propio de una sala normal ya existe en el código: `OnRenameRoom` (lápiz), `OnInvitePlayers`, `CopyRoomCode` (el chip del código y la fila de hueco libre comparten función), `OnCopyRoomCode` y `OnAnnounceRoom` (la caja «Estás solo»), `OnRecordHelp` y «No volver a mostrar», `OnRejoinGame` («Abrir el juego») e `OnInGameCancel`. Se arreglan los defectos de 15b: la lista de jugadores no tiene desplazamiento propio y crece con las plazas (los huecos de más se resumen en una línea), el estado de cada jugador va en su columna, el chat vacío no ocupa media ventana. En competitiva, «Para que cuente» y «Qué te juegas» (`EloPreview`) sustituyen al recordatorio de Record Game. En equipos: media de cada equipo (`MpTeamAvg`, oculta si alguien posiciona), probabilidad (`MpWinProbTeams`), bando elegible desde la sala y motivos de `StartGate` en el pie (`MpStartBlockedPlayers`, `MpStartBlockedUneven`). Con la partida en marcha el pie cambia y, si AoE3 se cierra, ofrece «Abrir el juego».

---

## 86 · Salas

**Prototipo:** `Salas v2.dc.html` (+ `PartidaRapidaTarjeta.dc.html`)  
**Archivos del repo:** Controls/MultiplayerTab.xaml (RoomsSectionHeader, QuickMatchButton), Services/Multiplayer/QuickMatchPlanner.cs, LobbySummary

### Qué cambia

Fondo neutro sin imagen de mod (la lista mezcla mods). La tarjeta de partida rápida sustituye a `QuickMatchButton` en la cabecera de la lista, con los estados de `PartidaRapidaTarjeta.dc.html`; es lo único con color de mod (el activo). Se mantiene lo de 56–62: un solo margen M y una separación G (12 por debajo de 1600, 16 por encima), al menos 4 filas a la vista, la actividad abajo (barra compacta o tarjetas si sobra alto), chat de 280/320 plegable a 44. Cada fila dice qué puedes hacer: Unirse, Ver sala, Ver partida, Instalar mod, Revisar mi copia (compara `mod_combined_hash` con la copia activa, como `OtherVersionCount`) o «Llena». La lista se ordena por lo que puedes hacer. Las filas salen de `LobbySummary`; el ping es el que ya muestra la lista. 86c–86e: carga, error y sin conexión. Torneos (80) debe tomar el mismo margen M de la barra de subpestañas.

---

## 81 · Nuevo torneo

**Prototipo:** `Nuevo torneo v2.dc.html` (+ `NuevoTorneo.dc.html`)  
**Archivos del repo:** CreateTournamentDialog.xaml(.cs)

### Qué cambia

Diálogo del mod: franja con su imagen, resplandor, barra teñida y su color en lo elegido y en «Crear torneo». De 470 a 560 de ancho (760 en dos columnas para portátil, menos de 650 de alto incluso en 3v3), en tres tarjetas: qué torneo (mod y nombre), cómo se juega (formato y, en equipos, cómo se forman) y quién entra (inscripción y plazas). Se mantiene lo del turno 8: contador y aviso del nombre en el campo, segmentados con una línea de consecuencia, cómo se forman los equipos solo en 2v2 y 3v3. Cambios: el mod se elige con su icono; plazas hasta 32 y 64; la cuenta de plazas en tres cifras (jugadores, rondas, salas); «Crear torneo» desactivado dice qué falta; «Abrir la inscripción al crearlo» cambia el botón a «Crear y abrir inscripción» o «Crear como borrador». Textos de cómo se forman los equipos: los de las notas de la v1.0.16e.

---

## 82 · Inscribirse

**Prototipo:** `Inscribirse v2.dc.html` (+ `InscribirseDialog.dc.html`)  
**Archivos del repo:** el selector de jugadores que hoy abre la inscripción (dos ventanas seguidas), TournamentDetail

### Qué cambia

Una sola ventana del mod del torneo que se adapta a cómo se forman los equipos. En 1v1 o «cada uno se apunta solo», una confirmación con lo que implica. Con equipo guardado: eliges el equipo y, en la misma ventana, quién juega. Si el capitán arma la alineación: nombre y compañeros entre los conectados, con los ya inscritos desactivados. Los equipos que no se pueden inscribir siguen a la vista con el motivo. El botón principal dice lo que hace («Inscribir a Los Andes», «Enviar solicitud») o el motivo por el que no puede, en lugar de apagarse.

---

## 83 · Mis equipos

**Prototipo:** `Mis equipos v2.dc.html` (+ `MisEquiposWindow.dc.html`)  
**Archivos del repo:** la ventana Mis equipos de hoy (520 px), teams/mine

### Qué cambia

Ventana global, neutros y azul de acción: invitaciones y equipos en la lista lateral, el elegido a la derecha con quién está conectado. Crear equipo e invitar en la misma ventana, a varios a la vez; quitar y salir piden confirmación, como disolver. Segunda vuelta (83h–83m): para qué formatos alcanza cada equipo, aceptar o rechazar desde el aviso, errores del servidor, «Invitando…», sin sesión y sin conexión.

### Requiere código

`teams/mine` debe devolver también las invitaciones que envió el capitán.

---

## 85 · Sala de torneo

**Prototipo:** `Sala de torneo v2.dc.html` · **Texto para pegar:** `SALA-DE-TORNEO-85.md`  
**Archivos del repo:** LobbyWindow.xaml(.cs) en modo torneo, TournamentDetail

### Qué cambia

La misma LobbyWindow en modo torneo con la base: franja del mod, barra teñida, paneles de cristal. En un cruce solo entran sus jugadores: sin Invitar, código, contraseña ni plazas libres; la cabecera dice torneo, ronda y quién contra quién; el chip dice «Torneo». El estado y el único botón principal van en el pie y cambian con cada paso (esperar, listos, cuenta atrás, en partida, resultado). «Para que cuente» explica que el resultado se lee de la grabación; «Qué está en juego» dice contra quién sigue el ganador (necesita el torneo y el cruce siguiente del `TournamentDetail` que ya carga Torneos; la sala solo trae `tournament_match_id`). En 3v3, bandos y alineaciones del cuadro. Salir antes de empezar no pide confirmación; abandonar en marcha, sí.

---

## 80 · Torneos

**Prototipo:** `Torneos v2.dc.html`  
**Archivos del repo:** Controls/MultiplayerTab.xaml (TournamentsView), Services/Multiplayer/BracketLayout.cs, TournamentDetail

### Qué cambia

Lista a la izquierda (plegable a 72) y el torneo elegido a la derecha, con la imagen de su mod detrás y su color en la selección, el botón principal y tu partido. Se mantiene lo de 8 y 14: conectores del cuadro, semillas en monoespaciada, ganador en blanco y perdedor atenuado, «Ganador de…», alineaciones, grupos Solicitudes · Dentro · Fuera del cuadro, zona de peligro aparte, celdas que no crecen ni llevan botones. Cambios: lo que te toca va arriba en una tarjeta con el único botón principal; el detalle se divide en Cuadro, Participantes y Organizar (solo quien organiza); con 32 o más, el cuadro se desplaza en las dos direcciones con las rondas fijas arriba, salto a cada ronda, «Ir a mi partido» y zoom; Participantes dice en qué ronda cayó cada uno. Casos de la lista: 32 en curso con tu partido, 3v3 con la sala abierta por tu rival, inscripción con solicitudes, terminado, borrador.

---

## 93 · Clasificación

**Prototipo:** `Clasificación v3.dc.html`  
**Archivos del repo:** Controls/MultiplayerTab.xaml (RankingView), Services/Multiplayer (PlayerStanding, edades)

### Qué cambia

La jerarquía sale de las edades que ya existen: los tres primeros suben a un podio (2.º · 1.º · 3.º) con foto grande, insignia, ELO y un pedestal del color de su edad, más alto cuanto mejor el puesto; el primero lleva un brillo periódico que desaparece con efectos reducidos. La tabla empieza en el 4.º y se agrupa por edad con cabecera de su color; cada fila y su barra toman ese tono. A la derecha, sobre «Últimas partidas», «Tu posición»: tu puesto y edad, quién tienes encima y debajo con la distancia en ELO y lo que falta para el siguiente puesto; en posicionamiento, tus partidas y cuántas quedan. «Quién más subió» usa los mismos datos. No se pide ningún dato nuevo al servidor.

---

## 97 · Inicio de sesión

**Prototipo:** `Inicio de sesión v2.dc.html`  
**Archivos del repo:** GitHubLoginDialog.xaml(.cs), Services/Multiplayer (StartSignInAsync, DeviceFlowStart), Localization/Strings.cs

### Qué cambia

Es GitHubLoginDialog (el archivo conserva el nombre del proveedor anterior), rehecho como diálogo global: neutros, barra de título #0b1422 con la transparencia del usuario, resplandor azul y «Abrir navegador» en el azul de acción global. Mantiene lo que ya se arregló en 13e: el enlace entero y seleccionable, el aviso de seguridad antes del botón, la espera en su propia fila y un solo botón sólido. Lo nuevo: el estado mientras el servidor prepara el enlace, los fallos con su motivo en una caja y «Reintentar», y el enlace caducado como caso propio.

### Detalle

- 520 de ancho, alto al contenido.
- Mientras `StartSignInAsync` no vuelve (97a): la caja del enlace lleva dos líneas provisionales, el estado dice «Preparando el enlace…», «Abrir navegador» y «Copiar» siguen apagados pero se ve por qué.
- «Copiar» pasa dentro de la caja del enlace como chip (sustituye a `CopyLinkButton`; conserva el «Copiado ✓» de 1,4 s de `MpSignInCopyLinkDone`). El pie queda con «Cancelar» sólido #1a2c48 y «Abrir navegador» azul.
- El dominio del enlace va en negrita blanca para que la comprobación del aviso sea de un vistazo.
- Fallos (`LobbyApiException` y el catch general): el mensaje deja de ir en `Brushes.Salmon` sobre `StatusText`; va en caja ámbar con título llano, el detalle técnico en Consolas (lo que hoy se escribe en `DiagnosticLog`) y, en el pie, «Cerrar» más «Reintentar» (repite `OnLoaded`).
- Enlace caducado (97d): caso propio con «Pedir otro enlace». Requiere mirar el `Code` de la excepción.
- El panel del código (97e, `UserCodeBorder`) se queda tal cual para un servidor que mande `user_code`; Discord no lo manda.
- La frase de privacidad va en su propia línea, encima de los botones. Al autorizar no hay aviso flotante: la ventana se cierra sola y el chip de cuenta ya lo muestra.
- La espera dice cuánto dura el enlace («sirve 10 minutos») solo si `DeviceFlowStart` trae `expires_in`; si no, se quita la frase.

### Variantes

97a preparando · 97b interactivo, enlace listo · 97c error del servidor · 97d enlace caducado · 97e código de dispositivo.

---

## 100 · Instalar copia estable y Desinstalar el launcher

**Prototipo:** `Launcher en este PC v2.dc.html`  
**Archivos del repo:** SelfInstallPromptDialog.xaml(.cs), SelfUninstallDialog.xaml(.cs), Services/SelfInstallService (UninstallFacts), Services/DiskSpaceService, Services/PathDisplay

### Qué cambia

Dos diálogos globales que hablan del propio launcher: neutros con resplandor azul, como Actualizar launcher v2.

**SelfInstallPromptDialog** (460, alto al contenido). Sale al activar «Arrancar con Windows» desde un exe portátil. La barra y el titular dejan de decir «arrancar con Windows»: nombran lo que hace (instalar una copia fija). Añade la tarjeta «Qué hace» (copia a %LocalAppData%, sin servicio ni administrador, entrada de inicio `Aoe3ModLauncher`), la caja con carpeta de destino y tamaño, y el aviso de poco espacio si `DiskSpaceService` lo detecta (100b), con el botón apagado. Botones de 32: «Ahora no» sólido y «Instalar copia estable» azul. El glifo ℹ dorado desaparece.

**SelfUninstallDialog** (560, cuerpo con desplazamiento y pie fijo). Conserva HeaderText, RemovedList (una viñeta por hecho de `UninstallFacts`, ruta en Consolas, nota del UAC en las claves del registro si no eres administrador), la franja verde con `DlgSelfUninstallSafeMods` (los mods por nombre) y ModsHint, PortablePanel con la ruta del exe cuando no es la copia instalada, DataPanel con OptDeleteData desmarcada, OptDataPending y OptDataBackups, y la nota del antivirus. Cambios: tuteo; la tarjeta roja y la franja verde comparten chasis con Desinstalar v2 (75); «Cancelar» es el botón por defecto (Enter y Esc), sólido; el destructivo #c84a4a dice «Desinstalar el launcher» o, portátil, «Quitar lo instalado», y con la casilla de datos marcada pasa a «Desinstalar y borrar mis datos».

### Requiere código o hay que confirmar

El tamaño real de la copia instalada (aquí 48 MB) y si `UninstallFacts` trae el tamaño de la carpeta de datos.

### Variantes

100a instalar copia · 100b sin espacio · 100c interactivo desinstalar · 100d con avisos de datos y restos · 100e portátil · 100f sin mods ni datos.

---

## 101 · Generar parche

**Prototipo:** `Generar parche v2.dc.html`  
**Archivos del repo:** PatchGeneratorDialog.xaml(.cs), Services/DeltaPatchService.cs (GeneratePatchAsync, PatchAssetNaming, AdviseRebaseline)

### Qué cambia

Main ya tiene el diseño 18 (tarjetas por versión, diagrama, vista previa, aviso del tag, rebase). Pasa a la base: chasis global (barra #0b1422 a 40, cristal, sin SetCard), campos con fondo #0d1828 y borde rgba(130,175,255,.2), el tag vacío se marca en ámbar solo al intentar generar sin él, rutas recortadas al final (PathDisplay), «Cerrar» sólido y «Generar parche» azul.

- Mientras `GeneratePatchAsync` trabaja (101c): el botón no cambia de texto; aparece bajo el título una tarjeta con la fase (comparando el .zip anterior, el nuevo, escribiendo el incremental, el acumulado), contador de archivos, barra y «Cancelar».
- Resultado (101a tras generar): archivos con tamaño y «Mostrar en la carpeta»; el aviso de rebase usa los dos tamaños que ya calcula.
- Error (101d): caja ámbar con título llano y la excepción en Consolas, en vez de `ErrorText` en rojo.

### Requiere código

`CancellationToken` en `DeltaPatchService`. Textos nuevos: Comparando {0}… · Escribiendo el parche incremental… · Escribiendo el parche acumulado… · {0} de {1} archivos · Mostrar en la carpeta · No se pudo generar el parche · Cancelar.

### Variantes

101a interactivo (genera, cancela, resultado) · 101b recién abierta · 101c generando · 101d error.

---

## 102 · Empaquetar mi traducción

**Prototipo:** `Empaquetar traducción v2.dc.html`  
**Archivos del repo:** TranslationPackagerDialog.xaml(.cs), Services/TranslationService.cs (ExportPackageAsync), TranslationIndexWriter.cs (NextRevision, Merge), TranslationPathPolicy.cs

### Qué cambia

720 de ancho, cinco tarjetas de cristal (Mod, Tu traducción, Archivos, Compatibilidad, Guardar en), campos de 32 con fondo sólido y un solo botón azul. Conserva piezas y lógica: ModCombo (pasa a selector con icono, nombre y LastKnownVersion; los no instalados atenuados con «no instalado»), IdBox, NameBox, AuthorBox, VersionBox con la propuesta de `NextRevision` y el chip PROPUESTA mientras `_versionIsAutoSuggested`, VersionExistsText como caja ámbar bajo la fila, DescriptionBox, FolderBox y OriginalsBox con los archivos elegidos como chips, CompatCurrentCheck (apagada con el motivo si la versión es «?»), CompatExtraBox, OutputBox con PROPUESTA mientras `_outputIsAutoSuggested`, y ResultPanel con «Abrir la carpeta», los pasos y «Añadir a mi índice».

Cambios: la vista previa de `BuildMenuPreview` se dibuja también en el formulario y se actualiza al escribir; los errores de `ShowError` marcan el campo (borde ámbar y nota en #F5BD76) además de la caja al pie; `DlgPackagerVersionLooksLikeMod` va en esa caja con «Generar de todos modos» como segundo botón; sin versión conocida del mod la casilla queda apagada y la fila dice que hay que escribirla en «Otras versiones». El pie del resultado: «Abrir la carpeta» y «Listo».

### Hay que confirmar

Textos reales `DlgPackager*` y si `ExportResult` trae el tamaño de `translation.json`.

### Variantes

102a interactivo · 102b versión ya publicada · 102c validación por campo · 102d resultado.

---

## 99 · Ver una partida del torneo

**Prototipo:** `Ver una partida v2.dc.html`  
**Archivos del repo:** MatchWatchWindow.xaml(.cs), Services/Multiplayer (BracketLayout, TournamentPermissions)

### Qué cambia

640 de ancho, SizeToContent. Como el torneo es de un mod, lleva su franja arriba y su color en la barra y la selección. Conserva MatchLabel (kicker con `BracketLayout.RoundLabelKey` en minúsculas), RoomTitleText, StateRow (pasa a chip con punto: verde en partida, color del mod esperando, gris terminada), RosterPanel (semilla en Consolas a la derecha, foto o inicial y nombre; en equipos agrupado por el lado del cuadro con la semilla del equipo), ChatPanel con su columna 0,75 : 1,5, ResultBox (solo con resultado; marcador en verde y duración) y el pie con PreviewNote y Cerrar.

Cambios: la caja de texto y «Enviar» desaparecen mientras el servidor no admita al organizador; en su lugar, el chip «Solo lectura» con tooltip y la nota del pie. «Decidir esta partida» entra en el pie como secundario sólido, llamando a lo mismo que el cuadro; no aparece con resultado. 99d (chat funcional) es un dibujo hasta que el servidor exima al organizador de las tres puertas.

Textos nuevos: Solo lectura · Esperando a {0} · Terminada hace {0} min · Escribe como organizador… · Ves el chat pero no puedes escribir: el servidor no deja entrar a quien no juega. · Si {0} no entra a tiempo, decide la partida desde aquí o desde el cuadro.

### Variantes

99a en partida · 99b esperando a un inscrito · 99c con resultado · 99d chat funcional (futuro) · 99e equipos.

---

## 96 · Partida rápida: la sala mientras buscas

**Prototipo:** `Partida rápida v2.dc.html`  
**Archivos del repo:** LobbyWindow (la de 87), Controls/MultiplayerTab.QuickMatch.cs, MainWindow.QuickMatch.cs, Services/Multiplayer/QuickMatchPlanner.cs, Localization/Strings.QuickMatch.cs, AppToast

### Qué cambia

Completa lo que ya tiene la partida rápida en Salas v2 (tarjeta y píldora). La sala de espera es la LobbyWindow de 87 con tres cambios mientras `IsQuickMatchSearching` y la sala es `_qmLobbyId`:

- La caja «Buscando rival» con el reloj (`QuickMatchPlanner.Elapsed`) sustituye a «Estás solo» y sus botones; explica que puedes minimizar y que cada 20 s se comprueba si alguien abrió una sala antes.
- El pie dice «Buscando rival · mm:ss» con la regla de los 30 minutos (`GiveUpAfter`) y su botón es «Dejar de buscar» (`CancelQuickMatchAsync` con `QmCancelled`), sin confirmación: la sala está vacía. Solo mientras `RoomPlayerCount() < 2`; con rival, la búsqueda terminó y el botón vuelve a «Salir de la sala» con la confirmación de siempre. La ✕ de la ventana sigue la misma regla.
- No hay lápiz: el título lo pone `RoomTitleProposal.QuickMatch`.
- La fila libre dice «Esperando rival…» con el punto animado en vez de «Copiar código».
- Al entrar el rival, `OnQuickMatchRivalJoined` termina la búsqueda y la ventana vuelve a ser la sala competitiva normal de 87b.
- Si la búsqueda te mueve a una sala más antigua (`ConvergeAsync`), llegas a la sala de otro con la línea `QmMovedToOlderRoom` en el chat.
- Radmin no listo: el pie lo dice en naranja con «Abrir el asistente» sin bloquear la búsqueda.
- Los avisos usan `AppToast.ToastOptions`; los tres motivos de fallo son los `NoticeAsync` de `StartQuickMatchAsync`, `CreateQuickMatchRoomAsync` y `FailQuickMatchAsync`.

### Hay que confirmar

Si la sala de partida rápida admite «Invitar».

### Variantes

96a interactivo · 96b movido a una sala ajena · 96c Radmin no listo · 96d mínimo 900×600 con 25 min de espera · avisos y motivos de fallo.

---

## 98 · Estadísticas

**Prototipo:** `Estadísticas v2.dc.html`  
**Archivos del repo:** Controls/MultiplayerTab.xaml (StatsView, StatsModPicker, StatsModePicker, StatsLeftColumn, StatsRightColumn), Services/Multiplayer/CivTableLayout.cs, CivStatsView.cs, CommunityStatsView.cs, DeckStatsView.cs, StatsDemoData.cs

### Qué cambia

Aquí se mira un solo mod, así que la página toma su color (selección, barras, horas punta); victoria/derrota siguen fijos y la navegación (pestañas y subpestañas) sigue en el azul global.

- StatsTitleText y StatsCountsText se quedan; StatsModPicker y StatsModePicker pasan a dos grupos de píldoras de 30, con el mod activo en su color mezclado al 58 % con #0f1c2e; el grupo de modo colapsado cuando el mod no tiene partidas de equipos.
- StatsRightColumn: `clamp(300px, 24cqw, 380px)` en vez de 310 fijos. Desde 1600 el balance y los enfrentamientos van lado a lado; por debajo se apilan.
- Balance: columnas de `CivTableLayout.All`; enfrentamientos y aliados, `CivTableLayout.Matchups`. La barra VICTORIAS es verde sobre rojo con el reparto de decididas y gris vacía bajo la muestra (`WinPercent` nulo). Ordenado por partidas; % solo desde `MinDecidedForPercent`.
- La cola de civilizaciones con 3 partidas o menos se agrupa en una fila con «Ver todas», igual que la de mapas con una partida.
- Actividad: `CommunityTotals` y `ActivityBuckets` pasados por `ToLocalHours` y `PeakWindow` (3 h, `MinSampleRooms = 20`); bajo 20 salas, las horas punta no se muestran y se dice por qué.
- Mazos: `DeckStatsView.Group` con sus bandas (% solo con `MinDecksForPercent` mazos), `CivPills` con `MaxCivPills = 7` y la cola plegada; la carta lleva el número del envío en la esquina y el nombre de `DeckCardNames`. Sin mazos compartidos, la tarjeta dice dónde se activa el envío.
- Equipos añade «Juegan juntas» (parejas en el mismo equipo).

### Variantes

98a interactivo (mod, modo, civilización, colas) · 98b 1366×768 · 98c Equipos · 98d sin civilizaciones (lo de hoy) · 98e cargando · 98f error.

---

## 95 · Perfil

**Prototipo:** `Perfil v4.dc.html`  
**Archivos del repo:** ProfileWindow (ProfileBody), Controls/MultiplayerTab.ProfileModes.cs, MultiplayerTab.Replays.cs, Services/Multiplayer (ProfileSummaryView, MatchHistoryView, CivStatsView, OwnDeckView, DeckTiles)

### Qué cambia

La ventana es la misma (1040×720, mínimo 780×480, sin UiScale); ProfileBody pasa a una lista lateral que es tu tarjeta de jugador, fija en las cuatro secciones (Resumen, Historial, Civilizaciones, Mazos). Arriba, el resplandor de tu edad con la foto o inicial grande, anillo, insignia, nombre y una píldora con edad, modo y puesto; luego la navegación y, debajo, tres cifras (partidas, mapa más jugado, rival habitual). Sin edad, la tarjeta usa el azul neutro, sin píldora ni insignia.

- Resumen: `BuildProfileModeCards` (cuatro formas) y `BuildHeadToHeadCard`, más `ProfileSummaryView` (RatingCurve, Totals, FrequentOpponent) y el mapa más jugado. Las tarjetas de modo se apilan por debajo de `ModeCardsStackBelow`; desde 900 px de contenido, la curva y «Contra cada rival» van lado a lado. La devolución de puntos sale arriba hasta que la cierras (55n).
- Historial: `MatchHistoryView`, agrupado por día, marca V / D / — y el cambio de ELO como cifra principal; la que no contó va atenuada con el motivo; cada partida se abre para ver a los jugadores; descarga de grabaciones de `MultiplayerTab.Replays.cs`; «Cargar más» (`LoadMoreMatches`).
- Civilizaciones: `CivStatsView` con `MinDecidedForPercent`.
- Mazos: `OwnDeckView` (SplitForeign y Labels) y `DeckTiles`, comparados con la comunidad desde `MinDecksForPercent` mazos de esa civilización.
- A 780×480 la navegación va justo bajo el nombre y las tarjetas se apilan; nada queda fuera de vista sin desplazamiento.

### Hay que confirmar

El texto exacto de `MpInactiveNotice`.

### Variantes

95a interactivo · 95b sin partidas puntuadas (sin edad) · 95c inactivo · 95d historial · 95e cargando · 95f error · 95g mazos.
