using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Asks for a private tournament's invite code (2026-11-13) and looks it up before closing, so a
/// wrong code is said here, beside the field, instead of after the dialog has gone.
///
/// <para>The code's shape is the SERVER's to judge; this only trims it and refuses the empty
/// field. The lookup is a callback so the dialog never touches the network itself.</para>
/// </summary>
public partial class JoinTournamentCodeDialog : BaseDialog
{
    /// <summary>Answers the tournament id the code opens, or null with the text to show.</summary>
    private readonly Func<string, Task<(string? Id, string? Error)>> _lookup;

    /// <summary>The tournament the code opened. Set only when the dialog answers yes.</summary>
    public string? FoundTournamentId { get; private set; }

    private bool _busy;

    public JoinTournamentCodeDialog(Func<string, Task<(string? Id, string? Error)>> lookup)
    {
        _lookup = lookup;
        InitializeComponent();

        SetBarTitle(Strings.Get("TpJoinCodeTitle"));
        SetHeadline(Strings.Get("TpJoinCodeHeadline"));
        FieldLabel.Text = Strings.Get("TpJoinCodeLabel");
        PromptText.Text = Strings.Get("TpJoinCodeHint");
        CancelAction.Content = Strings.Get("BtnCancel");
        PrimaryAction.Content = Strings.Get("TpJoinCodeOpen");
        Refresh();

        Loaded += (_, _) => CodeEntry.Focus();
    }

    /// <summary>What the field holds, as the server reads it: no spaces, upper case.</summary>
    internal string EnteredCode => (CodeEntry.Text ?? "").Replace(" ", "").Trim().ToUpperInvariant();

    private void CodeEntry_TextChanged(object sender, TextChangedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        Refresh();
    }

    private void Refresh() => PrimaryAction.IsEnabled = !_busy && EnteredCode.Length > 0;

    protected override async void OnPrimary()
    {
        var code = EnteredCode;
        if (code.Length == 0 || _busy) return;

        _busy = true;
        Refresh();
        PrimaryAction.Content = Strings.Get("TpJoinCodeLooking");
        try
        {
            var (id, error) = await _lookup(code);
            if (!string.IsNullOrEmpty(id))
            {
                FoundTournamentId = id;
                Answer(true);
                return;
            }
            ErrorText.Text = error ?? Strings.Get("TpJoinCodeNotFound");
            ErrorText.Visibility = Visibility.Visible;
        }
        finally
        {
            _busy = false;
            PrimaryAction.Content = Strings.Get("TpJoinCodeOpen");
            Refresh();
        }
    }
}
