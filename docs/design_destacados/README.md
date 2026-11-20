# Clasificación › Destacados (diseños 118 y 119)

Dos páginas del proyecto de diseño, tal como salieron de él:

- **`Destacados.dc.html` (118)** — el diagnóstico de las siete tarjetas iguales y tres salidas:
  118b (el ganador arriba), 118c (una línea por destacado) y 118d (el mosaico). 118e es el arreglo
  del selector de mes.
- **`Destacados mosaico.dc.html` (119)** — la opción 118d en su propia página, con la insignia de
  edad de cada jugador. **Es la que está construida** (119a), junto con 118e.

Abrir cualquiera de las dos necesita `support.js`, que va en esta misma carpeta.

## Qué se construyó

`WarsOfLibertyLauncher/Controls/MultiplayerTab.RankingHighlights.cs` dibuja las tarjetas,
`Controls/BentoPanel.cs` las reparte y las reglas sin ventana están en
`Services/Multiplayer/HighlightsMosaic.cs` y `HighlightLeadersView.cs`. Para verla con datos de
ejemplo: `--demo-elo=93f`.

No cambia qué destacados hay, sus reglas y umbrales, los cinco puestos, el interruptor
1v1 / Equipos, la fila propia en azul con «TÚ» ni los avisos. No se pide ningún dato nuevo al
servidor: las barras, los anillos y las marcas salen de las cifras que ya llegan.

## Dónde no se siguió al pie de la letra, y por qué

- **El selector de mes** usa la misma caja y el mismo estilo de botón que la cápsula
  1v1 · Equipos · Destacados · Partidas del launcher (relleno 4, radio 19, 16 a los lados), que
  quedan a un píxel de los 3 / 18 / 15 de la lámina. La lámina pide que los dos sean iguales, y
  lo son.
- **La insignia** es la que ya dibuja la página, tomada de la tabla cargada. Quien no está en ella
  (en posicionamiento, con el ELO privado, o más allá de la página que mandó el servidor) no lleva
  insignia y su sitio se conserva. Una sorpresa por equipos nombra a varios jugadores: tampoco.
- **El desenfoque de las tarjetas** es el cristal de la página (`GlassPanel`), como en el resto del
  launcher: sin desenfoque en vivo.
- **Sin interletrado** (WPF no lo tiene).
- En la vista previa las banderas de civilización salen como marcos vacíos: el banco de pruebas
  no tiene ningún mod instalado del que leerlas.
