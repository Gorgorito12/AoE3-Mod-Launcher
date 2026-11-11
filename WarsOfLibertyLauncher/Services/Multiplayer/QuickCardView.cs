using System.Collections.Generic;
using WarsOfLibertyLauncher.Localization;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>Which of the quick match bar's states the page is in.</summary>
internal enum QuickCardState
{
    /// <summary>Nothing running: the bar says what a click would do.</summary>
    Ready,
    /// <summary>Reading the list, joining or opening a room.</summary>
    Looking,
    /// <summary>Alone in its own room, waiting for a rival.</summary>
    Waiting,
    /// <summary>A rival is in the search's room.</summary>
    Found,
    /// <summary>The launcher must update before it can play online.</summary>
    Outdated,
    /// <summary>The chosen mod is not installed.</summary>
    NoMod,
    /// <summary>
    /// The chosen mod has no ranked ladder. Only the server knows that, and only when a competitive
    /// room is created: the launcher learns it from the first refusal and remembers it for the
    /// session. It is no longer a dead end: the search goes casual.
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

/// <summary>What the bar's buttons do. <see cref="SwitchMod"/> is the "Change" link of the notice
/// about somebody waiting in another installed mod.</summary>
internal enum QuickCardAction { Search, Cancel, ViewRoom, Stop, Update, Library, LeaveAndSearch, SwitchMod }

/// <summary>The small mark beside the title: a spinner while looking, a pulse while waiting, a green dot when found.</summary>
internal enum QuickCardMark { None, Spinner, Pulse, Found }

internal sealed record QuickCardButton(QuickCardAction Action, string Caption, bool Primary);

/// <summary>Everything the bar shows.</summary>
/// <param name="ShowPickers">The mod picker and the kind segment: only while nothing is under way,
/// since a running search has fixed both.</param>
/// <param name="Elsewhere">"Somebody is waiting in Hundred Days", with its "Change" link; null when
/// there is nothing to say.</param>
internal sealed record QuickCardModel(
    QuickCardMark Mark, string Title, bool ShowClock, string Line,
    IReadOnlyList<QuickCardButton> Buttons, bool ShowPickers, string? Elsewhere);

/// <param name="Host">The host of the room the search would enter (Wait = Room).</param>
/// <param name="Minutes">How long that room has waited.</param>
/// <param name="RoomCasual">The room the search would enter is a casual one.</param>
/// <param name="OtherRooms">How many rooms wait with another version (Wait = OtherVersion).</param>
/// <param name="Peak">"17:00 y 20:00", when the community payload knows the busy hours.</param>
/// <param name="Hosting">In the Found state: I host, so I create the game in AoE3.</param>
/// <param name="ElsewhereMod">Another installed mod with somebody waiting, by name.</param>
internal sealed record QuickCardFacts(
    QuickCardState State, QuickCardWait Wait, string Mod,
    QuickMatchKind Kind = QuickMatchKind.Ranked,
    string? Host = null, int Minutes = 0, bool RoomCasual = false, int OtherRooms = 0, string? Peak = null,
    string? Rival = null, bool Hosting = false, string? RoomName = null, string? ElsewhereMod = null);

/// <summary>
/// The quick match bar's words and buttons, one state at a time (2026-11-10: a one-line bar of about
/// 56 px with the mod and the kind of match to choose, replacing design 86's tall card). Pure, so
/// every state is pinned by a test and the page code only paints.
/// </summary>
internal static class QuickCardView
{
    /// <summary>"rated", "casual", "rated or casual": the kind said inside a sentence.</summary>
    internal static string KindText(QuickMatchKind kind) => Strings.Get(kind switch
    {
        QuickMatchKind.Casual => "QcKindCasual",
        QuickMatchKind.Either => "QcKindEither",
        _ => "QcKindRanked",
    });

