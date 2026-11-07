namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>The rules of a rematch (design 87l). The sequence itself is in the multiplayer tab.</summary>
public static class RematchPlan
{
    /// <summary>The network value a rematch room adds to its title. English like every room title
    /// (see <see cref="RoomTitleProposal"/>): it is read by people in other languages.</summary>
    public const string Suffix = " (rematch)";

    /// <summary>
    /// Offered after a decided 1v1 with a rival to invite. A match nobody knows the winner of is
    /// not refused because a rematch would be wrong, but because "Rematch" then answers a result
    /// the screen has just said it does not have.
    /// </summary>
    public static bool Offered(bool oneVOne, string? rivalLogin, MatchVerdict verdict)
        => oneVOne && !string.IsNullOrEmpty(rivalLogin) && verdict is MatchVerdict.Win or MatchVerdict.Loss;

    /// <summary>The new room's title: the old one marked once, cut so the mark always fits.</summary>
    public static string Title(string title, int max)
    {
        var plain = (title ?? "").Trim();
        if (plain.EndsWith(Suffix, System.StringComparison.Ordinal)) plain = plain[..^Suffix.Length].TrimEnd();
        var room = System.Math.Max(0, max - Suffix.Length);
        if (plain.Length > room) plain = plain[..room].TrimEnd();
        return plain + Suffix;
    }
}
