# Rediseño con la base de la Biblioteca unificada (68–79)

Todos los menús y diálogos del launcher, rehechos con la base de la Biblioteca unificada (68a). Las reglas comunes están en **BASE.md**; aquí va cada pantalla: qué cambia, en qué archivos del repo y qué necesita código nuevo.

**Los prototipos.** Abre cada `.dc.html` en un navegador, con `support.js` y las carpetas `mods/` y `design_handoff_workshop/` al lado. Son interactivos y la transparencia se cambia en Tweaks. Son referencia de diseño: recréalos en WPF/XAML con los estilos y controles que ya existen.

**Los textos.** Los textos en español que no salen de `Localization/Strings.cs` son propuestas (el archivo supera el límite de búsqueda y no pude leerlo entero). Si ya hay una clave equivalente, usa la suya. Todo texto nuevo va en español (es-419, tuteo) e inglés.

## Orden sugerido

1. **La base compartida** (BASE.md): niveles de cristal de Ajustes › Interfaz › Apariencia (predeterminado **Media**), barra de título de 40 uniforme con su transparencia, botones secundarios sólidos `#1a2c48`, colores de estado fijos y el cristal de menús y popovers.
2. Luego, una pantalla por commit, en este orden: 68 · 69 · 72 · 70 · 79 · 71 · 74 · 75 · 76 · 77 · 78 · 73.

## Pruebas para todas

1366×768, 1920×1080 y 2560×1440; las tres transparencias (Sólida, Media, Máxima); efectos reducidos (fondos sólidos); un mod con imagen (Wars of Liberty) y uno sin imagen (Improvement Mod).

---

## 68 · Biblioteca unificada (la base)

**Prototipo:** `Biblioteca unificada.dc.html`  
**Archivos del repo:** MainWindow.xaml (PlayView, Dashboard*, DashboardProgress*), Controls/ProgressPanel.xaml, Controls/ActionPanel.xaml, Controls/ModsBrowser.xaml(.cs), ModPropertiesDialog (Ajustes)

### Qué cambia

Una sola pestaña, BIBLIOTECA. La lista de la izquierda tiene dos grupos: Mis mods arriba y Catálogo debajo. La portada de la derecha es la de la Biblioteca de hoy (el hero que cambia de mod, el botón Jugar, Ajustes y el progreso), con los datos del Taller (autor, detalles y enlaces). Al haber un solo lugar, el botón ya puede decir «Instalar»: instalar un mod lo añade a Mis mods.
Descargas a la vista: la fila del mod que se descarga muestra una barra y el porcentaje, la portada muestra el progreso completo (con velocidad, tiempo restante, Pausar y Cancelar) y una píldora en la barra superior lo sigue mostrando desde Multijugador. Ajustes va junto a Jugar y abre los ajustes del mod de siempre.
Prueba: Improvement Mod se está actualizando, Struggle of Indonesia se está instalando y King's Return tiene la descarga detenida. Wars of Liberty tiene 3 copias instaladas (selector «Copia» junto a Jugar). Haz clic derecho en cualquier mod para ver su menú; desde ahí se puede desinstalar. La ☆ de cada fila lo marca como favorito. Cualquier mod del Catálogo se puede instalar. Los MB, la velocidad, el tiempo y las copias son de ejemplo.

### Variantes

- **68a:** Interactivo. Cambia el ancho y los efectos en Tweaks.
- **68b:** Colores del catálogo. Propuesta para 5 de los 6 dorados (The Asian Dynasties se queda dorado) y la intensidad igualada que aplica el launcher. Para aplicar la propuesta, cambia accentColor en el mod.json de cada uno. La igualación la hace el launcher al leer el color: lo lleva en OKLCH a una luminosidad de 0,56 a 0,68 y una saturación de 0,11 a 0,16, sin tocar el tono. En Tweaks, «Colores» e «Intensidad» permiten comparar en 68a.

---

## 69 · Ajustes del launcher › Interfaz

**Prototipo:** `Ajustes Interfaz.dc.html`  
**Archivos del repo:** LauncherSettingsDialog.xaml (TabInterfaceBtn, sección Interfaz), Models/LauncherConfig.cs

### Qué cambia

