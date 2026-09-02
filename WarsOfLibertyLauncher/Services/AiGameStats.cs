using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Reads the end-of-match statistics AoE3 writes into the AI's memory file.
///
/// <para><b>This is the statistics screen, serialised by the game itself.</b> Nothing else in the
/// launcher can produce it: the screen is computed in the live simulation and thrown away, and a
/// recording only carries DECISIONS (who, which civ, which map) and never OUTCOMES like units
/// built or resources gathered. The one place those survive is
/// <c>My Games\&lt;mod&gt;\AI4\&lt;ai&gt;.personality</c>, and it is there only because the AI keeps
/// notes on the humans it has played.</para>
///
/// <para><b>So it exists only when an AI was in the game.</b> A match between two people writes
/// nothing at all. Every caller has to say so on screen rather than let a player wonder why their
/// multiplayer games are missing.</para>
///
/// <para>Shaped like <see cref="GameRecordingPurge"/> beside it: the parse is pure and takes the
/// file's TEXT, so it is tested against a fixture with no disk, and exactly one method opens a
/// file.</para>
/// </summary>
public static class AiGameStats
{
    /// <summary>The subfolder of a mod's user-data directory the AI writes into.</summary>
    public const string FolderName = "AI4";

    public const string Extension = ".personality";

    /// <summary>
    /// A guard against a pathological file, not a real limit — the largest seen holds four games.
    /// </summary>
    private const int MaxGamesPerFile = 200;

    /// <summary>
    /// Every game a personality file records, oldest first, as the file orders them.
    ///
    /// <para>Returns an empty list rather than throwing for anything unreadable: this is called
    /// moments after a game exits, and a mod author's malformed file must cost the statistics of
    /// one match and nothing else.</para>
    /// </summary>
    /// <param name="personality">The file's name without its extension, e.g. <c>wolMenelik</c>.</param>
    /// <param name="modId">Which mod was played — the file itself does not say.</param>
    /// <param name="xml">The file's text.</param>
    /// <param name="capturedAtUtc">
    /// Stamped onto every record — the file carries no date of its own, so <see cref="Read"/>
    /// passes the file's write time and this parameter is what makes that testable.
    /// </param>
    public static IReadOnlyList<AiGameRecord> Parse(
        string personality, string modId, string xml, DateTime capturedAtUtc)
    {
        var games = new List<AiGameRecord>();
        if (string.IsNullOrWhiteSpace(xml)) return games;

        XDocument doc;
        try { doc = XDocument.Parse(xml, LoadOptions.None); }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"AiGameStats: '{personality}{Extension}' did not parse — {ex.Message}");
            return games;
        }

        var history = doc.Root?.Element("history");
        if (history == null) return games;

        foreach (var player in history.Elements("player"))
        {
            // MIXED CONTENT, and it is easy to get wrong: the player's name is the element's own
            // leading TEXT, not an attribute and not a child — `<player>Gorgorito` followed by
            // <uservars> and <game> children. Element.Value would concatenate every descendant's
            // text and hand back the name with every number in the file stuck to it.
            var name = LeadingText(player);

            foreach (var game in player.Elements("game"))
            {
                if (games.Count >= MaxGamesPerFile) return games;
                games.Add(ReadGame(personality, modId, name, game, capturedAtUtc));
            }
        }

        return games;
    }

    private static AiGameRecord ReadGame(
        string personality, string modId, string player, XElement game, DateTime capturedAtUtc)
    {
        var record = new AiGameRecord
        {
            Personality = personality,
            ModId = modId,
            PlayerName = player,
            CapturedAtUtc = capturedAtUtc.ToString("o", CultureInfo.InvariantCulture),
            FirstAttackSeconds = ReadInt(game.Element("firstattacktime"), -1),
        };

        // 1 = the HUMAN won. Measured against the outcome trailer of the recordings these games
        // paired with — see AiGameRecord.Won for why that had to be checked rather than assumed.
        var won = game.Element("myteamwon");
        if (won != null && int.TryParse(won.Value.Trim(), out var w)) record.Won = w == 1;

        // <stattime> is mixed content too: the duration is its own text and the totals are its
        // children.
        var stat = game.Element("stattime");
        if (stat != null)
        {
            if (long.TryParse(LeadingText(stat), NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out var ms))
                record.DurationMs = ms;

            record.Score = ReadInt(stat.Element("score"), 0);

            var totals = stat.Element("totalresources");
            if (totals != null)
            {
                record.Gold = ReadInt(totals.Element("gold"), 0);
                record.Wood = ReadInt(totals.Element("wood"), 0);
                record.Food = ReadInt(totals.Element("food"), 0);
                record.Fame = ReadInt(totals.Element("fame"), 0);
                record.Xp = ReadInt(totals.Element("xp"), 0);
                record.Trade = ReadInt(totals.Element("trade"), 0);
                record.Shipments = ReadInt(totals.Element("ships"), 0);
            }
        }

        var units = game.Element("unitcounts");
        if (units != null)
        {
            foreach (var unit in units.Elements())
            {
                if (!int.TryParse(unit.Value.Trim(), out var count) || count <= 0) continue;
                record.Units[unit.Name.LocalName] = count;
            }
        }

        return record;
    }

    /// <summary>
    /// The element's own text, ignoring its children — for the two mixed-content elements in this
    /// format. <c>Element.Value</c> is the wrong tool here and would silently return a name with
    /// every descendant number appended to it.
    /// </summary>
    private static string LeadingText(XElement element)
    {
        var text = element.Nodes().OfType<XText>().FirstOrDefault()?.Value;
        return text == null ? "" : text.Trim();
    }

    private static int ReadInt(XElement? element, int fallback)
        => element != null
           && int.TryParse(element.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                           out var value)
            ? value
            : fallback;

    // ------------------------------------------------------------------ the one method with IO

    /// <summary>
    /// Every game every personality file in this mod's user data records.
    ///
    /// <para><paramref name="userDataDir"/> is the resolved <c>My Games\&lt;mod&gt;</c> folder —
    /// a plain string, never a profile and never the config, so this stays testable and the
    /// caller keeps the dual-root resolution it already owns.</para>
    /// </summary>
    public static IReadOnlyList<AiGameRecord> Read(string userDataDir, string modId, DateTime capturedAtUtc)
    {
        var all = new List<AiGameRecord>();
        if (string.IsNullOrWhiteSpace(userDataDir)) return all;

        try
        {
            var dir = Path.Combine(userDataDir, FolderName);
            if (!Directory.Exists(dir)) return all;

            foreach (var file in Directory.EnumerateFiles(dir, "*" + Extension))
            {
                try
                {
                    // The files declare encoding="UTF-16" and carry a BOM. Reading them as UTF-8
                    // yields a string full of NULs that parses as nothing at all — silently.
                    var xml = File.ReadAllText(file, Encoding.Unicode);

                    // The FILE's write time, not the caller's clock: AoE3 writes it as the match
                    // ends, so for the game that just finished this is when it finished. Falls
                    // back to the caller's time if the stamp cannot be read.
                    var written = capturedAtUtc;
                    try { written = File.GetLastWriteTimeUtc(file); } catch { }

                    all.AddRange(Parse(
                        Path.GetFileNameWithoutExtension(file), modId, xml, written));
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Write($"AiGameStats: could not read '{file}' — {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"AiGameStats: could not scan '{userDataDir}' — {ex.Message}");
        }

        return all;
    }
}
