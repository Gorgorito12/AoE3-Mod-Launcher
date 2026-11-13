using System.Collections.Generic;

namespace WarsOfLibertyLauncher.Localization;

/// <summary>
/// The quick match against the server's queue (Controls/MultiplayerTab.Matchmaking.cs): the
/// searchers listed as rows, the "rival found" window, and what is said when a match does not
/// happen. Merged into the one table by the static constructor in <c>Strings.Rating.cs</c>, where a
/// duplicate key throws. Spanish is es-419 with tuteo.
/// </summary>
public static partial class Strings
{
    private static readonly Dictionary<string, Dictionary<string, string>> MatchmakingTable = new()
    {
        // ---- The "rival found" window ----
        ["MmFoundBarTitle"] = new() { [LangEn] = "Quick match", [LangEs] = "Partida rápida" },
        ["MmFoundHeadline"] = new() { [LangEn] = "Rival found!", [LangEs] = "¡Rival encontrado!" },
        ["MmChallengeSentHeadline"] = new() { [LangEn] = "Waiting for {0} to accept", [LangEs] = "Esperando a que {0} acepte" },
        ["MmFoundKindRanked"] = new() { [LangEn] = "Rated 1v1 · {0}", [LangEs] = "1v1 clasificatoria · {0}" },
        ["MmFoundKindCasual"] = new() { [LangEn] = "Casual 1v1 · {0}", [LangEs] = "1v1 casual · {0}" },
        ["MmFoundYouHost"] = new() { [LangEn] = "You host: you create the game in AoE3 and {0} joins it.", [LangEs] = "Tú eres el anfitrión: creas la partida en AoE3 y {0} entra." },
        ["MmFoundTheyHost"] = new() { [LangEn] = "{0} hosts: they create the game in AoE3 and you join it.", [LangEs] = "{0} es el anfitrión: crea la partida en AoE3 y tú entras." },
        ["MmFoundPlacing"] = new() { [LangEn] = "In placement {0}/{1}", [LangEs] = "En posicionamiento {0}/{1}" },
        ["MmFoundDeclineNote"] = new() { [LangEn] = "Declining or letting the time run out counts as a strike: a few in a row pause your searches.", [LangEs] = "Rechazar o dejar que se acabe el tiempo cuenta como falta: varias seguidas pausan tus búsquedas." },
        ["MmAccept"] = new() { [LangEn] = "Accept", [LangEs] = "Aceptar" },
        ["MmDecline"] = new() { [LangEn] = "Decline", [LangEs] = "Rechazar" },
        ["MmWithdraw"] = new() { [LangEn] = "Withdraw", [LangEs] = "Retirarme" },
        ["MmWaitingFor"] = new() { [LangEn] = "Waiting for {0}…", [LangEs] = "Esperando a {0}…" },
        ["MmAcceptedOf"] = new() { [LangEn] = "{0} of {1} accepted", [LangEs] = "{0} de {1} aceptaron" },
        ["MmSecondsLeft"] = new() { [LangEn] = "{0} s", [LangEs] = "{0} s" },
        ["MmReadyForYou"] = new() { [LangEn] = "You both accepted: you're marked ready.", [LangEs] = "Los dos aceptaron: ya estás marcado como listo." },

        // ---- When it does not happen ----
        ["MmYouDeclined"] = new() { [LangEn] = "You declined the match.", [LangEs] = "Rechazaste la partida." },
        ["MmYouMissed"] = new() { [LangEn] = "You didn't accept in time, so the search stopped.", [LangEs] = "No aceptaste a tiempo, así que la búsqueda se detuvo." },
        ["MmStrikeWarning"] = new() { [LangEn] = "Doing it again locks the quick match for a few minutes.", [LangEs] = "Si vuelve a pasar, la partida rápida se bloquea unos minutos." },
        ["MmPenalty"] = new() { [LangEn] = "You declined or missed a match a moment ago. Try again in a few minutes.", [LangEs] = "Rechazaste o dejaste pasar una partida hace un momento. Inténtalo de nuevo en unos minutos." },
        ["MmPenaltyUntil"] = new() { [LangEn] = "You can search again at {0}.", [LangEs] = "Podrás buscar de nuevo a las {0}." },
        ["MmRivalDidNotAccept"] = new() { [LangEn] = "{0} didn't accept.", [LangEs] = "{0} no aceptó." },
        ["MmRivalDidNotAcceptRequeued"] = new() { [LangEn] = "{0} didn't accept. You're still searching, in the same place.", [LangEs] = "{0} no aceptó. Sigues buscando, en el mismo lugar." },
        ["MmRivalNeverEntered"] = new() { [LangEn] = "{0} never entered the room. Searching again…", [LangEs] = "{0} no entró a la sala. Buscando de nuevo…" },
        ["MmYouNeverEntered"] = new() { [LangEn] = "You didn't enter the room in time, so it was closed.", [LangEs] = "No entraste a la sala a tiempo, así que se cerró." },
        ["MmErrInRoom"] = new() { [LangEn] = "Leave your room first.", [LangEs] = "Primero sal de tu sala." },
        ["MmErrInGame"] = new() { [LangEn] = "You're in a match.", [LangEs] = "Estás en una partida." },
        ["MmErrOtherVersion"] = new() { [LangEn] = "You have another version of that mod, so you can't play together.", [LangEs] = "Tienes otra versión de ese mod, así que no pueden jugar juntos." },
        ["MmErrNotSearching"] = new() { [LangEn] = "That player isn't searching any more.", [LangEs] = "Ese jugador ya no está buscando." },
        ["MmErrBusy"] = new() { [LangEn] = "That player is being matched right now.", [LangEs] = "Ese jugador está siendo emparejado ahora mismo." },
        ["MmErrRateLimited"] = new() { [LangEn] = "Too many searches in a minute. Wait a moment.", [LangEs] = "Demasiadas búsquedas en un minuto. Espera un momento." },

        // ---- Searchers in the rooms list ----
        ["MmRowTitle"] = new() { [LangEn] = "Looking for a 1v1", [LangEs] = "Busca rival 1v1" },
        ["MmRowPlayersTip"] = new() { [LangEn] = "Searching alone: press Play and the match is his and yours.", [LangEs] = "Busca solo: pulsa Jugar y la partida es suya y tuya." },
        ["MmChipEither"] = new() { [LangEn] = "1v1 · any", [LangEs] = "1v1 · cualquiera" },
        ["MmChipEitherTip"] = new() { [LangEn] = "Plays rated or casual.", [LangEs] = "Juega clasificatoria o casual." },
        ["MmChipWaiting"] = new() { [LangEn] = "Searching · {0} min", [LangEs] = "Buscando · {0} min" },
        ["MmChipQuick"] = new() { [LangEn] = "Searching", [LangEs] = "Buscando" },
        ["MmChipQuickTip"] = new() { [LangEn] = "In the quick match queue: no room is opened until a rival is found.", [LangEs] = "En la cola de partida rápida: no se abre sala hasta encontrar rival." },
        ["MmChipYourSearch"] = new() { [LangEn] = "Your search", [LangEs] = "Tu búsqueda" },
        ["MmPlay"] = new() { [LangEn] = "Play", [LangEs] = "Jugar" },
        ["MmPlayTip"] = new() { [LangEn] = "Challenge {0}: they have 20 s to accept.", [LangEs] = "Retar a {0}: tiene 20 s para aceptar." },
        ["MmSomeoneSearchingTitle"] = new() { [LangEn] = "{0} is looking for a 1v1", [LangEs] = "{0} busca rival 1v1" },
        ["MmSomeoneSearchingRanked"] = new() { [LangEn] = "Rated · {0}", [LangEs] = "Clasificatoria · {0}" },
        ["MmSomeoneSearchingCasual"] = new() { [LangEn] = "Casual · {0}", [LangEs] = "Casual · {0}" },

        // ---- The quick match bar ----
        ["QcQueuedTitle"] = new() { [LangEn] = "Searching for a rival", [LangEs] = "Buscando rival" },
        ["QcQueuedLineAlone"] = new() { [LangEn] = "Nobody else is searching {0} ({1}) right now. You're listed in Rooms, and we'll let you know.", [LangEs] = "Nadie más busca {0} ({1}) ahora. Apareces en Salas y te avisamos." },
        ["QcQueuedLineOne"] = new() { [LangEn] = "{0} more player is searching {1} ({2}). Matching by rating…", [LangEs] = "{0} jugador más busca {1} ({2}). Emparejando por ELO…" },
        ["QcQueuedLineMany"] = new() { [LangEn] = "{0} more players are searching {1} ({2}). Matching by rating…", [LangEs] = "{0} jugadores más buscan {1} ({2}). Emparejando por ELO…" },
        ["QcWaitSearcher"] = new() { [LangEn] = "{0} is searching right now. Search and you'll be matched at once.", [LangEs] = "{0} está buscando ahora. Busca y te emparejamos al momento." },
        ["QcWaitSearcherMany"] = new() { [LangEn] = "{0} and {1} more are searching right now. Search and you'll be matched at once.", [LangEs] = "{0} y {1} más están buscando ahora. Busca y te emparejamos al momento." },

        // ---- Settings ----
        ["DlgSettingsNotifySearchers"] = new() { [LangEn] = "Tell me when someone looks for a 1v1", [LangEs] = "Avisarme cuando alguien busque 1v1" },
        ["DlgSettingsNotifySearchersHint"] = new() { [LangEn] = "Only in mods you have, with your version.", [LangEs] = "Solo en mods que tienes, con tu versión." },
        ["DlgSettingsNotifySearchersTip"] = new() { [LangEn] = "A card with a Play button when a player starts a quick match search. \"Do not disturb\" silences it.", [LangEs] = "Una tarjeta con el botón Jugar cuando un jugador empieza a buscar partida rápida. «No molestar» la silencia." },
    };
}
