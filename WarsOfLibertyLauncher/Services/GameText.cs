using System.Text.RegularExpressions;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Turns a raw string-table entry into something worth putting on screen.
///
/// <para>The game's own tables carry display markup: colour spans, inline icon references, and
/// line breaks written as the two characters <c>\</c> and <c>n</c> rather than as a newline.
/// Printed verbatim they are visible junk — measured on one real deck, 2 of its 23 card
/// descriptions carry a colour span.</para>
///
/// <para><b>Deliberately conservative.</b> Stripping <c>&lt;[^&gt;]+&gt;</c> in general would eat
/// a legitimate <c>&lt;</c> and everything after it up to the next one, and these strings are
/// written by modders. Only the three forms the game actually uses are removed.</para>
/// </summary>
public static class GameText
{
    /// <summary>
    /// A colour span. The values are floats 0-1 separated by commas — <c>&lt;color=0.74, 0.25,
    /// 0.11&gt;</c> — not hex and not 0-255, so nothing here tries to honour them as a brush.
    /// </summary>
    private static readonly Regex ColourTag =
        new(@"</?color\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>An inline icon: <c>&lt;icon="(32)(ui/ingame/resource_population)"&gt;</c>.</summary>
    private static readonly Regex IconTag =
        new(@"<icon\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Markup out, real line breaks in. Null or blank comes back as an empty string.</summary>
    public static string Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var text = raw.Replace("\\n", "\n");
        text = ColourTag.Replace(text, "");
        text = IconTag.Replace(text, "");
        return text.Trim();
    }
}
