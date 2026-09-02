using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// The launcher-wide "text size" setting: it multiplies every font-size token and
/// <b>nothing else</b>.
///
/// <para><b>Why this exists, and why it is not <c>UiScale</c>.</b> The multiplayer type
/// sizes went round three times on one question — "the text looks small" — and each round
/// could only answer it by moving the tokens themselves, because that was the only lever
/// there was. Magnifying the surface through <see cref="Controls.UiScale"/> was tried and
/// rejected on sight as "gigante": a transform scales the padding, the gutters and the row
/// heights too, so over a half-empty table it reads as a zoom rather than as bigger type.
/// This scales the TYPE and leaves every measurement alone, so the layout stays the one the
/// design handoff specifies at any setting.</para>
///
/// <para><b>Why the XAML had to move to <c>DynamicResource</c>.</b> The size tokens are
/// <c>sys:Double</c> resources, and WPF resolves a <c>{StaticResource}</c> when the XAML is
/// PARSED — the value is baked into the Setter and never looks at the dictionary again.
/// Writing a scaled value here would therefore have moved the ~90 code-behind sites (which
/// call <c>FindResource</c> at runtime) and none of the ~390 XAML ones: half the interface
/// scaling and the other half not. Every one of those XAML sites now says
/// <c>{DynamicResource}</c>, which is also what lets the setting apply without a restart.
/// <b>A new font size must be declared the same way</b> — a <c>{StaticResource}</c> that
/// slips through does not fail the build, it just silently stops scaling, which is why
/// <c>TextScaleTests</c> walks the XAML and asserts there are none.</para>
///
/// <para><b>And it must be added to <see cref="ScaledKeys"/>, which is the OTHER half and
/// the one that was missed.</b> A token can be declared right, bound as
/// <c>{DynamicResource}</c> everywhere and still never move, because
/// <see cref="Apply"/> walks that list and nothing else. That is what happened to the
/// whole <c>Set*Size</c> family: the two settings windows stayed at 100 % beside a
/// multiplayer tab at 110 %, with the two scales declared identically rung for rung, so
/// the omission WAS the entire visible difference.
/// <c>EveryFontSizeTokenTheXamlBindsIsScaled</c> now walks from the markup and the code
/// back to the list, which is the direction none of the other checks could see.</para>
///
/// <para>The chrome is deliberately NOT scaled — see <see cref="UnscaledChromeKeys"/>.
/// Those tokens sit in a bar whose height comes from a fixed token, and
/// <c>App.ApplyWindowChrome</c> derives <c>WindowChrome.CaptionHeight</c> from that same
/// token; text growing inside a caption region that cannot grow with it is the documented
/// silent-breakage zone.</para>
/// </summary>
public static class TextScale
{
    /// <summary>Config value meaning "work it out from the screen".</summary>
    public const string Auto = "auto";

    /// <summary>
    /// What the settings dropdown offers, in order.
    ///
    /// <para><see cref="Auto"/> leads because it is the default — a dropdown should open on
    /// what the user actually has. It briefly led with 100 while that was the default; the
    /// order follows the default rather than the other way round.</para>
    /// </summary>
    public static readonly IReadOnlyList<string> Choices = new[] { Auto, "100", "110", "125" };

