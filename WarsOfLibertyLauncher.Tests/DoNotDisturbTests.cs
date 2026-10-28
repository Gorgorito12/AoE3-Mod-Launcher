using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// "Do not disturb" (2026-10-28): <see cref="ToastRouting"/> decides where a pop-up goes, and the
/// setting silences every card that is NEWS — in the window as well as on the desktop — while the
/// answers to the player's own clicks and the personal calls with a clock on them still show.
/// Nothing tested the routing before; the parity case pins that, with the setting off, it is
/// exactly what it always was.
/// </summary>
public class ToastRoutingTests
{
    /// <summary>
    /// THE ONE THAT MATTERS: with "Do not disturb" on, a news card shows NOWHERE — not in the
    /// window the player is looking at, not on the desktop, whatever the card prefers.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void THE_ONE_THAT_MATTERS_DoNotDisturbSilencesNewsInTheWindowAndOnTheDesktop(bool looking, bool preferDesktop)
    {
        Assert.Equal(ToastRoute.SuppressedDoNotDisturb,
            ToastRouting.Decide(doNotDisturb: true, ToastCause.News, looking, preferDesktop, gameRunning: false));
    }

    /// <summary>An answer to the player and a personal call are routed exactly as if the setting
    /// were off — including to the desktop when the window is hidden.</summary>
    [Theory]
    [InlineData(ToastCause.Answer)]
    [InlineData(ToastCause.Request)]
    public void AnswersAndCallsStillShow_WhereverThePlayerIsLooking(ToastCause cause)
    {
        Assert.Equal(ToastRoute.InWindow, ToastRouting.Decide(true, cause, userIsLooking: true, preferDesktop: false, gameRunning: false));
        Assert.Equal(ToastRoute.Desktop, ToastRouting.Decide(true, cause, userIsLooking: false, preferDesktop: false, gameRunning: false));
        Assert.Equal(ToastRoute.Desktop, ToastRouting.Decide(true, cause, userIsLooking: true, preferDesktop: true, gameRunning: false));
    }

    /// <summary>Nothing floats over a running game — not even an answer or an invitation — but a
    /// card the player can see inside the launcher's own window is not a topmost window.</summary>
    [Theory]
    [InlineData(ToastCause.News)]
    [InlineData(ToastCause.Answer)]
    [InlineData(ToastCause.Request)]
    public void NothingFloatsOverARunningGame(ToastCause cause)
    {
        Assert.Equal(ToastRoute.SuppressedGameRunning,
            ToastRouting.Decide(false, cause, userIsLooking: false, preferDesktop: false, gameRunning: true));
        Assert.Equal(ToastRoute.InWindow,
            ToastRouting.Decide(false, cause, userIsLooking: true, preferDesktop: false, gameRunning: true));
    }

    /// <summary>Parity: with the setting off, the old if-chain, case by case.</summary>
    [Theory]
    [InlineData(true, false, false, ToastRoute.InWindow)]
    [InlineData(true, true, false, ToastRoute.Desktop)]
    [InlineData(false, false, false, ToastRoute.Desktop)]
    [InlineData(false, false, true, ToastRoute.SuppressedGameRunning)]
    [InlineData(true, true, true, ToastRoute.SuppressedGameRunning)]
    public void WithDoNotDisturbOffTheRoutingIsUnchanged(bool looking, bool preferDesktop, bool game, ToastRoute expected)
    {
        foreach (var cause in Enum.GetValues<ToastCause>())
            Assert.Equal(expected, ToastRouting.Decide(false, cause, looking, preferDesktop, game));
    }

    /// <summary>A card that forgets to say what it is, is NEWS: it loses its pop-up under the
    /// setting, which is noticed and fixed, rather than breaking the promise the setting makes.</summary>
    [Fact]
    public void ACardThatSaysNothingIsNews()
    {
        var card = new AppToast.ToastOptions("i", "t", null, Array.Empty<AppToast.ToastAction>());
        Assert.Equal(ToastCause.News, card.Cause);
    }

    [Fact]
    public void TheTrayBalloonFollowsTheSameRule()
    {
        // The ordinary case: the bell's balloon, nobody looking, an icon to carry it.
        Assert.True(ToastRouting.ShouldShowBalloon(true, doNotDisturb: false, bypassDoNotDisturb: false, userIsLooking: false, trayIconVisible: true));
        // "Do not disturb" silences it — unless it answers the player.
        Assert.False(ToastRouting.ShouldShowBalloon(true, true, false, false, true));
        Assert.True(ToastRouting.ShouldShowBalloon(true, true, bypassDoNotDisturb: true, userIsLooking: false, trayIconVisible: true));
        // Never while the player is looking, never without the icon, never with balloons off.
        Assert.False(ToastRouting.ShouldShowBalloon(true, false, false, userIsLooking: true, trayIconVisible: true));
        Assert.False(ToastRouting.ShouldShowBalloon(true, false, false, false, trayIconVisible: false));
        Assert.False(ToastRouting.ShouldShowBalloon(false, false, true, false, true));
    }

