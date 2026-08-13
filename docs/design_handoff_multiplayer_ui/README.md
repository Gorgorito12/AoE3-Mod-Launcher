# Handoff: rediseño de la pestaña Multijugador (AoE3 Mod Launcher)

Repo destino: `Gorgorito12/AoE3-Mod-Launcher`, rama `main`, proyecto WPF `WarsOfLibertyLauncher/`.

## Alcance: SOLO estas cuatro pantallas

Del documento de diseño se implementan **1a, 1d, 1e y 1f**. Las opciones **1b y 1c se descartan** — no las implementes ni mezcles ideas de ellas.

| Id | Pantalla | Archivo del repo a modificar |
| --- | --- | --- |
| 1a | Pestaña Multijugador → Salas (cabecera, lista, panel derecho) | `Controls/MultiplayerTab.xaml.cs` (+ su `.xaml`), `Controls/TitleBar.xaml.cs`, `Controls/MainTabs.xaml.cs` |
| 1d | Diálogo «Crear una sala» | `CreateLobbyDialog.xaml.cs` (+ `.xaml`) |
| 1e | Ventana Lobby (sigue siendo ventana aparte) | `LobbyWindow.xaml.cs` (+ `.xaml`) |
| 1f | Partida en curso + tarjeta de resultado | `LobbyWindow.xaml.cs`, `Services/Multiplayer/RoomMatchState.cs`, `MatchResultResolver.cs`, `PlayerStanding.cs` |

## Sobre los archivos de diseño

`Launcher Multiplayer.dc.html` es una **referencia de diseño en HTML**: un prototipo estático que muestra el aspecto y la jerarquía previstos. **No es código para copiar.** La tarea es recrear esas pantallas en WPF/XAML con los estilos, controles y convenciones que ya existen en el proyecto (`Controls/`, `UiScale.cs`, `TooltipHelper.cs`, `Localization/Strings.cs`).

Abre el HTML en un navegador y haz zoom para leer los detalles; las opciones están rotuladas con su id (1a, 1d, 1e, 1f).

## Fidelidad

**Alta fidelidad.** Colores, tipografía, espaciado y estados son definitivos. Respeta los valores de la sección Tokens. Lo que no esté especificado, resuélvelo con los estilos ya existentes en el launcher.

## Reglas transversales (aplican a las cuatro pantallas)

1. **Todo el texto pasa por `Localization/Strings.cs`.** El diseño está en español; hay que dar de alta las claves en español e inglés. Ninguna cadena literal en el XAML.
2. **Radmin VPN deja de tener banner permanente.** Cuando el estado es correcto se reduce a un chip en la barra de título: `● Conectado │ VPN · 26.162.244.170`. El banner completo (verde/ámbar/rojo, con «Ver pasos» y «Abrir Radmin VPN») **solo** aparece si la VPN no está corriendo, no hay IP 26.x.x.x, o la conexión al lobby falla.
3. **Se elimina la fila de cuenta** (`@gorgorito_12 | Sign out` + Refresh + Create room). La identidad va al avatar de la barra de título con menú desplegable (Perfil, Cerrar sesión); Actualizar y Crear sala van a la barra de sub-pestañas.
4. **No dupliques estado.** «Connected / N players online / N active rooms» aparecía tres veces; ahora: el chip verde es el único indicador de conexión, y los recuentos viven en el panel derecho.
5. El **Lobby sigue siendo `Window`** (decisión del producto). No lo integres en la pestaña.
6. La ventana ya no debería necesitar scroll vertical a 1080 px de alto en el estado normal.

---

## 1a — Pestaña Salas

**Objetivo**: eliminar las cinco barras apiladas (título → sub-pestañas → banner VPN → fila de cuenta → cabecera de tabla) y dejar dos. En el diseño actual el contenido empieza a ~400 px; debe empezar a ~96 px.

