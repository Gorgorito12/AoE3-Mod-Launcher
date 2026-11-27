using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The profile's Achievements (design 121a/121c, docs/design_logros): a showcase over the game's
/// cover — a ring with the total, the game's picker, the three latest — and every medal of that
/// game as a tile, by group, four to a row. The same medals, three of them small, under the name
/// on the card.
///
/// <para>Everything is the SERVER's: which medals a game has, what each asks, who earned what. This
/// draws what arrives (<see cref="AchievementsView"/> decides how a tile reads) and reports new ones
/// to the window, which celebrates them (<see cref="AchievementsUnlocked"/>). A server without
/// achievements answers 404 and the section is simply not there.</para>
/// </summary>
public partial class MultiplayerTab
{
    private readonly AchievementsCache _achv = new();

    /// <summary>The game chosen in the showcase; null = the Library's.</summary>
    private string? _achvModPick;

    /// <summary>The sample medals the profile preview draws (--demo-elo=profile:achievements).</summary>
    private AchievementsResponse? _achvPreview;

    /// <summary>Who the cache was filled for: a different signed-in player starts it afresh.</summary>
    private string? _achvSessionUser;

    /// <summary>Whether this session has asked once for medals earned while the launcher was closed.</summary>
    private bool _achvUnseenChecked;

    /// <summary>"mod|id|tier" of the medals already handed to the window this session, so a push and
    /// a read bringing the same medal celebrate it once.</summary>
    private readonly HashSet<string> _achvCelebrated = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>New medals of the signed-in player, for the window to celebrate and put in the bell.</summary>
    public event Action<AchievementUnlockNotice>? AchievementsUnlocked;

    /// <summary>In a room or looking for a match: a medal's card would cover what he is waiting for.</summary>
    public bool IsInRoomOrSearching => (_session?.IsInLobby ?? false) || IsQuickMatchSearching;

    /// <summary>Whether the section exists: the server has achievements, or the preview brings its own.</summary>
    private bool AchievementsShown => _renderingPreviewProfile ? _achvPreview != null : !_achv.Unsupported;

    /// <summary>The game the section reads: the one picked, or the Library's.</summary>
    private string AchvModId() => _achvModPick ?? ProfileDeckMod()?.Id ?? "";

    /// <summary>
    /// What the profile being drawn has for its game, asking the server when a read is due. Null
    /// while nothing has arrived.
    /// </summary>
    private AchievementsResponse? AchvForProfile(bool fetch)
    {
        if (_renderingPreviewProfile) return _achvPreview;
        ForgetAchievementsOfAnotherSession();
        var subject = ProfileViewerId;
        var mod = AchvModId();
        if (subject == null || mod.Length == 0 || _achv.Unsupported) return null;
        var entry = _achv.Get(subject, mod);
        bool minimized = _profileWindow?.WindowState == WindowState.Minimized;
        if (fetch && !minimized && _session != null && !ConnectivityState.IsOffline
            && _achv.NeedsFetch(entry, DateTime.UtcNow))
        {
            _ = LoadAchievementsAsync(subject, mod, self: !_renderingOtherProfile);
        }
        return entry.Response;
    }

    private AchievementsCache.Entry? AchvEntryForProfile()
    {
        var subject = ProfileViewerId;
        var mod = AchvModId();
        return subject == null || mod.Length == 0 ? null : _achv.Get(subject, mod);
    }

    private void ForgetAchievementsOfAnotherSession()
    {
        var me = _session?.CurrentUser?.Id;
        if (string.Equals(me, _achvSessionUser, StringComparison.Ordinal)) return;
        _achvSessionUser = me;
        _achv.Clear();
        _achvCelebrated.Clear();
        _achvUnseenChecked = false;
    }

