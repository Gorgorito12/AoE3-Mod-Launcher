using System;
using System.Runtime.InteropServices;
using System.Text;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Sends a short, user-triggered keystroke burst to the AoE3 window so a joiner never
/// has to read the host's Radmin IP off one screen and retype it into another.
///
/// <para><b>Read this before changing anything here.</b> This is the one place in the
/// launcher that synthesises keyboard input, and <c>docs/AUDIT.md</c> used to advertise
/// that no such code existed. It is deliberately the smallest thing that does the job:
/// no mouse, no coordinates, no screen reading, no hooks, no injection, and nothing at
/// all unless the player presses the hotkey themselves. Widening it past that is what
/// would turn the launcher into something an antivirus is right to distrust.</para>
///
/// <para><b>THE SAFETY PROPERTY, and it is the whole design: this never steals focus and
/// refuses to type unless AoE3 is ALREADY the foreground window.</b> The obvious shape —
/// <c>SetForegroundWindow</c> then type — is the dangerous one. Focus changes race: the
/// window can lose it between the call and the keystrokes, and then the burst lands in
/// whatever is in front, which on a normal desktop is a chat client or a password box.
/// Typing an address into Discord is merely embarrassing; the same burst carrying
/// <c>{ENTER}</c> into someone's unlocked session is not. So the check is not a
/// nicety — it is what bounds the blast radius to "the window the player is looking at".
/// Refusing is always safe: the address is on the clipboard too, so the player pastes.</para>
///
/// <para>Nothing here throws. Every entry point returns false with a reason for the log,
/// because this runs moments before a match and must never be able to take the launcher
/// down or block a launch.</para>
/// </summary>
public static class GameWindowInput
{
    // ---- Win32 ----------------------------------------------------------------

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_V = 0x56;
    private const ushort VK_RETURN = 0x0D;

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    // ---- Public API -------------------------------------------------------------

    /// <summary>
    /// True when the window the user is currently looking at belongs to
    /// <paramref name="processId"/> — the precondition every send here is gated on.
    /// </summary>
    /// <param name="processId">The pid the launcher spawned, from the launch result.</param>
    public static bool GameIsInForeground(int processId)
    {
        if (processId <= 0) return false;
        try
        {
            var fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            _ = GetWindowThreadProcessId(fg, out var fgPid);
            return fgPid == (uint)processId;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"GameWindowInput.GameIsInForeground: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Ctrl+V into the focused control of the AoE3 window, optionally followed by Enter.
    ///
    /// <para>Paste rather than typing the characters one by one: the user confirmed the
    /// game's address box accepts Ctrl+V, whereas synthesised per-character input is
    /// unverified against an engine this old (a 2007 title may read text through
    /// DirectInput, where a <c>KEYEVENTF_UNICODE</c> burst produces nothing at all and
    /// fails SILENTLY — the worst shape for something a player triggers mid-launch).
    /// <see cref="TryTypeText"/> exists as the fallback if paste ever proves flaky, but
    /// paste is the measured path and should stay the default.</para>
    /// </summary>
    /// <param name="processId">The AoE3 pid. The send is refused unless this process owns
    /// the foreground window.</param>
    /// <param name="pressEnter">Send Enter after the paste (confirms the address box).</param>
    /// <param name="failure">A short English reason for the diagnostic log on false.</param>
    public static bool TryPasteIntoGame(int processId, bool pressEnter, out string failure)
    {
        if (!GameIsInForeground(processId))
        {
            failure = "AoE3 is not the foreground window";
            return false;
        }

        var keys = new System.Collections.Generic.List<INPUT>(6);
        AddKey(keys, VK_CONTROL, down: true);
        AddKey(keys, VK_V, down: true);
        AddKey(keys, VK_V, down: false);
        AddKey(keys, VK_CONTROL, down: false);
        if (pressEnter)
        {
            AddKey(keys, VK_RETURN, down: true);
            AddKey(keys, VK_RETURN, down: false);
        }

        return Send(keys.ToArray(), out failure);
    }

    /// <summary>
    /// Type <paramref name="text"/> character by character as Unicode input. The fallback
    /// for a setup where Ctrl+V does not reach the game's address box; see
    /// <see cref="TryPasteIntoGame"/> for why that is the default instead.
    /// </summary>
    public static bool TryTypeText(int processId, string text, bool pressEnter, out string failure)
    {
        if (string.IsNullOrEmpty(text))
        {
            failure = "nothing to type";
            return false;
        }
        if (!GameIsInForeground(processId))
        {
            failure = "AoE3 is not the foreground window";
            return false;
        }

        var keys = new System.Collections.Generic.List<INPUT>(text.Length * 2 + 2);
        foreach (var ch in text)
        {
            AddUnicode(keys, ch, down: true);
            AddUnicode(keys, ch, down: false);
        }
        if (pressEnter)
        {
            AddKey(keys, VK_RETURN, down: true);
            AddKey(keys, VK_RETURN, down: false);
        }

        return Send(keys.ToArray(), out failure);
    }

    /// <summary>The foreground window's title, for the log line that explains a refusal.
    /// Never throws; empty when it cannot be read.</summary>
    public static string DescribeForegroundWindow()
    {
        try
        {
            var fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return "(none)";
            _ = GetWindowThreadProcessId(fg, out var pid);
            var sb = new StringBuilder(256);
            var n = GetWindowText(fg, sb, sb.Capacity);
            var title = n > 0 ? sb.ToString() : "(untitled)";
            return $"pid={pid} '{title}'";
        }
        catch
        {
            return "(unreadable)";
        }
    }

    // ---- Internals ---------------------------------------------------------------

    private static bool Send(INPUT[] inputs, out string failure)
    {
        try
        {
            var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            if (sent != inputs.Length)
            {
                // The usual cause is UIPI: a process at a lower integrity level cannot
                // send input to a more privileged window. That is exactly the shape of
                // an AoE3 launched elevated (the compat-layer path) beside an asInvoker
                // launcher, so name it rather than printing a bare error number.
                var err = Marshal.GetLastWin32Error();
                failure = err == 5
                    ? "blocked by Windows (UIPI) — AoE3 is running elevated and the launcher is not"
                    : $"SendInput sent {sent}/{inputs.Length} (error {err})";
                return false;
            }
            failure = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            failure = ex.Message;
            return false;
        }
    }

    private static void AddKey(System.Collections.Generic.List<INPUT> into, ushort vk, bool down)
    {
        // Carry the scancode as well as the virtual key. A 2007 engine may read either,
        // and supplying both is what makes the same burst work on the menu (Windows
        // messages) and on anything reading scancodes.
        var scan = (ushort)MapVirtualKey(vk, 0 /* MAPVK_VK_TO_VSC */);
        into.Add(new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = scan,
                    dwFlags = down ? 0 : KEYEVENTF_KEYUP,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                },
            },
        });
    }

    private static void AddUnicode(System.Collections.Generic.List<INPUT> into, char ch, bool down)
    {
        into.Add(new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = ch,
                    dwFlags = KEYEVENTF_UNICODE | (down ? 0 : KEYEVENTF_KEYUP),
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                },
            },
        });
    }
}
