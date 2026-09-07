namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Whether multiplayer is closed until the launcher is updated.
///
/// <para><b>Every release is mandatory for multiplayer.</b> The moment the self-update check
/// finds a newer release on GitHub — the same moment the gold pill lights up — the
/// Multiplayer tab is covered by a notice that offers the update, and every way into a room
/// (the list, a toast, the bell, a deep link, a room code) lands on that notice instead. It
/// lifts by itself once the check says the launcher is current, which after an update is the
/// next launch.</para>
///
/// <para>This is the launcher's own gate, decided from what the launcher already knows. The
/// server keeps its own (<c>MIN_LAUNCHER_VERSION</c>, refused entry, <c>launcher_too_old</c>)
/// as the hard stop for anybody who turned update checks off: that one cannot be bypassed
/// from here, and this one does not replace it.</para>
///
/// <para><b>The one exception is a command-line switch, <c>--no-update-gate</c>.</b> A build
/// published locally reports itself without the letter (<c>v1.0.14</c>), because the letter
/// only exists as an argument to <c>publish.ps1</c>; against a released <c>v1.0.14d</c> that
/// build is "older" and would be shut out of multiplayer every single time the maintainer
/// runs it. Nobody else starts the launcher with arguments.</para>
/// </summary>
public static class LauncherUpdateGate
{
    /// <summary>The switch, as typed on the command line.</summary>
    public const string BypassArgument = "--no-update-gate";

    /// <summary>
    /// Whether this run is exempt from the gate at all.
    ///
    /// <para>Three ways in, and none of them is a player. <paramref name="debugBuild"/> is
    /// the person writing the launcher: "update to play online" is not a sentence addressed
    /// to them, and pressing F5 in Visual Studio passes no arguments, so the switch alone
    /// could not help there. <paramref name="debuggerAttached"/> says the same of a Release
    /// build somebody is stepping through. <paramref name="argument"/> is the maintainer's
    /// locally published build, which calls itself <c>v1.0.14</c> — the letter only exists as
    /// an argument to <c>publish.ps1</c> — and would otherwise be shut out for good.</para>
    /// </summary>
    public static bool Bypassed(bool argument, bool debugBuild, bool debuggerAttached)
        => argument || debugBuild || debuggerAttached;

    /// <summary>
    /// True when multiplayer should be covered. Pure: the update check's answer and the
    /// bypass are the whole input, so a test can pin every combination without a window.
    /// </summary>
    /// <param name="pending">The self-update check's latest answer, or null before it has
    /// run or when nothing is pending.</param>
    /// <param name="bypass">Whether the launcher was started with
    /// <see cref="BypassArgument"/>.</param>
    public static bool ShouldGate(LauncherUpdateService.UpdateCheckResult? pending, bool bypass)
        => pending?.UpdateAvailable == true && !bypass;

    /// <summary>The version to name on the notice, or null when nothing is gated.</summary>
    public static string? RequiredVersion(LauncherUpdateService.UpdateCheckResult? pending, bool bypass)
        => ShouldGate(pending, bypass) ? pending!.LatestVersion : null;
}
