using System;
using System.Collections.Concurrent;
using System.IO;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Lightweight, opt-in event counters for the multiplayer feature.
///
/// No network, no third-party SDK — just an in-memory tally that gets
/// flushed to a sibling log file (<c>multiplayer-events.log</c>) on
/// every increment. Designed to answer questions like "how often does
/// the mod-hash check fail?" and "how often does the rate limiter
/// fire?" without dragging in a telemetry vendor for v1.0.
///
/// The launcher reads <c>LauncherConfig.CheckUpdatesOnStartup</c> as a
/// crude proxy for "is the user OK with the launcher writing files for
/// diagnostics"; the multiplayer telemetry honours the same flag, so
/// turning off the startup network checks also disables event counters.
///
/// Counter names use a stable <c>mp_*</c> prefix so they can be grepped
/// out of the log file later — no schema, just lines of
/// <c>timestamp counter total_count</c>.
/// </summary>
public static class MultiplayerTelemetry
{
    public static bool Enabled { get; set; } = true;

    private static readonly ConcurrentDictionary<string, long> Counters = new();
    private static readonly string LogPath = Path.Combine(
        AppContext.BaseDirectory, "multiplayer-events.log");

    /// <summary>Increment a counter by one. Cheap; safe to call from any thread.</summary>
    public static void Bump(string counter)
    {
        if (!Enabled) return;
        if (string.IsNullOrEmpty(counter)) return;

        var newValue = Counters.AddOrUpdate(counter, 1, (_, v) => v + 1);

        // Best-effort append. Locks aren't strictly necessary (each
        // line is small enough to be atomic on Windows), but a lock
        // keeps the file readable when two counters fire at once
        // from different threads.
        try
        {
            lock (LogPath)
            {
                File.AppendAllText(LogPath,
                    $"{DateTime.UtcNow:O} {counter} {newValue}\n");
            }
        }
        catch
        {
            // Telemetry must never crash the launcher. Drop silently
            // if disk is full / log is locked / etc.
        }
    }

    /// <summary>Read-only snapshot of all counters seen so far this session.</summary>
    public static System.Collections.Generic.IReadOnlyDictionary<string, long> Snapshot()
    {
        var copy = new System.Collections.Generic.Dictionary<string, long>(Counters.Count);
        foreach (var kv in Counters) copy[kv.Key] = kv.Value;
        return copy;
    }

    // Standard counter names — referenced from the rest of the code so
    // typos don't fragment the keyspace.
    public const string SignInAttempted = "mp_signin_attempted";
    public const string SignInSucceeded = "mp_signin_succeeded";
    public const string SignInDeclined = "mp_signin_declined";
    public const string LobbyCreated = "mp_lobby_created";
    public const string LobbyJoined = "mp_lobby_joined";
    public const string ModMismatch = "mp_mod_mismatch";
    public const string RateLimited = "mp_rate_limited";
    public const string QuotaDegraded = "mp_quota_degraded";
    public const string QuotaExhausted = "mp_quota_exhausted";
    public const string ZtInstallNeeded = "mp_zt_install_needed";
    public const string ZtAuthTokenMissing = "mp_zt_auth_token_missing";
    public const string ReplayUploaded = "mp_replay_uploaded";
}
