using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using WarsOfLibertyLauncher.Localization;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// AoE3-style taunts for the LOBBY chat: a message whose body is just a number
/// (1..33) plays that taunt for everyone in the room.
///
/// <para><b>Nothing is sent over the wire.</b> The "11" already travels as an
/// ordinary chat message, so every client detects it and plays the taunt from
/// its OWN embedded set. That is what makes "each player hears it in their own
/// launcher language" possible at all — shipping the audio instead would force
/// one language on everybody. It also means zero backend changes.</para>
///
/// <para><b>Why MediaPlayer</b> when <see cref="SoundService"/> deliberately uses
/// SoundPlayer: taunts are MP3 and SoundPlayer only decodes WAV. Converting them
/// would cost ~9-20 MB instead of ~2 MB. SoundService's objection to MediaPlayer
/// (needs a UI-thread Dispatcher, first-play latency) doesn't apply here — taunts
/// are raised from the chat frame handler, which is already on the UI thread, and
/// a few ms of first-play latency is irrelevant for a taunt. SoundService is left
/// untouched.</para>
///
/// <para><b>Both sets are embedded</b> because the WoL payload ships the SPANISH
/// taunts only (verified: 33/33 hash match against a canonical install); the
/// English set exists nowhere on disk.</para>
/// </summary>
public static class TauntService
{
    /// <summary>Highest taunt number shipped. 1..33, no gaps, in both languages.</summary>
    public const int MaxTaunt = 33;

    /// <summary>Per-SENDER cooldown. See <see cref="Play"/> for why not global.</summary>
    private const long PerSenderThrottleMs = 1000;

    private static readonly ConcurrentDictionary<string, long> s_lastBySender = new();

    /// <summary>
    /// Live players. MediaPlayer is NOT rooted by anything once the local goes out
    /// of scope, so without this the GC can collect it mid-playback and the taunt
    /// audibly cuts off. Entries are dropped on MediaEnded/MediaFailed.
    /// </summary>
    private static readonly List<MediaPlayer> s_playing = new();

    /// <summary>
    /// True when <paramref name="body"/> is a bare taunt number.
    ///
    /// The rule is deliberately strict — the request was "just the number, not
    /// something that ends up appending the number and plays". So "11" is a taunt
    /// but "gg 11" and "11 gg" are ordinary chat and must stay silent. Pure and
    /// testable; no I/O.
    /// </summary>
    public static bool TryParseTaunt(string? body, out int number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(body)) return false;

        var t = body.Trim();
        if (t.Length is 0 or > 3) return false;
        foreach (var c in t)
            if (c is < '0' or > '9') return false;   // digits ONLY — no sign, no spaces

        if (!int.TryParse(t, out var n)) return false;
        if (n < 1 || n > MaxTaunt) return false;

        number = n;
        return true;
    }

    /// <summary>
    /// Play taunt <paramref name="number"/> in the launcher's current language.
    /// Best-effort: audio must never break the chat.
    ///
    /// <paramref name="senderUserId"/> drives a PER-SENDER cooldown. A single
    /// global throttle (SoundService's per-category style) would break the point
    /// of the feature: if two players taunt within the window, every client would
    /// play the first and silently drop the second — two people said different
    /// things and you heard one. Per-sender stops a spammer without eating the
    /// back-and-forth.
    /// </summary>
    public static void Play(int number, string? senderUserId = null)
    {
        // Shares the user's "play sounds" switch — no separate toggle.
        if (!SoundService.Enabled) return;
        if (number < 1 || number > MaxTaunt) return;

        var key = string.IsNullOrEmpty(senderUserId) ? "?" : senderUserId!;
        long now = Environment.TickCount64;
        var last = s_lastBySender.GetOrAdd(key, 0L);
        if (last != 0 && now - last < PerSenderThrottleMs) return;
        s_lastBySender[key] = now;

        try
        {
            var path = EnsureOnDisk(number, CurrentLang());
            if (path == null) return;

            var player = new MediaPlayer();
            lock (s_playing) s_playing.Add(player);

            void Drop(object? s, EventArgs e)
            {
                lock (s_playing) s_playing.Remove(player);
                try { player.Close(); } catch { /* best-effort */ }
            }
            player.MediaEnded += Drop;
            player.MediaFailed += (s, e) =>
            {
                DiagnosticLog.Write($"Taunt {number}: playback failed — {e.ErrorException?.Message}");
                Drop(s, e);
            };

            player.Open(new Uri(path, UriKind.Absolute));
            player.Play();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Taunt {number} failed (non-fatal): {ex.Message}");
        }
    }

    /// <summary>"es" while the UI is Spanish, else English (the default set).</summary>
    private static string CurrentLang() =>
        string.Equals(Strings.Language, Strings.LangEs, StringComparison.OrdinalIgnoreCase)
            ? "es" : "en";

    /// <summary>
    /// Absolute path to the taunt, extracting it from the embedded resources on
    /// first use.
    ///
    /// The extraction is not a convenience: MediaPlayer cannot open a
    /// <c>pack://</c> URI (unlike Image/BitmapImage), it needs a real file or http
    /// URI — so an embedded resource has to be materialised. Written once per
    /// (language, number) and reused; a stale/zero-length file is rewritten.
    /// </summary>
    private static string? EnsureOnDisk(int number, string lang)
    {
        var dir = Path.Combine(AppPaths.DataDir, "taunts", lang);
        var file = Path.Combine(dir, $"{number:D3}.mp3");

        try
        {
            if (File.Exists(file) && new FileInfo(file).Length > 0) return file;

            var uri = new Uri($"pack://application:,,,/Assets/Taunts/{lang}/{number:D3}.mp3",
                UriKind.Absolute);
            var info = Application.GetResourceStream(uri);
            if (info == null)
            {
                DiagnosticLog.Write($"Taunt {number} ({lang}): embedded resource not found.");
                return null;
            }

            Directory.CreateDirectory(dir);
            using (var src = info.Stream)
            using (var dst = File.Create(file))
                src.CopyTo(dst);

            return file;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Taunt {number} ({lang}): extract failed — {ex.Message}");
            return null;
        }
    }
}
