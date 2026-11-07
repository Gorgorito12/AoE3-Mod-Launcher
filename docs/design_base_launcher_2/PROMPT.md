Repo: Gorgorito12/AoE3-Mod-Launcher (WarsOfLibertyLauncher), rama main. Servidor: el de la API de multijugador (lobby, torneos, stats).
Copia esta carpeta en docs/design_base_launcher_2/ y lee README.md completo antes de escribir código. Las reglas comunes están en BASE.md (incluido aquí, es la Base de diseño por escrito); el paquete anterior (docs/design_base_launcher) ya está implementado y no se repite.

Qué es: todo lo que falta por implementar del rediseño con la base de la Biblioteca unificada: torneos y salas (80–88), Clasificación con edades (93), la tanda 95–102 (ya en main; aquí van solo los ajustes posteriores) y los diálogos pequeños, menús y popovers (103). El README trae, al principio, la lista completa de cambios que necesitan código del launcher o del servidor; después, una sección por pantalla.

Cómo trabajar:
1. Empieza por «Cambios de código y servidor» del README. Lo del servidor se coordina aparte: implementa el launcher de forma que funcione con y sin el dato nuevo (si falta, se oculta lo que lo necesita; nunca se inventa).
2. Luego las pantallas, una por commit, en el orden del README: 103 (porque las demás usan sus diálogos) → Salas (88 · 87 · 86) → Torneos (81 · 82 · 83 · 85 · 80) → 93 → ajustes de 95–102.
3. Cambia solo lo que dice cada sección. Lo marcado «Hay que confirmar» se comprueba antes y, si no se cumple, me avisas en vez de inventarlo.
4. Todo texto nuevo va en Localization/Strings.cs, en español (es-419, tuteo) e inglés. Los textos en español de los prototipos que no salen de Strings.cs son propuestas.
5. Prueba a 1366×768, 1920×1080 y 2560×1440, con las tres transparencias y con efectos reducidos. Los diálogos, en su tamaño mínimo.

Al terminar cada pantalla, dime qué cambiaste, qué quedó pendiente y qué dato del servidor falta.
