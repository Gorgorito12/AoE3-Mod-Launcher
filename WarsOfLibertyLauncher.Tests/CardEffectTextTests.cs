using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The card descriptions with percentages — the ones the game builds rather than stores.
///
/// <para>Every template quoted here is verbatim from Wars of Liberty's own
/// <c>stringtabley.xml</c>, because the point of this feature is to reproduce what the engine
/// prints rather than to word it ourselves. The rendered results were checked against the game's
/// tooltip for the same card.</para>
/// </summary>
public class CardEffectTextTests
{
    // The real templates, by symbol.
    private const string ChangeCost = "%1s: Changes %2s cost by %3.2f%%";
    private const string ChangeHitpoints = "%1s: Changes Hitpoints by %2.2f%%";
    private const string ChangeDamage = "%1s: Changes %2s Damage by %3.2f%%";
    private const string ChangeWorkRate = "%1s: Changes %2s Work Rate for %3s by %4.2f%%";
    private const string AddDamageBonus = "%1s: Adds %2.2f to %3s Damage Bonus against %4s";
    private const string FreeUnit = "Delivers %d %s";
    private const string ResourceToInventory = "%1s: Adds %2.2f %3s to your inventory";

    private static CardEffect Effect(
        string subtype, string relativity, double amount,
        string resource = "", string unitType = "", string action = "",
        string targetType = "ProtoUnit", string targetName = "TradingPost",
        bool allActions = false) =>
        new("Data", subtype, relativity, amount, resource, unitType, action,
            targetType, targetName, allActions);

    private static string? Render(
        CardEffect effect, string template, string? display = "Trading Post",
        string allActionsLabel = "All Actions")
    {
        var symbol = CardEffectText.SymbolFor(effect.Subtype, effect.Relativity);
        return CardEffectText.Render(
            effect,
            s => s == CardEffectText.AllActionsSymbol ? allActionsLabel
               : s == symbol ? template
               : null,
            _ => display);
    }

    // ------------------------------------------------------------------ picking the template

    [Theory]
    [InlineData("Cost", "Percent", "cStringChangeCostEffect")]
    [InlineData("Hitpoints", "BasePercent", "cStringChangeHitpointsEffect")]
    [InlineData("Hitpoints", "Absolute", "cStringAddHitpointsEffect")]
    [InlineData("Hitpoints", "Assign", "cStringSetHitpointsEffect")]
    [InlineData("DamageBonus", "Absolute", "cStringAddDamageBonusEffect")]
    public void TheRelativityChoosesTheVerb(string subtype, string relativity, string expected) =>
        Assert.Equal(expected, CardEffectText.SymbolFor(subtype, relativity));

    /// <summary>The file writes these two ways, and both mean the same thing.</summary>
    [Theory]
    [InlineData("basepercent")]
    [InlineData("BasePercent")]
    [InlineData("Basepercent")]
    public void RelativityIsReadWithoutRegardToCase(string relativity) =>
        Assert.Equal("cStringChangeDamageEffect", CardEffectText.SymbolFor("Damage", relativity));

    /// <summary>
    /// Both free-unit families share one template, and it is the one that fills the 20 cards of a
    /// real deck that have no written description at all.
    /// </summary>
    [Theory]
    [InlineData("FreeHomeCityUnit")]
    [InlineData("FreeHomeCityUnitIfTechObtainable")]
    public void DeliveringUnitsHasItsOwnTemplateWhateverTheRelativity(string subtype) =>
        Assert.Equal("cStringFreeHomeCityUnitEffect", CardEffectText.SymbolFor(subtype, "Absolute"));

    /// <summary>The one subtype whose template carries no Change/Add/Set verb of its own.</summary>
    [Fact]
    public void AddingResourcesUsesTheInventoryTemplate() =>
        Assert.Equal("cStringResourceEffect", CardEffectText.SymbolFor("Resource", "Absolute"));

    /// <summary>
    /// <b>Self-limiting by construction.</b> The symbol is BUILT rather than looked up in a list,
    /// so a subtype the engine cannot describe just names a template that is not in the string
    /// table and the line is dropped — no list to keep in step with 41 subtypes.
    /// </summary>
    [Theory]
    [InlineData("AddTrain", "Absolute")]
    [InlineData("PopulationCount", "Absolute")]
    public void ASubtypeTheEngineCannotDescribeStillNamesATemplateThatWillNotResolve(
        string subtype, string relativity)
    {
        var symbol = CardEffectText.SymbolFor(subtype, relativity);
        Assert.NotNull(symbol);

        // ...and the string table has no such symbol, so nothing is rendered.
        Assert.Null(CardEffectText.Render(
            Effect(subtype, relativity, 1), _ => null, _ => "Trading Post"));
    }

    [Theory]
    [InlineData("", "Percent")]
    [InlineData("Cost", "")]
    [InlineData("Cost", "Sideways")]
    public void NothingUsableYieldsNoSymbol(string subtype, string relativity) =>
        Assert.Null(CardEffectText.SymbolFor(subtype, relativity));

