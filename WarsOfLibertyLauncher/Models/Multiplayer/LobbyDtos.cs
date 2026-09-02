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

    /// <summary>
    /// When the account first signed in, ISO-8601 UTC. Read for the profile header's
    /// "joined in {month}" line.
    ///
    /// <para><c>GET /me</c> has always sent this — it is <c>users.created_at</c>, present
    /// since the first migration — and no client had ever deserialized it. Null on a
    /// response that omits it, which is the only reason the header's line is optional
    /// rather than required.</para>
    /// </summary>
    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }
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

    /// <summary>
    /// The host's Glicko deviation, and the ONLY thing that separates "1500 because they have
    /// never played" from "1500 because that is where they landed" — the rating alone cannot.
    ///
    /// <para>Nullable for the same reason the rating is: a backend that predates it says
    /// nothing, and nothing must not be read as a claim about the player. See
    /// <c>RatingDisplay.IsUnrated</c>.</para>
    /// </summary>
    [JsonPropertyName("rd")]
    public double? Rd { get; set; }
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

    /// <summary>
    /// Whether this room's match counts towards the ladder. Read-only from the server, which
    /// is the only side that decides it — see the note on <see cref="CreateLobbyRequest"/>.
    /// </summary>
    [JsonPropertyName("competitive")]
    public bool Competitive { get; set; }

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

    [JsonPropertyName("competitive")]
    public bool Competitive { get; set; }

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

    /// <summary>
    /// Ask for a competitive room. <b>A request, not a decision:</b> the server refuses it for a
    /// mod with no ladder and creates a casual room instead, so what the room actually is comes
    /// back on <see cref="CreateLobbyResponse.Competitive"/> and nowhere else. The launcher must
    /// never work out which mods are ranked — that policy lives on the server, and the day the
    /// list changes a local copy would be quietly wrong.
    /// </summary>
    [JsonPropertyName("competitive")]
    public bool Competitive { get; set; }
}

public class CreateLobbyResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    /// <summary>What the room actually is, after the server's clamp. Trust this, not the request.</summary>
    [JsonPropertyName("competitive")]
    public bool Competitive { get; set; }
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

/// <summary>
/// One player of a finished match, as returned inside a history row.
///
/// <para>Mirrors the shape <see cref="LobbyHost"/> already uses for a room's host, because
/// the backend sends the two the same way — one convention for "a named person", not a
/// second one invented for history.</para>
/// </summary>
public class MatchHistoryParticipant
{
    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("discord_username")]
    public string DiscordUsername { get; set; } = "";

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; set; }

    [JsonPropertyName("team")]
    public int Team { get; set; }

    /// <summary>
    /// The civilization this player used, by the name their mod calls it — resolved on the
    /// reporting machine by <c>CivNameResolver</c>, never a raw index.
    ///
    /// <para>Null is ordinary and means several different things: the match was reported before
    /// this existed, the mod ships no loose civ list, or the recording could not be joined to
    /// the room's roster. Every surface has to render without it.</para>
    /// </summary>
    [JsonPropertyName("civ")]
    public string? Civ { get; set; }

    /// <summary>1.0 won, 0.0 lost, 0.5 the outcome could not be read — which is what MOST
    /// stored rows carry, and is never a draw. Classified through
    /// <c>MatchOutcomeView.Classify</c> so this file's meaning of the number and the result
    /// card's cannot drift apart.</summary>
    [JsonPropertyName("result")]
    public double Result { get; set; }

    [JsonPropertyName("rating_before")]
    public double? RatingBefore { get; set; }

    [JsonPropertyName("rating_after")]
    public double? RatingAfter { get; set; }
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

    /// <summary>
    /// The POOL the map was drawn from ("ESOC Maps"), not the map itself — the recording carries
    /// both and only one of them was ever stored. A civ's record on the competitive pool and its
    /// record across whatever anyone picks are different questions.
    /// </summary>
    [JsonPropertyName("map_pool")]
    public string? MapPool { get; set; }

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

    /// <summary>Everyone who played, with their own win/loss — so a history row can name the
    /// opponent instead of only counting heads.
    ///
    /// <para>EMPTY on a backend that predates the field, exactly like <see cref="PlayerCount"/>
    /// before it: the row then renders as it always did, keeping the "N players" chip that the
    /// names otherwise replace.</para></summary>
    [JsonPropertyName("participants")]
    public List<MatchHistoryParticipant> Participants { get; set; } = new();

    /// <summary>
    /// Whether the server scored this match. <b>Null means an older row or an older
    /// backend — "we don't know" — and must not be read as "it counted".</b>
    /// </summary>
    [JsonPropertyName("rated")]
    public bool? Rated { get; set; }

    /// <summary>
    /// Why it did not score, verbatim from the server, or null when it did (or when the
    /// row predates the field).
    ///
    /// <para>The launcher deliberately does NOT work this out for itself — the policy of
    /// what counts lives on the server, and the one time a copy of it lived here too the
    /// two drifted. It maps to an explanation through
    /// <see cref="Services.Multiplayer.MatchOutcomeView.UnratedNoteKey"/>, the same
    /// mapping the end-of-match card uses, so the two surfaces cannot give a player
    /// different answers about the same match.</para>
    /// </summary>
    [JsonPropertyName("unrated_reason")]
    public string? UnratedReason { get; set; }
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

    /// <summary>
    /// The POOL the map was drawn from ("ESOC Maps"), not the map itself — the recording carries
    /// both and only one of them was ever stored. A civ's record on the competitive pool and its
    /// record across whatever anyone picks are different questions.
    /// </summary>
    [JsonPropertyName("map_pool")]
    public string? MapPool { get; set; }

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

