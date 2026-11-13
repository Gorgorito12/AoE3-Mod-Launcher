using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WarsOfLibertyLauncher.Models.Multiplayer;

/// <summary>
/// The quick-match queue's wire types (backend <c>src/matchmaking</c>). The live list arrives on
/// the global socket as <c>mm_searchers_changed</c>; <c>GET /matchmaking/searchers</c> answers the
/// same shape for the first paint.
/// </summary>
public class QueueSearcher
{
    [JsonPropertyName("ticket_id")] public string TicketId { get; set; } = "";
    [JsonPropertyName("user_id")] public string UserId { get; set; } = "";
    [JsonPropertyName("display_name")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("avatar_url")] public string? AvatarUrl { get; set; }
    /// <summary>1v1 ladder place; 0 below the bar; absent when unknown.</summary>
    [JsonPropertyName("ladder_rank")] public int? LadderRank { get; set; }
    [JsonPropertyName("badge_mode")] public string? BadgeMode { get; set; }
    /// <summary>Omitted while the player is being placed (placement privacy).</summary>
    [JsonPropertyName("rating")] public double? Rating { get; set; }
    [JsonPropertyName("games_played")] public int? GamesPlayed { get; set; }
    [JsonPropertyName("in_placement")] public bool InPlacement { get; set; }
    [JsonPropertyName("mod_id")] public string ModId { get; set; } = "";
    [JsonPropertyName("mod_combined_hash")] public string ModCombinedHash { get; set; } = "";
    /// <summary>"ranked", "casual" or "either".</summary>
    [JsonPropertyName("kind")] public string Kind { get; set; } = "ranked";
    [JsonPropertyName("queued_at")] public string QueuedAt { get; set; } = "";
    /// <summary>Being matched right now: no challenge can reach him.</summary>
    [JsonPropertyName("busy")] public bool Busy { get; set; }
}

public class QueueSearchersResponse
{
    [JsonPropertyName("searchers")] public List<QueueSearcher> Searchers { get; set; } = new();
}

public class MatchmakingQueueCount
{
    [JsonPropertyName("mod_id")] public string ModId { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("searching")] public int Searching { get; set; }
}

public class MatchmakingStatusResponse
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("queues")] public List<MatchmakingQueueCount> Queues { get; set; } = new();
}

/// <summary>The rival on the "match found" card (<c>mm_found.opponent</c>).</summary>
public class MatchFoundOpponent
{
    [JsonPropertyName("user_id")] public string UserId { get; set; } = "";
    [JsonPropertyName("display_name")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("avatar_url")] public string? AvatarUrl { get; set; }
    [JsonPropertyName("ladder_rank")] public int? LadderRank { get; set; }
    [JsonPropertyName("badge_mode")] public string? BadgeMode { get; set; }
    [JsonPropertyName("rating")] public double? Rating { get; set; }
    [JsonPropertyName("games_played")] public int? GamesPlayed { get; set; }
    [JsonPropertyName("in_placement")] public bool InPlacement { get; set; }
}

/// <summary><c>mm_found</c>: two players matched; each has <see cref="AcceptDeadlineMs"/> to accept.</summary>
public class MatchFoundFrame
{
    [JsonPropertyName("match_id")] public string MatchId { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "ranked";
    [JsonPropertyName("mod_id")] public string ModId { get; set; } = "";
    [JsonPropertyName("challenge")] public bool Challenge { get; set; }
    /// <summary>I sent the challenge (so the card says "waiting for X to accept").</summary>
    [JsonPropertyName("challenger")] public bool Challenger { get; set; }
    [JsonPropertyName("accept_deadline_ms")] public int AcceptDeadlineMs { get; set; } = 20000;
    [JsonPropertyName("you_are_host")] public bool YouAreHost { get; set; }
    [JsonPropertyName("opponent")] public MatchFoundOpponent Opponent { get; set; } = new();
}
