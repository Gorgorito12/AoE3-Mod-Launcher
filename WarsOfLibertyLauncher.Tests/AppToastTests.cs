using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher.Localization;
using Shape = System.Windows.Shapes.Path;
using IOPath = System.IO.Path;
using WarsOfLibertyLauncher.Controls;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Design 105 (docs/design_avisos_flotantes): one card for every floating notice. What is pinned is
/// what went wrong before it: an icon drawn as a character (black, toneless), a card with no tone,
/// and every primary button painted the green of "Join" whatever it did.
/// </summary>
[Collection("wpf-and-language")]
public class AppToastTests
{
    private static AppToast.ToastAction Act(string label, AppToast.ToastButton kind) => new(label, kind, () => { });

    private static AppToast.ToastOptions Card(
        AppToast.ToastTone tone = AppToast.ToastTone.Ok,
        AppToast.ToastIcon icon = AppToast.ToastIcon.Check,
        IReadOnlyList<AppToast.ToastAction>? actions = null,
        int autoDismissMs = 9000,
        bool important = false,
        AppToast.ToastCrest? crest = null)
        => new(tone, icon, "Opponent found", "Kanchay entered your room. The match is set up.",
            actions ?? Array.Empty<AppToast.ToastAction>(), AutoDismissMs: autoDismissMs,
            Subtitle: "Wars of Liberty", Crest: crest, Important: important);

    private static IEnumerable<object> Walk(object root)
    {
        yield return root;
        if (root is DependencyObject d)
            foreach (var child in LogicalTreeHelper.GetChildren(d))
                foreach (var x in Walk(child)) yield return x;
    }

    private static T Run<T>(Func<T> f)
    {
        T result = default!;
        var error = DialogXamlTests.RunOnStaThread(() => result = f());
        Assert.Null(error);
        return result;
    }

    // ---------- the one coloured button ----------

    [Fact]
    public void THE_ONE_THAT_MATTERS_ANoticeWearsOneColouredButtonAtMost()
    {
        var kinds = AppToast.ButtonKinds(new[]
        {
            Act("Repair", AppToast.ToastButton.Action),
            Act("Join", AppToast.ToastButton.Join),
            Act("Copy list", AppToast.ToastButton.Solid),
        });
        Assert.Equal(new[] { AppToast.ToastButton.Action, AppToast.ToastButton.Solid, AppToast.ToastButton.Solid }, kinds);
    }

    [Fact]
    public void TheColouredButtonSitsAtTheRightEnd()
    {
        var order = AppToast.ButtonOrder(new[]
        {
            Act("Join", AppToast.ToastButton.Join),
            Act("Mute", AppToast.ToastButton.Solid),
        });
        Assert.Equal(new[] { "Mute", "Join" }, order.Select(o => o.Action.Label));
    }

    [Fact]
    public void JoinIsTheOnlyGreenButton() => Run(() =>
    {
        var green = ((SolidColorBrush)Application.Current.FindResource("RmJoinBg")).Color;
        foreach (var kind in Enum.GetValues<AppToast.ToastButton>())
        {
            var shell = AppToast.Build(Card(actions: new[] { Act("Go", kind) }), () => { });
            var button = Walk(shell).OfType<Button>().Single(b => Equals(b.Tag, kind));
            var fill = ((SolidColorBrush)button.Background).Color;
            Assert.Equal(kind == AppToast.ToastButton.Join, fill == green);
        }
        return 0;
    });

    // ---------- the icon is never a character ----------

    [Fact]
    public void THE_ONE_THAT_MATTERS_NoTextOnTheCardIsASymbol() => Run(() =>
    {
        foreach (var tone in Enum.GetValues<AppToast.ToastTone>())
        foreach (var icon in Enum.GetValues<AppToast.ToastIcon>())
        foreach (var desktop in new[] { false, true })
        {
            var shell = AppToast.Build(Card(tone, icon, new[] { Act("See room", AppToast.ToastButton.Action) }), () => { }, desktop);
            var texts = Walk(shell).OfType<TextBlock>().Select(t => t.Text)
                .Concat(Walk(shell).OfType<Button>().Select(b => b.Content as string ?? ""));
            foreach (var text in texts)
                Assert.DoesNotContain(text, c => IsSymbol(c));
            // The icon is a drawn path, tagged with the pictogram it is.
            Assert.Contains(Walk(shell).OfType<Shape>(), p => Equals(p.Tag, icon));
        }
        return 0;
    });

    private static bool IsSymbol(char c)
    {
        var cat = char.GetUnicodeCategory(c);
        return cat is UnicodeCategory.OtherSymbol or UnicodeCategory.PrivateUse or UnicodeCategory.Surrogate;
    }

    [Fact]
    public void EveryButtonLabelTheNoticesUseIsPlainText()
    {
        foreach (var lang in new[] { Strings.LangEn, Strings.LangEs })
        foreach (var key in new[]
        {
            "MpToastJoin", "MpToastMute", "MpToastIgnore", "QmFoundView", "QmRadminOpen", "MmPlay",
            "RoomRematchNotNow", "MtToastDecline", "MtToastView", "MtToastAccept", "MtToastViewTeam",
            "InterruptedOperationRepair", "ToastActionRepairNow", "ToastActionCopyList", "ToastActionCopied",
            "MpReplayShowInFolder", "MpReplayRetry",
        })
            Assert.DoesNotContain(Strings.GetIn(lang, key), c => IsSymbol(c));
    }

