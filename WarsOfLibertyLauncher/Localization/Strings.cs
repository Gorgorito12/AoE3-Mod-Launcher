using System;
using System.Collections.Generic;

namespace WarsOfLibertyLauncher.Localization;

/// <summary>
/// Centralized string table for the launcher UI.
///
/// English is the default language. To add a new language, add a new key to
/// each entry's inner dictionary. Strings missing a translation fall back
/// to English; if even English is missing, the key itself is returned so
/// missing translations are visible in the UI.
///
/// Use <see cref="Format"/> for parameterized messages (uses string.Format
/// semantics with {0}, {1}, ... placeholders).
///
/// Diagnostic log messages are NOT localized — they're always in English
/// because they're meant for developers and bug reports.
/// </summary>
public static class Strings
{
    public const string LangEn = "en";
    public const string LangEs = "es";

    public static string Language { get; set; } = LangEn;

    /// <summary>Raised whenever <see cref="Language"/> changes so the UI can refresh.</summary>
    public static event Action? LanguageChanged;

    public static void SetLanguage(string lang)
    {
        if (lang != LangEn && lang != LangEs) lang = LangEn;
        if (Language == lang) return;
        Language = lang;
        LanguageChanged?.Invoke();
    }

    public static string Get(string key)
    {
        if (Table.TryGetValue(key, out var langs))
        {
            if (langs.TryGetValue(Language, out var localized)) return localized;
            if (langs.TryGetValue(LangEn, out var fallback)) return fallback;
        }
        return key;     // visible signal of missing translation
    }

    public static string Format(string key, params object?[] args) =>
        string.Format(Get(key), args);

