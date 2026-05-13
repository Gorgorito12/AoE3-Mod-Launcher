using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Voobly-style virtual network adapter.
///
/// What it does: installs / reuses Microsoft's KM-TEST Loopback
/// Adapter (ships with Windows since 7, no third-party driver), names
/// it <c>WoL Multiplayer</c>, assigns it a deterministic
/// <c>10.147.x.y</c> address derived from the user's id, and lowers
/// its interface metric so AoE3 enumerates it FIRST when reporting
/// the host's IP in its multiplayer lobby UI. Net effect: the IP
/// printed next to your name in AoE3's hosting screen reads
/// <c>10.147.x.y</c> instead of your real LAN address (192.168.x.x).
///
/// This is purely cosmetic — peers still receive your real public IP
/// over the <c>peer_announce</c> WS frame for hole-punching, and game
/// packets still flow through the WinDivert bridge. To actually hide
/// your public IP from peers, use the "relay-only" mode (option C in
/// the launcher conversation).
///
/// All operations run via PowerShell because:
///   * <c>pnputil</c> is built-in since Win7 — no <c>devcon</c> needed.
///   * <c>Get-NetAdapter</c> / <c>New-NetIPAddress</c> /
///     <c>Set-NetIPInterface</c> ship with Windows 10+ out of the box.
///   * Keeps the launcher free of native interop for what's a once-
///     per-machine setup step.
/// </summary>
public static class VirtualAdapterService
{
    /// <summary>Display name we'll give the adapter after install.</summary>
    public const string AdapterName = "WoL Multiplayer";

    /// <summary>
    /// Cheap probe: does an adapter with our chosen name already exist?
    /// Used at startup to decide whether to surface the "Install
    /// virtual adapter" UI vs proceeding directly.
    /// </summary>
    public static async Task<bool> IsInstalledAsync(CancellationToken ct = default)
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.Name.Equals(AdapterName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        // Async-friendly no-op so signature works as a Task. Probing
        // NIC enumeration is fast (millisecond-scale) and synchronous;
        // wrapping it lets callers await uniformly with the install
        // path below which IS slow.
        await Task.CompletedTask;
        return false;
    }

    /// <summary>
    /// Install the KM-TEST Loopback driver if needed and create one
    /// instance, then rename it to <see cref="AdapterName"/>. Requires
    /// admin rights — pops a UAC prompt the first time. Idempotent:
    /// if the adapter already exists, returns success immediately.
    /// </summary>
    public static async Task<bool> InstallAsync(CancellationToken ct = default)
    {
        if (await IsInstalledAsync(ct)) return true;

        // PowerShell flow:
        //   1. pnputil installs/refreshes the netloop.inf driver
        //      (Microsoft KM-TEST Loopback Adapter).
        //   2. Class GUID for network adapters is the well-known
        //      {4d36e972-...}; we add a new device instance via
        //      `pnputil /add-driver ... /install`.
        //   3. Rename the freshly-created instance to our convention.
        //
        // pnputil since Windows 10 1809 supports `/add-driver` +
        // `/install`. Earlier Windows versions need `devcon.exe`,
        // which we don't ship to keep the bundle small. Document the
        // minimum supported OS as Win10 1809 (Oct 2018) — AoE3 mods
        // are not running on older anyway.
        var script = $@"
$ErrorActionPreference = 'Stop'
$inf = Join-Path $env:WINDIR 'inf\netloop.inf'
if (-not (Test-Path $inf)) {{
    Write-Error 'netloop.inf not found — Windows 10/11 expected.'
    exit 2
}}

# Capture existing loopback adapters BEFORE install so we can spot
# the one created by this run.
$before = Get-NetAdapter -ErrorAction SilentlyContinue |
    Where-Object {{ $_.InterfaceDescription -like '*KM-TEST Loopback*' }} |
    ForEach-Object {{ $_.InterfaceGuid }}

pnputil.exe /add-driver $inf /install | Out-Null

# Trigger a PnP scan to create an instance of the freshly-installed
# driver. The classic devcon command is `devcon install <inf> *MSLOOP`
# but we can approximate it by importing Hardware Device Wizard...
# easier path: hint via INF rescan + give Windows a moment.
Start-Sleep -Seconds 2

# Find the new loopback adapter.
$after = Get-NetAdapter -ErrorAction SilentlyContinue |
    Where-Object {{ $_.InterfaceDescription -like '*KM-TEST Loopback*' }}
$new = $after | Where-Object {{ $_.InterfaceGuid -notin $before }} | Select-Object -First 1

if (-not $new) {{
    # pnputil installed the driver but Windows did not auto-create
    # a device instance. The user must run `hdwwiz` once to add
    # 'Microsoft KM-TEST Loopback Adapter' from the legacy hardware
    # wizard. We surface a clear error so the UI can guide them.
    Write-Error 'Driver installed but no adapter instance created. Run hdwwiz.exe and add Microsoft KM-TEST Loopback Adapter manually, then retry.'
    exit 3
}}

Rename-NetAdapter -Name $new.Name -NewName '{AdapterName}' -ErrorAction SilentlyContinue
exit 0
";
        var exit = await RunPowerShellElevatedAsync(script, ct);
        if (exit != 0)
        {
            DiagnosticLog.Write($"VirtualAdapterService.InstallAsync: exit {exit}");
            return false;
        }
        return await IsInstalledAsync(ct);
    }

