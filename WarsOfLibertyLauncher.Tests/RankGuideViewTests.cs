using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The rank guide's content (46a). It must describe the SAME places the badges wear, so every
/// expectation here is derived from the share-of-the-ladder cut: with 14 players that is
/// Sovereign 1-2, Imperial 3-4, Industrial 5-7, Fortress 8-10, Colonial 11-14.
/// </summary>
public class RankGuideViewTests
{
    private static readonly Dictionary<int, string> Fourteen =
        Enumerable.Range(1, 14).ToDictionary(i => i, i => "P" + i);

    [Fact]
    public void TheAgesSpanTheSamePlacesTheBadgesWear()
    {
        var view = RankGuideView.Build(11, 14, Fourteen);
        var spans = view.Ages.Where(a => a.Age != RankAge.Discovery).Select(a => (a.Age, a.From, a.To, a.Count)).ToList();
        Assert.Equal(new[]
        {
            (RankAge.Sovereign, 1, 2, 2),
            (RankAge.Imperial, 3, 4, 2),
            (RankAge.Industrial, 5, 7, 3),
            (RankAge.Fortress, 8, 10, 3),
            (RankAge.Colonial, 11, 14, 4),
        }, spans);
        Assert.Equal(14, view.Ages.Sum(a => a.Count));
        foreach (var a in view.Ages.Where(a => a.Age != RankAge.Discovery))
            for (var p = a.From; p <= a.To; p++)
                Assert.Equal(a.Age, RankAges.For(p, 14));
    }

    /// <summary>11th of 14 is Colonial; Fortress ends at 10th, so passing ONE player gets there,
    /// and the guide names who holds that place now.</summary>
    [Fact]
    public void TheNextStepCountsPlacesToTheNextAge()
    {
        var view = RankGuideView.Build(11, 14, Fourteen);
        Assert.Equal(RankAge.Colonial, view.MyAge);
        Assert.Equal(RankGuideStepKind.Pass, view.Next.Kind);
        Assert.Equal(1, view.Next.PlacesToPass);
        Assert.Equal(RankAge.Fortress, view.Next.NextAge);
        Assert.Equal("P10", view.Next.TargetName);
        Assert.Single(view.Ages, a => a.IsMine);
        Assert.True(view.Ages.Single(a => a.IsMine).Age == RankAge.Colonial);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void EverySovereignIsToldToDefend(int position)
        => Assert.Equal(RankGuideStepKind.Defend, RankGuideView.Build(position, 14, Fourteen).Next.Kind);

    [Fact]
    public void TheEdgesOfTheLadder()
    {
        var third = RankGuideView.Build(3, 14, Fourteen).Next;
        Assert.Equal((RankGuideStepKind.Pass, 1, RankAge.Sovereign), (third.Kind, third.PlacesToPass, third.NextAge));

        var discovery = RankGuideView.Build(0, 14, Fourteen);
        Assert.Equal(RankAge.Discovery, discovery.MyAge);
        Assert.Equal(RankGuideStepKind.PlayFirst, discovery.Next.Kind);
        Assert.True(discovery.Ages.Single(a => a.Age == RankAge.Discovery).IsMine);

        // Unknown place: no age claimed, no step, nobody marked.
        var unknown = RankGuideView.Build(null, 14, Fourteen);
        Assert.Null(unknown.MyAge);
        Assert.Equal(RankGuideStepKind.Unknown, unknown.Next.Kind);
        Assert.DoesNotContain(unknown.Ages, a => a.IsMine);
    }

    /// <summary>With the size unknown the guide falls back to the badges' own fixed positions —
    /// never a split the badges would not draw.</summary>
    [Fact]
    public void AnUnknownSizeUsesTheSameFallbackAsTheBadges()
    {
        var view = RankGuideView.Build(3, 0, new Dictionary<int, string>());
        Assert.Equal(RankAges.For(3), view.MyAge);
        Assert.Equal((1, 1), (view.Ages[0].From, view.Ages[0].To));
    }

    [Theory]
    [InlineData(1, "1st")]
    [InlineData(2, "2nd")]
    [InlineData(3, "3rd")]
    [InlineData(4, "4th")]
    [InlineData(11, "11th")]
    [InlineData(12, "12th")]
    [InlineData(13, "13th")]
    [InlineData(21, "21st")]
    [InlineData(22, "22nd")]
    public void EnglishOrdinals(int n, string expected)
        => Assert.Equal(expected, RankGuideView.Ordinal(n, Strings.LangEn));

    [Fact]
    public void SpanishOrdinals() => Assert.Equal("11.º", RankGuideView.Ordinal(11, Strings.LangEs));
}
