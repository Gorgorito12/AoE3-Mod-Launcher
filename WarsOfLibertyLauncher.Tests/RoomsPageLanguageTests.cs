using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Documents;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Changing the language repaints the ROOMS page, like the other three subtabs.
///
/// <para><b>The defect.</b> <c>ApplyStrings</c> assigns the fixed labels and then calls
/// <c>RefreshActiveSubtabStrings</c>, which exists for exactly this reason and says so in its
/// own comment — the code-built pages are not reached by those assignments. Its switch covered
/// Tournaments, Ranking and Stats. Rooms was missing, and the community-activity strip lives
/// there: its sentences went on saying "Hay más gente entre las 18:00 y 21:00" under an English
/// heading until the next poll happened to land, up to a minute later and only while the window
/// was in the foreground. The room ROWS were worse — the quiet refresh skips repainting when
/// the room list has not changed, so their chips would have stayed in the old language for as
/// long as nobody opened or closed a room.</para>
/// </summary>
[Collection("wpf-and-language")]
public class RoomsPageLanguageTests
{
    /// <summary>
    /// THE ONE THAT MATTERS. The strip's own sentence follows the switch, at once.
    ///
    /// <para>Driven through the real <c>ApplyStrings</c>, because that is the path a language
    /// change actually takes; a test that called the renderer directly would pass over the
    /// missing case that caused this.</para>
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_ChangingTheLanguageReachesTheRoomsPage()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            try
            {
                Strings.SetLanguage("es");
                var tab = new MultiplayerTab();
                Seed(tab);

                Apply(tab);
                var spanish = PeakLine(tab);

                // The card has to be ON, or everything below passes over an empty panel.
                Assert.False(string.IsNullOrWhiteSpace(spanish),
                    "the peak-hours card drew nothing, so this proves nothing about language.");
                Assert.Contains("gente", spanish, StringComparison.Ordinal);

                Strings.SetLanguage("en");
                Apply(tab);
                var english = PeakLine(tab);

                Assert.NotEqual(spanish, english);
                Assert.Contains("More people", english, StringComparison.Ordinal);

                // And back, because a one-way repaint is half a fix.
                Strings.SetLanguage("es");
                Apply(tab);
                Assert.Equal(spanish, PeakLine(tab));
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// A language change costs no request.
    ///
    /// <para><c>RefreshActiveSubtabStrings</c>'s own summary promises this — "none of these
    /// fetches anything, so switching language cannot cost a request" — and nothing checked
    /// it. The strip is the one that could break the promise, because its painting used to
    /// live INSIDE the fetch, behind the await. The tab here has no API at all, so a repaint
    /// that asks for anything throws rather than quietly costing a round trip.</para>
    /// </summary>
    [Fact]
    public void ALanguageChangeCostsNoRequest()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            try
            {
                Strings.SetLanguage("es");
                var tab = new MultiplayerTab();
                Seed(tab);
                Strings.SetLanguage("en");
                Apply(tab);           // no session, no Api: any fetch would throw
                Assert.Contains("More people", PeakLine(tab), StringComparison.Ordinal);
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// EVERY subtab is covered, which is the guard that stops the fifth from repeating this.
    ///
    /// <para>The switch has no <c>default</c>, so a missing case is silent — a page that keeps
    /// the old language and nothing anywhere saying it should not. Read off the source because
    /// the question is about the switch itself, not about one page's behaviour.</para>
    /// </summary>
    [Fact]
    public void EverySubtabIsCoveredWhenTheLanguageChanges()
    {
        var source = File.ReadAllText(RepoFile("Controls/MultiplayerTab.xaml.cs"));
        var start = source.IndexOf(
            "private void RefreshActiveSubtabStrings()", StringComparison.Ordinal);
        Assert.True(start >= 0, "RefreshActiveSubtabStrings is gone; this guard moves with it.");

        var end = source.IndexOf("\n    private ", start + 1, StringComparison.Ordinal);
        var body = source[start..end];

        foreach (var name in Enum.GetNames(typeof(MultiplayerTab.Subtab)))
            Assert.True(
                Regex.IsMatch(body, @"case\s+Subtab\." + name + @"\s*:"),
                $"{name} has no case, so a language change leaves that page in the old "
                + "language. There is no default here, so nothing else will say so.");
    }

    /// <summary>Community stats, straight into the field the strip reads.</summary>
    private static void Seed(MultiplayerTab tab) =>
        typeof(MultiplayerTab)
            .GetField("_communityStats", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(tab, StatsDemoData.Community());

    private static void Apply(MultiplayerTab tab) =>
        typeof(MultiplayerTab)
            .GetMethod("ApplyStrings", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(tab, null);

    /// <summary>The sentence under the bars. It is built from Runs, so it is read from them.</summary>
    private static string PeakLine(MultiplayerTab tab)
    {
        // A field, not a property: x:Name generates fields on the partial class.
        var block = (TextBlock)typeof(MultiplayerTab)
            .GetField("ActivityPeakLine", BindingFlags.Instance | BindingFlags.NonPublic
                                          | BindingFlags.Public)!
            .GetValue(tab)!;
        return string.Concat(block.Inlines.OfType<Run>().Select(r => r.Text));
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
        throw new DirectoryNotFoundException(
            "Could not find the WarsOfLibertyLauncher project above " + AppContext.BaseDirectory);
    }
}
