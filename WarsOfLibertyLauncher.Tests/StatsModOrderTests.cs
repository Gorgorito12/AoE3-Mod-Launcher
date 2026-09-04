using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Controls;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The row of mod chips on the Statistics page, and the one property it has to have: it does
/// not move.
///
/// <para>Reported as "I pick Asian Dynasties, then Improvement Mod, and they just swap
/// places". The row was built by one loop that answered two questions at once — who is in it
/// and in what order — and the selected mod was offered THIRD, ahead of the server's list and
/// the installed walk. So every click hoisted a chip and shoved the rest sideways.</para>
///
/// <para>These tests are all of the same shape on purpose: build the row twice from the same
/// SET and assert the two rows are identical. That is the property; the individual orderings
/// are not what matters and are deliberately not asserted beyond Wars of Liberty leading.</para>
/// </summary>
public class StatsModOrderTests
{
    private const string Wol = "wol";
    private static readonly string[] Catalogue =
        { Wol, "aoe3-tad", "indonesia", "improvement", "napoleonic" };

    private static List<string> Order(string? selected, IEnumerable<string?>? fromServer = null)
    {
        var wanted = new List<string?> { Wol, selected };
        wanted.AddRange(fromServer ?? Catalogue.Cast<string?>());
        return MultiplayerTab.StatsModOrder(Wol, Catalogue.Cast<string?>(), wanted);
    }

    /// <summary>
    /// THE ONE THAT MATTERS. Clicking a chip may change which chip is filled and nothing else.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheSelectionIsNotPartOfTheOrder()
    {
        var baseline = Order(Wol);

        foreach (var selected in Catalogue)
        {
            Assert.Equal(baseline, Order(selected));
        }
    }

    /// <summary>
    /// The slower half of the same bug. <c>/stats/mods</c> answers most-played first, and it
    /// is re-fetched every 60 seconds — so a row that took its order from that payload
    /// rearranged itself while nobody was touching it, whenever the ranking changed.
    /// </summary>
    [Fact]
    public void TheServersRankingDoesNotReachTheRow()
    {
        var asPlayed = Order(Wol, new string?[] { "napoleonic", "improvement", "indonesia" });
        var reversed = Order(Wol, new string?[] { "indonesia", "improvement", "napoleonic" });

        Assert.Equal(asPlayed, reversed);
    }

    /// <summary>Wars of Liberty first, always: it is the mod this launcher is for.</summary>
    [Fact]
    public void WarsOfLibertyLeadsWhateverElseIsThere()
    {
        Assert.Equal(Wol, Order("napoleonic").First());
        Assert.Equal(Wol, Order(null).First());
    }

    /// <summary>
    /// The reason the selected mod is in the SET even though it is not in the order: a chosen
    /// mod with no chip leaves the row showing every option except the one whose figures are
    /// on screen. A mod the local catalogue has never heard of still gets a place.
    /// </summary>
    [Fact]
    public void AWantedModOutsideTheCatalogueStillGetsAChip()
    {
        var order = MultiplayerTab.StatsModOrder(
            Wol,
            Catalogue.Cast<string?>(),
            new string?[] { Wol, "indonesia", "brand-new-mod" });

        Assert.Contains("brand-new-mod", order);
        // At the end, and sorted rather than caller-ordered, so the same set draws the same
        // row no matter which pass contributed the stranger.
        Assert.Equal("brand-new-mod", order.Last());
        Assert.Equal(
            order,
            MultiplayerTab.StatsModOrder(
                Wol,
                Catalogue.Cast<string?>(),
                new string?[] { "brand-new-mod", "indonesia", Wol }));
    }

    /// <summary>
    /// Only what was asked for is drawn. The catalogue is the ORDER, never the membership —
    /// otherwise every mod in the catalogue would get a chip whether or not anybody had ever
    /// played it or installed it.
    /// </summary>
    [Fact]
    public void TheCatalogueOrdersTheRowWithoutFillingIt()
    {
        var order = MultiplayerTab.StatsModOrder(
            Wol, Catalogue.Cast<string?>(), new string?[] { Wol, "napoleonic" });

        Assert.Equal(new[] { Wol, "napoleonic" }, order);
    }

    /// <summary>
    /// Blanks and repeats arrive for real: an unselected page has no <c>_statsModId</c>, and
    /// the same mod is contributed by the default, the active profile, the server and the
    /// installed walk all at once.
    /// </summary>
    [Fact]
    public void NothingIsDrawnTwiceAndNothingBlankIsDrawnAtAll()
    {
        var order = MultiplayerTab.StatsModOrder(
            Wol,
            Catalogue.Cast<string?>(),
            new string?[] { Wol, null, "", "  ", "indonesia", "indonesia", "INDONESIA", Wol });

        Assert.Equal(new[] { Wol, "indonesia" }, order);
    }
}