    /// <summary>
    /// Every token this multiplies. Font sizes only — a height or a padding in here would
    /// turn the setting into the zoom it exists to avoid.
    ///
    /// <para><b>A font size that is missing from this list does nothing wrong and is
    /// invisible.</b> <see cref="Apply"/> walks THIS array, never the dictionary, and skips
    /// a key it cannot find. So a token can be declared correctly, consumed correctly as
    /// <c>{DynamicResource}</c>, documented as following the setting — and simply never be
    /// multiplied. That is exactly what happened to the whole <c>Set*Size</c> family: both
    /// settings windows sat at 100 % while the multiplayer tab beside them ran at 110 %,
    /// and the two scales are declared identically rung for rung, so the entire visible
    /// difference was this omission. <c>EveryFontSizeTokenTheXamlBindsIsScaled</c> now
    /// walks the other way round — from the XAML back to this list — because every other
    /// check here goes list-first and could not see a gap in it.</para>
    /// </summary>
    public static readonly IReadOnlyList<string> ScaledKeys = new[]
    {
        "FontSizeCaption", "FontSizeBody", "FontSizeBodyStrong", "FontSizeSubtitle",
        "FontSizeTitle", "FontSizeHeading", "FontSizeDisplay",
        "MpLabelSize", "MpBodySize", "MpMetaSize", "MpPillSize", "MpMicroSize",
        "MpSectionLabelSize", "MpStatValueSize", "MpRoomNameSize", "MpResultTitleSize",
        "MpRatingSize", "MpActivityTitleSize", "MpActivityBodySize", "MpActivityHeadlineSize",
        "MpPageTitleSize", "MpProfileNameSize", "MpProfileRatingSize",
        "MpProfileRecordSize", "MpHistoryDeltaSize",
        "WsHeadingSize", "WsBodyStrongSize", "WsBodySize", "WsLabelSize",
        "WsMonoSize", "WsBadgeSize",
        "SetSectionTitleSize", "SetBodySize", "SetControlSize", "SetDescSize",
        "SetMonoSize", "SetGroupLabelSize", "SetTinySize", "SetBadgeSize",
        "SidebarNavTextSize", "NavTabTextSize",
    };

    /// <summary>
    /// The font sizes that are deliberately NOT scaled, and the only ones allowed to be.
    ///
    /// <para>All three live in the title bar, whose height comes from a fixed token that
    /// <c>App.ApplyWindowChrome</c> also derives <c>WindowChrome.CaptionHeight</c> from.
    /// Text growing inside a caption region that cannot grow with it is the silent-breakage
    /// zone this file's header warns about: the overflow is invisible, and the part of the
    /// bar that stops being draggable is invisible too.</para>
    ///
    /// <para>It is a LIST rather than a comment because
    /// <c>EveryFontSizeTokenTheXamlBindsIsScaled</c> reads it: a new chrome size has to be
    /// named here on purpose, which is the difference between an exemption and an
    /// oversight.</para>
    /// </summary>
    public static readonly IReadOnlyList<string> UnscaledChromeKeys = new[]
    {
        "TitleBarTitleSize", "TitleBarGlyphSize", "ChromeVersionSize",
    };

    /// <summary>
    /// Logical pixels per inch above which a panel is dense enough to bump one step. See
    /// the measured cases in <see cref="Recommend"/>.
    /// </summary>
    public const double DenseLogicalPpi = 135;

    public const double MinFactor = 1.0;
    public const double MaxFactor = 1.25;

    /// <summary>
    /// The unscaled values, captured the first time <see cref="Apply"/> runs.
    ///
    /// <para>Load-bearing: <see cref="Apply"/> writes its result into
    /// <c>Application.Current.Resources</c>, which SHADOWS the merged dictionary the token
    /// was declared in. Reading the "current" value on a second call would therefore read
    /// the previous call's output and compound it, so 110 % followed by 110 % would land on
    /// 121 %.</para>
    /// </summary>
    private static readonly Dictionary<string, double> Baseline = new(StringComparer.Ordinal);

    // ------------------------------------------------------------------ pure

