using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Reads the header of an Age of Empires III <c>.age3Yrec</c> recorded game — the
/// file the engine writes on its own when a match ends, in
/// <c>Documents\My Games\&lt;mod&gt;\Savegame\</c>.
///
/// <para><b>Why this exists.</b> The launcher reports finished matches to the lobby
/// backend, but has no idea what was played: the map goes up as null and every
/// participant as a draw. The recorded game is the only artefact that knows, and it
/// is written for every match without asking the player for anything.</para>
///
/// <para><b>What it does NOT do.</b> The header is the pre-game lobby configuration,
/// so it does not say who won — verified against a real 2v2: 309 keys, none about
/// the outcome (<c>rank</c> and <c>winratio</c> are the players' ESO ladder stats
/// from BEFORE the match), and the command stream that follows carries no readable
/// resign or defeat marker among its ~27,000 strings, which are all asset names.
/// Deriving a winner means decoding the command opcodes; that is a separate job.</para>
///
/// <para><b>Pure and WPF-free on purpose</b>, like <see cref="VerifyService"/> and
/// <c>AddonRisk</c>, so the format work is pinned by unit tests against bytes the
/// game actually wrote rather than a fixture built from a guess.</para>
///
/// <para><b>Read-only.</b> Nothing here writes to the replay; the byte-faithful rule
/// that governs installs applies just as much to a file the player may want to watch.</para>
/// </summary>
public static class ReplayParserService
{
    /// <summary>Container magic — the same four bytes AoE2 recorded games use.</summary>
    private static readonly byte[] Magic = { (byte)'l', (byte)'3', (byte)'3', (byte)'t' };

    /// <summary>
    /// Sanity ceiling on the decompressed stream. A real 25-minute 2v2 decompresses to
    /// ~8.9 MB, so 256 MB is far above anything legitimate while still refusing to
    /// inflate a hostile or corrupt file into memory without limit.
    /// </summary>
    internal const int MaxDecompressedBytes = 256 * 1024 * 1024;

    /// <summary>Longest plausible key or string value, in UTF-16 characters.</summary>
    private const int MaxStringChars = 1024;

    /// <summary>
    /// Dictionary value type tags and their widths, all four measured against a real
    /// 2v2 (370 keys: 159 ints, 150 strings, 35 bools, 26 floats).
    ///
    /// <para>Getting this table complete is what makes the walk work at all. With only
    /// the two obvious types the very first bool — <c>gamerestored</c>, the 3rd key —
    /// ends the walk, which still yields the map and player count (they come earlier)
    /// while silently returning ZERO players. A partial table doesn't fail loudly, it
    /// truncates.</para>
    /// </summary>
    private const uint TypeFloat = 1;   // 4 bytes
    private const uint TypeInt = 2;     // 4 bytes
    private const uint TypeBool = 5;    // 1 byte
    private const uint TypeString = 9;  // [uint32 chars][UTF-16LE]

    /// <summary>An empty slot. Those players are placeholders, not participants.</summary>
    private const uint SlotTypeEmpty = 3;

    /// <summary>A human player, as opposed to the AI (1) or the nature slot (2).</summary>
    public const uint SlotTypeHuman = 0;

    /// <summary>
    /// One player slot. <paramref name="Civilization"/> is the raw index the file
    /// carries, NOT a name: the index means different civilizations in different mods
    /// (civ 8 in Struggle of Indonesia is not civ 8 in Wars of Liberty), and resolving
    /// it needs that mod's own civ list. Handing back the number keeps this honest.
    ///
    /// <para>Turning it into a name is <see cref="CivNameResolver"/>'s job, and it needs an
    /// install path this class has no business knowing about. The index is <b>1-based</b> —
    /// measured, see the .age3Yrec section of the multiplayer rules — but that arithmetic lives
    /// there too, so nothing has to do it twice.</para>
    /// </summary>
    public sealed record ReplayPlayer(
        int Slot,
        string Name,
        int Civilization,
        int TeamId,
        uint SlotType)
    {
        public bool IsHuman => SlotType == SlotTypeHuman;
    }

