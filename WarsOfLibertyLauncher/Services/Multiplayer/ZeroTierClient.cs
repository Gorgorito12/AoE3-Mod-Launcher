using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Thin wrapper around the local ZeroTier daemon's HTTP JSON API.
///
/// Why the HTTP API and not the <c>zerotier-cli</c> binary?
///   * No stdout parsing — the daemon already returns structured JSON.
///   * Faster, no subprocess spawn per call.
///   * Works regardless of whether the cli is in PATH or behind the
///     <c>.bat</c> shim ZeroTier ships on Windows.
///
/// Auth: every request needs an <c>X-ZT1-Auth</c> header carrying the
/// daemon's secret authtoken. ZeroTier writes the token to two places:
///   * <c>%PROGRAMDATA%\ZeroTier\One\authtoken.secret</c> (admin to read)
///   * <c>%LOCALAPPDATA%\ZeroTier\One\authtoken.secret</c> (user-readable;
///     ZeroTier's tray UI populates this for any user who has opened it
///     at least once)
///
/// We try the user copy first and fall back to ProgramData. If neither
/// is readable, callers get null from <c>TryGetAuthToken</c> — the
/// facade above this one then surfaces a "we need a one-time
/// authorisation" prompt that copies the file across with UAC.
/// </summary>
public class ZeroTierClient : IDisposable
{
    /// <summary>Default daemon endpoint on every ZeroTier install.</summary>
    public const string DefaultBaseUrl = "http://127.0.0.1:9993";

    private readonly HttpClient _http;
    private readonly string _authToken;
    private readonly JsonSerializerOptions _jsonOptions;

    public ZeroTierClient(string authToken, string baseUrl = DefaultBaseUrl)
    {
        _authToken = authToken;
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            // Daemon is local; a slow response means the service is wedged.
            // Tight timeout keeps the UI responsive instead of hanging on
            // a stuck daemon.
            Timeout = TimeSpan.FromSeconds(10),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Aoe3ModLauncher/1.0");
        _http.DefaultRequestHeaders.Add("X-ZT1-Auth", _authToken);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };
    }

    public void Dispose() => _http.Dispose();

    /// <summary>
    /// Try to locate and read ZeroTier's authtoken without prompting for
    /// elevation. Returns null if neither candidate is readable; callers
    /// must then trigger an elevated copy step.
    /// </summary>
    public static string? TryReadAuthToken()
    {
        var candidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ZeroTier", "One", "authtoken.secret"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ZeroTier", "One", "authtoken.secret"),
        };

        foreach (var path in candidates)
        {
            try
            {
                if (!File.Exists(path)) continue;
                var token = File.ReadAllText(path).Trim();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    DiagnosticLog.Write($"ZeroTierClient: loaded authtoken from '{path}'");
                    return token;
                }
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"ZeroTierClient: cannot read authtoken at '{path}': {ex.Message}");
            }
        }
        return null;
    }

    /// <summary>
    /// Liveness probe: does the daemon answer at all? Used by the facade
    /// to distinguish "service not started" from "service running but
    /// authtoken missing".
    /// </summary>
    public static async Task<bool> IsDaemonReachableAsync(
        string baseUrl = DefaultBaseUrl,
        CancellationToken ct = default)
    {
        try
        {
            // Probe without auth — daemon answers 401 when alive, never 0.
            using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var resp = await probe.GetAsync(baseUrl.TrimEnd('/') + "/status", ct);
            return resp.StatusCode != System.Net.HttpStatusCode.RequestTimeout;
        }
        catch
        {
            return false;
        }
    }

    public async Task<ZeroTierNodeStatus?> GetStatusAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync("status", ct);
            if (!resp.IsSuccessStatusCode)
            {
                DiagnosticLog.Write($"ZeroTierClient.GetStatusAsync: HTTP {(int)resp.StatusCode}");
                return null;
            }
            var stream = await resp.Content.ReadAsStreamAsync(ct);
            return await JsonSerializer.DeserializeAsync<ZeroTierNodeStatus>(stream, _jsonOptions, ct);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"ZeroTierClient.GetStatusAsync: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Join a network. The daemon treats this idempotently — calling it
    /// twice with the same network id is harmless and returns the same
    /// membership object.
    /// </summary>
    public async Task<ZeroTierNetworkMembership?> JoinAsync(string networkId, CancellationToken ct = default)
    {
        ValidateNetworkId(networkId);
        try
        {
            // POST /network/<id> with an empty body = "join". The body
            // could also carry config overrides; we don't need any.
            using var content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync($"network/{networkId}", content, ct);
            if (!resp.IsSuccessStatusCode)
            {
                DiagnosticLog.Write($"ZeroTierClient.JoinAsync({networkId}): HTTP {(int)resp.StatusCode}");
                return null;
            }
            var stream = await resp.Content.ReadAsStreamAsync(ct);
            return await JsonSerializer.DeserializeAsync<ZeroTierNetworkMembership>(stream, _jsonOptions, ct);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"ZeroTierClient.JoinAsync({networkId}): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Look up the current membership for a network — used to poll for
    /// the "ACCESS_DENIED → OK" transition right after the Worker
    /// authorises us on the host's side.
    /// </summary>
    public async Task<ZeroTierNetworkMembership?> GetMembershipAsync(string networkId, CancellationToken ct = default)
    {
        ValidateNetworkId(networkId);
        try
        {
            using var resp = await _http.GetAsync($"network/{networkId}", ct);
            if (!resp.IsSuccessStatusCode)
            {
                DiagnosticLog.Write($"ZeroTierClient.GetMembershipAsync({networkId}): HTTP {(int)resp.StatusCode}");
                return null;
            }
            var stream = await resp.Content.ReadAsStreamAsync(ct);
            return await JsonSerializer.DeserializeAsync<ZeroTierNetworkMembership>(stream, _jsonOptions, ct);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"ZeroTierClient.GetMembershipAsync({networkId}): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Leave a network. Daemon returns 200 even if we weren't a member,
    /// which is exactly the idempotency we want when the launcher exits
    /// mid-game and re-runs the cleanup on next start.
    /// </summary>
    public async Task<bool> LeaveAsync(string networkId, CancellationToken ct = default)
    {
        ValidateNetworkId(networkId);
        try
        {
            using var resp = await _http.DeleteAsync($"network/{networkId}", ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"ZeroTierClient.LeaveAsync({networkId}): {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Wait for the membership status to become "OK" (i.e. the host
    /// authorised us on ZT Central and the daemon has refreshed). Polls
    /// every 1s up to <paramref name="timeout"/>. Returns the final
    /// membership state — caller inspects .Status to know if it timed out.
    /// </summary>
    public async Task<ZeroTierNetworkMembership?> WaitForOkAsync(
        string networkId,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var m = await GetMembershipAsync(networkId, ct);
            if (m != null && string.Equals(m.Status, "OK", StringComparison.OrdinalIgnoreCase))
                return m;
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
        return await GetMembershipAsync(networkId, ct);
    }

    private static void ValidateNetworkId(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length != 16)
            throw new ArgumentException("ZeroTier network id must be 16 hex chars.", nameof(id));
        for (int i = 0; i < id.Length; i++)
        {
            var c = id[i];
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                throw new ArgumentException("ZeroTier network id must be hex.", nameof(id));
        }
    }
}
