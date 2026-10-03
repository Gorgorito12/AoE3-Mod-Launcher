using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Points given back after a ban (design 55n), as the player is told: once in the bell, and as a
/// banner over the affected mode's card on the profile until "Got it". The SERVER computes and
/// stores the refund; this only words it — and never names the banned player, which is why no
/// field here could.
/// </summary>
public static class RefundView
{
    /// <summary>The refunds of one ladder the player has not dismissed yet, newest first.</summary>
    public static IReadOnlyList<RefundNotice> Unseen(IReadOnlyList<RefundNotice>? refunds, bool team)
        => refunds?.Where(r => !r.Seen && r.IsTeam == team).ToList() ?? new List<RefundNotice>();

    /// <summary>"You got 34 points back: …" — several refunds of one ladder are ONE banner, summed.</summary>
    public static string Text(IReadOnlyList<RefundNotice> refunds) => Text(refunds.Sum(r => r.Points));

    /// <summary>The sentence for a number of points, singular at one.</summary>
    public static string Text(int points)
        => Strings.Format(points == 1 ? "MpRefundBodyOne" : "MpRefundBody", points);
}
