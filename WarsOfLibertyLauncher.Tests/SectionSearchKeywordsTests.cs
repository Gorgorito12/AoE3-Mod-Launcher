using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// <c>SectionSearch.Keywords</c> (design handoff 50): how a button whose caption is a plain
/// string — which the search never reads — becomes findable, in either language.
///
/// <para><b>Why this exists.</b> Players could not find "Share diagnostics" by searching for it.
/// Two independent reasons, and both are pinned here: the caption is a plain string, so even
/// "Share diagnostics" found nothing; and somebody typing <c>diagnostico</c> into an English UI
/// was looking for a word that is not inside "diagnostics". The REJECTION and the clearing cases
/// matter as much as the hits — a keyword that made everything match would pass every
/// "finds it" assertion.</para>
/// </summary>
[Collection("wpf-and-language")]
public class SectionSearchKeywordsTests
{
    /// <summary>
    /// The shape that failed: a card with no settings rows, holding a header and a button whose
    /// caption is a string. The card is the panel's direct child, so the search matches it whole.
    /// </summary>
    private static (StackPanel Panel, Border Card, Button Share) BuildPanel()
    {
        var share = new Button { Content = Strings.Get("ModPropShareDiagnostics") };
        SectionSearch.SetKeywords(share, SectionSearch.KeywordsFor("ModPropShareDiagnostics", "SearchKwShareDiagnostics"));
        var card = new Border
        {
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = Strings.Get("ModPropTroubleTitle") },
                    share,
                },
            },
        };
        var unrelated = new Border { Child = new TextBlock { Text = "Change mod folder" } };
        var panel = new StackPanel();
        panel.Children.Add(card);
        panel.Children.Add(unrelated);
        return (panel, card, share);
    }

    private static void Run(Action body)
    {
        var error = StaTestThread.Run(() =>
        {
            TestApplication.Ensure();
            var was = Strings.Language;
            try { body(); }
            finally { Strings.SetLanguage(was); }
        }, TimeSpan.FromSeconds(30));
        Assert.Null(error);
    }

    /// <summary>
    /// THE ONE THAT MATTERS: every word the handoff names finds the button, with the UI in
    /// English AND in Spanish — the language somebody types in need not be the launcher's.
    /// </summary>
    [Theory]
    [InlineData("en", "diagnostico")]
    [InlineData("en", "diagnóstico")]
    [InlineData("en", "reporte")]
    [InlineData("en", "crash")]
    [InlineData("es", "diagnostico")]
    [InlineData("es", "diagnóstico")]
    [InlineData("es", "reporte")]
    [InlineData("es", "crash")]
    public void THE_ONE_THAT_MATTERS_TheKeywordsFindTheButtonInEitherLanguage(string ui, string query)
    {
        Run(() =>
        {
            Strings.SetLanguage(ui);
            var (panel, card, _) = BuildPanel();

            Assert.True(SectionSearch.FilterPanel(panel, query), $"\"{query}\" found nothing with the UI in {ui}");
            Assert.Equal(Visibility.Visible, card.Visibility);
            Assert.Equal(Visibility.Collapsed, panel.Children[1].Visibility);
        });
    }

    /// <summary>
    /// The caption alone was invisible to the search before — a string Content is never read.
    /// Its own name, in either language, now finds it whatever the UI is set to.
    /// </summary>
    [Theory]
    [InlineData("en", "Share diagnostics")]
    [InlineData("en", "Compartir diagnóstico")]
    [InlineData("es", "Share diagnostics")]
    [InlineData("es", "compartir")]
    public void ItsOwnNameFindsItInEitherLanguage(string ui, string query)
    {
        Run(() =>
        {
            Strings.SetLanguage(ui);
            var (panel, card, _) = BuildPanel();
            Assert.True(SectionSearch.FilterPanel(panel, query), $"\"{query}\" found nothing with the UI in {ui}");
            Assert.Equal(Visibility.Visible, card.Visibility);
        });
    }

    /// <summary>A word in neither list must not match: keywords widen the text, not the rule.</summary>
    [Fact]
    public void SomethingElseHidesIt()
    {
        Run(() =>
        {
            Strings.SetLanguage("es");
            var (panel, card, _) = BuildPanel();
            Assert.False(SectionSearch.FilterPanel(panel, "qqqzzz"));
            Assert.Equal(Visibility.Collapsed, card.Visibility);
        });
    }

    /// <summary>Clearing the search puts the card back exactly as it was.</summary>
    [Fact]
    public void ClearingRestoresTheCard()
    {
        Run(() =>
        {
            var (panel, card, _) = BuildPanel();
            var sections = new[] { new SectionSearch.Section(panel, () => { }) };
            SectionSearch.Apply("qqqzzz", sections);
            Assert.Equal(Visibility.Collapsed, card.Visibility);
            SectionSearch.Restore(sections);
            Assert.Equal(Visibility.Visible, card.Visibility);
        });
    }

    /// <summary>
    /// Both languages, whatever the UI says: the helper is the one place that loads them, so it
    /// is what has to be right.
    /// </summary>
    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public void KeywordsForLoadsBothLanguages(string ui)
    {
        Run(() =>
        {
            Strings.SetLanguage(ui);
            var words = SectionSearch.KeywordsFor("SearchKwShareDiagnostics");
            Assert.Contains("crash", words);
            Assert.Contains("reporte", words);
        });
    }

    /// <summary>
    /// What the results list prints after the path: the keyword that matched, found with or
    /// without the accent. Null when nothing matched, never a guess.
    /// </summary>
    [Theory]
    [InlineData("diagnostics diagnose report", "diagnose", "diagnose")]
    [InlineData("diagnóstico informe reporte", "diagnostico", "diagnóstico")]
    [InlineData("diagnóstico informe reporte", "REPORTE", "reporte")]
    [InlineData("logs log error", "qqq", null)]
    [InlineData("", "diagnostico", null)]
    [InlineData("logs log", "", null)]
    public void MatchingKeywordNamesTheWordThatMatched(string keywords, string query, string? expected)
    {
        var was = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("es-419");
            Assert.Equal(expected, SectionSearch.MatchingKeyword(keywords, query));
        }
        finally
        {
            CultureInfo.CurrentCulture = was;
        }
    }
}
