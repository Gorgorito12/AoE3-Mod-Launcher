using System.Windows;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;

namespace WarsOfLibertyLauncher;

/// <summary>
/// The quick match's pill in the title bar (phase 0, docs/DISENO-PARTIDA-RAPIDA.md): while a search
/// runs it says so from every tab — "Searching 1v1 · 02:13" — leads back to the room it waits in,
/// and its cross stops it. The search itself lives in <c>MultiplayerTab.QuickMatch.cs</c>; this
/// side only draws what that side hands it through the <c>setQuickMatchPill</c> callback.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Shows the pill with <paramref name="text"/>, or hides it when null. May be called from any
    /// thread: the search's clock ticks on the dispatcher, but the callback promises nothing.
    /// </summary>
    private void SetQuickMatchPill(string? text, string? tooltip)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => SetQuickMatchPill(text, tooltip));
            return;
        }
        if (QuickMatchPill == null) return;
        if (string.IsNullOrEmpty(text))
        {
            QuickMatchPill.Visibility = Visibility.Collapsed;
            return;
        }
        QuickMatchPillButton.Content = text;
        QuickMatchPillButton.ToolTip = string.IsNullOrEmpty(tooltip) ? null : TooltipHelper.Wrap(tooltip);
        QuickMatchPillCancel.ToolTip = TooltipHelper.Wrap(Strings.Get("QmPillCancelTip"));
        QuickMatchPill.Visibility = Visibility.Visible;
    }

    /// <summary>Back to the room the search waits in.</summary>
    private void QuickMatchPill_Click(object sender, RoutedEventArgs e)
    {
        BringToForeground();
        SwitchTopTab(TopTab.Multiplayer);
        MultiplayerView?.ShowQuickMatchRoom();
    }

    /// <summary>Stops the search, closing the room it was waiting in.</summary>
    private void QuickMatchPillCancel_Click(object sender, RoutedEventArgs e)
        => _ = MultiplayerView?.CancelQuickMatchAsync(Strings.Get("QmCancelled"));
}
