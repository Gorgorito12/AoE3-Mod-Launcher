using System;
using System.Runtime.InteropServices;

namespace WarsOfLibertyLauncher.Services.Multiplayer.P2P;

/// <summary>
/// P/Invoke layer for [WinDivert](https://reqrypt.org/windivert.html).
///
/// WinDivert is the userspace half of a kernel driver that captures
/// and (optionally) reinjects IP packets on Windows. We use it as the
/// foundation of the launcher's "fake LAN" — without it, AoE3's
/// LAN-discovery broadcasts never leave the host and joiners' game
/// packets never reach AoE3.
///
/// Licensing & redistribution:
///   * The userspace DLL (`WinDivert.dll`) is LGPL v3.
///   * The kernel driver (`WinDivert64.sys` / `WinDivert32.sys`) is
///     GPL v3.
///   We distribute both unchanged alongside the launcher .exe — that
///   satisfies LGPL §4 and GPL §6 for "aggregate" distribution
///   without forcing our own code to either license. Source for both
///   ships in `third_party/windivert/` in the launcher repo.
///
/// Privileges:
///   * Loading the driver requires administrator + signed driver.
///     The author ships an EV-signed `.sys`, so on Windows 10/11 with
///     default policy the driver loads silently after a one-time UAC
///     elevation by the launcher to copy it under %WINDIR%\System32.
///   * Once loaded, opening a handle is free for any process running
///     as the same user that installed it.
///
/// This file is the **bindings only**. The higher-level filter +
/// capture loop live in <see cref="VirtualLanService"/>.
/// </summary>
internal static class WinDivertNative
{
    private const string Dll = "WinDivert.dll";

    /// <summary>"All ones" handle from kernel space, matches <c>INVALID_HANDLE_VALUE</c>.</summary>
    public static readonly IntPtr InvalidHandle = new(-1);

    // ---- Layer constants (subset we use) -----------------------------
    public const int WINDIVERT_LAYER_NETWORK = 0;          // intercept on IP layer
    public const int WINDIVERT_LAYER_NETWORK_FORWARD = 1;  // forwarded packets

    // ---- Flags (subset) ----------------------------------------------
    public const ulong WINDIVERT_FLAG_SNIFF = 0x0001;      // capture without dropping
    public const ulong WINDIVERT_FLAG_DROP = 0x0002;       // drop captured packets
    public const ulong WINDIVERT_FLAG_RECV_ONLY = 0x0004;
    public const ulong WINDIVERT_FLAG_SEND_ONLY = 0x0008;
    public const ulong WINDIVERT_FLAG_NO_INSTALL = 0x0010;
    public const ulong WINDIVERT_FLAG_FRAGMENTS = 0x0020;

    // ---- Param ids (set/get) -----------------------------------------
    public const int WINDIVERT_PARAM_QUEUE_LENGTH = 0;
    public const int WINDIVERT_PARAM_QUEUE_TIME = 1;
    public const int WINDIVERT_PARAM_QUEUE_SIZE = 2;

    /// <summary>
    /// Per-packet metadata that comes back from <see cref="WinDivertRecv"/>.
    /// Layout matches the C struct; <c>StructLayout.Pack=1</c> mirrors
    /// the packed declaration in <c>windivert.h</c>.
    ///
    /// On 64-bit Windows the struct is 12 bytes:
    ///   1 byte  Layer
    ///   1 byte  Event
    ///   1 byte  Sniffed + Outbound + Loopback + Impostor + IPv6 + Reserved (bitfield, packed in 1 byte)
    ///   1 byte  ChecksumFlags (bitfield)
    ///   1 byte  Reserved
    ///   1 byte  Reserved
    ///   2 bytes IfIdx
    ///   2 bytes SubIfIdx
    ///   2 bytes padding (for 8-byte alignment in some versions)
    ///
    /// We model only the fields the launcher cares about; the bitfield
    /// gets pulled out via masks below.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Address
    {
        public long Timestamp;       // QPC counter when the packet was caught
        public uint LayerEventFlags; // pack of Layer:8 / Event:8 / Flags:8 / ChecksumFlags:8
        public uint Reserved1;
        public ulong Reserved2;
        public AddressNetwork Network;
    }

    /// <summary>Network-layer sub-struct of <see cref="Address"/>.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct AddressNetwork
    {
        public uint IfIdx;
        public uint SubIfIdx;
    }

    /// <summary>Bit positions inside <see cref="Address.LayerEventFlags"/>.</summary>
    [Flags]
    public enum AddressFlags : uint
    {
        Sniffed   = 1u << 16,
        Outbound  = 1u << 17,
        Loopback  = 1u << 18,
        Impostor  = 1u << 19,
        IPv6      = 1u << 20,
    }

    // ------------- Entry points ---------------------------------------

    /// <summary>
    /// Open a capture handle. <paramref name="filter"/> is a tiny C-like
    /// expression evaluated in the driver per packet — see WinDivert's
    /// docs. Returns <see cref="InvalidHandle"/> on failure (check
    /// <see cref="Marshal.GetLastWin32Error"/>).
    /// </summary>
    [DllImport(Dll, CallingConvention = CallingConvention.StdCall, SetLastError = true,
        CharSet = CharSet.Ansi, BestFitMapping = false)]
    public static extern IntPtr WinDivertOpen(
        string filter,
        int layer,
        short priority,
        ulong flags);

    /// <summary>
    /// Block until a packet matching the filter arrives. The driver
    /// copies the raw IP packet (Ethernet headers stripped) into
    /// <paramref name="packet"/> up to <paramref name="packetLen"/>
    /// bytes; the real length is written to <paramref name="readLen"/>.
    /// </summary>
    [DllImport(Dll, CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    public static extern bool WinDivertRecv(
        IntPtr handle,
        IntPtr packet,
        uint packetLen,
        out uint readLen,
        ref Address addr);

    /// <summary>
    /// Re-inject a packet. Used both to resume a sniffed packet that
    /// we want to keep flowing and to inject our own crafted ones (the
    /// "fake inbound from a LAN peer" path).
    /// </summary>
    [DllImport(Dll, CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    public static extern bool WinDivertSend(
        IntPtr handle,
        IntPtr packet,
        uint packetLen,
        out uint writeLen,
        ref Address addr);

    /// <summary>Close a handle (also unloads the driver if it was the last user).</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    public static extern bool WinDivertClose(IntPtr handle);

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    public static extern bool WinDivertSetParam(IntPtr handle, int param, ulong value);

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    public static extern bool WinDivertGetParam(IntPtr handle, int param, out ulong value);

    /// <summary>
    /// Returns true when <see cref="WinDivertOpen"/> can succeed —
    /// either WinDivert is already installed (DLL findable + driver
    /// signed-loadable) or we have admin rights to install it on the
    /// fly. The launcher uses this at startup to decide whether to
    /// show the "Install P2P driver" UAC bootstrap card or proceed
    /// straight to multiplayer.
    /// </summary>
    public static bool IsAvailable()
    {
        try
        {
            // A cheap probe: try to open a handle on a never-matching
            // filter at lowest priority. If the call succeeds we close
            // it immediately; if it fails the DLL is missing or the
            // driver wouldn't load.
            var h = WinDivertOpen("false", WINDIVERT_LAYER_NETWORK, 0, WINDIVERT_FLAG_SNIFF);
            if (h == InvalidHandle) return false;
            WinDivertClose(h);
            return true;
        }
        catch
        {
            // DllNotFoundException, BadImageFormatException etc. all
            // mean the driver isn't available right now. Caller will
            // surface the install card.
            return false;
        }
    }
}
