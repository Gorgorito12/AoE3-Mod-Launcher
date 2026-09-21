using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The one word a match row leads with, and the one case where it says nothing.
/// </summary>
public class MatchModeViewTests
{
    [Fact]
    public void ACompetitiveRoomSaysSo()
        => Assert.Equal("MpMatchModeCompetitive", MatchModeView.LabelKeyFor(true));

    [Fact]
    public void ACasualRoomSaysSo()
        => Assert.Equal("MpMatchModeCasual", MatchModeView.LabelKeyFor(false));

    /// <summary>
    /// THE ONE THAT MATTERS. The flag is joined from the lobby, so it is absent for every match
    /// stored before the field existed and for any whose lobby row has gone. "We don't know what
    /// kind of room this was" is not "casual", and printing CASUAL there would relabel a chunk
    /// of everybody's history with nothing on screen able to contradict it.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_AnUnknownRoomSaysNothing()
        => Assert.Null(MatchModeView.LabelKeyFor(null));

    /// <summary>
    /// Both keys exist in both languages. A missing key renders as the key itself — the visible
    /// not-found signal — and the compiler cannot catch a string literal, so a green build is no
    /// evidence at all that these landed.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheKeysAreRealStrings(bool competitive)
    {
        var key = MatchModeView.LabelKeyFor(competitive)!;

        foreach (var lang in new[] { "en", "es" })
        {
            var text = Strings.GetIn(lang, key);
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.NotEqual(key, text);
        }
    }
}
