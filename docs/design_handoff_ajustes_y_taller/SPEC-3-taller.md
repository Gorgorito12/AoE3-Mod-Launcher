# 3 · Taller (Workshop)

Referencia visual: **6a** (lista + ficha) en `Prototipo.html`. Es la dirección conservadora: mantiene la estructura actual de la pestaña y la arregla.

Archivos del repo que cubre esta parte:

| Pantalla | Archivos del repo |
| --- | --- |
| Taller (todas las opciones) | `Controls/ModsBrowser.xaml` + `Controls/ModsBrowser.xaml.cs` (65 KB) |
| Catálogo y perfiles | `Services/ModCatalogService.cs`, `Models/ModCatalogManifest.cs`, `Models/ModCatalogCache.cs`, `Services/ModRegistry.cs` |
| Estado y datos de fila | `Services/DiskSpaceService.cs`, `Services/UpdateService.cs` |
| Esquema del catálogo | `aoe3-mods-catalog-template/schema/mod.schema.json`, `aoe3-mods-catalog-template/mods/*/mod.json` |
| Reglas de la pantalla | `CLAUDE.md` (bloque del Workshop, ~líneas 2800-2960 y 4378-4400) |

El HTML es una **referencia de diseño**, no código para copiar. Todo el texto pasa por `Localization/Strings.cs` (español e inglés); nada de cadenas literales en el XAML. La paleta es la misma que la del resto del launcher y la de los ajustes del mod.

## La regla que manda sobre todo el diseño

`CLAUDE.md` la fija y ninguna de estas opciones la toca: **el Taller nunca instala, delega en la Biblioteca.** De ahí salen los dos ejes que conviven en cada fila, y que hay que mantener separados:

- **La insignia habla del disco**: `ModRowStatus` = `NotInstalled` / `Installed` / `UpdateAvailable` / `Incompatible`, con las etiquetas que ya existen en `ModsBrowser.xaml.cs` (`BadgeNotInstalled`, `BadgeInstalled`, `BadgeUpdateAvailable`) más `LocalModBadgeLabel` para los mods locales.
- **El botón habla de la colección**: azul `Añadir a mis mods` cuando el mod está fuera, y `Ver en biblioteca` cuando ya está dentro. Nunca `Instalar`, `Actualizar` ni `Jugar` — un segundo disparador de instalación aquí duplicaría la máquina de estados.

Un mod puede estar instalado en disco y fuera de la colección, o al revés; por eso son dos controles y no uno.

## Lo que la pantalla YA hace y no hay que rehacer

Antes de tocar nada, ten presente que `ModsBrowser` ya implementa:

- **Galería de capturas** con tira de miniaturas y visor grande, cargada de forma diferida (`ScreenshotRequester` → `EnsureScreenshotsAsync`), con GIF animado y oculta cuando el mod no trae ninguna.
- **Portada del detalle** que usa `banner` si existe y, si no, `BuildBannerGradient(accent)` a partir del `accentColor`; sin banner prefiere el icono del mod al monograma de letra.
- **Versión y tamaño** en la pila derecha de cada fila, junto a la insignia y la acción.
- **Aviso de compatibilidad** (`DetailCompatBanner`) que solo aparece cuando el mod no es compatible.
- **Orden por estado**: `UpdateAvailable` → `Installed` → `NotInstalled`.
- **Sub-pestañas** Catálogo / Mis mods (`SubTabMode.MyMods` filtra `Status != NotInstalled`) y cinco filtros (`FilterMode`: All / Installed / NotInstalled / Updates / Compatible).

El rediseño es de **presentación**: reordena y jerarquiza lo que ya existe. Si una opción parece pedir un dato nuevo, comprueba primero si `ModProfile` ya lo resuelve.

## Los cuatro problemas de la pantalla actual

