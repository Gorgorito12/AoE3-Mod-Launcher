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

    /// <summary>The host's rating, so the rooms table can show it beside their name.
    /// Null on a backend that predates it, and null for a player with no rating row —
    /// both mean "don't paint a number", never "1500".</summary>
    [JsonPropertyName("rating")]
    public double? Rating { get; set; }
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

    /// <summary>How many players took part in the match. Computed server-side
    /// as COUNT(match_participants) for this match. 0 on an old backend that
    /// doesn't emit the field → the UI hides the "N players" chip.</summary>
    [JsonPropertyName("player_count")]
    public int PlayerCount { get; set; }

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

    /// <summary>
    /// SHA-256 of the recording this result was read from, when there was one.
    ///
    /// <para>The server keeps it under a unique index, so one recording can score at
    /// most one match. Everything that picks the right <c>.age3Yrec</c> runs on the
    /// machine of the person who benefits from the answer, which makes it good against
    /// accidents and worth nothing against intent; this is the half that lives
    /// somewhere the player cannot edit.</para>
    ///
    /// <para>Null when the game was not recorded — the common case — and the server
    /// treats a missing hash as "no recording", not as a duplicate.</para>
    /// </summary>
    [JsonPropertyName("replay_sha256")]
    public string? ReplaySha256 { get; set; }

    /// <summary>
    /// The match's own fingerprint, read out of the recording: the map seed and the host
    /// clock beside it.
    ///
    /// <para>The seed is what makes every machine generate the same map, so both players
    /// of one game carry it and two different games do not — which is how the server can
    /// tell whether the two of them read the SAME match without comparing a single name.
    /// (Comparing AoE3 profile names was the obvious idea and was rejected: they are
    /// often nothing like the Discord account and are awkward to change.)</para>
    ///
    /// <para>It also identifies the GAME rather than the FILE, so re-packing a recording
    /// no longer gets past the duplicate check.</para>
    ///
    /// <para>Null when there was no recording, and never sent as 0 — see the send sites.
    /// The host clock is carried but nothing is allowed to DEPEND on it until a
    /// two-machine test confirms both sides record the same value.</para>
    /// </summary>
    [JsonPropertyName("game_seed")]
    public long? GameSeed { get; set; }

    [JsonPropertyName("game_host_time")]
    public long? GameHostTime { get; set; }
}

/// <summary>
/// Our own reading of a match somebody else reported, for <c>POST /matches/confirm</c>.
///
/// <para>Reporting stays host-only — every client reaches the end of the match, and N
/// reporters would insert N copies of it. This is the smaller, separate thing: the other
/// player's launcher already reads its own recording and works out its own result, and
/// until now threw it away. It is <b>evidence only</b> and gates nothing; the server
/// stores it beside the host's claim so that in a few weeks there is a real answer to
/// "do the two readings ever disagree, and how often does the second one even arrive".</para>
///
/// <para>Keyed by lobby rather than by match on purpose: the guest usually leaves the game
/// BEFORE the host, so this often arrives while the match row does not exist yet.</para>
/// </summary>
public class ConfirmMatchRequest
{
    [JsonPropertyName("lobby_id")]
    public string LobbyId { get; set; } = "";

    /// <summary>
    /// OUR score, as our own recording tells it: 1 won, 0 lost, 0.5 could not be read.
    ///
    /// <para>0.5 is sent rather than withheld, deliberately. How often a player cannot
    /// read their own recording is precisely the number that decides whether agreement
    /// could ever be required — staying quiet about it would leave the evidence counting
    /// only the cases that went well.</para>
    /// </summary>
    [JsonPropertyName("result")]
    public double Result { get; set; } = 0.5;

    [JsonPropertyName("replay_sha256")]
    public string? ReplaySha256 { get; set; }

