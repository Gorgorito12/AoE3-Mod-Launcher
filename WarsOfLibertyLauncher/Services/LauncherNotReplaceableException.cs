using System.IO;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// The self-update swap refused because the running process is not a binary we may rename —
/// see <see cref="AutoUpdatePolicy.IsOurExecutable"/> for what qualifies.
///
/// <para>A type rather than the <see cref="InvalidOperationException"/> this used to throw,
/// because the dialog has to tell these apart and cannot dispatch on a message string:
/// <c>RelaunchUpdated</c> throws that same type for "cannot determine current executable path"
/// and for "no pending launcher update was downloaded", and all three landed in one generic
/// catch that printed the raw English <c>ex.Message</c>.</para>
/// </summary>
public sealed class LauncherNotReplaceableException : Exception
{
    /// <summary>The running executable, as <c>Environment.ProcessPath</c> reported it.</summary>
    public string ProcessPath { get; }

    /// <summary>Just the filename — what the user sees and what the message asks them to change.</summary>
    public string ProcessFileName { get; }

    public LauncherNotReplaceableException(string processPath)
        : base($"Refusing to replace '{Path.GetFileName(processPath)}' — the launcher can only " +
               $"update itself when running as {AutoUpdatePolicy.ExpectedExecutableName}, or as " +
               $"its own self-contained bundle.")
    {
        ProcessPath = processPath;
        ProcessFileName = Path.GetFileName(processPath);
    }
}
