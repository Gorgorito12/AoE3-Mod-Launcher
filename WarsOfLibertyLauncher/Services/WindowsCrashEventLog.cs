using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Reads the Windows Application log for the event a real crash leaves behind: Application
/// Error, id 1000, naming the executable, the faulting module, the exception code and — on
/// every modern Windows — the process id in hex.
///
/// <para><b>Through <c>wevtutil</c>, on purpose.</b> <c>System.Diagnostics.EventLog</c> is a
/// NuGet package on .NET 8 and this launcher ships one self-contained binary that already fights
/// its size; <c>wevtutil</c> is on every Windows since Vista and answers the same question in a
/// few hundred milliseconds. Its XML output is parsed by <see cref="Parse"/>, which is pure and
/// tested against a real event's shape.</para>
///
/// <para>Best-effort throughout: a log that cannot be read is "no event", which is the
/// direction that costs the player the standard bargain rather than the server a false
/// void.</para>
/// </summary>
public static class WindowsCrashEventLog
{
    private const string EventNs = "http://schemas.microsoft.com/win/2004/08/events/event";

    /// <summary>How many most-recent Application Error events to look through.</summary>
    internal const int MaxEvents = 200;

    /// <summary>
    /// The 1000 event for <paramref name="exeName"/> inside [<paramref name="fromUtc"/>,
    /// <paramref name="toUtc"/>], for <paramref name="pid"/> when the event carries one, or
    /// null. Runs <c>wevtutil</c>; never throws.
    /// </summary>
    public static CrashEvent? FindCrash(string exeName, int? pid, DateTime fromUtc, DateTime toUtc)
    {
        try
        {
            var xml = QueryApplicationErrors();
            if (string.IsNullOrEmpty(xml)) return null;
            return Parse(xml, exeName, pid, fromUtc, toUtc);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"WindowsCrashEventLog: could not read the Application log — {ex.Message}");
            return null;
        }
    }

    private static string? QueryApplicationErrors()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "wevtutil",
            Arguments = "qe Application /q:\"*[System[Provider[@Name='Application Error'] and (EventID=1000)]]\""
                        + $" /c:{MaxEvents} /rd:true /f:xml",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi);
        if (p == null) return null;
        var output = p.StandardOutput.ReadToEnd();
        if (!p.WaitForExit(5000))
        {
            try { p.Kill(); } catch { /* best-effort */ }
            return null;
        }
        return output;
    }

    /// <summary>
    /// The pure half. <paramref name="xml"/> is <c>wevtutil</c>'s output: a sequence of
    /// <c>&lt;Event&gt;</c> elements with no root, which is why it is wrapped before parsing.
    ///
    /// <para>Event 1000's <c>EventData</c> is positional: [0] application name, [3] faulting
    /// module, [6] exception code, [8] process id in HEX. The pid is matched only when both
    /// sides have one — the elevated launch path knows none — and the time window is what
    /// keeps an old crash of the same game from being read as this match's.</para>
    /// </summary>
    internal static CrashEvent? Parse(string xml, string exeName, int? pid, DateTime fromUtc, DateTime toUtc)
    {
        XDocument doc;
        try { doc = XDocument.Parse("<Events>" + xml + "</Events>"); }
        catch { return null; }

        XNamespace ns = EventNs;
        CrashEvent? best = null;
        foreach (var ev in doc.Root!.Elements(ns + "Event"))
        {
            var timeRaw = ev.Element(ns + "System")?.Element(ns + "TimeCreated")?.Attribute("SystemTime")?.Value;
            if (!DateTime.TryParse(timeRaw, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var time))
                continue;
            if (time < fromUtc || time > toUtc) continue;

            var data = ev.Element(ns + "EventData")?.Elements(ns + "Data").Select(d => d.Value).ToList()
                       ?? new List<string>();
            if (data.Count < 7) continue;
            if (!string.Equals(data[0], exeName, StringComparison.OrdinalIgnoreCase)) continue;

            int? eventPid = null;
            if (data.Count > 8 && int.TryParse(data[8], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
                eventPid = hex;
            if (pid is int wanted && eventPid is int found && found != wanted) continue;

            var candidate = new CrashEvent(data[3], data[6], time, eventPid);
            // Newest first is how wevtutil lists them (/rd:true), so the first match is the
            // latest; a pid match beats a time-only match.
            if (best == null || (candidate.Pid == pid && best.Pid != pid)) best = candidate;
        }
        return best;
    }
}
