# 1 · Crear sala y crear torneo · una sola lógica

Referencias visuales en `Prototipo.html`: **13a** (crear sala, no competitiva), **13b** (crear sala, competitiva), **13c** (nuevo torneo con nombre propuesto), **13d** (tabla de comparación) y **13e** (inicio de sesión con Discord).

| Pantalla | Archivos del repo |
| --- | --- |
| Crear sala | `CreateLobbyDialog.xaml` + `.xaml.cs` |
| Nuevo torneo | `CreateTournamentDialog.xaml` + `.xaml.cs` |
| Inicio de sesión | `GitHubLoginDialog.xaml` + `.xaml.cs` (el archivo conserva el nombre de otro proveedor; sus registros dicen `DiscordLoginDialog` y sus textos son `MpSignIn*`) |
| Textos | `Localization/Strings.cs` — `MpTournament*`, `MpSignIn*` |

## El criterio no lo inventé: está en tu código

`CreateTournamentDialog.xaml.cs` documenta en su propio resumen las decisiones que tomó y el defecto que cada una reemplazó. Ese es el criterio de este handoff, y `CreateLobbyDialog` arrastra justo lo que aquel abandonó.

Citas literales de ese archivo:

- **«No line of help here may contradict the selection.»** Toda explicación se recalcula desde lo que está elegido. El defecto que sustituyó: *«the old dialog carried a fixed paragraph whose worked example was a 3v3 while 1v1 was selected»*.
- **«a greyed-out primary button with nothing anywhere saying what it was waiting for»** — por eso existe `NameProblem` bajo el campo.
- **El pie:** *«ONE solid element, and it is the one that does the thing. Cancel used to be a light solid button beside a greyed-out primary, so the only thing the eye landed on was the way out.»*
- **La etiqueta del botón:** *«NOT the window's title. "New tournament" on the button says what the window is, not what pressing it does»*.
- **La sub-pregunta que no puede variar se oculta:** `TeamSourceBlock` está `Collapsed` en 1v1, porque *«asking it for a 1v1 would be asking about something that cannot vary»*.

## Qué comparten ya

Los dos son `SizeToContent="Height"` — línea 8 en `CreateLobbyDialog.xaml`, línea 7 en `CreateTournamentDialog.xaml` — y los dos apagan su botón primario. **No comprobé con qué condición lo apaga la sala**: el `.xaml.cs` tiene `CreateButton.IsEnabled` en las líneas 492, 498, 636, 644, 747, 791 y 799, y no leí sus guardas. El torneo lo apaga en un solo sitio, `OkButton.IsEnabled = missing <= 0` dentro de `Refresh()`, que es validación de nombre.

Tampoco comprobé el ancho literal de cada ventana ni si la sala usa el `TitleBar` compartido (`CreateLobbyDialog.xaml` no declara `x:Name="TitleBarControl"`, que es como lo declara el torneo). Si esos puntos importan para unificar, míralos antes.

## 13a / 13b — Crear una sala

Tres cambios, uno por regla del torneo.

**1. El pie.** Hoy `Cancelar` es un botón sólido junto a `Crear`. Pasa a `MpLinkButton` como en el torneo, y `Crear sala` queda como el único elemento sólido. Es literalmente el estado que el torneo describe haber abandonado.

**2. El aviso de Record Game solo existe cuando aplica.** Hoy la caja ámbar aparece con «Sala competitiva» **desmarcada** — o sea avisa sobre el ELO en una sala que no puntúa. Es el defecto que el torneo nombra: una ayuda fija que puede contradecir la selección. Además dice lo mismo que el párrafo de la tarjeta 15 px más arriba, con otras palabras: **sobra una de las dos**. La versión corta se queda en la casilla; el aviso completo aparece solo al marcarla.

**3. La sub-pregunta inerte se colapsa.** Con la casilla desmarcada, hoy FORMATO queda visible con **ningún** segmento activo — un control segmentado en un estado que no existe. Aplica el patrón de `TeamSourceBlock`: `Collapsed` mientras no pueda variar, y al marcarla aparece con `1v1` activo por defecto.

**4. Añade la caja de cuentas.** El torneo tiene el mejor elemento de los dos: un resumen recalculado (`CapacityMath`). La sala no tiene equivalente, y su única frase de contexto —«Se anunciará en el chat global y en Discord»— es estática y vive en el pie. Sustitúyela por una caja igual a la del torneo, con lo elegido:

- no competitiva: `8 plazas · sala pública · sin ELO · se anuncia en el chat y en Discord`
- competitiva 2v2: `2v2 · 4 de 8 plazas · puntúa para el ELO · se anuncia en el chat y en Discord`

Recalculada en un solo método, como `Refresh()`, para que no pueda contradecir la selección.

**5. Un solo estilo de etiqueta.** Hoy mezcla minúsculas en el primer nivel («Mod», «Título de la sala», «Jugadores máx.») con `FORMATO` en mayúsculas dentro de la tarjeta. Usa `MpSectionLabelSize` + SemiBold en todas, como el torneo.

## 13c — Nuevo torneo

Solo un cambio, y es lo único que el torneo debe copiarle a la sala.

**Propón el nombre.** Hoy el campo nace vacío, así que el primer `Refresh()` muestra `NameProblem` y lo primero que lee el usuario es «Faltan 3: el nombre necesita al menos 3 caracteres», con el botón apagado. **El diálogo abre regañando.** La sala no tiene ese problema porque propone un título.

Rellena `NameEntry` con un valor por defecto derivado del mod (p. ej. «Torneo de Struggle of Indonesia», truncado a `MaxNameLength`) y selecciona el texto al enfocar, para que sobrescribirlo sea escribir. El error desaparece al abrir y el botón nace activo. `NameProblem` sigue existiendo: aparece solo si el usuario borra el campo.

Nada más cambia en este diálogo. El pie, la caja de cuentas y las ayudas recalculadas ya están bien.

## 13e — Inicio de sesión con Discord

**1. La URL entera.** Hoy sale recortada («…authorize?response_type=co…») en una tarjeta que te pide copiarla y cuyo propio texto te dice que compruebes que el navegador es de fiar. **No se puede verificar un enlace que no se puede leer.** Va completa, en monoespaciada, con `word-break` para que parta en varias líneas.

**2. El consejo de seguridad, antes del botón.** Hoy «si se abre un navegador que no reconoces, copia el enlace» está *debajo* de «Abrir navegador» — llega después de pulsarlo. Sube a una caja ámbar sobre los botones, y menciona qué comprobar: que el enlace empieza por `discord.com`.

**3. El estado de espera, en su propia fila.** «Esperando tu autorización en el navegador… esta ve…» se corta contra el botón. Mismo desbordamiento de pie que el asistente de Radmin (ver la parte 2). Fila propia, a ancho completo.

**4. Cancelar no es el elemento más llamativo de la ventana.** Hoy es un botón dorado sólido; el único sólido debe ser «Abrir navegador». Misma regla del torneo.

**5. El título no se repite.** La barra dice «Inicio de sesión D…» (recortado) y justo debajo hay un `<h1>` con el mismo texto completo. Quita el h1 y deja que la barra lo diga una vez.

## Tokens

Los mismos del resto del launcher (paleta navy `MpSurface`, no la dorada).

| Uso | Hex |
| --- | --- |
| Fondo del diálogo | `#12213a` |
| Campo, cápsula segmentada | `#0d1828` |
| Tarjeta interior, caja de cuentas | `#0f1c2e` / `rgba(47,127,224,.12)` |
| Borde interior | `rgba(130,175,255,.11–.16)` · campo enfocado `rgba(47,127,224,.55)` a 2 px |
| Azul de acción | `#2f7fe0` · texto sobre oscuro `#8cbcf5` / `#8fb6ea` |
| Texto primario | `#e8eef6` · titulares `#f0f5fb` |
| Texto de cuerpo | `#b9c9de` · secundario `#8ea4c0` |
| Texto atenuado | `#6d829d` → `#61779a` → `#5f7592` |
| Verde OK | `#4fd68a` · texto `#8fe0b0` |
| Ámbar aviso | `#e6b455` · texto `#d8bd8a` / `#f0dcae` sobre `rgba(230,180,85,.08)`, borde `rgba(230,180,85,.22)` |

**Tipografía.** UI en Segoe UI. Etiquetas de sección 11.5 px SemiBold. Cuerpo 12.5/1.6. Ayudas bajo control 11/1.45. Contadores, hashes, URLs y la caja de cuentas en **monoespaciada**.

**Medidas.** Campo 38 · segmento 32 · botón primario 36 con `MinWidth` 128 · casilla 16 · relleno del cuerpo 18/22/20. Separación entre secciones 17 px; entre etiqueta y control 6.
