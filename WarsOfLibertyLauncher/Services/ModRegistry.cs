using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// In-memory list of every mod profile the launcher ships with. A profile
/// is just a record of "how this mod is laid out, where its updates come
/// from, what its name + colors are". Adding a new mod = adding an entry
/// here.
///
/// The list is embedded (not loaded from disk) on purpose — we want users
/// to be unable to break the launcher by editing a JSON, and we want
/// every install of the launcher to know about the same set of supported
/// mods. If the list grows past ~5 entries we'll move it to an embedded
/// resource file for readability.
/// </summary>
public static class ModRegistry
{
    /// <summary>Wars of Liberty — full updater pipeline + community translations.</summary>
    public const string WolId = "wol";

    /// <summary>Improvement Mod — uses its own external patcher; the launcher only plays.</summary>
    public const string ImprovementModId = "improvement-mod";

    private static readonly List<ModProfile> _profiles = BuildProfiles();

    public static IReadOnlyList<ModProfile> All => _profiles;

    /// <summary>
    /// Returns the profile with the given id, or null when nothing matches.
    /// IDs are case-insensitive — config files written by hand often use
    /// different capitalization.
    /// </summary>
    public static ModProfile? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return _profiles.FirstOrDefault(
            p => string.Equals(p.Id, id, System.StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns the profile to use when nothing is specified. Today: WoL,
    /// because the launcher started its life there and the existing
    /// installed user base expects it. When the launcher gets a proper
    /// "first-run picker" UI this will go away.
    /// </summary>
    public static ModProfile Default => Find(WolId)
        ?? _profiles[0]; // defensive — should never trip with the embedded list.

    private static List<ModProfile> BuildProfiles() => new()
    {
        new ModProfile
        {
            Id = WolId,
            DisplayName = "Wars of Liberty",
            Subtitle = "Launcher",
            AccentColor = "#c8102e",
            // Reuse the launcher's app icon (WoL.ico, registered as a
            // pack-resource in the .csproj) so the WoL tile shows the real
            // logo instead of the "W" placeholder.
            BannerImage = "pack://application:,,,/WoL.ico",
            InstallType = ModInstallType.IsolatedFolder,
            DefaultInstallFolder = @"C:\Program Files (x86)\Wars of Liberty",
            InstallProbeFile = @"data\stringtabley.xml",
            GameExecutable = "age3y.exe",
            GameArguments = "",
            UpdateMechanism = ModUpdateMechanism.WolPatcher,
            Wol = new WolPatcherSettings
            {
                UpdateInfoUrl = "http://aoe3wol.com/updates/UpdateInfo.xml",
                UpdateInfoUrlAlt =
                    "http://master.dl.sourceforge.net/project/wars-of-liberty/Patches/UpdateInfo.xml",
                OfficialWebsite = "http://aoe3wol.com/",
                PayloadZipUrls = new[]
                {
                    "https://github.com/papillo12/Updater/releases/download/updater/WolPayload.zip.001",
                    "https://github.com/papillo12/Updater/releases/download/updater/WolPayload.zip.002",
                    "https://github.com/papillo12/Updater/releases/download/updater/WolPayload.zip.003",
                },
            },
            Translations = new TranslationsSettings
            {
                Repo = "papillo12/translations",
                CoveredFiles = new List<string>
                {
                    @"data\stringtabley.xml",
                    @"data\unithelpstringsy.xml",
                },
            },
        },
        new ModProfile
        {
            Id = ImprovementModId,
            DisplayName = "Improvement Mod",
            Subtitle = "AoE3:TAD overhaul",
            // Bluish accent so it's visually distinct from WoL's red without
            // clashing with the launcher's dark theme.
            AccentColor = "#3a8cd9",
            BannerImage = null,
            // IM extracts its files INTO the AoE3 folder (or its bin\
            // subfolder for Steam) and ships its own age3m.exe alongside
            // the original AoE3 binaries.
            InstallType = ModInstallType.InPlaceOverlay,
            // No default — IM lives wherever AoE3 lives. The launcher
            // resolves AoE3's path separately and uses that.
            DefaultInstallFolder = "",
            InstallProbeFile = "age3m.exe",
            GameExecutable = "age3m.exe",
            GameArguments = "",
            // Updates are out of scope for the launcher — IM has its own
            // patcher (which IS age3m.exe, run with a flag). For now we
            // only expose PLAY; later we can shell out to the patcher.
            UpdateMechanism = ModUpdateMechanism.DelegatedExternal,
            Wol = null,
            // The IM team doesn't currently maintain a community translation
            // pipeline compatible with our overlay system. Leaving null hides
            // the language menu for this mod.
            Translations = null,
        },
    };
}
