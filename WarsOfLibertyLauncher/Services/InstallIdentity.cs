using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Who an install IS on this machine: the Add/Remove key it owns, the name its shortcuts carry,
/// and whether it is a disposable AoE3 clone. Written once when the install is created and then
/// CARRIED by every operation that rewrites the install in place.
/// </summary>
/// <remarks>
/// <para>This exists because the re-overlay path (repair, full GitHubReleases update, version
/// pick, baseline rescue) used to derive all of it afresh from an install label that none of its
/// callers pass. Two real consequences, both silent: repairing a COPY re-pointed the primary's
/// desktop shortcut and its <c>{guid}_is1</c> Add/Remove key — the very key WoL detection reads —
/// at the copy, and wrote the primary's GUID into the copy's manifest, so uninstalling the copy
/// afterwards deleted the primary's entry; and every re-overlay stamped <c>clonedAoe3:false</c>,
/// after which Uninstall took the overlay-only branch and left the multi-GB clone on disk.</para>
/// <para>Carrying is refused unless the previous manifest provably belongs to this mod, so a
/// foreign manifest can never lend its identity. A manifest an OLDER build already reset is
/// carried as it is — this stops the damage, it does not undo it (inferring clone status after
/// the fact would re-arm a blanket delete on a guess). Uninstall's AoE3-root veto stays the
/// backstop for a manifest that claims a clone where the player's real game lives.</para>
/// </remarks>
internal sealed record InstallIdentity(
    string ProductGuid,
    string AppName,
    bool ClonedAoe3,
    string? Aoe3SourcePath,
    bool Carried)
{
    /// <summary>The identity an install gets when it is created. An empty label is the primary.</summary>
    public static InstallIdentity Fresh(ModProfile profile, string? installLabel, bool clonedAoe3 = false,
        string? aoe3SourcePath = null)
        => new(NativeInstallService.ProductGuidFor(profile, installLabel),
               NativeInstallService.AppNameFor(profile, installLabel),
               clonedAoe3, aoe3SourcePath, Carried: false);

    /// <summary>
    /// The identity to keep when an existing install is rewritten in place. An explicit caller
    /// label still wins (a user-initiated install that names its slot); otherwise the previous
    /// manifest's identity is carried verbatim when it belongs to this mod.
    /// </summary>
    public static InstallIdentity ForReoverlay(ModProfile profile, InstallManifest? previous, string? callerLabel)
    {
        if (!string.IsNullOrWhiteSpace(callerLabel))
            return Fresh(profile, callerLabel,
                previous != null && BelongsTo(previous, profile) && previous.ClonedAoe3,
                previous != null && BelongsTo(previous, profile) ? previous.Aoe3SourcePath : null);

        if (previous == null || !BelongsTo(previous, profile))
            return Fresh(profile, null);

        // Names travel only as a PAIR: a GUID without its name (or the reverse) would create
        // shortcuts for one install and an Add/Remove key for another.
        bool namesUsable = !string.IsNullOrWhiteSpace(previous.ProductGuid)
                           && !string.IsNullOrWhiteSpace(previous.AppName);
        return namesUsable
            ? new InstallIdentity(previous.ProductGuid.Trim(), previous.AppName.Trim(),
                previous.ClonedAoe3, previous.Aoe3SourcePath, Carried: true)
            : Fresh(profile, null, previous.ClonedAoe3, previous.Aoe3SourcePath) with { Carried = true };
    }

    /// <summary>
    /// Positive evidence that <paramref name="manifest"/> was written for <paramref name="profile"/>:
    /// its mod id is ours or one we used to have, or — for a legacy manifest that never recorded
    /// one — its product GUID is ours or one of our copies'.
    /// </summary>
    internal static bool BelongsTo(InstallManifest manifest, ModProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(manifest.ModId))
            return !ModInstallProbe.ManifestClaimsAnotherMod(manifest.ModId, profile.Id, profile.PreviousIds);

        var guid = manifest.ProductGuid?.Trim() ?? "";
        if (guid.Length == 0) return false;
        var ours = profile.EffectiveProductGuid;
        return string.Equals(guid, ours, StringComparison.OrdinalIgnoreCase)
               || guid.StartsWith(ours + "_", StringComparison.OrdinalIgnoreCase);
    }
}