    /// <summary>
    /// What "Automatic" resolves to. Pure, so the curve can be argued with in a test
    /// instead of on a screenshot.
    ///
    /// <para><b>The driver is the DIAGONAL, not the pixel density</b>, and that is the
    /// non-obvious part. A 32" 1440p panel and a 24" 1080p panel have the same ~92 PPI, so a
    /// character is physically the SAME height on both — density says there is nothing to
    /// fix. What differs is how far away you sit, which grows with the panel, and that is
    /// what the reports of small text on a large desktop monitor are actually about.</para>
    ///
    /// <para>Bands rather than a formula, because bands can be read and moved. The anchor is
    /// 32" giving 1.10, and it is the ONE number here that was set by looking at the screen
    /// rather than by arithmetic. It used to be 1.15, chosen to put a large desktop panel back
    /// at the size it had shown before the tokens were reverted (11.5 x 1.15 = 13.2) — a tidy
    /// derivation that turned out to be wrong on the machine the whole setting was asked for:
    /// shipped and looked at, 13.2 read as too big. So 12.65, and the lesson is worth more than
    /// the number: every earlier version of this curve was argued in the abstract.</para>
    ///
    /// <para><b>This IS what a fresh install gets, and it went round twice.</b> Automatic
    /// raises a 32" straight back above the handoff values the tokens had just been restored
    /// to, so the default was moved to 100 to stop it deciding for people. That lasted one
    /// round: the curve only ever RAISES, so a default of 100 leaves the small screens — the
    /// ones with the least room — on the smallest type in the launcher, which is the size this
    /// project's notes call unreadable at 125/150 %. Automatic is the default again; the two
    /// top bands then came DOWN a step (1.15 -> 1.10, 1.25 -> 1.20) once it was finally seen
    /// on the reference screen. The floor of 1.0 was questioned in that same argument and kept
    /// deliberately, on the machine-by-machine table in <c>TextScaleTests</c>.</para>
    ///
    /// <para>DPI is only a SAFETY net. Windows normally answers a dense panel with a display
    /// scale and WPF's DIPs already absorb it; the bump is for somebody running a
    /// high-resolution screen at 100 %, where nothing else would.</para>
    /// </summary>
    /// <param name="diagonalInches">Physical panel diagonal, or null when it can't be read.</param>
    /// <param name="pixelWidth">Primary screen width in physical pixels.</param>
    /// <param name="pixelHeight">Primary screen height in physical pixels.</param>
    /// <param name="dpiScale">The display scale, 1.0 at 96 DPI.</param>
    public static double Recommend(double? diagonalInches, int pixelWidth, int pixelHeight, double dpiScale)
    {
        // Not knowing is not a reason to change anything. This is also the path a virtual
        // display, a remote session and a projector take — every one a case where a guess
        // would be worse than the default.
        if (diagonalInches is not double d || d <= 0 || double.IsNaN(d) || double.IsInfinity(d))
            return 1.0;

        var factor =
              d < 26 ? 1.00
            : d < 29 ? 1.05
            : d < 34 ? 1.10
            :          1.20;

        if (pixelWidth > 0 && pixelHeight > 0 && dpiScale > 0)
        {
            var diagonalPixels = Math.Sqrt((double)pixelWidth * pixelWidth
                                         + (double)pixelHeight * pixelHeight);
            // Logical pixels per inch: what the panel packs in AFTER Windows' own display
            // scaling has had its say. Around 92 on an ordinary desktop monitor.
            //
            // The threshold is 135 and it was MEASURED against the configurations people
            // actually run, not picked. A 24" 4K at 150 % lands on 122 — that is a common
            // desktop and it is effectively a 24" 1440p, so it must NOT be bumped; 120
            // caught it. What has to be caught is a laptop panel left at 100 %: a 15.6"
            // 1080p is 141 and a 13.3" 1080p is 166, and on both of those the text really
            // is tiny because Windows is not compensating.
            var logicalPpi = diagonalPixels / dpiScale / d;
            if (logicalPpi > DenseLogicalPpi) factor += 0.10;
        }

        return Math.Round(Math.Clamp(factor, MinFactor, MaxFactor), 2);
    }

