using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using WarsOfLibertyLauncher;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// A competitive room announces itself in its own title.
///
/// <para>The proposed title was <c>"Sala de {mod}"</c> whatever the room was, so a host who
/// ticked "Sala competitiva" got a name that said nothing about the one fact worth knowing
/// before joining — that the match is rated. The badge on the browser row does say it, but it
/// is a small chip on the second line, and it is the SERVER's claim; the title is the host's.
/// Both, deliberately.</para>
/// </summary>
[Collection("wpf-and-language")]
public class RoomTitleProposalTests
{
    private const int Cap = 64;   // RoomTitleBox.MaxLength

    /// <summary>
    /// THE ONE THAT MATTERS. The marker carries the format the host actually chose.
    ///
    /// <para>A Theory over all three, because pinning only 1v1 would let through exactly the
    /// thing that was asked for by name: a 2v2 or a 3v3 room whose title claims a 1v1, or
    /// says nothing at all. The format label is asked of <see cref="RoomFormats.LabelKey"/>,
    /// the same source the browser row's chip uses, so a room and its own row can never
    /// disagree about what to call it.</para>
    /// </summary>
    [Theory]
    [InlineData(RoomFormat.OneVOne, "1v1")]
    [InlineData(RoomFormat.TwoVTwo, "2v2")]
    [InlineData(RoomFormat.ThreeVThree, "3v3")]
    public void THE_ONE_THAT_MATTERS_ACompetitiveRoomSaysSoInItsOwnTitle(
        RoomFormat format, string label)
    {
        WithLanguage("es", () =>
        {
            var title = RoomTitleProposal.Propose("WoL", competitive: true, format, Cap);
            Assert.Equal("Sala de WoL · COMPETITIVA " + label, title);
        });

        // And in the other language, which is the whole reason this goes through the string
        // table instead of a literal: the marker is not a code word, it is a sentence fragment
        // in whatever language the launcher is running.
        WithLanguage("en", () =>
        {
            var title = RoomTitleProposal.Propose("WoL", competitive: true, format, Cap);
            Assert.Equal("WoL room · COMPETITIVE " + label, title);
        });
    }

    /// <summary>
    /// A competitive room whose size names no format says the badge and stops there.
    ///
    /// <para><see cref="RoomFormat.Unknown"/> is a real state, not a defensive branch — a
    /// competitive room made before formats existed, or by a client that skipped the dialog.
    /// Naming a format there would be inventing one.</para>
    /// </summary>
    [Fact]
    public void AnUndeclaredFormatIsNotInvented()
    {
        WithLanguage("es", () =>
            Assert.Equal("Sala de WoL · COMPETITIVA",
                RoomTitleProposal.Propose("WoL", competitive: true, RoomFormat.Unknown, Cap)));
    }

    /// <summary>Unticking takes it back off, leaving the title it started with.</summary>
    [Fact]
    public void UntickingTakesItBackOff()
    {
        WithLanguage("es", () =>
            Assert.Equal("Sala de WoL",
                RoomTitleProposal.Propose("WoL", competitive: false, RoomFormat.Casual, Cap)));
    }

    /// <summary>
    /// THE SILENT ONE. Every title this class can write, it can also recognise.
    ///
    /// <para>The dialog only replaces a title it believes is its own. So the failure mode is
    /// not a wrong title — it is a title that stops moving: propose the competitive variant
    /// once, fail to recognise it, and from then on every change looks like a hand-typed name
    /// and is left alone. Nothing on screen says anything is wrong. Hence the round trip, over
    /// the generated list rather than a restated one, so a new variant is covered the day it
    /// is added instead of the day somebody remembers this test.</para>
    /// </summary>
    [Fact]
    public void EveryTitleWeWriteIsOneWeRecogniseAgain()
    {
        WithLanguage("es", () =>
        {
            var mods = new[] { "WoL", "Struggle of Indonesia" };
            var all = RoomTitleProposal.AllProposals(mods, Cap).ToList();

            // Casual plus four competitive states, per mod. Stated so that deleting a variant
            // fails here rather than quietly shrinking what the round trip covers.
            Assert.Equal(10, all.Count);

            foreach (var title in all)
                Assert.True(RoomTitleProposal.IsOurs(title, mods, Cap),
                    $"the dialog wrote \"{title}\" and would then mistake it for a title the "
                    + "host typed, so it would never update that box again.");
        });
    }

