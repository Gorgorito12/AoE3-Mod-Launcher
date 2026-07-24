using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Windows;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Short UI feedback sounds (chat / notification / connection), Discord-style.
///
/// Deliberately tiny and dependency-free: plays embedded 16-bit PCM WAV
/// <c>&lt;Resource&gt;</c>s via <see cref="SoundPlayer"/> — no <c>MediaPlayer</c>
/// (needs a UI-thread Dispatcher + has first-play latency) and no NAudio. Each
/// <see cref="Play"/> spins a fresh <see cref="SoundPlayer"/> over a cached byte
/// buffer so distinct sounds can overlap and any thread can call it.
///
/// Gated by <see cref="Enabled"/> (wired to <c>LauncherConfig.EnableSounds</c> at
/// startup and on settings save) and throttled per category so a burst of frames
/// (a busy chat, a presence flood on connect) can't machine-gun the speaker. All
/// playback is best-effort try/caught — audio must never take down the app.
/// </summary>
public static class SoundService
{
    /// <summary>Master on/off. False = every <see cref="Play"/> is a no-op.</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>
    /// Categories are SEMANTIC, not one-per-event. Eight notification kinds mapped to
    /// eight tones would be indistinguishable in practice — worse, the installed-mods
    /// sweep can raise several at once. Grouping by what the user needs to know
    /// ("something finished" vs "something is available" vs "something failed") is what
    /// makes them informative instead of noise.
    ///
    /// <see cref="Notification"/> IS the "available / heads-up" tone — it already maps to
    /// notify.wav and is the most frequent case, so it is reused rather than duplicated
    /// under a second name.
    /// </summary>
    public enum SoundKind { Chat, Notification, Connect, Success, Error }

    // Cached WAV bytes per resource path (loaded once from the pack resource).
    private static readonly ConcurrentDictionary<string, byte[]?> s_cache = new();

    /// <summary>
    /// Resource + throttle window per category, in one table so adding a sound is one
    /// entry instead of a new field plus two switch arms that can disagree.
    /// </summary>
    private static readonly IReadOnlyDictionary<SoundKind, (string Resource, long ThrottleMs)> s_kinds =
        new Dictionary<SoundKind, (string, long)>
        {
            [SoundKind.Chat]         = ("Assets/Sounds/chat.wav",    300),
            [SoundKind.Notification] = ("Assets/Sounds/notify.wav",  300),
            // Longer: presence altas arrive in clusters.
            [SoundKind.Connect]      = ("Assets/Sounds/connect.wav", 900),
            // Longer than a heads-up: completions are rare and deserve to land cleanly,
            // and a fresh install can finish into an immediate update-finished.
            [SoundKind.Success]      = ("Assets/Sounds/success.wav", 900),
            [SoundKind.Error]        = ("Assets/Sounds/error.wav",   900),
        };

    // Last-played tick (ms) per category, for the anti-spam throttle.
    private static readonly Dictionary<SoundKind, long> s_lastPlayed = new();

    private static readonly object s_throttleLock = new();

    public static void PlayChat() => Play(SoundKind.Chat);
    /// <summary>Heads-up: an update, translation or mod is available.</summary>
    public static void PlayNotification() => Play(SoundKind.Notification);
    public static void PlayConnect() => Play(SoundKind.Connect);
    /// <summary>An install, update or uninstall finished successfully.</summary>
    public static void PlaySuccess() => Play(SoundKind.Success);
    /// <summary>An operation failed. Not a notification kind — failures leave no bell item.</summary>
    public static void PlayError() => Play(SoundKind.Error);

    /// <summary>
    /// Play the sound for <paramref name="kind"/> if sounds are enabled and the
    /// per-category throttle window has elapsed. Never throws.
    /// </summary>
    public static void Play(SoundKind kind)
    {
        if (!Enabled) return;
        if (!s_kinds.TryGetValue(kind, out var spec)) return;
        if (!PassesThrottle(kind, spec.ThrottleMs)) return;

        try
        {
            var bytes = LoadCached(spec.Resource);
            if (bytes == null || bytes.Length == 0) return;
            // Fresh player + stream per call so overlapping sounds don't fight
            // over one instance. SoundPlayer.Play() copies/streams on its own
            // thread, so we can let both go out of scope safely.
            var player = new SoundPlayer(new MemoryStream(bytes));
            player.Play();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"SoundService play failed ({kind}): {ex.Message}");
        }
    }

    private static bool PassesThrottle(SoundKind kind, long throttleMs)
    {
        long now = Environment.TickCount64;
        lock (s_throttleLock)
        {
            if (s_lastPlayed.TryGetValue(kind, out var last) && now - last < throttleMs)
                return false;
            s_lastPlayed[kind] = now;
            return true;
        }
    }

    private static byte[]? LoadCached(string resourcePath)
    {
        return s_cache.GetOrAdd(resourcePath, path =>
        {
            try
            {
                var uri = new Uri($"pack://application:,,,/{path}", UriKind.Absolute);
                var info = Application.GetResourceStream(uri);
                if (info == null) return null;
                using var s = info.Stream;
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"SoundService load failed ({path}): {ex.Message}");
                return null;
            }
        });
    }
}
