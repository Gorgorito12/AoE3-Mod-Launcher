using WarsOfLibertyLauncher.Models.Multiplayer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Services.Multiplayer;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>
/// The unified Library (design 68a): the list of mods, its toolbar and the cover of the selected
/// mod. The Workshop lives here now, so installing starts from the same button as playing. The
/// install, update, repair and check code did not move: this file only draws their state.
/// </summary>
public partial class MainWindow
{
    private LibraryFilter _libraryFilter = LibraryFilter.All;
    private LibrarySort _librarySort = LibrarySort.Recent;
    private string _libraryQuery = "";
    private bool _libraryDescriptionOpen;
    private bool _libraryRefreshQueued;
    private string? _lastOperationModId;
    private Popup? _libraryCopyPopup;
    private Popup? _libraryDownloadPopup;
    private readonly List<string> _libraryShots = new();
    private int _libraryShotIndex;
    private readonly List<ImageBrush> _libraryCoverBrushes = new();
    private string _libraryRailKey = "";
    private string _libraryCoverKey = "";
    private bool? _libraryRailOpen;
    private bool _libraryClipQueued;

    /// <summary><c>--preview-dialog=name:variant</c>: paints one state of a dialog and nothing more.</summary>
    private void PreviewDialogState(string spec)
    {
        var parts = spec.Split(':', 2);
        var variant = parts.Length > 1 ? parts[1] : "";
        switch (parts[0].ToLowerInvariant())
        {
            case "signin":
                new GitHubLoginDialog(_multiplayerSession, variant) { Owner = this }.ShowDialog();
                break;
            case "small":
                PreviewSmallDialog(variant);
                break;
            case "createroom":
                PreviewCreateRoom(variant);
                break;
            case "newtournament":
                PreviewNewTournament(variant);
                break;
            case "enroll":
                PreviewEnroll(variant);
                break;
            case "teams":
                PreviewMyTeams(variant);
                break;
            case "matchwatch":
                PreviewMatchWatch(variant);
                break;
            case "packager":
                new TranslationPackagerDialog(_config, variant) { Owner = this }.ShowDialog();
                break;
            case "patchgen":
                new PatchGeneratorDialog(_config, variant) { Owner = this }.ShowDialog();
                break;
            case "selfinstall":
                const long copy = 165L * 1024 * 1024;
                new SelfInstallPromptDialog(SelfInstallService.CanonicalDir, copy,
                    variant == "lowdisk" ? 31L * 1024 * 1024 : 12L * 1024 * 1024 * 1024) { Owner = this }.ShowDialog();
                break;
            case "selfuninstall":
                new SelfUninstallDialog(PreviewUninstallFacts(variant), elevated: false) { Owner = this }.ShowDialog();
                break;
            case "launcherupdate":
                new LauncherUpdateDialog(LauncherUpdateDemo.Result(variant == "link"), _config,
                    variant == "game" ? () => true : null,
                    variant == "busy" ? () => "Hundred Days" : null,
                    preview: variant) { Owner = this }.ShowDialog();
                break;
            case "settingsupdate":
                _pendingLauncherUpdate = LauncherUpdateDemo.Result(false);
                _previewPendingUpdate = true;
                LauncherSettingsButton_Click(this, new RoutedEventArgs());
                _launcherSettingsDialog?.ShowSectionNamed("mods");
                break;
            case "radmin":
                var stage = variant switch
                {
                    "offline" => RadminStage.InstalledNotRunning,
                    "loggedin" => RadminStage.LoggedIn,
                    "connected" or "steps" => RadminStage.InAoE3Network,
                    _ => RadminStage.NotInstalled,
                };
                var radmin = new RadminAssistantWindow(_config, previewStage: stage);
                radmin.ConfigurePreview(stepsOpen: variant == "steps", installing: variant == "installing",
                    realDownload: variant == "realdownload");
                radmin.Show();
                break;
            case "publish":
                var wizard = new PublishModDialog { Owner = this };
                ConfigurePublishWizardStrings(wizard);
                wizard.FillPreview(int.TryParse(variant, out var wizardStep) ? wizardStep : 3);
                wizard.ShowDialog();
                break;
            case "bar":
                if (variant == "outnet")
                {
                    _previewOutOfNetwork = true;
                    SetConnectionChip(ConnectionChipStatus.Text.Length > 0 ? ConnectionChipStatus.Text : "Conectado");
                }
                else if (variant == "offline" || variant == "account-offline")
                    ConnectivityState.ReportFailure(new System.Net.Http.HttpRequestException("preview"));
                if (variant.StartsWith("account", StringComparison.Ordinal))
                {
                    MultiplayerView.PreviewAccount(new Models.Multiplayer.LobbyUserSummary
                    {
                        Id = StatsDemoData.ViewerId,
                        DiscordUsername = "gorgorito_12",
                        DisplayName = "Gorgorito",
                    });
                    if (variant == "account-room") MultiplayerView.PreviewRoomTitle = "Sala de Gorgorito";
                    OpenPreviewPopup(() => AccountButton_Click(this, new RoutedEventArgs()));
                }
                break;
            case "installfolder":
                PreviewInstallFolder(variant);
                break;
            case "updatepill":
                // A sample update is pending, so pressing the pill opens its dialog in preview mode
                // (as "settingsupdate" does): nothing is downloaded.
                _pendingLauncherUpdate = LauncherUpdateDemo.Result(false);
                _previewPendingUpdate = true;
                ApplyLauncherUpdatePillText(_pendingLauncherUpdate, staged: variant == "ready");
                LauncherUpdatePill.Visibility = Visibility.Visible;
                break;
        }
    }

    /// <summary>Design 103: each small dialog, menu and popover, with made-up contents.</summary>
    private void PreviewSmallDialog(string variant)
    {
        var mod = _updateService.Profile;
        switch (variant)
        {
            case "a":
                ThemedConfirmDialog.Ask(this, Strings.Get("DlgLauncherSettingsInstallRelaunchTitle"),
                    Strings.Get("DlgLauncherSettingsInstallRelaunchBody"), Strings.Get("DlgYes"), Strings.Get("DlgNo"));
                break;
            case "b":
                ThemedConfirmDialog.Notice(this, Strings.Get("ModsBrowserAddLocalInvalidTitle"),
                    Strings.Get("ModsBrowserAddLocalInvalidLead"), ConfirmTone.Caution,
                    detail: "mod.json: \"id\" is missing (line 3).");
                break;
            case "c":
                new RemoveFromCollectionDialog(mod.DisplayName, @"D:\Juegos\Wars of Liberty", mod).ShowOver(this);
                break;
            case "c2":
                ThemedConfirmDialog.Ask(this, Strings.Get("DlgGameRunningTitle"), Strings.Get("DlgGameRunningBody"),
                    Strings.Get("DlgGameRunningClose"), Strings.Get("BtnCancel"), ConfirmTone.Destructive, mod: mod);
                break;
            case "d":
                new BackgroundConsentDialog().ShowOver(this);
                break;
            case "e":
                new CompatibilityLayerDialog(@"C:\Games\Age of Empires III\bin\age3y.exe",
                    AppCompatLayerService.Parse("~ WINXPSP3", inCurrentUserHive: true)).ShowOver(this);
                break;
            case "e2":
                ThemedConfirmDialog.AskThreeWay(this, Strings.Get("DlgGameRunningTitle"), Strings.Get("DlgGameRunningBody"),
                    Strings.Get("DlgGameRunningClose"), Strings.Get("BtnCancel"), Strings.Get("DlgGameRunningKeep"), ConfirmTone.Caution);
                break;
            case "f":
                new CrossUserAccountDialog(new RunningAccount.AccountInfo("a-admin", "Miro", Elevated: true, Mismatch: true),
                    @"C:\Users\a-admin\Documents\My Games", @"C:\Users\Miro\Documents\My Games").ShowOver(this);
                break;
            case "g":
            case "g2":
                var update = new StartupUpdateWindow("v1.0.17");
                update.Report(new DownloadProgress(14_200_000, 48_000_000, 30));
                if (variant == "g2") update.ShowFailed("The remote server returned an error: (503) Server Unavailable.");
                update.Show();
                break;
            case "h":
                new UserDataRestoreDialog(new[]
                {
                    new UserDataService.BackupInfo(@"C:\a", new DateTime(2026, 11, 4, 9, 12, 0), 214, 3, 2_200_000),
                    new UserDataService.BackupInfo(@"C:\b", new DateTime(2026, 11, 3, 22, 40, 0), 210, 3, 2_150_000),
                    new UserDataService.BackupInfo(@"C:\c", new DateTime(2026, 11, 1, 18, 3, 0), 198, 0, 1_900_000),
                }, "Wars of Liberty").ShowOver(this);
                break;
            case "i":
                new PasswordPromptDialog(null, "Pedro", mod).ShowOver(this);
                break;
            case "j":
                new RenameRoomDialog("Noche de 2v2, sin lag", mod).ShowOver(this);
                break;
            case "l":
                new PlayerPickerDialog("Invitar jugadores", Strings.Format("MpTeamsInviteIntro", "Los Andes"), Strings.Get("MpTeamsCreateName"),
                    new[]
                    {
                        new PickCandidate("p", "Pedro", "1380 ELO"),
                        new PickCandidate("m", "Mariana", "1512 ELO"),
                        new PickCandidate("l", "Lucas", Disabled: true, DisabledReason: Strings.Get("MpTournamentAlreadyIn")),
                        new PickCandidate("a", "Ana"),
                    },
                    required: 2, okLabel: "", okLabelFormat: Strings.Get("MpPickInvite"), mod: mod).ShowOver(this);
                break;
            case "m":
                SwitchTopTab(TopTab.Multiplayer);
                _ = Controls.MpAlertOverlay.ConfirmAsync(MultiplayerView.TabRootGrid,
                    Strings.Get("MpLeaveDuringMatchTitle"), Strings.Get("MpLeaveDuringMatchHost"),
                    Strings.Get("MpLeaveDuringMatchYes"), Strings.Get("MpLeaveDuringMatchNo"));
                break;
            case "n":
                OpenPreviewPopup(() => LibraryModMenuButton_Click(this, new RoutedEventArgs()));
                break;
            case "o":
                ConnectionChip.Visibility = Visibility.Visible;
                OpenPreviewPopup(() => ConnectionChip_Click(this, new RoutedEventArgs()));
                break;
        }
    }

