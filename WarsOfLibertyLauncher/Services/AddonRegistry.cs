using System.Collections.Generic;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// One optional addon the launcher can offer.
/// </summary>
public sealed class AddonEntry
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string DescriptionEn { get; init; } = "";
    public string DescriptionEs { get; init; } = "";

    /// <summary>AoE3 Heaven file id, downloaded via <see cref="HeavenDownloader"/>.</summary>
    public string HeavenFileId { get; init; } = "";

    /// <summary>The author's page — credit, notes, and the manual route.</summary>
    public string SourceUrl { get; init; } = "";

    /// <summary>
    /// Exact entries to apply, or null to fall back to the automatic skip rules.
    /// Declaring the list states what an addon touches instead of inferring it.
    /// </summary>
    public IReadOnlyList<string>? IncludeOnly { get; init; }

    /// <summary>
    /// True when the author ships the addon as an executable installer, so there
    /// is nothing for the launcher to overlay. Rendered as a link rather than a
    /// checkbox — see <see cref="AddonRegistry"/> for why it is still listed.
    /// </summary>
    public bool ExternalInstallerOnly { get; init; }

    public string DescriptionFor(string language) =>
        language == "es" && !string.IsNullOrWhiteSpace(DescriptionEs) ? DescriptionEs : DescriptionEn;
}

/// <summary>
/// The addons the launcher offers, hard-coded for the same reason
/// <see cref="ModRegistry"/> keeps built-in mod profiles: it works on a cold
/// start, with no catalog fetch and — crucially here — without waiting on the
/// authors' permission to re-host their files. A catalog-backed list can be
/// merged in later without touching the UI.
///
/// Every entry's contents were read from the real archive rather than assumed;
/// the surprises are recorded per entry, because each one changed the design.
/// </summary>
public static class AddonRegistry
{
    public static IReadOnlyList<AddonEntry> All { get; } = new[]
    {
        new AddonEntry
        {
            Id = "heaven-1932",
            Name = "Building placement rotator",
            DescriptionEn = "Rotate buildings before placing them, with the middle mouse button.",
            DescriptionEs = "Rota los edificios antes de colocarlos, con el botón central del mouse.",
            HeavenFileId = "1932",
            SourceUrl = "https://aoe3.heavengames.com/downloads/showfile.php?fileid=1932",
            // The archive also carries a UPX-packed executable, a PDF and a
            // screenshot; only these startup configs are game files. All three
            // are declared rather than guessing which one applies: the engine
            // reads the one matching its executable (game/gamex/gamey ↔ vanilla /
            // WarChiefs / TAD), they are small text files, and the backup makes
            // every one of them reversible.
            IncludeOnly = new[]
            {
                "startup/game.con",
                "startup/gamex.con",
                "startup/gamey.con",
            },
        },
        new AddonEntry
        {
            Id = "heaven-3730",
            Name = "Gun smoke, weapon sounds and player colours",
            DescriptionEn = "Musket smoke effects, firearm sounds, and a clearer player-colour palette.",
            DescriptionEs = "Efectos de humo de mosquete, sonidos de armas de fuego y una paleta de "
                          + "colores de jugador más clara.",
            HeavenFileId = "3730",
            SourceUrl = "https://aoe3.heavengames.com/downloads/showfile.php?fileid=3730",
            // No include list needed: everything ships under a wrapper folder that
            // AddonPaths strips, and the automatic rules already drop what doesn't
            // belong. Note this one replaces .xmb files, so AddonRisk reports it as
            // a multiplayer risk and the user has to confirm.
        },
        new AddonEntry
        {
            Id = "heaven-1656",
            Name = "Transparent interface (Ekanta UI)",
            DescriptionEn = "A transparent in-game interface. Its author ships it as an installer, "
                          + "so it is applied outside the launcher.",
            DescriptionEs = "Interfaz transparente dentro del juego. Su autor lo distribuye como "
                          + "instalador, así que se aplica fuera del launcher.",
            HeavenFileId = "1656",
            SourceUrl = "https://aoe3.heavengames.com/downloads/showfile.php?fileid=1656",
            // The archive holds exactly one file — "Ekanta TAD UI.exe" — and no
            // game files at all, so there is nothing to overlay and a checkbox
            // would be a lie. Still listed, with a link: a player looking for it
            // should find it from the launcher even when the launcher can't be the
            // one to install it.
            ExternalInstallerOnly = true,
        },
    };

    public static AddonEntry? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        foreach (var a in All)
            if (string.Equals(a.Id, id, System.StringComparison.OrdinalIgnoreCase)) return a;
        return null;
    }
}
