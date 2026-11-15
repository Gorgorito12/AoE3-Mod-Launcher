# 5 · Estadísticas: el mismo contenido, sin huecos (diseño 109)

Prototipo: `Estadísticas v3.dc.html`. Anterior: `Estadísticas v2.dc.html`.

En el prototipo, 109a se abre a 2560 de ancho con mucho volumen (22 civilizaciones, sin enfrentamientos, 29 mapas con más de una partida); 109b es un portátil; 109c el modo Equipos; 109d vacío; 109e cargando; 109f error. En Tweaks se cambia el tamaño de la ventana. Es `StatsView`, en `Controls/MultiplayerTab.xaml`.

## El problema

En una pantalla ancha y con datos de verdad, el espacio se pierde en cuatro sitios:

1. **Un hueco en el centro:** la columna de los enfrentamientos reserva su sitio aunque no haya enfrentamientos que mostrar.
2. **El balance** es una sola tabla de columnas fijas: con 22 civilizaciones es una tira larga y estrecha.
3. **Los mapas** ocupan 40 px cada uno, con la barra debajo del nombre, y salen todos los que tienen 2 partidas o más.
4. **Los mazos:** cada porcentaje es un renglón a todo el ancho, aunque lleve una sola carta.

## El cambio

**Los datos, las reglas y los estados son los mismos; cambia el reparto.** De arriba abajo, la página pasa a ser:

1. Cabecera (título, mod y modo): igual que hoy.
2. **Franja de actividad**, a todo el ancho.
3. **Una fila de tarjetas** que salta de línea: Balance · Enfrentamientos (y «Juegan juntas» en Equipos) · Mapas.
4. **«Lo que lleva la comunidad»**, a todo el ancho.

Desaparece la columna derecha fija. La separación entre tarjetas es la de hoy (16 desde 1600 de ancho, 12 por debajo).

### 5.1 Franja de actividad

La tarjeta «Actividad» deja la columna derecha y pasa a ser una franja encima de las demás. Mismo cristal y borde que las otras tarjetas; relleno 12 × 2. Cuatro bloques en línea, cada uno con 16 de relleno lateral y separado del anterior por una raya vertical de 1 px de blanco al 8 %. En ventanas estrechas los bloques saltan de línea.

| Bloque | Reparto del ancho | Contenido |
| --- | --- | --- |
| Total | 2,4 partes, mínimo 340 | Rótulo «ACTIVIDAD»; el total en Consolas 26 a 34 / 600; «partidas en 30 días» en 12. A su derecha, las 30 barras por día, de 52 de alto, 2 de separación |
| Tres cifras | 1 parte cada una, mínimo 150 | Número en Consolas 22 / 600 y, debajo, su texto en 12 `#A8BCD2` |
| Horas punta | 1,7 partes, mínimo 300 | Rótulo «HORAS PUNTA», el tramo en Consolas 14 / 600 y, a la derecha, «en tu hora · N salas». Debajo, las 24 barras de 30 de alto y la escala 0 h · 6 h · 12 h · 18 h · 24 h |

Las tres cifras, con color:

| Cifra | Color | Texto debajo | Tooltip |
| --- | --- | --- | --- |
| Puntuaron | `#7FE0A8` | puntuaron para el ELO | Partidas que contaron para el ELO. |
| Sin resultado | `#F5BD76` | sin resultado leído | La grabación no se pudo abrir (no_decided_result). |
| Jugadores | `#F0F5FB` | jugadores en 7 días | Jugadores distintos en los últimos 7 días. |

Los textos se acortan porque la explicación larga pasa al tooltip. Si no hay horas punta (menos de 20 salas en 30 días), ese bloque muestra la nota de hoy. Mientras carga, la franja muestra cuatro bloques provisionales de 58 de alto.

### 5.2 La fila de tarjetas

Balance, Enfrentamientos y Mapas son tarjetas de una fila que salta de línea. **Las de una misma línea tienen la misma altura.** Se reparten el ancho libre así:

| Tarjeta | Ancho base | Crece |
| --- | --- | --- |
| Balance | N × 450 + 32, donde N es su número de columnas | N partes |
| Enfrentamientos (y «Juegan juntas») | 400 | 1 parte |
| Mapas | 340 | 1 parte |

**La tarjeta de Enfrentamientos no existe si no hay enfrentamientos ni modo Equipos.** No se reserva su sitio: el balance y los mapas se quedan con él. Este es el hueco de hoy.

**En Equipos,** «Juegan juntas» va al lado de Enfrentamientos si caben las cuatro tarjetas en una línea (482 + 380 + 380 + 340 más las separaciones); en ese caso el grupo de las dos tiene ancho base 760 más una separación y crece 2 partes. Si no caben, va debajo de Enfrentamientos, como hoy.

### 5.3 Balance en 1, 2 o 3 columnas

