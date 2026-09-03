using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// A deck drawn as the game's own card art, shared by the two surfaces that show one: the mod
/// window's DECKS section and the multiplayer profile's. They differ only in tile size.
/// </summary>
public static class DeckTiles
{
    /// <summary>Big enough to recognise the art, small enough that 25 fit on two rows.</summary>
    public const int DefaultSize = 48;

    /// <summary>
    /// The deck as tiles, <b>in deck order and never sorted</b> — the order of the file IS the
    /// slot, the one thing this data carries that nothing else does.
    ///
    /// <para><b>Each tile carries its card's internal name in <c>Tag</c>, and that is
    /// load-bearing for the tests rather than for the UI.</b> Once a card is a picture its name
    /// is nowhere in the visual tree as text, so an order assertion that reads the rendered
    /// strings — which is how this was checked while a deck was a list of names — would go on
    /// passing while checking nothing at all.</para>
    /// </summary>
    public static IReadOnlyList<Button> Build(
        HomeCityDeckEntry deck,
        IReadOnlyDictionary<string, CardDetail> details,
        IReadOnlyDictionary<string, ImageSource> icons,
        int tileSize = DefaultSize,
        string rimBrush = "MpRimSoft")
    {
        // Built here rather than held in a static field: a ControlTemplate is SEALED the first
        // time it is applied and belongs to that thread for ever after, so a shared one throws
        // "the calling thread cannot access this object" the moment a second one uses it. One
        // per deck instead of one per tile is where the saving was anyway.
        var chrome = new ControlTemplate(typeof(Button))
        {
            VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)),
        };

        // An EMPTY style of its own, so the tiles do not pick up the app-wide implicit Button
        // style. Nothing of it would be visible — the template above replaces the chrome — but
        // applying it SEALS that shared style on whichever thread drew first, and the next
        // thread to read it throws "the calling thread cannot access this object". That is a
        // test-harness symptom today and a real one the day a deck is drawn off the UI thread.
        var bare = new Style(typeof(Button));

        var tiles = new List<Button>(deck.Cards.Count);
        foreach (var card in deck.Cards)
            tiles.Add(BuildTile(card, details, icons, tileSize, rimBrush, chrome, bare));

        return tiles;
    }

    /// <summary>
    /// One card, as a chromeless Button around the picture.
    ///
    /// <para><b>A Button and not a Border with a mouse handler, and that is not tidiness.</b>
    /// <c>MouseLeftButtonUp</c> on a Border can be swallowed by the surrounding ScrollViewer —
    /// the same reason the language cards are built this way. It did not matter while hovering
    /// also opened a card; now that selecting is the only way to read one, a swallowed click is
    /// a card that cannot be opened at all. <c>Button.Click</c> fires reliably, and keyboard
    /// focus comes with it.</para>
    /// </summary>
    private static Button BuildTile(
        HomeCityCard card,
        IReadOnlyDictionary<string, CardDetail> details,
        IReadOnlyDictionary<string, ImageSource> icons,
        int tileSize,
        string rimBrush,
        ControlTemplate chrome,
        Style bare)
    {
        details.TryGetValue(card.InternalName, out var detail);
        var name = detail?.Name ?? card.InternalName;

        var face = new Border
        {
            Width = tileSize,
            Height = tileSize,
            Background = (Brush)Application.Current.FindResource("MpField"),
            BorderBrush = (Brush)Application.Current.FindResource(rimBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = (CornerRadius)Application.Current.FindResource("RadiusSm"),
        };

        if (detail?.IconPath != null && icons.TryGetValue(detail.IconPath, out var icon))
        {
            var image = new Image { Source = icon, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            face.Child = image;
        }
        else
        {
            // No picture is a real outcome — a mod may ship a card whose art it never shipped —
            // so the tile says which card it is rather than sitting blank.
            face.Child = new TextBlock
            {
                Text = name.Length > 0 ? name.Substring(0, 1).ToUpperInvariant() : "?",
                Foreground = (Brush)Application.Current.FindResource("MpTextMuted"),
                FontSize = tileSize * 0.35,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        return new Button
        {
            Content = face,
            Style = bare,
            Template = chrome,
            Margin = new Thickness(0, 0, 5, 5),
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Tag = card.InternalName,
            ToolTip = TooltipHelper.Wrap(name),
        };
    }

    /// <summary>
    /// Marks the chosen tile. The rim changes COLOUR and never thickness: growing a border to 2
    /// shifts every child of the tile by a pixel the moment you click it.
    /// </summary>
    public static Border Select(Button tile, Border? previous, string rimBrush = "MpRimSoft")
    {
        if (previous != null)
            previous.BorderBrush = (Brush)Application.Current.FindResource(rimBrush);

        var face = (Border)tile.Content;
        face.BorderBrush = (Brush)Application.Current.FindResource("MpAction");
        return face;
    }
}
