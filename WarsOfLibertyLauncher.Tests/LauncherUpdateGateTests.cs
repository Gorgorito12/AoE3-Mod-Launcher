using System;
using System.IO;
using System.Windows;
using WarsOfLibertyLauncher;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Multiplayer closes while a newer launcher release is pending.
///
/// <para><b>What was reported.</b> A launcher on 1.0.14 with the "Update v1.0.14d" pill lit
/// had the whole Multiplayer tab open — rooms, chat, ranking. That was what the code did on
/// purpose: the only gate that existed was the server's opt-in <c>MIN_LAUNCHER_VERSION</c>,
/// off by default and refusing room entry only. The maintainer's rule is the opposite: every
/// release is mandatory for multiplayer, decided by the launcher itself from the same check
/// that lights the pill.</para>
/// </summary>
[Collection("wpf-and-language")]
public class LauncherUpdateGateTests
{
    private static LauncherUpdateService.UpdateCheckResult Pending(bool available) => new(
        UpdateAvailable: available,
        CurrentVersion: "v1.0.14",
        LatestVersion: "v1.0.14d",
        DownloadUrl: available ? "https://example.invalid/Aoe3ModLauncher.exe" : null,
        DownloadSize: 0,
        RemoteTag: "v1.0.14d");

    // ---------------------------------------------------------------- the decision

    /// <summary>THE ONE THAT MATTERS: a pending update closes multiplayer.</summary>
    [Fact]
    public void APendingUpdateClosesMultiplayer()
    {
        Assert.True(LauncherUpdateGate.ShouldGate(Pending(true), bypass: false));
        Assert.Equal("v1.0.14d", LauncherUpdateGate.RequiredVersion(Pending(true), bypass: false));
    }

    /// <summary>Nothing pending — or no check yet — leaves it open.</summary>
    [Fact]
    public void NothingPendingLeavesItOpen()
    {
        Assert.False(LauncherUpdateGate.ShouldGate(Pending(false), bypass: false));
        Assert.False(LauncherUpdateGate.ShouldGate(null, bypass: false));
        Assert.Null(LauncherUpdateGate.RequiredVersion(Pending(false), bypass: false));
        Assert.Null(LauncherUpdateGate.RequiredVersion(null, bypass: false));
    }

    /// <summary>
    /// The maintainer's switch. A locally published build calls itself "v1.0.14" — the letter
    /// only exists as an argument to publish.ps1 — so against a released "v1.0.14d" it would
    /// be shut out of multiplayer every time it ran.
    /// </summary>
    [Fact]
    public void TheBypassKeepsItOpenForTheMaintainer()
    {
        Assert.False(LauncherUpdateGate.ShouldGate(Pending(true), bypass: true));
        Assert.Null(LauncherUpdateGate.RequiredVersion(Pending(true), bypass: true));
        Assert.Equal("--no-update-gate", LauncherUpdateGate.BypassArgument);
    }

    /// <summary>
    /// The four ways out, and none of them is a player. The switch is for a locally published
    /// build; a DEBUG build is the person writing the launcher, who presses F5 and passes no
    /// arguments at all; a debugger says the same of a Release build being stepped through;
    /// a developer build is any of them recognised from the folder it runs in. All four false
    /// is the only combination a player is ever in.
    /// </summary>
    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    [InlineData(true, true, true, true)]
    public void EachWayOutIsEnoughOnItsOwn(bool argument, bool debugBuild, bool debugger, bool developerBuild)
        => Assert.True(LauncherUpdateGate.Bypassed(argument, debugBuild, debugger, developerBuild));

    [Fact]
    public void APlayerHasNoWayOut()
        => Assert.False(LauncherUpdateGate.Bypassed(false, false, false, false));

    // ------------------------------------------------- recognising a build output

    /// <summary>
    /// WHAT WENT WRONG, and the reason this way out exists at all. The other three left a
    /// hole: a RELEASE build run locally without a debugger — Ctrl+F5, or a double-click on
    /// bin\Release\…\.exe — matched none of them, and since every local build calls itself
    /// "v1.0.14" it is "older" than the published release forever, so multiplayer closed on
    /// the maintainer every single time. It was diagnosed from the log's SILENCE: App writes
    /// "Multiplayer will not close…" whenever any way out is true, and that run had no such
    /// line.
    ///
    /// <para>The signal is the folder, so it covers every way of starting a local build
    /// without anybody configuring anything: a framework-dependent build leaves both
    /// *.deps.json and *.runtimeconfig.json beside the executable, and the published build —
    /// self-contained, single-file — embeds both and leaves neither.</para>
    /// </summary>
    [Fact]
    public void ABuildOutputIsRecognisedAndAPublishedReleaseIsNot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wol-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // What a player has: the single-file exe on its own.
            File.WriteAllText(Path.Combine(dir, "Aoe3ModLauncher.exe"), "");
            Assert.False(LauncherUpdateGate.IsDeveloperBuild(dir));

            // BOTH are required, not either: one alone is a leftover, and the safe side of a
            // wrong answer is "this is a player" — a player mistaken for a developer would
            // stop receiving automatic updates.
            File.WriteAllText(Path.Combine(dir, "Aoe3ModLauncher.deps.json"), "{}");
            Assert.False(LauncherUpdateGate.IsDeveloperBuild(dir));

