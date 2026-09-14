using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// One system-wide hotkey, alive only while something explicitly holds it.
///
/// <para>Exists because the thing it triggers has to happen while ANOTHER application —
/// AoE3, usually full-screen — has the keyboard. A WPF <c>KeyBinding</c> only fires while
/// the launcher is focused, which is precisely when the player does not need it.</para>
///
/// <para><b>Scope is the safety story.</b> This registers exactly one combination, only
/// while the caller keeps the instance, and <see cref="Dispose"/> gives it back to the
/// system. It is NOT a keyboard hook: <c>RegisterHotKey</c> asks Windows to tell us about
/// one chord and gives us no visibility into any other key, which is what keeps
/// <c>docs/AUDIT.md</c>'s "no keylogging" line true — see the note there. Do not replace
/// it with <c>SetWindowsHookEx</c>/<c>WH_KEYBOARD_LL</c> to "make it more reliable"; that
/// swaps a narrow, declared permission for the ability to see everything the user types,
/// and the antivirus heuristics this project already fights read the difference.</para>
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    /// <summary>Ctrl, as <c>RegisterHotKey</c> spells it.</summary>
    public const uint ModControl = 0x0002;
    /// <summary>Shift.</summary>
    public const uint ModShift = 0x0004;
    /// <summary>Don't repeat while the chord is held — one press, one event. Without it a
    /// player leaning on the keys queues a burst of pastes into the game.</summary>
    public const uint ModNoRepeat = 0x4000;

    /// <summary>Virtual-key code for J, the default second half of the chord.</summary>
    public const uint VkJ = 0x4A;

    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly HwndSource _source;
    private readonly int _id;
    private readonly Action _onPressed;
    private bool _disposed;

    private GlobalHotkey(HwndSource source, int id, Action onPressed)
    {
        _source = source;
        _id = id;
        _onPressed = onPressed;
        _source.AddHook(Hook);
    }

    /// <summary>
    /// Claim the chord. Returns null when the combination is already taken by another
    /// application (a very common outcome, and NOT an error worth a dialog — the caller
    /// reports it in the log and the feature simply stays off for that session).
    /// </summary>
    /// <param name="modifiers">Bitwise OR of the <c>Mod*</c> constants.</param>
    /// <param name="virtualKey">Virtual-key code of the chord's main key.</param>
    /// <param name="onPressed">Raised on the UI thread each time the chord is pressed.</param>
    /// <param name="failure">Short English reason for the diagnostic log on null.</param>
    public static GlobalHotkey? TryRegister(uint modifiers, uint virtualKey, Action onPressed, out string failure)
    {
        try
        {
            // A message-only window (HWND_MESSAGE as the parent): it never renders, never
            // appears in the taskbar or Alt+Tab, and still receives the posted WM_HOTKEY.
            var parameters = new HwndSourceParameters("Aoe3ModLauncher.Hotkey")
            {
                ParentWindow = (IntPtr)(-3), // HWND_MESSAGE
            };
            var source = new HwndSource(parameters);

            // Unique per instance so two registrations in one process cannot collide.
            var id = System.Threading.Interlocked.Increment(ref s_nextId);

            if (!RegisterHotKey(source.Handle, id, modifiers | ModNoRepeat, virtualKey))
            {
                var err = Marshal.GetLastWin32Error();
                source.Dispose();
                // 1409 == ERROR_HOTKEY_ALREADY_REGISTERED. Named because it is the
                // expected failure, not a bug: some other app owns the chord.
                failure = err == 1409
                    ? "the shortcut is already taken by another application"
                    : $"RegisterHotKey failed (error {err})";
                return null;
            }

            failure = string.Empty;
            return new GlobalHotkey(source, id, onPressed);
        }
        catch (Exception ex)
        {
            failure = ex.Message;
            return null;
        }
    }

    private static int s_nextId = 0xA0E3;

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == _id)
        {
            handled = true;
            // The callback runs user code moments before a match; a throw here would
            // travel up through the window procedure, so it is swallowed and logged.
            try { _onPressed(); }
            catch (Exception ex) { DiagnosticLog.Write($"GlobalHotkey callback: {ex.Message}"); }
        }
        return IntPtr.Zero;
    }

    /// <summary>Give the chord back to the system. Safe to call more than once.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _source.RemoveHook(Hook);
            UnregisterHotKey(_source.Handle, _id);
            _source.Dispose();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"GlobalHotkey.Dispose: {ex.Message}");
        }
    }
}
