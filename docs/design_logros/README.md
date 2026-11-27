# Logros · medallas por juego (diseño 121)

`Logros.dc.html` tal como salió del proyecto de diseño. Abrirla necesita `support.js`, que va en
esta misma carpeta, y las portadas e iconos de `mods/wol` y `mods/aoe3-tad`, copiados también.

Es una función nueva de punta a punta: las medallas las calcula el **servidor**
(`wol-launcher-lobby-node`, `src/achievements/`) y el launcher solo las dibuja. Sin el servidor
nuevo (respuesta 404) la sección no existe.

## Qué se construyó

- **121a / 121c · la sección «Logros» del Perfil.** Entrada propia en el carril con la cuenta
  «14/40», las tres últimas medallas pequeñas bajo el nombre, la vitrina (portada del juego
  difuminada, anillo con el total, selector de juego, leyenda de metales, ÚLTIMOS) y las fichas por
  grupo, cuatro por fila (tres o dos en ventanas estrechas). Cada ficha: ganada con su fecha, con
  avance («26 / 100», el aro de una medalla de ELO se llena desde 1500) o bloqueada («Sin ganar»);
  «Ascenso» con sus cinco edades y «Siguiente: llega a…». De otro jugador: las mismas fichas, sin
  sus números de ELO; si su ELO es privado, un aviso en lugar de las medallas.
  `Controls/MultiplayerTab.ProfileAchievements.cs`, reglas en `Services/Multiplayer/AchievementsView.cs`.
- **121b · la tarjeta de «logro desbloqueado»** en el centro de la ventana
  (`Controls/AchievementUnlockOverlay.cs`): velo, tarjeta 560 × 428, rayos dorados girando, seis
  destellos, la medalla de 132 que entra girando, un brillo que la cruza una vez, el nombre, la
  condición, «Medalla de oro · 15 de 40 en Wars of Liberty», Cerrar y «Ver mis medallas». Se cierra
  sola a los 8 s, con Esc o con un clic fuera.
- **121d · el aviso pequeño**: el aviso flotante de siempre (`AppToast`) con una medalla en lugar
  del pictograma, el tono dorado, una sola fila y el botón «Ver».
- **La campana**: un aviso por medalla (`NotificationKind.AchievementUnlocked`), sin globo de la
  bandeja: la medalla ya tiene su aviso.

**Cuál sale** (`Services/Multiplayer/AchievementCelebration.Decide`): la tarjeta, si se está
mirando la ventana, no hay sala, búsqueda ni partida, y es una sola medalla; el aviso pequeño si
no, o si llegan varias; con un juego abierto, solo la campana.

Para verlo sin ganar nada: `--demo-elo=profile:achievements` (WoL) y
`--demo-elo=profile:achievements-tad`, y `--preview-dialog=achievement:card`, `:notice` y
`:several` (las vistas previas se quedan 40 s).

## Dónde no se siguió al pie de la letra, y por qué

- **El borde metálico no es un degradado cónico**: WPF no lo tiene. Se calcula una vez como imagen
  (`Medal.Conic`) y se reutiliza. El aro de avance es un arco.
- **Sin desenfoque detrás de la tarjeta central**: solo el velo oscuro.
- **Sin interletrado** (WPF no lo tiene); la serif es la del launcher.
- **Las animaciones** siguen la nota del diseño, a 30 fps; los relojes que se repiten se paran por
  su controlador al cerrar y mientras nadie puede verlos. Con los efectos reducidos no se mueve
  nada.
- **El aviso pequeño conserva la barra de tiempo** que llevan todos los avisos que se cierran solos
  (diseño 105); 121d no la dibuja.
- **«Clásico»** es una regla real de «Crear sala», distinta de «Estándar»: la nota del diseño que
  dice lo contrario se equivoca, y la medalla pide ganar con la regla Classic.
- **La cuenta de cada grupo** cuenta logros, como la lámina («3/6», «Ascenso» es uno); la del total
  cuenta medallas («14/40», «Ascenso» son cinco).

## Más allá de la lámina (26 de noviembre de 2026)

El catálogo del servidor ya no es el de la lámina: Wars of Liberty pasa de 36 logros / 40 medallas a
**59 / 63**, The Asian Dynasties de 37 / 41 a **48 / 52** y los demás mods quedan en **46 / 50**. Las
láminas 121a y 121c y sus datos de ejemplo no cambian; la vista con el catálogo nuevo es
`--demo-elo=profile:achievements-more`, generada desde el `catalog.ts` del servidor.

- **Para todos los juegos:** Mariscal de campo (500 partidas), Constante (30 días distintos),
  Invencible (10 seguidas), Revancha, Cosmopolita (100 rivales), Todoterreno (1v1, 2v2 y 3v3), tres de
  partida rápida, En el cuadro y Finalista.
- **Solo Wars of Liberty:** cuatro continentes que suman sus 48 civilizaciones y el grupo
  «Guerras históricas» (8 enfrentamientos, ganando con cualquiera de los dos bandos).
- **TAD:** su grupo «Generales» se queda con las personales y aparecen «Formatos» y «Comunidad».

Reutilizan los 21 iconos y los siete esmaltes de la lámina; no hay dibujo nuevo. Los nombres largos
ocupan dos líneas en la ficha y las condiciones en inglés se escribieron cortas para no pasar de tres.

### Hazañas: el platino (26 de noviembre de 2026)

Nueve logros muy difíciles cierran cada juego en un grupo «Hazañas», y con ellos los totales quedan
en **68 / 72** (Wars of Liberty), **57 / 61** (TAD) y **55 / 59** (los demás): Gran maestro (1900
de ELO en 1v1), Número uno (el primero de una clasificación de 10 o más), Racha de hierro (10
competitivas 1v1 seguidas), Centurión y Legión (100 competitivas ganadas, 1v1 y en equipo),
Emperador (250 victorias), Virtuoso (100 con la misma civilización), Triple corona (3 torneos) y
Leyenda viva (tres de cada cuatro medallas del juego, contando las ya guardadas).

**Aquí sí me salgo de la lámina:** dibuja tres metales y siete esmaltes. El **platino** es un cuarto
metal (borde blanco con brillo cian y lila, para que nunca se lea como plata) y el **octavo esmalte**
es un acero oscuro, para que ese borde destaque. La leyenda muestra «Platino» solo si el juego tiene
alguna, así 121a y 121c siguen con sus tres. La tarjeta de logro desbloqueado cambia su luz dorada por
una fría cuando la medalla es de platino; el botón «Ver mis medallas» sigue dorado. Un launcher viejo
dibuja el platino como bronce. Para verla: `--preview-dialog=achievement:platinum`.
