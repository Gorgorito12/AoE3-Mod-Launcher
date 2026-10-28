using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// A rating still being PLACED is its owner's alone. The player sees his own "1490?"; everybody
/// else sees "In placement 3/10" where the number would be, and no win probability while anybody in
/// the room is placing — the percentage would give the hidden number back.
///
/// <para>The server withholds the number (the lobby backend's <c>src/elo/placementPrivacy.ts</c>), so
/// with a current server it simply does not arrive; this file is the launcher's half — the shared
/// helpers every surface that can show ANOTHER player's rating goes through, so the room, the
/// players panel, the rooms list, the team columns, the odds, the ranking, the history and the
/// result card cannot answer the question differently. Against an older server, which still sends
/// the number, the same helpers hide it.</para>
///
/// <para>The decision itself is <see cref="RatingDisplay.LookFor"/> (pure, tested); what lives
/// here is only the plumbing that needs the tab's state — whose number is the viewer's, how many
/// matches a ladder requires, and the viewer's own standing for his own rows.</para>
/// </summary>
public partial class MultiplayerTab
{
    /// <summary>Marks the "in placement 3/10" that stands where somebody else's rating would be.</summary>
    internal const string HiddenPlacementTag = "HiddenPlacement";

    /// <summary>Marks the odds card that says the odds are withheld during placement.</summary>
    internal const string OddsHiddenPlacementTag = "OddsHiddenPlacement";

    /// <summary>
    /// Rated matches a ladder requires before a player is placed: what the community payload says,
    /// else what the viewer's own standing says, else the rule's own 10 / 5. Never 0 — a label
    /// that reads "3/0" is worse than the rule written in.
    /// </summary>
    private int PlacementRequiredShown(bool team)
    {
        var fromCommunity = CommunityStatsView.PlacementRequiredFor(_communityStats, team);
        return fromCommunity > 0 ? fromCommunity : PlacementRequiredFor(ladder: null, team);
    }

    /// <summary>
    /// The rating to print beside a badge, and WHICH ladder it belongs to. The number follows the
    /// badge (the maintainer's call for handoff 51): a player shown with the team badge is shown
    /// with the team rating.
    ///
    /// <para><b>Chosen by the badge whenever ANY team datum arrived</b> — the rating, the count or
    /// the flag. It used to require the team RATING, which a current server withholds from
    /// everybody else while that ladder is placing the player: the team badge would then have
    /// stood beside his 1v1 number, which is wrong twice over — another ladder's figure, and
    /// presented as if it were the one he is being placed on. Only a server that sent nothing about
    /// the team ladder falls back to the 1v1 pair, as before.</para>
    /// </summary>
    internal static (double? Rating, double? Rd, int? Games, bool? InPlacement, bool Team) RatingForLadder(
        ShownBadge? badge, double? rating, double? rd, double? ratingTeam, double? rdTeam,
        int? games = null, int? gamesTeam = null, bool? inPlacement = null, bool? inPlacementTeam = null)
    {
        var team = badge is { Kind: BadgeKind.Team }
                   && (ratingTeam.HasValue || gamesTeam.HasValue || inPlacementTeam.HasValue);
        return team
            ? (ratingTeam, rdTeam, gamesTeam, inPlacementTeam, true)
            : (rating, rd, games, inPlacement, false);
    }

    /// <summary>Whether a player is being placed on a ladder: the server's flag, else his counts.</summary>
    private bool? PlacingOn(bool team, bool? serverFlag, int? games)
        => RatingDisplay.IsPlacing(serverFlag, games, PlacementRequiredShown(team));

    /// <summary>How a rating reads beside a name for this viewer (see <see cref="RatingDisplay.LookFor"/>).</summary>
    private RatingDisplay.Look RatingLookFor(bool isViewer, bool team, double? rd, int? games, bool? serverFlag)
        => RatingDisplay.LookFor(isViewer, rd, games, PlacingOn(team, serverFlag, games));

    /// <summary>
    /// The viewer's own number on a ladder, from his own standing: for his own row where the server
    /// sent none — a current server withholds a placing rating from every shared payload, the
    /// viewer's own copy included, and his launcher already holds it. Null when not known.
    /// </summary>
    private static (double? Rating, double? Rd, int? Games) OwnRating(EloSnapshot? standing, bool team)
        => standing == null ? (null, null, null)
         : team ? (standing.RatingTeam, standing.RdTeam, standing.GamesPlayedTeam)
         : (standing.Rating, standing.Rd, standing.GamesPlayed);

    /// <summary>
    /// The rating beside a name in a list (the rooms table's host, the players panel), as this
    /// viewer may see it: the number, "unrated", the viewer's own "1490?", or — somebody else still
    /// being placed — "3/10". Null when there is nothing to draw.
    /// </summary>
    private TextBlock? BuildRatingTextFor(
        bool isViewer, bool team, double? rating, double? rd, int? games, bool? serverFlag,
        double numberSize, double unitSize)
    {
        if (isViewer && !rating.HasValue)
        {
            var own = OwnRating(_cachedStanding, team);
            rating = own.Rating;
            rd ??= own.Rd;
            games ??= own.Games;
        }
        var required = PlacementRequiredShown(team);
        return BuildRatingText(rating, rd, numberSize, unitSize,
            gamesPlayed: games, inPlacement: RatingDisplay.IsPlacing(serverFlag, games, required),
            isViewer: isViewer, placementRequired: required);
    }

    /// <summary>The tooltip of a hidden rating: the long label, then why there is no number.</summary>
    internal static string HiddenPlacementTip(int? played, int required)
        => Strings.Format("MpEloHiddenPlacementTip", RatingDisplay.PlacementLabel(played, required, shortForm: false));

