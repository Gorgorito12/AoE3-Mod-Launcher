using System;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>What the global chat's "X opened a room" card knows about its room.</summary>
public enum RoomEventStatus
{
    /// <summary>The room is in the last rooms list: the card says what the Rooms page says.</summary>
    Live,
    /// <summary>No list asked since the announcement: the announcement is all there is.</summary>
    Announced,
    /// <summary>A list asked AFTER the announcement no longer has it: the room is gone.</summary>
    Closed,
}

/// <summary>The <c>lobby_created</c> frame, as the card keeps it.</summary>
public sealed record RoomEventSnapshot(
    string Id, string Title, string ModId, string HostUserId, string HostLogin,
    int MaxPlayers, bool Competitive, DateTime AnnouncedUtc, int? GameRules = null);

/// <summary>
/// The chat's room card is no longer a snapshot: it follows the room through the same rooms list
/// and the same <see cref="RoomActionRule"/> the Rooms page uses, so it stops offering Join the
/// moment the room fills, starts or closes. It used to be built once from the announcement and
/// say "Join" for ever. Pure, so <c>RoomEventCardTests</c> pins it.
/// </summary>
public static class RoomEventCard
{
    /// <summary>
    /// A room missing from a list asked BEFORE it was announced proves nothing (the list was older
    /// than the room); missing from one asked after, it closed.
    /// </summary>
    public static RoomEventStatus StatusOf(bool inList, DateTime? listAskedUtc, DateTime announcedUtc) =>
        inList ? RoomEventStatus.Live
        : listAskedUtc is DateTime asked && asked > announcedUtc ? RoomEventStatus.Closed
        : RoomEventStatus.Announced;

    /// <summary>
    /// The room as the announcement describes it, so the card goes through the Rooms page's own
    /// rule until the list brings the real one. Created with one player in it (the host), open,
    /// and with no password: password rooms are never announced.
    /// </summary>
    public static LobbySummary FromAnnouncement(RoomEventSnapshot s) => new()
    {
        Id = s.Id,
        Title = s.Title,
        ModId = s.ModId,
        MaxPlayers = s.MaxPlayers,
        Competitive = s.Competitive,
        GameRules = s.GameRules,
        CurrentPlayers = 1,
        Status = "open",
        Host = new LobbyHost { Id = s.HostUserId, DiscordUsername = s.HostLogin, DisplayName = s.HostLogin },
    };

    /// <summary>What decides whether a card needs repainting, so an unchanged room costs nothing.</summary>
    public static string KeyOf(RoomEventStatus status, LobbySummary? room, RoomAction? action) =>
        $"{status}|{room?.CurrentPlayers}|{room?.MaxPlayers}|{room?.SpectatorsPresent}|{room?.Status}|{room?.Title}|{room?.GameRules}|{action}";
}
