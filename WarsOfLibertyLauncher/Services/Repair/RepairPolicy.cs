using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services.Repair;

/// <summary>What a plain repair of a GitHubReleases mod lays down.</summary>
internal enum RepairTargetKind
{
    /// <summary>Today's effective tag (approved, or the cached latest). Also the self-heal for an
    /// install whose version was never known.</summary>
    Effective,
    /// <summary>The version the player HAS — a repair restores it, it does not change it.</summary>
    Installed,
    /// <summary>The installed version cannot be laid (gone from the repo, or an external host that
    /// only serves the approved tag). Only an explicit "X → Y" from the player may change it.</summary>
    ConfirmChange,
}

internal sealed record RepairTarget(RepairTargetKind Kind, string? Tag, string? From, string? To);

/// <summary>
/// The pure rules a repair decides by. Two real bugs are why they exist:
/// <list type="bullet">
/// <item>A plain repair of a GitHubReleases mod re-laid the EFFECTIVE tag, so "Repair" silently
/// changed the version — a pinned player was moved off the pin, and Improvement Mod, whose
/// installed release was newer than the catalog's approved one, could be DOWNGRADED whenever the
/// follow-latest cache was empty.</item>
/// <item>An INTACT repair (nothing re-laid) still stamped the effective tag as installed, which
/// hid the Update button for a player who was actually behind and fired a false "update
/// finished" bell.</item>
/// </list>
/// Tags are compared for EQUALITY only. GitHub tags have no total order (Improvement Mod uses
/// dd.mm.yyyy), so "newer" or "older" is never computed here.
/// </summary>
internal static class RepairPolicy
{
    /// <param name="installed">The version on disk: the install's own manifest version, else the
    /// launcher's remembered one. Empty = unknown.</param>
    /// <param name="graphKnown">Whether the release listing could be read at all.</param>
    /// <param name="installedHasFullPayload">Whether the installed tag's release carries a full
    /// payload (single zip or split parts). Only meaningful when <paramref name="graphKnown"/>.</param>
    internal static RepairTarget PickTarget(
        string? installed, string effective, bool externalHosted, bool graphKnown, bool installedHasFullPayload)
    {
        installed = installed?.Trim();
        if (string.IsNullOrEmpty(installed))
            return new RepairTarget(RepairTargetKind.Effective, null, null, null);
        if (string.Equals(installed, effective?.Trim(), StringComparison.OrdinalIgnoreCase))
            return new RepairTarget(RepairTargetKind.Effective, null, null, null);

        // An external host pins ONE hash, for the approved tag; nothing else can be verified.
        if (externalHosted)
            return new RepairTarget(RepairTargetKind.ConfirmChange, null, installed, effective);

        // Listing unreadable (offline, rate-limited): still aim at the installed tag. If it can't
        // be resolved the repair fails before writing anything, which beats changing the version.
        if (!graphKnown || installedHasFullPayload)
            return new RepairTarget(RepairTargetKind.Installed, installed, null, null);

        return new RepairTarget(RepairTargetKind.ConfirmChange, null, installed, effective);
    }

    /// <summary>
    /// The version to record as installed after a repair, or null to leave the record alone.
    /// Only what was ACTUALLY laid may be stamped.
    /// </summary>
    /// <param name="laidVersion">What this run wrote; null when nothing was re-laid (intact).</param>
    /// <param name="ownManifestVersion">The install's own manifest version — only when the
    /// manifest belongs to this mod. An intact verify just proved the lay it describes.</param>
    /// <param name="detectedVersion">WolPatcher's MD5-detected version, if any.</param>
    internal static string? StampAfterRepair(
        ModUpdateMechanism mechanism, string? laidVersion, string? ownManifestVersion, string? detectedVersion)
    {
        if (!string.IsNullOrWhiteSpace(laidVersion)) return laidVersion.Trim();

        // Intact: nothing was written, so the only honest labels are ones that describe the
        // bytes that are already there.
        return mechanism switch
        {
            ModUpdateMechanism.GitHubReleases =>
                string.IsNullOrWhiteSpace(ownManifestVersion) ? null : ownManifestVersion.Trim(),
            _ => string.IsNullOrWhiteSpace(detectedVersion) ? null : detectedVersion.Trim(),
        };
    }

    /// <summary>
    /// Whether the repair's own result line should stand as the status once the re-check that
    /// follows every repair has finished.
    ///
    /// <para>The re-check writes "Up to date" over it, and the result was otherwise never on
    /// screen: the player pressed Repair and was told nothing about what it did. The re-check
    /// still wins when it has something to OFFER — an update or pending patches — because
    /// that is the next thing the player has to act on.</para>
    /// </summary>
    internal static bool KeepResultAfterRecheck(string? repairResult, int pendingDownloads, bool updateOffered)
        => !string.IsNullOrWhiteSpace(repairResult) && pendingDownloads == 0 && !updateOffered;
}
