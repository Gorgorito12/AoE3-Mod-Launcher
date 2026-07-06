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
public static class DiskSpaceService
{
    public const long GiB = 1024L * 1024 * 1024;

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
