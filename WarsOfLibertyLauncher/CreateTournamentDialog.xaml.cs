using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using WarsOfLibertyLauncher.Localization;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Asks for the shape of a new tournament.
///
/// <para>Modelled on <see cref="RenameRoomDialog"/>: a <c>Window</c>, opened with
/// <c>ShowDialog</c>, results read off public properties afterwards. Unlike
/// <c>CreateLobbyDialog</c> it does NOT perform the POST itself — creating a tournament can
/// be refused for reasons the caller already knows how to explain (the per-user cap, the
/// server-wide one), and duplicating that handling here would mean two copies of it.</para>
///
/// <para>Everything this collects is a REQUEST. The server clamps the capacity, decides
/// whether the mod is ranked, and echoes back what it actually made — the launcher holds no
/// copy of those rules.</para>
///
/// <para><b>No line of help here may contradict the selection.</b> Every explanation is
/// recomputed from what is currently chosen, which is the defect this replaces: the old
/// dialog carried a fixed paragraph whose worked example was a 3v3 while 1v1 was selected,
/// and a greyed-out primary button with nothing anywhere saying what it was waiting for.</para>
/// </summary>
public partial class CreateTournamentDialog : Window
{
    private const int MinNameLength = 3;
    private const int MaxNameLength = 80;

    /// <summary>Places offered, counted in ENTRANTS rather than people: eight slots of 3v3
    /// is twenty-four players. Capped at sixteen because a sixteen-entrant first round is
    /// eight simultaneous rooms, which is half of what the whole server allows.</summary>
    private static readonly int[] Capacities = { 2, 4, 8, 16 };

    public string EnteredName { get; private set; } = "";
    public string Format { get; private set; } = "1v1";
    public string TeamSource { get; private set; } = "solo";
    public string EntryMode { get; private set; } = "open";
    public int Capacity { get; private set; } = 8;

    /// <summary>
    /// The mod this tournament will be played on, or null when the caller named none and the
    /// dialog therefore showed no picker.
    ///
    /// <para>It decides more than anything else collected here: the server stamps it on the
    /// tournament, every room the bracket opens is created with it, and a player whose active
    /// mod is not this one is turned away when they try to play their match. It used to be
    /// taken silently from whatever mod happened to be active in the launcher.</para>
    /// </summary>
    public Models.ModProfile? SelectedMod { get; private set; }

    /// <summary>The name this dialog proposed, so a change of mod can replace it while a name
    /// the person typed themselves is left alone.</summary>
    private string _proposedName = "";

    private readonly Button[] _capacityButtons = new Button[Capacities.Length];
    private int _capacityIndex = 2;      // 8