### Barra 1 — barra de título, alto 46 px, fondo `#233648`, padding lateral 14 px, `gap: 24px`
- Logo `AppIcon.png` 20×20, radio 5.
- Título «AoE3 Mod Launcher», 13 px / 700, serif (ver Tokens), `#eef3f9`.
- Pestañas principales BIBLIOTECA · TALLER · MULTIJUGADOR: 11.5 px / 600, `letter-spacing: .7px`, padding 8/11. Inactiva `#a8bcd2`; activa `#ffffff` sobre `rgba(47,127,224,.28)` con borde interior 1 px `rgba(120,180,255,.4)`, radio 6.
- Empuje flexible.
- Chip de conexión: alto 26, radio 999, fondo `rgba(53,196,111,.14)`, borde interior `rgba(53,196,111,.3)`, punto 7 px `#4fd68a`, «Conectado» 11.5/600 `#8fe0b0`, separador 1×11 px, «VPN · 26.162.244.170» 11.5/400 `#a9cbb9`.
- Selector ES/EN: dos pastillas 10.5/600, activa fondo `rgba(255,255,255,.14)`.
- Avatar 26 px circular + nombre 11.5/600 `#e4ecf6` + ELO 10/500 `#8fb6ea` debajo + chevron. Clic abre menú.

### Barra 2 — sub-pestañas, alto 48 px, fondo `#12213a`, línea inferior `rgba(130,175,255,.1)`
- Salas · Amigos · Perfil · Historial: 13 px, activa `#ffffff` sobre `rgba(255,255,255,.08)` radio 7 padding 9/12; inactiva `#93a7c3` / 500.
- A la derecha: campo de búsqueda (alto 32, radio 7, fondo `#0d1828`, borde interior `rgba(130,175,255,.16)`, placeholder «Buscar sala, mod o jugador» 12 px `#6d829d`), botón `↻ Actualizar` (fantasma, borde `rgba(130,175,255,.18)`) y `+ Crear sala` (alto 32, radio 7, fondo `#2f7fe0`, 12.5/600 blanco).

### Cuerpo — dos columnas, padding 14 px, `gap: 13px`, fondo `#0f1c2e`
Izquierda flexible; derecha 322 px fija.

**Lista de salas**
- Cabecera de sección: «Salas activas» 13/600 `#e8eef6` + contador en pastilla `rgba(255,255,255,.07)`; a la derecha «Actualizado hace N s · orden por ping» 11/400 `#5f7592`.
- Rejilla de 5 columnas: `minmax(0,1fr) 152px 88px 66px 96px`, `column-gap: 12px`, padding lateral 12 px. Cabecera: 10.5/600, `letter-spacing:.6px`, `#61779a` — SALA · ANFITRIÓN · JUGADORES · PING · (acción).
- **Cada fila es de una línea. Nunca dos.** Título y subtítulo con recorte por elipsis. Altura de fila uniforme ≈ 58 px, radio 8, `gap` vertical 6 px entre filas.
  - Icono del mod 30×30 radio 6 (usa el icono del catálogo; `WoL.ico` para Wars of Liberty).
  - Título 13/600 `#e8eef6`. Subtítulo 11/400 `#6d829d` con el patrón `«{mod} · {contexto} · hace {tiempo}»` (el mod va aquí, ya no tiene columna propia).
  - Etiqueta PRIVADA: 9.5/600, `#cba7f2` sobre `rgba(160,110,240,.16)`, radio 4, no se encoge.
  - Anfitrión: avatar 20 px + nombre 12/500 `#c3d2e5`.
  - Jugadores: «1/8» 12/600 + 4 segmentos de 9×5 px radio 2 (llenos `#2f7fe0`, vacíos `rgba(255,255,255,.13)`) proporcionales a `current_players / max_players`.
  - Ping: 12/600 — `#7fdca6` <60 ms, `#e6b455` 60–150 ms, `#d99a9a` >150 ms. El backend no envía ping (`LobbySummary` no lo tiene): se mide localmente por ICMP a la IP Radmin del anfitrión, igual que ya se hace en la sala.
  - Acción: alto 30, radio 7. «Unirse» sólido `#2f7fe0`; «Volver» (tu propia sala) fantasma azul `rgba(47,127,224,.16)` + borde `rgba(47,127,224,.55)`, texto `#8cbcf5`. Si es privada: «Unirse con contraseña».
  - Fila propia: fondo `#16263e` + borde interior `rgba(130,175,255,.14)`. Resto: `#12213a` + `rgba(130,175,255,.09)`.
