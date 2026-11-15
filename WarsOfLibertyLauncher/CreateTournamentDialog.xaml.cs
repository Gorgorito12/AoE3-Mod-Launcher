using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Asks for the shape of a new tournament (design 81, laid out again by design 108).
///
/// <para>Unlike <c>CreateLobbyDialog</c> it does NOT perform the POST itself: creating a tournament
/// can be refused for reasons the caller already knows how to explain, and a copy of that handling
/// here would be a second one to keep in step.</para>
///
/// <para>Everything this collects is a REQUEST. The server clamps the capacity, decides whether the
/// mod is ranked and echoes back what it actually made; the launcher holds no copy of those rules.</para>
///
/// <para><b>No line of help here may contradict the selection.</b> Every explanation is worked out
/// again from what is chosen, in one method.</para>
/// </summary>
public partial class CreateTournamentDialog : Window
{
    private const int MinNameLength = 3;
    private const int MaxNameLength = 80;

    /// <summary>What an older server takes when it does not say: it clips anything above in silence.</summary>
    private const int DefaultMaxCapacity = 16;

    private static readonly int[] AllCapacities = { 2, 4, 8, 16, 32, 64 };

    /// <summary>The places worth offering. Counted in ENTRANTS: eight places of 3v3 is twenty-four
    /// people. 32 and 64 appear only for a server that says it takes them.</summary>
    internal static IReadOnlyList<int> CapacitiesFor(int? serverMax)
        => AllCapacities.Where(n => n <= (serverMax ?? DefaultMaxCapacity)).ToList();

    public string EnteredName { get; private set; } = "";
    public string Format { get; private set; } = "1v1";
    public string TeamSource { get; private set; } = "solo";
    public string EntryMode { get; private set; } = "open";

    /// <summary>"public" or "private". Named so because <c>Visibility</c> is the window's own.</summary>
    public string TournamentVisibility { get; private set; } = "public";
    public int Capacity { get; private set; } = 16;

    /// <summary>Whether to open registration right after creating it. Otherwise it stays a draft.</summary>
    public bool OpenRegistration { get; private set; } = true;

    /// <summary>
    /// The mod this tournament will be played on, or null when the caller named none. It decides
    /// more than anything else here: every room the bracket opens is created with it, and a player
    /// whose active mod is not this one is turned away when they come to play their match.
    /// </summary>
    public ModProfile? SelectedMod { get; private set; }

    private readonly IReadOnlyList<ModProfile> _profiles;
    private readonly Func<ModProfile, bool> _isInstalled;
    private readonly IReadOnlyList<int> _capacities;

    /// <summary>The name this dialog proposed, so a change of mod can replace it while a name the
    /// person typed themselves is left alone.</summary>
    private string _proposedName = "";

    private string _format = "1v1";
    private string _entry = "open";
    private string _visibility = "public";
    private string _source = "registered";
    private int? _capacity;
    private Color _accent = Color.FromRgb(0x2F, 0x7F, 0xE0);
    private Popup? _modPopup;
    private Action? _refrost;

    private static readonly string[] Sources = { "registered", "adhoc", "draft" };

