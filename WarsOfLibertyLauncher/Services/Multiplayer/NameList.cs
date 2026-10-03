using System.Collections.Generic;
using System.Linq;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// A list of names written the way a person would say it: "Ana y Luis", "Ana, Luis y Marta" —
/// "Ana and Luis", "Ana, Luis and Marta". For the team reminders (the countdown, the "teams
/// didn't match" card) and "falta que Ana y Luis elijan equipo".
/// </summary>
public static class NameList
{
    public static string Join(IEnumerable<string> names, string language)
    {
        var list = names.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        var and = language == Localization.Strings.LangEs ? "y" : "and";
        return list.Count switch
        {
            0 => "",
            1 => list[0],
            _ => $"{string.Join(", ", list.Take(list.Count - 1))} {and} {list[^1]}",
        };
    }

    /// <summary>In the launcher's current language.</summary>
    public static string Join(IEnumerable<string> names)
        => Join(names, Localization.Strings.Language);
}
