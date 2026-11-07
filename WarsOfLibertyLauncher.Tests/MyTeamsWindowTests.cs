using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// My teams (design 83). The simulator stands in for the server, so these press the window's real
/// buttons; what is checked is what the player reads and what reached the server.
/// </summary>
[Collection("wpf-and-language")]
public class MyTeamsWindowTests
{
    private static string Me => TournamentDemoData.MeUserId;

    private static TeamMember P(string id, string? name = null) => new() { UserId = id, DisplayName = name ?? id };

    private static TeamSummary Team(string id, string owner, params string[] members) => new()
    {
        Id = id, Name = "Team " + id, OwnerUserId = owner, Members = members.Select(m => P(m)).ToList(),
    };

    private static MyTeamsWindow Window(TournamentSimulator? simulator = null, string? me = null, Func<string, bool>? online = null,
        IReadOnlyList<PickCandidate>? candidates = null, string? invite = null)
        => new(new PreviewTournamentApi(simulator ?? new TournamentSimulator(), () => null, null), me ?? Me,
            () => candidates ?? Array.Empty<PickCandidate>(), null, online, null, invite);

    private static void Run(Action body)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            Strings.SetLanguage(Strings.LangEs);
            try { body(); }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    private static void Press(ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static List<string> Texts(DependencyObject root)
    {
        var found = new List<string>();
        void Walk(object node)
        {
            if (node is TextBlock t) found.Add(string.Concat(t.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text)));
            if (node is DependencyObject d)
                foreach (var child in LogicalTreeHelper.GetChildren(d)) Walk(child);
        }
        Walk(root);
        return found;
    }

    [Theory]
    [InlineData(1, "falta 1 para 2v2")]
    [InlineData(2, "solo 2v2")]
    [InlineData(3, "2v2 y 3v3")]
    [InlineData(6, "2v2 y 3v3")]
    public void TheRailSaysWhatATeamCanEnter(int members, string expected)
        => Run(() => Assert.Equal(expected, MyTeamsView.Fit(members)));

    [Fact]
    public void TheChipsSayWhatIsMissing() => Run(() =>
    {
        var one = MyTeamsView.Chips(1);
        Assert.Equal(new[] { "2v2 · falta 1", "3v3 · faltan 2" }, one.Select(c => c.Text));
        Assert.All(one, c => Assert.False(c.Ok));
        Assert.All(MyTeamsView.Chips(3), c => Assert.True(c.Ok));
    });

    [Fact]
    public void TheRosterPutsTheCaptainThenMeThenWhoIsOnline()
    {
        var team = Team("t", "cap", "off", "live", "me", "cap");
        var order = MyTeamsView.Roster(team, "me", id => id == "live").Select(m => m.UserId);
        Assert.Equal(new[] { "cap", "me", "live", "off" }, order);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("  ", true)]
    [InlineData("A", true)]
    [InlineData("AB", false)]
    public void ANameOfTwoIsEnough(string name, bool blocked)
        => Run(() => Assert.Equal(blocked, MyTeamsView.NameBlock(name, 2) != null));

    [Fact]
    public void Teams_CreatingOneSelectsItAndSaysSo() => Run(() =>
    {
        var w = Window();
        w.LoadAsync().GetAwaiter().GetResult();
        Press(w.NewTeamButton);
        Assert.Equal(Visibility.Visible, w.NewView.Visibility);
        Assert.False(w.CreateButton.IsEnabled);

        w.NameEntry.Text = "Los Andes";
        Assert.True(w.CreateButton.IsEnabled);
        Assert.Equal("Los Andes", w.HeadName.Text);

        w.CreateAsync().GetAwaiter().GetResult();
        Assert.True(w.Changed);
        Assert.Equal(Visibility.Collapsed, w.NewView.Visibility);
        Assert.Equal("Los Andes", w.HeadName.Text);
        Assert.Equal(Strings.Get("MtDoneCreated"), w.NoteText.Text);
        Assert.Contains(w.Data!.Teams!, t => t.Name == "Los Andes");
        w.Close();
    });

    [Fact]
    public void AnInviteNamesWhoAndWhen() => Run(() =>
    {
        var w = Window();
        var invite = new TeamInvite { Id = "i", TeamId = "x", TeamName = "Team x", InvitedBy = "cap", CreatedAt = DateTimeOffset.UtcNow.AddHours(-3).ToString("O") };
        var data = new MyTeamsResponse { Teams = new(), Invites = new() { invite } };
        var team = Team("x", "cap", "cap", "two");
        team.Members![0].DisplayName = "TercioViejo";
        w.ShowPreview(data, new Dictionary<string, TeamSummary> { ["x"] = team }, "inv:i", "ready", null);
        Assert.StartsWith("Te invitó TercioViejo", w.HeadSub.Text);
        Assert.Contains("hace 3 h", w.HeadSub.Text);
        Assert.True(w.AcceptButton.IsEnabled);
        w.Close();
    });

