using System;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace WarsOfLibertyLauncher.Services;

public enum RadminInstallState
{
    NotInstalled,
    Installed,
}

/// <summary>
/// Snapshot of Radmin VPN's state at one moment in time. Held by the
/// UI so it can render a reactive banner that shows the right action
/// (install / open / join / nothing) without having to re-query the
/// registry + network adapters from XAML.
/// </summary>
/// <param name="InstallState">Whether Radmin's MSI is installed.</param>
/// <param name="ExePath">Path to RvGuiStarter.exe / RvRvpnGui.exe when installed; null otherwise.</param>
/// <param name="Version">Installed version string from the uninstall registry.</param>
/// <param name="IsServiceRunning">
/// True when the Radmin VPN virtual adapter is up with a 26.x.x.x IP.
/// That means the user's Radmin client has signed in to Famatech and
/// received its identity IP — but does NOT imply they're joined to any
/// specific network. Radmin's per-network membership lives inside its
/// own process and isn't reliably observable from the OS, so the
/// banner asks the user to verify the AoE3 network themselves.
/// </param>
/// <param name="AdapterIp">The user's own 26.x.x.x address when the service is running.</param>
public sealed record RadminStatus(
    RadminInstallState InstallState,
    string? ExePath,
    string? Version,
    bool IsServiceRunning,
    string? AdapterIp);

/// <summary>
/// Detect, launch and (optionally) silently install Radmin VPN.
/// Radmin VPN is the user-facing virtual LAN the launcher's multiplayer
/// layer expects: the lobby/chat/auth go through our self-hosted
/// backend, but the actual game traffic (AoE3 LAN discovery) needs the
/// 26.x.x.x Radmin overlay so both peers can see each other.
///
/// We can't fully automate "join the AoE3 TAD network" because Radmin
/// has no public CLI / URL scheme / file-import flow that doesn't carry
/// the user's RID, and network membership is stored server-side at
/// Famatech (the registry keys under HKLM\...\RadminVPN\1.0\Networks
/// are empty GUID placeholders). So the best we can do is:
///
///   * Detect whether Radmin is installed
///   * Optionally silent-install it from Famatech's MSI
///   * Detect whether the user's Radmin adapter has a 26.x.x.x IP
///     (= currently connected to ANY Radmin network)
///   * Launch the Radmin GUI on demand
///   * Pre-copy the AoE3 TAD network name to the clipboard so the
///     user only has to Ctrl+V into "Join network" instead of typing
///
/// The UI polls <see cref="GetStatus"/> on a 3-second timer while the
/// Multiplayer tab is visible — cheap (registry + NIC enumeration take
/// microseconds) and keeps the banner in sync with manual state
/// changes the user makes in Radmin's own window.
/// </summary>
public static class RadminVpnService
{
    /// <summary>
    /// Canonical Famatech download URL. Redirects to the latest stable
    /// MSI at the time the request is made, so we don't hard-code a
    /// version that goes stale every few months.
    /// </summary>
    private const string MsiUrl = "https://download.radmin-vpn.com/download/files/Radmin_VPN.msi";

    /// <summary>
    /// Name of the community Radmin network the AoE3 modding scene
    /// gathers on. Used as the clipboard payload when the user clicks
    /// "Open Radmin" so they can paste it straight into Radmin's
    /// "Join network" dialog instead of typing.
    /// </summary>
    public const string AoE3TadNetworkName = "Age of Empires III: The Asian Dynasties";

    /// <summary>
    /// Take a snapshot of Radmin's state right now. Safe to call from
    /// any thread; performs only registry reads + NIC enumeration, both
    /// of which are sub-millisecond on a typical desktop.
    /// </summary>
    public static RadminStatus GetStatus()
    {
        var (exe, version) = FindInstallation();
        if (exe == null)
        {
            return new RadminStatus(RadminInstallState.NotInstalled, null, null, false, null);
        }
        var (running, ip) = DetectServiceRunning();
        return new RadminStatus(RadminInstallState.Installed, exe, version, running, ip);
    }

