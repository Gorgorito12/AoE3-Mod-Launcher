using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WarsOfLibertyLauncher.Models.Multiplayer;

/// <summary>
/// All DTOs the launcher (de)serialises when talking to the lobby
/// backend. Property names match the JSON the backend emits exactly;
/// keeping them in one file makes it easy to spot drift between the
/// two repos.
/// </summary>

/// <summary>Result of <c>POST /auth/login/device</c>.</summary>
public class DeviceFlowStart
{
    [JsonPropertyName("user_code")]
    public string UserCode { get; set; } = "";

    [JsonPropertyName("verification_uri")]
    public string VerificationUri { get; set; } = "";

    /// <summary>Seconds between successive <c>POST /poll</c> requests.</summary>
    [JsonPropertyName("interval")]
    public int IntervalSeconds { get; set; } = 5;

    [JsonPropertyName("expires_in")]
    public int ExpiresInSeconds { get; set; } = 900;

    [JsonPropertyName("poll_handle")]
    public string PollHandle { get; set; } = "";
}

/// <summary>Successful <c>POST /auth/login/poll</c> result.</summary>
public class DeviceFlowComplete
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("token")]
    public string Token { get; set; } = "";

    [JsonPropertyName("expires_at")]
    public long ExpiresAt { get; set; }

    [JsonPropertyName("user")]
    public LobbyUserSummary User { get; set; } = new();

    [JsonPropertyName("config")]
    public ServerConfig Config { get; set; } = new();
}

public class LobbyUserSummary
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>Discord username (snowflake-account legacy field; lowercase,
    /// unique for newer accounts). May differ from <see cref="DisplayName"/>,
    /// which is Discord's user-editable "global name".</summary>
    [JsonPropertyName("discord_username")]
    public string DiscordUsername { get; set; } = "";

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; set; }
}

public class ServerConfig
{
    [JsonPropertyName("max_concurrent_users")]
    public int MaxConcurrentUsers { get; set; } = 60;

    [JsonPropertyName("max_active_games")]
    public int MaxActiveGames { get; set; } = 8;

    [JsonPropertyName("lobby_max_players")]
    public int LobbyMaxPlayers { get; set; } = 8;

    [JsonPropertyName("chat_msgs_per_min")]
    public int ChatMsgsPerMin { get; set; } = 30;
}

public class LobbyHost
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("discord_username")]
    public string DiscordUsername { get; set; } = "";

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; set; }
}

public class LobbySummary
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("mod_id")]
    public string ModId { get; set; } = "";

    [JsonPropertyName("mod_combined_hash")]
    public string ModCombinedHash { get; set; } = "";

    [JsonPropertyName("max_players")]
    public int MaxPlayers { get; set; }

    [JsonPropertyName("current_players")]
    public int CurrentPlayers { get; set; }

    [JsonPropertyName("is_private")]
    public bool IsPrivate { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; set; } = "";

    [JsonPropertyName("host")]
    public LobbyHost Host { get; set; } = new();
}

public class LobbyListResponse
{
    [JsonPropertyName("lobbies")]
    public List<LobbySummary> Lobbies { get; set; } = new();
}

/// <summary>One member in a lobby's roster (from GET /lobbies/:id), used by the
/// "see who's in a room without joining" peek.</summary>
public class LobbyMember
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("discord_username")]
    public string DiscordUsername { get; set; } = "";

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; set; }

    [JsonPropertyName("is_ready")]
    public bool IsReady { get; set; }

    [JsonPropertyName("role")]
    public string Role { get; set; } = "player";
}

/// <summary>GET /lobbies/:id — a lobby's details WITH its member roster.</summary>
public class LobbyDetail
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("mod_id")]
    public string ModId { get; set; } = "";

    [JsonPropertyName("max_players")]
    public int MaxPlayers { get; set; }

    [JsonPropertyName("current_players")]
    public int CurrentPlayers { get; set; }

    [JsonPropertyName("is_private")]
    public bool IsPrivate { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("host_user_id")]
    public string HostUserId { get; set; } = "";

    [JsonPropertyName("members")]
    public List<LobbyMember> Members { get; set; } = new();
}

