using System.Windows;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>
/// The one "there may not be room for this — carry on anyway?" prompt, shared by every download
/// the launcher makes.
///
/// <para>It lives in the UI layer, not in <see cref="DiskSpaceService"/>, because a service must
/// not open dialogs — the same reason <c>MainWindow.ConfirmUpdateSpaceOk</c> sits in the window
/// rather than in <c>UpdateService</c>. <see cref="DiskSpaceService"/> stays pure and testable and
/// answers only "which volume is short, and by how much"; this decides what to show.</para>
///
/// <para><b>Warn, never block.</b> Several of the requirements are estimates (see the delta-patch
/// factors), so refusing outright would cancel operations that would have completed. An
/// unmeasurable volume produces no <see cref="DiskSpaceShortfall"/> at all, so it never reaches
/// here — we don't cry wolf when we can't measure.</para>
/// </summary>
internal static class DiskSpacePrompt
{
    /// <summary>
    /// True when there is room (<paramref name="shortfall"/> is null) or the user chose to
    /// continue anyway; false only when they declined. Callers treat false as "cancel", and it is
    /// the caller's job to unwind cleanly — nothing has been downloaded at this point.
    /// </summary>
    /// <param name="bodyKey">
    /// Localization key for the message body. It must accept three arguments in this order:
    /// required, free, drive. <c>DiskSpaceConfirmDownloadBody</c> is the generic one; the
    /// install and repair flows pass their own wording.
    /// </param>
    public static bool ConfirmOrCancel(Window? owner, DiskSpaceShortfall? shortfall, string bodyKey)
    {
        if (shortfall == null) return true;

        // Several callers reach this from inside an async download, and none of those services
        // uses ConfigureAwait(false) today — so the continuation is on the UI thread and this is
        // a no-op. It stays because that is an easy thing to change in a service without anyone
        // noticing it broke a dialog three layers up.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
            return dispatcher.Invoke(() => ConfirmOrCancel(owner, shortfall, bodyKey));

        DiagnosticLog.Write(
            $"Low disk space on {shortfall.Drive}: need {shortfall.RequiredBytes}, " +
            $"have {shortfall.FreeBytes}.");

        var body = Strings.Format(bodyKey,
            DiskSpaceService.FormatBytes(shortfall.RequiredBytes),
            DiskSpaceService.FormatBytes(shortfall.FreeBytes),
            shortfall.Drive);
        var title = Strings.Get("DiskSpaceConfirmTitle");

        var result = owner != null
            ? MessageBox.Show(owner, body, title, MessageBoxButton.YesNo, MessageBoxImage.Warning)
            : MessageBox.Show(body, title, MessageBoxButton.YesNo, MessageBoxImage.Warning);
        return result == MessageBoxResult.Yes;
    }
}
