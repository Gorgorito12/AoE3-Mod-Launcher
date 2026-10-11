using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the profile the launcher creates for a mod that has never been run, so the settings the
/// player chose to bring over are there from the FIRST session (GameSettingsSync.BuildSeedProfile,
/// GameSettingsStore.TrySeedProfile), and the rule that an import which lands settles the one
/// still pending from the install (GameSettingsStore.SettlePending).
///
/// <para>The tests that matter are about what is LEFT OUT: a seed that carried the source mod's
/// lobby setups or its identity would hand this mod a civilisation and home cities from another
/// game, and a seed written over an existing profile would cost the player theirs.</para>
/// </summary>
public class GameSettingsSeedTests
{
    /// <summary>Same real shape as <c>GameSettingsSyncTests</c>' fixture — see the warning there.</summary>
    private static string Profile(
        string volume = "7", string keyMaps = "<KeyMapGroups />", string? record = null,
        string root = "<Profile Version=\"10\" GameID=\"2\">") =>
        "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" +
        root +
        "<UserInformation><PlayerName>Jeison</PlayerName><Name>Jeison</Name>" +
        "<LastSelectedSPHC>sp_British_homecity</LastSelectedSPHC></UserInformation>" +
        $"<GameSettings Name=\"GameOptions\" Def=\"cDefGameOptions\"><Settings Version=\"53\">" +
        $"<Setting Name=\"optionsoundlevel\">{volume}</Setting>" +
        (record == null ? "" : $"<Setting Name=\"optionrecordgame\">{record}</Setting>") +
        "</Settings></GameSettings>" +
        "<GameSettings Name=\"RandomMapGameSettings\"><Settings><Setting Name=\"civ\">Johor</Setting></Settings></GameSettings>" +
        "<GameSettings Name=\"MultiplayerGameSettings\"><Settings><Setting Name=\"map\">Andes</Setting></Settings></GameSettings>" +
        keyMaps +
        "</Profile>";

    private static readonly Guid AGuid = new("cb03c83a-c9ba-4ca5-8ca9-754b7dc09830");

    private static XmlDocument Parse(string xml)
    {
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        return doc;
    }

    private static XmlNode? Section(XmlDocument doc, string name) =>
        doc.SelectSingleNode($"/*/*[@Name='{name}']") ?? doc.SelectSingleNode($"/*/{name}");

    private static string Seed(string source) =>
        GameSettingsSync.BuildSeedProfile(source, GameSettingsSync.DefaultProfileName, AGuid)
        ?? throw new InvalidOperationException("no seed");

    // ---------------- the document ----------------

    [Fact]
    public void THE_ONE_THAT_MATTERS_NoLobbySetupOfTheSourceModTravels()
    {
        var doc = Parse(Seed(Profile()));

        Assert.Null(Section(doc, "RandomMapGameSettings"));
        Assert.Null(Section(doc, "MultiplayerGameSettings"));
        var top = doc.DocumentElement!.ChildNodes.OfType<XmlElement>()
            .Select(e => e.GetAttribute("Name") is { Length: > 0 } n ? $"{e.Name}[{n}]" : e.Name)
            .ToArray();
        Assert.Equal(new[] { "UserInformation", "GameSettings[GameOptions]", "KeyMapGroups" }, top);
    }

