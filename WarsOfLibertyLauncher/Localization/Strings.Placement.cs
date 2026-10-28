using System.Collections.Generic;

namespace WarsOfLibertyLauncher.Localization;

/// <summary>
/// A rating still being placed is its owner's alone: what stands where somebody else's number
/// would be ("In placement 3/10"), and why the room shows no win probability meanwhile. Merged
/// into the one table by the static constructor in <c>Strings.Rating.cs</c>, where a duplicate key
/// throws rather than shadowing another.
///
/// <para>Spanish is es-419 with tuteo, like the rest of the launcher.</para>
/// </summary>
public static partial class Strings
{
    private static readonly Dictionary<string, Dictionary<string, string>> PlacementTable = new()
    {
        // {0} played, {1} required. Where somebody else's number would be, in the long form.
        ["MpEloHiddenPlacement"] = new() { [LangEn] = "In placement {0}/{1}", [LangEs] = "En posicionamiento {0}/{1}" },
        // The same, when the server did not say how far along he is.
        ["MpEloHiddenPlacementNoCount"] = new() { [LangEn] = "In placement", [LangEs] = "En posicionamiento" },
        // {0} is MpEloHiddenPlacement (or its no-count form).
        ["MpEloHiddenPlacementTip"] = new()
        {
            [LangEn] = "{0}. Their rating shows once they finish placement.",
            [LangEs] = "{0}. Su ELO se mostrará cuando termine el posicionamiento.",
        },
        ["MpOddsHiddenPlacement"] = new()
        {
            [LangEn] = "The win probability shows once no player in the room is in placement.",
            [LangEs] = "La probabilidad de victoria se muestra cuando ningún jugador de la sala está en posicionamiento.",
        },
        ["MpTeamAvgHiddenPlacementTip"] = new()
        {
            [LangEn] = "No average while a teammate is in placement: it would give their rating away.",
            [LangEs] = "Sin promedio mientras un compañero esté en posicionamiento: revelaría su ELO.",
        },
        // The result card's rival, and the narrow rooms row's host: {0} name, {1} played, {2} required.
        ["MpResultRivalInPlacement"] = new()
        {
            [LangEn] = "{0} · in placement {1}/{2}",
            [LangEs] = "{0} · en posicionamiento {1}/{2}",
        },
    };
}
