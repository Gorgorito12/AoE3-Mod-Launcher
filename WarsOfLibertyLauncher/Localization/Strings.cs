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
        // {0} is the active mod profile's display name (e.g. "Wars of Liberty",
        // "Improvement Mod"). Both fields used to be hard-coded to WoL.
        ["WindowTitle"] = new()
        {
            [LangEn] = "{0} Launcher",
            [LangEs] = "{0} Launcher",
        },
        // -------- Mod selector popup --------
        ["ModSelectorInstalled"] = new()
        {
            [LangEn] = "Installed · v{0}",
            [LangEs] = "Instalado · v{0}",
        },
        ["ModSelectorInstalledNoVersion"] = new()
        {
            [LangEn] = "Installed",
            [LangEs] = "Instalado",
        },
        ["ModSelectorNotInstalled"] = new()
        {
            [LangEn] = "Not installed",
            [LangEs] = "No instalado",
        },
        ["ModTileTooltipActive"] = new()
        {
            [LangEn] = "Currently active",
            [LangEs] = "Activo actualmente",
        },
        // -------- Mod-switch blocking dialogs --------
        ["DlgModSwitchBlockedTitle"] = new()
        {
            [LangEn] = "Can't switch mod right now",
            [LangEs] = "No se puede cambiar de mod ahora",
        },
        ["DlgModSwitchBusyBody"] = new()
        {
            [LangEn] = "An operation is in progress. Wait for it to finish " +
                       "(or cancel it from the toolbar) before switching mods.",
            [LangEs] = "Hay una operación en curso. Esperá a que termine " +
                       "(o cancelala desde la barra) antes de cambiar de mod.",
        },
        ["DlgModSwitchGameRunningBody"] = new()
        {
            [LangEn] = "The game is currently running. Close it before " +
                       "switching to another mod.",
            [LangEs] = "El juego está abierto. Cerralo antes de cambiar a otro mod.",
        },
        // -------- New layout: top mods bar, sidebar labels, tabs --------
        ["ModsBarLabel"] = new()
        {
            [LangEn] = "MODS",
            [LangEs] = "MODS",
        },
        ["ActionsLabel"] = new()
        {
            [LangEn] = "MOD ACTIONS",
            [LangEs] = "ACCIONES DEL MOD",
        },
        ["TabNoticias"] = new()
        {
            [LangEn] = "News",
            [LangEs] = "Noticias",
        },
        ["TabChangelog"] = new()
        {
            [LangEn] = "Changelog",
            [LangEs] = "Changelog",
        },
        ["TabAyuda"] = new()
        {
            [LangEn] = "Help",
            [LangEs] = "Ayuda",
        },
        // Help tab body (per-mod help text). Profile-specific overrides
        // come from ModProfile.HelpText when populated.
        ["HelpDefaultBody"] = new()
        {
            [LangEn] = "No additional help is available for this mod yet. " +
                       "If you run into trouble, open the gear menu in the " +
                       "sidebar — it has tools to verify files, swap folders, " +
                       "back up your user data, and uninstall.",
            [LangEs] = "Todavía no hay ayuda adicional para este mod. Si " +
                       "tenés algún problema, abrí el menú de configuración " +
                       "en la barra lateral — tiene herramientas para " +
                       "verificar archivos, cambiar carpetas, hacer backup " +
                       "de tus datos y desinstalar.",
        },
        ["ChangelogPlaceholder"] = new()
        {
            [LangEn] = "No changelog available for this mod yet.",
            [LangEs] = "Todavía no hay changelog para este mod.",
        },
        // -------- Inline progress panel (top of news content) --------
        ["ProgressPanelHeader"] = new()
        {
            [LangEn] = "INSTALL / UPDATE PROGRESS",
            [LangEs] = "PROGRESO DE INSTALACIÓN / ACTUALIZACIÓN",
        },
        // Per-operation header — shown in the small label at the top of
        // the panel. Used to disambiguate which action is running.
        ["ProgressLabelInstall"] = new()
        {
            [LangEn] = "INSTALL PROGRESS",
            [LangEs] = "PROGRESO DE INSTALACIÓN",
        },
        ["ProgressLabelUpdate"] = new()
        {
            [LangEn] = "UPDATE PROGRESS",
            [LangEs] = "PROGRESO DE ACTUALIZACIÓN",
        },
        ["ProgressLabelRepair"] = new()
        {
            [LangEn] = "REPAIR PROGRESS",
            [LangEs] = "PROGRESO DE REPARACIÓN",
        },
        ["ProgressLabelVerify"] = new()
        {
            [LangEn] = "FILE VERIFICATION",
            [LangEs] = "VERIFICACIÓN DE ARCHIVOS",
        },
        ["ProgressLabelUninstall"] = new()
        {
            [LangEn] = "UNINSTALL PROGRESS",
            [LangEs] = "PROGRESO DE DESINSTALACIÓN",
        },
        // Bar labels (one per operation flavor — Repair calls them
        // Verify / Repair, Uninstall calls them Process / Cleanup, etc.)
        ["ProgressBarDownload"] = new()
        {
            [LangEn] = "Download",
            [LangEs] = "Descarga",
        },
        ["ProgressBarInstall"] = new()
        {
            [LangEn] = "Installation",
            [LangEs] = "Instalación",
        },
        ["ProgressBarVerify"] = new()
        {
            [LangEn] = "Verification",
            [LangEs] = "Verificación",
        },
        ["ProgressBarRepair"] = new()
        {
            [LangEn] = "Repair",
            [LangEs] = "Reparación",
        },
        ["ProgressBarProcess"] = new()
        {
            [LangEn] = "Process",
            [LangEs] = "Proceso",
        },
        ["ProgressBarCleanup"] = new()
        {
            [LangEn] = "Cleanup",
            [LangEs] = "Limpieza",
        },
        // -------- Idle state of the bottom-left panel --------
        // The panel that used to host "Game detected" now mirrors the mod's
        // current status when no operation is running: Ready / Update
        // available / Not installed / AoE3 missing. Header label sits
        // above the icon + title row.
        ["IdleHeader"] = new()
        {
            [LangEn] = "MOD STATUS",
            [LangEs] = "ESTADO DEL MOD",
        },
        // -------- New StatusCard rows (top of sidebar) --------
        ["StatusCardCurrentVersion"] = new()
        {
            [LangEn] = "Current version:",
            [LangEs] = "Versión actual:",
        },
        ["StatusCardLatestVersion"] = new()
        {
            [LangEn] = "Latest version:",
            [LangEs] = "Última versión:",
        },
        ["StatusCardInstalled"] = new()
        {
            [LangEn] = "Installed",
            [LangEs] = "Instalado",
        },
        ["StatusCardNotInstalled"] = new()
        {
            [LangEn] = "Not installed",
            [LangEs] = "No instalado",
        },
        // -------- ProgressPanel idle (bottom of sidebar) --------
        // Neutral "ready for any operation" look. Color flips to whatever
        // operation is running once StartProgressPanel takes over.
        ["ProgressIdleHeader"] = new()
        {
            [LangEn] = "PROGRESS PANEL",
            [LangEs] = "PANEL DE PROGRESO",
        },
        ["ProgressIdleTitle"] = new()
        {
            [LangEn] = "Ready for operations",
            [LangEs] = "Listo para operaciones",
        },
        ["IdleStateReady"] = new()
        {
            [LangEn] = "Ready to play",
            [LangEs] = "Listo para jugar",
        },
        ["IdleStateUpdateAvailable"] = new()
        {
            [LangEn] = "Update available",
            [LangEs] = "Actualización disponible",
        },
        // {0} = mod display name (e.g. "Wars of Liberty").
        ["IdleStateNotInstalled"] = new()
        {
            [LangEn] = "{0} is not installed",
            [LangEs] = "{0} no está instalado",
        },
        ["IdleStateUnknownVersion"] = new()
        {
            [LangEn] = "Version not recognised",
            [LangEs] = "Versión no reconocida",
        },
        ["IdleStateGameMissing"] = new()
        {
            [LangEn] = "Age of Empires III not found",
            [LangEs] = "Age of Empires III no encontrado",
        },
        // {0} = mod display name, {1} = installed version.
        ["IdleSubtitleReady"] = new()
        {
            [LangEn] = "{0} v{1}",
            [LangEs] = "{0} v{1}",
        },
        ["IdleSubtitleNotInstalled"] = new()
        {
            [LangEn] = "Click Install to get started.",
            [LangEs] = "Apretá Instalar para empezar.",
        },
        // {0} = current version, {1} = latest version.
        ["IdleSubtitleUpdateAvailable"] = new()
        {
            [LangEn] = "v{0} → v{1}",
            [LangEs] = "v{0} → v{1}",
        },
        ["IdleSubtitleGameMissing"] = new()
        {
            [LangEn] = "We need the game's location to play.",
            [LangEs] = "Necesitamos la ubicación del juego para poder jugar.",
        },
        // Inline button shown in the panel's idle state when AoE3 isn't
        // detected — replaces the old "..." button in the game footer.
        ["BtnFindAoE3"] = new()
        {
            [LangEn] = "Find AoE3",
            [LangEs] = "Buscar AoE3",
        },
        // Title shown in the panel header during uninstall.
        ["ProgressTitleUninstalling"] = new()
        {
            [LangEn] = "Uninstalling {0}",
            [LangEs] = "Desinstalando {0}",
        },
        // Subtitle for the uninstall flow.
        ["ProgressSubRemoving"] = new()
        {
            [LangEn] = "Removing mod files...",
            [LangEs] = "Eliminando archivos del mod...",
        },
        // Title row at the top of the panel — "Installing/Updating {0}".
        ["ProgressTitleInstalling"] = new()
        {
            [LangEn] = "Installing {0}",
            [LangEs] = "Instalando {0}",
        },
        ["ProgressTitleUpdating"] = new()
        {
            [LangEn] = "Updating {0}",
            [LangEs] = "Actualizando {0}",
        },
        ["ProgressTitleRepairing"] = new()
        {
            [LangEn] = "Repairing {0}",
            [LangEs] = "Reparando {0}",
        },
        ["ProgressTitleVerifying"] = new()
        {
            [LangEn] = "Verifying {0}",
            [LangEs] = "Verificando {0}",
        },
        ["ProgressTitleCompleted"] = new()
        {
            [LangEn] = "Completed",
            [LangEs] = "Completado",
        },
        ["ProgressTitleError"] = new()
        {
            [LangEn] = "Operation failed",
            [LangEs] = "La operación falló",
        },
        ["ProgressTitleCancelled"] = new()
        {
            [LangEn] = "Cancelled",
            [LangEs] = "Cancelado",
        },
        // End-state banners inside the panel.
        ["ProgressCompletedMessage"] = new()
        {
            [LangEn] = "All done. The mod is ready to play.",
            [LangEs] = "Listo. El mod ya está disponible para jugar.",
        },
        ["ProgressCancelledMessage"] = new()
        {
            [LangEn] = "The operation was cancelled. You can resume by retrying.",
            [LangEs] = "La operación fue cancelada. Podés reintentar para retomarla.",
        },
        // Sub-step phrases used as the panel's subtitle.
        ["ProgressSubDownloading"] = new()
        {
            [LangEn] = "Downloading...",
            [LangEs] = "Descargando...",
        },
        ["ProgressSubVerifying"] = new()
        {
            [LangEn] = "Verifying files...",
            [LangEs] = "Verificando archivos...",
        },
        // Step counter in the top-right of the panel — "Step {0} of {1}".
        ["ProgressStepFormat"] = new()
        {
            [LangEn] = "Step {0} of {1}",
            [LangEs] = "Paso {0} de {1}",
        },
        // Action button labels inside the panel.
        ["BtnRetry"] = new()
        {
            [LangEn] = "Retry",
            [LangEs] = "Reintentar",
        },
        ["BtnClose"] = new()
        {
            [LangEn] = "Close",
            [LangEs] = "Cerrar",
        },
        // Shown in the INSTALLED VERSION card for mods that don't have
        // Status line for mods whose updates are managed outside the
        // launcher (their own patcher, ModDB, etc.). {0} = mod name.
        ["StatusReadyExternalUpdates"] = new()
        {
            [LangEn] = "Ready to play. Updates for {0} are managed externally.",
            [LangEs] = "Listo para jugar. Las actualizaciones de {0} se manejan por fuera del launcher.",
        },
        ["StatusModNotInstalledExternal"] = new()
        {
            [LangEn] = "{0} isn't installed yet. Install it from its own site, then come back here to play.",
            [LangEs] = "{0} todavía no está instalado. Instalalo desde su sitio y volvé acá para jugar.",
        },
        ["Subtitle"] = new()
        {
            [LangEn] = "Launcher",
            [LangEs] = "Launcher",
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
        // {0} = mod display name (e.g. "Wars of Liberty", "Improvement Mod")
        ["DlgUninstallTitle"] = new()
        {
            [LangEn] = "Uninstall {0}",
            [LangEs] = "Desinstalar {0}",
        },
        ["DlgUninstallHeader"] = new()
        {
            [LangEn] = "Uninstall {0}",
            [LangEs] = "Desinstalar {0}",
        },
        // {0} = mod display name
        ["DlgUninstallDescription"] = new()
        {
            [LangEn] = "This will delete the entire {0} install folder. Your Age of Empires III base game lives in a separate folder and will not be touched.",
            [LangEs] = "Esto eliminará la carpeta completa de {0}. Tu instalación de Age of Empires III está en otra carpeta y no será modificada.",
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
        // {0} = mod display name (uppercased in the UI styling, not in the string)
        ["DlgUninstallNotValidTitle"] = new()
        {
            [LangEn] = "✗ NOT A VALID {0} INSTALL",
            [LangEs] = "✗ NO ES UNA INSTALACIÓN VÁLIDA DE {0}",
        },
        // {0} = folder path the user pointed at, {1} = probe file the mod
        // expects there (e.g. "age3m.exe", "data\\stringtabley.xml"), {2} =
        // mod display name.
        ["DlgUninstallNotValidDetail"] = new()
        {
            [LangEn] = "The folder '{0}' does not contain the {2} marker ({1}). For safety, the launcher refuses to delete it.\n\nIf this is a real {2} install with broken files, run Verify first to repair it.",
            [LangEs] = "La carpeta '{0}' no contiene el marcador de {2} ({1}). Por seguridad, el launcher se niega a eliminarla.\n\nSi es una instalación real de {2} con archivos rotos, ejecuta Verificar primero para repararla.",
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

        // {0} = mod display name
        ["StatusUninstalling"] = new()
        {
            [LangEn] = "Uninstalling {0}...",
            [LangEs] = "Desinstalando {0}...",
        },
        // {0} = mod display name, {1} = file count removed.
        ["StatusUninstallSuccess"] = new()
        {
            [LangEn] = "{0} was uninstalled successfully ({1} files removed).",
            [LangEs] = "{0} se desinstaló correctamente ({1} archivos eliminados).",
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
            [LangEn] = "Update",
            [LangEs] = "Actualizar",
        },
        ["BtnVerify"] = new()
        {
            [LangEn] = "Verify files",
            [LangEs] = "Verificar archivos",
        },
        ["BtnConfig"] = new()
        {
            [LangEn] = "Settings",
            [LangEs] = "Ajustes",
        },
        // Section headers inside the Settings menu — small-caps gray
        // labels grouping the items below each one. Not clickable.
        ["MenuSectionPaths"] = new()
        {
            [LangEn] = "PATHS",
            [LangEs] = "RUTAS",
        },
        ["MenuSectionUserData"] = new()
        {
            [LangEn] = "USER DATA",
            [LangEs] = "DATOS DE USUARIO",
        },
        ["MenuSectionLanguage"] = new()
        {
            [LangEn] = "LANGUAGE",
            [LangEs] = "IDIOMA",
        },
        ["MenuSectionMaintenance"] = new()
        {
            [LangEn] = "MAINTENANCE",
            [LangEs] = "MANTENIMIENTO",
        },
        ["MenuSectionAdvanced"] = new()
        {
            [LangEn] = "ADVANCED",
            [LangEs] = "AVANZADO",
        },
        ["MenuSectionDanger"] = new()
        {
            [LangEn] = "DANGER",
            [LangEs] = "PELIGRO",
        },

        // -------- Launcher Settings dialog (Tier 1) --------
        ["BtnLauncherSettings"] = new()
        {
            [LangEn] = "Launcher",
            [LangEs] = "Launcher",
        },
        ["DlgLauncherSettingsTitle"] = new()
        {
            [LangEn] = "Launcher Settings",
            [LangEs] = "Ajustes del launcher",
        },
        ["DlgLauncherSettingsSectionGeneral"] = new()
        {
            [LangEn] = "GENERAL",
            [LangEs] = "GENERAL",
        },
        ["DlgLauncherSettingsSectionUpdates"] = new()
        {
            [LangEn] = "UPDATES",
            [LangEs] = "ACTUALIZACIONES",
        },
        ["DlgLauncherSettingsSectionCatalog"] = new()
        {
            [LangEn] = "MODS CATALOG",
            [LangEs] = "CATÁLOGO DE MODS",
        },
        ["DlgLauncherSettingsLanguageLabel"] = new()
        {
            [LangEn] = "Launcher language",
            [LangEs] = "Idioma del launcher",
        },
        ["DlgLauncherSettingsThemeLabel"] = new()
        {
            [LangEn] = "Theme",
            [LangEs] = "Tema",
        },
        ["DlgLauncherSettingsThemeDark"] = new()
        {
            [LangEn] = "Dark",
            [LangEs] = "Oscuro",
        },
        ["DlgLauncherSettingsThemeLight"] = new()
        {
            [LangEn] = "Light",
            [LangEs] = "Claro",
        },
        ["DlgLauncherSettingsThemeSystem"] = new()
        {
            [LangEn] = "Follow Windows",
            [LangEs] = "Seguir Windows",
        },
        ["NewsReadMore"] = new()
        {
            [LangEn] = "Read more →",
            [LangEs] = "Leer más →",
        },
        ["TopTabPlay"] = new()
        {
            [LangEn] = "PLAY",
            [LangEs] = "JUGAR",
        },
        ["TopTabMods"] = new()
        {
            [LangEn] = "MODS",
            [LangEs] = "MODS",
        },
        ["TopTabMultiplayer"] = new()
        {
            [LangEn] = "MULTIPLAYER",
            [LangEs] = "MULTIJUGADOR",
        },
        ["TopTabNews"] = new()
        {
            [LangEn] = "NEWS",
            [LangEs] = "NOTICIAS",
        },
        ["TopTabSettings"] = new()
        {
            [LangEn] = "SETTINGS",
            [LangEs] = "AJUSTES",
        },
        // ====================================================================
        // Catalog redesign (post-v0.9): two-column layout strings.
        // ====================================================================
        ["ModsBrowserHeaderTitle"] = new()
        {
            [LangEn] = "Mods",
            [LangEs] = "Mods",
        },
        ["ModsBrowserHeaderSubtitle"] = new()
        {
            [LangEn] = "Explore, install and manage mods for Age of Empires III.",
            [LangEs] = "Explora, instala y administra mods para Age of Empires III.",
        },
        ["ModsBrowserEmpty"] = new()
        {
            [LangEn] = "No mods match your filters.",
            [LangEs] = "Ningún mod coincide con tus filtros.",
        },
        ["ModsBrowserDetailEmpty"] = new()
        {
            [LangEn] = "Select a mod from the list to see its details.",
            [LangEs] = "Selecciona un mod de la lista para ver sus detalles.",
        },
        ["ModsBrowserSearchPlaceholder"] = new()
        {
            [LangEn] = "Search mods…",
            [LangEs] = "Buscar mods...",
        },
        ["ModsBrowserListSummary"] = new()
        {
            [LangEn] = "Available mods ({0})",
            [LangEs] = "Mods disponibles ({0})",
        },
        ["ModsBrowserRefreshCatalog"] = new()
        {
            [LangEn] = "↻ Refresh catalog",
            [LangEs] = "↻ Actualizar catálogo",
        },
        ["ModsBrowserAddLocal"] = new()
        {
            [LangEn] = "+ Add local mod",
            [LangEs] = "+ Agregar mod local",
        },
        ["ModsBrowserSubTabMyMods"] = new()
        {
            [LangEn] = "My mods",
            [LangEs] = "Mis mods",
        },
        ["ModsBrowserSubTabCatalog"] = new()
        {
            [LangEn] = "Catalog",
            [LangEs] = "Catálogo",
        },
        ["ModsBrowserFiltersLabel"] = new()
        {
            [LangEn] = "Filters:",
            [LangEs] = "Filtros:",
        },
        ["ModsBrowserFilterAll"] = new()
        {
            [LangEn] = "All",
            [LangEs] = "Todos",
        },
        ["ModsBrowserFilterInstalled"] = new()
        {
            [LangEn] = "Installed",
            [LangEs] = "Instalados",
        },
        ["ModsBrowserFilterNotInstalled"] = new()
        {
            [LangEn] = "Not installed",
            [LangEs] = "No instalados",
        },
        ["ModsBrowserFilterUpdates"] = new()
        {
            [LangEn] = "Updates",
            [LangEs] = "Actualizaciones",
        },
        ["ModsBrowserFilterCompatible"] = new()
        {
            [LangEn] = "Compatible",
            [LangEs] = "Compatibles",
        },
        ["ModsBrowserSortLabel"] = new()
        {
            [LangEn] = "Sort by:",
            [LangEs] = "Ordenar por:",
        },
        ["ModsBrowserSortRecent"] = new()
        {
            [LangEn] = "Most recent",
            [LangEs] = "Más recientes",
        },
        ["ModsBrowserSortName"] = new()
        {
            [LangEn] = "Name",
            [LangEs] = "Nombre",
        },
        ["ModsBrowserSortStatus"] = new()
        {
            [LangEn] = "Status",
            [LangEs] = "Estado",
        },
        ["ModsBrowserBadgeNotInstalled"] = new()
        {
            [LangEn] = "Not installed",
            [LangEs] = "No instalado",
        },
        ["ModsBrowserBadgeInstalled"] = new()
        {
            [LangEn] = "Installed",
            [LangEs] = "Instalado",
        },
        ["ModsBrowserBadgeUpdate"] = new()
        {
            [LangEn] = "Update available",
            [LangEs] = "Actualización disponible",
        },
        ["ModsBrowserBadgeIncompatible"] = new()
        {
            [LangEn] = "Incompatible",
            [LangEs] = "Incompatible",
        },
        ["ModsBrowserBadgeError"] = new()
        {
            [LangEn] = "Error",
            [LangEs] = "Error",
        },
        ["ModsBrowserDetailDeveloper"] = new()
        {
            [LangEn] = "Developer",
            [LangEs] = "Desarrollador",
        },
        ["ModsBrowserDetailVersion"] = new()
        {
            [LangEn] = "Version",
            [LangEs] = "Versión",
        },
        ["ModsBrowserDetailAvailable"] = new()
        {
            [LangEn] = "Available",
            [LangEs] = "Disponible",
        },
        ["ModsBrowserDetailInstallType"] = new()
        {
            [LangEn] = "Install type",
            [LangEs] = "Tipo de instalación",
        },
        ["ModsBrowserDetailUpdates"] = new()
        {
            [LangEn] = "Updates",
            [LangEs] = "Actualizaciones",
        },
        ["ModsBrowserDetailWebsite"] = new()
        {
            [LangEn] = "Website",
            [LangEs] = "Sitio web",
        },
        ["ModsBrowserDetailLanguages"] = new()
        {
            [LangEn] = "Languages",
            [LangEs] = "Idiomas",
        },
        ["ModsBrowserActionInstall"] = new()
        {
            [LangEn] = "Install mod",
            [LangEs] = "Instalar mod",
        },
        ["ModsBrowserActionUpdate"] = new()
        {
            [LangEn] = "Update",
            [LangEs] = "Actualizar",
        },
        ["ModsBrowserActionPlay"] = new()
        {
            [LangEn] = "Play",
            [LangEs] = "Jugar",
        },
        ["ModsBrowserActionRepair"] = new()
        {
            [LangEn] = "Repair",
            [LangEs] = "Reparar",
        },
        ["ModsBrowserActionIncompatible"] = new()
        {
            [LangEn] = "Incompatible",
            [LangEs] = "Incompatible",
        },
        ["ModsBrowserActionViewWebsite"] = new()
        {
            [LangEn] = "View mod page",
            [LangEs] = "Ver página del mod",
        },
        ["ModsBrowserActionSwitchActive"] = new()
        {
            [LangEn] = "Set as active mod",
            [LangEs] = "Establecer como mod activo",
        },
        ["ModsBrowserActionUninstall"] = new()
        {
            [LangEn] = "Uninstall",
            [LangEs] = "Desinstalar",
        },
        ["ModsBrowserMenuPublish"] = new()
        {
            [LangEn] = "Publish my mod",
            [LangEs] = "Publicar mi mod",
        },
        ["ModsBrowserAddLocalSoonTitle"] = new()
        {
            [LangEn] = "Add local mod",
            [LangEs] = "Agregar mod local",
        },
        ["ModsBrowserAddLocalSoonBody"] = new()
        {
            [LangEn] = "Registering a manually-installed mod folder lands in a future update.",
            [LangEs] = "Registrar una carpeta de mod instalada manualmente llegará en una próxima actualización.",
        },
        ["PublishWizardTitle"] = new()
        {
            [LangEn] = "Publish my mod",
            [LangEs] = "Publicar mi mod",
        },
        ["PublishWizardCancel"] = new()
        {
            [LangEn] = "Cancel",
            [LangEs] = "Cancelar",
        },
        ["PublishWizardBack"] = new()
        {
            [LangEn] = "Back",
            [LangEs] = "Atrás",
        },
        ["PublishWizardNext"] = new()
        {
            [LangEn] = "Next",
            [LangEs] = "Siguiente",
        },
        ["PublishWizardFinish"] = new()
        {
            [LangEn] = "Finish",
            [LangEs] = "Finalizar",
        },
        ["PublishWizardStepFormat"] = new()
        {
            [LangEn] = "Step {0} of {1}",
            [LangEs] = "Paso {0} de {1}",
        },
        ["PublishWizardStep1Title"] = new()
        {
            [LangEn] = "Identity",
            [LangEs] = "Identidad",
        },
        ["PublishWizardStep1Hint"] = new()
        {
            [LangEn] = "Pick a stable id and a display name. These two fields anchor the catalog entry.",
            [LangEs] = "Elige un id estable y un nombre visible. Estos dos campos anclan la entrada del catálogo.",
        },
        ["PublishWizardStep2Title"] = new()
        {
            [LangEn] = "Look & feel",
            [LangEs] = "Apariencia",
        },
        ["PublishWizardStep2Hint"] = new()
        {
            [LangEn] = "Accent colour, icon and banner. Optional but recommended.",
            [LangEs] = "Color de acento, icono y banner. Opcional pero recomendado.",
        },
        ["PublishWizardStep3Title"] = new()
        {
            [LangEn] = "Install",
            [LangEs] = "Instalación",
        },
        ["PublishWizardStep3Hint"] = new()
        {
            [LangEn] = "How the mod's files live on disk and which executable launches it.",
            [LangEs] = "Cómo se almacenan los archivos del mod y qué ejecutable lo lanza.",
        },
        ["PublishWizardStep4Title"] = new()
        {
            [LangEn] = "Updates",
            [LangEs] = "Actualizaciones",
        },
        ["PublishWizardStep4Hint"] = new()
        {
            [LangEn] = "How the launcher pulls new versions: WoL patcher, GitHub Releases, external updater, or manual.",
            [LangEs] = "Cómo el launcher obtiene nuevas versiones: parcheador WoL, GitHub Releases, actualizador externo o manual.",
        },
        ["PublishWizardStep5Title"] = new()
        {
            [LangEn] = "Description & website",
            [LangEs] = "Descripción y sitio web",
        },
        ["PublishWizardStep5Hint"] = new()
        {
            [LangEn] = "Per-language description and the mod's homepage URL.",
            [LangEs] = "Descripción por idioma y la URL del sitio del mod.",
        },
        ["PublishWizardStep6Title"] = new()
        {
            [LangEn] = "Review & publish",
            [LangEs] = "Revisar y publicar",
        },
        ["PublishWizardStep6Hint"] = new()
        {
            [LangEn] = "Inspect the generated mod.json, copy it to the clipboard, and open the catalog PR template on GitHub.",
            [LangEs] = "Revisa el mod.json generado, cópialo al portapapeles y abre la plantilla de PR del catálogo en GitHub.",
        },
        ["PublishFieldId"] = new() { [LangEn] = "Id", [LangEs] = "Id" },
        ["PublishFieldIdHint"] = new()
        {
            [LangEn] = "Lowercase letters, digits, dashes. Used as the folder name under /mods/.",
            [LangEs] = "Minúsculas, dígitos y guiones. Se usa como nombre de carpeta dentro de /mods/.",
        },
        ["PublishFieldDisplayName"] = new() { [LangEn] = "Display name", [LangEs] = "Nombre visible" },
        ["PublishFieldAuthor"] = new() { [LangEn] = "Author (optional)", [LangEs] = "Autor (opcional)" },
        ["PublishFieldSubtitle"] = new() { [LangEn] = "Subtitle (optional)", [LangEs] = "Subtítulo (opcional)" },
        ["PublishFieldAccent"] = new() { [LangEn] = "Accent colour (optional)", [LangEs] = "Color de acento (opcional)" },
        ["PublishFieldAccentHint"] = new()
        {
            [LangEn] = "Hex format, e.g. #c8102e.",
            [LangEs] = "Formato hex, ej. #c8102e.",
        },
        ["PublishFieldIcon"] = new() { [LangEn] = "Icon filename (optional)", [LangEs] = "Nombre del icono (opcional)" },
        ["PublishFieldIconHint"] = new()
        {
            [LangEn] = "icon.png — 256x256, PNG with alpha, ≤100 KB.",
            [LangEs] = "icon.png — 256x256, PNG con alfa, ≤100 KB.",
        },
        ["PublishFieldBanner"] = new() { [LangEn] = "Banner filename (optional)", [LangEs] = "Nombre del banner (opcional)" },
        ["PublishFieldBannerHint"] = new()
        {
            [LangEn] = "banner.png/.jpg — 1200x300, ≤500 KB.",
            [LangEs] = "banner.png/.jpg — 1200x300, ≤500 KB.",
        },
        ["PublishFieldInstallType"] = new() { [LangEn] = "Install type", [LangEs] = "Tipo de instalación" },
        ["PublishFieldInstallTypeHint"] = new()
        {
            [LangEn] = "IsolatedFolder = own folder. InPlaceOverlay = on top of AoE3.",
            [LangEs] = "IsolatedFolder = carpeta propia. InPlaceOverlay = encima de AoE3.",
        },
        ["PublishFieldDefaultFolder"] = new() { [LangEn] = "Default install folder", [LangEs] = "Carpeta de instalación por defecto" },
        ["PublishFieldProbeFile"] = new() { [LangEn] = "Probe file", [LangEs] = "Archivo de detección" },
        ["PublishFieldExecutable"] = new() { [LangEn] = "Executable", [LangEs] = "Ejecutable" },
        ["PublishFieldArguments"] = new() { [LangEn] = "Arguments (optional)", [LangEs] = "Argumentos (opcional)" },
        ["PublishFieldMechanism"] = new() { [LangEn] = "Update mechanism", [LangEs] = "Mecanismo de actualización" },
        ["PublishFieldWolUpdateInfoUrl"] = new() { [LangEn] = "UpdateInfo.xml URL", [LangEs] = "URL de UpdateInfo.xml" },
        ["PublishFieldSourceRepo"] = new() { [LangEn] = "Source repo (owner/repo)", [LangEs] = "Repo fuente (owner/repo)" },
        ["PublishFieldSourceRepoHint"] = new()
        {
            [LangEn] = "Your mod's GitHub repository, e.g. yourname/your-mod.",
            [LangEs] = "El repositorio de GitHub de tu mod, ej. tunombre/tu-mod.",
        },
        ["PublishFieldApprovedTag"] = new() { [LangEn] = "Approved release tag", [LangEs] = "Tag de release aprobado" },
        ["PublishFieldDescriptionEn"] = new() { [LangEn] = "Description (English)", [LangEs] = "Descripción (Inglés)" },
        ["PublishFieldDescriptionEs"] = new() { [LangEn] = "Description (Spanish)", [LangEs] = "Descripción (Español)" },
        ["PublishFieldWebsite"] = new() { [LangEn] = "Official website (optional)", [LangEs] = "Sitio web oficial (opcional)" },
        ["PublishCopyJson"] = new() { [LangEn] = "Copy JSON", [LangEs] = "Copiar JSON" },
        ["PublishOpenPr"] = new() { [LangEn] = "Open PR on GitHub", [LangEs] = "Abrir PR en GitHub" },
        ["PublishErrorId"] = new()
        {
            [LangEn] = "Invalid id. Use lowercase letters, digits and dashes (max 31 chars, starts with a letter).",
            [LangEs] = "Id inválido. Usa minúsculas, dígitos y guiones (máx 31 chars, empieza por letra).",
        },
        ["PublishErrorDisplayName"] = new()
        {
            [LangEn] = "Display name is required (1–50 characters).",
            [LangEs] = "El nombre visible es obligatorio (1–50 caracteres).",
        },
        ["PublishErrorAccent"] = new()
        {
            [LangEn] = "Accent colour must be a six-digit hex string like #c8102e.",
            [LangEs] = "El color de acento debe ser un hex de seis dígitos, ej. #c8102e.",
        },
        ["PublishErrorIcon"] = new()
        {
            [LangEn] = "Icon filename must end with .png and contain only letters, digits, dashes or underscores.",
            [LangEs] = "El nombre del icono debe acabar en .png y contener solo letras, dígitos, guiones o guiones bajos.",
        },
        ["PublishErrorBanner"] = new()
        {
            [LangEn] = "Banner filename must end with .png/.jpg/.jpeg and contain only safe characters.",
            [LangEs] = "El nombre del banner debe acabar en .png/.jpg/.jpeg y contener solo caracteres seguros.",
        },
        ["PublishErrorExecutable"] = new()
        {
            [LangEn] = "Executable must be a filename ending in .exe (e.g. age3y.exe).",
            [LangEs] = "El ejecutable debe ser un archivo terminado en .exe (ej. age3y.exe).",
        },
        ["PublishErrorWebsite"] = new()
        {
            [LangEn] = "Website must start with http:// or https://.",
            [LangEs] = "El sitio web debe empezar con http:// o https://.",
        },
        ["MultiplayerComingSoon"] = new()
        {
            [LangEn] = "Multiplayer lobby — coming in v1.0.\nA Voobly-style room browser for AoE3 mods.",
            [LangEs] = "Sala multijugador — llega en v1.0.\nUn buscador de partidas estilo Voobly para mods de AoE3.",
        },
        // -------- Multiplayer (v1.0) --------
        ["MpSubtabRooms"] = new() { [LangEn] = "Rooms", [LangEs] = "Salas" },
        ["MpSubtabFriends"] = new() { [LangEn] = "Friends", [LangEs] = "Amigos" },
        ["MpSubtabProfile"] = new() { [LangEn] = "Profile", [LangEs] = "Perfil" },
        ["MpSubtabHistory"] = new() { [LangEn] = "History", [LangEs] = "Historial" },
        ["MpSignInTitle"] = new()
        {
            [LangEn] = "Sign in to play online",
            [LangEs] = "Inicia sesión para jugar online",
        },
        ["MpSignInBody"] = new()
        {
            [LangEn] = "Multiplayer uses Discord to sign you in — no new account needed. Your username and avatar are read; nothing else is requested.",
            [LangEs] = "El multijugador usa Discord para iniciar sesión: no necesitas crear una cuenta nueva. Solo se leen tu usuario y avatar; nada más.",
        },
        ["MpSignInButton"] = new() { [LangEn] = "Sign in with Discord", [LangEs] = "Iniciar sesión con Discord" },
        ["MpSignOutButton"] = new() { [LangEn] = "Sign out", [LangEs] = "Cerrar sesión" },
        ["MpSignInDialogTitle"] = new() { [LangEn] = "Discord sign-in", [LangEs] = "Inicio de sesión Discord" },
        ["MpSignInStep1"] = new()
        {
            [LangEn] = "Open this URL in your browser and click Authorize:",
            [LangEs] = "Abre esta URL en tu navegador y haz clic en Autorizar:",
        },
        ["MpSignInStep2"] = new()
        {
            // Only shown for legacy GitHub-style flows where the user has to
            // type a code into the browser. Discord skips this step entirely;
            // the dialog hides this text when the server returns an empty
            // user_code.
            [LangEn] = "Type or paste this code into the browser, then approve:",
            [LangEs] = "Escribe o pega este código en el navegador y aprueba:",
        },
        ["MpSignInWaiting"] = new()
        {
            [LangEn] = "Waiting for you to authorize in the browser…",
            [LangEs] = "Esperando tu autorización en el navegador…",
        },
        ["MpSignInOpenBrowser"] = new() { [LangEn] = "Open browser", [LangEs] = "Abrir navegador" },
        ["MpSignInCopy"] = new() { [LangEn] = "Copy code", [LangEs] = "Copiar código" },
        ["MpSignInCancel"] = new() { [LangEn] = "Cancel", [LangEs] = "Cancelar" },
        ["DlgClose"] = new() { [LangEn] = "Close", [LangEs] = "Cerrar" },
        ["MpRoomsCreate"] = new() { [LangEn] = "Create room", [LangEs] = "Crear sala" },
        ["MpRoomsRefresh"] = new() { [LangEn] = "Refresh", [LangEs] = "Actualizar" },
        ["MpRoomsEmpty"] = new()
        {
            [LangEn] = "No rooms right now. Be the first to create one.",
            [LangEs] = "No hay salas ahora mismo. Sé el primero en crear una.",
        },
        ["MpRoomsLoading"] = new() { [LangEn] = "Loading rooms…", [LangEs] = "Cargando salas…" },
        ["MpRoomJoin"] = new() { [LangEn] = "Join", [LangEs] = "Unirse" },
        ["MpRoomLeave"] = new() { [LangEn] = "Leave room", [LangEs] = "Salir de la sala" },
        ["MpRoomReady"] = new() { [LangEn] = "Ready", [LangEs] = "Listo" },
        ["MpRoomStart"] = new() { [LangEn] = "Start game", [LangEs] = "Empezar partida" },
        ["MpRoomChatPlaceholder"] = new()
        {
            [LangEn] = "Type a message…",
            [LangEs] = "Escribe un mensaje…",
        },
        ["MpRoomMembersHeader"] = new() { [LangEn] = "Players", [LangEs] = "Jugadores" },
        ["MpCreateDialogTitle"] = new() { [LangEn] = "Create a room", [LangEs] = "Crear una sala" },
        ["MpCreateDialogTitleLabel"] = new() { [LangEn] = "Room title", [LangEs] = "Título de la sala" },
        ["MpCreateDialogMaxPlayers"] = new() { [LangEn] = "Max players", [LangEs] = "Jugadores máx." },
        ["MpCreateDialogPassword"] = new()
        {
            [LangEn] = "Password (optional)",
            [LangEs] = "Contraseña (opcional)",
        },
        ["MpCreateDialogModLabel"] = new()
        {
            [LangEn] = "Mod",
            [LangEs] = "Mod",
        },
        ["MpCreateDialogHashLabel"] = new()
        {
            [LangEn] = "Mod fingerprint",
            [LangEs] = "Huella del mod",
        },
        ["MpCreateDialogCreate"] = new() { [LangEn] = "Create", [LangEs] = "Crear" },
        ["MpCreateDialogCancel"] = new() { [LangEn] = "Cancel", [LangEs] = "Cancelar" },
        ["MpModNotInstalled"] = new()
        {
            [LangEn] = "Install the mod first to host or join a room for it.",
            [LangEs] = "Instala primero el mod para crear o unirte a una sala suya.",
        },
        ["MpZtNotInstalledTitle"] = new()
        {
            [LangEn] = "ZeroTier is required for multiplayer",
            [LangEs] = "ZeroTier es necesario para el multijugador",
        },
        ["MpZtNotInstalledBody"] = new()
        {
            [LangEn] = "ZeroTier creates the virtual LAN that lets AoE3 see other players. Click below to install it — Windows will ask for permission.",
            [LangEs] = "ZeroTier crea la LAN virtual que permite a AoE3 ver a otros jugadores. Pulsa abajo para instalarlo — Windows te pedirá permiso.",
        },
        ["MpZtInstall"] = new() { [LangEn] = "Install ZeroTier", [LangEs] = "Instalar ZeroTier" },
        ["MpZtStarting"] = new() { [LangEn] = "Starting ZeroTier service…", [LangEs] = "Arrancando el servicio ZeroTier…" },
        ["MpZtRunning"] = new() { [LangEn] = "ZeroTier ready", [LangEs] = "ZeroTier listo" },
        ["MpZtAuthorizeBody"] = new()
        {
            [LangEn] = "We need to read ZeroTier's local API token. Click below — Windows will ask once.",
            [LangEs] = "Necesitamos leer el token local de ZeroTier. Pulsa abajo — Windows lo pedirá una sola vez.",
        },
        ["MpZtAuthorize"] = new() { [LangEn] = "Authorize", [LangEs] = "Autorizar" },
        ["MpQuotaBar"] = new()
        {
            [LangEn] = "{0}/{1} players online · {2}/{3} active rooms",
            [LangEs] = "{0}/{1} jugadores online · {2}/{3} salas activas",
        },
        // -------- NAT badge (Multiplayer header) --------
        // {0} is the human-readable NAT type label.
        ["MpNatBadge"] = new()
        {
            [LangEn] = "NAT: {0}",
            [LangEs] = "NAT: {0}",
        },
        ["MpNatProbing"] = new()
        {
            [LangEn] = "checking…",
            [LangEs] = "comprobando…",
        },
        ["MpNatUnknown"] = new()
        {
            [LangEn] = "unknown",
            [LangEs] = "desconocido",
        },
        ["MpNatOpen"] = new()
        {
            [LangEn] = "Open — hole-punching works trivially.",
            [LangEs] = "Abierta — la conexión directa funciona sin problema.",
        },
        ["MpNatModerate"] = new()
        {
            [LangEn] = "Moderate — direct connection with other players works.",
            [LangEs] = "Moderada — la conexión directa con otros jugadores funciona.",
        },
        ["MpNatStrict"] = new()
        {
            [LangEn] = "Strict — direct connection needs coordinated punching.",
            [LangEs] = "Estricta — la conexión directa necesita coordinación extra.",
        },
        ["MpNatSymmetric"] = new()
        {
            [LangEn] = "Symmetric — direct connection won't work; relay required.",
            [LangEs] = "Simétrica — sin conexión directa; hace falta usar relay.",
        },
        ["SettingsTabTeaser"] = new()
        {
            [LangEn] = "Launcher preferences (language, theme, autostart, mods catalog, …)",
            [LangEs] = "Preferencias del launcher (idioma, tema, autoarranque, catálogo de mods, …)",
        },
        ["SettingsTabOpen"] = new()
        {
            [LangEn] = "Open settings",
            [LangEs] = "Abrir ajustes",
        },
        ["DlgLauncherSettingsStartWithWindows"] = new()
        {
            [LangEn] = "Start with Windows",
            [LangEs] = "Iniciar con Windows",
        },
        ["DlgLauncherSettingsStartWithWindowsHint"] = new()
        {
            [LangEn] = "Launches automatically when you log in.",
            [LangEs] = "Se inicia automáticamente al iniciar sesión.",
        },
        ["DlgLauncherSettingsCloseOnGame"] = new()
        {
            [LangEn] = "Close launcher when game starts",
            [LangEs] = "Cerrar el launcher al iniciar el juego",
        },
        ["DlgLauncherSettingsCloseOnGameHint"] = new()
        {
            [LangEn] = "Frees resources while you play. Reopen the launcher manually after the game closes.",
            [LangEs] = "Libera recursos mientras juegas. Vuelve a abrir el launcher manualmente cuando termines.",
        },
        ["DlgLauncherSettingsMinimizeToTray"] = new()
        {
            [LangEn] = "Minimize to system tray on close",
            [LangEs] = "Minimizar a la bandeja al cerrar",
        },
        ["DlgLauncherSettingsMinimizeToTrayHint"] = new()
        {
            [LangEn] = "Closing the window keeps the launcher running in the tray. Right-click the tray icon → Exit to fully quit.",
            [LangEs] = "Cerrar la ventana mantiene el launcher en la bandeja. Click derecho en el icono → Salir para cerrarlo del todo.",
        },
        ["DlgLauncherSettingsShowToasts"] = new()
        {
            [LangEn] = "Show notifications when updates finish",
            [LangEs] = "Mostrar notificaciones cuando terminan las actualizaciones",
        },
        ["DlgLauncherSettingsShowToastsHint"] = new()
        {
            [LangEn] = "A system tray balloon appears when an update completes while the launcher window is hidden or minimised.",
            [LangEs] = "Aparece una notificación en la bandeja cuando termina una actualización y la ventana del launcher está oculta o minimizada.",
        },
        ["ToastUpdateCompleteTitle"] = new()
        {
            [LangEn] = "Update complete",
            [LangEs] = "Actualización completada",
        },
        ["ToastUpdateCompleteBody"] = new()
        {
            [LangEn] = "{0} is now up to date.",
            [LangEs] = "{0} está actualizado.",
        },
        ["ToastInstallCompleteTitle"] = new()
        {
            [LangEn] = "Install complete",
            [LangEs] = "Instalación completada",
        },
        ["ToastInstallCompleteBody"] = new()
        {
            [LangEn] = "{0} is ready to play.",
            [LangEs] = "{0} está listo para jugar.",
        },
        ["TrayTooltip"] = new()
        {
            [LangEn] = "AoE3 Mod Launcher",
            [LangEs] = "AoE3 Mod Launcher",
        },
        ["TrayMenuShow"] = new()
        {
            [LangEn] = "Show launcher",
            [LangEs] = "Mostrar launcher",
        },
        ["TrayMenuExit"] = new()
        {
            [LangEn] = "Exit",
            [LangEs] = "Salir",
        },
        ["DlgLauncherSettingsAutoCheck"] = new()
        {
            [LangEn] = "Check for updates on startup",
            [LangEs] = "Buscar actualizaciones al iniciar",
        },
        ["DlgLauncherSettingsAutoCheckHint"] = new()
        {
            [LangEn] = "Runs in the background; the launcher only surfaces a notice when something is pending.",
            [LangEs] = "Se ejecuta en segundo plano; el launcher solo te avisa cuando hay algo pendiente.",
        },
        ["DlgLauncherSettingsOpenPostUpdate"] = new()
        {
            [LangEn] = "Open post-update pages in browser",
            [LangEs] = "Abrir páginas post-actualización en el navegador",
        },
        ["DlgLauncherSettingsOpenPostUpdateHint"] = new()
        {
            [LangEn] = "Some mods link to a changelog page after applying a patch.",
            [LangEs] = "Algunos mods enlazan a una página de cambios después de aplicar un parche.",
        },
        ["DlgLauncherSettingsCatalogDefault"] = new()
        {
            [LangEn] = "Default catalog",
            [LangEs] = "Catálogo por defecto",
        },
        ["DlgLauncherSettingsCatalogCustom"] = new()
        {
            [LangEn] = "Custom repository:",
            [LangEs] = "Repositorio personalizado:",
        },
        ["DlgLauncherSettingsCatalogDisabled"] = new()
        {
            [LangEn] = "Disabled (built-in mods only)",
            [LangEs] = "Desactivado (solo mods integrados)",
        },
        ["DlgLauncherSettingsClearCache"] = new()
        {
            [LangEn] = "Clear catalog cache",
            [LangEs] = "Limpiar caché del catálogo",
        },
        ["DlgLauncherSettingsClearCacheHint"] = new()
        {
            [LangEn] = "Forces a fresh fetch on next start.",
            [LangEs] = "Fuerza una nueva descarga en el próximo arranque.",
        },
        ["DlgLauncherSettingsCacheCleared"] = new()
        {
            [LangEn] = "Cache cleared.",
            [LangEs] = "Caché eliminada.",
        },
        ["DlgLauncherSettingsSectionMaintenance"] = new()
        {
            [LangEn] = "MAINTENANCE",
            [LangEs] = "MANTENIMIENTO",
        },
        ["DlgLauncherSettingsClearAssets"] = new()
        {
            [LangEn] = "Clear mod icons cache",
            [LangEs] = "Limpiar caché de iconos de mods",
        },
        ["DlgLauncherSettingsClearAssetsHint"] = new()
        {
            [LangEn] = "Removes cached icon/banner images. They redownload on next launcher start.",
            [LangEs] = "Elimina las imágenes (iconos/banners) cacheadas. Se vuelven a descargar al reabrir el launcher.",
        },
        ["DlgLauncherSettingsClearTemp"] = new()
        {
            [LangEn] = "Clear temporary files",
            [LangEs] = "Limpiar archivos temporales",
        },
        ["DlgLauncherSettingsClearTempHint"] = new()
        {
            [LangEn] = "Removes leftover download/extract files from interrupted updates.",
            [LangEs] = "Elimina archivos sobrantes de descargas/extracciones interrumpidas.",
        },
        ["DlgLauncherSettingsAssetsCleared"] = new()
        {
            [LangEn] = "Asset cache cleared ({0} files).",
            [LangEs] = "Caché de imágenes eliminada ({0} archivos).",
        },
        ["DlgLauncherSettingsTempCleared"] = new()
        {
            [LangEn] = "Temp files cleared.",
            [LangEs] = "Archivos temporales eliminados.",
        },
        ["DlgLauncherSettingsNothingToClean"] = new()
        {
            [LangEn] = "Nothing to clean.",
            [LangEs] = "Nada que limpiar.",
        },
        ["DlgLauncherSettingsInvalidRepo"] = new()
        {
            [LangEn] = "Invalid repository format. Use owner/repo (e.g. Gorgorito12/aoe3-mods-catalog).",
            [LangEs] = "Formato inválido. Usa owner/repo (ej: Gorgorito12/aoe3-mods-catalog).",
        },
        ["BtnSave"] = new()
        {
            [LangEn] = "Save changes",
            [LangEs] = "Guardar cambios",
        },
        ["BtnCancel"] = new()
        {
            [LangEn] = "Cancel",
            [LangEs] = "Cancelar",
        },
        ["BtnOpenFolder"] = new()
        {
            [LangEn] = "Open folder",
            [LangEs] = "Abrir carpeta",
        },
        ["BtnRepair"] = new()
        {
            [LangEn] = "Repair install",
            [LangEs] = "Reparar instalación",
        },
        ["BtnUninstall"] = new()
        {
            [LangEn] = "Uninstall",
            [LangEs] = "Desinstalar",
        },
        // -------- New gear-menu items (Maintenance + Advanced) --------
        ["MenuRepairInstall"] = new()
        {
            [LangEn] = "Repair install",
            [LangEs] = "Reparar instalación",
        },
        ["MenuVerifyFiles"] = new()
        {
            [LangEn] = "Verify files",
            [LangEs] = "Verificar archivos",
        },
        ["MenuViewLogs"] = new()
        {
            [LangEn] = "View logs",
            [LangEs] = "Ver logs",
        },
        ["TooltipMenuRepairInstall"] = new()
        {
            [LangEn] = "Re-downloads the mod payload and overlays it on top " +
                       "of the existing install — replaces missing or corrupt " +
                       "files without losing user data.",
            [LangEs] = "Re-descarga el contenido del mod y lo aplica sobre " +
                       "la instalación actual — reemplaza archivos faltantes " +
                       "o corruptos sin perder los datos del usuario.",
        },
        ["TooltipMenuVerifyFiles"] = new()
        {
            [LangEn] = "Quick integrity check — flags missing or empty files " +
                       "and offers Repair if anything is wrong.",
            [LangEs] = "Verificación rápida de integridad — marca archivos " +
                       "faltantes o vacíos y ofrece Reparar si algo no está bien.",
        },
        ["TooltipMenuViewLogs"] = new()
        {
            [LangEn] = "Opens the launcher diagnostic log in your default " +
                       "text editor.",
            [LangEs] = "Abre el log de diagnóstico del launcher en tu editor " +
                       "de texto predeterminado.",
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
        // {0} = mod display name, {1} = current ver, {2} = latest ver,
        // {3} = official website URL (from the mod's catalog manifest).
        ["StatusVersionTooOld"] = new()
        {
            [LangEn] = "Your version of {0} ({1}) is too old to update via patches. " +
                       "Latest available: {2}. Please reinstall {0} from {3}.",
            [LangEs] = "Tu versión de {0} ({1}) es demasiado antigua para actualizar por parches. " +
                       "Última disponible: {2}. Necesitas reinstalar {0} desde {3}.",
        },
        ["StatusUpdatesAvailable"] = new()
        {
            [LangEn] = "{0} update(s) available ({1} total).",
            [LangEs] = "{0} actualización(es) disponible(s) ({1} total).",
        },
        // {0} = mod display name.
        ["StatusInstallNotFound"] = new()
        {
            [LangEn] = "{0} was not auto-detected. " +
                       "Use the \"Change...\" button to select the folder manually.",
            [LangEs] = "No se detectó automáticamente {0}. " +
                       "Usa el botón \"Cambiar...\" para indicar manualmente la carpeta.",
        },

        // -------- Status: in progress --------
        // {0} = active mod's display name (e.g. "Wars of Liberty",
        // "Improvement Mod"). Used to be hard-coded to WoL.
        ["StatusDetectingInstall"] = new()
        {
            [LangEn] = "Detecting {0} installation...",
            [LangEs] = "Detectando instalación de {0}...",
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
        // {0} = mod display name, {1} = probe file the launcher checks for
        // (relative to the install folder), e.g. "data\\stringtabley.xml"
        // for WoL or "age3m.exe" for Improvement Mod.
        ["DlgInvalidFolderBody"] = new()
        {
            [LangEn] = "The selected folder doesn't appear to be a valid {0} installation.\n\n" +
                       "Expected to find '{1}' inside.",
            [LangEs] = "La carpeta seleccionada no parece ser una instalación válida de {0}.\n\n" +
                       "Esperaba encontrar '{1}' adentro.",
        },
        // {0} = mod display name.
        ["DlgFolderPickerTitle"] = new()
        {
            [LangEn] = "Select {0} folder",
            [LangEs] = "Seleccionar carpeta de {0}",
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
        // {0} = mod display name (e.g. "Wars of Liberty", "Improvement Mod").
        // The 'age3y.exe' string stays literal — that's specifically the AoE3
        // base game's executable, which is the same file regardless of which
        // mod is on top.
        ["ErrGameExeNotFound"] = new()
        {
            [LangEn] = "'age3y.exe' (Age of Empires III: The Asian Dynasties) not found.\n\n" +
                       "{0} needs Age of Empires III installed to work.\n" +
                       "Use the \"Change...\" button to point to the correct folder, " +
                       "or set 'gameExecutable' manually in launcher-config.json.",
            [LangEs] = "No se encontró 'age3y.exe' (Age of Empires III: The Asian Dynasties).\n\n" +
                       "{0} necesita Age of Empires III instalado para funcionar.\n" +
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
        // {0} = mod display name. Size deliberately omitted — the progress
        // bar underneath already shows real bytes for whichever mod is
        // being downloaded.
        ["StatusDownloadingInstaller"] = new()
        {
            [LangEn] = "📥 Downloading {0} installer...",
            [LangEs] = "📥 Descargando instalador de {0}...",
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
        // {0} = mod display name (e.g. "Wars of Liberty", "Improvement Mod").
        ["DlgPickInstallFolderTitle"] = new()
        {
            [LangEn] = "Choose where to install {0}",
            [LangEs] = "Elige dónde instalar {0}",
        },
        ["DlgPickInstallFolderHeader"] = new()
        {
            [LangEn] = "Install location",
            [LangEs] = "Ubicación de instalación",
        },
        // {0} = mod display name. Appears twice — first as the subject, then
        // as the folder name (e.g. "Improvement Mod will be installed in its
        // own 'Improvement Mod' folder").
        ["DlgPickInstallFolderDescription"] = new()
        {
            [LangEn] = "{0} will be installed in its own \"{0}\" folder " +
                       "(separate from the original Age of Empires III install). The launcher will copy " +
                       "AoE3 there as a base and apply the mod on top. About 12 GB of free space recommended.",
            [LangEs] = "{0} se instalará en su propia carpeta \"{0}\" " +
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
            // {0} = mod display name.
            [LangEn] = "{0} may not be working correctly",
            [LangEs] = "{0} podría no estar funcionando correctamente",
        },
        // {0} = the detected install path on disk, {1} = mod display name.
        ["DlgBrokenInstallBody"] = new()
        {
            [LangEn] = "{1} was found at:\n\n{0}\n\n" +
                       "But this folder doesn't appear to be inside Age of Empires III. " +
                       "The mod files are on disk, but the AoE3 engine won't load them from " +
                       "this location.\n\n" +
                       "To fix this, reinstall {1} into the same folder as " +
                       "Age of Empires III (typically your Steam library).",
            [LangEs] = "Se encontró {1} en:\n\n{0}\n\n" +
                       "Pero esta carpeta no parece estar dentro de Age of Empires III. " +
                       "Los archivos del mod están en disco, pero el motor de AoE3 no los va a " +
                       "cargar desde esta ubicación.\n\n" +
                       "Para arreglarlo, reinstala {1} en la misma carpeta donde " +
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

        // -------- Download corruption retry (NativeInstall) --------
        // Shown when ZIP extraction fails because the downloaded payload is
        // corrupted (usually a flipped byte during the multi-GB download).
        // {0} = attempt just failed (1-based); {1} = total attempts allowed.
        ["DlgInstallRetryCorruptTitle"] = new()
        {
            [LangEn] = "Download appears corrupted",
            [LangEs] = "La descarga parece estar corrupta",
        },
        ["DlgInstallRetryCorruptBody"] = new()
        {
            [LangEn] = "The downloaded mod files failed integrity check (attempt {0} of {1}). " +
                       "This usually means a few bytes got dropped during the download.\n\n" +
                       "Retry the download from scratch?",
            [LangEs] = "Los archivos descargados del mod fallaron la verificación de integridad (intento {0} de {1}). " +
                       "Esto suele significar que se perdieron algunos bytes durante la descarga.\n\n" +
                       "¿Reintentar la descarga desde cero?",
        },
        ["StatusInstallRetrying"] = new()
        {
            [LangEn] = "Retrying install (attempt {0} of {1})...",
            [LangEs] = "Reintentando instalación (intento {0} de {1})...",
        },
        ["StatusInstallCorruptedGaveUp"] = new()
        {
            [LangEn] = "Download kept arriving corrupted after {0} attempts. Try again later or from a different network.",
            [LangEs] = "La descarga siguió llegando corrupta después de {0} intentos. Probá de nuevo más tarde o desde otra red.",
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

        // -------- User-data alert (Documents\<mod>\) --------
        // Title is mod-agnostic (no placeholder needed).
        ["DlgUserDataAlertTitle"] = new()
        {
            [LangEn] = "Previous user data detected",
            [LangEs] = "Datos de versión anterior detectados",
        },
        // {0} = mod display name (e.g. "Wars of Liberty", "Improvement Mod").
        ["DlgUserDataAlertHeader"] = new()
        {
            [LangEn] = "We found {0} user data on your computer",
            [LangEs] = "Encontramos datos de {0} en tu equipo",
        },
        // {0} = mod display name. The "base version" framing matches WoL's
        // WolPatcher flow, but the warning applies to any install that lays
        // down older binaries on top of newer saves — keep it generic.
        ["DlgUserDataAlertDescription"] = new()
        {
            [LangEn] = "We're about to install {0}. If you previously played a " +
                       "newer version, your saves and metropolises may not be " +
                       "compatible — the game can hang on the loading screen " +
                       "until you patch back to the latest version.",
            [LangEs] = "Vamos a instalar {0}. Si jugaste antes una versión más " +
                       "nueva, tus partidas guardadas y metrópolis pueden no " +
                       "ser compatibles — el juego puede quedarse en la pantalla " +
                       "de carga hasta que actualices a la versión más reciente.",
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
        // {0} = mod display name (used as the renamed-folder prefix:
        // "<modName>.bak.<timestamp>").
        ["DlgUserDataAlertRecommendation"] = new()
        {
            [LangEn] = "Recommended: back up the folder before the install runs. " +
                       "The launcher will rename it to \"{0}.bak.<timestamp>\" so " +
                       "the freshly installed game starts with a clean slate. " +
                       "Your old data stays on disk and you can restore it later " +
                       "from the gear menu (⚙ → User data → Restore backup).",
            [LangEs] = "Recomendado: respaldar la carpeta antes de instalar. El " +
                       "launcher la renombrará a \"{0}.bak.<fecha>\" para que el " +
                       "juego recién instalado arranque limpio. Tus datos " +
                       "antiguos siguen en el disco y puedes restaurarlos después " +
                       "desde el menú de tuerca (⚙ → Datos de usuario → Restaurar " +
                       "respaldo).",
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
            [LangEs] = "Ajustes",
        },
        ["MenuFolders"] = new()
        {
            [LangEn] = "Folders",
            [LangEs] = "Carpetas",
        },
        // Replaces "Folders" — the submenu is now for path settings, not
        // path opening (the sidebar's "Open folder" button covers the
        // common case for the active mod).
        ["MenuManagePaths"] = new()
        {
            [LangEn] = "Manage paths",
            [LangEs] = "Administrar rutas",
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
        // {0} = mod display name.
        ["MenuSelectModFolder"] = new()
        {
            [LangEn] = "Select {0} folder...",
            [LangEs] = "Seleccionar carpeta de {0}...",
        },
        ["MenuSelectAoE3Folder"] = new()
        {
            [LangEn] = "Select Age of Empires III folder...",
            [LangEs] = "Seleccionar carpeta de Age of Empires III...",
        },
        ["MenuCheckForUpdates"] = new()
        {
            [LangEn] = "Check for updates",
            [LangEs] = "Buscar actualizaciones",
        },
        ["MenuVerifyFiles"] = new()
        {
            [LangEn] = "Verify files",
            [LangEs] = "Verificar archivos",
        },
        ["MenuGameLanguage"] = new()
        {
            [LangEn] = "Game language",
            [LangEs] = "Idioma del juego",
        },
        ["MenuLangEnglish"] = new()
        {
            [LangEn] = "English (default)",
            [LangEs] = "Inglés (predeterminado)",
        },
        ["MenuLangRefresh"] = new()
        {
            [LangEn] = "Refresh list",
            [LangEs] = "Actualizar lista",
        },
        ["MenuLangRefreshing"] = new()
        {
            [LangEn] = "Refreshing...",
            [LangEs] = "Actualizando...",
        },
        ["MenuLangNoneAvailable"] = new()
        {
            [LangEn] = "No translations available yet",
            [LangEs] = "Aún no hay traducciones disponibles",
        },
        // Shown next to each translation in the menu. {0} is the comma-
        // separated list of mod versions from compatibleWith — that's what
        // users care about ("does this work with my mod version?").
        ["MenuLangModVersionLabel"] = new()
        {
            [LangEn] = "(mod {0})",
            [LangEs] = "(mod {0})",
        },
        ["MenuLangPackager"] = new()
        {
            [LangEn] = "Package my translation...",
            [LangEs] = "Empaquetar mi traducción...",
        },

        // -------- Translator packaging dialog --------
        ["DlgPackagerTitle"] = new()
        {
            [LangEn] = "Package translation",
            [LangEs] = "Empaquetar traducción",
        },
        ["DlgPackagerHeader"] = new()
        {
            [LangEn] = "Build a translation pack",
            [LangEs] = "Crear un paquete de traducción",
        },
        ["DlgPackagerDescription"] = new()
        {
            [LangEn] = "This generates a ready-to-publish .zip from a folder of " +
                       "translated XML files. The launcher computes the hashes " +
                       "and manifest automatically.",
            [LangEs] = "Genera un .zip listo para publicar a partir de una carpeta " +
                       "con archivos XML traducidos. El launcher calcula los hashes " +
                       "y el manifest automáticamente.",
        },
        ["DlgPackagerFieldId"] = new()
        {
            [LangEn] = "LANGUAGE ID",
            [LangEs] = "ID DEL IDIOMA",
        },
        ["DlgPackagerHintId"] = new()
        {
            [LangEn] = "Short identifier — e.g. \"es\", \"fr\", \"pt-br\"",
            [LangEs] = "Identificador corto — ej. \"es\", \"fr\", \"pt-br\"",
        },
        ["DlgPackagerFieldName"] = new()
        {
            [LangEn] = "DISPLAY NAME",
            [LangEs] = "NOMBRE VISIBLE",
        },
        ["DlgPackagerFieldAuthor"] = new()
        {
            [LangEn] = "AUTHOR / HANDLE",
            [LangEs] = "AUTOR / NOMBRE DE USUARIO",
        },
        ["DlgPackagerFieldVersion"] = new()
        {
            [LangEn] = "TRANSLATION VERSION  (e.g. 1.0)",
            [LangEs] = "VERSIÓN DE LA TRADUCCIÓN  (ej: 1.0)",
        },
        ["DlgPackagerHintVersion"] = new()
        {
            [LangEn] = "Version of YOUR translation pack — bump this when you " +
                       "publish changes (1.0 → 1.1 → 1.2...). NOT the mod version " +
                       "— that goes in the 'Compatibility' field below.",
            [LangEs] = "Versión de TU paquete de traducción — subila al publicar " +
                       "cambios (1.0 → 1.1 → 1.2...). NO es la versión del mod " +
                       "— eso va en el campo 'Compatibilidad' abajo.",
        },
        ["DlgPackagerVersionLooksLikeMod"] = new()
        {
            [LangEn] = "⚠ \"{0}\" looks like a mod version. The translation version is " +
                       "yours — start with 1.0 and bump it on each release.",
            [LangEs] = "⚠ \"{0}\" parece una versión del mod. La versión de la " +
                       "traducción es tuya — empezá con 1.0 y subila en cada release.",
        },
        ["DlgPackagerFieldFolder"] = new()
        {
            [LangEn] = "FOLDER WITH TRANSLATED XML FILES",
            [LangEs] = "CARPETA CON LOS XML TRADUCIDOS",
        },
        ["DlgPackagerHintFolder"] = new()
        {
            [LangEn] = "Should contain stringtabley.xml and/or unithelpstringsy.xml",
            [LangEs] = "Debe contener stringtabley.xml y/o unithelpstringsy.xml",
        },
        ["DlgPackagerFieldOriginals"] = new()
        {
            [LangEn] = "FOLDER WITH ORIGINAL ENGLISH XML FILES",
            [LangEs] = "CARPETA CON LOS XML ORIGINALES EN INGLÉS",
        },
        ["DlgPackagerHintOriginals"] = new()
        {
            [LangEn] = "Same file names as above (stringtabley.xml etc.) but the EN versions. " +
                       "Auto-filled from the launcher's snapshot when available.",
            [LangEs] = "Mismos nombres de archivo (stringtabley.xml, etc.) pero en inglés. " +
                       "Se auto-completa con el respaldo del launcher si está disponible.",
        },
        ["DlgPackagerFieldCompat"] = new()
        {
            [LangEn] = "MOD COMPATIBILITY",
            [LangEs] = "COMPATIBILIDAD CON EL MOD",
        },
        ["DlgPackagerCompatCurrent"] = new()
        {
            [LangEn] = "Compatible with current mod version ({0})",
            [LangEs] = "Compatible con la versión actual del mod ({0})",
        },
        ["DlgPackagerHintCompatExtra"] = new()
        {
            [LangEn] = "Extra versions, comma-separated (optional)",
            [LangEs] = "Otras versiones, separadas por coma (opcional)",
        },
        ["DlgPackagerFieldOutput"] = new()
        {
            [LangEn] = "OUTPUT .ZIP FILE",
            [LangEs] = "ARCHIVO .ZIP DE SALIDA",
        },
        ["DlgPackagerBtnGenerate"] = new()
        {
            [LangEn] = "Generate package",
            [LangEs] = "Generar paquete",
        },
        // Errors
        ["DlgPackagerErrorIdMissing"] = new()
        {
            [LangEn] = "Language ID is required (e.g. \"es\").",
            [LangEs] = "El ID del idioma es obligatorio (ej. \"es\").",
        },
        ["DlgPackagerErrorNameMissing"] = new()
        {
            [LangEn] = "Display name is required.",
            [LangEs] = "El nombre visible es obligatorio.",
        },
        ["DlgPackagerErrorVersionMissing"] = new()
        {
            [LangEn] = "Pack version is required.",
            [LangEs] = "La versión del paquete es obligatoria.",
        },
        ["DlgPackagerErrorFolderMissing"] = new()
        {
            [LangEn] = "The translated-files folder doesn't exist.",
            [LangEs] = "La carpeta con los archivos traducidos no existe.",
        },
        ["DlgPackagerErrorOutputMissing"] = new()
        {
            [LangEn] = "Output .zip path is required.",
            [LangEs] = "Falta la ruta del .zip de salida.",
        },
        ["DlgPackagerErrorNoCompat"] = new()
        {
            [LangEn] = "Specify at least one compatible mod version.",
            [LangEs] = "Indicá al menos una versión del mod compatible.",
        },
        ["DlgPackagerErrorNoSnapshotBody"] = new()
        {
            [LangEn] = "Cannot package: the launcher hasn't snapshotted the original " +
                       "English files yet. Run an install or update first to generate it.",
            [LangEs] = "No se puede empaquetar: el launcher aún no tiene un respaldo " +
                       "del original en inglés. Ejecutá una instalación o actualización primero.",
        },
        // Result
        ["DlgPackagerResultHeader"] = new()
        {
            [LangEn] = "Package created",
            [LangEs] = "Paquete creado",
        },
        ["DlgPackagerResultPath"] = new()
        {
            [LangEn] = "📦 {0} ({1})",
            [LangEs] = "📦 {0} ({1})",
        },
        ["DlgPackagerResultJsonPath"] = new()
        {
            [LangEn] = "📄 {0}",
            [LangEs] = "📄 {0}",
        },
        ["DlgPackagerResultInstructions"] = new()
        {
            [LangEn] = "ⓘ How to publish:\n" +
                       "1. Go to github.com/papillo12/translations and create a new release.\n" +
                       "2. Upload BOTH files above as assets on that release: the .zip " +
                       "and translation.json (keep that exact filename).\n" +
                       "3. Done — players will see the new translation in their launcher " +
                       "menu the next time it refreshes the list.",
            [LangEs] = "ⓘ Cómo publicar:\n" +
                       "1. Andá a github.com/papillo12/translations y creá una nueva release.\n" +
                       "2. Subí AMBOS archivos de arriba como assets de esa release: el .zip " +
                       "y translation.json (mantené ese nombre exacto).\n" +
                       "3. Listo — los jugadores verán la nueva traducción en su launcher " +
                       "la próxima vez que refresque la lista.",
        },
        ["DlgPackagerResultPreviewLabel"] = new()
        {
            [LangEn] = "MENU PREVIEW (how players will see it):",
            [LangEs] = "VISTA PREVIA EN EL MENÚ (así lo verán los jugadores):",
        },
        ["DlgPackagerBtnOpenFolder"] = new()
        {
            [LangEn] = "Open folder",
            [LangEs] = "Abrir carpeta",
        },
        ["DlgPackagerBtnDone"] = new()
        {
            [LangEn] = "Done",
            [LangEs] = "Listo",
        },

        // Game-language status / dialogs
        ["StatusLangIndexLoaded"] = new()
        {
            [LangEn] = "Translation list loaded ({0} available).",
            [LangEs] = "Lista de traducciones cargada ({0} disponibles).",
        },
        ["StatusLangIndexUnavailable"] = new()
        {
            [LangEn] = "Translation list is currently unavailable.",
            [LangEs] = "La lista de traducciones no está disponible.",
        },
        ["StatusLangDownloading"] = new()
        {
            [LangEn] = "Downloading {0} translation pack...",
            [LangEs] = "Descargando paquete de traducción {0}...",
        },
        ["StatusLangApplying"] = new()
        {
            [LangEn] = "Applying {0} translation...",
            [LangEs] = "Aplicando traducción {0}...",
        },
        ["StatusLangApplied"] = new()
        {
            [LangEn] = "✓ {0} translation applied.",
            [LangEs] = "✓ Traducción {0} aplicada.",
        },
        ["StatusLangRevertedToEnglish"] = new()
        {
            [LangEn] = "✓ Reverted game language to English.",
            [LangEs] = "✓ Idioma del juego restablecido a inglés.",
        },
        ["DlgLangApplyTitle"] = new()
        {
            [LangEn] = "Apply translation",
            [LangEs] = "Aplicar traducción",
        },
        ["DlgLangApplyByAuthor"] = new()
        {
            [LangEn] = "by {0}",
            [LangEs] = "por {0}",
        },
        ["DlgLangApplyModVersionsLabel"] = new()
        {
            [LangEn] = "MOD VERSIONS",
            [LangEs] = "VERSIONES DEL MOD",
        },
        ["DlgLangApplySizeLabel"] = new()
        {
            [LangEn] = "DOWNLOAD SIZE",
            [LangEs] = "TAMAÑO",
        },
        ["DlgLangApplyCompatOk"] = new()
        {
            [LangEn] = "Compatible with your installed mod ({0})",
            [LangEs] = "Compatible con tu instalación del mod ({0})",
        },
        ["DlgLangApplyCompatWarn"] = new()
        {
            [LangEn] = "Made for mod {1}, but you have {0}. Some new strings may stay in English.",
            [LangEs] = "Hecha para mod {1}, pero tenés {0}. Algunos strings nuevos pueden quedar en inglés.",
        },
        ["DlgLangApplyDownloading"] = new()
        {
            [LangEn] = "Downloading translation pack...",
            [LangEs] = "Descargando paquete de traducción...",
        },
        ["DlgLangApplyInstalling"] = new()
        {
            [LangEn] = "Extracting pack...",
            [LangEs] = "Extrayendo paquete...",
        },
        ["DlgLangApplyApplying"] = new()
        {
            [LangEn] = "Applying translation files...",
            [LangEs] = "Aplicando archivos de traducción...",
        },
        ["DlgLangApplyBtnApply"] = new()
        {
            [LangEn] = "Apply",
            [LangEs] = "Aplicar",
        },
        ["DlgLangApplyBtnForce"] = new()
        {
            [LangEn] = "Apply anyway",
            [LangEs] = "Aplicar igual",
        },
        ["DlgLangApplyFailedBodyDetail"] = new()
        {
            [LangEn] = "Could not apply the translation:\n\n{0}",
            [LangEs] = "No se pudo aplicar la traducción:\n\n{0}",
        },
        ["DlgLangIncompatibleTitle"] = new()
        {
            [LangEn] = "Translation may not be fully compatible",
            [LangEs] = "La traducción puede no ser totalmente compatible",
        },
        ["DlgLangIncompatibleBody"] = new()
        {
            [LangEn] = "This translation was made for a different version of the mod. " +
                       "Some new strings may stay in English.\n\nApply anyway?",
            [LangEs] = "Esta traducción se hizo para una versión diferente del mod. " +
                       "Algunos strings nuevos pueden quedar en inglés.\n\n¿Aplicar igual?",
        },
        ["DlgLangApplyFailedTitle"] = new()
        {
            [LangEn] = "Could not apply translation",
            [LangEs] = "No se pudo aplicar la traducción",
        },
        ["DlgLangApplyFailedBody"] = new()
        {
            [LangEn] = "The translation could not be applied:\n\n{0}",
            [LangEs] = "No se pudo aplicar la traducción:\n\n{0}",
        },
        ["DlgLangNoDownloadUrlBody"] = new()
        {
            [LangEn] = "This translation entry has no download URL configured.",
            [LangEs] = "Esta entrada de traducción no tiene URL de descarga configurada.",
        },
        ["DlgLangRevertFailedBody"] = new()
        {
            [LangEn] = "Could not revert to English — the original snapshot is missing. " +
                       "Run Verify files to repair the install.",
            [LangEs] = "No se pudo volver al inglés — el respaldo del original no existe. " +
                       "Ejecuta Verificar archivos para reparar la instalación.",
        },

        // -------- Settings menu tooltips --------
        // Hover help so the user knows what each option does without clicking.
        ["TooltipSettingsBody"] = new()
        {
            [LangEn] = "Manage folders, user data, and run health checks",
            [LangEs] = "Gestionar carpetas, datos de usuario y revisar el estado del mod",
        },
        ["TooltipMenuFolders"] = new()
        {
            [LangEn] = "Open folders or change where Wars of Liberty and Age of Empires III are installed",
            [LangEs] = "Abrir o cambiar las carpetas de Wars of Liberty y Age of Empires III",
        },
        ["TooltipMenuOpenModFolder"] = new()
        {
            [LangEn] = "Open the Wars of Liberty install folder in Windows Explorer",
            [LangEs] = "Abrir la carpeta del mod en el Explorador de Windows",
        },
        ["TooltipMenuOpenAoE3Folder"] = new()
        {
            [LangEn] = "Open the Age of Empires III install folder in Windows Explorer",
            [LangEs] = "Abrir la carpeta del juego base en el Explorador de Windows",
        },
        // {0} = mod display name.
        ["TooltipMenuSelectModFolder"] = new()
        {
            [LangEn] = "Manually point the launcher at an existing {0} install if auto-detection failed",
            [LangEs] = "Indicar manualmente dónde está {0} si la detección automática falló",
        },
        ["TooltipMenuSelectAoE3Folder"] = new()
        {
            [LangEn] = "Manually point the launcher at Age of Empires III if it wasn't detected",
            [LangEs] = "Indicar manualmente dónde está Age of Empires III si no se detectó",
        },
        ["TooltipMenuUserData"] = new()
        {
            [LangEn] = "Manage your saved games and backups in Documents\\My Games",
            [LangEs] = "Gestiona tus partidas guardadas y respaldos en Documents\\My Games",
        },
        ["TooltipMenuOpenUserDataFolder"] = new()
        {
            [LangEn] = "View your savegames, custom metropolises and game settings",
            [LangEs] = "Ver tus partidas guardadas, metrópolis y configuración del juego",
        },
        ["TooltipMenuCreateBackupNow"] = new()
        {
            [LangEn] = "Move your current data to a timestamped backup. The game will start fresh next time",
            [LangEs] = "Mover tus datos actuales a un respaldo con la fecha. El juego empezará limpio",
        },
        ["TooltipMenuRestoreUserData"] = new()
        {
            [LangEn] = "Restore an earlier backup. Your current data is automatically backed up first",
            [LangEs] = "Volver a una versión anterior de tus partidas. Los datos actuales se respaldan primero",
        },
        ["TooltipMenuCheckForUpdates"] = new()
        {
            [LangEn] = "Ask the server whether new patches are available",
            [LangEs] = "Comprobar el servidor por si hay parches nuevos disponibles",
        },
        ["TooltipMenuVerifyFiles"] = new()
        {
            [LangEn] = "Check the integrity of the mod's files and repair anything missing or corrupt",
            [LangEs] = "Revisar la integridad de los archivos del mod y reparar archivos dañados o faltantes",
        },
        ["TooltipMenuUninstall"] = new()
        {
            [LangEn] = "Remove Wars of Liberty from this computer. Age of Empires III is not affected",
            [LangEs] = "Eliminar Wars of Liberty del equipo. No afecta a Age of Empires III",
        },
        ["DlgOpenFolderNotFoundTitle"] = new()
        {
            [LangEn] = "Folder not found",
            [LangEs] = "Carpeta no encontrada",
        },
        // {0} = mod display name (appears twice).
        ["DlgOpenFolderNotFoundBody"] = new()
        {
            [LangEn] = "The {0} install folder is not detected. " +
                       "Use 'Select {0} folder' to point the launcher at it.",
            [LangEs] = "No se detectó la carpeta de {0}. " +
                       "Usa 'Seleccionar carpeta de {0}' para indicar dónde está.",
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
