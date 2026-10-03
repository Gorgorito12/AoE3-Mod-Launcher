using System;
using System.Collections.Generic;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Placement (design 55): a player's first 10 rated matches in 1v1 (5 in teams) place him. Until
/// then he is NOT ranked — no position, no badge — and his rating is written with a "?" ("1580?")
/// wherever it appears: the table, the profile, the room.
///
/// <para><b>This replaces the old "provisional" idea entirely.</b> That one was a judgement about a
/// Glicko deviation (rd &gt; 110) and was measured to be true of nearly everybody; placement is a
/// count the player can see the end of. The server decides it (it sends <c>placement_played</c> /
/// <c>placement_required</c>, <c>inPlacement</c>); this class only reads it, and when the server
/// says nothing (an older backend) nothing is marked.</para>
/// </summary>
public static class PlacementView
{
    /// <summary>Whether a player is still being placed, from the counts the server sent.</summary>
    public static bool InPlacement(int? played, int? required)
        => played is int p && required is int r && r > 0 && p > 0 && p < r;

    /// <summary>The number part of a rating, rounded the way every surface rounds it.</summary>
    public static string RatingText(double rating) => Math.Round(rating).ToString("0");

    /// <summary>How many rated matches are still missing, never negative.</summary>
    public static int Remaining(int played, int required) => Math.Max(0, required - Math.Max(0, played));

    /// <summary>What one placement segment shows.</summary>
    public enum Segment
    {
        /// <summary>Not played yet.</summary>
        Pending,
        /// <summary>Played — shown grey to everybody except the player himself.</summary>
        Played,
        Win,
        Loss,
    }

    /// <summary>
    /// The segments of a placement bar: one per required match. Another player's are only ever
    /// <see cref="Segment.Played"/> or <see cref="Segment.Pending"/> — the server does not send his
    /// results, and the design says the others see the progress in grey, with no wins or losses.
    /// The player's own bar uses his results when they are known, oldest first.
    /// </summary>
    public static IReadOnlyList<Segment> Segments(
        int played,
        int required,
        IReadOnlyList<PlacementResultEntry>? ownResults)
    {
        var list = new List<Segment>(Math.Max(0, required));
        for (var i = 0; i < required; i++)
        {
            if (i >= played)
            {
                list.Add(Segment.Pending);
                continue;
            }
            if (ownResults != null && i < ownResults.Count)
            {
                var r = ownResults[i].Result;
                list.Add(r >= 0.999 ? Segment.Win : r <= 0.001 ? Segment.Loss : Segment.Played);
            }
            else
            {
                list.Add(Segment.Played);
            }
        }
        return list;
    }
}