    /// <summary>
    /// The match's own fingerprint, read out of the recording: the map seed and the host
    /// clock beside it.
    ///
    /// <para>The seed is what makes every machine generate the same map, so both players
    /// of one game carry it and two different games do not — which is how the server can
    /// tell whether the two of them read the SAME match without comparing a single name.
    /// (Comparing AoE3 profile names was the obvious idea and was rejected: they are
    /// often nothing like the Discord account and are awkward to change.)</para>
    ///
    /// <para>It also identifies the GAME rather than the FILE, so re-packing a recording
    /// no longer gets past the duplicate check.</para>
    ///
    /// <para>Null when there was no recording, and never sent as 0 — see the send sites.
    /// The host clock is carried but nothing is allowed to DEPEND on it until a
    /// two-machine test confirms both sides record the same value.</para>
    /// </summary>
    [JsonPropertyName("game_seed")]
    public long? GameSeed { get; set; }

    [JsonPropertyName("game_host_time")]
    public long? GameHostTime { get; set; }
}

/// <summary>Answer to <c>POST /matches/confirm</c>. <c>Matched</c> is false when the host
/// has not reported yet, which is normal — the report ties it when it arrives.</summary>
public class ConfirmMatchResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("matched")]
    public bool Matched { get; set; }
}

public class ReportMatchResponse
{
    [JsonPropertyName("match_id")]
    public string MatchId { get; set; } = "";

    /// <summary>
    /// Whether the server actually moved anyone's rating. Defaults to false, which is
    /// what an older backend that says nothing effectively means — and false is the safe
    /// direction: it shows no rating claim rather than inventing one.
    /// </summary>
    [JsonPropertyName("rated")]
    public bool Rated { get; set; }

    /// <summary>
    /// Why it did not, when it did not. The launcher NEVER works this out for itself:
    /// what counts is the server's policy (which mods have a ladder, what a plausible
    /// match looks like), and keeping a second copy here is exactly how the card came to
    /// promise "it counted towards no one's rating" about matches the backend was busy
    /// rating. See <c>MatchOutcomeView.UnratedNoteKey</c> for the strings.
    /// </summary>
    [JsonPropertyName("unrated_reason")]
    public string? UnratedReason { get; set; }

    [JsonPropertyName("rating_changes")]
    public List<RatingChange> RatingChanges { get; set; } = new();
}

/// <summary>
/// A player's standing, from <c>GET /matches/elo/:userId</c>. Everything here lives on the
/// server — the launcher stores none of it.
///
/// <para><b><see cref="Wins"/> and <see cref="Losses"/> count DECIDED games only</b>, so they
/// do not add up to <see cref="GamesPlayed"/>: a match whose outcome could not be read is
/// recorded as 0.5 and is neither. That gap is the normal case, not an anomaly — see
/// <c>MultiplayerTab.FormatWinrate</c> for why the rate must divide by the two rather than by
/// the total. Both are 0 against a backend that predates them, which reads the same as "no
/// decided games yet" and hides the line either way.</para>
/// </summary>
public class EloSnapshot
{
    [JsonPropertyName("rating")]
    public double Rating { get; set; }

    /// <summary>Glicko deviation — high means the rating is still provisional.</summary>
    [JsonPropertyName("rd")]
    public double Rd { get; set; }

    [JsonPropertyName("games_played")]
    public int GamesPlayed { get; set; }

    [JsonPropertyName("wins")]
    public int Wins { get; set; }

    [JsonPropertyName("losses")]
    public int Losses { get; set; }
}

public class RatingChange
{
    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = "";

    /// <summary>
    /// This player's score for the match: 1 won, 0 lost, 0.5 "nobody could tell".
    ///
    /// <para>Carried alongside the ratings because the ratings alone cannot tell the
    /// GUEST what happened to them. The host derives the verdict from the recording on
    /// their own disk; the guest has no recording, so without this their end-of-match
    /// card could only say "no result" even when they had won. Null on a backend that
    /// predates it, which reads as unknown.</para>
    /// </summary>
    [JsonPropertyName("result")]
    public double? Result { get; set; }

