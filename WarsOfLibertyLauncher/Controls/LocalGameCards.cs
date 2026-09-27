using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The cards that describe the player's own games: one per recording against people, one per
/// stored game against the AI, and the saved deck a recording can carry.
///
/// <para>Built in code and shared by the mod window's STATISTICS section and the multiplayer
/// Profile's MATCHES section, so the same match cannot look different on the two.</para>
/// </summary>
/// <remarks>
/// <c>internal static</c> so <c>DialogXamlTests</c> can build the real thing rather than a
/// hand-copied imitation: no compile step checks a resource looked up by name, and neither
/// surface is one the startup smoke test ever opens. Static costs nothing — every brush it reads
/// is app-wide.
/// </remarks>
internal static class LocalGameCards
{
    /// <summary>How many unit types a card lists before it stops being a card and becomes a table.</summary>
    private const int TopUnitsPerCard = 8;

    /// <summary>One game against the AI, as a card.</summary>
    internal static Border BuildAiGameCard(
        AiGameRecord game, IReadOnlyDictionary<string, string> names)
    {
        var caption = (double)Application.Current.FindResource("FontSizeCaption");
        var stack = new StackPanel();

        // Result and length. Won is null on a block that did not carry the field, and then the
        // card says nothing about the outcome rather than guessing at one.
        var minutes = Math.Max(1, (int)Math.Round(game.DurationMs / 60000.0));
        var headline = new TextBlock
        {
            Foreground = (Brush)Application.Current.FindResource("MpTextPrimary"),
            FontSize = (double)Application.Current.FindResource("FontSizeBodyStrong"),
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        if (game.Won.HasValue)
        {
            headline.Inlines.Add(new Run(
                Strings.Get(game.Won.Value ? "ModPropStatsWon" : "ModPropStatsLost"))
            {
                Foreground = (Brush)Application.Current.FindResource(game.Won.Value ? "MpOk" : "MpDestructiveText"),
            });
            headline.Inlines.Add(new Run("  ·  "));
        }
        headline.Inlines.Add(new Run(
            Strings.Format("ModPropStatsDuration", minutes)));

        // When it was played. Through the same helper the chat's day divider uses, so the two
        // cannot disagree about when a day stops being "yesterday" or when the year is worth
        // printing — and so the month names follow the launcher's language rather than the OS.
        if (DateTime.TryParse(game.CapturedAtUtc, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var when))
        {
            var local = when.ToLocalTime();
            headline.Inlines.Add(new Run("  ·  "
                + ChatTimeFormat.DateLabel(
                    local, DateTime.Today,
                    Strings.Get("MpChatToday"), Strings.Get("MpChatYesterday"),
                    System.Globalization.CultureInfo.GetCultureInfo(
                        Strings.Language == Strings.LangEs ? "es" : "en")))
            {
                Foreground = (Brush)Application.Current.FindResource("OnSecondaryContainer"),
                FontWeight = FontWeights.Normal,
            });
        }

        stack.Children.Add(headline);

        // The totals, and ONLY the ones that were recorded. Zero here does not mean "gathered
        // nothing" — every game but the newest in a personality file has its totals wiped, so a
        // game imported from before the launcher started harvesting has real unit counts and no
        // resources at all. Printing "0 shipments" for those would be a statement, and a false one.
        var facts = new List<string>();
        if (game.Shipments > 0) facts.Add(Strings.Format("ModPropStatsShipments", game.Shipments));
        if (game.Score > 0) facts.Add(Strings.Format("ModPropStatsScore", game.Score.ToString("N0")));
        var resources = (long)game.Gold + game.Wood + game.Food;
        if (resources > 0) facts.Add(Strings.Format("ModPropStatsResources", resources.ToString("N0")));
        if (game.Xp > 0) facts.Add(Strings.Format("ModPropStatsXp", game.Xp.ToString("N0")));

        if (facts.Count > 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = string.Join("  ·  ", facts),
                Foreground = (Brush)Application.Current.FindResource("OnSecondaryContainer"),
                FontSize = caption,
                Margin = new Thickness(0, 3, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        // The units, biggest first. This is the part that is filled in for EVERY stored game.
        var top = game.Units
            .OrderByDescending(u => u.Value)
            .ThenBy(u => u.Key, StringComparer.Ordinal)
            .Take(TopUnitsPerCard)
            .Select(u => Strings.Format(
                "ModPropStatsUnitCount",
                names.TryGetValue(u.Key, out var pretty) ? pretty : u.Key,
                u.Value))
            .ToList();

        if (top.Count > 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = string.Join("   ", top),
                Foreground = (Brush)Application.Current.FindResource("MpTextMuted"),
                FontSize = caption,
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        return new Border
        {
            Child = stack,
            Padding = new Thickness(14, 11, 14, 12),
            Margin = new Thickness(0, 0, 0, 8),
            Background = (Brush)Application.Current.FindResource("MpSurfaceAlt"),
            BorderBrush = (Brush)Application.Current.FindResource("MpRimSoft"),
            BorderThickness = new Thickness(1),
            CornerRadius = (CornerRadius)Application.Current.FindResource("RadiusMd"),
        };
    }

    /// <summary>
    /// One local match, with its saved deck folded under it when there is one.
    /// </summary>
    internal static Border BuildHumanGameCard(
        LocalMatchRow row,
        Func<IReadOnlyList<HomeCityProfile>, System.Threading.Tasks.Task<(
            IReadOnlyDictionary<string, CardDetail> Details,
            IReadOnlyDictionary<string, ImageSource> Icons)>> resolveArt)
        => BuildHumanGameCard(
            row.FileName, row.PlayedLocal, row.Map, row.Players, row.LocalSlot, row.Result,
            row.LoserSlot, row.WinnerSlot, row.Civs,
            BuildDeckSnapshotSection(row.Decks, resolveArt));

    /// <summary>One local match, as a card.</summary>
    internal static Border BuildHumanGameCard(
        string fileName,
        DateTime playedLocal,
        string map,
        IReadOnlyList<ReplayParserService.ReplayPlayer> players,
        int localSlot,
        double? result,
        int loserSlot,
        int winnerSlot,
        IReadOnlyDictionary<int, string> civs,
        UIElement? deckSection = null)
    {
        var caption = (double)Application.Current.FindResource("FontSizeCaption");
        var stack = new StackPanel();

        var headline = new TextBlock
        {
            Foreground = (Brush)Application.Current.FindResource("MpTextPrimary"),
            FontSize = (double)Application.Current.FindResource("FontSizeBodyStrong"),
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };

        // No result is the common case past a 1v1, and it is drawn as SILENCE. A "Draw" badge
        // would be a claim, and the recording never makes it.
        if (result.HasValue)
        {
            var won = result.Value >= 1.0;
            headline.Inlines.Add(new Run(
                Strings.Get(won ? "ModPropStatsWon" : "ModPropStatsLost"))
            {
                Foreground = (Brush)Application.Current.FindResource(won ? "MpOk" : "MpDestructiveText"),
            });
            headline.Inlines.Add(new Run("  ·  "));
        }

        headline.Inlines.Add(new Run(
            map.Length > 0 ? map : Strings.Get("ModPropHumanMapUnknown")));

        // Through the same helper the chat's day divider uses, so the two cannot disagree about
        // when a day stops being "yesterday", and the month names follow the launcher's language
        // rather than the operating system's.
        headline.Inlines.Add(new Run("  ·  "
            + ChatTimeFormat.DateLabel(
                playedLocal, DateTime.Today,
                Strings.Get("MpChatToday"), Strings.Get("MpChatYesterday"),
                System.Globalization.CultureInfo.GetCultureInfo(
                    Strings.Language == Strings.LangEs ? "es" : "en")))
        {
            Foreground = (Brush)Application.Current.FindResource("OnSecondaryContainer"),
            FontWeight = FontWeights.Normal,
        });

        stack.Children.Add(headline);

        foreach (var player in players)
        {
            if (!player.IsHuman) continue;

            var facts = new List<string> { player.Name };
            if (civs.TryGetValue(player.Civilization, out var civ)) facts.Add(civ);
            if (!string.IsNullOrWhiteSpace(player.Explorer)) facts.Add(player.Explorer);
            if (player.HomeCityLevel > 0)
                facts.Add(Strings.Format("ModPropDecksLevel", player.HomeCityLevel));

            var city = LocalMatchView.HomeCityFrom(player.HomeCityFile);
            if (city.Length > 0) facts.Add(city);

            var mine = player.Slot == localSlot;
            var line = new TextBlock
            {
                Foreground = (Brush)Application.Current.FindResource(
                    mine ? "MpTextPrimary" : "OnSecondaryContainer"),
                FontSize = caption,
                FontWeight = mine ? FontWeights.SemiBold : FontWeights.Normal,
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            };

            // Marked per PLAYER rather than only for the viewer, because most of what a player
            // keeps is other people's recordings — a match somebody sent them, which they are
            // not in. Saying nothing there threw away the one fact the file does carry.
            var verdict = player.Slot == loserSlot ? "ModPropHumanLost"
                        : player.Slot == winnerSlot ? "ModPropHumanWon"
                        : null;

            if (verdict != null)
            {
                line.Inlines.Add(new Run(Strings.Get(verdict))
                {
                    Foreground = (Brush)Application.Current.FindResource(
                        verdict == "ModPropHumanWon" ? "MpOk" : "MpDestructiveText"),
                    FontWeight = FontWeights.SemiBold,
                });
                line.Inlines.Add(new Run("  ·  "));
            }

            line.Inlines.Add(new Run(string.Join("  ·  ", facts)));
            stack.Children.Add(line);
        }

        // The file, because AoE3 names every recording "Record Game N" and renumbers them, so
        // this is the only way to know which one on disk this row is.
        stack.Children.Add(new TextBlock
        {
            Text = fileName,
            Foreground = (Brush)Application.Current.FindResource("MpTextMuted"),
            FontFamily = new FontFamily("Consolas"),
            FontSize = caption,
            Margin = new Thickness(0, 6, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        if (deckSection != null) stack.Children.Add(deckSection);

        return new Border
        {
            Child = stack,
            Padding = new Thickness(14, 11, 14, 12),
            Margin = new Thickness(0, 0, 0, 8),
            Background = (Brush)Application.Current.FindResource("MpSurfaceAlt"),
            BorderBrush = (Brush)Application.Current.FindResource("MpRimSoft"),
            BorderThickness = new Thickness(1),
            CornerRadius = (CornerRadius)Application.Current.FindResource("RadiusMd"),
        };
    }

    /// <summary>
    /// The decks the viewer brought to THIS match, kept when it ended.
    ///
    /// <para><b>Folded away until asked for</b>, twice over. A match card that grew 25 tiles by
    /// itself would bury the list, and opening the deck needs the mod's card names — a 12 MB
    /// scan — which nobody should pay for merely opening the list.</para>
    ///
    /// <para>Null for every match played before snapshots existed, and for anyone else's
    /// recording: only the viewer's own home city files are on this disk.</para>
    /// </summary>
    /// <remarks>
    /// The art is passed in as a callback so a test can build the real thing. It is not
    /// decoration: the first version applied <c>SetActionQuiet</c> — a <c>TextBlock</c> style —
    /// to a <c>Button</c>, which throws, and because this runs inside the STATISTICS load it took
    /// BOTH groups of that page down with it. Every test passed: nothing built this element.
    /// </remarks>
    internal static UIElement? BuildDeckSnapshotSection(
        IReadOnlyList<HomeCityProfile>? decks,
        Func<IReadOnlyList<HomeCityProfile>, System.Threading.Tasks.Task<(
            IReadOnlyDictionary<string, CardDetail> Details,
            IReadOnlyDictionary<string, ImageSource> Icons)>> resolveArt)
    {
        if (decks == null || decks.Count == 0) return null;

        var host = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        var show = new Button
        {
            Content = Strings.Get("ModPropHumanDeckShow"),
            Style = (Style)Application.Current.FindResource("SetActionButtonSm"),
            HorizontalAlignment = HorizontalAlignment.Left,

            // The shared action buttons are FIXED width — 88 px for the small one — because they
            // line up in a column on the settings pages. This one sits alone under a sentence,
            // has nothing to line up with, and a caption that says what it opens does not fit in
            // 88 px in either language; a Button cannot ellipsise its own text, so it would just
            // be cut. NaN restores sizing to content, and a local value beats the style's setter.
            Width = double.NaN,
            Padding = new Thickness(12, 0, 12, 0),
        };

        show.Click += async (_, _) =>
        {
            show.IsEnabled = false;
            show.Content = Strings.Get("ModPropHumanDeckLoading");

            var (details, icons) = await resolveArt(decks);

            host.Children.Remove(show);

            // Says the two things that would otherwise be read as a claim: these are the cards
            // as they were that day, and the game does not record WHICH of a city's decks was
            // used — so every deck of it is shown rather than one of them picked.
            host.Children.Add(new TextBlock
            {
                Text = Strings.Get("ModPropHumanDeckNote"),
                Foreground = (Brush)Application.Current.FindResource("MpTextMuted"),
                FontSize = (double)Application.Current.FindResource("FontSizeCaption"),
                Margin = new Thickness(0, 0, 0, 6),
                TextWrapping = TextWrapping.Wrap,
            });

            foreach (var profile in decks)
                foreach (var deck in profile.Decks)
                {
                    host.Children.Add(new TextBlock
                    {
                        Text = string.IsNullOrWhiteSpace(deck.Name) ? profile.CityName : deck.Name,
                        Foreground = (Brush)Application.Current.FindResource("OnSecondaryContainer"),
                        FontSize = (double)Application.Current.FindResource("FontSizeCaption"),
                        Margin = new Thickness(0, 2, 0, 3),
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    });

                    var grid = new WrapPanel { MaxWidth = 560, Margin = new Thickness(0, 0, 0, 6) };
                    foreach (var tile in DeckTiles.Build(deck, details, icons, 34, "MpRimFaint"))
                        grid.Children.Add(tile);

                    host.Children.Add(grid);
                }
        };

        host.Children.Add(show);
        return host;
    }
}
