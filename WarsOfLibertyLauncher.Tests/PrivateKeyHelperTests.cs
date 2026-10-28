using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the elevated helper that creates a mod's private registry key without restarting the
/// launcher (<see cref="PrivateKeyHelper"/>), how it is started (<see cref="ElevationService.ResolveSelfLaunch"/>)
/// and when it is used instead of a full elevated restart (<see cref="ElevationService.Decide"/>).
///
/// <para><b>The refusals are the point.</b> The helper runs with administrator rights and takes
/// its orders from a command line, which any program can write: a key or a folder it accepts
/// when it should not is a write into the machine's registry on somebody else's behalf.</para>
/// </summary>
public class PrivateKeyHelperTests
{
    private const string Root = @"Software\Microsoft\Microsoft Games";
    private static readonly string AoDKey = $@"{Root}\Age of Dynasties (Remake)\1.0";
    private const string AoDFolder =
        @"C:\Program Files (x86)\Steam\steamapps\common\Age Of Empires 3\Age of Dynasties (Remake)";

    // ---------------- keys ----------------

    [Theory]
    [InlineData(@"Software\Microsoft\Microsoft Games\Age of Dynasties (Remake)\1.0")]
    [InlineData(@"Software\Microsoft\Microsoft Games\Struggle of Indonesia #2\1.0")]
    [InlineData(@"SOFTWARE\Microsoft\Microsoft Games\Napoleonic Era\1.0")]   // the registry ignores case
    public void AModsOwnKeyIsAccepted(string key)
    {
        Assert.True(PrivateKeyHelper.IsAllowedKey(key));
    }

