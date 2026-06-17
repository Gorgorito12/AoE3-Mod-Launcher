using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Steam-style notification bell backing store. Owns the in-memory
/// <see cref="ObservableCollection{T}"/> the bell panel binds to, mirrors it
/// into <see cref="LauncherConfig.Notifications"/> for persistence, and applies
/// the per-kind dedup rules so the same event never bells twice.
///
/// Deliberately UI-free (no WPF types): the caller passes already-localized
/// title/body strings and wires <see cref="ToastRequested"/> to the system-tray
/// toast and <see cref="ItemAdded"/> to the bell's one-shot pulse animation.
/// That keeps the dedup / cap / unread logic unit-testable without a UI thread.
/// </summary>
public sealed class NotificationCenter
{
    /// <summary>Most recent items kept; older ones are trimmed on add.</summary>
    public const int MaxItems = 50;

    private readonly LauncherConfig _config;
    private readonly Action _persist;

    /// <summary>Bound by the bell panel. Newest item first (index 0).</summary>
    public ObservableCollection<NotificationItem> Items { get; }

    /// <summary>Fires on any state change (add / read / remove / clear) so the badge can refresh.</summary>
    public event EventHandler? Changed;

    /// <summary>Fires only when a brand-new item is actually added — drives the one-shot bell pulse.</summary>
    public event EventHandler? ItemAdded;

    /// <summary>Raised (title, body) when a new item is added, so the host can show a tray toast.</summary>
    public event Action<string, string>? ToastRequested;

    /// <param name="config">Source of persisted history + per-mod dedup state.</param>
    /// <param name="persist">
    /// How to flush the config to disk after a mutation. Defaults to
    /// <see cref="LauncherConfig.Save"/>; tests pass a no-op so they never
    /// touch the real <c>launcher-config.json</c>.
    /// </param>
    public NotificationCenter(LauncherConfig config, Action? persist = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _persist = persist ?? _config.Save;
        // Seed newest-first; tolerate a hand-edited config with nulls.
        var seed = (_config.Notifications ?? new List<NotificationItem>())
            .Where(n => n != null)
            .OrderByDescending(n => n.CreatedAtUtc);
        Items = new ObservableCollection<NotificationItem>(seed);
    }

    /// <summary>Number of unread items — drives the red badge count.</summary>
    public int UnreadCount => Items.Count(i => !i.Read);

    // ---------------------------------------------------------------- raises

    /// <summary>
    /// "Update available" for a mod. Deduped on (mod, version) via
    /// <see cref="ModState.NotifiedUpdateVersion"/> so re-checks don't re-bell
    /// the same version. Returns true if a new item was added.
    /// </summary>
    public bool RaiseUpdateAvailable(string modId, string version, string title, string body)
    {
        if (string.IsNullOrWhiteSpace(modId)) return false;
        var state = _config.GetState(modId);
        if (string.Equals(state.NotifiedUpdateVersion, version, StringComparison.OrdinalIgnoreCase))
            return false;
        state.NotifiedUpdateVersion = version ?? "";
        return Add(new NotificationItem
        {
            Kind = NotificationKind.UpdateAvailable,
            ModId = modId,
            Title = title,
            Body = body,
        });
    }

    /// <summary>
    /// "Update finished" for a mod. Deduped against an existing finished item
    /// for the same (mod, version) still in the visible list (these are
    /// user-initiated and rare, so list-scan dedup is enough).
    /// </summary>
    public bool RaiseUpdateFinished(string modId, string version, string title, string body)
    {
        if (string.IsNullOrWhiteSpace(modId)) return false;
        bool dup = Items.Any(i => i.Kind == NotificationKind.UpdateFinished
            && string.Equals(i.ModId, modId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(i.TargetId, version, StringComparison.OrdinalIgnoreCase));
        if (dup) return false;
        // A completed update supersedes any pending "update available" for the
        // same mod — clear its dedup latch so a FUTURE version can bell again,
        // and drop the now-stale "available" item from the list.
        var state = _config.GetState(modId);
        state.NotifiedUpdateVersion = "";
        RemoveWhere(i => i.Kind == NotificationKind.UpdateAvailable
            && string.Equals(i.ModId, modId, StringComparison.OrdinalIgnoreCase));
        return Add(new NotificationItem
        {
            Kind = NotificationKind.UpdateFinished,
            ModId = modId,
            Title = title,
            Body = body,
            TargetId = version,
        });
    }

    /// <summary>
    /// "New translation" for a mod. Deduped on a stable <paramref name="translationKey"/>
    /// (e.g. <c>id@version</c>) via <see cref="ModState.NotifiedTranslationKeys"/>.
    /// </summary>
    public bool RaiseNewTranslation(string modId, string translationKey, string translationId, string title, string body)
    {
        if (string.IsNullOrWhiteSpace(modId) || string.IsNullOrWhiteSpace(translationKey)) return false;
        var state = _config.GetState(modId);
        if (state.NotifiedTranslationKeys.Contains(translationKey, StringComparer.OrdinalIgnoreCase))
            return false;
        state.NotifiedTranslationKeys.Add(translationKey);
        // Keep the dedup set from growing without bound on a busy translations repo.
        if (state.NotifiedTranslationKeys.Count > 200)
            state.NotifiedTranslationKeys.RemoveRange(0, state.NotifiedTranslationKeys.Count - 200);
        return Add(new NotificationItem
        {
            Kind = NotificationKind.NewTranslation,
            ModId = modId,
            Title = title,
            Body = body,
            TargetId = translationId,
        });
    }

    // --------------------------------------------------------------- mutators

    /// <summary>Marks every item read (badge → 0).</summary>
    public void MarkAllRead()
    {
        bool any = false;
        foreach (var i in Items)
            if (!i.Read) { i.Read = true; any = true; }
        if (any) { Persist(); Changed?.Invoke(this, EventArgs.Empty); }
    }

    /// <summary>Marks a single item read.</summary>
    public void MarkRead(NotificationItem item)
    {
        if (item == null || item.Read) return;
        item.Read = true;
        Persist();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Removes one item from the history.</summary>
    public void Remove(NotificationItem item)
    {
        if (item == null) return;
        if (Items.Remove(item)) { Persist(); Changed?.Invoke(this, EventArgs.Empty); }
    }

    /// <summary>Clears the whole history.</summary>
    public void Clear()
    {
        if (Items.Count == 0) return;
        Items.Clear();
        Persist();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // --------------------------------------------------------------- internals

    private bool Add(NotificationItem item)
    {
        Items.Insert(0, item);
        // Trim oldest beyond the cap.
        while (Items.Count > MaxItems)
            Items.RemoveAt(Items.Count - 1);
        Persist();
        ItemAdded?.Invoke(this, EventArgs.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
        ToastRequested?.Invoke(item.Title, item.Body);
        return true;
    }

    private void RemoveWhere(Func<NotificationItem, bool> pred)
    {
        for (int i = Items.Count - 1; i >= 0; i--)
            if (pred(Items[i])) Items.RemoveAt(i);
    }

    private void Persist()
    {
        _config.Notifications = Items.ToList();
        try { _persist(); }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"NotificationCenter save failed: {ex.Message}");
        }
    }
}