Los ajustes visuales de la Biblioteca pasan a la sección Interfaz, que hoy solo tiene el tamaño del texto y el orden de las pestañas. Arriba hay una vista previa que cambia en cuanto tocas un ajuste. Por defecto: transparencia Media, imagen y color del mod activados, y efectos y lista en Automático. Pruébalo: todos los controles funcionan.

---

## 72 · Ajustes del launcher

**Prototipo:** `Ajustes del launcher v2.dc.html`  
**Archivos del repo:** SettingsDialog.xaml.cs, Localization/Strings.cs

### Qué cambia

Las mismas 5 secciones de hoy (General, Interfaz, Partidas, Mods y actualizaciones, Avanzado). Es una ventana de todo el launcher, así que usa neutros y el azul de acción, sin imagen ni color de ningún mod. La transparencia que elijas en Interfaz se aplica en vivo a esta misma ventana. Prueba: busca un ajuste, apaga «Marcar Record Game» (aparece el aviso y el punto ámbar en Partidas). Avanzado › Desarrollador trae todas las herramientas de hoy; apaga «desarrollador» en Tweaks para verlo como un jugador normal, y reaparece al tocar 7 veces la versión del pie del menú. Activa «instalado» para ver la ventana de un launcher ya instalado, sin «Instalar en este PC».

### Variantes

- **72a:** Interactivo.

---

## 70 · Notificaciones

**Prototipo:** `Notificaciones.dc.html`  
**Archivos del repo:** Services/NotificationCenter.cs, Services/NotificationFeedService.cs, Models/LauncherConfig.cs (NotificationKind, NotificationItem), Controls/AppToast.cs, Controls/DesktopToastWindow.cs

### Qué cambia

La campana es solo para mods y launcher: 9 de los 13 tipos de NotificationKind. Los 4 de multijugador ya no entran, para que no tapen los demás; cada uno va a su sitio (70c). Los avisos van agrupados por día, con los filtros Mods · Launcher. Cada aviso lleva el icono del mod cuando es de un mod, o un círculo del color de su estado (fijos: verde OK, azul info, ámbar error). Además tiene una sola acción, la misma que haría el launcher: Actualizar, Jugar, Ver o Reiniciar y actualizar. Al pulsar el aviso se marca como leído y te lleva a donde corresponde.
Prueba: abre y cierra con la campana, filtra, pulsa un aviso o su acción, bórralo con ✕ o «Marcar todo como leído». La campana muestra cuántos quedan sin leer.

### Variantes

- **70a:** Panel abierto sobre la Biblioteca. Interactivo.
- **70b:** Avisos flotantes. Salen abajo a la derecha solo para lo que pide atención ahora: una sala nueva (si «Avisarme cuando alguien abre una sala» está activado), puntos recuperados, una instalación o actualización terminada y la pérdida de conexión. Se cierran a los 8 s, salvo la falta de conexión. Los de mods y del launcher quedan también en la campana; el de sala nueva no, porque en minutos deja de servir (70c). Si el launcher está en la bandeja, el mismo aviso sale en el escritorio. Se apilan como en el launcher, todos del mismo ancho. Los botones van siempre abajo a la derecha, con la acción principal en el extremo y un ancho mínimo de 96 px, así que caen en el mismo sitio en todos los avisos.
- **70c:** Multijugador, fuera de la campana. No propongo una segunda campana: con dos hay que revisar dos sitios y siempre se olvida uno. Cada aviso de multijugador va donde ya se mira. El único rastro en la barra es el número de salas nuevas sobre MULTIJUGADOR, que desaparece al entrar.

---

## 79 · Menú de cuenta y «Conectado ▾»

**Prototipo:** `Menu de cuenta v2.dc.html (+ BarraCuenta.dc.html)`  
**Archivos del repo:** MainWindow.xaml (AccountButton, ConnectionChip, OfflineChip), MainWindow.xaml.cs (AccountButton_Click, BuildAccountMenuHeader, SetAccountChip, OfflineChip_Click), MainWindow.Compact.cs (ConnectionChip_Click), Controls/MultiplayerTab.BadgeMode.cs

### Qué cambia

