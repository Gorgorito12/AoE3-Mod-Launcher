# Rediseño con la base de la Biblioteca unificada, segunda tanda (95–102)

Ocho pantallas más del launcher, rehechas con la base de la Biblioteca unificada (68a). Las reglas comunes están en el paquete anterior (`docs/design_base_launcher/BASE.md`); aquí va cada pantalla: qué cambia, en qué archivos del repo y qué necesita código nuevo o confirmación.

**Los prototipos.** Abre cada `.dc.html` en un navegador, con `support.js` y las carpetas `mods/` y `design_handoff_workshop/` al lado. Son interactivos; la transparencia (y en algunos el tamaño) se cambia en Tweaks. Son referencia de diseño: recréalos en WPF/XAML con los estilos y controles que ya existen.

**Los textos.** Los textos en español que no salen de `Localization/Strings.cs` son propuestas (el archivo supera el límite de lectura). Si ya hay una clave equivalente, usa la suya. Todo texto nuevo va en español (es-419, tuteo) e inglés.

**Una regla de la base que apareció en esta tanda.** Texto en una línea recortado con puntos suspensivos (`TextTrimming`) lleva interlineado mínimo 1,4; con 1,2–1,3 el recorte se come los trazos bajos de g, p, j, y.

## Orden sugerido

De la más pequeña a la más grande: 97 · 100 · 101 · 102 · 99 · 96 · 98 · 95.

## Pruebas para todas

1366×768, 1920×1080 y 2560×1440; las tres transparencias (Sólida, Media, Máxima); efectos reducidos. Los diálogos también en su tamaño mínimo (Perfil 780×480, Partida rápida 900×600).

## Dudas acumuladas (confirmar antes de implementar)

- `PlayerStanding.MinDecidedForPercent`: 5 según `StatsDemoData`, 6 según el handoff del perfil. Afecta a 95 y 98.
- `/stats/community`: ¿acepta mod y modo? La cabecera de 98 los da por hecho.
- Inicio de sesión: el enlace caducado llega como una `LobbyApiException` más; hace falta mirar su `Code` (p. ej. `expired_token`) para mostrar 97d.
- Partida rápida: ¿la sala de partida rápida admite «Invitar»? Si no, el botón se oculta mientras buscas.
- Generar parche: «Cancelar» mientras genera necesita un `CancellationToken` en `DeltaPatchService.GeneratePatchAsync`.
- Empaquetar traducción: los textos `DlgPackager*` del prototipo son propios; si ya existen, usa los de `Strings.cs`.
- Ver una partida: 99d (chat funcional para el organizador) solo se activa cuando el servidor exima al organizador de las tres puertas (`in_game`, cruce y plazas de observador).

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

`MinDecidedForPercent` y el texto exacto de `MpInactiveNotice`.

### Variantes

95a interactivo · 95b sin partidas puntuadas (sin edad) · 95c inactivo · 95d historial · 95e cargando · 95f error · 95g mazos.
