using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WarsOfLibertyLauncher.Models.Multiplayer;

// The rating system's own payloads (design handoff 55, docs/design_elo): placement, streaks,
// head-to-head, refunds, monthly highlights and room odds. Every one of them is computed by the
// SERVER; the launcher shows them and never works any of them out for itself. Every field is
// optional in practice — a backend that predates them omits it, and the part of the screen that
// would show it is simply not drawn.

/// <summary>Rated matches each ladder requires before a player is ranked: 10 in 1v1, 5 in teams.</summary>
public class PlacementRequirement
{
    [JsonPropertyName("default")]
    public int Default { get; set; }

    [JsonPropertyName("team")]
    public int Team { get; set; }
}

/// <summary>
/// A player still being PLACED on a ladder, listed after the ranked rows of the same table
/// (<c>leaderboard_placement</c> / <c>leaderboard_team_placement</c>). Another player's results are
/// never sent: only how far along he is.
/// </summary>
public class PlacementRow
{
    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("discord_username")]
    public string DiscordUsername { get; set; } = "";

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; set; }

    [JsonPropertyName("rating")]
    public double Rating { get; set; }

    [JsonPropertyName("rd")]
    public double Rd { get; set; }

    [JsonPropertyName("placement_played")]
    public int PlacementPlayed { get; set; }

    [JsonPropertyName("placement_required")]
    public int PlacementRequired { get; set; }

    [JsonPropertyName("last_rated_at")]
    public string? LastRatedAt { get; set; }

    /// <summary>Wins in a row right now, shown on a placement row too (design 55a).</summary>
    [JsonPropertyName("streak")]
    public int Streak { get; set; }
}

/// <summary>One rated placement match of the player himself, oldest first. Only ever sent to him.</summary>
public class PlacementResultEntry
{
    [JsonPropertyName("match_id")]
    public string MatchId { get; set; } = "";

    /// <summary>1 won, 0 lost.</summary>
    [JsonPropertyName("result")]
    public double Result { get; set; }

    [JsonPropertyName("rating_after")]
    public double RatingAfter { get; set; }

    [JsonPropertyName("at")]
    public string At { get; set; } = "";
}

/// <summary>A player's record against one opponent on one ladder. In teams, each opposing player counts.</summary>
public class HeadToHeadEntry
{
    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; set; }

    [JsonPropertyName("wins")]
    public int Wins { get; set; }

    [JsonPropertyName("losses")]
    public int Losses { get; set; }

    [JsonPropertyName("last_at")]
    public string LastAt { get; set; } = "";

    public int Games => Wins + Losses;
}

/// <summary>
/// One player's standing on ONE ladder (<c>ladders.default</c> / <c>ladders.team</c> of
/// <c>GET /matches/elo/:id</c>): the Profile's mode card and its "against each opponent" table.
/// </summary>
public class LadderStanding
{
    [JsonPropertyName("rating")]
    public double Rating { get; set; }

    /// <summary>The deviation as of now — it grows while the player does not play.</summary>
    [JsonPropertyName("rd")]
    public double Rd { get; set; }

    [JsonPropertyName("games_played")]
    public int GamesPlayed { get; set; }

    /// <summary>Position among ranked players; 0 = not ranked yet (placement); null = unknown.</summary>
    [JsonPropertyName("ladder_rank")]
    public int? LadderRank { get; set; }

    [JsonPropertyName("ladder_size")]
    public int? LadderSize { get; set; }

    [JsonPropertyName("placement_played")]
    public int PlacementPlayed { get; set; }

    [JsonPropertyName("placement_required")]
    public int PlacementRequired { get; set; }

    /// <summary>Only for the player himself; null for everybody else.</summary>
    [JsonPropertyName("placement_results")]
    public List<PlacementResultEntry>? PlacementResults { get; set; }

    [JsonPropertyName("inactive")]
    public bool Inactive { get; set; }

    [JsonPropertyName("last_rated_at")]
    public string? LastRatedAt { get; set; }

    /// <summary>Decided RATED matches on this ladder.</summary>
    [JsonPropertyName("wins")]
    public int Wins { get; set; }

    [JsonPropertyName("losses")]
    public int Losses { get; set; }

    [JsonPropertyName("streak_current")]
    public int StreakCurrent { get; set; }

    [JsonPropertyName("streak_best")]
    public int StreakBest { get; set; }

    [JsonPropertyName("loss_streak_best")]
    public int LossStreakBest { get; set; }

    /// <summary>When the current streak ended for lack of play (last match + 14 days); null otherwise.</summary>
    [JsonPropertyName("streak_ended_at")]
    public string? StreakEndedAt { get; set; }

    /// <summary>Null until placement is finished.</summary>
    [JsonPropertyName("rating_peak")]
    public double? RatingPeak { get; set; }

    [JsonPropertyName("rating_peak_at")]
    public string? RatingPeakAt { get; set; }

