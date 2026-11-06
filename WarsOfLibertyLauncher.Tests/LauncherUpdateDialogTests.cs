using System.Linq;
using System.Windows;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>Design 77: which buttons each state offers. A button that does nothing, or a Retry
/// that cannot help, is what this window used to have.</summary>
[Collection("wpf-and-language")]
public class LauncherUpdateDialogTests
{
    private static void InDialog(string state, System.Action<LauncherUpdateDialog> check, string lang = Strings.LangEs)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            var before = Strings.Language;
            Strings.SetLanguage(lang);
            try
            {
                var dialog = new LauncherUpdateDialog(LauncherUpdateDemo.Result(state == "link"), null,
                    state == "game" ? () => true : null,
                    state == "busy" ? () => "Hundred Days" : null,
                    preview: state);
                check(dialog);
                dialog.Close();
            }
            finally { Strings.SetLanguage(before); }
        });
        Assert.Null(error);
    }

    private static string[] Buttons(LauncherUpdateDialog d) =>
        new[] { d.SecondaryButton, d.OpenReleasePageButton, d.ActionButton }
            .Where(b => b.Visibility == Visibility.Visible)
            .Select(b => (string)b.Content).ToArray();

    [Theory]
    [InlineData("idle", "Más tarde|Descargar")]
    [InlineData("down", "Cancelar")]
    [InlineData("verify", "Cancelar")]
    [InlineData("ready", "Más tarde|Reiniciar ahora")]
    [InlineData("fdown", "Cerrar|Descargar desde GitHub ↗|Reintentar")]
    [InlineData("fblock", "Cerrar|Descargar desde GitHub ↗|Reintentar")]
    [InlineData("fverify", "Cerrar|Descargar desde GitHub ↗")]
    [InlineData("frestart", "Cerrar|Descargar desde GitHub ↗")]
    [InlineData("fname", "Cerrar|Descargar desde GitHub ↗")]
    public void EachStateOffersItsOwnButtons(string state, string expected) =>
        InDialog(state, d => Assert.Equal(expected.Split('|'), Buttons(d)));

    [Fact]
    public void WhileDownloadingThereIsNoDeadPrimaryButton() =>
        InDialog("down", d => Assert.Equal(Visibility.Collapsed, d.ActionButton.Visibility));

    [Fact]
    public void RetryIsNotOfferedWhenTheSameBytesWouldFailAgain() =>
        InDialog("fverify", d => Assert.DoesNotContain(Strings.Get("DlgLauncherUpdateBtnRetry"), Buttons(d)));

    [Fact]
    public void RestartWaitsForAMatch() => InDialog("game", d =>
    {
        Assert.False(d.ActionButton.IsEnabled);
        Assert.Equal(Visibility.Visible, d.InMatchNotice.Visibility);
    });

    [Fact]
    public void AModDownloadOnlyWarns() => InDialog("busy", d =>
    {
        Assert.True(d.ActionButton.IsEnabled);
        Assert.Equal(Visibility.Visible, d.ModBusyNotice.Visibility);
        Assert.Contains("Hundred Days", RevealText.PlainTextOf(d.ModBusyText));
    });

    [Fact]
    public void TheNotesShowOnlyBeforeTheDownload()
    {
        InDialog("idle", d => Assert.Equal(Visibility.Visible, d.ReleaseNotesSection.Visibility));
        InDialog("down", d => Assert.Equal(Visibility.Collapsed, d.ReleaseNotesSection.Visibility));
    }

    [Fact]
    public void ALinkOnlyNoteIsAButtonAndLowSpaceWarnsWithoutBlocking() => InDialog("link", d =>
    {
        Assert.Equal(Visibility.Visible, d.ReleaseLinkSection.Visibility);
        Assert.Equal(Visibility.Collapsed, d.ReleaseNotesSection.Visibility);
        Assert.Equal(Visibility.Visible, d.LowSpaceNotice.Visibility);
        Assert.True(d.ActionButton.IsEnabled);
    });

    [Fact]
    public void TheBlockedFolderIsNamed() => InDialog("fblock", d =>
    {
        Assert.Equal(Visibility.Visible, d.FolderBox.Visibility);
        Assert.NotEmpty(d.FolderText.Text);
    });

    [Fact]
    public void TheRenameProblemNamesBothFiles() => InDialog("fname", d =>
    {
        var text = RevealText.PlainTextOf(d.BodyText);
        Assert.Contains("Aoe3ModLauncher (1).exe", text);
        Assert.Contains(AutoUpdatePolicy.ExpectedExecutableName, text);
    });

    [Fact]
    public void EveryStateReadsInEnglishToo()
    {
        foreach (var state in new[] { "idle", "down", "verify", "ready", "fdown", "fblock", "fverify", "frestart", "fname" })
            InDialog(state, d => Assert.DoesNotContain("Dlg", d.HeaderText.Text), Strings.LangEn);
    }

    [Theory]
    [InlineData("v2", "v2", true, true)]
    [InlineData("V2", "v2", true, true)]
    [InlineData("v2", "v2", false, false)] // the file is gone
    [InlineData("v1", "v2", true, false)]  // another release is staged
    [InlineData(null, "v2", true, false)]  // nothing verified this session
    [InlineData("", "", true, false)]
    public void OnlyAFileThisSessionVerifiedCountsAsReady(string? staged, string? tag, bool exists, bool expected) =>
        Assert.Equal(expected, LauncherUpdateService.IsStagedFor(staged, tag, exists));
}