    /// <summary>
    /// What the pre-game header knows about a match.
    ///
    /// <para><paramref name="MapName"/> is the map that was actually played, which is
    /// <c>gamefilename</c> and NOT <c>gamemapname</c>. On a competitive game the latter
    /// holds the map POOL — a real 2v2 reports <c>gamemapname="ESOC Maps"</c> against
    /// <c>gamefilename="ESOC_Baja California"</c>. They agree on a plain skirmish
    /// (both "amazonia"), which is exactly why picking the wrong one looks right until
    /// it reaches the games people care about. <paramref name="MapPool"/> keeps the
    /// other value rather than throwing it away.</para>
    /// </summary>
    /// <param name="RandomSeed">
    /// The map seed, and with <paramref name="HostTime"/> the closest thing this format
    /// has to an identifier for the MATCH itself.
    ///
    /// <para>It is the number that makes every machine generate the same map, so by
    /// construction both players of one game carry it and two different games do not.
    /// Measured across six real recordings: six different seeds, including two
    /// back-to-back games by the same host on the same evening (22235 and 15346, host
    /// clocks fifteen apart) — which is exactly the case that a name or a timestamp
    /// cannot tell apart.</para>
    ///
    /// <para>Only 15 bits in practice (the largest seen is 32747), so it is never used
    /// alone: paired with the host clock, two distinct matches would have to collide on
    /// both. Zero when the file did not carry it.</para>
    /// </param>
    /// <param name="HostTime">
    /// The host's clock at match start, as the recording records it. Travels with the
    /// seed and is never the sole discriminator.
    ///
    /// <para><b>Unverified across machines.</b> Only one side of each match was available
    /// to measure, so whether the guest's recording carries the same value is plausible
    /// but not established. Nothing is allowed to depend on it until a two-machine test
    /// settles it — see the multiplayer rules.</para>
    /// </param>
    public sealed record ReplayHeader(
        string GameVersion,
        string GameName,
        string MapName,
        string MapPool,
        int PlayerCount,
        IReadOnlyList<ReplayPlayer> Players,
        uint RandomSeed = 0,
        uint HostTime = 0);

