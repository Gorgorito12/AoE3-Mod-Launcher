# Texto para pegar en Claude Code

Copia esta carpeta dentro del repo como `docs/design_14nov/`, abre una terminal en la raíz del repo, lanza `claude` y pega esto:

---

Lee `docs/design_14nov/README.md` completo y después las cinco especificaciones de `docs/design_14nov/specs/`, en orden, antes de escribir código. Son **cinco cambios de diseño del launcher**, independientes entre sí, que comparten una misma regla de color. Los `.dc.html` de la carpeta son prototipos de referencia: NO copies su código; cambia el código WPF que ya existe.

Los cinco bloques, en el orden en que quiero que los hagas (del más pequeño al más grande):

1. **Botón «Update launcher» en dorado** · `specs/1_boton_actualizar.md`
2. **Diálogo «Nuevo torneo»: ningún texto cortado** · `specs/2_nuevo_torneo.md`
3. **Torneos: cada estado con su color** · `specs/3_torneos_color.md`
4. **Clasificación: color donde hoy queda gris** · `specs/4_clasificacion.md`
5. **Estadísticas: el mismo contenido, sin huecos** · `specs/5_estadisticas.md`

Si al leer el código ves que alguno ya está hecho en `main`, dímelo y sáltalo.

**No toques nada más.** Ni el servidor, ni lo que hace cada botón, ni pantallas que no salen en estas cinco especificaciones.

## Antes de escribir código, hazme un plan

1. **Lee cómo está hoy cada cosa** y dime dónde vive: el botón de actualizar en `MainWindow.xaml`; `TournamentDialog.xaml`; `TournamentsView`, `RankingView` y `StatsView` en `Controls/MultiplayerTab.xaml` con su código. Los nombres de las especificaciones salen de las notas de diseño, no del código de hoy: si en `main` se llaman distinto, dime el equivalente.
2. **Pinceles compartidos.** Dime cuáles reutilizas de `Styles/Colors.xaml` (los de estado de Salas, `MpRoomJoin*`, `MpRoomFull*`) y cuáles nuevos declaras, con su nombre. El dorado y su degradado se usan en los bloques 1, 3 y 4: decláralos una sola vez.
3. **Por cada bloque**, una lista corta: qué archivos tocas, y qué choca con el código, con `CLAUDE.md` o con tests que fijen colores o medidas. Donde choque, dime qué propones en vez de forzar la especificación.
4. **Las comprobaciones que piden las especificaciones:** dónde sale hoy la etiqueta DQ en el cuadro (bloque 3), qué hace exactamente «Privado» en un torneo (bloque 2) y qué textos de los «nuevos» ya existen en `Localization/Strings*.cs`.
5. **Commits pequeños**, agrupados por bloque. Cada especificación trae su orden sugerido.

Espera a que te diga que el plan está bien.

## Reglas al implementar

- **Un bloque cada vez.** Al terminar cada uno, párate y enséñame las capturas que pide su especificación antes de empezar el siguiente.
- **Colores una sola vez, como pinceles con `DynamicResource`.** Ningún color de estado depende del mod. El color del mod solo va en la selección, en lo tuyo y en el botón principal.
- **Ningún símbolo como texto:** flechas, vistos, cruces y puntos son trazos o formas dibujadas, no caracteres (↑ ✓ ✕ ●).
- **Ningún texto con ancho o alto fijo que lo pueda cortar:** `MinWidth` / `MinHeight` y `TextWrapping`, o recorte con puntos suspensivos y tooltip donde la especificación lo diga.
- **Las animaciones** (halo que respira, punto que late) se quedan fijas con efectos reducidos.
- **Textos nuevos** en `Localization/Strings*.cs`, en español (es-419) e inglés.
- **Prueba** con las tres transparencias y con un mod rojo (Wars of Liberty), uno azul (Improvement Mod) y uno dorado (Hundred Days); en español y en inglés; a 1366 × 768 y con la ventana maximizada.
- Compila y pasa los tests antes de darme cada commit por terminado.