Los dos menús globales de la barra van en neutros: el color del mod solo aparece en lo que es del mod, como el subrayado de BIBLIOTECA. Usan el cristal de menú según la transparencia del usuario (Media: rgba(12,22,38,.74) + 28 px), con radio 12 y filas de 32. Se mantiene lo que ya hace el código: cada menú se alinea a la derecha bajo su botón y solo hay uno abierto a la vez. El de cuenta empieza por tu identidad y tu ELO, y la línea del ELO se omite si aún no se conoce. «Conectado ▾» muestra tu IP de Radmin para copiarla y «Ayuda para conectarte», que abre el asistente (78) y no aparece si el asistente está en «Nunca». Cambios: en la cuenta puedes elegir la insignia junto a tu nombre (La más alta · 1v1 · Equipos), el mismo selector del Perfil (51c). La cuenta muestra tus dos clasificaciones, con la que se ve resaltada. «Cerrar sesión» va al final, separado, y avisa si te saca de una sala. La cabecera de «Conectado» dice a qué estás conectado (Radmin VPN) y la IP se copia en su propia fila («Copiar» → «✓ Copiada»). Si Radmin está abierto pero no estás en la red AoE3, el chip pasa a «Falta la red», en naranja, y la ayuda sube al principio. «Sin conexión» pasa al naranja fijo de error y dice «Comprobando…» mientras reintenta. Sin sesión, aparece «Iniciar sesión».

### Variantes

- **79a:** Menú de cuenta. Con el ELO de equipos más alto, «La más alta» muestra el escudo doble.
- **79b:** «Conectado ▾»: IP de Radmin para copiar y ayuda para conectarte.
- **79c:** «Conectado ▾» sin IP de Radmin.
- **79d:** Sin partidas de equipo: «Equipos» desactivado, con el motivo; Equipos aparece como Descubrimiento.
- **79e:** Sin sesión: «Iniciar sesión».
- **79f:** Sin internet: chip «Sin conexión»; la insignia no se puede cambiar y el menú lo dice.
- **79g:** En una sala: «Cerrar sesión» avisa de que te sacará de ella.
- **79h:** Radmin abierto, fuera de la red AoE3: chip «Falta la red» y la ayuda primero.

### Requiere código o hay que confirmar

- Selector de insignia (51c) también en el menú de cuenta. Se guarda en el servidor, confirma «✓ Guardada» y se desactiva sin conexión.
- Las dos clasificaciones en la cabecera: confirmar que la sesión tiene el ELO de 1v1 y el de Equipos.
- «Cerrar sesión» avisa si estás en una sala.
- Chip «Falta la red» si Radmin está abierto pero fuera de la red AoE3: necesita la etapa de RadminAssistantService.ProbeAsync, no solo que Radmin esté abierto.
- «Sin conexión» en #F4900C, con «Comprobando…» mientras reintenta.
- «Iniciar sesión» sin sesión, que lleva al inicio de sesión de Multijugador.

---

## 71 · Ajustes del mod

**Prototipo:** `Ajustes del mod v2.dc.html`  
**Archivos del repo:** ModPropertiesDialog.xaml(.cs) (Tab*Btn, ModRail, RailDiagBox), Models/LauncherConfig.cs (OtherInstalls), Services/Addon*.cs, Services/IntegrityService.cs, Models/HomeCityDeck.cs, Models/AiGameRecord.cs, Services/AiGameStats.cs

### Qué cambia

Las mismas 7 secciones que hoy (General, Archivos, Datos, Idioma, Complementos, Barajas y Estadísticas), sobre la imagen del mod y con su color, porque esta ventana es de un solo mod. Tarjetas de cristal con el nivel de transparencia del jugador; botones de acción en una columna fija de 112 px a la derecha; lo destructivo, en rojo: junto a la copia en uso y al final del menú ⋯ de las demás.
Compartir diagnóstico pasa a ser una tarjeta azul fija al pie del menú lateral, visible desde cualquier sección. Prueba: cambia de sección, activa interruptores, elige otra copia (la «Copia antigua» muestra el aviso de versión), abre el menú ⋯ de una copia, verifica los archivos en General o activa «Humo de pólvora». En Tweaks puedes cambiar el mod, la transparencia y el tamaño de la ventana. Rutas, tamaños, complementos, barajas y partidas son de ejemplo; con Hundred Days o Improvement Mod ves Barajas y Estadísticas vacías.