- Fila «¿Te pasaron un código de sala?»: caja de código monoespaciada `letter-spacing:1.6px` + botón Entrar. Resuelve una sala privada por id sin figurar en la lista.

**Tira «Actividad de la comunidad»** (sustituye al estado vacío de media pantalla)
Tres tarjetas iguales (padding 12/13, radio 8, `#12213a`, borde `rgba(130,175,255,.09)`), título 10.5/600 `letter-spacing:.6px` `#61779a`:
1. HORA PUNTA: 7 barras (una por franja de 3 h de las últimas 24 h), alto 34 px, `gap` 3 px, la mayor `#2f7fe0`, el resto con alfa 0.35–0.6; pie «Hay más gente entre las 21:00 y 23:00».
2. ÚLTIMAS PARTIDAS: hasta 3 líneas de `GET /matches/history`. Punto verde `#4fd68a` si la partida se decidió; punto gris `#6d829d` + texto atenuado y «no contó» cuando `result == 0.5` (sin grabación).
3. CLASIFICACIÓN: top 3 por rating; tu fila resaltada con fondo `rgba(47,127,224,.12)` y la palabra «tú».

Con 0 salas: la lista muestra un texto de una línea y **esta tira sigue visible**. No se usa la ilustración grande ni el CTA gigante.

**Panel derecho, 322 px, radio 8, `#12213a`, borde `rgba(130,175,255,.11)`**
- Dos pestañas en la cabecera: «Chat global» / «Jugadores · N». Antes eran dos paneles apilados; ahora comparten alto y el chat gana ~300 px.
- Chat: separador de fecha centrado (línea + «7 AGO» 9.5/600 `#5f7592`) entre días distintos — hoy los mensajes de días diferentes se ven seguidos.
- Mensaje: avatar 24 px + nombre 12/600 `#cdd9e9` + hora 10.5/400 `#5f7592` + cuerpo 12.5/400 `#b9c9de` `line-height 1.5`. Sin burbujas.
- Eventos de sala insertados en el flujo: caja `rgba(47,127,224,.09)` con borde `rgba(47,127,224,.2)`, icono ⚑, «X abrió una sala», mod y cupo, y un enlace «Unirse» que entra directo.
- Respuestas rápidas: pastillas 11/600 `#8cbcf5` sobre `rgba(47,127,224,.12)` que rellenan el campo.
- Composer: campo alto 34 radio 7 `#0d1828` + botón cuadrado 34 `#2f7fe0`.
- Pestaña Jugadores: mantiene los grupos actuales (En partida / En sala / En el launcher) con sus puntos de color.

---

## 1d — Diálogo «Crear una sala» (596 px de ancho)

Barra de título 38 px `#233648`, título serif 12.5/700 `#f4d9a0` (se mantiene el acento dorado actual), ✕ a la derecha. Cuerpo padding 18 px `#12213a`. Etiquetas de sección: 11/600 `letter-spacing:.5px` `#8ea4c0`.