    [Fact]
    public void TheIdentityIsAFreshOneNeverTheSources()
    {
        var info = Parse(Seed(Profile())).SelectSingleNode("/Profile/UserInformation")!;
        var children = info.ChildNodes.OfType<XmlElement>().ToList();

        // The game's own first-run shape, in its order.
        Assert.Equal(new[]
        {
            "Name", "ESOName", "SaveESOName", "ShowESOWarning", "OnlineName", "Password", "GUID",
            "LastSelectedCiv", "LastSelectedESOHC", "LastSelectedSPHC", "LastSelectedLANHC",
            "LastSelectedDMESOHC", "LastSelectedDMSPHC", "LastSelectedDMLANHC",
        }, children.Select(e => e.Name).ToArray());

        string Value(string name) => children.Single(e => e.Name == name).InnerText;
        Assert.Equal("NewProfile3", Value("Name"));
        Assert.Equal("{CB03C83A-C9BA-4CA5-8CA9-754B7DC09830}", Value("GUID"));
        Assert.Equal("0", Value("SaveESOName"));
        Assert.Equal("1", Value("ShowESOWarning"));
        Assert.Equal("1", Value("LastSelectedCiv"));
        // The source's home city names another mod's file; it must not come along.
        Assert.Equal("", Value("LastSelectedSPHC"));
        Assert.DoesNotContain(children, e => e.Name == "PlayerName");
    }

    [Fact]
    public void TheEmptyFieldsAreSpelledTheWayTheGameSpellsThem()
    {
        var seed = Seed(Profile());
        Assert.Contains("<ESOName></ESOName>", seed);
        Assert.DoesNotContain("<ESOName />", seed);
    }

    [Fact]
    public void TheSeedCarriesExactlyWhatAGraftWouldBring()
    {
        var source = Profile(volume: "3", keyMaps: "<KeyMapGroups><Group Name=\"camera\" /></KeyMapGroups>");
        var shared = GameSettingsSync.ExtractSections(source)!;

        Assert.True(GameSettingsSync.SectionsMatch(Seed(source), shared));
    }

    [Fact]
    public void RecordingNeverTravelsInASeed()
    {
        var seed = Seed(Profile(record: "true"));
        Assert.Null(GameSettingsSync.ReadSetting(seed, GameSettingsSync.GameOptionsSection,
            GameSettingsSync.RecordGameSetting));
        Assert.Equal("7", GameSettingsSync.ReadSetting(seed, GameSettingsSync.GameOptionsSection, "optionsoundlevel"));
    }

    [Fact]
    public void TheRootAttributesComeFromTheSourceOrTheGamesDefaults()
    {
        var copied = Parse(Seed(Profile(root: "<Profile Version=\"11\" GameID=\"3\">"))).DocumentElement!;
        Assert.Equal("11", copied.GetAttribute("Version"));
        Assert.Equal("3", copied.GetAttribute("GameID"));

        var defaulted = Parse(Seed(Profile(root: "<Profile>"))).DocumentElement!;
        Assert.Equal("10", defaulted.GetAttribute("Version"));
        Assert.Equal("2", defaulted.GetAttribute("GameID"));
    }