    /// <summary>
    /// Configure the existing adapter: clear prior IPv4 addresses,
    /// assign <paramref name="ip"/>/24, set its interface metric to
    /// 1 (highest priority) so AoE3's NIC enumeration sees this
    /// adapter first.
    /// </summary>
    public static async Task<bool> ConfigureAsync(IPAddress ip, CancellationToken ct = default)
    {
        if (!await IsInstalledAsync(ct)) return false;

        var ipStr = ip.ToString();
        var script = $@"
$ErrorActionPreference = 'Stop'
$name = '{AdapterName}'

# Remove any prior IPv4 assignments. We don't care about errors —
# the adapter may already be clean.
Get-NetIPAddress -InterfaceAlias $name -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Remove-NetIPAddress -Confirm:$false -ErrorAction SilentlyContinue

# Assign our derived address. /24 mask matches the 10.147.x.0/24
# range the rest of the launcher's P2P code uses for peer IPs.
New-NetIPAddress -InterfaceAlias $name -IPAddress '{ipStr}' -PrefixLength 24 -ErrorAction Stop | Out-Null

# Lowest metric wins in Windows route ordering. AoE3's display
# enumeration usually picks the lowest metric NIC first.
Set-NetIPInterface -InterfaceAlias $name -InterfaceMetric 1 -ErrorAction SilentlyContinue
exit 0
";
        var exit = await RunPowerShellElevatedAsync(script, ct);
        if (exit != 0)
        {
            DiagnosticLog.Write($"VirtualAdapterService.ConfigureAsync: exit {exit}");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Derive the same FNV-1a-based 10.147.x.y address
    /// <see cref="VirtualLanService"/> uses for peer IPs, so the
    /// host's own self-IP plays by the same rules as how peers see
    /// each other. Stable across launcher restarts given the same
    /// user id.
    /// </summary>
    public static IPAddress DeriveIpFor(string userId)
    {
        if (string.IsNullOrEmpty(userId))
            throw new ArgumentException("userId required", nameof(userId));
        uint hash = 2166136261;
        foreach (var c in userId) hash = (hash ^ c) * 16777619;
        byte x = (byte)((hash >> 8) & 0xFF);
        byte y = (byte)(hash & 0xFF);
        if (x == 0) x = 1;
        if (y == 0) y = 1;
        return new IPAddress(new byte[] { 10, 147, x, y });
    }

    private static async Task<int> RunPowerShellElevatedAsync(string script, CancellationToken ct)
    {
        // Encode the script as base64 so we can pass it on the
        // command line without worrying about quoting hell — the
        // -EncodedCommand parameter takes UTF-16LE base64.
        var bytes = System.Text.Encoding.Unicode.GetBytes(script);
        var encoded = Convert.ToBase64String(bytes);

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            UseShellExecute = true,
            Verb = "runas",                 // UAC prompt
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        try
        {
            using var p = Process.Start(psi);
            if (p == null) return -1;
            await p.WaitForExitAsync(ct);
            return p.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception wex) when (wex.NativeErrorCode == 1223)
        {
            // 1223 = user clicked No on UAC.
            return -2;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"VirtualAdapterService.RunPowerShell: {ex.Message}");
            return -3;
        }
    }
}
