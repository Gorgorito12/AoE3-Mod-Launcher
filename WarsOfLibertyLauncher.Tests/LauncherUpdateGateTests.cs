using System;
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
