using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Which match recordings this player has already downloaded, and where they landed — so a row's
/// button reads "already in your Savegame folder" and a second click shows the file instead of
/// downloading a copy (handoff 63).
///
/// <para>Kept in <c>AppPaths.DataDir\replay-downloads.json</c>, never in the config: it is a
/// cache of facts about files, and a lost or corrupt one costs nothing but a re-download.
/// <b>A file that is no longer on disk does not count</b> — the player may have deleted or
/// renamed it, and then the button goes back to "download". Capped at <see cref="MaxEntries"/>,
/// newest kept.</para>
/// </summary>
public sealed class ReplayDownloadIndex
{
    /// <summary>The most entries kept; the oldest go first.</summary>
    public const int MaxEntries = 500;

    private readonly string _file;
    private readonly Func<string, bool> _fileExists;
    private readonly object _gate = new();
    private List<Entry>? _entries;

    public ReplayDownloadIndex(string file, Func<string, bool>? fileExists = null)
    {
        _file = file;
        _fileExists = fileExists ?? File.Exists;
    }

    /// <summary>The one the launcher uses.</summary>
    public static ReplayDownloadIndex Default { get; } =
        new(Path.Combine(AppPaths.DataDir, "replay-downloads.json"));

    /// <summary>The path the recording of <paramref name="matchId"/> was saved to, while it still exists.</summary>
    public bool TryGetDownloaded(string matchId, out string path)
    {
        path = "";
        if (string.IsNullOrEmpty(matchId)) return false;
        lock (_gate)
        {
            var hit = Load().LastOrDefault(e => string.Equals(e.MatchId, matchId, StringComparison.Ordinal));
            if (hit == null || string.IsNullOrEmpty(hit.Path) || !_fileExists(hit.Path)) return false;
            path = hit.Path;
            return true;
        }
    }

    /// <summary>Remember where a recording was saved. Best-effort: a write that fails only costs a re-download.</summary>
    public void Record(string matchId, string path)
    {
        if (string.IsNullOrEmpty(matchId) || string.IsNullOrEmpty(path)) return;
        lock (_gate)
        {
            var entries = Load();
            entries.RemoveAll(e => string.Equals(e.MatchId, matchId, StringComparison.Ordinal));
            entries.Add(new Entry { MatchId = matchId, Path = path, SavedAt = DateTime.UtcNow });
            Trim(entries);
            Save(entries);
        }
    }

    /// <summary>Drop the entries past <see cref="MaxEntries"/>, oldest first.</summary>
    internal static void Trim(List<Entry> entries)
    {
        if (entries.Count <= MaxEntries) return;
        var keep = entries.OrderByDescending(e => e.SavedAt).Take(MaxEntries).ToHashSet();
        entries.RemoveAll(e => !keep.Contains(e));
    }

    /// <summary>The entries in a saved file; an empty list for a missing or unreadable one.</summary>
    internal static List<Entry> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<Entry>();
        try
        {
            var list = JsonSerializer.Deserialize<List<Entry>>(json);
            return list?.Where(e => !string.IsNullOrEmpty(e.MatchId) && !string.IsNullOrEmpty(e.Path)).ToList()
                   ?? new List<Entry>();
        }
        catch (JsonException)
        {
            return new List<Entry>();
        }
    }

    private List<Entry> Load()
    {
        if (_entries != null) return _entries;
        try
        {
            _entries = File.Exists(_file) ? Parse(File.ReadAllText(_file)) : new List<Entry>();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"ReplayDownloadIndex: could not read '{_file}' - {ex.Message}");
            _entries = new List<Entry>();
        }
        return _entries;
    }

    private void Save(List<Entry> entries)
    {
        try
        {
            var dir = Path.GetDirectoryName(_file);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = _file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(entries));
            File.Move(tmp, _file, overwrite: true);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"ReplayDownloadIndex: could not save '{_file}' - {ex.Message}");
        }
    }

    internal sealed class Entry
    {
        [JsonPropertyName("match_id")]
        public string MatchId { get; set; } = "";

        [JsonPropertyName("path")]
        public string Path { get; set; } = "";

        [JsonPropertyName("saved_at")]
        public DateTime SavedAt { get; set; }
    }
}
