using System;
using System.Collections.Generic;
using System.Diagnostics;
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
/// True when the Radmin VPN GUI app (RvRvpnGui.exe) is running, the VPN is
/// NOT powered off (per the log's Switched On/Off toggle), AND the Radmin
/// virtual adapter is up with a 26.x.x.x IP. Two gates beyond the adapter
/// are load-bearing, because Radmin's background service (RvControlSvc)
/// keeps the adapter Up with its static identity IP regardless of the
/// app or the power toggle: (1) the GUI process must be alive (else a
/// CLOSED Radmin read as "running"); (2) the log power state must not be
/// Off (else an OPEN-but-DESCONECTADO Radmin read as "running"). Only a
/// POSITIVE log "Off" blocks — an unreadable log (Unknown) falls back to
/// app+adapter. It still does NOT imply they're joined to any specific
/// network — Radmin's per-network membership isn't reliably observable
/// from the OS, so the banner asks the user to verify the AoE3 network.
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

    // Cached Radmin power-toggle state (from the log). Read on the UI
    // thread by GetStatus; refreshed OFF-thread by MaybeRefreshPowerState
    // so the 3-second banner poll never does log IO on the UI thread —
    // mirrors the KickConnectionPing / _connectionPingMs pattern.
    private static volatile RadminPowerState s_powerState = RadminPowerState.Unknown;
    private static long s_powerStampMs;              // Environment.TickCount64 of last refresh
    private static int s_powerInFlight;              // 0/1 via Interlocked

    /// <summary>
    /// Take a snapshot of Radmin's state right now. Safe to call from
    /// any thread; performs only registry reads + NIC enumeration + a
    /// process check, all sub-millisecond. The log-based power state is
    /// read asynchronously (see <see cref="MaybeRefreshPowerState"/>) and
    /// folded in from a cache, so this stays cheap on the 3s poll.
    /// </summary>
    public static RadminStatus GetStatus()
    {
        // Keep the cached power state fresh without blocking the UI thread.
        MaybeRefreshPowerState();

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
    /// True when the Radmin VPN GUI app is running AND there's an "up"
    /// network adapter whose name contains "Radmin" with an IPv4 address
    /// in 26.0.0.0/8 (Radmin VPN's reserved CIDR).
    ///
    /// The GUI-process gate is what makes this honest: the background
    /// Radmin VPN Control Service (RvControlSvc) auto-starts at boot and
    /// keeps the 26.x adapter Up with its identity IP even after the user
    /// closes the Radmin app, so checking the adapter alone reported
    /// "running" for a shut Radmin (the false positive this method now
    /// avoids). A service-state check would NOT help — the service is
    /// always running; the signal that actually flips when the user
    /// closes Radmin is its GUI process.
    ///
    /// It still does NOT mean the user is joined to any network — Radmin
    /// assigns the same 26.x.x.x even when logged in but in zero networks.
    /// </summary>
    private static (bool running, string? ip) DetectServiceRunning()
    {
        try
        {
            // Radmin's adapter lingers Up (via RvControlSvc) after the app
            // is closed, so gate on the GUI process actually being alive.
            if (!IsAppRunning()) return (false, null);

            // The adapter ALSO stays Up with its static 26.x IP while the
            // app is open but the VPN is powered off ("Desconectado"), so
            // honour the log-derived power toggle: only a POSITIVE "Off"
            // blocks (Unknown = unreadable log ⇒ fall back to app+adapter,
            // don't cry wolf). This is the fix for the false green while
            // Radmin is switched off. See RadminLogService.GetPowerState.
            if (s_powerState == RadminPowerState.Off) return (false, null);

            var ip = TryGetAdapterIp();
            if (ip != null) return (true, ip);
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

    /// <summary>
    /// The IPv4 address (26.0.0.0/8) of an "up" Radmin adapter, or null when
    /// there's none — WITHOUT the GUI-running / power-state gates that
    /// <see cref="DetectServiceRunning"/> applies. The background service
    /// <c>RvControlSvc</c> keeps the adapter Up with its static 26.x identity
    /// IP whether the Radmin app is open, closed, or powered off
    /// ("Desconectado"), so this reads the IP that the game should bind to
    /// even when the "ready to play" banner would be red.
    ///
    /// This is why AoE3's <c>OverrideAddress</c> is bound to this IP rather
    /// than to <see cref="RadminStatus.AdapterIp"/> (which is null unless the
    /// full readiness gate passes): ligar el juego al NIC 26.x correcto es
    /// siempre mejor que dejarlo auto-elegir la wifi / VirtualBox, y si el
    /// usuario prende Radmin justo después ya queda ligado al adaptador bueno.
    /// Never throws — returns null on any enumeration error.
    /// </summary>
    /// <summary>
    /// One network interface, reduced to the four things that decide whether it is Radmin's.
    ///
    /// <para>A plain record because <see cref="NetworkInterface"/> cannot be constructed, which is
    /// why the selection below had no test at all — and the untested half is precisely the one
    /// that failed in the wild.</para>
    /// </summary>
    public sealed record AdapterCandidate(
        string Id,
        string Name,
        string Description,
        OperationalStatus Status,
        IReadOnlyList<string> IPv4Addresses)
    {
        /// <summary>The Radmin address this interface carries, or null. THE identity test.</summary>
        public string? Radmin26Ip
        {
            get
            {
                foreach (var ip in IPv4Addresses)
                    if (ip.StartsWith(Radmin26Prefix, StringComparison.Ordinal)) return ip;
                return null;
            }
        }
    }

    /// <summary>
    /// Radmin hands every machine an address in 26.0.0.0/8 and that block appears on a home PC by
    /// no other route, so it — not a name — is what says "this is the Radmin adapter".
    /// </summary>
    private const string Radmin26Prefix = "26.";

    /// <summary>
    /// Picks Radmin's adapter out of the machine's interfaces, BY ITS ADDRESS.
    ///
    /// <para><b>It used to demand that <c>nic.Name</c> contain "Radmin", and that cost a real user
    /// his multiplayer for days.</b> `Name` is the CONNECTION name — the label in Network
    /// Connections — which Windows generates ("Ethernet 2", "Ethernet 5") and anyone can rewrite;
    /// the driver string that cannot be changed is `Description`. His bundle showed
    /// `app=running power=On adapter=none` while his Radmin was on screen, online, with
    /// 26.217.215.106 and fifteen peers. The address was right there and the first line of the
    /// walk threw the interface away before looking at it.</para>
    ///
    /// <para><b><c>OperationalStatus</c> is a preference, not a requirement</b>, for the same
    /// reason: Windows routes and pings over an interface without consulting that field, and a
    /// virtual NIC with no physical media may legitimately report `Unknown` or `Dormant`. An
    /// interface that is carrying Radmin traffic is not made unusable by how its driver fills in
    /// a status word. `Up` still wins when something is `Up`.</para>
    ///
    /// <para>Name and description survive only as the last tiebreak, for the machine that somehow
    /// has two 26.x interfaces. <b>What is NOT relaxed is the address:</b> nothing without a 26.x
    /// IPv4 is ever returned, which is what stops a loosened name from selecting the wrong card —
    /// the very risk the old filter was guarding against, kept while dropping the part that
    /// misfired.</para>
    /// </summary>
    public static AdapterCandidate? SelectRadminAdapter(IEnumerable<AdapterCandidate> candidates)
    {
        AdapterCandidate? best = null;
        var bestScore = int.MinValue;

        foreach (var c in candidates)
        {
            if (c.Radmin26Ip == null) continue;

            var score = (c.Status == OperationalStatus.Up ? 2 : 0) + (MentionsRadmin(c) ? 1 : 0);
            if (score > bestScore)
            {
                best = c;
                bestScore = score;
            }
        }
        return best;
    }

    private static bool MentionsRadmin(AdapterCandidate c) =>
        c.Name.Contains("Radmin", StringComparison.OrdinalIgnoreCase)
        || c.Description.Contains("Radmin", StringComparison.OrdinalIgnoreCase);

    /// <summary>Every interface with an IPv4, in the shape the selector reasons about.</summary>
    private static List<AdapterCandidate> ReadAdapterCandidates()
    {
        var list = new List<AdapterCandidate>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            var ips = new List<string>();
            try
            {
                foreach (var uni in nic.GetIPProperties().UnicastAddresses)
                {
                    if (uni.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        ips.Add(uni.Address.ToString());
                }
            }
            catch
            {
                // One interface in an odd state must not abort the walk. It used to: the try
                // wrapped the whole loop, so a single throwing NIC hid every one after it.
                continue;
            }

            if (ips.Count == 0) continue;
            list.Add(new AdapterCandidate(
                nic.Id, nic.Name, nic.Description, nic.OperationalStatus, ips));
        }
        return list;
    }

    public static string? TryGetAdapterIp()
    {
        try
        {
            return SelectRadminAdapter(ReadAdapterCandidates())?.Radmin26Ip;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"RadminVpnService.TryGetAdapterIp: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// What the adapter walk actually saw, for the log — because <c>adapter=none</c> meant two
    /// different things and a user's bundle could not tell them apart.
    ///
    /// <para>It printed the same word whether nothing carried a 26.x address or every candidate
    /// was rejected by a filter, so the report that exposed the bug above arrived without the one
    /// fact that would have closed it. Only interfaces with an IPv4 are listed, so this stays a
    /// short line, and the caller only writes it when the state CHANGES.</para>
    /// </summary>
    public static string DescribeAdapterCandidates()
    {
        try
        {
            var candidates = ReadAdapterCandidates();
            if (candidates.Count == 0) return "no IPv4 interfaces";

            var parts = new List<string>();
            foreach (var c in candidates)
            {
                var ip = c.Radmin26Ip ?? string.Join("/", c.IPv4Addresses);
                parts.Add($"'{c.Name}' [{c.Description}] {c.Status} {ip}"
                          + (c.Radmin26Ip != null ? " <-26.x" : ""));
            }
            return string.Join(" | ", parts);
        }
        catch (Exception ex)
        {
            return $"candidate-walk-failed: {ex.Message}";
        }
    }

    /// <summary>
    /// One-line, English summary of every sub-signal behind
    /// <see cref="RadminStatus.IsServiceRunning"/> — for the diagnostic log.
    /// The record only exposes the collapsed <c>IsServiceRunning</c> boolean,
    /// so a bundle where Radmin "was open but wasn't recognized" gives no clue
    /// WHICH gate rejected it. This spells out all three: the GUI process, the
    /// log power toggle, and the 26.x adapter — plus, when the GUI process
    /// isn't detected, the list of Rv* processes that ARE running (which
    /// surfaces a process-name mismatch across Radmin versions, since the
    /// detection matches EXACTLY <c>RvRvpnGui.exe</c>). Never throws.
    /// </summary>
    public static string DescribeStateForLog()
    {
        try
        {
            var (exe, _) = FindInstallation();
            if (exe == null) return "installed=NotInstalled";

            var app = IsAppRunning();
            var appPart = app
                ? "app=running"
                : $"app=NOT-running(Rv procs: {ListRunningRvProcessNames()})";
            var power = s_powerState;      // cached; refreshed off-thread by MaybeRefreshPowerState
            var ip = TryGetAdapterIp();
            var serviceRunning = app && power != RadminPowerState.Off && ip != null;
            // When there is no address, say WHAT was on the machine. "adapter=none" alone is
            // what made a real report unanswerable: it reads the same whether nothing carried a
            // 26.x address or a filter threw the right interface away.
            var adapterPart = ip ?? $"none({DescribeAdapterCandidates()})";
            return $"installed=Installed {appPart} power={power} adapter={adapterPart} serviceRunning={serviceRunning}";
        }
        catch (Exception ex)
        {
            return $"state-probe-failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Comma-separated names of the currently-running processes that look like
    /// Radmin's (name starts with "Rv" or contains "radmin"), or "none". Used
    /// only by <see cref="DescribeStateForLog"/> when the exact
    /// <c>RvRvpnGui.exe</c> gate fails, to reveal a version whose GUI process
    /// is named differently. Never throws.
    /// </summary>
    private static string ListRunningRvProcessNames()
    {
        try
        {
            var names = new System.Collections.Generic.SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    var n = p.ProcessName;
                    if (n.StartsWith("Rv", StringComparison.OrdinalIgnoreCase) ||
                        n.Contains("radmin", StringComparison.OrdinalIgnoreCase))
                        names.Add(n);
                }
                catch { /* process exited between enumeration and read */ }
                finally { p.Dispose(); }
            }
            return names.Count == 0 ? "none" : string.Join(", ", names);
        }
        catch (Exception ex)
        {
            return $"enum-failed: {ex.Message}";
        }
    }

    /// <summary>
    /// True when the Radmin VPN GUI process (RvRvpnGui.exe) is alive — the
    /// user-facing "is Radmin open" signal. It's running whether the window
    /// is shown OR minimised to the system tray, and gone once the user
    /// exits Radmin fully. Deliberately does NOT count RvGuiStarter.exe
    /// (a transient launch stub, documented in <see cref="FindInstallation"/>
    /// as a zombie risk) nor RvControlSvc (the always-on background service,
    /// which is exactly why the adapter alone isn't a reliable signal).
    /// </summary>
    private static bool IsAppRunning()
    {
        try
        {
            var ps = Process.GetProcessesByName("RvRvpnGui");
            try { return ps.Length > 0; }
            finally { foreach (var p in ps) p.Dispose(); }
        }
        catch (Exception ex)
        {
            // Never let a Process query break the 3-second polling loop.
            DiagnosticLog.Write($"RadminVpnService.IsAppRunning: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Refresh the cached Radmin power state from the log OFF the UI thread,
    /// at most every ~2 s and never concurrently. Fire-and-forget: reading
    /// a ~1 MB rotated log every 3s poll on the UI thread would risk jank,
    /// so <see cref="GetStatus"/> reads the cache and this keeps it warm —
    /// same shape as the launcher's KickConnectionPing. Nothing here throws.
    /// </summary>
    private static void MaybeRefreshPowerState()
    {
        var now = Environment.TickCount64;
        if (now - System.Threading.Volatile.Read(ref s_powerStampMs) < 2000) return;
        if (System.Threading.Interlocked.CompareExchange(ref s_powerInFlight, 1, 0) != 0) return;
        _ = Task.Run(() =>
        {
            try { s_powerState = RadminLogService.GetPowerState(); }
            catch (Exception ex) { DiagnosticLog.Write($"RadminVpnService.MaybeRefreshPowerState: {ex.Message}"); }
            finally
            {
                System.Threading.Volatile.Write(ref s_powerStampMs, Environment.TickCount64);
                System.Threading.Interlocked.Exchange(ref s_powerInFlight, 0);
            }
        });
    }

    /// <summary>
    /// Total bytes (sent, received) on the Radmin VPN adapter since it
    /// came up, or null when there's no "up" Radmin 26.x adapter. The
    /// counters are OS-level — the whole adapter, not just one game — so
    /// callers wanting match-only traffic should snapshot a baseline when
    /// the match starts and display the delta.
    /// </summary>
    public static (long sent, long received)? GetAdapterBytes()
    {
        try
        {
            // The SAME selection TryGetAdapterIp makes, by id — this used to be a
            // character-for-character copy of the four filters, which is two places to get the
            // identity of the adapter wrong instead of one.
            var chosen = SelectRadminAdapter(ReadAdapterCandidates());
            if (chosen == null) return null;

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!string.Equals(nic.Id, chosen.Id, StringComparison.Ordinal)) continue;
                var stats = nic.GetIPv4Statistics();
                return (stats.BytesSent, stats.BytesReceived);
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"RadminVpnService.GetAdapterBytes: {ex.Message}");
        }
        return null;
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
    //
    // NOTE: "are you in network X" (membership) is a different question
    // from "how many peers are visible" (peer count) and has a much
    // more reliable answer. RadminLogService tails Radmin's own
    // service.log for "You joined gaming network 'X'" / "You left
    // network 'X'" events, which is what RadminAssistantService.ProbeAsync
    // now uses to promote LoggedIn → InAoE3Network. The seed-peer ping
    // sits behind it as a fallback for the rare machine where
    // ProgramData isn't readable.

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
    /// <param name="confirmSpace">
    /// Called with the MSI's size once the response headers arrive and BEFORE any of it is
    /// written, so the caller can check free space and ask the user. Returning false aborts and
    /// this method returns false — which lands on the documented "fall back to the browser
    /// download page" path, exactly where someone who cancelled for lack of space should end up.
    /// Optional; null skips the check.
    /// </param>
    public static async Task<bool> InstallSilentAsync(
        IProgress<int>? progress,
        CancellationToken ct,
        Func<long, bool>? confirmSpace = null)
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

                // The size was already being read here to drive the progress bar; comparing it
                // against free space costs nothing extra. No Content-Length (0) means unknown,
                // and unknown never warns.
                if (confirmSpace != null && !confirmSpace(total))
                {
                    DiagnosticLog.Write("RadminVpnService.InstallSilentAsync: cancelled — not enough disk space.");
                    return false;
                }

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
