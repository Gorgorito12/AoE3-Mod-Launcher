namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// How the Statistics page divides a given width (design 109, `docs/design_14nov`).
///
/// <para>The page is a strip of activity at full width, then ONE wrapping row of cards - Balance,
/// Matchups (and "Played together" in Teams), Maps - then the decks at full width. It used to keep
/// a fixed right column and reserve the matchups' place even when there were none to show, which
/// left a hole in the middle of a wide screen.</para>
/// </summary>
public readonly record struct StatsPageLayout(double Gutter, double RowHeight, bool Wide)
{
    /// <summary>One column of the balance table, at its narrowest, and the space between two.</summary>
    public const double BalanceColumnWidth = 450, BalanceColumnGap = 28, CardPadding = 32;

    /// <summary>The bases of the other cards in the row (design 109).</summary>
    public const double MatchupsBasis = 400, AlliesBasis = 380, MapsBasis = 340;

    /// <summary>A balance table opens another column only once every column would hold this many.</summary>
    public const int RowsPerBalanceColumn = 5;

    /// <summary>At most three columns of balance.</summary>
    public const int MaxBalanceColumns = 3;

    /// <summary>The maps shown before the fold.</summary>
    public const int MapsShown = 12;

    public static StatsPageLayout For(double width)
        => new(PageSpacing.For(width), 34, width >= PageSpacing.WideFrom);

    /// <summary>The width the cards share: the page less its own margins.</summary>
    public double Inner(double width) => Math.Max(0, width - Gutter * 2);

    /// <summary>
    /// How many columns the balance takes: the least of three, of one per five civilizations shown,
    /// and of how many fit at 450 beside the maps and the matchups (when there are matchups), and
    /// at least one.
    /// </summary>
    public int BalanceColumns(double width, int civsShown, double pairsBasis)
    {
        double free = Inner(width) - MapsBasis - Gutter - CardPadding - (pairsBasis > 0 ? pairsBasis + Gutter : 0);
        int fit = (int)Math.Floor((free + BalanceColumnGap) / (BalanceColumnWidth + BalanceColumnGap));
        return Math.Max(1, Math.Min(MaxBalanceColumns, Math.Min(civsShown / RowsPerBalanceColumn, fit)));
    }

    /// <summary>The balance card's basis for <paramref name="columns"/> columns.</summary>
    public static double BalanceBasis(int columns)
        => columns * BalanceColumnWidth + (columns - 1) * BalanceColumnGap + CardPadding;

    /// <summary>
    /// In Teams, "Played together" sits beside Matchups when all four cards fit on one line;
    /// otherwise under it (design 109).
    /// </summary>
    public bool AlliesBeside(double width)
        => BalanceBasis(1) + AlliesBasis * 2 + MapsBasis + Gutter * 3 <= Inner(width);
}
