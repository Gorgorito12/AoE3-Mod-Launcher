# Texto para pegar en Claude Code

Copia esta carpeta dentro del repo (p. ej. `docs/design_mazo_comunidad/`), abre una terminal ahí, lanza `claude` y pega esto:

---

Lee `docs/design_mazo_comunidad/README.md` completo antes de escribir código. Es el rediseño de la sección **«Cards the community brings»** de la pestaña Estadísticas de este launcher WPF.

`Prototipo.html` es una referencia de diseño: NO lo copies ni lo integres. Recréalo en WPF/XAML con los estilos y controles que ya existen en `WarsOfLibertyLauncher/`. **25a es el diseño; 25b documenta un fallo de datos de la versión actual**, no una pantalla a construir.

**No toques nada más de la pestaña.** El balance de civilizaciones, los enfrentamientos, los mapas más jugados, «cómo se mide» y las tarjetas de la derecha quedan como están — salvo lo que te pido comprobar en el punto 2.

## Antes de escribir código, hazme un plan

1. **Lee `Services/Multiplayer/DeckStatsView.cs`** —`DeckCivGroup`, `DeckCardRow`, `Tail`, `CivGroupsShown`— y dime cómo se construye el agregado. En particular: **de dónde sale el denominador de `row.Percent`** y si está disponible por civilización. El diseño necesita escribir «in 5 of the 6» junto al porcentaje, y hoy ese número no se muestra en ninguna parte de la UI.

2. **Confirma o descarta el fallo de 25b.** La fila de cola dice «51 more cards, seen once» y pone de ejemplo Fencing School, Fort, 3 Hussars y 8 Skirmishers; al desplegarla, esas cuatro aparecen con 5, 5, 4 y 4 mazos. Mira cómo `DeckStatsView` decide qué va a `Tail`: si el criterio es «no entra en las N primeras» y la etiqueta `MpStatsTailDecks` dice una frecuencia, la etiqueta miente. **Y comprueba la lista de mapas**, porque `BuildDeckTailRow` dice en su comentario que está hecha igual que ella. Ese arreglo puede que sea de una cadena, no del agregado — dímelo antes de tocar nada.

3. **Dime si el jugador tiene una civilización preferida derivable** de su historial, para ponerla primera en el selector. Si no, la primera es la de más mazos.

4. **Dime si existe algún flujo para compartir un mazo.** El botón «Share my deck» del prototipo asume uno que no he visto; si no existe, quítalo del diseño en vez de dejarlo sin función.

5. Dime qué claves de `Localization/Strings.cs` faltan o cambian, y cuáles quedan huérfanas al desaparecer la columna de porcentaje por fila (`MpStatsDecksCountAndShare`, `MpStatsDecksCivCards`).

6. Propón commits pequeños: primero las bandas por porcentaje, luego el selector horizontal, luego la tarjeta de carta.

## Reglas al implementar

- **El porcentaje va en el encabezado de banda, no en cada fila.** Aparece cuatro veces en vez de cincuenta, y siempre con su denominador al lado («83 % · in 5 of the 6»). El problema no era el símbolo: era estar en una columna «%» pegada a la columna «%» de victorias, con otro significado.
- **La primera banda es la respuesta de la sección** y lleva tratamiento propio (borde azul, texto más claro). Las demás son normales.
- **Las cartas se disponen en horizontal**, como el mazo de la ciudad natal en AoE 3. El nombre se recorta a dos líneas; la tarjeta nunca crece.
- **Si la muestra no da para nombrar, no se nombra.** La cola se cuenta y se explica, no se ilustra con ejemplos — que es justo lo que produce el fallo de 25b.
- **No rompas lo que ya está bien:** `BuildDeckCountCell` no dibuja nada bajo el mínimo de muestra (ni guion ni «0 %»); las dos cadenas distintas para nombres sin resolver; el contador de colaboradores siempre visible. Los tres tienen su razón escrita en el código.
- **El estado de la civilización elegida vive en la pestaña**, junto a `_deckCivsOpen` y compañía, porque `RenderStatsTab` reconstruye la página entera en cada payload.
- **Los datos del prototipo tienen procedencia declarada** en el README: las 27 cartas son reales, pero la captura de origen se corta en «3 Peasants», así que la banda del 50 % puede tener más y el «31» de la cola es un máximo. Saca las cifras del agregado, no de la maqueta.
- Cero cadenas literales: todo a `Localization/Strings.cs`, español e inglés. Porcentajes y conteos en monoespaciada.
- Respeta los valores exactos de la tabla de Tokens. Reutiliza los recursos compartidos del launcher; si creas un estilo nuevo (tarjeta de carta, encabezado de banda), hazlo compartido.
- Señala cualquier punto del README que choque con la arquitectura real, con `CLAUDE.md`, con `.claude/rules/multiplayer.md` o con un test existente, en vez de forzarlo.
- Compila y pasa los tests antes de darme cada commit por terminado.

Cuando acabes las bandas, párate y enséñame una captura antes de seguir con el selector.
