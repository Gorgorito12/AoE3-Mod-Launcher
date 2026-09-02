using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// THE TEST THAT WOULD HAVE CAUGHT THE BUG THAT SHIPPED IN 1.0.13l, and the reason none of the
/// existing ones could.
///
/// <para>Every other test around match history builds a <see cref="MatchHistoryRow"/> in C#
/// (<c>new MatchHistoryRow { … }</c>). That exercises the logic and never once exercises the
/// WIRE — and the wire is where it broke: the server added <c>rated</c> as a raw SQLite column,
/// which arrives as the number <c>1</c>, the DTO declared it <c>bool?</c>, and
/// <see cref="JsonSerializer"/> throws rather than binding a number to a bool. The throw does not
/// skip the field: it aborts the WHOLE response, so one integer took down the entire History
/// page and left it on "Loading…" for ever.</para>
///
/// <para>So these deserialise from JSON text, which is the only shape that can fail this way.
/// The first case is the ACTUAL payload captured from the live server on the day it broke.</para>
/// </summary>
public class HistoryWireContractTests
{
    /// <summary>The options the real client uses. Copied deliberately rather than shared: if the
    /// client's converters are removed, these tests must fail.</summary>
    private static JsonSerializerOptions Options() => new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new TolerantBoolConverter(), new TolerantNullableBoolConverter() },
    };

    /// <summary>
    /// The real thing, byte for byte off the wire: `"rated": 1`, a null `unrated_reason`, an
    /// integer `rating_before` beside a fractional `rating_after`, and a participants array.
    /// </summary>
    private const string RealPayload = """
    {"matches":[{
      "id":"5abae658-c0ae-41a8-8116-f93d52fb2759",
      "mod_id":"wol",
      "map_name":"ESOC_Fertile Crescent",
      "duration_seconds":1070,
      "started_at":"2026-08-30T01:09:55.5999949Z",
      "ended_at":"2026-08-30T01:27:46.0542381Z",
      "replay_object_key":null,
      "rated":1,
      "unrated_reason":null,
      "team":0,
      "civ":null,
      "score":0,
      "result":0,
      "rating_before":1500,
      "rating_after":1383.3580792733678,
      "player_count":2,
      "participants":[{"user_id":"cb6eef5f-65ae-4178-a6cb-809557ec3bc8","display_name":"Alu","result":1}]
    }]}
    """;

    [Fact]
    public void TheRealServerPayloadBinds()
    {
        var resp = JsonSerializer.Deserialize<MatchHistoryResponse>(RealPayload, Options());

        Assert.NotNull(resp);
        var row = Assert.Single(resp!.Matches);

        // The field that broke it.
        Assert.True(row.Rated);

        // And the rest of the row still binds, so the converter did not paper over a page that
        // was failing for some other reason.
        Assert.Equal("wol", row.ModId);
        Assert.Equal(1070, row.DurationSeconds);
        Assert.Equal(0, row.Result);
        Assert.Equal(1500, row.RatingBefore);
        Assert.Equal(1383.3580792733678, row.RatingAfter!.Value, 6);
        Assert.Equal(2, row.PlayerCount);
        Assert.Single(row.Participants);
    }

    /// <summary>
    /// Both spellings of the same fact. The server coerces this field now, so it sends a real
    /// boolean — and the launcher still has to read the number, because a launcher in the wild
    /// talks to whatever server is deployed, including one that predates the coercion.
    /// </summary>
    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void ABooleanFieldReadsBothWaysOffTheWire(string json, bool expected)
    {
        var resp = JsonSerializer.Deserialize<MatchHistoryResponse>(
            $$"""{"matches":[{"id":"m","rated":{{json}}}]}""", Options());

        Assert.Equal(expected, resp!.Matches[0].Rated);
    }

    /// <summary>
    /// NULL SURVIVES AS NULL. On this API a null <c>rated</c> means the row predates the
    /// migration that added the column — "we don't know", which is a different claim from "it did
    /// not count". Folding it into false would make every old match assert it was unrated, and
    /// the History card would explain a reason that was never given.
    /// </summary>
    [Fact]
    public void AnAbsentAnswerStaysAbsent()
    {
        var explicitNull = JsonSerializer.Deserialize<MatchHistoryResponse>(
            """{"matches":[{"id":"m","rated":null}]}""", Options());
        Assert.Null(explicitNull!.Matches[0].Rated);

        // And a server too old to send the field at all.
        var missing = JsonSerializer.Deserialize<MatchHistoryResponse>(
            """{"matches":[{"id":"m"}]}""", Options());
        Assert.Null(missing!.Matches[0].Rated);
    }

    /// <summary>
    /// The converter is deliberately NARROW: it accepts what a database produces and nothing
    /// else. A string "true" is refused rather than guessed at — quietly accepting anything is
    /// how a contract stops meaning something, and a wrong "rated" moves what the UI tells a
    /// player about their own match.
    /// </summary>
    [Fact]
    public void AStringIsNotABoolean()
    {
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<MatchHistoryResponse>(
            """{"matches":[{"id":"m","rated":"true"}]}""", Options()));
    }

    /// <summary>
    /// EVERY participant's civilization comes off the wire, not just the caller's own.
    ///
    /// <para>The history select has always carried <c>mp.civ</c> for the requesting user's row,
    /// so a match could say which civ YOU played and never which one you played against — and
    /// half a matchup is not a matchup. This pins the shape of the participant object, which is
    /// the half a launcher-side test can check; the server sending it is pinned by its own
    /// suite.</para>
    /// </summary>
    [Fact]
    public void EveryParticipantCarriesItsOwnCivilization()
    {
        var resp = JsonSerializer.Deserialize<MatchHistoryResponse>("""
        {"matches":[{"id":"m","civ":"Chinese","participants":[
          {"user_id":"a","display_name":"Gorgorito","result":1,"civ":"Chinese"},
          {"user_id":"b","display_name":"Alucard","result":0,"civ":"Colombians"}
        ]}]}
        """, Options());

        var row = Assert.Single(resp!.Matches);
        Assert.Equal("Chinese", row.Civ);
        Assert.Equal("Chinese", row.Participants[0].Civ);
        Assert.Equal("Colombians", row.Participants[1].Civ);
    }

    /// <summary>
    /// And a match stored before civilizations were reported reads as null rather than as an
    /// empty string, which every surface treats as "draw nothing".
    /// </summary>
    [Fact]
    public void AMatchFromBeforeCivsWereReportedSaysNothing()
    {
        var resp = JsonSerializer.Deserialize<MatchHistoryResponse>("""
        {"matches":[{"id":"m","participants":[{"user_id":"a","result":0.5}]}]}
        """, Options());

        Assert.Null(resp!.Matches[0].Civ);
        Assert.Null(resp.Matches[0].Participants[0].Civ);
    }

    // ------------------------------------------------------- what the section draws

    /// <summary>
    /// THE SECOND BUG, and the one that mattered more: an error must beat "loading".
    ///
    /// <para>The failure path repainted from inside its own <c>catch</c>, before the
    /// <c>finally</c> cleared the refreshing flag — so the spinner branch, which asks "no rows
    /// AND still refreshing", matched, returned, and the error line under it was unreachable.
    /// The message sat in a field, correct and invisible, for ever. That masked ANY first-fetch
    /// failure, not just the one above.</para>
    /// </summary>
    [Fact]
    public void AnErrorIsShownEvenWhileTheFlagSaysRefreshing()
    {
        Assert.Equal(HistorySection.Error,
            MatchHistoryView.SectionFor(rows: null, error: "boom", refreshing: true));

        Assert.Equal(HistorySection.Error,
            MatchHistoryView.SectionFor(rows: null, error: "boom", refreshing: false));
    }

    [Fact]
    public void NothingInHandAndNothingWrongIsLoading()
    {
        Assert.Equal(HistorySection.Loading,
            MatchHistoryView.SectionFor(rows: null, error: null, refreshing: true));

        // Also the beat before the fetch is kicked.
        Assert.Equal(HistorySection.Loading,
            MatchHistoryView.SectionFor(rows: null, error: null, refreshing: false));
    }

    /// <summary>
    /// A page in hand always wins. A refresh that fails keeps the matches already on screen —
    /// real results are worth more than a line about a hiccup — and an empty page is still a
    /// page, not an error.
    /// </summary>
    [Fact]
    public void RowsInHandBeatBothOfTheOtherStates()
    {
        var rows = new List<MatchHistoryRow>();

        Assert.Equal(HistorySection.Rows, MatchHistoryView.SectionFor(rows, "boom", true));
        Assert.Equal(HistorySection.Rows, MatchHistoryView.SectionFor(rows, null, false));
    }
}
