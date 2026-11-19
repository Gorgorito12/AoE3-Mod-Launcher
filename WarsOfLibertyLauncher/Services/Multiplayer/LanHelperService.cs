using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>What a game folder says about entering the LAN room by itself.</summary>
public enum LanHelperState
{
    /// <summary>Not a game executable the helper knows: nothing is offered.</summary>
    Unsupported,
    /// <summary>The mod already ships it (a cfg with <c>lanAutoStart</c> that is not ours): leave it alone.</summary>
    EnabledByMod,
    /// <summary>Supported, and nothing there: can be installed.</summary>
    Missing,
    /// <summary>Our helper, current.</summary>
    Installed,
    /// <summary>Our helper, an older build or without its cfg line: installing again brings it up to date.</summary>
    Outdated,
    /// <summary>A <c>dinput8.dll</c> that is not ours sits there (another tool): never replaced.</summary>
    ForeignDll,
    /// <summary>Ours is there, but the mod now enables the key itself (a patch that shipped
    /// things-new's DLL with the line): ours is taken out, or the key would run twice.</summary>
    SupersededByMod,
}

/// <summary>
/// The optional helper that makes Age of Empires III enter the room's LAN game by itself: two
/// files next to the game's executable, <c>dinput8.dll</c> (things-new's EngineExt, which the game
/// loads on its own because it imports that name) and <c>engine-ext\engine-ext.cfg</c> with the
/// line <c>lanAutoStart</c>. The launcher's arguments (<see cref="LanAutoStartArgs"/>) do nothing
/// without it.
///
/// <para><b>Only with the player's consent</b> (<see cref="Models.LauncherConfig.LanHelperChoice"/>),
/// because for the base game that folder is the player's own Age of Empires III — the one place the
/// launcher otherwise never writes. It replaces no file of the game, and everything it adds it can
/// take away: <see cref="Remove"/> deletes the DLL only when its hash is one the launcher shipped,
/// and only its own lines of the cfg.</para>
///
/// <para>The refusals are the point (<c>LanHelperServiceTests</c>): an executable it does not
/// know, another tool's <c>dinput8.dll</c>, and a mod that already ships the key are all left
/// exactly as they are.</para>
/// </summary>
public static class LanHelperService
{
    public const string DllName = "dinput8.dll";
    public const string CfgFolder = "engine-ext";
    public const string CfgName = "engine-ext.cfg";
    private const string ResourceName = "LanHelper.dinput8.dll";

    /// <summary>The line that marks what the launcher added to a cfg, so removal takes only that.</summary>
    internal const string CfgMarker = "# Added by AoE3 Mod Launcher (Settings > Games). Remove it there.";
    internal const string CfgKey = "lanAutoStart";

    /// <summary>SHA-256 of the embedded DLL. A test holds it to the resource; update both together.</summary>
    public const string CurrentSha256 = "d23dfa2678b51e4b1bcd286131712b639c0a78486eef3758c240b10d414ba718";

    /// <summary>Every build the launcher ever shipped, the current one first. Removal and update
    /// recognise the DLL as ours by these; a hash outside the list is somebody else's file.</summary>
    internal static readonly IReadOnlyList<string> KnownSha256 = new[] { CurrentSha256 };

    // The Asian Dynasties build every supported executable is made from (PE TimeDateStamp), with
    // its entry point either stock or moved to the UHC loader stub. The DLL checks the same.
    private const uint GameTimeDateStamp = 0x49EF86AC;
    private const uint StockEntryPoint = 0x001876AC;
    private const uint LoaderEntryPoint = 0x007836BE;

