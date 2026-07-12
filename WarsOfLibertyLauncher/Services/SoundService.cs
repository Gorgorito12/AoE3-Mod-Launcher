using System;
using System.Collections.Concurrent;
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

    public enum SoundKind { Chat, Notification, Connect }

    // Cached WAV bytes per resource path (loaded once from the pack resource).
    private static readonly ConcurrentDictionary<string, byte[]?> s_cache = new();

    // Last-played tick (ms) per category, for the anti-spam throttle.
    private static long s_lastChat, s_lastNotify, s_lastConnect;

    // Minimum gap between two sounds of the same category. Connect is longer
    // because presence altas can arrive in clusters.
    private const long ChatThrottleMs = 300;
    private const long NotifyThrottleMs = 300;
    private const long ConnectThrottleMs = 900;

    private static readonly object s_throttleLock = new();

    public static void PlayChat() => Play(SoundKind.Chat);
    public static void PlayNotification() => Play(SoundKind.Notification);
    public static void PlayConnect() => Play(SoundKind.Connect);

    /// <summary>
    /// Play the sound for <paramref name="kind"/> if sounds are enabled and the
    /// per-category throttle window has elapsed. Never throws.
    /// </summary>
    public static void Play(SoundKind kind)
    {
        if (!Enabled) return;
        if (!PassesThrottle(kind)) return;

        var res = kind switch
        {
            SoundKind.Chat => "Assets/Sounds/chat.wav",
            SoundKind.Notification => "Assets/Sounds/notify.wav",
            SoundKind.Connect => "Assets/Sounds/connect.wav",
            _ => null,
        };
        if (res == null) return;

        try
        {
            var bytes = LoadCached(res);
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

    private static bool PassesThrottle(SoundKind kind)
    {
        long now = Environment.TickCount64;
        lock (s_throttleLock)
        {
            switch (kind)
            {
                case SoundKind.Chat:
                    if (now - s_lastChat < ChatThrottleMs) return false;
                    s_lastChat = now; return true;
                case SoundKind.Notification:
                    if (now - s_lastNotify < NotifyThrottleMs) return false;
                    s_lastNotify = now; return true;
                case SoundKind.Connect:
                    if (now - s_lastConnect < ConnectThrottleMs) return false;
                    s_lastConnect = now; return true;
                default:
                    return false;
            }
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
