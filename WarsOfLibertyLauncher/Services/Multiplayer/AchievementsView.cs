using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>What a medal tile is: earned, under way with a count, or locked.</summary>
public enum MedalTileState { Earned, Progress, Locked }

/// <summary>
/// One tile of the Achievements section (design 121a/121c): the medal, its name, the line under it
/// and the pill at its foot.
/// </summary>
/// <param name="Progress">The share of the rim to fill while it is under way, -1 for none.</param>
/// <param name="Age">Promotion's highest age once earned: the rim wears that age's colours.</param>
/// <param name="Pips">Promotion's five ages: each one reached, or null.</param>
public sealed record MedalTile(
    string Id,
    string Name,
    string Line,
    string Tooltip,
    string Icon,
    string Metal,
    int Enamel,
    MedalTileState State,
    double Progress,
    string Foot,
    RankAge? Age,
    IReadOnlyList<RankAge?> Pips);

/// <summary>
/// The rules of the Achievements section, pure. The server says what each medal is and where the
/// player stands; this decides how a tile reads — the design's own rules, written once so the
/// profile, the small notice and the celebration cannot disagree.
/// </summary>
public static class AchievementsView
{
    /// <summary>Promotion's five ages, bottom to top, as the server keys its tiers.</summary>
    public static readonly (string Key, RankAge Age)[] PromotionAges =
    {
        ("colonial", RankAge.Colonial),
        ("fortress", RankAge.Fortress),
        ("industrial", RankAge.Industrial),
        ("imperial", RankAge.Imperial),
        ("sovereign", RankAge.Sovereign),
    };

    /// <summary>Where a rating medal's ring starts filling: every player begins at 1500.</summary>
    public const int RatingBase = 1500;

    /// <summary>The medal's name in the launcher's language, or the server's English for an id this
    /// launcher does not know.</summary>
    public static string NameOf(string id, string serverName)
        => Strings.TryGet("Achv_" + id, out var s) ? s : serverName;

    /// <summary>What the medal asks, the same way.</summary>
    public static string ConditionOf(string id, string serverCondition)
        => Strings.TryGet("AchvCond_" + id, out var s) ? s : serverCondition;

    /// <summary>A group's heading, upper-cased like the design's.</summary>
    public static string GroupTitle(AchievementGroup group)
    {
        string name = Strings.TryGet("AchvGroup_" + group.Id, out var s) ? s : group.Name;
        return name.ToUpper(Strings.Culture);
    }

    /// <summary>
    /// "3/6": achievements of a group held, over how many it has, as the design counts them —
    /// Promotion is ONE achievement here, held from its first age (the total of medals, where it
    /// counts five, is the showcase's).
    /// </summary>
    public static string GroupCount(AchievementGroup group)
    {
        int held = group.Items.Count(item => item.Tiers is { Count: > 0 } tiers
            ? tiers.Any(t => !string.IsNullOrEmpty(t.EarnedAt))
            : !string.IsNullOrEmpty(item.EarnedAt));
        return $"{held}/{group.Items.Count}";
    }

    /// <summary>The share of the game's medals held, for the ring in the showcase.</summary>
    public static double TotalFraction(AchievementsResponse r)
        => r.Total > 0 ? Math.Clamp((r.Earned ?? 0) / (double)r.Total, 0, 1) : 0;

