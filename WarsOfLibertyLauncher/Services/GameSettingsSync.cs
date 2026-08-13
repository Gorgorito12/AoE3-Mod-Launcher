using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Carries a player's graphics, sound and hotkey settings from one mod to another.
///
/// <para><b>Why it can be surgical.</b> Age of Empires III keeps everything for a player in a
/// single per-mod file, <c>My Games\&lt;mod&gt;\Users3\&lt;profile&gt;.xml</c> — but that file is
/// cleanly sectioned, and only two of its sections are settings:</para>
///
/// <list type="bullet">
///   <item><c>GameOptions</c> — resolution, quality, volumes.</item>
///   <item><c>KeyMapGroups</c> — hotkeys. Empty means "the defaults".</item>
/// </list>
///
/// <para><b>Read the real shape before touching this.</b> Only <c>KeyMapGroups</c> is an element
/// of that name; the rest are identical <c>&lt;GameSettings Name="…"&gt;</c> siblings keyed by an
/// attribute:</para>
/// <code>
/// &lt;Profile Version="10" GameID="2"&gt;
///   &lt;UserInformation&gt;…&lt;/UserInformation&gt;
///   &lt;GameSettings Name="GameOptions"&gt;…&lt;/GameSettings&gt;
///   &lt;GameSettings Name="RandomMapGameSettings"&gt;…&lt;/GameSettings&gt;
///   … four more …
///   &lt;KeyMapGroups /&gt;
/// &lt;/Profile&gt;
/// </code>
/// <para>It is easy to misread: PowerShell's XML adapter returns the <c>Name</c> ATTRIBUTE from
/// <c>$node.Name</c>, so inspecting a profile there shows a tree of neatly named sections that
/// does not exist. A fixture built on that misreading passes every test and matches no real
/// file.</para>
///
/// <para>Everything else in there belongs to the mod it came from: the profile's own identity,
/// and six <c>*GameSettings</c> blocks remembering the last game set up in each mode. Those are
/// deliberately NOT shared — a civilisation from Struggle of Indonesia does not exist in
/// Napoleonic Era, so copying them across would leave a setup that means nothing. Copying the
/// whole file would drag all of it, which is why this grafts sections instead.</para>
///
/// <para>Pure and disk-free on purpose: everything that decides what gets copied is here and
/// pinned by <c>GameSettingsSyncTests</c>, while reading and writing the real profile lives in
/// the caller. <b>Encoding is the caller's job and it matters</b> — the game writes these files
/// as UTF-16 and reads them at startup, so a round-trip through UTF-8 would corrupt the profile.
/// The XML declaration is preserved here so the caller has something honest to obey.</para>
/// </summary>
public static class GameSettingsSync
{
    /// <summary>The only sections that travel between mods.</summary>
    public static readonly string[] SharedSections = { "GameOptions", "KeyMapGroups" };

    /// <summary>Root of the small document the launcher stores as the shared copy.</summary>
    private const string SharedRoot = "LauncherSharedSettings";

    /// <summary>
    /// Pulls the shared sections out of a mod's profile into the small document the launcher
    /// keeps as the global copy. Null when the profile can't be read or carries none of them —
    /// so a caller that gets null simply keeps whatever it already had.
    /// </summary>
    public static string? ExtractSections(string profileXml)
    {
        var doc = TryLoad(profileXml);
        if (doc?.DocumentElement == null) return null;

        var shared = new XmlDocument();
        shared.AppendChild(shared.CreateXmlDeclaration("1.0", "UTF-16", null));
        var root = shared.CreateElement(SharedRoot);
        root.SetAttribute("Version", "1");
        shared.AppendChild(root);

        var found = 0;
        foreach (var name in SharedSections)
        {
            var section = FindSection(doc.DocumentElement, name);
            if (section == null) continue;
            var imported = shared.ImportNode(section, deep: true);
            StripRecordingSetting(imported);
            root.AppendChild(imported);
            found++;
        }

        return found == 0 ? null : shared.OuterXml;
    }

