using System;
using System.IO;
using System.Linq;

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
/// <para><b>Nothing built locally is ever gated.</b> Every local build reports itself
/// without the letter (<c>v1.0.14</c>), because the letter only exists as an argument to
/// <c>publish.ps1</c>; against a released <c>v1.0.14d</c> that build is "older" and would be
/// shut out of multiplayer every single time. <see cref="IsDeveloperBuild"/> recognises such
/// a build from the folder it runs in, so it needs no switch, no debugger and no particular
/// configuration — see <see cref="Bypassed"/> for the other three ways out and why this one
/// had to exist.</para>
/// </summary>
public static class LauncherUpdateGate
{
    /// <summary>The switch, as typed on the command line.</summary>
    public const string BypassArgument = "--no-update-gate";

    /// <summary>
    /// Whether this run is exempt from the gate at all.
    ///
    /// <para>Four ways out, and none of them is a player. <paramref name="debugBuild"/> is
    /// the person writing the launcher: "update to play online" is not a sentence addressed
    /// to them, and pressing F5 in Visual Studio passes no arguments, so the switch alone
    /// could not help there. <paramref name="debuggerAttached"/> says the same of a Release
    /// build somebody is stepping through. <paramref name="argument"/> is the maintainer's
    /// locally published build, which calls itself <c>v1.0.14</c> — the letter only exists as
    /// an argument to <c>publish.ps1</c> — and would otherwise be shut out for good.</para>
    ///
    /// <para><paramref name="developerBuild"/> is the one that had to be added, because the
    /// other three left a hole big enough to walk through: a <b>Release</b> build run locally
    /// <i>without</i> a debugger — Ctrl+F5, or a double-click on <c>bin\Release\…\.exe</c> —
    /// matched none of them, and being a local build it is "older" than the published release
    /// forever, so multiplayer closed on the maintainer every single time. Unlike the other
    /// three it cannot be forgotten: it reads the folder rather than a switch, a configuration
    /// or a debugger.</para>
    /// </summary>
    public static bool Bypassed(bool argument, bool debugBuild, bool debuggerAttached, bool developerBuild)
        => argument || debugBuild || debuggerAttached || developerBuild;

    /// <summary>
    /// True when the launcher is running out of a build output rather than an installed
    /// release.
    ///
    /// <para>The signal is physical, which is the point: a framework-dependent build leaves
    /// <c>*.deps.json</c> and <c>*.runtimeconfig.json</c> beside the executable, and the
    /// published build — self-contained, single-file — embeds both and leaves neither. So this
    /// is true for every way of starting a local build (F5, Ctrl+F5, a double-click on the
    /// <c>bin\</c> exe, <c>dotnet run</c>, <c>dotnet Aoe3ModLauncher.dll</c>) and false for
    /// the <c>.exe</c> a player downloads, without anybody having to configure it.</para>
    ///
    /// <para>Both files are required, not either: one of them alone is a leftover, and erring
    /// towards "this is a player" is the safe side — a player wrongly treated as a developer
    /// would stop receiving automatic updates.</para>
    ///
    /// <para>Pure in the sense that matters: the caller passes the directory
    /// (<c>AppContext.BaseDirectory</c>, never <c>Environment.ProcessPath</c>, which under
    /// <c>dotnet Aoe3ModLauncher.dll</c> points at <c>dotnet.exe</c> in an unrelated folder),
    /// so a test can pin it with a temp folder.</para>
    /// </summary>
    public static bool IsDeveloperBuild(string? baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory)) return false;

        try
        {
            // Enumerate rather than name the assembly: the check has to keep working if the
            // output ever gets renamed, and a build output holds exactly one of each.
            return Directory.EnumerateFiles(baseDirectory, "*.deps.json").Any()
                && Directory.EnumerateFiles(baseDirectory, "*.runtimeconfig.json").Any();
        }
        catch (Exception)
        {
            // A folder that cannot be read is not a build output anybody is working in.
            return false;
        }
    }

    /// <summary>
    /// True when multiplayer should be covered. Pure: the update check's answer and the
    /// bypass are the whole input, so a test can pin every combination without a window.
    /// </summary>
    /// <param name="pending">The self-update check's latest answer, or null before it has
    /// run or when nothing is pending.</param>
    /// <param name="bypass">The answer from <see cref="Bypassed"/> — the switch, a debug
    /// build, a debugger, or a developer build.</param>
    public static bool ShouldGate(LauncherUpdateService.UpdateCheckResult? pending, bool bypass)
        => pending?.UpdateAvailable == true && !bypass;

    /// <summary>The version to name on the notice, or null when nothing is gated.</summary>
    public static string? RequiredVersion(LauncherUpdateService.UpdateCheckResult? pending, bool bypass)
        => ShouldGate(pending, bypass) ? pending!.LatestVersion : null;
}
