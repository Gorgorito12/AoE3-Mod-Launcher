# El mazo de la comunidad

Rediseño de la sección «Cards the community brings» de la pestaña Estadísticas. Referencias visuales en `Prototipo.html`: **25a** (el diseño) y **25b** (un fallo de datos de la versión actual, evidenciado con dos capturas).

Repo: `Gorgorito12/AoE3-Mod-Launcher`, rama `main`, proyecto WPF `WarsOfLibertyLauncher/`.

| Qué | Archivos del repo |
| --- | --- |
| La sección | `Controls/MultiplayerTab.xaml.cs` — `BuildDeckCivGroup`, `BuildDeckCardRow`, `BuildDeckCountCell`, `BuildDeckTailRow`, `BuildDeckMoreCivsRow` (~10800-11300) |
| El agregado | `Services/Multiplayer/DeckStatsView.cs` — `DeckCivGroup`, `DeckCardRow`, `CivGroupsShown`, `Contributors` |
| Nombres de carta | el resolutor que usa `names.Resolved` en `BuildDecksSection`; ver también `Services/ModStringTable.cs` |
| Textos | `Localization/Strings.cs` — `MpStatsCommunityDecks*`, `MpStatsDecks*`, `MpStatsTailDecks*` |

**Leí los constructores de esta sección en `MultiplayerTab.xaml.cs`; no leí `DeckStatsView.cs`.** Lo que dependa del agregado —cómo se elige la cola, de dónde sale el denominador— está marcado como pendiente al final.

El HTML es una **referencia de diseño**, no código para copiar. Todo el texto pasa por `Localization/Strings.cs` (español e inglés).

## Lo que esta sección responde

Hoy es un censo: «800 distinct cards» repartidas en 39 civilizaciones, en una lista vertical que hay que desplegar de una en una. Un censo no contesta ninguna pregunta que un jugador se haga.

La pregunta que **sí** contesta con 9 mazos es: *«juego con Germans, ¿qué lleva la gente?»*. Y la respuesta ya está en los datos — tres cartas aparecen en los seis mazos alemanes. Eso es un mazo de partida para alguien que nunca jugó esa civilización, no un inventario.

Todo el rediseño sale de esa reformulación.

## 25a — Se dibuja como lo que es: un mazo

**En horizontal, y las cartas, no las civilizaciones.** AoE 3 enseña las cartas de la ciudad natal en fila; la sección que dice qué cartas trae la gente debería verse igual. En el prototipo caben **27 cartas sin desplazarse**, donde la lista vertical enseñaba siete.

**El porcentaje se queda, pero sube a encabezado de banda.** El punto de la sección es «qué lleva todo el mundo, aproximadamente», y para eso el porcentaje es la forma natural: «5/5» obliga a dividir mentalmente.

Lo que causaba confusión no era el símbolo sino **dónde estaba**: una columna «%» pegada a la columna «%» de la tabla de victorias, 400 px más arriba, con dos significados distintos. Agrupando por porcentaje, aparece **cuatro veces en lugar de cincuenta**, y siempre con su frase al lado:

- `100 %` — in every German deck — 3 cartas
- `83 %` — in 5 of the 6 — 6 cartas
- `67 %` — in 4 of the 6 — 11 cartas
- `50 %` — in 3 of the 6 — 7 cartas

La primera banda **es** la respuesta de la sección, así que lleva borde azul y texto más claro. El resto son bandas normales.

**El denominador se dice.** «in 5 of the 6» junto al porcentaje elimina la ambigüedad sin repetir la fracción en cada carta. Hoy ese denominador por civilización **no se muestra en ninguna parte**: el código tiene `group.DistinctCards` y un `Contributors` global, pero el número entre el que divide el porcentaje no aparece. Publicar un reparto sin decir sobre cuántos es el fondo del problema.

**Selector de civilización horizontal**, en pastillas, con la del jugador primero y activa. Hoy son 39 filas verticales de acordeón.

**La tarjeta de carta**: 104 px de ancho, arte arriba 74, nombre debajo a dos líneas como máximo. Radio 7, cuadrado — el arte de AoE 3 tiene marco y un recorte redondo se come la esquina. Las de la banda de consenso llevan borde de 2 px.

**La cola no se nombra.** «31 more cards, each in one or two decks — too thin a sample to say anything, so they are counted and not listed». Si la muestra no da para nombrar, no se nombra: es la misma regla que la sección ya aplica al porcentaje.

**Botón «Share my deck».** Con 6 mazos, lo que más mejora esta sección es que haya más, y ese botón no existe hoy.

## 25b — Un fallo de datos, no de diseño

En la tercera captura del usuario, la fila de cola dice:

> **51 more cards, seen once** · Fencing School, Fort, 3 Hussars, 8 Skirmishers…