1. **La misma descripción, dos veces.** En la lista sale cortada a mitad de palabra a dos líneas (`…home…`, `…Beng…`) y en la ficha entera. Leer media frase truncada no ayuda a decidir nada. En el rediseño la lista muestra **una sola línea** con elipsis limpia y la ficha lleva el texto completo.
2. **El catálogo no ofrece nada.** Los cinco mods dicen `Installed` y los cinco botones dicen `See in Library`. Una pestaña de descubrimiento donde todo está ya instalado y todas las acciones son la misma no da ninguna razón para entrar. Ahora la fila indica **tamaño, fecha y estado real**, y la acción cambia con el estado: `Instalar`, `Actualizar`, `Ver en biblioteca`.
3. **La ficha se queda con ~800 px vacíos** bajo los enlaces de comunidad, y su cabecera es un degradado rojo plano con el icono flotando. Ahora lleva portada, capturas, novedades de la versión y una tabla de detalles, con la acción principal fijada al pie.
4. **Cinco filtros y un desplegable de orden para cinco mods** es más superficie de control que contenido. Los cinco `FilterMode` se conservan (hay un test que los fija, `TheWorkshopFiltersRowFitsAtTheNarrowestWindow`), pero pierden peso visual: pastillas de 11 px sin relleno salvo la activa, y el recuento y el orden pasan a texto a la derecha en vez de un `ComboBox`.

**Las etiquetas de las pastillas se quedan en el eje del disco** — «Instalados» / «No instalados» — porque eso es exactamente lo que sus predicados hacen (`FilterMode.Installed => Status == Installed || Status == UpdateAvailable`). Renombrarlas en términos de colección haría que el filtro mintiera: un mod que está en disco pero fuera de la colección caería bajo «En mi colección». Si algún día queréis filtrar por colección, hace falta añadir modos nuevos al enum, no reetiquetar los que hay.

⚠ **La misma ambigüedad ya existe en las sub-pestañas y conviene resolverla, no profundizarla**: «Mis mods» es `SubTabMode.MyMods => s.Status != NotInstalled`, es decir, también estado en disco, pese a que su nombre sugiere colección. Decidid a cuál de los dos ejes pertenece esa sub-pestaña antes de tocar los filtros.

Además: `Install type: Isolated folder` y `Updates: WoL patcher (UpdateInfo.xml)` son jerga interna. En la ficha se dicen en el idioma del jugador — «En su propia carpeta. No modifica tu AoE3».

## Reglas comunes a las dos opciones

- **Estado y acción son lo mismo, y van juntos a la derecha de cada fila o tarjeta**: etiqueta de estado arriba (`INSTALADO` verde, `ACTUALIZACIÓN` azul, `NUEVO` violeta, `BASE` gris) y debajo el botón, en una columna fija de 132 px (6a) o alineado al pie de la tarjeta (6b). El botón nunca cambia de ancho con el texto.
- **La fila lleva una línea de metadatos monoespaciada** con lo que decide una instalación: `versión · tamaño`, y a la derecha la antigüedad (`actualizado hace 2 días`). Cuando hay actualización, la versión se escribe como transición: `v2.1.7b → v2.1.8 · 340 MB`, que es el dato que importa (lo que se descarga, no lo que ocupa).
- **Una línea de descripción, nunca dos truncadas.** `TextTrimming="CharacterEllipsis"`, una línea.
- **El juego base es una fila atenuada**, no un mod más: fondo `#101d31`, etiqueta `BASE`, y la explicación de para qué sirve («sirve de origen para clonar las instalaciones»). Hoy compite visualmente con las conversiones totales.
- El icono del mod pasa de 32 a **46 px** (6a) y las tarjetas de 6b llevan **portada de 104 px**. Si el catálogo no trae portada (`mod.json` no la define hoy), usa el color de acento del mod sobre fondo oscuro; los rectángulos rayados del prototipo son marcadores.
- La cabecera «Workshop» + subtítulo + «My mods / Catalog» + «Available mods (5)» son cuatro niveles de encabezado antes del contenido. Se quedan **dos**: las pestañas Catálogo / Mis mods, y la fila de filtros.

