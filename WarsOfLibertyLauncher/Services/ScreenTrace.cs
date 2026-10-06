namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Writes one <c>TAB</c> line to the log whenever the screen the player is looking at changes —
/// the top tab, and inside Multiplayer its subtab.
///
/// <para><b>Why it exists.</b> A player's bundle showed the UI thread doing nothing but slow
/// redraws from the moment the multiplayer data arrived, and nothing in it said which page was
/// on screen while that happened. The storm line carries the tab, but only when a storm is
/// reported, and on that machine none was. A bundle about lag has to say where the player was.</para>
///
/// <para>Written only on a CHANGE, so it costs nothing while somebody stays on one page: the
/// subtab is re-applied on every session refresh, and that must not become a line each time.
/// UI thread only, like every caller.</para>
/// </summary>
internal static class ScreenTrace
{
    private static string s_topTab = "";
    private static string s_subtab = "";
    private static string? s_last;

    /// <summary>The text of the line for a top tab and a subtab; the subtab only counts on the
    /// Multiplayer tab, where it is what the player is actually looking at.</summary>
    internal static string Describe(string topTab, string? subtab)
        => string.Equals(topTab, "Multiplayer", System.StringComparison.Ordinal) && !string.IsNullOrEmpty(subtab)
            ? $"{topTab}/{subtab}"
            : topTab;

    /// <summary>The line to write when the screen became <paramref name="current"/>, or null when
    /// it was already that — the rule, pure, for the tests.</summary>
    internal static string? Next(string? last, string current)
        => string.IsNullOrEmpty(current) || string.Equals(last, current, System.StringComparison.Ordinal)
            ? null
            : $"TAB  {current}";

    /// <summary>Called when the top tab changes.</summary>
    internal static void TopTab(string tab)
    {
        s_topTab = tab;
        Emit();
    }

    /// <summary>Called whenever the Multiplayer tab shows a subtab.</summary>
    internal static void Subtab(string subtab)
    {
        s_subtab = subtab;
        Emit();
    }

    private static void Emit()
    {
        var current = Describe(s_topTab, s_subtab);
        if (Next(s_last, current) is not { } line) return;
        s_last = current;
        DiagnosticLog.Write(line);
    }
}
