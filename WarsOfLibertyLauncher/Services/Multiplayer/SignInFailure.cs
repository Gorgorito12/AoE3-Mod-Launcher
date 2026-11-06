using System;
using System.Text;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

public enum SignInFailureKind { Unreachable, Refused, Expired }

/// <summary>
/// What the sign-in window says about a failure and about the link (design 97). Pure, so the cases
/// can be pinned without a server.
/// </summary>
public static class SignInFailure
{
    /// <summary>
    /// The server never says "expired" by name: its state row simply stops existing, so the poll
    /// answers 404 for the handle, and the launcher gives up by itself at <c>expires_in</c>. A 404
    /// while asking for the link is another thing, so it only counts during the wait.
    /// </summary>
    public static SignInFailureKind Classify(Exception ex, bool waitingForBrowser)
    {
        if (ex is not LobbyApiException api) return SignInFailureKind.Unreachable;
        if (api.Code is "device_flow_timeout" or "expired_token") return SignInFailureKind.Expired;
        if (waitingForBrowser && api.Status == 404) return SignInFailureKind.Expired;
        return SignInFailureKind.Refused;
    }

    /// <summary>The technical line, with every inner exception: transport details live there.</summary>
    public static string Describe(Exception ex)
    {
        var text = new StringBuilder();
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (text.Length > 0) text.Append(" || ");
            text.Append(e.GetType().Name).Append(": ").Append(e.Message);
        }
        return text.ToString();
    }

    /// <summary>The address up to the host, drawn in bold, and the rest. The window tells the player
    /// to check where the link goes, so that part has to be readable at a glance.</summary>
    public static (string Host, string Tail) SplitLink(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out _) || !url.Contains("://"))
            return ("", url);
        var at = url.IndexOf('/', url.IndexOf("://", StringComparison.Ordinal) + 3);
        return at < 0 ? (url, "") : (url[..at], url[at..]);
    }

    /// <summary>Whole minutes the link lasts, or null when the server did not say: no number is
    /// better than an invented one.</summary>
    public static int? LinkMinutes(int expiresInSeconds)
        => expiresInSeconds > 0 ? (int)Math.Ceiling(expiresInSeconds / 60.0) : null;
}
