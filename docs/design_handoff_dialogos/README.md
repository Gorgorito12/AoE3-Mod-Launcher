# Diálogos del launcher: creación y asistente de Radmin

Dos partes, para hacer en este orden.

| Orden | Parte | Spec | Archivos principales |
| --- | --- | --- | --- |
| 1 | Crear sala y crear torneo (13a-13e) | `SPEC-1-crear-sala-y-torneo.md` | `CreateLobbyDialog`, `CreateTournamentDialog`, `GitHubLoginDialog` |
| 2 | Asistente de Radmin (12a-12c) | `SPEC-2-asistente-radmin.md` | `RadminAssistantWindow` |

`Prototipo.html` contiene las dos, en ese orden. Ábrelo en un navegador y haz zoom; cada opción lleva su id (13a, 12c…) como etiqueta.

Repo: `Gorgorito12/AoE3-Mod-Launcher`, rama `main`, proyecto WPF `WarsOfLibertyLauncher/`.

**El HTML es una referencia de diseño, no código para copiar.** Recrea las pantallas en WPF/XAML con los estilos y controles que ya existen. Todo el texto pasa por `Localization/Strings.cs` en español e inglés; nada de cadenas literales en el XAML.

## Lo que estas dos partes tienen en común

**El criterio ya está escrito en tu código.** `CreateTournamentDialog.xaml.cs` documenta en su resumen las reglas que aplicó y el defecto que cada una reemplazó. Las tres que gobiernan este handoff:

1. **Un solo elemento sólido por diálogo, y es el que hace la cosa.** Cancelar es un enlace. Aparece tres veces: en crear sala, en el inicio de sesión de Discord (donde hoy Cancelar es el botón dorado más llamativo) y en el pie del asistente.
2. **Ninguna ayuda puede contradecir la selección.** Todo texto derivado se recalcula en un único método. Aparece en el aviso de Record Game de crear sala, que hoy se muestra con la sala no competitiva.
3. **Lo que no puede variar no se muestra.** `TeamSourceBlock` se colapsa en 1v1. Aparece en el FORMATO de crear sala, hoy visible sin ningún segmento activo.

**Nada se estira ni se recorta.** Las tres ventanas tienen ancho fijo y `WindowStyle="None"`, así que cualquier fila con tres elementos hay que medirla. El pie del asistente de Radmin y el del inicio de sesión de Discord están desbordados hoy, y en los dos casos la víctima es un control que el usuario necesita.

**Un texto que se pide copiar se muestra entero.** El nombre de red del asistente y la URL de OAuth del inicio de sesión salen hoy recortados con elipsis, y en ambos casos son lo único que la ventana existe para entregar.

## Lo que NO debes hacer

- **No cambies `SupportLink`.** Lo comparten cinco ventanas y su redacción está justificada en su propia documentación. Si estorba en un pie, el pie es el problema. La spec 2 lo explica con el precedente del propio control.
- **No inventes datos ni endpoints.** Si algo que la spec pide no existe, dilo en el plan.
- **No cambies el comportamiento de ningún ajuste.** Esto es reorganización de UI y de copy. Las dos excepciones están dichas: el torneo pasa a proponer un nombre, y el asistente pasa a `SizeToContent="Height"`.
- **No dupliques controles.** Si haces un estilo de fila plegada o de caja de cuentas, hazlo compartido.

## Lo que no comprobé

Para que no lo des por verificado:

- **Con qué condición apaga `CreateLobbyDialog` su botón.** Tiene `CreateButton.IsEnabled` en las líneas 492, 498, 636, 644, 747, 791 y 799; no leí sus guardas. El torneo lo apaga en un solo sitio, por validación de nombre.
- **El ancho literal de cada diálogo de creación.** Los dos son `SizeToContent="Height"`; el `Width=` de la sala no lo leí.
- **Si la sala usa el `TitleBar` compartido.** No declara `x:Name="TitleBarControl"`, que es como lo declara el torneo.
- **El ancho real de la pastilla de `SupportLink`** son 354 px des-escalados de la captura del usuario, no medidos en la build.

## Archivos de este paquete

- `Prototipo.html` — las dos partes.
- `SPEC-1-crear-sala-y-torneo.md`, `SPEC-2-asistente-radmin.md`.
- `PROMPT.md` — texto listo para pegar en Claude Code.