    /// <param name="modName">The mod to propose a name from, when <paramref name="profiles"/> names
    /// none. Null keeps an empty field and the visible <c>NameProblem</c>.</param>
    /// <param name="profiles">The mods to choose from. Null or empty draws no picker.</param>
    /// <param name="initiallySelected">Which one to start on: the launcher's active mod.</param>
    /// <param name="isInstalled">Which of them are on this PC. Null counts all of them.</param>
    /// <param name="serverMaxCapacity">The server's <c>max_capacity</c>, null when it predates it.</param>
    /// <param name="privateSupported">The server's <c>private_tournaments</c>: whether to offer a
    /// private tournament at all. An older server would make it public without a word.</param>
    public CreateTournamentDialog(
        string? modName = null,
        IReadOnlyList<ModProfile>? profiles = null,
        ModProfile? initiallySelected = null,
        Func<ModProfile, bool>? isInstalled = null,
        int? serverMaxCapacity = null,
        bool privateSupported = false)
    {
        InitializeComponent();
        VisibilityBlock.Visibility = privateSupported ? Visibility.Visible : Visibility.Collapsed;
        _profiles = profiles ?? Array.Empty<ModProfile>();
        _isInstalled = isInstalled ?? (_ => true);
        _capacities = CapacitiesFor(serverMaxCapacity);

        ApplyStrings();
        ModDialogChrome.SetTightLine(1, ModLabel, NameLabel, NameCount, FormatLabel, TeamSourceLabel, EntryModeLabel, CapacityLabel, VisibilityLabel);
        ModDialogChrome.SetTightLine(1.45, NameProblem, FormatWhy, WaitlistHint, OpenWhy);
        ModDialogChrome.SetTightLine(1.3, OpenTitle);
        FitToScreen();
        BuildRows();

        if (!string.IsNullOrWhiteSpace(modName)) ProposeNameFor(modName);

        if (_profiles.Count > 0)
        {
            ModBlock.Visibility = Visibility.Visible;
            ModChevron.Visibility = _profiles.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            ModPickButton.IsHitTestVisible = _profiles.Count > 1;
            PickMod(_profiles.FirstOrDefault(p => string.Equals(p.Id, initiallySelected?.Id, StringComparison.OrdinalIgnoreCase))
                    ?? _profiles[0]);
        }

        Refresh();

        FrameworkElement[] cards = { WhatCard, WhoCard, TeamSourceBlock, MathStrip };
        void Frost() => ModDialogChrome.FrostCards(Root, cards);
        foreach (var card in cards) card.SizeChanged += (_, _) => Frost();
        Scroller.ScrollChanged += (_, _) => Frost();
        _refrost = Frost;

        // Selected, not just focused: typing replaces the proposal.
        Loaded += (_, _) =>
        {
            NameEntry.Focus();
            NameEntry.SelectAll();
        };
    }

    private void ApplyStrings()
    {
        Title = Strings.Get("MpTournamentDialogTitle");
        TitleBarControl.Title = Title;
        ModLabel.Text = Strings.Get("NtLabelMod");
        ModInstalledText.Text = Strings.Get("NtInstalled");
        NameLabel.Text = Strings.Get("NtLabelName");
        FormatLabel.Text = Strings.Get("NtLabelFormat");
        TeamSourceLabel.Text = Strings.Get("NtLabelSource");
        EntryModeLabel.Text = Strings.Get("NtLabelEntry");
        VisibilityLabel.Text = Strings.Get("NtLabelVisibility");
        WaitlistHint.Text = Strings.Get("NtWaitlist");
        OpenTitle.Text = Strings.Get("NtOpenTitle");
        CancelButton.Content = Strings.Get("MpCreateDialogCancel");
    }

    /// <summary>With SizeToContent the window has no ceiling of its own: on a small screen the form
    /// scrolls instead of putting the footer past the bottom edge.</summary>
    private void FitToScreen()
    {
        var work = SystemParameters.WorkArea;
        if (work.Height > 200) MaxHeight = work.Height - 40;
    }

    private void BuildRows()
    {
        FormatRow.SetOptions(new[]
        {
            new SegmentOption("1v1", "1v1", Strings.Get("NtFormatSolo")),
            new SegmentOption("2v2", "2v2", Strings.Format("NtFormatTeams", 2)),
            new SegmentOption("3v3", "3v3", Strings.Format("NtFormatTeams", 3)),
        });
        FormatRow.Selected = _format;
        FormatRow.Picked += key => PickFormat((string)key);

        EntryRow.SetOptions(new[]
        {
            new OptionRow("open", Strings.Get("NtEntryOpen"), Strings.Get("NtWhyEntryOpen")),
            new OptionRow("approval", Strings.Get("NtEntryApproval"), Strings.Get("NtWhyEntryApproval")),
        });
        EntryRow.Selected = _entry;
        EntryRow.Picked += key =>
        {
            _entry = (string)key;
            Refresh();
        };

        VisibilityRow.SetOptions(new[]
        {
            new OptionRow("public", Strings.Get("NtVisibilityPublic"), Strings.Get("NtWhyPublic")),
            new OptionRow("private", Strings.Get("NtVisibilityPrivate"), Strings.Get("NtWhyPrivate")),
        });
        VisibilityRow.Selected = _visibility;
        VisibilityRow.Picked += key =>
        {
            _visibility = (string)key;
            Refresh();
        };

        CapacityRow.SetOptions(_capacities.Select(n => new SegmentOption(n, n.ToString())).ToList());
        CapacityRow.Selected = PlacesNow();
        CapacityRow.Picked += key =>
        {
            _capacity = (int)key;
            Refresh();
        };

        SourceRows.SetOptions(Sources.Select(key => key switch
        {
            "adhoc" => new OptionRow(key, Strings.Get("NtSourceAdhoc"), Strings.Get("NtSourceAdhocBody")),
            "draft" => new OptionRow(key, Strings.Get("NtSourceDraft"), Strings.Get("NtSourceDraftBody")),
            _ => new OptionRow(key, Strings.Get("NtSourceRegistered"), Strings.Get("NtSourceRegisteredBody")),
        }).ToList());
        SourceRows.Selected = _source;
        SourceRows.Picked += key =>
        {
            _source = (string)key;
            Refresh();
        };
    }

