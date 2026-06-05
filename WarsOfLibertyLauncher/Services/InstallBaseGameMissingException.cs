using System;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Thrown by <see cref="NativeInstallService.InstallAsync"/> when the AoE3
/// base-game clone copied ZERO files — i.e. the mod would be overlaid onto an
/// empty base and ship without the engine DLLs (RockallDLL/binkw32/granny2/…)
/// or <c>data\*.xml</c>, so the game exits instantly on launch.
///
/// This is a CONFIGURATION/source failure, not a transient one: the AoE3
/// source is missing/empty, or an exclusion removed it (the canonical case is
/// the stock-game <c>…\bin</c> path landing in the sibling-exclusion list —
/// see <c>LauncherConfig.GetSiblingInstallPaths</c>'s <c>IsStockGame</c>
/// guard). Retrying won't help, so the install flow surfaces it as a clear,
/// localized error instead of looping like it does for corrupt-payload
/// (<see cref="System.IO.InvalidDataException"/>) cases.
/// </summary>
public sealed class InstallBaseGameMissingException : Exception
{
    /// <summary>The AoE3 source folder the clone read from (for diagnostics).</summary>
    public string Aoe3SourcePath { get; }

    public InstallBaseGameMissingException(string aoe3SourcePath)
        : base($"AoE3 base clone copied 0 files from '{aoe3SourcePath}'. " +
               "Source missing/empty or fully excluded — refusing to overlay a " +
               "mod onto an empty base game.")
    {
        Aoe3SourcePath = aoe3SourcePath;
    }
}
