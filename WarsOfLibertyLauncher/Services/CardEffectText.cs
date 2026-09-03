using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WarsOfLibertyLauncher.Services;

/// <summary>One <c>&lt;Effect&gt;</c> of a tech, as the tech tree writes it.</summary>
/// <param name="Type">
/// <c>Data</c> is the one that describes a change. <c>TextOutput</c> is the chat line printed
/// when the shipment lands ("Trade Empire Shipment has arrived.") — it says nothing about what
/// the card does and is not shown.
/// </param>
/// <param name="AllActions">
/// The effect applies to every action rather than a named one — <c>allactions='1'</c>, which
/// 2,715 of the tech tree's damage effects carry. The templates still want a word in that slot,
/// and the engine has one for it: <c>cStringAllActionsEffect</c>, "All Actions". Without this
/// those effects have an empty action and the line is dropped, which is most damage cards.
/// </param>
public sealed record CardEffect(
    string Type,
    string Subtype,
    string Relativity,
    double Amount,
    string Resource,
    string UnitType,
    string Action,
    string TargetType,
    string TargetName,
    bool AllActions = false);

/// <summary>
/// Turns a card's effects into the sentences with percentages the game itself shows.
///
/// <para><b>The game does not store that text — it builds it.</b> A card carries a short
/// hand-written line in <c>RolloverTextID</c> ("Trading Posts are cheaper and stronger") and
/// the numbers live only in its <c>&lt;Effect&gt;</c> blocks. The engine renders them through
/// printf templates that sit in the mod's OWN string table under a <c>symbol</c> —
/// <c>cStringChangeCostEffect</c> is <c>"%1s: Changes %2s cost by %3.2f%%"</c> — so the launcher
/// can reproduce them exactly rather than inventing wording.</para>
///
/// <para>Reading them by SYMBOL and not by id matters: Wars of Liberty numbers that template
/// 42010 and nothing says another mod must. It also means a mod translated into Spanish yields
/// Spanish lines for free.</para>
///
/// <para><b>Why this fills more than it decorates:</b> measured on a real 35-card deck, 20 cards
/// carry no <c>RolloverTextID</c> at all — every crate and unit shipment — so their description
/// was simply blank. Those are exactly the ones that become "Delivers 8 Chu Ko Nu".</para>
/// </summary>
public static class CardEffectText
{
    /// <summary>The effect type that describes a change. Everything else is ignored.</summary>
    public const string DataEffect = "Data";

