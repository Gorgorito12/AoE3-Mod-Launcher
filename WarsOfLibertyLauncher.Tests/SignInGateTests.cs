using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using WarsOfLibertyLauncher.Controls;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Signed out, all four multiplayer subtabs say the same thing.
///
/// <para><b>The defect.</b> The sign-in panel was declared INSIDE <c>RoomsView</c>, so it was
/// the one subtab that could show it. The other three each improvised: Tournaments printed a
/// grey italic line in a corner of a page it had gone ahead and built, with no sign-in button
/// anywhere on it; Ranking said "there is no ranking to show yet", which is not true — with no
/// session <c>RefreshStatsForMod</c> returns on its first line, so nothing was ever asked and
/// the emptiness is the launcher's, not the server's; Stats drew a mod picker over empty
/// tables for the same reason.</para>
///
/// <para>Two tests, because there are two independent ways to have this bug: the panel can be
/// trapped in one view (where it started), or the table that decides visibility can gate the
/// wrong subtabs. Either one alone reproduces the screenshot.</para>
/// </summary>
public class SignInGateTests
{
    /// <summary>
    /// THE ONE THAT MATTERS. Signed out, every subtab asks the same question — and signing in,
    /// or opening a preview, gets out of the way of all four.
    /// </summary>
    [Theory]
    [InlineData(MultiplayerTab.Subtab.Rooms)]
    [InlineData(MultiplayerTab.Subtab.Tournaments)]
    [InlineData(MultiplayerTab.Subtab.Ranking)]
    [InlineData(MultiplayerTab.Subtab.Stats)]
    public void THE_ONE_THAT_MATTERS_SignedOutEverySubtabAsksTheSameQuestion(
        MultiplayerTab.Subtab subtab)
    {
        // The reported bug, once per subtab. Rooms passed this from the day it was written;
        // the other three are the whole point.
        Assert.True(
            MultiplayerTab.SubtabShowsSignInGate(subtab, signedIn: false, preview: false),
            $"{subtab} draws its own page to somebody with no session. Whatever it puts there "
            + "is either a corner note with no way to act on it or a claim about data nobody "
            + "asked the server for.");

        // And it gets out of the way the moment there is one. A gate that outstays a sign-in
        // is a launcher with four blank tabs.
        Assert.False(
            MultiplayerTab.SubtabShowsSignInGate(subtab, signedIn: true, preview: false),
            $"{subtab} is gated to a signed-in user.");
    }

    /// <summary>
    /// A preview is never gated, and that is ONE rule rather than one per subtab.
    ///
    /// <para><c>--demo-tournaments</c> and <c>--demo-stats</c> exist to draw these pages with
    /// no session at all, which is exactly the state the gate reacts to. The stats preview
    /// also fills <c>_communityStats</c>, which is what the RANKING page reads — so a rule
    /// that let the stats flag off the hook only on the Stats subtab would cover real,
    /// present data the moment somebody clicked Clasificación.</para>
    /// </summary>
    [Theory]
    [InlineData(MultiplayerTab.Subtab.Rooms)]
    [InlineData(MultiplayerTab.Subtab.Tournaments)]
    [InlineData(MultiplayerTab.Subtab.Ranking)]
    [InlineData(MultiplayerTab.Subtab.Stats)]
    public void APreviewIsNeverGated(MultiplayerTab.Subtab subtab)
    {
        Assert.False(
            MultiplayerTab.SubtabShowsSignInGate(subtab, signedIn: false, preview: true),
            $"the {subtab} preview is hidden behind a sign-in it was built not to need.");
    }

    /// <summary>
    /// THE ROOT CAUSE, and the half that logic alone cannot fix: the panel is not a child of
    /// any one subtab's view.
    ///
    /// <para>The decision table above can be perfectly right and the launcher still show a
    /// blank Tournaments page, because a <c>SignInPanel</c> declared inside <c>RoomsView</c>
    /// is collapsed along with it. Nesting is the bug; the table is only how it surfaced.</para>
    ///
    /// <para>Read as XML rather than loaded as XAML on purpose. <c>XamlReader.Load</c> cannot
    /// take this file — it carries <c>x:Class</c> and <c>Click="SignInButton_Click"</c>, which
    /// need the compiled code-behind — and the question here is about where an element is
    /// DECLARED, which is a fact about the document.</para>
    /// </summary>
    [Fact]
    public void THE_SIGN_IN_PANEL_IS_NOT_TRAPPED_INSIDE_ONE_SUBTAB()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var doc = XDocument.Load(RepoFile("Controls/MultiplayerTab.xaml"));

        var panel = doc.Descendants()
            .SingleOrDefault(e => (string?)e.Attribute(x + "Name") == "SignInPanel");
        Assert.True(panel != null, "SignInPanel is gone; this guard has to move with it.");

        var views = new[] { "RoomsView", "TournamentsView", "RankingView", "StatsView" };
        var trappedIn = panel!.Ancestors()
            .Select(a => (string?)a.Attribute(x + "Name"))
            .FirstOrDefault(name => name != null && views.Contains(name));

        Assert.True(trappedIn == null,
            $"SignInPanel is declared inside {trappedIn}, so it is the only subtab that can "
            + "ever show it — the other three collapse it along with their own view and are "
            + "left to improvise a sign-out state of their own. It belongs to the tab, not to "
            + "a page of it: a direct child of TabRootGrid, declared last so it paints over "
            + "whichever view is behind it.");

        // Declared last, which is what puts it on top: a Grid paints its children in order.
        var siblings = panel.Parent!.Elements()
            .Where(e => e.Attribute(x + "Name") != null || e.Name.LocalName != "Grid.RowDefinitions")
            .ToList();
        Assert.Same(panel, siblings.Last());
    }

    /// <summary>
    /// ONE writer, and it is the table that already decides which page is on screen.
    ///
    /// <para>This is the guard that could see the original defect. The rule above is a pure
    /// function and is satisfied by whoever wrote it; the bug lived in the wiring, where
    /// <c>SignInPanel.Visibility</c> was assigned in three separate places inside the ROOMS
    /// render path and <c>ShowSubtabView</c> - the one method that runs for every subtab -
    /// was not one of them.</para>
    ///
    /// <para>Same argument <c>ShowSubtabView</c>'s own comment makes about the four views:
    /// the failure of a missed line is a page drawn under another one, so the assignments
    /// live in one table rather than one per caller.</para>
    /// </summary>
    [Fact]
    public void ShowSubtabViewIsTheOnlyWriterOfTheGate()
    {
        var source = File.ReadAllText(RepoFile("Controls/MultiplayerTab.xaml.cs"));

        var writes = 0;
        for (var i = source.IndexOf("SignInPanel.Visibility =", StringComparison.Ordinal); i >= 0;
             i = source.IndexOf("SignInPanel.Visibility =", i + 1, StringComparison.Ordinal))
        {
            writes++;
        }

        Assert.True(writes == 1,
            $"the sign-in gate has {writes} writers. It had three, all of them on the Rooms "
            + "render path, which is why Tournaments, Ranking and Stats never showed it. It "
            + "belongs in ShowSubtabView beside the four views it replaces.");

        var start = source.IndexOf("private void ShowSubtabView()", StringComparison.Ordinal);
        Assert.True(start >= 0, "ShowSubtabView is gone; this guard has to move with it.");
        var end = source.IndexOf("\n    private ", start + 1, StringComparison.Ordinal);

        Assert.Contains("SignInPanel.Visibility =",
            source.Substring(start, end - start), StringComparison.Ordinal);
    }

    /// <summary>Walks up to the launcher project, as TextScaleTests does.</summary>
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