    [JsonPropertyName("rating_low")]
    public double? RatingLow { get; set; }

    [JsonPropertyName("rating_low_at")]
    public string? RatingLowAt { get; set; }

    [JsonPropertyName("head_to_head")]
    public List<HeadToHeadEntry> HeadToHead { get; set; } = new();

    [JsonPropertyName("head_to_head_total")]
    public int HeadToHeadTotal { get; set; }

    public bool InPlacement => GamesPlayed > 0 && PlacementRequired > 0 && GamesPlayed < PlacementRequired;
}

public class LadderStandings
{
    [JsonPropertyName("default")]
    public LadderStanding? Default { get; set; }

    [JsonPropertyName("team")]
    public LadderStanding? Team { get; set; }
}

/// <summary>
/// Points given back because an opponent the player lost to was banned for cheating — summed into
/// ONE notice per ladder. Never names the banned player.
/// </summary>
public class RefundNotice
{
    [JsonPropertyName("refund_id")]
    public string RefundId { get; set; } = "";

    /// <summary><c>default</c> (1v1) or <c>team</c>.</summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "default";

    [JsonPropertyName("points")]
    public int Points { get; set; }

    [JsonPropertyName("matches")]
    public int Matches { get; set; }

    [JsonPropertyName("rating_before")]
    public double RatingBefore { get; set; }

    [JsonPropertyName("rating_after")]
    public double RatingAfter { get; set; }

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; set; } = "";

    [JsonPropertyName("seen")]
    public bool Seen { get; set; }

    public bool IsTeam => Mode == "team";
}

public class RefundsSeenRequest
{
    [JsonPropertyName("refund_ids")]
    public List<string>? RefundIds { get; set; }
}

public class RefundsSeenResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }
}

/// <summary>A player named by the monthly highlights.</summary>
public class HighlightPlayer
{
    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; set; }

    /// <summary>Climb: points gained.</summary>
    [JsonPropertyName("points")]
    public int? Points { get; set; }

    /// <summary>Climb: post-placement matches in the month; most matches: rated matches.</summary>
    [JsonPropertyName("matches")]
    public int? Matches { get; set; }

    /// <summary>Best streak: wins in a row inside the month.</summary>
    [JsonPropertyName("wins")]
    public int? Wins { get; set; }
}

public class HighlightPerMode
{
    [JsonPropertyName("default")]
    public HighlightPlayer? Default { get; set; }

    [JsonPropertyName("team")]
    public HighlightPlayer? Team { get; set; }
}

/// <summary>One month's highlights (design 55l). Months run from the 1st at 06:00 UTC.</summary>
public class MonthHighlights
{
    /// <summary><c>YYYY-MM</c>.</summary>
    [JsonPropertyName("month")]
    public string Month { get; set; } = "";

    [JsonPropertyName("starts_at")]
    public string StartsAt { get; set; } = "";

    [JsonPropertyName("ends_at")]
    public string EndsAt { get; set; } = "";

    /// <summary>True while the month is still running ("so far").</summary>
    [JsonPropertyName("so_far")]
    public bool SoFar { get; set; }

    [JsonPropertyName("total_rated")]
    public int TotalRated { get; set; }

    /// <summary>The fewest post-placement matches in the month for "biggest climb".</summary>
    [JsonPropertyName("min_matches")]
    public int MinMatches { get; set; }

    [JsonPropertyName("biggest_climb")]
    public HighlightPerMode? BiggestClimb { get; set; }

    [JsonPropertyName("most_matches")]
    public HighlightPlayer? MostMatches { get; set; }

    [JsonPropertyName("best_streak")]
    public HighlightPerMode? BestStreak { get; set; }
}

public class MonthlyHighlights
{
    [JsonPropertyName("current")]
    public MonthHighlights? Current { get; set; }

    [JsonPropertyName("previous")]
    public MonthHighlights? Previous { get; set; }
}

/// <summary>
/// The win probability a room shows (design 55g/55h), computed by the SERVER with Glicko — the
/// launcher never computes it. Whole percent, 1-99. Sent in <c>room_state.odds</c> and in every
/// <c>room_odds</c> frame, recomputed whenever somebody arrives, leaves or changes team.
/// </summary>
public class RoomOdds
{
    /// <summary><c>default</c> (1v1) or <c>team</c>.</summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "default";

    /// <summary>Team rooms: <c>{"1": 58, "2": 42}</c>; null while a team is empty.</summary>
    [JsonPropertyName("teams")]
    public Dictionary<string, int>? Teams { get; set; }

    /// <summary>1v1 rooms: each player's chance by user id; null until both seats are taken.</summary>
    [JsonPropertyName("players")]
    public Dictionary<string, int>? Players { get; set; }
}

/// <summary>The bracket a history row was played for.</summary>
public class MatchTournamentRef
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("round")]
    public int? Round { get; set; }

    [JsonPropertyName("rounds_total")]
    public int? RoundsTotal { get; set; }
}