### Variantes

- **71a:** Interactivo.

---

## 74 · Dónde se instala

**Prototipo:** `Donde se instala v2.dc.html (+ DondeInstala.dc.html)`  
**Archivos del repo:** InstallFolderDialog.xaml(.cs), Services/PathDisplay (SplitForDisplay), Services/DiskSpaceService, Models/ModState (PendingSettingsImportFrom)

### Qué cambia

Es un diálogo de un mod, así que lleva su imagen como franja arriba que se funde con el fondo, el resplandor de su color, su icono en la barra de título y su color en «Instalar» y en la casilla. Sigue midiendo 700 de ancho y su alto se ajusta al contenido. Se mantiene lo del turno 19: las rutas se muestran como raíz, tramo del medio recortado y última carpeta (al pulsar el campo se ve la ruta entera), «↳ dentro de la carpeta de arriba» solo cuando la carpeta del mod va dentro de la de AoE3, y lo que necesita y el espacio que tienes en una sola línea. Novedades: «Instalar» se desactiva con el motivo si falta el AoE3 o no hay espacio, y los botones secundarios son sólidos.

### Variantes

- **74a:** Normal · Wars of Liberty. AoE3 detectado, el mod dentro de su carpeta y hay espacio.
- **74b:** Sin espacio · Hundred Days. Línea en ámbar e «Instalar» desactivado.
- **74c:** AoE3 no encontrado · Improvement Mod, sin imagen: solo el resplandor de su color. La carpeta del mod queda vacía hasta que se encuentra el AoE3.
- **74d:** Encima de tu AoE3: sin carpeta de origen.
- **74e:** Primer mod: sin la fila de copiar ajustes.
- **74f:** AoE3 sin abrir nunca: aviso en ámbar que no bloquea.

### Requiere código o hay que confirmar

- «Instalar» desactivado con el motivo: sin AoE3, sin carpeta de destino o sin espacio.
- Sin AoE3 encontrado, la carpeta del mod queda vacía («Elige primero tu AoE3») hasta que se elige.
- El diálogo ya tiene el diseño del turno 19: el resto es estilo de la base.

---

## 75 · Desinstalar

**Prototipo:** `Desinstalar v2.dc.html (+ DesinstalarDialog.dc.html)`  
**Archivos del repo:** UninstallDialog.xaml(.cs), Services/UninstallService (UninstallPlan, UninstallMode), Services/PathDisplay (BreakAtSeparators)

### Qué cambia

El mismo chasis que Dónde se instala (74): franja con la imagen del mod, resplandor de su color y barra de título teñida. Sigue midiendo 560 de ancho y se ajusta al contenido. Se mantiene lo del turno 49: la barra dice solo «Desinstalar» y la pregunta nombra la copia; la tarjeta de la carpeta con borde rojo; «Age of Empires III no se toca» una sola vez, en la franja verde; y cuando el plan se niega, desaparecen las casillas y el botón rojo, y solo queda «Cerrar». Cambios: la pregunta usa el texto de la Biblioteca unificada («¿Desinstalar la copia «X» de Y?»), «Cancelar» pasa de enlace a botón sólido y las casillas van en el color del mod. Las casillas funcionan.

### Variantes

- **75a:** La copia en uso, con más copias del mod. Aparece «Partidas guardadas y perfiles», sin marcar.
- **75b:** Otra copia. Sin «Partidas guardadas»: todas las copias comparten la misma carpeta de My Games.
- **75c:** Una sola copia: la franja verde no habla de otras copias.
- **75d:** Encima de tu AoE3: la carpeta se queda, solo se quitan los archivos del mod. Sin imagen, solo el resplandor.
- **75e:** La carpeta no es el mod. Sin casillas ni botón rojo; solo «Cerrar».
- **75f:** La carpeta contiene el juego base: el launcher nunca la borra.
- **75g:** Ya no queda nada en la carpeta.

### Requiere código o hay que confirmar

