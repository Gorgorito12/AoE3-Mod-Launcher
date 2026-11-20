using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>The bell's panel (designs 70a and 120): open it, place it under the bell, filter it,
/// and act on one row.</summary>
public partial class MainWindow
{
    private int _newRoomCount;

    /// <summary>Sample notices from <c>--preview-dialog=bell:…</c>; null in ordinary use.</summary>
    private List<NotificationItem>? _bellPreview;

    private IEnumerable<NotificationItem> BellPanelItems => _bellPreview ?? (IEnumerable<NotificationItem>)_notifications.Items;

    private void InitializeNotificationPanel()
    {
        NotificationPanelView.IconFor = item =>
        {
            // The launcher's own notices wear the launcher's icon, as a mod's wear the mod's.
            if (NotificationView.IsAboutTheLauncher(item.Kind))
                return TryLoadTileImage("pack://application:,,,/AppIcon.png");
            if (!NotificationView.IsAboutAMod(item.Kind)) return null;
            var profile = ModRegistry.Find(item.ModId);
            return profile == null ? null : TryLoadTileImage(profile.ResolveIconSource());
        };
        NotificationPanelView.Opened += (_, item) =>
        {
            if (_bellPreview != null) SetNotificationPanelOpen(false);
            else OpenNotification(item);
        };
        NotificationPanelView.ActionPressed += (_, item) =>
        {
            if (_bellPreview != null) SetNotificationPanelOpen(false);
            else RunNotificationAction(item);
        };
        NotificationPanelView.Dismissed += (_, item) =>
        {
            if (_bellPreview != null)
            {
                _bellPreview.Remove(item);
                NotificationPanelView.Show(_bellPreview);
            }
            else _notifications.Remove(item);
        };
        NotificationPanelView.MarkAllReadRequested += (_, _) =>
        {
            if (_bellPreview != null)
            {
                foreach (var item in _bellPreview) item.Read = true;
                NotificationPanelView.Show(_bellPreview);
            }
            else _notifications.MarkAllRead();
        };
        NotificationPanelView.ClearReadRequested += (_, _) =>
        {
            if (_bellPreview != null)
            {
                _bellPreview.RemoveAll(i => i.Read);
                NotificationPanelView.Show(_bellPreview);
            }
            else _notifications.ClearRead();
        };
        NotificationPanelView.ChooseRequested += (_, _) =>
        {
            SetNotificationPanelOpen(false);
            LauncherSettingsButton_Click(this, new RoutedEventArgs());
        };
        Deactivated += (_, _) => SetNotificationPanelOpen(false);
    }

    private void SetNotificationPanelOpen(bool open)
    {
        bool was = NotificationHost.Visibility == Visibility.Visible;
        if (open) NotificationPanelView.Show(BellPanelItems);
        else _bellPreview = null;
        NotificationHost.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        NotificationBellButton.Tag = open ? "open" : null;
        RefreshBellColor();

        // Only while it is open: the bell moves when the update pill appears or the window is
        // resized, and the panel has to follow it.
        if (open && !was) NotificationHost.LayoutUpdated += NotificationHost_LayoutUpdated;
        else if (!open && was) NotificationHost.LayoutUpdated -= NotificationHost_LayoutUpdated;
        if (open) PlaceNotificationPanel();
    }

    private void NotificationHost_LayoutUpdated(object? sender, EventArgs e) => PlaceNotificationPanel();

    /// <summary>
    /// The panel hangs from the bell (design 120): its right edge is the bell's, measured in the
    /// window, and it may grow to twelve from the bottom. Writes only when a value changed — this
    /// runs on every layout pass while the panel is open, and none of what it writes can move the
    /// bell, so it settles in one pass.
    /// </summary>
    private void PlaceNotificationPanel()
    {
        if (NotificationHost.Visibility != Visibility.Visible) return;
        double width = NotificationHost.ActualWidth, height = NotificationHost.ActualHeight;
        if (width <= 0 || height <= 0 || !NotificationBellButton.IsVisible) return;

        double bellLeft, bellRight;
        try
        {
            var toHost = NotificationBellButton.TransformToVisual(NotificationHost);
            bellLeft = toHost.Transform(new Point(0, 0)).X;
            bellRight = toHost.Transform(new Point(NotificationBellButton.ActualWidth, 0)).X;
        }
        catch (InvalidOperationException)
        {
            return; // Not in one tree yet: the next layout pass asks again.
        }

        double right = NotificationPlacement.RightMargin(width, bellRight);
        var margin = new Thickness(0, NotificationPlacement.Drop, right, 0);
        if (NotificationPanelView.Margin != margin) NotificationPanelView.Margin = margin;

        double most = NotificationPlacement.MaxHeight(height);
        if (Math.Abs(NotificationPanelView.MaxHeight - most) > .5) NotificationPanelView.MaxHeight = most;

        double caretRight = NotificationPlacement.CaretRightMargin(width, bellLeft, bellRight);
        var caretMargin = new Thickness(0, NotificationPlacement.Drop - NotificationPlacement.CaretHeight, caretRight, 0);
        if (NotificationCaret.Margin != caretMargin) NotificationCaret.Margin = caretMargin;
        var caret = NotificationPlacement.CaretFits(right, caretRight) ? Visibility.Visible : Visibility.Hidden;
        if (NotificationCaret.Visibility != caret) NotificationCaret.Visibility = caret;
    }

