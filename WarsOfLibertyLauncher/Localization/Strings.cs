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
        ["BrowseModButton"] = new()
        {
            [LangEn] = "Browse...",
            [LangEs] = "Examinar...",
        },

        // -------- Uninstall --------
        ["BtnUninstall"] = new()
        {
            [LangEn] = "Uninstall",
            [LangEs] = "Desinstalar",
        },
        ["BtnMore"] = new()
        {
            [LangEn] = "⋯",
            [LangEs] = "⋯",
        },
        ["MenuUninstall"] = new()
        {
            [LangEn] = "Uninstall mod...",
            [LangEs] = "Desinstalar mod...",
        },
        ["DlgUninstallTitle"] = new()
        {
            [LangEn] = "Uninstall Wars of Liberty",
            [LangEs] = "Desinstalar Wars of Liberty",
        },
        ["DlgUninstallHeader"] = new()
        {
            [LangEn] = "Uninstall Wars of Liberty",
            [LangEs] = "Desinstalar Wars of Liberty",
        },
        ["DlgUninstallDescription"] = new()
        {
            [LangEn] = "This will delete the entire Wars of Liberty install folder. Your Age of Empires III base game lives in a separate folder and will not be touched.",
            [LangEs] = "Esto eliminará la carpeta completa de Wars of Liberty. Tu instalación de Age of Empires III está en otra carpeta y no será modificada.",
        },
        ["DlgUninstallInstallPathLabel"] = new()
        {
            [LangEn] = "INSTALL FOLDER",
            [LangEs] = "CARPETA A ELIMINAR",
        },
        ["DlgUninstallOptionsTitle"] = new()
        {
            [LangEn] = "ALSO CLEAN UP",
            [LangEs] = "TAMBIÉN LIMPIAR",
        },
        ["DlgUninstallOptShortcuts"] = new()
        {
            [LangEn] = "Remove desktop and Start Menu shortcuts",
            [LangEs] = "Eliminar accesos directos del escritorio y menú inicio",
        },
        ["DlgUninstallOptRegistry"] = new()
        {
            [LangEn] = "Remove Windows registry entry (Add/Remove Programs)",
            [LangEs] = "Eliminar entrada del registro de Windows (Programas y características)",
        },
        ["DlgUninstallOptResetConfig"] = new()
        {
            [LangEn] = "Reset launcher config to defaults",
            [LangEs] = "Restablecer la configuración del launcher",
        },
        ["DlgUninstallAoE3SafeNote"] = new()
        {
            [LangEn] = "✓ Your Age of Empires III install (in Steam\\steamapps\\common\\Age Of Empires 3 or wherever it lives) will not be modified.",
            [LangEs] = "✓ Tu instalación de Age of Empires III (en Steam\\steamapps\\common\\Age Of Empires 3 o donde la tengas) no será modificada.",
        },
        ["DlgUninstallValidDetail"] = new()
        {
            [LangEn] = "{0} files in {1} folders will be removed.",
            [LangEs] = "Se eliminarán {0} archivos en {1} carpetas.",
        },
        ["DlgUninstallNotValidTitle"] = new()
        {
            [LangEn] = "✗ NOT A VALID WARS OF LIBERTY INSTALL",
            [LangEs] = "✗ NO ES UNA INSTALACIÓN VÁLIDA DE WARS OF LIBERTY",
        },
        ["DlgUninstallNotValidDetail"] = new()
        {
            [LangEn] = "The folder '{0}' does not contain the Wars of Liberty marker (art\\zulushield\\). For safety, the launcher refuses to delete it.\n\nIf this is a real WoL install with broken files, run Verify first to repair it.",
            [LangEs] = "La carpeta '{0}' no contiene el marcador de Wars of Liberty (art\\zulushield\\). Por seguridad, el launcher se niega a eliminarla.\n\nSi es una instalación real con archivos rotos, ejecuta Verificar primero para repararla.",
        },
        ["DlgUninstallNothingTitle"] = new()
        {
            [LangEn] = "NOTHING TO UNINSTALL",
            [LangEs] = "NADA QUE DESINSTALAR",
        },
        ["DlgUninstallNothingDetail"] = new()
        {
            [LangEn] = "No installation was detected.",
            [LangEs] = "No se detectó ninguna instalación.",
        },

        ["StatusUninstalling"] = new()
        {
            [LangEn] = "Uninstalling Wars of Liberty...",
            [LangEs] = "Desinstalando Wars of Liberty...",
        },
        ["StatusUninstallSuccess"] = new()
        {
            [LangEn] = "Wars of Liberty was uninstalled successfully ({0} files removed).",
            [LangEs] = "Wars of Liberty se desinstaló correctamente ({0} archivos eliminados).",
        },
        ["StatusUninstallPartial"] = new()
        {
            [LangEn] = "Uninstall finished with {0} error(s). Check the log for details.",
            [LangEs] = "Desinstalación terminada con {0} error(es). Revisa el log para más detalles.",
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
            [LangEn] = "VERIFY",
            [LangEs] = "VERIFICAR",
        },
        ["BtnCheckUpdates"] = new()
        {
            [LangEn] = "CHECK",
            [LangEs] = "BUSCAR",
        },
        ["BtnPlay"] = new()
        {
            [LangEn] = "PLAY",
            [LangEs] = "JUGAR",
        },
        ["BtnPlaying"] = new()
        {
            [LangEn] = "PLAYING...",
            [LangEs] = "JUGANDO...",
        },
        ["BtnStop"] = new()
        {
            [LangEn] = "STOP",
            [LangEs] = "DETENER",
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
        // Sub-phase-aware status lines shown just above the bars during update.
        // {0} = patch target version, {1} = current step, {2} = total steps.
        ["ProgressPatchStatusDownloading"] = new()
        {
            [LangEn] = "📥 Downloading {0} ({1}/{2})...",
            [LangEs] = "📥 Descargando {0} ({1}/{2})...",
        },
        ["ProgressPatchStatusVerifying"] = new()
        {
            [LangEn] = "✓ Verifying {0} ({1}/{2})...",
            [LangEs] = "✓ Verificando {0} ({1}/{2})...",
        },
        ["ProgressPatchStatusApplying"] = new()
        {
            [LangEn] = "🔧 Applying {0} ({1}/{2})...",
            [LangEs] = "🔧 Aplicando {0} ({1}/{2})...",
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
        // Phase-aware speed labels — picked dynamically so the user sees an
        // accurate description of what the bytes/sec figure represents.
        ["ProgressSpeedDownload"] = new()
        {
            [LangEn] = "📡 Download: {0}/s",
            [LangEs] = "📡 Descarga: {0}/s",
        },
        ["ProgressSpeedExtract"] = new()
        {
            [LangEn] = "📦 Extract: {0}/s",
            [LangEs] = "📦 Extracción: {0}/s",
        },
        ["ProgressSpeedCopy"] = new()
        {
            [LangEn] = "💾 Copy: {0}/s",
            [LangEs] = "💾 Copia: {0}/s",
        },
        ["ProgressSpeedVerify"] = new()
        {
            [LangEn] = "✓ Verifying: {0}/s",
            [LangEs] = "✓ Verificando: {0}/s",
        },
        ["ProgressUpdatingLabel"] = new()
        {
            [LangEn] = "UPDATING",
            [LangEs] = "ACTUALIZANDO",
        },
        ["ProgressPatchCounter"] = new()
        {
            [LangEn] = "PATCH {0}/{1}",
            [LangEs] = "PARCHE {0}/{1}",
        },
        ["ProgressEtaTotal"] = new()
        {
            [LangEn] = "⏱ {0} total",
            [LangEs] = "⏱ {0} total",
        },
        ["StatusExtractingPayload"] = new()
        {
            [LangEn] = "📦 Extracting mod files ({0}/{1})...",
            [LangEs] = "📦 Extrayendo archivos del mod ({0}/{1})...",
        },
        ["StatusInstallingMod"] = new()
        {
            [LangEn] = "🔧 Applying mod overlay ({0}/{1})...",
            [LangEs] = "🔧 Aplicando mod ({0}/{1})...",
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

        // -------- Phase breadcrumb step labels --------
        ["InstallStepDownload"] = new()
        {
            [LangEn] = "DOWNLOAD",
            [LangEs] = "DESCARGA",
        },
        ["InstallStepExtract"] = new()
        {
            [LangEn] = "EXTRACT",
            [LangEs] = "EXTRACCIÓN",
        },
        ["InstallStepClone"] = new()
        {
            [LangEn] = "CLONE",
            [LangEs] = "CLONAR",
        },
        ["InstallStepMod"] = new()
        {
            [LangEn] = "MOD",
            [LangEs] = "MOD",
        },
        ["InstallStepFinalize"] = new()
        {
            [LangEn] = "FINALIZE",
            [LangEs] = "FINALIZAR",
        },

        // -------- Update breadcrumb step labels --------
        ["UpdateStepDownload"] = new()
        {
            [LangEn] = "DOWNLOAD",
            [LangEs] = "DESCARGA",
        },
        ["UpdateStepVerify"] = new()
        {
            [LangEn] = "VERIFY",
            [LangEs] = "VERIFICAR",
        },
        ["UpdateStepApply"] = new()
        {
            [LangEn] = "APPLY",
            [LangEs] = "APLICAR",
        },

        // Subtitle of the header during update — shown under "Updating X → Y"
        ["ProgressPatchSubtitle"] = new()
        {
            [LangEn] = "Patch {0}/{1}: {2} → {3}",
            [LangEs] = "Parche {0}/{1}: {2} → {3}",
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
        ["DlgGameRunningTitle"] = new()
        {
            [LangEn] = "Game is running",
            [LangEs] = "El juego está en ejecución",
        },
        ["DlgGameRunningBody"] = new()
        {
            [LangEn] = "Age of Empires III is currently running.\n\n" +
                       "• Yes — Close the game and continue\n" +
                       "• No — Continue without closing (not recommended)\n" +
                       "• Cancel — Go back",
            [LangEs] = "Age of Empires III está actualmente en ejecución.\n\n" +
                       "• Sí — Cerrar el juego y continuar\n" +
                       "• No — Continuar sin cerrar (no recomendado)\n" +
                       "• Cancelar — Volver",
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
            [LangEn] = "Wars of Liberty is not installed. Choose a folder and click INSTALL MOD.",
            [LangEs] = "Wars of Liberty no está instalado. Elige una carpeta y haz click en INSTALAR MOD.",
        },

        // -------- Integrated install panel --------
        ["InstallPanelTitle"] = new()
        {
            [LangEn] = "Install Wars of Liberty",
            [LangEs] = "Instalar Wars of Liberty",
        },
        ["InstallAoe3Detected"] = new()
        {
            [LangEn] = "✓ Age of Empires III detected ({0})",
            [LangEs] = "✓ Age of Empires III detectado ({0})",
        },
        ["InstallAoe3NotDetected"] = new()
        {
            [LangEn] = "⚠ Age of Empires III was not detected automatically.\n" +
                       "Use the button below to select your AoE3 installation folder, " +
                       "or the mod will be installed without copying AoE3 files.",
            [LangEs] = "⚠ No se detectó Age of Empires III automáticamente.\n" +
                       "Usa el botón de abajo para seleccionar la carpeta de AoE3, " +
                       "o el mod se instalará sin copiar los archivos de AoE3.",
        },
        ["InstallFolderLabel"] = new()
        {
            [LangEn] = "Destination folder:",
            [LangEs] = "Carpeta destino:",
        },
        ["InstallDiskSpace"] = new()
        {
            [LangEn] = "Available disk space: {0} on {1}",
            [LangEs] = "Espacio en disco disponible: {0} en {1}",
        },
        ["StatusDownloadingInstaller"] = new()
        {
            [LangEn] = "📥 Downloading Wars of Liberty installer (~2.7 GB)...",
            [LangEs] = "📥 Descargando instalador de Wars of Liberty (~2.7 GB)...",
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
            [LangEn] = "Wars of Liberty will be installed in its own \"Wars of Liberty\" folder " +
                       "(separate from the original Age of Empires III install). The launcher will copy " +
                       "AoE3 there as a base and apply the mod on top. About 12 GB of free space recommended.",
            [LangEs] = "Wars of Liberty se instalará en su propia carpeta \"Wars of Liberty\" " +
                       "(separada de la instalación original de Age of Empires III). El launcher copiará " +
                       "AoE3 ahí como base y aplicará el mod encima. Se recomiendan unos 12 GB libres.",
        },
        ["DlgAoe3DetectedTitle"] = new()
        {
            [LangEn] = "AGE OF EMPIRES III DETECTED",
            [LangEs] = "AGE OF EMPIRES III DETECTADO",
        },
        ["DlgAoe3DetectedTitleWithSource"] = new()
        {
            [LangEn] = "AGE OF EMPIRES III DETECTED ({0})",
            [LangEs] = "AGE OF EMPIRES III DETECTADO ({0})",
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
        ["WarnInstallFolderSameAsAoe3"] = new()
        {
            [LangEn] = "The selected folder is the AoE3 source folder itself. " +
                       "Please choose a different destination — the mod must be installed in a separate folder.",
            [LangEs] = "La carpeta seleccionada es la misma carpeta de AoE3. " +
                       "Por favor elige un destino distinto — el mod debe instalarse en una carpeta aparte.",
        },
        ["StatusDetectingAoe3"] = new()
        {
            [LangEn] = "Detecting Age of Empires III installation...",
            [LangEs] = "Detectando instalación de Age of Empires III...",
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
            [LangEn] = "About to install in a new directory",
            [LangEs] = "Instalación en un nuevo directorio",
        },
        ["DlgConfirmCopyBody"] = new()
        {
            [LangEn] = "Wars of Liberty will be installed in a new directory.\n\n" +
                       "The launcher will now copy your Age of Empires III installation " +
                       "into the \"Wars of Liberty\" folder, and then apply the mod on top.\n\n" +
                       "This keeps your original AoE3 install untouched, but takes more time " +
                       "and additional disk space.\n\n" +
                       "  • AoE3 source: {0}\n" +
                       "  • Copy size: ~{1}\n" +
                       "  • Wars of Liberty folder: {2}\n" +
                       "  • Free space available: {3}\n\n" +
                       "Do you wish to proceed?",
            [LangEs] = "Wars of Liberty se instalará en un nuevo directorio.\n\n" +
                       "El launcher copiará tu instalación de Age of Empires III dentro de " +
                       "la carpeta \"Wars of Liberty\" y luego aplicará el mod sobre esa copia.\n\n" +
                       "Esto deja tu instalación original de AoE3 intacta, pero requiere más " +
                       "tiempo y espacio en disco.\n\n" +
                       "  • Fuente AoE3: {0}\n" +
                       "  • Tamaño de la copia: ~{1}\n" +
                       "  • Carpeta Wars of Liberty: {2}\n" +
                       "  • Espacio libre disponible: {3}\n\n" +
                       "¿Quieres continuar?",
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
        ["StatusInstallIncomplete"] = new()
        {
            [LangEn] = "Installation finished but {0} item(s) may be missing. Check the log for details.",
            [LangEs] = "La instalación finalizó pero {0} elemento(s) podrían faltar. Revisa el log para más detalles.",
        },

        // -------- Verification --------
        ["StatusVerifying"] = new()
        {
            [LangEn] = "Verifying installation...",
            [LangEs] = "Verificando instalación...",
        },
        ["StatusVerifyOk"] = new()
        {
            [LangEn] = "✓ Installation verified — {0} items checked, all OK.",
            [LangEs] = "✓ Instalación verificada — {0} elementos revisados, todo bien.",
        },
        ["StatusVerifyMissing"] = new()
        {
            [LangEn] = "⚠ {0} problem(s) found: {1}",
            [LangEs] = "⚠ {0} problema(s) encontrado(s): {1}",
        },
        ["StatusInstallSuccessVerified"] = new()
        {
            [LangEn] = "✓ Wars of Liberty installed and verified ({0} items checked).",
            [LangEs] = "✓ Wars of Liberty instalado y verificado ({0} elementos revisados).",
        },
        ["DlgVerifyRepairTitle"] = new()
        {
            [LangEn] = "Repair installation?",
            [LangEs] = "¿Reparar instalación?",
        },
        ["DlgVerifyRepairBody"] = new()
        {
            [LangEn] = "Found {0} problem(s) in the installation.\n\n" +
                       "Would you like to repair it? This will re-download the mod files " +
                       "and overwrite any damaged or missing files.\n\n" +
                       "Your AoE3 game files will NOT be affected.",
            [LangEs] = "Se encontraron {0} problema(s) en la instalación.\n\n" +
                       "¿Deseas repararla? Esto volverá a descargar los archivos del mod " +
                       "y sobrescribirá los archivos dañados o faltantes.\n\n" +
                       "Los archivos del juego AoE3 NO se verán afectados.",
        },
        ["StatusRepairing"] = new()
        {
            [LangEn] = "Repairing installation...",
            [LangEs] = "Reparando instalación...",
        },
        ["StatusRepairSuccess"] = new()
        {
            [LangEn] = "✓ Repair complete — all files verified successfully.",
            [LangEs] = "✓ Reparación completa — todos los archivos verificados correctamente.",
        },
        ["StatusRepairPartial"] = new()
        {
            [LangEn] = "⚠ Repair finished but {0} problem(s) remain. Some AoE3 base files may need manual reinstall.",
            [LangEs] = "⚠ Reparación terminada pero {0} problema(s) persisten. Algunos archivos base de AoE3 pueden necesitar reinstalación manual.",
        },

        // -------- Game state --------
        ["StatusPlaying"] = new()
        {
            [LangEn] = "🎮 Game is running — Wars of Liberty is active.",
            [LangEs] = "🎮 El juego está en ejecución — Wars of Liberty está activo.",
        },
        ["StatusGameClosed"] = new()
        {
            [LangEn] = "Game closed.",
            [LangEs] = "Juego cerrado.",
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
                       "  2. Ask where to install\n" +
                       "  3. Install Wars of Liberty silently\n\n" +
                       "Windows will ask for administrator permission. Continue?",
            [LangEs] = "El launcher hará lo siguiente:\n" +
                       "  1. Descargar el instalador oficial (~2.7 GB)\n" +
                       "  2. Preguntar dónde instalar\n" +
                       "  3. Instalar Wars of Liberty en silencio\n\n" +
                       "Windows pedirá permiso de administrador. ¿Continuar?",
        },
        ["DlgInstallNativeConfirmBody"] = new()
        {
            [LangEn] = "The mod will be installed to:\n  {0}\n\n" +
                       "AoE3 source: {1}\n\n" +
                       "This will copy Age of Empires III and Wars of Liberty files " +
                       "to the destination folder. Continue?",
            [LangEs] = "El mod se instalará en:\n  {0}\n\n" +
                       "Origen de AoE3: {1}\n\n" +
                       "Se copiarán los archivos de Age of Empires III y Wars of Liberty " +
                       "a la carpeta destino. ¿Continuar?",
        },
        ["DlgNoAoe3DetectedTitle"] = new()
        {
            [LangEn] = "AoE3 not found",
            [LangEs] = "AoE3 no encontrado",
        },
        ["DlgNoAoe3DetectedBody"] = new()
        {
            [LangEn] = "Age of Empires III was not detected on this computer.\n\n" +
                       "The mod will be installed WITHOUT copying AoE3 files. " +
                       "You will need to have AoE3: The Asian Dynasties already " +
                       "in the destination folder for the mod to work.\n\nContinue anyway?",
            [LangEs] = "No se detectó Age of Empires III en este computador.\n\n" +
                       "El mod se instalará SIN copiar archivos de AoE3. " +
                       "Necesitarás tener AoE3: The Asian Dynasties ya instalado " +
                       "en la carpeta destino para que el mod funcione.\n\n¿Continuar de todas formas?",
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
        ["DlgLauncherUpdateVersionInfo"] = new()
        {
            [LangEn] = "A new version of the launcher is available — " +
                       "current: {0}, new: {1} ({2}).",
            [LangEs] = "Hay una nueva versión del launcher disponible — " +
                       "actual: {0}, nueva: {1} ({2}).",
        },
        ["DlgLauncherUpdateConfirmPrompt"] = new()
        {
            [LangEn] = "Click DOWNLOAD to fetch the new version. " +
                       "You'll be asked to restart the launcher when it finishes.",
            [LangEs] = "Haz click en DESCARGAR para obtener la nueva versión. " +
                       "Al terminar te pedirá reiniciar el launcher.",
        },
        ["DlgLauncherUpdateReadyToDownload"] = new()
        {
            [LangEn] = "Ready to download",
            [LangEs] = "Listo para descargar",
        },
        ["DlgLauncherUpdateDownloading"] = new()
        {
            [LangEn] = "Downloading...",
            [LangEs] = "Descargando...",
        },
        ["DlgLauncherUpdateDownloadComplete"] = new()
        {
            [LangEn] = "Download complete",
            [LangEs] = "Descarga completa",
        },
        ["DlgLauncherUpdateRestartPrompt"] = new()
        {
            [LangEn] = "The new version was downloaded. The launcher needs to restart to apply it.\n" +
                       "Click RESTART NOW to apply the update, or LATER to keep using this version " +
                       "(the update will apply next time you open the launcher).",
            [LangEs] = "Se descargó la nueva versión. El launcher necesita reiniciarse para aplicarla.\n" +
                       "Haz click en REINICIAR AHORA para aplicarla, o MÁS TARDE para seguir usando esta " +
                       "versión (la actualización se aplicará la próxima vez que abras el launcher).",
        },
        ["DlgLauncherUpdateBtnDownload"] = new()
        {
            [LangEn] = "DOWNLOAD",
            [LangEs] = "DESCARGAR",
        },
        ["DlgLauncherUpdateBtnRestart"] = new()
        {
            [LangEn] = "RESTART NOW",
            [LangEs] = "REINICIAR AHORA",
        },
        ["DlgLauncherUpdateBtnRestartLater"] = new()
        {
            [LangEn] = "LATER",
            [LangEs] = "MÁS TARDE",
        },
        ["BtnClose"] = new()
        {
            [LangEn] = "CLOSE",
            [LangEs] = "CERRAR",
        },
        ["StatusDownloadingLauncherUpdate"] = new()
        {
            [LangEn] = "Downloading launcher update...",
            [LangEs] = "Descargando actualización del launcher...",
        },

        // -------- User-data alert (Documents\Wars of Liberty) --------
        ["DlgUserDataAlertTitle"] = new()
        {
            [LangEn] = "Previous user data detected",
            [LangEs] = "Datos de versión anterior detectados",
        },
        ["DlgUserDataAlertHeader"] = new()
        {
            [LangEn] = "We found Wars of Liberty user data on your computer",
            [LangEs] = "Encontramos datos de Wars of Liberty en tu equipo",
        },
        ["DlgUserDataAlertDescription"] = new()
        {
            [LangEn] = "We're about to install the 1.0.15d base version. If you " +
                       "previously played a newer version, your saves and " +
                       "metropolises may not be compatible — the game can hang " +
                       "on the loading screen until you patch back to the " +
                       "latest version.",
            [LangEs] = "Vamos a instalar la versión base 1.0.15d. Si jugaste " +
                       "antes una versión más nueva, tus partidas guardadas y " +
                       "metrópolis pueden no ser compatibles — el juego puede " +
                       "quedarse en la pantalla de carga hasta que actualices " +
                       "a la versión más reciente.",
        },
        ["DlgUserDataAlertFoundLabel"] = new()
        {
            [LangEn] = "FOUND AT",
            [LangEs] = "UBICACIÓN",
        },
        ["DlgUserDataAlertSavegameCount"] = new()
        {
            [LangEn] = "⚠ Found {0} file(s) in Savegame\\ — these may include " +
                       "metropolises in a newer format the older binary can't read.",
            [LangEs] = "⚠ Hay {0} archivo(s) en Savegame\\ — pueden incluir " +
                       "metrópolis en formato nuevo que el binario viejo no puede leer.",
        },
        ["DlgUserDataAlertRecommendation"] = new()
        {
            [LangEn] = "Recommended: back up the folder before the install runs. " +
                       "The launcher will rename it to \"Wars of Liberty.bak." +
                       "<timestamp>\" so the freshly installed game starts with a " +
                       "clean slate. Your old data stays on disk and you can " +
                       "restore it later from the gear menu (⚙ → User data → " +
                       "Restore backup).",
            [LangEs] = "Recomendado: respaldar la carpeta antes de instalar. El " +
                       "launcher la renombrará a \"Wars of Liberty.bak.<fecha>\" " +
                       "para que el juego recién instalado arranque limpio. Tus " +
                       "datos antiguos siguen en el disco y puedes restaurarlos " +
                       "después desde el menú de tuerca (⚙ → Datos de usuario → " +
                       "Restaurar respaldo).",
        },
        ["DlgUserDataAlertBtnBackup"] = new()
        {
            [LangEn] = "Back up and continue",
            [LangEs] = "Respaldar y continuar",
        },
        ["DlgUserDataAlertBtnOpen"] = new()
        {
            [LangEn] = "Open folder",
            [LangEs] = "Abrir carpeta",
        },
        ["DlgUserDataAlertBtnIgnore"] = new()
        {
            [LangEn] = "Ignore",
            [LangEs] = "Ignorar",
        },
        ["DlgUserDataAlertBackupFailedTitle"] = new()
        {
            [LangEn] = "Backup failed",
            [LangEs] = "Respaldo fallido",
        },
        ["DlgUserDataAlertBackupFailedBody"] = new()
        {
            [LangEn] = "Could not rename the user data folder. Make sure no other " +
                       "program (Explorer, the game, etc.) has it open and try again.",
            [LangEs] = "No se pudo renombrar la carpeta de datos. Verifica que " +
                       "ningún programa (Explorador, el juego, etc.) la tenga " +
                       "abierta e intenta de nuevo.",
        },
        ["StatusUserDataBackedUp"] = new()
        {
            [LangEn] = "User data backed up to: {0}",
            [LangEs] = "Datos respaldados en: {0}",
        },

        // -------- User data submenu --------
        ["MenuUserData"] = new()
        {
            [LangEn] = "User data",
            [LangEs] = "Datos de usuario",
        },
        ["MenuOpenUserDataFolder"] = new()
        {
            [LangEn] = "Open data folder",
            [LangEs] = "Abrir carpeta de datos",
        },
        ["MenuCreateBackupNow"] = new()
        {
            [LangEn] = "Create backup now",
            [LangEs] = "Crear respaldo ahora",
        },
        ["MenuRestoreUserData"] = new()
        {
            [LangEn] = "Restore backup...",
            [LangEs] = "Restaurar respaldo...",
        },

        // -------- Manual backup confirmation (gear menu → Create backup now) --------
        ["DlgBackupConfirmTitle"] = new()
        {
            [LangEn] = "Create backup?",
            [LangEs] = "¿Crear respaldo?",
        },
        ["DlgBackupConfirmBody"] = new()
        {
            [LangEn] = "Move the current Wars of Liberty user data to a backup " +
                       "folder named with today's timestamp?\n\n" +
                       "The game will create a fresh empty data folder the next " +
                       "time it runs.",
            [LangEs] = "¿Mover los datos actuales de Wars of Liberty a una " +
                       "carpeta de respaldo con la fecha de hoy?\n\n" +
                       "El juego creará una carpeta nueva vacía la próxima vez " +
                       "que se ejecute.",
        },
        ["DlgBackupNothingTitle"] = new()
        {
            [LangEn] = "Nothing to back up",
            [LangEs] = "Nada que respaldar",
        },
        ["DlgBackupNothingBody"] = new()
        {
            [LangEn] = "There is no Wars of Liberty user data to back up.",
            [LangEs] = "No hay datos de Wars of Liberty para respaldar.",
        },

        // -------- Restore dialog (styled list of backups) --------
        ["DlgRestoreDialogTitle"] = new()
        {
            [LangEn] = "Restore user data backup",
            [LangEs] = "Restaurar respaldo de datos",
        },
        ["DlgRestoreDialogHeader"] = new()
        {
            [LangEn] = "Pick a backup to restore",
            [LangEs] = "Elige un respaldo para restaurar",
        },
        ["DlgRestoreDialogDescriptionSingle"] = new()
        {
            [LangEn] = "Restoring will rename this backup back into place. Your " +
                       "current data will be saved as a new backup first, so you " +
                       "can swap back any time.",
            [LangEs] = "Al restaurar, este respaldo vuelve a ser la carpeta " +
                       "activa. Tus datos actuales se guardarán como un respaldo " +
                       "nuevo primero, así puedes volver cuando quieras.",
        },
        ["DlgRestoreDialogDescriptionMultiple"] = new()
        {
            [LangEn] = "We found {0} backups. Pick one to restore — your current " +
                       "data will be saved as a new backup first.",
            [LangEs] = "Encontramos {0} respaldos. Elige uno para restaurar — " +
                       "tus datos actuales se guardarán como un respaldo nuevo " +
                       "primero.",
        },
        ["DlgRestoreDialogListLabel"] = new()
        {
            [LangEn] = "AVAILABLE BACKUPS",
            [LangEs] = "RESPALDOS DISPONIBLES",
        },
        ["DlgRestoreDialogReassurance"] = new()
        {
            [LangEn] = "ⓘ Nothing is deleted. Your current data and any unselected " +
                       "backups stay on disk — you can manage them later via Explorer.",
            [LangEs] = "ⓘ No se elimina nada. Tus datos actuales y los respaldos " +
                       "no seleccionados quedan en disco — puedes gestionarlos " +
                       "después desde el Explorador.",
        },
        ["DlgRestoreDialogBtnRestore"] = new()
        {
            [LangEn] = "Restore selected",
            [LangEs] = "Restaurar seleccionado",
        },
        ["DlgRestoreDialogRowDetail"] = new()
        {
            [LangEn] = "{0} files",
            [LangEs] = "{0} archivos",
        },
        ["DlgRestoreDialogRowDetailWithSaves"] = new()
        {
            [LangEn] = "{0} files  ·  {1} savegames in Savegame\\",
            [LangEs] = "{0} archivos  ·  {1} partidas en Savegame\\",
        },

        ["DlgRestoreNoBackupsTitle"] = new()
        {
            [LangEn] = "No backups found",
            [LangEs] = "Sin respaldos",
        },
        ["DlgRestoreNoBackupsBody"] = new()
        {
            [LangEn] = "There are no user data backups to restore. Backups are " +
                       "created when you choose 'Back up and continue' in the " +
                       "previous-data alert after a fresh install.",
            [LangEs] = "No hay respaldos de datos para restaurar. Los respaldos " +
                       "se crean cuando eliges 'Respaldar y continuar' en la " +
                       "alerta de datos previos después de una instalación nueva.",
        },
        ["DlgRestoreFailedTitle"] = new()
        {
            [LangEn] = "Restore failed",
            [LangEs] = "Restauración fallida",
        },
        ["DlgRestoreFailedBody"] = new()
        {
            [LangEn] = "Could not restore the backup:\n\n{0}\n\n" +
                       "Make sure no program (Explorer, the game, etc.) has " +
                       "the folder open, and try again.",
            [LangEs] = "No se pudo restaurar el respaldo:\n\n{0}\n\n" +
                       "Verifica que ningún programa (Explorador, el juego, " +
                       "etc.) tenga la carpeta abierta e intenta de nuevo.",
        },
        ["StatusRestoreSuccess"] = new()
        {
            [LangEn] = "Restored backup '{0}'.",
            [LangEs] = "Respaldo '{0}' restaurado.",
        },
        ["StatusRestoreSuccessWithSnapshot"] = new()
        {
            [LangEn] = "Restored '{0}'. Your previous data was saved as '{1}'.",
            [LangEs] = "Restaurado '{0}'. Tus datos previos fueron guardados como '{1}'.",
        },

        // -------- Settings menu --------
        ["TooltipSettings"] = new()
        {
            [LangEn] = "Settings",
            [LangEs] = "Configuración",
        },
        ["MenuFolders"] = new()
        {
            [LangEn] = "Folders",
            [LangEs] = "Carpetas",
        },
        ["MenuOpenModFolder"] = new()
        {
            [LangEn] = "Open Wars of Liberty folder",
            [LangEs] = "Abrir carpeta de Wars of Liberty",
        },
        ["MenuOpenAoE3Folder"] = new()
        {
            [LangEn] = "Open Age of Empires III folder",
            [LangEs] = "Abrir carpeta de Age of Empires III",
        },
        ["MenuSelectModFolder"] = new()
        {
            [LangEn] = "Select Wars of Liberty folder...",
            [LangEs] = "Seleccionar carpeta de Wars of Liberty...",
        },
        ["MenuSelectAoE3Folder"] = new()
        {
            [LangEn] = "Select Age of Empires III folder...",
            [LangEs] = "Seleccionar carpeta de Age of Empires III...",
        },
        ["MenuVerifyFiles"] = new()
        {
            [LangEn] = "Verify files",
            [LangEs] = "Verificar archivos",
        },
        ["DlgOpenFolderNotFoundTitle"] = new()
        {
            [LangEn] = "Folder not found",
            [LangEs] = "Carpeta no encontrada",
        },
        ["DlgOpenFolderNotFoundBody"] = new()
        {
            [LangEn] = "The Wars of Liberty install folder is not detected. " +
                       "Use 'Select Wars of Liberty folder' to point the launcher at it.",
            [LangEs] = "No se detectó la carpeta de Wars of Liberty. " +
                       "Usa 'Seleccionar carpeta de Wars of Liberty' para indicar dónde está.",
        },
        ["DlgOpenAoE3NotFoundBody"] = new()
        {
            [LangEn] = "Age of Empires III is not detected. " +
                       "Use 'Select Age of Empires III folder' to point the launcher at it.",
            [LangEs] = "No se detectó Age of Empires III. " +
                       "Usa 'Seleccionar carpeta de Age of Empires III' para indicar dónde está.",
        },

        // -------- AoE3 folder browse --------
        ["BrowseAoE3Button"] = new()
        {
            [LangEn] = "Select AoE3 folder...",
            [LangEs] = "Seleccionar carpeta de AoE3...",
        },
        ["LblGamePath"] = new()
        {
            [LangEn] = "AGE OF EMPIRES III",
            [LangEs] = "AGE OF EMPIRES III",
        },
        ["DlgAoE3FolderPickerTitle"] = new()
        {
            [LangEn] = "Select Age of Empires III folder",
            [LangEs] = "Seleccionar carpeta de Age of Empires III",
        },
        ["DlgInvalidAoE3FolderTitle"] = new()
        {
            [LangEn] = "Invalid folder",
            [LangEs] = "Carpeta no válida",
        },
        ["DlgInvalidAoE3FolderBody"] = new()
        {
            [LangEn] = "Could not find 'age3y.exe' in the selected folder.\n\n" +
                       "Please select the Age of Empires III installation folder " +
                       "(the one that contains age3y.exe or has a 'bin' subfolder with it).",
            [LangEs] = "No se encontró 'age3y.exe' en la carpeta seleccionada.\n\n" +
                       "Selecciona la carpeta de instalación de Age of Empires III " +
                       "(la que contiene age3y.exe o tiene una subcarpeta 'bin' con él).",
        },
        ["StatusAoE3NotDetected"] = new()
        {
            [LangEn] = "Age of Empires III not detected. Click the button to select its folder.",
            [LangEs] = "Age of Empires III no detectado. Haz clic en el botón para seleccionar su carpeta.",
        },
        ["StatusAoE3Configured"] = new()
        {
            [LangEn] = "Age of Empires III configured successfully.",
            [LangEs] = "Age of Empires III configurado correctamente.",
        },
    };
}