En la cuarta, al desplegar esa misma cola, esas cuatro cartas salen con **5, 5, 4 y 4** apariciones — 83 %, 83 %, 67 % y 67 %. **Ninguna se vio una sola vez.**

Lo que la cola contiene no es «las vistas una vez»: es «todo lo que no cupo arriba», y la etiqueta afirma lo contrario. Peor, los cuatro ejemplos que elige para ilustrar «seen once» son precisamente cartas frecuentes, así que la contradicción está dentro de la misma fila.

En 25a el problema desaparece por construcción: agrupando por porcentaje no hay cola que etiquetar hasta llegar de verdad a las de uno o dos mazos.

**Pero hay que mirar el agregado igualmente.** Si la cola se corta por «no entra en las N primeras» y se rotula con una frecuencia, el mismo error estará en la lista de mapas, que usa la misma construcción — `BuildDeckTailRow` dice en su propio comentario que está hecha «like the maps list's own tail row, deliberately».

## Lo que el código ya hace bien y no hay que romper

- **`BuildDeckCountCell` no dibuja nada** cuando la muestra no llega al mínimo: ni un guion ni un «0 %». Su comentario dice que es «the same rule the civilization balance follows, for the same reason». Con las bandas, esas cartas simplemente caen en la cola.
- **`MpStatsDecksNotResolved` / `MpStatsDecksPartlyResolved`** distinguen el caso en que *algunos* nombres son identificadores del caso en que lo son todos, y el comentario explica que el mixto era el común y el que no tenía explicación. Consérvalo.
- **El contador de colaboradores** se muestra siempre: «this is opt-in, so a table built from three people must say it was built from three people».
- **El estado de despliegue vive en la pestaña** (`_deckCivsOpen`, `_deckTailsOpen`, `_deckCardsOpen`) porque `RenderStatsTab` reconstruye la página entera en cada payload. Si el selector de civilización sustituye al acordeón, la civilización elegida tiene que vivir en el mismo sitio.

## Tokens

Los mismos del resto del launcher.

| Uso | Hex |
| --- | --- |
| Fondo de la pestaña | `#0f1c2e` |
| Contenedor del mazo | `#0d1828` |
| Tarjeta de carta | `#12213a` · borde `rgba(130,175,255,.13)` · consenso `rgba(47,127,224,.5)` a 2 px |
| Pastilla de civilización | `#12213a` · activa `#2f7fe0` |
| Porcentaje de banda | consenso `#8cbcf5` · resto `#b9c9de` |
| Nombre de carta | consenso `#f0f5fb` · resto `#c3d2e5` |
| Texto atenuado | `#8ea4c0` → `#6d829d` → `#5f7592` |
| Caja de invitación | `rgba(47,127,224,.08)`, borde `rgba(47,127,224,.22)`, texto `#bcd3ee` |

**Tipografía.** UI en Segoe UI. **El porcentaje de banda en monoespaciada, 15 px, 700.** Nombre de carta 10.5/1.32, máximo dos líneas. Etiqueta de sección 10.5 SemiBold `letter-spacing:.6px`. Conteos y cifras en monoespaciada.

**Medidas.** Tarjeta de carta **104 × 118** (arte 74 + nombre 44) · separación 10 · pastilla de civilización 32 de alto · relleno del contenedor 16 · separación entre bandas 18.

**Regla de etiqueta.** Ni el porcentaje de banda, ni su frase, ni el conteo de la derecha se parten en dos líneas ni se recortan. Lo único que se recorta es el nombre de la carta, y a dos líneas.

## Datos

Antes de implementar, verifica y dime:

- **Cómo elige `DeckStatsView` la cola.** Si el criterio es «no entra en las N primeras» y la etiqueta dice «vistas una vez», ese es el fallo de 25b — y probablemente también está en la lista de mapas.
- **El denominador por civilización.** El porcentaje se calcula con él, pero la UI no lo expone. Confírmame que está disponible para poder escribir «in 5 of the 6».
- **Si el jugador tiene una civilización preferida derivable** de su historial, para ponerla primera. Si no, la primera pastilla es la de más mazos.
- **Cómo se comparte un mazo hoy**, si es que existe. El botón «Share my deck» del prototipo asume un flujo que no he visto.

### Procedencia de los datos del prototipo

Las 27 cartas de 25a y sus cuatro porcentajes salen de las capturas tercera y cuarta del usuario; **ninguna está inventada**. Con dos salvedades:

- **La cuarta captura se corta en «3 Peasants»**, así que la banda del 50 % puede seguir más abajo: siete es lo que cabía en la imagen.
- Por eso el **31** de la fila final (`58 − 27`) es un **máximo**, no el número.

Las civilizaciones del selector distintas de Germans están ahí por el layout; sus datos no se muestran.

## Archivos de este paquete

- `Prototipo.html` — 25a y 25b.
- `PROMPT.md` — texto listo para pegar en Claude Code.