    [Fact]
    public void AnEmptyHotkeySectionIsWrittenWhenTheSourceHasNone()
    {
        var seed = Seed(Profile(keyMaps: ""));
        var keys = Section(Parse(seed), "KeyMapGroups");
        Assert.NotNull(keys);
        Assert.False(keys!.HasChildNodes);
        Assert.Contains("<KeyMapGroups></KeyMapGroups>", seed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not xml at all")]
    [InlineData("<Profile Version=\"10\" GameID=\"2\"><UserInformation /></Profile>")]
    [InlineData("<LauncherSharedSettings><GameSettings Name=\"GameOptions\" /></LauncherSharedSettings>")]
    public void NothingIsBuiltFromASourceThatIsNotAProfileWithSettings(string source)
    {
        Assert.Null(GameSettingsSync.BuildSeedProfile(source, GameSettingsSync.DefaultProfileName, AGuid));
    }

    [Fact]
    public void ASeedNeedsAName()
    {
        Assert.Null(GameSettingsSync.BuildSeedProfile(Profile(), "  ", AGuid));
    }

    // ---------------- LastProfile3.dat ----------------

    [Fact]
    public void ThePointerFileIsByteForByteTheGames()
    {
        // Read off a real Users3\LastProfile3.dat: int32 LE 11, then "NewProfile3" in UTF-16LE.
        var expected = new byte[] { 0x0b, 0, 0, 0 }
            .Concat(Encoding.Unicode.GetBytes("NewProfile3"))
            .ToArray();

        var bytes = GameSettingsSync.LastProfileDat(GameSettingsSync.DefaultProfileName);

        Assert.Equal(26, bytes.Length);
        Assert.Equal(expected, bytes);
    }

    [Fact]
    public void ThePointerFileReadsBackAsTheProfileName()
    {
        using var dir = new TempDir();
        File.WriteAllBytes(Path.Combine(dir.Path, "LastProfile3.dat"),
            GameSettingsSync.LastProfileDat(GameSettingsSync.DefaultProfileName));

        Assert.Equal("NewProfile3", UserDataService.ReadActiveProfileFileName(dir.Path));
    }

    // ---------------- writing it ----------------

    [Fact]
    public void BothFilesAreWrittenAndTheProfileCarriesAByteOrderMark()
    {
        using var dir = new TempDir();
        var users3 = Path.Combine(dir.Path, "Users3");   // does not exist yet: the write creates it
        var seed = Seed(Profile());

        var result = GameSettingsStore.TrySeedProfile(users3, seed, "NewProfile3");

        Assert.Equal(GameSettingsStore.SeedWrite.Written, result);
        var xml = File.ReadAllBytes(Path.Combine(users3, "NewProfile3.xml"));
        Assert.Equal(new byte[] { 0xFF, 0xFE }, xml.Take(2).ToArray());
        Assert.Equal(seed, File.ReadAllText(Path.Combine(users3, "NewProfile3.xml"), Encoding.Unicode));
        Assert.Equal(26, new FileInfo(Path.Combine(users3, "LastProfile3.dat")).Length);
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_AnExistingProfileIsNeverReplaced()
    {
        using var dir = new TempDir();
        var users3 = Directory.CreateDirectory(Path.Combine(dir.Path, "Users3")).FullName;
        var theirs = Path.Combine(users3, "Jeison.xml");
        File.WriteAllText(theirs, "the player's own profile");

        var result = GameSettingsStore.TrySeedProfile(users3, Seed(Profile()), "NewProfile3");

        Assert.Equal(GameSettingsStore.SeedWrite.ProfileAlreadyThere, result);
        Assert.Equal("the player's own profile", File.ReadAllText(theirs));
        Assert.False(File.Exists(Path.Combine(users3, "NewProfile3.xml")));
        Assert.False(File.Exists(Path.Combine(users3, "LastProfile3.dat")));
    }

    [Fact]
    public void AnExistingPointerFileAlsoMeansHandsOff()
    {
        using var dir = new TempDir();
        var users3 = Directory.CreateDirectory(Path.Combine(dir.Path, "Users3")).FullName;
        File.WriteAllBytes(Path.Combine(users3, "LastProfile3.dat"), GameSettingsSync.LastProfileDat("Other"));

        var result = GameSettingsStore.TrySeedProfile(users3, Seed(Profile()), "NewProfile3");

        Assert.Equal(GameSettingsStore.SeedWrite.ProfileAlreadyThere, result);
        Assert.Empty(Directory.GetFiles(users3, "*.xml"));
    }

    [Fact]
    public void APointerThatCannotBeWrittenTakesTheProfileBackWithIt()
    {
        using var dir = new TempDir();
        var users3 = Directory.CreateDirectory(Path.Combine(dir.Path, "Users3")).FullName;
        // A FOLDER where the pointer file goes: the profile can be written, the pointer cannot.
        Directory.CreateDirectory(Path.Combine(users3, "LastProfile3.dat"));

        var result = GameSettingsStore.TrySeedProfile(users3, Seed(Profile()), "NewProfile3");

        Assert.Equal(GameSettingsStore.SeedWrite.Failed, result);
        Assert.False(File.Exists(Path.Combine(users3, "NewProfile3.xml")));
    }

    // ---------------- who may get one ----------------

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("", null, false)]
    [InlineData("   ", null, false)]
    [InlineData("Age of Empires 3", null, false)]                 // the player's own base game
    [InlineData("age of empires 3", "", false)]
    [InlineData("Knights and Barbarians", "userdata.zip", false)] // ships its own starter profile
    [InlineData("Age of Dynasties", null, true)]
    [InlineData("Age of Dynasties", "", true)]
    public void OnlyAModWithAKnownFolderAndNoStarterProfileOfItsOwnGetsOne(
        string? folder, string? payload, bool expected)
    {
        Assert.Equal(expected, GameSettingsStore.CanSeedBeforeFirstLaunch(folder, payload));
    }

    // ---------------- game recording on a created profile ----------------

    [Fact]
    public void THE_ONE_THAT_MATTERS_ASeededProfileReopensTheRecordingQuestion()
    {
        // The reported state: recording had been switched on in the mod's OLD profile (marker true,
        // and the marker survives an uninstall); the reinstall created a new profile, which carries
        // no recording setting — so the launcher believed it was on while the game had it off.
        var seed = Seed(Profile(record: "true"));
        var setting = GameSettingsSync.ReadSetting(
            seed, GameSettingsSync.GameOptionsSection, GameSettingsSync.RecordGameSetting);

        Assert.Null(GameSettingsStore.EffectiveRecordingMarker(
            applied: true, wants: true, profileFound: true, settingValue: setting));
        // …and with the marker cleared, the existing plan writes it.
        Assert.True(GameSettingsStore.PlanGameRecording(null, wants: true, noticeAlreadyShown: true).Write);
    }

    [Theory]
    [InlineData(true, true, true, "true", true)]
    [InlineData(true, true, true, "false", true)]   // the player's own choice in the game: never overridden
    [InlineData(true, true, false, null, true)]     // no profile yet: NoProfile already handles that
    [InlineData(true, false, true, null, true)]     // opted out: an absent setting already means "off"
    [InlineData(false, true, true, null, false)]    // only a stale TRUE is reopened
    [InlineData(null, true, true, null, null)]
    public void AnythingButAnAbsentSettingUnderATrueMarkerIsLeftAsItWas(
        bool? applied, bool wants, bool profileFound, string? setting, bool? expected)
    {
        Assert.Equal(expected, GameSettingsStore.EffectiveRecordingMarker(applied, wants, profileFound, setting));
    }

    // ---------------- the pending import ----------------

    [Fact]
    public void THE_ONE_THAT_MATTERS_AnImportThatLandsSettlesTheOneStillPending()
    {
        // The reported sequence: Wars of Liberty chosen at install (pending), Enhanced Legacy
        // Edition imported by hand afterwards — the pending one then overwrote it at the next launch.
        var config = new LauncherConfig();
        config.GetState("age-of-dynasties-remake").PendingSettingsImportFrom = "wol";

        var settled = GameSettingsStore.SettlePending(config, "age-of-dynasties-remake", out var superseded);

        Assert.True(settled);
        Assert.Equal("wol", superseded);
        Assert.Equal("", config.Mods["age-of-dynasties-remake"].PendingSettingsImportFrom);
    }

    [Fact]
    public void SettlingNeverCreatesStateForAModNobodyConfigured()
    {
        var config = new LauncherConfig();

        Assert.False(GameSettingsStore.SettlePending(config, "never-seen", out var superseded));
        Assert.Null(superseded);
        Assert.False(config.Mods.ContainsKey("never-seen"));
    }

    [Fact]
    public void NothingPendingIsNothingToSettle()
    {
        var config = new LauncherConfig();
        config.GetState("enhanced-legacy-edition");

        Assert.False(GameSettingsStore.SettlePending(config, "enhanced-legacy-edition", out _));
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "aoe3ml-seed-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* best-effort */ }
        }
    }
}
