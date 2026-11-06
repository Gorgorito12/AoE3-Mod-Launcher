using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

public class CopyVersionsTests
{
    [Theory]
    [InlineData("1.2.0e", "1.2.0d", true)]
    [InlineData("1.2.1", "1.2.0e", true)]
    [InlineData("v1.3", "1.2.0e", true)]
    [InlineData("1.2.0d", "1.2.0e", false)]
    [InlineData("1.2.0e", "1.2.0e", false)]
    public void ComparesVersionsThatCanBeOrdered(string candidate, string current, bool expected)
        => Assert.Equal(expected, CopyVersions.IsNewer(candidate, current));

    /// <summary>The refusals are the point: a tag that cannot be ordered never claims to be newer.</summary>
    [Theory]
    [InlineData("24.07.2026", "01.10.2026")]
    [InlineData("01.10.2026", "24.07.2026")]
    [InlineData("beta-0.10", "1.0")]
    [InlineData("", "1.0")]
    [InlineData("1.0", "")]
    [InlineData(null, "1.0")]
    public void ATagThatCannotBeOrderedIsNeverNewer(string? candidate, string current)
        => Assert.False(CopyVersions.IsNewer(candidate, current));

    [Fact]
    public void NamesTheNewestOfTheCopiesAhead()
    {
        var newest = CopyVersions.NewestAhead("1.2.0a", new[]
        {
            ("Vieja", "1.1.9"),
            ("Media", "1.2.0c"),
            ("Nueva", "1.2.0e"),
            ("Fecha", "24.07.2026"),
        });
        Assert.Equal(("Nueva", "1.2.0e"), newest);
    }

    [Fact]
    public void NothingAheadSaysNothing()
        => Assert.Null(CopyVersions.NewestAhead("1.2.0e", new[] { ("Otra", "1.2.0e"), ("Vieja", "1.0") }));
}
