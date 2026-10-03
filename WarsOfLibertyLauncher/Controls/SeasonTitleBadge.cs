using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The medal a top-3 finish in an ENDED rating season earns: a small gold, silver or bronze
/// disc with the season's number inside, drawn right after a player's name.
///
/// <para><b>One builder for every surface</b> — the ranking rows, the room roster, the Players
/// panel and the profile header — so the four cannot come to disagree about what a medal looks
/// like or what its tooltip says. Which medal a player shows when he has several is decided by
/// the SERVER (newest season, then the better place, then 1v1), never here.</para>
///
/// <para><b>It is not a rank badge and must not be mistaken for one.</b> The shield says where a
/// player stands NOW; the medal says where he FINISHED, once, in a season that is over. Its root
/// carries the <see cref="SeasonTitleInfo"/> as its <c>Tag</c> — never a <see cref="RankAge"/>,
/// which is what the tests and the badge walkers look for, and never a string, which is what
/// <c>RefreshRosterLiveCells</c> reads as a player id.</para>
///
/// <para>No <c>Effect</c> anywhere: the numeral is text, and an Effect on an ancestor of text
/// takes its ClearType away — the launcher-wide rule.</para>
/// </summary>
public static class SeasonTitleBadge
{
    /// <summary>
    /// The medal, or null when there is nothing honest to draw: no title, or one whose place is
    /// not a top-3 finish. Null rather than an empty element so every caller can skip both the
    /// element and the width it would have reserved.
    /// </summary>
    /// <param name="size">The disc's diameter in DIP.</param>
    public static FrameworkElement? Build(SeasonTitleInfo? title, double size)
    {
        if (title == null || !SeasonView.IsDrawable(title)) return null;

        var (fill, rim, ink) = SeasonView.MedalFor(title.Place) switch
        {
            SeasonMedal.Gold => ("SeasonMedalGold", "SeasonMedalGoldRim", "SeasonMedalGoldInk"),
            SeasonMedal.Silver => ("SeasonMedalSilver", "SeasonMedalSilverRim", "SeasonMedalSilverInk"),
            _ => ("SeasonMedalBronze", "SeasonMedalBronzeRim", "SeasonMedalBronzeInk"),
        };

        var root = new Grid
        {
            Width = size,
            Height = size,
            VerticalAlignment = VerticalAlignment.Center,
            Tag = title,
            // A hit-test target for the tooltip over the whole disc, not only over the glyph.
            Background = Brushes.Transparent,
            SnapsToDevicePixels = true,
        };
        root.Children.Add(new Ellipse
        {
            Fill = Resource(fill),
            Stroke = Resource(rim),
            StrokeThickness = Math.Max(1, Math.Round(size / 14, 1)),
        });

        // The season's number fills the disc — geometry, not type, like the monograms that fill
        // an avatar: it is sized to its container and does not follow the text-size setting,
        // which would push a two-digit season out of a 15-px disc.
        var digits = title.Season.ToString(System.Globalization.CultureInfo.InvariantCulture);
        root.Children.Add(new TextBlock
        {
            Text = digits,
            FontSize = Math.Round(size * (digits.Length > 1 ? 0.48 : 0.6), 1),
            FontWeight = FontWeights.Bold,
            Foreground = Resource(ink),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            // Optical centring: Segoe UI's digits sit a hair low in their line box.
            Margin = new Thickness(0, 0, 0, Math.Round(size / 16, 1)),
        });

        root.ToolTip = TooltipHelper.Wrap(TooltipText(title));
        ToolTipService.SetInitialShowDelay(root, 150);
        return root;
    }

    /// <summary>"1st place in Season 1 (1v1)" — the medal explained in words, for its tooltip.</summary>
    public static string TooltipText(SeasonTitleInfo title)
        => Strings.Format("MpSeasonMedalTip", PlaceWords(title.Place), title.Season, ModeWord(title.IsTeam));

    /// <summary>"1st place" / "1.er puesto", for the three places that earn a medal.</summary>
    public static string PlaceWords(int place) => place switch
    {
        1 => Strings.Get("MpSeasonPlace1"),
        2 => Strings.Get("MpSeasonPlace2"),
        3 => Strings.Get("MpSeasonPlace3"),
        _ => "#" + place,
    };

    /// <summary>The ladder's own word: "1v1" or "Teams".</summary>
    public static string ModeWord(bool team) => Strings.Get(team ? "MpBadgeModeTeams" : "MpBadgeMode1v1");

    private static Brush Resource(string key)
        => Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
}
