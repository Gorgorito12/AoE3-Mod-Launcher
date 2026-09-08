using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The wall an organiser hit: "Too many requests — slow down", with the tournament then
/// impossible to cancel, because cancelling comes out of the same per-minute bucket as
/// registering and withdrawing.
///
/// <para>Two of the three causes are here. The third is the ceiling itself, which lives on
/// the server. What this pins is that the launcher stops spending the budget on a button
/// that does nothing, and that when the ceiling IS reached the refusal says how long to wait
/// instead of reading as a permanent failure.</para>
/// </summary>
[Collection("wpf-and-language")]
public class TournamentRateLimitTests
{
    // ---------------------------------------------------------------- the wait

    /// <summary>
    /// The seconds come off the wire as a JSON number or as a string, depending on how the
    /// server serialised them, and the launcher was throwing both away.
    /// </summary>
    [Fact]
    public void TheWaitIsReadWhicheverWayTheServerSentIt()
    {
        Assert.Equal(42, RateLimitNotice.Seconds(Details(42)));
        Assert.Equal(42, RateLimitNotice.Seconds(Details("42")));
        Assert.Equal(42, RateLimitNotice.Seconds(
            new Dictionary<string, object?> { ["retry_after"] = 42 }));

        // Rounded UP: 12 seconds told to somebody with 12.4 left sends them straight back
        // into the same refusal.
        Assert.Equal(13, RateLimitNotice.Seconds(Details(12.4)));
    }

    /// <summary>
    /// No number is not zero. An older backend, a refusal that carries no detail, a value
    /// that makes no sense — every one of them means "word it without a number" rather than
    /// "wait 0 seconds", which would be a lie the reader can act on.
    /// </summary>
    [Fact]
    public void NoUsableNumberMeansNoNumber()
    {
        Assert.Null(RateLimitNotice.Seconds(null));
        Assert.Null(RateLimitNotice.Seconds(new Dictionary<string, object?>()));
        Assert.Null(RateLimitNotice.Seconds(
            new Dictionary<string, object?> { ["retry_after"] = null }));
        Assert.Null(RateLimitNotice.Seconds(Details("later")));
        Assert.Null(RateLimitNotice.Seconds(Details(0)));
        Assert.Null(RateLimitNotice.Seconds(Details(-5)));
        // A number in the thousands is a server that has stopped making sense, and "wait
        // 90000 seconds" is not a sentence anybody can act on.
        Assert.Null(RateLimitNotice.Seconds(Details(90_000)));
    }

    /// <summary>Both wordings exist in both languages: the one with the number and the one
    /// for a server that did not send it.</summary>
    [Theory]
    [InlineData("MpTournamentErrRateLimited")]
    [InlineData("MpTournamentErrRateLimitedIn")]
    public void TheWordingExistsInBothLanguages(string key)
    {
        var previous = Strings.Language;
        try
        {
            Strings.SetLanguage("es");
            Assert.NotEqual(key, Strings.Get(key));
            Strings.SetLanguage("en");
            Assert.NotEqual(key, Strings.Get(key));
        }
        finally { Strings.SetLanguage(previous); }
    }

    private static Dictionary<string, object?> Details(object value)
        => new()
        {
            ["retry_after"] = JsonSerializer.Deserialize<JsonElement>(
                JsonSerializer.Serialize(value)),
        };

    // ---------------------------------------------------------------- the dead button

    /// <summary>
    /// AN ENTRY THAT IS ALREADY OUT OFFERS NOTHING. "Withdraw" was drawn on every entry of
    /// mine, withdrawn ones included, because the rule it asked (`CanWithdraw`) is about the
    /// tournament and about me and never about the row. Pressing it sent a request the server
    /// accepts and does nothing with — and spent one of the account's tournament actions for
    /// that minute, which is how somebody reaches the ceiling by pressing a button that could
    /// not have worked.
    /// </summary>
    [Fact]
    public void AWithdrawnEntryDoesNotOfferToWithdrawAgain()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            try
            {
                Strings.SetLanguage("es");
                var tab = new WarsOfLibertyLauncher.Controls.MultiplayerTab();

                var t = new TournamentDetail
                {
                    Id = "c1",
                    Name = "Copa",
                    Status = "registration",
                    OwnerUserId = "me",
                    Capacity = 4,
                    Entrants = new List<TournamentEntrant>
                    {
                        new()
                        {
                            Id = "in", Kind = "solo", DisplayName = "Gorgorito12",
                            Status = "confirmed", CaptainUserId = "me",
                            MemberIds = new List<string> { "me" },
                        },
                        new()
                        {
                            Id = "out", Kind = "solo", DisplayName = "Gorgorito12",
                            Status = "withdrawn", CaptainUserId = "me",
                            MemberIds = new List<string> { "me" },
                        },
                    },
                };

                var list = (FrameworkElement)tab.BuildEntrantsList(t, "me");
                var withdraw = Strings.Get("MpTournamentWithdraw");
                var buttons = Descendants(list).OfType<Button>()
                    .Count(b => b.Content as string == withdraw);

                // One button for two rows of mine: the live entry has it, the withdrawn one
                // does not.
                Assert.Equal(1, buttons);
            }
            finally { Strings.SetLanguage(previous); }
        });

        Assert.Null(error);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }
}
