using System.Collections.Generic;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>Medals of one game the player has just earned, for the window to celebrate.</summary>
/// <param name="Earned">How many medals of that game he holds now.</param>
/// <param name="Total">How many the game has.</param>
public sealed record AchievementUnlockNotice(string ModId, IReadOnlyList<MedalRef> Medals, int Earned, int Total);

/// <summary>
/// Where a new medal is shown (design 121b/121d): the card in the middle of the window, the small
/// floating notice, or nowhere but the bell. Pure.
///
/// <para>The card is for one medal, with the player looking at the launcher and doing nothing else.
/// In a room or a search it would cover what he is waiting for, and several at once would be a
/// queue of cards: those get the small notice. With a game open nothing floats at all — a window
/// over AoE3 can knock it out of full screen — and the bell keeps it.</para>
/// </summary>
public static class AchievementCelebration
{
    public enum Surface { Card, Notice, BellOnly }

    /// <summary>How long the card stays before it closes by itself (the design's 8 s).</summary>
    public const int CardSeconds = 8;

    public static Surface Decide(int medals, bool userIsLooking, bool inRoomOrSearch, bool gameRunning)
    {
        if (medals <= 0 || gameRunning) return Surface.BellOnly;
        if (!userIsLooking || inRoomOrSearch || medals > 1) return Surface.Notice;
        return Surface.Card;
    }
}
