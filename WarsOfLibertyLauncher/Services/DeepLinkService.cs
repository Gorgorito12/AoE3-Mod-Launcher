using System;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Custom URI scheme (<c>wol-launcher://</c>) registration + parsing, for the
/// Discord "Join" deep link (<c>wol-launcher://join/&lt;lobbyId&gt;</c>) that opens
/// the launcher and auto-joins a multiplayer room.
///
/// Registration is per-user (<c>HKCU\Software\Classes</c>) — no admin — and
/// idempotent (re-writes the exe path each launch, self-healing after the .exe
/// moves). Parsing treats the URI as UNTRUSTED input: any web page can fire it,
/// so the only action a link can request is "join lobby X", and the lobby id is
/// strictly validated against <see cref="LobbyIdPattern"/>. Nothing else in the
/// URI is honoured.
/// </summary>
public static class DeepLinkService
{
    /// <summary>The registered protocol scheme (no <c>://</c>).</summary>
    public const string Scheme = "wol-launcher";

    /// <summary>The only supported action host: <c>wol-launcher://join/…</c>.</summary>
    private const string JoinHost = "join";

    private const string ClassRoot = @"Software\Classes\" + Scheme;

    // Lobby ids are short alphanumeric tokens (e.g. "NHHXP1NR"). Reject anything
    // else so a hostile deep link can't smuggle paths/traversal/args through.
    private static readonly Regex LobbyIdPattern =
        new(@"^[A-Za-z0-9]{1,32}$", RegexOptions.Compiled);

    /// <summary>
    /// Idempotently register the <c>wol-launcher://</c> scheme under HKCU so
    /// clicking a deep link launches this .exe with the URI as its argument.
    /// Best-effort — logs and continues on failure (the launcher still works;
    /// deep links just won't resolve). Rewrites the exe path every call so a
    /// moved/updated binary self-heals, like <see cref="StartupRegistrationService"/>.
    /// </summary>
    public static void EnsureRegistered()
    {
        try
        {
            // ProcessPath is the right primitive for the running .exe path — it
            // works in single-file published builds where Assembly.Location is empty.
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath))
            {
                DiagnosticLog.Write("DeepLink: ProcessPath empty; can't register scheme.");
                return;
            }

            using (var root = Registry.CurrentUser.CreateSubKey(ClassRoot))
            {
                if (root == null)
                {
                    DiagnosticLog.Write($"DeepLink: could not create '{ClassRoot}'.");
                    return;
                }
                root.SetValue(null, "URL:Wars of Liberty Launcher", RegistryValueKind.String);
                // Presence of the (empty) "URL Protocol" value is what tells the
                // shell this class is a URI-scheme handler.
                root.SetValue("URL Protocol", "", RegistryValueKind.String);
            }

            using var cmd = Registry.CurrentUser.CreateSubKey(ClassRoot + @"\shell\open\command");
            // Quote both the path (may contain spaces) and %1 (the URI arg).
            cmd?.SetValue(null, $"\"{exePath}\" \"%1\"", RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"DeepLink: register failed: {ex.Message}");
        }
    }

    /// <summary>
    /// If <paramref name="arg"/> is a valid <c>wol-launcher://join/&lt;id&gt;</c>
    /// deep link, extract the validated lobby id. Returns false for anything else
    /// (a normal arg like <c>--update-now</c>, junk, or a hostile URI).
    /// </summary>
    public static bool TryParseJoin(string? arg, out string lobbyId)
    {
        lobbyId = "";
        if (string.IsNullOrWhiteSpace(arg)) return false;
        if (!Uri.TryCreate(arg, UriKind.Absolute, out var uri)) return false;
        if (!string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(uri.Host, JoinHost, StringComparison.OrdinalIgnoreCase)) return false;

        var id = uri.AbsolutePath.Trim('/');
        if (!LobbyIdPattern.IsMatch(id)) return false;

        lobbyId = id;
        return true;
    }

    /// <summary>
    /// Scan a process's command-line args for the first valid join deep link,
    /// or null if none is present.
    /// </summary>
    public static string? FindJoinLobbyId(string[] args)
    {
        if (args == null) return null;
        foreach (var a in args)
            if (TryParseJoin(a, out var id))
                return id;
        return null;
    }
}
