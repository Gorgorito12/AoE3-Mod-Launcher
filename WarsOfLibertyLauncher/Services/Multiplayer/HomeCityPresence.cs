using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>Whether this player has a home city for a mod's LAN games.</summary>
public enum HomeCityState
{
    /// <summary>Not looked at, or nothing to look at (no folder resolved, the folder missing,
    /// unreadable). Changes nothing: the automatic entry goes on as before.</summary>
    Unknown,
    /// <summary>A readable save folder with no home city in it.</summary>
    None,
    /// <summary>At least one home city.</summary>
    Present,
}

/// <summary>
/// Whether the player ever created a home city for the mod a room plays (2026-11-18). The game
/// asks for one the first time it enters LAN, and the automatic entry (things-new's
/// <c>lanAutoStart</c>) does not know that screen: it switches past it 1.5 s after opening it and,
/// when the LAN dialog does not come, opens it again up to three times — which can pull the player
/// out of the very screen where he is creating the city. So a player with NO city launches without
/// the automatic entry that time and is told why (the room's card and a chat line); from the next
/// match the game enters by itself.
///
/// <para><b>What counts is the game's own file name, <c>sp_&lt;City&gt;_homecity.xml</c></b>, in
/// <c>My Games\&lt;mod&gt;\Savegame\</c> — the same files LAN uses (a recording names
/// <c>gameplayer1hcfilename 'sp_Beijing_homecity.xml'</c>). Deliberately NOT
/// <see cref="HomeCityDeckService"/>'s <c>*homecity*.xml</c>: that also matches
/// <c>LastHomeCityY.xml</c> and stray data files a modder left there, and here a wrong "yes" is the
/// failure (the player would be launched into the screen this exists to keep him out of).</para>
///
/// <para><b>Unknown is never None.</b> No resolvable folder, a folder that does not exist (a mod
/// never opened) or one that cannot be read all answer <see cref="HomeCityState.Unknown"/>, which
/// changes nothing. Only a save folder that was READ and holds no city is
/// <see cref="HomeCityState.None"/>. Pure core pinned by <c>HomeCityPresenceTests</c>, where the
/// refusals are the point.</para>
/// </summary>
public static class HomeCityPresence
{
    private static readonly Regex CityFile = new(
        @"^sp_.+_homecity\.xml$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Whether one file name is a home city the player made.</summary>
    public static bool IsCityFile(string? fileName)
        => !string.IsNullOrWhiteSpace(fileName) && CityFile.IsMatch(Path.GetFileName(fileName));

    /// <summary>The state of a save folder from its file names; null = the folder was not read.</summary>
    public static HomeCityState From(IEnumerable<string>? savegameFileNames)
        => savegameFileNames == null ? HomeCityState.Unknown
            : savegameFileNames.Any(IsCityFile) ? HomeCityState.Present
            : HomeCityState.None;

    /// <summary>
    /// The state for a mod's LAN games, read from disk. The folder is the MATCH folder
    /// (<see cref="UserDataService.ResolveMatchFolderName"/>): the mod's own, or the base game's
    /// for the stock profile. Never throws; anything unexpected is Unknown.
    /// </summary>
    public static HomeCityState For(ModProfile? profile, LauncherConfig? config)
    {
        if (profile == null || config == null) return HomeCityState.Unknown;
        try
        {
            var name = UserDataService.ResolveMatchFolderName(profile, config);
            if (string.IsNullOrWhiteSpace(name)) return HomeCityState.Unknown;
            var root = UserDataService.GetUserDataFolder(name);
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return HomeCityState.Unknown;
            var saves = Path.Combine(root, HomeCityDeckService.FolderName);
            // A mod opened once has its folder; Savegame appears with the first save or city.
            // A Users3 profile means the game ran there, so an absent Savegame is "no city".
            if (!Directory.Exists(saves))
                return Directory.Exists(Path.Combine(root, "Users3")) ? HomeCityState.None : HomeCityState.Unknown;
            return From(Directory.EnumerateFiles(saves, "*.xml", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName).OfType<string>().ToList());
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"HomeCityPresence: could not read the save folder of '{profile.Id}': {ex.Message}");
            return HomeCityState.Unknown;
        }
    }
}