    // ------------------------------------------------------------------ the arithmetic

    /// <summary>Both percentage relativities store a MULTIPLIER, not a delta.</summary>
    [Theory]
    [InlineData(0.80, -20)]
    [InlineData(1.33, 33)]
    [InlineData(1.0, 0)]
    public void APercentageIsTheMultiplierMinusOne(double amount, double expected) =>
        Assert.Equal(expected, CardEffectText.PercentOf(amount), 6);

    // ------------------------------------------------------------------ the refusals

    /// <summary>
    /// <b>The assertion the whole feature rests on.</b> A misordered argument list does not throw
    /// and does not look broken — it produces a confident sentence saying a card changes wood cost
    /// by 33 %. So a template is filled only when its slots match the arguments in KIND as well as
    /// in count, and this is the case count alone lets through: three arguments either way, and
    /// only the types tell them apart.
    /// </summary>
    [Fact]
    public void ATemplateIsRefusedWhenTheArgumentsAreInTheWrongOrder()
    {
        Assert.Equal(
            "Player: Adds 300.00 Trade to your inventory",
            CardEffectText.Format(ResourceToInventory, new object[] { "Player", 300.0, "Trade" }));

        Assert.Null(CardEffectText.Format(ResourceToInventory, new object[] { "Player", "Trade", 300.0 }));
    }

    [Fact]
    public void ATemplateWithMoreSlotsThanArgumentsIsRefused() =>
        Assert.Null(CardEffectText.Format(ChangeCost, new object[] { "Trading Post", 20.0 }));

    /// <summary>An argument that lands nowhere means this is the wrong template, not a shorter one.</summary>
    [Fact]
    public void AnArgumentWithNoSlotToLandInIsRefused() =>
        Assert.Null(CardEffectText.Format(
            ChangeHitpoints, new object[] { "Trading Post", 33.0, "Wood" }));

    [Fact]
    public void AnEmptyWordIsRefusedRatherThanLeavingAHole() =>
        Assert.Null(CardEffectText.Format(ChangeCost, new object[] { "Trading Post", "", 20.0 }));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoTemplateRendersNothing(string? template) =>
        Assert.Null(CardEffectText.Format(template, new object[] { "x" }));

    // ------------------------------------------------------------------ formatting

    /// <summary>
    /// <c>%%</c> is a literal per cent sign and has to be matched before anything else, or the
    /// second one reads as a placeholder and every percentage line comes out wrong.
    /// </summary>
    [Fact]
    public void ADoubledPerCentSignIsALiteralOne() =>
        Assert.Equal(
            "Trading Post: Changes Hitpoints by 33.00%",
            CardEffectText.Format(ChangeHitpoints, new object[] { "Trading Post", 33.0 }));

    /// <summary>The other form the templates use: no positions, filled in order.</summary>
    [Fact]
    public void SequentialPlaceholdersAreFilledInOrder() =>
        Assert.Equal("Delivers 8 Chu Ko Nu",
            CardEffectText.Format(FreeUnit, new object[] { 8.0, "Chu Ko Nu" }));

    /// <summary>The precision is the template's, and it is not the launcher's to change.</summary>
    [Fact]
    public void ThePrecisionComesFromTheTemplate() =>
        Assert.Equal("Trading Post: Changes Wood cost by -20.00%",
            CardEffectText.Format(ChangeCost, new object[] { "Trading Post", "Wood", -20.0 }));

    // ------------------------------------------------------------------ end to end

    /// <summary>
    /// The three lines of <c>YPHCExpandedTradingPost</c>, which is the card this whole feature was
    /// designed against: its written description says the posts are "cheaper and stronger" and
    /// never says by how much.
    /// </summary>
    [Fact]
    public void TheExampleCardRendersWhatTheGameShows()
    {
        Assert.Equal(
            "Trading Post: Changes Wood cost by -20.00%",
            Render(Effect("Cost", "Percent", 0.80, resource: "Wood"), ChangeCost));

        Assert.Equal(
            "Trading Post: Changes Hitpoints by 33.00%",
            Render(Effect("Hitpoints", "BasePercent", 1.33), ChangeHitpoints));

        Assert.Equal(
            "Delivers 1 Trading Post Rickshaw",
            Render(
                Effect("FreeHomeCityUnit", "Absolute", 1, unitType: "ypTradingPostWagon",
                    targetType: "Player", targetName: ""),
                FreeUnit, display: "Trading Post Rickshaw"));
    }

    /// <summary>
    /// 2,715 of the tech tree's damage effects name no action and set <c>allactions</c> instead.
    /// Without the engine's own word for that the slot is empty and the line is dropped — which
    /// was most damage cards.
    /// </summary>
    [Fact]
    public void AnEffectOnEveryActionUsesTheEnginesWordForIt()
    {
        var effect = Effect("Damage", "BasePercent", 1.30, allActions: true, targetName: "ypChuKoNu");

        Assert.Equal("Chu Ko Nu: Changes All Actions Damage by 30.00%",
            Render(effect, ChangeDamage, display: "Chu Ko Nu"));
    }