    /// <summary>
    /// Unwraps the container: checks the magic, reads the declared decompressed size
    /// and inflates the zlib stream that follows.
    ///
    /// <para>The declared size is verified against what actually came out. That check
    /// is free and it is the cheapest way to notice a truncated or corrupt recording
    /// before spending any effort parsing it.</para>
    ///
    /// <para>Returns null for anything that isn't a readable recorded game. It never
    /// throws: this reads a file written by a 2007 game engine, possibly half-flushed
    /// because the process was killed, and the caller is a best-effort path on the
    /// way out of a match.</para>
    /// </summary>
    public static byte[]? TryReadContainer(byte[] raw)
    {
        if (raw == null || raw.Length < 12) return null;

        for (var i = 0; i < Magic.Length; i++)
        {
            if (raw[i] != Magic[i]) return null;
        }

        try
        {
            var declared = BitConverter.ToUInt32(raw, 4);
            if (declared == 0 || declared > MaxDecompressedBytes) return null;

            using var input = new MemoryStream(raw, 8, raw.Length - 8, writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream((int)Math.Min(declared, 16 * 1024 * 1024));
            zlib.CopyTo(output);

            var data = output.ToArray();
            // A mismatch means the recording is truncated or the length lied. Either
            // way the offsets below can't be trusted, so refuse rather than parse it.
            if (data.Length != declared) return null;
            return data;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Replay: container unreadable: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Walks the settings dictionary in the decompressed stream.
    ///
    /// <para>Framing, decoded from real files:</para>
    /// <code>
    /// [uint32 nChars][key, UTF-16LE]
    /// [uint32 type]   9 = string -> [uint32 nChars][value, UTF-16LE]
    ///                 2 = int    -> [uint32 value]
    /// </code>
    ///
    /// <para>Anything unrecognised ends the walk instead of trying to resynchronise.
    /// Skipping ahead on a bad length would let the parser wander into the command
    /// stream and report confident nonsense; stopping yields whatever was read
    /// cleanly, which the caller can judge by whether it found any players.</para>
    /// </summary>
    public static ReplayHeader? ParseHeader(byte[] data)
    {
        if (data == null || data.Length < 32) return null;

        var dict = ReadDictionary(data);
        if (dict.Count == 0) return null;

        var players = new List<ReplayPlayer>();
        // 12 covers AoE3's 8 player slots plus the nature/placeholder ones above them.
        for (var slot = 0; slot <= 12; slot++)
        {
            if (!dict.TryGetValue($"gameplayer{slot}name", out var nameObj)) continue;

            var type = GetUInt(dict, $"gameplayer{slot}type");
            if (type == SlotTypeEmpty) continue;          // unfilled slot

            var name = nameObj as string ?? "";
            if (type != SlotTypeHuman && string.IsNullOrEmpty(name)) continue;   // nature slot

            players.Add(new ReplayPlayer(
                Slot: slot,
                Name: name,
                Civilization: unchecked((int)GetUInt(dict, $"gameplayer{slot}civ")),
                TeamId: unchecked((int)GetUInt(dict, $"gameplayer{slot}teamid")),
                SlotType: type));
        }

        var pool = GetString(dict, "gamemapname");
        var file = GetString(dict, "gamefilename");

        return new ReplayHeader(
            GameVersion: ReadVersionString(data),
            GameName: GetString(dict, "gamename"),
            // The specific map wins; the pool is the fallback for anything that only
            // records one of the two.
            MapName: file.Length > 0 ? file : pool,
            MapPool: pool,
            PlayerCount: unchecked((int)GetUInt(dict, "gamenumplayers")),
            Players: players,
            // Already in the dictionary this method walks — nothing new is parsed, two
            // more keys are simply surfaced. See the record's docs for what they buy.
            RandomSeed: GetUInt(dict, "gamerandomseed"),
            HostTime: GetUInt(dict, "gamehosttime"));
    }

    /// <summary>Convenience for the whole path: bytes on disk to a parsed header.</summary>
    public static ReplayHeader? TryParse(byte[] raw)
    {
        var data = TryReadContainer(raw);
        return data == null ? null : ParseHeader(data);
    }

    /// <summary>
    /// Whether this recording is plausibly the match the launcher just watched, rather
    /// than some other file that happens to be newer.
    ///
    /// <para><b>Picking "the newest file" alone is not safe, and the failure is ordinary
    /// rather than exotic.</b> Downloaded replays live in <c>Savegame\</c>, because that
    /// is where the game looks for them, and their timestamp is when they were copied.
    /// On the maintainer's own disk, two replays belonging to other players sat eleven
    /// minutes newer than his own games — so a match started in between would have
    /// selected a stranger's file and reported a result for two people who never
    /// played it.</para>
    ///
    /// <para>Two checks, both from data the launcher already holds:</para>
    /// <list type="bullet">
    ///   <item>the host is among the players — the strong one, since another player's
    ///         replay simply does not contain him;</item>
    ///   <item>the number of humans matches the room's participants.</item>
    /// </list>
    ///
    /// <para><b>There used to be a third — "the slot that recorded the file is the
    /// host's", taken from the trailer's B field — and it REJECTED THE WINNER'S OWN
    /// RECORDING in every multiplayer match.</b> B does not identify the recorder: the
    /// two players' copies of one match are byte-identical for the last 64 bytes, so no
    /// field in there can say which of them wrote it. In multiplayer B is the loser, so
    /// the gate passed only for whoever lost. Combined with <c>HostResultFrom</c> reading
    /// the same field as the host's slot, a match could be rated only when the host LOST;
    /// a host who won had his recording thrown away here with "is not this match". Proven
    /// on three matches with both players' files and both players' logs. See
    /// <see cref="ReadOutcome"/> for what B actually appears to be.</para>
    ///
    /// <para>The host's name comes from his AoE3 profile, so it is compared
    /// case-insensitively and trimmed; anything blank fails, because an unknown host
    /// cannot confirm anything.</para>
    ///
    /// <para><b>An unknown head count fails too</b>, for exactly that reason —
    /// <paramref name="expectedHumans"/> of zero means the room's roster was lost, not that the
    /// check is optional. It used to be skipped instead, which quietly reduced three checks to
    /// two at precisely the moment there was least to go on. The caller announces the recording
    /// separately when it only wants to name the file, so failing here costs nothing but a
    /// result nobody could stand behind.</para>
    /// </summary>
    public static bool LooksLikeThisMatch(
        ReplayHeader? header, string hostProfileName, int expectedHumans)
    {
        if (header == null) return false;

        var humans = header.Players.Where(p => p.IsHuman).ToList();
        if (expectedHumans <= 0 || humans.Count != expectedHumans) return false;

        return FindPlayerSlot(header, hostProfileName) >= 0;
    }

    /// <summary>
    /// The slot of the human called <paramref name="profileName"/>, or <c>-1</c>.
    ///
    /// <para><b>This is the only supported way to learn which slot the local player
    /// occupies.</b> The name comes from his own AoE3 profile, is written into the
    /// recording by the game, and is the one thing in the file that differs between two
    /// people — so it identifies him where the outcome trailer cannot. Matching is
    /// trimmed and case-insensitive; a blank name finds nobody, because an unknown player
    /// cannot confirm anything.</para>
    ///
    /// <para>Extracted so <see cref="LooksLikeThisMatch"/> and the caller that needs the
    /// host's slot for <see cref="HostResultFrom"/> cannot drift apart: they used to
    /// answer "which slot is ours" two different ways, and the other one was wrong.</para>
    /// </summary>
    public static int FindPlayerSlot(ReplayHeader? header, string profileName)
    {
        if (header == null) return -1;
        if (string.IsNullOrWhiteSpace(profileName)) return -1;

        var match = header.Players.FirstOrDefault(p => p.IsHuman
            && string.Equals(p.Name.Trim(), profileName.Trim(), StringComparison.OrdinalIgnoreCase));
        return match?.Slot ?? -1;
    }

    /// <summary>How much the outcome below can be trusted. Two values, not a bool,
    /// so a caller cannot read "I don't know" as "it was a draw".</summary>
    public enum ReplayOutcomeConfidence
    {
        /// <summary>Report a draw. Never guess a winner from this.</summary>
        Ambiguous,
        Confident,
    }

    /// <summary>
    /// Who lost and who won. Slots are -1 when unknown.
    ///
    /// <para><paramref name="SignaturePresent"/> answers a different question from
    /// <paramref name="Confidence"/>, and the caller needs both: false means the recording
    /// ends without the trailer at all — the game was closed before it finished writing
    /// the ending — while an Ambiguous outcome WITH a signature means the trailer was read
    /// and simply did not name a result we can use. Those are different things to tell a
    /// player, which is why they are different fields.</para>
    /// </summary>
    public sealed record ReplayOutcome(
        ReplayOutcomeConfidence Confidence,
        int LoserSlot,
        int WinnerSlot,
        int TrailerSecondSlot,
        bool SignaturePresent,
        System.Collections.Generic.IReadOnlyList<int>? EliminatedSlots = null);

    /// <summary>The 12 zero bytes + 8 × 0xFF that precede the trailing triple.</summary>
    private const int OutcomeTrailerBytes = 32;

    /// <summary>
    /// How far back from the end of the file the outcome block may sit.
    ///
    /// <para><b>This was 8, and 8 was losing one competitive match in five.</b> Measured over 20
    /// readable 1v1 recordings: the old bound decided 16 of them, and the four it gave up on had
    /// a perfectly valid block at 78, 79, 135 and 195 bytes from the end. The player saw "the
    /// recording gave no result" and the match went down unrated — which is how it was reported,
    /// by a player whose 21-minute game simply did not count.</para>
    ///
    /// <para><b>The measurement that makes a window this wide safe, because the old comment's
    /// caution was right about the danger and wrong about the remedy.</b> The pattern
    /// <c>00×12 FF×8</c> IS ordinary command-stream data — 3,155 occurrences in the reported
    /// file — so a parser that simply took the nearest hit would be reading noise. Within the
    /// last 512 bytes of those same 20 files there are 26 candidates; exactly 20 pass validation,
    /// <b>one per file, never two</b>, and the 6 rejected are unmistakable rubbish (<c>A = -1</c>
    /// with <c>C = 3212836864</c>, which is the float <c>-1.0</c> repeated). So the safety comes
    /// from validating each candidate, not from the window being small — which is what the old
    /// comment said in its last paragraph while the constant said otherwise.</para>
    ///
    /// <para>512 against a measured maximum of 195: wide enough that the next few bytes of
    /// variation cost nothing, narrow enough that it is still the tail of the file rather than a
    /// search. <b>Do not raise it without repeating the measurement</b> — the density of
    /// accidental signatures is what bounds this, and it grows with the window.</para>
    ///
    /// <para><b>Never do this by trimming trailing zeros instead.</b> The block's own last field
    /// is <c>02 00 00 00</c> — it ENDS in three zeros — so trimming eats the payload. Anchor on
    /// the signature, never on the end of the file.</para>
    /// </summary>
    private const int MaxTrailingSlack = 512;

    /// <summary>The signature at an exact offset: 12 zero bytes then 8 × 0xFF.</summary>
    private static bool HasSignatureAt(byte[] data, int start)
    {
        for (var i = 0; i < 12; i++)
            if (data[start + i] != 0x00) return false;
        for (var i = 12; i < 20; i++)
            if (data[start + i] != 0xFF) return false;
        return true;
    }

    /// <summary>
    /// Every signature in the tail, nearest to the end first.
    ///
    /// <para>It used to return only the nearest one, and <see cref="ReadOutcome"/> gave up if
    /// that one did not validate. <b>That is not enough, and the file that proved it is the one
    /// this change came from:</b> its nearest signature is 12 bytes from the end and is rubbish
    /// (<c>A = -1</c>), while the real block sits at 135. Widening the window alone would still
    /// have thrown that match away. Enumerating is the other half.</para>
    /// </summary>
    private static IEnumerable<int> TrailerCandidates(byte[] data)
    {
        for (var slack = 0; slack <= MaxTrailingSlack; slack++)
        {
            var start = data.Length - OutcomeTrailerBytes - slack;
            if (start < 0) yield break;
            if (HasSignatureAt(data, start)) yield return start;
        }
    }

    /// <summary>
    /// Reads the match outcome from the end of the stream.
    ///
    /// <para>The last 32 bytes of a normally-finished recording are
    /// <c>[00 × 12][FF × 8][A][B][C]</c>, three uint32 where <b>A is the slot that
    /// LOST</b>, C is the number of humans, and <b>B is not understood</b>.</para>
    ///
    /// <para><b>A is measured, then predicted, then confirmed.</b> A is the loser across
    /// five games whose result was known independently: three skirmishes (two resigned,
    /// one won) and two real 1v1s between humans. The rival readings die on the same case
    /// — a game the recorder WON reports A = the opponent's slot, so A is neither the
    /// winner nor the recorder.</para>
    ///
    /// <para><b>B was read as "the slot that recorded the file" and that was WRONG. Never
    /// use it to identify anybody.</b> The evidence for it was two singleplayer files from
    /// the same human, where B pointed at him across different slots — but with one player
    /// and one machine, "the recorder" and "the one who ended the game" cannot be told
    /// apart. Three multiplayer matches captured from BOTH players settle it: the two
    /// copies of a match are byte-identical for the last 64 bytes, so B cannot name which
    /// machine wrote one, and in all six copies B equals the loser. The consequence was
    /// severe — see <see cref="LooksLikeThisMatch"/>.</para>
    ///
    /// <para>What fits all eight observations is <i>the slot that ENDED the game</i>: the
    /// human who destroyed the AI in the singleplayer files, the player who resigned in
    /// the multiplayer ones. That is a hypothesis, not a reading — nothing depends on it,
    /// and nothing should. Use <see cref="FindPlayerSlot"/> to find the local player.</para>
    ///
    /// <para><b>Two of seven recordings have no trailer at all</b> — games that ended
    /// abnormally. That is why this checks for the signature instead of assuming it, and
    /// why the enum exists: no trailer means no answer, not a draw that happens to be
    /// reported as one.</para>
    ///
    /// <para><b>And some carry it with a byte or two of slack after it</b>, which used to read as
    /// "no trailer" and cost the loser's reading of a match both players had recorded correctly.
    /// See <see cref="MaxTrailingSlack"/> — including why this is not the tolerant backwards
    /// search the format notes forbid.</para>
    ///
    /// <para>Confident requires everything to line up: the exact signature, an A that
    /// names a slot the header actually has, exactly two players, and <b>no AI among
    /// them</b>. Anything else is Ambiguous, which the caller must report as a draw:
    /// this feeds a rating, and an invented winner takes points from someone silently.</para>
    ///
    /// <para><b>The AI check is defence in depth, not the main gate.</b> Reporting
    /// already cannot include a skirmish — <c>TryReportMatchAsync</c> needs a lobby and
    /// two participants drawn from the room's members, and an AI is never a room member.
    /// But the replay is chosen as "newest file written after the match started", so a
    /// stray skirmish recording could reach this method, and a verdict derived from one
    /// would look exactly as trustworthy as a real one. Cheap to refuse, expensive to
    /// notice later.</para>
    ///
    /// <para>Note what this does NOT prevent: two real people agreeing on a result. That
    /// replay is genuine — two humans, a real loser — and belongs to rate-limiting and
    /// repeat-pairing checks on the backend, not here.</para>
    /// </summary>
    public static ReplayOutcome ReadOutcome(byte[] data, ReplayHeader? header)
    {
        var noSignature = new ReplayOutcome(ReplayOutcomeConfidence.Ambiguous, -1, -1, -1, false);
        if (data == null || header == null || data.Length < OutcomeTrailerBytes) return noSignature;

        // How many of the header's slots are people. The block's THIRD field equals this in
        // every 1v1 ever measured (48 of 48), which makes it a second, independent way to tell
        // this match's block from an accidental one in the command stream.
        //
        // It is a PREFERENCE and not a requirement, and a four-player recording is why: its
        // blocks read C = 1 with four humans, so "C == humans" is a property of the 1v1 and not
        // of the format. Requiring it would make that match unreadable.
        var humans = header.Players.Count(p => p.IsHuman);

        var first = -1;     // the nearest signature, whatever it turned out to say
        var strong = -1;    // nearest one whose loser AND head count describe this match
        var weak = -1;      // nearest one whose loser does, on its own

        // Every block that names a real slot, nearest-to-the-end FIRST. A match of more than two
        // players writes one per elimination — measured on a four-player game carrying two, at
        // 0 and 81 bytes out, naming two different slots — so this is the elimination sequence in
        // reverse. Diagnostic only: the DECISION is the first entry, which is what LoserSlot
        // already was, because a team game ends when the last member of a side falls.
        var eliminated = new System.Collections.Generic.List<int>();

        foreach (var start in TrailerCandidates(data))
        {
            if (first < 0) first = start;

            var a = unchecked((int)BitConverter.ToUInt32(data, start + 20));
            if (header.Players.All(p => p.Slot != a)) continue;

            if (!eliminated.Contains(a)) eliminated.Add(a);

            var c = unchecked((int)BitConverter.ToUInt32(data, start + 28));
            // Not a break, which it used to be: the walk continues to the end of the window so
            // the elimination list is complete even after the decision is settled. Both picks
            // are taken once, by the nearest candidate that qualifies.
            if (c == humans && strong < 0) strong = start;
            else if (weak < 0 && strong < 0) weak = start;
        }

        if (first < 0) return noSignature;

        // The head count is a PREFERENCE, never a refusal, and that asymmetry is deliberate.
        // The two fixtures this parser is tested against are real skirmishes whose block says
        // C = 1; the tests relabel their AI as human to exercise the 1v1 path, so demanding
        // C == humans would reject genuine files over a doctored header. Preferring it costs
        // nothing when there is one candidate and settles it when there are several — which is
        // the only case a 512-byte window introduces.
        var chosen = strong >= 0 ? strong : weak;
        if (chosen < 0)
        {
            // A signature was there and named nobody we recognise. Different from "the game
            // never wrote its ending", which is why SignaturePresent is a separate field.
            return new ReplayOutcome(
                ReplayOutcomeConfidence.Ambiguous, -1, -1,
                unchecked((int)BitConverter.ToUInt32(data, first + 24)), true, eliminated);
        }

        if (strong < 0)
        {
            DiagnosticLog.Write(
                "Replay: outcome block accepted on its loser slot alone — its head count " +
                $"({unchecked((int)BitConverter.ToUInt32(data, chosen + 28))}) disagrees with the " +
                $"header's {humans}.");
        }

        var loser = unchecked((int)BitConverter.ToUInt32(data, chosen + 20));
        var second = unchecked((int)BitConverter.ToUInt32(data, chosen + 24));

        // Past the signature, so the trailer exists whatever it turns out to say.
        var unknown = new ReplayOutcome(
            ReplayOutcomeConfidence.Ambiguous, -1, -1, second, true, eliminated);

        // Beyond a 1v1, "X lost" doesn't name a winner: the others may have lost too,
        // and nothing here says in what order. Those stay draws until the room state
        // can identify every player.
        //
        // The loser slot is still handed back on both this path and the AI one below —
        // it was read correctly and is worth having in a diagnostic bundle. What the
        // caller loses is permission to treat it as a result. MatchResultResolver.
        // ResolveTeamResults is the one caller that can use it, because the room's team
        // map turns one named loser into a whole side.
        if (header.Players.Count != 2)
            return unknown with { LoserSlot = loser };

        // A skirmish is not a match, whoever the trailer says lost it.
        if (header.Players.Any(p => !p.IsHuman))
            return unknown with { LoserSlot = loser };

        var winner = header.Players.First(p => p.Slot != loser).Slot;
        return new ReplayOutcome(
            ReplayOutcomeConfidence.Confident, loser, winner, second, true, eliminated);
    }

    /// <summary>
    /// Turns an outcome into the host's score — <c>1.0</c> won, <c>0.0</c> lost — or
    /// <b>null</b> when the recording does not say.
    ///
    /// <para>Null is the answer for everything that is not a clean, confident 1v1 the host
    /// played in: an ambiguous outcome, a host whose slot is neither of the two named. The
    /// caller turns null into the 0.5 draw that reporting used before any of this existed,
    /// so the failure mode is "no worse than before" rather than a wrong winner.</para>
    ///
    /// <para><b><paramref name="hostSlot"/> must come from <see cref="FindPlayerSlot"/>,
    /// never from the trailer.</b> It was fed the trailer's B field, which in multiplayer
    /// is the loser — so this method could only ever return 0.0, and no host ever
    /// registered a win.</para>
    ///
    /// <para>Kept here, pure, rather than in the caller: <c>MultiplayerTab</c> is WPF and
    /// cannot be tested, and this is the one line where a mistake silently moves rating
    /// points between two real people.</para>
    /// </summary>
    public static double? HostResultFrom(ReplayOutcome? outcome, int hostSlot)
    {
        if (outcome == null || outcome.Confidence != ReplayOutcomeConfidence.Confident) return null;
        if (hostSlot < 0) return null;

        if (hostSlot == outcome.WinnerSlot) return 1.0;
        if (hostSlot == outcome.LoserSlot) return 0.0;

        // The host is not one of the two the trailer named. Either the wrong recording was
        // picked or the slots do not mean what we think; both are reasons to say nothing.
        return null;
    }

    private static uint GetUInt(IReadOnlyDictionary<string, object> dict, string key)
        => dict.TryGetValue(key, out var v) && v is uint u ? u : 0;

    private static string GetString(IReadOnlyDictionary<string, object> dict, string key)
        => dict.TryGetValue(key, out var v) && v is string s ? s : "";

    /// <summary>
    /// The executable version ("age3y.exe 6.0108.0321.0137") sits near the start as a
    /// plain UTF-16 run, before the dictionary proper. Read leniently — it is
    /// informational, and a miss must not cost the rest of the header.
    /// </summary>
    private static string ReadVersionString(byte[] data)
    {
        const int scanLimit = 512;
        var end = Math.Min(scanLimit, data.Length - 1);
        var sb = new StringBuilder();

        for (var i = 0; i < end - 1; i++)
        {
            if (data[i] < 32 || data[i] >= 127 || data[i + 1] != 0) continue;

            sb.Clear();
            var j = i;
            while (j < end - 1 && data[j] >= 32 && data[j] < 127 && data[j + 1] == 0)
            {
                sb.Append((char)data[j]);
                j += 2;
            }
            if (sb.Length >= 8 && sb.ToString().Contains(".exe", StringComparison.OrdinalIgnoreCase))
                return sb.ToString();
            i = j;
        }
        return "";
    }

    private static Dictionary<string, object> ReadDictionary(byte[] data)
    {
        var dict = new Dictionary<string, object>(StringComparer.Ordinal);

        // Start after the version run rather than at 0: the bytes before it are fixed
        // fields, and feeding them to the walker would just be noise.
        var pos = FindDictionaryStart(data);

        while (pos + 8 <= data.Length)
        {
            if (!TryReadString(data, ref pos, out var key)) break;
            if (key.Length == 0) break;

            if (pos + 4 > data.Length) break;
            var type = BitConverter.ToUInt32(data, pos);
            pos += 4;

            if (type == TypeString)
            {
                if (!TryReadString(data, ref pos, out var value)) break;
                dict.TryAdd(key, value);
            }
            else if (type == TypeInt)
            {
                if (pos + 4 > data.Length) break;
                dict.TryAdd(key, BitConverter.ToUInt32(data, pos));
                pos += 4;
            }
            else if (type == TypeFloat)
            {
                if (pos + 4 > data.Length) break;
                dict.TryAdd(key, BitConverter.ToSingle(data, pos));
                pos += 4;
            }
            else if (type == TypeBool)
            {
                if (pos + 1 > data.Length) break;
                dict.TryAdd(key, data[pos] != 0);
                pos += 1;
            }
            else
            {
                // Unknown tag, or the 0xFFFFFFFF terminator that closes the dictionary.
                // Either way the settings are over: keep what was read and stop rather
                // than resynchronise into the command stream and report nonsense.
                break;
            }
        }

        return dict;
    }

    /// <summary>
    /// Finds the first plausible <c>[len][key]</c> pair. The dictionary begins right
    /// after the version string, but its exact offset shifts with the version's
    /// length, so it is located rather than hardcoded.
    /// </summary>
    private static int FindDictionaryStart(byte[] data)
    {
        var limit = Math.Min(4096, data.Length - 8);
        for (var i = 0; i < limit; i++)
        {
            var len = BitConverter.ToUInt32(data, i);
            if (len is < 4 or > 64) continue;
            if (i + 4 + (int)len * 2 > data.Length) continue;

            var probe = i;
            if (!TryReadString(data, ref probe, out var candidate)) continue;
            // Every settings key in a real file starts with "game".
            if (candidate.StartsWith("game", StringComparison.Ordinal)) return i;
        }
        return 0;
    }

    private static bool TryReadString(byte[] data, ref int pos, out string value)
    {
        value = "";
        if (pos + 4 > data.Length) return false;

        var chars = BitConverter.ToUInt32(data, pos);
        if (chars > MaxStringChars) return false;

        var bytes = (int)chars * 2;
        if (pos + 4 + bytes > data.Length) return false;

        value = chars == 0 ? "" : Encoding.Unicode.GetString(data, pos + 4, bytes);
        pos += 4 + bytes;
        return true;
    }
}
