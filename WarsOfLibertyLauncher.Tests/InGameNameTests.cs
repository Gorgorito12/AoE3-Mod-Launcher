using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Tests for reading the name a player appears under inside the game — the only link
/// between a recorded game and the person who played it.
///
/// <para><b>The assumption this replaced was wrong, and wrong in the worst way.</b> The
/// plan was to take it from <c>LastProfile3.dat</c>. That file holds the active profile's
/// FILE name, which on all five installs checked is the stock <c>NewProfile3</c> — the
/// same string for everybody. Used as an identity it would have matched every player
/// against the same placeholder: not a crash, just quietly attributing results to whoever
/// happened to be checked first.</para>
///
/// <para>The real name is <c>&lt;OnlineName&gt;</c> inside the profile XML. The snippets
/// below are shaped like the real files, which are UTF-16 and around 230 KB.</para>
/// </summary>
public class InGameNameTests
{
    [Fact]
    public void ReadsTheOnlineName()
    {
        const string xml = "<Profile><Name>NewProfile3</Name><ESOName></ESOName>"
                         + "<OnlineName>Gorgorito</OnlineName></Profile>";

        Assert.Equal("Gorgorito", UserDataService.ExtractInGameName(xml));
    }

    [Fact]
    public void DoesNotReturnTheProfileFileName()
    {
        // <Name> is the file, and it is the same on every install. Returning it would
        // make every player look like the same person.
        const string xml = "<Profile><Name>NewProfile3</Name>"
                         + "<OnlineName>Gorgorito</OnlineName></Profile>";

        Assert.NotEqual("NewProfile3", UserDataService.ExtractInGameName(xml));
    }

    [Fact]
    public void FallsBackToTheSkirmishNickname()
    {
        // Present in its own right on every profile inspected, so accepting it costs
        // nothing and covers a profile whose OnlineName was never filled in.
        const string xml = "<Profile><OnlineName></OnlineName>"
                         + "<Setting Name=\"optionskirmishnickname\">Gorgorito</Setting></Profile>";

        Assert.Equal("Gorgorito", UserDataService.ExtractInGameName(xml));
    }

    [Fact]
    public void PrefersTheOnlineNameOverTheNickname()
    {
        const string xml = "<Profile><OnlineName>Online</OnlineName>"
                         + "<Setting Name=\"optionskirmishnickname\">Skirmish</Setting></Profile>";

        Assert.Equal("Online", UserDataService.ExtractInGameName(xml));
    }

    [Fact]
    public void TrimsSurroundingWhitespace()
        => Assert.Equal("Gorgorito",
            UserDataService.ExtractInGameName("<OnlineName>  Gorgorito  </OnlineName>"));

    [Theory]
    [InlineData("")]
    [InlineData("<Profile><Name>NewProfile3</Name></Profile>")]      // no name fields at all
    [InlineData("<OnlineName></OnlineName>")]                        // present but empty
    [InlineData("<OnlineName>   </OnlineName>")]
    [InlineData("<OnlineName>unterminated")]                         // truncated file
    public void UnreadableProfilesGiveNull(string xml)
        => Assert.Null(UserDataService.ExtractInGameName(xml));

    [Fact]
    public void NullInputIsNull()
        => Assert.Null(UserDataService.ExtractInGameName(null!));

    [Fact]
    public void TheNameIsPerModSoCasingCannotBeRelIedOn()
    {
        // The same person really is "Gorgorito" in one mod and "gorgorito" in another,
        // because each mod keeps its own profile. Everything comparing against this has
        // to be case-insensitive — pinned here so the reason is recorded.
        var a = UserDataService.ExtractInGameName("<OnlineName>Gorgorito</OnlineName>")!;
        var b = UserDataService.ExtractInGameName("<OnlineName>gorgorito</OnlineName>")!;

        Assert.NotEqual(a, b);
        Assert.Equal(a, b, ignoreCase: true);
    }
}