            // What bin\Debug and bin\Release both look like.
            File.WriteAllText(Path.Combine(dir, "Aoe3ModLauncher.runtimeconfig.json"), "{}");
            Assert.True(LauncherUpdateGate.IsDeveloperBuild(dir));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    /// <summary>Nothing to read is not a build output. A folder that is missing, empty or
    /// unnamed answers "player", which is the side that only ever costs the developer a
    /// switch and never costs a player their updates.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingToReadMeansAPlayer(string? baseDirectory)
        => Assert.False(LauncherUpdateGate.IsDeveloperBuild(baseDirectory));

    [Fact]
    public void AFolderThatDoesNotExistMeansAPlayer()
        => Assert.False(LauncherUpdateGate.IsDeveloperBuild(
            Path.Combine(Path.GetTempPath(), "wol-gate-missing-" + Guid.NewGuid().ToString("N"))));

    /// <summary>
    /// The one thing here that is NOT covered, said out loud rather than faked: App.NoUpdateGate
    /// itself. It reads a const from an #if DEBUG split and static state on a WPF Application,
    /// so a test cannot vary it. Its runtime oracle is the diagnostic log line — if
    /// "Multiplayer will not close for a pending update: …" is absent, every way out was false.
    /// </summary>
    [Fact]
    public void TheWiringItselfIsPinnedByTheLogNotByThisSuite()
        => Assert.Equal("--no-update-gate", LauncherUpdateGate.BypassArgument);

    // ---------------------------------------------------------------- the tab

    /// <summary>
    /// The gate is a real element over the whole tab, toggled by the version it is handed,
    /// and it names both versions in the language in use.
    /// </summary>
    [Fact]
    public void TheTabIsCoveredWhileAVersionIsPendingAndOpenWhenItIsNot()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            try
            {
                Strings.SetLanguage("es");
                var tab = new MultiplayerTab();
                Assert.False(tab.IsUpdateGated);
                Assert.Equal(Visibility.Collapsed, tab.UpdateGateOverlay.Visibility);

                tab.SetLauncherUpdateGate("v1.0.14d", "v1.0.14");
                Assert.True(tab.IsUpdateGated);
                Assert.Equal(Visibility.Visible, tab.UpdateGateOverlay.Visibility);
                Assert.Contains("v1.0.14d", tab.UpdateGateBodyText.Text);
                Assert.Contains("v1.0.14", tab.UpdateGateBodyText.Text);
                Assert.Contains("v1.0.14d", (string)tab.UpdateGateButton.Content);
                Assert.Equal("Actualiza el launcher para jugar en línea", tab.UpdateGateTitleText.Text);

                // A language change while the gate is up re-renders it.
                Strings.SetLanguage("en");
                tab.ApplyStrings();
                Assert.Equal("Update the launcher to play online", tab.UpdateGateTitleText.Text);

                tab.SetLauncherUpdateGate(null, "v1.0.14");
                Assert.False(tab.IsUpdateGated);
                Assert.Equal(Visibility.Collapsed, tab.UpdateGateOverlay.Visibility);

                // Blank is the same as null: nothing to update to, nothing to close.
                tab.SetLauncherUpdateGate("  ", "v1.0.14");
                Assert.False(tab.IsUpdateGated);
            }
            finally { Strings.SetLanguage(previous); }
        });

        Assert.Null(error);
    }

    /// <summary>The update button is the gold pill by another name: it hands the click to
    /// whoever owns the self-update dialog, and survives having nobody to hand it to.</summary>
    [Fact]
    public void TheGateButtonAsksForTheUpdate()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            // No Attach at all: the click must not throw.
            tab.SetLauncherUpdateGate("v1.0.14d", "v1.0.14");
            tab.UpdateGateButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        });

        Assert.Null(error);
    }

    /// <summary>All four texts exist in both languages: a key defined in only one renders as
    /// the key itself, which is visible but easy to miss in a language you do not read.</summary>
    [Theory]
    [InlineData("MpUpdateGateTitle")]
    [InlineData("MpUpdateGateBody")]
    [InlineData("MpUpdateGateButton")]
    [InlineData("MpUpdateGateNote")]
    public void TheGateTextsExistInBothLanguages(string key)
    {
        var previous = Strings.Language;
        try
        {
            Strings.SetLanguage("es");
            Assert.NotEqual(key, Strings.Get(key));
            Strings.SetLanguage("en");
            Assert.NotEqual(key, Strings.Get(key));
        }
        finally { Strings.SetLanguage(previous); }
    }

    // ---------------------------------------------------------------- the server's refusal

    /// <summary>
    /// The server's own gate, for whoever turned update checks off. Creating a room used to
    /// catch its refusal generically and print the server's sentence in the dialog's error
    /// strip — no notice, no update offer — while joining one already did both. The dialog
    /// now recognises the refusal by its code, which is the only stable part of it.
    /// </summary>
    [Fact]
    public void CreatingARoomRecognisesTheServersTooOldRefusal()
    {
        var tooOld = new LobbyApiException(426, "launcher_too_old",
            "Este launcher es demasiado antiguo para el multijugador.", null);
        var other = new LobbyApiException(400, "bad_request", "no", null);

        Assert.True(CreateLobbyDialog.IsLauncherTooOld(tooOld));
        Assert.True(CreateLobbyDialog.IsLauncherTooOld(
            new LobbyApiException(426, "LAUNCHER_TOO_OLD", "", null)));
        Assert.False(CreateLobbyDialog.IsLauncherTooOld(other));
        Assert.False(CreateLobbyDialog.IsLauncherTooOld(null!));
    }
}
