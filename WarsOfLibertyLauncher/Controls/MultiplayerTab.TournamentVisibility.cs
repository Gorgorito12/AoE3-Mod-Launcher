using System.Threading.Tasks;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// A tournament's visibility and invite code after it was created (2026-11-18): the OWNER only, on
/// a server that says <c>visibility_editable</c> (or the fabricated tournaments, whose simulated
/// server applies the same refusals). Both ask first, renewing in the destructive tone, because each
/// changes who can open the tournament.
/// </summary>
public partial class MultiplayerTab
{
    /// <summary>The server can change a tournament's visibility and renew its code.</summary>
    private bool VisibilityEditable => _demoTournaments || _tournaments?.VisibilityEditable == true;

    private async Task ConfirmVisibilityAsync(TournamentDetail t, string visibility)
    {
        bool toPrivate = visibility == "private";
        if (toPrivate == t.IsPrivate) return;
        bool ok = await MpAlertOverlay.ConfirmAsync(
            TabRootGrid,
            Strings.Get(toPrivate ? "TpOrgVisibilityToPrivateTitle" : "TpOrgVisibilityToPublicTitle"),
            Strings.Get(toPrivate ? "TpOrgVisibilityToPrivateBody" : "TpOrgVisibilityToPublicBody"),
            Strings.Get(toPrivate ? "TpOrgVisibilityPrivate" : "TpOrgVisibilityPublic"),
            Strings.Get("MpAlertCancel"));
        if (!ok)
        {
            RenderTournamentDetail();
            return;
        }
        DiagnosticLog.Write($"Tournament {t.Id}: visibility -> {visibility}.");
        await RunTournamentActionAsync(() => TournamentApi.SetTournamentVisibilityAsync(t.Id, visibility));
    }

    private async Task ConfirmRenewCodeAsync(TournamentDetail t)
    {
        bool ok = await MpAlertOverlay.ConfirmAsync(
            TabRootGrid,
            Strings.Get("TpOrgCodeRenewTitle"),
            Strings.Get("TpOrgCodeRenewBody"),
            Strings.Get("TpOrgCodeRenew"),
            Strings.Get("MpAlertCancel"));
        if (!ok) return;
        DiagnosticLog.Write($"Tournament {t.Id}: invite code renewed.");
        await RunTournamentActionAsync(() => TournamentApi.RenewTournamentCodeAsync(t.Id));
    }
}