    private void NotificationVeil_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => SetNotificationPanelOpen(false);

    private void OpenNotification(NotificationItem item)
    {
        SetNotificationPanelOpen(false);
        _notifications.MarkRead(item);
        NavigateToNotification(item);
    }

    /// <summary>The row's button does what the launcher itself would: Update and Play press the
    /// mod's primary button once that mod is the displayed one.</summary>
    private void RunNotificationAction(NotificationItem item)
    {
        OpenNotification(item);
        var profile = ModRegistry.Find(item.ModId);
        if (profile == null) return;
        switch (NotificationView.ActionOf(item.Kind))
        {
            case NotificationAction.Update:
            case NotificationAction.Play:
                WithLibraryMod(profile, () => PlayButton_Click(this, new RoutedEventArgs()));
                break;
        }
    }

    /// <summary>
    /// <c>--preview-dialog=bell:b|c|d</c>: the sheet's three panels (two launcher notices, eight
    /// mixed, none) with nothing written to the notification history. Its buttons only act on the
    /// samples.
    /// </summary>
    private void PreviewBell(string variant)
    {
        bool en = Strings.Language == Strings.LangEn;
        var now = DateTime.UtcNow;
        var yesterday = DateTime.Now.Date.AddDays(-1).AddHours(21).AddMinutes(36).ToUniversalTime();
        NotificationItem Sample(NotificationKind kind, string mod, string title, string body, DateTime at, bool read = false, string? target = null)
            => new() { Kind = kind, ModId = mod, Title = title, Body = body, CreatedAtUtc = at, Read = read, TargetId = target, IsPreview = true };

        var news = Sample(NotificationKind.Announcement, "",
            en ? "v1.1.0: a new Library, quick match and a redrawn multiplayer" : "v1.1.0: Biblioteca nueva, partida rápida y multijugador redibujado",
            en ? "The Library and the Workshop are now one tab, built around each mod's cover. Quick match finds you a 1v1 rival from the Rooms page."
               : "La Biblioteca y el Workshop son ahora una sola pestaña, armada alrededor de la portada de cada mod. La partida rápida te busca rival 1v1 desde Salas.",
            yesterday);
        var launcher = Sample(NotificationKind.LauncherUpdate, "",
            Strings.Get("NotifLauncherUpdateTitle"), Strings.Format("NotifLauncherUpdateBody", "v1.1.0"), yesterday);

        var items = new List<NotificationItem>();
        switch (variant.ToLowerInvariant())
        {
            case "d":
                break;
            case "c":
                items.Add(Sample(NotificationKind.UpdateAvailable, "wol",
                    en ? "Update available" : "Actualización disponible",
                    en ? "Wars of Liberty 1.2.0f is available to download." : "Wars of Liberty 1.2.0f está disponible para descargar.",
                    now.AddMinutes(-12)));
                items.Add(Sample(NotificationKind.Connectivity, "",
                    Strings.Get("NotifOfflineTitle"), Strings.Get("NotifOfflineBody"), now.AddHours(-1), read: true, target: "offline"));
                items.Add(news);
                items.Add(launcher);
                items.Add(Sample(NotificationKind.NewTranslation, "wol",
                    en ? "New translation" : "Nueva traducción",
                    en ? "A new translation is available for Wars of Liberty: Español." : "Hay una nueva traducción para Wars of Liberty: Español.",
                    yesterday.AddHours(-3), read: true));
                items.Add(Sample(NotificationKind.NewMod, "hundred-days",
                    en ? "New mod available" : "Nuevo mod disponible",
                    en ? "Hundred Days was just added to the Workshop." : "Hundred Days se acaba de añadir al Workshop.",
                    now.AddDays(-3), read: true));
                items.Add(Sample(NotificationKind.UpdateFinished, "wol",
                    en ? "Update complete" : "Actualización completada",
                    en ? "Wars of Liberty was updated to 1.2.0e." : "Wars of Liberty se actualizó a 1.2.0e.",
                    now.AddDays(-4), read: true));
                items.Add(Sample(NotificationKind.Installed, "improvement-mod",
                    en ? "Installation complete" : "Instalación completada",
                    en ? "Improvement Mod was installed (1.4)." : "Improvement Mod se instaló (1.4).",
                    now.AddDays(-5), read: true));
                break;
            default:
                items.Add(news);
                items.Add(launcher);
                break;
        }
        _bellPreview = items;
        // Straight to the host: SetNotificationPanelOpen(false) is what forgets the samples.
        if (NotificationHost.Visibility == Visibility.Visible) NotificationPanelView.Show(items);
        else SetNotificationPanelOpen(true);
    }
}