- **MOD**: caja alto ~48 radio 8 `#0d1828` borde `rgba(130,175,255,.18)`; icono 26 px, nombre 13/600, y debajo el **estado de la huella** — punto `#4fd68a` + «Huella verificada · {primeros 6 de mod_combined_hash}». Si el hash no se ha calculado: ámbar «Calculando huella…»; si falla: rojo con enlace a la ayuda. Esto sustituye al acordeón «Advanced details (Mod fingerprint)», que desaparece: es la causa habitual de que otro jugador no pueda unirse, así que debe estar visible.
- **TÍTULO**: contador `20/64` monoespaciado a la derecha de la etiqueta; campo alto 40, radio 8, borde de foco 2 px `rgba(47,127,224,.55)`. Debajo, 3 pastillas de sugerencia («1v1 rápido», «Sin rush 10 min», «Solo LatAm») que se añaden al título.
- **JUGADORES MÁXIMOS**: segmentado 2–8 en lugar de desplegable. Botones flexibles, alto ~30, radio 6; activo `#2f7fe0` blanco 12/600, inactivo borde `rgba(130,175,255,.14)` texto `#b9c9de`.
- **Sala privada**: casilla en caja violeta (`rgba(160,110,240,.08)`, borde `rgba(160,110,240,.24)`) con la explicación en dos líneas dentro de la propia caja. El campo de contraseña **solo se renderiza cuando está marcada** (hoy aparece deshabilitado y confunde); alto 38, borde `rgba(160,110,240,.3)`, texto `#cba7f2`, y un enlace «Mostrar» para revelarla.
- **Aviso de grabación** (ámbar `rgba(230,180,85,.08)` / borde `rgba(230,180,85,.22)`, texto 11.5/400 `#d8bd8a`): «Para que la partida cuente en el ELO, marca **Record Game** en la pantalla de configuración de AoE3. Sin grabación nadie sabe quién ganó.» Va aquí, antes de crear la sala, no después.
- **Pie** `#0f1c2e`: a la izquierda «Se anunciará en el chat global y en Discord» 11/400 `#6d829d`; a la derecha Cancelar (fantasma) y Crear sala (`#2f7fe0`, padding lateral 22).

---

## 1e — Ventana Lobby (980 px de ancho)

Sigue siendo `Window` con su barra de título; el título pasa a «Sala · {nombre}» para que la barra de tareas sea legible.

**Cabecera de sala** (radio 9, `#16263e`, borde `rgba(130,175,255,.14)`, padding 13/15): icono del mod 42 px; nombre 16/700 serif con lápiz de renombrar; línea de estado «● En el lobby» `#8fe0b0` + «LAN P2P lista · abierta hace N s» `#6d829d`. A la derecha, tres bloques con etiqueta 9.5/600 `#61779a` y valor 14/600: JUGADORES `1/8`, CONEXIÓN `6 ms` (`#7fdca6`), CÓDIGO monoespaciado + botón copiar. El bug visible en las capturas actuales (el nombre del jugador cortado por el borde del panel) desaparece porque el roster ya no está en un contenedor de alto fijo.

**Columna izquierda, 352 px**
1. **JUGADORES**: una fila por miembro, alto ~44, radio 7. Avatar 26 px (`avatarUrl`, si no hay: monograma). Nombre 12.5/600; etiqueta ANFITRIÓN 9.5/600 `#8cbcf5` sobre `rgba(47,127,224,.16)`. Segunda línea 10.5/400 `#6d829d`: `{rating} ELO · {ping} ms` (ping por ICMP a `radminIp`; si aún no lo ha reportado, «esperando VPN»). Estado a la derecha: «listo» `#8fe0b0` / «esperando» `#e6b455`. Huecos libres como fila punteada «Hueco libre · comparte el código». Enlace «Invitar» en la cabecera del bloque.
2. **ANTES DE EMPEZAR**: checklist de dos ítems. «Mods idénticos en los N jugadores» se marca solo cuando los `mod_combined_hash` coinciden. «Marcar **Record Game** en AoE3 para que cuente el ELO» queda sin marcar, en ámbar, con enlace «Ver cómo». Sustituye al bloque «So the match counts» + «Don't show this again» — el aviso ya no se puede silenciar, pero ocupa dos líneas en vez de siete.
3. **Acciones**: una primaria `▶ Empezar partida` (alto 42, `#2f7fe0`, 14/600) y debajo dos secundarias del mismo peso visual: «Marcarme listo» (fantasma) y «Salir de la sala» (fantasma rojo, texto `#d99a9a`, borde `rgba(200,120,120,.28)`). Hoy son tres botones sólidos verde/azul/rojo compitiendo. Reglas de `RoomMatchState`: `Empezar partida` solo para el anfitrión; a un invitado con la partida viva y su juego cerrado se le ofrece «Abrir el juego»; al salir, el diálogo de confirmación usa el `LeaveWarning` correspondiente.

