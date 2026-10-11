using System.Collections;
using System.Collections.Generic;
using System.Text.Json;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// What a tournament refusal's <c>details</c> say, read the same way whether they came from the
/// real server (a <see cref="JsonElement"/> in a dictionary) or from the preview's simulator
/// (plain strings, numbers and lists) — so one sentence is shown for one refusal either way.
/// </summary>
internal static class TournamentRefusals
{
    /// <summary>
    /// The string key for a <c>roster_invalid</c> refusal, from its <c>reason</c> — which player
    /// or rule is the problem, instead of "that line-up cannot enter" for all five.
    /// </summary>
    internal static string RosterReasonKey(string? reason) => reason switch
    {
        "wrong_size" => "MpTournamentErrRosterWrongSize",
        "duplicate_member" => "MpTournamentErrRosterDuplicate",
        "already_entered" => "MpTournamentErrRosterTaken",
        "member_not_eligible" => "MpTournamentErrRosterIneligible",
        "not_team_member" => "MpTournamentErrRosterNotMember",
        _ => "MpTournamentErrRoster",
    };

    /// <summary>
    /// Why a played bracket match has not moved (<c>pending_report.state</c>), as a string key,
    /// or null for a state this launcher does not know — which then simply says nothing.
    /// </summary>
    internal static string? PendingReportKey(string? state) => state switch
    {
        "awaiting_confirmation" => "MpTournamentReportAwaiting",
        "teams_mismatch" => "MpTournamentReportSidesMismatch",
        "lineups_do_not_match" => "MpTournamentReportLineupsMismatch",
        "undecided" => "MpTournamentReportUndecided",
        _ => null,
    };

    /// <summary>A string field of the details, or null.</summary>
    internal static string? Text(IReadOnlyDictionary<string, object?>? details, string key)
    {
        if (details == null || !details.TryGetValue(key, out var raw) || raw == null) return null;
        return raw switch
        {
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } el => el.GetString(),
            _ => null,
        };
    }

    /// <summary>How many items a list field of the details holds, or null.</summary>
    internal static int? Count(IReadOnlyDictionary<string, object?>? details, string key)
    {
        if (details == null || !details.TryGetValue(key, out var raw) || raw == null) return null;
        return raw switch
        {
            JsonElement { ValueKind: JsonValueKind.Array } el => el.GetArrayLength(),
            ICollection c => c.Count,
            _ => null,
        };
    }
}