/// <summary>
/// A match that had been stored WITHOUT a result was decided afterwards, from a recording one
/// of its two players read after the report had already gone out.
///
/// <para>It arrives on the always-on global socket rather than the room's, because by the time
/// it exists the room has been closed for minutes: the correction has nowhere else to land.</para>
/// </summary>
public sealed record MatchRatedNotice(
    string MatchId,
    string ModId,
    string? MapName,
    double? Result,
    double? RatingBefore,
    double? RatingAfter);

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
/// <summary>
/// One civilization's record in one mod at one VERSION — a row of <c>GET /stats/civs</c>.
///
/// <para>The version matters and is why this is not grouped by mod alone: a balance figure that
/// averages 1.2.0e with 1.2.0f stops meaning anything at exactly the moment a modder changes
/// something, which is the moment it exists for.</para>
/// </summary>
public class CivStatEntry
{
    [JsonPropertyName("mod_id")]
    public string ModId { get; set; } = "";

    /// <summary>The mod's combined fingerprint — the exact build these matches were played on.</summary>
    [JsonPropertyName("mod_version")]
    public string ModVersion { get; set; } = "";

    [JsonPropertyName("civ")]
    public string Civ { get; set; } = "";

    /// <summary>Matches it was played in, INCLUDING the ones whose outcome could not be read.</summary>
    [JsonPropertyName("played")]
    public int Played { get; set; }

    [JsonPropertyName("wins")]
    public int Wins { get; set; }

    [JsonPropertyName("losses")]
    public int Losses { get; set; }

    /// <summary>Mean match length in seconds, or null when the server did not say.</summary>
    [JsonPropertyName("avg_seconds")]
    public int? AvgSeconds { get; set; }
}

/// <summary><c>GET /stats/civs</c> — how each civilization is doing, per mod and version.</summary>
public class CivStatsResponse
{
    [JsonPropertyName("generated_at")]
    public string GeneratedAt { get; set; } = "";

    /// <summary>
    /// How many rated matches contributed a civilization at all. Printed above the table: with
    /// civilizations only reported from one build onwards, a near-zero here is the honest state
    /// for a while and a blank table would read as broken instead of as new.
    /// </summary>
    [JsonPropertyName("rated_matches_with_civ")]
    public int RatedMatchesWithCiv { get; set; }

    [JsonPropertyName("civs")]
    public List<CivStatEntry> Civs { get; set; } = new();
}

public class CommunityStats
{
    [JsonPropertyName("generated_at")]
    public string GeneratedAt { get; set; } = "";

    /// <summary>Decided games the server required before ranking anyone.</summary>
    [JsonPropertyName("min_decided")]
    public int MinDecided { get; set; }

    [JsonPropertyName("leaderboard")]
    public List<LeaderboardRow> Leaderboard { get; set; } = new();

    /// <summary>
    /// The TEAM ladder — 2v2 and 3v3 share it, and it is separate from the 1v1 one above.
    ///
    /// <para><b>Nullable, and the null is the point.</b> A backend that predates the team
    /// ladder simply omits the field, and that is "this server has no team ladder", not "the
    /// team ladder is empty" — the same distinction <see cref="Totals"/> makes, and for the
    /// same reason: drawing an empty table under a live heading reports something false.</para>
    /// </summary>
    [JsonPropertyName("leaderboard_team")]
    public List<LeaderboardRow>? LeaderboardTeam { get; set; }

