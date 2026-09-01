using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="RevealText"/>'s decisions — and this class is mostly REFUSALS, because
/// that is where the whole risk of the feature is.
///
/// <para>It is armed by an implicit style on every trimming TextBlock in the launcher, so it
/// runs over roughly 65 places and is not opted into anywhere. A rule that reveals too
/// eagerly puts a balloon over text that was perfectly readable; a rule that ignores who
/// already owns the element COVERS a sentence somebody wrote with a repeat of what is already
/// on screen. The showing cases are one test; the not-showing cases are the rest.</para>
/// </summary>
public class RevealTextTests
{
    // ------------------------------------------------------------------ shape

    /// <summary>The ordinary case: a block that trims and stays on one line.</summary>
    [Fact]
    public void ASingleLineBlockThatTrimsQualifies()
    {
        Assert.True(RevealText.ShapeAllows(TextTrimming.CharacterEllipsis, TextWrapping.NoWrap));
        Assert.True(RevealText.ShapeAllows(TextTrimming.WordEllipsis, TextWrapping.NoWrap));
    }

    /// <summary>
    /// A block that does not trim has nothing hidden to reveal — which is the overwhelming
    /// majority of the TextBlocks in the launcher, and the reason the implicit style is free.
    /// </summary>
    [Fact]
    public void ABlockThatDoesNotTrimIsLeftAlone()
    {
        Assert.False(RevealText.ShapeAllows(TextTrimming.None, TextWrapping.NoWrap));
        Assert.False(RevealText.ShapeAllows(TextTrimming.None, TextWrapping.Wrap));
    }

    /// <summary>
    /// A WRAPPING block is out of scope, and this is a limit rather than an oversight: such a
    /// block is cut by HEIGHT — it ran out of lines — and no width measurement can see that,
    /// so revealing on width would either miss the cut ones or fire on blocks that fit.
    ///
    /// <para>The rooms table's room name is exactly this case (two line boxes, then an
    /// ellipsis) and keeps a hand-written tooltip of its own for it.</para>
    /// </summary>
    [Fact]
    public void AWrappingBlockIsOutOfScope()
    {
        Assert.False(RevealText.ShapeAllows(TextTrimming.CharacterEllipsis, TextWrapping.Wrap));
        Assert.False(RevealText.ShapeAllows(TextTrimming.CharacterEllipsis, TextWrapping.WrapWithOverflow));
    }

    // ------------------------------------------------------------------ overflow

    /// <summary>Text wider than its cell is cut, and that is the case worth revealing.</summary>
    [Fact]
    public void TextWiderThanItsCellIsCut()
    {
        Assert.True(RevealText.Overflows(contentWidth: 260, availableWidth: 180));
    }

    /// <summary>
    /// TEXT THAT FITS REVEALS NOTHING. Showing the same words back, in a box, over the words
    /// themselves, is worse than doing nothing at all — and with the behaviour armed on every
    /// trimming block it would happen constantly, since most of them fit most of the time.
    /// </summary>
    [Fact]
    public void TextThatFitsRevealsNothing()
    {
        Assert.False(RevealText.Overflows(contentWidth: 120, availableWidth: 180));
        Assert.False(RevealText.Overflows(contentWidth: 180, availableWidth: 180));
    }

    /// <summary>
    /// The slack, at its boundary. A string that fills its cell EXACTLY lands a fraction of a
    /// pixel over it often enough to matter — WPF's layout rounds, and the measurement here is
    /// taken with FormattedText rather than by the same code that arranged the line — so a
    /// hair of overflow is not a cut.
    /// </summary>
    [Fact]
    public void AHairOfOverflowIsNotACut()
    {
        Assert.False(RevealText.Overflows(180 + RevealText.OverflowSlack, 180));
        Assert.True(RevealText.Overflows(180 + RevealText.OverflowSlack + 0.01, 180));
    }

    // ------------------------------------------------------------------ the words