    /// <summary>"Adds N to X" puts the number second; "Changes X by N" puts it last.</summary>
    /// <summary>
    /// "Adds N to X" puts the number second; "Changes X by N" puts it last. And the unit a bonus
    /// is AGAINST is a second name to resolve, not the target — left raw it printed
    /// "against AbstractHeavyInfantry".
    /// </summary>
    [Fact]
    public void TheAddFamilyPutsTheNumberBeforeTheWordsAndNamesBothUnits()
    {
        var names = new Dictionary<string, string>
        {
            ["ypChuKoNu"] = "Chu Ko Nu",
            ["AbstractHeavyInfantry"] = "Heavy Infantry",
        };

        var effect = Effect("DamageBonus", "Absolute", 1, unitType: "AbstractHeavyInfantry",
            allActions: true, targetName: "ypChuKoNu");

        var line = CardEffectText.Render(
            effect,
            s => s == CardEffectText.AllActionsSymbol ? "All Actions" : AddDamageBonus,
            p => names.TryGetValue(p, out var n) ? n : null);

        Assert.Equal("Chu Ko Nu: Adds 1.00 to All Actions Damage Bonus against Heavy Infantry", line);
    }

    [Fact]
    public void WorkRateNamesTheActionAndThenWhatItIsWorkedOn()
    {
        var names = new Dictionary<string, string> { ["AbstractVillager"] = "Villager" };

        var effect = Effect("WorkRate", "BasePercent", 1.20, unitType: "Tree", action: "Gather",
            targetName: "AbstractVillager");

        var line = CardEffectText.Render(
            effect, _ => ChangeWorkRate, p => names.TryGetValue(p, out var n) ? n : null);

        // "Tree" is left as the file writes it: the mod names no group for it, and the internal
        // name still says what it is.
        Assert.Equal("Villager: Changes Gather Work Rate for Tree by 20.00%", line);
    }

    /// <summary>
    /// A target of <c>Player</c> or <c>techAll</c> has no name, and the engine has no generic word
    /// this could borrow — its "Player" strings are all UI captions. Seven effects of a real deck
    /// land here and say nothing rather than something invented.
    /// </summary>
    [Theory]
    [InlineData("Player")]
    [InlineData("techAll")]
    public void AnEffectWithNoNameableTargetSaysNothing(string targetType) =>
        Assert.Null(Render(
            Effect("ResourceTrickleRate", "Absolute", 0.3, resource: "Wood",
                targetType: targetType, targetName: ""),
            "%1s: Adds %2.2f to %3s Trickle Rate", display: null));

    // ------------------------------------------------------------------ the batches

    [Fact]
    public void EveryTemplateAndNameASetOfEffectsNeedsIsAskedForOnce()
    {
        var effects = new[]
        {
            Effect("Cost", "Percent", 0.8, resource: "Wood"),
            Effect("Damage", "BasePercent", 1.3, allActions: true, targetName: "ypChuKoNu"),
            Effect("FreeHomeCityUnit", "Absolute", 1, unitType: "ypTradingPostWagon",
                targetType: "Player", targetName: ""),
        };

        var symbols = CardEffectText.SymbolsIn(effects).ToList();
        Assert.Contains("cStringChangeCostEffect", symbols);
        Assert.Contains("cStringFreeHomeCityUnitEffect", symbols);

        // Not a template but a word one of them needs, so it has to travel with them.
        Assert.Contains(CardEffectText.AllActionsSymbol, symbols);

        var protos = CardEffectText.ProtoNamesIn(effects).ToList();
        Assert.Contains("TradingPost", protos);
        Assert.Contains("ypChuKoNu", protos);
        Assert.Contains("ypTradingPostWagon", protos);
    }

    /// <summary>A non-Data effect describes nothing — <c>TextOutput</c> is the arrival chat line.</summary>
    [Fact]
    public void OnlyDataEffectsDescribeAnything()
    {
        var chatLine = new CardEffect("TextOutput", "", "", 0, "", "", "", "", "");

        Assert.Null(CardEffectText.Render(chatLine, _ => ChangeHitpoints, _ => "Trading Post"));
        Assert.Empty(CardEffectText.SymbolsIn(new[] { chatLine }));
    }

    [Theory]
    [InlineData("AbstractBuilding", "cStringAbstractNameBuilding")]
    [InlineData("AbstractVillager", "cStringAbstractNameVillager")]
    public void AnAbstractTypeAsksTheStringTableForItsGroupName(string proto, string expected) =>
        Assert.Equal(expected, CardEffectText.AbstractSymbolFor(proto));

    [Theory]
    [InlineData("TradingPost")]
    [InlineData("Abstract")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingThatIsNotAnAbstractTypeAsksForNothing(string? proto) =>
        Assert.Null(CardEffectText.AbstractSymbolFor(proto));
}
