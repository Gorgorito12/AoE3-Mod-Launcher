using System.Linq;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The release-notes linkifier.
///
/// <para>It exists because the maintainer's release bodies are a single bare URL pointing at the
/// real changelog, and the "What's new" panel rendered it as dead text. <b>The refusals are what
/// these tests are for</b>: every release note written before this has no URL in it at all and
/// has to come out unchanged, and a scheme the launcher will not open must not be dressed up as
/// something clickable.</para>
/// </summary>
public class LinkedTextTests
{
    /// <summary>The real shape of every recent release: the body IS the address.</summary>
    [Fact]
    public void ABodyThatIsNothingButAUrlIsOneLink()
    {
        const string url = "https://github.com/Gorgorito12/AoE3-Mod-Launcher/blob/main/releases/v1.0.13.md";
        var segments = LinkedText.Split(url);

        var only = Assert.Single(segments);
        Assert.True(only.IsLink);
        Assert.Equal(url, only.Text);
    }

    /// <summary>
    /// <b>The no-op case, and the important one.</b> Ordinary notes must render exactly as they
    /// did before any of this existed — one prose segment, byte for byte.
    /// </summary>
    [Fact]
    public void ProseWithNoUrlComesOutWhole()
    {
        const string body = "Arreglado el chat de la sala.\n\n- Ya no se pierde el resultado.";
        var segments = LinkedText.Split(body);

        var only = Assert.Single(segments);
        Assert.False(only.IsLink);
        Assert.Equal(body, only.Text);
    }

    [Fact]
    public void TextAroundALinkIsKept()
    {
        var segments = LinkedText.Split("Notas completas en https://example.com/notes y nada más.");

        Assert.Equal(3, segments.Count);
        Assert.Equal("Notas completas en ", segments[0].Text);
        Assert.True(segments[1].IsLink);
        Assert.Equal("https://example.com/notes", segments[1].Text);
        Assert.Equal(" y nada más.", segments[2].Text);
    }

    /// <summary>
    /// A full stop ends the sentence, not the address — and clicking a link with a stray dot on
    /// the end lands on a page that does not exist.
    /// </summary>
    [Theory]
    [InlineData("Mira https://example.com/a.", "https://example.com/a")]
    [InlineData("Mira https://example.com/a,", "https://example.com/a")]
    [InlineData("¿Mira https://example.com/a?", "https://example.com/a")]
    [InlineData("(mira https://example.com/a)", "https://example.com/a")]
    public void TrailingPunctuationStaysOutsideTheLink(string body, string expected)
    {
        var link = Assert.Single(LinkedText.Split(body).Where(s => s.IsLink));
        Assert.Equal(expected, link.Text);
    }

    /// <summary>A bracket the address itself opened has to survive.</summary>
    [Fact]
    public void ABalancedBracketInsideTheUrlIsNotTrimmed()
    {
        var link = Assert.Single(
            LinkedText.Split("https://es.wikipedia.org/wiki/Age_of_Empires_(serie)").Where(s => s.IsLink));

        Assert.Equal("https://es.wikipedia.org/wiki/Age_of_Empires_(serie)", link.Text);
    }

    /// <summary>
    /// Anything SafeUrl would refuse stays prose. Offering a link that does nothing when clicked
    /// is worse than offering none — the same refusal SafeUrl itself makes at open time.
    /// </summary>
    [Theory]
    [InlineData("Descarga en ftp://example.com/file.zip ahora")]
    [InlineData("Ruta local file:///C:/temp/notes.md")]
    public void AnUnopenableSchemeIsNotALink(string body)
    {
        Assert.DoesNotContain(LinkedText.Split(body), s => s.IsLink);
    }

    /// <summary>The display trick SafeUrl exists to refuse: a real host in the userinfo.</summary>
    [Fact]
    public void CredentialsInTheAuthorityAreNotLinkified()
    {
        Assert.DoesNotContain(
            LinkedText.Split("https://github.com@evil.example/notes"), s => s.IsLink);
    }

    [Fact]
    public void TwoLinksBothSurvive()
    {
        var links = LinkedText.Split("https://a.example/1 y https://b.example/2")
            .Where(s => s.IsLink).Select(s => s.Text).ToList();

        Assert.Equal(new[] { "https://a.example/1", "https://b.example/2" }, links);
    }

    [Fact]
    public void EmptyInputYieldsNothing()
    {
        Assert.Empty(LinkedText.Split(null));
        Assert.Empty(LinkedText.Split(""));
    }
}
