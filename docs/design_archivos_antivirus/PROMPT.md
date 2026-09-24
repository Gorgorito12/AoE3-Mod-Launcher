# Texto para pegar en Claude Code

Copia esta carpeta dentro del repo (p. ej. `docs/design_archivos_antivirus/`), abre una terminal ahí, lanza `claude` y pega esto:

---

Lee `docs/design_archivos_antivirus/README.md` completo antes de escribir código. Es el rediseño de tres ventanas de este launcher WPF, en este orden:

1. **Pestaña LOCAL FILES** de las propiedades del mod: `ModPropertiesDialog`
2. **Ventana de desinstalar**: `UninstallDialog`
3. **Exclusión de antivirus**: `AntivirusExclusionDialog`

Los dos prototipos (`Prototipo-archivos.dc.html` y `Prototipo-antivirus.dc.html`) son referencia de diseño: NO los copies ni los integres. Ábrelos en un navegador con `support.js` en la misma carpeta. 49c y 48c son notas sobre el código, no pantallas.

**No toques ninguna otra pantalla.**

## Antes de escribir código, hazme un plan

1. **Confirma la seguridad antes de añadir «Uninstall…» a las copias.** Lee `Services/UninstallService.cs` y `Tests/UninstallSafetyTests.cs` y dime si el plan se niega a borrar cuando la carpeta es la de AoE3 o la contiene. **Si no está cubierto, el primer commit es ese test**, antes de conectar ningún botón.
2. **Confirma que `UninstallService` puede construir un plan para cualquier carpeta**, no solo para `_service.InstallPath`. El Uninstall de las copias depende de eso. Si hoy solo trabaja con la copia activa, dime qué hay que cambiar.
3. **Dime qué hace `OptResetConfig`:** ¿reinicia la configuración de todo el launcher o solo la de este mod? Si es la de todo el launcher, dímelo antes de tocar esa casilla.
4. **Dime si `UninstallDialog` puede saber si hay otras copias.** Si no, la frase «your other copies are not touched» se queda en «Age of Empires III is not touched».
5. **Mira por qué `Background="#3a3d44"` no se aplica** en los botones de `AntivirusExclusionDialog`. Dime si es la plantilla de `SidebarPrimaryButton` y qué estilo de enlace o botón secundario existe ya para Cancel.
6. **Prueba `windowsdefender://threatsettings`** en Windows 10 y 11. Si no funciona en los dos, no añadas el botón «Open Windows Security».
7. Dime qué claves de `Localization/Strings.cs` hacen falta o cambian, en español e inglés.
8. Propón commits pequeños, en este orden: tests de seguridad → LOCAL FILES → Uninstall de las copias → ventana de desinstalar → antivirus.

## Reglas al implementar

- **«Remove from list» y «Uninstall…» son dos acciones distintas y cada una lleva su nombre en el botón.** `RemoveInstall` no borra archivos; Uninstall sí. Nada de ✕.
- **No crees un segundo camino para borrar.** El Uninstall de una copia usa el mismo `UninstallDialog` con el plan de esa carpeta y, si termina bien, llama a `RemoveInstall(id)`.
- **En el juego base (`IsStockGame`) se oculta todo, como hoy:** las copias, Uninstall y el nuevo Uninstall de las copias.
- **Con `NotAValidInstall` o `NothingToDo`, oculta** las casillas y el botón Uninstall. No los dejes desactivados a la vista. La única acción es `Close`.
- **Un solo botón sólido por ventana**, y es el que hace la cosa. Cancel siempre como enlace.
- **Cada cosa se dice una vez.** Quita el título repetido de `UninstallDialog` y una de las dos frases sobre AoE3.
- **Las rutas cortan en las barras, no en los espacios:** U+200B después de cada `\`, solo en el texto que se ve. Lo que se copia al portapapeles no cambia.
- **Los recuentos de archivos con separador de miles (`N0`).**
- **No cambies `SupportLink`.**
- **No cambies el comportamiento del antivirus:** sigue sin tocar nunca su configuración. El enlace a Windows Security solo abre la pantalla.
- Cero cadenas literales: todo a `Localization/Strings.cs`.
- Respeta los valores de la tabla de Tokens del README. Reutiliza los estilos que ya existen; si creas uno nuevo (fila de copia, tarjeta de acción), hazlo compartido.
- Señala cualquier punto del README que choque con la arquitectura, con `CLAUDE.md` o con un test existente, en vez de forzarlo.
- Compila y pasa los tests antes de darme cada commit por terminado.

Cuando termines la pestaña LOCAL FILES, para y enséñame una captura antes de seguir con la ventana de desinstalar.
