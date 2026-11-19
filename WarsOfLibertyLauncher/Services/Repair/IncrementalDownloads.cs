namespace WarsOfLibertyLauncher.Services.Repair;

/// <summary>
/// "Download only what changed" (Settings, on by default) as ONE rule for the repair and the
/// update (2026-11-18). On: a repair restores single files when it safely can
/// (<see cref="GranularRestore"/>) and an update patches. Off: the whole download wherever one
/// exists — and the granular shadow plan is not even computed. <b>A release that ships patches
/// only still patches when off</b>: it has no full download, and refusing would re-download a
/// baseline the player already has. Pure, pinned by <c>IncrementalDownloadsTests</c>.
/// </summary>
internal static class IncrementalDownloads
{
    /// <summary>The Verify text that describes what Repair will do: the same rule, never a copy.</summary>
    internal static string VerifyRelayBodyKey(bool enabled)
        => enabled ? "VerifyRelayGranularBody" : "DlgVerifyRepairBody";

    /// <summary>Whether a repair may plan and act on a single-file restore.</summary>
    internal static bool MayRestoreFiles(bool enabled) => enabled;

    /// <summary>Whether an update may go by patches.</summary>
    internal static bool MayPatch(bool enabled, bool targetHasFullZip) => enabled || !targetHasFullZip;
}
