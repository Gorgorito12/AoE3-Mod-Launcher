using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Localization;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// The room title the create dialog offers, and the question of whether the host has since
/// written one of their own.
///
/// <para><b>Why the title says it at all, when the browser row already carries a badge.</b>
/// The title is the first thing read about a room and the only part of it the host writes.
/// The competitive badge is a small gold chip on the row's SECOND line, and it is derived
/// from the server's boolean — that is deliberate and stays that way, because anyone can type
/// "competitive" into a room name and a badge a stranger can forge is worth less than no badge
/// at all. So the two are different claims: the badge is what the room IS, the title is what
/// its host is announcing. They will read the same word twice on one row, and that was
/// weighed and accepted.</para>
///
/// <para><b>Why this is a separate class instead of two lines in the dialog.</b>
/// <see cref="IsOurs"/> is the half that is easy to get wrong and impossible to see going
/// wrong: the dialog only replaces a title it recognises as its own, so the instant the
/// proposal gains a variant this method does not enumerate, the box freezes — ticking the
/// competitive box once would make every later change look like a hand-typed title and be
/// left alone. That is a silent failure, so it is pinned by tests rather than by reading.</para>
/// </summary>
public static class RoomTitleProposal
{
    /// <summary>
    /// The separator between the room's name and what is being announced about it. The same
    /// one the suggestion pills under this field already use to append themselves.
    /// </summary>
    private const string Join = " · ";

    /// <summary>
    /// What the dialog would put in an untouched title box.
    /// </summary>
    /// <param name="modName">The picked mod's display name.</param>
    /// <param name="competitive">Whether the room is declared competitive.</param>
    /// <param name="format">The declared format. <see cref="RoomFormat.Unknown"/> is a real
    /// case — a competitive room whose size names no format — and it is answered by saying
    /// only the badge rather than by inventing a 1v1.</param>
    /// <param name="maxLength">The title field's own cap.</param>
    public static string Propose(string modName, bool competitive, RoomFormat format, int maxLength)
    {
        var name = Strings.Format("MpCreateDialogDefaultTitle", modName ?? "");
        if (!competitive) return Clamp(name, maxLength);

        // Composed exactly as the browser row composes its chip, so a room and its own row
        // never disagree about what the format is called.
        var key = RoomFormats.LabelKey(format);
        var marker = key == null
            ? Strings.Get("MpRoomCompetitiveBadge")
            : Strings.Get("MpRoomCompetitiveBadge") + " " + Strings.Get(key);

        var suffix = Join + marker;

        // THE NAME GIVES WAY, NEVER THE MARKER. The marker is the thing that was added on
        // purpose and the reason the host ticked anything; a title cut off mid-"COMPETITI…"
        // announces nothing and looks like a bug. So the room's own name is what is trimmed,
        // which is also the only part with anything to spare.
        if (name.Length + suffix.Length > maxLength)
        {
            var room = Math.Max(0, maxLength - suffix.Length);
            name = name[..Math.Min(name.Length, room)].TrimEnd();
        }

        return name + suffix;
    }

    /// <summary>
    /// Whether <paramref name="current"/> is a title this class wrote, rather than one a
    /// person typed.
    ///
    /// <para>Every variant, for every mod: casual, and competitive at each of the formats
    /// including the one that names none. Miss one and the dialog stops updating the title
    /// the moment it produces that one — see the class remarks.</para>
    /// </summary>
    public static bool IsOurs(string? current, IEnumerable<string> modNames, int maxLength)
    {
        var text = (current ?? "").Trim();
        if (text.Length == 0) return true;   // an empty box is nobody's title

        return AllProposals(modNames, maxLength)
            .Any(p => string.Equals(p, text, StringComparison.Ordinal));
    }

    /// <summary>Every title this class can produce for these mods. Exposed so a test can
    /// assert the round trip rather than restate the list.</summary>
    public static IEnumerable<string> AllProposals(IEnumerable<string> modNames, int maxLength)
    {
        foreach (var mod in modNames ?? Enumerable.Empty<string>())
        {
            yield return Propose(mod, competitive: false, RoomFormat.Casual, maxLength);
            foreach (var f in new[]
                     {
                         RoomFormat.OneVOne, RoomFormat.TwoVTwo,
                         RoomFormat.ThreeVThree, RoomFormat.Unknown,
                     })
            {
                yield return Propose(mod, competitive: true, f, maxLength);
            }
        }
    }

    private static string Clamp(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength];
}
