using System;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Where Radmin VPN is installed, remembered between polls.
///
/// <para>The banner's 3-s poll — and since the assistant moved off the UI thread, the assistant's
/// too — read the whole Windows uninstall list, both hives, every time, for an answer that changes
/// when the user installs or uninstalls Radmin. Only a HIT is kept, re-checked against the disk on
/// every read and dropped after <see cref="Rescan"/>, so an uninstall is noticed at once (the file
/// is gone) and a move within minutes. A miss is never kept: installing Radmin must be picked up on
/// the very next poll.</para>
///
/// <para>Thread-safe by construction: the whole hit is one immutable reference, swapped whole.</para>
/// </summary>
internal sealed class RadminInstallCache
{
    /// <summary>How long a remembered install is trusted before the registry is read again.</summary>
    public static readonly TimeSpan Rescan = TimeSpan.FromMinutes(5);

    private sealed record Hit(string Exe, string? Version, long AtMs);
    private Hit? _hit;

    /// <summary>The remembered install, or null when there is none or it no longer holds.</summary>
    public (string? exe, string? version)? Get(long nowMs, Func<string, bool> fileExists)
    {
        var hit = _hit;
        if (hit == null) return null;
        if (nowMs - hit.AtMs >= Rescan.TotalMilliseconds || !fileExists(hit.Exe))
        {
            _hit = null;
            return null;
        }
        return (hit.Exe, hit.Version);
    }

    /// <summary>Remembers a hit; a miss (null exe) forgets instead.</summary>
    public void Store(string? exe, string? version, long nowMs)
        => _hit = string.IsNullOrEmpty(exe) ? null : new Hit(exe, version, nowMs);

    public void Forget() => _hit = null;
}