public class CreateLobbyRequest
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("mod_id")]
    public string ModId { get; set; } = "";

    [JsonPropertyName("mod_combined_hash")]
    public string ModCombinedHash { get; set; } = "";

    [JsonPropertyName("max_players")]
    public int MaxPlayers { get; set; } = 8;

    [JsonPropertyName("password")]
    public string? Password { get; set; }
}

public class CreateLobbyResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";
}

public class JoinLobbyRequest
{
    [JsonPropertyName("mod_combined_hash")]
    public string ModCombinedHash { get; set; } = "";

    [JsonPropertyName("password")]
    public string? Password { get; set; }
}

public class JoinLobbyResponse
{
    [JsonPropertyName("lobby_id")]
    public string LobbyId { get; set; } = "";

    [JsonPropertyName("join_token")]
    public string JoinToken { get; set; } = "";

    [JsonPropertyName("ws_url")]
    public string WsUrl { get; set; } = "";
}

public class QuotaSnapshot
{
    [JsonPropertyName("requests")]
    public QuotaRequests Requests { get; set; } = new();

    [JsonPropertyName("lobbies")]
    public QuotaCount Lobbies { get; set; } = new();

    [JsonPropertyName("players")]
    public QuotaCount Players { get; set; } = new();
}

public class QuotaRequests
{
    [JsonPropertyName("used_today")]
    public int UsedToday { get; set; }

    [JsonPropertyName("budget")]
    public int Budget { get; set; }

    [JsonPropertyName("soft_limit")]
    public int SoftLimit { get; set; }

    [JsonPropertyName("hard_limit")]
    public int HardLimit { get; set; }
}

public class QuotaCount
{
    [JsonPropertyName("active")]
    public int Active { get; set; }

    [JsonPropertyName("max")]
    public int Max { get; set; }
}

/// <summary>One line of match history as returned by GET /matches/history/:userId.</summary>
public class MatchHistoryRow
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("mod_id")]
    public string ModId { get; set; } = "";

    [JsonPropertyName("map_name")]
    public string? MapName { get; set; }

    [JsonPropertyName("duration_seconds")]
    public int DurationSeconds { get; set; }

    [JsonPropertyName("started_at")]
    public string StartedAt { get; set; } = "";

    [JsonPropertyName("ended_at")]
    public string EndedAt { get; set; } = "";

    [JsonPropertyName("replay_object_key")]
    public string? ReplayObjectKey { get; set; }

    [JsonPropertyName("team")]
    public int Team { get; set; }

    [JsonPropertyName("civ")]
    public string? Civ { get; set; }

    [JsonPropertyName("score")]
    public int Score { get; set; }

    [JsonPropertyName("result")]
    public double Result { get; set; }

    [JsonPropertyName("rating_before")]
    public double? RatingBefore { get; set; }

    [JsonPropertyName("rating_after")]
    public double? RatingAfter { get; set; }
}

public class MatchHistoryResponse
{
    [JsonPropertyName("matches")]
    public List<MatchHistoryRow> Matches { get; set; } = new();
}

/// <summary>A player participating in a finished match.</summary>
public class MatchParticipantReport
{
    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("team")]
    public int Team { get; set; }

    [JsonPropertyName("civ")]
    public string? Civ { get; set; }

    [JsonPropertyName("score")]
    public int Score { get; set; }

    /// <summary>1.0 = win, 0.5 = draw, 0.0 = loss.</summary>
    [JsonPropertyName("result")]
    public double Result { get; set; }
}

/// <summary>Body of <c>POST /matches</c>.</summary>
public class ReportMatchRequest
{
    [JsonPropertyName("lobby_id")]
    public string? LobbyId { get; set; }

    [JsonPropertyName("mod_id")]
    public string ModId { get; set; } = "";

    [JsonPropertyName("mod_combined_hash")]
    public string ModCombinedHash { get; set; } = "";

    [JsonPropertyName("map_name")]
    public string? MapName { get; set; }