    internal static QuickCardModel Of(QuickCardFacts f)
    {
        var mark = QuickCardMark.None;
        var title = Strings.Get("QcBarTitle");
        var clock = false;
        string line;
        var pickers = false;
        string? elsewhere = null;
        var buttons = new List<QuickCardButton>();
        QuickCardButton B(QuickCardAction a, string key, bool primary) => new(a, Strings.Get(key), primary);

        switch (f.State)
        {
            case QuickCardState.Looking:
                mark = QuickCardMark.Spinner;
                title = Strings.Format("QcLookingBar", f.Mod, KindText(f.Kind));
                line = Strings.Get("QcLookingLine");
                buttons.Add(B(QuickCardAction.Cancel, "QcCancel", false));
                break;
            case QuickCardState.Waiting:
                mark = QuickCardMark.Pulse;
                title = Strings.Get("QcWaitingTitle");
                clock = true;
                line = Strings.Format("QcWaitingBarLine", f.Mod, KindText(f.Kind));
                buttons.Add(B(QuickCardAction.ViewRoom, "QcViewRoom", false));
                buttons.Add(B(QuickCardAction.Stop, "QcStop", false));
                break;
            case QuickCardState.Found:
                mark = QuickCardMark.Found;
                title = Strings.Format("QcFoundTitle", f.Rival ?? "");
                line = Strings.Get("QcFoundReady") + " " + (f.Hosting
                    ? Strings.Format("QcFoundYouHost", f.Rival ?? "")
                    : Strings.Format("QcFoundTheyHost", f.Rival ?? ""));
                buttons.Add(B(QuickCardAction.ViewRoom, "QcViewRoom", true));
                break;
            case QuickCardState.Outdated:
                line = Strings.Get("QcOutdatedLine");
                buttons.Add(B(QuickCardAction.Update, "QcUpdate", true));
                break;
            case QuickCardState.NoMod:
                pickers = true;
                line = Strings.Format("QcNoModBarLine", f.Mod);
                buttons.Add(B(QuickCardAction.Library, "QcLibrary", false));
                break;
            case QuickCardState.InOtherRoom:
                line = Strings.Format("QcInOtherRoomLine", f.RoomName ?? "");
                buttons.Add(B(QuickCardAction.ViewRoom, "QcViewRoom", false));
                buttons.Add(B(QuickCardAction.LeaveAndSearch, "QcLeaveAndSearch", true));
                break;
            default:
                // Ready, and NotRanked: a mod with no ladder searches casual instead of offering nothing.
                pickers = true;
                line = f.Wait switch
                {
                    QuickCardWait.Room => f.RoomCasual
                        ? Strings.Format("QcWaitRoomCasual", f.Host ?? "", f.Minutes)
                        : Strings.Format("QcWaitRoom", f.Host ?? "", f.Minutes),
                    QuickCardWait.OtherVersion => Strings.Format(f.OtherRooms == 1 ? "QcWaitOtherVersionOne" : "QcWaitOtherVersionMany", f.OtherRooms, f.Mod),
                    QuickCardWait.Unknown => Strings.Get("QcWaitUnknown"),
                    _ => Strings.Get("QcWaitNobodyBar") + (string.IsNullOrEmpty(f.Peak) ? "" : " " + Strings.Format("QcPeak", f.Peak)),
                };
                if (f.State == QuickCardState.NotRanked && f.Wait != QuickCardWait.Room)
                    line = Strings.Format("QcNotRankedCasualLine", f.Mod) + " " + line;
                if (f.Wait is QuickCardWait.Nobody or QuickCardWait.OtherVersion && !string.IsNullOrEmpty(f.ElsewhereMod))
                {
                    line = Strings.Format("QcNobodyIn", f.Mod);
                    elsewhere = Strings.Format("QcElsewhere", f.ElsewhereMod);
                }
                buttons.Add(B(QuickCardAction.Search, "QcSearch1v1", true));
                break;
        }
        return new QuickCardModel(mark, title, clock, line, buttons, pickers, elsewhere);
    }
}
