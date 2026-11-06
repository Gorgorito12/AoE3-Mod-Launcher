namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>What the Profile can say about the player's rating right now (design 95e, 95f).</summary>
internal enum ProfileRatingState
{
    Ready,
    Loading,
    Failed,
}

internal static class ProfileRatingView
{
    /// <summary>
    /// A standing in hand always wins, even over a later failure. With the launcher offline no
    /// fetch is even tried, so "loading" would be a promise nothing is keeping.
    /// </summary>
    internal static ProfileRatingState StateOf(bool hasStanding, bool fetchFailed, bool offline)
        => hasStanding ? ProfileRatingState.Ready
            : fetchFailed || offline ? ProfileRatingState.Failed
            : ProfileRatingState.Loading;
}