    [Fact]
    public void EachToneTintsItsTileAndANeutralOneStaysWhite() => Run(() =>
    {
        var tileFills = new Dictionary<AppToast.ToastTone, Color>();
        foreach (var tone in Enum.GetValues<AppToast.ToastTone>())
        {
            var shell = AppToast.Build(Card(tone), () => { });
            var tile = Walk(shell).OfType<Grid>().Single(g => Equals(g.Tag, "ToastTile"));
            tileFills[tone] = ((SolidColorBrush)((Border)tile.Children[0]).Background).Color;
        }
        Assert.Equal(4, tileFills.Values.Distinct().Count());
        var neutral = tileFills[AppToast.ToastTone.Neutral];
        Assert.Equal(neutral.R, neutral.G);
        Assert.Equal(neutral.G, neutral.B);
        return 0;
    });

    [Fact]
    public void ATeamNoticeShowsItsShieldAndNoPictogram() => Run(() =>
    {
        var shell = AppToast.Build(Card(crest: new AppToast.ToastCrest("TF")), () => { });
        var tile = Walk(shell).OfType<Grid>().Single(g => Equals(g.Tag, "ToastTile"));
        Assert.Contains(Walk(tile).OfType<TextBlock>(), t => t.Text == "TF");
        Assert.Empty(Walk(tile).OfType<Shape>());
        return 0;
    });

    // ---------- important, surfaces, bar ----------

    [Fact]
    public void OnlyAnImportantNoticeHasAHaloAndATonedRim() => Run(() =>
    {
        int Layers(bool important) => ((Grid)AppToast.Build(Card(important: important), () => { }).Children[0]).Children.Count;
        Assert.Equal(1, Layers(false));
        Assert.Equal(2, Layers(true));

        var rim = ((SolidColorBrush)Application.Current.FindResource("ToastRim")).Color;
        Color Border(bool important) => ((SolidColorBrush)((Border)AppToast.Build(Card(important: important), () => { }).Children[1]).BorderBrush).Color;
        Assert.Equal(rim, Border(false));
        Assert.NotEqual(rim, Border(true));
        return 0;
    });

    [Fact]
    public void THE_ONE_THAT_MATTERS_OnlyTheTwoRivalNoticesAreImportant()
    {
        var root = IOPath.GetDirectoryName(RepoFile("MainWindow.xaml.cs"))!;
        var marked = Directory.EnumerateFiles(IOPath.Combine(root, "Controls"), "*.cs")
            .Where(f => File.ReadAllText(f).Contains("Important: true", StringComparison.Ordinal))
            .Select(IOPath.GetFileName)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(new[] { "MultiplayerTab.QuickMatch.cs", "MultiplayerTab.RoomView.cs" }, marked);
        foreach (var file in marked)
            Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(
                File.ReadAllText(IOPath.Combine(root, "Controls", file!)), "Important: true").Count);
    }

    [Fact]
    public void TheCardIs420OnBothSurfacesAndTheDesktopOneSaysWhoseItIs() => Run(() =>
    {
        foreach (var desktop in new[] { false, true })
        {
            var shell = AppToast.Build(Card(), () => { }, desktop);
            shell.Measure(new Size(1000, double.PositiveInfinity));
            shell.Arrange(new Rect(shell.DesiredSize));
            Assert.Equal(AppToast.CardWidth, shell.ActualWidth);
            Assert.Equal(desktop, Walk(shell).OfType<TextBlock>().Any(t => t.Text == "AoE3 Mod Launcher"));
        }
        return 0;
    });

    [Fact]
    public void ANoticeThatDoesNotCloseByItselfHasNoTimeBar() => Run(() =>
    {
        Border? Bar(int ms) => Walk(AppToast.Build(Card(autoDismissMs: ms), () => { })).OfType<Border>()
            .SingleOrDefault(b => Equals(b.Tag, AppToast.ToastBarTag));
        Assert.NotNull(Bar(9000));
        Assert.Null(Bar(0));
        return 0;
    });

    [Fact]
    public void TheCloseButtonIsDrawnAndSaysClose() => Run(() =>
    {
        var close = Walk(AppToast.Build(Card(), () => { })).OfType<Button>().Single(b => Equals(b.Tag, AppToast.ToastCloseTag));
        Assert.Null(close.Content);
        return 0;
    });

    // ---------- the countdown pauses under the pointer ----------

    [Fact]
    public void THE_ONE_THAT_MATTERS_TheCountdownStandsStillWhileThePointerIsOnTheCard() => Run(() =>
    {
        var now = new DateTime(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);
        var expired = 0;
        var countdown = new ToastCountdown(TimeSpan.FromSeconds(9), () => expired++, bar: null, animate: false, () => now);
        countdown.Start();
        now = now.AddSeconds(3);
        countdown.Pause();
        Assert.Equal(TimeSpan.FromSeconds(6), countdown.Remaining);
        Assert.False(countdown.IsRunning);

        now = now.AddMinutes(5);   // the pointer rests on the card
        Assert.Equal(TimeSpan.FromSeconds(6), countdown.Remaining);

        countdown.Resume();
        now = now.AddSeconds(2);
        Assert.Equal(TimeSpan.FromSeconds(4), countdown.Remaining);
        countdown.Stop();
        Assert.False(countdown.IsRunning);
        Assert.Equal(0, expired);
        return 0;
    });

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = IOPath.Combine(dir.FullName, "WarsOfLibertyLauncher", relative);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(relative);
    }
}
