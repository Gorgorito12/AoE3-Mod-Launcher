using System.Xml;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="GameSettingsSync"/> — which parts of an Age of Empires III profile travel
/// between mods, and which must not.
///
/// The tests that matter are the ones about what is LEFT ALONE. This code rewrites the file the
/// game reads at startup, so grafting a section too many, or writing a half-formed document,
/// costs the player their profile.
/// </summary>
public class GameSettingsSyncTests
{
    /// <summary>
    /// The REAL shape of an Age of Empires III profile, copied from one on disk — six identical
    /// <c>&lt;GameSettings&gt;</c> siblings keyed by a <c>Name</c> ATTRIBUTE, plus
    /// <c>&lt;KeyMapGroups&gt;</c>, which is the only one actually named by its element.
    ///
    /// <para>This fixture is the point of the file. The first version of these tests invented a
    /// tree of neatly named elements — <c>&lt;GameOptions&gt;</c>, <c>&lt;RandomMapGameSettings&gt;</c>
    /// — because PowerShell's XML adapter reports the <c>Name</c> attribute as the node name, and
    /// the implementation was written to match. Every test passed against a file that does not
    /// exist. If you change this fixture, check it against a real profile first.</para>
    ///
    /// <para><paramref name="record"/> adds <c>optionrecordgame</c>, which a real profile carries
    /// beside <c>optionsoundlevel</c> as lowercase <c>true</c>/<c>false</c> with no type attribute.
    /// It is OMITTED by default so every test written before it stays exactly as it was.</para>
    /// </summary>
    private static string Profile(
        string volume = "7", string keyMaps = "<KeyMapGroups />", string? record = null) =>
        "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" +
        "<Profile Version=\"10\" GameID=\"2\">" +
        "<UserInformation><PlayerName>Jeison</PlayerName></UserInformation>" +
        $"<GameSettings Name=\"GameOptions\" Def=\"cDefGameOptions\"><Settings Version=\"53\">" +
        $"<Setting Name=\"optionsoundlevel\">{volume}</Setting>" +
        (record == null ? "" : $"<Setting Name=\"optionrecordgame\">{record}</Setting>") +
        "</Settings></GameSettings>" +
        "<GameSettings Name=\"RandomMapGameSettings\"><Settings><Setting Name=\"civ\">Johor</Setting></Settings></GameSettings>" +
        "<GameSettings Name=\"MultiplayerGameSettings\"><Settings><Setting Name=\"map\">Andes</Setting></Settings></GameSettings>" +
        keyMaps +
        "</Profile>";

    /// <summary>
    /// The named section, found the way the profile really keys them — by the <c>Name</c>
    /// attribute, falling back to the element name for <c>KeyMapGroups</c>. Root-agnostic so the
    /// same helper reads a profile (<c>Profile</c>) and the shared copy
    /// (<c>LauncherSharedSettings</c>).
    /// </summary>
    private static XmlNode? Section(XmlDocument doc, string name) =>
        doc.SelectSingleNode($"/*/*[@Name='{name}']") ?? doc.SelectSingleNode($"/*/{name}");

    private static XmlDocument Parse(string xml)
    {
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        return doc;
    }

    [Fact]
    public void Graft_BringsOverTheSettingsSections()
    {
        var shared = GameSettingsSync.ExtractSections(Profile(volume: "2"))!;

        var doc = Parse(GameSettingsSync.Graft(Profile(volume: "7"), shared)!);

        Assert.Equal("2", Section(doc, "GameOptions")!
            .SelectSingleNode("Settings/Setting[@Name='optionsoundlevel']")!.InnerText);
    }

    [Fact]
    public void Graft_LeavesTheOtherGameSettingsSiblingsAlone()
    {
        // The sharp edge of the real format: all six of these are &lt;GameSettings&gt; elements, so an
        // implementation matching on the element name would graft graphics settings over the last
        // multiplayer setup — and a civilisation from one mod means nothing in another.
        var shared = GameSettingsSync.ExtractSections(Profile(volume: "2"))!;

        var doc = Parse(GameSettingsSync.Graft(Profile(), shared)!);

        Assert.Equal("Jeison", doc.SelectSingleNode("/Profile/UserInformation/PlayerName")!.InnerText);
        Assert.Equal("Johor", Section(doc, "RandomMapGameSettings")!
            .SelectSingleNode("Settings/Setting[@Name='civ']")!.InnerText);
        Assert.Equal("Andes", Section(doc, "MultiplayerGameSettings")!
            .SelectSingleNode("Settings/Setting[@Name='map']")!.InnerText);
    }

    [Fact]
    public void ExtractSections_TakesTheAttributeKeyedSectionAndNotItsSiblings()
    {
        // The bug this file exists to prevent: matching by element name found only KeyMapGroups
        // and silently shared nothing at all.
        var shared = Parse(GameSettingsSync.ExtractSections(Profile())!);

        Assert.NotNull(Section(shared, "GameOptions"));
        Assert.NotNull(Section(shared, "KeyMapGroups"));
        Assert.Null(Section(shared, "RandomMapGameSettings"));
        Assert.Null(Section(shared, "MultiplayerGameSettings"));
        Assert.Null(shared.SelectSingleNode("/LauncherSharedSettings/UserInformation"));
    }

