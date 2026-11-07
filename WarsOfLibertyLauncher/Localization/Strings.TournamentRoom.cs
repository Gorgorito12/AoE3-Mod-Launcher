using System.Collections.Generic;

namespace WarsOfLibertyLauncher.Localization;

/// <summary>
/// The room of a bracket match (design 85). Merged into the one table by the static constructor in
/// <c>Strings.Rating.cs</c>, where a duplicate key throws. Spanish is es-419 with tuteo.
/// </summary>
public static partial class Strings
{
    private static readonly Dictionary<string, Dictionary<string, string>> TournamentRoomTable = new()
    {
        ["MpTournamentRoundEighth"] = new() { [LangEn] = "ROUND OF 16", [LangEs] = "OCTAVOS" },
        ["MpTournamentRoundSixteenth"] = new() { [LangEn] = "ROUND OF 32", [LangEs] = "DIECISEISAVOS" },
        ["RtPathSemi"] = new() { [LangEn] = "Semi-final", [LangEs] = "Semifinal" },
        ["RtCount4"] = new() { [LangEn] = "four", [LangEs] = "cuatro" },
        ["RtCount6"] = new() { [LangEn] = "six", [LangEs] = "seis" },
        ["RtWindowTitle"] = new() { [LangEn] = "Match room · {0}", [LangEs] = "Sala del cruce · {0}" },
        ["RtTitleVs"] = new() { [LangEn] = "{0} vs {1}", [LangEs] = "{0} contra {1}" },
        ["RtChip"] = new() { [LangEn] = "Tournament · {0}", [LangEs] = "Torneo · {0}" },
        ["RtViewBracket"] = new() { [LangEn] = "View bracket", [LangEs] = "Ver cuadro" },
        ["RtPanelMatch"] = new() { [LangEn] = "THE MATCH", [LangEs] = "EL CRUCE" },
        ["RtPanelStake"] = new() { [LangEn] = "WHAT'S AT STAKE", [LangEs] = "QUÉ ESTÁ EN JUEGO" },
        ["RtNote1v1"] = new() { [LangEn] = "Only the two players of this match can join: there are no free seats and no code to share.", [LangEs] = "Solo pueden entrar los dos jugadores del cruce: no hay plazas libres ni código que compartir." },
        ["RtNoteTeams"] = new() { [LangEn] = "The bracket sets the sides and line-ups: a game played with others does not count.", [LangEs] = "Los bandos y las alineaciones los fija el cuadro: si se juega con otros, la partida no cuenta." },
        ["RtCountsBody"] = new() { [LangEn] = "Before you start in AoE3, tick «Record Game». The result is read from the recording: without it, the match does not move on.", [LangEs] = "Antes de empezar en AoE3, marca «Record Game». El resultado se lee de la grabación: sin ella, el cruce no avanza." },
        ["RtSide"] = new() { [LangEn] = "Side {0}", [LangEs] = "Bando {0}" },
        ["RtSeedTip"] = new() { [LangEn] = "Seed", [LangEs] = "Cabeza de serie" },

        ["RtRoundTheFinal"] = new() { [LangEn] = "the final", [LangEs] = "la final" },
        ["RtRoundTheSemi"] = new() { [LangEn] = "the semi-finals", [LangEs] = "las semifinales" },
        ["RtRoundTheQuarter"] = new() { [LangEn] = "the quarter-finals", [LangEs] = "los cuartos" },
        ["RtRoundTheEighth"] = new() { [LangEn] = "the round of 16", [LangEs] = "los octavos" },
        ["RtRoundTheSixteenth"] = new() { [LangEn] = "the round of 32", [LangEs] = "los dieciseisavos" },
        ["RtRoundTheNext"] = new() { [LangEn] = "the next round", [LangEs] = "la siguiente ronda" },

        ["RtStakeRival"] = new() { [LangEn] = "The winner plays {0} against {1}.", [LangEs] = "Quien gane juega {0} contra {1}." },
        ["RtStakePair"] = new() { [LangEn] = "The winner plays {0} against the winner of {1} – {2}.", [LangEs] = "Quien gane juega {0} contra quien gane {1} – {2}." },
        ["RtStakeRound"] = new() { [LangEn] = "The winner plays {0}.", [LangEs] = "Quien gane juega {0}." },
        ["RtStakeFinal"] = new() { [LangEn] = "The winner takes the tournament.", [LangEs] = "Quien gane se lleva el torneo." },
        ["RtStakeFinalTeam"] = new() { [LangEn] = "The team that wins takes the tournament.", [LangEs] = "El equipo que gane se lleva el torneo." },
        ["RtStakeOut"] = new() { [LangEn] = "The loser is out.", [LangEs] = "Quien pierda queda eliminado." },
        ["RtStakeConfirm"] = new() { [LangEn] = "For the bracket to move on, a player of the other team confirms the result in their launcher.", [LangEs] = "Para que el cuadro avance, un jugador del otro equipo confirma el resultado en su launcher." },
        ["RtStakeEloAll"] = new() { [LangEn] = "It counts for the rating of all {0}.", [LangEs] = "Cuenta para el ELO de los {0}." },
        ["RtThrough"] = new() { [LangEn] = "You go through to {0}.", [LangEs] = "Pasas a {0}." },
        ["RtRivalOut"] = new() { [LangEn] = "{0} is out of the tournament.", [LangEs] = "{0} queda fuera del torneo." },
        ["RtYouOut"] = new() { [LangEn] = "You are out of the tournament.", [LangEs] = "Quedas fuera del torneo." },
        ["RtRivalThrough"] = new() { [LangEn] = "{0} goes through to {1}.", [LangEs] = "{0} pasa a {1}." },
        ["RtWonTournament"] = new() { [LangEn] = "You won {0}.", [LangEs] = "Ganaste {0}." },

        ["RtFtSoloSub"] = new() { [LangEn] = "{0} sees the room in Tournaments. If they don't show up, {1}, who runs the tournament, can give you the win.", [LangEs] = "{0} ve la sala en Torneos. Si no llega, {1}, que organiza el torneo, puede darte la victoria." },
        ["RtFtSoloSubNoName"] = new() { [LangEn] = "{0} sees the room in Tournaments. If they don't show up, whoever runs the tournament can give you the win.", [LangEs] = "{0} ve la sala en Torneos. Si no llega, quien organiza el torneo puede darte la victoria." },
        ["RtFtReadyNoRival"] = new() { [LangEn] = "You're ready · waiting for {0} to join", [LangEs] = "Estás listo · falta que entre {0}" },
        ["RtFtRivalJoined"] = new() { [LangEn] = "{0} joined", [LangEs] = "{0} entró" },
        ["RtFtInRivalRoom"] = new() { [LangEn] = "You're in {0}'s room", [LangEs] = "Estás en la sala de {0}" },
        ["RtFtReadyWaiting"] = new() { [LangEn] = "You're ready · waiting for {0}", [LangEs] = "Estás listo · falta {0}" },
        ["RtFtAutoTwo"] = new() { [LangEn] = "The game starts by itself when you're both ready.", [LangEs] = "La partida empieza sola cuando los dos estén listos." },
        ["RtFtAutoAll"] = new() { [LangEn] = "The game starts by itself when all {0} are ready.", [LangEs] = "La partida empieza sola cuando los {0} estén listos." },
        ["RtFtCountdown"] = new() { [LangEn] = "The game starts in", [LangEs] = "La partida empieza en" },
        ["RtFtCountdownHost"] = new() { [LangEn] = "When AoE3 opens, create the game in Multiplayer → LAN and tick «Record Game».", [LangEs] = "Cuando se abra AoE3, crea la partida en Multijugador → LAN y marca «Record Game»." },
        ["RtFtCountdownGuest"] = new() { [LangEn] = "When AoE3 opens, join {0}'s game from Multiplayer → LAN.", [LangEs] = "Cuando se abra AoE3, únete a la partida de {0} desde Multijugador → LAN." },
        ["RtFtGameHost"] = new() { [LangEn] = "When it ends, your launcher sends the result: don't close it until then.", [LangEs] = "Al terminar, tu launcher envía el resultado: no lo cierres hasta entonces." },
        ["RtFtGameGuest"] = new() { [LangEn] = "When it ends, {0}'s launcher sends the result.", [LangEs] = "Al terminar, el launcher de {0} envía el resultado." },
        ["RtFtMoved"] = new() { [LangEn] = "The bracket has moved on", [LangEs] = "El cuadro ya avanzó" },
        ["RtFtFollow"] = new() { [LangEn] = "You can follow the rest of the tournament in Tournaments.", [LangEs] = "Puedes seguir el resto del torneo en Torneos." },
        ["RtNextRival"] = new() { [LangEn] = "You play {0} against {1}.", [LangEs] = "Juegas {0} contra {1}." },
        ["RtNextPair"] = new() { [LangEn] = "You play {0} against the winner of {1} – {2}.", [LangEs] = "Juegas {0} contra quien gane {1} – {2}." },
        ["RtNextRound"] = new() { [LangEn] = "You play {0}.", [LangEs] = "Juegas {0}." },
        ["RtFtNotYet"] = new() { [LangEn] = "The game doesn't count yet", [LangEs] = "La partida no cuenta todavía" },
        ["RtFtSentWaiting"] = new() { [LangEn] = "Result sent · waiting for confirmation", [LangEs] = "Resultado enviado · falta la confirmación" },
        ["RtFtVpnSub"] = new() { [LangEn] = "The others can't see you on the network until it is. You can still mark yourself ready, but you need it to play.", [LangEs] = "Los demás no te ven en la red hasta que lo esté. Puedes marcarte listo igual, pero lo necesitas para jugar." },
        ["RtBtnAbandon"] = new() { [LangEn] = "Leave the game", [LangEs] = "Abandonar la partida" },

        ["RtStAbsent"] = new() { [LangEn] = "Not here yet", [LangEs] = "No ha entrado" },
        ["RtStThrough"] = new() { [LangEn] = "Through", [LangEs] = "Pasa" },
        ["RtStOut"] = new() { [LangEn] = "Out", [LangEs] = "Eliminado" },

        ["RtAbandonTitle"] = new() { [LangEn] = "Leave the game?", [LangEs] = "¿Abandonar la partida?" },
        ["RtAbandonBody1v1"] = new() { [LangEn] = "The match game is under way. If you leave after the first 5 minutes, it can count as walking out: your rival's recording decides the match, and you lose it.", [LangEs] = "La partida del cruce está en marcha. Si sales después de los primeros 5 minutos, puede contar como abandono: la grabación de tu rival decide el cruce y lo pierdes." },
        ["RtAbandonBodyTeams"] = new() { [LangEn] = "The match game is under way. Your team plays on without you, and the recording decides the match.", [LangEs] = "La partida del cruce está en marcha. Tu equipo sigue sin ti, y el cruce lo decide la grabación." },
        ["RtAbandonStay"] = new() { [LangEn] = "Keep playing", [LangEs] = "Seguir jugando" },
        ["RtAbandonGo"] = new() { [LangEn] = "Leave", [LangEs] = "Abandonar" },

        ["RtToastJoined"] = new() { [LangEn] = "{0} joined your match room", [LangEs] = "{0} entró en la sala del cruce" },
        ["RtToastBody"] = new() { [LangEn] = "{0} · {1}. Mark yourself ready to start.", [LangEs] = "{0} · {1}. Márcate como listo para empezar." },
    };
}
