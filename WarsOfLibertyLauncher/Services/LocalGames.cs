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
    internal static async Task<(IReadOnlyList<AiGameRecord> Games, IReadOnlyDictionary<string, string> Names)>
        LoadAiGamesAsync(string? modId, string? installPath, string? gameExecutable)
    {
        var games = AiGameStatsStore.Load()
            .Where(g => string.Equals(g.ModId, modId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (games.Count == 0)
            return (games, new Dictionary<string, string>());

        // Every proto any of these games used, resolved in one pass rather than one per card.
        var protoNames = games.SelectMany(g => g.Units.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        try
        {
            var names = await Task.Run(
                () => ProtoNameResolver.Resolve(installPath, gameExecutable, protoNames));
            return (games, names);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Local games: unit names unavailable — {ex.Message}");
            return (games, new Dictionary<string, string>());
        }
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
}

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