    /// <param name="modName">
    /// The mod this tournament will be for, used to propose a name. Ignored when
    /// <paramref name="profiles"/> names some, since the picked one is then the answer.
    ///
    /// <para><b>Optional, and that is load-bearing.</b> Null keeps the old behaviour exactly
    /// — an empty field, a disabled button and a visible <c>NameProblem</c> — so the
    /// parameterless construction that <c>DialogXamlTests</c> pins still describes something
    /// real rather than something no caller does.</para>
    /// </param>
    /// <param name="profiles">
    /// The mods this tournament could be played on, for the picker. Null or empty draws no
    /// picker at all, which is the demo path and the old behaviour.
    /// </param>
    /// <param name="initiallySelected">Which of them to start on — the launcher's active mod,
    /// which is what the tournament used to take without asking.</param>
    public CreateTournamentDialog(
        string? modName = null,
        IReadOnlyList<Models.ModProfile>? profiles = null,
        Models.ModProfile? initiallySelected = null)
    {
        InitializeComponent();

        Title = Strings.Get("MpTournamentDialogTitle");
        TitleBarControl.Title = Strings.Get("MpTournamentDialogTitle");
        // The same word the room dialog uses for the same thing: one string, so the two
        // pickers cannot end up calling it different things.
        ModLabel.Text = Strings.Get("MpCreateDialogModLabel");
        NameLabel.Text = Strings.Get("MpTournamentDialogName");
        FormatLabel.Text = Strings.Get("MpTournamentDialogFormat");
        TeamSourceLabel.Text = Strings.Get("MpTournamentDialogTeamSource");
        SourceRegistered.Content = Strings.Get("MpTournamentSourceRegistered");
        SourceAdhoc.Content = Strings.Get("MpTournamentSourceAdhoc");
        SourceDraft.Content = Strings.Get("MpTournamentSourceDraft");
        EntryModeLabel.Text = Strings.Get("MpTournamentDialogEntryMode");
        EntryOpen.Content = Strings.Get("MpTournamentEntryOpen");
        EntryApproval.Content = Strings.Get("MpTournamentEntryApproval");
        CapacityLabel.Text = Strings.Get("MpTournamentDialogCapacity");
        CapacityHint.Text = Strings.Get("MpTournamentDialogCapacityHint");
        CancelButton.Content = Strings.Get("BtnCancel");
        // NOT the window's title. "New tournament" on the button says what the window is,
        // not what pressing it does, and it was the same words as the caption above it.
        OkButton.Content = Strings.Get("MpTournamentCreateAction");

        BuildCapacitySegments();

        Format1v1.Tag = "active";
        EntryOpen.Tag = "active";

        // PROPOSE, do not demand. Empty, this dialog opened with "3 more characters: a name
        // needs at least 3" already on screen and OkButton dead - it greeted you with a
        // complaint about something you had not done yet. The room dialog never had that
        // problem for exactly this reason. NameProblem still exists; it appears if you clear
        // the field, which is when it is finally about a choice you made.
        if (!string.IsNullOrWhiteSpace(modName))
        {
            ProposeNameFor(modName);
        }

        // The picker, and the name that follows it. Built AFTER the proposal above so a
        // caller that passes both gets the picked mod's name, not the string's.
        if (profiles is { Count: > 0 })
        {
            foreach (var p in profiles)
            {
                var item = new ComboBoxItem { Content = p.DisplayName, Tag = p };
                ModCombo.Items.Add(item);
                if (initiallySelected != null
                    && string.Equals(p.Id, initiallySelected.Id, StringComparison.OrdinalIgnoreCase))
                {
                    item.IsSelected = true;
                }
            }
            if (ModCombo.SelectedItem == null && ModCombo.Items.Count > 0)
                ((ComboBoxItem)ModCombo.Items[0]!).IsSelected = true;

            // One installed mod is not a choice — but the block still shows, because naming
            // the mod the tournament is being tied to is half the point of having it here.
            ModCombo.IsEnabled = profiles.Count > 1;
            ModBlock.Visibility = Visibility.Visible;
        }

        Refresh();
        // Selected, not just focused: a proposal you have to erase before typing is worse
        // than an empty box. Typing replaces it; the caret is at the end if you would rather
        // edit it.
        Loaded += (_, _) =>
        {
            NameEntry.Focus();
            NameEntry.SelectAll();
        };
    }

    private void BuildCapacitySegments()
    {
        for (int i = 0; i < Capacities.Length; i++)
        {
            var b = new Button
            {
                Content = Capacities[i].ToString(),
                Tag = i == _capacityIndex ? "active" : null,
                Margin = i == Capacities.Length - 1 ? new Thickness(0) : new Thickness(0, 0, 4, 0),
            };
            b.SetResourceReference(StyleProperty, "MpSegment");
            int index = i;
            b.Click += (_, _) => { _capacityIndex = index; Refresh(); };
            Grid.SetColumn(b, i);
            CapacityRow.Children.Add(b);
            _capacityButtons[i] = b;
        }
    }

    /// <summary>
    /// Put "{mod} tournament" in the field, truncated to what the field itself would accept —
    /// <c>MaxNameLength</c> and the XAML's <c>MaxLength</c> are the same number, and an
    /// untruncated proposal would be silently cut anyway.
    /// </summary>
    private void ProposeNameFor(string? modName)
    {
        if (string.IsNullOrWhiteSpace(modName)) return;
        var proposed = Strings.Format("MpTournamentDialogDefaultName", modName!.Trim());
        _proposedName = proposed.Length > MaxNameLength
            ? proposed.Substring(0, MaxNameLength)
            : proposed;
        NameEntry.Text = _proposedName;
    }

