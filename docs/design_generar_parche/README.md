# Generar parche

Rediseño del diálogo «Generate patch», la herramienta de mantenedor que escribe los parches delta de un release. Referencias visuales en `Prototipo.html`: **18a** (el formulario reorganizado) y **18b** (el explicador como diagrama plegable). Las dos son la misma ventana: 18a es su estado normal, 18b lo que aparece al pulsar «Cómo funciona».

Repo: `Gorgorito12/AoE3-Mod-Launcher`, rama `main`, proyecto WPF `WarsOfLibertyLauncher/`.

| Qué | Archivos del repo |
| --- | --- |
| El diálogo | búscalo por el título «Generate patch» — **no localicé el archivo** |
| El motor | `Services/DeltaPatchService.cs` |
| Lo que fija su comportamiento | `WarsOfLibertyLauncher.Tests/DeltaPatchTests.cs` |
| Textos | `Localization/Strings.cs` |

**Esto se diseñó desde capturas.** No leí el diálogo ni `DeltaPatchService`; lo único que consulté del repo son los nombres de test de `DeltaPatchTests`. Los nombres de archivo, las versiones y los tamaños del prototipo son ilustrativos.

El HTML es una **referencia de diseño**, no código para copiar. Recréalo en WPF/XAML con los estilos y controles que ya existen. Todo el texto pasa por `Localization/Strings.cs` (español e inglés).

## 1. Una de las tres secciones ya lo hace bien

El diálogo pide seis datos que forman **tres pares** — un .zip y su tag por cada release. Y los agrupa así:

| Sección actual | Qué contiene |
| --- | --- |
| `SOURCE OVERLAYS` | los dos .zip (anterior y nuevo) |
| `VERSION TAGS` | los dos tags (from y to) |
| `BASELINE RELEASE` | **el .zip de la base y su tag, juntos** |

La tercera sección hace lo correcto. Las otras dos parten sus pares y los separan unos 400 px, así que la relación entre «el .zip anterior» y «su tag» hay que reconstruirla cruzando la pantalla. **La misma ventana contiene el patrón correcto y su contrario.**

No hay que inventar nada: **extiende a las tres el patrón que la de baseline ya usa.** Tres tarjetas, una por release, cada una con su .zip y su tag:

1. **Release anterior** — «ya publicado». Icono neutro.
2. **Release nuevo** — «el que vas a publicar». Icono azul, y el tag es el campo con foco al abrir.
3. **Base** — «opcional · recomendado». Icono violeta y fondo más apagado, porque se puede dejar vacía.

### El síntoma que confirma el diagnóstico

En una sola pantalla hay **seis palabras en mayúsculas** puestas para desambiguar: `OLD`, `NEW`, `EXACTLY`, `ONCE`, `TWO`, `FRESH`. Son la muleta de una estructura que no distingue sus propios campos. Con cada release en su tarjeta, «the OLD version» y «the NEW version» sobran: lo dice el título de la tarjeta.

**Al reagrupar, quita esas mayúsculas.** Si al terminar sigues necesitando gritar en una etiqueta, la estructura no ha quedado clara.

## 2. Los dos parches son el producto, y no se ven juntos

El explicador dice en prosa que la herramienta escribe **hasta dos parches**, pero el formulario está organizado por tipo de entrada, así que en ningún momento se ven los dos resultados juntos.

Añade una caja **SE ESCRIBIRÁ** con los nombres reales que se van a generar:

```
patch-v1.2.0d-to-v1.2.0e.zip     incremental
patch-v1.2.0d-to-v1.2.0e.json    su índice
```

Y una línea, en el mismo violeta de la tarjeta de base, diciendo que **el acumulativo aparecerá ahí en cuanto se rellene la base**. Así la relación entre rellenar un campo opcional y obtener un segundo parche es visible antes de pulsar el botón, no un párrafo que hay que leer.

Encima de la caja, a la derecha: «todo al release v1.2.0e». Es el destino de los archivos y hoy solo se dice dentro del explicador.

## 3. Más de 300 palabras de explicación para seis campos

Súmalas: el subtítulo, los cinco párrafos de «How this works» y las cuatro ayudas en cursiva. **La instrucción pesa más que el formulario**, y tiene una consecuencia medible: el explicador ocupa unos **250 px del arranque**, así que al abrir la ventana lo primero que se ve es la explicación, no los campos.

Y hay una prueba de que además no sirve donde está: en la captura del usuario se lee «*ow this works*» **cortado por el scroll** — el bloque se había ido justo cuando hacía falta.

Los cambios:

- **El explicador nace plegado**, tras un botón «Cómo funciona» en la cabecera. Recuerda el estado entre aperturas.
- **Una línea de ayuda por campo**, dentro de su tarjeta. Las cuatro ayudas actuales tienen entre 18 y 78 palabras; ninguna necesita más de una frase si la tarjeta ya dice de qué release habla.
- **La ayuda que sea un aviso, se saca de la cursiva.** Ver el punto 4.

## 4. El fallo silencioso es un aviso, no una nota al pie

La frase más importante de la ventana está al final del tercer renglón de un párrafo en cursiva gris:

> «If a tag doesn't match, the launcher can't tell which versions the patch bridges, so it ignores it in silence and every player downloads the whole mod instead. **Nothing errors, so you would not notice.**»

