namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Ordinal numbers for the anti-farm texts ("8.ª victoria seguida" / "8th win in a row").
/// Spanish feminine ordinal, because the noun is "victoria"; English with the usual suffixes,
/// including the teens (11th, 12th, 13th), which is where hand-written versions go wrong.
/// </summary>
public static class Ordinals
{
    public static string Spanish(int n) => $"{n}.ª";

    public static string English(int n)
    {
        var mod100 = n % 100;
        if (mod100 is >= 11 and <= 13) return $"{n}th";
        return (n % 10) switch
        {
            1 => $"{n}st",
            2 => $"{n}nd",
            3 => $"{n}rd",
            _ => $"{n}th",
        };
    }

    /// <summary>The ordinal in the given language (<c>es</c> or anything else = English).</summary>
    public static string For(int n, string language)
        => language == Localization.Strings.LangEs ? Spanish(n) : English(n);
}
