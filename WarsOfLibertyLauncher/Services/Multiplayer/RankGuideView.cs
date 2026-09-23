namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>What the next step up is, for the guide's second line.</summary>
public enum RankGuideStepKind
{
    /// <summary>Nothing to say: the viewer's place is unknown.</summary>
    Unknown,
    /// <summary>Not on the ladder yet: play a first competitive match.</summary>
    PlayFirst,
    /// <summary>Already Sovereign: the only thing left is to keep it.</summary>
    Defend,
    /// <summary>Overtake <see cref="RankGuideStep.PlacesToPass"/> players to reach the next age.</summary>
    Pass,
}

/// <param name="PlacesToPass">How many players stand between the viewer and the next age.</param>
/// <param name="NextAge">The age reached by passing them.</param>
/// <param name="TargetName">Who holds the last place of that age now, when the ladder page names them.</param>
/// <param name="TargetPosition">That place.</param>
public sealed record RankGuideStep(
    RankGuideStepKind Kind, int PlacesToPass, RankAge? NextAge, string? TargetName, int TargetPosition);

/// <param name="From">First place of the age, 1-based. 0 for Discovery (not a place).</param>
/// <param name="To">Last place, or 0 when the ladder is too small to reach this age.</param>
/// <param name="Holders">Who holds it now, in ladder order — as far as the loaded page names them.</param>
/// <param name="Count">How many players hold it. For Discovery, unknown: 0.</param>
/// <param name="IsMine">The viewer's own age.</param>
public sealed record RankGuideAge(RankAge Age, int From, int To, IReadOnlyList<string> Holders, int Count, bool IsMine);

/// <summary>
/// The guide's content (docs/design_insignias_pantallas_guia, 46a), with no WPF in it: the
/// viewer's age and place, the next step, and every age with its places and who holds it.
///
/// <para><b>One rule, shared with every badge.</b> The places each age spans come from
/// <see cref="RankAges.BoundsFor"/> — the same share-of-the-ladder cut that paints the badges —
/// so the guide cannot tell a player they are Imperial while their badge says Industrial.</para>
///
/// <para><b>The next step counts PLACES, not rating points</b>: the age comes from the place,
/// so "pass one player" is the true distance, and the rating it would take is not something the
/// launcher can know.</para>
/// </summary>
public sealed class RankGuideView
{
    public required RankAge? MyAge { get; init; }
    public required int? MyPosition { get; init; }
    public required int LadderSize { get; init; }
    public required RankGuideStep Next { get; init; }
    public required IReadOnlyList<RankGuideAge> Ages { get; init; }

    /// <param name="myPosition">The viewer's place as the server numbered it; 0 = not on the
    /// ladder; null = unknown.</param>
    /// <param name="ladderSize">How many are on the ladder; 0 or less = unknown.</param>
    /// <param name="namesByPosition">The loaded ladder page: place → name. May be partial.</param>
    public static RankGuideView Build(int? myPosition, int ladderSize, IReadOnlyDictionary<int, string> namesByPosition)
    {
        var n = ladderSize > 0 ? ladderSize : namesByPosition.Count;
        var bounds = RankAges.BoundsFor(n > 0 ? n : null);
        var myAge = RankAges.ForOptional(myPosition, n > 0 ? n : null);

        var ages = new List<RankGuideAge>();
        var ladderAges = new[] { RankAge.Sovereign, RankAge.Imperial, RankAge.Industrial, RankAge.Fortress, RankAge.Colonial };
        var from = 1;
        for (var i = 0; i < ladderAges.Length; i++)
        {
            // Colonial is everybody after the last cut; with the ladder size unknown it has no
            // end, so it is reported as open-ended (To = 0 is "and below").
            var lastCut = i < bounds.Length ? bounds[i] : int.MaxValue;
            var to = n > 0 ? Math.Min(lastCut, n) : (i < bounds.Length ? lastCut : 0);
            var count = n > 0 ? Math.Max(0, to - from + 1) : 0;
            var holders = new List<string>();
            var end = to > 0 ? to : (n > 0 ? n : from + namesByPosition.Count);
            for (var p = from; p <= end; p++)
                if (namesByPosition.TryGetValue(p, out var name)) holders.Add(name);
            ages.Add(new RankGuideAge(ladderAges[i], from, to >= from ? to : 0, holders, count, myAge == ladderAges[i]));
            if (i < bounds.Length) from = bounds[i] + 1;
        }
        ages.Add(new RankGuideAge(RankAge.Discovery, 0, 0, Array.Empty<string>(), 0, myAge == RankAge.Discovery));

        return new RankGuideView
        {
            MyAge = myAge,
            MyPosition = myPosition,
            LadderSize = n,
            Next = NextStep(myPosition, myAge, bounds, namesByPosition),
            Ages = ages,
        };
    }

    private static RankGuideStep NextStep(
        int? position, RankAge? age, int[] bounds, IReadOnlyDictionary<int, string> names)
    {
        if (position is not { } p || age is not { } a) return new(RankGuideStepKind.Unknown, 0, null, null, 0);
        if (a == RankAge.Discovery) return new(RankGuideStepKind.PlayFirst, 0, RankAge.Colonial, null, 0);
        if (a == RankAge.Sovereign) return new(RankGuideStepKind.Defend, 0, null, null, 0);

        // The next age up, and the last place that still wears it: that is the place to take.
        var next = a + 1;
        var index = next switch
        {
            RankAge.Sovereign => 0,
            RankAge.Imperial => 1,
            RankAge.Industrial => 2,
            _ => 3,
        };
        var target = bounds[index];
        var toPass = Math.Max(1, p - target);
        names.TryGetValue(target, out var who);
        return new(RankGuideStepKind.Pass, toPass, next, who, target);
    }

    /// <summary>
    /// A place written the way each language writes it: English "1st, 2nd, 3rd, 11th, 21st",
    /// Spanish "1.º". Pure, so it is tested rather than trusted.
    /// </summary>
    public static string Ordinal(int n, string language)
    {
        if (language == Localization.Strings.LangEs) return $"{n}.º";
        var lastTwo = n % 100;
        var suffix = lastTwo is >= 11 and <= 13 ? "th" : (n % 10) switch
        {
            1 => "st",
            2 => "nd",
            3 => "rd",
            _ => "th",
        };
        return n + suffix;
    }
}
