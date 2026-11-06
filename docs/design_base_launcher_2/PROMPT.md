Repo: Gorgorito12/AoE3-Mod-Launcher (WarsOfLibertyLauncher), rama main.
Copia esta carpeta en docs/design_base_launcher_2/ y lee README.md completo antes de escribir código. Las reglas comunes son las de docs/design_base_launcher/BASE.md (el paquete anterior, pantallas 68 a 79); este paquete las da por hechas.

Qué es: la segunda tanda del rediseño con la base de la Biblioteca unificada (68a): pantallas 95 a 102. Perfil, Partida rápida, Inicio de sesión, Estadísticas, Ver una partida, Instalar copia estable y Desinstalar el launcher, Generar parche y Empaquetar traducción. Cada una tiene su prototipo HTML: ábrelo en un navegador con support.js y las carpetas mods/ y design_handoff_workshop/ al lado. El HTML es referencia de diseño: recréalo en WPF/XAML con los estilos y controles que ya existen; no lo copies.

Cómo trabajar:
1. Comprueba que la base de BASE.md ya está en main (niveles de cristal, barra de título de 40 uniforme, botones secundarios sólidos #1a2c48 con borde rgba(130,175,255,.22), colores de estado fijos). Si falta algo, hazlo primero.
2. Una pantalla por commit, en el orden del README: 97 · 100 · 101 · 102 · 99 · 96 · 98 · 95 (de la más pequeña a la más grande).
3. Cambia solo lo que dice la sección de cada pantalla. Lo de «Requiere código o hay que confirmar» es comportamiento nuevo o un dato que no pude ver: compruébalo antes y avísame si no se cumple, sin inventarlo.
4. Todo texto nuevo va en Localization/Strings.cs, en español (es-419, tuteo) e inglés. Los textos en español de los prototipos que no salen de Strings.cs son propuestas: si ya hay una clave equivalente, usa la suya.
5. Prueba a 1366×768, 1920×1080 y 2560×1440, con las tres transparencias y con efectos reducidos. Los diálogos, además, en su tamaño mínimo.

Al terminar cada pantalla, dime qué cambiaste y qué quedó pendiente.
