using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher;

/// <summary>
/// A match being played, as the person RUNNING the tournament sees it.
///
/// <para><b>Deliberately not <see cref="LobbyWindow"/>.</b> That window is the room you are IN,
/// and two thirds of it — Ready, Start, Leave, the Record Game card, the peer pings, the Radmin
/// address, the abandonment warning — exists because you are about to play. Somebody supervising
/// is not a member and none of it is theirs. This shows the four things they came for: which
/// bracket slot this is, who is in it, what is being said, and the result when it lands.</para>
///
/// <para><b>Preview surface, and it says so on itself.</b> The server refuses this three times
/// over: a lobby in <c>in_game</c> rejects every join before it looks at seats or roles, a lobby
/// bound to a bracket slot admits only that slot's entrants with no owner exemption, and
/// tournament rooms are created with zero spectator slots. So nothing here talks to a server —
/// it is filled from <see cref="TournamentDemoData"/> and reached only from the fabricated
/// tournaments, which is what makes it something to argue about before any of those three doors
/// is opened.</para>
///
/// <para><b>The roster is read off the BRACKET, not off a second fixture.</b> Who is meant to be
/// in this room is exactly the two entrants the bracket froze, so taking the names from anywhere
/// else would let a sample drift into showing a room whose occupants do not match the slot —
/// which is the one thing an organiser opens this to check.</para>
/// </summary>
public partial class MatchWatchWindow : Window
{
    private readonly TournamentDetail _tournament;
    private readonly TournamentMatch _match;
    private readonly TournamentDemoData.MatchWatchSample _sample;

    /// <summary>
    /// internal, because the sample it takes is: the fixture types live beside the fabricated
    /// tournaments and nothing outside this assembly has any business constructing one.
    /// </summary>
    internal MatchWatchWindow(
        TournamentDetail tournament,
        TournamentMatch match,
        TournamentDemoData.MatchWatchSample sample)
    {
        _tournament = tournament;
        _match = match;
        _sample = sample;

        InitializeComponent();
        ApplyStrings();
        Render();
    }

    /// <summary>
    /// What the organiser pressed to get here, so pressing it again reads the same.
    ///
    /// <para>Composed rather than stored: the round's name already has one owner in
    /// <c>BracketLayout.RoundLabelKey</c>, and a second copy of "Semifinal" is a second thing to
    /// keep in step with the bracket beside it.</para>
    /// </summary>
    private string MatchHeading()
    {
        // Lowercased, as MyPlayableRound does with the same keys: those strings are
        // BRACKET COLUMN headers and are written in caps for that job. Dropped into a
        // sentence they shout.
        var round = Strings.Format(
            BracketLayout.RoundLabelKey(_match.Round, _tournament.RoundsTotal), _match.Round)
            .ToLowerInvariant();
        var a = NameOf(_match.Entrant1Id);
        var b = NameOf(_match.Entrant2Id);
        return $"{round} · {a} vs {b}";
    }

    private string NameOf(string? entrantId)
        => _tournament.Entrants?
               .FirstOrDefault(e => string.Equals(e.Id, entrantId, StringComparison.Ordinal))?
               .DisplayName
           ?? "—";

    private void ApplyStrings()
    {
        Title = Strings.Get("MpWatchWindowTitle");
        TitleBarControl.Title = Strings.Get("MpWatchWindowTitle");
        RosterLabel.Text = Strings.Get("MpWatchRoster");
        ChatLabel.Text = Strings.Get("MpWatchChat");
        ChatSendButton.Content = Strings.Get("MpWatchSend");
        CloseBtn.Content = Strings.Get("DlgClose");
        PreviewNote.Text = Strings.Get("MpWatchPreviewNote");
    }

    private void Render()
    {
        MatchLabel.Text = MatchHeading();
        RoomTitleText.Text = _sample.RoomTitle;
        ModText.Text = _sample.ModName;
        StateText.Text = Strings.Format("MpWatchInGameFor", _sample.StartedMinutesAgo);

        RenderRoster();
        RenderChat();

        // Nothing has been decided, so nothing is shown. An empty result box would read as a
        // result the organiser was not allowed to see.
        ResultBox.Visibility = Visibility.Collapsed;
    }

    /// <summary>The two sides the bracket froze, in bracket order, seed first.</summary>
    private void RenderRoster()
    {
        RosterPanel.Children.Clear();
        foreach (var id in new[] { _match.Entrant1Id, _match.Entrant2Id })
        {
            var entrant = _tournament.Entrants?
                .FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal));
            if (entrant == null) continue;
            RosterPanel.Children.Add(RosterRow(entrant));
        }
    }

    private UIElement RosterRow(TournamentEntrant entrant)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 7) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Monospaced and fixed width, exactly as the bracket draws it, so the seeds line up
        // down the column instead of wandering with the name beside them.
        var seed = new TextBlock
        {
            Text = entrant.Seed?.ToString() ?? "—",
            Width = 20,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        seed.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        seed.SetResourceReference(TextBlock.FontSizeProperty, "MpPillSize");
        seed.SetResourceReference(TextBlock.ForegroundProperty, "MpTextGhost");
        Grid.SetColumn(seed, 0);
        grid.Children.Add(seed);

        var name = new TextBlock
        {
            Text = entrant.DisplayName,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        name.SetResourceReference(TextBlock.FontSizeProperty, "MpBodySize");
        name.SetResourceReference(TextBlock.ForegroundProperty, "MpTextPrimary");
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);

        return grid;
    }

    private void RenderChat()
    {
        ChatPanel.Children.Clear();
        foreach (var line in _sample.Chat)
        {
            var block = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6),
            };
            block.SetResourceReference(TextBlock.FontSizeProperty, "MpLabelSize");
            block.SetResourceReference(TextBlock.ForegroundProperty, "MpTextBody");

            var who = new System.Windows.Documents.Run(line.Author + "  ")
            {
                FontWeight = FontWeights.SemiBold,
            };
            who.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty,
                                     "MpTextMuted");
            block.Inlines.Add(who);
            block.Inlines.Add(new System.Windows.Documents.Run(line.Text));
            ChatPanel.Children.Add(block);
        }
    }

    /// <summary>
    /// Inert, like every other action under the fabricated tournaments.
    ///
    /// <para>The box is drawn anyway rather than hidden, because an organiser who can watch an
    /// argument and cannot answer it is not refereeing — what is being previewed here is a
    /// surface that would send, not one that would only listen.</para>
    /// </summary>
    private async void ChatSendButton_Click(object sender, RoutedEventArgs e)
    {
        ChatInputBox.Text = "";
        DiagnosticLog.Write("Match watch: demo chat send pressed; nothing was sent.");
        await Controls.MpAlertOverlay.NoticeAsync(
            RootGrid,
            Strings.Get("MpTournamentDemoInertTitle"),
            Strings.Get("MpTournamentDemoInert"),
            Strings.Get("MpAlertOk"));
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
}