    [Fact]
    public void Graft_KeepsTheUtf16Declaration()
    {
        // The game writes and reads these as UTF-16. Losing the declaration here would tell the
        // caller to write the wrong encoding, and the profile would stop loading.
        var shared = GameSettingsSync.ExtractSections(Profile())!;

        var result = GameSettingsSync.Graft(Profile(), shared)!;

        Assert.Contains("encoding=\"UTF-16\"", result);
    }

    [Fact]
    public void Graft_AnEmptyHotkeySectionStillTravels()
    {
        // Empty KeyMapGroups means "I use the defaults". Under last-played-wins it has to be
        // able to clear a customisation made in another mod, or hotkeys would only ever travel
        // one way.
        var shared = GameSettingsSync.ExtractSections(Profile(keyMaps: "<KeyMapGroups />"))!;
        var customised = Profile(keyMaps: "<KeyMapGroups><Group Id=\"7\" /></KeyMapGroups>");

        var doc = Parse(GameSettingsSync.Graft(customised, shared)!);

        Assert.Empty(Section(doc, "KeyMapGroups")!.ChildNodes);
    }

    [Fact]
    public void Graft_AddsASectionTheProfileDoesNotHaveYet()
    {
        var shared = GameSettingsSync.ExtractSections(Profile())!;
        var withoutOptions =
            "<?xml version=\"1.0\" encoding=\"UTF-16\"?><Profile><KeyMapGroups /></Profile>";

        var doc = Parse(GameSettingsSync.Graft(withoutOptions, shared)!);

        Assert.NotNull(Section(doc, "GameOptions"));
    }

    [Theory]
    [InlineData("not xml at all")]
    [InlineData("<Profile><GameOptions></Profile>")]     // malformed
    [InlineData("")]
    public void Graft_RefusesToTouchAProfileItCannotRead(string broken)
    {
        // Null, never a half-written document: this runs moments before the game starts, and the
        // worst outcome would be a profile the game can no longer load.
        var shared = GameSettingsSync.ExtractSections(Profile())!;

        Assert.Null(GameSettingsSync.Graft(broken, shared));
    }

    [Fact]
    public void Graft_DoesNothingWhenTheSharedCopySaysNothing()
    {
        var emptyShared = "<?xml version=\"1.0\" encoding=\"UTF-16\"?><LauncherSharedSettings />";

        Assert.Null(GameSettingsSync.Graft(Profile(), emptyShared));
    }

    [Fact]
    public void ExtractSections_ReturnsNothingForAProfileWithNoSettings()
    {
        var bare = "<?xml version=\"1.0\" encoding=\"UTF-16\"?><Profile><UserInformation /></Profile>";

        Assert.Null(GameSettingsSync.ExtractSections(bare));
    }

    [Fact]
    public void SectionsMatch_IsWhatLetsAnUnchangedProfileSkipTheWrite()
    {
        var shared = GameSettingsSync.ExtractSections(Profile())!;

        Assert.True(GameSettingsSync.SectionsMatch(Profile(), shared));
        Assert.False(GameSettingsSync.SectionsMatch(Profile(volume: "9"), shared));
    }

    // ---------------- one setting at a time ----------------

    private const string Options = GameSettingsSync.GameOptionsSection;
    private const string Record = GameSettingsSync.RecordGameSetting;

    private static string? RecordValue(string profileXml) =>
        Section(Parse(profileXml), "GameOptions")!
            .SelectSingleNode($"Settings/Setting[@Name='{Record}']")?.InnerText;

    [Fact]
    public void EnsureSetting_AddsTheSettingWhenTheProfileHasNone()
    {
        var written = GameSettingsSync.EnsureSetting(Profile(), Options, Record, "true");

        Assert.NotNull(written);
        Assert.Equal("true", RecordValue(written!));
    }

    [Fact]
    public void EnsureSetting_ChangesAValueThatDisagrees()
    {
        var written = GameSettingsSync.EnsureSetting(Profile(record: "false"), Options, Record, "true");

        Assert.NotNull(written);
        Assert.Equal("true", RecordValue(written!));
    }

