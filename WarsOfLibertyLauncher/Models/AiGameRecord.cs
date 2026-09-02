using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WarsOfLibertyLauncher.Models;

/// <summary>
/// One game against the AI, as the game itself recorded it — the end-of-match statistics screen,
/// which AoE3 serialises into the AI's own memory file and nothing else ever reads.
///
/// <para><b>Why this is stored rather than read on demand.</b> The
/// <c>My Games\&lt;mod&gt;\AI4\&lt;ai&gt;.personality</c> file accumulates a block per game, but
/// <b>only the NEWEST block carries the totals</b>: measured on a real four-game file, the latest
/// game reads gold 300820 / xp 84506 / 42 shipments while the three before it read zero for every
/// one of those, with their unit counts intact. So the resources, the score and the shipment count
/// exist for exactly one game at a time and the game forgets them on the next launch. Harvesting
/// after each match is the only way they survive, and that — not convenience — is why there is a
/// store at all.</para>
///
/// <para><b>It only exists when an AI played.</b> The file is the AI's memory of the humans it has
/// faced; a match between two people writes nothing, so nothing here says anything about the
/// ladder and it must never be mixed with it.</para>
/// </summary>
public class AiGameRecord
{
    /// <summary>The AI's personality file name without its extension — <c>wolMenelik</c>.</summary>
    [JsonPropertyName("personality")]
    public string Personality { get; set; } = "";

    /// <summary>
    /// Which mod this was played in. <b>Not in the file</b> — the launcher knows it because it
    /// launched the game, and without it two mods' games would pile into one list.
    /// </summary>
    [JsonPropertyName("modId")]
    public string ModId { get; set; } = "";

    /// <summary>
    /// The human the AI recorded this about, by AoE3 profile name. Not an account: the same
    /// person is a different name in every mod, which is why nothing joins this to Discord.
    /// </summary>
    [JsonPropertyName("player")]
    public string PlayerName { get; set; } = "";

    /// <summary>Match length in milliseconds, from <c>stattime</c>.</summary>
    [JsonPropertyName("durationMs")]
    public long DurationMs { get; set; }

    /// <summary>
    /// Whether the HUMAN won. <b>Measured, because the field is called <c>myteamwon</c> and "my"
    /// could as easily have meant the AI's team</b> — reading it backwards would print a defeat
    /// as a victory. Checked against the outcome trailer of the recording each game paired with,
    /// three times and in both directions: two losses read 0 and a win reads 1.
    ///
    /// <para>Null when the block did not carry it.</para>
    /// </summary>
    [JsonPropertyName("won")]
    public bool? Won { get; set; }

    /// <summary>Seconds into the match before the first attack, or -1 when there never was one.</summary>
    [JsonPropertyName("firstAttackSeconds")]
    public int FirstAttackSeconds { get; set; } = -1;

    /// <summary>
    /// Final score. <b>Zero is ordinary and does not mean "scored nothing"</b> — see the class
    /// summary: every block but the newest is rewritten with zeroed totals, so an imported game
    /// from before the launcher started harvesting has this at 0 with real unit counts beside it.
    /// The view has to treat 0 as "not recorded" rather than as a number.
    /// </summary>
    [JsonPropertyName("score")]
    public int Score { get; set; }

    /// <summary>Total resources gathered. Same "0 means not recorded" caveat as the score.</summary>
    [JsonPropertyName("gold")] public int Gold { get; set; }
    [JsonPropertyName("wood")] public int Wood { get; set; }
    [JsonPropertyName("food")] public int Food { get; set; }
    [JsonPropertyName("fame")] public int Fame { get; set; }
    [JsonPropertyName("xp")] public int Xp { get; set; }
    [JsonPropertyName("trade")] public int Trade { get; set; }

    /// <summary>
    /// How many home-city shipments were sent — the closest thing to "cards played" that exists
    /// anywhere outside the command stream, and the reason this file was worth reading at all.
    /// Same "0 means not recorded" caveat.
    /// </summary>
    [JsonPropertyName("shipments")]
    public int Shipments { get; set; }

    /// <summary>
    /// Every unit and building type, by its INTERNAL proto name (<c>gwtank</c>,
    /// <c>ypsettlerasian</c>), with how many there were. <b>This is the one part that is filled in
    /// for every stored game</b>, newest or not.
    /// </summary>
    [JsonPropertyName("units")]
    public Dictionary<string, int> Units { get; set; } = new();

    /// <summary>
    /// When the match ended, ISO-8601 UTC — taken from the personality FILE's own write time,
    /// because nothing inside the file carries a date.
    ///
    /// <para><b>Exact for every game harvested normally</b>: AoE3 writes the file as the match
    /// ends and the launcher reads it seconds later, so the game being stored is the one that
    /// just finished. For the OLDER blocks already sitting in a file the first time it is read,
    /// it is an upper bound — they share the newest game's timestamp — which is bounded and
    /// explainable, where stamping them "now" would date a July game to today.</para>
    /// </summary>
    [JsonPropertyName("capturedAt")]
    public string CapturedAtUtc { get; set; } = "";

    /// <summary>
    /// What makes two entries the same game.
    ///
    /// <para>The file is rewritten whole after every match, so the same block is re-read every
    /// time and the store would grow without bound. There is no id in the file, so the key is the
    /// facts: who, how long, and a fingerprint of the unit counts.</para>
    ///
    /// <para><b>The score and the resources are deliberately NOT in it, and putting them back
    /// would break this quietly.</b> The same game reads differently on a later visit: the game
    /// zeroes the totals of every block but the newest, so a match captured with a score of
    /// 664331 comes back as 0 after the next launch. With the score in the key those two readings
    /// are different games, and the store would fill with a zeroed twin of everything it already
    /// holds. Duration and unit counts are what survive a rewrite, so they are what identifies a
    /// game.</para>
    ///
    /// <para>Two genuinely distinct games colliding on both is not credible; two aborted
    /// six-second games colliding is, and merging those loses nothing anybody would miss.</para>
    /// </summary>
    [JsonIgnore]
    public string DedupKey
    {
        get
        {
            var units = new List<string>(Units.Count);
            foreach (var kv in Units) units.Add(kv.Key + "=" + kv.Value.ToString());
            units.Sort(StringComparer.Ordinal);
            return string.Join("|", Personality, PlayerName, DurationMs, string.Join(",", units));
        }
    }
}
