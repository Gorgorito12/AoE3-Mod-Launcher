using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>What the check can say about an install's key files.</summary>
internal enum ModFingerprintState
{
    /// <summary>Nothing to compare against, or a file could not be read. Shows no chip.</summary>
    Unknown,
    Ok,
    Different,
}

/// <summary>
/// The chip on each row of Settings → Mods (design 72): do the three files multiplayer compares
/// still match what the launcher recorded when it laid the mod down?
///
/// <para>It reads the same hashes version recognition reads, through the same rule: the string
/// table comes from the <c>_originals</c> snapshot, so a translation the player applied does not
/// count as a file touched by hand.</para>
/// </summary>
internal static class ModFingerprintCheck
{
    internal static ModFingerprintState Compare(
        IReadOnlyDictionary<string, string>? baseline,
        IReadOnlyDictionary<string, string>? live)
    {
        if (baseline == null || live == null || baseline.Count == 0) return ModFingerprintState.Unknown;

        foreach (var (file, expected) in baseline)
        {
            if (string.IsNullOrEmpty(expected) || !live.TryGetValue(file, out var actual)
                || string.IsNullOrEmpty(actual))
                return ModFingerprintState.Unknown;
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                return ModFingerprintState.Different;
        }
        return ModFingerprintState.Ok;
    }

    /// <summary>Hashes a few files, so it runs off the UI thread. Never throws.</summary>
    internal static async Task<ModFingerprintState> CheckAsync(string installPath, CancellationToken ct = default)
    {
        try
        {
            var manifest = InstallManifest.TryLoad(installPath);
            if (manifest == null || manifest.KeyFileHashes.Count == 0) return ModFingerprintState.Unknown;

            var live = await Task.Run(
                () => UpdateService.ComputeRecognitionKeyHashesAsync(installPath, ct), ct).ConfigureAwait(false);
            return Compare(manifest.KeyFileHashes, live);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DiagnosticLog.Write($"Fingerprint check of '{installPath}' skipped: {ex.Message}");
            return ModFingerprintState.Unknown;
        }
        catch (OperationCanceledException)
        {
            return ModFingerprintState.Unknown;
        }
    }
}
