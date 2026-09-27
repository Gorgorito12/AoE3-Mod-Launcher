using System.IO;
using System.Text;
using System.Text.Json;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services.Repair;

/// <summary>Why a repair was refused. <see cref="None"/> means it may run.</summary>
internal enum RepairRefusal
{
    None,
    /// <summary>The detect-only base game: its "install folder" is the player's own AoE3.</summary>
    StockGame,
    NotInstalled,
    /// <summary>DelegatedExternal / Manual: the launcher has nothing to re-lay the mod from.</summary>
    NotLauncherInstallable,
    /// <summary>The folder's manifest names ANOTHER mod — repairing would lay our payload over it.</summary>
    ForeignManifest,
}

/// <summary>
/// The ONE answer to "may Repair run on this install?". Before it, the only check lived on a
/// gear-menu item inside the collapsed legacy panel, while the visible entry points — Mod
/// Properties' Repair button and the Verify panel's Retry — reached <c>RepairInstallAsync</c>
/// directly. So a DelegatedExternal/Manual mod could be "repaired" (failing with "no install
/// URL", or worse, downloading WoL's payload through the WoL-named global override), and a
/// folder whose manifest belongs to another mod had nothing stopping our payload being laid on
/// it. Keyed on profile DATA only (install mechanism, stock flag, manifest owner) — never a mod id.
/// </summary>
internal static class RepairEligibility
{
    internal static RepairRefusal Evaluate(ModProfile profile, bool installFolderExists, string? manifestModId)
    {
        if (profile.IsStockGame) return RepairRefusal.StockGame;
        if (profile.UpdateMechanism != ModUpdateMechanism.WolPatcher
            && profile.UpdateMechanism != ModUpdateMechanism.GitHubReleases)
            return RepairRefusal.NotLauncherInstallable;
        if (!installFolderExists) return RepairRefusal.NotInstalled;
        // Positive evidence only, exactly like detection and uninstall: a legacy manifest with
        // no mod id, or no manifest at all, is not a reason to refuse.
        if (ModInstallProbe.ManifestClaimsAnotherMod(manifestModId, profile.Id, profile.PreviousIds))
            return RepairRefusal.ForeignManifest;
        return RepairRefusal.None;
    }

    /// <summary>
    /// The <c>modId</c> recorded in an install's manifest, read WITHOUT deserialising the whole
    /// file — a WoL manifest lists ~54,000 files and hashes (tens of MB), and this runs on the UI
    /// thread before a repair starts. <c>modId</c> is the first property the launcher writes, so
    /// the first 64 KB always holds it; a manifest where it is absent reads as "no evidence".
    /// </summary>
    internal static string? ReadManifestModId(string installPath)
    {
        foreach (var name in new[] { InstallManifest.FileName, InstallManifest.LegacyFileName })
        {
            try
            {
                var path = Path.Combine(installPath, name);
                if (!File.Exists(path)) continue;
                var buffer = new byte[64 * 1024];
                int read;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    read = fs.Read(buffer, 0, buffer.Length);
                return ReadModId(buffer.AsSpan(0, read));
            }
            catch
            {
                // Unreadable is no evidence either way.
            }
        }
        return null;
    }

    internal static string? ReadModId(ReadOnlySpan<byte> json)
    {
        // Skip a UTF-8 BOM if the file carries one.
        if (json.Length >= 3 && json[0] == 0xEF && json[1] == 0xBB && json[2] == 0xBF) json = json[3..];
        var reader = new Utf8JsonReader(json, isFinalBlock: false, state: default);
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == 1
                    && reader.ValueTextEquals("modId"u8))
                {
                    if (!reader.Read()) return null;
                    return reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                }
            }
        }
        catch (JsonException)
        {
            // Malformed or truncated before modId: no evidence.
        }
        return null;
    }
}
