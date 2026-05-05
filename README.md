# Wars of Liberty Launcher (v0.2)

Reemplazo del updater Java oficial. **100% compatible** con el servidor actual
de Wars of Liberty — habla el mismo formato `UpdateInfo.xml` y procesa los mismos
`.tar.xz`, pero sin necesidad de Java y con mejor experiencia.

## Cambios respecto al updater Java

| | Updater Java (v1.4) | Launcher nuevo (v0.2) |
|---|---|---|
| Runtime | Requiere Java | Self-contained .exe (sin instalar nada) |
| Resume si se corta | No | Sí (HTTP Range requests) |
| Timeout | 5 segundos (causa errores en conexiones lentas) | 30 minutos por archivo |
| Detección de instalación | Registry | Registry (igual) |
| Verificación | CRC32 | CRC32 (igual) |
| Backup antes de aplicar | Sí | Sí (igual) |
| URL primaria + fallback | Sí | Sí (igual) |
| UI | Swing | WPF nativa con tema oscuro |
| Tamaño .exe | ~4 MB + JRE | ~12 MB self-contained |

## Cómo funciona

```
┌────────────────────────────────────────────────────┐
│  1. Detecta instalación leyendo el registry de    │
│     Windows ({EB448764-CABB-4766-8055-...})       │
├────────────────────────────────────────────────────┤
│  2. Descarga UpdateInfo.xml de aoe3wol.com        │
│     (con fallback a SourceForge)                  │
├────────────────────────────────────────────────────┤
│  3. Calcula MD5 de tres archivos clave:           │
│       data\protoy.xml                              │
│       data\techtreey.xml                           │
│       data\stringtabley.xml                        │
│     y los compara con la tabla del XML para       │
│     identificar la versión instalada.             │
├────────────────────────────────────────────────────┤
│  4. Determina cuáles parches .tar.xz necesita     │
│     descargar (todos desde minreqdownload).       │
├────────────────────────────────────────────────────┤
│  5. Para cada parche:                             │
│       a. Descarga (con resume y URL de respaldo)  │
│       b. Verifica CRC32                           │
│       c. Hace backup de los archivos a sobrescribir│
│       d. Extrae el .tar.xz sobre la instalación   │
│       e. Aplica la lista de archivos a borrar     │
│       f. Abre la página post-update si existe     │
└────────────────────────────────────────────────────┘
```

## Compilar

Necesitas **.NET 8 SDK** instalado.

```powershell
cd WarsOfLibertyLauncher

# Restaurar dependencias (SharpCompress, System.IO.Hashing)
dotnet restore

# Compilar
dotnet build -c Release

# Generar .exe portable y self-contained (no requiere .NET en el PC del usuario)
dotnet publish WarsOfLibertyLauncher\WarsOfLibertyLauncher.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true
```

El `.exe` final queda en
`WarsOfLibertyLauncher\bin\Release\net8.0-windows\win-x64\publish\WarsOfLibertyLauncher.exe`

## Configuración (`launcher-config.json`)

Se crea automáticamente al primer arranque. Valores por defecto:

```json
{
  "updateInfoUrl": "http://aoe3wol.com/updates/UpdateInfo.xml",
  "updateInfoUrlAlt": "http://master.dl.sourceforge.net/project/wars-of-liberty/Patches/UpdateInfo.xml",
  "modInstallPath": "",
  "gameExecutable": "C:\\Program Files (x86)\\Microsoft Games\\Age of Empires III\\age3y.exe",
  "gameArguments": "",
  "openPostUpdatePages": true
}
```

- **`modInstallPath` vacío** → se autodetecta desde el registry. Solo edita
  esto si tienes una instalación no estándar.
- **`gameExecutable`** → ajustar si tu Age of Empires III está en otra ruta
  (Steam, GOG, etc.).

## Estructura del proyecto

```
WarsOfLibertyLauncher/
├── Models/
│   ├── UpdateInfo.cs          Modelos del XML del servidor
│   └── LauncherConfig.cs      Config local
└── Services/
    ├── HashService.cs         MD5 + CRC32
    ├── RegistryService.cs     Detecta instalación de WoL
    ├── DownloadService.cs     HTTP con resume y fallback
    ├── UpdateInfoService.cs   Descarga y parsea UpdateInfo.xml
    ├── ArchiveService.cs      Extrae .tar.xz con backup
    ├── UpdateService.cs       Orquesta todo el flujo
    └── GameLauncher.cs        Lanza Age of Empires III
```

## Lo que viene

- **Fase 2 — Traducciones de la comunidad:** sistema de paquetes opcionales
  con backup/restore para múltiples idiomas.
- **Fase 3 — Pulido:** instalador con Inno Setup, panel de noticias real,
  auto-update del propio launcher (`<updaterinfo>` ya viene en el XML).