    /// <summary>"Mar 2" in the launcher's language; empty for a date that does not parse.</summary>
    public static string ShortDate(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)
            || !DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
            return "";
        var local = at.ToLocalTime();
        var culture = Strings.Culture;
        string month = culture.DateTimeFormat.GetAbbreviatedMonthName(local.Month).TrimEnd('.');
        if (month.Length > 0) month = char.ToUpper(month[0], culture) + month[1..];
        return $"{month} {local.Day}";
    }

    /// <summary>The age a Promotion tier stands for.</summary>
    public static RankAge? AgeOfTier(int tier)
        => tier >= 1 && tier <= PromotionAges.Length ? PromotionAges[tier - 1].Age : null;

    /// <summary>The age's name in the launcher's language.</summary>
    public static string AgeName(RankAge age) => Strings.Get(RankAges.NameKey(age));

    /// <summary>One tile, as the design draws it.</summary>
    public static MedalTile Tile(AchievementItem item, int enamel)
    {
        string name = NameOf(item.Id, item.Name);
        string condition = ConditionOf(item.Id, item.Condition);

        if (item.Tiers is { Count: > 0 } tiers)
        {
            int reached = tiers.Count(t => !string.IsNullOrEmpty(t.EarnedAt));
            var pips = PromotionAges.Select((a, i) => i < reached ? a.Age : (RankAge?)null).ToList();
            if (reached == 0)
            {
                return new MedalTile(item.Id, name, condition, condition, item.Icon, item.Metal, enamel,
                    MedalTileState.Locked, -1, Strings.Get("AchvLocked"), null, pips);
            }

            var age = PromotionAges[reached - 1].Age;
            string line = reached < PromotionAges.Length
                ? Strings.Format("AchvNextAge", AgeName(PromotionAges[reached].Age))
                : condition;
            var lastDate = tiers.Where(t => !string.IsNullOrEmpty(t.EarnedAt)).Select(t => t.EarnedAt).Max();
            return new MedalTile(item.Id, $"{name} · {AgeName(age)}", line, condition, item.Icon, item.Metal,
                enamel, MedalTileState.Earned, -1, ShortDate(lastDate), age, pips);
        }

        var noPips = Array.Empty<RankAge?>();
        if (!string.IsNullOrEmpty(item.EarnedAt))
        {
            return new MedalTile(item.Id, name, condition, condition, item.Icon, item.Metal, enamel,
                MedalTileState.Earned, -1, ShortDate(item.EarnedAt), null, noPips);
        }

        // Under way: a count the server sent. A rating medal with no rating yet (placement, or a
        // number withheld from others) is locked, not "0 / 1600".
        bool rating = item.Ladder != null;
        if (item.Goal is int goal && goal > 1 && item.Value is int value && (!rating || value > 0))
        {
            double basis = rating ? RatingBase : 0;
            double share = Math.Clamp((value - basis) / (goal - basis), 0, 1);
            return new MedalTile(item.Id, name, condition, condition, item.Icon, item.Metal, enamel,
                MedalTileState.Progress, share, $"{value} / {goal}", null, noPips);
        }

        return new MedalTile(item.Id, name, condition, condition, item.Icon, item.Metal, enamel,
            MedalTileState.Locked, -1, Strings.Get("AchvLocked"), null, noPips);
    }

    /// <summary>
    /// The line a medal is announced with — its condition, or for Promotion the age reached.
    /// Used by the latest three, the small notice and the celebration.
    /// </summary>
    public static (string Name, string Line, RankAge? Age) Announce(MedalRef medal)
    {
        string name = NameOf(medal.Id, medal.Name);
        if (AgeOfTier(medal.Tier) is { } age && medal.Tier > 0)
            return ($"{name} · {AgeName(age)}", Strings.Format("AchvReachAge", AgeName(age)), age);
        return (name, ConditionOf(medal.Id, medal.Condition), null);
    }

    /// <summary>"Gold medal · 14 of 40 in Wars of Liberty": the line under a celebration.</summary>
    public static string MedalLine(MedalRef medal, int earned, int total, string modName)
    {
        string metal = medal.Tier > 0
            ? Strings.Get("AchvMetalAge")
            : Strings.Get(medal.Metal switch
            {
                "p" => "AchvMetalPlatinum", "g" => "AchvMetalGold", "s" => "AchvMetalSilver", _ => "AchvMetalBronze",
            });
        return Strings.Format("AchvCelebrationLine", metal, earned, total, modName);
    }

    /// <summary>The metals, for the legend under the title: design 121's three, and platinum only
    /// when the game has a platinum medal (so the design's sheets keep their three).</summary>
    public static IReadOnlyList<(string Metal, string Label)> Legend(AchievementsResponse? r = null)
    {
        var metals = new List<(string, string)>
        {
            ("b", Strings.Get("AchvMetalBronzeShort")),
            ("s", Strings.Get("AchvMetalSilverShort")),
            ("g", Strings.Get("AchvMetalGoldShort")),
        };
        if (r?.Groups?.Any(g => g.Items.Any(i => i.Metal == "p")) == true)
            metals.Add(("p", Strings.Get("AchvMetalPlatinumShort")));
        return metals;
    }

    /// <summary>"13 of 40 medals".</summary>
    public static string CountLine(AchievementsResponse r)
        => Strings.Format("AchvCountLine", r.Earned ?? 0, r.Total);
}
