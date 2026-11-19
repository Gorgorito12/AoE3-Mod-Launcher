using System;
using System.Windows;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// While the launcher does something by itself (an automatic mod update), no window may open
/// over the player. Every question and notice on the way goes through
/// <see cref="ThemedConfirmDialog"/> or <see cref="AntivirusExclusionDialog"/>, so those two ask
/// here first: a question addressed to the window running the operation answers NO (the
/// conservative answer every caller already handles — cancel, do not retry, do not elevate), and a
/// notice is handed to <see cref="NoticeSink"/>, which puts it on a toast instead of a modal.
///
/// <para><b>Scoped to one OWNER window, on purpose.</b> The operation runs on the main window; a
/// question the player raises meanwhile from another window (settings, a mod's properties) has
/// another owner and still opens. A question owned by the main window while the operation runs is
/// that operation's, or something the busy gate already blocks.</para>
///
/// <para>Every answer given in the player's place is logged by title, so a bundle says what was
/// skipped. Pure decision in <see cref="Answers"/>, pinned by <c>UnattendedTests</c>.</para>
/// </summary>
internal static class Unattended
{
    /// <summary>The window an unattended operation runs on; null when none runs.</summary>
    private static Window? _owner;

    /// <summary>Where a notice goes instead of a modal: (title, body, isWarning, what its one
    /// button reopens — null for a notice with nothing more to show).</summary>
    internal static Action<string, string, bool, Action?>? NoticeSink { get; set; }

    internal static bool Active => _owner != null;

    /// <summary>Marks <paramref name="owner"/> as running an unattended operation until disposed.</summary>
    internal static IDisposable Begin(Window owner)
    {
        var previous = _owner;
        _owner = owner;
        return new Scope(previous);
    }

    /// <summary>Whether a dialog owned by <paramref name="dialogOwner"/> is answered without asking.</summary>
    internal static bool Answers(Window? unattendedOwner, Window? dialogOwner)
        => unattendedOwner != null && dialogOwner != null && ReferenceEquals(unattendedOwner, dialogOwner);

    /// <summary>True (and logged) when this dialog must not open: the caller takes "no".</summary>
    internal static bool Skip(Window? dialogOwner, string kind, string title)
    {
        if (!Answers(_owner, dialogOwner)) return false;
        DiagnosticLog.Write($"Unattended: {kind} '{title}' answered without asking (no).");
        return true;
    }

    /// <summary>A notice that would have opened: logged, then shown as a toast when there is a sink.</summary>
    internal static bool SkipNotice(Window? dialogOwner, string title, string body, bool warning, Action? reopen = null)
    {
        if (!Answers(_owner, dialogOwner)) return false;
        DiagnosticLog.Write($"Unattended: notice '{title}' shown as a toast instead of a window.");
        try { NoticeSink?.Invoke(title, body, warning, reopen); }
        catch (Exception ex) { DiagnosticLog.Write($"Unattended: the notice toast failed: {ex.Message}"); }
        return true;
    }

    private sealed class Scope : IDisposable
    {
        private readonly Window? _previous;
        private bool _done;

        public Scope(Window? previous) => _previous = previous;

        public void Dispose()
        {
            if (_done) return;
            _done = true;
            _owner = _previous;
        }
    }
}
