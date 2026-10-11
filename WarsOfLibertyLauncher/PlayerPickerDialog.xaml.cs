using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Localization;

namespace WarsOfLibertyLauncher;

/// <summary>One thing that can be chosen: a player, a team, a solo entrant.</summary>
/// <param name="Locked">Chosen and cannot be unchosen — the captain of his own line-up.</param>
/// <param name="Disabled">Cannot be chosen, with the reason as its tooltip — somebody already
/// entered in this tournament.</param>
internal sealed record PickCandidate(
    string Id, string Label, string? Detail = null,
    bool Locked = false, bool Disabled = false, string? DisabledReason = null);

/// <summary>
/// "Choose exactly N" — every team question the tournaments ask, in one dialog: whom to invite
/// to a team, the players of an ad-hoc line-up, who of a registered team plays, and which solo
/// players the organiser folds into a team.
///
/// <para><b>It only collects.</b> The tab makes the call with what this returns, exactly as with
/// <see cref="CreateTournamentDialog"/>, so every call keeps going through the swappable
/// tournament server and the preview never reaches the real one.</para>
///
/// <para><b>OK is enabled only at exactly N</b> (and with a valid name when one is asked for),
/// and the counter says so — the rule is something you see yourself satisfying, not something
/// you discover by being refused.</para>
/// </summary>
public partial class PlayerPickerDialog : Window
{
    private const int MinNameLength = 2;

    private readonly int _required;
    private readonly int _nameMax;
    private readonly bool _askName;
    private readonly bool _nameRequired;
    private readonly Func<string, Task<IReadOnlyList<PickCandidate>>>? _search;
    private readonly List<PickCandidate> _candidates;
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _searchDelay;
    private int _searchGeneration;

    /// <summary>What was chosen, in the order the candidates were listed.</summary>
    public IReadOnlyList<string> SelectedIds { get; private set; } = Array.Empty<string>();

    /// <summary>The name typed, trimmed, or empty when none was asked for.</summary>
    public string EnteredName { get; private set; } = "";

    internal PlayerPickerDialog(
        string title,
        string intro,
        string listLabel,
        IReadOnlyList<PickCandidate> candidates,
        int required,
        string okLabel,
        string? nameLabel = null,
        string? initialName = null,
        int nameMax = 80,
        Func<string, Task<IReadOnlyList<PickCandidate>>>? search = null,
        bool nameRequired = true)
    {
        InitializeComponent();

        _required = Math.Max(1, required);
        _nameMax = nameMax;
        _askName = nameLabel != null;
        _nameRequired = nameRequired;
        _search = search;
        _candidates = candidates.ToList();
        foreach (var c in _candidates.Where(c => c.Locked)) _selected.Add(c.Id);

        Title = title;
        TitleBarControl.Title = title;
        IntroText.Text = intro;
        ListLabel.Text = listLabel;
        OkButton.Content = okLabel;
        CancelButton.Content = Strings.Get("BtnCancel");
        EmptyText.Text = Strings.Get("MpPickEmpty");

        if (_askName)
        {
            NameBlock.Visibility = Visibility.Visible;
            NameLabel.Text = nameLabel;
            NameEntry.MaxLength = nameMax;
            NameEntry.Text = initialName ?? "";
        }
        if (_search != null)
        {
            SearchBlock.Visibility = Visibility.Visible;
            SearchLabel.Text = Strings.Get("MpPickSearch");
        }

        // Asking the server as the user types, but not on every keystroke.
        _searchDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _searchDelay.Tick += async (_, _) =>
        {
            _searchDelay.Stop();
            await RunSearchAsync();
        };
        Closed += (_, _) => _searchDelay.Stop();

        Render();
    }

