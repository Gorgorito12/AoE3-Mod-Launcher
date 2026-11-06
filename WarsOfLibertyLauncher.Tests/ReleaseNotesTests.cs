using System.Linq;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>Design 77a/77b. The note a release ships is Markdown in two languages, and the update
/// dialog shows one language, with its format and without the download quote.</summary>
public class ReleaseNotesTests
{
    private static string AllText(ReleaseNotes.View view) => string.Join("\n",
        view.Sections.SelectMany(s => new[] { s.Heading }.Concat(
            s.Items.Select(i => string.Concat(i.Pieces.Select(p => p.Text))))));

    [Fact]
    public void ThePlayerGetsOneLanguage()
    {
        var es = ReleaseNotes.Parse(LauncherUpdateDemo.Notes, spanish: true);
        Assert.Equal(new[] { "Torneos por equipos", "Instalar mods", "Arreglos" }, es.Sections.Select(s => s.Heading));
        Assert.DoesNotContain("Team tournaments", AllText(es));

        var en = ReleaseNotes.Parse(LauncherUpdateDemo.Notes, spanish: false);
        Assert.Equal(new[] { "Team tournaments", "Installing mods", "Fixes" }, en.Sections.Select(s => s.Heading));
        Assert.DoesNotContain("Torneos", AllText(en));
    }

    [Fact]
    public void TheQuoteWithTheDownloadLinkIsGone()
    {
        foreach (var spanish in new[] { true, false })
        {
            var text = AllText(ReleaseNotes.Parse(LauncherUpdateDemo.Notes, spanish));
            Assert.DoesNotContain("Aoe3ModLauncher.exe", text);
            Assert.DoesNotContain("http", text);
            Assert.DoesNotContain("**", text);
        }
    }

    [Fact]
    public void TheBoldLeadIsItsOwnPieceAndWrappedLinesJoin()
    {
        var first = ReleaseNotes.Parse(LauncherUpdateDemo.Notes, spanish: true).Sections[0].Items[0];
        Assert.True(first.Bullet);
        Assert.Equal("Ya se pueden jugar torneos 2v2 y 3v3.", first.Pieces[0].Text);
        Assert.True(first.Pieces[0].Bold);
        Assert.Equal(" Hasta ahora se podían crear, pero nadie podía inscribirse.", first.Pieces[1].Text);
        Assert.False(first.Pieces[1].Bold);
    }

    [Fact]
    public void ANoteThatIsOnlyAnAddressIsAButtonNotText()
    {
        var view = ReleaseNotes.Parse("  " + LauncherUpdateDemo.Link + "\r\n", spanish: true);
        Assert.Equal(LauncherUpdateDemo.Link, view.OnlyLink);
        Assert.Empty(view.Sections);
    }

    [Fact]
    public void AnAddressInsideASentenceIsNotALinkOnlyNote()
    {
        var view = ReleaseNotes.Parse("See " + LauncherUpdateDemo.Link + " for more.", spanish: false);
        Assert.Null(view.OnlyLink);
        Assert.Single(view.Sections);
    }

    [Fact]
    public void AnAddressThatMayNotBeOpenedIsText()
    {
        var view = ReleaseNotes.Parse("file:///C:/Windows/system32/calc.exe", spanish: false);
        Assert.Null(view.OnlyLink);
    }

    [Fact]
    public void ASpanishPlayerStillReadsANoteWithNoSpanishTitle()
    {
        var view = ReleaseNotes.Parse("## Fixes\n- One fix\n\n---\n\nFooter", spanish: true);
        Assert.Equal("Fixes", view.Sections.Single().Heading);
        Assert.DoesNotContain("Footer", AllText(view));
    }

    [Fact]
    public void AnEmptyNoteIsEmpty()
    {
        Assert.True(ReleaseNotes.Parse(null, true).IsEmpty);
        Assert.True(ReleaseNotes.Parse("   \n ", false).IsEmpty);
    }

    [Fact]
    public void ANestedBulletKeepsItsLevelAndItalicsLoseTheirStars()
    {
        var items = ReleaseNotes.Parse("## A\n- top\n  - *inner:* text\n", false).Sections[0].Items;
        Assert.Equal(0, items[0].Level);
        Assert.Equal(1, items[1].Level);
        Assert.Equal("inner: text", items[1].Pieces.Single().Text);
    }

    [Fact]
    public void AStrayMarkIsNotShownAndDoesNotBoldTheRest()
    {
        var pieces = ReleaseNotes.Parse("- half **open", false).Sections[0].Items[0].Pieces;
        Assert.Equal("half open", string.Concat(pieces.Select(p => p.Text)));
        Assert.DoesNotContain(pieces, p => p.Bold);
    }
}