    /// <summary>
    /// Sounds: "Do not disturb" quiets the AMBIENT ones (the news a card would have announced);
    /// everything else follows the Sounds switch alone, and with sounds off nothing plays.
    /// </summary>
    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, false, false)]
    public void DoNotDisturbQuietsOnlyTheAmbientSounds(bool enabled, bool dnd, bool ambient, bool plays)
    {
        Assert.Equal(plays, SoundService.ShouldPlay(enabled, dnd, ambient));
    }

    /// <summary>
    /// A tournament push about the player's OWN match is a call with a clock on it (an organiser
    /// can disqualify a no-show), so it still shows; a result or an entry accepted is news.
    /// </summary>
    [Theory]
    [InlineData("match_ready", ToastCause.Request)]
    [InlineData("room_opened", ToastCause.Request)]
    [InlineData("match_replay", ToastCause.Request)]
    [InlineData("match_done", ToastCause.News)]
    [InlineData("entry_accepted", ToastCause.News)]
    [InlineData("entry_promoted", ToastCause.News)]
    [InlineData(null, ToastCause.News)]
    public void ATournamentCallStillShows_ItsNewsDoesNot(string? kind, ToastCause expected)
    {
        Assert.Equal(expected, MultiplayerTab.TournamentToastCause(new TournamentUpdateNotice { Kind = kind }));
    }
}

/// <summary>
/// The funnel: "Do not disturb" is decided in exactly two places, <c>MainWindow.ShowAppToast</c>
/// and <c>MainWindow.ShowToast</c>, so nothing else may put a card or a tray balloon on screen — a
/// third path would silently ignore the setting. Pinned by reading the source, the way the other
/// wiring tests are: no behavioural test can reach a real tray icon or a desktop window.
/// </summary>
public class ToastFunnelTests
{
    private static string Source(string relative) => File.ReadAllText(RepoFile(relative));

    private static int Count(string text, string needle)
        => Regex.Matches(text, Regex.Escape(needle)).Count;

    [Fact]
    public void THE_ONE_THAT_MATTERS_OnlyTheTwoRoutersPutAPopUpOnScreen()
    {
        var root = Path.GetDirectoryName(RepoFile("MainWindow.xaml.cs"))!;
        var paths = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();
        Assert.Contains(paths, p => Path.GetFileName(p) == "MainWindow.xaml.cs");

        foreach (var path in paths)
        {
            var file = Path.GetFileName(path);
            var text = File.ReadAllText(path);
            var cards = Count(text, "AppToast.Show(");
            var desktop = Count(text, "DesktopToastWindow.Show(");
            var balloons = Count(text, "ShowBalloonTip(");
            switch (file)
            {
                case "MainWindow.xaml.cs":
                    Assert.Equal(1, cards);
                    Assert.Equal(1, desktop);
                    // ShowToast plus the two one-time disclosures (recording notice, closed-to-tray hint).
                    Assert.Equal(3, balloons);
                    break;
                case "DesktopToastWindow.cs":
                    Assert.Equal(0, balloons);
                    break;
                default:
                    Assert.True(cards == 0 && desktop == 0 && balloons == 0,
                        $"{file} puts a pop-up on screen without going through MainWindow's routing.");
                    break;
            }
        }
    }

    /// <summary>The router reads the setting and the multiplayer game, not only the dashboard's.</summary>
    [Fact]
    public void TheCardRouterReadsDoNotDisturbAndTheMultiplayerGame()
    {
        var src = Source("MainWindow.xaml.cs");
        var start = src.IndexOf("public void ShowAppToast(", StringComparison.Ordinal);
        Assert.True(start > 0);
        var body = src[start..src.IndexOf("\n    }", start, StringComparison.Ordinal)];
        Assert.Contains("ToastRouting.Decide(", body);
        Assert.Contains("DoNotDisturb", body);
        Assert.Contains("MultiplayerView?.IsGameRunning", body);
    }

    /// <summary>A room invitation is a call, never news: hiding it would discard it.</summary>
    [Fact]
    public void ARoomInvitationIsACall()
    {
        var src = Source("Controls/MultiplayerTab.xaml.cs");
        var start = src.IndexOf("void HandleInviteFrame(", StringComparison.Ordinal);
        Assert.True(start > 0, "HandleInviteFrame has moved or been renamed.");
        var end = src.IndexOf("void HandleInviteError(", start, StringComparison.Ordinal);
        Assert.Contains("ToastCause.Request", src[start..end]);
    }

    /// <summary>The pushed room obeys "Tell me when someone opens a room", like the poll always did,
    /// and it decides that before any card, dot or sound.</summary>
    [Fact]
    public void APushedRoomObeysTheRoomSwitchBeforeAnyCard()
    {
        var src = Source("MainWindow.xaml.cs");
        var start = src.IndexOf("private void OnNewRoomFromWs(", StringComparison.Ordinal);
        Assert.True(start > 0);
        var gate = src.IndexOf("_config.NotifyNewRooms", start, StringComparison.Ordinal);
        var card = src.IndexOf("ShowAppToast(", start, StringComparison.Ordinal);
        Assert.True(gate > start && gate < card, "the room switch must be read before the card is shown");
    }

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "WarsOfLibertyLauncher", relative);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(relative);
    }
}
