using System;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Our AoE3 profile name, read off disk once per room instead of on every tick.
///
/// <para><b>Why.</b> <c>MaybeReportInGameName</c> read the whole profile — about 230 KB of UTF-16
/// XML, plus My Games folder discovery for a mod that declares no folder, never remembered when it
/// failed — on every 2.5-s lobby tick, on every <c>room_state</c>, and three times at match start.
/// The lobby tick runs for as long as the room window is open, the match included, which defeated
/// the in-game tick's own "only until confirmed" gate. The name changes only when the player
/// renames their profile, which happens between matches, not during a tick.</para>
///
/// <para><b>The rules, each a refusal:</b> a non-blank read is remembered per mod; a blank read is
/// never remembered and never erases a name already known (a profile the game is rewriting reads
/// blank for a moment); a blank re-read is tried at most once per <see cref="BlankRetry"/> — nothing
/// is ever sent for a blank name, so this changes no behaviour, only how often a missing profile is
/// looked for. <see cref="Forget"/> is called on every room change and after every game exit, which
/// is when the name can really have changed.</para>
///
/// <para>Pure — the reader and the clock are injected — like <see cref="InGameNamePublishState"/>,
/// which keeps the separate question of whether the server has it.</para>
/// </summary>
public sealed class InGameNameCache
{
    /// <summary>How often a profile that read blank is looked for again.</summary>
    public static readonly TimeSpan BlankRetry = TimeSpan.FromSeconds(15);

    private readonly Func<DateTime> _clock;
    private string? _modId;
    private string? _name;
    private DateTime? _lastBlankRead;

    public InGameNameCache(Func<DateTime>? clock = null) => _clock = clock ?? (() => DateTime.UtcNow);

    /// <summary>
    /// The name for <paramref name="modId"/>: remembered, or read through <paramref name="read"/>.
    /// <paramref name="refresh"/> forces a read (a match start) — and a refresh that reads blank
    /// still answers the name already known.
    /// </summary>
    public string? Get(string modId, Func<string?> read, bool refresh = false)
    {
        if (!string.Equals(modId, _modId, StringComparison.Ordinal))
        {
            _modId = modId;
            _name = null;
            _lastBlankRead = null;
        }

        if (!refresh)
        {
            if (_name != null) return _name;
            if (_lastBlankRead is { } last && _clock() - last < BlankRetry) return null;
        }

        var fresh = read();
        if (!string.IsNullOrWhiteSpace(fresh))
        {
            _name = fresh;
            _lastBlankRead = null;
            return _name;
        }

        _lastBlankRead = _clock();
        return _name;
    }

    /// <summary>The next <see cref="Get"/> reads the disk again.</summary>
    public void Forget()
    {
        _modId = null;
        _name = null;
        _lastBlankRead = null;
    }
}