    private async Task LoadAchievementsAsync(string userId, string modId, bool self)
    {
        var session = _session;
        if (session == null) return;
        var entry = _achv.Get(userId, modId);
        entry.Loading = true;
        entry.Stale = false;
        entry.AskedUtc = DateTime.UtcNow;
        try
        {
            var r = await session.Api.GetAchievementsAsync(userId, modId);
            entry.Response = r;
            entry.Failed = false;
            if (self) CelebrateUnseen(r);
        }
        catch (LobbyApiException ex) when (ex.Status == 404)
        {
            // No achievements on this server: the section goes, for the session.
            _achv.Unsupported = true;
            DiagnosticLog.Write("Achievements: the server has none (404); the profile section is hidden.");
        }
        catch (Exception ex)
        {
            entry.Failed = true;
            DiagnosticLog.Write($"Achievements of {userId} in {modId} not read: {ex.Message}");
        }
        finally
        {
            entry.Loading = false;
        }

        if (_profileWindow != null || ProfileBodyOverride != null) RenderProfileTab();
    }

    /// <summary>
    /// Once a session, signed in: ask the server whether medals were earned while this launcher was
    /// closed (they come back as "unseen"). The push covers everything earned while it is open.
    /// </summary>
    private void MaybeCheckUnseenAchievements()
    {
        if (_achvUnseenChecked || _session?.Status != MultiplayerSession.SessionStatus.SignedIn) return;
        ForgetAchievementsOfAnotherSession();
        var me = _session.CurrentUser?.Id;
        var mod = ProfileDeckMod()?.Id;
        if (me == null || string.IsNullOrEmpty(mod) || _achv.Unsupported || ConnectivityState.IsOffline) return;
        _achvUnseenChecked = true;
        _ = LoadAchievementsAsync(me, mod!, self: true);
    }

    /// <summary>The medals the server says this launcher has not celebrated yet, every game.</summary>
    private void CelebrateUnseen(AchievementsResponse r)
    {
        if (r.Unseen is not { Count: > 0 } unseen) return;
        foreach (var group in unseen.GroupBy(m => (m.Mod ?? r.Mod).ToLowerInvariant()))
        {
            var count = r.Mods?.FirstOrDefault(m => string.Equals(m.Mod, group.Key, StringComparison.OrdinalIgnoreCase));
            int earned = count?.Earned ?? (string.Equals(group.Key, r.Mod, StringComparison.OrdinalIgnoreCase) ? r.Earned ?? 0 : 0);
            int total = count?.Total ?? r.Total;
            Celebrate(group.Key, group.ToList(), earned, total);
        }
    }

    /// <summary>
    /// The server's push: medals earned just now. Every game of the player is read again, and the
    /// window celebrates them.
    /// </summary>
    private void HandleAchievementUnlockedFrame(JsonElement json)
    {
        try
        {
            var frame = json.Deserialize<AchievementUnlockedFrame>();
            if (frame == null || frame.Items.Count == 0) return;
            if (_session?.CurrentUser?.Id is { } me) _achv.MarkStale(me);
            Celebrate(frame.Mod, frame.Items, frame.Earned, frame.Total);
            if (_profileWindow != null && _profileOther == null && !_eloPreview) RenderProfileTab();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Achievements: push not read: {ex.Message}");
        }
    }