    // ------------------------------------------------------------------------
    // String table: ordered roughly by where strings appear in the launcher.
    // Keep keys descriptive and stable — they're referenced from XAML/code.
    // ------------------------------------------------------------------------
    private static readonly Dictionary<string, Dictionary<string, string>> Table = new()
    {
        // -------- Window / labels --------
        ["WindowTitle"] = new()
        {
            [LangEn] = "Wars of Liberty Launcher",
            [LangEs] = "Wars of Liberty Launcher",
        },
        ["Subtitle"] = new()
        {
            [LangEn] = "Launcher",
            [LangEs] = "Launcher",
        },
        ["InstalledVersion"] = new()
        {
            [LangEn] = "INSTALLED VERSION",
            [LangEs] = "VERSIÓN INSTALADA",
        },
        ["LatestVersion"] = new()
        {
            [LangEn] = "LATEST AVAILABLE",
            [LangEs] = "ÚLTIMA DISPONIBLE",
        },
        ["ModPath"] = new()
        {
            [LangEn] = "MOD PATH",
            [LangEs] = "RUTA DEL MOD",
        },
        ["ChangePathButton"] = new()
        {
            [LangEn] = "Change...",
            [LangEs] = "Cambiar...",
        },
        ["NewsPlaceholder"] = new()
        {
            [LangEn] = "News from the latest update will appear here.",
            [LangEs] = "Las novedades de la última actualización aparecerán aquí.",
        },

        // -------- Buttons --------
        ["BtnUpdate"] = new()
        {
            [LangEn] = "UPDATE",
            [LangEs] = "ACTUALIZAR",
        },
        ["BtnVerify"] = new()
        {
            [LangEn] = "CHECK",
            [LangEs] = "VERIFICAR",
        },
        ["BtnPlay"] = new()
        {
            [LangEn] = "PLAY",
            [LangEs] = "JUGAR",
        },
        ["BtnPause"] = new()
        {
            [LangEn] = "PAUSE",
            [LangEs] = "PAUSAR",
        },
        ["BtnResume"] = new()
        {
            [LangEn] = "RESUME",
            [LangEs] = "REANUDAR",
        },
        ["BtnCancel"] = new()
        {
            [LangEn] = "CANCEL",
            [LangEs] = "CANCELAR",
        },
        ["StatusPaused"] = new()
        {
            [LangEn] = "Download paused. Click RESUME to continue from where you left off.",
            [LangEs] = "Descarga pausada. Haz click en REANUDAR para continuar desde donde quedaste.",
        },

        // -------- Status: idle / ready --------
        ["StatusReady"] = new()
        {
            [LangEn] = "Ready.",
            [LangEs] = "Listo.",
        },
        ["StatusUpToDate"] = new()
        {
            [LangEn] = "Up to date. Version {0}. Ready to play!",
            [LangEs] = "Todo al día. Versión {0}. ¡Listo para jugar!",
        },
        ["StatusVersionTooOld"] = new()
        {
            [LangEn] = "Your version ({0}) is too old to update via patches. " +
                       "Latest available: {1}. Please reinstall Wars of Liberty from aoe3wol.com.",
            [LangEs] = "Tu versión ({0}) es demasiado antigua para actualizar por parches. " +
                       "Última disponible: {1}. Necesitas reinstalar Wars of Liberty desde aoe3wol.com.",
        },
        ["StatusUpdatesAvailable"] = new()
        {
            [LangEn] = "{0} update(s) available ({1} total).",
            [LangEs] = "{0} actualización(es) disponible(s) ({1} total).",
        },
        ["StatusInstallNotFound"] = new()
        {
            [LangEn] = "Wars of Liberty was not auto-detected. " +
                       "Use the \"Change...\" button to select the folder manually.",
            [LangEs] = "No se detectó automáticamente Wars of Liberty. " +
                       "Usa el botón \"Cambiar...\" para indicar manualmente la carpeta.",
        },

        // -------- Status: in progress --------
        ["StatusDetectingInstall"] = new()
        {
            [LangEn] = "Detecting Wars of Liberty installation...",
            [LangEs] = "Detectando instalación de Wars of Liberty...",
        },
        ["StatusFetchingManifest"] = new()
        {
            [LangEn] = "Downloading update information...",
            [LangEs] = "Descargando información de actualizaciones...",
        },
        ["StatusIdentifyingVersion"] = new()
        {
            [LangEn] = "Identifying installed version...",
            [LangEs] = "Identificando versión instalada...",
        },
        ["StatusVerifyingExisting"] = new()
        {
            [LangEn] = "Verifying existing file for update #{0}...",
            [LangEs] = "Verificando archivo existente para actualización #{0}...",
        },
        ["StatusDownloading"] = new()
        {
            [LangEn] = "Downloading update #{0} (version {1})...",
            [LangEs] = "Descargando actualización #{0} (versión {1})...",
        },
        ["StatusVerifyingDownload"] = new()
        {
            [LangEn] = "Verifying integrity of update #{0}...",
            [LangEs] = "Verificando integridad de actualización #{0}...",
        },
        ["StatusApplying"] = new()
        {
            [LangEn] = "Applying update #{0}...",
            [LangEs] = "Aplicando actualización #{0}...",
        },
        ["StatusExtracting"] = new()
        {
            [LangEn] = "Extracting: {0}",
            [LangEs] = "Extrayendo: {0}",
        },
        ["StatusExtractFailedRestoring"] = new()
        {
            [LangEn] = "Extraction failed. Restoring backup files...",
            [LangEs] = "Error durante la extracción. Restaurando archivos...",
        },
        ["StatusCleanup"] = new()
        {
            [LangEn] = "Running post-update cleanup #{0}...",
            [LangEs] = "Aplicando limpieza post-actualización #{0}...",
        },
        ["StatusAllDone"] = new()
        {
            [LangEn] = "All updates applied successfully.",
            [LangEs] = "Todas las actualizaciones aplicadas correctamente.",
        },
        ["StatusCancelledCheck"] = new()
        {
            [LangEn] = "Check cancelled.",
            [LangEs] = "Verificación cancelada.",
        },
        ["StatusCancelledUpdate"] = new()
        {
            [LangEn] = "Update cancelled.",
            [LangEs] = "Actualización cancelada.",
        },

        // -------- Progress display --------
        ["ProgressUpdating"] = new()
        {
            [LangEn] = "Updating {0} → {1}",
            [LangEs] = "Actualizando {0} → {1}",
        },
        ["ProgressPatchOf"] = new()
        {
            [LangEn] = "Patch {0} of {1}: {2} → {3}",
            [LangEs] = "Parche {0} de {1}: {2} → {3}",
        },
        ["ProgressCurrentPatch"] = new()
        {
            [LangEn] = "Current patch",
            [LangEs] = "Parche actual",
        },
        ["ProgressOverall"] = new()
        {
            [LangEn] = "Overall",
            [LangEs] = "Total",
        },
        ["ProgressSpeed"] = new()
        {
            [LangEn] = "Speed: {0}/s",
            [LangEs] = "Velocidad: {0}/s",
        },
        ["ProgressEta"] = new()
        {
            [LangEn] = "ETA: {0}",
            [LangEs] = "Tiempo restante: {0}",
        },
        ["ProgressEtaCalculating"] = new()
        {
            [LangEn] = "calculating...",
            [LangEs] = "calculando...",
        },

        // -------- Dialogs --------
        ["DlgInvalidFolderTitle"] = new()
        {
            [LangEn] = "Invalid folder",
            [LangEs] = "Carpeta no válida",
        },
        ["DlgInvalidFolderBody"] = new()
        {
            [LangEn] = "The selected folder doesn't appear to be a valid Wars of Liberty installation.\n\n" +
                       "Expected to find the subfolder 'art\\zulushield' inside.",
            [LangEs] = "La carpeta seleccionada no parece ser una instalación válida de Wars of Liberty.\n\n" +
                       "Esperaba encontrar la subcarpeta 'art\\zulushield' adentro.",
        },
        ["DlgFolderPickerTitle"] = new()
        {
            [LangEn] = "Select Wars of Liberty folder",
            [LangEs] = "Seleccionar carpeta de Wars of Liberty",
        },
        ["DlgGameLaunchErrorTitle"] = new()
        {
            [LangEn] = "Could not start the game",
            [LangEs] = "Error al iniciar el juego",
        },
        ["DlgUpdateErrorTitle"] = new()
        {
            [LangEn] = "Update error",
            [LangEs] = "Error en actualización",
        },

        // -------- Errors (also surface in dialogs) --------
        ["ErrManifestUnreachable"] = new()
        {
            [LangEn] = "Could not fetch UpdateInfo.xml from any server.\n" +
                       "Primary ({0}): {1}\nAlternate ({2}): {3}",
            [LangEs] = "No se pudo obtener UpdateInfo.xml de ningún servidor.\n" +
                       "Primario ({0}): {1}\nAlternativo ({2}): {3}",
        },
        ["ErrManifestEmpty"] = new()
        {
            [LangEn] = "UpdateInfo.xml is empty or malformed.",
            [LangEs] = "UpdateInfo.xml está vacío o malformado.",
        },
        ["ErrCorruptDownload"] = new()
        {
            [LangEn] = "Update #{0} arrived corrupted. Expected CRC32: {1}, actual: {2}.",
            [LangEs] = "La actualización #{0} llegó corrupta. CRC32 esperado: {1}, real: {2}.",
        },
        ["ErrGameExeNotFound"] = new()
        {
            [LangEn] = "'age3y.exe' (Age of Empires III: The Asian Dynasties) not found.\n\n" +
                       "Wars of Liberty needs Age of Empires III installed to work.\n" +
                       "Use the \"Change...\" button to point to the correct folder, " +
                       "or set 'gameExecutable' manually in launcher-config.json.",
            [LangEs] = "No se encontró 'age3y.exe' (Age of Empires III: The Asian Dynasties).\n\n" +
                       "Wars of Liberty necesita Age of Empires III instalado para funcionar.\n" +
                       "Usa el botón \"Cambiar...\" para indicar la carpeta correcta, " +
                       "o configura 'gameExecutable' manualmente en launcher-config.json.",
        },
        ["ErrInstallPathMissing"] = new()
        {
            [LangEn] = "Install path not detected. Call CheckAsync first.",
            [LangEs] = "Ruta de instalación no detectada. Llama a CheckAsync primero.",
        },

        // -------- Installer flow (used when WoL isn't installed yet) --------
        ["BtnInstall"] = new()
        {
            [LangEn] = "INSTALL MOD",
            [LangEs] = "INSTALAR MOD",
        },
        ["StatusNotInstalled"] = new()
        {
            [LangEn] = "Wars of Liberty is not installed. Click \"INSTALL MOD\" to download and install it.",
            [LangEs] = "Wars of Liberty no está instalado. Haz click en \"INSTALAR MOD\" para descargarlo e instalarlo.",
        },
        ["StatusDownloadingInstaller"] = new()
        {
            [LangEn] = "Downloading Wars of Liberty installer (large file, ~2.7 GB)...",
            [LangEs] = "Descargando instalador de Wars of Liberty (archivo grande, ~2.7 GB)...",
        },
        ["StatusExtractingInstaller"] = new()
        {
            [LangEn] = "Extracting installer files...",
            [LangEs] = "Extrayendo archivos del instalador...",
        },
        ["StatusLaunchingInstaller"] = new()
        {
            [LangEn] = "Installer running. Follow the on-screen wizard to choose where to install.",
            [LangEs] = "Instalador en ejecución. Sigue el asistente en pantalla para elegir dónde instalar.",
        },
        ["DlgInstallTitle"] = new()
        {
            [LangEn] = "Install Wars of Liberty",
            [LangEs] = "Instalar Wars of Liberty",
        },
        ["DlgPickInstallFolderTitle"] = new()
        {
            [LangEn] = "Choose where to install Wars of Liberty",
            [LangEs] = "Elige dónde instalar Wars of Liberty",
        },
        ["DlgPickInstallFolderHeader"] = new()
        {
            [LangEn] = "Install location",
            [LangEs] = "Ubicación de instalación",
        },
        ["DlgPickInstallFolderDescription"] = new()
        {
            [LangEn] = "Wars of Liberty will be installed to the folder below. " +
                       "The folder will be created automatically if it doesn't exist. " +
                       "About 12 GB of free space is recommended.",
            [LangEs] = "Wars of Liberty se instalará en la carpeta de abajo. " +
                       "Se creará automáticamente si no existe. " +
                       "Se recomiendan unos 12 GB de espacio libre.",
        },
        ["DlgPickInstallFolderLabel"] = new()
        {
            [LangEn] = "INSTALL FOLDER",
            [LangEs] = "CARPETA DE INSTALACIÓN",
        },
        ["WarnPathEmpty"] = new()
        {
            [LangEn] = "Please enter a folder path.",
            [LangEs] = "Por favor ingresa la ruta de una carpeta.",
        },
        ["WarnPathInvalid"] = new()
        {
            [LangEn] = "This doesn't look like a valid Windows path.",
            [LangEs] = "Esto no parece una ruta válida de Windows.",
        },
        ["WarnPathSystem"] = new()
        {
            [LangEn] = "This folder is reserved by Windows. Please choose a different location.",
            [LangEs] = "Esta carpeta está reservada por Windows. Por favor elige otra ubicación.",
        },

        // -------- AoE3 detection warnings --------
        ["DlgFolderNotInAoE3Title"] = new()
        {
            [LangEn] = "This folder is not inside Age of Empires III",
            [LangEs] = "Esta carpeta no está dentro de Age of Empires III",
        },
        ["DlgFolderNotInAoE3Body"] = new()
        {
            [LangEn] = "The chosen folder doesn't appear to be inside an Age of Empires III " +
                       "installation:\n\n{0}\n\n" +
                       "Wars of Liberty is a mod for AoE3 — the game engine only loads the mod " +
                       "files when they sit inside the AoE3 folder. If you install elsewhere, " +
                       "the files will copy successfully but the game won't actually use them.\n\n" +
                       "Click OK to install here anyway, or Cancel to choose a different folder.",
            [LangEs] = "La carpeta elegida no parece estar dentro de una instalación de " +
                       "Age of Empires III:\n\n{0}\n\n" +
                       "Wars of Liberty es un mod para AoE3 — el motor del juego solo carga los " +
                       "archivos del mod cuando están dentro de la carpeta de AoE3. Si lo instalas " +
                       "en otro lugar, los archivos se copiarán pero el juego no los usará.\n\n" +
                       "Haz click en Aceptar para instalar aquí de todos modos, o Cancelar para " +
                       "elegir otra carpeta.",
        },
        ["DlgBrokenInstallTitle"] = new()
        {
            [LangEn] = "Wars of Liberty may not be working correctly",
            [LangEs] = "Wars of Liberty podría no estar funcionando correctamente",
        },
        ["DlgBrokenInstallBody"] = new()
        {
            [LangEn] = "Wars of Liberty was found at:\n\n{0}\n\n" +
                       "But this folder doesn't appear to be inside Age of Empires III. " +
                       "The mod files are on disk, but the AoE3 engine won't load them from " +
                       "this location.\n\n" +
                       "To fix this, reinstall Wars of Liberty into the same folder as " +
                       "Age of Empires III (typically your Steam library).",
            [LangEs] = "Se encontró Wars of Liberty en:\n\n{0}\n\n" +
                       "Pero esta carpeta no parece estar dentro de Age of Empires III. " +
                       "Los archivos del mod están en disco, pero el motor de AoE3 no los va a " +
                       "cargar desde esta ubicación.\n\n" +
                       "Para arreglarlo, reinstala Wars of Liberty en la misma carpeta donde " +
                       "tienes Age of Empires III (típicamente tu biblioteca de Steam).",
        },

        // -------- Elevation (UAC) --------
        ["DlgElevationRequiredTitle"] = new()
        {
            [LangEn] = "Administrator permission required",
            [LangEs] = "Se requieren permisos de administrador",
        },
        ["DlgElevationRequiredBody"] = new()
        {
            [LangEn] = "Wars of Liberty is installed in a protected folder:\n{0}\n\n" +
                       "To apply updates there, the launcher needs to be run as administrator. " +
                       "Click OK to restart the launcher with elevated privileges. " +
                       "Windows will ask for confirmation.",
            [LangEs] = "Wars of Liberty está instalado en una carpeta protegida:\n{0}\n\n" +
                       "Para aplicar actualizaciones ahí, el launcher necesita ejecutarse como " +
                       "administrador. Haz click en Aceptar para reiniciar el launcher con " +
                       "permisos elevados. Windows pedirá confirmación.",
        },
        ["StatusElevationDenied"] = new()
        {
            [LangEn] = "Update cancelled — administrator permission was denied.",
            [LangEs] = "Actualización cancelada — se rechazó el permiso de administrador.",
        },
        ["StatusRunningAsAdmin"] = new()
        {
            [LangEn] = "(running as administrator)",
            [LangEs] = "(ejecutando como administrador)",
        },

        // -------- AoE3 detection / clone flow --------
        ["BtnContinue"] = new()
        {
            [LangEn] = "CONTINUE",
            [LangEs] = "CONTINUAR",
        },
        ["DlgAoe3PickerTitle"] = new()
        {
            [LangEn] = "Choose Age of Empires III source",
            [LangEs] = "Elige la fuente de Age of Empires III",
        },
        ["DlgAoe3PickerHeader"] = new()
        {
            [LangEn] = "Select the AoE3 installation to copy from",
            [LangEs] = "Selecciona la instalación de AoE3 que se copiará",
        },
        ["DlgAoe3PickerDescription"] = new()
        {
            [LangEn] = "Wars of Liberty is installed alongside a copy of Age of Empires III: " +
                       "The Asian Dynasties so it works without affecting your Steam/GOG install. " +
                       "Pick the source below or browse to a folder manually.",
            [LangEs] = "Wars of Liberty se instala junto con una copia de Age of Empires III: " +
                       "The Asian Dynasties para que funcione sin afectar tu instalación de Steam/GOG. " +
                       "Elige la fuente abajo o navega manualmente a una carpeta.",
        },
        ["DlgAoe3PickerBrowse"] = new()
        {
            [LangEn] = "Browse for AoE3 folder...",
            [LangEs] = "Buscar carpeta de AoE3...",
        },
        ["AoeSourceManual"] = new()
        {
            [LangEn] = "Manual",
            [LangEs] = "Manual",
        },
        ["WarnNotValidAoe3"] = new()
        {
            [LangEn] = "That folder doesn't look like a valid AoE3: The Asian Dynasties install. " +
                       "It must contain bin\\age3y.exe, bin\\RockallDLL.dll and data\\protoy.xml.",
            [LangEs] = "Esa carpeta no parece una instalación válida de AoE3: The Asian Dynasties. " +
                       "Debe contener bin\\age3y.exe, bin\\RockallDLL.dll y data\\protoy.xml.",
        },

        // -------- AoE3 not found --------
        ["DlgAoe3NotFoundTitle"] = new()
        {
            [LangEn] = "Age of Empires III not found",
            [LangEs] = "No se encontró Age of Empires III",
        },
        ["DlgAoe3NotFoundBody"] = new()
        {
            [LangEn] = "The launcher couldn't find Age of Empires III: The Asian Dynasties on your " +
                       "computer.\n\nWars of Liberty needs an existing AoE3:TAD installation to copy " +
                       "from (the original AoE3, not the Definitive Edition).\n\n" +
                       "If you have AoE3 installed in a non-standard location, click Browse... to " +
                       "point to it manually.",
            [LangEs] = "El launcher no pudo encontrar Age of Empires III: The Asian Dynasties en tu " +
                       "computadora.\n\nWars of Liberty necesita una instalación existente de AoE3:TAD " +
                       "para copiar (el AoE3 original, no la Definitive Edition).\n\n" +
                       "Si tienes AoE3 instalado en una ubicación no estándar, haz click en Buscar... " +
                       "para indicarla manualmente.",
        },

        // -------- Disk space confirmation --------
        ["DlgConfirmCopyTitle"] = new()
        {
            [LangEn] = "Confirm install",
            [LangEs] = "Confirmar instalación",
        },
        ["DlgConfirmCopyBody"] = new()
        {
            [LangEn] = "About to install Wars of Liberty:\n\n" +
                       "  • Copy AoE3 from: {0}\n" +
                       "  • Copy size: ~{1}\n" +
                       "  • Install to: {2}\n" +
                       "  • Free space available: {3}\n\n" +
                       "After copying, the installer will download Wars of Liberty (~2.7 GB) and " +
                       "apply it on top.\n\nContinue?",
            [LangEs] = "Por instalar Wars of Liberty:\n\n" +
                       "  • Copiar AoE3 desde: {0}\n" +
                       "  • Tamaño de la copia: ~{1}\n" +
                       "  • Instalar en: {2}\n" +
                       "  • Espacio libre disponible: {3}\n\n" +
                       "Después de copiar, el instalador descargará Wars of Liberty (~2.7 GB) y " +
                       "lo aplicará encima.\n\n¿Continuar?",
        },
        ["DlgNotEnoughSpaceTitle"] = new()
        {
            [LangEn] = "Not enough disk space",
            [LangEs] = "No hay espacio suficiente en disco",
        },
        ["DlgNotEnoughSpaceBody"] = new()
        {
            [LangEn] = "The selected destination has only {0} free, but the install needs about {1}. " +
                       "Free up space or choose a different destination.",
            [LangEs] = "El destino seleccionado tiene solo {0} libres, pero la instalación necesita " +
                       "unos {1}. Libera espacio o elige otro destino.",
        },

        // -------- Clone progress --------
        ["StatusCopyingAoe3"] = new()
        {
            [LangEn] = "Copying Age of Empires III...",
            [LangEs] = "Copiando Age of Empires III...",
        },
        ["StatusCopyingFile"] = new()
        {
            [LangEn] = "Copying: {0}",
            [LangEs] = "Copiando: {0}",
        },
        ["StatusInstallingFiles"] = new()
        {
            [LangEn] = "Installing files: {0:0.0}% ({1} files copied)",
            [LangEs] = "Instalando archivos: {0:0.0}% ({1} archivos copiados)",
        },
        ["StatusFinishingInstall"] = new()
        {
            [LangEn] = "Finishing installation (registry, shortcuts)...",
            [LangEs] = "Finalizando instalación (registro, accesos directos)...",
        },
        ["StatusInstallSuccess"] = new()
        {
            [LangEn] = "Wars of Liberty installed successfully.",
            [LangEs] = "Wars of Liberty se instaló correctamente.",
        },
        ["StatusInstallFailed"] = new()
        {
            [LangEn] = "Installer exited with code {0}. The installation may not be complete.",
            [LangEs] = "El instalador terminó con código {0}. Es posible que la instalación no se haya completado.",
        },
        ["DlgInstallNoUrlBody"] = new()
        {
            [LangEn] = "No installer URL is configured.\n\n" +
                       "Click OK to open the official Wars of Liberty website where you can " +
                       "download the installer manually.",
            [LangEs] = "No hay una URL de instalador configurada.\n\n" +
                       "Haz click en Aceptar para abrir el sitio oficial de Wars of Liberty " +
                       "y descargar el instalador manualmente.",
        },
        ["DlgInstallConfirmTitle"] = new()
        {
            [LangEn] = "Install Wars of Liberty",
            [LangEs] = "Instalar Wars of Liberty",
        },
        ["DlgInstallConfirmBody"] = new()
        {
            [LangEn] = "The launcher will:\n" +
                       "  1. Download the official installer (~2.7 GB)\n" +
                       "  2. Detect your AoE3 installation\n" +
                       "  3. Copy AoE3 to a \"Wars of Liberty\" folder\n" +
                       "  4. Install the mod on top of that copy\n\n" +
                       "Your original AoE3 installation will not be modified.\n" +
                       "Windows will ask for administrator permission. Continue?",
            [LangEs] = "El launcher hará lo siguiente:\n" +
                       "  1. Descargar el instalador oficial (~2.7 GB)\n" +
                       "  2. Detectar tu instalación de AoE3\n" +
                       "  3. Copiar AoE3 a una carpeta \"Wars of Liberty\"\n" +
                       "  4. Instalar el mod sobre esa copia\n\n" +
                       "Tu instalación original de AoE3 no será modificada.\n" +
                       "Windows pedirá permiso de administrador. ¿Continuar?",
        },
        ["ErrInstallerExeNotFound"] = new()
        {
            [LangEn] = "Could not locate the installer .exe inside the downloaded ZIP.",
            [LangEs] = "No se pudo encontrar el .exe del instalador dentro del ZIP descargado.",
        },

        // -------- Launcher self-update --------
        ["DlgLauncherUpdateTitle"] = new()
        {
            [LangEn] = "Launcher update available",
            [LangEs] = "Actualización del launcher disponible",
        },
        ["DlgLauncherUpdateBody"] = new()
        {
            [LangEn] = "A new version of the launcher is available.\n\n" +
                       "  Current: {0}\n  New: {1}\n  Size: {2}\n\n" +
                       "Update now?",
            [LangEs] = "Hay una nueva versión del launcher disponible.\n\n" +
                       "  Actual: {0}\n  Nueva: {1}\n  Tamaño: {2}\n\n" +
                       "¿Actualizar ahora?",
        },
        ["StatusDownloadingLauncherUpdate"] = new()
        {
            [LangEn] = "Downloading launcher update...",
            [LangEs] = "Descargando actualización del launcher...",
        },
    };
}
