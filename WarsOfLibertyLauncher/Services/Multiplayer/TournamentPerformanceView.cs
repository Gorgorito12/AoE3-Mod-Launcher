using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>What an entrant's tile says about his run: "Perf. 1712" and "Best win: Gorgo (1688)".</summary>
/// <param name="Figure">"1712", "1880+" (capped above), "1120−" (capped below); null under two games.</param>
/// <param name="BestWin">"Gorgo (1688)"; null without a counted win.</param>
/// <param name="HiddenWhy">The viewer's OWN entry when a rival's rating keeps it hidden; null otherwise.</param>
public sealed record TournamentPerformanceLine(string? Figure, string? BestWin, string? HiddenWhy);

/// <summary>
/// Tournament performance (2026-11-18), from the server's <c>performance</c> on each entrant
/// (src/tournaments/performanceDetail.ts) — never computed here. Hidden entries say why on the
/// viewer's own tile only; other tiles say nothing. Pure; pinned by <c>TournamentPerformanceViewTests</c>.
/// </summary>
public static class TournamentPerformanceView
{
    public static TournamentPerformanceLine? For(EntrantPerformance? p, bool mine)
    {
        if (p == null) return null;
        if (!string.IsNullOrEmpty(p.Hidden))
            return mine
                ? new TournamentPerformanceLine(null, null,
                    Strings.Get(p.Hidden == "private" ? "TpPerfHiddenPrivate" : "TpPerfHiddenPlacement"))
                : null;
        var figure = p.Rating is int r
            ? r + (p.Capped == "above" ? "+" : p.Capped == "below" ? "−" : "")
            : null;
        var best = p.BestWin is { } b && !string.IsNullOrEmpty(b.Name) ? $"{b.Name} ({b.Rating})" : null;
        return figure == null && best == null ? null : new TournamentPerformanceLine(figure, best, null);
    }
}