    /// <summary>
    /// How many players are on each ladder in total, which is NOT the length of the lists
    /// above: those are capped at the page size, so once the league passes 50 the list
    /// length would quietly start reporting the page as the size of the league.
    ///
    /// <para>Read by the profile's "rank N of M". 0 on a backend that predates the fields,
    /// and the profile then shows the rank alone rather than inventing a denominator.</para>
    /// </summary>
    [JsonPropertyName("ranked_players")]
    public int RankedPlayers { get; set; }

    [JsonPropertyName("ranked_players_team")]
    public int RankedPlayersTeam { get; set; }

    [JsonPropertyName("activity")]
    public ActivityBuckets? Activity { get; set; }

    /// <summary>How much has been going on lately. Null on a backend that predates the
    /// field, and the card is then not drawn at all — never zeroes, which would report a
    /// dead community rather than an unanswered question.</summary>
    [JsonPropertyName("totals")]
    public CommunityTotals? Totals { get; set; }

    /// <summary>The last few matches ANYONE played. Empty on an older backend, and the
    /// card then falls back to the viewer's own history exactly as before.</summary>
    [JsonPropertyName("recent_matches")]
    public List<CommunityMatch> RecentMatches { get; set; } = new();
}

/// <summary>
/// The community's recent numbers.
///
/// <para>Each window travels WITH its figure rather than being assumed here: the server
/// owns how far back it looked, and a card that hardcoded "30 days" would start lying the
/// day that constant changed.</para>
/// </summary>
/// <summary>One map and how many matches were played on it.</summary>
public class MapCount
{
    [JsonPropertyName("map")]
    public string Map { get; set; } = "";

    [JsonPropertyName("matches")]
    public int Matches { get; set; }
}

public class CommunityTotals
{
    /// <summary>Days behind <see cref="Matches"/>.</summary>
    [JsonPropertyName("window_days")]
    public int WindowDays { get; set; }

    /// <summary>Matches reported inside that window.</summary>
    [JsonPropertyName("matches")]
    public int Matches { get; set; }

    /// <summary>Days behind <see cref="Players"/> — shorter than the match window, since
    /// this answers "is anyone around" rather than "when do people play".</summary>
    [JsonPropertyName("players_window_days")]
    public int PlayersWindowDays { get; set; }

    /// <summary>Players seen inside that shorter window.</summary>
    [JsonPropertyName("players")]
    public int Players { get; set; }

    /// <summary>
    /// The most-played maps with their match counts, newest server only.
    ///
    /// <para><b>Null on any backend that does not send it</b>, which every deployed one does
    /// today — and the UI hides the whole card rather than drawing an empty one. That is the
    /// same degradation every field added here follows: a card of zeroes reads as a bug, an
    /// absent card reads as a feature that has not arrived.</para>
    /// </summary>
    [JsonPropertyName("top_maps")]
    public List<MapCount>? TopMaps { get; set; }

    /// <summary>The most-played map, or null when no match in the window named one.
    /// Null and not "" — the row is drawn only when there is a map to name.</summary>
    [JsonPropertyName("top_map")]
    public string? TopMap { get; set; }

    [JsonPropertyName("top_map_matches")]
    public int TopMapMatches { get; set; }
}

/// <summary>
/// One finished match, from anyone — the community's activity rather than the viewer's.
///
/// <para>Carries the same <see cref="MatchHistoryParticipant"/> list the History rows do,
/// assembled by the same helper on the server, so "who played and who won" is read one way
/// on this side too.</para>
/// </summary>
public class CommunityMatch
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("mod_id")]
    public string ModId { get; set; } = "";

    [JsonPropertyName("map_name")]
    public string? MapName { get; set; }

    // No map_pool here on purpose: the community strip's sub-line already carries mod, map and
    // the civ matchup, and it trims from the right — a fourth segment would push out one that
    // says more. The pool is stored, and read from the history row, which has room for it.

    [JsonPropertyName("duration_seconds")]
    public int DurationSeconds { get; set; }

    /// <summary>When the server RECORDED it, not when the client says it started — the
    /// one of the two timestamps that cannot be moved by a wrong clock.</summary>
    [JsonPropertyName("reported_at")]
    public string ReportedAt { get; set; } = "";

    [JsonPropertyName("participants")]
    public List<MatchHistoryParticipant> Participants { get; set; } = new();
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

    /// <summary>The member's AoE3 profile name, reported via <c>set_ingame_name</c>. It is what
    /// joins a slot in the recording to a Discord account, which is what lets a team game be
    /// stored with real teams. Null until reported, and null from a backend that predates it —
    /// the team map then refuses and the match is reported with no teams, as it always was.
    /// camelCase key, riding inside the room-state member object like the two above.</summary>
    [JsonPropertyName("ingameName")]
    public string? InGameName { get; set; }

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
