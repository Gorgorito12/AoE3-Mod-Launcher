using System;
using System.IO;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Free-disk-space checks + a conservative estimate of what an install / repair
/// needs, so the launcher can WARN a user with too little space before starting
/// (an install that fails half-way on a full disk is the thing this prevents).
///
/// The estimate is deliberately conservative and network-free (per the product
/// decision): the dominant, variable cost — the AoE3 clone (~10 GB) — is measured
/// exactly (<see cref="FolderCloneService.CountCloneableBytes"/>); everything else
/// (the compressed payload download, its extraction to temp, the mod overlay, and
/// a safety headroom) is folded into a single fixed allowance rather than fetched.
/// It's a warning, not a hard gate — the caller lets the user proceed anyway.
/// </summary>
/// <summary>
/// Which volume is short and by how much, for an operation that writes to two of them.
/// </summary>
/// <param name="Drive">Root of the volume that doesn't have room, for the message to name.</param>
public record DiskSpaceShortfall(string Drive, long RequiredBytes, long FreeBytes);

public static class DiskSpaceService
{
    public const long MiB = 1024L * 1024;
    public const long GiB = 1024L * MiB;

    /// <summary>
    /// Space an install needs ON TOP of the measured AoE3 clone: the payload
    /// download (compressed) + its extraction to temp + the mod overlay + a
    /// safety headroom, as one conservative fixed number.
    /// </summary>
    public const long InstallExtraAllowanceBytes = 4 * GiB;

    /// <summary>
    /// Space a repair needs. Repair re-overlays the mod only (NO AoE3 clone), so
    /// this covers just the payload download + extraction + overlay + headroom.
    /// </summary>
    public const long RepairAllowanceBytes = 3 * GiB;

    /// <summary>
    /// Headroom on the INSTALL volume for an operation that re-lays the overlay (repair, a
    /// version switch). Small on purpose and separate from <see cref="RepairAllowanceBytes"/>:
    /// re-laying overwrites files that are already there, so the install folder barely grows —
    /// the multi-GB download and extraction land in <c>%TEMP%</c>, which is the other side of
    /// the same <see cref="Check(string?, long, string?, long)"/> call.
    /// </summary>
    public const long OverlayHeadroomBytes = 1 * GiB;

    /// <summary>
    /// Free bytes on the volume that holds <paramref name="path"/>, or -1 when it
    /// can't be determined (invalid path, removed drive, access error). Never
    /// throws — callers treat -1 as "unknown, don't warn".
    /// </summary>
    public static long SafeFreeSpace(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return -1;
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root)) return -1;
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// Conservative total space a full (clone + payload) install needs, given the
    /// measured clone size. Pure — unit-testable. A zero/negative clone size (an
    /// unmeasured or missing source) contributes nothing, leaving just the fixed
    /// allowance.
    /// </summary>
    public static long EstimateInstallRequirement(long cloneBytes)
        => Math.Max(0, cloneBytes) + InstallExtraAllowanceBytes;

    /// <summary>
    /// True when <paramref name="freeBytes"/> is a real reading (>= 0) AND below
    /// <paramref name="requiredBytes"/>. An unknown reading (-1) is never a
    /// warning — we don't cry wolf when we can't measure.
    /// </summary>
    public static bool IsShort(long freeBytes, long requiredBytes)
        => freeBytes >= 0 && freeBytes < requiredBytes;

    /// <summary>
    /// The volume that hasn't got room for an operation writing to two of them — typically a
    /// download staged in <c>%TEMP%</c> whose result lands in the install folder — or null when
    /// both are fine.
    ///
    /// <para><b>When both paths are on the SAME volume the requirements are ADDED, not checked
    /// separately.</b> That is the whole reason this exists as one function: two independent
    /// checks each pass on a drive with 4 GB free and 3 GB needed on each side, and the operation
    /// still fills the disk. The two callers that predate this each looked at a single path and
    /// could not have got that right.</para>
    ///
    /// <para>An unmeasurable volume yields no warning, keeping the rule
    /// <see cref="IsShort"/> already follows — we don't cry wolf when we can't measure. The drive
    /// root comes back with the answer so the message can NAME it: "3 GB short on C:" is
    /// actionable when the user was looking at D:.</para>
    /// </summary>
    public static DiskSpaceShortfall? Check(
        string? destPath, long destRequired, string? tempPath, long tempRequired)
        => Check(destPath, destRequired, tempPath, tempRequired, SafeFreeSpace);

    /// <summary>
    /// Core of <see cref="Check"/> with the free-space reading injected, so the rules can be
    /// pinned by tests instead of depending on whatever the machine running them happens to have
    /// free. Same seam as <see cref="UserDataService.PickUserDataFolder"/>.
    /// </summary>
    internal static DiskSpaceShortfall? Check(
        string? destPath, long destRequired, string? tempPath, long tempRequired,
        Func<string?, long> freeSpace)
    {
        var destRoot = RootOf(destPath);
        var tempRoot = RootOf(tempPath);

        if (destRoot != null && tempRoot != null
            && string.Equals(destRoot, tempRoot, StringComparison.OrdinalIgnoreCase))
            return ShortfallFor(destPath, destRequired + tempRequired, freeSpace);

        return ShortfallFor(destPath, destRequired, freeSpace)
            ?? ShortfallFor(tempPath, tempRequired, freeSpace);
    }

    private static DiskSpaceShortfall? ShortfallFor(
        string? path, long required, Func<string?, long> freeSpace)
    {
        if (string.IsNullOrWhiteSpace(path) || required <= 0) return null;
        var free = freeSpace(path);
        if (!IsShort(free, required)) return null;
        return new DiskSpaceShortfall(RootOf(path) ?? path!, required, free);
    }

    /// <summary>Volume root of a path, or null when it has none we can read.</summary>
    private static string? RootOf(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var root = Path.GetPathRoot(path);
            return string.IsNullOrEmpty(root) ? null : root;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Human-readable size (GB/MB/…). Small, dependency-free.</summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "?";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return u >= 3 ? $"{v:0.0} {units[u]}" : $"{v:0} {units[u]}";
    }
}
