using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>The rows of the mod window's STATISTICS (design 71).</summary>
[Collection("wpf-and-language")]
public class ModStatsRowsTests
{
    private static void Run(Action body)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            var was = Strings.Language;
            try
            {
                Strings.SetLanguage(Strings.LangEs);
                body();
            }
            finally { Strings.SetLanguage(was); }
        });
        Assert.Null(error);
    }

    private static List<string> Texts(DependencyObject root)
    {
        var found = new List<string>();
        void Walk(object node)
        {
            if (node is TextBlock t) found.Add(RevealText.PlainTextOf(t));
            if (node is DependencyObject d)
                foreach (var child in LogicalTreeHelper.GetChildren(d)) Walk(child);
        }
        Walk(root);
        return found;
    }

    private static readonly ReplayParserService.ReplayPlayer Me = new(
        1, "Jeison", 33, -1, ReplayParserService.SlotTypeHuman);
    private static readonly ReplayParserService.ReplayPlayer Rival = new(
        2, "Nathan", 7, -1, ReplayParserService.SlotTypeHuman);

    private static LocalMatchRow Match(int localSlot, double? result, int loser = -1, int winner = -1) => new(
        "Record Game 3", new DateTime(2026, 7, 27), null, "ESOC Arizona", new[] { Me, Rival },
        localSlot, result, loser, winner, new Dictionary<int, string>());

    [Theory]
    [InlineData(1.0, "VICTORIA")]
    [InlineData(0.0, "DERROTA")]
    [InlineData(null, "SIN RESULTADO")]
    public void AMatchThePlayerIsInNamesTheRivalAndItsResult(double? result, string chip)
    {
        Run(() =>
        {
            var texts = Texts(LocalGameCards.BuildHumanGameRow(Match(1, result)));
            Assert.Contains("Contra Nathan", texts);
            Assert.Contains(chip, texts);
            Assert.Contains(texts, t => t.StartsWith("ESOC Arizona", StringComparison.Ordinal));
        });
    }

    /// <summary>
    /// A recording somebody sent: the viewer has no result in it, and "no result" would be false
    /// about a file that names the loser.
    /// </summary>
    [Fact]
    public void AnotherPlayersRecordingSaysWhoLostAndClaimsNoResult()
    {
        Run(() =>
        {
            var texts = Texts(LocalGameCards.BuildHumanGameRow(Match(-1, null, loser: 1, winner: 2)));
            Assert.DoesNotContain("SIN RESULTADO", texts);
            Assert.Contains(texts, t => t.Contains("Perdió: Jeison", StringComparison.Ordinal));
            Assert.Contains(texts, t => t.Contains("Ganó: Nathan", StringComparison.Ordinal));
        });
    }

    /// <summary>The game wipes the totals of older games, so zero is printed as absence.</summary>
    [Fact]
    public void AGameWithoutTotalsSaysSoInsteadOfPrintingZeros()
    {
        Run(() =>
        {
            var game = new AiGameRecord { DurationMs = 1680000, Won = false };
            var texts = Texts(LocalGameCards.BuildAiGameRow(game, new Dictionary<string, string>()));
            Assert.Contains("DERROTA", texts);
            Assert.Contains(texts, t => t.Contains("puntuación no registrada", StringComparison.Ordinal));
            Assert.Contains("Recursos no registrados por el juego.", texts);
            Assert.DoesNotContain(texts, t => t.Contains("Oro", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void RecordedTotalsAreListed()
    {
        Run(() =>
        {
            var game = new AiGameRecord { DurationMs = 60000, Won = true, Score = 18240, Gold = 12400, Shipments = 31 };
            var texts = Texts(LocalGameCards.BuildAiGameRow(game, new Dictionary<string, string>()));
            Assert.Contains("VICTORIA", texts);
            Assert.Contains(texts, t => t.Contains("Oro", StringComparison.Ordinal) && t.Contains("31", StringComparison.Ordinal));
        });
    }
}