    /// <summary>Whether a PE header is a game executable the helper can act in.</summary>
    internal static bool IsSupportedExeHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length < 0x40 || header[0] != (byte)'M' || header[1] != (byte)'Z') return false;
        var pe = BitConverter.ToInt32(header.Slice(0x3C, 4));
        if (pe < 0 || pe + 44 > header.Length) return false;
        if (header[pe] != (byte)'P' || header[pe + 1] != (byte)'E') return false;
        var stamp = BitConverter.ToUInt32(header.Slice(pe + 8, 4));
        var entry = BitConverter.ToUInt32(header.Slice(pe + 40, 4));
        return stamp == GameTimeDateStamp && (entry == StockEntryPoint || entry == LoaderEntryPoint);
    }

    private static bool IsSupportedExe(string exePath)
    {
        try
        {
            using var stream = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var header = new byte[1024];
            var read = stream.Read(header, 0, header.Length);
            return IsSupportedExeHeader(header.AsSpan(0, read));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Read-only: what the folder of <paramref name="exePath"/> holds.</summary>
    public static LanHelperState Probe(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath) || !IsSupportedExe(exePath))
            return LanHelperState.Unsupported;
        var folder = Path.GetDirectoryName(exePath)!;
        var dll = Path.Combine(folder, DllName);
        var enabledIn = LanAutoStartArgs.CfgEnablingLanAutoStart(folder);
        var ourLines = CfgHasOurLines(folder);
        // The key turned on by a Startup cfg is the mod's own DLL (things-new's UHC.dll) doing it.
        var enabledByModDll = enabledIn != null &&
            !string.Equals(enabledIn, Path.Combine(CfgFolder, CfgName), StringComparison.OrdinalIgnoreCase);
        // The key in an engine-ext cfg without our marker is a pair the mod's patch shipped.
        var modOwnsPair = enabledIn != null && !enabledByModDll && !ourLines;

        if (!File.Exists(dll))
            return enabledByModDll || modOwnsPair ? LanHelperState.EnabledByMod : LanHelperState.Missing;

        var hash = TryHash(dll);
        if (hash == null || !KnownSha256.Contains(hash, StringComparer.OrdinalIgnoreCase))
            return enabledIn != null ? LanHelperState.EnabledByMod : LanHelperState.ForeignDll;
        if (enabledByModDll) return LanHelperState.SupersededByMod;
        // Same bytes as ours, but the patch's cfg: the mod's file now, not ours to update or remove.
        if (modOwnsPair) return LanHelperState.EnabledByMod;
        return ourLines && string.Equals(hash, CurrentSha256, StringComparison.OrdinalIgnoreCase)
            ? LanHelperState.Installed
            : LanHelperState.Outdated;
    }

    /// <summary>Whether the folder's engine-ext cfg carries the line the launcher writes.</summary>
    private static bool CfgHasOurLines(string folder)
    {
        var cfg = Path.Combine(folder, CfgFolder, CfgName);
        return File.Exists(cfg) && SafeReadLines(cfg).Any(l => l.Trim() == CfgMarker);
    }

    /// <summary>The engine-ext cfg turns the key on and the launcher did not write that line.</summary>
    private static bool CfgBelongsToMod(string folder)
    {
        var cfg = Path.Combine(folder, CfgFolder, CfgName);
        if (!File.Exists(cfg)) return false;
        var lines = SafeReadLines(cfg);
        return LanAutoStartArgs.CfgEnablesLanAutoStart(lines) && !lines.Any(l => l.Trim() == CfgMarker);
    }

    private static string[] SafeReadLines(string path)
    {
        try { return File.ReadAllLines(path); }
        catch (Exception) { return Array.Empty<string>(); }
    }

    /// <summary>
    /// Puts the two files next to the executable. Only from <see cref="LanHelperState.Missing"/> or
    /// <see cref="LanHelperState.Outdated"/>; anything else is refused. Returns null on success, or
    /// why not (for the log; the caller words it for the player).
    /// </summary>
    public static string? Install(string exePath)
    {
        var state = Probe(exePath);
        if (state is not (LanHelperState.Missing or LanHelperState.Outdated))
            return $"refused: the folder is {state}";
        var folder = Path.GetDirectoryName(exePath)!;
        if (HarnessMode.BlockOutsideSandbox($"install LAN helper in {folder}", folder))
            return "blocked by the test harness";
        try
        {
            var bytes = ReadEmbeddedDll();
            var dll = Path.Combine(folder, DllName);
            var staged = dll + ".aoe3ml-new";
            File.WriteAllBytes(staged, bytes);
            File.Move(staged, dll, overwrite: true);

            var cfgDir = Path.Combine(folder, CfgFolder);
            Directory.CreateDirectory(cfgDir);
            var cfg = Path.Combine(cfgDir, CfgName);
            var lines = File.Exists(cfg) ? File.ReadAllLines(cfg).ToList() : new List<string>();
            if (!LanAutoStartArgs.CfgEnablesLanAutoStart(lines))
            {
                lines.Add(CfgMarker);
                lines.Add(CfgKey);
                File.WriteAllLines(cfg, lines, new UTF8Encoding(false));
            }
            DiagnosticLog.Write($"LanHelper: installed in {folder} ({DllName} {CurrentSha256[..8]}, {CfgFolder}\\{CfgName})");
            return null;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"LanHelper: install in {folder} failed: {ex.Message}");
            return ex.Message;
        }
    }

    /// <summary>
    /// Takes away what <see cref="Install"/> added to <paramref name="folder"/> and nothing else:
    /// the DLL only when its hash is one of ours, our two lines of the cfg (the file when nothing
    /// else is left in it), the DLL's own log, and the folder when it ends up empty. True when
    /// nothing of ours is left there.
    /// </summary>
    public static bool Remove(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return true;
        if (HarnessMode.BlockOutsideSandbox($"remove LAN helper from {folder}", folder)) return false;
        try
        {
            // A patch that shipped its own pair, even byte-identical to ours: nothing here is ours.
            if (CfgBelongsToMod(folder))
            {
                DiagnosticLog.Write($"LanHelper: {folder} carries the mod's own {CfgFolder}\\{CfgName}; left alone");
                return true;
            }
            var dll = Path.Combine(folder, DllName);
            var foreignDll = false;
            if (File.Exists(dll))
            {
                var hash = TryHash(dll);
                if (hash != null && KnownSha256.Contains(hash, StringComparer.OrdinalIgnoreCase))
                    File.Delete(dll);
                else
                {
                    foreignDll = true;
                    DiagnosticLog.Write($"LanHelper: {dll} is not the launcher's file; it and its cfg are left alone");
                }
            }

            var cfgDir = Path.Combine(folder, CfgFolder);
            var cfg = Path.Combine(cfgDir, CfgName);
            // A mod's patch replaced our DLL with its own: the cfg now belongs to that DLL, and
            // taking the key out of it could switch off the mod's own feature.
            if (foreignDll) return true;
            if (File.Exists(cfg))
            {
                var lines = File.ReadAllLines(cfg).ToList();
                var marker = lines.FindIndex(l => l.Trim() == CfgMarker);
                if (marker >= 0)
                {
                    // The marker and the key line it announces; a key line somebody else wrote stays.
                    if (marker + 1 < lines.Count && lines[marker + 1].Trim() == CfgKey) lines.RemoveAt(marker + 1);
                    lines.RemoveAt(marker);
                    if (lines.All(string.IsNullOrWhiteSpace)) File.Delete(cfg);
                    else File.WriteAllLines(cfg, lines, new UTF8Encoding(false));
                }
            }
            if (Directory.Exists(cfgDir))
            {
                // The DLL's own log is ours too, once its cfg is gone.
                var log = Path.Combine(cfgDir, "engine-ext.log");
                if (!File.Exists(cfg) && File.Exists(log)) File.Delete(log);
                if (!Directory.EnumerateFileSystemEntries(cfgDir).Any()) Directory.Delete(cfgDir);
            }
            DiagnosticLog.Write($"LanHelper: removed from {folder}");
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"LanHelper: removing from {folder} failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Takes the helper out of every folder the launcher put it in, and returns the folders where
    /// something of ours is still there. Writable folders directly; the rest in ONE elevated run
    /// (<see cref="LanHelperElevated"/>), then each is checked again on disk rather than trusting
    /// the exit code. A declined prompt simply leaves those folders listed.
    ///
    /// <para>Shared by the Settings switch and the launcher's own uninstall, so both mean the
    /// same thing by "removed". The folder list is updated in <paramref name="folders"/> itself;
    /// the caller saves the config.</para>
    /// </summary>
    public static async System.Threading.Tasks.Task<IReadOnlyList<string>> RemoveEverywhereAsync(List<string> folders)
    {
        folders.RemoveAll(f => ElevationService.CanWriteTo(f) && Remove(f));

        var protectedFolders = folders.Where(f => !ElevationService.CanWriteTo(f)).ToList();
        foreach (var batch in protectedFolders.Chunk(LanHelperElevated.MaxFoldersPerRemoval))
        {
            var args = LanHelperElevated.BuildRemoveArguments(batch);
            if (args == null) continue;
            var run = await ElevationService.RunSelfElevatedAsync(args, TimeSpan.FromMinutes(2));
            DiagnosticLog.Write($"LanHelper: elevated removal from {batch.Length} folder(s) -> {run.Status} {run.ExitCode}");
            if (run.Status == ElevationService.ElevatedRunStatus.Declined) break;
        }

        // Whatever the helper did, the disk says what is left.
        folders.RemoveAll(f => !HasOurDll(f));
        return folders.ToList();
    }

    /// <summary>Whether <paramref name="folder"/> still holds a <c>dinput8.dll</c> the launcher shipped.</summary>
    internal static bool HasOurDll(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return false;
        var dll = Path.Combine(folder, DllName);
        if (!File.Exists(dll)) return false;
        var hash = TryHash(dll);
        return hash != null && KnownSha256.Contains(hash, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The folder <see cref="ExportTo"/> writes, inside the place the player chose.</summary>
    public const string ExportFolderName = "AoE3 LAN helper";

    /// <summary>
    /// For the manual way: writes the same two files <see cref="Install"/> would put next to the
    /// game, plus a short note, into <c>&lt;parent&gt;\AoE3 LAN helper\</c>, for the player to copy
    /// by hand. The cfg carries the launcher's marker, so a copy made by hand is recognised as ours
    /// (<see cref="Probe"/> says Installed) and switching the helper off removes it like any other.
    /// Returns the folder written, or null with the reason in <paramref name="error"/>.
    /// </summary>
    public static string? ExportTo(string parent, string readme, out string? error)
    {
        error = null;
        try
        {
            var folder = Path.Combine(parent, ExportFolderName);
            var cfgDir = Path.Combine(folder, CfgFolder);
            Directory.CreateDirectory(cfgDir);
            File.WriteAllBytes(Path.Combine(folder, DllName), ReadEmbeddedDll());
            File.WriteAllLines(Path.Combine(cfgDir, CfgName), new[] { CfgMarker, CfgKey }, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(folder, "README.txt"), readme, new UTF8Encoding(true));
            DiagnosticLog.Write($"LanHelper: files saved for a manual copy in {folder}");
            return folder;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"LanHelper: saving the files for a manual copy in {parent} failed: {ex.Message}");
            error = ex.Message;
            return null;
        }
    }

    internal static byte[] ReadEmbeddedDll()
    {
        using var stream = typeof(LanHelperService).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The launcher has no {ResourceName} resource.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    internal static string Sha256Of(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string? TryHash(string path)
    {
        try { return Sha256Of(File.ReadAllBytes(path)); }
        catch (Exception) { return null; }
    }
}
