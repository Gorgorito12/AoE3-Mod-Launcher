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
    /// Remove the <c>wol-launcher://</c> registration (the whole class subtree).
    /// Best-effort — used when the user turns the feature off. Idempotent (no-op
    /// when nothing is registered).
    /// </summary>
    public static void EnsureUnregistered()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(ClassRoot, throwOnMissingSubKey: false);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"DeepLink: unregister failed: {ex.Message}");
        }
    }

    /// <summary>True if the <c>wol-launcher://</c> scheme is currently registered.</summary>
    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ClassRoot + @"\shell\open\command");
            return key?.GetValue(null) is string s && !string.IsNullOrWhiteSpace(s);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"DeepLink: IsRegistered read failed: {ex.Message}");
            return false;
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

    /// <summary>True if <paramref name="id"/> is a well-formed lobby id (the only
    /// value we ever accept from an untrusted deep link or the IPC pipe).</summary>
    public static bool IsValidLobbyId(string? id)
        => !string.IsNullOrEmpty(id) && LobbyIdPattern.IsMatch(id);

    /// <summary>
    /// The canonical deep link for a lobby id — built here rather than by any caller, so the
    /// scheme and host stay in the one file that owns them.
    ///
    /// <para>The startup auto-update uses it to carry a pending link across its own restart.
    /// It rebuilds from the VALIDATED id rather than passing the original argument through:
    /// that string came from a browser, and nothing that arbitrary belongs in a command line
    /// this launcher constructs.</para>
    /// </summary>
    public static string BuildJoinUri(string lobbyId) => $"{Scheme}://{JoinHost}/{lobbyId}";

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

    // ------------------------------------------------------------------------
    // Add a translation source with one click
    // ------------------------------------------------------------------------

    /// <summary>The second action host: <c>wol-launcher://add-source?url=…</c> or <c>?repo=…</c>.</summary>
    private const string AddSourceHost = "add-source";

    /// <summary>Longest add-source link accepted (the source itself is capped at 2048 characters).</summary>
    public const int MaxAddSourceLength = 4096;

    /// <summary>
    /// If <paramref name="arg"/> is a <c>wol-launcher://add-source</c> link carrying exactly one
    /// <c>url</c> or <c>repo</c> parameter that <see cref="TranslationSourceRef.TryParse"/>
    /// accepts, returns that source. Like the join link it is UNTRUSTED — any web page can fire
    /// it — so it is never acted on directly: the launcher shows the full address and the player
    /// confirms (Cancel being the default). Anything ambiguous is refused: a second parameter,
    /// both kinds at once, a repeated one, an encoded control character, a <c>url</c> that is not
    /// a link or a <c>repo</c> that is.
    /// </summary>
    public static bool TryParseAddSource(string? arg, out TranslationSourceRef? source)
    {
        source = null;
        if (string.IsNullOrWhiteSpace(arg) || arg.Length > MaxAddSourceLength) return false;
        if (!Uri.TryCreate(arg.Trim(), UriKind.Absolute, out var uri)) return false;
        if (!string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(uri.Host, AddSourceHost, StringComparison.OrdinalIgnoreCase)) return false;
        if (uri.AbsolutePath.Trim('/').Length > 0) return false;

        var query = uri.Query.TrimStart('?');
        if (query.Length == 0) return false;
        var parts = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 1) return false;

        var eq = parts[0].IndexOf('=');
        if (eq <= 0) return false;
        var key = parts[0][..eq];
        string value;
        try { value = Uri.UnescapeDataString(parts[0][(eq + 1)..]); }
        catch { return false; }
        foreach (var c in value)
            if (char.IsControl(c)) return false;

        bool isUrl = string.Equals(key, "url", StringComparison.Ordinal);
        bool isRepo = string.Equals(key, "repo", StringComparison.Ordinal);
        if (!isUrl && !isRepo) return false;
        bool looksLikeLink = value.Contains("://", StringComparison.Ordinal);
        if (isUrl != looksLikeLink) return false;

        if (!TranslationSourceRef.TryParse(value, out var parsed, out _) || parsed == null) return false;
        source = parsed;
        return true;
    }

    /// <summary>
    /// The canonical add-source link for <paramref name="source"/>. Rebuilt from the VALIDATED
    /// source whenever one has to be passed on (the pipe to a running launcher, the startup
    /// update's restart) — the original string came from a browser.
    /// </summary>
    public static string BuildAddSourceUri(TranslationSourceRef source)
    {
        var key = source.Kind == Models.TranslationSourceKind.GitHubFolder ? "repo" : "url";
        return $"{Scheme}://{AddSourceHost}?{key}={Uri.EscapeDataString(source.Location)}";
    }

    /// <summary>The first valid add-source link among a process's arguments, or null.</summary>
    public static TranslationSourceRef? FindAddSource(string[] args)
    {
        if (args == null) return null;
        foreach (var a in args)
            if (TryParseAddSource(a, out var s) && s != null)
                return s;
        return null;
    }
}
