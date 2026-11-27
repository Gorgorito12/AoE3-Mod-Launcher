using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher;

/// <summary>
/// New medals (design 121b/121d): the bell always, and one notice — the card in the middle of the
/// window, or the small floating one — as <see cref="AchievementCelebration.Decide"/> says. The
/// multiplayer tab reports them (<see cref="MultiplayerTab.AchievementsUnlocked"/>); this decides
/// where they show.
/// </summary>
public partial class MainWindow
{
    private Action? _closeCelebration;

    private void OnAchievementsUnlocked(AchievementUnlockNotice notice)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnAchievementsUnlocked(notice)));
            return;
        }
        if (notice.Medals.Count == 0) return;
        try
        {
            string modName = ModName(notice.ModId);
            var (name, _, _) = AchievementsView.Announce(notice.Medals[0]);
            var keys = notice.Medals.Select(m => $"{notice.ModId}|{m.Id}|{m.Tier}".ToLowerInvariant()).ToList();
            string body = notice.Medals.Count == 1
                ? Strings.Format("NotifAchievementBody", name, modName)
                : Strings.Format("NotifAchievementsBody", notice.Medals.Count, modName);
            _notifications?.RaiseAchievements(notice.ModId, keys, Strings.Get("NotifAchievementTitle"), body);

            bool looking = IsVisible && WindowState != WindowState.Minimized && IsActive;
            bool gameRunning = _isGameRunning || (MultiplayerView?.IsGameRunning ?? false);
            var surface = AchievementCelebration.Decide(notice.Medals.Count, looking,
                MultiplayerView?.IsInRoomOrSearching ?? false, gameRunning);
            DiagnosticLog.Write($"Achievements: {notice.Medals.Count} medal(s) in {notice.ModId} shown as {surface}.");
            ShowCelebration(notice, surface);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Achievements: could not show the celebration: {ex.Message}");
        }
    }

    private static string ModName(string modId)
        => ModRegistry.Find(modId)?.DisplayName ?? modId;

    private void ShowCelebration(AchievementUnlockNotice notice, AchievementCelebration.Surface surface, bool preview = false)
    {
        var first = notice.Medals[0];
        var (name, line, age) = AchievementsView.Announce(first);
        var look = new Medal.Look(first.Icon, first.Enamel, true, first.Metal, age);
        void SeeMedals() => MultiplayerView?.ShowAchievements(notice.ModId);

        switch (surface)
        {
            case AchievementCelebration.Surface.Card:
            {
                _closeCelebration?.Invoke();
                var dot = age is { } a ? Medal.Hex(RankAgeColors.Of(a).Light) : Medal.Hex(Medal.MetalOf(first.Metal).Main);
                var model = new AchievementUnlockOverlay.Model(look, name, line,
                    AchievementsView.MedalLine(first, notice.Earned, notice.Total, ModName(notice.ModId)), dot);
                bool reduced = RankBadge.ReducedEffects || !SystemParameters.ClientAreaAnimation;
                // A preview stays as long as the other previews, so there is time to look at it.
                _closeCelebration = AchievementUnlockOverlay.Show(CelebrationHost, model, SeeMedals, reduced,
                    preview ? TimeSpan.FromMilliseconds(PreviewDismissMs) : null);
                break;
            }
            case AchievementCelebration.Surface.Notice:
            {
                bool several = notice.Medals.Count > 1;
                ShowAppToast(new AppToast.ToastOptions(
                    AppToast.ToastTone.Gold,
                    AppToast.ToastIcon.Check,
                    several ? Strings.Format("AchvSeveralTitle", notice.Medals.Count) : name,
                    several
                        ? string.Join(" · ", notice.Medals.Select(m => AchievementsView.Announce(m).Name))
                        : line,
                    new[] { new AppToast.ToastAction(Strings.Get("AchvView"), AppToast.ToastButton.Gold, SeeMedals) },
                    AutoDismissMs: preview ? PreviewDismissMs : 12_000,
                    Subtitle: Strings.Get("NotifAchievementTitle"),
                    Cause: ToastCause.Answer,
                    Medal: look));
                break;
            }
            default:
                break;
        }
    }

    /// <summary>
    /// <c>--preview-dialog=achievement:card|notice|several|platinum</c>: the celebration with a sample
    /// medal (Giant Slayer, gold, the design's example; or Grandmaster, a platinum feat), touching
    /// neither the bell nor the server.
    /// </summary>
    internal void PreviewAchievement(string? variant)
    {
        var medal = new MedalRef
        {
            Id = "giant_slayer", Tier = 0, Metal = "g", Icon = "climb", Enamel = 1,
            Name = "Giant Slayer", Condition = "Beat someone with 200 more ELO", EarnedAt = DateTime.UtcNow.ToString("o"),
        };
        var second = new MedalRef
        {
            Id = "unstoppable", Tier = 0, Metal = "s", Icon = "flame", Enamel = 1,
            Name = "Unstoppable", Condition = "Win 5 matches in a row", EarnedAt = DateTime.UtcNow.ToString("o"),
        };
        var platinum = new MedalRef
        {
            Id = "grandmaster", Tier = 0, Metal = "p", Icon = "climb", Enamel = 7,
            Name = "Grandmaster", Condition = "Reach 1900 ELO in 1v1", EarnedAt = DateTime.UtcNow.ToString("o"),
        };
        var notice = variant switch
        {
            "several" => new AchievementUnlockNotice("wol", new[] { medal, second }, 16, 40),
            "platinum" => new AchievementUnlockNotice("wol", new[] { platinum }, 52, 72),
            _ => new AchievementUnlockNotice("wol", new[] { medal }, 15, 40),
        };
        var surface = variant is "notice" or "several"
            ? AchievementCelebration.Surface.Notice
            : AchievementCelebration.Surface.Card;
        if (surface == AchievementCelebration.Surface.Card || IsActive)
        {
            ShowCelebration(notice, surface, preview: true);
            return;
        }
        // The small notice goes through the toast routing, which sends it to the desktop while
        // nobody is looking at the launcher: wait for the window to come to the front (15 s at
        // most), as --preview-toasts does, or a test launch never sees it in the window.
        bool shown = false;
        EventHandler? onActive = null;
        void Fire()
        {
            if (shown) return;
            shown = true;
            Activated -= onActive;
            ShowCelebration(notice, surface, preview: true);
        }
        onActive = (_, _) => Fire();
        Activated += onActive;
        var fallback = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        fallback.Tick += (_, _) => { fallback.Stop(); Fire(); };
        fallback.Start();
    }
}
