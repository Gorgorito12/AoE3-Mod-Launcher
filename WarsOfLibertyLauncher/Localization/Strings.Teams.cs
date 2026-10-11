using System.Collections.Generic;

namespace WarsOfLibertyLauncher.Localization;

/// <summary>
/// The strings of TEAM tournaments: entering a 2v2 or 3v3 in its three modes, the organiser
/// forming the teams of a draft, "My teams" and its dialogs, and why a played bracket match has
/// not moved.
///
/// <para>Merged into the one table at startup by the static constructor in
/// <c>Strings.Rating.cs</c>; a key that already exists THROWS, as there. Spanish is es-419 with
/// tuteo, like the rest of the launcher.</para>
/// </summary>
public static partial class Strings
{
    private static readonly Dictionary<string, Dictionary<string, string>> TeamsTable = new()
    {
        // ---------------------------------------------------------------- the picker
        ["MpPickPlayers"] = new() { [LangEn] = "PLAYERS", [LangEs] = "JUGADORES" },
        ["MpPickSearch"] = new() { [LangEn] = "Search for a player by name", [LangEs] = "Busca un jugador por nombre" },
        ["MpPickCount"] = new() { [LangEn] = "{0} of {1} chosen", [LangEs] = "{0} de {1} elegidos" },
        ["MpPickEmpty"] = new()
        {
            [LangEn] = "Nobody to choose here. Search by name, or ask them to come online.",
            [LangEs] = "No hay nadie para elegir. Busca por nombre o pídeles que se conecten.",
        },

        // ---------------------------------------------------------------- entering a team tournament
        ["MpTournamentNeedTeamTitle"] = new() { [LangEn] = "You need a team", [LangEs] = "Necesitas un equipo" },
        ["MpTournamentNeedTeamBody"] = new()
        {
            [LangEn] = "This tournament is entered with a saved team of at least {0} players, captained by you. Create one in My teams and invite your players; they accept the invitation from their own launcher.",
            [LangEs] = "A este torneo se entra con un equipo guardado de al menos {0} jugadores, en el que tú seas el capitán. Crea uno en Mis equipos e invita a tus jugadores; ellos aceptan la invitación desde su launcher.",
        },
        ["MpTournamentOpenMyTeams"] = new() { [LangEn] = "Open My teams", [LangEs] = "Abrir Mis equipos" },
        ["MpTournamentPickTeamTitle"] = new() { [LangEn] = "Enter with which team?", [LangEs] = "¿Con qué equipo te inscribes?" },
        ["MpTournamentPickTeamIntro"] = new()
        {
            [LangEn] = "Your teams with at least {0} players.",
            [LangEs] = "Tus equipos con al menos {0} jugadores.",
        },
        ["MpTournamentPickTeamList"] = new() { [LangEn] = "TEAMS", [LangEs] = "EQUIPOS" },
        ["MpTournamentPickTeamOk"] = new() { [LangEn] = "Continue", [LangEs] = "Continuar" },
        ["MpTournamentLineupTitle"] = new() { [LangEn] = "Who plays?", [LangEs] = "¿Quién juega?" },
        ["MpTournamentLineupIntro"] = new()
        {
            [LangEn] = "Choose the {0} players of {1} who play this tournament. You play as captain; the line-up is fixed once you enter.",
            [LangEs] = "Elige los {0} jugadores de {1} que juegan este torneo. Tú juegas como capitán; la alineación queda fija al inscribirte.",
        },
        ["MpTournamentAdhocTitle"] = new() { [LangEn] = "Your line-up", [LangEs] = "Tu alineación" },
        ["MpTournamentAdhocIntro"] = new()
        {
            [LangEn] = "Choose the {0} players of your team, you included, and give it a name. The line-up is fixed once you enter.",
            [LangEs] = "Elige los {0} jugadores de tu equipo, tú incluido, y ponle un nombre. La alineación queda fija al inscribirte.",
        },
        ["MpTournamentAdhocName"] = new() { [LangEn] = "TEAM NAME", [LangEs] = "NOMBRE DEL EQUIPO" },
        ["MpTournamentAlreadyIn"] = new() { [LangEn] = "Already in this tournament", [LangEs] = "Ya está en este torneo" },

        // ---------------------------------------------------------------- draft
        ["MpTournamentWithoutTeam"] = new()
        {
            [LangEn] = "{0} player(s) still without a team. Form teams of {1} before seeding.",
            [LangEs] = "{0} jugador(es) todavía sin equipo. Forma equipos de {1} antes de sortear.",
        },
        ["MpTournamentFormTeam"] = new() { [LangEn] = "Form a team…", [LangEs] = "Formar equipo…" },
        ["MpTournamentFormTeamTitle"] = new() { [LangEn] = "Form a team", [LangEs] = "Formar un equipo" },
        ["MpTournamentFormTeamIntro"] = new()
        {
            [LangEn] = "Choose the {0} players who play together. Their entries become one team.",
            [LangEs] = "Elige los {0} jugadores que juegan juntos. Sus inscripciones pasan a ser un solo equipo.",
        },
        ["MpTournamentFormTeamName"] = new() { [LangEn] = "TEAM NAME (OPTIONAL)", [LangEs] = "NOMBRE DEL EQUIPO (OPCIONAL)" },
        ["MpTournamentFormTeamOk"] = new() { [LangEn] = "Form team", [LangEs] = "Formar equipo" },
        ["MpTournamentNeedsTeam"] = new() { [LangEn] = "Needs a team", [LangEs] = "Sin equipo" },
        ["MpTournamentInTeam"] = new() { [LangEn] = "In {0}", [LangEs] = "En {0}" },
        ["MpTournamentRemoveEntrant"] = new() { [LangEn] = "Remove", [LangEs] = "Quitar" },
        ["MpTournamentBlockedIncomplete"] = new()
        {
            [LangEn] = "{0} entrant(s) still without a full team. Form the teams first.",
            [LangEs] = "{0} inscrito(s) todavía sin el equipo completo. Forma los equipos primero.",
        },

        // ---------------------------------------------------------------- a played match that did not move
        ["MpTournamentReportAwaiting"] = new()
        {
            [LangEn] = "Played. Waiting for a player of the other team to confirm the result in their launcher; the bracket moves then.",
            [LangEs] = "Jugada. Falta que un jugador del otro equipo confirme el resultado en su launcher; entonces el cuadro avanza.",
        },
        ["MpTournamentReportSidesMismatch"] = new()
        {
            [LangEn] = "Played with other sides than the bracket's, so it does not count. Replay it with each team on its own side, or the organiser decides.",
            [LangEs] = "Se jugó con otros bandos que los del cuadro, así que no cuenta. Vuelvan a jugarla con cada equipo en su bando, o decide el organizador.",
        },
        ["MpTournamentReportLineupsMismatch"] = new()
        {
            [LangEn] = "The players of that game were not the two registered line-ups, so it does not decide this match.",
            [LangEs] = "Los jugadores de esa partida no eran las dos alineaciones inscritas, así que no decide este cruce.",
        },
        ["MpTournamentReportUndecided"] = new()
        {
            [LangEn] = "Played, but no result could be read from the recording. Play it again, or the organiser decides.",
            [LangEs] = "Se jugó, pero no se pudo leer un resultado de la grabación. Vuelvan a jugarla, o decide el organizador.",
        },

        // The preview console: a team result one side reported, the other has not confirmed.
        ["MpTournamentPreviewAwaiting"] = new()
        {
            [LangEn] = "Reported, waiting for the other team",
            [LangEs] = "Reportada, falta el otro equipo",
        },

        // ---------------------------------------------------------------- my teams
        ["MpTeamsButton"] = new() { [LangEn] = "My teams", [LangEs] = "Mis equipos" },
        ["MpTeamsButtonInvites"] = new() { [LangEn] = "My teams · {0} invitation(s)", [LangEs] = "Mis equipos · {0} invitación(es)" },
        ["MpTeamsButtonTip"] = new()
        {
            [LangEn] = "The saved teams you enter team tournaments with, and the invitations waiting for you.",
            [LangEs] = "Los equipos guardados con los que te inscribes en torneos por equipos, y las invitaciones que te esperan.",
        },
        ["MpTeamsTitle"] = new() { [LangEn] = "My teams", [LangEs] = "Mis equipos" },
        ["MpTeamsNew"] = new() { [LangEn] = "+ New team", [LangEs] = "+ Nuevo equipo" },
        ["MpTeamsClose"] = new() { [LangEn] = "Close", [LangEs] = "Cerrar" },
        ["MpTeamsUnavailable"] = new()
        {
            [LangEn] = "Teams are not available on this server yet.",
            [LangEs] = "Los equipos todavía no están disponibles en este servidor.",
        },
        ["MpTeamsLoading"] = new() { [LangEn] = "Loading…", [LangEs] = "Cargando…" },
        ["MpTeamsLoadFailed"] = new()
        {
            [LangEn] = "Your teams could not be loaded. Try again in a moment.",
            [LangEs] = "No se pudieron cargar tus equipos. Vuelve a intentarlo en un momento.",
        },
        ["MpTeamsInvitesHeading"] = new() { [LangEn] = "INVITATIONS", [LangEs] = "INVITACIONES" },
        ["MpTeamsInviteFrom"] = new() { [LangEn] = "Invitation to join {0}", [LangEs] = "Invitación para unirte a {0}" },
        ["MpTeamsAccept"] = new() { [LangEn] = "Accept", [LangEs] = "Aceptar" },
        ["MpTeamsAccepted"] = new() { [LangEn] = "You joined the team.", [LangEs] = "Te uniste al equipo." },
        ["MpTeamsDecline"] = new() { [LangEn] = "Decline", [LangEs] = "Rechazar" },
        ["MpTeamsHeading"] = new() { [LangEn] = "TEAMS", [LangEs] = "EQUIPOS" },
        ["MpTeamsEmpty"] = new()
        {
            [LangEn] = "You are not in any team yet. Create one and invite your players.",
            [LangEs] = "Todavía no estás en ningún equipo. Crea uno e invita a tus jugadores.",
        },
        ["MpTeamsMemberCount"] = new() { [LangEn] = "{0}/{1} players", [LangEs] = "{0}/{1} jugadores" },
        ["MpTeamsCaptain"] = new() { [LangEn] = "captain", [LangEs] = "capitán" },
        ["MpTeamsYou"] = new() { [LangEn] = "you", [LangEs] = "tú" },
        ["MpTeamsInvite"] = new() { [LangEn] = "Invite…", [LangEs] = "Invitar…" },
        ["MpTeamsRemove"] = new() { [LangEn] = "Remove", [LangEs] = "Quitar" },
        ["MpTeamsLeave"] = new() { [LangEn] = "Leave", [LangEs] = "Salir" },
        ["MpTeamsDisband"] = new() { [LangEn] = "Disband", [LangEs] = "Disolver" },
        ["MpTeamsDisbandTitle"] = new() { [LangEn] = "Disband the team?", [LangEs] = "¿Disolver el equipo?" },
        ["MpTeamsDisbandBody"] = new()
        {
            [LangEn] = "{0} will be disbanded. Tournaments it already played keep its name, and line-ups already entered are not affected.",
            [LangEs] = "{0} se va a disolver. Los torneos que ya jugó conservan su nombre, y las alineaciones ya inscritas no se ven afectadas.",
        },
        ["MpTeamsInviteTitle"] = new() { [LangEn] = "Invite a player", [LangEs] = "Invitar a un jugador" },
        ["MpTeamsInviteIntro"] = new()
        {
            [LangEn] = "Choose who to invite to {0}. They see the invitation in their launcher, even if they are offline now.",
            [LangEs] = "Elige a quién invitar a {0}. Verá la invitación en su launcher, aunque ahora no esté conectado.",
        },
        ["MpTeamsInviteOk"] = new() { [LangEn] = "Invite", [LangEs] = "Invitar" },
        ["MpTeamsInviteSent"] = new() { [LangEn] = "Invitation sent.", [LangEs] = "Invitación enviada." },
        ["MpTeamsCreated"] = new() { [LangEn] = "Team created. Now invite your players.", [LangEs] = "Equipo creado. Ahora invita a tus jugadores." },
        ["MpTeamsInviteToastTitle"] = new() { [LangEn] = "Team invitation", [LangEs] = "Invitación a un equipo" },
        ["MpTeamsInviteToastBody"] = new() { [LangEn] = "You were invited to join {0}.", [LangEs] = "Te invitaron a unirte a {0}." },
        ["MpTeamsInviteToastOpen"] = new() { [LangEn] = "See", [LangEs] = "Ver" },

        // ---------------------------------------------------------------- new team
        ["MpTeamsCreateTitle"] = new() { [LangEn] = "New team", [LangEs] = "Nuevo equipo" },
        ["MpTeamsCreateName"] = new() { [LangEn] = "NAME", [LangEs] = "NOMBRE" },
        ["MpTeamsCreateTag"] = new() { [LangEn] = "TAG (OPTIONAL, UP TO 8)", [LangEs] = "ETIQUETA (OPCIONAL, HASTA 8)" },
        ["MpTeamsCreateHint"] = new()
        {
            [LangEn] = "You are its captain. A team holds up to 6 players, so it can carry reserves; you choose who plays when you enter a tournament.",
            [LangEs] = "Tú eres el capitán. Un equipo admite hasta 6 jugadores, así que puede tener suplentes; eliges quién juega al inscribirte en un torneo.",
        },
        ["MpTeamsCreateButton"] = new() { [LangEn] = "Create", [LangEs] = "Crear" },
    };
}