    /// <summary>Design 88a-c, with the hash and Radmin made up so nothing is read.</summary>
    private void PreviewCreateRoom(string variant)
    {
        ModProfile Mod(string id) => ModRegistry.Find(id) ?? ModRegistry.Default;
        var mods = new[] { Mod("wol"), Mod("hundred-days"), Mod("improvement-mod") };
        var copies = new ModCopyInfo(true, true, new[]
        {
            new ModCopyChoice("a", @"Steam · D:\Juegos\WoL", variant != "c"),
            new ModCopyChoice("b", "Copia de pruebas", variant == "c"),
        });
        var dialog = new CreateLobbyDialog(
            new Services.Multiplayer.MultiplayerSession(_config),
            mods,
            variant == "b" ? mods[1] : mods[0],
            _ => variant switch
            {
                "b" => new TaskCompletionSource<string>().Task,
                "c" => throw new InvalidOperationException("preview"),
                _ => Task.FromResult("3f9a52aab07dc21d"),
            },
            mod => mod.Id == "wol" ? copies : new ModCopyInfo(false, false, Array.Empty<ModCopyChoice>()),
            _ => Task.CompletedTask,
            developerMode: true,
            config: _config,
            openRadminHelp: () => { })
        { Owner = this };
        dialog.PaintNetwork(variant != "b", "26.41.8.112");
        if (variant == "b") dialog.FillPreview("Hundred Days · Ranked 2v2", RoomFormat.TwoVTwo, 1, "guardia");
        if (variant == "c") dialog.FillPreview("WoL", RoomFormat.Casual, 0, "");
        if (variant == "a") dialog.SelectKind(RoomFormat.OneVOne);
        dialog.ShowDialog();
    }

    /// <summary>Design 83, on made-up teams: its window talks to the simulator.</summary>
    private void PreviewMyTeams(string variant)
    {
        if (variant == "l")
        {
            static AppToast.ToastAction Inert(string key, bool primary) => new(Strings.Get(key), primary, () => { });
            const string sample = "Tercios de Flandes";
            ShowAppToast(MultiplayerTab.TeamDeclinedToast(sample) with { AutoDismissMs = 120000 });
            ShowAppToast(MultiplayerTab.TeamJoinedToast(sample, new[] { Inert("MtToastViewTeam", false) }) with { AutoDismissMs = 120000 });
            ShowAppToast(MultiplayerTab.TeamInviteToast(sample, new[] { Inert("MtToastDecline", false), Inert("MtToastView", false), Inert("MtToastAccept", true) }) with { AutoDismissMs = 120000 });
            return;
        }
        const string me = "gorgo";
        static TeamMember P(string name) => new() { UserId = name.ToLowerInvariant(), DisplayName = name };
        static TeamSummary Team(string id, string name, string? tag, string captain, params string[] members) => new()
        {
            Id = id, Name = name, Tag = tag, OwnerUserId = captain.ToLowerInvariant(), Members = members.Select(P).ToList(),
        };
        var ago = DateTimeOffset.UtcNow;
        var teams = new List<TeamSummary>
        {
            Team("andes", "Los Andes", "LA", "Gorgo", "Gorgo", "Cuzco", "Malinche", "Bicorne", "Chasqui"),
            Team("sur", "Hermandad del Sur", "SUR", "Rioplatense", "Rioplatense", "PatagonBravo", "Gorgo"),
            Team("indias", "Compañía de Indias", null, "Gorgo", "Gorgo", "Corsario"),
        };
        var tercios = variant == "h"
            ? Team("tf", "Tercios de Flandes", "TF", "TercioViejo", "TercioViejo", "Sombrerete", "Kaiser", "Vandalia", "Aluclown", "Filibustero")
            : Team("tf", "Tercios de Flandes", "TF", "TercioViejo", "TercioViejo", "Sombrerete", "Kaiser");
        var invites = new List<TeamInvite>
        {
            new() { Id = "tf", TeamId = "tf", TeamName = "Tercios de Flandes", TeamTag = "TF", InvitedBy = "tercioviejo", CreatedAt = ago.AddHours(-3).ToString("O") },
        };
        var sent = variant == "m"
            ? new List<TeamSentInvite> { new() { Id = "s", TeamId = "andes", InvitedUserId = "mariscal", InvitedName = "Mariscal", CreatedAt = ago.AddDays(-2).ToString("O") } }
            : null;
        var data = new MyTeamsResponse { Teams = teams, Invites = invites, SentInvites = sent };
        if (variant == "f") data = new MyTeamsResponse { Teams = new(), Invites = new() };

        var online = new[] { "Gorgo", "Cuzco", "Malinche", "Chasqui", "Almirante", "Zanjón", "Filibustero", "Kaiser", "Rioplatense", "TercioViejo" };
        var live = new HashSet<string>(online.Select(n => n.ToLowerInvariant()));
        IReadOnlyList<PickCandidate> Online() => online.Select(n => new PickCandidate(n.ToLowerInvariant(), n)).ToList();
        var dialog = new MyTeamsWindow(new PreviewTournamentApi(new TournamentSimulator(), () => null, null), me, Online, null, live.Contains) { Owner = this };
        var select = variant switch
        {
            "b" => "sur",
            "c" or "h" => "inv:tf",
            "d" => "new",
            "e" => "indias",
            "a" or "i" or "m" => "andes",
            _ => null,
        };
        var state = variant switch { "e" => "invite", "g" => "failed", "j" => "signedout", "k" => "offline", _ => "ready" };
        var note = variant == "i" ? "No se pudo enviar la invitación a Zanjón: ya tiene una invitación pendiente de Los Andes." : null;
        dialog.ShowPreview(data, new Dictionary<string, TeamSummary> { ["tf"] = tercios }, select, state, note);
        dialog.ShowDialog();
    }

    /// <summary>Design 82a-l, on a made-up tournament: nothing here reaches a server.</summary>
    private void PreviewEnroll(string variant)
    {
        ModProfile Mod(string id) => ModRegistry.Find(id) ?? ModRegistry.Default;
        const string me = "me";
        static TournamentEntrant Team(string id, string name, string captain, string captainName, string other, string otherName) => new()
        {
            Id = id, Kind = "team", DisplayName = name, Status = "confirmed", CaptainUserId = captain,
            MemberIds = new() { captain, other },
            Members = new()
            {
                new() { UserId = captain, DisplayName = captainName },
                new() { UserId = other, DisplayName = otherName },
            },
        };
        var cup = variant is "a" or "e" or "f" or "h" or "l";
        var build = variant is "c" or "g" or "i" or "j" or "k";
        var tournament = new TournamentDetail
        {
            Id = "t",
            Name = cup ? "Copa de Octubre" : build ? "Copa 2v2 de Improvement" : "Liga 3v3 de Otoño",
            ModId = cup ? "wol" : build ? "improvement-mod" : "hundred-days",
            Format = cup ? "1v1" : build ? "2v2" : "3v3",
            TeamSource = cup ? "solo" : build ? "adhoc" : "registered",
            EntryMode = build ? "approval" : "open",
            Status = "registration",
            Capacity = cup ? 32 : build ? 16 : 8,
            ConfirmedCount = cup ? 29 : build ? 6 : 5,
            RosterSize = cup ? 1 : build ? 2 : 3,
            Entrants = new()
            {
                new() { Id = "x1", Kind = "solo", Status = "confirmed", DisplayName = "Bicorne", CaptainUserId = "bicorne", MemberIds = new() { "bicorne" } },
                new() { Id = "x2", Kind = "solo", Status = "confirmed", DisplayName = "Kaiser", CaptainUserId = "kaiser", MemberIds = new() { "kaiser" } },
            },
        };
        if (variant == "k") tournament.Entrants.Add(new() { Id = "x3", Kind = "team", Status = "confirmed", DisplayName = "Otros", CaptainUserId = "cuzco", MemberIds = new() { "cuzco" } });
        if (variant == "h") tournament.Entrants.Add(new() { Id = "e", Kind = "solo", Status = "confirmed", DisplayName = "Gorgo", CaptainUserId = me, MemberIds = new() { me } });
        if (variant == "i") tournament.Entrants.Add(Team("e", "Los Cóndores", me, "Gorgo", "cuzco", "Cuzco"));
        if (variant == "j") tournament.Entrants.Add(Team("e", "Coraceros del Plata", "rio", "Rioplatense", me, "Gorgo"));

        static TeamMember P(string id, string name) => new() { UserId = id, DisplayName = name };
        var teams = new List<TeamSummary>
        {
            new() { Id = "andes", Name = "Los Andes", OwnerUserId = me, Members = new() { P(me, "Gorgo"), P("cuzco", "Cuzco"), P("malinche", "Malinche"), P("bicorne", "Bicorne"), P("chasqui", "Chasqui") } },
            new() { Id = "sur", Name = "Hermandad del Sur", OwnerUserId = "rio", Members = new() { P("rio", "Rioplatense"), P("pat", "PatagonBravo"), P(me, "Gorgo") } },
            new() { Id = "indias", Name = "Compañía de Indias", OwnerUserId = me, Members = new() { P(me, "Gorgo"), P("corsario", "Corsario") } },
        };
        if (variant == "d") teams.RemoveAt(0);

        var recent = new[] { new EnrollPlayer("cuzco", "Cuzco", true, "2 d"), new EnrollPlayer("mariscal", "Mariscal", false, "5 d"), new EnrollPlayer("malinche", "Malinche", true, "9 d") };
        var online = new[] { "Cuzco", "Malinche", "Bicorne", "Almirante", "Chasqui", "Zanjón", "Kaiser", "Filibustero" }
            .Select(n => new EnrollPlayer(n.ToLowerInvariant(), n, true)).ToList();
        var actions = new EnrollActions
        {
            Enter = _ => variant == "l"
                ? throw new System.Net.Http.HttpRequestException("preview")
                : Task.FromResult(new TournamentEntryResponse { Status = build ? "pending" : "confirmed" }),
            Withdraw = _ => Task.CompletedTask,
            Reload = () => Task.FromResult(tournament),
            ErrorText = ex => ex.Message,
            OpenMyTeams = () => { },
            OpenModInLibrary = () => { },
        };
        var dialog = new EnrollDialog(tournament, me, Mod(tournament.ModId), variant != "e", teams, recent, online, actions) { Owner = this };
        dialog.KnowName(me, "Gorgo");
        if (variant == "f") dialog.ShowDone("confirmed");
        if (variant == "g")
        {
            dialog.PreviewBuild("Los Cóndores", "cuzco");
            dialog.ShowDone("pending");
        }
        if (variant == "k")
        {
            dialog.PreviewBuild("Los Cóndores", null);
            dialog.ShowError(Strings.Get("EnErrSent"), Strings.Format("EnErrTaken", "Cuzco"));
        }
        if (variant == "l") dialog.Loaded += async (_, _) => await dialog.SubmitAsync();
        dialog.ShowDialog();
    }

