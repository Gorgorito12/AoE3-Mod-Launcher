using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// "Uninstall from this PC" for the launcher itself (2026-10-28): WHAT it deletes
/// (<see cref="SelfInstallService.PlanRemoval"/>), and the rule that makes a wrong target
/// impossible rather than unlikely (<see cref="SelfInstallService.IsSafeToDeleteTree"/>).
///
/// <para><b>The refusals are the point.</b> Everything here ends in <c>rmdir /s</c> after the
/// launcher has exited, with nobody watching: a folder planned by mistake is gone for good. The
/// one rule worth a test of its own is the one about the player's own file — a portable exe is
/// never deleted, and neither is any folder it sits in. None of these tests call
/// <c>UninstallAndExit</c>.</para>
/// </summary>
public class SelfUninstallPlanTests
{
    private const string Profile = @"C:\Users\x";
    private const string Local = Profile + @"\AppData\Local";
    private const string Canon = Local + @"\Programs\Aoe3ModLauncher";
    private const string Temp = Local + @"\Temp\WarsOfLibertyLauncher";
    private const string Data = Local + @"\AoE3ModLauncher";
    private const string Downloads = Profile + @"\Downloads";

    private static readonly string[] Protected =
    {
        Profile, Profile + @"\Desktop", Profile + @"\Documents", Downloads, Local, Local + @"\Temp",
        Profile + @"\AppData\Roaming", @"C:\Program Files", @"C:\Program Files (x86)", @"C:\Windows",
        @"C:\Windows\System32",
    };

