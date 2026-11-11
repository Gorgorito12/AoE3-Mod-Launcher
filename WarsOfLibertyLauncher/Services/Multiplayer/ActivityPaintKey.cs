using System;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Everything outside the payload that the community block draws from.
/// </summary>
/// <param name="Error">The fetch's failure state, as a number (the enum is the tab's own).</param>
/// <param name="FallbackIds">The viewer's own matches, by id, on a backend with no community list.</param>
/// <param name="ArtStamp">Which flag tables are loaded: the flags arrive after the rows do, and a
/// table learned by ANOTHER page would otherwise never reach rows already drawn without them.</param>
/// <param name="Fluid">The block's sizes, which the rows carry as local values.</param>
/// <param name="Catalog">Which mod list the names were resolved against (<c>ModRegistry.Generation</c>):
/// a match row names its mod, and a mod the catalog only knows once it has loaded would otherwise
/// stay nameless in rows already drawn.</param>
public readonly record struct ActivityPaintContext(
    int Error, string? FallbackIds, string? UserId, TimeSpan UtcOffset, DateTime LocalDate,
    string Language, double TextScale, bool ShowPreviousMonth, string ArtStamp, string Fluid,
    int Catalog = 0);

/// <summary>
/// What the Rooms page's community block shows, as one comparable string, so the minute-by-minute
/// fetch repaints it only when that changed.
///
/// <para>It repainted on every answer: six two-line rows with their flags, five ladder rows with
/// animated top-three badges, 24 peak bars and the facts, once a minute, for a payload that is
/// almost never different. It is also never BYTE-identical — <c>generated_at</c> is stamped per
/// response and every rating deviation (<c>rd</c>) decays between two answers — and neither is
/// drawn, so both are left out. Everything else in the payload is in, which is the safe
/// direction: a field nobody draws costs a repaint, a drawn field left out costs a stale block.
/// </para>
///
/// <para>Null when the key cannot be built; the caller then paints.</para>
/// </summary>
public static class ActivityPaintKey
{
    private static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { DropUndrawn } },
    };

    /// <summary>The JSON names that change between two answers and are drawn nowhere.</summary>
    private static void DropUndrawn(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object) return;
        for (var i = info.Properties.Count - 1; i >= 0; i--)
            if (info.Properties[i].Name is "generated_at" or "rd")
                info.Properties.RemoveAt(i);
    }

    /// <summary>The payload alone, without what is drawn nowhere: the Ranking page compares
    /// answers by this.</summary>
    public static string? Content(CommunityStats? stats) => ContentOf(stats);

    public static string? For(CommunityStats? stats, ActivityPaintContext context)
        => ContentOf(stats) is { } content ? content + "\n" + context : null;

    /// <summary>
    /// Any payload's content (without <c>generated_at</c> and <c>rd</c>), serialized ONCE per
    /// object: the Rooms block, the Ranking page and the Statistics page each compared the same
    /// answer and serialized it again on the UI thread every time.
    /// </summary>
    public static string? ContentOf(object? value)
    {
        if (value == null) return "null";
        if (s_content.TryGetValue(value, out var known)) return known;
        try
        {
            var content = JsonSerializer.Serialize(value, value.GetType(), Options);
            s_content.AddOrUpdate(value, content);
            return content;
        }
        catch
        {
            return null;
        }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, string> s_content = new();

    /// <summary>
    /// The answer that just arrived, or the one already held when the two say the same thing.
    /// Keeping the old object is what lets every "nothing changed" check downstream - which
    /// compare by reference - see that nothing changed: an identical answer used to rebuild a
    /// whole page because it was a new object.
    /// </summary>
    public static T? KeepIfSame<T>(T? held, T? fresh) where T : class
    {
        if (held == null || fresh == null) return fresh;
        var a = ContentOf(held);
        return a != null && string.Equals(a, ContentOf(fresh), StringComparison.Ordinal) ? held : fresh;
    }
}
