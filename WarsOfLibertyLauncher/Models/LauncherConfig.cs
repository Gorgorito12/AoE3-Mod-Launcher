using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WarsOfLibertyLauncher.Models;

/// <summary>
/// Local launcher config. Most defaults match the official servers; the install
/// path is normally auto-detected from the Windows registry on first run.
/// </summary>
public class LauncherConfig
{
    /// <summary>Primary URL of UpdateInfo.xml. Default: official aoe3wol.com server.</summary>
    [JsonPropertyName("updateInfoUrl")]
    public string UpdateInfoUrl { get; set; } = "http://aoe3wol.com/updates/UpdateInfo.xml";

    /// <summary>Fallback URL used if the primary fails. Default: SourceForge mirror.</summary>
    [JsonPropertyName("updateInfoUrlAlt")]
    public string UpdateInfoUrlAlt { get; set; } =
        "http://master.dl.sourceforge.net/project/wars-of-liberty/Patches/UpdateInfo.xml";

    /// <summary>
    /// Where Wars of Liberty is installed. If empty, the launcher tries to
    /// detect it from the Windows registry on startup.
    /// </summary>
    [JsonPropertyName("modInstallPath")]
    public string ModInstallPath { get; set; } = "";

    /// <summary>
    /// Path to age3y.exe (Age of Empires III: The Asian Dynasties).
    /// If empty, the launcher tries to find it automatically by walking up
    /// from the WoL install folder. Wars of Liberty does NOT have its own
    /// .exe — it patches AoE3's data files and the game is launched via
    /// age3y.exe in the AoE3 folder.
    /// </summary>
    [JsonPropertyName("gameExecutable")]
    public string GameExecutable { get; set; } = "";

    /// <summary>Optional command-line arguments for the game.</summary>
    [JsonPropertyName("gameArguments")]
    public string GameArguments { get; set; } = "";

    /// <summary>If true, opens the postUpdatePage URLs in the browser after each update.</summary>
    [JsonPropertyName("openPostUpdatePages")]
    public bool OpenPostUpdatePages { get; set; } = true;

    /// <summary>UI language: "en" (default) or "es".</summary>
    [JsonPropertyName("language")]
    public string Language { get; set; } = "en";

    /// <summary>
    /// URL of the Wars of Liberty installer ZIP. The ZIP must contain the
    /// Inno Setup launcher .exe AND its companion .bin data files (the
    /// installer is split because the full payload is ~2.7 GB).
    /// </summary>
    [JsonPropertyName("installerZipUrl")]
    public string InstallerZipUrl { get; set; } =
        "https://aoe3wol.com/files/Wars%20of%20Liberty%20Setup%20-%20v1.0.15d.zip";

    /// <summary>
    /// Default install folder shown in the install dialog. The user can
    /// override it before installing.
    /// </summary>
    [JsonPropertyName("defaultInstallFolder")]
    public string DefaultInstallFolder { get; set; } =
        @"C:\Program Files (x86)\Wars of Liberty";

    /// <summary>
    /// Official Wars of Liberty website. Used as a fallback link if the
    /// installer ZIP URL is empty or fails.
    /// </summary>
    [JsonPropertyName("officialWebsite")]
    public string OfficialWebsite { get; set; } = "http://aoe3wol.com/";

    private const string ConfigFileName = "launcher-config.json";

    public static LauncherConfig Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, ConfigFileName);
        if (!File.Exists(path))
        {
            var defaults = new LauncherConfig();
            defaults.Save();
            return defaults;
        }
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<LauncherConfig>(json) ?? new LauncherConfig();
    }

    public void Save()
    {
        var path = Path.Combine(AppContext.BaseDirectory, ConfigFileName);
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(this, options));
    }
}
