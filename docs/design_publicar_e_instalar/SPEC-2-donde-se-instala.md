# 2 · Dónde se instala

Referencias visuales en `Prototipo.html`: **19a** (el diálogo reorganizado) y **19b** (el campo de ruta, medido antes y después).

| Qué | Archivos del repo |
| --- | --- |
| El diálogo | `InstallFolderDialog.xaml` + `.xaml.cs` |
| La regla de la copia de ajustes | `Models/LauncherConfig.cs` — `pendingSettingsImportFrom` (~235-251) |
| Textos | `Localization/Strings.cs` (~2299, 9233-9279) |

Geometría real: `Width="560"` con `SizeToContent="Height"`. Tu captura sale a 710 px, o sea escalada.

**Leí el `.xaml` y la documentación de `pendingSettingsImportFrom`; no leí el `.xaml.cs`.**

## El defecto principal: las dos rutas se recortan por extremos opuestos

Los dos `TextBox` van en columna `*` con el mismo estilo, así que tienen el mismo ancho, y **ninguna de las dos rutas cabe**:

- El de origen pierde el **final** — «…steamapps\common\Ag» — así que no se ve qué carpeta es.
- El de destino pierde el **principio** — «ps\common\Age Of Empires 3\Knights and Barbarians» — así que no se ve ni en qué disco va.

En una ventana cuyo único trabajo es que confirmes dos rutas, ninguna es verificable. Es la peor combinación posible.

**Las cifras, medidas en la maqueta** (19b): el campo recorta a los 358 px de su caja de relleno, no a los 336 de contenido, así que el texto dispone de **347 px visibles**. La ruta de origen mide 375 → se pierden 28 por la derecha, cortando a mitad de «Empires». La de destino mide 514 y, alineada al final como la deja el cursor, se pierden 167 por la izquierda.

**La corrección: raíz + cola.** En reposo la ruta se muestra como `C:\` + un tramo elidido + la última carpeta destacada en blanco. El tramo del medio se acorta o se alarga según el sitio que quede, pero **la raíz y la última carpeta no se pierden nunca**. Al enfocar el campo editable, vuelve a mostrarse la ruta completa.

Detalle que el código impone: **`Aoe3PathTextBox` es `IsReadOnly="True"`**, así que el de origen solo cambia por «Cambiar…». El de destino sí es editable (`TextChanged`).

## La relación entre las dos rutas estaba solo en prosa

La carpeta del mod va **dentro** de la de AoE 3, que es lo que el párrafo de arriba tarda cinco líneas en explicar. Dicho en la estructura, el párrafo baja a dos líneas.

**Aviso de la primera versión de esta maqueta, para que no lo repitas:** dibujé la anidación como un carril vertical en una columna de 11 px más 9 de hueco *dentro de la segunda fila*, y eso le robó 20 px de ancho a su campo — las dos filas dejaron de ser simétricas y los dos «Cambiar…» quedaron desalineados. **La relación va en la etiqueta**, no en el flujo: una flecha `↳` y «dentro de la carpeta de arriba». Las dos filas tienen que medir exactamente lo mismo.

## El requisito y su dato estaban separados 200 px

Hoy «About 12 GB … recommended» está al final del párrafo introductorio y «Available disk space: 1,2 TB» en gris pequeño bajo el campo de destino. Separados, son dos datos; juntos en un renglón, son una respuesta:

> ✓ Necesita unos **12 GB** y tienes **1,2 TB** libres en C:

En verde cuando cabe, en ámbar o rojo cuando no. El espacio libre sale de `DiskSpaceService`.

## La frase de la casilla, corregida con el código

Hoy dice dos cosas que se leen como contrarias: «la copia se aplica *una vez que lo hayas abierto*» y «*no* en el primer arranque». La regla real está documentada en el propio diálogo:

> «The copy is NOT made here. This dialog only reports the choice; see `ModState.PendingSettingsImportFrom` for why it usually cannot happen until the mod has been opened once.»

O sea: la elección se guarda y se aplica sola la primera vez que abres el mod. En una frase:

> **Se copian la primera vez que abras el mod, no al instalarlo.** Tus partidas guardadas, ciudades de origen y perfil no se tocan.

El **motivo** —que hasta entonces no existe la carpeta de datos donde escribir— lo deduzco de que el catálogo exija «a resolvable user-data folder»; **no leí el consumidor de ese campo**, así que confírmalo antes de explicarlo en la UI. Si no lo confirmas, di solo el cuándo y omite el por qué.

## Lo que este diálogo ya hace bien y no conviene romper

- **`Aoe3Row` se colapsa entero** en una instalación superpuesta, porque entonces no hay carpeta de origen que elegir.
- **`CopySettingsRow` solo aparece si hay otro mod del que copiar** — su comentario dice «so the question only ever appears to somebody who can answer it». Es el mismo principio que gobierna el resto de este proyecto.

## Tokens

| Uso | Hex |
| --- | --- |
| Fondo del diálogo | `#0f1c2e` |
| Tarjeta de rutas | `#12213a` |
| Campo | `#0d1828` |
| Borde interior | `rgba(130,175,255,.11–.20)` |
| Azul de la casilla activa | `#2f7fe0` sobre `rgba(47,127,224,.07)`, borde `rgba(47,127,224,.4)` |
| Verde del espacio | `#4fd68a` · texto `#a9cbb9` / `#dbe6e0` sobre `rgba(53,196,111,.09)`, borde `rgba(53,196,111,.22)` |
| Ruta: raíz | `#8ea4c0` · tramo elidido `#6d829d` · última carpeta `#dce7f5` |
| Texto | titular `#f4f8fc` · cuerpo `#b9c9de` · atenuado `#8ea4c0` → `#6d829d` → `#5f7592` |

**Tipografía.** UI en Segoe UI; el titular en serif. **Rutas y tamaños en monoespaciada.** Etiqueta de sección 10.5 SemiBold `letter-spacing:.6px`.

**Medidas.** Ventana 560 · campo 34 · botón «Cambiar…» 34 · botón primario 36 con `MinWidth` 136 · casilla 16 · relleno del cuerpo 18/20/20.
