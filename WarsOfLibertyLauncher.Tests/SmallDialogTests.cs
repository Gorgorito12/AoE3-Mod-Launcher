using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>The one chassis of the small dialogs (design 103). What a player cannot see go wrong
/// here is an answer given by accident, so most of these are about what does NOT say yes.</summary>
[Collection("wpf-and-language")]
public class SmallDialogTests
{
    private static void Run(Action body)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            body();
        });
        Assert.Null(error);
    }

    [Fact]
    public void TheUpdateBarIsTheDialogsOwn_AndOneLineSaysHowMuch()
    {
        Run(() =>
        {
            Strings.SetLanguage("es");
            var window = new StartupUpdateWindow("v1.0.17");
            Assert.Same(Application.Current.FindResource("DialogProgress"), window.DownloadProgress.Style);
            Assert.False(window.Chrome.ShowMinimize);

            window.Report(new DownloadProgress(30 * 1024 * 1024, 100 * 1024 * 1024, 30));

            Assert.Equal("30 %", window.ProgressPercentText.Text);
            Assert.StartsWith("30 MB de 100 MB", window.ProgressLabelText.Text);
        });
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public void RemovingAModSpeaksOfTheLibrary_NotOfAScreenThatIsGone(string language)
    {
        Run(() =>
        {
            Strings.SetLanguage(language);
            var body = Strings.Get("DlgRemoveModBodyInstalled");
            Assert.DoesNotContain("Workshop", body);
            Assert.DoesNotContain("MODS", body);
        });
    }

    private static void PressEscape(Window window)
        => window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new FakePresentationSource(), 0, Key.Escape)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        });

    private sealed class FakePresentationSource : PresentationSource
    {
        protected override System.Windows.Media.CompositionTarget GetCompositionTargetCore() => null!;
        public override bool IsDisposed => false;
        public override System.Windows.Media.Visual RootVisual { get => null!; set { } }
    }

    [Fact]
    public void EnterAnswersThePrimaryButton_ExceptOnWhatCannotBeUndone()
    {
        Run(() =>
        {
            var plain = new ThemedConfirmDialog("t", "b", "Do", "No", ConfirmTone.Caution);
            Assert.True(plain.ConfirmButton.IsDefault);
            Assert.False(plain.CancelButton.IsDefault);

            var red = new ThemedConfirmDialog("t", "b", "Delete", "No", ConfirmTone.Destructive);
            Assert.False(red.ConfirmButton.IsDefault);
            Assert.True(red.CancelButton.IsDefault);
        });
    }

    [Fact]
    public void TheToneDecidesTheActionsColourAndARedOneStaysRed()
    {
        Run(() =>
        {
            var plain = new ThemedConfirmDialog("t", "b", "Do", "No");
            var red = new ThemedConfirmDialog("t", "b", "Delete", "No", ConfirmTone.Destructive);
            Assert.NotSame(plain.ConfirmButton.Style, red.ConfirmButton.Style);
            Assert.Same(Application.Current.FindResource("WindowDangerButton"), red.ConfirmButton.Style);

            var dialog = new RemoveFromCollectionDialog("Mod", null);
            Assert.Same(Application.Current.FindResource("WindowDangerButton"), dialog.PrimaryAction.Style);
            Assert.False(dialog.PrimaryAction.IsDefault);
        });
    }

    [Fact]
    public void EscapeIsCancel_EvenWithNoCancelButtonOnScreen()
    {
        Run(() =>
        {
            var dialog = new CrossUserAccountDialog(
                new RunningAccount.AccountInfo("a", "b", Elevated: false, Mismatch: true), @"C:\a", null);
            Assert.Equal(Visibility.Collapsed, dialog.CancelAction.Visibility);
            var closed = false;
            dialog.Closed += (_, _) => closed = true;
            PressEscape(dialog);
            Assert.True(closed);
        });
    }

    [Fact]
    public void TheDetailBoxExistsOnlyWhenThereIsSomethingInIt()
    {
        Run(() =>
        {
            var empty = new RemoveFromCollectionDialog("Mod", "");
            Assert.Equal(440, empty.Width);

            var withPath = new RemoveFromCollectionDialog("Mod", @"D:\Mods\Mod");
            Assert.Equal(480, withPath.Width);
        });
    }

    [Fact]
    public void WhatGoesInsideTheTagsLandsInTheBodyAndTheChassisSurvives()
    {
        Run(() =>
        {
            var dialog = new CreateTeamDialog();
            Assert.Same(dialog.NameLabel.Parent, ((StackPanel)dialog.Body!));
            Assert.IsType<Grid>(dialog.Content);
            Assert.NotNull(dialog.Chrome);
        });
    }

    [Fact]
    public void TheHeadlineNamesTheModBeingRemoved()
    {
        Run(() =>
        {
            var previous = Strings.Language;
            try
            {
                foreach (var language in new[] { Strings.LangEn, Strings.LangEs })
                {
                    Strings.SetLanguage(language);
                    Assert.Contains("Hundred Days", new RemoveFromCollectionDialog("Hundred Days", null).HeadlineText.Text);
                }
            }
            finally { Strings.SetLanguage(previous); }
        });
    }

    [Fact]
    public void ThePickerSaysHowManyItInvites()
    {
        Run(() =>
        {
            var dialog = new PlayerPickerDialog("t", "i", "L",
                new[] { new PickCandidate("a", "Ana"), new PickCandidate("b", "Luis") },
                required: 2, okLabel: "", okLabelFormat: "Invite {0}");
            dialog.Toggle("a", true);
            Assert.Equal("Invite 1", dialog.PrimaryAction.Content);
            dialog.Toggle("b", true);
            Assert.Equal("Invite 2", dialog.PrimaryAction.Content);
            Assert.True(dialog.PrimaryAction.IsEnabled);
        });
    }

    /// <summary>A player who cannot be chosen stays in view: dimmed, with the reason.</summary>
    [Fact]
    public void WhoCannotBeChosenStaysInViewWithTheReason()
    {
        Run(() =>
        {
            var dialog = new PlayerPickerDialog("t", "i", "L",
                new[] { new PickCandidate("a", "Ana", Disabled: true, DisabledReason: "already in") },
                required: 1, okLabel: "OK");
            var row = Assert.IsType<CheckBox>(dialog.ListPanel.Children[0]);
            Assert.False(row.IsEnabled);
            Assert.Contains("already in", DialogXamlTests.RenderedText((FrameworkElement)row.Content));
        });
    }

    [Fact]
    public void TheStartupWindowOffersAWayBackOnlyAfterAFailure()
    {
        Run(() =>
        {
            var window = new StartupUpdateWindow("v1.0.17");
            Assert.Equal(Visibility.Collapsed, window.PrimaryAction.Visibility);
            var retried = 0;
            window.RetryRequested += () => retried++;

            window.ShowFailed("503");
            Assert.Equal(Visibility.Visible, window.PrimaryAction.Visibility);
            Assert.Equal(DialogTone.Caution, window.Tone);
            Assert.True(window.SkipButton.IsEnabled);
            window.PrimaryAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, retried);

            window.ShowRetrying("v1.0.17");
            Assert.Equal(Visibility.Collapsed, window.PrimaryAction.Visibility);
            Assert.Equal(DialogTone.None, window.Tone);
            Assert.Same(window.ProgressBlock, window.Body);
            window.CloseByOwner();
        });
    }

    /// <summary>The implicit styles are the whole of a bare menu's look: without them, white.</summary>
    [Fact]
    public void ABareContextMenuIsOurs()
    {
        Run(() =>
        {
            var menu = new ContextMenu();
            menu.Items.Add(new MenuItem { Header = "x" });
            var implicitMenu = Application.Current.TryFindResource(typeof(ContextMenu));
            var implicitItem = Application.Current.TryFindResource(typeof(MenuItem));
            Assert.NotNull(implicitMenu);
            Assert.NotNull(implicitItem);
            Assert.NotNull(Application.Current.TryFindResource(MenuItem.SeparatorStyleKey));
        });
    }

    /// <summary>
    /// Every floating surface follows the player's level, and never as thin as the design's
    /// numbers: those assume a blur WPF does not have, and without it the text behind reads
    /// through the menu's own.
    /// </summary>
    [Theory]
    [InlineData(GlassTheme.Solid)]
    [InlineData(GlassTheme.Medium)]
    [InlineData(GlassTheme.Maximum)]
    public void AFloatingSurfaceStaysReadableWithNoBlur(string level)
        => Assert.True(GlassTheme.For(level).Floating.A >= 0.85 * 255, level);

    [Fact]
    public void TheLevelsOfAFloatingSurfaceAreDifferent()
    {
        Assert.True(GlassTheme.For(GlassTheme.Solid).Floating.A > GlassTheme.For(GlassTheme.Medium).Floating.A);
        Assert.True(GlassTheme.For(GlassTheme.Medium).Floating.A > GlassTheme.For(GlassTheme.Maximum).Floating.A);
    }

    /// <summary>
    /// The launcher has no Windows message boxes (BASE.md). The two that remain answer inside
    /// MainWindow.OnClosing, which needs the answer before it returns, and a third anywhere else
    /// is a white window in a dark flow.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_NoWindowsMessageBoxOutsideTheClosingPath()
    {
        var root = RepoRoot();
        var hits = Directory.EnumerateFiles(Path.Combine(root, "WarsOfLibertyLauncher"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                        && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .SelectMany(f => File.ReadAllLines(f).Where(l => l.Contains("MessageBox.Show(") && !l.TrimStart().StartsWith("//"))
                .Select(_ => Path.GetFileName(f)))
            .ToList();
        Assert.Equal(new[] { "MultiplayerTab.xaml.cs", "MultiplayerTab.xaml.cs" }, hits);
    }

    private static string RepoRoot([CallerFilePath] string here = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, ".."));
}
