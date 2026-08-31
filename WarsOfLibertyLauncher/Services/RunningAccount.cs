using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Whether the launcher is running under a DIFFERENT Windows account than the one whose session is
/// open — the "right-click, run as administrator, type another account's credentials" case.
///
/// <para><b>Why this exists.</b> Age of Empires III writes its recordings, saves, decks and
/// settings into the Documents of the account that launched it, and it inherits the launcher's
/// token. A player who opens the launcher elevated with a second account on some days and normally
/// on others ends up with his game history split across two profiles, with nothing on screen saying
/// so. Measured on a real machine: three competitive recordings under <c>C:\Users\a-admin\</c> and
/// three casual ones under <c>C:\Users\Miro\</c>, same player, same evening, each folder internally
/// consistent. His launcher settings were split the same way, because <see cref="AppPaths.DataDir"/>
/// also comes from the process token.
/// </para>
///
/// <para>The launcher cannot merge the two without launching the game under someone else's token,
/// which is the pattern antivirus heuristics punish and which this project has always refused. So it
/// does the honest thing instead: it says so.</para>
///
/// <para><b>The decision is pure</b> (<see cref="Evaluate"/>) so it can be tested without any
/// interop, and every path that touches Windows fails toward silence — see <see cref="Current"/>.</para>
/// </summary>
public static class RunningAccount
{
    /// <summary><c>WTSUserName</c>: the account that OWNS the session, not the caller's.</summary>
    private const int WtsUserName = 5;

    /// <summary><c>WTS_CURRENT_SESSION</c> — the session this process runs in.</summary>
    private const uint WtsCurrentSession = unchecked((uint)-1);

    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WTSQuerySessionInformationW(
        IntPtr hServer, uint sessionId, int wtsInfoClass, out IntPtr ppBuffer, out uint pBytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr pMemory);

    /// <summary>Who is running the launcher, and who owns the session it runs in.</summary>
    /// <param name="ProcessUser">The account the process runs as, without its domain prefix.</param>
    /// <param name="SessionUser">The account whose Windows session is open, without its domain prefix.</param>
    /// <param name="Elevated">Whether the process holds administrator rights.</param>
    /// <param name="Mismatch">
    /// The two accounts differ. Only ever true when BOTH names are known: an unanswered question is
    /// not evidence of a problem.
    /// </param>
    public readonly record struct AccountInfo(
        string ProcessUser,
        string SessionUser,
        bool Elevated,
        bool Mismatch);

    /// <summary>
    /// The decision, with no Windows in it.
    ///
    /// <para>Names are compared without their domain prefix and without case, because the same
    /// account legitimately appears as <c>Miro</c>, <c>PC\Miro</c> and <c>miro</c> depending on which
    /// API answered. A blank on either side yields no mismatch — <b>the failure direction is
    /// deliberate</b>: a false negative leaves things exactly as they are today, while a false
    /// positive is an alarming message that is also wrong. Same reasoning as
    /// <see cref="ConnectivityState"/> observing rather than probing.</para>
    /// </summary>
    internal static AccountInfo Evaluate(string? processUser, string? sessionUser, bool elevated)
    {
        var process = StripDomain(processUser);
        var session = StripDomain(sessionUser);

        var mismatch = process.Length > 0
            && session.Length > 0
            && !string.Equals(process, session, StringComparison.OrdinalIgnoreCase);

        return new AccountInfo(process, session, elevated, mismatch);
    }

    /// <summary>
    /// <c>PC\Miro</c> and <c>miro@example.com</c> both become <c>Miro</c>.
    ///
    /// <para>Trimming a UPN suffix can only make two names MORE likely to match, which is the safe
    /// direction here.</para>
    /// </summary>
    internal static string StripDomain(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";

        var trimmed = name.Trim();

        var slash = trimmed.LastIndexOf('\\');
        if (slash >= 0) trimmed = trimmed[(slash + 1)..];

        var at = trimmed.IndexOf('@');
        if (at > 0) trimmed = trimmed[..at];

        return trimmed.Trim();
    }