## 6a — Lista y ficha

Mantiene la estructura actual (lista a la izquierda, ficha a la derecha) y la arregla.

**Lista** — filas de ~86 px, radio 9, `gap` 6. La seleccionada lleva fondo `#16263e` y borde `rgba(47,127,224,.42)`.

**Ficha, 452 px** — de arriba abajo:
- **Portada de 132 px** con el icono del mod de 64 px solapando el borde inferior (`box-shadow` de 3 px del color del panel para recortarlo). Sustituye al degradado plano.
- Nombre 18/700 serif + etiqueta de estado, y debajo `autor · versión · tamaño`.
- Descripción completa, 12.5/1.6, `text-wrap: pretty`.
- **Tira de capturas**: tres huecos de 66 px, el último con el contador del resto (`+4`). Si el mod no aporta capturas, la tira no se dibuja — no dejes marcadores vacíos en la build.
- **NOVEDADES DE LA <versión>**: hasta tres viñetas del changelog + «Ver el registro completo». Es lo que convierte «hay una actualización» en una razón para pulsarla.
- **DETALLES** como tabla de cuatro filas con etiqueta de 104 px: Instalación (en lenguaje llano), Espacio (`9,4 GB · te quedan 214 GB`, de `DiskSpaceService`), Idiomas (pastillas) y Multijugador (`3 jugadores con esta versión`, del recuento de salas/usuarios por versión — si ese dato no existe hoy, omite la fila entera en vez de inventarla).
- **Enlaces de comunidad como enlaces de 11.5 px**, no cuatro botones con icono: son destinos externos, no acciones de la pantalla.
- **Pie fijo** con la acción principal (`Jugar` si está instalado, `Instalar · 9,4 GB` si no) y `Ver en biblioteca` como secundaria.

Sin selección, el panel muestra una sola línea centrada, no un panel vacío con cabecera.

## Datos: qué existe de verdad

Esta tabla corrige una versión anterior de este documento que decía que estos campos no existían. **Existen en el esquema**; lo que falta es rellenarlos en las entradas del catálogo — es un hueco de contenido, no de esquema, y el arreglo es editar `mod.json`, no ampliar el modelo de C#.

| Campo | En `mod.schema.json` | En `mods/wol/mod.json` |
| --- | --- | --- |
| `accentColor` | sí | **sí** — `#c8102e` |
| `icon` | sí (PNG cuadrado 256-1024) | no |
| `banner` | sí (4:1, 1200-4800 px) — descrito como «Workshop mod card» | no |
| `heroImage` / `heroImages` | sí (16:9, 1920-3840 px) — para el panel del dashboard | no |
| `screenshots` | sí (hasta 8, GIF permitido) | no |
| `links` | sí (hasta 4: discord, moddb, foro, wiki, vídeo) | no — solo `officialWebsite` |
| `subtitle` | sí (50 caracteres) | sí — «Launcher» |
| `description` | sí, por idioma | sí, en/es |

**Antes de implementar, verifica en `Models/ModCatalogManifest.cs` cuáles de esos campos parsea hoy el launcher** — el esquema y el modelo pueden ir desacompasados. `ModsBrowser` ya llama a `ResolveBannerSource()`, `ResolveScreenshotSources()` y `ResolveIconSource()`, así que al menos esos tres están conectados.

Lo que **no** sale del catálogo:

- **Tamaño en disco y espacio libre** — `DiskSpaceService`.
- **Estado instalado / actualizable** — `ModRegistry` + `UpdateService`.
- **Changelog y fecha de publicación** — no están en el esquema. O se añaden, o esos bloques (las novedades de 7a y 6a) se ocultan cuando no hay datos. **No pongas marcadores vacíos en la build**: la galería ya tiene ese comportamiento y hay que copiarlo.
- **«N jugadores con esta versión»** — vendría del recuento de salas por versión; si no existe, omite la fila entera.
