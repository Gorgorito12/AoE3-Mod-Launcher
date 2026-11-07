using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;

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
public partial class PlayerPickerDialog : BaseDialog
{
    private const int MinNameLength = 2;

    private readonly int _required;
    private readonly int _nameMax;
    private readonly bool _askName;
    private readonly bool _nameRequired;
    private readonly string? _okLabelFormat;
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
        bool nameRequired = true,
        string? headline = null,
        string? okLabelFormat = null,
        ModProfile? mod = null)
    {
        InitializeComponent();

        _required = Math.Max(1, required);
        _nameMax = nameMax;
        _askName = nameLabel != null;
        _nameRequired = nameRequired;
        _search = search;
        _candidates = candidates.ToList();
        foreach (var c in _candidates.Where(c => c.Locked)) _selected.Add(c.Id);

        _okLabelFormat = okLabelFormat;
        SetBarTitle(title);
        SetHeadline(headline ?? title);
        SetLead(intro);
        System.Windows.Automation.AutomationProperties.SetName(ListPanel, listLabel);
        FootNote.Visibility = Visibility.Visible;
        PrimaryAction.Content = okLabel;
        PrimaryAction.IsEnabled = false;
        CancelAction.Content = Strings.Get("BtnCancel");
        UseMod(mod);
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

    /// <summary>"2 of 3 chosen": the rule, visibly satisfied or not.</summary>
    internal TextBlock CountText => FootNote;

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
            ListPanel.Children.Add(BuildRow(c));
        }

        EmptyText.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
        CountText.Text = Strings.Format("MpPickCount", _selected.Count, _required);
        CountText.SetResourceReference(TextBlock.ForegroundProperty,
            _selected.Count == _required ? "MpOkText" : "WindowFineText");
        if (_okLabelFormat != null) PrimaryAction.Content = string.Format(_okLabelFormat, _selected.Count);
        PrimaryAction.IsEnabled = CanConfirm;
    }

    /// <summary>A row of 44: the initial, the name, why it cannot be chosen, and the box. One that
    /// cannot be chosen stays in view, dimmed, so nobody wonders where a player went.</summary>
    private UIElement BuildRow(PickCandidate c)
    {
        var line = new Grid();
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var initial = new TextBlock
        {
            Text = c.Label.Length == 0 ? "?" : char.ToUpperInvariant(c.Label[0]).ToString(),
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        line.Children.Add(new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = c.Disabled ? new SolidColorBrush(Color.FromArgb(0x1F, 255, 255, 255)) : AvatarBrush(c.Label),
            VerticalAlignment = VerticalAlignment.Center,
            Child = initial,
        });

        var text = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(text, 1);
        var label = new TextBlock
        {
            Text = c.Label,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "UiTitleText");
        label.SetResourceReference(TextBlock.FontSizeProperty, "DialogBodySize");
        text.Children.Add(label);
        var detailText = c.Disabled ? c.DisabledReason ?? c.Detail : c.Detail;
        if (!string.IsNullOrEmpty(detailText))
        {
            var detail = new TextBlock { Text = detailText, TextTrimming = TextTrimming.CharacterEllipsis };
            detail.SetResourceReference(TextBlock.ForegroundProperty, "WindowFineText");
            detail.SetResourceReference(TextBlock.FontSizeProperty, "SetDescSize");
            text.Children.Add(detail);
        }
        line.Children.Add(text);

        var row = new CheckBox
        {
            Content = line,
            Tag = c.Id,
            IsChecked = _selected.Contains(c.Id),
            IsEnabled = !c.Locked && !c.Disabled,
            Margin = new Thickness(0, 0, 0, 2),
        };
        row.SetResourceReference(StyleProperty, "DialogPickRow");
        System.Windows.Automation.AutomationProperties.SetName(row, c.Label);
        var id = c.Id;
        row.Checked += (_, _) => { if (!_selected.Contains(id)) Toggle(id, true); };
        row.Unchecked += (_, _) => { if (_selected.Contains(id)) Toggle(id, false); };
        return row;
    }

    private static Brush AvatarBrush(string label)
    {
        string[] colours = { "#2F7FE0", "#7A5BD6", "#2B9C8A", "#C0783A" };
        var at = label.Length == 0 ? 0 : char.ToUpperInvariant(label[0]) % colours.Length;
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colours[at]));
        brush.Freeze();
        return brush;
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
        PrimaryAction.IsEnabled = CanConfirm;
    }

    protected override void OnPrimary()
    {
        if (!CanConfirm) return;
        SelectedIds = _candidates.Where(c => _selected.Contains(c.Id)).Select(c => c.Id).ToList();
        EnteredName = _askName ? NameEntry.Text.Trim() : "";
        if (EnteredName.Length > _nameMax) EnteredName = EnteredName[.._nameMax];
        Answer(true);
    }
}