    private static SelfInstallService.RemovalPlan Plan(
        string running, bool removeData, IEnumerable<string> dirs, IEnumerable<string>? files = null,
        string canon = Canon, string data = Data)
    {
        var dirSet = new HashSet<string>(dirs, StringComparer.OrdinalIgnoreCase);
        var fileSet = new HashSet<string>(files ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        return SelfInstallService.PlanRemoval(running, canon, Temp, data, removeData, Protected,
            dirSet.Contains, fileSet.Contains);
    }

    [Fact]
    public void AnInstalledCopyGoesWithItsFolder_AndTheDataOnlyWhenAsked()
    {
        var running = Canon + @"\Aoe3ModLauncher.exe";

        var keep = Plan(running, removeData: false, new[] { Canon, Temp, Data });
        Assert.True(keep.RunningInstalledCopy);
        Assert.Equal(new[] { Canon, Temp }, keep.Folders);
        Assert.Equal(Canon, keep.InstalledCopyFolder);
        Assert.Null(keep.DataFolder);
        Assert.Empty(keep.Files);

        var wipe = Plan(running, removeData: true, new[] { Canon, Temp, Data });
        Assert.Equal(new[] { Canon, Temp, Data }, wipe.Folders);
        Assert.Equal(Data, wipe.DataFolder);
    }

    /// <summary>THE ONE THAT MATTERS: keeping the data never puts the data folder — or anything
    /// that holds it — on the list, from whichever copy the launcher runs.</summary>
    [Theory]
    [InlineData(Canon + @"\Aoe3ModLauncher.exe")]
    [InlineData(Downloads + @"\Aoe3ModLauncher.exe")]
    [InlineData(@"D:\Games\Aoe3ModLauncher.exe")]
    public void THE_ONE_THAT_MATTERS_KeepingTheDataNeverListsTheDataFolder(string running)
    {
        var plan = Plan(running, removeData: false, new[] { Canon, Temp, Data });
        Assert.DoesNotContain(plan.Folders, f =>
            string.Equals(f, Data, StringComparison.OrdinalIgnoreCase)
            || Data.StartsWith(f + @"\", StringComparison.OrdinalIgnoreCase));
        Assert.Null(plan.DataFolder);
    }

    [Fact]
    public void APortableExeLeavesItselfAndItsFolder_AndTakesOnlyItsUpdateLeftovers()
    {
        var running = Downloads + @"\Aoe3ModLauncher.exe";
        var leftovers = new[]
        {
            running + ".old",
            running + ".new",
            running + ".new.part",
            Downloads + @"\WarsOfLibertyLauncher_new.exe",
            Downloads + @"\WarsOfLibertyLauncher_new.exe.part",
        };
        var unrelated = Downloads + @"\holiday-photos.zip";

        var plan = Plan(running, removeData: false, new[] { Temp, Downloads },
            leftovers.Append(unrelated).Append(running));

        Assert.False(plan.RunningInstalledCopy);
        Assert.Equal(new[] { Temp }, plan.Folders);
        Assert.Null(plan.InstalledCopyFolder);
        Assert.Equal(leftovers.OrderBy(f => f), plan.Files.OrderBy(f => f));
        Assert.DoesNotContain(running, plan.Files);
        Assert.DoesNotContain(unrelated, plan.Files);
    }

    [Fact]
    public void APortableExeAlsoTakesAnInstalledCopyItIsNotRunningFrom()
    {
        var plan = Plan(Downloads + @"\Aoe3ModLauncher.exe", removeData: false, new[] { Canon, Temp });
        Assert.Equal(Canon, plan.InstalledCopyFolder);
        Assert.Contains(Canon, plan.Folders);
    }

    /// <summary>
    /// THE ONE THAT MATTERS: the file the player started is never inside a folder this deletes —
    /// a portable exe renamed inside the installed folder, one kept in the data folder, one in the
    /// scratch folder. Only the installed copy itself is exempt, because removing it is the point.
    /// </summary>
    [Theory]
    [InlineData(Canon + @"\Aoe3ModLauncher (2).exe", false)]
    [InlineData(Data + @"\Aoe3ModLauncher.exe", true)]
    [InlineData(Temp + @"\copy\Aoe3ModLauncher.exe", true)]
    public void THE_ONE_THAT_MATTERS_TheRunningPortableExeIsNeverInsideADeletedFolder(string running, bool removeData)
    {
        var plan = Plan(running, removeData, new[] { Canon, Temp, Data });
        Assert.False(plan.RunningInstalledCopy);
        Assert.DoesNotContain(plan.Folders, f => running.StartsWith(f + @"\", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AFolderWithoutTheNameTheLauncherGivesItIsKept()
    {
        var running = Downloads + @"\Aoe3ModLauncher.exe";
        var wrongCanon = Local + @"\Programs";
        Assert.Empty(Plan(running, removeData: false, new[] { wrongCanon }, canon: wrongCanon).Folders);

        // A data folder moved somewhere else (the AOE3ML_DATA_DIR override) goes only when it is
        // provably the launcher's: its config file is in it.
        var moved = @"D:\Portable\launcher-data";
        Assert.Null(Plan(running, removeData: true, new[] { moved }, data: moved).DataFolder);
        Assert.Equal(moved, Plan(running, removeData: true, new[] { moved },
            files: new[] { moved + @"\launcher-config.json" }, data: moved).DataFolder);
    }

    [Fact]
    public void AFolderThatIsNotThereIsNotPlanned()
    {
        var plan = Plan(Canon + @"\Aoe3ModLauncher.exe", removeData: true, Array.Empty<string>());
        Assert.Empty(plan.Folders);
        Assert.Empty(plan.Files);
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData("C:")]
    [InlineData(@"Programs\Aoe3ModLauncher")]
    [InlineData("")]
    [InlineData(@"C:\Users")]                     // holds the profile
    [InlineData(Profile)]                          // the profile itself
    [InlineData(Local)]                            // a special folder
    [InlineData(Profile + @"\AppData")]            // holds Local and Roaming
    [InlineData(@"C:\Windows\System32")]
    [InlineData(Downloads)]
    public void IsSafeToDeleteTree_RefusesRootsRelativePathsAndAnythingThatIsOrHoldsAProtectedFolder(string folder)
    {
        Assert.False(SelfInstallService.IsSafeToDeleteTree(folder, Protected));
    }

    [Theory]
    [InlineData(Canon)]
    [InlineData(Temp)]
    [InlineData(Data)]
    [InlineData(Data + @"\")]
    public void IsSafeToDeleteTree_AcceptsTheLaunchersOwnFolders(string folder)
    {
        Assert.True(SelfInstallService.IsSafeToDeleteTree(folder, Protected));
    }

    [Fact]
    public void TheRealProtectedListCoversTheProfileAndTheSystem()
    {
        var folders = SelfInstallService.ProtectedFolders();
        Assert.Contains(folders, f => string.Equals(Path.TrimEndingDirectorySeparator(f),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(folders, f => string.Equals(Path.TrimEndingDirectorySeparator(f),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase));
        Assert.False(SelfInstallService.IsSafeToDeleteTree(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), folders));
    }

    /// <summary>
    /// A kept configuration forgets auto-start (or another copy of the launcher would register
    /// itself again) and the Discord session, whose token is in that file in plain text — and
    /// nothing else: the player's settings and mods are what keeping it is for.
    /// </summary>
    [Fact]
    public void AKeptConfigurationForgetsAutoStartAndTheSessionOnly()
    {
        var config = new LauncherConfig
        {
            StartWithWindows = true,
            MinimizeToTray = true,
            StartMinimized = true,
            Language = "es",
        };
        config.Multiplayer.SessionToken = "eyJ.secret";
        config.Multiplayer.SessionExpiresAt = 123;
        config.Multiplayer.CachedUser = new Models.Multiplayer.LobbyUserSummary();
        config.Mods["wol"] = new ModState { InstallPath = @"D:\Games\Wars of Liberty" };

        SelfInstallService.ForgetOnUninstall(config);

        Assert.False(config.StartWithWindows);
        Assert.False(config.MinimizeToTray);
        Assert.False(config.StartMinimized);
        Assert.Equal("", config.Multiplayer.SessionToken);
        Assert.Equal(0, config.Multiplayer.SessionExpiresAt);
        Assert.Null(config.Multiplayer.CachedUser);
        Assert.Equal("es", config.Language);
        Assert.Equal(@"D:\Games\Wars of Liberty", config.Mods["wol"].InstallPath);
    }

    /// <summary>The mods the window promises to leave alone are the ones on disk, by the name the
    /// player knows — never the base game, never a mod whose folder is gone, never a bare id.</summary>
    [Fact]
    public void TheModsThatStayAreNamedByWhatIsOnDisk()
    {
        var config = new LauncherConfig();
        config.Mods["wol"] = new ModState { InstallPath = @"D:\Games\Wars of Liberty" };
        config.Mods["aoe3-tad"] = new ModState { InstallPath = @"C:\Steam\Age Of Empires 3\bin" };
        config.Mods["gone"] = new ModState { InstallPath = @"D:\Games\Deleted" };
        config.Mods["left-catalog"] = new ModState { InstallPath = @"D:\Games\Old Mod Folder" };
        var copies = new ModState { InstallPath = @"D:\Missing\Napoleonic Era" };
        copies.OtherInstalls.Add(new ModInstall { Id = "2", InstallPath = @"E:\Napoleonic Era (2)" });
        config.Mods["ne"] = copies;

        var profiles = new Dictionary<string, ModProfile>
        {
            ["wol"] = new() { Id = "wol", DisplayName = "Wars of Liberty" },
            ["aoe3-tad"] = new() { Id = "aoe3-tad", DisplayName = "Age of Empires III", IsStockGame = true },
            ["gone"] = new() { Id = "gone", DisplayName = "Gone Mod" },
            ["ne"] = new() { Id = "ne", DisplayName = "Napoleonic Era" },
        };
        var onDisk = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"D:\Games\Wars of Liberty", @"C:\Steam\Age Of Empires 3\bin", @"D:\Games\Old Mod Folder",
            @"E:\Napoleonic Era (2)",
        };

        var names = SelfInstallService.InstalledModNames(config,
            id => id != null && profiles.TryGetValue(id, out var p) ? p : null, onDisk.Contains);

        Assert.Equal(new[] { "Napoleonic Era", "Old Mod Folder", "Wars of Liberty" }, names);
    }
}

/// <summary>
/// The script that deletes the plan after the launcher has exited
/// (<see cref="SelfInstallService.BuildDeferredDelete"/>). Three bugs of the script it replaced
/// are pinned here: it wrote paths into its own text, so a user folder like <c>C:\Users\José</c>
/// was mangled by the console code page and nothing was deleted; it found <c>ping</c>,
/// <c>tasklist</c> and <c>find</c> by searching, current folder first; and it waited on a process
/// id, which a broken <c>tasklist</c> made read as "exited" while the launcher still ran.
/// </summary>
public class DeferredDeleteScriptTests
{
    private const string Sys = @"C:\Windows\System32";

    private static SelfInstallService.DeferredDelete Build(string[] folders, string[] files)
        => SelfInstallService.BuildDeferredDelete(
            @"C:\Users\José\AppData\Local\Temp\aoe3ml-uninstall-1.lock", folders, files, Sys);

    [Fact]
    public void THE_ONE_THAT_MATTERS_TheScriptIsAsciiAndNamesNoPath()
    {
        var folder = @"C:\Users\José\100% & !Ñ\Aoe3ModLauncher";
        var file = @"C:\Users\José\Downloads\Aoe3ModLauncher.exe.old";
        var delete = Build(new[] { folder }, new[] { file });

        Assert.All(delete.Script, c => Assert.True(c < 0x80, $"non-ASCII character U+{(int)c:X4} in the script"));
        foreach (var fragment in new[] { "José", "Ñ", "100%", @"C:\", "Users", "Downloads", "Aoe3ModLauncher" })
            Assert.DoesNotContain(fragment, delete.Script);

        Assert.Equal(folder, delete.Variables["AOE3ML_DEL_1"]);
        Assert.Equal(file, delete.Variables["AOE3ML_FILE_1"]);
        Assert.Equal(@"C:\Users\José\AppData\Local\Temp\aoe3ml-uninstall-1.lock", delete.Variables["AOE3ML_LOCK"]);
        Assert.Equal(Sys, delete.Variables["AOE3ML_SYS"]);
    }

    [Fact]
    public void EveryProgramIsCalledByItsSystem32Path()
    {
        var script = Build(new[] { @"C:\a\Aoe3ModLauncher" }, new[] { @"C:\a\x.old" }).Script;
        var builtins = new[] { "@echo ", "if ", "set ", "goto ", "rmdir ", "del ", ":" };
        foreach (var line in script.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith('"'))
                Assert.StartsWith("\"%AOE3ML_SYS%\\", line);
            else
                Assert.Contains(builtins, b => line.StartsWith(b, StringComparison.Ordinal));
        }
        foreach (var searched in new[] { "tasklist", "find ", "find.exe", "timeout", "powershell" })
            Assert.DoesNotContain(searched, script, StringComparison.OrdinalIgnoreCase);
        // Every ping is the System32 one.
        Assert.Equal(
            Regex.Matches(script, "ping", RegexOptions.IgnoreCase).Count,
            Regex.Matches(script, Regex.Escape("\"%AOE3ML_SYS%\\ping.exe\"")).Count);
    }

    /// <summary>A variable that did not arrive ends the script before anything is deleted — never
    /// <c>rmdir ""</c>, which cmd would read relative to whatever folder it stands in.</summary>
    [Fact]
    public void AMissingVariableStopsTheScriptBeforeAnythingIsDeleted()
    {
        var delete = Build(new[] { @"C:\a\One", @"C:\a\Two" }, new[] { @"C:\a\x.old" });
        var lines = delete.Script.Split("\r\n").ToList();
        var firstDelete = lines.FindIndex(l => l.StartsWith("rmdir ") || l.StartsWith("del /f /q \"%AOE3ML_"));
        Assert.True(firstDelete > 0);
        foreach (var name in delete.Variables.Keys)
        {
            var guard = lines.IndexOf($"if not defined {name} goto done");
            Assert.True(guard >= 0 && guard < firstDelete, $"{name} is not checked before the first delete");
        }
    }

    [Fact]
    public void ItDeletesExactlyWhatItWasGiven_Twice_AndNothingByPattern()
    {
        var script = Build(new[] { @"C:\a\One", @"C:\a\Two" }, new[] { @"C:\a\x.old" }).Script;
        Assert.Equal(2, Regex.Matches(script, Regex.Escape("rmdir /s /q \"%AOE3ML_DEL_1%\"")).Count);
        Assert.Equal(2, Regex.Matches(script, Regex.Escape("rmdir /s /q \"%AOE3ML_DEL_2%\"")).Count);
        Assert.Equal(2, Regex.Matches(script, Regex.Escape("del /f /q \"%AOE3ML_FILE_1%\"")).Count);
        Assert.DoesNotContain("AOE3ML_DEL_3", script);
        Assert.DoesNotContain("AOE3ML_FILE_2", script);
        Assert.DoesNotContain("*", script);
        Assert.DoesNotContain("?", script);
    }

    /// <summary>It waits on the LOCK, and the way out of that wait that is not "the lock is gone"
    /// lands past every delete.</summary>
    [Fact]
    public void ItWaitsForTheLockAndGivesUpWithoutDeleting()
    {
        var lines = Build(new[] { @"C:\a\One" }, Array.Empty<string>()).Script.Split("\r\n").ToList();
        var wait = lines.IndexOf(":wait");
        var gone = lines.IndexOf(":gone");
        var done = lines.IndexOf(":done");
        Assert.True(wait >= 0 && gone > wait && done > gone);
        Assert.Contains("if not exist \"%AOE3ML_LOCK%\" goto gone", lines.Skip(wait).Take(gone - wait));
        Assert.Contains($"if %AOE3ML_N% geq {SelfInstallService.MaxWaitTicks} goto done", lines.Skip(wait).Take(gone - wait));
        Assert.DoesNotContain(lines.Take(gone), l => l.StartsWith("rmdir ") || l.StartsWith("del /f /q \"%AOE3ML_DEL"));
        Assert.Equal("del /f /q \"%~f0\" >nul 2>&1", lines[done + 1]);
    }

    /// <summary>
    /// THE ONE THAT MATTERS, end to end, in a folder of its own: the real script, started the real
    /// way, under a path holding <c>José</c>, <c>%</c>, <c>&amp;</c> and <c>!</c>. It must leave
    /// everything alone while the lock is held, then delete exactly the folder and the file it was
    /// given — and nothing beside them — and remove itself.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheRealScriptWaitsForTheLockThenDeletesOnlyWhatItWasGiven()
    {
        var root = Directory.CreateTempSubdirectory("aoe3ml-José 100% & !Ñ-").FullName;
        Process? script = null;
        FileStream? lockStream = null;
        try
        {
            var target = Path.Combine(root, "Aoe3ModLauncher");
            Directory.CreateDirectory(Path.Combine(target, "runtimes", "win"));
            File.WriteAllText(Path.Combine(target, "Aoe3ModLauncher.exe"), "exe");
            File.WriteAllText(Path.Combine(target, "runtimes", "win", "native.dll"), "dll");
            var leftover = Path.Combine(root, "Aoe3ModLauncher.exe.old");
            File.WriteAllText(leftover, "old");
            var keepFolder = Path.Combine(root, "keep");
            Directory.CreateDirectory(keepFolder);
            File.WriteAllText(Path.Combine(keepFolder, "save.txt"), "mine");
            var keepFile = Path.Combine(root, "Aoe3ModLauncher.exe");
            File.WriteAllText(keepFile, "the portable exe");

            var lockPath = Path.Combine(root, "uninstall.lock");
            lockStream = new FileStream(lockPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                1, FileOptions.DeleteOnClose);
            var scriptPath = Path.Combine(root, "uninstall.cmd");
            var delete = SelfInstallService.BuildDeferredDelete(
                lockPath, new[] { target }, new[] { leftover }, Environment.SystemDirectory);
            script = SelfInstallService.StartDeferredDelete(delete, scriptPath);

            Thread.Sleep(2500);
            Assert.False(script.HasExited, "the script ended while the launcher still held the lock");
            Assert.True(Directory.Exists(target));
            Assert.True(File.Exists(leftover));

            lockStream.Dispose();     // the launcher exits
            lockStream = null;
            Assert.True(script.WaitForExit(60_000), "the script did not finish");

            Assert.False(Directory.Exists(target));
            Assert.False(File.Exists(leftover));
            Assert.True(File.Exists(Path.Combine(keepFolder, "save.txt")));
            Assert.True(File.Exists(keepFile));
            Assert.False(File.Exists(scriptPath));
        }
        finally
        {
            try { if (script is { HasExited: false }) script.Kill(entireProcessTree: true); } catch { }
            script?.Dispose();
            lockStream?.Dispose();
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}

/// <summary>
/// When removing the launcher must wait (<see cref="SelfInstallService.UninstallBlockerFor"/>). The
/// order is pinned: a running game first, then a match whose result is owed, then an operation,
/// then a launcher running as another Windows account — which would remove THAT account's launcher.
/// </summary>
[Collection("wpf-and-language")]
public class SelfUninstallBlockerTests
{
    private static RunningAccount.AccountInfo Same => RunningAccount.Evaluate("PC\\Miro", "Miro", false);
    private static RunningAccount.AccountInfo Other => RunningAccount.Evaluate("PC\\a-admin", "Miro", true);

    [Fact]
    public void NothingBlocksAnIdleLauncherRunningAsThePlayer()
        => Assert.Null(SelfInstallService.UninstallBlockerFor(false, false, false, Same));

    [Fact]
    public void EachReasonIsSaid_TheGameFirst()
    {
        Assert.Equal(Strings.Get("DlgSelfUninstallBlockedGame"), SelfInstallService.UninstallBlockerFor(true, true, true, Other));
        Assert.Equal(Strings.Get("DlgSelfUninstallBlockedMatch"), SelfInstallService.UninstallBlockerFor(false, true, true, Other));
        Assert.Equal(Strings.Get("DlgSelfUninstallBlockedBusy"), SelfInstallService.UninstallBlockerFor(false, false, true, Other));
        var account = SelfInstallService.UninstallBlockerFor(false, false, false, Other);
        Assert.Equal(Strings.Format("DlgSelfUninstallBlockedAccount", "a-admin", "Miro"), account);
        Assert.Contains("a-admin", account);
    }
}
