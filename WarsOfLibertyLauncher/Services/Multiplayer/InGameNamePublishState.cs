using System;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Whether our AoE3 profile name has actually reached the ROOM — and therefore whether to send it
/// (again).
///
/// <para><b>Why this is a state machine and not a "last sent" string.</b> The first competitive
/// 2v2s went down with two and three of their four names missing, and every way they were lost
/// had the same shape: the launcher remembered what it had SENT and treated that as what the room
/// KNEW. A frame written to a socket that was not open yet is dropped without a word; a frame
/// that lands before the server has handled our <c>hello</c> is refused; and a reconnect makes the
/// server rebuild our member WITHOUT the name, while the old guard still said "already sent". Each
/// of those left the name missing for the whole match, and one missing name refuses the team map
/// for everybody.</para>
///
/// <para>So the rules are about what the SERVER says, never about what we wrote:</para>
/// <list type="bullet">
///   <item><b>Send only once a <c>room_state</c> has arrived on the current connection</b> — that
///         frame is the server's answer to our hello, so nothing sent after it can be read as
///         unauthenticated.</item>
///   <item><b>Confirmed only by the server</b>: our name inside that <c>room_state</c>, or the
///         <c>member_ingame_name</c> broadcast for our own id (the server sends it to the sender
///         too). Until then every lobby tick sends again; the server ignores an unchanged name, so
///         a resend costs one small frame.</item>
///   <item><b>A lost connection forgets the confirmation</b>, because the server will rebuild us
///         without the name — and the next <c>room_state</c> is what says so.</item>
/// </list>
///
/// <para>Pure and free of WPF, like its neighbours, so the refusals can be pinned.</para>
/// </summary>
public sealed class InGameNamePublishState
{
    /// <summary>Whether a <c>room_state</c> has arrived on the current connection.</summary>
    public bool Ready { get; private set; }

    /// <summary>The name the SERVER holds for us, or null when it holds none we know of.</summary>
    public string? Confirmed { get; private set; }

    /// <summary>A different room's socket: nothing is known about it yet.</summary>
    public void Reset()
    {
        Ready = false;
        Confirmed = null;
    }

    /// <summary>
    /// The connection dropped. Until the next <c>room_state</c> nothing may be sent — it could
    /// land before the hello — and whatever the server held for us is gone with the member it
    /// deletes on close.
    /// </summary>
    public void ConnectionLost()
    {
        Ready = false;
        Confirmed = null;
    }

    /// <summary>
    /// A <c>room_state</c> arrived: the hello was handled, and <paramref name="ourNameOnServer"/>
    /// is what the server holds for OUR member (null when it holds nothing).
    /// </summary>
    public void RoomState(string? ourNameOnServer)
    {
        Ready = true;
        Confirmed = Clean(ourNameOnServer);
    }

    /// <summary>The server announced a name for OUR id.</summary>
    public void Echo(string? name)
    {
        var clean = Clean(name);
        if (clean != null) Confirmed = clean;
    }

    /// <summary>
    /// Whether <paramref name="name"/> should go out now: there is one, the connection is known
    /// to be past its hello, and the server does not already hold exactly this name.
    /// </summary>
    public bool ShouldSend(string? name)
    {
        var clean = Clean(name);
        if (clean == null || !Ready) return false;
        return !string.Equals(Confirmed, clean, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same trimming the server applies (<c>frame.name.trim()</c>), so a name we read with a
    /// stray space is not sent again for ever against the server's trimmed copy of it.
    /// </summary>
    private static string? Clean(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
