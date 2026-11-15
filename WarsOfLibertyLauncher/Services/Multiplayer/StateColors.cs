using System.Windows.Media;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>What a coloured thing on a multiplayer page MEANS (docs/design_14nov).</summary>
internal enum StateTone
{
    /// <summary>Open, won, decided: green.</summary>
    Open,
    /// <summary>In progress, being played, being placed: blue.</summary>
    Live,
    /// <summary>Champion, finished tournament, best mark: gold.</summary>
    Gold,
    /// <summary>Unavailable, absent, did not count: amber.</summary>
    Caution,
    /// <summary>Lost, disqualified: red.</summary>
    Danger,
    /// <summary>Draft, undecided: neutral grey.</summary>
    Neutral,
}

/// <summary>
/// The fixed state colours of the Rooms page, shared by Tournaments, Ranking and Statistics
/// (docs/design_14nov). They never take the mod's colour: the mod's colour is for what is YOURS
/// (your selection, your match, "YOU", the primary button), and a champion drawn in a red mod's
/// colour read as an error. Each has the colour of its mark and the colour of text written in it.
/// The values are the <c>RmGroup*</c> / <c>UiGold*</c> / <c>UiDanger*</c> brushes of
/// <c>Styles/Colors.xaml</c>; <c>StateColorsTests</c> holds them to those.
/// </summary>
internal static class StateColors
{
    internal static readonly Color Green = Color.FromRgb(0x4F, 0xD6, 0x8A), GreenText = Color.FromRgb(0x7F, 0xE0, 0xA8);
    internal static readonly Color Blue = Color.FromRgb(0x55, 0xAC, 0xEE), BlueText = Color.FromRgb(0xA9, 0xD6, 0xF7);
    internal static readonly Color Gold = Color.FromRgb(0xE6, 0xC0, 0x6A), GoldText = Color.FromRgb(0xF3, 0xD9, 0x95);
    internal static readonly Color Amber = Color.FromRgb(0xF4, 0x90, 0x0C), AmberText = Color.FromRgb(0xF5, 0xBD, 0x76);
    internal static readonly Color Red = Color.FromRgb(0xC8, 0x4A, 0x4A), RedText = Color.FromRgb(0xF0, 0xA6, 0xA6);
    internal static readonly Color Neutral = Color.FromRgb(0x8C, 0x9C, 0xB1), NeutralText = Color.FromRgb(0xC3, 0xD2, 0xE5);

    /// <summary>The won square's fill in a bracket (<c>RmJoinBg</c>).</summary>
    internal static readonly Color WonFill = Color.FromRgb(0x1D, 0x4A, 0x37);

    internal static Color Mark(StateTone tone) => tone switch
    {
        StateTone.Open => Green,
        StateTone.Live => Blue,
        StateTone.Gold => Gold,
        StateTone.Caution => Amber,
        StateTone.Danger => Red,
        _ => Neutral,
    };

    internal static Color Text(StateTone tone) => tone switch
    {
        StateTone.Open => GreenText,
        StateTone.Live => BlueText,
        StateTone.Gold => GoldText,
        StateTone.Caution => AmberText,
        StateTone.Danger => RedText,
        _ => NeutralText,
    };

    /// <summary>A tournament's state, the same everywhere it shows (chip, list dot, filter, steps):
    /// registration open is green like an open room, being played is blue like a room in a match,
    /// finished is gold.</summary>
    internal static StateTone ForTournament(string? status) => status switch
    {
        "registration" => StateTone.Open,
        "ready" or "running" => StateTone.Live,
        "finished" => StateTone.Gold,
        "cancelled" => StateTone.Danger,
        _ => StateTone.Neutral,
    };

    /// <summary>Only a tournament being played has the beating dot.</summary>
    internal static bool Pulses(string? status) => status == "running";
}
