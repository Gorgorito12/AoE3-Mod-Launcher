using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Card name, description and icon, read out of a mod's own tech files and string table.
///
/// <para><b>The adjacency case is the one that matters.</b>
/// <c>XmlReader.ReadElementContentAsString</c> has already advanced by the time it returns, so a
/// plain <c>while (reader.Read())</c> loop steps over whatever follows the field it just read.
/// With one field wanted that cost a node nobody needed; with three it costs the NEXT FIELD, and
/// a card whose title and description sit next to each other loses its description while every
/// other card looks perfect. It reads as a patchy string table rather than as a parser bug —
/// the same trap <c>ModStringTable</c> was already carrying a warning about.</para>
/// </summary>
public class CardNameResolverTests : IDisposable
{
    private readonly List<string> _temp = new();

    public CardNameResolverTests() => CardNameResolver.ResetCache();

    public void Dispose()
    {
        CardNameResolver.ResetCache();
        foreach (var dir in _temp)
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// A mod install with just the two files this reads. The colour span is written escaped,
    /// exactly as the shipped table stores it, so the value that reaches the cleaner is the
    /// literal tag rather than XML the parser would have swallowed.
    /// </summary>
    private string NewInstall()
    {
        var dir = Directory.CreateTempSubdirectory("wol-card-test-").FullName;
        _temp.Add(dir);

        var data = Path.Combine(dir, "data");
        Directory.CreateDirectory(data);

        File.WriteAllText(Path.Combine(data, "techtreey.xml"), """
            <techtree>
              <Tech name='Adjacent' type='Normal'>
                <DBID>1</DBID>
                <DisplayNameID>10</DisplayNameID>
                <RolloverTextID>11</RolloverTextID>
                <Icon>ui\techs\adjacent</Icon>
                <Flag>HomeCity</Flag>
              </Tech>
              <Tech name='Spaced' type='Normal'>
                <DBID>2</DBID>
                <DisplayNameID>20</DisplayNameID>
                <Cost resourcetype='Ships'>1.0000</Cost>
                <Status>UNOBTAINABLE</Status>
                <Icon>ui\techs\spaced</Icon>
                <RolloverTextID>21</RolloverTextID>
                <Flag>HomeCity</Flag>
              </Tech>
              <Tech name='TitleOnly' type='Normal'>
                <DBID>3</DBID>
                <DisplayNameID>30</DisplayNameID>
                <Icon>ui\techs\titleonly</Icon>
                <Flag>HomeCity</Flag>
              </Tech>
              <Tech name='Nameless' type='Normal'>
                <DBID>4</DBID>
                <Icon>ui\techs\nameless</Icon>
                <Flag>HomeCity</Flag>
              </Tech>
            </techtree>
            """);

        File.WriteAllText(Path.Combine(data, "stringtabley.xml"), """
            <language>
              <String _locID='10'>Adjacent Card</String>
              <String _locID='11'>Does a thing with &lt;color=0.1, 0.2, 0.3&gt;colour&lt;/color&gt;.</String>
              <String _locID='20'>Spaced Card</String>
              <String _locID='21'>Ships 3 units.</String>
              <String _locID='30'>Title Only</String>
            </language>
            """);

        return dir;
    }

    private IReadOnlyDictionary<string, CardDetail> Details(params string[] cards) =>
        CardNameResolver.ResolveDetails(NewInstall(), "age3y.exe", cards);

    // ------------------------------------------------------------------

    [Fact]
    public void ACardWhoseTitleAndDescriptionAreAdjacentKeepsBoth()
    {
        var detail = Details("Adjacent")["Adjacent"];

        Assert.Equal("Adjacent Card", detail.Name);
        Assert.Equal("Does a thing with colour.", detail.Description);
        Assert.Equal(@"ui\techs\adjacent", detail.IconPath);
    }

    [Fact]
    public void ACardWithOtherFieldsBetweenThemKeepsBothToo()
    {
        var detail = Details("Spaced")["Spaced"];

        Assert.Equal("Spaced Card", detail.Name);
        Assert.Equal("Ships 3 units.", detail.Description);
        Assert.Equal(@"ui\techs\spaced", detail.IconPath);
    }

    /// <summary>
    /// A third of a real deck. Every one of those is a unit shipment whose title already says
    /// what it does ("8 Tigermen"), so a null here is an absence to draw as nothing, never a
    /// hole to fill with a placeholder.
    /// </summary>
    [Fact]
    public void ACardWithNoRolloverHasNoDescriptionRatherThanAnEmptyOne()
    {
        var detail = Details("TitleOnly")["TitleOnly"];

        Assert.Equal("Title Only", detail.Name);
        Assert.Null(detail.Description);
        Assert.Equal(@"ui\techs\titleonly", detail.IconPath);
    }

    /// <summary>
    /// The icon is still worth having when the mod does not name the card: the caller falls back
    /// to the internal name, which identifies it, and the picture is unaffected.
    /// </summary>
    [Fact]
    public void ACardTheModDoesNotNameStillReportsItsIcon()
    {
        var detail = Details("Nameless")["Nameless"];

        Assert.Null(detail.Name);
        Assert.Equal(@"ui\techs\nameless", detail.IconPath);
    }

    [Fact]
    public void ACardThatIsNotInTheTechFilesIsSimplyAbsent() =>
        Assert.False(Details("NoSuchCard").ContainsKey("NoSuchCard"));

    /// <summary>
    /// The older, name-only view must keep behaving exactly as it did: a card the mod does not
    /// name is left out entirely, because its callers print the internal name for anything
    /// missing.
    /// </summary>
    [Fact]
    public void TheNameOnlyViewStillOmitsWhatItAlwaysOmitted()
    {
        var names = CardNameResolver.Resolve(
            NewInstall(), "age3y.exe", new[] { "Adjacent", "Spaced", "TitleOnly", "Nameless" });

        Assert.Equal(3, names.Count);
        Assert.Equal("Adjacent Card", names["Adjacent"]);
        Assert.False(names.ContainsKey("Nameless"));
    }

    [Fact]
    public void NoInstallPathResolvesNothingRatherThanThrowing()
    {
        Assert.Empty(CardNameResolver.ResolveDetails(null, "age3y.exe", new[] { "Adjacent" }));
        Assert.Empty(CardNameResolver.ResolveDetails("   ", "age3y.exe", new[] { "Adjacent" }));
    }

    [Fact]
    public void BlankCardNamesAreIgnored() =>
        Assert.Empty(CardNameResolver.ResolveDetails(NewInstall(), "age3y.exe", new[] { "", "  " }));

    /// <summary>The mod's own layer is read, on top of the base ones — Napoleonic Era's <c>n</c>.</summary>
    [Fact]
    public void TheExecutableDecidesWhichExtraLayerIsRead()
    {
        Assert.Contains("techtreen.xml", CardNameResolver.TechFilesFor("age3n.exe"));
        Assert.DoesNotContain("techtreen.xml", CardNameResolver.TechFilesFor("age3y.exe"));
        Assert.Equal(3, CardNameResolver.TechFilesFor("age3.exe").Count);
    }
}
