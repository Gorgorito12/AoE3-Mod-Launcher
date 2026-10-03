using System;
using WarsOfLibertyLauncher.Localization;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// The short date the rating screens print ("Máximo: 1720 · 12 ago", "Se cortó el 18 sep"):
/// day and abbreviated month, plus the year only when it is not the current one ("12 ago 2025").
///
/// <para>Month names are spelled out here rather than taken from .NET's <c>MMM</c>: the Spanish
/// culture writes "ago." with a trailing dot, which the design does not. Always in the LAUNCHER's
/// language (<see cref="Strings.Language"/>), never Windows'.</para>
/// </summary>
public static class ShortDate
{
    private static readonly string[] Es =
        { "ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic" };
    private static readonly string[] En =
        { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

    public static string Format(DateTimeOffset when, DateTimeOffset now, string language)
    {
        var local = when.ToLocalTime();
        var es = language == Strings.LangEs;
        var month = (es ? Es : En)[local.Month - 1];
        var sameYear = local.Year == now.ToLocalTime().Year;
        if (es) return sameYear ? $"{local.Day} {month}" : $"{local.Day} {month} {local.Year}";
        return sameYear ? $"{month} {local.Day}" : $"{month} {local.Day}, {local.Year}";
    }

    public static string Format(DateTimeOffset when) => Format(when, DateTimeOffset.Now, Strings.Language);

    /// <summary>Parses the server's ISO timestamps; null when absent or unreadable.</summary>
    public static DateTimeOffset? Parse(string? iso)
        => DateTimeOffset.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : null;
}
