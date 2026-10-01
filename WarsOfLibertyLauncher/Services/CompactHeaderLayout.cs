using System.Collections.Generic;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Something the compact header can give up when its single row does not fit, in the order it
/// gives them up — least useful first.
/// </summary>
public enum HeaderReduction
{
    /// <summary>The "v1.0.14" chip. The brand button's tooltip carries the version instead.</summary>
    VersionChip,

    /// <summary>The update pill's caption. The gold pill and its icon stay; the tooltip says what it is.</summary>
    UpdatePillCaption,

    /// <summary>The "AoE3 Mod Launcher" wordmark. The icon stays, and so does the brand menu behind it.</summary>
    BrandWordmark,

    /// <summary>The word "Connected". The green dot and the ▾ stay; the dropdown says the rest.</summary>
    ConnectionWord,

    /// <summary>The account name. The avatar stays, and the account menu names you.</summary>
    AccountName,

    /// <summary>The ELO chip beside the account. Last, because it is the one number the header carries.</summary>
    AccountElo,
}

/// <summary>
/// How much of the compact header's single row has to be given up so it fits, and in what order.
///
/// <para><b>Why this exists.</b> Design handoff turn 36 folds the three main tabs and the account
/// block into the title bar, and was drawn at 1280 px. The launcher's minimum is 900, and at the
/// 1100-px default the row already overflows the moment the update pill appears. Nothing in that
/// row ellipsises — the caption buttons sit in their own columns and the content would simply be
/// clipped under them — so something has to decide, deliberately, what leaves first.</para>
///
/// <para>Pure and WPF-free, the same reason <see cref="RoomsTableLayout"/> is: the order is a
/// judgement, and a judgement that only exists inside a layout pass cannot be pinned by a test.
/// The caller measures; this only decides.</para>
///
/// <para><b>Never reduced:</b> the tabs, the bell, the caption buttons, the avatar, the
/// connection dot and its ▾, and the update pill's icon. Those are the things a person aims at.</para>
/// </summary>
public static class CompactHeaderLayout
{
    /// <summary>Every reduction, in the order they are applied.</summary>
    public static readonly IReadOnlyList<HeaderReduction> DropOrder = new[]
    {
        HeaderReduction.VersionChip,
        HeaderReduction.UpdatePillCaption,
        HeaderReduction.BrandWordmark,
        HeaderReduction.ConnectionWord,
        HeaderReduction.AccountName,
        HeaderReduction.AccountElo,
    };

    /// <summary>
    /// The least empty space the row keeps for dragging the window. The flexible gap in the
    /// middle of the title bar IS the drag region: a row packed edge to edge leaves a window that
    /// can only be moved by its version chip.
    /// </summary>
    public const double MinDragWidth = 48;

    /// <summary>
    /// How many entries of <see cref="DropOrder"/> to apply so the row fits.
    /// </summary>
    /// <param name="available">The width the row's content may use (DIP).</param>
    /// <param name="requiredAtFull">What the row asks for with nothing given up.</param>
    /// <param name="savings">
    /// What each entry of <see cref="DropOrder"/> gives back, in that order. Zero for something
    /// not on screen right now (an update pill that is not showing saves nothing).
    /// </param>
    /// <param name="minDrag">The drag space to keep on top of the content.</param>
    /// <returns>
    /// A count between 0 and <c>savings.Count</c>. When even every reduction is not enough the
    /// answer is all of them, and the rest is clipped on the right — the accepted floor.
    /// </returns>
    public static int ReductionsNeeded(
        double available,
        double requiredAtFull,
        IReadOnlyList<double> savings,
        double minDrag = MinDragWidth)
    {
        if (savings == null || savings.Count == 0) return 0;
        if (!(available > 0) || double.IsNaN(requiredAtFull)) return 0;

        var shortfall = requiredAtFull + minDrag - available;
        var count = 0;
        while (shortfall > 0 && count < savings.Count)
        {
            var saving = savings[count];
            if (saving > 0) shortfall -= saving;
            count++;
        }
        return count;
    }
}
