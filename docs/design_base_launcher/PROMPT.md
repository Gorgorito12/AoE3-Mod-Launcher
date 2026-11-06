Repo: Gorgorito12/AoE3-Mod-Launcher (WarsOfLibertyLauncher), rama main.
Copia esta carpeta en docs/design_base_launcher/ y lee README.md y BASE.md completos antes de escribir código.

Qué es: el rediseño de los menús y diálogos del launcher (pantallas 68 a 79) con la base de la Biblioteca unificada (68a). Cada pantalla tiene su prototipo HTML: ábrelo en un navegador, con support.js y las carpetas mods/ y design_handoff_workshop/ al lado. El HTML es referencia de diseño: recréalo en WPF/XAML con los estilos y controles que ya existen; no lo copies.

Cómo trabajar:
1. Primero la base compartida de BASE.md: los niveles de cristal según Ajustes › Interfaz › Apariencia (predeterminado Media), la barra de título de 40 de un solo color con su transparencia, los botones secundarios sólidos #1a2c48 con borde rgba(130,175,255,.22), los colores de estado fijos y el cristal de menús y popovers.
2. Después, una pantalla por commit, en el orden del README.
3. Cambia solo lo que dice la sección de cada pantalla. Lo de «Requiere código o hay que confirmar» es comportamiento nuevo o un dato que no pude ver: compruébalo antes y avísame si no se cumple, sin inventarlo.
4. Todo texto nuevo va en Localization/Strings.cs, en español (es-419, tuteo) e inglés. Los textos en español de los prototipos que no salen de Strings.cs son propuestas: si ya hay una clave equivalente, usa la suya.
5. Prueba a 1366×768, 1920×1080 y 2560×1440, con las tres transparencias y con efectos reducidos.

Al terminar cada pantalla, dime qué cambiaste y qué quedó pendiente.