    /// <summary>The proposal, cut to what the field accepts: <c>MaxNameLength</c> and the XAML's
    /// <c>MaxLength</c> are the same number.</summary>
    private void ProposeNameFor(string? modName)
    {
        if (string.IsNullOrWhiteSpace(modName)) return;
        var proposed = Strings.Format("MpTournamentDialogDefaultName", modName!.Trim());
        _proposedName = proposed.Length > MaxNameLength ? proposed[..MaxNameLength] : proposed;
        NameEntry.Text = _proposedName;
    }

    // ------------------------------------------------------------------- the mod

    private void ModPickButton_Click(object sender, RoutedEventArgs e)
    {
        if (_profiles.Count < 2) return;
        if (ChromePopups.ConsumeToggleOff(ModPickButton)) return;
        if (_modPopup != null)
        {
            _modPopup.IsOpen = false;
            return;
        }
        OpenModList();
    }

    internal void OpenModList()
    {
        var popup = ModSearchPopup.Build(ModPickButton, _profiles, _isInstalled, SelectedMod, IconOf,
            mod => ModDialogChrome.AccentOf(mod) ?? _accent, PickMod);
        ChromePopups.Track(popup, ModPickButton);
        popup.Closed += (_, _) =>
        {
            _modPopup = null;
            ModChevronTurn.Angle = 0;
            ModPickButton.ClearValue(BorderBrushProperty);
        };
        _modPopup = popup;
        ModChevronTurn.Angle = 180;
        ModPickButton.BorderBrush = new SolidColorBrush(_accent);
        popup.IsOpen = true;
    }

    private static Brush? IconOf(ModProfile profile)
        => MainWindow.TryLoadTileImage(profile.ResolveIconSource(), MainWindow.IconDecodeWidth);

    /// <summary>A different mod: its window and its colour, and the proposed name while the field
    /// still holds the proposal. A name somebody typed is theirs.</summary>
    internal void PickMod(ModProfile profile)
    {
        SelectedMod = profile;
        ModNameText.Text = profile.DisplayName;
        ModIconHost.Background = IconOf(profile);
        ModInstalledChip.Visibility = _isInstalled(profile) ? Visibility.Visible : Visibility.Collapsed;
        _accent = ModDialogChrome.AttachPicked(this, TitleBarControl, Root, profile, Width, topGlow: true);
        _refrost?.Invoke();
        FormatRow.Accent = _accent;
        EntryRow.Accent = _accent;
        VisibilityRow.Accent = _accent;
        CapacityRow.Accent = _accent;
        SourceRows.Accent = _accent;

        if (string.IsNullOrEmpty(NameEntry.Text) || string.Equals(NameEntry.Text, _proposedName, StringComparison.Ordinal))
            ProposeNameFor(profile.DisplayName);
        Refresh();
    }

    // ------------------------------------------------------------------- the rest of the form

    internal void PickFormat(string format)
    {
        _format = format;
        _capacity = null;
        FormatRow.Selected = format;
        Refresh();
    }

