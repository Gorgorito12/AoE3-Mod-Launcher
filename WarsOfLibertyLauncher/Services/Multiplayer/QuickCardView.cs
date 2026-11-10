using System.Collections.Generic;
using WarsOfLibertyLauncher.Localization;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>Which of the quick match card's states the page is in (design 86, PartidaRapidaTarjeta).</summary>
internal enum QuickCardState
{
    /// <summary>Nothing running: the card says what a click would do.</summary>
    Ready,
    /// <summary>Reading the list, joining or opening a room.</summary>
    Looking,
    /// <summary>Alone in its own room, waiting for a rival.</summary>
    Waiting,
    /// <summary>A rival is in the search's room.</summary>
    Found,
    /// <summary>The launcher must update before it can play online.</summary>
    Outdated,
    /// <summary>The active mod is not installed.</summary>
    NoMod,
    /// <summary>
    /// The active mod has no ranked ladder. Only the server knows that, and only when a competitive
    /// room is created: the launcher learns it from the first refusal and remembers it for the session.
    /// </summary>
    NotRanked,
    /// <summary>Already in another room: searching would leave it.</summary>
    InOtherRoom,
}

/// <summary>What the list says about a rival before searching, in the Ready state.</summary>
internal enum QuickCardWait
{
    /// <summary>A room of this mod and version waits for a 1v1 rival.</summary>
    Room,
    /// <summary>Nobody waits.</summary>
    Nobody,
    /// <summary>Only rooms with another version of the mod wait.</summary>
    OtherVersion,
    /// <summary>The list or my copy's fingerprint is not known yet.</summary>
    Unknown,
}

/// <summary>What the card's buttons do.</summary>
internal enum QuickCardAction { Search, Cancel, ViewRoom, Stop, Update, Library, LeaveAndSearch }

/// <summary>The small mark beside the kicker: a spinner while looking, a pulse while waiting, a green dot when found.</summary>
internal enum QuickCardMark { None, Spinner, Pulse, Found }

internal sealed record QuickCardButton(QuickCardAction Action, string Caption, bool Primary);

/// <summary>Everything the card shows.</summary>
internal sealed record QuickCardModel(
    string? Kicker, bool KickerGreen, QuickCardMark Mark, string Title, bool ShowClock,
    string Line, string? Foot, bool ShowVersion, IReadOnlyList<QuickCardButton> Buttons);

/// <param name="Host">The host of the room the search would enter (Wait = Room).</param>
/// <param name="Minutes">How long that room has waited.</param>
/// <param name="OtherRooms">How many rooms wait with another version (Wait = OtherVersion).</param>
/// <param name="Peak">"17:00 y 20:00", when the community payload knows the busy hours.</param>
/// <param name="Hosting">In the Found state: I host, so I create the game in AoE3.</param>
internal sealed record QuickCardFacts(
    QuickCardState State, QuickCardWait Wait, string Mod, bool Compact,
    string? Host = null, int Minutes = 0, int OtherRooms = 0, string? Peak = null,
    string? Rival = null, bool Hosting = false, string? RoomName = null);

/// <summary>
/// The quick match card's words and buttons, one state at a time. Pure, so every state is
/// pinned by a test and the page code only paints.
/// </summary>
internal static class QuickCardView
{
    internal static QuickCardModel Of(QuickCardFacts f)
    {
        string kicker = Strings.Get("QcKicker");
        var green = false;
        var mark = QuickCardMark.None;
        var title = Strings.Format("QcTitle", f.Mod);
        var clock = false;
        string line;
        string? foot = null;
        var version = true;
        var buttons = new List<QuickCardButton>();
        QuickCardButton B(QuickCardAction a, string key, bool primary) => new(a, Strings.Get(key), primary);

        switch (f.State)
        {
            case QuickCardState.Looking:
                mark = QuickCardMark.Spinner;
                title = Strings.Format("QcLookingTitle", f.Mod);
                line = Strings.Get("QcLookingLine");
                version = false;
                buttons.Add(B(QuickCardAction.Cancel, "QcCancel", false));
                break;
            case QuickCardState.Waiting:
                mark = QuickCardMark.Pulse;
                kicker = Strings.Get("QcKickerWaiting");
                title = Strings.Get("QcWaitingTitle");
                clock = true;
                line = Strings.Format("QcWaitingLine", f.Mod);
                foot = Strings.Get("QcWaitingFoot");
                version = false;
                buttons.Add(B(QuickCardAction.ViewRoom, "QcViewRoom", false));
                buttons.Add(B(QuickCardAction.Stop, "QcStop", false));
                break;
            case QuickCardState.Found:
                mark = QuickCardMark.Found;
                kicker = Strings.Get("QcKickerFound");
                green = true;
                title = Strings.Format("QcFoundTitle", f.Rival ?? "");
                line = Strings.Get("QcFoundReady") + " " + (f.Hosting
                    ? Strings.Format("QcFoundYouHost", f.Rival ?? "")
                    : Strings.Format("QcFoundTheyHost", f.Rival ?? ""));
                version = false;
                buttons.Add(B(QuickCardAction.ViewRoom, "QcViewRoom", true));
                break;
            case QuickCardState.Outdated:
                line = Strings.Get("QcOutdatedLine");
                buttons.Add(B(QuickCardAction.Update, "QcUpdate", true));
                break;
            case QuickCardState.NoMod:
                line = Strings.Format("QcNoModLine", f.Mod);
                version = false;
                buttons.Add(B(QuickCardAction.Library, "QcLibrary", true));
                break;
            case QuickCardState.NotRanked:
                title = Strings.Format("QcNotRankedTitle", f.Mod);
                line = Strings.Format("QcNotRankedLine", f.Mod);
                version = false;
                buttons.Add(B(QuickCardAction.Library, "QcLibrary", false));
                break;
            case QuickCardState.InOtherRoom:
                line = Strings.Format("QcInOtherRoomLine", f.RoomName ?? "");
                version = false;
                buttons.Add(B(QuickCardAction.ViewRoom, "QcViewRoom", false));
                buttons.Add(B(QuickCardAction.LeaveAndSearch, "QcLeaveAndSearch", true));
                break;
            default:
                line = f.Wait switch
                {
                    QuickCardWait.Room => Strings.Format("QcWaitRoom", f.Host ?? "", f.Minutes),
                    QuickCardWait.OtherVersion => Strings.Format(f.OtherRooms == 1 ? "QcWaitOtherVersionOne" : "QcWaitOtherVersionMany", f.OtherRooms, f.Mod),
                    QuickCardWait.Unknown => Strings.Get("QcWaitUnknown"),
                    _ => Strings.Get("QcWaitNobody"),
                };
                foot = Strings.Get("QcCounts") + (string.IsNullOrEmpty(f.Peak) ? "" : " " + Strings.Format("QcPeak", f.Peak));
                buttons.Add(B(QuickCardAction.Search, "QmFindMatch", true));
                break;
        }

        if (f.Compact)
        {
            // The compact card has no kicker line: the title says what the card is, and the
            // mark sits beside it instead.
            if (f.State is QuickCardState.Ready or QuickCardState.Outdated or QuickCardState.NoMod or QuickCardState.InOtherRoom)
                title = Strings.Format("QcTitleCompact", f.Mod);
            kicker = null;
            foot = null;
            version = false;
        }
        return new QuickCardModel(kicker, green, mark, title, clock, line, foot, version, buttons);
    }
}
