namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Why a pop-up card exists — the one thing "Do not disturb" (<c>LauncherConfig.DoNotDisturb</c>)
/// looks at.
/// </summary>
public enum ToastCause
{
    /// <summary>
    /// Something the player did not just ask for: a new room, a team invite, tournament news, a
    /// finished update. Silenced by "Do not disturb". <b>The default</b>, on purpose: a new call
    /// site that forgets to say what it is loses a card, which is noticed and fixed — it never
    /// breaks the promise the setting makes.
    /// </summary>
    News,

    /// <summary>
    /// The answer to something the player just did (a check, an invite sent, a download), or the
    /// result of their own match or operation that is kept nowhere else (a match that could not
    /// be reported, an operation that did not finish). Always shown: it is how the launcher
    /// replies, and silencing it makes a button look broken or loses the only notice there is.
    /// </summary>
    Answer,

    /// <summary>
    /// A personal call with a clock on it: a room invitation (it exists nowhere but the card, and
    /// has its own switch, "Let players invite me to their rooms") or "your tournament match is
    /// ready" (an organiser can disqualify a no-show). Always shown.
    /// </summary>
    Request,
}

/// <summary>Where a card ends up.</summary>
public enum ToastRoute
{
    /// <summary>In the launcher's window, which the player is looking at.</summary>
    InWindow,

    /// <summary>On the desktop, the window being minimised, in the tray or behind another app.</summary>
    Desktop,

    /// <summary>Not shown: "Do not disturb" is on and the card is <see cref="ToastCause.News"/>.</summary>
    SuppressedDoNotDisturb,

    /// <summary>Not shown: a topmost window over a running game can knock AoE3 out of full screen.</summary>
    SuppressedGameRunning,
}

/// <summary>
/// The routing of the launcher's pop-ups, pure so it has tests — <c>MainWindow.ShowAppToast</c>
/// and <c>MainWindow.ShowToast</c> are its only callers, and nothing else may put a card or a
/// tray balloon on screen (<c>ToastFunnelTests</c>).
/// </summary>
public static class ToastRouting
{
    /// <summary>
    /// "Do not disturb" first, so it silences a card in the window as well as on the desktop;
    /// then the old routing, unchanged: in the window when the player is looking (unless the
    /// card prefers the desktop), never over a running game, otherwise on the desktop.
    /// </summary>
    public static ToastRoute Decide(
        bool doNotDisturb, ToastCause cause, bool userIsLooking, bool preferDesktop, bool gameRunning)
    {
        if (doNotDisturb && cause == ToastCause.News) return ToastRoute.SuppressedDoNotDisturb;
        if (userIsLooking && !preferDesktop) return ToastRoute.InWindow;
        return gameRunning ? ToastRoute.SuppressedGameRunning : ToastRoute.Desktop;
    }

    /// <summary>
    /// Whether a tray balloon shows: the bell's balloons are switched on, "Do not disturb" is off
    /// (or this balloon answers the player — <paramref name="bypassDoNotDisturb"/>), the player is
    /// not looking at the window (they would already see it there), and the tray icon is there to
    /// carry it.
    /// </summary>
    public static bool ShouldShowBalloon(
        bool showToastNotifications, bool doNotDisturb, bool bypassDoNotDisturb,
        bool userIsLooking, bool trayIconVisible)
        => showToastNotifications
           && (!doNotDisturb || bypassDoNotDisturb)
           && !userIsLooking
           && trayIconVisible;
}