**Columna derecha — Chat de la sala**: cabecera 12.5/600 + «Limpiar»; mensajes con avatar 22 px; eventos de sistema como línea con icono cuadrado 22 px (`→` azul para entradas/salidas, `!` ámbar para avisos) en vez de `[System]` monoespaciado; composer con botón «Enviar» de texto.

---

## 1f — Partida en curso y resultado

**Partida en curso** (reemplaza la columna izquierda del Lobby mientras el juego corre):
- Encabezado: punto `#4fd68a` + «PARTIDA EN CURSO» 13/600 `letter-spacing:.4px` + cronómetro monoespaciado 14/600 a la derecha.
- Tres celdas (radio 8, `#12213a`): TRÁFICO (`640 B`), CONEXIÓN (`5 ms`, verde), GRABACIÓN (`activa` `#4fd68a` / `no detectada` ámbar — leído del plan de `GameRecordingPlan`).
- Caja ámbar cuando eres el único en la sala: título «Eres el único jugador en la sala», explicación de que la red P2P está lista pero falta otro launcher, y dos botones fantasma ámbar: «Copiar código» y «Avisar en el chat» (publica en el chat global). Hoy ese texto está en cursiva dentro de un recuadro sin acciones.
- «✕ Abortar partida» como botón fantasma rojo al pie.

**Tarjeta de resultado** (al terminar, misma ventana):
- Icono 44 px en caja `rgba(53,196,111,.16)` con ✓ verde (victoria) / ✕ `#d99a9a` (derrota) / — gris (indecidida).
- «Victoria» 17/700 serif; subtítulo `{mod} · {mapa} · {duración} · {N} jugadores` (de `MatchHistoryRow`).
- A la derecha: rating nuevo 22/600 monoespaciado + delta (`+18` verde / `-14` rojo) y «antes 1524» 10.5 `#6d829d`. Directo de `rating_before` → `rating_after`.
- Tres celdas: DECIDIDAS `4-1 · 80 %` (usa `PlayerStanding.WinPercent`; si es null, muestra «—», nunca 0 %), REPETICIÓN (estado de subida + enlace), RIVAL (nombre + rating).
- Pie: nota de rating provisional cuando `rd` es alto («tras N partidas decididas se estabiliza») + botón «Revancha» que recrea la sala con el mismo mod y título.
- **Si no hubo grabación** (`result == 0.5`): el icono es gris, el título «Sin resultado», y el texto dice que la partida no contó para el ELO de nadie y por qué. No se inventa un empate.

---

## Tokens

**Color**
| Uso | Hex |
| --- | --- |
| Fondo de la app | `#0f1c2e` |
| Barra de título / diálogos | `#233648` |
| Barra de sub-pestañas / panel | `#12213a` |
| Fila o tarjeta destacada | `#16263e` |
| Campo de entrada | `#0d1828` |
| Borde interior sutil | `rgba(130,175,255,.09–.18)` |
| Texto primario | `#e8eef6` · titulares `#f0f5fb` |
| Texto secundario | `#b9c9de` / `#c3d2e5` |
| Texto atenuado | `#8ea4c0` → `#6d829d` → `#5f7592` |
| Etiqueta de sección | `#61779a` |
| Azul de acción | `#2f7fe0` · texto sobre oscuro `#8cbcf5` |
| Verde OK | `#4fd68a` · texto `#8fe0b0` / `#7fdca6` |
| Ámbar aviso | `#e6b455` · texto `#d8bd8a` / `#f0dcae` |
| Rojo destructivo | texto `#d99a9a`, borde `rgba(200,120,120,.3)` |
| Violeta privado | `#a06ef0` · texto `#cba7f2` |

