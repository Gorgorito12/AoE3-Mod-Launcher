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
    /// A community-deck card: the art, and nothing else.
    ///
    /// <para>It was 104 x 118 with the name on two lines under the art, which is four times this
    /// area per card — and the cost was VERTICAL: the section ran to about 900 px and pushed its
    /// own last band off the bottom of the page. The game itself had already answered this, at
    /// ~52 with the name in the balloon, and the name below was also what PREVENTED the
    /// description: it spent the two lines the game reserves for what a card does, and it was cut
    /// anyway ("Recycling Railroad Tracks"). Thirty-four cards go from five rows to four.</para>
    /// </summary>
    public const int CardSize = 52;

    /// <summary>The gap between cards, matched by the band's own negative margin.</summary>
    public const int CardGap = 5;

    /// <summary>
    /// One card of the COMMUNITY deck: a 52 x 52 icon with the game's own gold frame, the
    /// shipment number in its corner, and NO name under it.
    ///
    /// <para><b>The name is not missing, it moved.</b> AoE 3 draws a deck exactly this way and
    /// puts the name and what the card does in the balloon on hover - which is also the only
    /// place either of them fits. See <see cref="CardSize"/> for what the old shape cost.</para>
    ///
    /// <para><b>Nothing here grows with text</b>, so a row of cards can never be pushed out of
    /// line by a long name. That was the whole reason the old card needed a pinned line height
    /// and a clipped second line.</para>
    ///
    /// <para>The rim is 1 px in BOTH states. A 2-px rim on the highlighted card would move its
    /// own art by a pixel the moment the pointer arrived; the frame changes COLOUR instead.</para>
    ///
    /// <para><c>Tag</c> carries the internal name, for the same reason the deck tiles do: a
    /// picture puts the name nowhere in the tree as text, so a test that wants to count the
    /// cards of a band has nothing else to count.</para>
    /// </summary>
    /// <param name="internalName">The card's internal name, kept on <c>Tag</c>.</param>
    /// <param name="labelMarkup">Only used for the placeholder initial when the mod ships no
    /// art - about one card in two hundred. Never printed on the card.</param>
    /// <param name="icon">The card's art, or null when the mod is not on disk to read it.</param>
    /// <param name="consensus">In the first band: the highlighted frame.</param>
    /// <param name="count">The figure the game paints in the corner, or null for a card that
    /// has none - which is 43% of them, and the game draws nothing there either.</param>
    /// <param name="tooltip">Already built by the caller.</param>
    public static Border BuildCard(
        string internalName,
        string? labelMarkup,
        ImageSource? icon,
        bool consensus,
        int? count = null,
        object? tooltip = null)
    {
        var face = new Grid { ClipToBounds = true };

        if (icon != null)
        {
            var image = new Image { Source = icon, Stretch = Stretch.UniformToFill };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            face.Children.Add(image);
        }
        else
        {
            // No picture is a real outcome - the mod may not be installed, or may ship a card
            // whose art it never shipped - so the tile says which card it is rather than
            // sitting blank. Same placeholder the deck tiles use.
            var plain = GameText.Clean(labelMarkup) ?? "";
            var initial = new TextBlock
            {
                Text = plain.Length > 0 ? plain.Substring(0, 1).ToUpperInvariant() : "?",
                FontSize = CardSize * 0.4,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            initial.SetResourceReference(TextBlock.ForegroundProperty, "MpTextMuted");
            face.Children.Add(initial);
        }

        if (count.HasValue)
        {
            // White on whatever the art happens to be, so it carries its own contrast: the game
            // uses a shadow for exactly this and the art underneath is not ours to choose.
            var number = new TextBlock
            {
                Text = count.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 3, 2),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 2,
                    ShadowDepth = 1,
                    Direction = 270,
                    Color = Colors.Black,
                    Opacity = 1,
                },
            };
            face.Children.Add(number);
        }

        var card = new Border
        {
            Width = CardSize,
            Height = CardSize,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, CardGap, CardGap),
            // The clip is what rounds the ART's corners: a Border's CornerRadius rounds its rim
            // and leaves a child painting square over it.
            Clip = new RectangleGeometry(new Rect(0, 0, CardSize, CardSize), 3, 3),
            CornerRadius = new CornerRadius(3),
            Child = face,
            Tag = internalName,
            ToolTip = tooltip,
        };
        card.SetResourceReference(Border.BackgroundProperty, "MpPanelDim");
        // The game's own frame. Gold for every card, the blue of this tab for the one being
        // pointed at - never a different thickness.
        card.SetResourceReference(Border.BorderBrushProperty, consensus ? "MpCardFrameOn" : "MpCardFrame");

        if (tooltip != null)
        {
            // Revealed, not waited for. WPF's default InitialShowDelay is a full SECOND, and on a
            // 52-px icon that reads as "there is nothing here" - which is exactly how it was
            // reported. The card carries the name, the age and what the card does, so it is the
            // only thing the icon says; a delay in front of it is a delay in front of everything.
            // Same pair, and the same reason, as the MOST PLAYED flags in MultiplayerTab.
            ToolTipService.SetInitialShowDelay(card, 0);
            // And it STAYS while it is read: the default five seconds is less than a dozen effect
            // lines take, so the balloon used to close underneath the reader.
            ToolTipService.SetShowDuration(card, 30_000);
        }

        return card;
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
