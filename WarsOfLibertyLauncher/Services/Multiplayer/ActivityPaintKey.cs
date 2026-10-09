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
public readonly record struct ActivityPaintContext(
    int Error, string? FallbackIds, string? UserId, TimeSpan UtcOffset, DateTime LocalDate,
    string Language, double TextScale, bool ShowPreviousMonth, string ArtStamp, string Fluid);

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

    public static string? For(CommunityStats? stats, ActivityPaintContext context)
    {
        try
        {
            return JsonSerializer.Serialize(stats, Options) + "\n" + context;
        }
        catch
        {
            return null;
        }
    }
}