    /// <summary>Choose (or unchoose) a candidate as a click would. For the tests.</summary>
    internal void Toggle(string id, bool chosen)
    {
        var c = _candidates.FirstOrDefault(x => x.Id == id);
        if (c == null || c.Locked || c.Disabled) return;
        if (chosen)
        {
            // Choosing exactly one is a radio: the new choice replaces the old.
            if (_required == 1) _selected.RemoveWhere(x => !_candidates.Any(k => k.Id == x && k.Locked));
            _selected.Add(id);
        }
        else
        {
            _selected.Remove(id);
        }
        Render();
    }

    /// <summary>Whether OK may be pressed right now. For the tests and the button.</summary>
    internal bool CanConfirm
        => _selected.Count == _required
           && (!_askName || !_nameRequired || NameEntry.Text.Trim().Length >= MinNameLength);

    private void Render()
    {
        var filter = SearchEntry.Text.Trim();
        ListPanel.Children.Clear();
        var shown = 0;
        foreach (var c in _candidates)
        {
            var match = filter.Length == 0
                        || c.Label.Contains(filter, StringComparison.OrdinalIgnoreCase)
                        || _selected.Contains(c.Id);
            if (!match) continue;
            shown++;

            var text = new StackPanel();
            var label = new TextBlock { Text = c.Label, TextTrimming = TextTrimming.CharacterEllipsis };
            label.SetResourceReference(TextBlock.ForegroundProperty, "MpTextBody");
            label.SetResourceReference(TextBlock.FontSizeProperty, "MpLabelSize");
            text.Children.Add(label);
            var detailText = c.Disabled ? c.DisabledReason ?? c.Detail : c.Detail;
            if (!string.IsNullOrEmpty(detailText))
            {
                var detail = new TextBlock
                {
                    Text = detailText,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 1, 0, 0),
                };
                detail.SetResourceReference(TextBlock.ForegroundProperty, c.Disabled ? "MpCautionText" : "MpTextMuted");
                detail.SetResourceReference(TextBlock.FontSizeProperty, "MpPillSize");
                text.Children.Add(detail);
            }

            var box = new CheckBox
            {
                Content = text,
                Tag = c.Id,
                IsChecked = _selected.Contains(c.Id),
                IsEnabled = !c.Locked && !c.Disabled,
                Margin = new Thickness(0, 4, 0, 4),
                VerticalContentAlignment = VerticalAlignment.Top,
            };
            var id = c.Id;
            box.Checked += (_, _) => { if (!_selected.Contains(id)) Toggle(id, true); };
            box.Unchecked += (_, _) => { if (_selected.Contains(id)) Toggle(id, false); };
            ListPanel.Children.Add(box);
        }

        EmptyText.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
        CountText.Text = Strings.Format("MpPickCount", _selected.Count, _required);
        CountText.SetResourceReference(TextBlock.ForegroundProperty,
            _selected.Count == _required ? "MpOkText" : "MpTextGhost");
        OkButton.IsEnabled = CanConfirm;
    }

    private async Task RunSearchAsync()
    {
        if (_search == null) return;
        var query = SearchEntry.Text.Trim();
        if (query.Length < 2) return;
        var generation = ++_searchGeneration;
        IReadOnlyList<PickCandidate> found;
        try
        {
            found = await _search(query);
        }
        catch
        {
            return;
        }
        // A newer search has started since this one was asked.
        if (generation != _searchGeneration) return;
        foreach (var c in found)
        {
            if (_candidates.All(x => x.Id != c.Id)) _candidates.Add(c);
        }
        Render();
    }

    private void SearchEntry_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsInitialized || _candidates == null) return;
        Render();
        if (_search != null)
        {
            _searchDelay.Stop();
            _searchDelay.Start();
        }
    }

    private void NameEntry_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsInitialized || _candidates == null) return;
        OkButton.IsEnabled = CanConfirm;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanConfirm) return;
        SelectedIds = _candidates.Where(c => _selected.Contains(c.Id)).Select(c => c.Id).ToList();
        EnteredName = _askName ? NameEntry.Text.Trim() : "";
        if (EnteredName.Length > _nameMax) EnteredName = EnteredName[.._nameMax];
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