    /// <summary>
    /// THE ONE THAT MATTERS. This runs moments before the game opens the profile, so a setting
    /// that is already correct must produce no write at all — the same contract as
    /// <c>Graft</c>, and what keeps the launcher off the file on every launch but the first.
    /// </summary>
    [Fact]
    public void EnsureSetting_ReturnsNullWhenItIsAlreadyCorrect()
        => Assert.Null(GameSettingsSync.EnsureSetting(Profile(record: "true"), Options, Record, "true"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not xml at all")]
    [InlineData("<Profile><GameSettings Name=\"GameOptions\"></Profile>")]
    public void EnsureSetting_RefusesToTouchAProfileItCannotRead(string profileXml)
        => Assert.Null(GameSettingsSync.EnsureSetting(profileXml, Options, Record, "true"));

    /// <summary>
    /// Never invent the section. A profile without it is not a profile we understand, and adding
    /// structure to one risks leaving the player a file the game will not load.
    /// </summary>
    [Fact]
    public void EnsureSetting_RefusesWhenTheSectionIsMissing()
    {
        var bare = "<?xml version=\"1.0\" encoding=\"UTF-16\"?><Profile><UserInformation /></Profile>";

        Assert.Null(GameSettingsSync.EnsureSetting(bare, Options, Record, "true"));
    }

    /// <summary>Same rule one level down: the <c>Settings</c> container is never fabricated either.</summary>
    [Fact]
    public void EnsureSetting_RefusesWhenTheSettingsContainerIsMissing()
    {
        var noContainer = "<?xml version=\"1.0\" encoding=\"UTF-16\"?><Profile>" +
                          "<GameSettings Name=\"GameOptions\" /></Profile>";

        Assert.Null(GameSettingsSync.EnsureSetting(noContainer, Options, Record, "true"));
    }

    /// <summary>
    /// The sibling sections key their settings by the same <c>Name</c> attribute, so a
    /// document-wide search would reach straight into them.
    /// </summary>
    [Fact]
    public void EnsureSetting_TouchesOnlyTheNamedSetting()
    {
        var doc = Parse(GameSettingsSync.EnsureSetting(Profile(), Options, Record, "true")!);

        Assert.Equal("7", Section(doc, "GameOptions")!
            .SelectSingleNode("Settings/Setting[@Name='optionsoundlevel']")!.InnerText);
        Assert.Equal("Johor", Section(doc, "RandomMapGameSettings")!
            .SelectSingleNode("Settings/Setting[@Name='civ']")!.InnerText);
        Assert.Equal("Andes", Section(doc, "MultiplayerGameSettings")!
            .SelectSingleNode("Settings/Setting[@Name='map']")!.InnerText);
    }

    /// <summary>
    /// The format a real profile uses: lowercase text, no type attribute. The replay container has
    /// a typed value table; this file does not, and carrying that over would produce a setting the
    /// game ignores.
    /// </summary>
    [Fact]
    public void EnsureSetting_WritesTheFormatTheProfileReallyUses()
    {
        var doc = Parse(GameSettingsSync.EnsureSetting(Profile(), Options, Record, "true")!);
        var setting = (XmlElement)Section(doc, "GameOptions")!
            .SelectSingleNode($"Settings/Setting[@Name='{Record}']")!;

        Assert.Equal("true", setting.InnerText);
        Assert.Equal(1, setting.Attributes.Count);      // Name, and nothing else
        Assert.Equal(Record, setting.GetAttribute("Name"));
    }

    [Fact]
    public void EnsureSetting_KeepsTheSettingsVersionAndTheDeclaration()
    {
        var written = GameSettingsSync.EnsureSetting(Profile(), Options, Record, "true")!;
        var doc = Parse(written);

        Assert.Equal("53", ((XmlElement)Section(doc, "GameOptions")!
            .SelectSingleNode("Settings")!).GetAttribute("Version"));
        Assert.Contains("encoding=\"UTF-16\"", written);
    }

    [Fact]
    public void ReadSetting_ReadsTheValueAndAdmitsWhenItCannot()
    {
        Assert.Equal("false", GameSettingsSync.ReadSetting(Profile(record: "false"), Options, Record));
        Assert.Null(GameSettingsSync.ReadSetting(Profile(), Options, Record));          // absent
        Assert.Null(GameSettingsSync.ReadSetting("not xml", Options, Record));          // unreadable
    }

    /// <summary>
    /// Recording is a launcher preference, not one of the settings the player asked to share.
    ///
    /// <para>Without this, turning recording off would undo itself: the capture would carry the
    /// old <c>true</c> into the shared copy, the next launch would graft it back, and the code
    /// that owns the preference would correctly conclude it had already applied what was asked
    /// for — so nothing could ever notice.</para>
    /// </summary>
    [Fact]
    public void ExtractSections_LeavesGameRecordingBehind()
    {
        var shared = GameSettingsSync.ExtractSections(Profile(record: "true"))!;

        Assert.DoesNotContain(Record, shared);
        // The rest of GameOptions still travels — this strips one setting, not the section.
        Assert.Contains("optionsoundlevel", shared);
    }

    /// <summary>The other direction: a graft must not carry recording INTO a profile either.</summary>
    [Fact]
    public void Graft_NeverOverwritesGameRecording()
    {
        var shared = GameSettingsSync.ExtractSections(Profile(volume: "2", record: "false"))!;

        var grafted = GameSettingsSync.Graft(Profile(volume: "7", record: "true"), shared)!;

        Assert.Equal("true", RecordValue(grafted));   // the player's own choice survives
        Assert.Equal("2", Section(Parse(grafted), "GameOptions")!
            .SelectSingleNode("Settings/Setting[@Name='optionsoundlevel']")!.InnerText);
    }
}