    [JsonPropertyName("started_at")]
    public string StartedAt { get; set; } = "";

    [JsonPropertyName("ended_at")]
    public string EndedAt { get; set; } = "";

    [JsonPropertyName("duration_seconds")]
    public int DurationSeconds { get; set; }

    [JsonPropertyName("participants")]
    public List<MatchParticipantReport> Participants { get; set; } = new();
}

public class ReportMatchResponse
{
    [JsonPropertyName("match_id")]
    public string MatchId { get; set; } = "";

    [JsonPropertyName("rating_changes")]
    public List<RatingChange> RatingChanges { get; set; } = new();
}

public class RatingChange
{
    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("rating_before")]
    public double? RatingBefore { get; set; }

    [JsonPropertyName("rating_after")]
    public double? RatingAfter { get; set; }
}

/// <summary>Result of <c>POST /replays/upload-url</c>.</summary>
public class ReplayUploadHandle
{
    [JsonPropertyName("upload_url")]
    public string UploadUrl { get; set; } = "";

    [JsonPropertyName("method")]
    public string Method { get; set; } = "PUT";

    [JsonPropertyName("max_bytes")]
    public long MaxBytes { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresInSeconds { get; set; }
}

/// <summary>One chat line as broadcast over the room WebSocket.</summary>
public class WsChatLine
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("login")]
    public string Login { get; set; } = "";

    [JsonPropertyName("body")]
    public string Body { get; set; } = "";

    /// <summary>Milliseconds since the Unix epoch.</summary>
    [JsonPropertyName("at")]
    public long AtMs { get; set; }
}

/// <summary>Per-member entry inside <see cref="WsRoomState.Members"/>.</summary>
public class WsRoomMemberFlags
{
    [JsonPropertyName("ready")]
    public bool Ready { get; set; }

    /// <summary>Display login (Discord username) at the time the member
    /// joined. Empty when the server didn't have it cached (rare; only
    /// legacy lobbies that pre-date the member-with-login schema). The
    /// JSON key stays the generic "login" so the room WS protocol is
    /// provider-agnostic.</summary>
    [JsonPropertyName("login")]
    public string Login { get; set; } = "";

    /// <summary>The member's Radmin VPN IP (26.x.x.x), reported via
    /// <c>set_radmin_ip</c> once they're actually on the VPN. Lets every peer
    /// ICMP-ping every other peer for the in-game per-player ping column. Null
    /// until reported. camelCase JSON key — it rides inside the room-state
    /// member object alongside ready/login (which are also bare names), unlike
    /// the snake_case top-level frames.</summary>
    [JsonPropertyName("radminIp")]
    public string? RadminIp { get; set; }

    /// <summary>The member's Discord avatar URL, so the roster can paint their real
    /// photo. Null for legacy rooms that don't send it → the roster falls back to a
    /// monogram. camelCase key, rides inside the room-state member object.</summary>
    [JsonPropertyName("avatarUrl")]
    public string? AvatarUrl { get; set; }
}

/// <summary>Initial snapshot sent by the DO when our hello succeeds.</summary>
public class WsRoomState
{
    [JsonPropertyName("lobby_id")]
    public string LobbyId { get; set; } = "";

    [JsonPropertyName("host_user_id")]
    public string? HostUserId { get; set; }

    [JsonPropertyName("members")]
    public Dictionary<string, WsRoomMemberFlags> Members { get; set; } = new();

    [JsonPropertyName("chat")]
    public List<WsChatLine> Chat { get; set; } = new();
}

// (Pre-n2n: this file used to carry WsPeerEndpoint / WsPeerAnnounce /
//  WsPeerRelay DTOs the launcher serialised over the room WS to
//  coordinate STUN-based hole-punching with each peer. With n2n the
//  edges find each other through the supernode by community name —
//  there is no per-peer launcher-side signaling left to model.)

/// <summary>Standard error envelope returned by every endpoint on failure.</summary>
public class ApiErrorBody
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("details")]
    public Dictionary<string, object?>? Details { get; set; }
}