    private void NameEntry_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsInitialized && OkButton != null) Refresh();
    }

    private void OpenToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (IsInitialized && OkButton != null) Refresh();
    }

    private void OpenRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && IsInside(source, OpenToggle)) return;
        OpenToggle.IsChecked = OpenToggle.IsChecked != true;
    }

    private static bool IsInside(DependencyObject element, DependencyObject ancestor)
    {
        for (DependencyObject? at = element; at != null; at = at is Visual ? VisualTreeHelper.GetParent(at) : null)
        {
            if (ReferenceEquals(at, ancestor)) return true;
        }
        return false;
    }

    private int RosterSize() => _format switch
    {
        "3v3" => 3,
        "2v2" => 2,
        _ => 1,
    };

    /// <summary>The places in force: what was picked, or what the format starts on. The second half
    /// keeps a server's shorter list honest.</summary>
    private int PlacesNow()
    {
        var wanted = _capacity ?? (_format == "1v1" ? 16 : 8);
        return _capacities.Contains(wanted) ? wanted : _capacities[^1];
    }

    /// <summary>Repaint every derived thing at once, so no two of them can disagree.</summary>
    private void Refresh()
    {
        var name = (NameEntry.Text ?? "").Trim();
        var missing = MinNameLength - name.Length;
        var tooShort = missing > 0;

        NameCount.Text = Strings.Format("NtCount", name.Length, MaxNameLength);
        NameProblem.Text = !tooShort ? ""
            : missing == 1 ? Strings.Format("NtNameShortOne", MinNameLength)
            : Strings.Format("NtNameShortMany", missing, MinNameLength);
        NameProblem.Visibility = tooShort ? Visibility.Visible : Visibility.Collapsed;
        if (tooShort) NameField.BorderBrush = new SolidColorBrush(Color.FromArgb(0xCC, 0xF4, 0x90, 0x0C));
        else NameField.ClearValue(Border.BorderBrushProperty);

        var open = OpenToggle.IsChecked == true;
        OkButton.Content = Strings.Get(open ? "NtCreateOpen" : "NtCreateDraft");
        OkButton.IsEnabled = !tooShort;
        OkButton.Foreground = new SolidColorBrush(ModAccent.OnAccent(_accent));
        var footer = tooShort ? Strings.Format("NtNameFooter", MinNameLength) : null;
        ErrorText.Text = footer ?? "";
        ErrorText.Visibility = footer == null ? Visibility.Collapsed : Visibility.Visible;
        OkButton.ToolTip = footer == null ? null : TooltipHelper.Wrap(footer);

        var team = _format != "1v1";
        var roster = RosterSize();
        TeamSourceBlock.Visibility = team ? Visibility.Visible : Visibility.Collapsed;
        FormatWhy.Text = team ? Strings.Format("NtWhyTeam", roster) : Strings.Get("NtWhySolo");
        EntryRow.Selected = _entry;
        VisibilityRow.Selected = _visibility;
        SourceRows.Selected = _source;
        CapacityLabel.Text = Strings.Get(team ? "NtLabelPlacesTeams" : "NtLabelPlaces");

        var places = PlacesNow();
        CapacityRow.Selected = places;
        var rounds = (int)Math.Round(Math.Log2(places));
        var rooms = places / 2;
        MathPlayers.Text = (places * roster).ToString();
        MathPlayersWord.Text = team ? Strings.Format("NtMathPlayersTeams", places, roster) : Strings.Get("NtMathPlayers");
        MathRounds.Text = rounds.ToString();
        MathRoundsWord.Text = Strings.Get(rounds == 1 ? "NtMathRoundOne" : "NtMathRounds");
        MathRooms.Text = rooms.ToString();
        MathRoomsWord.Text = Strings.Get(rooms == 1 ? "NtMathRoomOne" : "NtMathRooms");
        // The strip: the mod's colour at .10, its rim at .35 (design 108).
        MathStrip.Background = new SolidColorBrush(Color.FromArgb(0x1A, _accent.R, _accent.G, _accent.B));
        MathStrip.BorderBrush = new SolidColorBrush(Color.FromArgb(0x59, _accent.R, _accent.G, _accent.B));

        OpenWhy.Text = Strings.Get(open ? "NtOpenYes" : "NtOpenNo");
        OpenToggle.Foreground = new SolidColorBrush(open ? ModAccent.OnAccent(_accent) : Color.FromRgb(0x8E, 0xA4, 0xC0));
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var name = (NameEntry.Text ?? "").Trim();
        if (name.Length < MinNameLength) return;

        EnteredName = name;
        Format = _format;
        // 'solo' is the only value a 1v1 may carry, and the server enforces that too.
        TeamSource = _format == "1v1" ? "solo" : _source;
        EntryMode = _entry;
        TournamentVisibility = VisibilityBlock.Visibility == Visibility.Visible ? _visibility : "public";
        Capacity = PlacesNow();
        OpenRegistration = OpenToggle.IsChecked == true;

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