    /// <summary>
    /// Returns <paramref name="profileXml"/> with its shared sections replaced by the ones in
    /// <paramref name="sharedXml"/>, or <b>null</b> when there is nothing safe to do — either
    /// document unreadable, or the shared copy carrying none of the sections.
    ///
    /// <para>Null rather than an exception, and null rather than a partially-written document:
    /// this runs moments before the game launches, and the worst outcome would be leaving a
    /// player with a profile the game can't read. A caller that gets null leaves the file alone
    /// and the mod simply keeps its own settings.</para>
    ///
    /// <para>An <b>empty</b> section in the shared copy is still grafted, on purpose. Empty
    /// <c>KeyMapGroups</c> means "this player uses the default hotkeys", and under
    /// last-played-wins that has to be able to clear a customisation made in another mod —
    /// otherwise hotkeys could travel one way and never back.</para>
    /// </summary>
    public static string? Graft(string profileXml, string sharedXml)
    {
        var target = TryLoad(profileXml);
        var source = TryLoad(sharedXml);
        if (target?.DocumentElement == null || source?.DocumentElement == null) return null;

        var grafted = 0;
        foreach (var name in SharedSections)
        {
            var incoming = FindSection(source.DocumentElement, name);
            if (incoming == null) continue;   // the shared copy says nothing about this one

            var imported = target.ImportNode(incoming, deep: true);
            var existing = FindSection(target.DocumentElement, name);
            CarryOverRecordingSetting(target, existing, imported);
            if (existing != null) target.DocumentElement.ReplaceChild(imported, existing);
            else target.DocumentElement.AppendChild(imported);
            grafted++;
        }

        return grafted == 0 ? null : target.OuterXml;
    }

    /// <summary>Whether two profiles already agree, so a write can be skipped entirely.</summary>
    public static bool SectionsMatch(string profileXml, string sharedXml)
    {
        var mine = ExtractSections(profileXml);
        return mine != null && string.Equals(mine, sharedXml, StringComparison.Ordinal);
    }

    /// <summary>The section holding the options the player sets in the game's own Options screen.</summary>
    public const string GameOptionsSection = "GameOptions";

    /// <summary>
    /// Whether Age of Empires III writes a recording of each game. Read from a real profile, not
    /// guessed: it sits in <see cref="GameOptionsSection"/> beside <c>optionsoundlevel</c>, and
    /// ships as <c>false</c>.
    /// </summary>
    public const string RecordGameSetting = "optionrecordgame";

    /// <summary>
    /// The raw text of one setting, or <b>null</b> when the profile, the section, the
    /// <c>Settings</c> container or the setting itself is missing.
    ///
    /// <para>Exists because <see cref="EnsureSetting"/> returns null for two very different
    /// reasons — "already correct" and "I don't understand this file" — and a caller that
    /// remembers having applied something must not confuse them.</para>
    /// </summary>
    public static string? ReadSetting(string profileXml, string sectionName, string key)
    {
        var doc = TryLoad(profileXml);
        if (doc?.DocumentElement == null) return null;

        var section = FindSection(doc.DocumentElement, sectionName);
        if (section == null) return null;

        var settings = FindSettingsContainer(section);
        return settings == null ? null : FindSetting(settings, key)?.InnerText;
    }

    /// <summary>
    /// Returns <paramref name="profileXml"/> with a single setting set to
    /// <paramref name="value"/>, or <b>null</b> when there is nothing to do — the same contract as
    /// <see cref="Graft"/>, and for the same reason: this runs moments before the game opens the
    /// file, so "already correct" must produce no write at all.
    ///
    /// <para>Null also covers every shape we don't recognise: an unreadable document, a missing
    /// section, or a section with no <c>Settings</c> container. <b>Nothing here ever fabricates a
    /// <c>GameSettings</c> or a <c>Settings</c> element</b> — a profile without them is not a
    /// profile we understand, and inventing structure inside one risks leaving the player with a
    /// file the game refuses to load. A missing <c>Setting</c> IS created, because that is an
    /// absent value inside a shape we do recognise.</para>
    /// </summary>
    public static string? EnsureSetting(string profileXml, string sectionName, string key, string value)
    {
        var doc = TryLoad(profileXml);
        if (doc?.DocumentElement == null) return null;

        var section = FindSection(doc.DocumentElement, sectionName);
        if (section == null) return null;

        var settings = FindSettingsContainer(section);
        if (settings == null) return null;

        var setting = FindSetting(settings, key);
        if (setting != null)
        {
            if (string.Equals(setting.InnerText, value, StringComparison.Ordinal)) return null;
            setting.InnerText = value;
            return doc.OuterXml;
        }

        var created = doc.CreateElement("Setting");
        created.SetAttribute("Name", key);
        created.InnerText = value;
        settings.AppendChild(created);
        return doc.OuterXml;
    }

    /// <summary>
    /// The <c>Settings</c> element holding a section's individual values. The real nesting is two
    /// levels deep — <c>&lt;GameSettings Name="GameOptions"&gt;&lt;Settings Version="53"&gt;</c> —
    /// and the container carries a <c>Version</c> attribute we must never disturb.
    /// </summary>
    private static XmlElement? FindSettingsContainer(XmlElement section)
        => section.ChildNodes.OfType<XmlElement>()
            .FirstOrDefault(e => string.Equals(e.Name, "Settings", StringComparison.Ordinal));

