using WarsOfLibertyLauncher.Localization;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>One line under the result's figure saying why it is that figure.</summary>
/// <param name="Prefix">"+6" — drawn bold and mono, like the anti-farm line; null for an
/// odds-only line that sits under another sentence.</param>
/// <param name="Text">The rest, already worded.</param>
public sealed record ResultWhy(string? Prefix, string Text);

/// <summary>
/// "Why these points" on the result card (2026-11-18): "+6: you were the favourite (72 %)",
/// "+21: you won as the underdog (31 %)", "an even match", "−17: you lost as the favourite", and
/// during placement "your first matches move more". The percentage is the SERVER's, stored when it
/// rated the match (<c>win_expected</c>) and sent to its owner only — never worked out here, so a
/// hidden rival's rating can never come back through it. Pure; pinned by <c>ResultWhyViewTests</c>.
/// </summary>
public static class ResultWhyView
{
    /// <summary>From here up the player went in as the favourite.</summary>
    public const int FavouriteFrom = 60;

    /// <summary>From here down he went in as the underdog.</summary>
    public const int UnderdogUpTo = 40;

    /// <summary>The line for the card; null when there is nothing honest to say.</summary>
    public static ResultWhy? For(MatchOutcomeView m)
    {
        var delta = m.RatingDelta;
        if (delta is not int d) return null;
        var prefix = RatingDisplay.FormatDelta(d);

        // During placement the swing is the point of the line, whatever the odds were.
        if (m.PlacementPlayed is int played && m.PlacementRequired is int required && played < required)
            return new ResultWhy(prefix, Strings.Format("MpResultWhyPlacement", played, required));

        if (m.WinExpected is not int pct) return null;
        var pctText = Strings.Format("MpPercentValue", pct);

        // The anti-farm card already explains the figure; the odds go under it, with no prefix.
        if (m.FarmDiscounted)
            return new ResultWhy(null, Strings.Format(
                pct >= FavouriteFrom ? "MpResultWhyFarmFav" : pct <= UnderdogUpTo ? "MpResultWhyFarmDog" : "MpResultWhyFarmEven",
                pctText));

        string key = m.Verdict switch
        {
            MatchVerdict.Win => pct >= FavouriteFrom ? "MpResultWhyWinFav" : pct <= UnderdogUpTo ? "MpResultWhyWinDog" : "MpResultWhyEven",
            MatchVerdict.Loss => pct >= FavouriteFrom ? "MpResultWhyLossFav" : pct <= UnderdogUpTo ? "MpResultWhyLossDog" : "MpResultWhyEven",
            _ => "",
        };
        return key.Length == 0 ? null : new ResultWhy(prefix, Strings.Format(key, pctText));
    }
}
