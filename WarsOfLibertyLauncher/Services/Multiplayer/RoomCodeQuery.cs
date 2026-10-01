using System.Text.RegularExpressions;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Whether what somebody typed into the rooms search box is a ROOM CODE — design handoff
/// turn 36, which retired the separate "room code" field and its send button: the code is
/// pasted into the search, and a code gets a "Join room XXXXXXXX" row at the top of the list.
///
/// <para><b>The format is the server's, exactly.</b> A lobby id is minted by
/// <c>shortId(8)</c> in the lobby backend (<c>src/lib/ids.ts</c>): eight characters of
/// Crockford base32 — digits and capitals with no I, L, O or U, so a code can be read aloud
/// without confusing 1/I or 0/O. The server looks it up with a case-SENSITIVE
/// <c>WHERE id = ?</c>, which is why the answer is uppercased here and never sent as typed.</para>
///
/// <para><b>Strict on purpose.</b> No I→1 or O→0 remapping: a code is copied, not dictated,
/// and a remapped guess that happens to name another room is a worse outcome than "not found".
/// The accepted false positive is an eight-letter word spelled only from that alphabet
/// ("TRAPPERS"): it shows a join row and Enter answers "room not found". Requiring a digit was
/// rejected — about one real id in twenty contains none.</para>
///
/// <para>Pure and WPF-free, pinned by <c>RoomCodeQueryTests</c>.</para>
/// </summary>
public static class RoomCodeQuery
{
    private static readonly Regex Code = new("^[0-9A-HJKMNP-TV-Z]{8}$", RegexOptions.CultureInvariant);

    /// <summary>
    /// The room code in <paramref name="text"/>, uppercased, or null when it is not one.
    /// A pasted <c>wol-launcher://join/&lt;id&gt;</c> link counts: it is the same code with a
    /// prefix, and people paste whatever they were sent.
    /// </summary>
    public static string? TryParse(string? text)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0) return null;

        if (DeepLinkService.TryParseJoin(t, out var linked)) t = linked;

        t = t.ToUpperInvariant();
        return Code.IsMatch(t) ? t : null;
    }
}
