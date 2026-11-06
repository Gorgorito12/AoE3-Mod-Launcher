using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

public enum LibraryFilter { All, Installed, NotInstalled, Updates, Compatible }

public enum LibrarySort { Recent, Name, Status }

public enum LibraryGroupKind { Favorites, Mine, Catalog }

/// <summary>One group of the Library's list (design 68a).</summary>
public sealed record LibraryGroup(LibraryGroupKind Kind, IReadOnlyList<ModProfile> Mods);

/// <summary>
/// Which mods the Library's list shows and in what order. Pure, so the rules can be pinned without
/// a window: favourites first, then the player's mods by when they were last played, then the
/// catalog. A favourite never appears twice.
/// </summary>
public static class LibraryList
{
    public static bool Passes(LibraryFilter filter, ModRowState state) => filter switch
    {
        LibraryFilter.Installed => state.Status is ModRowStatus.Installed or ModRowStatus.UpdateAvailable,
        LibraryFilter.NotInstalled => state.Status == ModRowStatus.NotInstalled,
        LibraryFilter.Updates => state.Status == ModRowStatus.UpdateAvailable,
        LibraryFilter.Compatible => state.Status != ModRowStatus.Incompatible,
        _ => true,
    };

    public static bool Matches(ModProfile profile, string? query)
    {
        var q = query?.Trim();
        if (string.IsNullOrEmpty(q)) return true;
        return (profile.DisplayName ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
            || (profile.Author ?? "").Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>In the player's list: installed, added by hand, or built in.</summary>
    public static bool IsMine(ModRowState state)
        => state.IsBuiltIn || state.IsInUserCollection || state.Status != ModRowStatus.NotInstalled;

    public static IReadOnlyList<LibraryGroup> Build(
        IEnumerable<ModProfile> profiles,
        Func<ModProfile, ModRowState> stateOf,
        Func<string, bool> isFavorite,
        Func<string, DateTime?> lastPlayed,
        LibraryFilter filter,
        string? query,
        LibrarySort sort = LibrarySort.Recent)
    {
        var visible = profiles
            .Where(p => Matches(p, query))
            .Select(p => (Profile: p, State: stateOf(p)))
            .Where(x => Passes(filter, x.State))
            .ToList();

        var favorites = visible.Where(x => isFavorite(x.Profile.Id)).ToList();
        var mine = visible
            .Where(x => !isFavorite(x.Profile.Id) && IsMine(x.State))
            .OrderByDescending(x => lastPlayed(x.Profile.Id) ?? DateTime.MinValue)
            .ThenBy(x => x.Profile.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var catalog = visible.Where(x => !isFavorite(x.Profile.Id) && !IsMine(x.State));
        catalog = sort switch
        {
            LibrarySort.Name => catalog.OrderBy(x => x.Profile.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            LibrarySort.Status => catalog.OrderBy(x => x.State.Status).ThenBy(x => x.Profile.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            _ => catalog,
        };

        var groups = new List<LibraryGroup>();
        void Add(LibraryGroupKind kind, IEnumerable<ModProfile> mods)
        {
            var list = mods.ToList();
            if (list.Count > 0) groups.Add(new LibraryGroup(kind, list));
        }
        Add(LibraryGroupKind.Favorites, favorites.Select(x => x.Profile));
        Add(LibraryGroupKind.Mine, mine.Select(x => x.Profile));
        Add(LibraryGroupKind.Catalog, catalog.Select(x => x.Profile).ToList());
        return groups;
    }
}

/// <summary>Which part of an image CSS <c>center 25%/cover</c> shows, as a relative Viewbox.</summary>
public static class HeroCover
{
    public static System.Windows.Rect Viewbox(double imageWidth, double imageHeight, double boxWidth, double boxHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || boxWidth <= 0 || boxHeight <= 0)
            return new System.Windows.Rect(0, 0, 1, 1);
        var image = imageWidth / imageHeight;
        var box = boxWidth / boxHeight;
        if (image > box)
        {
            var visible = box / image;
            return new System.Windows.Rect((1 - visible) / 2, 0, visible, 1);
        }
        else
        {
            var visible = image / box;
            return new System.Windows.Rect(0, (1 - visible) * .25, 1, visible);
        }
    }
}

/// <summary>
/// The launcher's one status line. A check always says something ("up to date"), and the chip above
/// the title already says that, so only a message worth reading gets the line under the buttons.
/// </summary>
public static class LibraryStatus
{
    public static bool IsRoutine(string? message, IEnumerable<string> templates)
    {
        if (string.IsNullOrWhiteSpace(message)) return true;
        foreach (var template in templates)
        {
            var open = template.IndexOf('{');
            if (open < 0)
            {
                if (message == template) return true;
                continue;
            }
            var close = template.LastIndexOf('}');
            if (message.StartsWith(template[..open], StringComparison.Ordinal)
                && message.EndsWith(template[(close + 1)..], StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}

/// <summary>
/// The prototype's <c>clamp()</c> rules, in DIPs of the window's width. WPF has no container
/// queries, so the layout asks here whenever the width changes.
/// </summary>
public readonly record struct LibraryMetrics(
    double RailWidth, double ColumnGap, double SidePadding, double BottomPadding,
    double RowHeight, double RowNameSize, double TitleSize, double AuthorSize, double DescriptionSize,
    double ShotWidth, double DetailGap)
{
    /// <summary>The prototype folds the list on the 1366 canvas and opens it above, and
    /// Ajustes › Interfaz says "menos de 1400 px".</summary>
    public const double RailFoldsBelow = 1400;

    public const double FoldedRailWidth = 72;

    public const int DescriptionLines = 4;

    public const double DescriptionLineHeight = 1.6;

    /// <summary>72ch: the width of a Segoe UI zero is .539 of the font size.</summary>
    public const double DescriptionWidthEm = 72 * .539;

    public static LibraryMetrics For(double width)
    {
        double Clamp(double min, double percent, double max) => Math.Clamp(width * percent / 100, min, max);
        return new LibraryMetrics(
            RailWidth: Clamp(320, 23, 460),
            ColumnGap: Clamp(20, 2.4, 72),
            SidePadding: Clamp(16, 1.4, 32),
            BottomPadding: Clamp(16, 1.2, 28),
            RowHeight: Clamp(56, 3.1, 68),
            RowNameSize: Clamp(13.5, .6, 15.5),
            TitleSize: Clamp(34, 2.7, 64),
            AuthorSize: Clamp(13.5, .62, 16),
            DescriptionSize: Clamp(14, .62, 16.5),
            ShotWidth: Clamp(120, 7, 176),
            DetailGap: Clamp(14, 1, 22));
    }

    /// <summary><c>open</c> and <c>closed</c> are the player's; anything else follows the width.</summary>
    public static bool RailOpen(string? mode, double width) => mode switch
    {
        "open" => true,
        "closed" => false,
        _ => width >= RailFoldsBelow,
    };
}
