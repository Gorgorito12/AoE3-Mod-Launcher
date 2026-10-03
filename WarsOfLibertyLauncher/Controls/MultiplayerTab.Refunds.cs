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

    /// <summary>
    /// The mode's card, with the banner over it when that ladder has a refund not yet dismissed.
    /// The card itself is unchanged — the banner sits above it, as 55n draws — so everything that
    /// finds the card by its tag still finds it.
    /// </summary>
    private FrameworkElement WithRefundBanner(Border card, bool team)
    {
        var refunds = RefundView.Unseen(_cachedStanding?.Refunds, team);
        if (refunds.Count == 0) return card;

        var stack = new StackPanel();
        stack.Children.Add(BuildRefundBanner(refunds, preview: _renderingPreviewProfile));
        stack.Children.Add(card);
        return stack;
    }

    private Border BuildRefundBanner(IReadOnlyList<RefundNotice> refunds, bool preview)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var glyph = new TextBlock
        {
            Text = "↺",
            FontSize = (double)Application.Current.FindResource("MpRefundGlyphSize"),
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.FindResource("MpOk"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        Grid.SetColumn(glyph, 0);
        grid.Children.Add(glyph);

        var text = new TextBlock
        {
            Text = RefundView.Text(refunds),
            FontSize = (double)Application.Current.FindResource("MpBodySize"),
            Foreground = (Brush)Application.Current.FindResource("MpRefundText"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var ok = new Button
        {
            Content = Strings.Get("MpRefundDismiss"),
            Style = (Style)Application.Current.FindResource("SetActionButton"),
            Width = double.NaN,
            Height = 28,
            Padding = new Thickness(12, 0, 12, 0),
            FontSize = (double)Application.Current.FindResource("MpMetaSize"),
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ok.Click += (_, _) => DismissRefunds(refunds, preview);
        Grid.SetColumn(ok, 2);
        grid.Children.Add(ok);

        return new Border
        {
            Child = grid,
            Tag = RefundBannerTag,
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(14, 11, 14, 11),
            CornerRadius = (CornerRadius)Application.Current.FindResource("RadiusPanel"),
            Background = (Brush)Application.Current.FindResource("MpRefundBg"),
            BorderBrush = (Brush)Application.Current.FindResource("MpRefundRim"),
            BorderThickness = new Thickness(1),
        };
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