    /// <summary>Hand new medals to the window once, and tell the server they were shown.</summary>
    private void Celebrate(string modId, IReadOnlyList<MedalRef> medals, int earned, int total)
    {
        var fresh = medals.Where(m => _achvCelebrated.Add($"{modId}|{m.Id}|{m.Tier}")).ToList();
        if (fresh.Count == 0) return;
        DiagnosticLog.Write($"Achievements: {fresh.Count} new in {modId} ({string.Join(", ", fresh.Select(m => m.Tier > 0 ? $"{m.Id}#{m.Tier}" : m.Id))}).");
        try { AchievementsUnlocked?.Invoke(new AchievementUnlockNotice(modId, fresh, earned, total)); }
        catch (Exception ex) { DiagnosticLog.Write($"Achievements: celebration failed: {ex.Message}"); }

        var session = _session;
        if (session == null) return;
        var seen = fresh.Select(m => new AchievementSeenItem { Mod = modId, Id = m.Id, Tier = m.Tier }).ToList();
        _ = Task.Run(async () =>
        {
            try { await session.Api.MarkAchievementsSeenAsync(seen); }
            catch (Exception ex) { DiagnosticLog.Write($"Achievements: not marked seen: {ex.Message}"); }
        });
    }

    /// <summary>The player's own profile on its medals, of this game when one is named (the bell).</summary>
    public void ShowAchievements(string? modId)
    {
        _achvModPick = string.IsNullOrWhiteSpace(modId) ? null : modId;
        _profileSection = ProfileSection.Achievements;
        if (_profileOther != null)
        {
            OpenProfileOf(_session?.CurrentUser?.Id, null);
            _profileSection = ProfileSection.Achievements;
        }
        if (_profileWindow != null)
        {
            RenderProfileTab();
            _profileWindow.Activate();
            return;
        }
        OpenProfileWindowCore();
    }

    // ---------------------------------------------------------------- the card

    /// <summary>"13/40" beside the section's entry, once the game's medals have arrived.</summary>
    private string AchvRailCount()
    {
        var r = AchvForProfile(fetch: false);
        return r == null || r.IsHidden ? "" : $"{r.Earned ?? 0}/{r.Total}";
    }

    /// <summary>The three latest medals, small, under the name on the card (design 121a).</summary>
    private FrameworkElement? BuildProfileLatestMedals()
    {
        if (!AchievementsShown) return null;
        var r = AchvForProfile(fetch: true);
        if (r?.Latest is not { Count: > 0 } latest || r.IsHidden) return null;
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 14, 0, 0),
            Tag = "ProfileLatestMedals",
        };
        foreach (var m in latest.Take(3))
        {
            var (name, line, age) = AchievementsView.Announce(m);
            var medal = Medal.Build(new Medal.Look(m.Icon, m.Enamel, true, m.Metal, age), 34);
            var holder = new Border
            {
                Child = medal,
                Margin = new Thickness(row.Children.Count == 0 ? 0 : 9, 0, 0, 0),
                Background = Brushes.Transparent,
                ToolTip = TooltipHelper.Wrap($"{name}\n{line}"),
            };
            row.Children.Add(holder);
        }
        return row;
    }

    // ---------------------------------------------------------------- the section

    /// <summary>The Achievements section: the showcase, then every group of the game.</summary>
    private void RenderProfileAchievements(Panel body)
    {
        var r = AchvForProfile(fetch: true);
        if (r == null)
        {
            var entry = _renderingPreviewProfile ? null : AchvEntryForProfile();
            if (entry?.Failed == true)
            {
                var stack = new StackPanel();
                stack.Children.Add(ProfileNote(Strings.Get("AchvFailed")));
                var retry = new Button
                {
                    Content = Strings.Get("AchvRetry"),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 10, 0, 0),
                    Padding = new Thickness(12, 4, 12, 4),
                };
                retry.SetResourceReference(FrameworkElement.StyleProperty, "RmRowButton");
                retry.Click += (_, _) =>
                {
                    entry.Failed = false;
                    entry.AskedUtc = DateTime.MinValue;
                    RenderProfileTab();
                };
                stack.Children.Add(retry);
                body.Children.Add(ProfileGlass(stack, new Thickness(18, 16, 18, 16)));
            }
            else body.Children.Add(ProfileGlass(ProfileNote(Strings.Get("AchvLoading")), new Thickness(18, 16, 18, 16)));
            return;
        }

        if (r.IsHidden)
        {
            body.Children.Add(ProfileGlass(ProfileNote(Strings.Get("AchvPrivateThem")), new Thickness(18, 16, 18, 16)));
            return;
        }

        body.Children.Add(BuildAchvShowcase(r));
        foreach (var group in r.Groups ?? new List<AchievementGroup>())
            body.Children.Add(BuildAchvGroup(group));
    }

    /// <summary>How many tiles stand in a row: four, as the design draws them, fewer when narrow.</summary>
    internal static int AchvColumns(double contentWidth)
        => Math.Clamp((int)((contentWidth + 10) / 180), 2, 4);

    /// <summary>
    /// The showcase over the game's cover (design 121a, top): a ring with the total, the game's
    /// picker, the title and the count, the metals' legend, and the three latest.
    /// </summary>
    private FrameworkElement BuildAchvShowcase(AchievementsResponse r)
    {
        var mod = ModRegistry.Find(r.Mod) ?? ModRegistry.Find(AchvModId());
        var layers = new Grid { ClipToBounds = true };

        // The cover, blurred and darkened (the prototype's blur 14 + brightness .5). A picture still
        // downloading is not frozen, so it belongs to the thread that asked for it first: another
        // thread (a second test, in practice) gets the plain fill rather than an exception.
        if (mod?.ResolveHeroSources().FirstOrDefault() is { } hero
            && MainWindow.TryLoadTileImage(hero, MainWindow.StripDecodeWidth) is { } coverBrush
            && coverBrush.CheckAccess()
            && coverBrush.ImageSource is { } source && source.CheckAccess())
        {
            var blurred = MainWindow.BuildBlurredCover(source);
            layers.Children.Add(new Border { Background = (Brush?)blurred ?? new ImageBrush(source) { Stretch = Stretch.UniformToFill } });
            layers.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)) });
        }
        else
        {
            layers.Children.Add(new Border { Background = new SolidColorBrush(Medal.Hex("#13233A")) });
        }
        var goldGlow = new RadialGradientBrush
        {
            Center = new Point(0.12, 0.5),
            GradientOrigin = new Point(0.12, 0.5),
            RadiusX = 0.5,
            RadiusY = 1.2,
        };
        goldGlow.GradientStops.Add(new GradientStop(Color.FromArgb(0x4D, 0xE6, 0xC0, 0x6A), 0));
        goldGlow.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xE6, 0xC0, 0x6A), 0.7));
        var shade = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        shade.GradientStops.Add(new GradientStop(Color.FromArgb(0xB8, 15, 28, 46), 0));
        shade.GradientStops.Add(new GradientStop(Color.FromArgb(0x80, 15, 28, 46), 0.5));
        shade.GradientStops.Add(new GradientStop(Color.FromArgb(0xC7, 15, 28, 46), 1));
        layers.Children.Add(new Border { Background = shade });
        layers.Children.Add(new Border { Background = goldGlow });

        bool together = ProfileContentWidth >= 860;
        var content = new Grid { Margin = new Thickness(22, 20, 22, 20) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (!together)
        {
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        content.Children.Add(BuildAchvRing(r));

        var words = new StackPanel { Margin = new Thickness(20, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(BuildAchvGamePicker(r, mod));
        var title = new TextBlock
        {
            Text = Strings.Get("AchvSection"),
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 8, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Effect = null,
        };
        title.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        title.SetResourceReference(TextBlock.FontSizeProperty, "ProfileTitleSize");
        words.Children.Add(title);
        var count = StatsText(AchievementsView.CountLine(r), "StatsProseSize", "StatsSoft", FontWeights.Normal);
        count.Margin = new Thickness(0, 6, 0, 0);
        count.ToolTip = TooltipHelper.Wrap(Strings.Get("AchvPerGameTip"));
        words.Children.Add(count);
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var (metal, label) in AchievementsView.Legend(r))
        {
            legend.Children.Add(new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = new SolidColorBrush(Medal.Hex(Medal.MetalOf(metal).Main)),
                Margin = new Thickness(legend.Children.Count == 0 ? 0 : 12, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            legend.Children.Add(StatsText(label, "StatsLabelSize", "StatsSoft", FontWeights.Normal));
        }
        words.Children.Add(legend);
        content.Children.Add(WithColumn(words, 1));

        if (r.Latest is { Count: > 0 } latest)
        {
            var side = BuildAchvLatest(latest);
            if (together) content.Children.Add(WithColumn(side, 2));
            else
            {
                side.HorizontalAlignment = HorizontalAlignment.Left;
                side.Margin = new Thickness(0, 18, 0, 0);
                Grid.SetRow(side, 1);
                Grid.SetColumnSpan(side, 3);
                content.Children.Add(side);
            }
        }
        layers.Children.Add(content);

        var card = new Border
        {
            Child = layers,
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x57, 0xE6, 0xC0, 0x6A)),
            Margin = new Thickness(0, 0, 0, 18),
            Tag = "AchvShowcase",
        };
        // Round corners for the cover: a Border's corner radius does not clip its child.
        layers.SizeChanged += (_, e) => layers.Clip = new RectangleGeometry(
            new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 15, 15);
        return card;
    }

    /// <summary>The ring with the total: gold up to the share held, the count in its middle.</summary>
    private static FrameworkElement BuildAchvRing(AchievementsResponse r)
    {
        const double size = 104;
        var grid = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(new Ellipse
        {
            Fill = new SolidColorBrush(Color.FromArgb(0x4D, 0xE6, 0xC0, 0x6A)),
            Effect = new BlurEffect { Radius = 20 },
            CacheMode = new BitmapCache(),
            IsHitTestVisible = false,
        });
        grid.Children.Add(new Ellipse { Fill = new SolidColorBrush(Color.FromArgb(0x24, 255, 255, 255)) });
        double share = AchievementsView.TotalFraction(r);
        if (share > 0)
        {
            Shape arc;
            if (share >= 0.999) arc = new Ellipse { Fill = new SolidColorBrush(Medal.Hex("#F3D995")) };
            else
            {
                double rad = size / 2;
                double angle = share * 2 * Math.PI;
                var figure = new PathFigure { StartPoint = new Point(rad, rad), IsClosed = true };
                figure.Segments.Add(new LineSegment(new Point(rad, 0), true));
                figure.Segments.Add(new ArcSegment(new Point(rad + rad * Math.Sin(angle), rad - rad * Math.Cos(angle)),
                    new Size(rad, rad), 0, share > 0.5, SweepDirection.Clockwise, true));
                arc = new Path { Data = new PathGeometry { Figures = { figure } }, Fill = new SolidColorBrush(Medal.Hex("#F3D995")) };
            }
            grid.Children.Add(arc);
        }
        grid.Children.Add(new Ellipse { Margin = new Thickness(8), Fill = new SolidColorBrush(Medal.Hex("#101D30")) });

        var middle = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var held = new TextBlock
        {
            Text = (r.Earned ?? 0).ToString(),
            FontFamily = new FontFamily("Consolas"),
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        held.SetResourceReference(TextBlock.FontSizeProperty, "ProfileTitleSize");
        middle.Children.Add(held);
        var of = StatsText(Strings.Format("AchvOfTotal", r.Total), "StatsLabelSize", "AccentBrush", FontWeights.SemiBold);
        of.Foreground = new SolidColorBrush(Medal.Hex("#F3D995"));
        of.HorizontalAlignment = HorizontalAlignment.Center;
        of.Margin = new Thickness(0, 2, 0, 0);
        middle.Children.Add(of);
        grid.Children.Add(middle);
        return grid;
    }

    /// <summary>"[icon] WARS OF LIBERTY ▾": the games to see medals of.</summary>
    private FrameworkElement BuildAchvGamePicker(AchievementsResponse r, ModProfile? current)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 8, 0),
            Background = current != null ? ResolveRoomModIcon(current) : null,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var gold = new SolidColorBrush(Medal.Hex("#F3D995"));
        var name = StatsText(ResolveModDisplayName(r.Mod).ToUpper(Strings.Culture), "StatsLabelSize", "AccentBrush", FontWeights.Bold);
        name.Foreground = gold;
        name.VerticalAlignment = VerticalAlignment.Center;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        row.Children.Add(name);
        row.Children.Add(new Path
        {
            Data = Geometry.Parse("M0,0 L4,4 L8,0"),
            Stroke = gold,
            StrokeThickness = 1.6,
            Width = 8,
            Height = 5,
            Margin = new Thickness(8, 1, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var button = new Button
        {
            Content = row,
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = TooltipHelper.Wrap(Strings.Get("AchvChooseGame")),
        };
        button.Template = BareButtonTemplate();
        System.Windows.Automation.AutomationProperties.SetAutomationId(button, "ProfileAchievements:Game");
        System.Windows.Automation.AutomationProperties.SetName(button, ResolveModDisplayName(r.Mod));
        button.Click += (_, _) =>
        {
            var mods = StatsModOptions();
            foreach (var m in r.Mods ?? new List<AchievementModCount>())
            {
                if (ModRegistry.Find(m.Mod) is { } p && !mods.Any(x => string.Equals(x.Id, p.Id, StringComparison.OrdinalIgnoreCase)))
                    mods.Add(p);
            }
            var popup = ModSearchPopup.Build(button, mods, _ => true, current,
                p => ResolveRoomModIcon(p),
                p => ModTheme.AccentOf(p.AccentColor, _config?.UseModColor ?? true),
                picked =>
                {
                    if (string.Equals(AchvModId(), picked.Id, StringComparison.OrdinalIgnoreCase)) return;
                    _achvModPick = picked.Id;
                    RenderProfileTab();
                });
            if (popup.Child is Border { Child: FrameworkElement body }) body.Width = Math.Max(body.Width, 300);
            ChromePopups.Track(popup, button);
            popup.IsOpen = true;
        };
        return button;
    }

    /// <summary>A button that is only its content: the picker draws its own chip.</summary>
    private static ControlTemplate BareButtonTemplate()
    {
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.AppendChild(presenter);
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    /// <summary>"LATEST" and the three most recent medals, each with its name and date.</summary>
    private static FrameworkElement BuildAchvLatest(IReadOnlyList<MedalRef> latest)
    {
        var side = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        var label = StatsText(Strings.Get("AchvLatest"), "StatsHeaderSize", "AccentBrush", FontWeights.Bold);
        label.Foreground = new SolidColorBrush(Medal.Hex("#F3D995"));
        label.HorizontalAlignment = HorizontalAlignment.Right;
        side.Children.Add(label);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        foreach (var m in latest.Take(3))
        {
            var (name, line, age) = AchievementsView.Announce(m);
            var cell = new StackPanel
            {
                Width = 112,
                Margin = new Thickness(row.Children.Count == 0 ? 0 : 18, 0, 0, 0),
                ToolTip = TooltipHelper.Wrap(line),
                Background = Brushes.Transparent,
            };
            var medal = Medal.Build(new Medal.Look(m.Icon, m.Enamel, true, m.Metal, age), 66);
            medal.HorizontalAlignment = HorizontalAlignment.Center;
            cell.Children.Add(medal);
            var title = StatsText(name, "StatsRowNameSizeCompact", "StatsStrong", FontWeights.SemiBold);
            title.Foreground = Brushes.White;
            title.TextAlignment = TextAlignment.Center;
            title.TextWrapping = TextWrapping.Wrap;
            title.HorizontalAlignment = HorizontalAlignment.Center;
            title.Margin = new Thickness(0, 7, 0, 0);
            cell.Children.Add(title);
            var when = StatsText(AchievementsView.ShortDate(m.EarnedAt), "StatsLabelSize", "StatsSoft", FontWeights.Normal);
            when.HorizontalAlignment = HorizontalAlignment.Center;
            when.Margin = new Thickness(0, 3, 0, 0);
            cell.Children.Add(when);
            row.Children.Add(cell);
        }
        side.Children.Add(row);
        return side;
    }

    /// <summary>One group: its heading with the colour and the count, then its tiles.</summary>
    private FrameworkElement BuildAchvGroup(AchievementGroup group)
    {
        var enamel = Medal.Enamels[Math.Clamp(group.Enamel, 0, Medal.Enamels.Length - 1)];
        var light = Medal.Hex(enamel[0]);
        var block = new StackPanel { Margin = new Thickness(0, 0, 0, 18), Tag = "AchvGroup:" + group.Id };

        var head = new Grid { Margin = new Thickness(2, 0, 0, 9) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.Children.Add(new Border
        {
            Width = 10,
            Height = 10,
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(light),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var title = StatsText(AchievementsView.GroupTitle(group), "StatsHeaderSize", "StatsStrong", FontWeights.Bold);
        title.Margin = new Thickness(10, 0, 0, 0);
        title.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(WithColumn(title, 1));
        var count = StatsMono(AchievementsView.GroupCount(group), "StatsHeaderSize", "StatsNote", FontWeights.SemiBold);
        count.Margin = new Thickness(10, 0, 0, 0);
        count.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(WithColumn(count, 2));
        var rule = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        rule.GradientStops.Add(new GradientStop(Color.FromArgb(0x88, light.R, light.G, light.B), 0));
        rule.GradientStops.Add(new GradientStop(Color.FromArgb(0, light.R, light.G, light.B), 1));
        head.Children.Add(WithColumn(new Border
        {
            Height = 1,
            Background = rule,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        }, 3));
        block.Children.Add(head);

        var tiles = new UniformGrid { Columns = AchvColumns(ProfileContentWidth), Margin = new Thickness(-5, 0, -5, 0) };
        foreach (var item in group.Items)
            tiles.Children.Add(BuildAchvTile(AchievementsView.Tile(item, group.Enamel)));
        block.Children.Add(tiles);
        return block;
    }

    /// <summary>One medal as a tile (design 121a): the coin, its name, its line, and the pill at its foot.</summary>
    internal static FrameworkElement BuildAchvTile(MedalTile t)
    {
        bool earned = t.State == MedalTileState.Earned;
        var enamel = Medal.Enamels[Math.Clamp(t.Enamel, 0, Medal.Enamels.Length - 1)];
        var c0 = Medal.Hex(enamel[0]);
        var c1 = Medal.Hex(enamel[1]);

        var dock = new DockPanel { LastChildFill = false };

        var top = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var medal = Medal.Build(new Medal.Look(t.Icon, t.Enamel, earned, t.Metal, t.Age,
            t.State == MedalTileState.Progress ? t.Progress : -1), 68);
        medal.HorizontalAlignment = HorizontalAlignment.Center;
        top.Children.Add(medal);

        var name = new TextBlock
        {
            Text = t.Name,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 9, 0, 0),
            Foreground = new SolidColorBrush(Medal.Hex(earned ? "#F0F5FB" : "#C3D2E5")),
        };
        name.SetResourceReference(TextBlock.FontSizeProperty, "StatsRowNameSize");
        top.Children.Add(name);

        if (t.Pips.Count > 0)
        {
            var pips = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
            foreach (var age in t.Pips)
            {
                pips.Children.Add(new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Margin = new Thickness(pips.Children.Count == 0 ? 0 : 4, 0, 0, 0),
                    Fill = age is { } a
                        ? new SolidColorBrush(Medal.Hex(RankAgeColors.Of(a).Light))
                        : new SolidColorBrush(Color.FromArgb(0x29, 255, 255, 255)),
                });
            }
            top.Children.Add(pips);
        }

        var line = new TextBlock
        {
            Text = t.Line,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 5, 0, 0),
            MinHeight = 32,
            Foreground = new SolidColorBrush(Medal.Hex(earned ? "#C3D2E5" : "#A8BCD2")),
        };
        line.SetResourceReference(TextBlock.FontSizeProperty, "StatsLabelSize");
        line.MaxHeight = 3 * 16.5;
        top.Children.Add(line);
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);

        var footText = new TextBlock
        {
            Text = t.Foot,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Medal.Hex(earned ? "#F3D995" : t.State == MedalTileState.Progress ? "#CFE2F7" : "#8C9CB1")),
            VerticalAlignment = VerticalAlignment.Center,
        };
        footText.SetResourceReference(TextBlock.FontSizeProperty, "StatsLabelSize");
        if (t.State == MedalTileState.Progress) footText.FontFamily = new FontFamily("Consolas");
        var foot = new Border
        {
            Height = 22,
            Padding = new Thickness(10, 0, 10, 0),
            CornerRadius = new CornerRadius(11),
            Margin = new Thickness(0, 9, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = earned
                ? new SolidColorBrush(Color.FromArgb(0x24, 0xE6, 0xC0, 0x6A))
                : new SolidColorBrush(Color.FromArgb(0x0F, 255, 255, 255)),
            Child = footText,
        };
        DockPanel.SetDock(foot, Dock.Bottom);
        dock.Children.Add(foot);

        Brush background;
        if (earned)
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0x3D, c1.R, c1.G, c1.B), 0));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0x0A, 255, 255, 255), 0.62));
            background = b;
        }
        else background = new SolidColorBrush(Color.FromArgb(0x06, 255, 255, 255));

        var tile = new Border
        {
            Child = dock,
            Padding = new Thickness(12, 16, 12, 13),
            Margin = new Thickness(5),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Background = background,
            BorderBrush = earned
                ? new SolidColorBrush(Color.FromArgb(0x66, c0.R, c0.G, c0.B))
                : new SolidColorBrush(Color.FromArgb(0x0F, 255, 255, 255)),
            ToolTip = TooltipHelper.Wrap(t.Tooltip),
            Tag = "AchvTile:" + t.Id,
        };
        System.Windows.Automation.AutomationProperties.SetName(tile, t.Name);
        return tile;
    }
}