Un fallo que no da error y que hace que todos tus jugadores descarguen el mod completo es lo peor que puede salir de esta ventana. Va en **caja ámbar**, con titular propio: «Si un tag no existe en GitHub, nadie se entera».

**Y corrige la severidad.** La ayuda actual dice que los tags deben coincidir `EXACTLY`. `DeltaPatchTests` tiene un caso llamado `SelectPatch_IsCaseInsensitive`, que encuentra el parche de `"v1.0"` buscando `"V1.0"` — **si eso es cierto, las mayúsculas no importan** y «EXACTLY» asusta más de lo que el propio código exige. Verifícalo en `DeltaPatchService.SelectPatch` antes de suavizar el texto; lo que sí hay que decir es que el tag **tiene que existir** en la página de releases.

## 5. El explicador es un dibujo, no cinco párrafos

Lo que la prosa describe es una cadena de tres releases y dos parches. Eso se dibuja, y ocupa un tercio del espacio:

```
[ v1.2.0a ]   [ v1.2.0d ]   [ v1.2.0e ]
 base           anterior      nuevo
                └─ incremental ─┘
 └────────── acumulativo ───────┘
```

**Detalle de implementación que importa:** las tres filas tienen que estar en la **misma rejilla de tres columnas**, y cada barra declara su tramo — incremental columnas 2-3, acumulativo 1-3. En la primera versión del prototipo puse los nodos en una fila de tres y las barras en filas de dos, y la barra del acumulativo se quedaba 167 px antes del release al que apunta. En un diagrama la posición **es** el significado; si las barras no llegan a su nodo, el diagrama miente. Es la misma regla que las specs del cuadro de torneo de este proyecto.

Bajo el diagrama, tres viñetas cortas —para qué sirve cada parche y quién elige la ruta— y una línea verde con la idea de fondo: publicas el mod completo una vez, y a partir de ahí solo parches.

## 6. Detalles menores

- **Tres «Browse…» en dorado sólido** más «Generate patch» en dorado sólido: cuatro elementos sólidos en la ventana. Los de examinar pasan a fantasma; el único sólido es el que genera. Es la regla que documenta `CreateTournamentDialog.xaml.cs`: *«ONE solid element, and it is the one that does the thing»*.
- **Los campos de ruta miden ~2400 px.** Acota la columna de contenido; una ruta no necesita el ancho de un monitor.
- **El botón dice «Generar parches»**, en plural: escribe hasta dos.
- **`Close` no es un botón sólido**, es un enlace.

## Tokens

Los mismos del resto del launcher.

| Uso | Hex |
| --- | --- |
| Fondo del diálogo | `#0f1c2e` |
| Tarjeta de release | `#12213a` · la opcional `#101d31` |
| Campo | `#0d1828` |
| Borde interior | `rgba(130,175,255,.08–.20)` · campo con foco `rgba(47,127,224,.55)` a 2 px |
| Caja de salida | `rgba(47,127,224,.10)` con borde `rgba(47,127,224,.28)` |
| Azul (release nuevo, incremental) | `#2f7fe0` · texto `#8cbcf5` / `#a8ccf5` / `#8fb6ea` |
| Violeta (base, acumulativo) | `#cba7f2` / `#c9b3e8` / `#b39fd0` sobre `rgba(160,110,240,.12–.16)` |
| Ámbar del aviso | `#e6b455` · texto `#d8bd8a` / `#f0dcae` sobre `rgba(230,180,85,.08)`, borde `rgba(230,180,85,.22)` |
| Verde de la idea de fondo | `#4fd68a` sobre `rgba(53,196,111,.16)` |
| Texto | titular `#f4f8fc` · primario `#f0f5fb` · cuerpo `#b9c9de` · dato `#dce7f5` · atenuado `#8ea4c0` → `#6d829d` → `#5f7592` |

**Tipografía.** UI en Segoe UI; los titulares en serif. **Rutas, tags y nombres de archivo en monoespaciada** — son datos que se copian y pegan. Etiquetas de sección 10.5 px SemiBold `letter-spacing:.6px`. Ayudas 10.5 px.

**Medidas.** Diálogo 700 de ancho · campo 32 · botón de examinar 32 · botón primario 36 con `MinWidth` 136 · columna de tag 158 · icono de tarjeta 20 · separación entre tarjetas 9.

**Regla de etiqueta.** Ninguna etiqueta de sección, nota a la derecha ni tag se parte en dos líneas: van sin envolver y con un espaciador flexible que absorbe la holgura.

## Datos

Antes de implementar, verifica y dime:

- **Dónde vive el diálogo.** No lo localicé en `main`.
- **Si `SelectPatch` compara los tags sin distinguir mayúsculas.** El nombre del test lo sugiere; de ello depende suavizar «EXACTLY».
- **Si el motor puede dar el recuento de archivos cambiados y el tamaño del parche** al terminar. Si los da, la pantalla de resultado merece existir. `DeltaPatchTests` tiene un caso `ShouldAdviseRebaseline(51, 100)`, así que el servicio **ya sabe** cuándo el parche pasa de media descarga completa y toca publicar una base nueva: ese juicio hoy no aparece en ninguna pantalla y es la conclusión que un mantenedor necesita. Confírmalo y dime si conviene diseñarla.

## Archivos de este paquete

- `Prototipo.html` — 18a y 18b.
- `PROMPT.md` — texto listo para pegar en Claude Code.