    /// <summary>Design 81a-c, and <c>pick</c> with the mod list open.</summary>
    private void PreviewNewTournament(string variant)
    {
        ModProfile Mod(string id) => ModRegistry.Find(id) ?? ModRegistry.Default;
        var here = new[] { "wol", "aoe3-tad", "age-of-dynasties-remake", "hundred-days", "improvement-mod" };
        var mod = Mod(variant switch { "b" => "hundred-days", "c" => "improvement-mod", _ => "wol" });
        var mods = ModRegistry.All.OrderBy(m => !here.Contains(m.Id)).ToList();
        var dialog = new CreateTournamentDialog(mod.DisplayName, mods, mod, m => here.Contains(m.Id), 64) { Owner = this };
        if (variant == "b") dialog.PickFormat("3v3");
        if (variant == "c") dialog.NameEntry.Text = "Co";
        if (variant == "pick") dialog.Loaded += (_, _) => OpenPreviewPopup(dialog.OpenModList);
        dialog.ShowDialog();
    }

    /// <summary>The testbed moves the window to the test monitor just after it appears, and a popup
    /// opened before that stays behind on the other monitor.</summary>
    private void OpenPreviewPopup(Action open)
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            open();
        };
        timer.Start();
    }

    /// <summary>Design 74: one mod per state, as the prototype draws them, with the disk figures
    /// made up so nothing is measured.</summary>
    private void PreviewInstallFolder(string variant)
    {
        const string source = InstallFolderDialog.PreviewAoe3Folder;
        const long gb = 1L << 30;
        var (id, clone, free) = variant switch
        {
            "nospace" => ("hundred-days", 5 * gb, (long)(4.1 * gb)),
            "noaoe" => ("improvement-mod", 7 * gb, 1229 * gb),
            _ => ("wol", 8 * gb, 1229 * gb),
        };
        var mod = ModRegistry.Find(id) ?? _updateService.Profile;
        var overlay = variant == "overlay";
        var noAoe = variant == "noaoe";
        var sources = variant == "first"
            ? new List<ModProfile>()
            : ModRegistry.All.Where(p => !p.IsStockGame && p.Id != mod.Id).OrderBy(p => p.Id != "hundred-days").Take(3).ToList();

        var previous = Controls.ModDialogChrome.Current;
        Controls.ModDialogChrome.Current = mod;
        try
        {
            var dialog = new InstallFolderDialog(
                overlay ? source : noAoe ? "" : System.IO.Path.Combine(source, mod.DisplayName),
                overlay || noAoe ? null : source, null, mod.DisplayName,
                requiresAoe3Source: !overlay,
                settingsSources: sources,
                settingsBeforeFirstLaunch: true,
                inPlaceOverlay: overlay,
                preview: new InstallFolderPreview(free, clone, NeverOpened: variant == "neverrun"))
            { Owner = this };
            // Ticked as the prototype draws it. A real install opens with the box empty.
            dialog.CopySettingsCheck.IsChecked = true;
            dialog.CopySettingsCombo.IsEnabled = true;
            dialog.ShowDialog();
        }
        finally { Controls.ModDialogChrome.Current = previous; }
    }

    private void PreviewMatchWatch(string variant)
    {
        var teams = variant == "teams";
        var t = teams ? TournamentDemoData.Teams() : TournamentDemoData.Organiser();
        var m = t.Matches!.FirstOrDefault(x => x.Lobby != null)
            ?? t.Matches!.First(x => x.Entrant1Id != null && x.Entrant2Id != null);
        var stage = variant switch
        {
            "waiting" => TournamentDemoData.MatchWatchStage.Waiting,
            "result" => TournamentDemoData.MatchWatchStage.Finished,
            _ => TournamentDemoData.MatchWatchStage.Playing,
        };
        new MatchWatchWindow(t, m, TournamentDemoData.WatchSample(stage),
            (winner, name) => DiagnosticLog.Write($"Preview: decide {winner} ({name}).")) { Owner = this }.ShowDialog();
    }

    private static SelfInstallService.UninstallFacts PreviewUninstallFacts(string variant)
    {
        var dir = SelfInstallService.CanonicalDir;
        var portable = variant == "portable";
        var warnings = variant == "warnings";
        var none = variant == "nomods";
        return new SelfInstallService.UninstallFacts(
            RunningInstalledCopy: !portable,
            RunningExe: portable ? @"D:\Juegos\Portables\Aoe3ModLauncher.exe" : System.IO.Path.Combine(dir, "Aoe3ModLauncher.exe"),
            InstalledCopyDir: dir,
            ShortcutsPresent: true,
            RemovesTempFiles: true,
            LeftoverFiles: warnings ? 2 : 0,
            DataDir: AppPaths.DataDir,
            CanRemoveUserData: !none,
            InstalledMods: none ? System.Array.Empty<string>() : new[] { "Wars of Liberty", "Hundred Days" },
            LeftoverKeys: warnings ? 2 : 0,
            PendingUploads: warnings ? 2 : 0,
            HasRepairBackups: warnings);
    }

    private void InitializeLibrary()
    {
        PopupGlass.Register();
        PopupGlass.Source = target =>
            LibraryBlurLayer.IsVisible && LibraryBlurLayer.Background is ImageBrush blur
            && target != null && ReferenceEquals(Window.GetWindow(target), this)
                ? (blur, LibraryBackdrop, LibraryOverlays(), GlassTheme.For(_config.GlassLevel).Panel)
                : null;
        LibraryRail.IconFor = p =>
        {
            var icon = TryLoadTileImage(p.ResolveIconSource());
            LearnAccentFromArt(p, icon);
            return icon;
        };
        LibraryRail.ContextMenuFor = BuildLibraryContextMenu;
        LibraryRail.Selected += (_, profile) => SelectLibraryMod(profile);
        LibraryRail.FavoriteToggled += (_, profile) => ToggleLibraryFavorite(profile);
        LibraryRail.FoldToggled += (_, _) =>
        {
            var open = !LibraryMetrics.RailOpen(_config.ModListMode, PlayView.ActualWidth);
            _config.ModListMode = open ? "open" : "closed";
            PersistConfigInBackground();
            ApplyLibraryLayout();
            RefreshLibrary();
        };

        Loaded += (_, _) =>
        {
            ApplyLibraryLayout();
            UpdateLibraryCover();
        };
        PlayView.SizeChanged += (_, _) => ApplyLibraryLayout();
        LibraryBackdrop.SizeChanged += (_, _) => UpdateLibraryCover();
        TournamentBackdrop.SizeChanged += (_, _) =>
        {
            UpdateTournamentCover();
            QueueTournamentClip();
        };
        MainNav.SizeChanged += (_, _) => UpdateLibraryBlurClip();
        LibraryRail.SizeChanged += (_, _) => UpdateLibraryBlurClip();
        LibraryRail.IsVisibleChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateLibraryBlurClip));
        ContentRendered += (_, _) => UpdateLibraryBlurClip();
        LibrarySearchShell.SizeChanged += (_, _) => UpdateLibraryBlurClip();
        ApplyLibraryStrings();
        ApplyLibraryShade();
    }

    /// <summary>Settings › Interface: everything it changes applies at once, cover included.</summary>
    private void ApplyLibraryAppearance()
    {
        GlassTheme.Apply(_config.GlassLevel, LibraryReducedEffects);
        // "Reduced" turns the multiplayer lights off too (rank badges, the update pill, the quick
        // match card's marks), not only the Library's.
        Controls.RankBadge.SetUserReducedEffects(_config.EffectsMode == "low");
        ApplyLibraryShade();
        ApplyDashboardHero(_updateService.Profile);
        UpdateTournamentBackdrop();
        _libraryRailKey = "";
        _libraryFiltersKey = null;
        ApplyLibraryLayout();
        RefreshLibrary();
    }

    private void ApplyLibraryStrings()
    {
        LibrarySearchHint.Text = Strings.Get("LibSearchHint");
        LibraryPublishButton.Content = Strings.Get("LibPublish");
        LibraryRefreshButton.ToolTip = TooltipHelper.Wrap(Strings.Get("LibRefreshTip"));
        AutomationProperties.SetName(LibraryRefreshButton, Strings.Get("LibRefreshTip"));
        DashboardSettingsButton.ToolTip = TooltipHelper.Wrap(Strings.Get("LibSettingsTip"));
        AutomationProperties.SetName(DashboardSettingsButton, Strings.Get("LibSettingsTip"));
        LibraryPlayAnywayButton.Content = Strings.Get("LibPlayAnyway");
        DashboardSearchInstallButtonText.Text = Strings.Get("LibPickFolder");
        DashboardSearchInstallButton.ToolTip = TooltipHelper.Wrap(Strings.Get("LibPickFolderTip"));
        LibraryCopyCaption.Text = Strings.Get("LibCopyCaption");
        DashboardCopyChip.ToolTip = TooltipHelper.Wrap(Strings.Get("LibCopyTip"));
        LibraryLinksCaption.Text = Strings.Get("LibCommunity");
        DashboardCancelButton.Content = Strings.Get("BtnCancel");
        DashboardRetryButton.Content = Strings.Get("BtnRetry");
        LibrarySortText.Text = SortLabel(_librarySort);
        RefreshLibraryDescriptionButton();
    }

    private static string SortLabel(LibrarySort sort) => Strings.Get(sort switch
    {
        LibrarySort.Name => "LibSortName",
        LibrarySort.Status => "LibSortStatus",
        _ => "LibSortRecent",
    });

    /// <summary>What the primary button says in the Library, or null to keep the legacy text.</summary>
    private static string? LibraryCaption(PrimaryAction action) => action switch
    {
        PrimaryAction.Play => Strings.Get("LibPlay"),
        PrimaryAction.Update => Strings.Get("LibUpdate"),
        PrimaryAction.Install => Strings.Get("LibInstall"),
        _ => null,
    };

    /// <summary>Coalesced: a check, a mod switch and an operation can all ask in the same moment,
    /// and the list is rebuilt once.</summary>
    private void ScheduleLibraryRefresh()
    {
        if (_libraryRefreshQueued) return;
        _libraryRefreshQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _libraryRefreshQueued = false;
            RefreshLibrary();
        }));
    }

    private bool LibraryReducedEffects => GlassTheme.IsReduced(_config.EffectsMode);

    private void RefreshLibrary()
    {
        if (LibraryRail == null || _updateService == null) return;
        var profile = _updateService.Profile;
        ModTheme.Apply(profile.AccentColor, _config.UseModColor);
        ModDialogChrome.Current = profile;
        ModDialogChrome.Config = _config;
        LibraryDetailScroll.Resources["BgNeutral"] = FindResource("ModAccentThumb");

        var all = ModRegistry.All.ToList();
        var states = new Dictionary<string, ModRowState>(StringComparer.OrdinalIgnoreCase);
        ModRowState StateOf(ModProfile p)
        {
            if (!states.TryGetValue(p.Id, out var s)) states[p.Id] = s = BuildModRowState(p);
            return s;
        }

        RefreshLibraryFilters(all, StateOf);

        var groups = LibraryList.Build(all, StateOf, _config.IsFavoriteMod, LastPlayedOf,
            _libraryFilter, _libraryQuery, _librarySort);
        var width = PlayView.ActualWidth;
        var open = LibraryMetrics.RailOpen(_config.ModListMode, width);
        var metrics = LibraryMetrics.For(width, TextScale.CurrentFactor);

        // A check, a switch and an operation each ask for a refresh, usually with nothing new. The
        // list is only rebuilt when something it draws changed: a rebuild is a full layout pass.
        // The selected mod is NOT part of the key: choosing another mod only moves the highlight.
        var railKey = string.Join("|", groups.SelectMany(g => g.Mods.Select(m =>
        {
            var s = StateOf(m);
            return $"{g.Kind}:{m.Id}:{s.Status}:{s.CurrentVersion}:{_config.IsFavoriteMod(m.Id)}:{SubtitleFor(m)}:{m.ResolveIconSource()}";
        }))) + $"|{open}|{metrics.RowHeight:0.0}|{Strings.Language}|{_libraryFilter}";
        if (railKey != _libraryRailKey)
        {
            _libraryRailKey = railKey;
            LibraryRail.Show(groups, StateOf, _config.IsFavoriteMod, profile.Id, CurrentRailOperation(),
                open, metrics, LibraryEmptyTitle(), LibraryEmptyBody(), SubtitleFor);
        }
        else LibraryRail.Select(profile.Id);

        RefreshLibraryStatusChip(profile, StateOf(profile));
        var coverKey = $"{profile.Id}|{Strings.Language}|{string.Join(",", profile.ResolveGallerySources())}";
        if (coverKey != _libraryCoverKey)
        {
            _libraryCoverKey = coverKey;
            RefreshLibraryFacts(profile);
            RefreshLibraryLinks(profile);
            RefreshLibraryShots(profile);
        }
        RefreshLibraryProgress();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(RefreshLibraryDescriptionButton));
    }

    private string? _libraryFiltersKey;

    private bool LibraryIsSearching => !string.IsNullOrWhiteSpace(_libraryQuery);

    private string LibraryEmptyTitle() => Strings.Get(
        _libraryFilter == LibraryFilter.Updates && !LibraryIsSearching ? "LibEmptyUpdatesTitle" : "LibEmptyTitle");

    private string LibraryEmptyBody() => Strings.Get(
        _libraryFilter == LibraryFilter.Updates && !LibraryIsSearching ? "LibEmptyUpdatesBody" : "LibEmptyBody");

    /// <summary>The second line of a row with copies: "v1.2.0e · Principal · 3 copias".</summary>
    private string? SubtitleFor(ModProfile profile)
    {
        if (!_config.Mods.TryGetValue(profile.Id, out var state) || !state.HasMultipleInstalls) return null;
        var live = state.OtherInstalls.Count(o => System.IO.Directory.Exists(o.InstallPath)) + 1;
        if (live < 2) return null;
        var version = state.LastKnownVersion ?? "";
        if (version.Length > 0 && !version.StartsWith("v", StringComparison.OrdinalIgnoreCase)) version = "v" + version;
        return Strings.Format("LibRowCopies", version, CopyDisplayLabel(null, state.InstallPath), live);
    }

    private RailOperation? CurrentRailOperation()
    {
        if (App.PreviewLibrary is { } preview)
            return new RailOperation(_updateService.Profile.Id, preview == "updating" ? 42 : preview == "installing" ? 19 : 63, preview == "stopped");
        var percent = ProgressPanelControl.OverallProgress.Value;
        if (_isBusy && !_isCheckOnly && _operatingModId != null)
            return new RailOperation(_operatingModId, percent, false);
        if (_progressState == ProgressState.Error && _lastOperationModId != null && LibraryCanRetry)
            return new RailOperation(_lastOperationModId, percent, true);
        return null;
    }

    private bool LibraryCanRetry => _progressRetryAction != null
        && ProgressPanelControl.ProgressActionRetry.Visibility == Visibility.Visible;

    private void RefreshLibraryFilters(List<ModProfile> all, Func<ModProfile, ModRowState> stateOf)
    {
        var searched = all.Where(p => LibraryList.Matches(p, _libraryQuery)).ToList();
        // Rebuilt only when what the pills show changed: a mod switch changes none of it.
        int CountOf(LibraryFilter f) => searched.Count(p => LibraryList.Passes(f, stateOf(p)));
        var filtersKey = $"{_libraryFilter}|{Strings.Language}|{CountOf(LibraryFilter.All)}|{CountOf(LibraryFilter.Installed)}"
            + $"|{CountOf(LibraryFilter.NotInstalled)}|{CountOf(LibraryFilter.Updates)}|{CountOf(LibraryFilter.Compatible)}";
        if (filtersKey == _libraryFiltersKey && LibraryFilterRow.Children.Count > 1) return;
        _libraryFiltersKey = filtersKey;
        while (LibraryFilterRow.Children.Count > 1) LibraryFilterRow.Children.RemoveAt(1);

        void Add(LibraryFilter filter, string key)
        {
            var on = filter == _libraryFilter;
            var count = searched.Count(p => LibraryList.Passes(filter, stateOf(p)));
            var words = new StackPanel { Orientation = Orientation.Horizontal };
            words.Children.Add(new TextBlock { Text = Strings.Get(key), VerticalAlignment = VerticalAlignment.Center });
            var number = new TextBlock
            {
                Text = count.ToString(),
                FontWeight = FontWeights.Normal,
                Margin = new Thickness(7, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            number.SetResourceReference(TextBlock.ForegroundProperty, on ? "UiSoftText" : "UiDimText");
            words.Children.Add(number);

            var pill = new Button
            {
                Content = words,
                Style = (Style)FindResource(on ? "LibraryPillOn" : "LibraryPill"),
            };
            AutomationProperties.SetAutomationId(pill, "LibraryFilter_" + filter);
            AutomationProperties.SetName(pill, Strings.Get(key));
            pill.Click += (_, _) =>
            {
                _libraryFilter = filter;
                RefreshLibrary();
            };
            LibraryFilterRow.Children.Add(pill);
        }

        Add(LibraryFilter.All, "LibFilterAll");
        Add(LibraryFilter.Installed, "LibFilterInstalled");
        Add(LibraryFilter.NotInstalled, "LibFilterNotInstalled");
        Add(LibraryFilter.Updates, "LibFilterUpdates");
        Add(LibraryFilter.Compatible, "LibFilterCompatible");
    }

    private void RefreshLibraryStatusChip(ModProfile profile, ModRowState state)
    {
        var (key, brush) = state.Status switch
        {
            _ when profile.IsStockGame => ("LibStatusBase", "UiMutedText"),
            ModRowStatus.Installed => ("LibStatusInstalled", "UiOk"),
            ModRowStatus.UpdateAvailable => ("LibStatusUpdate", "UiInfo"),
            _ => ("LibStatusNotInstalled", "UiLabelText"),
        };
        LibraryStatusText.Text = Strings.Get(key);
        LibraryStatusDot.SetResourceReference(Shape.FillProperty, brush);
        DashboardVersionChip.Visibility = profile.IsStockGame || string.IsNullOrWhiteSpace(_updateService.CurrentVersion?.Ver)
                                          || state.Status == ModRowStatus.NotInstalled
            ? Visibility.Collapsed
            : Visibility.Visible;
        DashboardSettingsButton.Visibility = state.Status == ModRowStatus.NotInstalled
            ? Visibility.Collapsed
            : Visibility.Visible;
        LibraryPlayAnywayButton.Visibility = state.Status == ModRowStatus.UpdateAvailable
                                             && _primaryAction == PrimaryAction.Update
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void RefreshLibraryFacts(ModProfile profile)
    {
        var (first, firstTip, second, secondTip) = profile.IsStockGame
            ? ("LibFactBaseCopy", "LibFactBaseCopyTip", "LibFactManual", "LibFactManualTip")
            : profile.UpdateMechanism == ModUpdateMechanism.Manual
                ? ("LibFactOwnFolder", "LibFactOwnFolderTip", "LibFactManual", "LibFactManualTip")
                : ("LibFactOwnFolder", "LibFactOwnFolderTip", "LibFactAuto", "LibFactAutoTip");

        TextBlock Fact(string key, string tip) => new()
        {
            Text = Strings.Get(key),
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xEE, 0xF6)),
            ToolTip = TooltipHelper.Wrap(Strings.Get(tip)),
        };

        LibraryFactsRow.Children.Clear();
        LibraryFactsRow.Children.Add(Fact(first, firstTip));
        var dot = new TextBlock { Text = "·", FontSize = 13, Margin = new Thickness(10, 0, 10, 0) };
        dot.SetResourceReference(TextBlock.ForegroundProperty, "UiLabelText");
        LibraryFactsRow.Children.Add(dot);
        LibraryFactsRow.Children.Add(Fact(second, secondTip));
    }

    private void RefreshLibraryLinks(ModProfile profile)
    {
        while (LibraryLinks.Children.Count > 1) LibraryLinks.Children.RemoveAt(1);
        var links = ModLink.BuildDisplayList(profile.OfficialWebsite, profile.Links);
        for (var i = 0; i < links.Count; i++)
        {
            var link = links[i];
            var words = new StackPanel { Orientation = Orientation.Horizontal };
            var label = string.IsNullOrEmpty(link.Label) ? ModsBrowserView.DefaultLinkLabel(link.Type) : link.Label;
            words.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            var arrow = new TextBlock { Text = "↗", FontSize = 11, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            arrow.SetResourceReference(TextBlock.ForegroundProperty, "UiDimText");
            words.Children.Add(arrow);

            var pill = new Button
            {
                Content = words,
                Style = (Style)FindResource("LibraryLinkPill"),
                ToolTip = TooltipHelper.Wrap(link.Url),
            };
            AutomationProperties.SetAutomationId(pill, "LibraryLink_" + i);
            AutomationProperties.SetName(pill, label);
            pill.Click += (_, _) => SafeUrl.TryOpen(link.Url);
            LibraryLinks.Children.Add(pill);
        }
        LibraryLinks.Visibility = links.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshLibraryShots(ModProfile profile)
    {
        _ = EnsureScreenshotsAsync(profile);
        _libraryShots.Clear();
        _libraryShots.AddRange(profile.ResolveGallerySources());
        LibraryShots.Children.Clear();
        var width = LibraryMetrics.For(PlayView.ActualWidth).ShotWidth;
        for (var i = 0; i < _libraryShots.Count; i++)
        {
            var index = i;
            var thumb = new Button
            {
                Width = width,
                Height = width * 9 / 16,
                Background = TryLoadTileImage(_libraryShots[i], 360),
                Style = (Style)FindResource("LibraryThumb"),
                ToolTip = TooltipHelper.Wrap(Strings.Get("LibShotTip")),
            };
            AutomationProperties.SetAutomationId(thumb, "LibraryShot_" + i);
            AutomationProperties.SetName(thumb, Strings.Get("LibShotTip"));
            thumb.Click += (_, _) => OpenLibraryShot(index);
            LibraryShots.Children.Add(thumb);
        }
        LibraryShots.Visibility = _libraryShots.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>What the card, the pill and the row of the mod being downloaded say. Runs on every
    /// progress tick, so it touches text and values and never rebuilds anything.</summary>
    private void RefreshLibraryProgress()
    {
        if (LibraryRail == null || _updateService == null) return;
        var operation = CurrentRailOperation();
        LibraryRail.UpdateOperation(operation);

        var displayed = _updateService.Profile.Id;
        var here = operation is { } op && string.Equals(op.ModId, displayed, StringComparison.OrdinalIgnoreCase);
        var percent = (int)(operation?.Percent ?? ProgressPanelControl.OverallProgress.Value);
        var stopped = operation is { Stopped: true };
        if (App.PreviewLibrary != null)
        {
            DashboardProgressLabel.Text = Strings.Format(
                App.PreviewLibrary == "updating" ? "ProgressTitleUpdating" : "ProgressTitleInstalling",
                _updateService.Profile.DisplayName);
            DashboardProgressSubtitle.Text = Strings.Format("LibProgressBytes", "520 MB", "1,24 GB");
            DashboardProgressBar.Value = percent;
            DashboardProgressActions.Visibility = Visibility.Visible;
            DashboardPauseButtonIcon.Text = Strings.Get("LibCardPause");
            DashboardCancelButton.Visibility = stopped ? Visibility.Collapsed : Visibility.Visible;
            DashboardPauseButton.Visibility = stopped ? Visibility.Collapsed : Visibility.Visible;
            DashboardRetryButton.Visibility = stopped ? Visibility.Visible : Visibility.Collapsed;
        }

        DashboardProgressContainer.Visibility = here ? Visibility.Visible : Visibility.Collapsed;
        LibraryActions.Visibility = here ? Visibility.Collapsed : Visibility.Visible;
        if (here)
        {
            DashboardProgressPercent.Text = percent + " %";
            DashboardProgressLabel.SetResourceReference(TextBlock.ForegroundProperty, "UiTitleText");
            DashboardProgressIcon.FontFamily = (FontFamily)FindResource("BodyFont");
            DashboardProgressIcon.FontWeight = FontWeights.Bold;
            DashboardProgressIcon.FontSize = 15;
            DashboardProgressIcon.Text = stopped ? "!" : "↓";
            DashboardProgressIcon.SetResourceReference(TextBlock.ForegroundProperty, stopped ? "UiErrorText" : "ModAccentText");
            if (stopped)
            {
                DashboardProgressLabel.Text = Strings.Get("LibStoppedTitle");
                LibraryStatusText.Text = Strings.Get("LibStatusStopped");
                LibraryStatusDot.SetResourceReference(Shape.FillProperty, "UiError");
            }
            else
            {
                LibraryStatusText.Text = Strings.Format(
                    (App.PreviewLibrary ?? (_primaryAction == PrimaryAction.Install ? "installing" : "updating")) == "installing"
                        ? "LibStatusInstalling"
                        : "LibStatusUpdating",
                    percent);
                LibraryStatusDot.SetResourceReference(Shape.FillProperty, "UiInfo");
            }
            var bytes = ProgressPanelControl.OverallBytesText.Text ?? "";
            if (!stopped && bytes.Contains('/'))
            {
                // The legacy panel writes "520 MB / 1,24 GB"; the card says it in words (68a).
                var halves = bytes.Split('/', 2);
                DashboardProgressSubtitle.Text = Strings.Format("LibProgressBytes", halves[0].Trim(), halves[1].Trim());
            }
            LibraryProgressFoot.Text = ProgressFoot();
            DashboardProgressContainer.SetResourceReference(Border.BorderBrushProperty, stopped ? "LibraryErrorRing" : "ModAccentLine");
            LibraryProgressDisc.SetResourceReference(Border.BackgroundProperty, stopped ? "LibraryErrorSoft" : "ModAccentSoft");
            DashboardProgressPercent.SetResourceReference(TextBlock.ForegroundProperty, stopped ? "UiErrorText" : "ModAccentText");
            DashboardProgressBar.SetResourceReference(ForegroundProperty, stopped ? "UiError" : "ModAccent");
        }

        var showLine = !here && !LibraryStatus.IsRoutine(DashboardProgressSubtitle.Text, RoutineStatusTemplates());
        LibraryStatusLine.Text = DashboardProgressSubtitle.Text;
        LibraryStatusLine.Visibility = showLine && !string.IsNullOrWhiteSpace(DashboardProgressSubtitle.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;

        RefreshLibraryDownloadPill(operation, percent);
    }

    /// <summary>The bar under the cover says speed and time left; the legacy labels carry a phase
    /// name in front ("Descarga: 8,4 MB/s") that the design does not use.</summary>
    private string ProgressFoot()
    {
        if (App.PreviewLibrary != null) return "8,4 MB/s · " + Strings.Format("LibProgressLeft", "2 min 14 s");
        static string Value(string? text)
        {
            text ??= "";
            var colon = text.LastIndexOf(':');
            return (colon >= 0 && colon + 1 < text.Length ? text[(colon + 1)..] : text).Trim();
        }
        if (string.Equals(ProgressPanelControl.PauseButton.Content?.ToString(), Strings.Get("BtnResume"), StringComparison.Ordinal))
            return Strings.Get("LibPaused");
        var eta = Value(ProgressPanelControl.EtaText.Text);
        // "calculating..." is not a time, so it is not "... left".
        var calculating = string.Equals(eta, Strings.Get("ProgressEtaCalculating"), StringComparison.OrdinalIgnoreCase);
        var parts = new[] { Value(ProgressPanelControl.SpeedText.Text),
                            eta.Length == 0 ? "" : calculating ? eta : Strings.Format("LibProgressLeft", eta) }
            .Where(p => p.Length > 0);
        return string.Join(" · ", parts);
    }

    private static IReadOnlyList<string> RoutineStatusTemplates() => new[]
    {
        Strings.Get("StatusUpToDate"),
        Strings.Get("StatusGameClosed"),
        Strings.Get("StatusCancelledCheck"),
        Strings.Get("StatusNotInstalled"),
        Strings.Get("StatusStockReady"),
        Strings.Get("StatusInstalledVersionUnknown"),
        Strings.Get("StatusUpdatesAvailable"),
        Strings.Get("StatusUpdatePausedPinned"),
        Strings.Get("StatusGhVersionUnknownCanUpdate"),
        Strings.Get("ProgressIdleTitle"),
    };

    private void RefreshLibraryDownloadPill(RailOperation? operation, int percent)
    {
        if (operation is not { } op)
        {
            LibraryDownloadPill.Visibility = Visibility.Collapsed;
            return;
        }
        var name = ModRegistry.Find(op.ModId)?.DisplayName ?? op.ModId;
        LibraryDownloadPill.Visibility = Visibility.Visible;
        LibraryDownloadLabel.Text = op.Stopped ? Strings.Get("LibDownloadStopped") : name;
        LibraryDownloadBar.Value = percent;
        LibraryDownloadPercent.Text = percent + " %";
        LibraryDownloadGlyph.Text = op.Stopped ? "!" : "↓";
        LibraryDownloadBar.SetResourceReference(ForegroundProperty, op.Stopped ? "UiError" : "ModAccent");
        LibraryDownloadPill.SetResourceReference(BorderBrushProperty, op.Stopped ? "LibraryErrorRing" : "ModAccentLine");
        LibraryDownloadDisc.SetResourceReference(Border.BackgroundProperty, op.Stopped ? "LibraryErrorSoft" : "ModAccentSoft");
        var text = op.Stopped ? "UiErrorText" : "ModAccentText";
        LibraryDownloadPercent.SetResourceReference(TextBlock.ForegroundProperty, text);
        LibraryDownloadGlyph.SetResourceReference(TextBlock.ForegroundProperty, text);
    }

    private void SelectLibraryMod(ModProfile profile)
    {
        DiagnosticLog.Write($"LIBRARY select '{profile.Id}'.");
        _libraryDescriptionOpen = false;
        if (string.Equals(profile.Id, _updateService.Profile.Id, StringComparison.OrdinalIgnoreCase)) return;
        LoadModProfile(profile);
    }

    private void ToggleLibraryFavorite(ModProfile profile)
    {
        if (_config.IsFavoriteMod(profile.Id)) _config.RemoveFavoriteMod(profile.Id);
        else _config.AddFavoriteMod(profile.Id);
        PersistConfigInBackground();
        RefreshLibrary();
    }

    private void LibrarySearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _libraryQuery = LibrarySearchBox.Text;
        LibrarySearchHint.Visibility = _libraryQuery.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ScheduleLibraryRefresh();
    }

    private void LibrarySortButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            Style = (Style)FindResource("LibraryMenu"),
            Width = 170,
            PlacementTarget = LibrarySortButton,
            Placement = PlacementMode.Bottom,
        };
        foreach (var sort in new[] { LibrarySort.Recent, LibrarySort.Name, LibrarySort.Status })
        {
            var item = MenuRow(SortLabel(sort), () =>
            {
                _librarySort = sort;
                LibrarySortText.Text = SortLabel(sort);
                RefreshLibrary();
            });
            AutomationProperties.SetAutomationId(item, "LibrarySort_" + sort);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void LibraryRefreshButton_Click(object sender, RoutedEventArgs e)
        => ModsBrowserView_RefreshCatalogRequested(this, EventArgs.Empty);

    private void LibraryPublishButton_Click(object sender, RoutedEventArgs e)
        => ModsBrowserView_PublishRequested(this, EventArgs.Empty);

    private void LibraryMoreButton_Click(object sender, RoutedEventArgs e)
    {
        _libraryDescriptionOpen = !_libraryDescriptionOpen;
        ApplyLibraryLayout();
    }

    private void LibraryPlayAnywayButton_Click(object sender, RoutedEventArgs e) => ExecutePlay(sender, e);

    /// <summary>Four lines, and a "Read more" only when a fifth exists: measured with the same font
    /// and width the description is laid out in, so the button cannot lie about the text.</summary>
    private void RefreshLibraryDescriptionButton()
    {
        if (LibraryMoreButton == null) return;
        var width = DashboardDescText.MaxWidth;
        if (double.IsInfinity(width) || width <= 0) return;
        var metrics = LibraryMetrics.For(PlayView.ActualWidth, TextScale.CurrentFactor);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var text = new FormattedText(
            DashboardDescText.Text ?? "",
            Strings.Culture,
            FlowDirection.LeftToRight,
            new Typeface(DashboardDescText.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            metrics.DescriptionSize,
            Brushes.White,
            dpi)
        {
            MaxTextWidth = width,
            LineHeight = metrics.DescriptionSize * LibraryMetrics.DescriptionLineHeight,
        };
        var clamped = text.Height > metrics.DescriptionSize * LibraryMetrics.DescriptionLineHeight * LibraryMetrics.DescriptionLines + 1;
        LibraryMoreButton.Visibility = clamped || _libraryDescriptionOpen ? Visibility.Visible : Visibility.Collapsed;
        LibraryMoreButton.Content = Strings.Get(_libraryDescriptionOpen ? "LibLess" : "LibMore");
    }

    /// <summary>Also called when the text size changes in Settings (<see cref="App.RefreshTextScale"/>).</summary>
    internal void ApplyLibraryLayout()
    {
        var width = PlayView.ActualWidth;
        if (width <= 0) return;
        var m = LibraryMetrics.For(width, TextScale.CurrentFactor);
        var open = LibraryMetrics.RailOpen(_config.ModListMode, width);
        LibraryRail.ApplyMetrics(m);
        if (_libraryRailOpen != open)
        {
            // Crossing 1400 px folds or opens the list, and nothing else would ask for that.
            _libraryRailOpen = open;
            ScheduleLibraryRefresh();
        }

        LibraryRailColumn.Width = new GridLength(open ? m.RailWidth : LibraryMetrics.FoldedRailWidth);
        LibraryGapColumn.Width = new GridLength(m.ColumnGap);
        LibraryBody.Margin = new Thickness(m.SidePadding, 0, m.SidePadding, m.BottomPadding);
        LibraryToolbar.Margin = new Thickness(m.SidePadding, 14, m.SidePadding, 14);
        LibrarySearchShell.Width = Math.Max(200, Math.Clamp(width * .23, 260, 460));

        DashboardTitleText.FontSize = m.TitleSize;
        LibraryTitleShadow.FontSize = m.TitleSize;
        DashboardTitleText.LineHeight = LibraryTitleShadow.LineHeight = m.TitleSize * 1.05;
        LibraryAuthorText.FontSize = m.AuthorSize;

        var line = m.DescriptionSize * LibraryMetrics.DescriptionLineHeight;
        DashboardDescText.FontSize = m.DescriptionSize;
        DashboardDescText.LineHeight = line;
        DashboardDescText.MaxWidth = m.DescriptionSize * LibraryMetrics.DescriptionWidthEm;
        DashboardDescText.MaxHeight = _libraryDescriptionOpen ? double.PositiveInfinity : line * LibraryMetrics.DescriptionLines + 1;
        RefreshLibraryDescriptionButton();

        var gap = m.DetailGap;
        LibraryTitleBlock.Margin = new Thickness(0, gap, 0, 0);
        DashboardDescText.Margin = new Thickness(0, gap, 0, 0);
        LibraryMoreButton.Margin = new Thickness(0, gap - 8, 0, 0);
        LibraryActions.Margin = new Thickness(0, gap, 0, 0);
        DashboardProgressContainer.Margin = new Thickness(0, gap, 0, 0);
        LibraryFacts.Margin = new Thickness(0, Math.Max(0, gap - 10), 0, 0);
        LibraryShots.Margin = new Thickness(0, gap, 0, 0);
        LibraryLinks.Margin = new Thickness(0, gap, 0, 0);

        foreach (var thumb in LibraryShots.Children.OfType<Button>())
        {
            thumb.Width = m.ShotWidth;
            thumb.Height = m.ShotWidth * 9 / 16;
        }
        UpdateLibraryBlurClip();
    }

    private readonly HashSet<string> _accentTried = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A mod whose author declared no colour takes the dominant colour of its icon, once the icon is
    /// decoded (the rail already decodes it, so this costs no download). Kept in the accent cache, so
    /// the next session opens in it; a declared colour is never touched.
    /// </summary>
    private void LearnAccentFromArt(ModProfile profile, ImageBrush? icon)
    {
        if (profile.AccentDeclared || icon?.ImageSource is not BitmapSource bitmap) return;
        if (ModAccentCache.ImageKeyOf(profile) is not { } key) return;
        if (!_accentTried.Add(profile.Id + "|" + key)) return;

        void Take()
        {
            try
            {
                var taken = ModRegistry.TakenAccents(ModRegistry.All, except: profile);
                if (ModAccentResolver.Pick(ModAccentResolver.FromBitmap(bitmap), taken) is not { } color) return;
                var hex = ModAccentResolver.Hex(color);
                _ = System.Threading.Tasks.Task.Run(() => ModAccentCache.Put(key, hex));
                profile.AccentLearned = true;
                if (string.Equals(profile.AccentColor, hex, StringComparison.OrdinalIgnoreCase)) return;
                profile.AccentColor = hex;
                DiagnosticLog.Write($"Accent: '{profile.Id}' declares no colour; took {hex} from its icon.");
                _libraryRailKey = "";
                ScheduleLibraryRefresh();
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"Accent from art failed for '{profile.Id}': {ex.Message}");
            }
        }

        if (bitmap.IsDownloading) bitmap.DownloadCompleted += (_, _) => Take();
        else Take();
    }

    /// <summary>The layers drawn over the blurred cover and under every panel, in order: a menu
    /// lays the same ones under its tint so it is the same glass as the panel it opens over.</summary>
    private Brush[] LibraryOverlays()
    {
        var overlays = new List<Brush>();
        bool afterBlur = false;
        foreach (UIElement child in LibraryBackdrop.Children)
        {
            if (ReferenceEquals(child, LibraryBlurLayer)) { afterBlur = true; continue; }
            if (afterBlur && child is Shape { Fill: { } fill, Visibility: Visibility.Visible }) overlays.Add(fill);
        }
        return overlays.ToArray();
    }

    private void ApplyLibraryShade()
    {
        var (l0, l1) = GlassTheme.Normalize(_config.GlassLevel) switch
        {
            GlassTheme.Solid => (.96, .86),
            GlassTheme.Maximum => (.80, .66),
            _ => (.86, .72),
        };
        static Color Navy(double alpha) => Color.FromArgb((byte)Math.Round(alpha * 255), 0x0F, 0x1C, 0x2E);
        var shade = new LinearGradientBrush { StartPoint = new Point(0, .5), EndPoint = new Point(1, .5) };
        shade.GradientStops.Add(new GradientStop(Navy(l0), 0));
        shade.GradientStops.Add(new GradientStop(Navy(l1), .26));
        shade.GradientStops.Add(new GradientStop(Navy(.42), .58));
        shade.GradientStops.Add(new GradientStop(Navy(.22), 1));
        shade.Freeze();
        LibraryShadeSide.Fill = shade;
    }

    /// <summary>Paints the cover: the mod's image with its blurred twin, or its colour when there is
    /// no image, when the player turned images off, or with reduced effects.</summary>
    private void PaintLibraryCover(ModProfile profile, IReadOnlyList<string> heroes, ImageBrush? first)
    {
        _libraryCoverBrushes.Clear();
        var accent = ModTheme.AccentOf(profile.AccentColor, _config.UseModColor);
        var useImage = first != null && _config.ShowModImage && !LibraryReducedEffects;
        if (useImage)
        {
            _libraryCoverBrushes.Add(first!);
            WatchCoverDownload(first!);
            DashboardBgFill.Background = first;
        }
        else
        {
            DashboardBgFill.Background = !LibraryReducedEffects && _config.ShowModImage
                ? ModTheme.HeroGradient(accent)
                : ModTheme.QuietBackground(accent);
        }

        LibraryBlurLayer.Background = null;
        LibraryBlurLayer.Visibility = Visibility.Collapsed;
        if (useImage && GlassTheme.Normalize(_config.GlassLevel) != GlassTheme.Solid)
        {
            if (first!.ImageSource is BitmapImage { IsDownloading: true } remote)
                remote.DownloadCompleted += (_, _) => PaintLibraryBlur(first);
            else
                PaintLibraryBlur(first);
        }
        UpdateLibraryCover();
        UpdateLibraryBlurClip();
    }

    private void PaintLibraryBlur(ImageBrush cover)
    {
        if (!_libraryCoverBrushes.Contains(cover)) return;
        var blurred = ReferenceEquals(cover, _heroBrush) ? _heroBlurBrush : null;
        blurred ??= BuildBlurredCover(cover.ImageSource);
        if (blurred == null) return;
        if (ReferenceEquals(cover, _heroBrush))
        {
            _heroBlurBrush = blurred;
            RememberHeroBlur(cover, blurred);
        }
        LibraryBlurLayer.Background = blurred;
        LibraryBlurLayer.Visibility = Visibility.Visible;
        _libraryCoverBrushes.Add(blurred);
        UpdateLibraryCover();
        UpdateLibraryBlurClip();
    }

    private void WatchCoverDownload(ImageBrush brush)
    {
        if (brush.ImageSource is BitmapImage { IsDownloading: true } image)
            image.DownloadCompleted += (_, _) => UpdateLibraryCover();
    }

    /// <summary>The cover drawn small and blurred once, so the panels cost no more than a picture.</summary>
    internal static ImageBrush? BuildBlurredCover(ImageSource source)
    {
        try
        {
            if (source is not BitmapSource { PixelWidth: > 0 } bitmap) return null;
            const int width = 320;
            var height = Math.Max(1, width * bitmap.PixelHeight / bitmap.PixelWidth);
            // Rendered off screen this goes through the software rasterizer, which keeps its own copy
            // of what it was handed. Handing it the size it needs keeps that copy small.
            var fit = (width + 24.0) / bitmap.PixelWidth;
            var small = new CachedBitmap(new TransformedBitmap(bitmap, new ScaleTransform(fit, fit)),
                BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            small.Freeze();
            var image = new Image
            {
                Source = small,
                Stretch = Stretch.Fill,
                Margin = new Thickness(-12),
                Effect = new BlurEffect { Radius = 7 },
            };
            var host = new Grid { Width = width, Height = height, ClipToBounds = true };
            host.Children.Add(image);
            host.Measure(new Size(width, height));
            host.Arrange(new Rect(0, 0, width, height));
            var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            target.Render(host);
            target.Freeze();
            return new ImageBrush(target) { Stretch = Stretch.Fill };
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Blurred cover failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>CSS <c>center 25%/cover</c>: the image fills the window and is never distorted. The
    /// window is the size that matters because the cover runs behind the title bar too.</summary>
    private void UpdateLibraryCover()
    {
        var box = LibraryBackdrop.RenderSize;
        if (box.Width <= 0 || box.Height <= 0) return;
        foreach (var brush in _libraryCoverBrushes)
        {
            if (brush.ImageSource is not BitmapSource { PixelWidth: > 0 } image) continue;
            brush.Viewbox = HeroCover.Viewbox(image.PixelWidth, image.PixelHeight, box.Width, box.Height);
        }
    }

    /// <summary>The blurred image shows only behind the panels that ask for it.</summary>
    private void UpdateLibraryBlurClip()
    {
        if (LibraryBlurLayer.Visibility != Visibility.Visible) return;
        if (!IsLoaded)
        {
            LibraryBlurLayer.Clip = Geometry.Empty;
            return;
        }
        // Once per frame at most: a resize moves three panels, and each would rebuild the clip.
        if (_libraryClipQueued) return;
        _libraryClipQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            _libraryClipQueued = false;
            BuildLibraryBlurClip();
        }));
    }

    private void BuildLibraryBlurClip()
    {
        var group = new GeometryGroup();
        void Add(FrameworkElement element, double radius)
        {
            if (!element.IsVisible || element.ActualWidth <= 0) return;
            // TransformToVisual: the tabs and the list are siblings of the backdrop, not its children.
            var rect = element.TransformToVisual(LibraryBackdrop).TransformBounds(new Rect(element.RenderSize));
            group.Children.Add(new RectangleGeometry(rect, radius, radius));
        }
        try
        {
            Add(MainNav, 0);
            Add(LibraryRail, 14);
            Add(LibrarySearchShell, 8);
            group.Freeze();
            LibraryBlurLayer.Clip = group;
        }
        catch (InvalidOperationException)
        {
            // A panel between layouts; the next size change draws it.
        }
    }

    private MenuItem MenuRow(string label, Action action, bool danger = false, string? aux = null)
    {
        var item = new MenuItem { Header = label, Style = (Style)FindResource("LibraryMenuItem"), InputGestureText = aux ?? "" };
        if (danger) item.SetResourceReference(ForegroundProperty, "UiDangerText");
        AutomationProperties.SetName(item, label);
        item.Click += (_, _) => action();
        return item;
    }

    private Separator MenuLine() => new() { Style = (Style)FindResource("LibraryMenuSeparator") };

    /// <summary>Runs an action that reads the displayed mod once that mod IS the displayed one.
    /// Switching rebuilds the update service and checks, so "now" would act on the previous mod.</summary>
    private void WithLibraryMod(ModProfile profile, Action action)
    {
        if (string.Equals(profile.Id, _updateService.Profile.Id, StringComparison.OrdinalIgnoreCase))
        {
            action();
            return;
        }
        SelectLibraryMod(profile);
        var waited = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) =>
        {
            var ready = string.Equals(profile.Id, _updateService.Profile.Id, StringComparison.OrdinalIgnoreCase) && !_isBusy;
            if (!ready && ++waited < 100) return;
            timer.Stop();
            if (ready) action();
        };
        timer.Start();
    }

    private ContextMenu BuildLibraryContextMenu(ModProfile profile)
    {
        var state = BuildModRowState(profile);
        var installed = state.Status is ModRowStatus.Installed or ModRowStatus.UpdateAvailable;
        var operation = CurrentRailOperation();
        var running = operation is { } op && string.Equals(op.ModId, profile.Id, StringComparison.OrdinalIgnoreCase);
        var favorite = _config.IsFavoriteMod(profile.Id);

        var menu = new ContextMenu { Style = (Style)FindResource("LibraryMenu") };

        if (installed && !running)
            menu.Items.Add(MenuRow(Strings.Get("LibMenuPlay"), () => WithLibraryMod(profile, () => ExecutePlay(this, new RoutedEventArgs()))));
        if (state.Status == ModRowStatus.UpdateAvailable && !running)
            menu.Items.Add(MenuRow(Strings.Get("LibUpdate"), () => WithLibraryMod(profile, () => PlayButton_Click(this, new RoutedEventArgs()))));
        if (state.Status == ModRowStatus.NotInstalled && !running)
            menu.Items.Add(MenuRow(Strings.Get("LibInstall"), () => WithLibraryMod(profile, () => PlayButton_Click(this, new RoutedEventArgs()))));
        if (running && operation!.Value.Stopped)
            menu.Items.Add(MenuRow(Strings.Get("LibMenuRetry"), () => DashboardRetryButton_Click(this, new RoutedEventArgs())));
        if (running && !operation!.Value.Stopped)
        {
            var paused = string.Equals(ProgressPanelControl.PauseButton.Content?.ToString(), Strings.Get("BtnResume"), StringComparison.Ordinal);
            menu.Items.Add(MenuRow(Strings.Get(paused ? "LibMenuResume" : "LibMenuPause"), () => DashboardPauseButton_Click(this, new RoutedEventArgs())));
            menu.Items.Add(MenuRow(Strings.Get("LibMenuCancel"), () => DashboardCancelButton_Click(this, new RoutedEventArgs())));
        }

        if (installed)
        {
            menu.Items.Add(MenuRow(Strings.Get("LibSettingsTip"), () => WithLibraryMod(profile, () => DashboardSettingsButton_Click(this, new RoutedEventArgs()))));
            menu.Items.Add(MenuRow(Strings.Get("LibMenuFolder"), () => WithLibraryMod(profile, () => OpenFolderButton_Click(this, new RoutedEventArgs()))));
            if (!profile.IsStockGame && !running)
                menu.Items.Add(MenuRow(Strings.Get("LibMenuCheckUpdate"), () => WithLibraryMod(profile, () =>
                {
                    InvalidateActiveModCheckCache();
                    _ = CheckAsync();
                }), aux: state.CurrentVersion));
        }
        menu.Items.Add(MenuRow(Strings.Get(favorite ? "LibFavoriteRemove" : "LibFavoriteAdd"), () => ToggleLibraryFavorite(profile)));
        var links = ModLink.BuildDisplayList(profile.OfficialWebsite, profile.Links);
        if (links.Count > 0)
            menu.Items.Add(MenuRow(Strings.Get("LibMenuPage"), () => SafeUrl.TryOpen(links[0].Url)));
        var removable = state.IsInUserCollection && !state.IsBuiltIn;
        var uninstallable = installed && !profile.IsStockGame && !running;
        if (removable || uninstallable) menu.Items.Add(MenuLine());
        if (removable)
            menu.Items.Add(MenuRow(Strings.Get("LibMenuRemove"), () => ModsBrowserView_RemoveFromCollectionRequested(this, profile), danger: true));
        if (uninstallable)
        {
            var copy = SubtitleFor(profile) != null ? CopyDisplayLabel(null, _config.GetState(profile.Id).InstallPath) : null;
            menu.Items.Add(MenuRow(
                copy != null ? Strings.Format("LibMenuUninstallCopy", copy) : Strings.Get("LibMenuUninstall"),
                () => WithLibraryMod(profile, () => UninstallMenuItem_Click(this, new RoutedEventArgs())),
                danger: true));
        }
        return menu;
    }

    /// <summary>The mod's menu, the same one as a right click: a right click is a shortcut and is
    /// never the only way to anything (BASE.md).</summary>
    private void LibraryModMenuButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = BuildLibraryContextMenu(_updateService.Profile);
        menu.PlacementTarget = LibraryModMenuButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        menu.VerticalOffset = -6;
        menu.IsOpen = true;
    }

    private void DashboardCopyChip_Click(object sender, RoutedEventArgs e)
    {
        if (_libraryCopyPopup != null)
        {
            _libraryCopyPopup.IsOpen = false;
            return;
        }

        var state = _config.GetActiveState();
        var rows = new List<(string Id, string Label, string Path, string Version, bool Active)>
        {
            (state.ActiveInstallId, CopyDisplayLabel(null, state.InstallPath), state.InstallPath, state.LastKnownVersion, true),
        };
        foreach (var other in state.OtherInstalls.Where(o => System.IO.Directory.Exists(o.InstallPath)))
            rows.Add((other.Id, CopyDisplayLabel(null, other.InstallPath), other.InstallPath, other.LastKnownVersion, false));
        var labels = PathDisplay.DisambiguateLabels(rows.Select(r => (r.Label, r.Path)).ToList());

        var panel = new StackPanel { Width = 386 };
        var header = new TextBlock
        {
            Text = Strings.Get("LibCopiesHeader"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(10, 10, 10, 6),
        };
        header.SetResourceReference(TextBlock.ForegroundProperty, "UiMutedText");
        panel.Children.Add(header);

        Popup? popup = null;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var mark = new TextBlock
            {
                Text = row.Active ? "✓" : "",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) };
            var name = new TextBlock { Text = labels[i], FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            name.SetResourceReference(TextBlock.ForegroundProperty, "UiHeadText");
            var path = new TextBlock
            {
                Text = PathDisplay.CompactPathMiddle(row.Path, 48),
                FontSize = 11,
                Margin = new Thickness(0, 5, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = TooltipHelper.Wrap(row.Path),
            };
            path.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            path.SetResourceReference(TextBlock.ForegroundProperty, "UiDimText");
            words.Children.Add(name);
            words.Children.Add(path);

            var content = new Grid { Margin = new Thickness(10) };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            content.ColumnDefinitions.Add(new ColumnDefinition());
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.Children.Add(mark);
            Grid.SetColumn(words, 1);
            content.Children.Add(words);
            if (!string.IsNullOrWhiteSpace(row.Version))
            {
                var version = new Border
                {
                    Height = 22,
                    Padding = new Thickness(8, 0, 8, 0),
                    CornerRadius = new CornerRadius(11),
                    Background = new SolidColorBrush(Color.FromArgb(0x29, 0x3F, 0xB9, 0x50)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0xB3, 0x56, 0xD3, 0x64)),
                    BorderThickness = new Thickness(1),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = row.Version,
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Foreground = (Brush)FindResource("LibraryVersionText"),
                        FontFamily = (FontFamily)FindResource("MonoFont"),
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                Grid.SetColumn(version, 2);
                content.Children.Add(version);
            }

            var button = new Button
            {
                Content = content,
                Margin = new Thickness(0, 0, 0, 2),
                Style = (Style)FindResource(row.Active ? "LibraryRowSelected" : "LibraryRow"),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            AutomationProperties.SetAutomationId(button, "LibraryCopy_" + i);
            AutomationProperties.SetName(button, labels[i]);
            var id = row.Id;
            var active = row.Active;
            button.Click += async (_, _) =>
            {
                if (popup != null) popup.IsOpen = false;
                if (!active) await SwitchActiveInstallAsync(id);
            };
            panel.Children.Add(button);
        }

        var foot = new Border { Margin = new Thickness(0, 4, 0, 0), Padding = new Thickness(4, 8, 4, 4), BorderThickness = new Thickness(0, 1, 0, 0) };
        foot.SetResourceReference(Border.BorderBrushProperty, "LibraryFooterRule");
        var footRow = new StackPanel { Orientation = Orientation.Horizontal };
        var add = new Button { Content = Strings.Get("LibCopyAdd"), Style = (Style)FindResource("LibraryCardButton"), Height = 30, Padding = new Thickness(12, 0, 12, 0), FontSize = 12 };
        AutomationProperties.SetAutomationId(add, "LibraryCopyAdd");
        add.Click += (_, _) =>
        {
            if (popup != null) popup.IsOpen = false;
            _ = InstallAsync(addNewSlot: true);
        };
        var manage = new Button { Content = Strings.Get("LibCopyManage"), Style = (Style)FindResource("LibraryCardButton"), Height = 30, Padding = new Thickness(12, 0, 12, 0), FontSize = 12, Margin = new Thickness(6, 0, 0, 0), BorderThickness = new Thickness(0), Background = Brushes.Transparent };
        manage.SetResourceReference(ForegroundProperty, "UiMutedText");
        AutomationProperties.SetAutomationId(manage, "LibraryCopyManage");
        manage.Click += (_, _) =>
        {
            if (popup != null) popup.IsOpen = false;
            DashboardSettingsButton_Click(this, new RoutedEventArgs());
            _modPropertiesDialog?.ShowTabNamed("local-files");
        };
        footRow.Children.Add(add);
        footRow.Children.Add(manage);
        foot.Child = footRow;
        panel.Children.Add(foot);

        popup = LibraryPopup(panel, DashboardCopyChip, PlacementMode.Top, 8);
        _libraryCopyPopup = popup;
        popup.Closed += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background,
            new Action(() => _libraryCopyPopup = null));
        popup.IsOpen = true;
    }

    private void LibraryDownloadPill_Click(object sender, RoutedEventArgs e)
    {
        if (_libraryDownloadPopup != null)
        {
            _libraryDownloadPopup.IsOpen = false;
            return;
        }
        if (CurrentRailOperation() is not { } op) return;
        var profile = ModRegistry.Find(op.ModId);

        var panel = new StackPanel { Width = 356 };
        var header = new TextBlock
        {
            Text = Strings.Format("LibDownloadsHeader", op.Stopped ? 0 : 1),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(10, 10, 10, 6),
        };
        header.SetResourceReference(TextBlock.ForegroundProperty, "UiMutedText");
        panel.Children.Add(header);

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) };
        var title = new TextBlock { Text = ProgressPanelControl.ProgressTitleText.Text, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        title.SetResourceReference(TextBlock.ForegroundProperty, "UiHeadText");
        var bar = new ProgressBar { Style = (Style)FindResource("LibraryProgressBar"), Height = 4, Margin = new Thickness(0, 7, 0, 0), Value = ProgressPanelControl.OverallProgress.Value };
        if (op.Stopped) bar.SetResourceReference(ForegroundProperty, "UiError");
        words.Children.Add(title);
        words.Children.Add(bar);

        var content = new Grid { Margin = new Thickness(10, 9, 10, 9) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        content.ColumnDefinitions.Add(new ColumnDefinition());
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (profile != null)
            content.Children.Add(new Border { Width = 32, Height = 32, Background = TryLoadTileImage(profile.ResolveIconSource()) });
        Grid.SetColumn(words, 1);
        content.Children.Add(words);
        var percent = new TextBlock { Text = (int)op.Percent + " %", FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        percent.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        percent.SetResourceReference(TextBlock.ForegroundProperty, op.Stopped ? "UiErrorText" : "ModAccentText");
        Grid.SetColumn(percent, 2);
        content.Children.Add(percent);

        Popup? popup = null;
        var row = new Button { Content = content, Style = (Style)FindResource("LibraryRow"), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(row, "LibraryDownloadRow");
        AutomationProperties.SetName(row, ProgressPanelControl.ProgressTitleText.Text ?? "");
        row.Click += (_, _) =>
        {
            if (popup != null) popup.IsOpen = false;
            SwitchTopTab(TopTab.Play);
            if (profile != null) SelectLibraryMod(profile);
        };
        panel.Children.Add(row);

        popup = LibraryPopup(panel, LibraryDownloadPill, PlacementMode.Bottom, 8);
        popup.HorizontalOffset = LibraryDownloadPill.ActualWidth - 370;
        _libraryDownloadPopup = popup;
        popup.Closed += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background,
            new Action(() => _libraryDownloadPopup = null));
        popup.IsOpen = true;
    }

    private Popup LibraryPopup(UIElement content, UIElement anchor, PlacementMode placement, double offset)
    {
        var card = new Border
        {
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Child = content,
        };
        card.SetResourceReference(Border.BackgroundProperty, "LibraryMenuBg");
        card.SetResourceReference(Border.BorderBrushProperty, "GlassRim");
        var popup = new Popup
        {
            Child = card,
            PlacementTarget = anchor,
            Placement = placement,
            VerticalOffset = placement == PlacementMode.Top ? -offset : offset,
            AllowsTransparency = true,
            StaysOpen = false,
        };
        ChromePopups.Track(popup, anchor as Button);
        return popup;
    }

    private void OpenLibraryShot(int index)
    {
        if (_libraryShots.Count == 0) return;
        _libraryShotIndex = (index + _libraryShots.Count) % _libraryShots.Count;
        try
        {
            var source = _libraryShots[_libraryShotIndex];
            // A GIF animates, as it did in the Workshop's viewer; anything else is one decode.
            if (source.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
            {
                LibraryShotImage.Source = null;
                XamlAnimatedGif.AnimationBehavior.SetSourceUri(LibraryShotImage, new Uri(source, UriKind.RelativeOrAbsolute));
            }
            else
            {
                XamlAnimatedGif.AnimationBehavior.SetSourceUri(LibraryShotImage, null);
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 1920;
                image.UriSource = new Uri(source, UriKind.RelativeOrAbsolute);
                image.EndInit();
                LibraryShotImage.Source = image;
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Screenshot failed: {ex.Message}");
            return;
        }
        LibraryShotTitle.Text = Strings.Format("LibShotTitle", _updateService.Profile.DisplayName,
            _libraryShotIndex + 1, _libraryShots.Count);
        var several = _libraryShots.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        LibraryShotPrev.Visibility = several;
        LibraryShotNext.Visibility = several;
        LibraryShotViewer.Visibility = Visibility.Visible;
    }

    private void LibraryShotViewer_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => CloseLibraryShot();

    private void LibraryShotClose_Click(object sender, RoutedEventArgs e) => CloseLibraryShot();

    /// <summary>Closing also stops a GIF, or its clock would keep drawing a hidden image.</summary>
    private void CloseLibraryShot()
    {
        XamlAnimatedGif.AnimationBehavior.SetSourceUri(LibraryShotImage, null);
        LibraryShotImage.Source = null;
        LibraryShotViewer.Visibility = Visibility.Collapsed;
    }

    private void LibraryShotPrev_Click(object sender, RoutedEventArgs e) => OpenLibraryShot(_libraryShotIndex - 1);

    private void LibraryShotNext_Click(object sender, RoutedEventArgs e) => OpenLibraryShot(_libraryShotIndex + 1);
}