    /// <summary>
    /// The saved setting turned into a factor. Anything unrecognised — including a config
    /// hand-edited to nonsense — reads as <see cref="Auto"/> rather than as a number.
    /// </summary>
    public static double Resolve(string? setting, double? diagonalInches,
                                 int pixelWidth, int pixelHeight, double dpiScale)
    {
        if (!string.IsNullOrWhiteSpace(setting)
            && !string.Equals(setting.Trim(), Auto, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(setting.Trim(), out var percent))
        {
            return Math.Round(Math.Clamp(percent / 100.0, MinFactor, MaxFactor), 2);
        }

        return Recommend(diagonalInches, pixelWidth, pixelHeight, dpiScale);
    }

    // ---------------------------------------------------------------- impure

    /// <summary>
    /// Applies a factor to every token in <see cref="ScaledKeys"/>.
    ///
    /// <para>Whole-body best-effort: a text size is not worth failing a launch over, and a
    /// key that is missing simply means that token was renamed and this one does nothing.</para>
    /// </summary>
    public static void Apply(double factor)
    {
        var app = Application.Current;
        if (app == null) return;

        try
        {
            foreach (var key in ScaledKeys)
            {
                if (!Baseline.TryGetValue(key, out var declared))
                {
                    if (app.Resources[key] is not double found) continue;
                    Baseline[key] = declared = found;
                }

                // Rounded to a half point: WPF is happy with any double, but a size of
                // 13.799999 in a diagnostic screenshot invites a bug report of its own.
                app.Resources[key] =
                    Math.Round(declared * factor * 2, MidpointRounding.AwayFromZero) / 2;
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"TextScale.Apply({factor:0.00}): {ex.Message}");
        }
    }

    /// <summary>The primary screen's size in physical pixels, and its display scale.</summary>
    public static (int Width, int Height, double DpiScale) DescribePrimaryScreen()
    {
        int w = 0, h = 0;
        var dpi = 1.0;
        try
        {
            w = GetSystemMetrics(SM_CXSCREEN);
            h = GetSystemMetrics(SM_CYSCREEN);
            dpi = GetDpiForSystem() / 96.0;
            if (dpi <= 0) dpi = 1.0;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"TextScale.DescribePrimaryScreen: {ex.Message}");
        }
        return (w, h, dpi);
    }

    /// <summary>
    /// The primary monitor's physical diagonal in inches, read from its EDID, or null.
    ///
    /// <para>EDID is the panel's own description of itself, which is the only place its
    /// physical size actually lives. <c>GetDeviceCaps(HORZSIZE)</c> looks like an answer and
    /// is derived from the DPI, so it reports the same "size" for a 24" and a 32" at the same
    /// scale. It is read from the registry rather than through WMI so this needs no
    /// <c>System.Management</c> package in a self-contained publish, and it is an HKLM
    /// <b>read</b>, which needs no elevation.</para>
    ///
    /// <para>Null on every failure, which the caller reads as "leave it at 100 %": a remote
    /// session, a virtual display and a projector all land here, and the manual setting is
    /// the way out for anyone whose panel lies about itself.</para>
    /// </summary>
    public static double? DetectPrimaryDiagonalInches()
    {
        try
        {
            var monitorId = FindPrimaryMonitorDeviceId();
            if (monitorId == null) return null;

            // Shaped like: \\?\DISPLAY#GSM5B09#5&11a5f4e6&0&UID4352#{e6f07b5f-...}
            var parts = monitorId.Split('#');
            if (parts.Length < 3) return null;

            using var key = Registry.LocalMachine.OpenSubKey(
                "SYSTEM\\CurrentControlSet\\Enum\\DISPLAY\\" + parts[1] + "\\" + parts[2]
                + "\\Device Parameters");
            if (key?.GetValue("EDID") is not byte[] edid || edid.Length < 23) return null;

            // EDID 1.x basic display parameters: byte 21 is the maximum horizontal image
            // size in cm, byte 22 the vertical. Both zero means the panel declined to say
            // (projectors do this), which is not the same as a 0" screen.
            double cmWide = edid[21], cmTall = edid[22];
            if (cmWide <= 0 || cmTall <= 0) return null;

            var inches = Math.Sqrt(cmWide * cmWide + cmTall * cmTall) / 2.54;

            // A believable panel. Outside this the EDID is junk, and junk would silently
            // pick a text size for somebody.
            return inches is >= 5 and <= 120 ? Math.Round(inches, 1) : null;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"TextScale.DetectPrimaryDiagonalInches: {ex.Message}");
            return null;
        }
    }

    private static string? FindPrimaryMonitorDeviceId()
    {
        for (uint adapter = 0; ; adapter++)
        {
            var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(null, adapter, ref dd, 0)) break;
            if ((dd.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) == 0) continue;

            var mon = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            return EnumDisplayDevices(dd.DeviceName, 0, ref mon, EDD_GET_DEVICE_INTERFACE_NAME)
                ? mon.DeviceID
                : null;
        }
        return null;
    }

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int DISPLAY_DEVICE_PRIMARY_DEVICE = 0x00000004;
    private const uint EDD_GET_DEVICE_INTERFACE_NAME = 0x00000001;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(
        string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }
}
