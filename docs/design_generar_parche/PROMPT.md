# Texto para pegar en Claude Code

Copia esta carpeta dentro del repo (p. ej. `docs/design_generar_parche/`), abre una terminal ahí, lanza `claude` y pega esto:

---

Lee `docs/design_generar_parche/README.md` completo antes de escribir código. Es el rediseño del diálogo **«Generate patch»** de este launcher WPF — la herramienta de mantenedor que escribe los parches delta de un release.

`Prototipo.html` es una referencia de diseño: NO lo copies ni lo integres. Recréalo en WPF/XAML con los estilos y controles que ya existen en `WarsOfLibertyLauncher/`. **18a** es el estado normal de la ventana y **18b** lo que aparece al pulsar «Cómo funciona»: son la misma ventana, no dos alternativas.

**No toques ninguna otra pantalla.** Salas, Torneos, Clasificación, Estadísticas, Taller, el asistente de Radmin y los diálogos de ajustes quedan como están.

## Antes de escribir código, hazme un plan

Este handoff se diseñó desde capturas. Empieza verificando sus premisas:

1. **Localiza el diálogo.** Búscalo por el título «Generate patch»; no lo encontré en `main`. Dime dónde vive y cómo están montadas hoy sus tres secciones.
2. **Confirma el hallazgo principal:** que `BASELINE RELEASE` ya pone su .zip y su tag en el mismo grupo, mientras `SOURCE OVERLAYS` y `VERSION TAGS` parten los otros dos pares. Todo el rediseño consiste en extender a las tres el patrón que esa ya usa.
3. **Lee `DeltaPatchTests.cs` y `Services/DeltaPatchService.cs`** y dime dos cosas:
   - Si `SelectPatch` compara los tags **sin distinguir mayúsculas**. Hay un test cuyo nombre lo sugiere. De ello depende suavizar la ayuda que hoy dice que deben coincidir `EXACTLY`.
   - Si el servicio devuelve, al terminar, el **recuento de archivos cambiados y el tamaño del parche**, y si expone el juicio de `ShouldAdviseRebaseline`. Ese último dato —que el parche ya pesa más de media descarga completa y toca publicar una base nueva— hoy no aparece en ninguna pantalla, y es la conclusión que un mantenedor necesita. Dime si conviene diseñar la pantalla de resultado.
4. Dime qué claves de `Localization/Strings.cs` faltan y dame la lista.
5. Propón commits pequeños: primero las tres tarjetas de release, luego la caja de salida, luego el explicador plegable.

## Reglas al implementar

- **Un .zip y su tag van siempre juntos**, en la tarjeta de su release. Es lo que la sección de baseline ya hace.
- **Quita las mayúsculas de emergencia.** Hoy hay seis en una pantalla: `OLD`, `NEW`, `EXACTLY`, `ONCE`, `TWO`, `FRESH`. Si al terminar sigues necesitando gritar en una etiqueta, la estructura no ha quedado clara.
- **El fallo silencioso va en caja ámbar con titular propio**, no al final de un párrafo en cursiva. Es lo peor que puede salir de esta ventana: un tag inexistente hace que todos los jugadores descarguen el mod completo y no da ningún error.
- **El explicador nace plegado** y recuerda su estado. Hoy ocupa unos 250 px del arranque, así que al abrir se ve la explicación y no los campos.
- **Una línea de ayuda por campo.** Las cuatro actuales tienen entre 18 y 78 palabras.
- **En el diagrama, las tres filas comparten una rejilla de tres columnas** y cada barra declara su tramo: incremental columnas 2-3, acumulativo 1-3. Los bordes de cada barra tienen que coincidir con los de los nodos que une. Si una barra no llega a su nodo, el diagrama miente — es la misma regla que las specs del cuadro de torneo de este proyecto.
- **Un solo elemento sólido, y es el que hace la cosa.** Hoy hay cuatro dorados sólidos: tres «Browse…» y «Generate patch». Los de examinar pasan a fantasma y `Close` a enlace. Es la regla que documenta `CreateTournamentDialog.xaml.cs`.
- **Acota la columna de contenido.** Los campos de ruta miden hoy ~2400 px.
- **Los nombres de archivo, versiones y tamaños del prototipo son ilustrativos.** Sácalos de los datos reales; si alguno no está disponible, quita ese elemento en vez de rellenarlo.
- Cero cadenas literales: todo a `Localization/Strings.cs`, español e inglés. Rutas, tags y nombres de archivo en monoespaciada.
- Respeta los valores exactos de la tabla de Tokens. Reutiliza los recursos compartidos del launcher.
- Señala cualquier punto del README que choque con la arquitectura real, con `CLAUDE.md` o con un test existente, en vez de forzarlo.
- Compila y pasa los tests antes de darme cada commit por terminado.

Cuando acabes las tres tarjetas de release, párate y enséñame una captura antes de seguir.