    /// <summary>
    /// A different mod: the tournament's, and the proposed name with it — but only while the
    /// field still holds the proposal. A name somebody typed is theirs, and having it
    /// overwritten by a click on the picker above it is the kind of thing that makes people
    /// retype it and then not trust the field.
    /// </summary>
    private void ModCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModCombo.SelectedItem is not ComboBoxItem item
            || item.Tag is not Models.ModProfile profile) return;

        SelectedMod = profile;
        if (string.IsNullOrEmpty(NameEntry.Text)
            || string.Equals(NameEntry.Text, _proposedName, StringComparison.Ordinal))
        {
            ProposeNameFor(profile.DisplayName);
        }
        Refresh();
    }

    private void NameEntry_TextChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void Format_Click(object sender, RoutedEventArgs e)
    {
        Format1v1.Tag = ReferenceEquals(sender, Format1v1) ? "active" : null;
        Format2v2.Tag = ReferenceEquals(sender, Format2v2) ? "active" : null;
        Format3v3.Tag = ReferenceEquals(sender, Format3v3) ? "active" : null;
        Refresh();
    }

    private void Entry_Click(object sender, RoutedEventArgs e)
    {
        EntryOpen.Tag = ReferenceEquals(sender, EntryOpen) ? "active" : null;
        EntryApproval.Tag = ReferenceEquals(sender, EntryApproval) ? "active" : null;
        Refresh();
    }

    /// <summary>How many people one entrant is, which is what makes the places arithmetic
    /// worth showing at all.</summary>
    private int RosterSize() => SelectedFormat() switch
    {
        "3v3" => 3,
        "2v2" => 2,
        _ => 1,
    };

    private string SelectedFormat()
        => (string?)Format3v3.Tag == "active" ? "3v3"
         : (string?)Format2v2.Tag == "active" ? "2v2"
         : "1v1";

    /// <summary>
    /// Repaint every derived thing at once.
    ///
    /// <para>One method rather than a handler per control, because the point is that no two
    /// of these can ever disagree with each other or with the selection.</para>
    /// </summary>
    private void Refresh()
    {
        string name = (NameEntry.Text ?? "").Trim();
        int missing = MinNameLength - name.Length;

        NameCount.Text = Strings.Format("MpTournamentDialogNameCount", name.Length, MaxNameLength);
        if (missing > 0)
        {
            NameProblem.Text = Strings.Format("MpTournamentDialogNameShort", missing, MinNameLength);
            NameProblem.Visibility = Visibility.Visible;
        }
        else
        {
            NameProblem.Visibility = Visibility.Collapsed;
        }
        OkButton.IsEnabled = missing <= 0;

        bool team = SelectedFormat() != "1v1";
        // The team-source question only exists for a team format; asking it for a 1v1 would
        // be asking about something that cannot vary.
        TeamSourceBlock.Visibility = team ? Visibility.Visible : Visibility.Collapsed;
        FormatWhy.Text = team
            ? Strings.Format("MpTournamentWhyFormatTeam", RosterSize())
            : Strings.Get("MpTournamentWhyFormatSolo");

        EntryWhy.Text = (string?)EntryApproval.Tag == "active"
            ? Strings.Get("MpTournamentWhyEntryApproval")
            : Strings.Get("MpTournamentWhyEntryOpen");

        for (int i = 0; i < _capacityButtons.Length; i++)
        {
            _capacityButtons[i].Tag = i == _capacityIndex ? "active" : null;
        }

        int places = Capacities[_capacityIndex];
        int roster = RosterSize();
        // Rounds to the final, and the size of the first round in ROOMS. Both come out of the
        // same number and both are things somebody wants before choosing it, not after.
        int rounds = (int)Math.Round(Math.Log2(places));
        CapacityMath.Text = Strings.Format("MpTournamentWhyCapacity",
            places, roster, places * roster, places / 2, rounds);
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var name = (NameEntry.Text ?? "").Trim();
        if (name.Length < MinNameLength) return;

        EnteredName = name;
        Format = SelectedFormat();

        // 'solo' is the only value a 1v1 may carry, and the server enforces that too —
        // sending anything else would simply be refused.
        TeamSource = Format == "1v1"
            ? "solo"
            : SourceDraft.IsChecked == true ? "draft"
            : SourceAdhoc.IsChecked == true ? "adhoc"
            : "registered";

        EntryMode = (string?)EntryApproval.Tag == "active" ? "approval" : "open";
        Capacity = Capacities[_capacityIndex];

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
