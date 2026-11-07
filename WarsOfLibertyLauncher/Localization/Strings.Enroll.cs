using System.Collections.Generic;

namespace WarsOfLibertyLauncher.Localization;

/// <summary>
/// The enrol window (design 82). Merged into the one table by the static constructor in
/// <c>Strings.Rating.cs</c>, where a duplicate key throws. Spanish is es-419 with tuteo.
/// </summary>
public static partial class Strings
{
    private static readonly Dictionary<string, Dictionary<string, string>> EnrollTable = new()
    {
        ["EnTitle"] = new() { [LangEn] = "Enter tournament", [LangEs] = "Inscribirse" },
        ["EnMyEntry"] = new() { [LangEn] = "My entry", [LangEs] = "Mi inscripción" },

        ["EnMetaPlaces"] = new() { [LangEn] = "{0} places", [LangEs] = "{0} plazas" },
        ["EnMetaTeams"] = new() { [LangEn] = "{0} teams", [LangEs] = "{0} equipos" },
        ["EnMetaEntered"] = new() { [LangEn] = "{0} entered", [LangEs] = "{0} inscritos" },
        ["EnHowOpen"] = new() { [LangEn] = "first come, first served", [LangEs] = "por orden de llegada" },
        ["EnHowApproval"] = new() { [LangEn] = "with approval", [LangEs] = "con aprobación" },
        ["EnHowRegistered"] = new() { [LangEn] = "a captain enters a saved team", [LangEs] = "un capitán inscribe un equipo guardado" },
        ["EnHowAdhoc"] = new() { [LangEn] = "the captain picks the line-up", [LangEs] = "el capitán arma la alineación" },
        ["EnHowDraft"] = new() { [LangEn] = "everyone enters alone", [LangEs] = "cada uno se apunta solo" },

        ["EnFactFormat"] = new() { [LangEn] = "FORMAT", [LangEs] = "FORMATO" },
        ["EnFactPlaces"] = new() { [LangEn] = "PLACES", [LangEs] = "PLAZAS" },
        ["EnFactHow"] = new() { [LangEn] = "HOW YOU GET IN", [LangEs] = "CÓMO SE ENTRA" },
        ["EnFactChange"] = new() { [LangEn] = "IF YOU CHANGE YOUR MIND", [LangEs] = "SI CAMBIAS DE IDEA" },
        ["EnFactState"] = new() { [LangEn] = "STATUS", [LangEs] = "ESTADO" },
        ["EnFactNext"] = new() { [LangEn] = "WHAT COMES NEXT", [LangEs] = "CÓMO SIGUE" },
        ["EnFactLineup"] = new() { [LangEn] = "LINE-UP", [LangEs] = "ALINEACIÓN" },
        ["EnFactLeave"] = new() { [LangEn] = "IF YOU WANT OUT", [LangEs] = "SI QUIERES SALIR" },

        ["EnFormatSolo"] = new() { [LangEn] = "{0} · you play alone", [LangEs] = "{0} · juegas tú solo" },
        ["EnFormatDraft"] = new() { [LangEn] = "{0} · you enter alone and the organizer forms the teams", [LangEs] = "{0} · te apuntas solo y el organizador forma los equipos" },
        ["EnPlacesLeft"] = new() { [LangEn] = "{0} of {1} taken. {2} left.", [LangEs] = "{0} de {1} ocupadas. Quedan {2}." },
        ["EnPlacesLeftOne"] = new() { [LangEn] = "{0} of {1} taken. 1 left.", [LangEs] = "{0} de {1} ocupadas. Queda 1." },
        ["EnPlacesFull"] = new() { [LangEn] = "{0} of {1} taken. If you enter, you go on the waiting list.", [LangEs] = "{0} de {1} ocupadas. Si te inscribes, quedas en espera." },
        ["EnPlacesCount"] = new() { [LangEn] = "{0} entered.", [LangEs] = "{0} inscritos." },
        ["EnHowOpenLine"] = new() { [LangEn] = "First come, first served: you're in as soon as you enter.", [LangEs] = "Por orden de llegada: entras en cuanto te inscribes." },
        ["EnHowApprovalLine"] = new() { [LangEn] = "With approval: the organizer accepts each entry.", [LangEs] = "Con aprobación: el organizador acepta cada inscripción." },
        ["EnChangeSolo"] = new() { [LangEn] = "You can withdraw while registration is open.", [LangEs] = "Puedes retirarte mientras la inscripción siga abierta." },
        ["EnChangeTeam"] = new() { [LangEn] = "You can withdraw the team while registration is open. The line-up can no longer change.", [LangEs] = "Puedes retirar al equipo mientras la inscripción siga abierta. La alineación ya no se puede cambiar." },
        ["EnLeaveMember"] = new() { [LangEn] = "Only {0}, the captain, can withdraw the team while registration is open.", [LangEs] = "Solo {0}, el capitán, puede retirar al equipo mientras la inscripción siga abierta." },

        ["EnStateIn"] = new() { [LangEn] = "Entered.", [LangEs] = "Inscrito." },
        ["EnStateTeamIn"] = new() { [LangEn] = "Entered with {0}.", [LangEs] = "Inscrito con {0}." },
        ["EnStateLeft"] = new() { [LangEn] = "{0} places left.", [LangEs] = "Quedan {0} plazas." },
        ["EnStateLeftOne"] = new() { [LangEn] = "1 place left.", [LangEs] = "Queda 1 plaza." },
        ["EnStatePending"] = new() { [LangEn] = "Request sent. It waits for the organizer to accept it.", [LangEs] = "Solicitud enviada. Espera a que el organizador la acepte." },
        ["EnStateWaitlist"] = new() { [LangEn] = "On the waiting list. You get in if a place frees up.", [LangEs] = "En lista de espera. Entras si se libera una plaza." },
        ["EnNextLine"] = new() { [LangEn] = "When the bracket is drawn, you'll see your first match on the tournament's page.", [LangEs] = "Cuando se arme el cuadro, verás tu primer partido en la ficha del torneo." },
        ["EnCaptainMark"] = new() { [LangEn] = "{0} (captain)", [LangEs] = "{0} (capitán)" },
        ["EnYouMark"] = new() { [LangEn] = "{0} (you)", [LangEs] = "{0} (tú)" },
        ["EnYouCaptain"] = new() { [LangEn] = "{0} (you, captain)", [LangEs] = "{0} (tú, capitán)" },

        ["EnPickTeam"] = new() { [LangEn] = "1 · CHOOSE THE TEAM", [LangEs] = "1 · ELIGE EL EQUIPO" },
        ["EnPickTeamHint"] = new() { [LangEn] = "Only the ones you captain", [LangEs] = "Solo los que capitaneas" },
        ["EnFrozenNote"] = new() { [LangEn] = "Whoever enters is who plays: the line-up stays fixed even if you change the team later.", [LangEs] = "Quien se inscribe es quien juega: la alineación queda fija aunque después cambies el equipo." },
        ["EnTeamMembers"] = new() { [LangEn] = "{0} players · {1}", [LangEs] = "{0} jugadores · {1}" },
        ["EnTeamShort"] = new() { [LangEn] = "It has {0} of {1} players: {2} missing.", [LangEs] = "Tiene {0} de {1} jugadores: le faltan {2}." },
        ["EnTeamShortOne"] = new() { [LangEn] = "It has {0} of {1} players: 1 missing.", [LangEs] = "Tiene {0} de {1} jugadores: le falta 1." },
        ["EnTeamNotCaptain"] = new() { [LangEn] = "Only its captain, {0}, can enter it.", [LangEs] = "Solo puede inscribirlo su capitán, {0}." },
        ["EnPickLineup"] = new() { [LangEn] = "2 · CHOOSE WHO PLAYS", [LangEs] = "2 · ELIGE QUIÉN JUEGA" },
        ["EnLineupHint"] = new() { [LangEn] = "It has {0} players; {1} play", [LangEs] = "Tiene {0} jugadores; juegan {1}" },
        ["EnOfN"] = new() { [LangEn] = "{0} of {1}", [LangEs] = "{0} de {1}" },

        ["EnTeamName"] = new() { [LangEn] = "TEAM NAME", [LangEs] = "NOMBRE DEL EQUIPO" },
        ["EnTeamNameHint"] = new() { [LangEn] = "For example, Los Andes", [LangEs] = "Por ejemplo, Los Andes" },
        ["EnMates"] = new() { [LangEn] = "YOUR TEAMMATES", [LangEs] = "TUS COMPAÑEROS" },
        ["EnMatesHint"] = new() { [LangEn] = "Recent and online first; search for the rest", [LangEs] = "Recientes y conectados primero; busca para ver al resto" },
        ["EnSearchPlayer"] = new() { [LangEn] = "Search for any player", [LangEs] = "Buscar a cualquier jugador" },
        ["EnGroupRecent"] = new() { [LangEn] = "PLAYED WITH YOU RECENTLY", [LangEs] = "JUGARON CONTIGO HACE POCO" },
        ["EnGroupOnline"] = new() { [LangEn] = "ONLINE", [LangEs] = "CONECTADOS" },
        ["EnGroupOther"] = new() { [LangEn] = "OTHER PLAYERS", [LangEs] = "OTROS JUGADORES" },
        ["EnOnline"] = new() { [LangEn] = "Online", [LangEs] = "Conectado" },
        ["EnOffline"] = new() { [LangEn] = "Offline", [LangEs] = "Desconectado" },
        ["EnTaken"] = new() { [LangEn] = "Already entered", [LangEs] = "Ya está inscrito" },
        ["EnPlayedAgo"] = new() { [LangEn] = "{0} · played with you {1} ago", [LangEs] = "{0} · jugó contigo hace {1}" },
        ["EnNoPlayer"] = new() { [LangEn] = "Nobody matches «{0}».", [LangEs] = "Nadie coincide con «{0}»." },

        ["EnNoTeamTitle"] = new() { [LangEn] = "You don't have a team of {0} you can enter", [LangEs] = "No tienes un equipo de {0} que puedas inscribir" },
        ["EnNoTeamBody"] = new() { [LangEn] = "In this tournament, a captain enters a saved team. Create one in My teams.", [LangEs] = "En este torneo, un capitán inscribe un equipo guardado. Crea uno en Mis equipos." },
        ["EnNoTeamShort"] = new() { [LangEn] = "In this tournament, a captain enters a saved team. Create one in My teams or complete the one you have: «{0}» is missing {1}.", [LangEs] = "En este torneo, un capitán inscribe un equipo guardado. Crea uno en Mis equipos o completa el que tienes: a «{0}» le faltan {1}." },
        ["EnOpenTeams"] = new() { [LangEn] = "Open My teams", [LangEs] = "Abrir Mis equipos" },

        ["EnModMissing"] = new() { [LangEn] = "This tournament is played with {0} and you don't have it installed. You can enter now; install it before your first match.", [LangEs] = "Este torneo se juega con {0} y no lo tienes instalado. Puedes inscribirte ya; instálalo antes de tu primer partido." },
        ["EnInstall"] = new() { [LangEn] = "Install", [LangEs] = "Instalar" },
        ["EnApprovalNote"] = new() { [LangEn] = "This tournament needs approval: your request waits for the organizer to accept it.", [LangEs] = "Este torneo es con aprobación: tu solicitud espera a que el organizador la acepte." },

        ["EnOkSolo"] = new() { [LangEn] = "Enter", [LangEs] = "Inscribirme" },
        ["EnOkSend"] = new() { [LangEn] = "Send request", [LangEs] = "Enviar solicitud" },
        ["EnOkTeam"] = new() { [LangEn] = "Enter {0}", [LangEs] = "Inscribir a {0}" },
        ["EnOkBuild"] = new() { [LangEn] = "Enter team", [LangEs] = "Inscribir equipo" },
        ["EnSending"] = new() { [LangEn] = "Entering…", [LangEs] = "Inscribiendo…" },
        ["EnRetry"] = new() { [LangEn] = "Retry", [LangEs] = "Reintentar" },
        ["EnDone"] = new() { [LangEn] = "Done", [LangEs] = "Listo" },
        ["EnClose"] = new() { [LangEn] = "Close", [LangEs] = "Cerrar" },

        ["EnBlockMore"] = new() { [LangEn] = "Choose {0} more players.", [LangEs] = "Elige {0} jugadores más." },
        ["EnBlockMoreOne"] = new() { [LangEn] = "Choose 1 more player.", [LangEs] = "Elige 1 jugador más." },
        ["EnBlockExtra"] = new() { [LangEn] = "{0} too many: {1} play.", [LangEs] = "Sobran {0}: juegan {1}." },
        ["EnBlockMates"] = new() { [LangEn] = "Choose {0} teammates.", [LangEs] = "Elige {0} compañeros." },
        ["EnBlockMatesOne"] = new() { [LangEn] = "Choose 1 teammate.", [LangEs] = "Elige 1 compañero." },
        ["EnBlockName"] = new() { [LangEn] = "Give the team a name.", [LangEs] = "Ponle nombre al equipo." },

        ["EnDoneTitle"] = new() { [LangEn] = "You're in", [LangEs] = "Ya estás inscrito" },
        ["EnDoneBody"] = new() { [LangEn] = "When the bracket is drawn, you'll see your first match on the tournament's page. You can withdraw while registration is open.", [LangEs] = "Cuando se arme el cuadro, verás tu primer partido en la ficha del torneo. Puedes retirarte mientras la inscripción siga abierta." },
        ["EnSentTitle"] = new() { [LangEn] = "Request sent", [LangEs] = "Solicitud enviada" },
        ["EnSentBody"] = new() { [LangEn] = "Wait for the organizer to accept it. You can withdraw it while registration is open.", [LangEs] = "Espera a que el organizador la acepte. Puedes retirarla mientras la inscripción siga abierta." },
        ["EnWaitTitle"] = new() { [LangEn] = "You're on the waiting list", [LangEs] = "Quedaste en lista de espera" },
        ["EnWaitBody"] = new() { [LangEn] = "All the places were taken. You get in if one frees up.", [LangEs] = "Las plazas ya estaban ocupadas. Entras si se libera una." },
        ["EnAnd"] = new() { [LangEn] = " and ", [LangEs] = " y " },
        ["EnDoneTeam"] = new() { [LangEn] = "{0} · {1}", [LangEs] = "{0} · {1}" },

        ["EnLeave"] = new() { [LangEn] = "Withdraw…", [LangEs] = "Retirarme…" },
        ["EnLeaveTeam"] = new() { [LangEn] = "Withdraw the team…", [LangEs] = "Retirar al equipo…" },
        ["EnLeaveTitle"] = new() { [LangEn] = "Withdraw from {0}?", [LangEs] = "¿Retirarte de {0}?" },
        ["EnLeaveBody"] = new() { [LangEn] = "Your place is freed for another player. You can enter again while registration is open.", [LangEs] = "Tu plaza queda libre para otro jugador. Puedes volver a inscribirte mientras la inscripción siga abierta." },
        ["EnLeaveYes"] = new() { [LangEn] = "Withdraw", [LangEs] = "Retirarme" },
        ["EnLeaveTeamTitle"] = new() { [LangEn] = "Withdraw {0} from {1}?", [LangEs] = "¿Retirar a {0} de {1}?" },
        ["EnLeaveTeamBody"] = new() { [LangEn] = "The team's place is freed. You can enter it again while registration is open.", [LangEs] = "Las plazas del equipo quedan libres. Puedes volver a inscribirlo mientras la inscripción siga abierta." },
        ["EnLeaveTeamYes"] = new() { [LangEn] = "Withdraw the team", [LangEs] = "Retirar al equipo" },

        ["EnErrSent"] = new() { [LangEn] = "It wasn't sent", [LangEs] = "No se envió la solicitud" },
        ["EnErrTaken"] = new() { [LangEn] = "{0} entered with another team while you were building yours. Choose another teammate and send it again.", [LangEs] = "{0} se inscribió con otro equipo mientras armabas el tuyo. Elige a otro compañero y vuelve a enviarla." },
        ["EnErrConnTitle"] = new() { [LangEn] = "The entry didn't go through", [LangEs] = "No se completó la inscripción" },
        ["EnErrConnBody"] = new() { [LangEn] = "The connection to the server was lost before it confirmed. You haven't entered; try again.", [LangEs] = "Se perdió la conexión con el servidor antes de confirmar. No te has inscrito; vuelve a intentarlo." },
    };
}