    /// <summary>A name somebody typed is theirs, in either state of the tick box.</summary>
    [Fact]
    public void ATypedTitleIsNotOurs()
    {
        WithLanguage("es", () =>
        {
            var mods = new[] { "WoL" };
            Assert.False(RoomTitleProposal.IsOurs("Vengan noobs", mods, Cap));
            // Close, but not ours: the host edited what we proposed.
            Assert.False(RoomTitleProposal.IsOurs("Sala de WoL · COMPETITIVA 1v1 sin rush", mods, Cap));
            // An empty box belongs to nobody, so it is free to fill.
            Assert.True(RoomTitleProposal.IsOurs("", mods, Cap));
        });
    }

    /// <summary>
    /// The cap trims the room's name, never the marker.
    ///
    /// <para>Sixty-four characters is the field's own limit, and " · COMPETITIVA 3v3" is
    /// eighteen of them. A title cut off mid-"COMPETITI…" announces nothing, so what gives way
    /// is the part that still reads when it is shorter.</para>
    /// </summary>
    [Fact]
    public void TheMarkerSurvivesTheLengthCap()
    {
        WithLanguage("es", () =>
        {
            var huge = new string('M', 200);
            var title = RoomTitleProposal.Propose(huge, competitive: true, RoomFormat.ThreeVThree, Cap);

            Assert.True(title.Length <= Cap, $"{title.Length} characters in a {Cap} field.");
            Assert.EndsWith("· COMPETITIVA 3v3", title);
        });
    }

    /// <summary>
    /// END TO END, through the real dialog: ticking a format rewrites the box.
    ///
    /// <para>The core above can be perfectly right and the dialog never call it. 2v2 rather
    /// than 1v1 on purpose — it is the format the dialog does NOT fall back to, so a handler
    /// that fires but reads the wrong state fails here too.</para>
    /// </summary>
    [Fact]
    public void TheDialogPutsItInTheBoxWhenAFormatIsPicked()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            try
            {
                Strings.SetLanguage("es");
                var dlg = NewDialog(out _);

                Assert.Equal("Sala de WoL", dlg.RoomTitleBox.Text);

                var twoVTwo = dlg.FormatRow.Children.OfType<Button>().ElementAt(1);
                twoVTwo.RaiseEvent(new System.Windows.RoutedEventArgs(
                    System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

                Assert.Equal("Sala de WoL · COMPETITIVA 2v2", dlg.RoomTitleBox.Text);

                // And back off again, which is the half a one-way handler would fail.
                dlg.CompetitiveCheck.IsChecked = false;
                Assert.Equal("Sala de WoL", dlg.RoomTitleBox.Text);
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// And it leaves a host's own title alone — in both directions, which is the half that
    /// gets forgotten: unticking must not "restore" a name the host never asked for.
    /// </summary>
    [Fact]
    public void TheDialogNeverOverwritesAName()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            try
            {
                Strings.SetLanguage("es");
                var dlg = NewDialog(out _);
                dlg.RoomTitleBox.Text = "Vengan noobs";

                dlg.FormatRow.Children.OfType<Button>().ElementAt(1).RaiseEvent(
                    new System.Windows.RoutedEventArgs(
                        System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.Equal("Vengan noobs", dlg.RoomTitleBox.Text);

                dlg.CompetitiveCheck.IsChecked = false;
                Assert.Equal("Vengan noobs", dlg.RoomTitleBox.Text);
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    private static CreateLobbyDialog NewDialog(out ModProfile profile)
    {
        profile = new ModProfile { Id = "wol", DisplayName = "WoL" };
        var session = new MultiplayerSession(new LauncherConfig());
        return new CreateLobbyDialog(
            session,
            new List<ModProfile> { profile },
            profile,
            _ => Task.FromResult("0123456789abcdef"),
            _ => new ModCopyInfo(false, false, Array.Empty<ModCopyChoice>()),
            _ => Task.CompletedTask);
    }

    private static void WithLanguage(string lang, Action body)
    {
        var previous = Strings.Language;
        try { Strings.SetLanguage(lang); body(); }
        finally { Strings.SetLanguage(previous); }
    }
}
