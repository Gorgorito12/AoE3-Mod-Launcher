using System.Text.RegularExpressions;

namespace WarsOfLibertyLauncher.Services.Repair;

/// <summary>
/// The command-line ticket an elevated relaunch carries so the repair the player asked for
/// continues by itself: <c>--repair-now=&lt;modId&gt;</c>, or <c>--repair-now=&lt;modId&gt;:update</c>
/// for a GitHubReleases update.
///
/// <para><b>Why an argument and not a file.</b> The relaunch can run as a DIFFERENT Windows account
/// (a standard user typing an administrator's password), whose <c>%LocalAppData%</c> is somebody
/// else's — a ticket written to the data folder would simply not be there. The argument always
/// arrives. Before this, the elevated instance opened on the dashboard and the player had to find
/// and press Repair again, not knowing the first press had done nothing.</para>
///
/// <para>The id is validated on BOTH ends: a command line is text any program can write, and the
/// value is only ever compared against the active mod, never used as a path.</para>
/// </summary>
internal static class RepairResume
{
    internal const string Arg = "--repair-now";
    private const string UpdateSuffix = ":update";
    private static readonly Regex IdPattern = new("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$");

    internal sealed record Ticket(string ModId, bool Update);

    /// <summary>The argument for <paramref name="modId"/>, or null when the id could not round-trip.</summary>
    internal static string? Build(string? modId, bool update)
    {
        var id = (modId ?? "").Trim();
        if (!IdPattern.IsMatch(id)) return null;
        return $"{Arg}={id}{(update ? UpdateSuffix : "")}";
    }

    /// <summary>The first well-formed ticket among <paramref name="args"/>, or null.</summary>
    internal static Ticket? TryParse(IEnumerable<string>? args)
    {
        foreach (var raw in args ?? Enumerable.Empty<string>())
        {
            var a = (raw ?? "").Trim();
            if (!a.StartsWith(Arg + "=", StringComparison.OrdinalIgnoreCase)) continue;
            var value = a[(Arg.Length + 1)..];
            bool update = value.EndsWith(UpdateSuffix, StringComparison.OrdinalIgnoreCase);
            if (update) value = value[..^UpdateSuffix.Length];
            if (IdPattern.IsMatch(value)) return new Ticket(value, update);
        }
        return null;
    }

    /// <summary>True when any argument looks like a ticket, well-formed or not.</summary>
    internal static bool IsPresent(IEnumerable<string>? args)
        => (args ?? Enumerable.Empty<string>()).Any(a =>
            (a ?? "").Trim().StartsWith(Arg, StringComparison.OrdinalIgnoreCase));
}
