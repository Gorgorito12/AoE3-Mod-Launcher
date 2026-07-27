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
    /// </summary>
    private static string Profile(string volume = "7", string keyMaps = "<KeyMapGroups />") =>
        "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" +
        "<Profile Version=\"10\" GameID=\"2\">" +
        "<UserInformation><PlayerName>Jeison</PlayerName></UserInformation>" +
        $"<GameSettings Name=\"GameOptions\" Def=\"cDefGameOptions\"><Settings Version=\"53\">" +
        $"<Setting Name=\"optionsoundlevel\">{volume}</Setting></Settings></GameSettings>" +
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
}
