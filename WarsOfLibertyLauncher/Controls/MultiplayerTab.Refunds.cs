using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// Points given back after a ban (design 55n). The bell says it once — raised when the standing
/// arrives, deduped by refund id in <see cref="NotificationCenter"/> — and the profile keeps a
/// banner over the affected mode's card until the player presses "Got it", which tells the server
/// through <c>POST /matches/refunds/seen</c>. Refunds come only in the player's OWN standing, so
/// nobody else's profile can carry one; and nothing here names the banned opponent.
/// </summary>
public partial class MultiplayerTab
{
    /// <summary>The <c>Tag</c> of the refund banner, for the tests.</summary>
    internal const string RefundBannerTag = "RefundBanner";

    /// <summary>Raises the bell for a refund: set by <see cref="Attach"/>.</summary>
    private Action<RefundNotice>? _onRatingRefund;

    /// <summary>Every refund the player has not dismissed goes to the bell; the bell dedups.</summary>
    private void AnnounceRefunds(EloSnapshot standing)
    {
        if (_onRatingRefund == null || standing.Refunds == null) return;
        foreach (var refund in standing.Refunds.Where(r => !r.Seen && !string.IsNullOrEmpty(r.RefundId)))
        {
            try { _onRatingRefund(refund); }
            catch (Exception ex) { DiagnosticLog.Write($"MultiplayerTab: refund bell: {ex.Message}"); }
        }
    }

    /// <summary>The notice at the top of the Summary until it is closed (55n).</summary>
    private FrameworkElement? BuildRefundNotice()
    {
        var all = new List<RefundNotice>();
        foreach (var team in new[] { false, true }) all.AddRange(RefundView.Unseen(_cachedStanding?.Refunds, team));
        return all.Count == 0 ? null : BuildRefundBanner(all, preview: _renderingPreviewProfile);
    }

    private Border BuildRefundBanner(IReadOnlyList<RefundNotice> refunds, bool preview)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var glyph = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(9),
            Margin = new Thickness(0, 0, 12, 0),
            Child = new TextBlock
            {
                Text = "\u21BA",
                FontSize = (double)Application.Current.FindResource("MpRefundGlyphSize"),
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)Application.Current.FindResource("MpOk"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        glyph.SetResourceReference(Border.BackgroundProperty, "ProfileGainFill");
        grid.Children.Add(glyph);

        var text = new TextBlock
        {
            Text = RefundView.Text(refunds),
            FontSize = (double)Application.Current.FindResource("StatsBodySize"),
            Foreground = (Brush)Application.Current.FindResource("StatsFigure"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        // What came back, and to which ladder.
        int points = refunds.Sum(r => r.Points);
        var worth = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var figure = StatsMono("+" + points, "ProfileRefundFigureSize", "StatsWin", FontWeights.SemiBold);
        figure.HorizontalAlignment = HorizontalAlignment.Right;
        worth.Children.Add(figure);
        var ladder = StatsText("ELO " + ModeName(refunds[0].IsTeam), "StatsDeckCountSize",
                               "StatsProse", FontWeights.Normal);
        ladder.HorizontalAlignment = HorizontalAlignment.Right;
        ladder.Margin = new Thickness(0, 4, 0, 0);
        worth.Children.Add(ladder);
        Grid.SetColumn(worth, 2);
        grid.Children.Add(worth);

        var close = new Button
        {
            Content = "\u2715",
            Width = 28,
            Height = 28,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = TooltipHelper.Wrap(Strings.Get("MpRefundDismiss")),
        };
        close.SetResourceReference(FrameworkElement.StyleProperty, "MpIconButton");
        close.Click += (_, _) => DismissRefunds(refunds, preview);
        System.Windows.Automation.AutomationProperties.SetName(close, Strings.Get("MpRefundDismiss"));
        Grid.SetColumn(close, 3);
        grid.Children.Add(close);

        var banner = new Border
        {
            Child = grid,
            Tag = RefundBannerTag,
            Margin = new Thickness(0, 0, 0, 14),
            Padding = new Thickness(12, 10, 10, 10),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
        };
        banner.SetResourceReference(Border.BackgroundProperty, "MpRefundBg");
        banner.SetResourceReference(Border.BorderBrushProperty, "MpRefundRim");
        return banner;
    }
    /// <summary>
    /// "Got it": the banner goes at once — the refunds are marked on the objects the banner was
    /// built from, so this works the same for the sample profile — and the server is told, except
    /// in the preview, where nothing came from a server. A failed request is logged and the banner
    /// stays gone for this session; the next standing would bring it back, which is the honest
    /// outcome of a "seen" the server never stored.
    /// </summary>
    private void DismissRefunds(IReadOnlyList<RefundNotice> refunds, bool preview)
    {
        foreach (var r in refunds) r.Seen = true;
        RenderProfileTab();
        if (preview || _session == null) return;

        var ids = refunds.Select(r => r.RefundId).Where(id => !string.IsNullOrEmpty(id)).ToList();
        _ = MarkRefundsSeenAsync(ids);
    }

    private async System.Threading.Tasks.Task MarkRefundsSeenAsync(List<string> ids)
    {
        try
        {
            if (_session != null) await _session.Api.MarkRefundsSeenAsync(ids);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"MultiplayerTab: refunds seen: {ex.Message}");
        }
    }
}