    [JsonPropertyName("rating_before")]
    public double? RatingBefore { get; set; }

    [JsonPropertyName("rating_after")]
    public double? RatingAfter { get; set; }
}

/// <summary>
/// Everything the community strip needs, from <c>GET /stats/community</c>.
///
/// <para>One endpoint for two cards on purpose: they appear on the same click, and the
/// request budget is per IP — shared by everyone behind the same Radmin network — so two
/// routes would cost exactly twice as much for nothing. Every field is optional in
/// practice: a backend that predates this answers 404 and both cards simply stay hidden.
/// </para>
/// </summary>
public class CommunityStats
{
    [JsonPropertyName("generated_at")]
    public string GeneratedAt { get; set; } = "";

    /// <summary>Decided games the server required before ranking anyone.</summary>
    [JsonPropertyName("min_decided")]
    public int MinDecided { get; set; }

    [JsonPropertyName("leaderboard")]
    public List<LeaderboardRow> Leaderboard { get; set; } = new();

    [JsonPropertyName("activity")]
    public ActivityBuckets? Activity { get; set; }
}

/// <summary>One row of the ladder.</summary>
public class LeaderboardRow
{
    /// <summary>
    /// Position, decided by the server's own ordering.
    ///
    /// <para>Never recomputed here. If the launcher renumbered after filtering its copy,
    /// the fourth player would be shown as the third, and two people looking at the same
    /// table would read different numbers.</para>
    /// </summary>
    [JsonPropertyName("rank")]
    public int Rank { get; set; }

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

    [JsonPropertyName("games_played")]
    public int GamesPlayed { get; set; }

    /// <summary>Decided wins and losses. As everywhere, these two are the denominator of
    /// the percentage — not <see cref="GamesPlayed"/>.</summary>
    [JsonPropertyName("wins")]
    public int Wins { get; set; }

    [JsonPropertyName("losses")]
    public int Losses { get; set; }
}

/// <summary>
/// When rooms get opened, bucketed by hour.
///
/// <para>The buckets are in the timezone the payload names, which is UTC — the server has
/// no idea where a given player lives. <c>CommunityStatsView.ToLocalHours</c> is what turns
/// them into the viewer's own day. And the source is rooms OPENED, not matches played, so
/// the card's wording has to say that.</para>
/// </summary>
public class ActivityBuckets
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    [JsonPropertyName("window_days")]
    public int WindowDays { get; set; }

    [JsonPropertyName("timezone")]
    public string Timezone { get; set; } = "UTC";

    /// <summary>Rooms in the whole window. The card hides itself when this is too small
    /// to call anything a peak — see <c>CommunityStatsView.MinSampleRooms</c>.</summary>
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("hours")]
    public List<ActivityHour> Hours { get; set; } = new();
}

public class ActivityHour
{
    [JsonPropertyName("hour")]
    public int Hour { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }
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

    /// <summary>
    /// The member's Glicko rating and deviation, read on join so the roster can show
    /// everyone's ELO — asking <c>/matches/elo</c> once per player would not fit in
    /// 20 requests a minute shared across everyone behind the same Radmin NAT.
    ///
    /// <para><b>Nullable on purpose, both of them.</b> A plain <c>double</c> would
    /// arrive as 0.0 from a backend that doesn't send these, which is indistinguishable
    /// from a real value. Null means the server said nothing, and the roster then shows
    /// nothing — the server also leaves them out for a player with no rating row, so the
    /// 1500 it hands new players never travels and can never be painted as earned.</para>
    ///
    /// <para><see cref="Rd"/> matters as much as the rating: it is what decides whether
    /// the number means anything yet. See <c>RatingDisplay.ShouldShow</c>.</para>
    /// </summary>
    [JsonPropertyName("rating")]
    public double? Rating { get; set; }

    [JsonPropertyName("rd")]
    public double? Rd { get; set; }
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
