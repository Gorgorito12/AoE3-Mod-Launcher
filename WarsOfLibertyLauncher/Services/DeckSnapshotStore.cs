using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>One match's worth of home city files, as they were when that match ended.</summary>
/// <param name="Files">File name to the content hash it was stored under.</param>
public sealed class DeckSnapshotEntry
{
    [JsonPropertyName("modId")] public string ModId { get; set; } = "";
    [JsonPropertyName("capturedUtc")] public DateTime CapturedUtc { get; set; }
    [JsonPropertyName("files")] public Dictionary<string, string> Files { get; set; } = new();
}

/// <summary>
/// Keeps a copy of the player's decks as they were when a match ended, so a match in the local
/// list can show what was actually brought rather than what the file happens to hold today.
///
/// <para><b>Why a copy is needed at all.</b> The recording names the home city FILE
/// (<c>sp_Beijing_homecity.xml</c>) and nothing else about the deck — there is no deck id, no
/// deck name, no marker of which deck was active, and both of a city's decks can share a
/// <c>gameid</c>. The file itself is on disk and readable, but it is MUTABLE: opening a July
/// match would show September's cards. So the only honest record is one taken at the time.</para>
///
/// <para><b>Captured at EXIT rather than at launch.</b> At launch the launcher cannot know which
/// home city the player is about to pick; at exit the recording names it and the file on disk is
/// still the one that was used.</para>
///
/// <para><b>Content-addressed.</b> A deck rarely changes between matches, so storing one copy
/// per match would keep forty identical files to preserve the three days it did change. Each
/// distinct file is written once under its hash and entries point at it.</para>
/// </summary>
public static class DeckSnapshotStore
{
    /// <summary>Matches kept. Comfortably more than the recordings the local list scans.</summary>
    public const int MaxEntries = 40;

    /// <summary>
    /// A multiplayer game reaches its end through TWO paths — the dashboard's exit monitor and
    /// the lobby's own watcher — so without a debounce one match would leave two entries.
    /// </summary>
    public const int DebounceSeconds = 120;

    /// <summary>
    /// How far a recording's timestamp may sit from a snapshot's and still be the same match.
    /// Both are "when the match ended" and differ by the seconds the launcher took to notice, so
    /// this is slack, not a guess; past it the answer is "no snapshot" rather than a wrong one.
    /// </summary>
    public static readonly TimeSpan MatchWindow = TimeSpan.FromMinutes(5);

    private const string FolderName = "deck-snapshots";
    private const string IndexName = "index.json";
    private const string SavegameFolder = "Savegame";
    private const string FilePattern = "*homecity*.xml";

    /// <summary>A guard against a folder that is not what we think it is.</summary>
    private const int MaxFilesPerCapture = 64;

    // ------------------------------------------------------------------ pure rules

    /// <summary>
    /// Whether this exit is a new match rather than the second report of one already captured.
    /// </summary>
    internal static bool ShouldCapture(
        IReadOnlyList<DeckSnapshotEntry> entries, string modId, DateTime nowUtc)
    {
        foreach (var entry in entries)
        {
            if (!string.Equals(entry.ModId, modId, StringComparison.OrdinalIgnoreCase)) continue;
            if (Math.Abs((nowUtc - entry.CapturedUtc).TotalSeconds) < DebounceSeconds) return false;
        }
        return true;
    }

    /// <summary>
    /// The snapshot belonging to a match that ended at <paramref name="matchEndedUtc"/>, or null
    /// when none is close enough to be that match.
    /// </summary>
    internal static DeckSnapshotEntry? NearestTo(
        IReadOnlyList<DeckSnapshotEntry> entries, string modId, DateTime matchEndedUtc)
    {
        DeckSnapshotEntry? best = null;
        var bestGap = TimeSpan.MaxValue;

        foreach (var entry in entries)
        {
            if (!string.Equals(entry.ModId, modId, StringComparison.OrdinalIgnoreCase)) continue;

            var gap = (entry.CapturedUtc - matchEndedUtc).Duration();
            if (gap > MatchWindow || gap >= bestGap) continue;

            best = entry;
            bestGap = gap;
        }

        return best;
    }

    /// <summary>The newest <paramref name="keep"/> entries, newest first.</summary>
    internal static IReadOnlyList<DeckSnapshotEntry> Trim(
        IReadOnlyList<DeckSnapshotEntry> entries, int keep) =>
        entries.OrderByDescending(e => e.CapturedUtc).Take(Math.Max(0, keep)).ToList();

    /// <summary>
    /// The stored files no surviving entry points at. Content addressing means one blob can be
    /// shared by many matches, so a blob is only dead once the LAST of them is gone.
    /// </summary>
    internal static IReadOnlyList<string> UnreferencedBlobs(
        IEnumerable<string> blobIds, IReadOnlyList<DeckSnapshotEntry> entries)
    {
        var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
            foreach (var id in entry.Files.Values)
                live.Add(id);

        return blobIds.Where(id => !live.Contains(id)).ToList();
    }