    /// <summary>Reads both accounts from Windows. Never throws; any failure reports no mismatch.</summary>
    public static AccountInfo Current()
    {
        var processUser = "";
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            processUser = identity.Name ?? "";
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"RunningAccount: could not read the process account: {ex.Message}");
        }

        return Evaluate(processUser, SessionUserName(), ElevationService.IsRunningAsAdmin());
    }

    /// <summary>
    /// The account that owns this process's session.
    ///
    /// <para><b>The session, not the console.</b> "Run as different user" leaves the process in the
    /// SAME Terminal Services session as the person sitting at the machine, so asking the session who
    /// owns it answers exactly the question — and unlike <c>WTSGetActiveConsoleSessionId</c> it stays
    /// right over Remote Desktop, where the physical console belongs to nobody.</para>
    /// </summary>
    private static string SessionUserName()
    {
        var buffer = IntPtr.Zero;
        try
        {
            if (!WTSQuerySessionInformationW(IntPtr.Zero, WtsCurrentSession, WtsUserName, out buffer, out _))
            {
                DiagnosticLog.Write(
                    $"RunningAccount: WTSQuerySessionInformation failed ({Marshal.GetLastWin32Error()}).");
                return "";
            }

            return Marshal.PtrToStringUni(buffer) ?? "";
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"RunningAccount: could not read the session account: {ex.Message}");
            return "";
        }
        finally
        {
            if (buffer != IntPtr.Zero) WTSFreeMemory(buffer);
        }
    }

    /// <summary>
    /// That account's profile folder (<c>C:\Users\Miro</c>), or null when it cannot be confirmed.
    ///
    /// <para><b>Resolved exactly, never guessed.</b> Name to SID, SID to <c>ProfileImagePath</c> —
    /// building <c>C:\Users\{name}</c> by hand would be wrong for Microsoft accounts, whose folder is
    /// a truncation of the address rather than the account name. Reading <c>HKLM</c> needs no
    /// elevation. A path we cannot confirm on disk is reported as unknown, so the UI shows nothing
    /// rather than a plausible-looking invention.</para>
    /// </summary>
    public static string? ProfileFolderOf(string? userName)
    {
        if (string.IsNullOrWhiteSpace(userName)) return null;

        try
        {
            var sid = (SecurityIdentifier)new NTAccount(userName).Translate(typeof(SecurityIdentifier));

            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\" + sid.Value);

            if (key?.GetValue("ProfileImagePath") is not string raw || string.IsNullOrWhiteSpace(raw))
                return null;

            var path = Environment.ExpandEnvironmentVariables(raw);
            return Directory.Exists(path) ? path : null;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write(
                $"RunningAccount: could not resolve the profile folder of '{userName}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// The signed-in account's game-data folder — the mirror of the one the process account is
    /// writing to — or null when it cannot be confirmed on disk.
    ///
    /// <para>Narrows from the mod's own folder to <c>My Games</c> to the profile root, taking the
    /// first that exists, so callers show the most specific REAL path instead of a deep one that
    /// was never created. Returning null rather than an unverified guess is the point: a
    /// plausible-looking wrong path sends the player looking in the wrong place.</para>
    /// </summary>
    public static string? SignedInDataFolder(string? sessionUser, string? modFolderName)
    {
        var profile = ProfileFolderOf(sessionUser);
        if (string.IsNullOrEmpty(profile)) return null;

        try
        {
            var myGames = Path.Combine(profile, "Documents", "My Games");

            if (!string.IsNullOrWhiteSpace(modFolderName))
            {
                var modFolder = Path.Combine(myGames, modFolderName);
                if (Directory.Exists(modFolder)) return modFolder;
            }

            if (Directory.Exists(myGames)) return myGames;

            var documents = Path.Combine(profile, "Documents");
            return Directory.Exists(documents) ? documents : profile;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write(
                $"RunningAccount: could not resolve the signed-in data folder: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// One line for the debug log, written on EVERY launch.
    ///
    /// <para>"They match" is the answer that rules the whole hypothesis out, so it is worth as much as
    /// the alarming one — and before this line existed a diagnostic bundle could not tell "two Windows
    /// accounts" apart from "elevated with someone else's", which is what made a real report take an
    /// hour to read.</para>
    /// </summary>
    public static string Describe(AccountInfo info)
    {
        var process = info.ProcessUser.Length > 0 ? info.ProcessUser : "(unknown)";
        var session = info.SessionUser.Length > 0 ? info.SessionUser : "(unknown)";
        var verdict = info.Mismatch
            ? "MISMATCH — game data, launcher config and the auto-start key all belong to the process account"
            : "same account";

        return $"Account: process='{process}' session='{session}' elevated={info.Elevated} — {verdict}.";
    }
}
