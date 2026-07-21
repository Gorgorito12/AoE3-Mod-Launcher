using System.Collections.Generic;

namespace WarsOfLibertyLauncher.Services;

/// <summary>How an addon's files are packaged inside its download.</summary>
public enum AddonPackaging
{
    /// <summary>Plain archive: the files are extracted directly.</summary>
    Overlay,
    /// <summary>
    /// The archive holds an NSIS self-extracting installer. The launcher runs it
    /// silently into a scratch folder and applies the result — see
    /// <see cref="NsisExtractor"/> for why that is safer than the alternative.
    /// </summary>
    NsisInstaller,
}

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

    /// <summary>How the launcher gets this addon's files out of its download.</summary>
    public AddonPackaging Packaging { get; init; } = AddonPackaging.Overlay;

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
            DescriptionEn = "A transparent in-game interface: 25 UI layouts and 11 textures. "
                          + "It changes files the game compares between players, so it may "
                          + "affect multiplayer.",
            DescriptionEs = "Interfaz transparente dentro del juego: 25 diseños de interfaz y 11 "
                          + "texturas. Modifica archivos que el juego compara entre jugadores, "
                          + "así que puede afectar el multijugador.",
            HeavenFileId = "1656",
            SourceUrl = "https://aoe3.heavengames.com/downloads/showfile.php?fileid=1656",
            // The download is a single file, "Ekanta TAD UI.exe" — but it is an
            // NSIS self-extractor whose payload IS ordinary game content: 25
            // data\ui*.xml.xmb layouts and 11 art\ui\ingame\*.ddt textures. The
            // obstacle was packaging, not the addon.
            //
            // Parsing NSIS in-process was rejected: no existing dependency handles
            // it (SharpCompress covers zip/rar/7z/tar/gzip) and a hand-written
            // parser for untrusted input is a poor trade for one addon. Running
            // the installer against the game folder was rejected too — the files
            // would land with no backup and no manifest entry, so verify would
            // call them corrupt and Repair would wipe them.
            //
            // Running it into a SCRATCH folder and applying the result through the
            // normal path avoids both, and is safer than the alternative it
            // replaces: telling the player to run the same installer themselves,
            // against their game, with no way to undo it. Verified in silent mode:
            // exit 0, no window, 39 files, of which the readme, the .url and
            // uninst.exe are dropped by the existing skip rules.
            Packaging = AddonPackaging.NsisInstaller,
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
