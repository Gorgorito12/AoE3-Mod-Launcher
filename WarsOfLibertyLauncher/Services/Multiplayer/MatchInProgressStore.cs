using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// The match the launcher was following when it last ran, persisted so a launcher that DIES
/// mid-match can finish the job when it comes back.
///
/// <para><b>The gap this closes.</b> The game is launched re-parented under explorer.exe, so a
/// launcher crash or a Task Manager kill leaves the game running — the player finishes the
/// match — and the launcher, when reopened, knew nothing about it: <see cref="MatchContext"/>
/// lived in memory only. No recording was read, nothing was reported or confirmed, and since
/// the socket had dropped, a match the opponent's recording could not settle was scored an
/// abandonment against somebody who had played it to the end.</para>
///
/// <para><b>Only for a launcher that died on its own.</b> A DELIBERATE exit mid-match kills
/// the game and clears this file (<see cref="Clear"/>): that exit is a walkout by the
/// maintainer's decision, and there is nothing to resume. What is written here is what the
/// next launch needs to pick the match back up: the frozen context, the game's pid and exe
/// (to re-arm the exit watcher if it is still running), and when it was launched (the
/// recording window). The context comes back exactly as it was captured — the same type, the
/// same immutability, no live room can enter it.</para>
/// </summary>
public static class MatchInProgressStore
{
    public const string FileName = "match-in-progress.json";

    /// <summary>Older than this and the server would refuse the report anyway (MAX_AGE_MS).</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    public static string FilePath => Path.Combine(AppPaths.DataDir, FileName);

    /// <summary>What is written. A DTO rather than the record itself so the file's shape is
    /// owned here and a change to <see cref="MatchContext"/> cannot silently rewrite it.</summary>
    public sealed class Saved
    {
        public bool IsHost { get; set; }
        public List<string> Participants { get; set; } = new();
        public string? LobbyId { get; set; }
        public string? ModId { get; set; }
        public string? ReporterUserId { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public bool IsCompetitive { get; set; }
        public Dictionary<string, string>? InGameNames { get; set; }
        public string Format { get; set; } = RoomFormat.Casual.ToString();
        public string ProfileId { get; set; } = "";
        public int GamePid { get; set; } = -1;
        public string? ExePath { get; set; }
        public DateTime LaunchedAtUtc { get; set; }

        [JsonIgnore]
        public MatchContext Context => new(
            IsHost,
            Participants.AsReadOnly(),
            LobbyId,
            ModId,
            ReporterUserId,
            StartedAtUtc,
            IsCompetitive,
            InGameNames,
            Enum.TryParse<RoomFormat>(Format, out var f) ? f : RoomFormat.Casual);
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static Saved Describe(MatchContext ctx, string profileId, int gamePid, string? exePath, DateTime launchedAtUtc) => new()
    {
        IsHost = ctx.IsHost,
        Participants = ctx.Participants.ToList(),
        LobbyId = ctx.LobbyId,
        ModId = ctx.ModId,
        ReporterUserId = ctx.ReporterUserId,
        StartedAtUtc = ctx.StartedAtUtc,
        IsCompetitive = ctx.IsCompetitive,
        InGameNames = ctx.InGameNames == null ? null : new Dictionary<string, string>(ctx.InGameNames, StringComparer.Ordinal),
        Format = ctx.Format.ToString(),
        ProfileId = profileId,
        GamePid = gamePid,
        ExePath = exePath,
        LaunchedAtUtc = launchedAtUtc,
    };

    /// <summary>Write it. Best-effort: a match must never fail to launch over this file.</summary>
    public static void Save(Saved saved) => Save(saved, FilePath);

    internal static void Save(Saved saved, string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(saved, Options));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"MatchInProgressStore: could not save — {ex.Message}");
        }
    }

    /// <summary>The saved match, or null when there is none or it is unreadable.</summary>
    public static Saved? Load() => Load(FilePath);

    internal static Saved? Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<Saved>(File.ReadAllText(path), Options);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"MatchInProgressStore: unreadable — {ex.Message}");
            return null;
        }
    }

    /// <summary>Forget it: the match was reported, or the exit was deliberate.</summary>
    public static void Clear() => Clear(FilePath);

    internal static void Clear(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { DiagnosticLog.Write($"MatchInProgressStore: could not clear — {ex.Message}"); }
    }

    /// <summary>
    /// Whether a saved match is still worth resuming. Pure, so the two refusals — too old for
    /// the server, and a shape nothing could report — are pinned by a test.
    /// </summary>
    public static bool IsResumable(Saved? saved, DateTime nowUtc)
    {
        if (saved == null) return false;
        if (string.IsNullOrWhiteSpace(saved.ProfileId)) return false;
        if (string.IsNullOrWhiteSpace(saved.LobbyId)) return false;
        if (saved.Participants.Count < 2) return false;
        if (nowUtc - saved.LaunchedAtUtc > MaxAge) return false;
        if (saved.LaunchedAtUtc > nowUtc + TimeSpan.FromMinutes(5)) return false;
        return true;
    }
}
