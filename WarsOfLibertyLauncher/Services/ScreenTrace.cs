using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Threading;

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
///
/// <para><b>A click also gets a <c>SWITCH</c> line</b>: how long from the click to the new page
/// being painted, and whether the page had been shown before. Lag between tabs was invisible: the
/// slow-operation lines only start at 250 ms and say nothing about what the player pressed.</para>
/// </summary>
internal static class ScreenTrace
{
    private static string s_topTab = "";
    private static string s_subtab = "";
    private static string? s_last;
    private static long s_clickedAt;
    private static readonly HashSet<string> s_shown = new(System.StringComparer.Ordinal);

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

    /// <summary>The <c>SWITCH</c> line, pure, for the tests.</summary>
    internal static string SwitchLine(string? from, string to, long ms, bool firstTime)
        => $"SWITCH  {(string.IsNullOrEmpty(from) ? "(start)" : from)} -> {to}  {ms} ms ({(firstTime ? "first time" : "repeat")})";

    /// <summary>Called when a click asks for another page, before anything is changed. A page that
    /// changes without one (a notification, a deep link while hidden) gets no SWITCH line.</summary>
    internal static void Clicked() => s_clickedAt = Stopwatch.GetTimestamp();

    /// <summary>
    /// A mod picked in the Library: <c>MODSWITCH a -> b  N ms</c>, from the click to the frame that
    /// shows the new mod, the same measure as a tab's SWITCH. First time or repeat, per mod.
    /// </summary>
    internal static void ModSwitch(string from, string to, long clickedAt)
    {
        var firstTime = s_modsShown.Add(to);
        var dispatcher = Dispatcher.CurrentDispatcher;
        long firstFrame = -1;
        // Two numbers: the first frame after the click, and the frame after the dispatcher ran out
        // of work (the list and the detail are redrawn at Background priority, after that first
        // frame, so the first number alone can miss the part of a switch a person sees).
        void OnFirst(object? sender, System.EventArgs e)
        {
            CompositionTarget.Rendering -= OnFirst;
            firstFrame = (long)Stopwatch.GetElapsedTime(clickedAt).TotalMilliseconds;
            dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new System.Action(() =>
                CompositionTarget.Rendering += OnSettled));
        }
        void OnSettled(object? sender, System.EventArgs e)
        {
            CompositionTarget.Rendering -= OnSettled;
            var settled = (long)Stopwatch.GetElapsedTime(clickedAt).TotalMilliseconds;
            dispatcher.BeginInvoke(DispatcherPriority.Loaded, new System.Action(() =>
                DiagnosticLog.Write($"MODSWITCH  {from} -> {to}  {firstFrame} ms, settled {settled} ms ({(firstTime ? "first time" : "repeat")})")));
        }
        CompositionTarget.Rendering += OnFirst;
    }

    private static readonly HashSet<string> s_modsShown = new(System.StringComparer.OrdinalIgnoreCase);

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
        var clickedAt = s_clickedAt;
        s_clickedAt = 0;
        if (Next(s_last, current) is not { } line) return;
        var from = s_last;
        s_last = current;
        DiagnosticLog.Write(line);
        var firstTime = s_shown.Add(current);
        if (clickedAt != 0) MeasurePaint(from, current, clickedAt, firstTime);
    }

    /// <summary>
    /// Rendering is raised inside the frame that carries the change, so what is queued from it runs
    /// once that frame has been handed over. The subscription lasts one frame: leaving it on would
    /// make WPF draw every frame for nothing.
    /// </summary>
    private static void MeasurePaint(string? from, string to, long clickedAt, bool firstTime)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        void OnRendering(object? sender, System.EventArgs e)
        {
            CompositionTarget.Rendering -= OnRendering;
            dispatcher.BeginInvoke(DispatcherPriority.Loaded, new System.Action(() =>
                DiagnosticLog.Write(SwitchLine(from, to,
                    (long)Stopwatch.GetElapsedTime(clickedAt).TotalMilliseconds, firstTime))));
        }
        CompositionTarget.Rendering += OnRendering;
    }
}