    /// <summary>
    /// A card's effects, and one of the two families that name a unit rather than change it.
    /// Both use the same template, which has no target slot at all.
    /// </summary>
    private static readonly HashSet<string> FreeUnitSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "FreeHomeCityUnit",
        "FreeHomeCityUnitIfTechObtainable",
    };

    /// <summary>The label for an effect that applies to every action rather than a named one.</summary>
    public const string AllActionsSymbol = "cStringAllActionsEffect";

    /// <summary>
    /// The engine's name for an abstract unit TYPE — <c>AbstractBuilding</c> is "Buildings" — or
    /// null for anything that is not one.
    ///
    /// <para>These are not units, so <c>proto*.xml</c> does not name them and
    /// <see cref="ProtoNameResolver"/> cannot. The string table has its own family for them, and
    /// even declares a <c>cStringAbstractNameNotFound</c> for the ones it lacks — which are many:
    /// Wars of Liberty defines no name for <c>AbstractVillager</c>. Those keep their internal
    /// name, which still identifies the group to anyone who mods.</para>
    /// </summary>
    public static string? AbstractSymbolFor(string? protoName)
    {
        const string prefix = "Abstract";
        var name = (protoName ?? "").Trim();

        return name.Length > prefix.Length
               && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? "cStringAbstractName" + name.Substring(prefix.Length)
            : null;
    }

    /// <summary>
    /// Which template renders this effect, or null when the engine has none for it.
    ///
    /// <para><b>Built rather than looked up in a list</b>, which is what makes it self-limiting:
    /// a subtype the engine cannot describe — <c>AddTrain</c>, <c>AllowedAge</c>,
    /// <c>PopulationCount</c> and a handful more — simply produces a symbol that is not in the
    /// string table, and the caller drops the line. No list to keep in step with 41 subtypes.</para>
    /// </summary>
    public static string? SymbolFor(string? subtype, string? relativity)
    {
        if (string.IsNullOrWhiteSpace(subtype)) return null;
        if (FreeUnitSubtypes.Contains(subtype!)) return "cStringFreeHomeCityUnitEffect";

        // The one subtype whose template carries no Change/Add/Set verb of its own:
        // "%1s: Adds %2.2f %3s to your inventory".
        if (string.Equals(subtype, "Resource", StringComparison.OrdinalIgnoreCase))
            return "cStringResourceEffect";

        // Exactly four values exist in the whole file.
        var verb = (relativity ?? "").Trim().ToLowerInvariant() switch
        {
            "percent" or "basepercent" => "Change",
            "absolute" => "Add",
            "assign" => "Set",
            _ => null,
        };

        return verb == null ? null : "cString" + verb + subtype!.Trim() + "Effect";
    }

    /// <summary>
    /// The number the percentage templates want. <c>Percent</c> and <c>BasePercent</c> both store
    /// a multiplier, so 0.80 is −20 % and 1.33 is +33 %.
    /// </summary>
    public static double PercentOf(double amount) => (amount - 1) * 100;

    /// <summary>
    /// Whether this effect's number is a percentage — which decides both the arithmetic and, in
    /// the templates, the trailing <c>%%</c>.
    /// </summary>
    public static bool IsPercentage(string? relativity) =>
        string.Equals(relativity, "Percent", StringComparison.OrdinalIgnoreCase)
        || string.Equals(relativity, "BasePercent", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The arguments a template needs, in its order, or null when this effect is not one we can
    /// describe.
    ///
    /// <para>The DEFAULT — target then value — covers most of the family
    /// (<c>"%1s: Changes Hitpoints by %2.2f%%"</c> and its two dozen siblings). The exceptions
    /// are the templates with extra slots, and <b><c>Resource</c>, which puts the number in the
    /// MIDDLE</b>: <c>"%1s: Adds %2.2f %3s to your inventory"</c>. That one is why the order
    /// cannot be inferred from the attributes an effect happens to carry.</para>
    ///
    /// <para>Getting this wrong for a subtype not listed here is not dangerous, because
    /// <see cref="Format"/> refuses a template whose slots do not match the arguments in both
    /// COUNT and KIND — see its own note.</para>
    /// </summary>
    /// <param name="unitLabel">
    /// What the mod calls <see cref="CardEffect.UnitType"/> on screen. It is a second unit, not
    /// the target — the one a damage bonus is AGAINST, or the thing a work rate is applied to —
    /// and it needs resolving just as much: left raw it prints "against AbstractHeavyInfantry".
    /// </param>
    public static IReadOnlyList<object>? ArgumentsFor(
        CardEffect effect, string? targetName, string? actionLabel = null, string? unitLabel = null)
    {
        if (effect == null) return null;

        var value = IsPercentage(effect.Relativity) ? PercentOf(effect.Amount) : effect.Amount;
        var target = targetName ?? "";

        if (FreeUnitSubtypes.Contains(effect.Subtype))
        {
            // "Delivers %d %s" — a count and a unit, and no target at all: these are delivered to
            // the player, which is why the template has no room for one.
            if (string.IsNullOrWhiteSpace(target)) return null;
            return new object[] { effect.Amount, target };
        }

        // A target of Player or techAll has no name, and the engine has no generic word for one
        // that this could borrow — the string table's "Player" symbols are all UI captions. So
        // the line is dropped rather than filled with something invented.
        if (string.IsNullOrWhiteSpace(target)) return null;

        var action = !string.IsNullOrWhiteSpace(effect.Action) ? effect.Action
                   : effect.AllActions ? actionLabel ?? ""
                   : "";

        var unit = string.IsNullOrWhiteSpace(unitLabel) ? effect.UnitType : unitLabel!;

        // What a subtype needs BESIDES the target and the number, in the templates' own order.
        var extras = effect.Subtype.ToLowerInvariant() switch
        {
            "cost" or "inventoryamount" or "resourcetricklerate" or "xptricklerate"
                => new[] { effect.Resource },
            // "%1s: Changes %2s Work Rate for %3s by %4.2f%%" — the ACTION first, then what it
            // is worked on, which the file puts in `unittype` far more often than in `resource`.
            "workrate" or "workratespecific"
                => new[] { action, Prefer(unit, effect.Resource) },
            "damage" => new[] { action },
            "damagebonus" => new[] { action, unit },
            _ => Array.Empty<string>(),
        };

        foreach (var extra in extras) if (string.IsNullOrWhiteSpace(extra)) return null;

        // "Changes X by N" puts the number LAST; "Adds N to X" puts it second. Getting this
        // backwards is caught by Format, because the number and the words are different kinds.
        var args = new List<object>(extras.Length + 2) { target };
        var numberFirst = !IsPercentage(effect.Relativity);

        if (numberFirst) args.Add(value);
        args.AddRange(extras);
        if (!numberFirst) args.Add(value);

        return args;
    }

    private static string Prefer(string first, string second) =>
        string.IsNullOrWhiteSpace(first) ? second : first;

    /// <summary>
    /// Both placeholder forms the templates use: positional with an optional precision
    /// (<c>%1s</c>, <c>%3.2f</c>) and plain sequential (<c>%d</c>, <c>%s</c>). No template mixes
    /// them. <c>%%</c> is a literal per cent sign and is matched first so it never reads as one.
    /// </summary>
    private static readonly Regex Placeholder =
        new(@"%%|%(?<pos>\d+)?(?:\.(?<prec>\d+))?(?<kind>[sdf])", RegexOptions.Compiled);

    /// <summary>
    /// Fills a template, or returns <b>null</b> rather than something plausible and wrong.
    ///
    /// <para><b>This refusal is the whole safety of the feature.</b> A misordered argument list
    /// does not throw and does not look broken — it produces a confident sentence saying a card
    /// changes wood cost by 33 %, which is worse than showing nothing. So a template is filled
    /// only when its slots match the arguments in COUNT and in KIND: a <c>%s</c> must receive a
    /// string and a <c>%f</c>/<c>%d</c> a number. That second half is what catches the swaps the
    /// count alone would let through — <c>Resource</c>'s (target, value, resource) against a
    /// naive (target, resource, value) is three arguments either way, and only the kinds
    /// differ.</para>
    /// </summary>
    public static string? Format(string? template, IReadOnlyList<object>? args)
    {
        if (string.IsNullOrEmpty(template) || args == null) return null;

        var used = new bool[args.Count];
        var next = 0;
        var failed = false;

        var text = Placeholder.Replace(template!, match =>
        {
            if (failed) return "";
            if (match.Value == "%%") return "%";

            var index = match.Groups["pos"].Success
                ? int.Parse(match.Groups["pos"].Value, CultureInfo.InvariantCulture) - 1
                : next++;

            if (index < 0 || index >= args.Count) { failed = true; return ""; }

            var arg = args[index];
            var kind = match.Groups["kind"].Value;
            used[index] = true;

            if (kind == "s")
            {
                if (arg is not string s || s.Length == 0) { failed = true; return ""; }
                return s;
            }

            if (arg is not double number) { failed = true; return ""; }

            var precision = match.Groups["prec"].Success
                ? int.Parse(match.Groups["prec"].Value, CultureInfo.InvariantCulture)
                : kind == "d" ? 0 : 2;

            return number.ToString("F" + precision, CultureInfo.InvariantCulture);
        });

        if (failed) return null;

        // Every argument has to land somewhere: a template with fewer slots than we brought is
        // the wrong template for this effect, not a shorter way of saying the same thing.
        foreach (var hit in used) if (!hit) return null;

        return text.Trim();
    }

    /// <summary>
    /// One effect as a line, or null when it cannot be described honestly.
    /// </summary>
    /// <param name="template">Looks a <c>cString*Effect</c> symbol up in the mod's string table.</param>
    /// <param name="displayName">
    /// Turns an internal proto name into what the mod calls it on screen — the same
    /// <c>proto*.xml</c> to string-table chain <see cref="ProtoNameResolver"/> already walks.
    /// </param>
    public static string? Render(
        CardEffect effect, Func<string, string?> template, Func<string, string?> displayName)
    {
        if (effect == null || !string.Equals(effect.Type, DataEffect, StringComparison.OrdinalIgnoreCase))
            return null;

        var symbol = SymbolFor(effect.Subtype, effect.Relativity);
        if (symbol == null) return null;

        var body = template(symbol);
        if (string.IsNullOrEmpty(body)) return null;

        // The unit a "free unit" effect delivers is named by an attribute; everything else names
        // what it CHANGES through its target. A target of Player has no name and no slot.
        var subject = FreeUnitSubtypes.Contains(effect.Subtype)
            ? effect.UnitType
            : effect.TargetName;

        var resolved = string.IsNullOrWhiteSpace(subject) ? null : displayName(subject) ?? subject;
        var actionLabel = effect.AllActions ? template(AllActionsSymbol) : null;

        // The SECOND unit — what a damage bonus is against, what a work rate is worked on — needs
        // naming as much as the target does, or the line reads "against AbstractHeavyInfantry".
        var unitLabel = string.IsNullOrWhiteSpace(effect.UnitType)
            ? null
            : displayName(effect.UnitType) ?? effect.UnitType;

        return Format(body, ArgumentsFor(effect, resolved, actionLabel, unitLabel));
    }

    /// <summary>
    /// Every proto name a set of effects will need resolved, so the caller can ask for them in
    /// one batch instead of one file scan per card.
    /// </summary>
    public static IEnumerable<string> ProtoNamesIn(IEnumerable<CardEffect> effects)
    {
        foreach (var effect in effects)
        {
            if (!string.Equals(effect.Type, DataEffect, StringComparison.OrdinalIgnoreCase)) continue;

            if (FreeUnitSubtypes.Contains(effect.Subtype))
            {
                if (!string.IsNullOrWhiteSpace(effect.UnitType)) yield return effect.UnitType;
                continue;
            }

            if (string.Equals(effect.TargetType, "ProtoUnit", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(effect.TargetName))
            {
                yield return effect.TargetName;
            }

            // DamageBonus names a second proto in an attribute rather than in its target.
            if (!string.IsNullOrWhiteSpace(effect.UnitType)) yield return effect.UnitType;
        }
    }

    /// <summary>Every template symbol a set of effects could ask for.</summary>
    public static IEnumerable<string> SymbolsIn(IEnumerable<CardEffect> effects)
    {
        var needsAllActions = false;

        foreach (var effect in effects)
        {
            if (!string.Equals(effect.Type, DataEffect, StringComparison.OrdinalIgnoreCase)) continue;

            if (effect.AllActions) needsAllActions = true;

            var symbol = SymbolFor(effect.Subtype, effect.Relativity);
            if (symbol != null) yield return symbol;
        }

        // Not a template but a word one of them needs, so it has to be fetched with them.
        if (needsAllActions) yield return AllActionsSymbol;
    }
}
