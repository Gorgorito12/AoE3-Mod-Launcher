# 4 · Clasificación: color donde hoy queda gris (diseño 107)

Prototipo: `Clasificación v4.dc.html` (página) y `ReplayRowColor.dc.html` (la fila de una partida). Anterior: `Clasificación v3.dc.html` y `ReplayRow.dc.html`.

En el prototipo, 107a se abre con una tabla de 11 clasificados, 13 jugadores en posicionamiento y tú con 1 partida de 10. Es `RankingView`, en `Controls/MultiplayerTab.xaml`.

## El problema

El podio y las edades ya tienen color. Lo que se ve triste es lo que queda fuera de las edades, y con la tabla de hoy es más de media pantalla:

- **El posicionamiento:** cada jugador ocupa una fila gris tan alta como la de un clasificado.
- **«Últimas partidas»:** casi todo es «casual · sin resultado» en gris, con un punto gris.
- **«Tu posición»** mientras posicionas: gris.

## Lo que cambia

No se pide ningún dato nuevo al servidor. El podio y los colores de las edades no se tocan.

### 4.1 Posicionamiento: de filas a una rejilla de fichas azules

Azul es «en curso», como en Salas y Torneos.

**Cabecera del grupo «EN POSICIONAMIENTO»:** el texto pasa a `#A9D6F7` y la raya a un degradado de azul `#55ACEE` al 60 % a transparente. El contador («13 jugadores») y el tooltip no cambian.

**Rejilla:** debajo de la cabecera, en lugar de una fila por jugador. Columnas automáticas de 250 como mínimo, que se reparten el ancho; 6 de separación.

**Cada ficha** (toda ella abre el perfil, igual que hoy la fila):

| Parte | Valor |
| --- | --- |
| Relleno | 9 · 12 · 10 |
| Radio | 10 |
| Fondo | degradado de izquierda a derecha, azul `#55ACEE` del 14 % al 4 % |
| Borde | 1 de azul al 22 %, más un filo de luz de 1 arriba, blanco al 5 % |
| Cursor encima | un 12 % más clara |
| Avatar | círculo de 30, a la izquierda, ocupa las dos líneas; el mismo de la fila de hoy |
| Nombre | 13,5 / 600, `#DCE7F5`; se recorta con puntos suspensivos |
| Racha | el mismo chip de hoy, tras el nombre |
| Avance | a la derecha, «8/10» en Consolas 13 / 700, `#A9D6F7` |
| Casillas | segunda línea, de la columna del nombre hasta el final: tantas como partidas pide el posicionamiento; 4 de alto, radio 2, 3 de separación |

Color de cada casilla: jugada `#55ACEE`; por jugar `rgba(130,175,255,.16)`.

**Tu ficha:**

- Fondo azul del 28 % al 10 %; borde de 1,5 de azul al 75 %; nombre en blanco.
- «TÚ» tras el nombre: 10,5 / 700, `#9FD0F5`.
- Tus casillas dicen cómo te fue: victoria `#4FD68A`, derrota `#C84A4A`.
- Antes del avance, tu ELO provisional en Consolas 13 / 600 `#F0F5FB`, seguido de «?» en `#F5BD76`. Solo en tu ficha, como hoy.

### 4.2 «Últimas partidas»: la fila dice qué pasó

La fila sigue midiendo 50 de alto. El botón de descarga y sus estados no cambian.

**El punto** (6 de diámetro):

| Caso | Color | Halo |
| --- | --- | --- |
| Partida decidida | `#4FD68A` | 3 px de verde al 18 % |
| Competitiva que no contó (sin resultado) | `#F4900C` | 3 px de ámbar al 18 % |
| Casual sin resultado | `#5D6F8A` | ninguno |

**Primera línea:**

- «venció a» en `#7FE0A8`, 600. «vs» sigue en `#8C9CB1`, 400.
- El segundo nombre va en `#A8BCD2` si la partida se decidió (es el que perdió) y en `#DCE7F5` si no.
- Las banderas solo aparecen si la partida trae civilización. Si no, la segunda línea empieza al borde, sin sangría.

**Segunda línea,** en este orden:

1. **Pastilla del modo** («COMPETITIVA 1v1», «CASUAL 1v1»): 15 de alto, radio 8, relleno 0 × 6, 9,5 / 700 con espaciado de 0,04 em, 4 entre el modo y el formato.
   - Competitiva: texto `#E6C06A`, fondo dorado al 14 %, borde dorado al 45 %.
   - Casual: texto `#A8BCD2`, fondo blanco al 6 %, borde blanco al 12 %.
2. **Icono del mod:** 14 × 14, radio 3, borde interior blanco al 16 %, con el nombre del mod como tooltip.
3. **El resto**, en 11 `#8C9CB1`: «sin resultado» (en `#F5BD76` si era competitiva, gris si casual), el mapa y la duración, separados por « · ».

### 4.3 «Tu posición» mientras posicionas

De gris a azul: el rótulo «EN POSICIONAMIENTO» pasa a `#A9D6F7`, y el resplandor de la tarjeta a un degradado a 155° de azul `#55ACEE` al 32 % → al 8 % en la mitad → transparente al 85 %.

### 4.4 Detalles

- **Podio:** el número del puesto va en oro `#E6C06A` (1.º), plata `#CFD9E6` (2.º) y bronce `#D59056` (3.º), con un degradado vertical de blanco al color, que llega al 62 %. Antes tomaba el color de la edad.
- **Máximo en dorado:** en el podio y en la tabla, «máx. 1628» va en `#F3D995` cuando el jugador está en su mejor ELO (ELO actual ≥ máximo). Si no, sigue en `#C3D2E5`. El tooltip pasa a «Está en su máximo, alcanzado el {fecha}».
- **Títulos de columna como barra,** igual que en Salas: 30 de alto, margen 8 · 8 · 4, relleno 0 × 12, radio 8, fondo blanco al 6 %, borde blanco al 9 %, texto 11 / 700 `#A8BCD2`. Solo se muestra si hay al menos un jugador clasificado.
- El aviso de «nadie completó todavía el posicionamiento» lleva 8 de margen arriba.

## Lo que NO cambia

- El podio (salvo el número), las edades y sus colores, las filas de los clasificados (salvo el máximo).
- «Tu posición» cuando ya estás clasificado.
- «Destacados».
- La tabla vacía y sus botones.

## Textos nuevos

Tooltips; comprueba cuáles existen ya:

- «Está en su máximo, alcanzado el {fecha}»
- Ficha: «{nombre}: {n} de {m} partidas puntuadas. No tiene puesto hasta terminarlas.» y, en la tuya, «Tu posicionamiento: {n} de {m} partidas puntuadas.»
- Casillas: «Jugada», «Por jugar», «Partida {n}: victoria», «Partida {n}: derrota»

## Orden sugerido y capturas

Commits: títulos de columna como barra → máximo en dorado y números del podio → fila de «Últimas partidas» → rejilla de posicionamiento → «Tu posición» en azul.

Al terminar, dos capturas de la clasificación 1v1 de Wars of Liberty: arriba (podio y tabla) y abajo (posicionamiento y «Últimas partidas»).
