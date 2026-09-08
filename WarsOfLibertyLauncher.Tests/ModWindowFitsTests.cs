using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The mod window looks the same whichever mod it is about, and whichever language it is in.
///
/// <para><b>Both halves were reported from screenshots of the same window.</b> With "Age of
/// Empires III: The Asian Dynasties" the name came out as "Age of Empires II…", because the
/// column beside it holds a STATUS that is sometimes a sentence ("detected — ready to play")
/// in the type size of a version number, and it was free to take as much width as it wanted.
/// And the version row's Install button shipped clipped as "Instalar esta versió": these
/// action buttons are a fixed 88 / 112 / 132 px on purpose — a column whose width follows its
/// caption zigzags down the page — so the caption is what has to fit, and nobody was checking
/// that it did.</para>
/// </summary>
[Collection("wpf-and-language")]
public class ModWindowFitsTests
{
    private static ModPropertiesDialog Build()
    {
        var config = new LauncherConfig();
        var profile = WarsOfLibertyLauncher.Services.ModRegistry.Default;
        return new ModPropertiesDialog(
            profile,
            new WarsOfLibertyLauncher.Services.UpdateService(config, profile),
            config,
            translationIndex: null,
            applyTranslation: _ => { },
            revertToEnglish: () => { },
            openVerify: () => { },
            openRepair: () => { },
            checkForUpdates: () =>
                Task.FromResult<WarsOfLibertyLauncher.Services.UpdateService.CheckResult?>(null),
            openAoE3Folder: () => { },
            changeModFolder: () => { },
            changeAoE3Folder: () => { },
            openUserDataFolder: () => { },
            createBackup: () => null,
            restoreBackup: () => null,
            viewLogs: () => { },
            shareDiagnostics: () => { },
            uninstall: () => { });
    }

