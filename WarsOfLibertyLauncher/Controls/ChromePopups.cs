using System;
using System.Windows.Controls.Primitives;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// App-wide coordinator for the launcher's hand-built transient menus —
/// the code-behind <see cref="Popup"/>s like the title-bar brand dropdown
/// (<c>BuildBrandPopup</c>) and the dashboard MODS switcher
/// (<c>DashboardChangeModButton_Click</c>).
///
/// Why this exists: those popups use <c>AllowsTransparency=true</c> +
/// <c>StaysOpen=false</c>, and WPF's auto-dismiss for that combination is
/// unreliable when a non-modal Window steals activation — the popup lingers
/// behind a freshly-opened dialog (the reported "open MODS, click the gear,
/// the menu stays open" bug). Rather than patch every opener, this class
/// centralises the rule: at most ONE tracked popup is open at a time, and any
/// dialog/window activation closes it (wired once in <c>App.OnAnyWindowLoaded</c>).
///
/// It also makes the openers behave as TOGGLES (see <see cref="ConsumeToggleOff"/>):
/// clicking a button whose popup is showing closes it instead of reopening.
///
/// WPF <see cref="System.Windows.Controls.ContextMenu"/>s are NOT tracked here —
/// they capture input and auto-dismiss reliably on their own. Only the
/// hand-built <see cref="Popup"/>s need this.
/// </summary>
internal static class ChromePopups
{
    private static Popup? _open;

    // Toggle support: when a tracked popup closes we remember WHICH opener it
    // belonged to and WHEN. A StaysOpen=false popup auto-dismisses on the
    // mouse-down that re-clicks its own opener, so by the time the opener's
    // Click fires the popup is already gone — a naive "open on click" would
    // immediately reopen it (the "just reopens" flicker). ConsumeToggleOff lets
    // the opener detect "I was just dismissed by this very click" and skip the
    // reopen, turning the button into a real toggle.
    private static object? _lastClosedOwner;
    private static int _lastClosedTick;
    private const int ToggleGuardMs = 300;

    /// <summary>
    /// Register a popup so it participates in the single-open invariant. Call
    /// once right after constructing the popup (before <c>IsOpen = true</c>).
    /// Wires <see cref="Popup.Opened"/> (claim the slot, close the previous
    /// tracked popup) and <see cref="Popup.Closed"/> (release the slot, with a
    /// <c>ReferenceEquals</c> race guard so a newer popup isn't clobbered).
    /// </summary>
    /// <param name="owner">The opener control (e.g. the button) this popup
    /// belongs to. Pass a stable instance so <see cref="ConsumeToggleOff"/> can
    /// recognise a re-click of the same opener. Must be the same reference on
    /// every open for toggle to work.</param>
    public static void Track(Popup popup, object? owner = null)
    {
        popup.Opened += (_, _) =>
        {
            if (ReferenceEquals(_open, popup)) return;
            var previous = _open;
            _open = popup;
            // Close whatever was open BEFORE this one — mutual exclusion
            // between the launcher's transient menus.
            if (previous != null)
                previous.IsOpen = false;
        };
        popup.Closed += (_, _) =>
        {
            if (ReferenceEquals(_open, popup))
                _open = null;
            if (owner != null)
            {
                _lastClosedOwner = owner;
                _lastClosedTick = Environment.TickCount;
            }
        };
    }

    /// <summary>
    /// Call at the TOP of an opener's click handler. Returns <c>true</c> if a
    /// popup owned by <paramref name="owner"/> was dismissed within the last
    /// few hundred ms — i.e. this click is the "toggle off" that just closed it,
    /// so the caller should return WITHOUT reopening. Consumes the record so the
    /// next click reopens normally.
    /// </summary>
    public static bool ConsumeToggleOff(object owner)
    {
        if (ReferenceEquals(_lastClosedOwner, owner) &&
            unchecked(Environment.TickCount - _lastClosedTick) < ToggleGuardMs)
        {
            _lastClosedOwner = null;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Close the currently-open tracked popup, if any. Safe to call when none
    /// is open. Clears the field first so the popup's own <c>Closed</c> handler
    /// is a no-op re-entrancy-wise.
    /// </summary>
    public static void CloseOpen()
    {
        var open = _open;
        _open = null;
        if (open != null)
            open.IsOpen = false;
    }
}
