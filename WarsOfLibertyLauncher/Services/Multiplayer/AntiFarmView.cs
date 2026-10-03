using System;
using WarsOfLibertyLauncher.Localization;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// The anti-farm rule as the player is told it (design 55j/55k). The SERVER applies it and sends
/// <c>elo_factor</c> (0.2-1.0) and <c>farm_streak</c>; this class only words it, without accusing
/// anybody: it says what happened and what brings it back to normal.
///
/// <para>The rule, for the text's sake: wins in a row of the same side over the same rival are
/// worth 100 % for the 1st and 2nd, then 10 % less each, down to 20 % from the 10th; back to 100 %
/// when the rival wins one, and 10 % recovered per day the two do not play. Never in tournaments.
/// It is not announced before the match — only on the result and in the History.</para>
/// </summary>
public static class AntiFarmView
{
    /// <summary>The factor as a whole percent.</summary>
    public static int Percent(double factor) => (int)Math.Round(factor * 100, MidpointRounding.AwayFromZero);

    /// <summary>Whether the match was worth less than full points. A null factor is "not rated", never discounted.</summary>
    public static bool IsDiscounted(double? factor) => factor is double f && f < 0.995;

    /// <summary>
    /// The winner's line: "+5 (40 %): 8.ª victoria seguida contra este rival. …" — the delta as
    /// already formatted ("+5"), the percent, and the streak behind it.
    /// </summary>
    public static string WinText(string deltaText, double factor, int streak)
        => Strings.Format("MpResultFarmWin", deltaText, Percent(factor), Ordinals.For(streak, Strings.Language));

    /// <summary>The loser's line: "−4 (40 %): Pedro te ganó 8 veces seguidas. …".</summary>
    public static string LossText(string deltaText, double factor, string rivalNames, int streak)
        => Strings.Format("MpResultFarmLoss", deltaText, Percent(factor), rivalNames, streak);

    /// <summary>The History's short suffix: " · 40 %".</summary>
    public static string HistorySuffix(double factor) => Strings.Format("MpHistFarmPct", Percent(factor));
}
