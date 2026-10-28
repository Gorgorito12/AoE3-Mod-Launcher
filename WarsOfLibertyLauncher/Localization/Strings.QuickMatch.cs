using System.Collections.Generic;

namespace WarsOfLibertyLauncher.Localization;

/// <summary>
/// The quick match (phase 0, docs/DISENO-PARTIDA-RAPIDA.md): one button that joins a competitive
/// 1v1 room of the active mod waiting for a rival, or opens one and waits. Merged into the one table
/// by the static constructor in <c>Strings.Rating.cs</c>, where a duplicate key throws.
///
/// <para>The ROOM TITLE is not here, on purpose: it is a network value every player reads, built
/// by <c>RoomTitleProposal.QuickMatch</c> from an English constant (see "Why the title is not
/// localized" there).</para>
///
/// <para>Spanish is es-419 with tuteo, like the rest of the launcher.</para>
/// </summary>
public static partial class Strings
{
    private static readonly Dictionary<string, Dictionary<string, string>> QuickMatchTable = new()
    {
        ["QmFindMatch"] = new() { [LangEn] = "Find a 1v1 match", [LangEs] = "Buscar partida 1v1" },
        ["QmFindMatchTip"] = new()
        {
            [LangEn] = "Joins a competitive 1v1 room of your active mod that is waiting for a rival, or opens one "
                       + "and waits until somebody joins. It counts for your rating.",
            [LangEs] = "Entra en una sala competitiva 1v1 de tu mod activo que esté esperando rival, o abre una "
                       + "y espera a que alguien entre. Cuenta para tu ELO.",
        },
        // {0} is the time searched, "02:13".
        ["QmSearching"] = new() { [LangEn] = "Searching… {0} · Cancel", [LangEs] = "Buscando… {0} · Cancelar" },
        ["QmSearchingTip"] = new()
        {
            [LangEn] = "Your room is listed for anybody looking for a 1v1. Click to stop searching and close it.",
            [LangEs] = "Tu sala está anunciada para quien busque un 1v1. Haz clic para dejar de buscar y cerrarla.",
        },
        // The title-bar pill: {0} is the time searched.
        ["QmPill"] = new() { [LangEn] = "Searching 1v1 · {0}", [LangEs] = "Buscando 1v1 · {0}" },
        // {0} is the mod's name.
        ["QmPillTip"] = new()
        {
            [LangEn] = "Searching for a 1v1 rival in {0}. Click to see your room.",
            [LangEs] = "Buscando rival 1v1 en {0}. Haz clic para ver tu sala.",
        },
        ["QmPillCancelTip"] = new() { [LangEn] = "Stop searching", [LangEs] = "Dejar de buscar" },
        // Chat system lines in the room.
        ["QmWaitingInRoom"] = new()
        {
            [LangEn] = "Searching for a rival… Your room is listed; you'll be told when somebody joins.",
            [LangEs] = "Buscando rival… Tu sala está anunciada; te avisamos cuando alguien entre.",
        },
        ["QmMovedToOlderRoom"] = new()
        {
            [LangEn] = "Another player was already searching: you joined their room.",
            [LangEs] = "Otro jugador ya estaba buscando: entraste en su sala.",
        },
        ["QmJoinedRoom"] = new()
        {
            [LangEn] = "Quick match: you joined a room that was waiting for a rival.",
            [LangEs] = "Partida rápida: entraste en una sala que esperaba rival.",
        },
        ["QmFoundTitle"] = new() { [LangEn] = "Opponent found!", [LangEs] = "¡Rival encontrado!" },
        // {0} is the rival's name.
        ["QmFoundBody"] = new()
        {
            [LangEn] = "{0} joined your quick match. Mark yourself ready to start.",
            [LangEs] = "{0} entró en tu partida rápida. Márcate como listo para empezar.",
        },
        ["QmFoundView"] = new() { [LangEn] = "See room", [LangEs] = "Ver sala" },
        ["QmTimedOut"] = new()
        {
            [LangEn] = "Nobody joined in 30 minutes, so the search stopped and your room was closed.",
            [LangEs] = "Nadie entró en 30 minutos, así que la búsqueda se detuvo y tu sala se cerró.",
        },
        ["QmCancelled"] = new() { [LangEn] = "Search stopped.", [LangEs] = "Búsqueda detenida." },
        ["QmStoppedByGame"] = new()
        {
            [LangEn] = "Search stopped: you started a game.",
            [LangEs] = "Búsqueda detenida: abriste un juego.",
        },
        // {0} is how many rooms are waiting with another version of the same mod.
        ["QmOtherVersion"] = new()
        {
            [LangEn] = "Rooms waiting with another version of this mod: {0}. Update or repair yours to play them.",
            [LangEs] = "Salas esperando con otra versión de este mod: {0}. Actualiza o repara el tuyo para jugar en ellas.",
        },
        ["QmModNotRanked"] = new()
        {
            [LangEn] = "This mod has no ranked ladder, so it has no quick match. Your room was closed.",
            [LangEs] = "Este mod no tiene clasificación, así que no tiene partida rápida. Tu sala se cerró.",
        },
        ["QmModMissing"] = new()
        {
            [LangEn] = "The quick match plays your active mod, and it isn't installed. Install it from the Library first.",
            [LangEs] = "La partida rápida juega con tu mod activo, y no está instalado. Instálalo primero desde la Biblioteca.",
        },
        // {0} is the server's or the network's message.
        ["QmFailed"] = new()
        {
            [LangEn] = "The quick match could not start: {0}",
            [LangEs] = "No se pudo iniciar la partida rápida: {0}",
        },
        // Shown while the search keeps going: Radmin is needed to PLAY the match, not to find it.
        ["QmRadminTitle"] = new() { [LangEn] = "Radmin VPN isn't ready", [LangEs] = "Radmin VPN no está listo" },
        ["QmRadminNotice"] = new()
        {
            [LangEn] = "Radmin VPN isn't on. The search goes on, but you'll need it to play the match.",
            [LangEs] = "Radmin VPN no está encendido. La búsqueda sigue, pero lo necesitarás para jugar la partida.",
        },
        ["QmRadminOpen"] = new() { [LangEn] = "Open the assistant", [LangEs] = "Abrir el asistente" },
        // The result card's second button, beside "Close".
        ["QmFindAnother"] = new() { [LangEn] = "Find another", [LangEs] = "Buscar otra" },
        ["QmFindAnotherTip"] = new()
        {
            [LangEn] = "Leave this room and look for another 1v1 rival right away.",
            [LangEs] = "Sal de esta sala y busca otro rival 1v1 enseguida.",
        },
    };
}
