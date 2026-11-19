using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Repair;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The install context ("fresh", "copy") survives the elevated relaunch, so the bell after an
/// install that needed administrator rights says "installed". The case that matters:
/// <c>App.OnStartup</c> must still recognise the argument WITH the context attached, or the startup
/// self-update restarts out from under the elevated job.
/// </summary>
public class UpdateNowArgTests
{
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheArgumentWithAContextIsStillTheArgument()
    {
        Assert.True(UpdateNowArg.IsPresent(new[] { "--update-now=fresh" }));
        Assert.True(UpdateNowArg.IsPresent(new[] { "--update-now" }));
        Assert.True(UpdateNowArg.IsPresent(new[] { "--UPDATE-NOW=copy" }));
        Assert.False(UpdateNowArg.IsPresent(new[] { "--update-nowhere" }));
        Assert.False(UpdateNowArg.IsPresent(null));
    }

    [Theory]
    [InlineData("fresh", "--update-now=fresh")]
    [InlineData("copy", "--update-now=copy")]
    [InlineData(null, "--update-now")]
    [InlineData("other", "--update-now")]
    public void TheContextRoundTrips(string? context, string expected)
    {
        Assert.Equal(expected, UpdateNowArg.For(context));
        Assert.Equal(context is "fresh" or "copy" ? context : null, UpdateNowArg.ContextOf(new[] { expected }));
    }

    [Fact]
    public void AnUnknownContextIsIgnoredNotGuessed()
        => Assert.Null(UpdateNowArg.ContextOf(new[] { "--update-now=fresh;rm" }));

    [Fact]
    public void TheRepairTicketCarriesItTooAndStillParses()
    {
        var arg = RepairResume.Build("knights-and-barbarians", update: true, context: "fresh");
        Assert.Equal("--repair-now=knights-and-barbarians:update:fresh", arg);
        var ticket = RepairResume.TryParse(new[] { arg! });
        Assert.NotNull(ticket);
        Assert.Equal("knights-and-barbarians", ticket!.ModId);
        Assert.True(ticket.Update);
        Assert.Equal("fresh", RepairResume.ContextOf(new[] { arg! }));
        // A plain repair carries no context, whatever is asked.
        Assert.Equal("--repair-now=wol", RepairResume.Build("wol", update: false, context: "copy"));
    }
}
