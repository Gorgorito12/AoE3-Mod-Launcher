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
    /// <summary>The mod already does it (a cfg of its own with <c>lanAutoStart</c>): leave it alone.</summary>
    EnabledByMod,
    /// <summary>Supported, and nothing there: can be installed.</summary>
    Missing,
    /// <summary>Our helper, current, and nothing of the earlier one left.</summary>
    Installed,
    /// <summary>Our helper, an older build, or with files of the earlier helper still beside it:
    /// installing again brings it up to date.</summary>
    Outdated,
    /// <summary>An <c>avifil32.dll</c> that is not ours sits there (another tool): never replaced.</summary>
    ForeignDll,
    /// <summary>Ours is there, but the mod now enables the key itself: ours is taken out, or the
    /// game would enter twice.</summary>
    SupersededByMod,
}

/// <summary>
/// The optional helper that makes Age of Empires III enter the room's LAN game by itself: ONE file
/// next to the game's executable, <c>avifil32.dll</c>, the launcher's own build of things-new's
/// lanAutoStart steps. The game loads it on its own because it imports that name. It has no cfg:
/// it acts only on the launcher's arguments (<see cref="LanAutoStartArgs"/>), writes no byte of the
/// game and hooks nothing, so it sits beside a mod's own <c>UHC.dll</c> and a mod's own
/// <c>dinput8.dll</c> whatever cfg those bring.
///
/// <para><b>It replaced <c>dinput8.dll</c> plus two lines in <c>engine-ext\engine-ext.cfg</c></b>
/// (launcher v1.1.0). That name and that cfg belong to the mod now: a mod's patch that ships its
/// own pair without the key used to switch the entry off with nothing the launcher could do.
/// <see cref="Install"/> and <see cref="Remove"/> take the earlier pair away, and only when it is
/// provably the launcher's (<see cref="LegacySha256"/>, the marked cfg lines).</para>
///
/// <para><b>Only with the player's consent</b> (<see cref="Models.LauncherConfig.LanHelperChoice"/>),
/// because for the base game that folder is the player's own Age of Empires III — the one place the
/// launcher otherwise never writes. It replaces no file of the game, and everything it adds it can
/// take away.</para>
///
/// <para>The refusals are the point (<c>LanHelperServiceTests</c>): an executable it does not
/// know, another tool's <c>avifil32.dll</c>, a <c>dinput8.dll</c> that is not ours, and a mod that
/// already turns the key on are all left exactly as they are.</para>
/// </summary>
public static class LanHelperService
{
    public const string DllName = "avifil32.dll";
    /// <summary>The DLL's own log, next to the executable.</summary>
    internal const string LogName = "lan-helper.log";
    private const string ResourceName = "LanHelper.avifil32.dll";

    // The earlier helper (launcher v1.1.0): things-new's dinput8.dll and its cfg.
    internal const string LegacyDllName = "dinput8.dll";
    internal const string CfgFolder = "engine-ext";
    internal const string CfgName = "engine-ext.cfg";
    /// <summary>The line that marked what the launcher added to that cfg, so removal takes only that.</summary>
    internal const string CfgMarker = "# Added by AoE3 Mod Launcher (Settings > Games). Remove it there.";
    internal const string CfgKey = "lanAutoStart";

    /// <summary>SHA-256 of the embedded DLL. A test holds it to the resource; update both together.</summary>
    public const string CurrentSha256 = "a3ed4c6d58e8ced43bfff272663045bbd86d32459ae2f17be1ee1e4ac14b77d5";

    /// <summary>Every <c>avifil32.dll</c> the launcher ever shipped, the current one first. Removal
    /// and update recognise the file as ours by these; a hash outside the list is somebody else's.</summary>
    internal static readonly IReadOnlyList<string> KnownSha256 = new[] { CurrentSha256 };

    /// <summary>Every <c>dinput8.dll</c> the launcher shipped as the earlier helper (0.4.0, with
    /// v1.1.0). Only a file with one of these hashes is ever deleted under that name.</summary>
    internal static readonly IReadOnlyList<string> LegacySha256 = new[]
    {
        "d23dfa2678b51e4b1bcd286131712b639c0a78486eef3758c240b10d414ba718",
    };

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
        var modEnables = ModEnablesKey(folder);