    [Theory]
    [InlineData(@"Software\Microsoft\Microsoft Games\Age of Empires 3 Expansion Pack 2\1.0")] // the base game
    [InlineData(@"Software\Microsoft\Microsoft Games\Age of Empires 3 Expansion Pack\1.0")]
    [InlineData(@"Software\Microsoft\Microsoft Games\Age of Empires 3\1.0")]
    [InlineData(@"Software\Microsoft\Windows\CurrentVersion\Run")]                            // outside the AoE3 root
    [InlineData(@"Software\Evil\Age of Dynasties\1.0")]
    [InlineData(@"Software\Microsoft\Microsoft Games\Age of Dynasties\1.0\Extra")]           // an extra segment
    [InlineData(@"Software\Microsoft\Microsoft Games\Age of Dynasties")]                     // no \1.0
    [InlineData(@"Software\Microsoft\Microsoft Games\Age of Dynasties\2.0")]
    [InlineData(@"Software\Microsoft\Microsoft Games\Age of Dynasties\1.0\")]               // trailing separator
    [InlineData(@"Software\Microsoft\Microsoft Games\..\1.0")]
    [InlineData(@"Software\Microsoft\Microsoft Games\Age of ""Dynasties""\1.0")]            // a quote
    [InlineData(@"Software\Microsoft\Microsoft Games\Módulo\1.0")]                          // not ASCII
    [InlineData(@"Software\Microsoft\Microsoft Games\A name far too long for the exe slot\1.0")]
    [InlineData("")]
    [InlineData(null)]
    public void THE_ONE_THAT_MATTERS_AnythingElseIsRefused(string? key)
    {
        Assert.False(PrivateKeyHelper.IsAllowedKey(key));
    }

    // ---------------- folders ----------------

    [Theory]
    [InlineData(AoDFolder)]
    [InlineData(AoDFolder + @"\")]
    [InlineData(@"D:\Games\Age of Dynasties")]
    public void AnAbsoluteFolderIsAccepted(string path)
    {
        Assert.True(PrivateKeyHelper.IsAllowedSetupPath(path));
    }

    [Theory]
    [InlineData(@"Games\Age of Dynasties")]          // relative
    [InlineData(@"C:\Games\..\Windows")]             // not in its canonical form
    [InlineData(@"C:\")]                             // a bare drive
    [InlineData("C:")]
    [InlineData(@"\\?\C:\Games\Age of Dynasties")]   // device path
    [InlineData(@"C:\Games\Age of ""Dynasties""")]   // a quote
    [InlineData("C:\\Games\\Age\tof Dynasties")]     // a control character
    [InlineData("")]
    [InlineData(null)]
    public void AnyOtherFolderIsRefused(string? path)
    {
        Assert.False(PrivateKeyHelper.IsAllowedSetupPath(path));
    }

    // ---------------- the command line ----------------

    [Fact]
    public void TheArgumentsRoundTripThroughWindowsOwnParsing()
    {
        var arguments = PrivateKeyHelper.BuildArguments(AoDKey, AoDFolder + @"\");
        Assert.NotNull(arguments);

        // Exactly what the elevated process will see in e.Args: Windows' own splitting.
        var argv = SplitLikeWindows("Aoe3ModLauncher.exe " + arguments);
        var args = argv[1..];

        Assert.True(PrivateKeyHelper.IsInvocation(args));
        Assert.True(PrivateKeyHelper.TryParse(args, out var key, out var folder));
        Assert.Equal(AoDKey, key);
        // The trailing separator is dropped on purpose: before a closing quote it would escape it.
        Assert.Equal(AoDFolder, folder);
    }

    [Fact]
    public void WhatCannotBeExpressedIsNotBuilt()
    {
        Assert.Null(PrivateKeyHelper.BuildArguments($@"{Root}\Bad ""Name""\1.0", AoDFolder));
        Assert.Null(PrivateKeyHelper.BuildArguments(AoDKey, @"relative\folder"));
    }

    [Fact]
    public void ParsingTakesExactlyTheFlagAKeyAndAFolder()
    {
        Assert.False(PrivateKeyHelper.TryParse(null, out _, out _));
        Assert.False(PrivateKeyHelper.TryParse(new[] { PrivateKeyHelper.Flag, AoDKey }, out _, out _));
        Assert.False(PrivateKeyHelper.TryParse(new[] { PrivateKeyHelper.Flag, AoDKey, AoDFolder, "--extra" }, out _, out _));
        // The flag must come first: anywhere else it is just an unknown argument to a normal launch.
        Assert.False(PrivateKeyHelper.IsInvocation(new[] { "--minimized", PrivateKeyHelper.Flag, AoDKey, AoDFolder }));
        Assert.False(PrivateKeyHelper.TryParse(new[] { "--minimized", PrivateKeyHelper.Flag, AoDKey }, out _, out _));
        Assert.False(PrivateKeyHelper.TryParse(
            new[] { PrivateKeyHelper.Flag, $@"{Root}\Age of Empires 3 Expansion Pack 2\1.0", AoDFolder }, out _, out _));
        Assert.True(PrivateKeyHelper.TryParse(new[] { PrivateKeyHelper.Flag, AoDKey, AoDFolder }, out _, out _));
    }

    [Fact]
    public void BadArgumentsNeverReachTheRegistry()
    {
        Assert.Equal((int)PrivateKeyHelper.Exit.BadArguments,
            PrivateKeyHelper.Run(new[] { PrivateKeyHelper.Flag, @"Software\Microsoft\Windows\CurrentVersion\Run", AoDFolder }));
    }

    // ---------------- the removal verb (the launcher's own uninstall) ----------------

    private static readonly string SoIKey = $@"{Root}\Struggle of Indonesia\1.0";

    /// <summary>
    /// The removal verb must be recognised as a helper launch: otherwise it would reach the
    /// single-instance mutex, ask the running launcher to show itself and exit 0 — a helper that
    /// "succeeded" having removed nothing.
    /// </summary>
    [Fact]
    public void TheRemovalVerbIsAHelperLaunchToo()
    {
        Assert.True(PrivateKeyHelper.IsInvocation(new[] { PrivateKeyHelper.RemoveFlag, AoDKey }));
        Assert.False(PrivateKeyHelper.IsInvocation(new[] { "--minimized", PrivateKeyHelper.RemoveFlag, AoDKey }));
    }

    [Fact]
    public void TheRemovalArgumentsRoundTripThroughWindowsOwnParsing()
    {
        var arguments = PrivateKeyHelper.BuildRemoveArguments(new[] { AoDKey, SoIKey });
        Assert.NotNull(arguments);

        var args = SplitLikeWindows("Aoe3ModLauncher.exe " + arguments)[1..];
        Assert.True(PrivateKeyHelper.IsInvocation(args));
        Assert.True(PrivateKeyHelper.TryParseRemove(args, out var keys));
        Assert.Equal(new[] { AoDKey, SoIKey }, keys);
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_TheRemovalVerbRefusesAnythingItCouldNotHaveCreated()
    {
        bool Parses(params string[] args) => PrivateKeyHelper.TryParseRemove(args, out _);

        Assert.False(Parses(PrivateKeyHelper.RemoveFlag));                                      // no key
        Assert.False(Parses(PrivateKeyHelper.RemoveFlag, AoDKey, AoDKey.ToUpperInvariant()));  // a duplicate
        Assert.False(Parses(PrivateKeyHelper.RemoveFlag, $@"{Root}\Age of Empires 3 Expansion Pack 2\1.0"));
        Assert.False(Parses(PrivateKeyHelper.RemoveFlag, @"Software\Microsoft\Windows\CurrentVersion\Run"));
        Assert.False(Parses(PrivateKeyHelper.RemoveFlag, AoDKey, "--extra"));
        Assert.False(Parses(PrivateKeyHelper.RemoveFlag, AoDKey, AoDFolder));                  // a folder is not a key
        Assert.False(Parses(PrivateKeyHelper.Flag, AoDKey));                                   // the WRITE verb
        Assert.False(PrivateKeyHelper.TryParse(new[] { PrivateKeyHelper.RemoveFlag, AoDKey, AoDFolder }, out _, out _));

        var seventeen = new[] { PrivateKeyHelper.RemoveFlag }
            .Concat(Enumerable.Range(1, PrivateKeyHelper.MaxKeysPerRemoval + 1).Select(i => $@"{Root}\Leftover {i}\1.0"))
            .ToArray();
        Assert.False(Parses(seventeen));
        Assert.Null(PrivateKeyHelper.BuildRemoveArguments(seventeen[1..]));
        Assert.True(Parses(seventeen[..^1]));
    }

    [Fact]
    public void BadRemovalArgumentsNeverReachTheRegistry()
    {
        Assert.Equal((int)PrivateKeyHelper.Exit.BadArguments,
            PrivateKeyHelper.Run(new[] { PrivateKeyHelper.RemoveFlag, @"Software\Microsoft\Windows\CurrentVersion\Run" }));
    }

    /// <summary>A key that is not there is already gone — not an error. Read-only: nothing exists to
    /// remove, so this touches no registry key.</summary>
    [Fact]
    public void AKeyAlreadyGoneIsNotAFailure()
    {
        Assert.Equal((int)PrivateKeyHelper.Exit.Ok,
            PrivateKeyHelper.Run(new[] { PrivateKeyHelper.RemoveFlag, $@"{Root}\AoE3ML no such key 7f3a\1.0" }));
    }

    /// <summary>
    /// The helper may run as ANOTHER Windows account, whose per-user hive is somebody else's: the
    /// removal verb reaches HKLM only. Pinned by reading the source, like the startup order below.
    /// </summary>
    [Fact]
    public void TheRemovalVerbNeverTouchesThePerUserHive()
    {
        var source = File.ReadAllText(RepoFile(@"Services\PrivateKeyHelper.cs"));
        var start = source.IndexOf("private static int RunRemove(", StringComparison.Ordinal);
        Assert.True(start > 0, "RunRemove has moved or been renamed.");
        var end = source.IndexOf("private static int RunWrite(", start, StringComparison.Ordinal);
        var body = source[start..end];

        Assert.Contains("RemovePrivateKeyMachine(", body);
        foreach (var userSide in new[] { "CurrentUser", "RemovePrivateKeyUser", "RemoveLeftoverUserMarker", "RemovePrivateKey(" })
            Assert.DoesNotContain(userSide, body);
    }

    // ---------------- starting ourselves ----------------

    private const string Base = @"C:\src\AoE3-Mod-Launcher\WarsOfLibertyLauncher\bin\Release\net8.0-windows\";

    [Fact]
    public void ABundleOrApphostStartsItself()
    {
        var launch = ElevationService.ResolveSelfLaunch(
            @"C:\Users\x\AppData\Local\Programs\Aoe3ModLauncher\Aoe3ModLauncher.exe", Base, _ => false);
        Assert.Equal((@"C:\Users\x\AppData\Local\Programs\Aoe3ModLauncher\Aoe3ModLauncher.exe", ""), launch);
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_TheSmokeTestNeverElevatesTheDotnetHostWithOurArguments()
    {
        var host = @"C:\Program Files\dotnet\dotnet.exe";

        var viaApphost = ElevationService.ResolveSelfLaunch(host, Base, p => p == Base + "Aoe3ModLauncher.exe");
        Assert.Equal((Base + "Aoe3ModLauncher.exe", ""), viaApphost);

        var viaAssembly = ElevationService.ResolveSelfLaunch(host, Base, p => p == Base + "Aoe3ModLauncher.dll");
        Assert.Equal((host, $"\"{Base}Aoe3ModLauncher.dll\" "), viaAssembly);

        Assert.Null(ElevationService.ResolveSelfLaunch(host, Base, _ => false));
    }

    // ---------------- which route ----------------

    [Fact]
    public void THE_ONE_THAT_MATTERS_AWritableFolderNeverRestartsTheLauncherWhenTheHelperCanDoIt()
    {
        Assert.Equal(ElevationService.ElevationRoute.Proceed,
            ElevationService.Decide(folderWritable: true, keyNeedsWrite: false, helperExpressible: false));
        Assert.Equal(ElevationService.ElevationRoute.WritePrivateKey,
            ElevationService.Decide(folderWritable: true, keyNeedsWrite: true, helperExpressible: true));
    }

    [Fact]
    public void OnlyAProtectedFolderOrAnInexpressibleKeyRestartsIt()
    {
        Assert.Equal(ElevationService.ElevationRoute.RelaunchElevated,
            ElevationService.Decide(folderWritable: false, keyNeedsWrite: false, helperExpressible: true));
        Assert.Equal(ElevationService.ElevationRoute.RelaunchElevated,
            ElevationService.Decide(folderWritable: false, keyNeedsWrite: true, helperExpressible: true));
        Assert.Equal(ElevationService.ElevationRoute.RelaunchElevated,
            ElevationService.Decide(folderWritable: true, keyNeedsWrite: true, helperExpressible: false));
    }

    // ---------------- where it runs ----------------

    /// <summary>
    /// The helper must be dispatched before anything in <c>OnStartup</c> that a second process
    /// must not do: it would otherwise lose the single-instance race (forward "show" to the
    /// running launcher and exit having written nothing), create a data folder in what may be
    /// another account's profile, or rotate the log the running launcher is writing.
    /// </summary>
    [Fact]
    public void TheHelperRunsBeforeAnythingElseInStartup()
    {
        var source = File.ReadAllText(RepoFile("App.xaml.cs"));
        var start = source.IndexOf("protected override void OnStartup(", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var dispatch = source.IndexOf("Services.PrivateKeyHelper.IsInvocation(", start, StringComparison.Ordinal);
        Assert.True(dispatch > start, "the helper dispatch was not found in OnStartup");

        foreach (var later in new[]
                 {
                     "base.OnStartup(e);",
                     "Services.AppPaths.EnsureReady();",
                     "new Mutex(",
                     "if (!primary)",
                 })
        {
            var at = source.IndexOf(later, start, StringComparison.Ordinal);
            Assert.True(at > dispatch, $"{later} must come after the helper dispatch");
        }
    }

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var project = Path.Combine(dir.FullName, "WarsOfLibertyLauncher");
            if (File.Exists(Path.Combine(project, "App.xaml")))
                return Path.GetFullPath(Path.Combine(project, relative));
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("WarsOfLibertyLauncher project folder not found");
    }

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CommandLineToArgvW(string lpCmdLine, out int pNumArgs);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    private static string[] SplitLikeWindows(string commandLine)
    {
        var argv = CommandLineToArgvW(commandLine, out var count);
        if (argv == IntPtr.Zero) throw new InvalidOperationException("CommandLineToArgvW failed");
        try
        {
            var result = new List<string>(count);
            for (var i = 0; i < count; i++)
                result.Add(Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size))!);
            return result.ToArray();
        }
        finally
        {
            LocalFree(argv);
        }
    }
}
