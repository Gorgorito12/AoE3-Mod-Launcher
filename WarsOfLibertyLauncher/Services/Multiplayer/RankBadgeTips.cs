using System.Collections.Generic;
using WarsOfLibertyLauncher.Localization;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// The words that go with a rank badge (design handoff 51a): its tooltip, and the line under a
/// name. Pure and WPF-free.
///
/// <para><b>The tooltip ALWAYS names the other badge</b> — "#2 in the teams ladder · 1v1:
/// Industrial #5" — because a player now has two ranks and whoever is looking sees only one.
/// Discovery and an unknown other badge have their own clauses, so "#0" is never printed and a
/// ladder the server did not report is simply not mentioned.</para>
/// </summary>
public static class RankBadgeTips
{
    /// <summary>
    /// The tooltip: a title line, then the body. <paramref name="minDecided"/> is the server's
    /// entry bar, quoted for a 1v1 Discovery; <paramref name="explainOrder"/> adds the line the
    /// ranking tables have always carried about why their order is not the ELO's.
    /// </summary>
    public static string Text(ShownBadge badge, int? minDecided, bool explainOrder = false)
    {
        var title = Strings.Format("MpBadgeTipTitle",
            Strings.Get(RankBadgeChoice.ModeKey(badge.Kind)),
            Strings.Get(RankAges.NameKey(badge.Age)));

        string place;
        if (badge.Age != RankAge.Discovery)
            place = Strings.Format("MpBadgeTipPlace", badge.Position, Strings.Get(LadderKey(badge.Kind)));
        else if (badge.Kind == BadgeKind.Team)
            place = Strings.Get("MpBadgeModeTeamsLocked");
        else
            place = minDecided is > 0
                ? Strings.Format("MpRankBadgeTipDiscovery", minDecided.Value)
                : Strings.Get("MpRankBadgeTipDiscoveryNoBar");

        var body = place;
        if (badge.OtherAge is { } other)
        {
            var otherKind = badge.Kind == BadgeKind.Team ? BadgeKind.Solo : BadgeKind.Team;
            var otherMode = Strings.Get(RankBadgeChoice.ModeKey(otherKind));
            var otherName = Strings.Get(RankAges.NameKey(other));
            body += " · " + (other == RankAge.Discovery
                ? Strings.Format("MpBadgeTipOtherUnplaced", otherMode, otherName)
                : Strings.Format("MpBadgeTipOther", otherMode, otherName, badge.OtherPosition));
        }

        var text = title + "\n" + body;
        if (explainOrder) text += "\n" + Strings.Get("MpBadgeTipOrder");
        return text;
    }

    /// <summary>
    /// The line under a name: "1612 ELO · you · Teams · Imperial". Every segment is optional and
    /// an absent one leaves no stray separator; the mode and age come only with a known badge.
    /// </summary>
    public static string DetailLine(string? elo, string? middle, ShownBadge? badge)
    {
        var parts = new List<string>(4);
        if (!string.IsNullOrWhiteSpace(elo)) parts.Add(elo!);
        if (!string.IsNullOrWhiteSpace(middle)) parts.Add(middle!);
        if (badge is { } b)
        {
            parts.Add(Strings.Get(RankBadgeChoice.ModeKey(b.Kind)));
            parts.Add(Strings.Get(RankAges.NameKey(b.Age)));
        }
        return string.Join(" · ", parts);
    }

    /// <summary>"Teams · Imperial" — what the preview row and the account menu say.</summary>
    public static string ModeAndAge(ShownBadge badge) => DetailLine(null, null, badge);

    private static string LadderKey(BadgeKind kind) => kind == BadgeKind.Team ? "MpBadgeLadderTeam" : "MpBadgeLadderSolo";
}
