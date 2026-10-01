using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Who an AI game was played against (<see cref="AiGameStats.ParseIdentity"/>) and how a game's
/// units are listed (<see cref="LocalGames.UnitLines"/>).
/// </summary>
public class AiOpponentTests
{
    /// <summary>The head of a real personality file (wolAliPasha), trimmed.</summary>
    private const string RealHead = """
        <?xml version="1.0" encoding="UTF-16"?>
        <ai>
        	<version>2</version>
        	<script>aiLoaderStandard</script>
        	<icon>WoL\ui\singleplayer\cpai_avatar_egyptians-xs</icon>
        	<nameid>550512</nameid>
        	<tooltipid>550513</tooltipid>
        	<forcedciv>Egyptians</forcedciv>
        	<playernames>
        		<nameid>999
        			<civ>Erucakran</civ>
        		</nameid>
        	</playernames>
        	<history/>
        </ai>
        """;

    [Fact]
    public void TheIdentityComesFromTheRootsOwnChildren()
    {
        var id = AiGameStats.ParseIdentity(RealHead.Replace("encoding=\"UTF-16\"", ""));

        Assert.NotNull(id);
        Assert.Equal(550512, id!.NameId);
        Assert.Equal(@"WoL\ui\singleplayer\cpai_avatar_egyptians-xs", id.Icon);
        Assert.Equal("Egyptians", id.ForcedCiv);
    }

    /// <summary>
    /// THE ONE THAT MATTERS: <c>nameid</c> appears again inside <c>playernames</c>. A file whose
    /// root has none must not borrow that one — it would put a wrong name on the card.
    /// </summary>
    [Fact]
    public void ANestedNameIdIsNeverTaken()
    {
        var xml = """
            <ai>
            	<playernames><nameid>999<civ>X</civ></nameid></playernames>
            </ai>
            """;

        var id = AiGameStats.ParseIdentity(xml);
        Assert.NotNull(id);
        Assert.Null(id!.NameId);
        Assert.Null(id.ForcedCiv);
    }

    [Fact]
    public void TextThatDoesNotParseIsNull()
        => Assert.Null(AiGameStats.ParseIdentity("<ai><broken"));

    private static AiGameRecord Game(Dictionary<string, int> units) => new() { Units = units };

    /// <summary>Two protos with one display name were two entries on a real card.</summary>
    [Fact]
    public void UnitsSharingADisplayNameAreMerged()
    {
        var lines = LocalGames.UnitLines(
            Game(new() { ["RuinsA"] = 1, ["RuinsB"] = 1, ["Settler"] = 7 }),
            new Dictionary<string, string>
            {
                ["RuinsA"] = "Ancient Ruins", ["RuinsB"] = "Ancient Ruins", ["Settler"] = "Settler",
            },
            max: 8);

        Assert.Equal(new[] { ("Settler", 7, true), ("Ancient Ruins", 2, true) }, lines);
    }

    [Fact]
    public void RandomMapPropsAreDropped_AndUnresolvedNamesAreFlaggedNotHidden()
    {
        var lines = LocalGames.UnitLines(
            Game(new() { ["RT_bone_universal"] = 1, ["warga"] = 6, ["Mill"] = 1 }),
            new Dictionary<string, string> { ["Mill"] = "Mill" },
            max: 8);

        Assert.DoesNotContain(lines, l => l.Text.StartsWith("RT_"));
        Assert.Contains(("warga", 6, false), lines);
        Assert.Contains(("Mill", 1, true), lines);
    }

    /// <summary>A game with no recorded result counts in the total and on neither side.</summary>
    [Fact]
    public void TheSummaryNeverCountsAnUnknownResultAsALoss()
    {
        var games = new List<AiGameRecord>
        {
            new() { Won = true }, new() { Won = false }, new() { Won = false }, new() { Won = null },
        };

        Assert.Equal((4, 1, 2), LocalGames.SummarizeAi(games));
    }
}