        if (!File.Exists(dll))
            return modEnables ? LanHelperState.EnabledByMod : LanHelperState.Missing;

        var hash = TryHash(dll);
        if (hash == null || !KnownSha256.Contains(hash, StringComparer.OrdinalIgnoreCase))
            return modEnables ? LanHelperState.EnabledByMod : LanHelperState.ForeignDll;
        if (modEnables) return LanHelperState.SupersededByMod;
        return string.Equals(hash, CurrentSha256, StringComparison.OrdinalIgnoreCase) && !HasLegacy(folder)
            ? LanHelperState.Installed
            : LanHelperState.Outdated;
    }

    /// <summary>
    /// A cfg of the folder turns <c>lanAutoStart</c> on for a DLL of the mod: a Startup cfg (its
    /// <c>UHC.dll</c>), or the engine-ext cfg read by a <c>dinput8.dll</c>. The earlier helper's own
    /// pair is not the mod: our marked lines count only when a <c>dinput8.dll</c> that is not ours
    /// reads them (a patch put its DLL over ours and runs with our lines).
    /// </summary>
    private static bool ModEnablesKey(string folder)
    {
        var enabledIn = LanAutoStartArgs.CfgEnablingLanAutoStart(folder);
        if (enabledIn == null) return false;
        if (!string.Equals(enabledIn, Path.Combine(CfgFolder, CfgName), StringComparison.OrdinalIgnoreCase))
            return true;
        return !CfgHasOurLines(folder) || ForeignLegacyDll(folder);
    }

    /// <summary>Whether the folder's engine-ext cfg carries the line the launcher used to write.</summary>
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

    /// <summary>A <c>dinput8.dll</c> is there and it is not one the launcher shipped.</summary>
    private static bool ForeignLegacyDll(string folder)
    {
        var dll = Path.Combine(folder, LegacyDllName);
        if (!File.Exists(dll)) return false;
        var hash = TryHash(dll);
        return hash == null || !LegacySha256.Contains(hash, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The earlier helper's <c>dinput8.dll</c>, provably ours: one of our hashes, and not
    /// beside a cfg the mod wrote (a patch may ship a byte-identical DLL with its own cfg).</summary>
    private static bool OurLegacyDll(string folder)
    {
        var dll = Path.Combine(folder, LegacyDllName);
        if (!File.Exists(dll) || CfgBelongsToMod(folder)) return false;
        var hash = TryHash(dll);
        return hash != null && LegacySha256.Contains(hash, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Something of the earlier helper that <see cref="RemoveLegacy"/> would take away.</summary>
    internal static bool HasLegacy(string folder) =>
        OurLegacyDll(folder) || (CfgHasOurLines(folder) && !ForeignLegacyDll(folder));

    /// <summary>
    /// The earlier helper is whole and still makes the entry (its DLL and its marked line): what
    /// the room says before <see cref="Install"/> has replaced it.
    /// </summary>
    internal static bool LegacyStillEnters(string? folder) =>
        !string.IsNullOrWhiteSpace(folder) && OurLegacyDll(folder) && CfgHasOurLines(folder);

    private static string[] SafeReadLines(string path)
    {
        try { return File.ReadAllLines(path); }
        catch (Exception) { return Array.Empty<string>(); }
    }

    /// <summary>
    /// Puts the file next to the executable and takes the earlier helper away. Only from
    /// <see cref="LanHelperState.Missing"/> or <see cref="LanHelperState.Outdated"/>; anything else
    /// is refused. Returns null on success, or why not (for the log; the caller words it for the
    /// player).
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
            // Once the new file is in place: the earlier pair would make the entry a second time
            // (the new DLL stands aside while that cfg line is there).
            RemoveLegacy(folder);
            DiagnosticLog.Write($"LanHelper: installed in {folder} ({DllName} {CurrentSha256[..8]})");
            return null;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"LanHelper: install in {folder} failed: {ex.Message}");
            return ex.Message;
        }
    }

    /// <summary>
    /// Takes away what the launcher added to <paramref name="folder"/> and nothing else: the DLL
    /// only when its hash is one of ours, its log, and the earlier helper
    /// (<see cref="RemoveLegacy"/>). True when nothing of ours is left there.
    /// </summary>
    public static bool Remove(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return true;
        if (HarnessMode.BlockOutsideSandbox($"remove LAN helper from {folder}", folder)) return false;
        try
        {
            var dll = Path.Combine(folder, DllName);
            var foreign = false;
            if (File.Exists(dll))
            {
                var hash = TryHash(dll);
                if (hash != null && KnownSha256.Contains(hash, StringComparer.OrdinalIgnoreCase))
                    File.Delete(dll);
                else
                {
                    foreign = true;
                    DiagnosticLog.Write($"LanHelper: {dll} is not the launcher's file; left alone");
                }
            }
            var log = Path.Combine(folder, LogName);
            if (!foreign && File.Exists(log)) File.Delete(log);
            RemoveLegacy(folder);
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
    /// Takes away the earlier helper (v1.1.0) and nothing else: <c>dinput8.dll</c> only when its
    /// hash is one the launcher shipped, our two marked lines of the engine-ext cfg (the file when
    /// nothing else is left in it), that DLL's own log, and the folder when it ends up empty.
    /// Throws what the file system throws; the callers report it.
    /// </summary>
    private static void RemoveLegacy(string folder)
    {
        // A patch that shipped its own pair, even byte-identical to ours: nothing here is ours.
        if (CfgBelongsToMod(folder))
        {
            if (File.Exists(Path.Combine(folder, LegacyDllName)))
                DiagnosticLog.Write($"LanHelper: {folder} carries the mod's own {CfgFolder}\\{CfgName}; left alone");
            return;
        }
        var dll = Path.Combine(folder, LegacyDllName);
        if (File.Exists(dll))
        {
            var hash = TryHash(dll);
            if (hash != null && LegacySha256.Contains(hash, StringComparer.OrdinalIgnoreCase))
            {
                File.Delete(dll);
                DiagnosticLog.Write($"LanHelper: the earlier helper's {LegacyDllName} removed from {folder}");
            }
            else
            {
                // A mod's patch put its own DLL there: the cfg now belongs to that DLL, and taking
                // the key out of it could switch off the mod's own feature.
                DiagnosticLog.Write($"LanHelper: {dll} is not the launcher's file; it and its cfg are left alone");
                return;
            }
        }

        var cfgDir = Path.Combine(folder, CfgFolder);
        var cfg = Path.Combine(cfgDir, CfgName);
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
            // That DLL's own log is ours too, once its cfg is gone.
            var log = Path.Combine(cfgDir, "engine-ext.log");
            if (!File.Exists(cfg) && File.Exists(log)) File.Delete(log);
            if (!Directory.EnumerateFileSystemEntries(cfgDir).Any()) Directory.Delete(cfgDir);
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

    /// <summary>Whether <paramref name="folder"/> still holds a file the launcher shipped: its
    /// <c>avifil32.dll</c>, or the earlier helper's <c>dinput8.dll</c>.</summary>
    internal static bool HasOurDll(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return false;
        var dll = Path.Combine(folder, DllName);
        if (File.Exists(dll))
        {
            var hash = TryHash(dll);
            if (hash != null && KnownSha256.Contains(hash, StringComparer.OrdinalIgnoreCase)) return true;
        }
        return OurLegacyDll(folder);
    }

    /// <summary>The folder <see cref="ExportTo"/> writes, inside the place the player chose.</summary>
    public const string ExportFolderName = "AoE3 LAN helper";

    /// <summary>
    /// For the manual way: writes the file <see cref="Install"/> would put next to the game, plus a
    /// short note, into <c>&lt;parent&gt;\AoE3 LAN helper\</c>, for the player to copy by hand. A
    /// copy made by hand is recognised as ours by its hash (<see cref="Probe"/> says Installed) and
    /// switching the helper off removes it like any other. Returns the folder written, or null with
    /// the reason in <paramref name="error"/>.
    /// </summary>
    public static string? ExportTo(string parent, string readme, out string? error)
    {
        error = null;
        try
        {
            var folder = Path.Combine(parent, ExportFolderName);
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, DllName), ReadEmbeddedDll());
            File.WriteAllText(Path.Combine(folder, "README.txt"), readme, new UTF8Encoding(true));
            DiagnosticLog.Write($"LanHelper: file saved for a manual copy in {folder}");
            return folder;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"LanHelper: saving the file for a manual copy in {parent} failed: {ex.Message}");
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
