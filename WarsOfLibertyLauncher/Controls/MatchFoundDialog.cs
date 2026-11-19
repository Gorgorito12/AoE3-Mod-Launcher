using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>One side of the duel: the face (avatar with its badge, built by the tab, which owns
/// the badge rules), the name and the line under it ("1383 ELO", "In placement 3/10").</summary>
internal sealed record MatchDuelist(string Name, FrameworkElement Face, string? Line);

/// <summary>
/// "Rival found": the quick-match queue matched two players and each has a few seconds to say
/// yes. It is the one moment of the search that needs the player, so it comes forward on its own
/// — over the desktop, with its own sound and the taskbar flashing (the caller does those) — and
/// counts down where it can be seen.
///
/// <para><b>Design 117</b> (<c>docs/design_sala_competitiva</c>): the crossed swords in green, the
/// two players face to face with who accepted under each, the room's stake card, the seconds in
/// large over a thick bar that turns amber at <see cref="LowSeconds"/>, the buttons at the right
/// and the strike note under them. Who hosts is a tag on that player's tile; the whole sentence
/// is its tooltip.</para>
///
/// <para><b>Non-modal on purpose.</b> It must open with the launcher in the tray, and a modal
/// needs an owner that is on screen. Escape and the ✕ DECLINE, which costs a strike on the
/// server, so the note says so before anybody reaches for them.</para>
///
/// <para>The answer leaves through <see cref="Accepted"/> / <see cref="Declined"/>; the server's
/// reply (both accepted, somebody declined, the clock ran out) is what closes the window, through
/// <see cref="CloseQuietly"/>, so a click and a timeout never race each other into two answers.</para>
/// </summary>
internal sealed class MatchFoundDialog : BaseDialog
{
    /// <summary>From this many seconds down, the clock is amber.</summary>
    internal const int LowSeconds = 5;

    internal const string HostTag = "MatchFoundHostTag";

    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DateTime _deadlineUtc;
    private readonly int _totalMs;
    private readonly string _rival;
    private readonly TextBlock _clockLabel = new();
    private readonly System.Windows.Documents.Run _secondsRun = new();
    private readonly ColumnDefinition _barDone = new();
    private readonly ColumnDefinition _barLeft = new();
    private readonly AcceptMark _meMark;
    private readonly AcceptMark _rivalMark;
    /// <summary>Where the points at stake go when the server's <c>mm_stake</c> arrives.</summary>
    private readonly ContentControl _stakeHost = new() { Margin = new Thickness(0, 14, 0, 0), Visibility = Visibility.Collapsed, Focusable = false };
    private bool _answered;
    private bool _iAccepted;
    private bool _rivalAccepted;
    private int _acceptedCount;
    private bool _closingQuietly;

    /// <summary>The seconds, in large; amber from <see cref="LowSeconds"/> down.</summary>
    internal TextBlock ClockSeconds { get; } = new();
    internal Border ClockFill { get; } = new() { CornerRadius = new CornerRadius(4) };
    internal Border MyTile { get; }
    internal Border RivalTile { get; }
    internal bool StakeShown => _stakeHost.Visibility == Visibility.Visible;
    internal bool MeMarkedAccepted => _meMark.On;
    internal bool RivalMarkedAccepted => _rivalMark.On;
    internal string ClockLabelText => _clockLabel.Text;
    internal string ClockSecondsText => _secondsRun.Text;

    /// <summary>The player pressed Accept.</summary>
    public event Action? Accepted;

    /// <summary>The player pressed Decline, Escape or the ✕.</summary>
    public event Action? Declined;

    /// <param name="rated">A rated match: the kind line is gold. Casual is grey.</param>
    /// <param name="hostLine">The whole sentence about who hosts; the tooltip of the HOST tag.</param>
    /// <param name="iHost">Which tile wears the HOST tag; null = unknown, no tag.</param>
    /// <param name="iChallenged">I pressed "Play" on his row: the card says I am waiting for him.</param>
    public MatchFoundDialog(MatchDuelist me, MatchDuelist rival, string kindLine, bool rated, string? hostLine,
        bool? iHost, int acceptMs, bool iChallenged, ModProfile? mod)
    {
        _rival = rival.Name;
        _totalMs = Math.Max(1000, acceptMs);
        _deadlineUtc = DateTime.UtcNow.AddMilliseconds(_totalMs);
        Tone = DialogTone.Info;
        Topmost = true;
        ShowInTaskbar = true;
        Width = 480;
        SetBarTitle(Strings.Get("MmFoundBarTitle"));
        var headline = iChallenged ? Strings.Format("MmChallengeSentHeadline", rival.Name) : Strings.Get("MmFoundHeadline");
        ReplaceHeader(BuildHeader(headline, kindLine, rated));

        var accent = (mod != null ? ModDialogChrome.AccentOf(mod) : null) ?? ((SolidColorBrush)B("MpAction")).Color;
        _meMark = new AcceptMark(mirrored: false);
        _rivalMark = new AcceptMark(mirrored: true);
        MyTile = BuildTile(me, _meMark, mine: true, mirrored: false, host: iHost == true, hostLine, accent);
        RivalTile = BuildTile(rival, _rivalMark, mine: false, mirrored: true, host: iHost == false, hostLine, accent);

        var duel = new Grid();
        duel.ColumnDefinitions.Add(new ColumnDefinition());
        duel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        duel.ColumnDefinitions.Add(new ColumnDefinition());
        duel.Children.Add(MyTile);
        var versus = new TextBlock
        {
            Text = "VS",
            Margin = new Thickness(9, 0, 9, 0),
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = B("UiDimText"),
        };
        versus.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        versus.SetResourceReference(TextBlock.FontSizeProperty, "SetControlSize");
        Grid.SetColumn(versus, 1);
        duel.Children.Add(versus);
        Grid.SetColumn(RivalTile, 2);
        duel.Children.Add(RivalTile);

        var body = new StackPanel();
        body.Children.Add(duel);
        body.Children.Add(_stakeHost);
        body.Children.Add(BuildClock());
        Body = body;

        FootNote.Text = Strings.Get("MmFoundDeclineNote");
        FootNote.Visibility = Visibility.Visible;
        FootNote.SetResourceReference(TextBlock.ForegroundProperty, "UiDimText");
        FootNote.SetResourceReference(TextBlock.FontSizeProperty, "SetDescSize");
        PutFootNoteBelow();

        PrimaryAction.Content = Strings.Get("MmAccept");
        PrimaryAction.Height = 36;
        PrimaryAction.MinWidth = 168;
        PrimaryAction.SetResourceReference(FontSizeProperty, "SetRowTitleSize");
        CancelAction.Content = Strings.Get("MmDecline");
        CancelAction.Height = 36;
        AutomationProperties_SetIds();
        UseMod(mod);

        // A challenger has already said yes by challenging; he can still withdraw.
        if (iChallenged) MarkAccepted();
        PaintAccept();

        _tick.Tick += (_, _) => Paint();
        Loaded += (_, _) => { Paint(); _tick.Start(); };
        Closed += (_, _) => _tick.Stop();
        Closing += (_, e) =>
        {
            // The ✕ and Alt+F4 are a decline, unless the server already decided.
            if (!_closingQuietly && !_answered) { _answered = true; Declined?.Invoke(); }
        };
        Paint();
    }

    private static Brush B(string key) => (Brush)Application.Current.FindResource(key);

    /// <summary>The swords in green, the headline and the kind as a small gold (rated) or grey label.</summary>
    private static UIElement BuildHeader(string headline, string kindLine, bool rated)
    {
        var header = new Grid { Margin = new Thickness(0, -2, 0, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());

        var swords = new Path
        {
            Width = 20,
            Height = 20,
            StrokeThickness = 1.7,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Stroke = B("RmTextGreen"),
        };
        swords.SetResourceReference(Path.DataProperty, "ToastIconSwords");
        header.Children.Add(new Border
        {
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 0, 13, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = B("MmFoundToneFill"),
            BorderBrush = B("MmFoundToneRing"),
            BorderThickness = new Thickness(1),
            Child = swords,
        });

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { Text = headline, TextWrapping = TextWrapping.Wrap };
        title.SetResourceReference(StyleProperty, "DialogHeadline");
        title.SetResourceReference(TextBlock.FontSizeProperty, "MmFoundHeadlineSize");
        // The design's 20/1.2, from the size the text-size setting already applied.
        title.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        title.LineHeight = Math.Ceiling((double)Application.Current.FindResource("MmFoundHeadlineSize") * 1.2);
        System.Windows.Automation.AutomationProperties.SetAutomationId(title, "MatchFoundHeadline");
        words.Children.Add(title);

        var kind = new Grid { Margin = new Thickness(0, 5, 0, 0) };
        kind.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        kind.ColumnDefinitions.Add(new ColumnDefinition());
        kind.Children.Add(new Ellipse
        {
            Width = 6,
            Height = 6,
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Fill = B(rated ? "MmFoundKindRatedDot" : "UiDimText"),
        });
        var kindText = new TextBlock
        {
            Text = kindLine.ToUpper(Strings.Culture),
            FontWeight = FontWeights.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = B(rated ? "MmFoundKindRatedText" : "UiMutedText"),
        };
        kindText.SetResourceReference(TextBlock.FontSizeProperty, "SetGroupLabelSize");
        System.Windows.Automation.AutomationProperties.SetAutomationId(kindText, "MatchFoundKind");
        Grid.SetColumn(kindText, 1);
        kind.Children.Add(kindText);
        words.Children.Add(kind);

        Grid.SetColumn(words, 1);
        header.Children.Add(words);
        return header;
    }

    /// <summary>
    /// One player's tile: the face and the name over the line, then who accepted and, on whoever
    /// hosts, the HOST tag. The rival's is the mirror image. Mine wears the mod's colour.
    /// </summary>
    private static Border BuildTile(MatchDuelist who, AcceptMark mark, bool mine, bool mirrored, bool host,
        string? hostLine, Color accent)
    {
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = mirrored ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = mirrored ? GridLength.Auto : new GridLength(1, GridUnitType.Star) });
        who.Face.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(who.Face, mirrored ? 1 : 0);
        top.Children.Add(who.Face);

        var side = mirrored ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        var words = new StackPanel
        {
            Margin = mirrored ? new Thickness(0, 0, 10, 0) : new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var name = new TextBlock
        {
            Text = who.Name,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = side,
            Foreground = B("UiHeadText"),
        };
        name.SetResourceReference(TextBlock.FontSizeProperty, "MmFoundNameSize");
        System.Windows.Automation.AutomationProperties.SetAutomationId(name, mine ? "MatchFoundMyName" : "MatchFoundRivalName");
        words.Children.Add(name);
        if (!string.IsNullOrEmpty(who.Line))
        {
            // Wraps rather than trims: "In placement 3/10" is longer than the tile's text
            // column, and cutting it would hide the one figure in it.
            var line = new TextBlock
            {
                Text = who.Line,
                Margin = new Thickness(0, 3, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = mirrored ? TextAlignment.Right : TextAlignment.Left,
                HorizontalAlignment = side,
                Foreground = B("UiMutedText"),
            };
            line.SetResourceReference(TextBlock.FontSizeProperty, "SetControlSize");
            words.Children.Add(line);
        }
        Grid.SetColumn(words, mirrored ? 0 : 1);
        top.Children.Add(words);

        var bottom = new DockPanel { Margin = new Thickness(0, 9, 0, 0), MinHeight = 18, LastChildFill = false };
        DockPanel.SetDock(mark.Root, mirrored ? Dock.Right : Dock.Left);
        bottom.Children.Add(mark.Root);
        if (host)
        {
            var tagText = new TextBlock
            {
                Text = Strings.Get("MmFoundHostTag").ToUpper(Strings.Culture),
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = B("UiSoftText"),
            };
            tagText.SetResourceReference(TextBlock.FontSizeProperty, "SetTinySize");
            var tag = new Border
            {
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(7, 0, 7, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = B("MmFoundTagBg"),
                BorderBrush = B("MmFoundTagRim"),
                BorderThickness = new Thickness(1),
                Child = tagText,
                Tag = HostTag,
                // The sentence nobody reads with 20 seconds on the clock is one hover away.
                ToolTip = string.IsNullOrEmpty(hostLine) ? null : TooltipHelper.Wrap(hostLine),
            };
            DockPanel.SetDock(tag, mirrored ? Dock.Left : Dock.Right);
            bottom.Children.Add(tag);
        }

        var stack = new StackPanel();
        stack.Children.Add(top);
        stack.Children.Add(bottom);

        Brush fill, rim;
        if (mine)
        {
            var wash = new LinearGradientBrush(
                Color.FromArgb(0x42, accent.R, accent.G, accent.B),
                Color.FromArgb(0x1A, accent.R, accent.G, accent.B), 90);
            wash.Freeze();
            fill = wash;
            var edge = new SolidColorBrush(Color.FromArgb(0x99, accent.R, accent.G, accent.B));
            edge.Freeze();
            rim = edge;
        }
        else
        {
            fill = B("MmFoundTileBg");
            rim = B("MmFoundTileRim");
        }
        return new Border
        {
            Padding = new Thickness(13, 12, 13, 12),
            CornerRadius = new CornerRadius(12),
            Background = fill,
            BorderBrush = rim,
            BorderThickness = new Thickness(1),
            Child = stack,
        };
    }

    /// <summary>"Time to accept" and the seconds in large over the bar.</summary>
    private UIElement BuildClock()
    {
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _clockLabel.VerticalAlignment = VerticalAlignment.Bottom;
        _clockLabel.TextTrimming = TextTrimming.CharacterEllipsis;
        _clockLabel.Margin = new Thickness(0, 0, 10, 2);
        _clockLabel.Foreground = B("UiSoftText");
        _clockLabel.SetResourceReference(TextBlock.FontSizeProperty, "SetBodySize");
        head.Children.Add(_clockLabel);

        // One TextBlock, two runs: the figure and its unit share a baseline by construction.
        _secondsRun.FontWeight = FontWeights.Bold;
        _secondsRun.SetResourceReference(System.Windows.Documents.TextElement.FontFamilyProperty, "MonoFont");
        _secondsRun.SetResourceReference(System.Windows.Documents.TextElement.FontSizeProperty, "MmFoundClockSize");
        var unit = new System.Windows.Documents.Run(" s") { FontWeight = FontWeights.SemiBold };
        unit.SetResourceReference(System.Windows.Documents.TextElement.FontSizeProperty, "SetBodySize");
        ClockSeconds.Inlines.Add(_secondsRun);
        ClockSeconds.Inlines.Add(unit);
        System.Windows.Automation.AutomationProperties.SetAutomationId(ClockSeconds, "MatchFoundClock");
        Grid.SetColumn(ClockSeconds, 1);
        head.Children.Add(ClockSeconds);

        var bar = new Grid { Height = 8, Margin = new Thickness(0, 7, 0, 0) };
        bar.ColumnDefinitions.Add(_barDone);
        bar.ColumnDefinitions.Add(_barLeft);
        var track = new Border { CornerRadius = new CornerRadius(4), Background = B("MmFoundClockTrack") };
        Grid.SetColumnSpan(track, 2);
        bar.Children.Add(track);
        bar.Children.Add(ClockFill);

        var clock = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        clock.Children.Add(head);
        clock.Children.Add(bar);
        return clock;
    }

    /// <summary>The room's stake card in this window's dress (design 117); null hides it.</summary>
    public void ShowStake(FrameworkElement? card)
    {
        _stakeHost.Content = card;
        _stakeHost.Visibility = card == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void AutomationProperties_SetIds()
    {
        System.Windows.Automation.AutomationProperties.SetAutomationId(PrimaryAction, "MatchFoundAccept");
        System.Windows.Automation.AutomationProperties.SetAutomationId(CancelAction, "MatchFoundDecline");
    }

    /// <summary>
    /// How many of the two have said yes (<c>mm_accept_progress</c>). The server never says who;
    /// <see cref="MatchAcceptView"/> works it out and each tile is marked.
    /// </summary>
    public void SetProgress(int accepted, int total)
    {
        _acceptedCount = accepted;
        PaintAccept();
    }

    private void PaintAccept()
    {
        var (me, rival) = MatchAcceptView.From(_acceptedCount, _iAccepted, _rivalAccepted);
        _rivalAccepted = rival;
        _meMark.Set(me);
        _rivalMark.Set(rival);
        _clockLabel.Text = Strings.Get(me ? "MmFoundTimeLeft" : "MmFoundAcceptIn");
    }

    /// <summary>The server decided: close without answering again.</summary>
    public void CloseQuietly()
    {
        _closingQuietly = true;
        try { Close(); } catch (InvalidOperationException) { /* already closing */ }
    }

    protected override void OnPrimary()
    {
        if (_answered) return;
        _answered = true;
        MarkAccepted();
        PaintAccept();
        Accepted?.Invoke();
    }

    protected override void OnCancel()
    {
        // Accepted and then thought better of it is a decline too: the server takes the last word.
        _answered = true;
        Declined?.Invoke();
        CloseQuietly();
    }

    private void MarkAccepted()
    {
        _answered = true;
        _iAccepted = true;
        PrimaryAction.IsEnabled = false;
        PrimaryAction.Content = Strings.Format("MmWaitingFor", _rival);
        CancelAction.Content = Strings.Get("MmWithdraw");
    }

    private void Paint() => PaintAt(DateTime.UtcNow);

    /// <summary>The clock as of <paramref name="utcNow"/> (a parameter so a test can move time).</summary>
    internal void PaintAt(DateTime utcNow)
    {
        var left = Math.Max(0, (_deadlineUtc - utcNow).TotalMilliseconds);
        var share = Math.Clamp(left / _totalMs, 0, 1);
        _barDone.Width = new GridLength(share, GridUnitType.Star);
        _barLeft.Width = new GridLength(1 - share, GridUnitType.Star);
        var seconds = (int)Math.Ceiling(left / 1000);
        _secondsRun.Text = seconds.ToString(Strings.Culture);
        var low = seconds <= LowSeconds;
        ClockSeconds.SetResourceReference(TextBlock.ForegroundProperty, low ? "UiErrorText" : "UiHeadText");
        ClockFill.SetResourceReference(Border.BackgroundProperty, low ? "MmFoundClockLow" : "MmFoundClockBar");
        if (left <= 0) _tick.Stop();
    }

    /// <summary>"Accepted" with a green disc and a drawn tick, or "Pending" with an empty ring.</summary>
    private sealed class AcceptMark
    {
        private readonly Border _disc;
        private readonly Path _tick;
        private readonly TextBlock _text = new() { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };

        public StackPanel Root { get; }
        public bool On { get; private set; }

        public AcceptMark(bool mirrored)
        {
            _tick = new Path
            {
                Width = 11,
                Height = 11,
                Stretch = Stretch.None,
                StrokeThickness = 1.65,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                // The toast's tick, at 11 of its 20.
                Data = Geometry.Parse("M2.5,5.8 L4.6,7.9 L8.5,3.3"),
                Stroke = B("MpAppBg"),
            };
            _disc = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(8),
                VerticalAlignment = VerticalAlignment.Center,
                Child = _tick,
            };
            _text.SetResourceReference(TextBlock.FontSizeProperty, "SetControlSize");
            _text.Margin = mirrored ? new Thickness(0, 0, 6, 0) : new Thickness(6, 0, 0, 0);
            Root = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (mirrored) { Root.Children.Add(_text); Root.Children.Add(_disc); }
            else { Root.Children.Add(_disc); Root.Children.Add(_text); }
            Set(false);
        }

        public void Set(bool accepted)
        {
            On = accepted;
            _tick.Visibility = accepted ? Visibility.Visible : Visibility.Collapsed;
            _disc.Background = accepted ? B("UiOk") : Brushes.Transparent;
            _disc.BorderBrush = accepted ? null : B("MmFoundPendingRing");
            _disc.BorderThickness = new Thickness(accepted ? 0 : 1.5);
            _text.Text = Strings.Get(accepted ? "MmFoundAccepted" : "MmFoundPending");
            _text.Foreground = B(accepted ? "RmTextGreen" : "UiDimText");
        }
    }
}
