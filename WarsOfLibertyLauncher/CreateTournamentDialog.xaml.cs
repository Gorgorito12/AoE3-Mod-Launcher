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
/// </summary>
public partial class CreateTournamentDialog : Window
{
    private const int MinNameLength = 3;

    /// <summary>Places offered, counted in ENTRANTS rather than people: eight slots of 3v3
    /// is twenty-four players. Capped at sixteen because a sixteen-entrant first round is
    /// eight simultaneous rooms, which is half of what the whole server allows.</summary>
    private static readonly int[] Capacities = { 2, 4, 8, 16 };

    public string EnteredName { get; private set; } = "";
    public string Format { get; private set; } = "1v1";
    public string TeamSource { get; private set; } = "solo";
    public string EntryMode { get; private set; } = "open";
    public int Capacity { get; private set; } = 8;

    public CreateTournamentDialog()
    {
        InitializeComponent();

        Title = Strings.Get("MpTournamentDialogTitle");
        TitleBarControl.Title = Strings.Get("MpTournamentDialogTitle");
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
        OkButton.Content = Strings.Get("MpTournamentCreate");

        foreach (var n in Capacities) CapacityBox.Items.Add(n.ToString());
        CapacityBox.SelectedIndex = 2;   // 8

        // The team-source question only exists for a team format, and asking it for a 1v1
        // would be asking about something that cannot vary.
        void SyncTeamSource(object? _, RoutedEventArgs? __)
            => TeamSourceBlock.Visibility =
                Format1v1.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;

        Format1v1.Checked += SyncTeamSource;
        Format2v2.Checked += SyncTeamSource;
        Format3v3.Checked += SyncTeamSource;
        SyncTeamSource(null, null);

        RefreshOkState();
        Loaded += (_, _) => NameEntry.Focus();
    }

    private void NameEntry_TextChanged(object sender, TextChangedEventArgs e) => RefreshOkState();

    private void RefreshOkState()
        => OkButton.IsEnabled = (NameEntry.Text ?? "").Trim().Length >= MinNameLength;

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var name = (NameEntry.Text ?? "").Trim();
        if (name.Length < MinNameLength) return;

        EnteredName = name;
        Format = Format3v3.IsChecked == true ? "3v3"
               : Format2v2.IsChecked == true ? "2v2"
               : "1v1";

        // 'solo' is the only value a 1v1 may carry, and the server enforces that too —
        // sending anything else would simply be refused.
        TeamSource = Format == "1v1"
            ? "solo"
            : SourceDraft.IsChecked == true ? "draft"
            : SourceAdhoc.IsChecked == true ? "adhoc"
            : "registered";

        EntryMode = EntryApproval.IsChecked == true ? "approval" : "open";

        int idx = CapacityBox.SelectedIndex;
        Capacity = idx >= 0 && idx < Capacities.Length ? Capacities[idx] : 8;

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
