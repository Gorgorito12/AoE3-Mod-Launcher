using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Adding a mod is a DATA change and never a code change. Run: <c>dotnet test</c>.
///
/// <para>That is the contract this file exists to hold, and the deck-card resolver is where it
/// was most at risk: it is the newest thing on the statistics page and the one that reaches into
/// a mod's own files. If it ever grows a branch for a particular mod, adding the next mod stops
/// being a catalogue entry and starts being an implementation — which is exactly what the
/// request that produced it asked to prevent.</para>
///
/// <para>These are not integration tests. Nothing here installs a mod or reads a tech tree; the
/// resolvers underneath already have their own tests (<see cref="CardNameResolverTests"/>,
/// <see cref="CivNameResolverTests"/>). What is pinned is the SHAPE: every profile in the
/// catalogue goes down the same path, and a mod that is not installed degrades to identifiers
/// instead of to a crash or an empty table.</para>
/// </summary>
public class DeckCardNamesTests
{
    private static readonly string[] Cards = { "HCXPRefrigeration", "HCCigarRollers" };
    private static readonly string[] Civs = { "Mexicans", "Americans" };

    // ---------------------------------------------------------------- the contract

    [Fact]
    public async Task THE_ONE_THAT_MATTERS_EveryCataloguedModTakesTheSamePath()
    {
        // Walked over the WHOLE registry rather than a couple of known ids: a special case
        // written for one mod would show up here as one profile behaving differently, and a
        // hand-picked list would be updated alongside the special case and hide it.
        Assert.NotEmpty(ModRegistry.All);

        foreach (var profile in ModRegistry.All)
        {
            // Not installed, which is the state every mod is in on somebody's machine.
            var vocabulary = await DeckCardNames.ResolveAsync(
                profile.Id, _ => null, Cards, Civs);

            Assert.False(vocabulary.Resolved);

            // And the rows still draw: the identifier, never a blank and never an invention.
            Assert.Equal("HCXPRefrigeration", vocabulary.NameOf("HCXPRefrigeration"));
            Assert.Equal("Mexicans", vocabulary.CivOf("Mexicans"));
            Assert.Null(vocabulary.IconOf("HCXPRefrigeration"));
        }
    }

    [Fact]
    public async Task AnUnknownModIdIsNotAnException()
    {
        // The server names the mods it has matches for, and it can name one this build's
        // catalogue has never heard of. That has to be a quiet empty answer: the picker skips
        // the chip and the table shows identifiers.
        var vocabulary = await DeckCardNames.ResolveAsync(
            "a-mod-that-does-not-exist", _ => @"C:\nowhere", Cards, Civs);

        Assert.False(vocabulary.Resolved);
        Assert.Equal("HCCigarRollers", vocabulary.NameOf("HCCigarRollers"));
    }

    [Fact]
    public async Task NoModIdMeansNoWork()
    {
        bool asked = false;
        var vocabulary = await DeckCardNames.ResolveAsync(
            null, _ => { asked = true; return @"C:\nowhere"; }, Cards, Civs);

        Assert.False(vocabulary.Resolved);
        Assert.False(asked, "an absent mod id must not send anybody looking for an install");
    }

    // ---------------------------------------------------------------- the empty vocabulary

    [Fact]
    public void AnEmptyNameStaysEmptyRatherThanBecomingAnIdentifier()
    {
        // A blank card or civilization is a row the server should not have sent, and the right
        // answer to it is nothing at all - not the string "null" and not a stray placeholder.
        Assert.Equal("", DeckCardNames.Vocabulary.None.NameOf(null));
        Assert.Equal("", DeckCardNames.Vocabulary.None.NameOf("   "));
        Assert.Equal("", DeckCardNames.Vocabulary.None.CivOf(null));
    }

    [Fact]
    public void PeekAnswersNothingBeforeAnythingIsResolved()
    {
        // Null is "not read yet", which is what makes the render draw identifiers now and ask
        // for the names in the background. It must never be confused with an empty answer.
        Assert.Null(DeckCardNames.Peek(null));
        Assert.Null(DeckCardNames.Peek("   "));
        Assert.Null(DeckCardNames.Peek("a-mod-that-does-not-exist"));
    }

    [Fact]
    public async Task AnEmptyAnswerIsNotRemembered()
    {
        // The whole reason an empty result is not cached: the player may install the mod during
        // the session, and a cached "nothing" would keep the table on identifiers until the
        // launcher was restarted.
        var profile = ModRegistry.All.First();
        await DeckCardNames.ResolveAsync(profile.Id, _ => null, Cards, Civs);

        Assert.Null(DeckCardNames.Peek(profile.Id));
    }

    // ---------------------------------------------------------------- resolution itself

    [Fact]
    public void AResolvedNameWinsAndAMissingOneFallsBack()
    {
        // Built by hand rather than from a mod on disk: what is being pinned is the lookup, and
        // a test that needed an installed mod would simply not run on most machines.
        var vocabulary = new DeckCardNames.Vocabulary(
            new Dictionary<string, CardDetail>(StringComparer.Ordinal)
            {
                ["HCXPRefrigeration"] = new CardDetail("Refrigeración", null, null),
            },
            new Dictionary<string, System.Windows.Media.ImageSource>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Mexicans"] = "México",
            });

        Assert.True(vocabulary.Resolved);
        Assert.Equal("Refrigeración", vocabulary.NameOf("HCXPRefrigeration"));
        Assert.Equal("México", vocabulary.CivOf("Mexicans"));

        // One card resolving does not make the next one resolve. The table mixes both states in
        // the same column and the row decides its own styling from exactly this comparison.
        Assert.Equal("HCCigarRollers", vocabulary.NameOf("HCCigarRollers"));
        Assert.Equal("Americans", vocabulary.CivOf("Americans"));
    }

    [Fact]
    public void ACardWithNoNameFallsBackInsteadOfDrawingBlank()
    {
        // A detail that resolved but carries an empty name is worse than one that did not
        // resolve at all: it would blank the cell. The identifier is the floor.
        var vocabulary = new DeckCardNames.Vocabulary(
            new Dictionary<string, CardDetail>(StringComparer.Ordinal)
            {
                ["HCXPRefrigeration"] = new CardDetail("", null, null),
            },
            new Dictionary<string, System.Windows.Media.ImageSource>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal));

        Assert.Equal("HCXPRefrigeration", vocabulary.NameOf("HCXPRefrigeration"));
    }
}
