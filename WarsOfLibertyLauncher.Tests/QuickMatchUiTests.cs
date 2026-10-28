using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The quick match's three surfaces — the button over the rooms list, the pill in the title bar
/// and "Find another" on the result card. The interface is PROVISIONAL (no design handoff yet), so
/// these pin what must survive any redesign: that each one is reachable, says something in both
/// languages, and appears only where it means something.
/// </summary>
[Collection("wpf-and-language")]
public class QuickMatchUiTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>
    /// THE ONE THAT MATTERS. The pill lives in the CAPTION region of the title bar, where a click
    /// drags the window unless the control says otherwise — and the flag does not reach a
    /// control's content, so each of the two buttons must carry it itself. Without it the pill is
    /// on screen, looks clickable, and every click moves the window instead: no error, no log.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_BothHalvesOfThePillAreClickableInTheCaption()
    {
        var doc = XDocument.Load(RepoFile("MainWindow.xaml"));
        var pill = Named(doc, "QuickMatchPill");

        Assert.Equal("Collapsed", (string?)pill.Attribute("Visibility"));
        foreach (var name in new[] { "QuickMatchPillButton", "QuickMatchPillCancel" })
        {
            var button = Named(doc, name);
            Assert.True(pill.Descendants().Contains(button), $"{name} has left the pill.");
            var flag = button.Attributes().SingleOrDefault(a => a.Name.LocalName == "WindowChrome.IsHitTestVisibleInChrome");
            Assert.True(flag != null && (string)flag == "True", $"{name} does not opt out of the caption's drag.");
        }

        // Its own column: sharing one with the update pill or the offline chip would paint the
        // two on top of each other the day both show.
        var column = (string?)pill.Attribute("Grid.Column");
        var siblings = pill.Parent!.Elements().Where(e => e != pill)
            .Select(e => (string?)e.Attribute("Grid.Column")).Where(c => c != null);
        Assert.DoesNotContain(column, siblings);
    }

    /// <summary>
    /// The button over the rooms list is built empty in the XAML and captioned from code, so a
    /// missing key would show the key itself — visible, but easy to miss in the language you do
    /// not read. Both languages, and the tooltip says what it does.
    /// </summary>
    [Fact]
    public void TheQuickMatchButtonSaysWhatItDoesInBothLanguages()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            try
            {
                foreach (var lang in new[] { "es", "en" })
                {
                    Strings.SetLanguage(lang);
                    var tab = new MultiplayerTab();

                    var caption = tab.QuickMatchButton.Content as string;
                    Assert.False(string.IsNullOrWhiteSpace(caption), lang);
                    Assert.Equal(Strings.Get("QmFindMatch"), caption);
                    Assert.NotEqual("QmFindMatch", caption);
                    Assert.NotNull(tab.QuickMatchButton.ToolTip);
                    Assert.True(tab.QuickMatchButton.IsEnabled, lang);
                }
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// A room that filled, started or closed between the list and the join is a LOST RACE: the
    /// search tries the next room in silence. Everything else the server says is about the player
    /// (another version, a password, a room already joined, an old launcher) and still reaches him.
    /// A refusal misread as a race would be swallowed; a race misread as a refusal ends the search
    /// with a dialog about a room that simply got taken.
    /// </summary>
    [Theory]
    [InlineData(409, "lobby_full", true)]
    [InlineData(409, "conflict", true)]
    [InlineData(404, "not_found", true)]
    [InlineData(409, "mod_mismatch", false)]
    [InlineData(403, "forbidden", false)]
    [InlineData(409, "already_in_lobby", false)]
    [InlineData(426, "launcher_too_old", false)]
    [InlineData(401, "unauthorized", false)]
    public void OnlyARoomThatChangedUnderneathIsALostRace(int status, string code, bool race)
    {
        Assert.Equal(race, MultiplayerTab.IsRaceLoss(new LobbyApiException(status, code, "", null)));
    }

    /// <summary>"Find another" follows the one kind of match the quick match looks for: a ranked 1v1.</summary>
    [Theory]
    [InlineData(RoomFormat.OneVOne, true)]
    [InlineData(RoomFormat.TwoVTwo, false)]
    [InlineData(RoomFormat.ThreeVThree, false)]
    [InlineData(RoomFormat.Casual, false)]
    [InlineData(RoomFormat.Unknown, false)]
    public void FindAnotherIsOfferedOnlyAfterARankedOneVOne(RoomFormat format, bool offered)
    {
        Assert.Equal(offered, MultiplayerTab.OffersFindAnother(Outcome(RoomFormats.LabelKey(format))));
    }

    /// <summary>
    /// The card draws the button only when it is handed the action — a card for a team match, or
    /// from an older code path, keeps its old footer — and the button is captioned in both languages.
    /// </summary>
    [Fact]
    public void TheResultCardCarriesFindAnotherOnlyWhenOffered()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            try
            {
                foreach (var lang in new[] { "es", "en" })
                {
                    Strings.SetLanguage(lang);
                    var model = Outcome("MpFormat1v1");

                    var with = MatchResultCard.Build(model,
                        new MatchResultCard.Actions(OnRematch: null, OnDismiss: () => { }, OnFindAnother: () => { }));
                    var button = Descendants(with).OfType<Button>().Distinct()
                        .SingleOrDefault(b => Equals(b.Tag, MatchResultCard.FindAnotherTag));
                    Assert.NotNull(button);
                    Assert.Equal(Strings.Get("QmFindAnother"), button!.Content as string);
                    Assert.NotNull(button.ToolTip);

                    var without = MatchResultCard.Build(model,
                        new MatchResultCard.Actions(OnRematch: null, OnDismiss: () => { }));
                    Assert.DoesNotContain(Descendants(without).OfType<Button>(),
                        b => Equals(b.Tag, MatchResultCard.FindAnotherTag));
                }
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// Every quick-match text that takes a value formats with exactly what the code hands it, in
    /// both languages. A placeholder one language has and the other does not throws at runtime —
    /// on the timer that ticks every second while somebody is searching.
    /// </summary>
    [Fact]
    public void EveryQuickMatchTextFormatsWithWhatTheCodeHandsIt()
    {
        var previous = Strings.Language;
        try
        {
            foreach (var lang in new[] { "es", "en" })
            {
                Strings.SetLanguage(lang);
                Assert.Contains("02:13", Strings.Format("QmSearching", "02:13"));
                Assert.Contains("02:13", Strings.Format("QmPill", "02:13"));
                Assert.Contains("WoL", Strings.Format("QmPillTip", "WoL"));
                Assert.Contains("Pedro", Strings.Format("QmFoundBody", "Pedro"));
                Assert.Contains("3", Strings.Format("QmOtherVersion", 3));
                Assert.Contains("lobby_full", Strings.Format("QmFailed", "lobby_full"));
            }
        }
        finally { Strings.SetLanguage(previous); }
    }

    private static MatchOutcomeView Outcome(string? formatLabelKey)
        => new(MatchVerdict.Win, "wol", "Texas", 1440, 2,
            RatingBefore: 1524, RatingAfter: 1542,
            RivalLogin: "someone", RivalRating: 1592,
            Wins: 4, Losses: 1, Rd: 60)
        { FormatLabelKey = formatLabelKey };

    private static XElement Named(XDocument doc, string name)
        => doc.Descendants().Single(e => (string?)e.Attribute(X + "Name") == name);

    private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var stack = new System.Collections.Generic.Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
                stack.Push(child);
            if (node is Visual or System.Windows.Media.Media3D.Visual3D)
            {
                var n = VisualTreeHelper.GetChildrenCount(node);
                for (var i = 0; i < n; i++) stack.Push(VisualTreeHelper.GetChild(node, i));
            }
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
        throw new DirectoryNotFoundException("WarsOfLibertyLauncher/App.xaml not found above the test output.");
    }
}
