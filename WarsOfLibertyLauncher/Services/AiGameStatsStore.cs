using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Keeps the games against the AI that <see cref="AiGameStats"/> reads, because the game itself
/// does not.
///
/// <para><b>This store is not a cache — it is the only copy.</b> AoE3 rewrites the personality
/// file after every match and zeroes the totals of every block but the newest, so a match's
/// score, resources and shipment count exist on disk for exactly one launch. Harvesting after each
/// game and keeping the result here is what turns a single visible game into a history.</para>
///
/// <para>Local only. Nothing here is sent anywhere, which is why it needs no consent and no
/// privacy note; the day any of it is uploaded, that changes and it becomes opt-in like the
/// multiplayer telemetry.</para>
///
/// <para>Load/Save follow <see cref="AddonOwnership"/>: both swallow their own errors, both write
/// indented, and the directory is created before every write because
/// <c>AppPaths.EnsureReady</c> only makes the root.</para>
/// </summary>
public static class AiGameStatsStore
{
    public static string FilePath { get; } = Path.Combine(AppPaths.DataDir, "ai-games.json");

    /// <summary>
    /// How many games are kept, newest first. Generous — a record is under a kilobyte — but
    /// bounded, because nothing else ever prunes this and a player who only plays the AI would
    /// otherwise grow it for years.
    /// </summary>
    public const int MaxGames = 500;

    public static List<AiGameRecord> Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<List<AiGameRecord>>(File.ReadAllText(FilePath));
                if (loaded != null) return loaded;
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"AI games: could not read {FilePath}: {ex.Message}");
        }
        return new List<AiGameRecord>();
    }

    public static void Save(IReadOnlyList<AiGameRecord> games)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(
                games, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"AI games: could not write {FilePath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Folds a fresh reading into what is already stored, and returns the whole set newest first.
    ///
    /// <para><b>Pure — no disk — because every rule in it is a decision worth pinning.</b></para>
    ///
    /// <para><b>The load-bearing one: on a collision the RICHER value wins, never the newer.</b>
    /// A game read again after the next match comes back with its totals zeroed, so "last write
    /// wins" would erase the score, the resources and the shipment count of every game in the
    /// store, one launch at a time — the exact data this exists to preserve, destroyed by the
    /// mechanism meant to keep it. Taking the maximum is safe because these are cumulative totals
    /// and the degraded reading is always zero.</para>
    /// </summary>
    public static List<AiGameRecord> Merge(
        IReadOnlyList<AiGameRecord> stored, IReadOnlyList<AiGameRecord> fresh)
    {
        var byKey = new Dictionary<string, AiGameRecord>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var game in stored.Concat(fresh))
        {
            if (game == null) continue;
            var key = game.DedupKey;

            if (!byKey.TryGetValue(key, out var kept))
            {
                byKey[key] = game;
                order.Add(key);
                continue;
            }

            byKey[key] = Richer(kept, game);
        }

        // Newest first, by when the launcher first saw each game. Ties keep insertion order, so a
        // file's own ordering survives for games captured in one pass — which is every game
        // imported the first time this runs.
        return order
            .Select(k => byKey[k])
            .OrderByDescending(g => g.CapturedAtUtc, StringComparer.Ordinal)
            .Take(MaxGames)
            .ToList();
    }

    /// <summary>
    /// The same game seen twice, reconciled field by field: the larger total wins, and the
    /// EARLIER capture time is kept so a re-read does not make an old game look new.
    /// </summary>
    private static AiGameRecord Richer(AiGameRecord a, AiGameRecord b)
    {
        return new AiGameRecord
        {
            Personality = string.IsNullOrEmpty(a.Personality) ? b.Personality : a.Personality,
            // A mod id is never empty on a fresh capture, but an entry written by a build that
            // predates the field would be — take whichever knows.
            ModId = string.IsNullOrEmpty(a.ModId) ? b.ModId : a.ModId,
            PlayerName = string.IsNullOrEmpty(a.PlayerName) ? b.PlayerName : a.PlayerName,
            DurationMs = Math.Max(a.DurationMs, b.DurationMs),
            Won = a.Won ?? b.Won,
            // -1 means "never attacked", so the larger value is the one that saw an attack.
            FirstAttackSeconds = Math.Max(a.FirstAttackSeconds, b.FirstAttackSeconds),
            Score = Math.Max(a.Score, b.Score),
            Gold = Math.Max(a.Gold, b.Gold),
            Wood = Math.Max(a.Wood, b.Wood),
            Food = Math.Max(a.Food, b.Food),
            Fame = Math.Max(a.Fame, b.Fame),
            Xp = Math.Max(a.Xp, b.Xp),
            Trade = Math.Max(a.Trade, b.Trade),
            Shipments = Math.Max(a.Shipments, b.Shipments),
            Units = a.Units.Count >= b.Units.Count ? a.Units : b.Units,
            CapturedAtUtc = Earlier(a.CapturedAtUtc, b.CapturedAtUtc),
        };
    }

    private static string Earlier(string a, string b)
    {
        if (string.IsNullOrEmpty(a)) return b;
        if (string.IsNullOrEmpty(b)) return a;
        return string.CompareOrdinal(a, b) <= 0 ? a : b;
    }

    /// <summary>
    /// Reads this mod's personality files and folds anything new into the store. The one entry
    /// point with IO, and it never throws — it runs on the game-exit path.
    /// </summary>
    /// <param name="userDataDir">The resolved <c>My Games\&lt;mod&gt;</c> folder.</param>
    public static void Harvest(string userDataDir, string modId, DateTime capturedAtUtc)
    {
        try
        {
            var fresh = AiGameStats.Read(userDataDir, modId, capturedAtUtc);
            if (fresh.Count == 0) return;

            var stored = Load();
            var merged = Merge(stored, fresh);

            // ALWAYS saved when anything was read, and the "skip if nothing new" guard that used
            // to sit here is gone on purpose. It compared only DEDUP KEYS, so a fresh reading that
            // improved an existing record — a better timestamp, a total recovered — matched an
            // existing key, counted as "nothing new" and was thrown away. That is exactly what
            // Merge exists to fold in, and a guard that can discard a correction is worth less
            // than the write it saves: the file is a few kilobytes, once per game exit.
            Save(merged);
            DiagnosticLog.Write(
                $"AI games: {fresh.Count} read for '{modId}', {merged.Count} stored in total.");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"AI games: harvest failed: {ex.Message}");
        }
    }
}
