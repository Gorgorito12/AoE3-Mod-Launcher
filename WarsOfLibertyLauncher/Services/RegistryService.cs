using System;
using System.IO;
using Microsoft.Win32;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Detects the Wars of Liberty installation by reading the same registry keys
/// the original Java updater uses. Wars of Liberty is installed via Inno Setup
/// with a fixed product GUID, which makes detection reliable.
/// </summary>
public static class RegistryService
{
    private const string ProductGuid = "{EB448764-CABB-4766-8055-495AEA292020}_is1";

    private const string Key32 =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + ProductGuid;
    private const string Key64 =
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\" + ProductGuid;

    /// <summary>
    /// Tries to find a valid Wars of Liberty installation.
    /// Returns null if nothing found.
    /// </summary>
    public static string? FindInstallPath()
    {
        // Try every combination of registry view + key path + value name.
        // The original updater tries these in order and uses the first that works.
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        foreach (var keyPath in new[] { Key64, Key32 })
        foreach (var valueName in new[] { "Inno Setup: App Path", "Path" })
        {
            var path = ReadValue(view, keyPath, valueName);
            if (!string.IsNullOrWhiteSpace(path) && IsValidInstall(path))
                return path.TrimEnd('\\', '/');
        }
        return null;
    }

    /// <summary>
    /// Validates an installation by checking for a known marker file.
    /// The original updater checks for "art\zulushield" — replicating that.
    /// </summary>
    public static bool IsValidInstall(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return false;

        var marker = Path.Combine(path, "art", "zulushield");
        return Directory.Exists(marker);
    }

    private static string? ReadValue(RegistryView view, string keyPath, string valueName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var subKey = baseKey.OpenSubKey(keyPath);
            return subKey?.GetValue(valueName) as string;
        }
        catch
        {
            return null;
        }
    }
}
