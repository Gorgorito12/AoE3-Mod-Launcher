# Tarjeta Ranking con banner por edad

Arreglo de la tarjeta **Ranking** de «Community activity» en la pestaña Rooms. Tiene dos partes: corregir el reparto de edades que salió mal, y añadir un banner semitransparente del color de cada medalla.

| Archivo del paquete | Qué contiene |
| --- | --- |
| `Prototipo.html` | **47a** la tarjeta con el banner por edad · **45e** la tarjeta en su fila, junto a «Community matches» |

El HTML es una **referencia de diseño**, no código para copiar. Recrea en WPF/XAML con lo que ya existe.

Repo: `Gorgorito12/AoE3-Mod-Launcher`, rama `main`, proyecto WPF `WarsOfLibertyLauncher/`.

| Qué | Archivos del repo |
| --- | --- |
| La tarjeta | `Controls/MultiplayerTab.xaml` (estilo `MpActivityCard`) y su constructor en `MultiplayerTab.xaml.cs` |
| La insignia y el cálculo de la edad | lo que ya implementaste con `docs/design_insignias_rango/` |

---

## 1. Primero: el reparto de edades está mal

En la build actual, la tarjeta enseña:

| Puesto | Sale hoy | Debería ser |
| --- | --- | --- |
| 1.º | Soberano | **Soberano** |
| 2.º | Soberano ✗ | **Imperial** |
| 3.º | Imperial | **Imperial** |
| 4.º | Imperial ✗ | **Industrial** |
| 5.º | Industrial | **Industrial** |

Hay **dos Soberanos**, y todo el resto sale un puesto desplazado. Soberano es **solo** el 1.º. El reparto del paquete de insignias es:

| Edad | Puestos |
| --- | --- |
| Soberano | 1.º |
| Imperial | 2.º-3.º |
| Industrial | 4.º-6.º |
| Fortalezas | 7.º-10.º |
| Colonial | resto con partidas decididas |

Busca por qué. Lo más probable es uno de estos dos:

- **Un desfase de uno:** el puesto se trata como índice desde 0 en un sitio y desde 1 en otro.
- **Esta tarjeta calcula la edad por su cuenta** en vez de llamar al mismo método que la tabla completa.

**Arréglalo en el método compartido y añade un test** que fije la edad de los puestos 1 a 7. Si la tarjeta tiene su propia lógica, bórrala y haz que llame al método compartido.

---

## 2. El banner (47a)

Cada fila lleva detrás un degradado **del color de su edad**, que sale del escudo y se desvanece antes de llegar al rating.

### Degradado por fila

`LinearGradientBrush` horizontal, de izquierda a derecha, con el color de resplandor de la edad:

| Parada | Offset | Opacidad |
| --- | --- | --- |
| 1 | 0 | `A` |
| 2 | 0,34 | `A × 0,45` |
| 3 | 0,72 | 0 |

`A` sube con la edad:

| Edad | Color (rgb) | `A` |
| --- | --- | --- |
| Soberano | `255,72,98` | 0,34 |
| Imperial | `198,112,255` | 0,26 |
| Industrial | `255,152,42` | 0,20 |
| Fortalezas | `72,202,245` | 0,15 |
| Colonial | `236,146,62` | 0,11 |

Son los mismos colores de resplandor que ya usa la insignia. **No declares otros:** usa los mismos recursos.

### Filo y remate

- **Filo izquierdo de 2 px** del color de la edad, con opacidad 0,7 (0,95 en el Soberano). **Sustituye a la barra blanca** que hoy lleva el 1.º, que además se monta sobre el escudo.
- **Línea superior de 1 px** del mismo color, a `A × 0,6`.
- Esquinas de **7 px** de radio.

### La luz del Soberano

Solo el 1.º lleva una luz que recorre su banner. Datos:

- Una franja clara (`rgba(255,190,200,.16)` en el centro, transparente a los lados) de un 40 % del ancho de la fila.
- Cruza de izquierda a derecha en **9 s**, con la curva `cubic-bezier(.33,0,.18,1)`, la misma que el reflejo de su insignia.
- Aparece al empezar el ciclo, cruza hasta el 46 %, se desvanece, y la fila queda sin luz el resto del ciclo.

### Lo más importante: qué recorta y qué no

**La fila NO recorta.** El halo, las banderas y las estrellas de la insignia del Soberano salen **6-14 px** fuera del escudo, también por la izquierda y por encima y debajo de la fila. Con `ClipToBounds` en la fila, esos adornos se cortan.

La luz del Soberano sí tiene que quedarse dentro de las esquinas redondeadas. Para eso:

- La luz va en **su propia capa**: un `Border` con `CornerRadius` 7 que ocupa toda la fila, **`ClipToBounds="True"` solo en ella**, y `IsHitTestVisible="False"`.
- Esa capa va **debajo** de la insignia, el avatar, el nombre y el rating.
- El degradado va en el `Background` de la fila, que no recorta.

Es la misma regla del paquete de insignias: nunca `Clip` en el contenedor de una insignia.

---

## 3. La fila (45e)

- **44 px de alto.** Las filas no crecen por llevar banner.
- **Cada insignia en una casilla fija de 30 px**, centrada. La del Soberano es más grande (28 px frente a 25), pero la casilla es la misma, así que avatar y nombre quedan en la misma x en las cinco filas.
- Avatar 24 px, nombre en una línea con elipsis, rating en monoespaciada alineado a la derecha.
- 5 px de separación entre filas.
- La tarjeta mantiene el relleno y el radio de `MpActivityCard`.

En 45e se ve la tarjeta junto a «Community matches», que **no lleva insignia ni banner**: ahí cada línea ya tiene nombre, bandera y civilización por jugador.

---

## 4. Cómo comprobarlo

- La tabla del apartado 1 sale bien: un solo Soberano.
- Avatar y nombre en la misma x en las cinco filas.
- Ninguna parte de la insignia del Soberano aparece cortada, tampoco por la izquierda.
- La luz del Soberano no sale de sus esquinas redondeadas.
- Con las animaciones del sistema desactivadas (`SystemParameters.ClientAreaAnimation`), el banner se queda sin luz y sin moverse, con sus colores.
- `RenderStatsTab` y el refresco de la tarjeta no reinician la luz en cada payload: el retardo sale del puesto, no de un contador.