**Tipografía**: UI en Segoe UI (la del sistema, ya en uso). Titulares de ventana y cifras grandes en serif (`Source Serif 4` en el prototipo; en WPF usa Georgia o la serif que ya emplea el título dorado). Escala: 9.5 · 10.5 · 11 · 11.5 · 12 · 12.5 · 13 · 14 · 16 · 17 · 22 px. Etiquetas de sección 9.5–10.5 px, 600, `letter-spacing` .5–.7 px, mayúsculas. Datos numéricos (ping, ELO, código de sala, tráfico, cronómetro) en monoespaciada.

**Espaciado**: 2 · 3 · 6 · 7 · 8 · 9 · 11 · 12 · 13 · 14 · 16 · 18 · 22 px. Padding de panel 12–14 px; de diálogo 18 px.

**Radios**: 4 (etiquetas) · 5–6 (iconos, celdas) · 7 (botones, campos) · 8 (filas, cajas) · 9–10 (paneles) · 999 (chips).

**Alturas**: barra de título 46 · sub-barra 48 · fila de sala ≈58 · botón normal 30–34 · botón primario 38–42 · campo 34–40.

**Bordes**: 1 px interior con alfa (en WPF, `BorderThickness=1` con `BorderBrush` translúcido). Sin sombras salvo las de las ventanas nativas.

## Interacciones y estados

- Hover de fila de sala: fondo un paso más claro (`#16263e`), sin desplazamiento. Doble clic = acción principal de la fila.
- Botones: hover +8 % de luminosidad; pressed −8 %; foco con anillo `rgba(47,127,224,.55)` de 2 px.
- Ordenación: la cabecera solo mantiene orden en SALA, JUGADORES y PING; por defecto ping ascendente. Se eliminan las flechas dobles decorativas de las columnas que nadie ordena.
- Refresco: la lista se actualiza sola; «Actualizado hace N s» cuenta en vivo. El botón Actualizar muestra spinner 500 ms mínimo para que el clic se sienta.
- Alta de sala nueva mientras miras la lista: se inserta con un realce azul de 1 s.
- Estados de la lista: cargando (3 filas esqueleto), vacía (texto de una línea + tira de actividad), error (línea ámbar con reintentar). Nunca la ilustración a pantalla completa.
- Crear sala: «Crear» deshabilitado sin título o sin huella válida; error del servidor en línea bajo el pie, no en un `MessageBox`.
- Copiar código: cambia a «Copiado ✓» 1,5 s.
- Todo lo demás (VPN, timeouts, conflictos «Lobby already in game») conserva la lógica actual.

## Estado y datos

Sin campos nuevos en el backend. Se consumen: `LobbySummary` (título, mod, cupos, privada, estado, creada, anfitrión), `LobbyDetail.members` para avatares de la lista, `WsRoomState` + `WsRoomMemberFlags` (ready, login, `radminIp`, `avatarUrl`) en el Lobby, `EloSnapshot` (rating, `rd`, wins, losses) para el chip de ELO y la clasificación, `MatchHistoryRow` (+ `rating_before/after`) para las últimas partidas y la tarjeta de resultado, `QuotaSnapshot` para los recuentos del panel. El ping es local (ICMP a IP Radmin) y no se persiste.

Estado nuevo de UI: pestaña activa del panel derecho, filtro/orden de la lista, visibilidad del banner VPN (derivada del estado real), casilla de sala privada, y la agrupación por fecha del chat.

## Assets

- `AppIcon.png` y `WoL.ico` — ya en el repo (`WarsOfLibertyLauncher/`). Se usan para el logo de la barra de título y el icono de Wars de Liberty.
- Iconos de mod: los del catálogo (`mod.json` → `icon`), vía `ModIconConverter`.
- Los rectángulos rayados del prototipo son marcadores de banner: el catálogo no incluye imagen todavía. Si no hay banner, usa el color de acento del mod (`accentColor`, p. ej. `#c8102e` en WoL) sobre fondo oscuro.

## Archivos de este paquete

- `Launcher Multiplayer.dc.html` — prototipo con las seis opciones. **Implementa solo 1a, 1d, 1e, 1f.**
- `PROMPT.md` — texto listo para pegar en Claude Code.
