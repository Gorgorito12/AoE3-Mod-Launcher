using System;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Where a click on a player's name, photo or plate goes: the viewer's own profile, somebody
/// else's, or nowhere (no id: an older server, a sample row). Pure, pinned by
/// <c>ProfileLinkRuleTests</c>.
/// </summary>
public static class ProfileLinkRule
{
    public enum Target { None, Own, Other }

    public static Target Decide(string? userId, string? viewerId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return Target.None;
        return string.Equals(userId, viewerId, StringComparison.Ordinal) ? Target.Own : Target.Other;
    }
}
