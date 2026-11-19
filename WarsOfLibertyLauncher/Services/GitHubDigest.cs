using System.Text.RegularExpressions;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// GitHub's per-asset <c>digest</c> field (<c>"sha256:&lt;64 hex&gt;"</c>, on releases uploaded
/// since mid-2025). The ONE parser: the self-updater and the mod payloads both read it here.
///
/// <para>Verifying it is INTEGRITY, not authenticity: the digest and the bytes come from the same
/// origin, so it catches a corrupt or truncated download and a CDN serving the wrong file, never a
/// compromised release. The catalog's SHA-256 pins remain the authenticity layer. Anything that is
/// not exactly <c>sha256:</c> + 64 hex reads as absent, never as a hash to compare against.</para>
/// </summary>
internal static class GitHubDigest
{
    private static readonly Regex Shape = new("^sha256:([0-9a-fA-F]{64})$", RegexOptions.CultureInvariant);

    /// <summary>The lowercase hex SHA-256, or null.</summary>
    internal static string? TryParseSha256(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest)) return null;
        var m = Shape.Match(digest.Trim());
        return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
    }
}
