using System.Collections.Generic;

namespace WarsOfLibertyLauncher.Localization;

/// <summary>
/// The new-tournament window (design 81). Merged into the one table by the static constructor in
/// <c>Strings.Rating.cs</c>, where a duplicate key throws. Spanish is es-419 with tuteo.
/// </summary>
public static partial class Strings
{
    private static readonly Dictionary<string, Dictionary<string, string>> NewTournamentTable = new()
    {
        ["NtLabelMod"] = new() { [LangEn] = "MOD", [LangEs] = "MOD" },
        ["NtLabelName"] = new() { [LangEn] = "NAME", [LangEs] = "NOMBRE" },
        ["NtLabelFormat"] = new() { [LangEn] = "FORMAT", [LangEs] = "FORMATO" },
        ["NtLabelSource"] = new() { [LangEn] = "HOW TEAMS ARE FORMED", [LangEs] = "CÓMO SE FORMAN LOS EQUIPOS" },
        ["NtLabelEntry"] = new() { [LangEn] = "WHO GETS IN", [LangEs] = "QUIÉN ENTRA" },
        ["NtLabelPlaces"] = new() { [LangEn] = "PLACES", [LangEs] = "PLAZAS" },
        ["NtLabelPlacesTeams"] = new() { [LangEn] = "PLACES (TEAMS)", [LangEs] = "PLAZAS (EQUIPOS)" },

        ["NtInstalled"] = new() { [LangEn] = "Installed", [LangEs] = "Instalado" },
        ["NtSearchMod"] = new() { [LangEn] = "Search mod", [LangEs] = "Buscar mod" },
        ["NtGroupInstalled"] = new() { [LangEn] = "INSTALLED", [LangEs] = "INSTALADOS" },
        ["NtGroupOther"] = new() { [LangEn] = "OTHER MODS", [LangEs] = "OTROS MODS" },
        ["NtNoMod"] = new() { [LangEn] = "No mod matches «{0}».", [LangEs] = "Ningún mod coincide con «{0}»." },

        ["NtCount"] = new() { [LangEn] = "{0} / {1}", [LangEs] = "{0} / {1}" },
        ["NtNameShortOne"] = new() { [LangEn] = "1 more character (minimum {0}).", [LangEs] = "Falta 1 carácter (mínimo {0})." },
        ["NtNameShortMany"] = new() { [LangEn] = "{0} more characters (minimum {1}).", [LangEs] = "Faltan {0} caracteres (mínimo {1})." },
        ["NtNameFooter"] = new() { [LangEn] = "Give it a name of at least {0} characters.", [LangEs] = "Ponle un nombre de al menos {0} caracteres." },

        ["NtFormatSolo"] = new() { [LangEn] = "One against one", [LangEs] = "Uno contra uno" },
        ["NtFormatTeams"] = new() { [LangEn] = "Teams of {0}", [LangEs] = "Equipos de {0}" },
        ["NtWhySolo"] = new() { [LangEn] = "Each entrant is one player. There are no teams.", [LangEs] = "Cada participante es un jugador. No hay equipos." },
        ["NtWhyTeam"] = new() { [LangEn] = "Each entrant is a team of {0} players.", [LangEs] = "Cada participante es un equipo de {0} jugadores." },

        ["NtSourceRegistered"] = new() { [LangEn] = "A captain enters a saved team", [LangEs] = "Un capitán inscribe un equipo guardado" },
        ["NtSourceRegisteredBody"] = new() { [LangEn] = "They enter one of their teams and choose who plays.", [LangEs] = "Inscribe uno de sus equipos y elige quién juega." },
        ["NtSourceAdhoc"] = new() { [LangEn] = "The captain picks the line-up when entering", [LangEs] = "El capitán arma la alineación al inscribirse" },
        ["NtSourceAdhocBody"] = new() { [LangEn] = "They choose their teammates and name the team.", [LangEs] = "Elige a sus compañeros y le pone nombre al equipo." },
        ["NtSourceDraft"] = new() { [LangEn] = "Everyone enters alone", [LangEs] = "Cada uno se apunta solo" },
        ["NtSourceDraftBody"] = new() { [LangEn] = "You form the teams with «Form team…».", [LangEs] = "Tú formas los equipos con «Formar equipo…»." },

        ["NtEntryOpen"] = new() { [LangEn] = "First come, first served", [LangEs] = "Por orden de llegada" },
        ["NtEntryApproval"] = new() { [LangEn] = "With approval", [LangEs] = "Con aprobación" },
        ["NtWhyEntryOpen"] = new() { [LangEn] = "They get in as they arrive until the places are full.", [LangEs] = "Entran por orden de llegada hasta llenar las plazas." },
        ["NtWhyEntryApproval"] = new() { [LangEn] = "Each entry waits for you to accept it.", [LangEs] = "Cada inscripción espera a que la aceptes." },

        ["NtMathPlayers"] = new() { [LangEn] = "players", [LangEs] = "jugadores" },
        ["NtMathPlayersTeams"] = new() { [LangEn] = "players ({0} teams of {1})", [LangEs] = "jugadores ({0} equipos de {1})" },
        ["NtMathRoundOne"] = new() { [LangEn] = "round", [LangEs] = "ronda" },
        ["NtMathRounds"] = new() { [LangEn] = "rounds to the final", [LangEs] = "rondas hasta la final" },
        ["NtMathRoomOne"] = new() { [LangEn] = "room in the first round", [LangEs] = "sala en la primera ronda" },
        ["NtMathRooms"] = new() { [LangEn] = "rooms at once in the first round", [LangEs] = "salas a la vez en la primera ronda" },
        ["NtWaitlist"] = new() { [LangEn] = "Once the places are full, the next ones go on a waiting list.", [LangEs] = "Si se llenan las plazas, los siguientes quedan en espera." },

        ["NtOpenTitle"] = new() { [LangEn] = "Open registration when it's created", [LangEs] = "Abrir la inscripción al crearlo" },
        ["NtOpenYes"] = new() { [LangEn] = "People can enter as soon as you create it.", [LangEs] = "Se podrá inscribir gente en cuanto lo crees." },
        ["NtOpenNo"] = new() { [LangEn] = "It's saved as a draft and you open registration when you want.", [LangEs] = "Se guarda como borrador y abres la inscripción cuando quieras." },
        ["NtCreateOpen"] = new() { [LangEn] = "Create and open registration", [LangEs] = "Crear y abrir inscripción" },
        ["NtCreateDraft"] = new() { [LangEn] = "Create as draft", [LangEs] = "Crear como borrador" },

        ["NtOpenFailedTitle"] = new() { [LangEn] = "Created, but registration is still closed", [LangEs] = "Creado, pero la inscripción sigue cerrada" },
        ["NtOpenFailedBody"] = new() { [LangEn] = "The tournament was saved as a draft. Open registration from its page.\n\n{0}", [LangEs] = "El torneo quedó guardado como borrador. Abre la inscripción desde su página.\n\n{0}" },
        ["NtCapacityChangedTitle"] = new() { [LangEn] = "Fewer places than you chose", [LangEs] = "Menos plazas de las que elegiste" },
        ["NtCapacityChangedBody"] = new() { [LangEn] = "This server takes up to {1} places, so the tournament has {1} and not {0}.", [LangEs] = "Este servidor admite hasta {1} plazas, así que el torneo tiene {1} y no {0}." },
    };
}
