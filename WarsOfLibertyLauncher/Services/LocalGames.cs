using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// The player's own games, read from this PC: the recordings in the mod's Savegame folder and
/// the statistics the launcher harvests from games against the AI.
///
/// <para><b>Two screens show these and both read them through here</b> — the mod window's
/// STATISTICS section and the multiplayer Profile's MATCHES section. The second was asked for
/// because the first is hard to find; a copy of the reading code would let the two come to
/// disagree about what the same recording says.</para>
/// </summary>
internal static class LocalGames
{
    /// <summary>
    /// How many recordings are opened. Each one is inflated whole before its header can be read,
    /// so this is a real cost rather than a directory listing.
    /// </summary>
    internal const int MaxRecordingsScanned = 20;

    /// <summary>
    /// The newest recordings that turn out to be games against people, most recent first.
    ///
    /// <para><b>This is not the match history repeated.</b> That list comes from the lobby
    /// backend and a row exists only because the host reported the match, so a skirmish, a LAN
    /// game outside a room, or a match whose host closed the launcher is absent from it. These
    /// files are the only record of those.</para>
    ///
    /// <para>Runs off the UI thread and is bounded: opening one of these means inflating the
    /// whole file, and a player who records everything can have a folder full of them.</para>
    /// </summary>
    internal static List<LocalMatchRow> ReadHumanMatches(
        string userDataDir, string? myName, string? installPath, string? modId)
    {
        var rows = new List<LocalMatchRow>();

        var dir = Path.Combine(userDataDir, "Savegame");
        if (!Directory.Exists(dir)) dir = userDataDir;
        if (!Directory.Exists(dir)) return rows;

        var files = new DirectoryInfo(dir)
            .EnumerateFiles("*.age3?rec", SearchOption.TopDirectoryOnly)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Take(MaxRecordingsScanned)
            .ToList();

        foreach (var file in files)
        {
            try
            {
                var raw = File.ReadAllBytes(file.FullName);
                var data = ReplayParserService.TryReadContainer(raw);
                if (data == null) continue;

                var header = ReplayParserService.ParseHeader(data);
                if (!LocalMatchView.IsHumanMatch(header)) continue;

                var slot = ReplayParserService.FindPlayerSlot(header, myName ?? "");
                var outcome = ReplayParserService.ReadOutcome(data, header);

                // The same rule the match report trusts: a result only in a clean two-human
                // 1v1, and nothing at all otherwise. Never a draw — "not known" and "drawn" are
                // different things and only one of them is ever true here.
                var result = ReplayParserService.HostResultFrom(outcome, slot);

                var civs = new Dictionary<int, string>();
                foreach (var player in header!.Players)
                {
                    if (civs.ContainsKey(player.Civilization)) continue;
                    var name = CivNameResolver.Resolve(installPath, player.Civilization);
                    if (!string.IsNullOrWhiteSpace(name)) civs[player.Civilization] = name!;
                }

                // What the viewer's own decks held when this match ended, if the launcher was
                // there to keep a copy. Null for every match played before snapshots existed,
                // which is what the card has to draw itself without.
                var mine = slot >= 0
                    ? header.Players.FirstOrDefault(p => p.Slot == slot)?.HomeCityFile
                    : null;
                var decks = DeckSnapshotStore.Read(modId, file.LastWriteTimeUtc, mine);

                rows.Add(new LocalMatchRow(
                    FileName: Path.GetFileNameWithoutExtension(file.Name),
                    Decks: decks,
                    PlayedLocal: file.LastWriteTime,
                    Map: LocalMatchView.PrettyMap(header.MapName),
                    Players: header.Players,
                    LocalSlot: slot,
                    Result: result,
                    // Who lost is MEASURED and stands on its own; who won is DERIVED, and only
                    // in a clean two-human 1v1. Kept apart so the card can say the first
                    // without implying the second.
                    LoserSlot: outcome.LoserSlot,
                    WinnerSlot: outcome.Confidence
                        == ReplayParserService.ReplayOutcomeConfidence.Confident
                            ? outcome.WinnerSlot
                            : -1,
                    Civs: civs));
            }
            catch (Exception ex)
            {
                // One unreadable recording costs one row. They are written by the game while it
                // exits, so a truncated file is a normal thing to find.
                DiagnosticLog.Write($"Local games: could not read '{file.Name}' — {ex.Message}");
            }
        }

        return rows;
    }

