using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Splitting a map's pack off its name. Run: <c>dotnet test</c>.
///
/// <para>The bug this closes is that the STATS tab and the ranking summary printed
/// <c>ESOC_Fertile Crescent</c> - a file-derived identifier - straight into the UI, while
/// every other place in the launcher that shows a map name had already been fixed. The
/// launcher's rule is that an internal name never reaches a player.</para>
///
/// <para>What is worth pinning is the REFUSALS. There is no table of map names anywhere in
/// the repo and this invents none: it separates a prefix that is already in the string, and
/// the whole risk is taking a prefix that was never a pack tag. A map called
/// <c>Great_Plains</c> losing half its name would be worse than the identifier it
/// replaced.</para>
/// </summary>
public class MapLabelTests
{
    [Fact]
    public void APackPrefixBecomesALabelAndLeavesTheNameClean()
    {
        var (name, pack) = LocalMatchView.MapLabel("ESOC_Fertile Crescent");
        Assert.Equal("Fertile Crescent", name);
        Assert.Equal("ESOC", pack);
    }

    [Fact]
    public void AMapWithNoPrefixKeepsItsWholeNameAndGetsNoLabel()
    {
        var (name, pack) = LocalMatchView.MapLabel("Yucatan");
        Assert.Equal("Yucatan", name);
        Assert.Null(pack);
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_AnOrdinaryFirstWordIsNotAPack()
    {
        // This is the case that would silently rename a map: a two-word name joined by an
        // underscore, which is how the engine writes most of them. "Great" is short and is
        // all letters, so only its CASE separates it from a pack tag - which is why the rule
        // is uppercase and not length. It comes back whole, with the underscore made readable.
        var (name, pack) = LocalMatchView.MapLabel("Great_Plains");
        Assert.Equal("Great Plains", name);
        Assert.Null(pack);

        var (n2, p2) = LocalMatchView.MapLabel("Painted_Desert");
        Assert.Equal("Painted Desert", n2);
        Assert.Null(p2);
    }

    [Fact]
    public void ShortRealPackTagsAreStillTaken()
    {
        Assert.Equal("KOTH", LocalMatchView.MapLabel("KOTH_Andes").Pack);
        Assert.Equal("WOL", LocalMatchView.MapLabel("WOL_Pampas Secas").Pack);
    }

    [Fact]
    public void AMixedCasePrefixIsNotAPack()
    {
        // The rule stated from the other side: a tag is shouted, a word is not.
        Assert.Null(LocalMatchView.MapLabel("Esoc_Arizona").Pack);
        Assert.Null(LocalMatchView.MapLabel("eSOC_Arizona").Pack);
    }

    [Fact]
    public void ANonWordPrefixIsNotAPack()
    {
        // Punctuation in a prefix means this is not a tag, whatever else it is.
        var (name, pack) = LocalMatchView.MapLabel("my-map_Something");
        Assert.Equal("my-map Something", name);
        Assert.Null(pack);
    }

    [Fact]
    public void NothingInMeansNothingOut()
    {
        Assert.Equal(("", null), LocalMatchView.MapLabel(null));
        Assert.Equal(("", null), LocalMatchView.MapLabel("   "));
        // A leading or trailing underscore is not a split: there is no name on one side of
        // it, so nothing is taken as a pack and the name is just made readable.
        Assert.Null(LocalMatchView.MapLabel("_Solo").Pack);
        Assert.Equal("Solo", LocalMatchView.MapLabel("_Solo").Name);
        Assert.Null(LocalMatchView.MapLabel("Solo_").Pack);
    }
}
