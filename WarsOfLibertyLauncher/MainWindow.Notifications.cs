using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>The bell's panel (design 70a): open it, filter it, and act on one row.</summary>
public partial class MainWindow
{
    private int _newRoomCount;

    private void InitializeNotificationPanel()
    {
        NotificationPanelView.IconFor = item =>
        {
            if (!NotificationView.IsAboutAMod(item.Kind)) return null;
            var profile = ModRegistry.Find(item.ModId);
            return profile == null ? null : TryLoadTileImage(profile.ResolveIconSource());
        };
        NotificationPanelView.Opened += (_, item) => OpenNotification(item);
        NotificationPanelView.ActionPressed += (_, item) => RunNotificationAction(item);
        NotificationPanelView.Dismissed += (_, item) => _notifications.Remove(item);
        NotificationPanelView.MarkAllReadRequested += (_, _) => _notifications.MarkAllRead();
        NotificationPanelView.ClearReadRequested += (_, _) => _notifications.ClearRead();
        NotificationPanelView.ChooseRequested += (_, _) =>
        {
            SetNotificationPanelOpen(false);
            LauncherSettingsButton_Click(this, new RoutedEventArgs());
        };
        Deactivated += (_, _) => SetNotificationPanelOpen(false);
    }

    private void SetNotificationPanelOpen(bool open)
    {
        if (open) NotificationPanelView.Show(_notifications.Items);
        NotificationHost.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        NotificationBellButton.Tag = open ? "open" : null;
        RefreshBellColor();
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
}
