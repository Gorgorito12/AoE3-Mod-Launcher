# Paquete del 14 de noviembre · cinco cambios de diseño

Repo: `Gorgorito12/AoE3-Mod-Launcher`, rama `main`, proyecto WPF `WarsOfLibertyLauncher/`.

Cinco cambios independientes, pedidos el mismo día, que comparten una regla de color. Cada uno tiene su especificación y su prototipo.

| # | Qué | Especificación | Prototipo (turno) | Dónde está en el código (según las notas) |
| --- | --- | --- | --- | --- |
| 1 | Botón «Update launcher» en dorado | `specs/1_boton_actualizar.md` | `Botón actualizar launcher.dc.html` (110) | `MainWindow.xaml`, barra de pestañas |
| 2 | Diálogo «Nuevo torneo»: ningún texto cortado | `specs/2_nuevo_torneo.md` | `Nuevo torneo v3.dc.html` + `NuevoTorneo2.dc.html` (108) | `TournamentDialog.xaml` |
| 3 | Torneos: cada estado con su color | `specs/3_torneos_color.md` | `Torneos v3.dc.html` (106) | `Controls/MultiplayerTab.xaml` › `TournamentsView`, `Services/Multiplayer/BracketLayout.cs` |
| 4 | Clasificación: color donde hoy queda gris | `specs/4_clasificacion.md` | `Clasificación v4.dc.html` + `ReplayRowColor.dc.html` (107) | `Controls/MultiplayerTab.xaml` › `RankingView` |
| 5 | Estadísticas: el mismo contenido, sin huecos | `specs/5_estadisticas.md` | `Estadísticas v3.dc.html` (109) | `Controls/MultiplayerTab.xaml` › `StatsView` |

Para comparar, van también los diseños anteriores, con los que se hizo el código de hoy: `Nuevo torneo v2` + `NuevoTorneo`, `Torneos v2`, `Clasificación v3` + `ReplayRow`, `Estadísticas v2`. En `uploads/` hay dos capturas del launcher de hoy: `torneo-hoy.png` y `boton-actualizar-hoy.png`.

Abre los `.dc.html` en un navegador con `support.js` al lado (necesitan internet). Son **referencia de diseño**, no código para copiar. Los datos que muestran son de relleno.

> Los nombres de clases y archivos salen de las notas del proyecto de diseño, no del código de hoy. Si en `main` se llaman distinto, usa los equivalentes y dímelo.

## La regla de color que comparten

**El color del mod** solo va en lo que es tuyo o del mod: la fila o la opción elegida, tu partido, «TÚ» y el botón principal. Relleno de selección: el color del mod mezclado al 58 % con `#0F1C2E`.

**Todo lo demás** usa colores de estado fijos, que nunca toman el color del mod. Son los mismos de Salas:

| Color | Valor | Texto en ese color | Significa |
| --- | --- | --- | --- |
| Verde | `#4FD68A` | `#7FE0A8` | Abierto, ganado, decidido |
| Azul | `#55ACEE` | `#A9D6F7` | En curso: se está jugando, en posicionamiento |
| Ámbar | `#F4900C` | `#F5BD76` | No disponible, ausente, no contó |
| Rojo | `#C84A4A` | `#F0A6A6` | Derrota, descalificado |
| Dorado | `#E6C06A` | `#F3D995` | Campeón, torneo terminado, mejor marca, competitiva, **y hay versión nueva del launcher** |
| Neutro | `#8C9CB1` | `#C3D2E5` | Borrador, sin decidir |

Textos: `#F0F5FB` / `#DCE7F5` / `#C3D2E5` / `#A8BCD2` / `#8C9CB1`. No uses `#61779A` para texto: no se lee sobre el azul marino.

**Degradado dorado** (bloques 1, 3 y 4): cada especificación da el suyo, porque cambian el ángulo y los extremos. Declara el color base una vez y los degradados como recursos con nombre.

**Animación.** El «punto que late» es opacidad 1 → 0,45 y escala 1 → 0,72, ciclo de 1,6 s. El halo del botón dorado respira en 2,4 s. Con efectos reducidos, todo fijo.

## Qué no está en este paquete

Las filas de Salas (turno 104) y los avisos flotantes (turno 105) tienen sus propios paquetes, de días anteriores: `handoff_salas_filas` y `handoff_avisos_flotantes`. No se repiten aquí.

## Lo que no cambia en ningún bloque

- El servidor: ningún campo ni llamada nueva.
- Lo que hace cada botón y qué se muestra en cada estado.
- El podio y los colores de las edades en Clasificación; la disposición de Torneos.