    /// <summary>
    /// EVERY caption fits its button, in both languages. Walked off the built window rather
    /// than from a list of keys, so a button added tomorrow — or a translation that grows —
    /// is covered without touching this test.
    /// </summary>
    [Theory]
    [InlineData("es", "mod")]
    [InlineData("en", "mod")]
    [InlineData("es", "settings")]
    [InlineData("en", "settings")]
    public void EveryActionButtonCaptionFitsItsFixedWidth(string language, string window)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            var previous = Strings.Language;
            try
            {
                Strings.SetLanguage(language);
                // BOTH windows that use this button family, because the family is the point:
                // the launcher's own settings share every one of these styles with the mod's.
                DependencyObject dlg = window == "mod"
                    ? Build()
                    : new LauncherSettingsDialog(new LauncherConfig());

                var tooWide = new List<string>();
                var examined = 0;
                foreach (var b in Descendants(dlg).OfType<Button>())
                {
                    // Only the fixed-width family: everything else is free to size itself.
                    if (double.IsNaN(b.Width) || b.Width <= 0) continue;
                    if (b.Content is not string caption || string.IsNullOrWhiteSpace(caption)) continue;
                    examined++;

                    var (needed, room) = Fit(b, WarsOfLibertyLauncher.Services.TextScale.MaxFactor);
                    if (needed > room)
                    {
                        tooWide.Add(
                            $"'{caption}' needs {needed:F0} px and has {room:F0} "
                            + $"(button {b.Name}, width {b.Width})");
                    }
                }

                // A tree walk that finds nothing passes every assertion after it. This window
                // has eleven of these buttons; anything near zero means the walk stopped
                // seeing them and the test has quietly stopped testing.
                Assert.True(examined >= 5,
                    $"only {examined} fixed-width buttons were examined in the {window} "
                    + "window — the walk is not reaching them any more, so nothing below "
                    + "this line means anything.");

                Assert.True(tooWide.Count == 0,
                    $"These captions are clipped in the {window} window in '{language}' at "
                    + "the largest text size: " + string.Join("; ", tooWide)
                    + ". The widths are deliberate — shorten the caption, not the button.");
            }
            finally { Strings.SetLanguage(previous); }
        });

        Assert.Null(error);
    }

    /// <summary>
    /// The identity card cannot be rearranged by the mod it is about: the name has a floor,
    /// the status has a ceiling, and the author line is one line however long the site is.
    /// </summary>
    [Fact]
    public void TheIdentityCardIsTheSameShapeForEveryMod()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            var dlg = Build();

            var name = (StackPanel)dlg.ValName.Parent;
            var status = (StackPanel)dlg.ValVersion.Parent;
            var card = (Grid)name.Parent;

            // Two thirds and one third: a proportion cannot starve either side at any width,
            // where the Auto column this replaces could take everything.
            Assert.Equal(2d, card.ColumnDefinitions[1].Width.Value);
            Assert.Equal(GridUnitType.Star, card.ColumnDefinitions[1].Width.GridUnitType);
            Assert.Equal(1d, card.ColumnDefinitions[2].Width.Value);
            Assert.Equal(GridUnitType.Star, card.ColumnDefinitions[2].Width.GridUnitType);
            Assert.Equal(200d, card.ColumnDefinitions[2].MaxWidth);
            Assert.Equal(TextWrapping.Wrap, dlg.ValVersion.TextWrapping);

            // One line, ellipsised: the rail's footer carries the author and the site in full.
            Assert.Equal(TextWrapping.NoWrap, dlg.ValAuthor.TextWrapping);
            Assert.Equal(TextTrimming.CharacterEllipsis, dlg.ValAuthor.TextTrimming);
            Assert.NotNull(dlg.RailAuthorText);
            Assert.NotNull(dlg.RailSiteText);

            // The longest name and the longest status at once, at the window's own minimum
            // width: the name still gets its floor, so it is trimmed rather than erased.
            dlg.ValName.Text = "Age of Empires III: The Asian Dynasties";
            dlg.ValVersion.Text = "detectado — listo para jugar";
            dlg.ValAuthor.Text = "Ensemble Studios, Big Huge Games  ·  "
                               + "https://www.ageofempires.com/games/aoeiii/";

            var room = dlg.MinWidth - 360;
            card.Measure(new Size(room, double.PositiveInfinity));
            card.Arrange(new Rect(0, 0, room, card.DesiredSize.Height));
            card.UpdateLayout();

            Assert.True(dlg.ValName.ActualWidth >= 180,
                $"the name was squeezed to {dlg.ValName.ActualWidth:F0} px by the status beside it");
            Assert.True(status.ActualWidth <= 200,
                $"the status took {status.ActualWidth:F0} px of a card that is not its own");
            // And nothing spills past the edge of the card, which is what a pair of minimum
            // widths would have done at this size.
            Assert.True(card.DesiredSize.Width <= room + 0.5,
                $"the card wants {card.DesiredSize.Width:F0} px of {room:F0} and would clip");
        });

        Assert.Null(error);
    }

    /// <summary>
    /// What the caption needs and what the button has, in DIPs, at a given text size.
    ///
    /// <para><paramref name="scale"/> is the launcher's text-size setting, and measuring at
    /// the LARGEST one is the whole point: the widths deliberately do not scale — there is a
    /// test forbidding it, because a setting that grows the boxes too is the zoom this one
    /// exists instead of — so the caption is what has to fit at every size the setting
    /// offers. At 100 % "Instalar esta versión" fits by four pixels, which is why it shipped:
    /// the person who reported it reads the launcher at 110 %.</para>
    /// </summary>
    private static (double Needed, double Room) Fit(Button b, double scale)
    {
        var text = new FormattedText(
            b.Content as string ?? "",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(b.FontFamily, b.FontStyle, b.FontWeight, b.FontStretch),
            b.FontSize * scale,
            Brushes.Black,
            1.0);
        var room = b.Width - b.Padding.Left - b.Padding.Right
                 - b.BorderThickness.Left - b.BorderThickness.Right;
        return (text.Width, room);
    }

    /// <summary>
    /// The check itself, pinned: a caption that is a sentence does NOT fit one of these
    /// buttons, and a caption that is a word does. Without this, the walk above could stop
    /// catching anything — a comparison the wrong way round, a width read as NaN — and go on
    /// passing for ever, which is exactly what a green test with nothing behind it looks like.
    /// </summary>
    [Fact]
    public void TheFitCheckCatchesASentenceAndPassesAWord()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            var style = (Style)Application.Current!.Resources["SetActionButton"];

            var scale = WarsOfLibertyLauncher.Services.TextScale.MaxFactor;

            // The caption that shipped clipped, on the button it shipped clipped in.
            var sentence = new Button { Style = style, Content = "Instalar esta versión" };
            sentence.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var wide = Fit(sentence, scale);
            Assert.True(wide.Needed > wide.Room,
                $"'Instalar esta versión' measured {wide.Needed:F0} px against {wide.Room:F0} "
                + "of room and was called a fit — the check is not checking.");

            var word = new Button { Style = style, Content = "Instalar" };
            word.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var narrow = Fit(word, scale);
            Assert.True(narrow.Needed <= narrow.Room,
                $"'Instalar' measured {narrow.Needed:F0} px against {narrow.Room:F0} of room.");
        });

        Assert.Null(error);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }
}