    /// <summary>
    /// The stored games against the AI for one mod, and the display names of every unit they
    /// used.
    ///
    /// <para><b>The name resolution runs off the UI thread</b> — it streams every proto file the
    /// mod ships, 12 MB in Wars of Liberty. A mod whose proto files cannot be read still gets
    /// its games, under the internal names, which identify the unit to anyone who mods.</para>
    /// </summary>
    internal static async Task<AiGamesData> LoadAiGamesAsync(
        string? modId, string? installPath, string? gameExecutable, string? userDataFolder = null)
    {
        // Newest first: the store keeps them in harvest order, and a list of thirty games whose
        // top card is a month old reads as if nothing had been played since.
        var games = AiGameStatsStore.Load()
            .Where(g => string.Equals(g.ModId, modId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(g => g.CapturedAtUtc, StringComparer.Ordinal)
            .ToList();

        var empty = new AiGamesData(games, new Dictionary<string, string>(),
            new Dictionary<string, AiOpponent>(StringComparer.OrdinalIgnoreCase));
        if (games.Count == 0) return empty;

        // Every proto any of these games used, resolved in one pass rather than one per card.
        var protoNames = games.SelectMany(g => g.Units.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var personalities = games.Select(g => g.Personality)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        try
        {
            return await Task.Run(() =>
            {
                var names = ProtoNameResolver.Resolve(installPath, gameExecutable, protoNames);
                var opponents = ResolveOpponents(installPath, userDataFolder, personalities);
                return new AiGamesData(games, names, opponents);
            });
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Local games: unit names unavailable — {ex.Message}");
            return empty;
        }
    }

    /// <summary>
    /// Who each AI is — name, portrait, and its civilization with the flag — from its own
    /// personality file, resolved against the mod's string table and art. Every part is
    /// optional: a file that is gone, a name id the table does not carry, a picture the mod does
    /// not ship each drop only their own part of the line.
    /// </summary>
    private static IReadOnlyDictionary<string, AiOpponent> ResolveOpponents(
        string? installPath, string? userDataFolder, IReadOnlyList<string> personalities)
    {
        var result = new Dictionary<string, AiOpponent>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(userDataFolder) || personalities.Count == 0) return result;

        var identities = personalities
            .Select(p => (Personality: p, Identity: AiGameStats.ReadIdentity(userDataFolder, p)))
            .Where(x => x.Identity != null)
            .ToList();
        if (identities.Count == 0) return result;

        var ids = new HashSet<int>(identities.Where(x => x.Identity!.NameId.HasValue)
            .Select(x => x.Identity!.NameId!.Value));
        var names = string.IsNullOrWhiteSpace(installPath) || ids.Count == 0
            ? new Dictionary<int, string>()
            : ModStringTable.Resolve(installPath!, ids);

        // The civ as the player saw it, and its flag — from the same playable list the deck pills
        // use, so a native ally sharing a name never lends its painting.
        var playable = Multiplayer.CivNameResolver.ResolvePlayableCivs(installPath)
            .ToDictionary(c => c.InternalName, StringComparer.OrdinalIgnoreCase);

        var artPaths = new List<string?>();
        foreach (var (_, identity) in identities)
        {
            artPaths.Add(identity!.Icon);
            if (identity.ForcedCiv != null && playable.TryGetValue(identity.ForcedCiv, out var civ))
                artPaths.Add(civ.Art);
        }
        var art = CardArtService.Load(installPath, artPaths);

        foreach (var (personality, identity) in identities)
        {
            string? name = identity!.NameId.HasValue && names.TryGetValue(identity.NameId.Value, out var n)
                ? GameText.Clean(n) : null;
            ImageSource? portrait = identity.Icon != null && art.TryGetValue(identity.Icon, out var p) ? p : null;

            string? civName = null;
            ImageSource? flag = null;
            if (identity.ForcedCiv != null && playable.TryGetValue(identity.ForcedCiv, out var civ))
            {
                civName = civ.DisplayName ?? civ.InternalName;
                if (civ.Art != null && art.TryGetValue(civ.Art, out var f)) flag = f;
            }

            result[personality] = new AiOpponent(name, portrait, civName, flag);
        }
        return result;
    }

    /// <summary>
    /// Card names, descriptions and pictures for a saved deck. Off the UI thread: it streams the
    /// mod's tech files, 12 MB in Wars of Liberty.
    /// </summary>
    internal static async Task<(
        IReadOnlyDictionary<string, CardDetail> Details,
        IReadOnlyDictionary<string, ImageSource> Icons)>
        ResolveDeckArtAsync(string? installPath, string? gameExecutable, IReadOnlyList<HomeCityProfile> decks)
    {
        try
        {
            return await Task.Run(() =>
            {
                var names = decks.SelectMany(p => p.Decks).SelectMany(d => d.Cards)
                    .Select(c => c.InternalName)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                var resolved = CardNameResolver.ResolveDetails(installPath, gameExecutable, names);
                var art = CardArtService.Load(
                    installPath, resolved.Values.Select(d => d.IconPath));

                return (resolved, art);
            });
        }
        catch (Exception ex)
        {
            // The deck still draws, under the internal names and without pictures.
            DiagnosticLog.Write($"Local games: saved deck art unavailable — {ex.Message}");
            return (new Dictionary<string, CardDetail>(),
                    new Dictionary<string, ImageSource>());
        }
    }

    /// <summary>A game shorter than this is folded away rather than given a card of its own.</summary>
    internal const long ShortGameMs = 2 * 60_000;

    /// <summary>
    /// How many games there are, won and lost. A game whose block carried no result counts in
    /// the total and in neither side — never as a loss by default.
    /// </summary>
    internal static (int Total, int Won, int Lost) SummarizeAi(IReadOnlyList<AiGameRecord> games)
        => (games.Count, games.Count(g => g.Won == true), games.Count(g => g.Won == false));

    /// <summary>
    /// The units a card lists, biggest first: one entry per NAME the player would recognise,
    /// with the engine's own leftovers taken out.
    ///
    /// <list type="bullet">
    /// <item><b>Merged by display name</b>, because two protos can carry one name — a real card
    /// said "Ancient Ruins x1 · Ancient Ruins x1".</item>
    /// <item><b><c>RT_*</c> protos are dropped</b>: they are props the random-map script places
    /// (bones, markers), not anything the player trained or built.</item>
    /// <item><b>An unresolved name is kept and FLAGGED</b>, not hidden: it identifies the unit to
    /// anyone who mods, and it is usually the sign of a game played with another mod's exe — the
    /// card draws it dimmed so it does not read as this mod's vocabulary.</item>
    /// </list>
    /// </summary>
    internal static IReadOnlyList<(string Text, int Count, bool Resolved)> UnitLines(
        AiGameRecord game, IReadOnlyDictionary<string, string> names, int max)
    {
        return game.Units
            .Where(u => u.Value > 0 && !u.Key.StartsWith("RT_", StringComparison.OrdinalIgnoreCase))
            .Select(u => names.TryGetValue(u.Key, out var pretty) && !string.IsNullOrWhiteSpace(pretty)
                ? (Text: pretty, Count: u.Value, Resolved: true)
                : (Text: u.Key, Count: u.Value, Resolved: false))
            .GroupBy(u => (u.Text.ToLowerInvariant(), u.Resolved))
            .Select(g => (Text: g.First().Text, Count: g.Sum(x => x.Count), Resolved: g.Key.Item2))
            .OrderByDescending(u => u.Count)
            .ThenBy(u => u.Text, StringComparer.Ordinal)
            .Take(max)
            .ToList();
    }
}

/// <summary>The games against the AI for one mod, and what is needed to describe them.</summary>
internal sealed record AiGamesData(
    IReadOnlyList<AiGameRecord> Games,
    IReadOnlyDictionary<string, string> Names,
    IReadOnlyDictionary<string, AiOpponent> Opponents);

/// <summary>
/// The AI a game was played against, as the player would name it. Every part is optional —
/// see <c>LocalGames.ResolveOpponents</c>.
/// </summary>
internal sealed record AiOpponent(string? Name, ImageSource? Portrait, string? CivName, ImageSource? CivFlag);

/// <summary>One local recording, reduced to what can honestly be said about it.</summary>
internal sealed record LocalMatchRow(
    string FileName,
    DateTime PlayedLocal,
    /// <summary>The decks the viewer brought, as they were that day, when a snapshot exists.</summary>
    IReadOnlyList<HomeCityProfile>? Decks,
    string Map,
    IReadOnlyList<ReplayParserService.ReplayPlayer> Players,
    int LocalSlot,
    double? Result,
    int LoserSlot,
    int WinnerSlot,
    IReadOnlyDictionary<int, string> Civs);
