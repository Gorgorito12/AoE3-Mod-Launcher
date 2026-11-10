using System.Collections.Generic;

namespace WarsOfLibertyLauncher.Localization;

/// <summary>
/// The Clasificación page's podium, age groups and "Your position" (design 93). Merged into the one
/// table by the static constructor in <c>Strings.Rating.cs</c>, where a duplicate key throws.
/// Spanish is es-419 with tuteo.
/// </summary>
public static partial class Strings
{
    private static readonly Dictionary<string, Dictionary<string, string>> RankingTable = new()
    {
        // The summary at the top right: the figures are drawn apart, in the mono face.
        ["RkCountRanked"] = new() { [LangEn] = " ranked · ", [LangEs] = " clasificados · " },
        ["RkCountPlacing"] = new() { [LangEn] = " in placement", [LangEs] = " en posicionamiento" },

        // The table's groups.
        ["RkGroupPlaces"] = new() { [LangEn] = "places {0}–{1}", [LangEs] = "puestos {0}–{1}" },
        ["RkGroupPlace"] = new() { [LangEn] = "place {0}", [LangEs] = "puesto {0}" },
        ["RkGroupTip"] = new() { [LangEn] = "{0}: places {1} to {2} of the table. Ages are shares of the table, so they grow with it.", [LangEs] = "{0}: del puesto {1} al {2} de la tabla. Las edades son porcentajes de la tabla, así que crecen con ella." },
        ["RkGroupTipOne"] = new() { [LangEn] = "{0}: place {1} of the table. Ages are shares of the table, so they grow with it.", [LangEs] = "{0}: el puesto {1} de la tabla. Las edades son porcentajes de la tabla, así que crecen con ella." },
        ["RkGroupPlacing"] = new() { [LangEn] = "IN PLACEMENT", [LangEs] = "EN POSICIONAMIENTO" },
        ["RkGroupPlayers"] = new() { [LangEn] = "{0} players", [LangEs] = "{0} jugadores" },
        ["RkGroupPlayerOne"] = new() { [LangEn] = "1 player", [LangEs] = "1 jugador" },
        ["RkGroupPlacingTip"] = new() { [LangEn] = "Discovery: they haven't played their {0} rated matches yet. They have no place until they do.", [LangEs] = "Descubrimiento: todavía no jugaron sus {0} partidas puntuadas. No tienen puesto hasta terminarlas." },
        ["RkGroupPlacingTipPlain"] = new() { [LangEn] = "Discovery: they haven't finished their placement matches yet. They have no place until they do.", [LangEs] = "Descubrimiento: todavía no terminaron sus partidas de posicionamiento. No tienen puesto hasta terminarlas." },

        // The podium.
        ["RkPeakShort"] = new() { [LangEn] = "peak {0}", [LangEs] = "máx. {0}" },
        ["RkEloLabel"] = new() { [LangEn] = "ELO", [LangEs] = "ELO" },
        ["RkInMonth"] = new() { [LangEn] = "in {0}", [LangEs] = "en {0}" },
        ["RkClimbTip"] = new() { [LangEn] = "This month: {0} → {1} · {2} matches", [LangEs] = "Este mes: {0} → {1} · {2} partidas" },
        ["RkClimbTipPlain"] = new() { [LangEn] = "ELO gained this month.", [LangEs] = "ELO ganado este mes." },

        // "Your position".
        ["RkYouTitle"] = new() { [LangEn] = "Your position", [LangEs] = "Tu posición" },
        ["RkYouProfile"] = new() { [LangEn] = "View my profile →", [LangEs] = "Ver mi perfil →" },
        ["RkYouOf"] = new() { [LangEn] = "of {0}", [LangEs] = "de {0}" },
        ["RkYouFirst"] = new() { [LangEn] = "You're first on the table.", [LangEs] = "Vas primero en la tabla." },
        ["RkYouNext"] = new() { [LangEn] = "You need {0} ELO to reach place {1}.", [LangEs] = "Te faltan {0} ELO para el {1}.º puesto." },
        ["RkYouNextAge"] = new() { [LangEn] = "You need {0} ELO to reach place {1} and rise to {2}.", [LangEs] = "Te faltan {0} ELO para el {1}.º puesto y subir a {2}." },
        ["RkYouLeftOne"] = new() { [LangEn] = "1 rated match left to enter the table.", [LangEs] = "Te queda 1 partida puntuada para entrar en la tabla." },
        ["RkYouLeft"] = new() { [LangEn] = "{0} rated matches left to enter the table.", [LangEs] = "Te quedan {0} partidas puntuadas para entrar en la tabla." },
        ["RkYouProvisionalTip"] = new() { [LangEn] = "Provisional ELO: it settles during placement. Only you see it.", [LangEs] = "ELO provisional: se ajusta durante el posicionamiento. Solo lo ves tú." },

        ["RkInactiveTip"] = new() { [LangEn] = "Inactive: {0} days without a rated match. The place is kept.", [LangEs] = "Inactivo: lleva {0} días sin jugar una partida puntuada. Conserva su puesto en la tabla." },
    };
}
