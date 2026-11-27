using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WarsOfLibertyLauncher.Models.Multiplayer;

// A player's medals (design 121, `GET /users/:id/achievements`). The SERVER decides every one of
// them: which exist in a game, what each asks, who has earned what and when. The launcher draws
// what arrives, in the order it arrives, and names each medal from its own string table when it
// knows the id — the English name the server sends is the fallback for one it does not.

/// <summary>One game's medals for one player, with his progress toward the missing ones.</summary>
public class AchievementsResponse
{
    [JsonPropertyName("mod")]
    public string Mod { get; set; } = "";

    /// <summary>How many medals the game has: one per achievement, five for Promotion.</summary>
    [JsonPropertyName("total")]
    public int Total { get; set; }

    /// <summary>How many he holds. Absent while hidden.</summary>
    [JsonPropertyName("earned")]
    public int? Earned { get; set; }

    /// <summary>"private": his rating is private and the server shows his medals to nobody else.</summary>
    [JsonPropertyName("hidden")]
    public string? Hidden { get; set; }

    /// <summary>Every game he has anything in, most played first.</summary>
    [JsonPropertyName("mods")]
    public List<AchievementModCount>? Mods { get; set; }

    [JsonPropertyName("groups")]
    public List<AchievementGroup>? Groups { get; set; }

    /// <summary>His three most recent medals in this game.</summary>
    [JsonPropertyName("latest")]
    public List<MedalRef>? Latest { get; set; }

    /// <summary>His own medals the launcher has not celebrated yet, every game. Sent to him only.</summary>
    [JsonPropertyName("unseen")]
    public List<MedalRef>? Unseen { get; set; }

    [JsonIgnore]
    public bool IsHidden => string.Equals(Hidden, "private", System.StringComparison.OrdinalIgnoreCase);
}

public class AchievementModCount
{
    [JsonPropertyName("mod")]
    public string Mod { get; set; } = "";

    [JsonPropertyName("earned")]
    public int Earned { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }
}

public class AchievementGroup
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>The colour its medals are enamelled in, 0-6.</summary>
    [JsonPropertyName("enamel")]
    public int Enamel { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("items")]
    public List<AchievementItem> Items { get; set; } = new();
}

public class AchievementItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("icon")]
    public string Icon { get; set; } = "";

    /// <summary>"b", "s" or "g".</summary>
    [JsonPropertyName("metal")]
    public string Metal { get; set; } = "b";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("condition")]
    public string Condition { get; set; } = "";

    /// <summary>The count it asks for; absent for a yes/no medal.</summary>
    [JsonPropertyName("goal")]
    public int? Goal { get; set; }

    /// <summary>Where he is toward the goal. Never sent to others for a rating medal.</summary>
    [JsonPropertyName("value")]
    public int? Value { get; set; }

    /// <summary>"default" or "team" on the rating medals.</summary>
    [JsonPropertyName("ladder")]
    public string? Ladder { get; set; }

    [JsonPropertyName("earned_at")]
    public string? EarnedAt { get; set; }

    /// <summary>"placement": no rating to show until his placement on that ladder ends.</summary>
    [JsonPropertyName("locked_reason")]
    public string? LockedReason { get; set; }

    /// <summary>Promotion's five ages, bottom to top.</summary>
    [JsonPropertyName("tiers")]
    public List<AchievementTier>? Tiers { get; set; }

    [JsonPropertyName("tier_reached")]
    public int? TierReached { get; set; }
}

public class AchievementTier
{
    [JsonPropertyName("tier")]
    public int Tier { get; set; }

    /// <summary>"colonial", "fortress", "industrial", "imperial" or "sovereign".</summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("earned_at")]
    public string? EarnedAt { get; set; }
}

/// <summary>An earned medal with everything needed to draw it: the latest three, an unseen one, a
/// celebration.</summary>
public class MedalRef
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>0, or 1-5 for Promotion's ages.</summary>
    [JsonPropertyName("tier")]
    public int Tier { get; set; }

    [JsonPropertyName("metal")]
    public string Metal { get; set; } = "b";

    [JsonPropertyName("icon")]
    public string Icon { get; set; } = "";

    [JsonPropertyName("enamel")]
    public int Enamel { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("condition")]
    public string Condition { get; set; } = "";

    [JsonPropertyName("earned_at")]
    public string EarnedAt { get; set; } = "";

    /// <summary>The game, on an unseen medal (the others are all of the response's game).</summary>
    [JsonPropertyName("mod")]
    public string? Mod { get; set; }
}

/// <summary>The push the server sends the moment he earns medals (`achievement_unlocked`).</summary>
public class AchievementUnlockedFrame
{
    [JsonPropertyName("mod")]
    public string Mod { get; set; } = "";

    [JsonPropertyName("items")]
    public List<MedalRef> Items { get; set; } = new();

    [JsonPropertyName("earned")]
    public int Earned { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }
}

public class AchievementsSeenRequest
{
    [JsonPropertyName("items")]
    public List<AchievementSeenItem>? Items { get; set; }
}

public class AchievementSeenItem
{
    [JsonPropertyName("mod")]
    public string Mod { get; set; } = "";

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("tier")]
    public int Tier { get; set; }
}

public class AchievementsSeenResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }
}