    /// <summary>Twelve hex characters of the content's SHA-256 — plenty for a few dozen files.</summary>
    internal static string BlobIdFor(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).Substring(0, 12).ToLowerInvariant();

    // ------------------------------------------------------------------ disk

    private static string Root => Path.Combine(AppPaths.DataDir, FolderName);

    /// <summary>
    /// Stores the player's home city files as they are now, under this match. Best-effort
    /// throughout: this runs while a game is closing, and losing a snapshot must never cost
    /// anything else.
    /// </summary>
    public static void Capture(string? userDataDir, string? modId, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(userDataDir) || string.IsNullOrWhiteSpace(modId)) return;

        try
        {
            var savegame = Path.Combine(userDataDir!, SavegameFolder);
            if (!Directory.Exists(savegame)) return;

            var entries = ReadIndex();
            if (!ShouldCapture(entries, modId!, nowUtc)) return;

            var files = Directory.EnumerateFiles(savegame, FilePattern)
                .Take(MaxFilesPerCapture).ToList();
            if (files.Count == 0) return;

            Directory.CreateDirectory(Root);

            var entry = new DeckSnapshotEntry { ModId = modId!, CapturedUtc = nowUtc };
            foreach (var file in files)
            {
                var content = File.ReadAllBytes(file);
                var id = BlobIdFor(content);
                var blob = Path.Combine(Root, id + ".xml");

                // Written once. A deck that has not changed since the last match costs nothing.
                if (!File.Exists(blob)) File.WriteAllBytes(blob, content);
                entry.Files[Path.GetFileName(file)] = id;
            }

            var kept = Trim(entries.Append(entry).ToList(), MaxEntries);
            WriteIndex(kept);

            foreach (var dead in UnreferencedBlobs(BlobIds(), kept))
            {
                try { File.Delete(Path.Combine(Root, dead + ".xml")); }
                catch { /* best-effort */ }
            }

            DiagnosticLog.Write(
                $"DeckSnapshot: stored {entry.Files.Count} home city file(s) for '{modId}'.");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"DeckSnapshot: could not capture — {ex.Message}");
        }
    }

    /// <summary>
    /// The decks a match was played with, or null when that match happened before snapshots
    /// existed — which is every match already on disk, and is why the caller has to draw itself
    /// without one.
    /// </summary>
    public static IReadOnlyList<HomeCityProfile>? Read(
        string? modId, DateTime matchEndedUtc, string? homeCityFileName)
    {
        if (string.IsNullOrWhiteSpace(modId)) return null;

        try
        {
            var entry = NearestTo(ReadIndex(), modId!, matchEndedUtc);
            if (entry == null) return null;

            // The recording names the city; the snapshot says what was inside it.
            var wanted = (homeCityFileName ?? "").Trim();
            var names = wanted.Length > 0 && entry.Files.ContainsKey(wanted)
                ? new[] { wanted }
                : entry.Files.Keys.ToArray();

            var profiles = new List<HomeCityProfile>();
            foreach (var name in names)
            {
                if (!entry.Files.TryGetValue(name, out var id)) continue;

                var blob = Path.Combine(Root, id + ".xml");
                if (!File.Exists(blob)) continue;

                // No encoding argument: these files are UTF-16 with a BOM and reading them as
                // UTF-8 yields nothing without erroring.
                var parsed = HomeCityDeckService.Parse(
                    Path.GetFileNameWithoutExtension(name), File.ReadAllText(blob));

                if (parsed != null) profiles.Add(parsed);
            }

            return profiles.Count == 0 ? null : profiles;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"DeckSnapshot: could not read — {ex.Message}");
            return null;
        }
    }

    internal static IReadOnlyList<DeckSnapshotEntry> ReadIndex()
    {
        try
        {
            var path = Path.Combine(Root, IndexName);
            if (!File.Exists(path)) return Array.Empty<DeckSnapshotEntry>();

            var entries = JsonSerializer.Deserialize<List<DeckSnapshotEntry>>(File.ReadAllText(path));
            return entries ?? (IReadOnlyList<DeckSnapshotEntry>)Array.Empty<DeckSnapshotEntry>();
        }
        catch (Exception ex)
        {
            // A corrupt index costs the history of snapshots, not the launcher: the next capture
            // starts a fresh one.
            DiagnosticLog.Write($"DeckSnapshot: index unreadable — {ex.Message}");
            return Array.Empty<DeckSnapshotEntry>();
        }
    }

    private static void WriteIndex(IReadOnlyList<DeckSnapshotEntry> entries) =>
        File.WriteAllText(
            Path.Combine(Root, IndexName),
            JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));

    private static IEnumerable<string> BlobIds()
    {
        if (!Directory.Exists(Root)) yield break;

        foreach (var file in Directory.EnumerateFiles(Root, "*.xml"))
            yield return Path.GetFileNameWithoutExtension(file);
    }
}