La tabla se parte en columnas iguales, cada una con su fila de títulos, separadas 28. Las civilizaciones se reparten en orden, llenando primero la primera columna.

**Cuántas columnas (N):** el menor de estos tres números, y como mínimo 1:

- 3;
- civilizaciones mostradas ÷ 5, redondeado hacia abajo (solo se abre otra columna si todas llegan a 5 filas);
- las que caben a 450 cada una en el ancho de la página, después de restar 340 de los mapas, el ancho base de Enfrentamientos si existe, las separaciones entre tarjetas y 32 de relleno.

**Filas de 34** (antes 44 y 40). Columnas de cada tabla, con 10 de separación:

| Columna | Ancho | Título |
| --- | --- | --- |
| Civilización | 1,4 partes, mínimo 110 | CIVILIZACIÓN |
| Partidas | 40 | PART. (tooltip «Partidas») |
| Récord | 54 | RÉCORD |
| Barra de victorias | 1 parte, mínimo 56 | VICTORIAS |
| Porcentaje | 46 | % |
| Duración | 54 | DUR. (tooltip «Duración media») |

Fila de títulos de 28 de alto, 10,5 / 700, `#8C9CB1`. El contenido de cada celda, sus colores y la regla del porcentaje (solo desde 5 partidas decididas; con menos, la barra queda vacía) son los de hoy. La cola («N civilizaciones más con 3 partidas o menos», «Ver todas») y la nota del pie siguen debajo, a todo el ancho de la tarjeta.

### 5.4 Enfrentamientos y «Juegan juntas»

Solo se estrechan las columnas, para que quepan los dos nombres: pareja 1 parte · partidas 34 (título «PART.») · récord 54 · % 46, con 10 de separación. Filas de 34.

### 5.5 Mapas en una línea

Cada mapa es **una línea de 30** (antes 40 o más, con la barra debajo). Columnas, con 10 de separación:

| Columna | Ancho |
| --- | --- |
| Puesto | 18 |
| Nombre y, pegado, el chip del paquete (ESOC, KOTH…) | 1,9 partes; el nombre se recorta con puntos suspensivos |
| Barra | 1 parte, mínimo 36; 5 de alto, radio 3 |
| Partidas | 30 |

**Cuántos se muestran:** los **12 primeros** de los que tienen 2 partidas o más (antes, todos los que tenían 2 o más). El resto se pliega bajo «N mapas más, con menos partidas.» con «Ver todos»; desplegado dice «Mostrando los N mapas.» Si menos de dos mapas tienen 2 partidas o más, se toman los 12 primeros de todos.

### 5.6 Mazos en un solo bloque

Las bandas dejan de ser un renglón cada una. Todas las cartas fluyen en un solo bloque que salta de línea (6 de separación), y cada banda empieza con su porcentaje:

- **Marca de la banda:** texto sin caja, 28 de alto para alinearse con las cartas. Porcentaje en Consolas 14 / 700 `#F0F5FB` y, a 6, «18 de 19» (o «los 19» si es el 100 %) en 12 `#A8BCD2`. El texto completo de hoy («en 18 de los 19 mazos») pasa al tooltip.
- **A partir de la segunda banda,** la marca lleva delante una raya vertical de 1 px de blanco al 22 %, con 8 de margen a la izquierda y 14 de relleno.
- **Las cartas** son las de hoy: mismo chip, mismo número de envío en la esquina.

Los botones de civilización, la cola («N cartas más en 1 o 2 mazos», «Ver todas») y la nota del pie no cambian.

### 5.7 De paso

Los rótulos de tarjeta y los títulos de columna que estaban en `#61779A` pasan a `#8C9CB1`: el anterior no se lee bien sobre el azul marino.

## Lo que NO cambia

- Qué datos se piden y qué reglas se aplican: mínimos de partidas, colas, qué se calla por debajo de cada umbral.
- Los estados vacío, cargando y error: mismas piezas y mismos textos, con el reparto nuevo.
- El selector de mod y de modo.
- Que la página se desplaza entera.

## Textos

- **Cambian:** los tres textos de las cifras de actividad (más cortos, con la explicación en el tooltip), los títulos «PART.» y «DUR.», la marca de las bandas («18 de 19», «los 19») y la línea de la cola de mapas.
- **Nuevos:** los tooltips de las tres cifras y los de «PART.» y «DUR.».

## Orden sugerido y capturas

Commits: franja de actividad → fila de tarjetas sin el hueco → balance en columnas → columnas de Enfrentamientos → mapas en una línea → mazos en un bloque.

Cuando tengas la franja y la fila de tarjetas, párate y enséñame una captura con la ventana maximizada. Al terminar, tres capturas de Wars of Liberty: 1v1 maximizada, 1v1 a 1366 × 768 y Equipos.
