namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// "Update mods automatically" (Settings, OFF by default; 2026-11-18). Pure, pinned by
/// <c>ModAutoUpdatePolicyTests</c>. <see cref="AutoUpdatePolicy"/> is the launcher's OWN update;
/// this is a mod's.
///
/// <para><b>The refusals are the feature, and their order is pinned.</b> An automatic update
/// never opens a window: anything that would need an answer — administrator rights, a private
/// key, too little disk — is refused here, and the player keeps the ordinary bell and Update
/// button. It never starts while a game runs (started by the launcher or not), in a room, a match
/// or a search, or while another operation runs, and gives up on a version after two failed
/// attempts so a broken release cannot loop.</para>
///
/// <para>Scope: every installed mod, both mechanisms — the one on screen first, then one at a time
/// the mods the notification sweep found behind, each on its own service. Never automated:
/// installs, repairs, version picks, the baseline rescue, an install of unknown version, any copy
/// other than a mod's active one.</para>
/// </summary>
internal static class ModAutoUpdatePolicy
{
    internal const int MaxAttemptsPerVersion = 2;

    internal enum Verdict
    {
        Go,
        NotEnabled,
        ChecksOff,
        Offline,
        NothingPending,
        VersionUnknown,
        GaveUp,
        Busy,
        GameRunning,
        InMultiplayer,
        NeedsAdmin,
        ShortOnDisk,
    }

    internal sealed record Facts(
        bool Enabled,
        bool ChecksAllowed,
        bool Offline,
        bool UpdatePending,
        bool VersionKnown,
        int Attempts,
        bool Busy,
        bool GameRunning,
        bool InMultiplayer,
        bool NeedsAdmin,
        bool ShortOnDisk);

    internal static Verdict Decide(Facts f)
    {
        if (!f.Enabled) return Verdict.NotEnabled;
        // Metered connections: "don't check" means "don't download" too.
        if (!f.ChecksAllowed) return Verdict.ChecksOff;
        if (f.Offline) return Verdict.Offline;
        if (!f.UpdatePending) return Verdict.NothingPending;
        if (!f.VersionKnown) return Verdict.VersionUnknown;
        if (f.Attempts >= MaxAttemptsPerVersion) return Verdict.GaveUp;
        if (f.Busy) return Verdict.Busy;
        if (f.GameRunning) return Verdict.GameRunning;
        if (f.InMultiplayer) return Verdict.InMultiplayer;
        if (f.NeedsAdmin) return Verdict.NeedsAdmin;
        if (f.ShortOnDisk) return Verdict.ShortOnDisk;
        return Verdict.Go;
    }
}