    /// <summary>
    /// Locate the Radmin install via the Windows uninstall registry.
    /// We prefer this over hard-coding the Program Files path because
    /// some users install to D:\ or to a portable location, and the
    /// uninstall entry is the source of truth Windows itself uses.
    /// Returns (exePath, version) or (null, null) when not installed.
    /// </summary>
    private static (string? exe, string? version) FindInstallation()
    {
        string[] uninstallRoots =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        };
        foreach (var root in uninstallRoots)
        {
            using var key = Registry.LocalMachine.OpenSubKey(root);
            if (key == null) continue;
            foreach (var subName in key.GetSubKeyNames())
            {
                using var sub = key.OpenSubKey(subName);
                if (sub == null) continue;
                var name = sub.GetValue("DisplayName") as string;
                if (string.IsNullOrEmpty(name)) continue;
                if (!name.StartsWith("Radmin VPN", StringComparison.OrdinalIgnoreCase)) continue;

                var loc = sub.GetValue("InstallLocation") as string;
                var version = sub.GetValue("DisplayVersion") as string;
                if (string.IsNullOrEmpty(loc)) continue;

                // Prefer RvRvpnGui.exe (the main Qt process — actually
                // opens the window). RvGuiStarter.exe is a stub
                // intended for Start-menu shortcuts that relies on the
                // Radmin VPN Control Service being in a specific
                // state, and we've observed it sit there as a zombie
                // process without ever launching the GUI when called
                // from outside its expected context (e.g. via
                // Process.Start from the launcher). Falling back to
                // RvGuiStarter only if the main GUI binary is missing
                // — that should never happen on a healthy install but
                // gives us a graceful degradation path.
                string[] candidates =
                {
                    Path.Combine(loc, "RvRvpnGui.exe"),
                    Path.Combine(loc, "RvGuiStarter.exe"),
                };
                foreach (var c in candidates)
                {
                    if (File.Exists(c)) return (c, version);
                }
            }
        }
        return (null, null);
    }

    /// <summary>
    /// True when there's an "up" network adapter whose name contains
    /// "Radmin" AND that has an IPv4 address in 26.0.0.0/8 (Radmin
    /// VPN's reserved CIDR). That means the Radmin VPN Control Service
    /// is running AND the client has signed in to Famatech and been
    /// assigned its identity IP. It does NOT mean the user is joined
    /// to any network — Radmin will assign you the same 26.x.x.x even
    /// when you're logged in but in zero networks.
    /// </summary>
    private static (bool running, string? ip) DetectServiceRunning()
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!nic.Name.Contains("Radmin", StringComparison.OrdinalIgnoreCase)) continue;
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var uni in nic.GetIPProperties().UnicastAddresses)
                {
                    if (uni.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;
                    var ipStr = uni.Address.ToString();
                    if (ipStr.StartsWith("26.", StringComparison.Ordinal)) return (true, ipStr);
                }
            }
        }
        catch (Exception ex)
        {
            // NIC enumeration occasionally throws on machines with WMI
            // service issues; treat as "not running" instead of
            // crashing the polling loop.
            DiagnosticLog.Write($"RadminVpnService.DetectServiceRunning: {ex.Message}");
        }
        return (false, null);
    }

    // NOTE: peer-count detection was removed. We tried to infer "is the
    // user in an active Radmin network with N other people" by counting
    // 26.x.x.x peers visible via arp -a / Get-NetNeighbor. The reality:
    // Radmin learns its peer list from a private TCP control channel to
    // its server at 148.113.190.78 — that information is held entirely
    // inside the Radmin VPN process and never surfaces to the OS until
    // there's actual IP traffic with a specific peer. Even after
    // joining a network of 20+ active members, the local ARP cache
    // typically only contains 1-2 peers (those the user previously
    // exchanged packets with). Broadcast pings don't help because
    // Radmin peers don't respond to ICMP broadcast.
    //
    // So the banner can't truthfully say "you're connected to the AoE3
    // network with N peers". It now reports a simpler honest signal —
    // "Radmin's service is running and you have a 26.x.x.x identity" —
    // and asks the user to verify network membership themselves in the
    // Radmin window. Reading the Radmin GUI via UI Automation would
    // give us the real peer list but breaks on every Radmin update
    // (and runs afoul of Famatech's TOS).

    /// <summary>
    /// Launch the Radmin GUI. Process.Start with UseShellExecute lets
    /// Windows handle "bring existing window to front" if Radmin is
    /// already running — we don't have to detect a second instance
    /// ourselves.
    /// </summary>
    public static bool LaunchGui(string exePath)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty,
            });
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"RadminVpnService.LaunchGui: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Open Famatech's download page in the user's browser. Used as a
    /// graceful fallback when <see cref="InstallSilentAsync"/> fails
    /// (download blocked by AV, msiexec refuses UAC, etc.).
    /// </summary>
    public static void OpenDownloadPageInBrowser()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://www.radmin-vpn.com/",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"RadminVpnService.OpenDownloadPageInBrowser: {ex.Message}");
        }
    }

    /// <summary>
    /// Download Radmin's MSI and run a silent install. Triggers a UAC
    /// prompt because the MSI installs a system service + driver. The
    /// progress reports go 0..90 during download and bump to 100 once
    /// msiexec exits.
    /// </summary>
    /// <returns>
    /// <c>true</c> when msiexec exited with code 0 (success). <c>false</c>
    /// on any failure — caller should fall back to opening the browser
    /// download page so the user can install manually.
    /// </returns>
    public static async Task<bool> InstallSilentAsync(
        IProgress<int>? progress,
        CancellationToken ct)
    {
        var tmpPath = Path.Combine(Path.GetTempPath(), "RadminVPN_setup.msi");
        try
        {
            // 1. Download MSI to %TEMP%.
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Aoe3ModLauncher/1.0");
                using var resp = await http.GetAsync(
                    MsiUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    ct);
                resp.EnsureSuccessStatusCode();
                var total = resp.Content.Headers.ContentLength ?? 0L;

                await using var fs = File.Create(tmpPath);
                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                var buf = new byte[64 * 1024];
                long downloaded = 0;
                int read;
                while ((read = await stream.ReadAsync(buf.AsMemory(0, buf.Length), ct)) > 0)
                {
                    await fs.WriteAsync(buf.AsMemory(0, read), ct);
                    downloaded += read;
                    if (total > 0 && progress != null)
                    {
                        // Reserve the last 10% for the msiexec phase
                        // so the bar doesn't sit at 100% while the user
                        // waits for the installer to finish.
                        progress.Report((int)(downloaded * 90 / total));
                    }
                }
            }

            // 2. Run msiexec /qn (silent, no UI). Verb="runas" triggers
            //    the UAC prompt — required because the MSI installs the
            //    Radmin VPN Control Service + the TAP driver.
            progress?.Report(92);
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "msiexec.exe",
                Arguments = $"/i \"{tmpPath}\" /qn /norestart",
                UseShellExecute = true,
                Verb = "runas",
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return false;
            await proc.WaitForExitAsync(ct);
            progress?.Report(100);

            var ok = proc.ExitCode == 0;
            if (!ok)
            {
                DiagnosticLog.Write($"RadminVpnService.InstallSilentAsync: msiexec exit={proc.ExitCode}");
            }
            return ok;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"RadminVpnService.InstallSilentAsync: {ex.Message}");
            return false;
        }
        finally
        {
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); }
            catch { /* best effort */ }
        }
    }
}
