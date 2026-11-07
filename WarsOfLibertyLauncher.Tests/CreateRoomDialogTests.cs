using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>Design 88. What matters is what the window says before the click, and that what it
/// shows is the room it would ask the server for.</summary>
[Collection("wpf-and-language")]
public class CreateRoomDialogTests
{
    private static CreateLobbyDialog NewDialog(LauncherConfig? config = null, Func<ModProfile, Task<string>>? hash = null, bool observers = false)
    {
        var profile = new ModProfile { Id = "wol", DisplayName = "WoL" };
        return new CreateLobbyDialog(
            new MultiplayerSession(new LauncherConfig()),
            new List<ModProfile> { profile },
            profile,
            hash ?? (_ => Task.FromResult("0123456789abcdef")),
            _ => new ModCopyInfo(false, false, Array.Empty<ModCopyChoice>()),
            _ => Task.CompletedTask,
            developerMode: observers,
            config: config);
    }

    private static void Run(Action body)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            try
            {
                Strings.SetLanguage("es");
                body();
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    [Theory]
    [InlineData("casual", 0)]
    [InlineData("1v1", 1)]
    [InlineData("2v2", 2)]
    [InlineData("3v3", 3)]
    [InlineData("anything else", 0)]
    public void ItOpensOnTheLastKindOfRoom(string last, int segment)
    {
        Run(() =>
        {
            var dialog = NewDialog(new LauncherConfig { LastRoomKind = last });
            Assert.Equal("active", dialog.KindRow.Buttons.ElementAt(segment).Tag as string);
            dialog.Close();
        });
    }

    [Theory]
    [InlineData(RoomFormat.Casual, "casual")]
    [InlineData(RoomFormat.OneVOne, "1v1")]
    [InlineData(RoomFormat.TwoVTwo, "2v2")]
    [InlineData(RoomFormat.ThreeVThree, "3v3")]
    public void TheKindSurvivesTheSettings(RoomFormat kind, string token)
    {
        Assert.Equal(token, RoomFormats.KindToken(kind));
        Assert.Equal(kind, RoomFormats.FromKindToken(token));
    }

    /// <summary>The footer says what stops the room before the click, the first thing first.</summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheFooterNamesTheFirstErrorBeforeTheClick()
    {
        Run(() =>
        {
            var dialog = NewDialog();
            Assert.True(dialog.CreateButton.IsEnabled);
            Assert.Equal(Visibility.Collapsed, dialog.ErrorText.Visibility);

            dialog.RoomTitleBox.Text = "Wo";
            Assert.False(dialog.CreateButton.IsEnabled);
            Assert.Equal(Strings.Get("MpCreateDialogTitleTooShort"), dialog.ErrorText.Text);

            dialog.RoomTitleBox.Text = "Mi sala";
            dialog.PrivateRoomCheck.IsChecked = true;
            Assert.False(dialog.CreateButton.IsEnabled);
            Assert.Equal(Strings.Get("MpCreateDialogNoPassword"), dialog.ErrorText.Text);

            dialog.PasswordBox.Password = "guardia";
            Assert.True(dialog.CreateButton.IsEnabled);
            Assert.Equal(Visibility.Collapsed, dialog.ErrorText.Visibility);
            dialog.Close();
        });
    }

    [Fact]
    public void AFailedFingerprintComesBeforeTheNameAndStopsTheRoom()
    {
        Run(() =>
        {
            var dialog = NewDialog(hash: _ => throw new InvalidOperationException("no"));
            dialog.RoomTitleBox.Text = "Wo";
            Assert.False(dialog.CreateButton.IsEnabled);
            Assert.Equal(Strings.Get("MpCreateDialogNoFingerprint"), dialog.ErrorText.Text);
            dialog.Close();
        });
    }

    [Fact]
    public void WhileTheFingerprintIsComputedCreateWaitsAndNothingIsWrong()
    {
        Run(() =>
        {
            var dialog = NewDialog(hash: _ => new TaskCompletionSource<string>().Task);
            Assert.False(dialog.CreateButton.IsEnabled);
            Assert.Equal(Visibility.Collapsed, dialog.ErrorText.Visibility);
            dialog.Close();
        });
    }

    [Fact]
    public void ASuggestionIsTheNameNotAnAddition()
    {
        Run(() =>
        {
            var dialog = NewDialog();
            dialog.RoomTitleBox.Text = "Vengan noobs";
            dialog.Suggest2.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal(Strings.Get("MpCreateDialogSuggestNight"), dialog.RoomTitleBox.Text);
            dialog.Close();
        });
    }

    /// <summary>A competitive room grows to fit its observers; a casual one gives up seats.</summary>
    [Fact]
    public void TheSummaryCountsThePlayersOnBothSidesOfTheChoice()
    {
        Run(() =>
        {
            var dialog = NewDialog(observers: true);
            dialog.SelectSpectatorSlots(1);
            Assert.Equal("3", dialog.SummaryPlayers.Text);

            dialog.SelectKind(RoomFormat.TwoVTwo);
            Assert.Equal("4", dialog.SummaryPlayers.Text);
            Assert.Equal(Visibility.Collapsed, dialog.SummaryObserversBlock.Visibility);

            dialog.SelectSpectatorSlots(2);
            Assert.Equal("4", dialog.SummaryPlayers.Text);
            Assert.Equal(Visibility.Visible, dialog.SummaryObserversBlock.Visibility);
            Assert.Equal("2", dialog.SummaryObservers.Text);
            dialog.Close();
        });
    }

    [Fact]
    public void ObserversStayShutWithoutDeveloperMode()
    {
        Run(() =>
        {
            var dialog = NewDialog();
            Assert.Equal(Visibility.Collapsed, dialog.ObserversBlock.Visibility);
            dialog.Close();
        });
    }

    [Fact]
    public void TheShortHashKeepsBothEnds()
        => Assert.Equal("0123\u2026cdef", CreateLobbyDialog.ShortHash("0123456789abcdef"));
}
