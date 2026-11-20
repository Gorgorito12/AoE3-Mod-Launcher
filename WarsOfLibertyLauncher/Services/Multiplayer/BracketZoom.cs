using System;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// The bracket's zoom, in percent. A small draw used to sit at 100 % in the top-left corner of a
/// panel five times its size; now the page fits it by itself, and the player may zoom further.
///
/// <para>The automatic fit only ever ENLARGES, and never past <see cref="AutoMax"/>: a big draw
/// still opens at 100 % and scrolls, as it always did (shrinking 64 places to fit would make every
/// name unreadable), and a 4-player draw does not become a poster. The Fit button and the manual
/// steps go the whole range. Pure, so <c>BracketZoomTests</c> pins it.</para>
/// </summary>
public static class BracketZoom
{
    public const double Min = 60;
    public const double Max = 200;
    public const double AutoMax = 150;
    public const double StepSize = 10;

    /// <summary>The breathing room left around a fitted sheet, in pixels of the viewport.</summary>
    public const double FitMargin = 24;

    public static double Clamp(double percent) =>
        double.IsNaN(percent) ? 100 : Math.Round(Math.Clamp(percent, Min, Max));

    /// <summary>
    /// One press of − or +, or one notch of Ctrl+wheel: the next multiple of ten in that direction.
    /// From a fitted 129 % that is 130 or 120, so the figure the player reads is round from the
    /// first press on.
    /// </summary>
    public static double Step(double current, int direction)
    {
        if (direction == 0 || double.IsNaN(current)) return Clamp(current);
        var next = direction > 0
            ? Math.Floor(current / StepSize) * StepSize + StepSize
            : Math.Ceiling(current / StepSize) * StepSize - StepSize;
        return Clamp(next);
    }

    /// <summary>The largest zoom at which the whole sheet fits the viewport, or null when either
    /// size is unknown.</summary>
    public static double? FitPercent(double sheetWidth, double sheetHeight, double viewportWidth, double viewportHeight)
    {
        if (sheetWidth <= 0 || sheetHeight <= 0 || viewportWidth <= FitMargin || viewportHeight <= FitMargin)
            return null;
        var scale = Math.Min((viewportWidth - FitMargin) / sheetWidth, (viewportHeight - FitMargin) / sheetHeight);
        return Math.Floor(scale * 100);
    }

    /// <summary>What a tournament opens at: the fit, between 100 and <see cref="AutoMax"/>.</summary>
    public static double Auto(double sheetWidth, double sheetHeight, double viewportWidth, double viewportHeight) =>
        FitPercent(sheetWidth, sheetHeight, viewportWidth, viewportHeight) is double fit
            ? Math.Clamp(fit, 100, AutoMax)
            : 100;

    /// <summary>The Fit button: the whole sheet on screen, within the manual range.</summary>
    public static double Fit(double sheetWidth, double sheetHeight, double viewportWidth, double viewportHeight) =>
        FitPercent(sheetWidth, sheetHeight, viewportWidth, viewportHeight) is double fit ? Clamp(fit) : 100;
}