    /// <summary>The roster of the team comes in a second request: without it the invitation still
    /// reads, it just cannot say who sent it.</summary>
    [Fact]
    public void AnInviteWithoutItsTeamStillReads() => Run(() =>
    {
        var w = Window();
        var invite = new TeamInvite { Id = "i", TeamId = "x", TeamName = "Team x", InvitedBy = "cap" };
        w.ShowPreview(new MyTeamsResponse { Teams = new(), Invites = new() { invite } }, new Dictionary<string, TeamSummary>(), "inv:i", "ready", null);
        Assert.Equal(Strings.Get("MtInvited"), w.HeadSub.Text);
        Assert.Empty(w.HeadChips.Children);
        w.Close();
    });

    [Fact]
    public void AFullTeamCannotBeAcceptedAndSaysWhy() => Run(() =>
    {
        var w = Window();
        var invite = new TeamInvite { Id = "i", TeamId = "x", TeamName = "Team x", InvitedBy = "a" };
        var data = new MyTeamsResponse { Teams = new(), Invites = new() { invite } };
        w.ShowPreview(data, new Dictionary<string, TeamSummary> { ["x"] = Team("x", "a", "a", "b", "c", "d", "e", "f") }, "inv:i", "ready", null);
        Assert.False(w.AcceptButton.IsEnabled);
        Assert.Contains("6 de 6", w.InviteBlock.Text);
        Assert.True(w.DeclineButton.IsEnabled);
        w.Close();
    });

    [Fact]
    public void TheSentInvitesBlockHidesWithoutTheServerField() => Run(() =>
    {
        var team = Team("t", Me, Me, "two");
        var heading = Strings.Get("MtSent");

        var old = Window();
        old.ShowPreview(new MyTeamsResponse { Teams = new() { team }, Invites = new() }, new Dictionary<string, TeamSummary>(), "t", "ready", null);
        Assert.DoesNotContain(Texts(old.TeamBody), t => t.Contains(heading));
        old.Close();

        var sent = new TeamSentInvite { Id = "s", TeamId = "t", InvitedUserId = "m", InvitedName = "Mariscal" };
        var fresh = Window();
        fresh.ShowPreview(new MyTeamsResponse { Teams = new() { team }, Invites = new(), SentInvites = new() { sent } }, new Dictionary<string, TeamSummary>(), "t", "ready", null);
        Assert.Contains(Texts(fresh.TeamBody), t => t.Contains(heading));
        fresh.Close();
    });

    [Fact]
    public void OnlyTheCaptainOffersToInviteOrRemove() => Run(() =>
    {
        var captain = Team("mine", Me, Me, "two");
        var other = Team("theirs", "two", "two", Me);
        var w = Window();
        var data = new MyTeamsResponse { Teams = new() { captain, other }, Invites = new() };
        var none = new Dictionary<string, TeamSummary>();

        w.ShowPreview(data, none, "mine", "ready", null);
        Assert.Equal(Strings.Get("MtDisband"), w.DangerButton.Content);
        Assert.Contains(Texts(w.TeamBody).Concat(Captions(w.TeamBody)), t => t == Strings.Get("MtInvite"));

        w.ShowPreview(data, none, "theirs", "ready", null);
        Assert.Equal(Strings.Get("MtLeave"), w.DangerButton.Content);
        Assert.DoesNotContain(Captions(w.TeamBody), t => t == Strings.Get("MtInvite") || t == Strings.Get("MtRemove"));
        w.Close();
    });

    private static List<string> Captions(DependencyObject root)
    {
        var found = new List<string>();
        void Walk(object node)
        {
            if (node is Button { Content: string text }) found.Add(text);
            if (node is DependencyObject d)
                foreach (var child in LogicalTreeHelper.GetChildren(d)) Walk(child);
        }
        Walk(root);
        return found;
    }

    /// <summary>Leaving and disbanding cost something, so a click alone must not send them.</summary>
    [Fact]
    public void LeavingAsksFirst() => Run(() =>
    {
        var simulator = new TournamentSimulator();
        var w = Window(simulator);
        w.LoadAsync().GetAwaiter().GetResult();
        var teams = w.Data!.Teams!.Count;
        var before = w.Root.Children.Count;

        Press(w.DangerButton);

        Assert.True(w.Root.Children.Count > before, "the confirmation is on screen");
        Assert.Equal(teams, simulator.MyTeams().Teams!.Count);
        w.Close();
    });

