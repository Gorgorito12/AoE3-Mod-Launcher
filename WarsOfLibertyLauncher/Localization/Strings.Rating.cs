using System.Collections.Generic;

namespace WarsOfLibertyLauncher.Localization;

/// <summary>
/// The rating system's strings (design handoff 55, <c>docs/design_elo/README.md</c> §10): ranking,
/// placement, streaks, profile mode cards, head-to-head, team rooms and win probability, the team
/// countdown, the result card, the History, monthly highlights and refunds.
///
/// <para>Kept in their own file because there are many and they belong together; they are merged
/// into the one table at startup, and a key that already exists there THROWS (an
/// <c>ArgumentException</c> from <c>Dictionary.Add</c>) — a duplicate key must fail loudly, never
/// silently shadow the other.</para>
///
/// <para>Spanish is es-419 with tuteo, like the rest of the launcher.</para>
/// </summary>
public static partial class Strings
{
    static Strings()
    {
        foreach (var kv in RatingTable) Table.Add(kv.Key, kv.Value);
    }

    private static readonly Dictionary<string, Dictionary<string, string>> RatingTable = new()
    {
        // ---------------------------------------------------------------- 55a-55c ranking
        ["MpRankCountSummary"] = new() { [LangEn] = "{0} ranked · {1} in placement", [LangEs] = "{0} clasificados · {1} en posicionamiento" },
        ["MpRankColNum"] = new() { [LangEn] = "#", [LangEs] = "#" },
        ["MpRankColPlayer"] = new() { [LangEn] = "PLAYER", [LangEs] = "JUGADOR" },
        ["MpRankColElo"] = new() { [LangEn] = "ELO", [LangEs] = "ELO" },
        ["MpRankColWL"] = new() { [LangEn] = "W-L", [LangEs] = "V-D" },
        ["MpRankColPct"] = new() { [LangEn] = "%", [LangEs] = "%" },
        ["MpRankYouTag"] = new() { [LangEn] = "YOU", [LangEs] = "TÚ" },
        ["MpPlacementProgress"] = new() { [LangEn] = "Placement {0}/{1}", [LangEs] = "Posicionamiento {0}/{1}" },
        ["MpPlacementProgressShort"] = new() { [LangEn] = "{0}/{1}", [LangEs] = "{0}/{1}" },
        // Design 59's footnote, inside the table: {0} is the days without a decided match.
        ["MpRankFootRule"] = new()
        {
            [LangEn] = "Only competitive matches with a result count. 🔥 marks 3 or more wins in a row. Inactive: no decided match in {0} days.",
            [LangEs] = "Solo cuentan partidas competitivas con resultado. 🔥 marca 3 o más victorias seguidas. Inactivo: sin partidas decididas en {0} días.",
        },
        ["MpRankHowElo"] = new() { [LangEn] = "How ELO works", [LangEs] = "Cómo funciona el ELO" },
        ["MpStreakTipTitle"] = new() { [LangEn] = "{0} wins in a row", [LangEs] = "{0} victorias seguidas" },
        ["MpStreakTipBody"] = new()
        {
            [LangEn] = "Ends when you lose or after 14 days without a rated match.",
            [LangEs] = "Se corta al perder o tras 14 días sin jugar una partida puntuada.",
        },
        ["MpRankEmptyPlacementTitle"] = new() { [LangEn] = "Nobody has finished placement yet", [LangEs] = "Nadie completó todavía el posicionamiento" },
        ["MpRankEmptyPlacementBody"] = new()
        {
            [LangEn] = "The first ranks appear once someone plays their {0} rated matches.",
            [LangEs] = "Los primeros puestos aparecen cuando alguien juegue sus {0} partidas puntuadas.",
        },
        ["MpRankEmptyTitle"] = new() { [LangEn] = "Nobody has played a rated match yet", [LangEs] = "Todavía nadie jugó una partida puntuada" },
        ["MpRankEmptyBody"] = new()
        {
            [LangEn] = "Create a competitive room and turn on Record Game so the result counts.",
            [LangEs] = "Crea una sala competitiva y activa Record Game para que el resultado cuente.",
        },
        ["MpRankCreateRoom"] = new() { [LangEn] = "+ Create room", [LangEs] = "+ Crear sala" },
        ["MpModeOneVsOne"] = new() { [LangEn] = "1v1", [LangEs] = "1v1" },
        ["MpRankInactiveTag"] = new() { [LangEn] = "INACTIVE", [LangEs] = "INACTIVO" },
        ["MpRankPctFromTip"] = new() { [LangEn] = "Shown from {0} matches", [LangEs] = "Se muestra a partir de {0} partidas" },
        ["MpEloProvisional"] = new() { [LangEn] = "{0}?", [LangEs] = "{0}?" },
        ["MpEloProvisionalTip"] = new()
        {
            [LangEn] = "Provisional ELO: it settles during placement.",
            [LangEs] = "ELO provisional: se ajusta durante el posicionamiento.",
        },

        // ---------------------------------------------------------------- 55d-55f profile
        ["MpProfileStatusRanked"] = new() { [LangEn] = "{0} in {1}", [LangEs] = "{0} en {1}" },
        ["MpProfileStatusPlacement"] = new() { [LangEn] = "in placement in {0}", [LangEs] = "en posicionamiento en {0}" },
        ["MpProfileRankOf"] = new() { [LangEn] = "rank {0} of {1}", [LangEs] = "puesto {0} de {1}" },
        ["MpProfileNoRankYet"] = new() { [LangEn] = "no rank yet", [LangEs] = "sin puesto todavía" },
        ["MpProfilePeak"] = new() { [LangEn] = "Peak: {0} · {1}", [LangEs] = "Máximo: {0} · {1}" },
        ["MpProfileLow"] = new() { [LangEn] = "Low: {0} · {1}", [LangEs] = "Mínimo: {0} · {1}" },
        ["MpProfileStreak"] = new() { [LangEn] = "CURRENT STREAK", [LangEs] = "RACHA ACTUAL" },
        ["MpProfileLongestWin"] = new() { [LangEn] = "LONGEST WIN STREAK", [LangEs] = "RACHA DE VICTORIAS MÁS LARGA" },
        ["MpProfileLongestLoss"] = new() { [LangEn] = "LONGEST LOSING STREAK", [LangEs] = "RACHA DE DERROTAS MÁS LARGA" },
        ["MpProfileStreakTipTitle"] = new() { [LangEn] = "Current streak: {0} wins in a row", [LangEs] = "Racha actual: {0} victorias seguidas" },
        ["MpProfileStreakTipBody"] = new()
        {
            [LangEn] = "Ends when you lose or after 14 days without a rated match. Your longest streak is kept.",
            [LangEs] = "Se corta al perder o tras 14 días sin jugar una partida puntuada. La racha más larga queda guardada.",
        },
        ["MpProfileStreakExpired"] = new() { [LangEn] = "Ended {0}: 14 days without playing", [LangEs] = "Se cortó el {0}: 14 días sin jugar" },
        ["MpProfilePlacementLine"] = new() { [LangEn] = "Placement {0}", [LangEs] = "Posicionamiento {0}" },
        ["MpPlacementLeft"] = new() { [LangEn] = "{0} more rated matches to enter the table.", [LangEs] = "Faltan {0} partidas puntuadas para entrar en la tabla." },
        ["MpPlacementLeftOne"] = new() { [LangEn] = "1 more rated match to enter the table.", [LangEs] = "Falta 1 partida puntuada para entrar en la tabla." },
        ["MpProfileModeEmptyYou"] = new()
        {
            [LangEn] = "You haven't played rated matches in {0} yet. Your first {1} are placement.",
            [LangEs] = "Todavía no jugaste partidas puntuadas en {0}. Tus primeras {1} son de posicionamiento.",
        },
        ["MpProfileModeEmptyOther"] = new() { [LangEn] = "No rated matches in {0} yet.", [LangEs] = "Todavía no jugó partidas puntuadas en {0}." },
        ["MpProfileNoMatchesYou"] = new()
        {
            [LangEn] = "You haven't played rated matches yet. Your first match in each mode starts its placement.",
            [LangEs] = "Todavía no jugaste partidas puntuadas. Tu primera partida en cada modo empieza su posicionamiento.",
        },
        ["MpProfileNoMatchesOther"] = new() { [LangEn] = "No rated matches yet.", [LangEs] = "Todavía no jugó partidas puntuadas." },
        ["MpInactiveNotice"] = new()
        {
            [LangEn] = "Inactive: you haven't played a rated match in 30 days. You keep your place in the table.",
            [LangEs] = "Inactivo: llevas 30 días sin jugar una partida puntuada. Conservas tu puesto en la tabla.",
        },
        ["MpInactiveNoticeOther"] = new() { [LangEn] = "Inactive: no rated match in 30 days.", [LangEs] = "Inactivo: lleva 30 días sin jugar una partida puntuada." },
        ["MpInactiveLast"] = new() { [LangEn] = "Last rated match: {0}", [LangEs] = "Última partida puntuada: {0}" },
        ["MpH2HTitle"] = new() { [LangEn] = "Against each opponent", [LangEs] = "Contra cada rival" },
        ["MpH2HSub"] = new() { [LangEn] = "rated matches", [LangEs] = "partidas puntuadas" },
        ["MpH2HRow"] = new() { [LangEn] = "Against {0}", [LangEs] = "Contra {0}" },
        ["MpH2HRowTip"] = new() { [LangEn] = "Against {0}: {1}–{2}", [LangEs] = "Contra {0}: {1}–{2}" },
        ["MpH2HSeeAll"] = new() { [LangEn] = "See all {0} opponents", [LangEs] = "Ver los {0} rivales" },
        ["MpH2HSeeLess"] = new() { [LangEn] = "See fewer", [LangEs] = "Ver menos" },
        ["MpH2HEmptyYou"] = new()
        {
            [LangEn] = "You haven't played anyone yet. Your record against each opponent will show here.",
            [LangEs] = "Todavía no jugaste contra nadie. Aquí verás tu balance con cada rival.",
        },
        ["MpH2HEmptyOther"] = new() { [LangEn] = "No opponents yet.", [LangEs] = "Todavía no jugó contra nadie." },
        // How long since a date, in whole days ("hoy", "hace 2 días", "hace 3 semanas").
        ["MpAgoToday"] = new() { [LangEn] = "today", [LangEs] = "hoy" },
        ["MpAgoYesterday"] = new() { [LangEn] = "yesterday", [LangEs] = "ayer" },
        ["MpAgoDays"] = new() { [LangEn] = "{0} days ago", [LangEs] = "hace {0} días" },
        ["MpAgoWeek"] = new() { [LangEn] = "1 week ago", [LangEs] = "hace 1 semana" },
        ["MpAgoWeeks"] = new() { [LangEn] = "{0} weeks ago", [LangEs] = "hace {0} semanas" },
        ["MpAgoMonth"] = new() { [LangEn] = "1 month ago", [LangEs] = "hace 1 mes" },
        ["MpAgoMonths"] = new() { [LangEn] = "{0} months ago", [LangEs] = "hace {0} meses" },
        ["MpAgoYear"] = new() { [LangEn] = "1 year ago", [LangEs] = "hace 1 año" },
        ["MpAgoYears"] = new() { [LangEn] = "{0} years ago", [LangEs] = "hace {0} años" },

        // ---------------------------------------------------------------- 55g-55h rooms
        ["MpRoomPlayers1v1"] = new() { [LangEn] = "PLAYERS · COMPETITIVE 1V1", [LangEs] = "JUGADORES · COMPETITIVA 1V1" },
        ["MpRoomPlayersTeams"] = new() { [LangEn] = "PLAYERS · COMPETITIVE {0} · {1} OF {2}", [LangEs] = "JUGADORES · COMPETITIVA {0} · {1} DE {2}" },
        ["MpRoomPlayersTeamsCasual"] = new() { [LangEn] = "PLAYERS · {0} · {1} OF {2}", [LangEs] = "JUGADORES · {0} · {1} DE {2}" },
        ["MpRoomRecord"] = new() { [LangEn] = "your record {0}–{1}", [LangEs] = "tu balance {0}–{1}" },
        // {0} is MpPercentValue, so the figure AND its sign are drawn bold (55g).
        ["MpWinProb1v1"] = new() { [LangEn] = "You have a {0} chance to win", [LangEs] = "Tienes un {0} de probabilidad de ganar" },
        ["MpPercentValue"] = new() { [LangEn] = "{0}%", [LangEs] = "{0} %" },
        ["MpWinProbTeams"] = new() { [LangEn] = "Team 1: {0}% · Team 2: {1}%", [LangEs] = "Equipo 1: {0} % · Equipo 2: {1} %" },
        ["MpWinProbTeamsNote"] = new()
        {
            [LangEn] = "Based on each team's average ELO. Updates when someone moves.",
            [LangEs] = "Según el ELO medio de cada equipo. Cambia al mover a alguien.",
        },
        ["MpWinProbLabel"] = new() { [LangEn] = "WIN CHANCE", [LangEs] = "PROBABILIDAD" },
        ["MpWinProbTeamsEmpty"] = new() { [LangEn] = "Shows once both teams have players.", [LangEs] = "Aparece cuando los dos equipos tengan jugadores." },
        ["MpTeam1"] = new() { [LangEn] = "TEAM 1", [LangEs] = "EQUIPO 1" },
        ["MpTeam2"] = new() { [LangEn] = "TEAM 2", [LangEs] = "EQUIPO 2" },
        ["MpNoTeam"] = new() { [LangEn] = "NO TEAM", [LangEs] = "SIN EQUIPO" },
        ["MpTeamAvg"] = new() { [LangEn] = "Avg. ELO {0}", [LangEs] = "ELO medio {0}" },
        ["MpTeamEmpty"] = new() { [LangEn] = "Nobody yet", [LangEs] = "Nadie todavía" },
        ["MpTeamYouTag"] = new() { [LangEn] = "you", [LangEs] = "tú" },
        ["MpTeamHostTag"] = new() { [LangEn] = "host", [LangEs] = "anfitrión" },
        ["MpTeamPickTip"] = new() { [LangEn] = "Team {0}", [LangEs] = "Equipo {0}" },
        ["MpTeamsMismatchWarn"] = new()
        {
            [LangEn] = "If you don't pick the same teams in the game, the match won't count",
            [LangEs] = "Si no eligen los mismos equipos dentro del juego, la partida no contará",
        },
        ["MpStartBlockedPrefix"] = new() { [LangEn] = "You can't start yet:", [LangEs] = "No puedes empezar todavía:" },
        ["MpStartBlockedPlayers"] = new() { [LangEn] = "players missing ({0} of {1}).", [LangEs] = "faltan jugadores ({0} de {1})." },
        ["MpStartBlockedNoTeam"] = new() { [LangEn] = "{0} still has to pick a team.", [LangEs] = "falta que {0} elija equipo." },
        ["MpStartBlockedNoTeamPl"] = new() { [LangEn] = "{0} still have to pick a team.", [LangEs] = "falta que {0} elijan equipo." },
        ["MpStartBlockedUneven"] = new() { [LangEn] = "teams aren't even ({0} vs {1}).", [LangEs] = "los equipos no están parejos ({0} contra {1})." },
        ["MpStartReady"] = new()
        {
            [LangEn] = "All set. When you start, everyone sees a countdown with the teams.",
            [LangEs] = "Todo listo. Al empezar verán una cuenta atrás con los equipos escritos.",
        },
        ["MpTeamErrTeamFull"] = new() { [LangEn] = "That team is full.", [LangEs] = "Ese equipo está completo." },
        ["MpTeamErrForbidden"] = new() { [LangEn] = "Only the host can move other players.", [LangEs] = "Solo el anfitrión puede mover a otros jugadores." },
        ["MpTeamErrInGame"] = new() { [LangEn] = "Teams can't change during a match.", [LangEs] = "Los equipos no se pueden cambiar durante la partida." },

        // ---------------------------------------------------------------- 55i countdown
        ["MpCountdownOpening"] = new() { [LangEn] = "OPENING THE GAME", [LangEs] = "ABRIENDO EL JUEGO" },
        ["MpCountdownTitle"] = new() { [LangEn] = "Pick these teams in the game", [LangEs] = "Elijan estos equipos en el juego" },
        // {0} and {2} are MpCountdownTeam1/2, drawn bold in each team's colour; {1} and {3} the names.
        ["MpCountdownTeams"] = new() { [LangEn] = "{0} {1} · {2} {3}. Pick this in the game.", [LangEs] = "{0} {1} · {2} {3}. Elijan esto en el juego." },
        ["MpCountdownTeam1"] = new() { [LangEn] = "Team 1:", [LangEs] = "Equipo 1:" },
        ["MpCountdownTeam2"] = new() { [LangEn] = "Team 2:", [LangEs] = "Equipo 2:" },
        ["MpCountdownFoot"] = new() { [LangEn] = "If they don't match, the match won't count.", [LangEs] = "Si no coinciden, la partida no contará." },

        // ---------------------------------------------------------------- 55j result card
        ["MpResultVs"] = new() { [LangEn] = "vs {0} · {1}", [LangEs] = "contra {0} · {1}" },
        ["MpResultVsAlone"] = new() { [LangEn] = "vs {0}", [LangEs] = "contra {0}" },
        ["MpResultTeamsVs"] = new() { [LangEn] = "{0} vs {1}", [LangEs] = "{0} contra {1}" },
        ["MpResultStreak"] = new() { [LangEn] = "{0} wins in a row", [LangEs] = "{0} victorias seguidas" },
        ["MpResultFarmWin"] = new()
        {
            [LangEn] = "{0} ({1}%): {2} win in a row against this opponent. It goes back to normal if they win one, or recovers 10% for each day you two don't play.",
            [LangEs] = "{0} ({1} %): {2} victoria seguida contra este rival. Vuelve a la normalidad si él gana una, o se recupera un 10 % por cada día sin jugar entre ustedes.",
        },
        ["MpResultFarmLoss"] = new()
        {
            [LangEn] = "{0} ({1}%): {2} has beaten you {3} times in a row. It goes back to normal if you win one, or recovers 10% for each day you two don't play.",
            [LangEs] = "{0} ({1} %): {2} te ganó {3} veces seguidas. Vuelve a la normalidad si le ganas una, o se recupera un 10 % por cada día sin jugar entre ustedes.",
        },
        ["MpResultTeamsMismatch"] = new()
        {
            [LangEn] = "The match didn't count: the in-game teams didn't match the room's (in the game: {0} vs {1}).",
            [LangEs] = "La partida no contó: los equipos del juego no coincidieron con los de la sala (en el juego: {0} contra {1}).",
        },
        ["MpResultTeamsMismatchShort"] = new()
        {
            [LangEn] = "The match didn't count: the in-game teams didn't match the room's.",
            [LangEs] = "La partida no contó: los equipos del juego no coincidieron con los de la sala.",
        },
        ["MpResultPlacementDone"] = new() { [LangEn] = "Placement complete!", [LangEs] = "¡Posicionamiento completado!" },
        ["MpResultPlacementRank"] = new() { [LangEn] = "You enter the table at rank {0}.", [LangEs] = "Entras en la tabla en el puesto {0}." },
        ["MpResultPlacementLeft"] = new() { [LangEn] = "Placement {0}/{1}", [LangEs] = "Posicionamiento {0}/{1}" },
        ["MpResultNewAccount"] = new()
        {
            [LangEn] = "This match didn't count for ELO (new account and a very short match).",
            [LangEs] = "Esta partida no contó para el ELO (cuenta nueva y partida muy corta).",
        },
        ["MpResultUnratedTeamsMismatch"] = new()
        {
            [LangEn] = "The match didn't count: the in-game teams didn't match the room's.",
            [LangEs] = "La partida no contó: los equipos del juego no coincidieron con los de la sala.",
        },

        // ---------------------------------------------------------------- 55k history
        ["MpHistUnrated"] = new() { [LangEn] = "UNRATED", [LangEs] = "NO PUNTUADA" },
        ["MpHistTournament"] = new() { [LangEn] = "TOURNAMENT", [LangEs] = "TORNEO" },
        ["MpHistFarmPct"] = new() { [LangEn] = "· {0}%", [LangEs] = "· {0} %" },
        ["MpHistReasonTeams"] = new() { [LangEn] = "The in-game teams didn't match the room's.", [LangEs] = "Los equipos del juego no coincidieron con los de la sala." },
        ["MpHistReasonNoResult"] = new() { [LangEn] = "The result couldn't be read: Record Game was off.", [LangEs] = "No se pudo leer el resultado: Record Game estaba desactivado." },
        ["MpHistReasonCasual"] = new() { [LangEn] = "Casual room: doesn't move ELO.", [LangEs] = "Sala casual: no mueve el ELO." },
        ["MpHistoryDayToday"] = new() { [LangEn] = "TODAY", [LangEs] = "HOY" },
        ["MpHistoryDayYesterday"] = new() { [LangEn] = "YESTERDAY", [LangEs] = "AYER" },
        ["MpHistRoundFinal"] = new() { [LangEn] = "final", [LangEs] = "final" },
        ["MpHistRoundSemi"] = new() { [LangEn] = "semi-final", [LangEs] = "semifinal" },
        ["MpHistRoundQuarter"] = new() { [LangEn] = "quarter-final", [LangEs] = "cuartos de final" },
        ["MpHistRoundN"] = new() { [LangEn] = "round {0}", [LangEs] = "ronda {0}" },
        ["MpHistReasonNewAccount"] = new()
        {
            [LangEn] = "This match didn't count for ELO (new account and a very short match).",
            [LangEs] = "Esta partida no contó para el ELO (cuenta nueva y partida muy corta).",
        },

        // ---------------------------------------------------------------- 55l-55m highlights
        ["MpHlSeePrev"] = new() { [LangEn] = "See {0}", [LangEs] = "Ver {0}" },
        ["MpHlSeeCurrent"] = new() { [LangEn] = "Back to {0}", [LangEs] = "Volver a {0}" },
        // The seven highlights' names: the card titles of Ranking › Highlights (once the cells of
        // 57a's strip, which design 60 replaced with the community block's data strip).
        ["MpHlStripTopGain"] = new() { [LangEn] = "Biggest climb", [LangEs] = "Quién más subió" },
        ["MpHlStripMostGames"] = new() { [LangEn] = "Most matches", [LangEs] = "Más partidas" },
        ["MpHlStripBestStreak"] = new() { [LangEn] = "Best streak", [LangEs] = "Mejor racha" },
        ["MpHlStripMostWins"] = new() { [LangEn] = "Most wins", [LangEs] = "Más victorias" },
        ["MpHlStripBestRate"] = new() { [LangEn] = "Best win rate", [LangEs] = "Mejor % de victorias" },
        ["MpHlStripUpset"] = new() { [LangEn] = "Biggest upset", [LangEs] = "Mayor sorpresa" },
        ["MpHlStripTopCiv"] = new() { [LangEn] = "Civilization of the month", [LangEs] = "Civilización del mes" },
        // ---- Ranking › Highlights: the month's top five of every highlight -----------------
        ["MpRankingModeHighlights"] = new() { [LangEn] = "Highlights", [LangEs] = "Destacados" },
        ["MpHlLeadersLoading"] = new() { [LangEn] = "Loading the month's highlights…", [LangEs] = "Cargando los destacados del mes…" },
        ["MpHlLeadersUnavailable"] = new()
        {
            [LangEn] = "Your server doesn't send the highlights yet.",
            [LangEs] = "Tu servidor todavía no envía los destacados.",
        },
        ["MpHlLeadersFailed"] = new()
        {
            [LangEn] = "Couldn't load the highlights. Try again in a moment.",
            [LangEs] = "No se pudieron cargar los destacados. Prueba de nuevo en un momento.",
        },
        ["MpHlLeadersEmpty"] = new() { [LangEn] = "Nothing to highlight in {0} yet.", [LangEs] = "Todavía no hay nada que destacar en {0}." },
        ["MpHlLeadersSubtitle"] = new() { [LangEn] = "{0} rated matches in {1}", [LangEs] = "{0} partidas puntuadas en {1}" },
        ["MpHlLeadersSubtitleSoFar"] = new()
        {
            [LangEn] = "{0} rated matches in {1} so far",
            [LangEs] = "{0} partidas puntuadas en {1} hasta hoy",
        },
        // The rule of each card, one line under its title.
        ["MpHlRuleClimb"] = new()
        {
            [LangEn] = "From {0} rated matches after placement",
            [LangEs] = "Desde {0} partidas puntuadas tras el posicionamiento",
        },
        ["MpHlRuleWins"] = new() { [LangEn] = "A tie goes to whoever played fewer", [LangEs] = "Si empatan, gana quien jugó menos" },
        ["MpHlRuleMatches"] = new() { [LangEn] = "Rated, 1v1 and Teams", [LangEs] = "Puntuadas, 1v1 y Equipos" },
        ["MpHlRuleStreak"] = new() { [LangEn] = "Wins in a row inside the month", [LangEs] = "Victorias seguidas dentro del mes" },
        ["MpHlRuleRate"] = new()
        {
            [LangEn] = "From {0} rated matches in the month",
            [LangEs] = "Desde {0} partidas puntuadas en el mes",
        },
        ["MpHlRuleUpset"] = new()
        {
            [LangEn] = "Average ELO before the match · placed players only",
            [LangEs] = "ELO medio antes de la partida · solo jugadores ya posicionados",
        },
        ["MpHlRuleCiv"] = new()
        {
            [LangEn] = "Most picked in rated matches · from {0} picks",
            [LangEs] = "La más elegida en partidas puntuadas · desde {0} elecciones",
        },
        // The detail line under each name.
        ["MpHlDetailClimb"] = new() { [LangEn] = "{0} → {1} · {2} matches", [LangEs] = "{0} → {1} · {2} partidas" },
        ["MpHlDetailMatches"] = new() { [LangEn] = "{0} matches", [LangEs] = "{0} partidas" },
        ["MpHlDetailWins"] = new() { [LangEn] = "out of {0} matches · {1} %", [LangEs] = "de {0} partidas · {1} %" },
        ["MpHlDetailWon"] = new() { [LangEn] = "{0} won", [LangEs] = "{0} ganadas" },
        ["MpHlDetailRate"] = new() { [LangEn] = "{0} of {1} matches", [LangEs] = "{0} de {1} partidas" },
        ["MpHlDetailUpset"] = new() { [LangEn] = "beat {0} · {1} vs {2}", [LangEs] = "le ganó a {0} · {1} contra {2}" },
        ["MpHlDetailUpsetTeam"] = new() { [LangEn] = "beat {0} · {1} vs {2}", [LangEs] = "les ganaron a {0} · {1} contra {2}" },
        ["MpHlDetailCiv"] = new() { [LangEn] = "{0} won · {1} %", [LangEs] = "{0} ganadas · {1} %" },

        // Design 60's data strip: each label goes in capitals over its value. {0} is the month's
        // abbreviation ("oct") or the window in days, both from the payload.
        ["MpFactTopGain"] = new() { [LangEn] = "Biggest climb · {0}", [LangEs] = "Quién más subió · {0}" },
        ["MpFactMostMatches"] = new() { [LangEn] = "Most matches · {0}", [LangEs] = "Más partidas · {0}" },
        ["MpFactBestStreak"] = new() { [LangEn] = "Best streak · {0}", [LangEs] = "Mejor racha · {0}" },
        ["MpFactMatches"] = new() { [LangEn] = "Matches · {0} d", [LangEs] = "Partidas · {0} d" },
        ["MpFactPlayers"] = new() { [LangEn] = "Players · {0} d", [LangEs] = "Jugadores · {0} d" },
        ["MpFactMostPlayed"] = new() { [LangEn] = "Most played", [LangEs] = "Mapa más jugado" },

        // ---------------------------------------------------------------- 57 rooms space
        // The chat tab (57a): "Chat ● 3" — the dot and the count are separate elements.
        ["MpChatTabTitle"] = new() { [LangEn] = "Chat", [LangEs] = "Chat" },
        ["MpChatFoldTip"] = new() { [LangEn] = "Fold the chat", [LangEs] = "Plegar el chat" },
        ["MpChatUnfoldTip"] = new() { [LangEn] = "Unfold the chat", [LangEs] = "Desplegar el chat" },
        ["MpChatRailUnreadTip"] = new()
        {
            [LangEn] = "Unfold the chat · {0} unread",
            [LangEs] = "Desplegar el chat · {0} sin leer",
        },
        ["MpChatRailPlayersTip"] = new()
        {
            [LangEn] = "{0} players connected · see them",
            [LangEs] = "{0} jugadores conectados · verlos",
        },
        // 56a's notice under an empty list, when the busy hours are known.
        ["MpRoomsEmptyPeak"] = new()
        {
            [LangEn] = "More people usually play around {0}–{1} (your local time).",
            [LangEs] = "Más gente suele jugar entre las {0} y {1} (tu hora local).",
        },

        // ---------------------------------------------------------------- 55n refunds
        ["MpRefundBody"] = new()
        {
            [LangEn] = "You got {0} points back: an opponent you lost to was penalized for cheating.",
            [LangEs] = "Recuperaste {0} puntos: un rival contra el que perdiste fue sancionado por hacer trampas.",
        },
        ["MpRefundBodyOne"] = new()
        {
            [LangEn] = "You got 1 point back: an opponent you lost to was penalized for cheating.",
            [LangEs] = "Recuperaste 1 punto: un rival contra el que perdiste fue sancionado por hacer trampas.",
        },
        ["MpRefundDismiss"] = new() { [LangEn] = "Got it", [LangEs] = "Entendido" },
        ["NotifRefundTitle"] = new() { [LangEn] = "Points refunded", [LangEs] = "Puntos devueltos" },

        // ---------------------------------------------------------------- the rating preview
        // Settings → Developer and --demo-elo=<scene> (Controls/MultiplayerTab.EloPreview.cs). The
        // window stays open so one scene after another can be looked at.
        ["DlgSettingsDemoElo"] = new() { [LangEn] = "Rating preview", [LangEs] = "Vista previa de la clasificación" },
        ["DlgSettingsDemoEloHint"] = new()
        {
            [LangEn] = "Shows the rating screens with made-up data: the ranking, placement, the profile, both rooms, "
                       + "the team countdown, the result cards, the History, the monthly highlights and a refund. "
                       + "Nothing is saved or sent, and it lasts until the launcher restarts.",
            [LangEs] = "Muestra las pantallas de la clasificación con datos inventados: la tabla, el posicionamiento, "
                       + "el perfil, las dos salas, la cuenta atrás de equipos, las tarjetas de resultado, el Historial, "
                       + "los destacados del mes y una devolución. No se guarda ni se envía nada, y dura hasta que "
                       + "reinicies el launcher.",
        },
        ["SettingsDemoElo"] = new() { [LangEn] = "Show it", [LangEs] = "Ver" },
        ["SettingsDemoEloSceneRanking"] = new() { [LangEn] = "Ranking", [LangEs] = "Clasificación" },
        ["SettingsDemoEloScenePlacement"] = new() { [LangEn] = "Ranking: nobody placed yet", [LangEs] = "Clasificación: nadie posicionado" },
        ["SettingsDemoEloSceneProfile"] = new() { [LangEn] = "Profile", [LangEs] = "Perfil" },
        ["SettingsDemoEloSceneRoom1v1"] = new() { [LangEn] = "1v1 room", [LangEs] = "Sala 1v1" },
        ["SettingsDemoEloSceneRoomTeams"] = new() { [LangEn] = "Team room", [LangEs] = "Sala de equipos" },
        ["SettingsDemoEloSceneCountdown"] = new() { [LangEn] = "Team countdown", [LangEs] = "Cuenta atrás de equipos" },
        ["SettingsDemoEloSceneResult"] = new() { [LangEn] = "Result cards", [LangEs] = "Tarjetas de resultado" },
        ["SettingsDemoEloSceneHistory"] = new() { [LangEn] = "History", [LangEs] = "Historial" },
        ["SettingsDemoEloSceneHighlights"] = new() { [LangEn] = "Community block (rooms)", [LangEs] = "Bloque de la comunidad (salas)" },
        ["SettingsDemoEloSceneRankingHighlights"] = new() { [LangEn] = "Ranking: highlights", [LangEs] = "Clasificación: destacados" },
        ["SettingsDemoEloSceneRankingMatches"] = new() { [LangEn] = "Ranking: matches and replays", [LangEs] = "Clasificación: partidas y grabaciones" },
        ["SettingsDemoEloSceneRefund"] = new() { [LangEn] = "Points refund", [LangEs] = "Devolución de puntos" },
        ["MpEloPreviewNotice"] = new()
        {
            [LangEn] = "Rating preview - made-up data, nothing here is saved",
            [LangEs] = "Vista previa de la clasificación - datos inventados, aquí no se guarda nada",
        },
    };
}
