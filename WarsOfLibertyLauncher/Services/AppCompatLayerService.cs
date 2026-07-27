using System;
using System.Linq;
using Microsoft.Win32;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// What Windows' <c>AppCompatFlags\Layers</c> says about one executable.
/// </summary>
/// <param name="RawValue">The value exactly as stored, e.g. <c>"~ WINXPSP3"</c>.</param>
/// <param name="AppliedByWindows">
/// The value starts with <c>~</c>, which is how the Program Compatibility Assistant marks
/// a layer IT applied on its own. Without the tilde the layer was set deliberately —
/// through the Compatibility tab or by an installer — and is the user's own choice, so we
/// must be far more careful about offering to undo it.
/// </param>
/// <param name="HasRunAsAdmin">The layer explicitly forces "run as administrator".</param>
/// <param name="HasCompatibilityMode">The layer pins an OS compatibility mode (WINXPSP3, WIN7RTM…).</param>
/// <param name="InCurrentUserHive">
/// Found under HKCU, so it is removable without elevation. A machine-wide (HKLM) layer is
/// reported but never touched: it needs admin and it isn't this user's to undo.
/// </param>
internal readonly record struct AppCompatLayerInfo(
    string RawValue,
    bool AppliedByWindows,
    bool HasRunAsAdmin,
    bool HasCompatibilityMode,
    bool InCurrentUserHive);

/// <summary>
/// Reads — and, only on explicit user confirmation, removes — the compatibility layer
/// Windows attaches to a game executable.
///
/// <para><b>Why this exists.</b> The stock AoE3 profile launches the raw
/// <c>bin\age3y.exe</c>. Windows' Program Compatibility Assistant decided on its own to
/// pin <c>WINXPSP3</c> on that exe (value <c>"~ WINXPSP3"</c> — the launcher writes
/// nothing here and never has). That layer makes <c>CreateProcess</c> fail with
/// <c>ERROR_ELEVATION_REQUIRED</c>, which costs the player two things at once: a UAC
/// prompt on every single launch, and — silently — the re-parenting under explorer.exe
/// that keeps the game alive when the launcher is force-closed. Removing the layer
/// restores both; that was measured, not assumed.</para>
///
/// <para><b>On the "leave AoE3 pure" rule.</b> The repo's standing rule against touching
/// another program's Windows configuration was written against a SILENT strip, and rested
/// on a premise that turned out to be false ("clearing the layer doesn't help" — it
/// does). What survives of that rule is its spirit, and it is enforced here: nothing
/// happens without an explicit click, only HKCU is written, only the one exe the user
/// launched is touched, and the removed value is logged verbatim so it can be restored by
/// hand. Probing is pure reading and always safe.</para>
/// </summary>
internal static class AppCompatLayerService
{
    private const string LayersSubKey =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";

    /// <summary>
    /// The layer applying to <paramref name="exePath"/>, or null when there is none.
    /// HKCU wins over HKLM: it is the one that can actually be removed, and it is where
    /// the Compatibility Assistant writes. Never throws.
    /// </summary>
    public static AppCompatLayerInfo? Probe(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return null;

        var user = ReadValue(Registry.CurrentUser, exePath);
        if (user != null) return Parse(user, inCurrentUserHive: true);

        var machine = ReadValue(Registry.LocalMachine, exePath);
        if (machine != null) return Parse(machine, inCurrentUserHive: false);

        return null;
    }

    /// <summary>
    /// Deletes the HKCU layer for <paramref name="exePath"/>. Returns true when a value
    /// was actually removed. Refuses HKLM — that needs elevation and belongs to the
    /// machine, not to this user. Never throws.
    /// </summary>
    public static bool Remove(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(LayersSubKey, writable: true);
            if (key == null) return false;

            // Log the exact value first: this is the user's only route back if the game
            // turns out to have needed that compatibility mode after all.
            if (key.GetValue(exePath) is not string existing) return false;
            key.DeleteValue(exePath, throwOnMissingValue: false);
            DiagnosticLog.Write(
                $"AppCompatLayer: removed HKCU layer '{existing}' for '{exePath}' (user confirmed).");
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"AppCompatLayer: could not remove layer for '{exePath}': {ex.Message}");
            return false;
        }
    }

    private static string? ReadValue(RegistryKey hive, string exePath)
    {
        try
        {
            using var key = hive.OpenSubKey(LayersSubKey);
            var value = key?.GetValue(exePath) as string;
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"AppCompatLayer: could not read layers: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Pure parser for a Layers value — kept separate from the registry so it can be
    /// unit-tested. The value is a space-separated token list; a leading <c>~</c> (its own
    /// token, or glued to the first one) marks a layer the Compatibility Assistant applied
    /// by itself.
    /// </summary>
    internal static AppCompatLayerInfo Parse(string rawValue, bool inCurrentUserHive)
    {
        var raw = rawValue ?? "";
        var tokens = raw.Split(' ', '\t')
                        .Where(t => !string.IsNullOrWhiteSpace(t))
                        .ToArray();

        bool appliedByWindows = tokens.Length > 0 && tokens[0].StartsWith('~');

        // Drop the marker so "~WINXPSP3" (no space) classifies like "~ WINXPSP3".
        var layers = tokens
            .Select(t => t.TrimStart('~'))
            .Where(t => t.Length > 0)
            .ToArray();

        return new AppCompatLayerInfo(
            RawValue: raw,
            AppliedByWindows: appliedByWindows,
            HasRunAsAdmin: layers.Any(t => t.Equals("RUNASADMIN", StringComparison.OrdinalIgnoreCase)),
            HasCompatibilityMode: layers.Any(IsCompatibilityMode),
            InCurrentUserHive: inCurrentUserHive);
    }

    /// <summary>
    /// An OS-version compatibility mode (as opposed to a behaviour fix like RUNASADMIN,
    /// HIGHDPIAWARE or DISABLEDXMAXIMIZEDWINDOWEDMODE). Matched by prefix rather than by a
    /// closed list: Microsoft keeps adding modes, and a mode we fail to recognise would
    /// silently read as "no compatibility mode set".
    /// </summary>
    private static bool IsCompatibilityMode(string token) =>
        token.StartsWith("WIN", StringComparison.OrdinalIgnoreCase) ||
        token.StartsWith("VISTA", StringComparison.OrdinalIgnoreCase);
}
