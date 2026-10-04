using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// An invite to a player in a match is not sent; whoever invites is warned instead (asked for by
/// a player: "warning whoever invites that they are playing would be enough"). The target's
/// launcher shows no card while their game runs, so the invite was simply lost — and it spent
/// their 60-s per-sender cooldown, so the invite after the match was dropped too.
/// </summary>
[Collection("wpf-and-language")]
public class InviteTargetTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData("in_game")]
    [InlineData("in_room")]
    [InlineData("idle")]
    [InlineData(null)]
    public void InNoRoomThereIsNothingToInviteTo(string? status)
        => Assert.Equal(InviteAction.Disabled, InviteTarget.Decide(inRoom: false, status));

    /// <summary>THE ONE THAT MATTERS: a player in a match is warned about, never sent the invite.</summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_APlayerInAMatchIsWarnedAbout()
        => Assert.Equal(InviteAction.WarnPlaying, InviteTarget.Decide(inRoom: true, "in_game"));

    /// <summary>Everybody else is sent the invite — including a status nobody knows, so an older backend changes nothing.</summary>
    [Theory]
    [InlineData("in_room")]
    [InlineData("idle")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("IN_GAME")]
    [InlineData("something_new")]
    public void EverybodyElseIsSentTheInvite(string? status)
        => Assert.Equal(InviteAction.Send, InviteTarget.Decide(inRoom: true, status));

    /// <summary>
    /// The real chip: a player in a match says so on hover, and a click raises ONE warning toast
    /// naming them and sends nothing (there is no socket here, so a send would be a silent no-op —
    /// the toast is what proves the warning branch ran).
    /// </summary>
    [Fact]
    public void ThePlayingChipWarnsOnHoverAndOnClickAndSendsNothing()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            try
            {
                Strings.SetLanguage("es");
                var tab = new MultiplayerTab();
                var toasts = new List<AppToast.ToastOptions>();
                typeof(MultiplayerTab).GetField("_showAppToast", Private)!
                    .SetValue(tab, new Action<AppToast.ToastOptions>(toasts.Add));

                var playing = tab.BuildInviteIconButton("u1", "Kaiser", InviteAction.WarnPlaying);
                Assert.Equal(Strings.Format("MpInviteTooltipInGame", "Kaiser"), playing.ToolTip);
                Assert.Contains("Kaiser", (string)playing.ToolTip);
                Assert.Equal(Cursors.Hand, playing.Cursor);

                playing.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = UIElement.MouseLeftButtonUpEvent,
                });
                var toast = Assert.Single(toasts);
                Assert.Equal(Strings.Format("MpInviteTargetInGame", "Kaiser"), toast.Title);
                Assert.Empty(toast.Actions);

                // Somebody not in a match keeps the ordinary invite.
                var idle = tab.BuildInviteIconButton("u2", "Aluclown", InviteAction.Send);
                Assert.Equal(Strings.Get("MpInviteTooltip"), idle.ToolTip);
                idle.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = UIElement.MouseLeftButtonUpEvent,
                });
                Assert.Single(toasts);   // no warning for them

                // In no room the chip is dimmed and says why.
                var disabled = tab.BuildInviteIconButton("u3", "El Taita", InviteAction.Disabled);
                Assert.Equal(Strings.Get("MpInviteTooltipDisabled"), disabled.ToolTip);
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    [Theory]
    [InlineData("MpInviteTooltipInGame")]
    [InlineData("MpInviteTargetInGame")]
    public void TheWarningExistsInBothLanguages(string key)
    {
        var previous = Strings.Language;
        try
        {
            foreach (var lang in new[] { "en", "es" })
            {
                Strings.SetLanguage(lang);
                var text = Strings.Format(key, "Kaiser");
                Assert.NotEqual(key, text);
                Assert.Contains("Kaiser", text);
            }
        }
        finally { Strings.SetLanguage(previous); }
    }
}
