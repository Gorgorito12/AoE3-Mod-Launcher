using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The community block's FACTS (designs 60 and 61): the month's highlights and the community's
/// figures, on the block's header line beside its title, each «LABEL value» on ONE line. They
/// replaced two strips with similar data in two styles (the highlights under the list and a line of
/// figures beside the panel's title); 61 moved them up from a strip of their own, where each was a
/// label over a value and the strip wrapped — two 80-px lines on a laptop.
///
/// <para><b>Which facts</b> is <see cref="ActivityFactsView"/>'s decision: most matches and best
/// streak of the month, matches, players, the most played map — plus the biggest climb when the
/// month has one. The four highlights added later (most wins, best win rate, biggest upset,
/// civilization of the month) live in Ranking › Highlights, in depth.</para>
///
/// <para><b>They never wrap</b>: the facts sit in a <see cref="FitRowPanel"/>, which shows the ones
/// that fit whole and drops the rest FROM THE END — Most played first, then Players, then Matches
/// (61). Only a name trims, at 200 px.</para>
/// </summary>
public partial class MultiplayerTab
{
    /// <summary>Last month's highlights are on screen ("See September" pressed).</summary>
    private bool _highlightsShowPrevious;

    /// <summary>The <c>Tag</c> of each fact, for the tests.</summary>
    internal const string ActivityFactTag = "ActivityFact";

    /// <summary>
    /// Build the facts from <c>_communityStats</c> into the header line's <c>ActivityFacts</c>, and
    /// set the month link. Returns how many facts were built (the line may show fewer).
    /// </summary>
    private int RenderActivityFacts()
    {
        if (ActivityFacts == null) return 0;

        var all = _communityStats?.MonthlyHighlights;
        var current = all?.Current;
        var previous = all?.Previous;
        var showingPrevious = _highlightsShowPrevious && HighlightsView.HasCells(previous);
        var month = showingPrevious ? previous : current;

        var facts = ActivityFactsView.Build(
            month, CommunityStatsView.Totals(_communityStats), Strings.Culture,
            (key, args) => Strings.Format(key, args));

        var fluid = CurrentActivityFluid;
        ActivityFacts.Children.Clear();
        foreach (var fact in facts) ActivityFacts.Children.Add(BuildActivityFact(fact, fluid));

        // "See September" — the way to last month's highlights, on the header line (61), only when
        // last month has some; "Back to October" while it is on screen. The community figures do
        // not change with it.
        MonthHighlights? other = showingPrevious ? current : HighlightsView.HasCells(previous) ? previous : null;
        if (other != null)
        {
            var otherName = HighlightsView.MonthName(other.Month, Strings.Culture) ?? other.Month;
            ActivityFactsMonthLink.Content = Strings.Format(showingPrevious ? "MpHlSeeCurrent" : "MpHlSeePrev", otherName);
            ActivityFactsMonthLink.Visibility = Visibility.Visible;
        }
        else
        {
            ActivityFactsMonthLink.Visibility = Visibility.Collapsed;
        }
        return facts.Count;
    }

    /// <summary>
    /// One fact on one line (61): its label in capitals, then its value — a name and a figure, a
    /// name and the 🔥 pill, or a figure alone — with a 1-px rule on its left. The fact stretches to
    /// the header line's height, so every rule runs the full line.
    /// </summary>
    private static Border BuildActivityFact(ActivityFact fact, ActivityFluid fluid)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(new TextBlock
        {
            Text = fact.Label.ToUpper(Strings.Culture),
            Foreground = (Brush)Application.Current.FindResource("MpTextLabel"),
            FontWeight = FontWeights.SemiBold,
            FontSize = fluid.FactLabelSize,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
        });

        const double gap = 6;
        if (!string.IsNullOrWhiteSpace(fact.Name))
        {
            line.Children.Add(new TextBlock
            {
                Text = fact.Name,
                MaxWidth = 200,
                Margin = new Thickness(gap, 0, 0, 0),
                Foreground = (Brush)Application.Current.FindResource("MpTextHeading"),
                FontWeight = FontWeights.SemiBold,
                FontSize = fluid.FactValueSize,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        if (fact.Streak is int wins)
        {
            var pill = BuildStreakPill(wins, table: true);
            pill.Margin = new Thickness(gap, 0, 0, 0);
            pill.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(pill);
        }
        else if (!string.IsNullOrWhiteSpace(fact.Figure))
        {
            line.Children.Add(new TextBlock
            {
                Text = fact.Figure,
                FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
                FontWeight = FontWeights.Bold,
                FontSize = fluid.FactValueSize,
                Foreground = (Brush)Application.Current.FindResource(
                    fact.Kind == ActivityFactKind.TopClimb ? "MpOkText" : "MpTextHeading"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(gap, 0, 0, 0),
            });
        }

        return new Border
        {
            Tag = ActivityFactTag,
            Child = line,
            Padding = new Thickness(12, 0, 12, 0),
            BorderBrush = (Brush)Application.Current.FindResource("MpRimMedium"),
            BorderThickness = new Thickness(1, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Stretch,
        };
    }

    /// <summary>"See September" / "Back to October": the month of the highlights facts only.</summary>
    private void ActivityFactsMonthLink_Click(object sender, RoutedEventArgs e)
    {
        _highlightsShowPrevious = !_highlightsShowPrevious;
        RenderActivityFacts();
        QueueActivityLayout();
    }
}