    [Fact]
    public void AcceptingJoinsAndLandsOnTheTeam() => Run(() =>
    {
        var simulator = new TournamentSimulator();
        var invite = simulator.MyTeams().Invites!.First();
        var w = Window(simulator, invite: invite.Id);
        w.LoadAsync().GetAwaiter().GetResult();
        Assert.Equal(invite.TeamName, w.HeadName.Text);

        Press(w.AcceptButton);

        Assert.True(w.Changed);
        Assert.Empty(simulator.MyTeams().Invites!);
        Assert.Contains(simulator.MyTeams().Teams!, t => t.Id == invite.TeamId);
        Assert.Equal(invite.TeamName, w.HeadName.Text);
        Assert.Equal(Strings.Get("MtDoneJoined"), w.NoteText.Text);
        w.Close();
    });

    /// <summary>The sheet sends one request per player and the sent list then shows them, which is
    /// the only way a captain learns who still has to answer.</summary>
    [Fact]
    public void InvitingSendsEachChosenPlayerAndListsThem() => Run(() =>
    {
        var simulator = new TournamentSimulator();
        var invite = simulator.MyTeams().Invites!.First();
        var stranger = simulator.GetTeam(invite.TeamId!).Members!.First();
        var candidates = new[] { new PickCandidate(stranger.UserId, MyTeamsView.NameOf(stranger)) };
        var w = Window(simulator, candidates: candidates);
        w.LoadAsync().GetAwaiter().GetResult();
        Press(Buttons(w.TeamBody).First(b => Equals(b.Content, Strings.Get("MtInvite"))));
        Assert.Equal(Visibility.Visible, w.SheetLayer.Visibility);
        Assert.False(w.SheetOk.IsEnabled);

        Press(Buttons(w.SheetList).Single());
        Assert.True(w.SheetOk.IsEnabled);
        Assert.Equal(Strings.Format("MtInviteOne", MyTeamsView.NameOf(stranger)), w.SheetOk.Content);

        w.SendInvitesAsync().GetAwaiter().GetResult();

        Assert.Equal(Visibility.Collapsed, w.SheetLayer.Visibility);
        Assert.Single(simulator.MyTeams().SentInvites!);
        Assert.Contains(Texts(w.TeamBody), t => t.Contains(Strings.Get("MtSent")));
        w.Close();
    });

    [Fact]
    public void WithNoSessionItAsksToSignInAndAsksTheServerNothing() => Run(() =>
    {
        var w = new MyTeamsWindow(new PreviewTournamentApi(new TournamentSimulator(), () => null, null), null,
            () => Array.Empty<PickCandidate>(), null);
        w.LoadAsync().GetAwaiter().GetResult();
        Assert.Null(w.Data);
        Assert.Equal(Strings.Get("MtSignInTitle"), w.CenterTitle.Text);
        Assert.Equal(Strings.Get("MtSignIn"), w.CenterButton.Content);
        Assert.Equal(Visibility.Collapsed, w.RailCard.Visibility);
        w.Close();
    });

    /// <summary>A card built in code is checked by nothing at compile time, and this one only
    /// ever appears when somebody else invites the player.</summary>
    [Fact]
    public void TheTeamToastsBuildAndSayWhatTheInviteNeeds() => Run(() =>
    {
        static AppToast.ToastAction Act(string key, bool primary) => new(Strings.Get(key), primary, () => { });
        var invite = MultiplayerTab.TeamInviteToast("Tercios de Flandes", new[] { Act("MtToastDecline", false), Act("MtToastView", false), Act("MtToastAccept", true) });
        Assert.Equal(WarsOfLibertyLauncher.Services.ToastCause.News, invite.Cause);
        Assert.True(invite.PreferDesktop);
        Assert.Equal("TF", invite.Crest!.Text);

        var panel = new StackPanel { Width = 420 };
        panel.Children.Add(AppToast.BuildCrested(invite, invite.Crest!, () => { }));
        var joined = MultiplayerTab.TeamJoinedToast("Tercios de Flandes", new[] { Act("MtToastViewTeam", false) });
        panel.Children.Add(AppToast.BuildCrested(joined, joined.Crest!, () => { }));
        var declined = MultiplayerTab.TeamDeclinedToast("Tercios de Flandes");
        panel.Children.Add(AppToast.BuildCrested(declined, declined.Crest!, () => { }));
        panel.Measure(new Size(420, double.PositiveInfinity));
        panel.Arrange(new Rect(panel.DesiredSize));
        panel.UpdateLayout();
        Assert.Equal(3, panel.Children.Count);
        Assert.Equal(3, Buttons(panel).Count(b => b.Content is string && b.MinWidth == 96 && panel.Children[0].IsAncestorOf(b)));
    });

    private static List<Button> Buttons(DependencyObject root)
    {
        var found = new List<Button>();
        void Walk(object node)
        {
            if (node is Button b) found.Add(b);
            if (node is DependencyObject d)
                foreach (var child in LogicalTreeHelper.GetChildren(d)) Walk(child);
        }
        Walk(root);
        return found;
    }
}
