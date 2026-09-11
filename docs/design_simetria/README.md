# El nombre del mod no puede mover el layout

Los ajustes de cada mod se reparten el espacio de forma distinta según el mod. Referencias visuales en `Prototipo.html`: **21a** (la prueba, con las tres ventanas a escala real) y **21b** (la regla y la tabla de qué hacer con cada dato del mod).

Repo: `Gorgorito12/AoE3-Mod-Launcher`, rama `main`, proyecto WPF `WarsOfLibertyLauncher/`.

| Qué | Archivos del repo |
| --- | --- |
| El diálogo | `ModPropertiesDialog.xaml` + `.xaml.cs` |
| El carril y sus estilos | `Styles/` — `SetModRailWidth`, `SetRailFooterText`, `SetNavButton`, `SetNavLabel` |
| El mismo patrón, ventana hermana | `LauncherSettingsDialog.xaml` |
| Tests | `WarsOfLibertyLauncher.Tests/DialogXamlTests.cs` |

Leí `ModPropertiesDialog.xaml` (el archivo se trunca a ~50 KB; leí hasta la zona de ARCHIVOS). No leí el `.xaml.cs` ni los diccionarios de estilo.

## El diagnóstico: no es el nombre, es la URL del pie del carril

Las tres capturas son la **misma ventana**. `ModPropertiesDialog.xaml` declara `Width="900"`; salen a ~1120 px porque están al 125 %. Lo que cambia no es la ventana, es el reparto interno.

El carril está en una columna `Auto`:

```xml
<ColumnDefinition Width="Auto"/>
...
<Border Grid.Column="0" MinWidth="{StaticResource SetModRailWidth}"   <!-- 206 -->
```

Y su pie contiene dos `TextBlock` **sin `TextTrimming` ni `TextWrapping`**:

```xml
<TextBlock x:Name="RailAuthorText" Style="{StaticResource SetRailFooterText}"/>
<TextBlock x:Name="RailSiteText"   Style="{StaticResource SetRailFooterText}"
           FontFamily="{DynamicResource MonoFont}" Margin="0,4,0,0"/>
```

Una columna `Auto` mide lo que pide su hijo más ancho. Ese hijo es una URL que viene del `mod.json`, en monoespaciada, sin límite. **El carril mide lo que mida la URL del mod.**

### La correlación, medida en las tres capturas

Devuelto a DIP (÷ 1,248):

| Mod | URL del pie | Caracteres | Carril | Contenido |
| --- | --- | --- | --- | --- |
| Wars of Liberty | `http://aoe3wol.com/` | 19 | **200** (mínimo declarado 206) | 699 |
| AoE III: TAD | `https://www.ageofempires.com/games/aoeiii/` | 42 | **296** (+90) | 603 |
| Knights and Barbarians | `https://www.moddb.com/mods/knights-and-barbarians` | 49 | **336** (+130) | 563 |

El orden de los carriles coincide exactamente con el orden de las URLs. Y el mod cuya URL cabe en el mínimo es, precisamente, el único cuyo carril se queda en 206.

### Por qué importa más allá de la estética

**136 px de diferencia en el ancho de contenido** entre el primero y el último (699 − 563). Cada mod recibe una medida distinta, así que:

- El mismo párrafo ámbar corta en un sitio distinto en cada mod (se ve en las tres capturas).
- Las filas de *Archivos* reparten su columna de botones distinto.
- El desplegable de versión de *Knights and Barbarians* sale recortado —«1.3.6c — installed, recommendec»— y es justo el mod al que el carril le robó más sitio. Su `MaxWidth="260"` no es el problema; el problema es que su contenedor tiene 136 px menos que el de otro mod. (Contenido = 900 − carril − 1 px de borde.)

## La regla que falta, y el precedente que ya existe en el archivo

**Este mismo defecto ya se arregló una vez en este archivo**, en la tarjeta de cabecera, y el motivo está escrito en el XAML:

> «TWO THIRDS AND ONE THIRD, always. The status column used to be `Auto`, so a status that is a sentence ("detected — ready to play", for a mod with no version of its own) took as much as it liked and the name was ellipsised down to "Age of Empires II…". A proportion cannot starve either side at any window width.»

Se resolvió con `Auto / 2* / * MaxWidth 200`, y el comentario la llama «the one place in the window whose contents belong to the MOD rather than to the launcher, so it is the one place a long name or a long status could rearrange».

**No lo era.** El carril hace exactamente lo mismo, con más consecuencia, porque reparte el ancho de toda la ventana en vez del de una tarjeta.

La regla general, en una frase:

> **El ancho de un contenedor del launcher nunca puede depender de un dato del mod.**

El matiz importa: que el carril crezca con sus **rótulos de navegación** al subir el tamaño de texto **es intencionado** — está documentado en `LauncherSettingsDialog.xaml` y por eso es `MinWidth` y no `Width`. Esos rótulos son cadenas del launcher: acotadas, traducidas por ti, conocidas. Una URL de `mod.json` no lo es. **La corrección no es fijar el carril: es sacar el texto del mod de la medida.**

## Qué hacer con cada dato del mod

| Dato del mod | Cómo se trata |
| --- | --- |
| **URL del pie del carril** | `MaxWidth` = ancho útil del carril, más `TextTrimming="CharacterEllipsis"`. Mejor aún: mostrar solo el dominio, que es lo legible; la URL entera no se lee en un pie de 10 px. |
| **Autor del pie del carril** | Igual. Puede envolver a dos líneas, pero nunca ensanchar. |
| **Nombre en la barra de título** | Trunca. Hoy «Age of Empires III: The Asian Dynastie» se mete por debajo de los iconos superpuestos de la barra. |
| **Nombre en la cabecera** | Ya resuelto: columna `2*` con elipsis. |
| **Estado de versión** | Ya resuelto: columna `* MaxWidth 200`, envuelve dentro de la suya. |
| **Rutas de instalación** | Raíz + cola, como en «Dónde se instala». Nunca ensanchan su fila. |
| **Texto del desplegable de versión** | Ya tiene `MaxWidth="260"`. Se arregla solo al devolverle al contenido sus 136 px. |

### Cómo se comprueba

Un test que instancie el diálogo con un nombre, un autor y una URL absurdamente largos, y verifique que **el ancho del carril no cambia** respecto al mismo diálogo con un mod de datos cortos. `DialogXamlTests` ya cubre estos diálogos.

Es la única forma de que no vuelva: el defecto solo se ve comparando dos mods, y nadie abre dos mods seguidos.

## Un defecto aparte que sale en la misma captura

En **AoE III: The Asian Dynasties**, la tarjeta «Stay on this version» aparece **sin interruptor y sin su línea de descripción**, pero **con el aviso ámbar**: «Some updates fix multiplayer compatibility. If you stay behind, you may not be able to play with people who updated.»

Es un aviso sobre quedarse atrás en actualizaciones, en el juego base, que no tiene actualizaciones que ofrecer — la propia ventana oculta ahí el interruptor, la tarjeta de estado de actualización y la sección VERSION. Si se oculta el control, **se oculta también su advertencia**: la caja ámbar vive dentro del mismo `Border` que el `ToggleButton`, así que es un cambio de una línea.

## Archivos de este paquete

- `Prototipo.html` — 21a y 21b.
- `PROMPT.md` — texto listo para pegar en Claude Code.