    /// <summary>
    /// Whether anybody PLAYING in the room (spectators aside) is being placed on the ladder the
    /// room's odds are computed on. The server already withholds the odds then
    /// (<c>hidden_reason</c>); this is the second layer for a server that still sends them.
    /// </summary>
    private bool AnyRoomPlayerPlacing(bool team)
        => _roomMembers.Values
            .Where(m => !string.Equals(m.Role, "spectator", StringComparison.Ordinal))
            .Any(m => PlacingOn(team, team ? m.InPlacementTeam : m.InPlacement,
                team ? m.GamesPlayedTeam : m.GamesPlayed) == true);

    /// <summary>
    /// Whether a member's team rating is withheld from THIS viewer — he is somebody else and still
    /// being placed on the team ladder. Drives his row in the team column and the team's average:
    /// an average beside one visible partner gives the hidden rating straight back.
    /// </summary>
    private bool TeamRatingHiddenFromViewer(RoomMemberEntry m)
        => !IsRoomViewer(m.UserId) && PlacingOn(team: true, m.InPlacementTeam, m.GamesPlayedTeam) == true;

    /// <summary>
    /// The 1v1 odds card while the odds are withheld for placement: the sentence that says why, and
    /// no bar — an empty bar would read as a 0 % chance. Shown only once both seats are taken, the
    /// moment odds would otherwise have appeared.
    /// </summary>
    private FrameworkElement BuildOddsHiddenCard()
        => new Border
        {
            Child = new TextBlock
            {
                Text = Strings.Get("MpOddsHiddenPlacement"),
                FontSize = (double)Application.Current.FindResource("MpMetaSize"),
                Foreground = (Brush)Application.Current.FindResource("MpRankMutedText"),
                TextWrapping = TextWrapping.Wrap,
            },
            Tag = OddsHiddenPlacementTag,
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(14, 12, 14, 12),
            CornerRadius = (CornerRadius)Application.Current.FindResource("RadiusPanel"),
            Background = (Brush)Application.Current.FindResource("MpPanel"),
            BorderBrush = (Brush)Application.Current.FindResource("MpRimMedium"),
            BorderThickness = new Thickness(1),
        };

    /// <summary>
    /// The 1v1 rival's placement progress from a stored row, when the server marked him as still
    /// being placed — what the guest's result card says instead of his number. Null otherwise.
    /// </summary>
    private static (int Played, int Required)? RivalPlacementFrom(MatchHistoryRow row, string myId)
    {
        if (row.PlayerCount != 2) return null;
        var rival = row.Participants?.FirstOrDefault(p => !string.Equals(p.UserId, myId, StringComparison.Ordinal));
        return rival is { InPlacement: true, PlacementPlayed: int played, PlacementRequired: int required }
            ? (played, required)
            : null;
    }

    /// <summary>How many PLAYERS (spectators aside) the room holds right now.</summary>
    private int RoomPlayerCount()
        => _roomMembers.Values.Count(m => !string.Equals(m.Role, "spectator", StringComparison.Ordinal));

    /// <summary>
    /// A rooms-list host's rating on the ladder the row's badge shows — the one rule both the HOST
    /// cell and the narrow layout's subtitle use, so the two layouts of one row cannot disagree.
    /// </summary>
    private static (double? Rating, double? Rd, int? Games, bool? InPlacement, bool Team) HostRatingOf(
        LobbySummary lobby, ShownBadge? hostBadge)
        => RatingForLadder(hostBadge, lobby.Host?.Rating, lobby.Host?.Rd,
            lobby.Host?.RatingTeam, lobby.Host?.RdTeam,
            lobby.Host?.GamesPlayed, lobby.Host?.GamesPlayedTeam,
            lobby.Host?.InPlacement, lobby.Host?.InPlacementTeam);

    /// <summary>
    /// The host with his rating, in words, for the narrow layout's subtitle: "Pedro 1534 ELO",
    /// "Pedro unrated", "Pedro · in placement 3/10" for somebody else still being placed, or just
    /// the name when there is no number to give. <paramref name="mine"/>: the room is the viewer's.
    /// </summary>
    private string HostWithRatingWords(LobbySummary lobby, string hostName, bool mine)
    {
        var badge = BadgeOf(
            RoomFormats.Resolve(lobby.Competitive, lobby.MaxPlayers, lobby.SpectatorSlots),
            lobby.Host?.BadgeMode, lobby.Host?.LadderRank, lobby.Host?.LadderRankTeam);
        var (rating, rd, games, flag, team) = HostRatingOf(lobby, badge);
        if (mine && !rating.HasValue)
        {
            var own = OwnRating(_cachedStanding, team);
            rating = own.Rating;
            rd ??= own.Rd;
            games ??= own.Games;
        }
        var look = RatingLookFor(mine, team, rd, games, flag);
        if (look == RatingDisplay.Look.HiddenPlacement)
        {
            var required = PlacementRequiredShown(team);
            return games is int g
                ? Strings.Format("MpResultRivalInPlacement", hostName, Math.Clamp(g, 0, required), required)
                : hostName + " · " + RatingDisplay.PlacementLabel(null, required, shortForm: false);
        }
        if (!RatingDisplay.ShouldShow(rating)) return hostName;
        return look switch
        {
            RatingDisplay.Look.Unrated => hostName + " " + Strings.Get("MpEloUnrated"),
            RatingDisplay.Look.Provisional => hostName + " " + Strings.Format("MpEloProvisional",
                (int)Math.Round(rating!.Value)) + " " + Strings.Get("MpEloUnit"),
            _ => hostName + " " + Strings.Format("MpChipElo", (int)Math.Round(rating!.Value)),
        };
    }
}