- DlgUninstallQuestion necesita dos parámetros, copia y mod: «¿Desinstalar la copia «Principal» de Wars of Liberty?».
- «Cancelar» pasa de enlace a botón sólido; las casillas, en el color del mod.

---

## 76 · Exclusión del antivirus

**Prototipo:** `Antivirus v2.dc.html (+ AntivirusDialog.dc.html)`  
**Archivos del repo:** AntivirusExclusionDialog.xaml(.cs), Controls/SupportLink.cs, Services/PathDisplay (BreakAtSeparators), AppPaths.InstallTempRoot

### Qué cambia

Mismo chasis que Dónde se instala (74) y Desinstalar (75): franja con la imagen del mod, resplandor de su color y barra de título teñida. Sigue midiendo 560 de ancho y su alto se ajusta al contenido. Se mantiene lo del turno 48: un solo diálogo con dos modos, los pasos numerados, las rutas cortadas en las barras y, en el modo bloqueado, sin casilla ni «Cancelar». Cambios: el escudo usa en los dos modos el naranja fijo de error de la base (#F4900C), porque el rojo queda para lo destructivo; en el modo bloqueado, la etiqueta del archivo también va en naranja. Los botones secundarios son sólidos y «Continuar la instalación» va en el color del mod. Cada ruta lleva su propio «Copiar», porque Windows añade las exclusiones de una en una y «Copiar las dos» pegaba las dos rutas en un campo que solo admite una. «Abrir ↗» pasa al paso 1, que es el que hace por ti, así que los pasos quedan en tres; si Seguridad de Windows no se abre, el texto del paso dice dónde ir. Al final, una línea para quien usa otro antivirus. «Copiar» muestra «✓ Copiado» durante 1,4 s y la casilla se puede marcar.

### Variantes

- **76a:** Antes de instalar. Wars of Liberty declara AI3\wolai.upl como falso positivo conocido.
- **76b:** Después de que Defender quitó el archivo. Sin casilla ni «Cancelar»: solo «Cerrar».
- **76c:** Bloqueado antes de elegir la carpeta del mod: una sola ruta.

### Requiere código o hay que confirmar

- ShowBlocked tiene que recibir el mod (hoy pasa ""), para la franja y el color.
- Un «Copiar» por ruta: Windows añade las exclusiones de una en una. El portapapeles recibe la ruta sin las marcas U+200B.
- «Abrir ↗» (windowsdefender://threatsettings) pasa al paso 1 y los pasos quedan en tres; línea final para otros antivirus.
- Escudo naranja #F4900C en los dos modos (ApplyShield).
- Comprobar los nombres de los menús de Windows en un Windows en español.

---

## 77 · Actualización del launcher

**Prototipo:** `Actualizar launcher v2.dc.html (+ ActualizarLauncher.dc.html)`  
**Archivos del repo:** LauncherUpdateDialog.xaml(.cs), MainWindow.xaml (LauncherUpdatePill), Services/LauncherUpdateService, DiskSpacePrompt, releases/v1.0.16e.md

### Qué cambia

Es un diálogo global, así que va en neutros: barra de título #0b1422 con la transparencia del usuario, resplandor azul en vez de la imagen de un mod y «Descargar» en el azul de acción global (#2f7fe0). Sigue midiendo 540 de ancho y su alto se ajusta al contenido. Cambios respecto a hoy: el título ya no se repite en la barra y debajo; las versiones van en chips (la tuya → la nueva) con el tamaño de la descarga; la barra vacía y «Listo para descargar» no aparecen hasta que empieza la descarga; mientras descarga solo queda «Cancelar», porque el botón principal desactivado no hacía nada; al llegar al 100 % se ve «Comprobando la descarga» mientras se verifica el archivo, que hoy sigue diciendo «Descargando»; el aviso de poco espacio va dentro del diálogo en vez de en otra ventana; y en los errores se quita «Cancelar», que hacía lo mismo que «Cerrar», y se añade «Reintentar» cuando falla la descarga. Las novedades se muestran con formato y solo en el idioma del jugador; hoy salen el Markdown tal cual y los dos idiomas. Si la nota es solo un enlace, se muestra un botón. Además, «Reiniciar ahora» espera si estás en una partida (77l) y avisa si hay una descarga en curso (77m); y si la actualización ya está descargada, la píldora dice «Reiniciar para actualizar» y el diálogo abre directamente en «Lista para instalar» (77k). Prueba: en 77a pulsa «Descargar»; en 77f, «Reintentar».

### Variantes

- **77l:** Estás en una partida: «Reiniciar ahora» espera a que termine, porque el launcher tiene que seguir abierto para enviar el resultado.
- **77m:** Hay una descarga de mod en curso: aviso que no bloquea.
- **77a:** Hay una versión nueva. Novedades de la v1.0.16e, solo en español y con formato. Pulsa «Descargar».
- **77b:** La nota de la versión es solo un enlace, y no hay espacio suficiente: aviso en ámbar que no bloquea.
- **77c:** Descargando: porcentaje, MB, velocidad y tiempo restante. Solo «Cancelar».
- **77d:** Al llegar al 100 %, mientras se comprueba el archivo.
- **77e:** Lista para instalar. «Más tarde» deja la píldora de la barra superior (77k).
- **77f:** La descarga se cortó. «Reintentar» es nuevo.
- **77g:** Windows no dejó guardar la descarga. Se nombra la carpeta.
- **77h:** El archivo no pasó la comprobación y se borró. Reintentar no ayuda, así que no se ofrece.
- **77i:** Se descargó, pero no se pudo reiniciar.
- **77j:** El .exe tiene otro nombre: se nombran el actual y el esperado.
- **77k:** La píldora de la barra superior que abre este diálogo. Hoy es dorada; pasa al azul fijo de actualización (#55ACEE), con la forma del chip «Conectado» (28 de alto, radio 14), y sigue apareciendo en cada arranque hasta que actualices. Arriba, con una versión nueva. Abajo, cuando ya está descargada: dice «Reiniciar para actualizar» y abre el diálogo en «Lista para instalar» (77e), sin volver a descargar 170 MB.

### Requiere código o hay que confirmar

- Novedades con formato básico (#, -, **) y solo en el idioma del jugador: en español, desde el título que termina en «(Español)»; si no, hasta el primer ---. Sin la cita inicial con el enlace de descarga.
- «Comprobando la descarga» al llegar al 100 % mientras se verifica el archivo.
- En los errores, quitar «Cancelar» (hacía lo mismo que «Cerrar») y añadir «Reintentar» en el fallo de descarga y en la carpeta bloqueada.
- Comprobar el espacio al abrir el diálogo y avisar dentro, en vez de con DiskSpacePrompt.
- «Reiniciar ahora» desactivado con el juego abierto; aviso si hay una descarga de mod en curso.
- Si la actualización ya está descargada, abrir en «Lista para instalar», y la píldora dice «Reiniciar para actualizar». Confirmar si DownloadUpdateAsync ya reutiliza el archivo.
- LauncherUpdatePill: de dorada a #55ACEE, 28 de alto y radio 14.

---

## 78 · Asistente de Radmin

**Prototipo:** `Asistente de Radmin v2.dc.html (+ RadminAsistente.dc.html)`  
**Archivos del repo:** RadminAssistantWindow.xaml(.cs), Services/RadminAssistantService.cs (RadminStage), Services/RadminVpnService.cs, Controls/SupportLink.cs

### Qué cambia

Es una ventana global y flotante (abajo a la derecha, siempre encima), así que va en neutros: barra #0b1422 con la transparencia del usuario y resplandor azul. Sigue midiendo 430 de ancho y su alto se ajusta al contenido. Se mantiene lo del turno 12: un solo paso abierto, los hechos plegados a un renglón con check verde, los pendientes atenuados, la barra de segmentos, el nombre de la red completo y, con todo en verde, la confirmación con la red y «Ver pasos». La ayuda de Discord sigue sola en su línea. Cambios: tres pasos en vez de cuatro, porque «Confirmar la conexión» no era algo que hicieras: el launcher pasa de «Únete a la red» a todo en verde, y la línea «Esperando a que te unas» ya hace ese papel. «Abrir Radmin» va dentro de los pasos que lo necesitan (iniciar sesión y unirse a la red), en vez de como enlace en el renglón del paso 1. «Copiado» aparece solo al copiar, en lugar de decir siempre «Ya está en tu portapapeles». El botón principal es la acción del paso, y «Cerrar» pasa a secundario salvo cuando ya estás en la red. En el paso 3 hay una pista para cuando ya te uniste y no se marca, que es lo que pasa si el launcher no puede leer el registro de Radmin. Además, el título dice «Asistente de Radmin» para no confundirse con la ventana de Radmin, que se abre justo al lado. El paso 1 instala Radmin desde el launcher, con su barra y el aviso del permiso de Windows, y deja la web como alternativa (78f). El punto de «Esperando…» late cada 3 s, al ritmo del sondeo, para que se note que el launcher está comprobando. En el prototipo, la acción de cada paso simula que lo hiciste.

### Variantes

- **78a:** Radmin no está instalado. «Instalar Radmin VPN» lo instala desde el launcher; la web queda como alternativa.
- **78b:** Instalado, pero sin sesión. «Abrir Radmin» va en este paso.
- **78c:** Con sesión iniciada: falta unirse a la red. «Copiar» es la acción principal.
- **78d:** En la red. Con todo en verde, la ventana es la confirmación y el nombre de la red.
- **78e:** En la red, con «Ver pasos» abierto: los tres pasos plegados.
- **78f:** Instalando Radmin desde el launcher: la barra sustituye al botón dentro del paso 1.

### Requiere código o hay que confirmar

- Tres pasos (StepCount = 3): el paso 4 nunca se abría, porque DoneCount salta de LoggedIn a InAoE3Network.
- «Abrir Radmin» dentro de los pasos 2 y 3, en lugar del enlace del renglón del paso 1.
- RadAsstCopyDone solo después de copiar. Confirmar que nada copia el nombre de la red al abrir la ventana.
- «Cerrar» secundario salvo en «En la red»; el botón principal es la acción del paso.
- Pista en el paso 3 para cuando el launcher no puede leer el registro de Radmin.
- Instalar desde el asistente reutilizando el instalador del aviso de Multijugador. Cambia la decisión escrita en Step1OpenBtn_Click: confirmar antes.
- Título «Asistente de Radmin»; el punto de «Esperando…» late con cada sondeo (3 s).
- Sugerencia: ocultar la ventana al lanzar el juego, porque es Topmost.

---

## 73 · Publicar mi mod

**Prototipo:** `Publicar mi mod v2.dc.html`  
**Archivos del repo:** PublishModDialog.xaml, PublishModDialog.xaml.cs, Localization/Strings.cs (PublishWizard*)

### Qué cambia

Mismo chasis que Ajustes del launcher (72): barra de título neutra, los 6 pasos en la lista de la izquierda con su estado (✓ hecho, punto ámbar si falta un dato) y el contenido en tarjetas de cristal. Cada campo es una fila como en Ajustes: nombre y ayuda a la izquierda, campo a la derecha; los de varias líneas ocupan todo el ancho. Así la ventana crece con el launcher sin dejar una columna estrecha en medio. Se mantiene lo decidido en el turno 20: campos agrupados por pregunta, marcador tras una casilla, «Avanzado» plegado y Revisar empezando por lo que falta. Prueba: rellena «Archivo testigo», cambia el tipo o el mecanismo, abre «Avanzado» o pulsa una pastilla en Revisar. En el paso 4, «Comprobar» sale bien con v1.0.0, sin .zip con v0.9.0 y sin release con cualquier otra etiqueta. El mod.json es ilustrativo: sus claves reales las fija BuildModJsonTests.cs.

### Variantes

- **73a:** Interactivo.

### Requiere código o hay que confirmar

- La ventana deja de ser fija (760 × 640) y crece con el launcher, con el chasis de Ajustes del launcher: hay que rehacerla.
- «Comprobar en GitHub» (paso 4) es nuevo: consulta si existe la release de la etiqueta y si trae el .zip.
- Confirmar antes de cerrar si hay datos escritos; «Abrir PR en GitHub» desactivado sin id válido o sin nombre, con el motivo.
- Confirmar qué datos son obligatorios con el esquema del catálogo (BuildModJsonTests.cs).
- Contrastar los textos con Strings.cs (PublishWizard*): varios los deduje del nombre de su clave.
