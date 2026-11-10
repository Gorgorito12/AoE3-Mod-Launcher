using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>
/// The image behind the Tournaments page (design 80): the chosen tournament's mod, running behind
/// the title bar and the tabs exactly as the Library's does, but its own layer. The Library's cover
/// is tied to the active mod; this one follows the tournament, and going back to the Library must
/// find its own image where it left it.
/// </summary>
public partial class MainWindow
{
    private ModProfile? _tournamentCoverMod;
    private string? _tournamentCoverKey;
    private readonly List<ImageBrush> _tournamentBrushes = new();
    private bool _tournamentClipQueued;

    /// <summary>The cover of this mod, or none. Called on every page render, so it does nothing
    /// unless the image really changes.</summary>
    private void SetTournamentBackdrop(ModProfile? mod)
    {
        _tournamentCoverMod = mod;
        UpdateTournamentBackdrop();
    }

    private void UpdateTournamentBackdrop()
    {
        var mod = _tournamentCoverMod;
        bool show = mod != null && _activeTopTab == TopTab.Multiplayer;
        TournamentBackdrop.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        MultiplayerView.SetCoverBehind(show);
        if (!show) return;

        var key = $"{mod!.Id}|{_config.GlassLevel}|{_config.ShowModImage}|{LibraryReducedEffects}|{_config.UseModColor}";
        if (key == _tournamentCoverKey)
        {
            QueueTournamentClip();
            return;
        }
        _tournamentCoverKey = key;
        _tournamentBrushes.Clear();

        var hero = mod.ResolveHeroSources().FirstOrDefault();
        var brush = _config.ShowModImage && !LibraryReducedEffects && hero != null ? BuildHeroFillBrush(hero) : null;
        var accent = ModTheme.AccentOf(mod.AccentColor, _config.UseModColor);
        TournamentBlur.Background = null;
        TournamentBlur.Visibility = Visibility.Collapsed;
        if (brush == null)
        {
            TournamentCover.Background = _config.ShowModImage && !LibraryReducedEffects
                ? ModTheme.HeroGradient(accent)
                : ModTheme.QuietBackground(accent);
            return;
        }

        _tournamentBrushes.Add(brush);
        TournamentCover.Background = brush;
        void Soften()
        {
            if (!_tournamentBrushes.Contains(brush)) return;
            UpdateTournamentCover();
            if (GlassTheme.Normalize(_config.GlassLevel) == GlassTheme.Solid) return;
            if (BuildBlurredCover(brush.ImageSource) is not { } blurred) return;
            _tournamentBrushes.Add(blurred);
            TournamentBlur.Background = blurred;
            TournamentBlur.Visibility = Visibility.Visible;
            UpdateTournamentCover();
            QueueTournamentClip();
        }
        if (brush.ImageSource is BitmapImage { IsDownloading: true } remote) remote.DownloadCompleted += (_, _) => Soften();
        else Soften();
    }

    /// <summary>CSS <c>center 25%/cover</c>, as the Library.</summary>
    private void UpdateTournamentCover()
    {
        var box = TournamentBackdrop.RenderSize;
        if (box.Width <= 0 || box.Height <= 0) return;
        foreach (var brush in _tournamentBrushes)
        {
            if (brush.ImageSource is not BitmapSource { PixelWidth: > 0 } image) continue;
            brush.Viewbox = HeroCover.Viewbox(image.PixelWidth, image.PixelHeight, box.Width, box.Height);
        }
    }

    /// <summary>The blurred image shows only behind the panels, as the Library's does.</summary>
    private void QueueTournamentClip()
    {
        if (TournamentBlur.Visibility != Visibility.Visible || _tournamentClipQueued) return;
        _tournamentClipQueued = true;
        // After layout, which is when a panel built a moment ago has a place.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _tournamentClipQueued = false;
            var group = new GeometryGroup();
            void Add(FrameworkElement element, double radius)
            {
                if (!element.IsVisible || element.ActualWidth <= 0) return;
                var rect = element.TransformToVisual(TournamentBackdrop).TransformBounds(new Rect(element.RenderSize));
                group.Children.Add(new RectangleGeometry(rect, radius, radius));
            }
            try
            {
                Add(MainNav, 0);
                foreach (var (element, radius) in MultiplayerView.TournamentGlassElements()) Add(element, radius);
                group.Freeze();
                TournamentBlur.Clip = group;
            }
            catch (InvalidOperationException)
            {
                // A panel between layouts; the next size change draws it.
            }
        }));
    }
}