    /// <summary>
    /// One <c>Setting</c> by its <c>Name</c> attribute, <b>among direct children only</b>. The same
    /// rule as <see cref="FindSection"/> and for the same reason: a document-wide search would
    /// reach into a sibling section, and <c>optionsoundlevel</c> in <c>GameOptions</c> sits beside
    /// a <c>civ</c> in <c>RandomMapGameSettings</c> and a <c>map</c> in
    /// <c>MultiplayerGameSettings</c>.
    /// </summary>
    private static XmlElement? FindSetting(XmlElement settings, string key)
        => settings.ChildNodes.OfType<XmlElement>()
            .FirstOrDefault(e => string.Equals(e.GetAttribute("Name"), key, StringComparison.Ordinal));

    /// <summary>
    /// Moves this profile's own <see cref="RecordGameSetting"/> into the section about to replace
    /// it, so a graft leaves the player's recording choice exactly where it was.
    ///
    /// <para><b>Stripping it from the shared copy is not enough on its own.</b> A graft REPLACES
    /// the whole section, so a shared copy that simply lacks the setting would delete it from the
    /// profile — leaving recording off while the launcher, having applied the value once, sees no
    /// reason to write again. The setting has to be carried across, not merely left out.</para>
    ///
    /// <para>The incoming value is dropped first, so a shared copy captured by an older build —
    /// which still carries one — cannot win over the profile it is being grafted into.</para>
    /// </summary>
    private static void CarryOverRecordingSetting(XmlDocument target, XmlElement? existing, XmlNode imported)
    {
        StripRecordingSetting(imported);

        if (existing == null || imported is not XmlElement incoming) return;

        var mineContainer = FindSettingsContainer(existing);
        var mine = mineContainer == null ? null : FindSetting(mineContainer, RecordGameSetting);
        if (mine == null) return;

        var container = FindSettingsContainer(incoming);
        if (container == null) return;

        container.AppendChild(target.ImportNode(mine, deep: true));
    }

    /// <summary>
    /// Drops <see cref="RecordGameSetting"/> out of a section on its way into the shared copy, so
    /// game recording never travels between mods.
    ///
    /// <para><b>This closes a leak that would have made the recording opt-out undoable.</b> The
    /// setting lives inside <see cref="GameOptionsSection"/>, which this class grafts wholesale —
    /// so for a mod in the sharing group, a capture would carry <c>true</c> into the shared copy
    /// and the next launch would graft it straight back over a player who had just switched
    /// recording off. Whoever owns that preference would then see it re-enable itself every
    /// launch, with nothing in the recording code able to notice: it would correctly conclude it
    /// had already applied the value the player asked for.</para>
    ///
    /// <para>It cuts both ways, which is the real justification: a mod where recording was turned
    /// off in-game must not export that <c>false</c> over a mod where it is wanted. Recording is a
    /// launcher preference, not one of the settings the player asked to share.</para>
    /// </summary>
    private static void StripRecordingSetting(XmlNode imported)
    {
        if (imported is not XmlElement element) return;

        var settings = FindSettingsContainer(element);
        if (settings == null) return;

        var setting = FindSetting(settings, RecordGameSetting);
        if (setting != null) settings.RemoveChild(setting);
    }

    /// <summary>
    /// A section of the profile by its name.
    ///
    /// <para><b>Most sections are not named by their element.</b> Six of the seven are
    /// <c>&lt;GameSettings Name="…"&gt;</c> siblings telling themselves apart by an ATTRIBUTE —
    /// so matching on the element name alone finds all of them or none, and would happily graft
    /// graphics settings over the last multiplayer setup. <c>KeyMapGroups</c> is the odd one out
    /// and really is its own element, hence both rules. Verified against a real profile; a
    /// synthetic fixture that names the elements after the sections looks right and is wrong.</para>
    ///
    /// <para>Direct children only — a descendant search could match something nested inside
    /// another section.</para>
    /// </summary>
    private static XmlElement? FindSection(XmlElement root, string name)
    {
        var children = root.ChildNodes.OfType<XmlElement>().ToList();
        return children.FirstOrDefault(e =>
                   string.Equals(e.GetAttribute("Name"), name, StringComparison.Ordinal))
            ?? children.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.Ordinal));
    }

    /// <summary>Parses, keeping whitespace so a grafted file stays as readable as it was.</summary>
    private static XmlDocument? TryLoad(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;
        try
        {
            var doc = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
            doc.LoadXml(xml);
            return doc;
        }
        catch (XmlException)
        {
            return null;
        }
    }
}
