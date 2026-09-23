# Texto para pegar en Claude Code

Copia esta carpeta dentro del repo (p. ej. `docs/design_ranking_card_banner/`), abre una terminal ahí, lanza `claude` y pega esto:

---

Lee `docs/design_ranking_card_banner/README.md` completo antes de escribir código. Es un arreglo de la tarjeta **Ranking** de «Community activity», en la pestaña Rooms: corregir el reparto de edades y añadir un banner del color de cada medalla. `Prototipo.html` es la referencia (47a y 45e); no lo copies, recrea en WPF/XAML.

**No toques nada más:** ni la tabla completa de Clasificación, ni «Community matches», ni «Peak hours».

## Antes de escribir código

1. **El reparto de edades está mal.** En la build actual la tarjeta enseña dos Soberanos (1.º y 2.º), dos Imperiales (3.º y 4.º) y un Industrial en 5.º. Debería ser Soberano 1.º, Imperial 2.º-3.º, Industrial 4.º-6.º. Encuentra la causa y dímela antes de arreglarla: ¿desfase de índice, o esta tarjeta calcula la edad por su cuenta en vez de usar el método compartido?
2. **Comprueba si la tabla completa de Clasificación tiene el mismo fallo.** Si comparten método, sí; dime qué ves.
3. Dime dónde viven los colores de resplandor de la insignia, para reutilizarlos en el banner en vez de declararlos otra vez.

## Reglas

- **Arregla el reparto en el método compartido** y añade un test que fije la edad de los puestos 1 a 7. Si la tarjeta tenía su propia lógica, bórrala.
- **Banner:** degradado horizontal del color de la edad, más intenso cuanto más alto el rango (tabla del README). Filo izquierdo de 2 px en ese color en vez de la barra blanca actual.
- **La fila NO recorta.** La insignia del Soberano sale 6-14 px fuera del escudo. La luz que recorre su banner va en su propia capa con `CornerRadius` 7 y `ClipToBounds` solo en esa capa, debajo del contenido.
- **Filas de 44 px** que no crecen. **Cada insignia en una casilla fija de 30 px**, para que avatar y nombre queden en la misma x en las cinco filas.
- Reutiliza los colores de la insignia, no declares otros.
- Respeta las animaciones desactivadas del sistema.
- Compila y pasa los tests antes de darlo por terminado.

Cuando esté, enséñame una captura de la tarjeta con las cinco filas.
