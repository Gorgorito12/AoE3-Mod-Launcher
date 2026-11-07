using System.Collections.Generic;

namespace WarsOfLibertyLauncher.Localization;

/// <summary>
/// The small dialogs and menus (design 103). Merged into the one table by the static constructor
/// in <c>Strings.Rating.cs</c>, where a duplicate key throws. Spanish is es-419 with tuteo.
/// </summary>
public static partial class Strings
{
    private static readonly Dictionary<string, Dictionary<string, string>> SmallDialogsTable = new()
    {
        ["DlgUnderstood"] = new() { [LangEn] = "Got it", [LangEs] = "Entendido" },
        ["DlgYes"] = new() { [LangEn] = "Yes", [LangEs] = "Sí" },
        ["DlgNo"] = new() { [LangEn] = "No", [LangEs] = "No" },
        ["DlgChooseFolder"] = new() { [LangEn] = "Choose folder", [LangEs] = "Elegir carpeta" },
        ["DlgGameRunningClose"] = new() { [LangEn] = "Close the game", [LangEs] = "Cerrar el juego" },
        ["DlgGameRunningKeep"] = new() { [LangEn] = "Continue without closing", [LangEs] = "Continuar sin cerrar" },
        ["ModsBrowserAddLocalInvalidLead"] = new()
        {
            [LangEn] = "The mod was not added. Fix what is below and choose the file again.",
            [LangEs] = "El mod no se agregó. Corrige lo de abajo y vuelve a elegir el archivo.",
        },
        ["LibMenuCheckUpdate"] = new() { [LangEn] = "Check for updates", [LangEs] = "Buscar actualización" },
        // {0} how many.
        ["MpChipOnlinePlayers"] = new() { [LangEn] = "{0} players online", [LangEs] = "{0} jugadores conectados" },
        ["DlgWhatFailed"] = new() { [LangEn] = "WHAT FAILED", [LangEs] = "QUÉ FALLÓ" },

        ["DlgBackgroundConsentBar"] = new() { [LangEn] = "Run in the background", [LangEs] = "Seguir en segundo plano" },
        ["DlgCompatLayerBar"] = new() { [LangEn] = "Compatibility layer", [LangEs] = "Capa de compatibilidad" },
        ["DlgCrossUserBar"] = new() { [LangEn] = "Another Windows account", [LangEs] = "Otra cuenta de Windows" },
        // {0} the mod.
        ["DlgRemoveModHeadline"] = new() { [LangEn] = "Remove {0} from your collection?", [LangEs] = "¿Quitar {0} de tu colección?" },
        ["StartupUpdateAmount"] = new() { [LangEn] = "{0} of {1}", [LangEs] = "{0} de {1}" },
        ["MpTeamsCreateTagHint"] = new() { [LangEn] = "The tag shows in the bracket when the name does not fit.", [LangEs] = "La etiqueta sale en el cuadro cuando no cabe el nombre." },

        // {0} the version.
        ["StartupUpdateHeadline"] = new() { [LangEn] = "Downloading version {0}", [LangEs] = "Descargando la versión {0}" },
        ["StartupUpdateFailedTitle"] = new() { [LangEn] = "The update could not be downloaded", [LangEs] = "No se pudo descargar la actualización" },
        ["StartupUpdateFailedBody"] = new()
        {
            [LangEn] = "You can try again or keep this version. The launcher will offer the update again when it opens.",
            [LangEs] = "Puedes reintentarlo o seguir con esta versión. El launcher te la volverá a ofrecer al abrirse.",
        },
        ["StartupUpdateRetry"] = new() { [LangEn] = "Try again", [LangEs] = "Reintentar" },

        ["DlgRestoreFailedLead"] = new()
        {
            [LangEn] = "Your current data was not changed.",
            [LangEs] = "Tus datos actuales no cambiaron.",
        },

        // {0} the host.
        ["MpJoinPasswordHeadline"] = new() { [LangEn] = "{0}'s room has a password", [LangEs] = "La sala de {0} tiene contraseña" },
        ["MpJoinPasswordField"] = new() { [LangEn] = "PASSWORD", [LangEs] = "CONTRASEÑA" },
        ["MpJoinPasswordPlaceholder"] = new() { [LangEn] = "Type it here", [LangEs] = "Escríbela aquí" },
        ["MpRenameHeadline"] = new() { [LangEn] = "Room name", [LangEs] = "Nombre de la sala" },
        ["MpTeamsCreateHeadline"] = new() { [LangEn] = "Name your team", [LangEs] = "Ponle nombre a tu equipo" },
        ["MpTeamsCreateFieldLabel"] = new() { [LangEn] = "NAME · TAG (OPTIONAL, UP TO 8)", [LangEs] = "NOMBRE · ETIQUETA (OPCIONAL, HASTA 8)" },
        // {0} how many.
        ["MpPickInvite"] = new() { [LangEn] = "Invite {0}", [LangEs] = "Invitar a {0}" },

        ["PublishBrowserFailedTitle"] = new() { [LangEn] = "The browser could not be opened", [LangEs] = "No se pudo abrir el navegador" },
        ["PublishBrowserFailedLead"] = new()
        {
            [LangEn] = "Open the catalog page yourself and paste your manifest there.",
            [LangEs] = "Abre tú la página del catálogo y pega allí tu manifiesto.",
        },
    };
}
