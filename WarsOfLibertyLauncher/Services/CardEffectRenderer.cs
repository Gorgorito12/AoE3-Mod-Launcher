using System;
using System.Collections.Generic;
using System.Linq;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Turns a batch of cards into the lines the game itself would show for them — the sentences
/// with percentages that <see cref="CardEffectText"/> renders, resolved against one install.
///
/// <para><b>A batch and not one card at a time, deliberately.</b> Naming the units an effect
/// targets means streaming <c>proto*.xml</c> (12 MB for Wars of Liberty) and the templates mean
/// walking the string table, so a per-card call would pay for both on every tile. One pass for a
/// whole deck resolves 57 names and a dozen templates.</para>
///
/// <para><b>Do not call this on the UI thread.</b> Both scans are the reason.</para>
/// </summary>
public static class CardEffectRenderer
{
    /// <summary>
    /// One list of lines per card, keyed by internal card name. A card whose effects could none
    /// of them be described honestly is simply absent.
    ///
    /// <para><b>Nothing is truncated.</b> These land in a detail panel that shows one selected
    /// card at a time and scrolls with its page, so a card with a dozen effects is long rather
    /// than silently short — which is the failure a cap would cause on exactly the cards worth
    /// reading.</para>
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> RenderAll(
        string? installPath,
        string? gameExecutable,
        IReadOnlyDictionary<string, CardDetail> cards)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(installPath) || cards.Count == 0) return result;

        var effects = cards.Values.SelectMany(c => c.EffectsOrEmpty).ToList();
        if (effects.Count == 0) return result;

        var symbols = CardEffectText.SymbolsIn(effects).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var protoNames = CardEffectText.ProtoNamesIn(effects)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        IReadOnlyDictionary<string, string> protoDisplay;
        try
        {
            protoDisplay = ProtoNameResolver.Resolve(installPath, gameExecutable, protoNames);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"CardEffectRenderer: proto names unavailable — {ex.Message}");
            protoDisplay = new Dictionary<string, string>();
        }

        // Abstract unit types are not units, so the proto files do not name them; ask the string
        // table for the ones it does define, in the same batch as the templates.
        foreach (var name in protoNames)
        {
            if (protoDisplay.ContainsKey(name)) continue;
            var symbol = CardEffectText.AbstractSymbolFor(name);
            if (symbol != null) symbols.Add(symbol);
        }

        Dictionary<string, string> templates;
        try
        {
            templates = ModStringTable.ResolveBySymbol(installPath!, symbols);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"CardEffectRenderer: effect templates unavailable — {ex.Message}");
            return result;
        }

        string? Template(string symbol) => templates.TryGetValue(symbol, out var t) ? t : null;

        string? Display(string proto)
        {
            if (protoDisplay.TryGetValue(proto, out var named)) return named;

            var symbol = CardEffectText.AbstractSymbolFor(proto);
            if (symbol != null && templates.TryGetValue(symbol, out var group)
                && !string.IsNullOrWhiteSpace(group))
            {
                return group;
            }

            return null;
        }

        foreach (var (name, detail) in cards)
        {
            List<string>? lines = null;
            foreach (var effect in detail.EffectsOrEmpty)
            {
                var line = CardEffectText.Render(effect, Template, Display);
                if (line == null) continue;      // no template, or nothing to put in its slots
                (lines ??= new List<string>()).Add(line);
            }

            if (lines != null) result[name] = lines;
        }

        return result;
    }
}
