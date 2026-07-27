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

    /// <summary>What the pre-game header knows about a match.</summary>
    public sealed record ReplayHeader(
        string GameVersion,
        string GameName,
        string MapName,
        int PlayerCount,
        IReadOnlyList<ReplayPlayer> Players);

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

        return new ReplayHeader(
            GameVersion: ReadVersionString(data),
            GameName: dict.TryGetValue("gamename", out var gn) ? gn as string ?? "" : "",
            MapName: dict.TryGetValue("gamemapname", out var mn) ? mn as string ?? "" : "",
            PlayerCount: unchecked((int)GetUInt(dict, "gamenumplayers")),
            Players: players);
    }

    /// <summary>Convenience for the whole path: bytes on disk to a parsed header.</summary>
    public static ReplayHeader? TryParse(byte[] raw)
    {
        var data = TryReadContainer(raw);
        return data == null ? null : ParseHeader(data);
    }

    private static uint GetUInt(IReadOnlyDictionary<string, object> dict, string key)
        => dict.TryGetValue(key, out var v) && v is uint u ? u : 0;

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
