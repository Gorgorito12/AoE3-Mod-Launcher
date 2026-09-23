# Insignias de rango por edades

Rango visible para cada jugador, basado en las edades de AoE 3, con una insignia que brilla más cuanto más alto es el rango. Se muestra en tres sitios: **Clasificación**, **lista de salas** y **panel de jugadores de la sala**.

Repo: `Gorgorito12/AoE3-Mod-Launcher`, rama `main`, proyecto WPF `WarsOfLibertyLauncher/`.

| Archivo del paquete | Qué contiene |
| --- | --- |
| `Prototipo-insignias.html` | La insignia en sí: 43a escala de seis rangos, 43b extremos a tamaño grande, 43c lista, 43d por qué, 43e nombres alternativos para el 1.º, 43f-h tres marcas de TOP 5 (**la elegida es 43g**) |
| `Prototipo-pantallas.html` | Cómo se aplica en tus pantallas reales: 45a Clasificación, 45b fila de sala, 45c panel de jugadores |

Los dos HTML son **referencia de diseño**, no código para copiar. Recrea en WPF/XAML con los estilos que ya existen. Todo el texto pasa por `Localization/Strings.cs` (español e inglés).

| Qué | Archivos del repo |
| --- | --- |
| Tabla de Clasificación | `Controls/MultiplayerTab.xaml.cs` (`BuildRankingHeader`, `BuildLeaderboardRow`), `Services/Multiplayer/RankingTableLayout.cs`, `WarsOfLibertyLauncher.Tests/RankingTableLayoutTests.cs` |
| Datos del ranking | `Models/Multiplayer/LobbyDtos.cs` (`LeaderboardRow`), `Services/Multiplayer/CommunityStatsView.cs` |
| Fila de sala | `Controls/MultiplayerTab.xaml.cs` (`BuildRoomCard`) |
| Panel de jugadores | `LobbyWindow.xaml` + `.xaml.cs` |
| Provisional | `MatchOutcomeView.ProvisionalRd` |

---

## 1. La regla que decide todo: la edad sale del ORDEN, no del número impreso

Lo dice `RankingTableLayout.cs`: la tabla se ordena por el **rating conservador** (`rating − 2·rd`, `LADDER_ORDER_BY` en el servidor), no por el rating que imprime. Por eso los números no bajan en orden: AleReis tiene 1720 y va 4.º.

**Si la edad saliera del rating impreso**, AleReis tendría el rango más alto de la tabla y aparecería debajo de tres jugadores de rango menor. La tabla y la insignia se contradirían en cada fila.

Así que:

- La edad se calcula a partir de la **posición en el ladder** que manda el servidor (o del rating conservador, si se usan umbrales). Nunca del rating impreso.
- **Soberano es el puesto 1 del servidor**, sin renumerar. Se gana por posición y cambia de manos en cuanto alguien te adelanta.
- **Descubrimiento** es la edad de quien no tiene partidas decididas y por eso no entra en la tabla. No aparece en la Clasificación; sí en salas, sala y perfil.

### Reparto propuesto, para una comunidad pequeña (14 jugadores)

Por puesto, no por umbrales de ELO:

| Edad | Puestos |
| --- | --- |
| Soberano | 1.º |
| Imperial | 2.º-3.º |
| Industrial | 4.º-6.º |
| Fortalezas | 7.º-10.º |
| Colonial | resto con partidas decididas |
| Descubrimiento | sin partidas decididas |

Cuando la comunidad crezca, se puede pasar a umbrales sobre el rating conservador. Los que aparecen en 43a (1750+, 1550-1749…) son de ejemplo: con tus datos de hoy nadie sería Imperial.

**Déjalo en un solo método puro**, sin WPF (`AgeFor(position, decidedMatches)` o similar), con tests como los de `RankingTableLayoutTests`. Lo usan las tres pantallas.

---

## 2. La insignia

Escudo heráldico con el **puesto** dentro (en la Clasificación) o el numeral de la edad (en la escala). Forma, declarada **una sola vez** como geometría en recursos:

```
polygon(6% 0%, 94% 0%, 100% 15%, 96% 60%, 50% 100%, 4% 60%, 0% 15%)
```

Las dos facetas usan la misma base partida a la altura del 40-50 %. Si placa y facetas no comparten exactamente la geometría, aparece una costura de 1 px en el borde.

### Colores: la saturación y la temperatura suben con la edad

| Edad | Claro | Medio | Tinta | Resplandor (rgb) |
| --- | --- | --- | --- | --- |
| Soberano | `#ffffff` | `#ff2f45` | `#2e0209` | `255,72,98` |
| Imperial | `#ffd76b` | `#a742ff` | `#1d0636` | `198,112,255` |
| Industrial | `#ffc247` | `#e06a12` | `#2b1000` | `255,152,42` |
| Fortalezas | `#a8ecff` | `#1e9fd4` | `#03151f` | `72,202,245` |
| Colonial | `#f0a75a` | `#a85c1e` | `#241002` | `236,146,62` |
| Descubrimiento | placa plana `#2b332c` / `#161c17`, numeral `#78837a` | — | — | sin luz |

El velo oscuro sobre la placa **se retira según subes**: más opaco en los rangos bajos y más transparente en Imperial y Soberano, que enseñan más color propio. Los valores exactos de cada capa están en el prototipo.

**Descubrimiento no tiene color ni luz** a propósito: si el primer peldaño ya brilla, subir no se nota.

### Las capas de luz, por rango

Se acumulan: cada rango tiene las del anterior más las suyas. Duraciones reales del prototipo:

| Edad | Capas | Duraciones |
| --- | --- | --- |
| Descubrimiento | ninguna | — |
| Colonial | lámina de luz | 10 s |
| Fortalezas | + aura, 2 facetas, reflejo, golpe del número | aura 6,4 · facetas 5,5 · lámina 8 · reflejo 8,6 · golpe 8,6 |
| Industrial | + filo exterior, luz de canto, **5 chispas de forja** | canto 6,2 · chispas 3,0-4,4 |
| Imperial | + brillo interior giratorio, **7 motas de oro** | aura 5,6 · interior 10,3 · lámina 8,8 · reflejo 9,8 · motas 3,3-4,8 |
| Soberano | + halo doble contrarrotante, banderas, **10 estrellas** (14 a partir de 60 px) | aura 5,2 · halo 8,0 / 12,2 · facetas 4,3 · lámina 9,4 · reflejo 11,5 · estrellas 4,3-6,2 |

**La luz es lenta, y más lenta cuanto más alto el rango.** El reflejo cruza el escudo en el 34 % del ciclo en Colonial, 46 % en Industrial y Fortalezas, 58 % en Imperial y 70 % en Soberano. Banda ancha de seis paradas, curva `cubic-bezier(.33,0,.18,1)`.

**El número crece un 5 % justo cuando el reflejo pasa por el centro.** Como el centro queda al 38 % del recorrido y la curva frena al final, el pico no cae a mitad: está medido al **12,4 %** (Industrial/Fortalezas), **15,7 %** (Imperial) y **18,9 %** (Soberano) del ciclo. El golpe y el reflejo comparten duración y retardo. Si cambias una curva, vuelve a medir.

### Chispas: ningún escudo repite patrón

Cada escudo sortea **posición, tamaño, duración y retardo** de cada chispa, con semilla estable (por jugador o por puesto). Dos Imperiales seguidos en la lista nunca centellean igual ni a la vez.

- **Industrial:** chispas pequeñas que suben y se apagan.
- **Imperial:** motas doradas de distintos tamaños que giran al brillar.
- **Soberano:** estrellas de cuatro y ocho puntas, algunas fuera del borde del escudo.

### Numeral

Monoespaciada con cifras alineadas (la misma que ya usan los números de la tabla), con un velo radial oscuro debajo para que se lea sobre la luz. **No uses Georgia**: sus cifras son de estilo antiguo y bailan de tamaño.

---

## 3. Dónde va

### 45a · Clasificación

- La insignia **sustituye al número de la columna #** (44 px) y lleva dentro el puesto del servidor. 24 px de ancho; 28 px la del 1.º.
- **TOP 5 = bloque de honor (43g):** las cinco primeras filas en un panel un tono más claro, con cabecera «TOP 5» en dorado y una línea. **Sin margen lateral**: el marco va con borde interior. Si el bloque estrecha las filas, las columnas se desalinean con la cabecera, que es justo lo que `RankingTableLayout` existe para evitar.
- La fila del 1.º lleva barra de acento a la izquierda.
- **No toques los anchos de columna.** Se reparten como ya dice `RankingTableLayout.All`: PLAYER y RATING a partes iguales con poco sitio, PLAYER se para en 340 px cuando sobra. El prototipo lo reproduce con `min(340px, calc((100% - 430px) / 2))`.
- Barra, banderas, DECID., W-L y % no cambian.

### 45b · Fila de sala

- En la celda HOST, la insignia (17 px) **ocupa el sitio del avatar**. Así la columna se queda en sus **152 px** de `BuildRoomCard`: con avatar e insignia a la vez no cabe.
- El ELO va al lado en su pastilla. El nombre es lo único que se recorta.

### 45c · Panel de jugadores de la sala

- Insignia (24 px) junto al avatar de cada jugador. La segunda línea añade la edad en texto: `1383 ELO · you · Colonial`.
- El hueco de «esperando rival» no lleva insignia.

---

## 4. Implementación en WPF

- **Geometría del escudo en recursos**, referenciada por placa, facetas y filo. No copiada.
- **Un pincel por rango**, declarado una vez.
- Giros de halo y brillo interior: gira el **pincel** (`RelativeTransform` con `RotateTransform`), no la forma. El interior va con un `OpacityMask` con la silueta.
- Chispas: elipses o `Path` pequeños con `Opacity` y `ScaleTransform`. Pasan casi todo el ciclo apagadas.
- **El coste va con el rango:** Descubrimiento y Colonial no animan casi nada, y hay un solo Soberano. En una tabla normal casi todo es barato.
- **Nada de `Clip`** en el contenedor de la insignia: el halo y las banderas del Soberano salen unos 6 px por cada lado.
- **`RenderStatsTab` reconstruye la pestaña en cada payload.** Deriva los retardos y la semilla de las chispas del **puesto o del id del jugador**, nunca de un contador, o el patrón cambiará en cada refresco.
- **Respeta la preferencia de animaciones del sistema** (`SystemParameters.ClientAreaAnimation`): si están desactivadas, la insignia se queda con sus colores y sin moverse.
- Anima solo lo que está en pantalla.

---

## 5. Lo que hay que confirmar antes de empezar

- **De dónde sale la edad en salas y en la sala.** La fila de sala solo trae el ELO del anfitrión, no su puesto. Opciones: que el servidor envíe la edad (o el puesto) en los DTO de sala y jugador, o que el launcher la saque del ranking que ya tiene cargado. Decide cuál y dímelo.
- **Si `LeaderboardRow` trae el `rd`** que hace falta para saber quién es provisional. `RankingTableLayout` dice que sí viaja en cada fila.
- **Cuántas partidas decididas hacen falta para salir de Descubrimiento.** Usa el mismo criterio que ya decide quién entra en la tabla, no un número nuevo.
- **El nombre del 1.º es Soberano.** Hay alternativas en 43e (Libertador, Generalísimo, Leyenda), pero la elegida es Soberano.