    /// <summary>
    /// A BLOCK BUILT FROM RUNS STILL KNOWS ITS OWN WORDS, and this is the trap that cost a
    /// round: <c>TextBlock.Text</c> reports only content assigned THROUGH that property and
    /// answers the empty string for a block whose content was added as <c>Run</c>s.
    ///
    /// <para>That is not an exotic shape here — it is how <c>BuildEmphasisRuns</c> writes the
    /// community strip's totals line, which is the exact line the reveal was reported for. An
    /// emptiness guard written on <c>tb.Text</c> refuses it while looking perfectly correct.
    /// </para>
    /// </summary>
    [Fact]
    public void ABlockBuiltFromRunsStillKnowsItsOwnWords()
    {
        RunSta(() =>
        {
            var built = new TextBlock();
            built.Inlines.Add(new Run("Mapa mas jugado: "));
            built.Inlines.Add(new Run("ESOC Fertile Crescent") { FontWeight = FontWeights.SemiBold });

            // The property everybody reaches for, and what it actually says.
            Assert.Equal("", built.Text);
            Assert.Equal("Mapa mas jugado: ESOC Fertile Crescent", RevealText.PlainTextOf(built));

            // And the simple case still goes through it unchanged.
            Assert.Equal("una sala", RevealText.PlainTextOf(new TextBlock { Text = "una sala" }));
        });
    }

    // ------------------------------------------------------------------ ownership

    /// <summary>
    /// A block nobody else explains is ours to explain.
    /// </summary>
    [Fact]
    public void PlainTextInAPlainPanelIsFree()
    {
        RunSta(() =>
        {
            var text = new TextBlock { Text = "ESOC Fertile Crescent" };
            var panel = new StackPanel();
            panel.Children.Add(text);
            var card = new Border { Child = panel };

            Assert.NotNull(card);
            Assert.False(RevealText.AlreadyExplained(text));
        });
    }

    /// <summary>
    /// THE REFUSAL THAT KEEPS THIS FROM COSTING INFORMATION. When an ancestor already carries a
    /// tooltip, that tooltip wins and we add nothing — a tooltip on the child would sit in
    /// front of it, and WPF resolves tooltips UPWARD from whatever the pointer is over, so the
    /// ancestor's would simply never be seen again.
    ///
    /// <para>Real cases, all of them with the trimmed text as a descendant: the rooms table's
    /// PLAYERS cell (tooltip on the StackPanel), the end-of-match stat cards (on the Border),
    /// and every gear-menu item (a two-line explanation). Each would have been traded for a
    /// repeat of what is already on screen.</para>
    /// </summary>
    [Fact]
    public void AnAncestorThatAlreadyExplainsItselfWins()
    {
        RunSta(() =>
        {
            var text = new TextBlock { Text = "ESOC Fertile Crescent" };
            var cell = new StackPanel { ToolTip = "Los jugadores de esta sala" };
            cell.Children.Add(text);

            Assert.True(RevealText.AlreadyExplained(text));

            // And from further up: the walk does not stop at the first parent.
            var deeper = new TextBlock { Text = "ESOC Fertile Crescent" };
            var inner = new StackPanel();
            inner.Children.Add(deeper);
            var outer = new Border { Child = inner, ToolTip = "El resultado de la partida" };

            Assert.NotNull(outer);
            Assert.True(RevealText.AlreadyExplained(deeper));
        });
    }

    /// <summary>
    /// The element's OWN tooltip is not an ancestor's, so it is not this method's business —
    /// the caller checks it separately. Stated because the two conditions read alike and
    /// collapsing them would make the walk answer a question about the wrong element.
    /// </summary>
    [Fact]
    public void ItsOwnTooltipIsNotAnAncestorsTooltip()
    {
        RunSta(() =>
        {
            var text = new TextBlock { Text = "…", ToolTip = "mine" };
            var panel = new StackPanel();
            panel.Children.Add(text);

            Assert.False(RevealText.AlreadyExplained(text));
        });
    }

    /// <summary>An orphan block has no ancestors and must not throw walking none.</summary>
    [Fact]
    public void AnOrphanIsNotExplainedByAnybody()
    {
        RunSta(() =>
        {
            Assert.False(RevealText.AlreadyExplained(new TextBlock { Text = "x" }));
            Assert.False(RevealText.AlreadyExplained(null));
        });
    }

    // ------------------------------------------------------------------ harness

    /// <summary>
    /// WPF elements carry thread affinity, so they are built on an STA thread of this test's
    /// own. Nothing here is measured or rendered — the walks above are over the object graph —
    /// but constructing them anywhere else is asking for trouble later.
    /// </summary>
    private static void RunSta(Action body)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { captured = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "the STA thread did not finish");
        if (captured != null) throw captured;
    }
}
