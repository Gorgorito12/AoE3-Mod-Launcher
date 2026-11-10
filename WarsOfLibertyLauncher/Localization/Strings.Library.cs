using System.Collections.Generic;

namespace WarsOfLibertyLauncher.Localization;

/// <summary>
/// The unified Library (design 68a, docs/design_base_launcher): the list of mods, its toolbar and
/// the cover. Merged into the one table by the static constructor in <c>Strings.Rating.cs</c>, where
/// a duplicate key throws. Spanish is es-419 with tuteo, like the rest of the launcher.
/// </summary>
public static partial class Strings
{
    private static readonly Dictionary<string, Dictionary<string, string>> LibraryTable = new()
    {
        ["LibSearchHint"] = new() { [LangEn] = "Search mods…", [LangEs] = "Buscar mods…" },
        ["LibFilterAll"] = new() { [LangEn] = "All", [LangEs] = "Todos" },
        ["LibFilterInstalled"] = new() { [LangEn] = "Installed", [LangEs] = "Instalados" },
        ["LibFilterNotInstalled"] = new() { [LangEn] = "Not installed", [LangEs] = "No instalados" },
        ["LibFilterUpdates"] = new() { [LangEn] = "Updates", [LangEs] = "Actualizaciones" },
        ["LibFilterCompatible"] = new() { [LangEn] = "Compatible", [LangEs] = "Compatibles" },
        ["LibSortRecent"] = new() { [LangEn] = "Most recent", [LangEs] = "Más recientes" },
        ["LibSortName"] = new() { [LangEn] = "Name", [LangEs] = "Nombre" },
        ["LibSortStatus"] = new() { [LangEn] = "Status", [LangEs] = "Estado" },
        ["LibRefreshTip"] = new() { [LangEn] = "Update the catalog", [LangEs] = "Actualizar catálogo" },
        ["LibPublish"] = new() { [LangEn] = "Publish my mod", [LangEs] = "Publicar mi mod" },

        ["LibRailTitle"] = new() { [LangEn] = "MODS", [LangEs] = "MODS" },
        ["LibRailFold"] = new() { [LangEn] = "Fold the list", [LangEs] = "Plegar la lista" },
        ["LibRailUnfold"] = new() { [LangEn] = "Unfold the list", [LangEs] = "Desplegar la lista" },
        ["LibGroupFavorites"] = new() { [LangEn] = "FAVORITES", [LangEs] = "FAVORITOS" },
        ["LibGroupMine"] = new() { [LangEn] = "MY MODS", [LangEs] = "MIS MODS" },
        ["LibGroupMineNote"] = new() { [LangEn] = "{0} · most played first", [LangEs] = "{0} · más jugados primero" },
        ["LibGroupCatalog"] = new() { [LangEn] = "CATALOG", [LangEs] = "CATÁLOGO" },
        ["LibRowUpdate"] = new() { [LangEn] = "Update available", [LangEs] = "Actualización disponible" },
        ["LibRowBase"] = new() { [LangEn] = "Base game", [LangEs] = "Juego base" },
        ["LibRowNotInstalled"] = new() { [LangEn] = "Not installed", [LangEs] = "No instalado" },
        // {0} is the percentage.
        ["LibRowStopped"] = new() { [LangEn] = "Download stopped · {0} %", [LangEs] = "Descarga detenida · {0} %" },
        // {0} version, {1} the copy, {2} how many.
        ["LibRowCopies"] = new() { [LangEn] = "{0} · {1} · {2} copies", [LangEs] = "{0} · {1} · {2} copias" },
        ["LibEmptyTitle"] = new() { [LangEn] = "No mod matches", [LangEs] = "Ningún mod coincide" },
        ["LibEmptyBody"] = new() { [LangEn] = "Try another name or clear the filter.", [LangEs] = "Prueba con otro nombre o quita el filtro." },
        ["LibEmptyUpdatesTitle"] = new() { [LangEn] = "All up to date", [LangEs] = "Todo al día" },
        ["LibEmptyUpdatesBody"] = new() { [LangEn] = "None of your mods has an update waiting.", [LangEs] = "Ninguno de tus mods tiene actualizaciones pendientes." },
        ["LibFavoriteAdd"] = new() { [LangEn] = "Add to favorites", [LangEs] = "Añadir a favoritos" },
        ["LibFavoriteRemove"] = new() { [LangEn] = "Remove from favorites", [LangEs] = "Quitar de favoritos" },

        ["LibStatusInstalled"] = new() { [LangEn] = "INSTALLED", [LangEs] = "INSTALADO" },
        ["LibStatusBase"] = new() { [LangEn] = "BASE GAME", [LangEs] = "JUEGO BASE" },
        ["LibStatusNotInstalled"] = new() { [LangEn] = "NOT INSTALLED", [LangEs] = "NO INSTALADO" },
        ["LibStatusUpdate"] = new() { [LangEn] = "UPDATE AVAILABLE", [LangEs] = "ACTUALIZACIÓN DISPONIBLE" },
        ["LibStatusUpdating"] = new() { [LangEn] = "UPDATING · {0} %", [LangEs] = "ACTUALIZANDO · {0} %" },
        ["LibStatusInstalling"] = new() { [LangEn] = "INSTALLING · {0} %", [LangEs] = "INSTALANDO · {0} %" },
        ["LibStatusStopped"] = new() { [LangEn] = "DOWNLOAD STOPPED", [LangEs] = "DESCARGA DETENIDA" },

        ["LibMore"] = new() { [LangEn] = "Read more", [LangEs] = "Leer más" },
        ["LibLess"] = new() { [LangEn] = "Read less", [LangEs] = "Leer menos" },
        ["LibPlay"] = new() { [LangEn] = "▶  Play", [LangEs] = "▶  Jugar" },
        ["LibUpdate"] = new() { [LangEn] = "Update", [LangEs] = "Actualizar" },
        ["LibInstall"] = new() { [LangEn] = "Install", [LangEs] = "Instalar" },
        ["LibPlayAnyway"] = new() { [LangEn] = "Play without updating", [LangEs] = "Jugar sin actualizar" },
        ["LibPickFolder"] = new() { [LangEn] = "I have it: choose its folder", [LangEs] = "Ya lo tengo: elegir su carpeta" },
        ["LibPickFolderTip"] = new()
        {
            [LangEn] = "If you installed it before, choose the folder it is in and the launcher will use it without downloading it again.",
            [LangEs] = "Si ya lo instalaste antes, elige la carpeta donde está y el launcher lo usará sin volver a descargarlo.",
        },
        ["LibSettingsTip"] = new() { [LangEn] = "Mod settings", [LangEs] = "Ajustes del mod" },
        ["LibCopyCaption"] = new() { [LangEn] = "Copy", [LangEs] = "Copia" },
        ["LibCopyTip"] = new() { [LangEn] = "Choose which copy opens", [LangEs] = "Elegir qué copia se abre" },
        ["LibCopiesHeader"] = new() { [LangEn] = "INSTALLED COPIES", [LangEs] = "COPIAS INSTALADAS" },
        ["LibCopyAdd"] = new() { [LangEn] = "＋ Add another copy…", [LangEs] = "＋ Añadir otra copia…" },
        ["LibCopyManage"] = new() { [LangEn] = "Manage in Settings", [LangEs] = "Gestionar en Ajustes" },

        ["LibFactOwnFolder"] = new() { [LangEn] = "In its own folder", [LangEs] = "En su propia carpeta" },
        ["LibFactOwnFolderTip"] = new() { [LangEn] = "It installs apart. It doesn't touch your AoE3.", [LangEs] = "Se instala aparte. No toca tu AoE3." },
        ["LibFactBaseCopy"] = new() { [LangEn] = "Your own copy of AoE3", [LangEs] = "Tu propia copia de AoE3" },
        ["LibFactBaseCopyTip"] = new() { [LangEn] = "The launcher doesn't install the base game: it uses your copy.", [LangEs] = "El launcher no instala el juego base: usa tu copia." },
        ["LibFactAuto"] = new() { [LangEn] = "Automatic updates", [LangEs] = "Actualizaciones automáticas" },
        ["LibFactAutoTip"] = new() { [LangEn] = "The launcher downloads them for you.", [LangEs] = "El launcher las descarga solo." },
        ["LibFactManual"] = new() { [LangEn] = "Manual updates", [LangEs] = "Actualizaciones manuales" },
        ["LibFactManualTip"] = new() { [LangEn] = "The launcher doesn't update this one.", [LangEs] = "El launcher no actualiza este." },
        ["LibCommunity"] = new() { [LangEn] = "Community", [LangEs] = "Comunidad" },
        ["LibShotTip"] = new() { [LangEn] = "View screenshot", [LangEs] = "Ver captura" },
        // {0} the mod, {1} of {2}.
        ["LibShotTitle"] = new() { [LangEn] = "{0} · {1} / {2}", [LangEs] = "{0} · {1} / {2}" },

        ["LibPaused"] = new() { [LangEn] = "Paused", [LangEs] = "En pausa" },
        ["LibStoppedTitle"] = new() { [LangEn] = "The download stopped", [LangEs] = "La descarga se detuvo" },
        ["LibDownloadsHeader"] = new() { [LangEn] = "DOWNLOADS · {0} in progress", [LangEs] = "DESCARGAS · {0} en curso" },
        ["LibDownloadStopped"] = new() { [LangEn] = "Download stopped", [LangEs] = "Descarga detenida" },

        ["DlgSetAppearanceGroup"] = new() { [LangEn] = "APPEARANCE", [LangEs] = "APARIENCIA" },
        ["DlgSetTextTabsGroup"] = new() { [LangEn] = "TEXT AND TABS", [LangEs] = "TEXTO Y PESTAÑAS" },
        ["DlgSetPreviewCaption"] = new() { [LangEn] = "PREVIEW", [LangEs] = "VISTA PREVIA" },
        ["DlgSetPreviewSettings"] = new() { [LangEn] = "Settings", [LangEs] = "Ajustes" },
        ["DlgSetGlassTitle"] = new() { [LangEn] = "Transparency", [LangEs] = "Transparencia" },
        ["DlgSetGlassHint"] = new() { [LangEn] = "How much of the mod's image shows behind the panels. Default: Medium.", [LangEs] = "Cuánto deja ver la imagen del mod detrás de los paneles. Predeterminada: Media." },
        ["DlgSetGlassSolid"] = new() { [LangEn] = "Solid", [LangEs] = "Sólida" },
        ["DlgSetGlassMedium"] = new() { [LangEn] = "Medium", [LangEs] = "Media" },
        ["DlgSetGlassMaximum"] = new() { [LangEn] = "Maximum", [LangEs] = "Máxima" },
        ["DlgSetModImageTitle"] = new() { [LangEn] = "Mod image in the background", [LangEs] = "Imagen del mod de fondo" },
        ["DlgSetModImageHint"] = new() { [LangEn] = "When off, only the glow of its colour stays.", [LangEs] = "Desactivada, solo queda el resplandor de su color." },
        ["DlgSetModColorTitle"] = new() { [LangEn] = "Each mod's colour", [LangEs] = "Color de cada mod" },
        ["DlgSetModColorHint"] = new() { [LangEn] = "When off, the launcher's blue is always used.", [LangEs] = "Desactivado, se usa siempre el azul del launcher." },
        ["DlgSetEffectsTitle"] = new() { [LangEn] = "Effects and animations", [LangEs] = "Efectos y animaciones" },
        ["DlgSetEffectsHint"] = new() { [LangEn] = "Automatic reduces them if Windows has animations turned off.", [LangEs] = "Automático los reduce si Windows tiene las animaciones desactivadas." },
        ["DlgSetEffectsAuto"] = new() { [LangEn] = "Automatic", [LangEs] = "Automático" },
        ["DlgSetEffectsFull"] = new() { [LangEn] = "Full", [LangEs] = "Completos" },
        ["DlgSetEffectsLow"] = new() { [LangEn] = "Reduced", [LangEs] = "Reducidos" },
        ["DlgSetModListTitle"] = new() { [LangEn] = "Mod list", [LangEs] = "Lista de mods" },
        ["DlgSetModListHint"] = new() { [LangEn] = "Automatic: folded to icons only in windows under 1400 px.", [LangEs] = "Automática: plegada a solo iconos en ventanas de menos de 1400 px." },
        ["DlgSetModListAuto"] = new() { [LangEn] = "Automatic", [LangEs] = "Automática" },
        ["DlgSetModListOpen"] = new() { [LangEn] = "Unfolded", [LangEs] = "Desplegada" },
        ["DlgSetModListClosed"] = new() { [LangEn] = "Folded", [LangEs] = "Plegada" },
        ["DlgSetStartTabTitle"] = new() { [LangEn] = "Tab on opening", [LangEs] = "Pestaña al abrir" },
        ["DlgSetStartTabHint"] = new()
        {
            [LangEn] = "With the Workshop and the Library together there are two tabs.",
            [LangEs] = "Con Taller y Biblioteca unidas quedan dos pestañas.",
        },
        ["DlgSetStartMultiplayer"] = new() { [LangEn] = "Multiplayer", [LangEs] = "Multijugador" },
        ["DlgSetStartLibrary"] = new() { [LangEn] = "Library", [LangEs] = "Biblioteca" },
        ["DlgSetResetAppearance"] = new() { [LangEn] = "Reset", [LangEs] = "Restablecer" },

        // {0} how many.
        ["NotifUnreadCount"] = new() { [LangEn] = "{0} unread", [LangEs] = "{0} sin leer" },
        ["NotifFilterAll"] = new() { [LangEn] = "All", [LangEs] = "Todas" },
        ["NotifFilterMods"] = new() { [LangEn] = "Mods", [LangEs] = "Mods" },
        ["NotifFilterLauncher"] = new() { [LangEn] = "Launcher", [LangEs] = "Launcher" },
        ["NotifDayToday"] = new() { [LangEn] = "TODAY", [LangEs] = "HOY" },
        ["NotifDayYesterday"] = new() { [LangEn] = "YESTERDAY", [LangEs] = "AYER" },
        ["NotifDayThisWeek"] = new() { [LangEn] = "THIS WEEK", [LangEs] = "ESTA SEMANA" },
        ["NotifDayEarlier"] = new() { [LangEn] = "EARLIER", [LangEs] = "ANTES" },
        ["NotifActionUpdate"] = new() { [LangEn] = "Update", [LangEs] = "Actualizar" },
        ["NotifActionPlay"] = new() { [LangEn] = "▶  Play", [LangEs] = "▶  Jugar" },
        ["NotifActionView"] = new() { [LangEn] = "View", [LangEs] = "Ver" },
        ["NotifActionRestartAndUpdate"] = new() { [LangEn] = "Restart and update", [LangEs] = "Reiniciar y actualizar" },
        ["NotifChoose"] = new() { [LangEn] = "Choose which notices I get", [LangEs] = "Elegir qué avisos recibo" },
        ["NotifClearRead"] = new() { [LangEn] = "Clear the read ones", [LangEs] = "Borrar las leídas" },
        ["NotifDismiss"] = new() { [LangEn] = "Delete", [LangEs] = "Borrar" },
        ["NotifTimeAgo"] = new() { [LangEn] = "{0} ago", [LangEs] = "hace {0}" },
        ["NotifTimeYesterday"] = new() { [LangEn] = "yesterday, {0}", [LangEs] = "ayer, {0}" },

        ["MpBadgeAccountLabel"] = new() { [LangEn] = "BADGE BESIDE YOUR NAME", [LangEs] = "INSIGNIA JUNTO A TU NOMBRE" },
        ["MpBadgeAccountHint"] = new() { [LangEn] = "This is how you look in the chat and the players list.", [LangEs] = "Así te ven en el chat y en la lista de jugadores." },

        ["MpSignInHeadline"] = new() { [LangEn] = "Authorize the launcher on Discord", [LangEs] = "Autoriza al launcher en Discord" },
        ["MpSignInLead"] = new()
        {
            [LangEn] = "It opens in your browser. When you press “Authorize”, this window closes itself and you are connected.",
            [LangEs] = "Se abre en tu navegador. Cuando pulses «Autorizar», esta ventana se cierra sola y quedas conectado.",
        },
        ["MpSignInLeadCode"] = new()
        {
            [LangEn] = "It opens in your browser. Type the code when asked; this window closes itself when you finish.",
            [LangEs] = "Se abre en tu navegador. Escribe el código cuando te lo pida; esta ventana se cierra sola al terminar.",
        },
        ["MpSignInPreparing"] = new() { [LangEn] = "Preparing the link…", [LangEs] = "Preparando el enlace…" },
        ["MpSignInNotReady"] = new() { [LangEn] = "Available once the link is ready", [LangEs] = "Disponible cuando el enlace esté listo" },
        ["MpSignInCopyChip"] = new() { [LangEn] = "Copy", [LangEs] = "Copiar" },
        // {0} minutes.
        ["MpSignInLinkLasts"] = new() { [LangEn] = "The link lasts {0} minutes.", [LangEs] = "El enlace sirve {0} minutos." },
        ["MpSignInFailPrepare"] = new() { [LangEn] = "Sign-in could not be prepared", [LangEs] = "No se pudo preparar el inicio de sesión" },
        ["MpSignInFailFinish"] = new() { [LangEn] = "Sign-in could not be finished", [LangEs] = "No se pudo completar el inicio de sesión" },
        ["MpSignInFailLead"] = new()
        {
            [LangEn] = "The room server did not answer. Check your connection and try again.",
            [LangEs] = "El servidor de salas no respondió. Revisa tu conexión y vuelve a intentarlo.",
        },
        ["MpSignInFailTitle"] = new() { [LangEn] = "No answer from the server", [LangEs] = "Sin respuesta del servidor" },
        ["MpSignInFailBody"] = new() { [LangEn] = "The authorization link could not be requested.", [LangEs] = "No se pudo pedir el enlace de autorización." },
        ["MpSignInFailBodyWait"] = new() { [LangEn] = "Contact was lost while waiting for your authorization.", [LangEs] = "Se perdió el contacto mientras esperábamos tu autorización." },
        ["MpSignInRefusedLead"] = new()
        {
            [LangEn] = "The room server answered with an error. Try again in a moment.",
            [LangEs] = "El servidor de salas respondió con un error. Vuelve a intentarlo en un momento.",
        },
        ["MpSignInRefusedTitle"] = new() { [LangEn] = "The server refused the request", [LangEs] = "El servidor rechazó la petición" },
        ["MpSignInRetry"] = new() { [LangEn] = "Try again", [LangEs] = "Reintentar" },
        ["MpSignInExpiredHeadline"] = new() { [LangEn] = "The link expired", [LangEs] = "El enlace caducó" },
        // {0} minutes.
        ["MpSignInExpiredLead"] = new()
        {
            [LangEn] = "More than {0} minutes went by without authorizing. Ask for a new link: the old one no longer works.",
            [LangEs] = "Pasaron más de {0} minutos sin autorizar. Pide un enlace nuevo: el anterior ya no sirve.",
        },
        ["MpSignInExpiredLeadPlain"] = new()
        {
            [LangEn] = "Too much time went by without authorizing. Ask for a new link: the old one no longer works.",
            [LangEs] = "Pasó demasiado tiempo sin autorizar. Pide un enlace nuevo: el anterior ya no sirve.",
        },
        ["MpSignInExpiredTitle"] = new() { [LangEn] = "Link expired", [LangEs] = "Enlace caducado" },
        ["MpSignInExpiredBody"] = new()
        {
            [LangEn] = "If you already pressed “Authorize” in the browser, it was too late: Discord discarded it.",
            [LangEs] = "Si ya pulsaste «Autorizar» en el navegador, fue tarde: Discord lo descartó.",
        },
        ["MpSignInNewLink"] = new() { [LangEn] = "Get another link", [LangEs] = "Pedir otro enlace" },

        ["DlgSelfInstallHeadline"] = new()
        {
            [LangEn] = "For Start with Windows to keep working, the launcher needs a fixed copy",
            [LangEs] = "Para que el arranque con Windows siga funcionando, el launcher necesita una copia fija",
        },
        ["DlgSelfInstallWhatItDoes"] = new() { [LangEn] = "WHAT IT DOES", [LangEs] = "QUÉ HACE" },
        ["DlgSelfInstallStepFolder"] = new()
        {
            [LangEn] = "Copies the launcher to a folder of *your user* and points the startup there.",
            [LangEs] = "Copia el launcher a una carpeta de *tu usuario* y apunta el arranque ahí.",
        },
        ["DlgSelfInstallStepNoService"] = new()
        {
            [LangEn] = "No service, no scheduled task, no administrator permissions.",
            [LangEs] = "Sin servicio, sin tarea programada, sin permisos de administrador.",
        },
        ["DlgSelfInstallStepEntry"] = new()
        {
            [LangEn] = "The startup entry is called `Aoe3ModLauncher` and you can remove it in Settings › General.",
            [LangEs] = "La entrada de inicio se llama `Aoe3ModLauncher` y puedes quitarla en Ajustes › General.",
        },
        ["DlgSelfInstallDestination"] = new() { [LangEn] = "COPIED TO", [LangEs] = "SE COPIA A" },
        // {0} the copy's size, {1} the free space, {2} the drive.
        ["DlgSelfInstallSize"] = new() { [LangEn] = "{0} · {1} left on {2}", [LangEs] = "{0} · quedan {1} en {2}" },
        // {0} the drive, {1} how much is missing.
        ["DlgSelfInstallNoRoom"] = new()
        {
            [LangEn] = "There is no room on {0} for the copy. Free at least {1} and try again, or leave the startup as it is.",
            [LangEs] = "No hay sitio en {0} para la copia. Libera al menos {1} y vuelve a intentarlo, o deja el arranque como está.",
        },
        ["DlgSelfInstallNotNow"] = new()
        {
            [LangEn] = "If you say “Not now”, the startup stays as it is: it works as long as you don't move or delete this folder. You can come back to this from Settings › General.",
            [LangEs] = "Si dices «Ahora no», el arranque se queda como está: funciona mientras no muevas ni borres esta carpeta. Puedes volver a esto desde Ajustes › General.",
        },
        ["DlgSelfUninstallLead"] = new()
        {
            [LangEn] = "The launcher closes and is deleted. Your mods and your Age of Empires III are not touched.",
            [LangEs] = "El launcher se cierra y se borra. Tus mods y tu Age of Empires III no se tocan.",
        },
        ["DlgSelfUninstallLeadPortable"] = new()
        {
            [LangEn] = "You are running the portable file, but there is an installed copy and startup entries left from an earlier time. This removes them; the file you opened stays.",
            [LangEs] = "Estás ejecutando el archivo portátil, pero hay una copia instalada y entradas de inicio que dejó una vez anterior. Esto las quita; el archivo que abriste se queda.",
        },
        ["DlgSelfUninstallLeadPortableAlone"] = new()
        {
            [LangEn] = "You are running the portable file. This removes what the launcher left on this PC; the file you opened stays.",
            [LangEs] = "Estás ejecutando el archivo portátil. Esto quita lo que el launcher dejó en este PC; el archivo que abriste se queda.",
        },
        ["DlgSelfUninstallPortableLabel"] = new() { [LangEn] = "STAYS · THE FILE YOU OPENED", [LangEs] = "SE QUEDA · EL ARCHIVO QUE ABRISTE" },
        ["DlgSelfUninstallConfirm"] = new() { [LangEn] = "Uninstall the launcher", [LangEs] = "Desinstalar el launcher" },
        ["DlgSelfUninstallConfirmData"] = new() { [LangEn] = "Uninstall and delete my data", [LangEs] = "Desinstalar y borrar mis datos" },
        ["DlgSelfUninstallAnd"] = new() { [LangEn] = " and ", [LangEs] = " y " },

        // {0} a .zip's name.
        ["DlgPatchGenComparing"] = new() { [LangEn] = "Comparing {0}…", [LangEs] = "Comparando {0}…" },
        ["DlgPatchGenWritingIncremental"] = new() { [LangEn] = "Writing the incremental patch…", [LangEs] = "Escribiendo el parche incremental…" },
        ["DlgPatchGenWritingCumulative"] = new() { [LangEn] = "Writing the cumulative patch…", [LangEs] = "Escribiendo el parche acumulado…" },
        // {0} done, {1} total.
        ["DlgPatchGenFiles"] = new() { [LangEn] = "{0} of {1} files", [LangEs] = "{0} de {1} archivos" },
        ["DlgPatchGenSubCompare"] = new()
        {
            [LangEn] = "Works out the SHA-256 of each file to know which ones changed.",
            [LangEs] = "Calcula el SHA-256 de cada archivo para saber cuáles cambiaron.",
        },
        // {0} from, {1} to.
        ["DlgPatchGenSubIncremental"] = new()
        {
            [LangEn] = "Only the files that changed between {0} and {1}, and the list of the deleted ones.",
            [LangEs] = "Solo los archivos que cambiaron entre {0} y {1}, y la lista de los borrados.",
        },
        // {0} the base.
        ["DlgPatchGenSubCumulative"] = new()
        {
            [LangEn] = "The same from {0}. It takes the longest: it compares against the whole base.",
            [LangEs] = "Lo mismo desde {0}. Es el que más tarda: compara contra la base entera.",
        },
        ["DlgPatchGenCancel"] = new() { [LangEn] = "Cancel", [LangEs] = "Cancelar" },
        ["DlgPatchGenAnother"] = new() { [LangEn] = "Generate another", [LangEs] = "Generar otro" },
        ["DlgPatchGenShowInFolder"] = new() { [LangEn] = "Show in the folder", [LangEs] = "Mostrar en la carpeta" },
        ["DlgPatchGenDiagIncremental"] = new() { [LangEn] = "→  Incremental patch: from the previous to the new", [LangEs] = "→  Parche incremental: de la anterior a la nueva" },
        ["DlgPatchGenDiagCumulative"] = new() { [LangEn] = "→  Cumulative patch: from the base to the new", [LangEs] = "→  Parche acumulado: desde la base hasta la nueva" },
        ["DlgPatchGenFootReady"] = new()
        {
            [LangEn] = "One button does the job: it writes the patches to the output folder and opens Explorer.",
            [LangEs] = "Un solo botón hace el trabajo: escribe los parches en la carpeta de salida y abre el Explorador.",
        },
        ["DlgPatchGenFootMissing"] = new() { [LangEn] = "The two .zip files and their tags are missing.", [LangEs] = "Faltan los dos .zip y sus tags para poder generar." },
        ["DlgPatchGenFootWorking"] = new() { [LangEn] = "You can cancel: nothing half done stays in the output folder.", [LangEs] = "Puedes cancelar: no queda nada a medias en la carpeta de salida." },
        ["DlgPatchGenFootDone"] = new() { [LangEn] = "The files are already in the output folder.", [LangEs] = "Los archivos ya están en la carpeta de salida." },
        ["DlgPatchGenFailZip"] = new()
        {
            [LangEn] = "One of the .zip files could not be opened. Check it is not damaged or in use by another program.",
            [LangEs] = "Uno de los .zip no se pudo abrir. Revisa que no esté dañado ni en uso por otro programa.",
        },
        ["DlgPatchGenFailOther"] = new()
        {
            [LangEn] = "Check the detail below, correct what you can and try again.",
            [LangEs] = "Revisa el detalle, corrige lo que puedas y vuelve a intentarlo.",
        },

        ["DlgPackagerHintName"] = new() { [LangEn] = "Just as it will appear in the language menu.", [LangEs] = "Tal cual saldrá en el menú de idioma." },
        ["DlgPackagerHintModMissing"] = new()
        {
            [LangEn] = "It is not installed on this PC: you can package anyway, but you will have to choose the originals by hand and type the mod’s version.",
            [LangEs] = "No está instalado en este PC: puedes empaquetar igual, pero tendrás que elegir los originales a mano y escribir la versión del mod.",
        },
        ["DlgPackagerHintVersionUnknown"] = new()
        {
            [LangEn] = "With no known version of the mod, type your own (1.0, 1.1…).",
            [LangEs] = "Sin versión conocida del mod, escribe la tuya (1.0, 1.1…).",
        },
        ["DlgPackagerHintOriginalsMissing"] = new()
        {
            [LangEn] = "With the mod not installed, choose your copy of the English XMLs so they can be compared.",
            [LangEs] = "Sin el mod instalado, elige tu copia de los XML en inglés para poder comparar.",
        },
        ["DlgPackagerCompatUnknown"] = new() { [LangEn] = "The installed version of the mod", [LangEs] = "La versión instalada del mod" },
        ["DlgPackagerCompatHint"] = new()
        {
            [LangEn] = "It is the one players who update from the launcher have today.",
            [LangEs] = "Es la que tienen hoy los jugadores que actualizan desde el launcher.",
        },
        ["DlgPackagerCompatHintUnknown"] = new()
        {
            [LangEn] = "The launcher does not know it yet: type it in “Other versions”.",
            [LangEs] = "El launcher no la conoce todavía: escríbela en «Otras versiones».",
        },
        ["DlgPackagerCompatPlaceholder"] = new() { [LangEn] = "1.6.1, 1.6.0", [LangEs] = "1.6.1, 1.6.0" },
        ["DlgPackagerChooseFiles"] = new() { [LangEn] = "Choose files…", [LangEs] = "Elegir archivos…" },
        ["DlgPackagerBrowse"] = new() { [LangEn] = "Browse…", [LangEs] = "Examinar…" },
        ["DlgPackagerProposed"] = new() { [LangEn] = "PROPOSED", [LangEs] = "PROPUESTA" },
        ["DlgPackagerProposedVersionTip"] = new()
        {
            [LangEn] = "Proposed by the launcher: the mod’s version and the next free revision. Type another if you like.",
            [LangEs] = "Propuesta por el launcher: la versión del mod y la siguiente revisión libre. Escribe otra si quieres.",
        },
        ["DlgPackagerProposedOutputTip"] = new()
        {
            [LangEn] = "It follows the mod and the language code until you type a path yourself.",
            [LangEs] = "Sigue al mod y al código de idioma hasta que escribas una ruta tú.",
        },
        ["DlgPackagerFootReady"] = new()
        {
            [LangEn] = "One button does the job: it works out the fingerprints, writes the manifest and leaves the pack in the chosen folder.",
            [LangEs] = "Un solo botón hace el trabajo: calcula las huellas, escribe el manifiesto y deja el paquete en la carpeta elegida.",
        },
        ["DlgPackagerFootConfirm"] = new() { [LangEn] = "Correct the version or confirm it is the one you want.", [LangEs] = "Corrige la versión o confirma que es la que quieres." },
        ["DlgPackagerFootDone"] = new() { [LangEn] = "You can open the packager again from Settings › Advanced.", [LangEs] = "Puedes volver a abrir el empaquetador desde Ajustes › Avanzado." },
        ["DlgPackagerGenerateAnyway"] = new() { [LangEn] = "Generate anyway", [LangEs] = "Generar de todos modos" },
        ["DlgPackagerLooksLikeModTitle"] = new() { [LangEn] = "That looks like a version of the mod", [LangEs] = "Eso parece una versión del mod" },
        ["DlgPackagerMissingOne"] = new() { [LangEn] = "One thing is missing to generate the pack", [LangEs] = "Falta una cosa para generar el paquete" },
        // {0} how many.
        ["DlgPackagerMissingMany"] = new() { [LangEn] = "{0} things are missing to generate the pack", [LangEs] = "Faltan {0} cosas para generar el paquete" },
        ["DlgPackagerMissingBody"] = new()
        {
            [LangEn] = "The marked fields say what is missing. Nothing has been written yet.",
            [LangEs] = "Los campos marcados dicen qué falta. Nada se escribió todavía.",
        },
        ["DlgPackagerFailTitle"] = new() { [LangEn] = "The pack could not be generated", [LangEs] = "No se pudo generar el paquete" },
        // {0} language name, {1} mod, {2} version, {3} where.
        ["DlgPackagerResultSummary"] = new()
        {
            [LangEn] = "{0} for {1}, version `{2}`. Everything is in {3}, in the folder ready to upload.",
            [LangEs] = "{0} para {1}, versión `{2}`. Todo está en {3}, en la carpeta lista para subir.",
        },
        ["DlgPackagerOnTheDesktop"] = new() { [LangEn] = "the Desktop", [LangEs] = "el escritorio" },
        ["DlgPackagerWritten"] = new() { [LangEn] = "WHAT WAS WRITTEN", [LangEs] = "LO QUE SE ESCRIBIÓ" },
        ["DlgPackagerRoleFolder"] = new() { [LangEn] = "The folder ready to upload to the repository, with the manifest inside.", [LangEs] = "La carpeta lista para subir al repositorio, con el manifiesto dentro." },
        ["DlgPackagerRoleZip"] = new() { [LangEn] = "The pack: the XMLs and the manifest.", [LangEs] = "El paquete: los XML y el manifiesto." },
        ["DlgPackagerRoleJson"] = new() { [LangEn] = "The manifest on its own, in case you publish the .zip as a release attachment.", [LangEs] = "El manifiesto suelto, por si publicas el .zip como adjunto de una release." },
        ["DlgPackagerPublishHeader"] = new() { [LangEn] = "TO PUBLISH IT IN THE MOD’S REPOSITORY", [LangEs] = "PARA PUBLICARLA EN EL REPOSITORIO DEL MOD" },
        // {0} the repository.
        ["DlgPackagerStepFork"] = new() { [LangEn] = "Fork `{0}` on GitHub, if you have not already.", [LangEs] = "Haz un fork de `{0}` en GitHub, si no lo tienes." },
        // {0} the folder.
        ["DlgPackagerStepCopy"] = new() { [LangEn] = "Copy the `{0}` folder as it is into your fork, on the same path.", [LangEs] = "Copia la carpeta `{0}` tal cual dentro de tu fork, en la misma ruta." },
        ["DlgPackagerStepPr"] = new()
        {
            [LangEn] = "Open a pull request. The automatic check refuses a repeated version, so you don’t need to review that yourself.",
            [LangEs] = "Abre un pull request. La comprobación automática rechaza una versión repetida, así que no hace falta que lo revises tú.",
        },
        ["DlgPackagerPublishNote"] = new()
        {
            [LangEn] = "When the maintainer accepts the change, players see it in the language menu without doing anything else.",
            [LangEs] = "Cuando el mantenedor acepte el cambio, los jugadores la ven en el menú de idioma sin hacer nada más.",
        },
        // {0} the mod's versions.
        ["DlgPackagerPreviewCompat"] = new() { [LangEn] = "for mod {0}", [LangEs] = "para el mod {0}" },
        ["DlgPackagerPreviewNoCompat"] = new() { [LangEn] = "no mod version", [LangEs] = "sin versión del mod" },

        ["MpWatchReadOnly"] = new() { [LangEn] = "READ ONLY", [LangEs] = "SOLO LECTURA" },
        ["MpWatchReadOnlyTip"] = new()
        {
            [LangEn] = "The server does not let in anyone who is not playing: you see the chat, but you cannot write.",
            [LangEs] = "El servidor no deja entrar a quien no juega: ves el chat, pero no puedes escribir.",
        },
        ["MpWatchDecideTip"] = new()
        {
            [LangEn] = "Set the result of this match yourself: the same as “Decide this match” in the bracket",
            [LangEs] = "Fija tú el resultado del cruce: lo mismo que «Decidir esta partida» en el cuadro",
        },
        // {0} who is missing.
        ["MpWatchWaitingFor"] = new() { [LangEn] = "Waiting for {0}", [LangEs] = "Esperando a {0}" },
        // {0} minutes.
        ["MpWatchFinishedAgo"] = new() { [LangEn] = "Finished {0} min ago", [LangEs] = "Terminada hace {0} min" },
        // {0} the format, such as 1v1.
        ["MpWatchCompetitive"] = new() { [LangEn] = "Competitive · {0}", [LangEs] = "Competitiva · {0}" },
        ["MpWatchNotIn"] = new() { [LangEn] = "Not in yet", [LangEs] = "No entró todavía" },
        ["MpWatchCaptain"] = new() { [LangEn] = "captain", [LangEs] = "capitán" },
        // {0} the seed.
        ["MpWatchSeed"] = new() { [LangEn] = "seed {0}", [LangEs] = "semilla {0}" },
        ["MpWatchRosterNote"] = new() { [LangEn] = "Only the two entrants of the match can go in.", [LangEs] = "Solo pueden entrar los dos inscritos del cruce." },
        ["MpWatchResult"] = new() { [LangEn] = "RESULT", [LangEs] = "RESULTADO" },
        // {0} the winner.
        ["MpWatchWon"] = new() { [LangEn] = "{0} won", [LangEs] = "Ganó {0}" },
        ["MpWatchResultSub"] = new()
        {
            [LangEn] = "British against French · 33 min · recording sent by {0}. It counts for both players’ ELO.",
            [LangEs] = "Británicos contra Franceses · 33 min · grabación enviada por {0}. Cuenta para el ELO de los dos.",
        },
        ["MpWatchFootPlaying"] = new()
        {
            [LangEn] = "You can see the chat but not write: the server does not let in anyone who is not playing. If there is a dispute, decide the match from here or from the bracket.",
            [LangEs] = "El chat se ve pero no se puede escribir: el servidor no deja entrar a quien no juega. Si hay disputa, decide la partida desde aquí o desde el cuadro.",
        },
        // {0} who is missing.
        ["MpWatchFootWaiting"] = new()
        {
            [LangEn] = "If {0} does not get in on time, decide the match from here or from the bracket. You can see the chat but not write: the server does not let in anyone who is not playing.",
            [LangEs] = "Si {0} no entra a tiempo, decide la partida desde aquí o desde el cuadro. El chat se ve pero no se puede escribir: el servidor no deja entrar a quien no juega.",
        },
        // {0} the winner.
        ["MpWatchFootDone"] = new()
        {
            [LangEn] = "The result is already in the bracket and {0} moves on. You can see the chat but not write.",
            [LangEs] = "El resultado ya está en el cuadro y {0} pasa de ronda. El chat se ve pero no se puede escribir.",
        },
        ["MpTournamentDemoWatchStarted"] = new() { [LangEn] = "Match under way.", [LangEs] = "Partida en marcha." },
        ["MpTournamentDemoWatchEnded"] = new() { [LangEn] = "Match over. The result and the recording were sent.", [LangEs] = "Partida terminada. Se enviaron el resultado y la grabación." },
        ["MpTournamentDemoWatchOpened"] = new() { [LangEn] = "The match’s room was opened.", [LangEs] = "Se abrió la sala del cruce." },
        ["MpTournamentDemoWatchReady"] = new() { [LangEn] = "I’m in, tell me when you are", [LangEs] = "estoy dentro, avisa cuando entres" },
        ["MpTournamentDemoWatchOnlyEntrants"] = new() { [LangEn] = "The room waits for the other entrant; nobody else can go in.", [LangEs] = "La sala espera al otro inscrito; nadie más puede entrar." },

        ["MpStatsHeadPlayers"] = new() { [LangEn] = "{0} players in {1} days", [LangEs] = "{0} jugadores en {1} días" },
        ["MpStatsColRecord"] = new() { [LangEn] = "RECORD", [LangEs] = "RÉCORD" },
        ["MpStatsLoadingShort"] = new() { [LangEn] = "Loading…", [LangEs] = "Cargando…" },
        ["MpStatsErrorTitle"] = new() { [LangEn] = "Couldn't load the statistics", [LangEs] = "No se pudieron cargar las estadísticas" },
        ["MpStatsErrorBody"] = new() { [LangEn] = "The server didn't answer. The other tabs keep working; there is nothing saved to show here.", [LangEs] = "El servidor no respondió. Las demás pestañas siguen funcionando; aquí no hay nada guardado que mostrar." },
        ["MpStatsRetry"] = new() { [LangEn] = "Try again", [LangEs] = "Reintentar" },
        ["MpStatsCivsScope"] = new() { [LangEn] = "{0} of {1} matches carry a civilization", [LangEs] = "{0} de {1} partidas traen civilización" },
        ["MpStatsCivsScopeEmpty"] = new() { [LangEn] = "{0} of {1} rated matches carry a civilization", [LangEs] = "{0} de {1} partidas puntuadas traen civilización" },
        ["MpStatsCivsEmptyBody"] = new() { [LangEn] = "The launcher started recording each player's civilization only recently. This table and the matchups fill up with the matches played from now on: there is no way to work it out for the older ones. {0} has many civilizations, so it will take a good sample before a percentage means anything.", [LangEs] = "El launcher empezó a registrar la civilización de cada jugador hace poco. Esta tabla y los enfrentamientos se llenan con las partidas que se jueguen desde ahora: en las viejas no hay forma de deducirla. {0} trae muchas civilizaciones, así que hará falta bastante muestra antes de que un porcentaje signifique algo." },
        ["MpStatsTailCivsOpen"] = new() { [LangEn] = "Also showing the {0} with {1} matches or fewer.", [LangEs] = "Mostrando también las {0} con {1} partidas o menos." },
        ["MpStatsSeeLess"] = new() { [LangEn] = "See less", [LangEs] = "Ver menos" },
        ["MpStatsSeeAllCivs"] = new() { [LangEn] = "See all", [LangEs] = "Ver todas" },
        ["MpStatsBarTip"] = new() { [LangEn] = "{0} wins out of {1} decided", [LangEs] = "{0} victorias de {1} decididas" },
        ["MpStatsBarTipNoSample"] = new() { [LangEn] = "No sample: {0} decided, {1} needed", [LangEs] = "Sin muestra: {0} decididas, hacen falta {1}" },
        ["MpStatsPctTip"] = new() { [LangEn] = "Shown from {0} decided matches", [LangEs] = "Se muestra a partir de {0} partidas decididas" },
        ["MpStatsMatchupsScope"] = new() { [LangEn] = "civilization against civilization", [LangEs] = "civilización contra civilización" },
        ["MpStatsAlliesScope"] = new() { [LangEn] = "on the same team", [LangEs] = "en el mismo equipo" },
        ["MpStatsActivityMatches"] = new() { [LangEn] = "matches", [LangEs] = "partidas" },
        ["MpStatsActivityRated"] = new() { [LangEn] = "Counted for the ELO", [LangEs] = "Puntuaron para el ELO" },
        ["MpStatsActivityNoResult"] = new() { [LangEn] = "No result read: the recording couldn't be opened", [LangEs] = "Sin resultado leído: la grabación no se pudo abrir" },
        ["MpStatsActivityUnrated"] = new() { [LangEn] = "Didn't count: {0}", [LangEs] = "No contaron: {0}" },
        ["MpStatsDaysTip"] = new() { [LangEn] = "Matches per day, from {0} days ago to today", [LangEs] = "Partidas por día, de hace {0} días a hoy" },
        ["MpStatsPeakTitle"] = new() { [LangEn] = "Peak hours", [LangEs] = "Horas punta" },
        ["MpStatsPeakNoSample"] = new() { [LangEn] = "Peak hours appear from {0} rooms opened in {1} days; with fewer, the tallest hour would be noise.", [LangEs] = "Las horas punta se muestran a partir de {0} salas abiertas en {1} días; con menos, la hora más alta sería ruido." },
        ["MpStatsPeakCaption"] = new() { [LangEn] = "This is when most rooms open, in your time. Over {0} rooms in {1} days.", [LangEs] = "Es cuando más salas se abren, en tu hora. Sobre {0} salas en {1} días." },
        ["MpStatsHoursTip"] = new() { [LangEn] = "Rooms opened per hour, in your time", [LangEs] = "Salas abiertas por hora, en tu horario" },
        ["MpStatsMapsEmpty"] = new() { [LangEn] = "No map has been recorded yet.", [LangEs] = "Todavía no hay ningún mapa registrado." },
        ["MpStatsTailMapsOpen"] = new() { [LangEn] = "Also showing the {0} maps with one match.", [LangEs] = "Mostrando también los {0} mapas con una partida." },
        ["MpStatsNoMapOne"] = new() { [LangEn] = "{0} match has no map: its recording couldn't be read, so it doesn't count here.", [LangEs] = "{0} partida no tiene mapa: su grabación no se pudo leer, así que no suma aquí." },
        ["MpStatsNoMapMany"] = new() { [LangEn] = "{0} matches have no map: their recording couldn't be read, so they don't count here.", [LangEs] = "{0} partidas no tienen mapa: su grabación no se pudo leer, así que no suman aquí." },
        ["MpStatsMapPackTip"] = new() { [LangEn] = "Map pack", [LangEs] = "Paquete de mapas" },
        ["MpStatsDecksScope"] = new() { [LangEn] = "{0} players shared their deck", [LangEs] = "{0} jugadores compartieron su mazo" },
        ["MpStatsDecksScopeOne"] = new() { [LangEn] = "{0} player shared their deck", [LangEs] = "{0} jugador compartió su mazo" },
        ["MpStatsDecksUnderOne"] = new() { [LangEn] = "The community shared {0} {1} deck: {2} are needed to say what it brings.", [LangEs] = "La comunidad compartió {0} mazo de {1}: hacen falta {2} para decir qué lleva." },
        ["MpStatsDecksUnderMany"] = new() { [LangEn] = "The community shared {0} {1} decks: {2} are needed to say what it brings.", [LangEs] = "La comunidad compartió {0} mazos de {1}: hacen falta {2} para decir qué lleva." },
        ["MpStatsDecksDistinctOne"] = new() { [LangEn] = "{0} distinct card.", [LangEs] = "{0} carta distinta." },
        ["MpStatsDecksDistinctMany"] = new() { [LangEn] = "{0} distinct cards.", [LangEs] = "{0} cartas distintas." },
        ["MpStatsDecksNote"] = new() { [LangEn] = "Cards people bring in their deck, not the ones they send in a match: no recording keeps that. The number in the corner is the size of the shipment.", [LangEs] = "Cartas que la gente lleva en el mazo, no las que envía en partida: ninguna grabación guarda eso. El número de la esquina es la cantidad del envío." },
        ["MpStatsTailDecksOne"] = new() { [LangEn] = "{0} more card in 1 or 2 decks: too few to say the community prefers it.", [LangEs] = "{0} carta más en 1 o 2 mazos: muy pocos para decir que la comunidad la prefiere." },
        ["MpStatsTailDecksOpen"] = new() { [LangEn] = "Also showing the {0} cards that go in 1 or 2 decks.", [LangEs] = "Mostrando también las {0} cartas que van en 1 o 2 mazos." },
        ["MpStatsTailDecksNone"] = new() { [LangEn] = "Every card in {0} decks or more is above.", [LangEs] = "Todas las cartas con {0} mazos o más están arriba." },
        ["MpStatsTailCivsOne"] = new() { [LangEn] = "{0} more civilization with {1} matches or fewer: too few to say anything about it.", [LangEs] = "{0} civilización más con {1} partidas o menos: muy pocas para decir nada de ella." },
        ["MpStatsTailCivsOpenOne"] = new() { [LangEn] = "Also showing the {0} with {1} matches or fewer.", [LangEs] = "Mostrando también la {0} con {1} partidas o menos." },
        ["MpProfileTier"] = new() { [LangEn] = "{0} · {1} · #{2} of {3}", [LangEs] = "{0} · {1} · #{2} de {3}" },
        ["MpProfileTierInactive"] = new() { [LangEn] = "{0} · {1} · inactive", [LangEs] = "{0} · {1} · inactivo" },
        ["MpProfileSectionCivs"] = new() { [LangEn] = "Civilizations", [LangEs] = "Civilizaciones" },
        ["MpProfileFactMatches"] = new() { [LangEn] = "Matches", [LangEs] = "Partidas" },
        ["MpProfileFactDecided"] = new() { [LangEn] = "{0} decided", [LangEs] = "{0} decididas" },
        ["MpProfileFactMap"] = new() { [LangEn] = "{0} matches", [LangEs] = "{0} partidas" },
        ["MpProfileFactMapOne"] = new() { [LangEn] = "{0} match", [LangEs] = "{0} partida" },
        ["MpProfileFactRival"] = new() { [LangEn] = "Usual rival", [LangEs] = "Rival habitual" },
        ["MpProfileFactRecord"] = new() { [LangEn] = "{0}–{1} in {2}", [LangEs] = "{0}–{1} en {2}" },
        ["MpProfileCurveSub"] = new() { [LangEn] = "Last {0} rated matches", [LangEs] = "Últimas {0} partidas puntuadas" },
        ["MpProfileCurveChangeTip"] = new() { [LangEn] = "From {0} to {1} in these matches", [LangEs] = "De {0} a {1} en estas partidas" },
        ["MpProfileCurveToday"] = new() { [LangEn] = "today", [LangEs] = "hoy" },
        ["MpProfileCurveMax"] = new() { [LangEn] = "Peak {0}", [LangEs] = "Máx. {0}" },
        ["MpProfileCurveMin"] = new() { [LangEn] = "Low {0}", [LangEs] = "Mín. {0}" },
        ["MpProfileCurveTeamWait"] = new() { [LangEn] = "Your Teams ELO shows once placement is over.", [LangEs] = "Tu ELO de Equipos se muestra cuando termines el posicionamiento." },
        ["MpProfileCivsRules"] = new() { [LangEn] = "Sorted by matches played. The percentage only appears with {0} or more decided matches; with fewer, just the record.", [LangEs] = "Ordenadas por partidas jugadas. El porcentaje solo sale con {0} o más partidas decididas; con menos, solo el récord." },
        ["MpProfileCivsNoCiv"] = new() { [LangEn] = "{0} matches with no civilization recorded aren't counted.", [LangEs] = "{0} partidas sin civilización registrada no se cuentan." },
        ["MpProfileCivsNoCivOne"] = new() { [LangEn] = "{0} match with no civilization recorded isn't counted.", [LangEs] = "{0} partida sin civilización registrada no se cuenta." },
        ["MpProfileStreakNow"] = new() { [LangEn] = "CURRENT STREAK", [LangEs] = "RACHA ACTUAL" },
        ["MpProfileStreakLong"] = new() { [LangEn] = "LONGEST STREAK", [LangEs] = "RACHA MÁS LARGA" },
        ["MpProfileStreakWins"] = new() { [LangEn] = "wins", [LangEs] = "victorias" },
        ["MpProfileStreakWinsOne"] = new() { [LangEn] = "win", [LangEs] = "victoria" },
        ["MpProfileStreakSince"] = new() { [LangEn] = "since {0}", [LangEs] = "desde el {0}" },
        ["MpProfileStreakBestWins"] = new() { [LangEn] = "Wins", [LangEs] = "Victorias" },
        ["MpProfileStreakBestLosses"] = new() { [LangEn] = "Losses", [LangEs] = "Derrotas" },
        ["MpProfileStreakPipsTip"] = new() { [LangEn] = "Your last rated matches, oldest to newest", [LangEs] = "Tus últimas partidas puntuadas, de la más antigua a la más reciente" },
        ["MpProfilePlacementBox"] = new() { [LangEn] = "PLACEMENT", [LangEs] = "POSICIONAMIENTO" },
        ["MpProfilePlacementLeftMode"] = new() { [LangEn] = "{0} matches left to have an ELO in {1}.", [LangEs] = "Te quedan {0} partidas para tener ELO en {1}." },
        ["MpProfilePlacementLeftOneMode"] = new() { [LangEn] = "1 match left to have an ELO in {0}.", [LangEs] = "Te queda 1 partida para tener ELO en {0}." },
        ["MpProfileProvisional"] = new() { [LangEn] = "Provisional ELO until placement is over", [LangEs] = "ELO provisional hasta que termines el posicionamiento" },
        ["MpPlacementWinShort"] = new() { [LangEn] = "W", [LangEs] = "V" },
        ["MpPlacementLossShort"] = new() { [LangEn] = "L", [LangEs] = "D" },
        ["MpHistorySideYou"] = new() { [LangEn] = "YOU", [LangEs] = "TÚ" },
        ["MpHistorySideRival"] = new() { [LangEn] = "RIVAL", [LangEs] = "RIVAL" },
        ["MpHistorySideYourTeam"] = new() { [LangEn] = "YOUR TEAM", [LangEs] = "TU EQUIPO" },
        ["MpHistorySideRivals"] = new() { [LangEn] = "RIVALS", [LangEs] = "RIVALES" },
        ["MpHistoryYouTag"] = new() { [LangEn] = " · you", [LangEs] = " · tú" },
        ["MpHistoryShowPlayers"] = new() { [LangEn] = "Show players ▾", [LangEs] = "Ver jugadores ▾" },
        ["MpHistoryHidePlayers"] = new() { [LangEn] = "Hide players ▴", [LangEs] = "Ocultar jugadores ▴" },
        ["MpProfileDeckNoName"] = new() { [LangEn] = "No name", [LangEs] = "Sin nombre" },
        ["MpProfileDeckCity"] = new() { [LangEn] = "City: {0} · {1}", [LangEs] = "Ciudad: {0} · {1}" },
        ["MpProfileDeckAge"] = new() { [LangEn] = "AGE {0}", [LangEs] = "EDAD {0}" },
        ["MpProfileDeckAgeUnknown"] = new() { [LangEn] = "OTHER CARDS", [LangEs] = "OTRAS CARTAS" },
        ["MpProfileDeckCommunity"] = new() { [LangEn] = "THE COMMUNITY ALSO BRINGS", [LangEs] = "LA COMUNIDAD TAMBIÉN LLEVA" },
        ["MpProfileDeckCompared"] = new() { [LangEn] = "Compared with the {0} {1} decks the community shared (Statistics › Decks). The percentage is the share of those decks carrying the card; cards in {2} or fewer have none.", [LangEs] = "Comparado con los {0} mazos de {1} que compartió la comunidad (Estadísticas › Mazos). El porcentaje es la parte de esos mazos que lleva la carta; las que llevan {2} o menos no lo tienen." },
        ["MpProfileDeckCardTip"] = new() { [LangEn] = "{0}% of the {1} decks the community shared carry it", [LangEs] = "La llevan el {0} % de los mazos de {1} que compartió la comunidad" },
        ["ModPropTitlePrefix"] = new() { [LangEn] = "Settings for", [LangEs] = "Ajustes de" },
        ["ModPropStateSection"] = new() { [LangEn] = "MOD STATUS", [LangEs] = "ESTADO DEL MOD" },
        ["ModPropCopyChip"] = new() { [LangEn] = "Copy \"{0}\"", [LangEs] = "Copia «{0}»" },
        ["ModPropFilesRowTitle"] = new() { [LangEn] = "Mod files", [LangEs] = "Archivos del mod" },
        ["ModPropFilesRowHint"] = new() { [LangEn] = "Checks every file without downloading anything. If it keeps failing, use \"Share diagnostics\" in the menu.", [LangEs] = "Comprueba cada archivo sin descargar nada. Si sigue fallando, usa «Compartir diagnóstico» en el menú." },
        ["ModPropRailDiagBody"] = new() { [LangEn] = "Mod failing or won't open? Create a report for support.", [LangEs] = "¿El mod falla o no abre? Crea un informe para el soporte." },
        ["ModPropRailCreateReport"] = new() { [LangEn] = "Create report", [LangEs] = "Crear informe" },
        ["ModPropRailLogs"] = new() { [LangEn] = "Logs", [LangEs] = "Registros" },
        ["ModPropRailDiscord"] = new() { [LangEn] = "Discord ↗", [LangEs] = "Discord ↗" },
        ["DlgAntivirusCopyRow"] = new() { [LangEn] = "Copy", [LangEs] = "Copiar" },
        ["DlgAntivirusOpenShort"] = new() { [LangEn] = "Open ↗", [LangEs] = "Abrir ↗" },
        ["DlgAntivirusOtherAv"] = new() { [LangEn] = "Using another antivirus? Add the same folders to its exclusions.", [LangEs] = "¿Usas otro antivirus? Añade las mismas carpetas en sus exclusiones." },
        ["DlgLauncherUpdateHeadIdle"] = new()
        {
            [LangEn] = "There's a new launcher version",
            [LangEs] = "Hay una versión nueva del launcher",
        },
        ["DlgLauncherUpdateHeadDown"] = new()
        {
            [LangEn] = "Downloading the new version",
            [LangEs] = "Descargando la versión nueva",
        },
        ["DlgLauncherUpdateHeadVerify"] = new()
        {
            [LangEn] = "Checking the download",
            [LangEs] = "Comprobando la descarga",
        },
        ["DlgLauncherUpdateHeadReady"] = new()
        {
            [LangEn] = "Ready to install",
            [LangEs] = "Lista para instalar",
        },
        ["DlgLauncherUpdateReadyBody"] = new()
        {
            [LangEn] = "Downloaded and checked. When you restart, the launcher closes and reopens on {0}.",
            [LangEs] = "Descargada y comprobada. Al reiniciar, el launcher se cierra y se vuelve a abrir con la {0}.",
        },
        ["DlgLauncherUpdateVerifying"] = new()
        {
            [LangEn] = "Checking that the file is the original…",
            [LangEs] = "Comprobando que el archivo sea el original…",
        },
        ["DlgLauncherUpdateBytes"] = new()
        {
            [LangEn] = "{0} of {1}",
            [LangEs] = "{0} de {1}",
        },
        ["DlgLauncherUpdateEtaMin"] = new()
        {
            [LangEn] = "{0} min {1} s left",
            [LangEs] = "quedan {0} min {1} s",
        },
        ["DlgLauncherUpdateEtaSec"] = new()
        {
            [LangEn] = "{0} s left",
            [LangEs] = "quedan {0} s",
        },
        ["DlgLauncherUpdateBtnRetry"] = new()
        {
            [LangEn] = "Retry",
            [LangEs] = "Reintentar",
        },
        ["DlgLauncherUpdateSeeOnGitHub"] = new()
        {
            [LangEn] = "View on GitHub ↗",
            [LangEs] = "Ver en GitHub ↗",
        },
        ["DlgLauncherUpdateNotesElsewhere"] = new()
        {
            [LangEn] = "They're on this version's page, on GitHub.",
            [LangEs] = "Están en la página de esta versión, en GitHub.",
        },
        ["DlgLauncherUpdateSeeNotes"] = new()
        {
            [LangEn] = "See what's new ↗",
            [LangEs] = "Ver las novedades ↗",
        },
        ["DlgLauncherUpdateLowSpace"] = new()
        {
            [LangEn] = "It needs about *`{0}`* free on {2} and you only have *`{1}`*. You can try anyway.",
            [LangEs] = "Necesita unos *`{0}`* libres en {2} y solo tienes *`{1}`*. Puedes intentarlo igual.",
        },
        ["DlgLauncherUpdateInMatch"] = new()
        {
            [LangEn] = "You're in a match. Restart when it ends: the launcher has to stay open to send the result.",
            [LangEs] = "Estás en una partida. Reinicia cuando termine: el launcher tiene que seguir abierto para enviar el resultado.",
        },
        ["DlgLauncherUpdateModBusy"] = new()
        {
            [LangEn] = "Restarting will stop the download of *{0}*.",
            [LangEs] = "Al reiniciar se detendrá la descarga de *{0}*.",
        },
        ["LauncherUpdatePillReady"] = new()
        {
            [LangEn] = "Restart to update",
            [LangEs] = "Reiniciar para actualizar",
        },
        ["LauncherUpdatePillReadyTooltip"] = new()
        {
            [LangEn] = "The new version is downloaded and checked. Click to restart.",
            [LangEs] = "La versión nueva ya está descargada y comprobada. Haz clic para reiniciar.",
        },
        ["DlgInstallWindowTitle"] = new()
        {
            [LangEn] = "Install {0}",
            [LangEs] = "Instalar {0}",
        },
        ["DlgInstallHeadline"] = new()
        {
            [LangEn] = "Where do you want to install {0}?",
            [LangEs] = "¿Dónde quieres instalar {0}?",
        },
        ["DlgInstallHeadlineOverlay"] = new()
        {
            [LangEn] = "Where is your AoE3?",
            [LangEs] = "¿Dónde está tu AoE3?",
        },
        ["DlgInstallDesc"] = new()
        {
            [LangEn] = "It installs in a folder of its own, as a copy of your AoE3. Your original game isn't touched.",
            [LangEs] = "Se instala en una carpeta aparte, como copia de tu AoE3. Tu juego original no se toca.",
        },
        ["DlgInstallDescOverlay"] = new()
        {
            [LangEn] = "Its files are added to your AoE3 install.",
            [LangEs] = "Tus archivos se añaden a tu instalación de AoE3.",
        },
        ["DlgInstallYourAoe3"] = new()
        {
            [LangEn] = "Your AoE3",
            [LangEs] = "Tu AoE3",
        },
        ["DlgInstallModFolder"] = new()
        {
            [LangEn] = "Mod folder",
            [LangEs] = "Carpeta del mod",
        },
        ["DlgInstallOverlayFolder"] = new()
        {
            [LangEn] = "Your AoE3 folder",
            [LangEs] = "Carpeta de tu AoE3",
        },
        ["DlgInstallPickAoe3First"] = new()
        {
            [LangEn] = "Choose your AoE3 first",
            [LangEs] = "Elige primero tu AoE3",
        },
        ["DlgInstallPickFolder"] = new()
        {
            [LangEn] = "Choose where to install the mod.",
            [LangEs] = "Elige dónde instalar el mod.",
        },
        ["DlgInstallNoRoom"] = new()
        {
            [LangEn] = "Not enough space on {0}.",
            [LangEs] = "No hay espacio suficiente en {0}.",
        },
        ["DlgInstallSpaceShort"] = new()
        {
            [LangEn] = "It needs about *`{0}`* and you only have *`{1}`* free on {2}. Choose another disk.",
            [LangEs] = "Necesita unos *`{0}`* y solo tienes *`{1}`* libres en {2}. Elige otro disco.",
        },
        ["RadAsstStep1Body"] = new()
        {
            [LangEn] = "It's free and makes the private network the matches are played on. Windows will ask for permission to install it.",
            [LangEs] = "Es gratis y crea la red privada por la que se juegan las partidas. Windows te pedirá permiso para instalarlo.",
        },
        ["RadAsstStep2Body"] = new()
        {
            [LangEn] = "If it's your first time, finish creating your account in Radmin.",
            [LangEs] = "Si es la primera vez, termina de crear tu cuenta en Radmin.",
        },
        ["RadAsstStep3Body"] = new()
        {
            [LangEn] = "In the Radmin window press *Join a network* and paste this name.",
            [LangEs] = "En la ventana de Radmin pulsa *Unirse a una red* y pega este nombre.",
        },
        ["RadAsstStep1Web"] = new()
        {
            [LangEn] = "or download it from its website ↗",
            [LangEs] = "o descárgalo desde su web ↗",
        },
        ["RadAsstOpenRadmin"] = new()
        {
            [LangEn] = "Open Radmin",
            [LangEs] = "Abrir Radmin",
        },
        ["RadAsstDownloading"] = new()
        {
            [LangEn] = "Downloading Radmin VPN…",
            [LangEs] = "Descargando Radmin VPN…",
        },
        ["RadAsstSettingUp"] = new()
        {
            [LangEn] = "Installing… accept Windows' prompt.",
            [LangEs] = "Instalando… acepta el permiso de Windows.",
        },
        ["RadAsstWaitingInstall"] = new()
        {
            [LangEn] = "When it finishes installing, this ticks itself.",
            [LangEs] = "Cuando termine de instalarse, esto se marca solo.",
        },
        ["RadAsstWaitingLogin"] = new()
        {
            [LangEn] = "Waiting for you to sign in. This ticks itself.",
            [LangEs] = "Esperando a que inicies sesión. Esto se marca solo.",
        },
        ["RadAsstProgressDone"] = new()
        {
            [LangEn] = "Done",
            [LangEs] = "Completado",
        },
        ["PublishFieldInstallTypeLabel"] = new()
        {
            [LangEn] = "Install type",
            [LangEs] = "Tipo de instalación",
        },
        ["PublishRailNoName"] = new()
        {
            [LangEn] = "Unnamed mod",
            [LangEs] = "Mod sin nombre",
        },
        ["PublishRailNoId"] = new()
        {
            [LangEn] = "no id yet",
            [LangEs] = "sin id todavía",
        },
        ["PublishGuide"] = new()
        {
            [LangEn] = "Guide ↗",
            [LangEs] = "Guía ↗",
        },
        ["PublishFoldShow"] = new()
        {
            [LangEn] = "Show",
            [LangEs] = "Mostrar",
        },
        ["PublishFoldHide"] = new()
        {
            [LangEn] = "Hide",
            [LangEs] = "Ocultar",
        },
        ["PublishFoldFilled"] = new()
        {
            [LangEn] = "Filled in.",
            [LangEs] = "Rellenado.",
        },
        ["PublishTrSummary"] = new()
        {
            [LangEn] = "Only if there's a translations repository for your mod.",
            [LangEs] = "Solo si hay un repositorio de traducciones de tu mod.",
        },
        ["PublishGrpAdvanced"] = new()
        {
            [LangEn] = "ADVANCED",
            [LangEs] = "AVANZADO",
        },
        ["PublishGrpGhAdvanced"] = new()
        {
            [LangEn] = "GITHUB · ADVANCED",
            [LangEs] = "GITHUB · AVANZADO",
        },
        ["PublishGrpDescription"] = new()
        {
            [LangEn] = "DESCRIPTION",
            [LangEs] = "DESCRIPCIÓN",
        },
        ["PublishGrpLinks"] = new()
        {
            [LangEn] = "LINKS",
            [LangEs] = "ENLACES",
        },
        ["PublishCheckLabel"] = new()
        {
            [LangEn] = "Check on GitHub",
            [LangEs] = "Comprobar en GitHub",
        },
        ["PublishCheckHint"] = new()
        {
            [LangEn] = "Sees whether that release exists and carries the mod's .zip.",
            [LangEs] = "Mira si existe esa release y si trae el .zip del mod.",
        },
        ["PublishCheckButton"] = new()
        {
            [LangEn] = "Check",
            [LangEs] = "Comprobar",
        },
        ["PublishCheckBusy"] = new()
        {
            [LangEn] = "Checking…",
            [LangEs] = "Comprobando…",
        },
        ["PublishCheckOk"] = new()
        {
            [LangEn] = "Release {0} exists and carries {1}.",
            [LangEs] = "La release {0} existe y trae {1}.",
        },
        ["PublishCheckNoZip"] = new()
        {
            [LangEn] = "Release {0} exists, but it carries no .zip. Upload it to the release: without it the mod can't be installed.",
            [LangEs] = "La release {0} existe, pero no trae ningún .zip. Súbelo a la release: sin él no se puede instalar.",
        },
        ["PublishCheckNoRelease"] = new()
        {
            [LangEn] = "There is no release tagged {0} in {1}.",
            [LangEs] = "No hay ninguna release con la etiqueta {0} en {1}.",
        },
        ["PublishCheckUnreachable"] = new()
        {
            [LangEn] = "Couldn't ask GitHub. Try again in a moment.",
            [LangEs] = "No se pudo preguntar a GitHub. Vuelve a intentarlo en un momento.",
        },
        ["PublishMissingZip"] = new()
        {
            [LangEn] = "Release with no .zip",
            [LangEs] = "Release sin .zip",
        },
        ["PublishMissingRelease"] = new()
        {
            [LangEn] = "Tag with no release",
            [LangEs] = "Etiqueta sin release",
        },
        ["PublishMissingPill"] = new()
        {
            [LangEn] = "Step {0} · {1}",
            [LangEs] = "Paso {0} · {1}",
        },
        ["PublishMissingNoneHint"] = new()
        {
            [LangEn] = "Check the mod.json and open the PR.",
            [LangEs] = "Revisa el mod.json y abre el PR.",
        },
        ["PublishBlockId"] = new()
        {
            [LangEn] = "A valid id is needed to create the mod's folder.",
            [LangEs] = "Falta un id válido para crear la carpeta del mod.",
        },
        ["PublishBlockName"] = new()
        {
            [LangEn] = "The display name is missing.",
            [LangEs] = "Falta el nombre visible.",
        },
        ["PublishCloseTitle"] = new()
        {
            [LangEn] = "Close without publishing?",
            [LangEs] = "¿Cerrar sin publicar?",
        },
        ["PublishCloseBody"] = new()
        {
            [LangEn] = "What you wrote in the wizard will be lost.",
            [LangEs] = "Se perderá lo que escribiste en el asistente.",
        },
        ["PublishCloseKeep"] = new()
        {
            [LangEn] = "Keep editing",
            [LangEs] = "Seguir editando",
        },
        ["PublishCloseConfirm"] = new()
        {
            [LangEn] = "Close",
            [LangEs] = "Cerrar",
        },
        ["PublishCopiedJson"] = new()
        {
            [LangEn] = "Copied ✓",
            [LangEs] = "Copiado ✓",
        },
        ["PublishAddLink"] = new()
        {
            [LangEn] = "+ Add link",
            [LangEs] = "+ Añadir enlace",
        },
        ["PublishLinkError"] = new()
        {
            [LangEn] = "It has to start with https://",
            [LangEs] = "Tiene que empezar por https://",
        },
        ["PublishLink_discord"] = new()
        {
            [LangEn] = "Discord",
            [LangEs] = "Discord",
        },
        ["PublishLink_moddb"] = new()
        {
            [LangEn] = "ModDB",
            [LangEs] = "ModDB",
        },
        ["PublishLink_forum"] = new()
        {
            [LangEn] = "Forum",
            [LangEs] = "Foro",
        },
        ["PublishLink_wiki"] = new()
        {
            [LangEn] = "Wiki",
            [LangEs] = "Wiki",
        },
        ["PublishLink_video"] = new()
        {
            [LangEn] = "Video",
            [LangEs] = "Vídeo",
        },
        ["PublishLink_other"] = new()
        {
            [LangEn] = "Other",
            [LangEs] = "Otro",
        },
        ["MpChipNoNetwork"] = new()
        {
            [LangEn] = "Network missing",
            [LangEs] = "Falta la red",
        },
        ["MpChipHeadConnected"] = new()
        {
            [LangEn] = "Connected to Radmin VPN",
            [LangEs] = "Conectado a Radmin VPN",
        },
        ["MpChipHeadNoNetwork"] = new()
        {
            [LangEn] = "Radmin is open, but you're not on the AoE3 network",
            [LangEs] = "Radmin está abierto, pero no estás en la red AoE3",
        },
        ["MpChipNoNetworkBody"] = new()
        {
            [LangEn] = "Join the AoE3 network to be able to play online.",
            [LangEs] = "Únete a la red AoE3 para poder jugar en línea.",
        },
        ["MpChipYourIp"] = new()
        {
            [LangEn] = "Your Radmin IP",
            [LangEs] = "Tu IP de Radmin",
        },
        ["MpChipCopy"] = new()
        {
            [LangEn] = "Copy",
            [LangEs] = "Copiar",
        },
        ["MpChipCopied"] = new()
        {
            [LangEn] = "✓ Copied",
            [LangEs] = "✓ Copiada",
        },
        ["MpAccountSignOutRoom"] = new()
        {
            [LangEn] = "You'll leave the room «{0}»",
            [LangEs] = "Saldrás de la sala «{0}»",
        },
        ["OfflineChipChecking"] = new()
        {
            [LangEn] = "Checking…",
            [LangEs] = "Comprobando…",
        },
        ["MpBadgeAccountOffline"] = new()
        {
            [LangEn] = "No connection: you can change it when the internet is back.",
            [LangEs] = "Sin conexión: podrás cambiarla cuando vuelva internet.",
        },
        ["MpBadgeAccountLocked"] = new()
        {
            [LangEn] = "Play a team match to unlock Teams.",
            [LangEs] = "Juega una partida en equipo para desbloquear «Equipos».",
        },
        ["NavSignIn"] = new()
        {
            [LangEn] = "Sign in",
            [LangEs] = "Iniciar sesión",
        },
        ["ModPropFilesIntro"] = new() { [LangEn] = "In each copy's ⋯ menu, “Remove from list” forgets it and keeps its files, and “Uninstall…” deletes it from the disk.", [LangEs] = "En el menú ⋯ de cada copia, «Quitar de la lista» la olvida y deja sus archivos, y «Desinstalar…» la borra del disco." },
        ["ModPropCopiesNote"] = new() { [LangEn] = "{0} copies · only the one in use opens with Play", [LangEs] = "{0} copias · solo la que está en uso se abre con Jugar" },
        ["ModPropCopiesNoteOne"] = new() { [LangEn] = "1 copy · only the one in use opens with Play", [LangEs] = "1 copia · solo la que está en uso se abre con Jugar" },
        ["ModPropCopyNewer"] = new() { [LangEn] = "“{0}” has {1}, newer than this one.", [LangEs] = "«{0}» tiene la {1}, más nueva que esta." },
        ["ModPropInstallCopyBtn"] = new() { [LangEn] = "Install…", [LangEs] = "Instalar…" },
        ["ModPropChooseFolderBtn"] = new() { [LangEn] = "Choose folder…", [LangEs] = "Elegir carpeta…" },
        ["ModPropSearchDisksBtn"] = new() { [LangEn] = "Search my drives…", [LangEs] = "Buscar en mis discos…" },
        ["ModPropMoreOptions"] = new() { [LangEn] = "More options", [LangEs] = "Más opciones" },
        ["BtnChangeEllipsis"] = new() { [LangEn] = "Change…", [LangEs] = "Cambiar…" },
        ["ModPropVerifyBtn"] = new() { [LangEn] = "Verify", [LangEs] = "Verificar" },
        ["ModPropUserDataPathTitle"] = new() { [LangEn] = "Data folder", [LangEs] = "Carpeta de datos" },
        ["ModPropRestoreNoneHint"] = new() { [LangEn] = "Create one and it will show up here.", [LangEs] = "Crea una y aparecerá aquí." },
        ["ModPropTranslationsChecked"] = new() { [LangEn] = "Checked just now · no new translations", [LangEs] = "Comprobado ahora · no hay traducciones nuevas" },
        ["LangChipCompatible"] = new() { [LangEn] = "COMPATIBLE", [LangEs] = "COMPATIBLE" },
        ["LangInUse"] = new() { [LangEn] = "✓  In use", [LangEs] = "✓  En uso" },
        ["AddonsCatalogInCopy"] = new() { [LangEn] = "In the copy “{0}”", [LangEs] = "En la copia «{0}»" },
        ["ModPropDecksEmptyHint"] = new() { [LangEn] = "They will show up here when you create a home city with this mod and open the game.", [LangEs] = "Aparecerán aquí cuando crees una metrópoli con este mod y abras el juego." },
        ["ModPropDeckCityLabel"] = new() { [LangEn] = "HOME CITY", [LangEs] = "METRÓPOLI" },
        ["ModPropDeckGroupLabel"] = new() { [LangEn] = "DECKS", [LangEs] = "BARAJAS" },
        ["ModPropDeckCityFacts"] = new() { [LangEn] = "Civilization according to the file: {0} · level {1}", [LangEs] = "Civilización según el archivo: {0} · nivel {1}" },
        ["ModPropDeckCityCiv"] = new() { [LangEn] = "Civilization according to the file: {0}", [LangEs] = "Civilización según el archivo: {0}" },
        ["ModPropDeckCount"] = new() { [LangEn] = "{0} decks", [LangEs] = "{0} barajas" },
        ["ModPropDeckCountOne"] = new() { [LangEn] = "1 deck", [LangEs] = "1 baraja" },
        ["ModPropDeckCardOf"] = new() { [LangEn] = "CARD {0} OF {1}", [LangEs] = "CARTA {0} DE {1}" },
        ["ModPropDecksLevel"] = new() { [LangEn] = "level {0}", [LangEs] = "nivel {0}" },
        ["ModStatsPeopleNote"] = new() { [LangEn] = "From your recordings, LAN games and skirmishes included", [LangEs] = "De tus grabaciones, también las de LAN y escaramuzas" },
        ["ModStatsAiNote"] = new() { [LangEn] = "From the game's own match log", [LangEs] = "Del registro de partidas del juego" },
        ["ModStatsEmptyTitle"] = new() { [LangEn] = "No games yet", [LangEs] = "Todavía no hay partidas" },
        ["ModStatsEmptyHint"] = new() { [LangEn] = "They will show up here when you play with this mod, against people or the AI.", [LangEs] = "Aparecerán aquí cuando juegues con este mod, contra personas o contra la IA." },
        ["ModStatsAgainst"] = new() { [LangEn] = "Against {0}", [LangEs] = "Contra {0}" },
        ["ModStatsAgainstAi"] = new() { [LangEn] = "Against the AI", [LangEs] = "Contra la IA" },
        ["ModStatsScore"] = new() { [LangEn] = "score {0}", [LangEs] = "puntuación {0}" },
        ["ModStatsNoScore"] = new() { [LangEn] = "score not recorded", [LangEs] = "puntuación no registrada" },
        ["ModStatsFirstAttack"] = new() { [LangEn] = "first attack at {0} min", [LangEs] = "primer ataque a los {0} min" },
        ["ModStatsGold"] = new() { [LangEn] = "Gold {0}", [LangEs] = "Oro {0}" },
        ["ModStatsWood"] = new() { [LangEn] = "Wood {0}", [LangEs] = "Madera {0}" },
        ["ModStatsFood"] = new() { [LangEn] = "Food {0}", [LangEs] = "Comida {0}" },
        ["ModStatsNoTotals"] = new() { [LangEn] = "Resources not recorded by the game.", [LangEs] = "Recursos no registrados por el juego." },
        ["ModChipWin"] = new() { [LangEn] = "VICTORY", [LangEs] = "VICTORIA" },
        ["ModChipLoss"] = new() { [LangEn] = "DEFEAT", [LangEs] = "DERROTA" },
        ["ModChipNoResult"] = new() { [LangEn] = "NO RESULT", [LangEs] = "SIN RESULTADO" },
        ["ModStatsPeopleLabel"] = new() { [LangEn] = "AGAINST PEOPLE", [LangEs] = "CONTRA PERSONAS" },
        ["ModStatsAiLabel"] = new() { [LangEn] = "AGAINST THE AI", [LangEs] = "CONTRA LA IA" },
        ["DlgSettingsModsOpen"] = new() { [LangEn] = "Mod settings…", [LangEs] = "Ajustes del mod…" },
        ["DlgSettingsModsUpdateChip"] = new() { [LangEn] = "UPDATE AVAILABLE", [LangEs] = "HAY ACTUALIZACIÓN" },
        ["DlgSettingsModsFingerprintOk"] = new() { [LangEn] = "FINGERPRINT OK", [LangEs] = "HUELLA OK" },
        ["DlgSettingsModsFingerprintDifferent"] = new() { [LangEn] = "DIFFERENT FINGERPRINT", [LangEs] = "HUELLA DISTINTA" },
        ["DlgSettingsModsFingerprintHint"] = new() { [LangEn] = "Some files were changed by hand. You can repair them in Mod settings › Files.", [LangEs] = "Hay archivos tocados a mano. Puedes repararlos en Ajustes del mod › Archivos." },
        ["DlgSettingsLauncherUpdateTitle"] = new() { [LangEn] = "A new launcher version is available: {0}", [LangEs] = "Hay una versión nueva del launcher: {0}" },
        ["DlgSettingsLauncherUpdateHint"] = new() { [LangEn] = "It installs when you restart the launcher.", [LangEs] = "Se instala al reiniciar el launcher." },
        ["DlgSettingsLauncherUpdateNotes"] = new() { [LangEn] = "See changes", [LangEs] = "Ver cambios" },
        ["DlgSettingsLauncherUpdateRestart"] = new() { [LangEn] = "Restart and update", [LangEs] = "Reiniciar y actualizar" },
        ["MpProfileRatingLoading"] = new()
        {
            [LangEn] = "Loading your ranking…",
            [LangEs] = "Cargando tu clasificación…",
        },
        ["MpProfileRatingFailed"] = new()
        {
            [LangEn] = "Your ranking couldn't be loaded.",
            [LangEs] = "No se pudo cargar tu clasificación.",
        },
        ["MpProfileRatingFailedTitle"] = new()
        {
            [LangEn] = "Your ranking couldn't be loaded",
            [LangEs] = "No se pudo cargar tu clasificación",
        },
        ["MpProfileRatingFailedBody"] = new()
        {
            [LangEn] = "The server didn't answer. Your decks are still in their section: they're read from your PC.",
            [LangEs] = "El servidor no respondió. Tus mazos siguen disponibles en su sección: se leen de tu PC.",
        },
        ["MpHistoryLoadFailed"] = new()
        {
            [LangEn] = "Your matches couldn't be loaded. The server didn't answer.",
            [LangEs] = "No se pudieron cargar tus partidas. El servidor no respondió.",
        },
        ["LibMenuPlay"] = new() { [LangEn] = "Play", [LangEs] = "Jugar" },
        ["LibMenuRetry"] = new() { [LangEn] = "Retry download", [LangEs] = "Reintentar descarga" },
        ["LibMenuPause"] = new() { [LangEn] = "Pause download", [LangEs] = "Pausar descarga" },
        ["LibMenuResume"] = new() { [LangEn] = "Resume download", [LangEs] = "Reanudar descarga" },
        ["LibMenuCancel"] = new() { [LangEn] = "Cancel download", [LangEs] = "Cancelar descarga" },
        ["LibMenuFolder"] = new() { [LangEn] = "Open the mod's folder", [LangEs] = "Abrir la carpeta del mod" },
        ["LibMenuPage"] = new() { [LangEn] = "Open the mod's page", [LangEs] = "Abrir la página del mod" },
        ["LibMenuRemove"] = new() { [LangEn] = "Remove from my mods", [LangEs] = "Quitar de mis mods" },
        ["LibMenuUninstall"] = new() { [LangEn] = "Uninstall…", [LangEs] = "Desinstalar…" },
        ["LibMenuUninstallCopy"] = new() { [LangEn] = "Uninstall the “{0}” copy…", [LangEs] = "Desinstalar la copia «{0}»…" },
    };
}
