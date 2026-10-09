# UI design handoffs — index

`docs/` carries seventeen design-handoff folders — the five named `design_handoff_*` plus
`design_generar_parche/`, `design_publicar_e_instalar/`, `design_simetria/`,
`design_sala/`, `design_mazo_comunidad/`, `design_mazo_tamano/` and
`design_insignias_rango/`, which arrived later and kept their own names. **Nine of the
eleven are fully implemented** and are kept as *historical reference*, not as work to do.
`design_insignias_rango/` is built on all three screens (45a-45c) and still awaits a visual check against the prototype on Windows.

**`design_sala/` is the exception: it is PARTLY done.** Its players panel (22b) is built;
the rest of 22a — the wording fixes, the two checklist items that become notes, the chat's
opening line and starter phrases, and the space split — is still outstanding. Read its own
README before assuming any part of it describes the shipped window, and read the
*"what the handoff assumes and is not so"* note below first: it was designed from a
screenshot rather than from the code, and three of its premises are wrong.

## Why they are still here

They are the **provenance of the design values in the code**. The token dictionaries cite
them by name and take their authority from them — `Styles/Tokens.xaml` says, of the
settings scale, *"every value is the handoff's own"*, and `Styles/Colors.xaml`,
`Styles/Controls.xaml`, `App.xaml`, `LauncherSettingsDialog.xaml` and
`Controls/MultiplayerTab.xaml` all point back here. Delete these folders and those
comments stop meaning anything: the numbers become arbitrary, and the next person to
change one has nothing to check it against.

They are also the record of a **standing instruction**: match a reference exactly rather
than substituting "close enough" values. `CLAUDE.md` ("Design handoffs: follow the
reference 100%") carries that rule, the arguments it survived, and the places where the
reference was deliberately overruled — read that section before treating anything here as
a current specification.

**They are in Spanish** because they are the record of a design conversation held in
Spanish. That is the one deliberate exception to this repo's English-prose rule; nothing
in them is a live instruction, so translating them would only restate history in another
language.

## What each one covers

| Folder | Screens | Options |
|---|---|---|
| `design_handoff_multiplayer_ui/` | Rooms, create-room, the lobby, the in-game surface | 1a, 1d, 1e, 1f (1b and 1c were discarded) |
| `design_handoff_ranking_historial_perfil/` | Clasificación, Historial, Perfil | 3a, 3b, 3c |
| `design_handoff_ajustes_y_taller/` | Launcher settings, mod settings, the Workshop | 4a-4d, 5a-5d, 6a |
| `design_handoff_dialogos/` | Radmin assistant, create-room, new-tournament, Discord sign-in | 12a-12c, 13a-13e |
| `design_generar_parche/` | The delta-patch generator | 18a, 18b |
| `design_publicar_e_instalar/` | The publish-my-mod wizard, the install-folder dialog | 20a-20c, 19a-19b |
| `design_simetria/` | The mod window's rail — one mod's `mod.json` deciding the width of the page | 21a, 21b |
| `design_sala/` | The room window's players panel, and the rest of that window | 22b (done), 22a (outstanding) |
| `design_mazo_comunidad/` | The community deck: bands by percentage, and the tail that lied | 25a, 25b |
| `design_mazo_tamano/` | The same deck at the game's own card size, and the bar under it | 26a, 26b, 26c |
| `design_insignias_rango/` | Rank badges by AoE3 age on Ranking, the rooms row and the room's players panel | 43a-43h (43g chosen), 45a-45c |
| `design_insignias_pantallas_guia/` | Badges on the players panel and the account block, and the rank guide popup | 45d, 45e, 46a, 46b |
| `design_ranking_card_banner/` | The community strip's Ranking card: an age-coloured banner behind each row | 47a, 45e |
| `design_archivos_antivirus/` | LOCAL FILES (copies, diagnostics, Uninstall… on each copy), the uninstall window, the antivirus exclusion dialog | 49a, 49b, 49d, 48a, 48b |
| `design_handoff_salas_laptop/` | Multiplayer › Rooms on laptop windows: the three bars at lower heights, the room list that scrolls itself, the community panel under it (open or folded) | 38a, 38b, 39a, 39b (built — they replace 36a, which shipped first); 40a, 40b (built — 40 replaces 38a's growing panel); 36b (not built) |
| `design_share_diagnostics/` | The mod window: Share diagnostics as step 2 of "Something not working?", a fixed box in the rail, search keywords and a results list | 50a, 50b, 50c (built) |
| `design_insignia_equipos/` | The TEAMS rank badge (two shields), which badge shows where, and the Profile selector | 51a, 51b, 51c (built) |
| `design_botones_secundarios/` | The secondary buttons of the mod window and Launcher settings: a fill of their own instead of a bare rim; then the two large LOCAL FILES card-buttons | 52a (built — chosen over the README's 52b), 52d (built); 52b, 52c (not built) |
| `design_guia_rangos_equipos/` | The rank guide: a 1v1 / Teams selector and a Teams tab built from the team ladder | 53a, 53b (built) |
| `design_grabaciones/` | Downloading competitive recordings: the button in Ranking's «Latest matches» rows, its states and tooltip, the notices, and the Matches view (a year of matches, search, filters, month groups, "Load 30 more") | 63a-63f (built) |
| `design_elo/` | Rating v3: the ranking with placement, the profile per mode, odds and teams in the room, the team countdown, the result card, History, the month's highlights, refunds | 55a-55l, 55n (built); 55m is the server's |
| `design_salas_y_ranking/` | Rooms: the height shared by priority, the chat at 280/320 px and foldable to a rail, the room list in a card, the highlights as a strip; Ranking: the page shared 60/40, proportional columns, sizes that grow with the width, placement segments; 18 × 12 flags | 57a, 57b, 57c, 59a-59c, 58b (built); 58a replaced by 59; 56a's empty notice and strip (built, through 57b); 57a's highlights strip and ACTIVITY bar replaced by 60 |
| `design_comunidad_bloque/` | Rooms: the month's highlights and the community's figures as ONE data strip inside the community block, open (60a) and folded to one line (60b); the separate highlights strip, the figures beside the title and the ACTIVITY bar are gone | 60a, 60b (built; the two-line strip under the title replaced by 61) |
| `design_comunidad_compacta/` | Rooms: the community block compact and ANCHORED AT THE BOTTOM — the facts on the title line, one line each, dropped from the end when short; shorter cards (the matches two lines each, the maintainer's correction); the rooms take everything above, and an empty list centres its notice | 61a, 61b (built) |

## Where `design_elo/` was deliberately not followed

- **The win probability is the SERVER's (Glicko), never §5's formula.** The maintainer's call:
  the launcher only formats `room_odds`, and a room without odds shows no card.
- **Seasons were removed rather than kept beside the new ladder** (the maintainer's call). The
  server sends no season, and the launcher's selector, medals and season bell are gone.
- **Players in placement travel in separate arrays** (`leaderboard_placement`,
  `leaderboard_team_placement`): an older launcher's non-nullable `Rank` would break on a row
  without one. They are still drawn at the bottom of the same table.
- **The anti-farm discount is not announced in the room** (the README's own rule, chosen over
  the alternative) — only on the result card and in History.
- **The profile header carries no ELO**: the mode cards below carry it, and the header states
  the place per mode in words. The inactive card keeps its badge.
- **In the 1v1 room the rival's line keeps its ping and age** beside the odds card.
- **Start is gated in every competitive team room**, and the reason sits in the players card;
  team rows also say when a player is ready. Known risk: one older launcher in the room (which
  cannot pick a team) locks the host's Start, although the server would allow it.
- **The team room's left column is 580 px**, not the 1v1 352: the two teams side by side do not
  fit in less.
- **The team countdown (55i) is drawn inside the room's left column**, not as a window over the
  chat — the chat stays reachable through the countdown, as everywhere else in the room.
- **The result card (55j) keeps the 1f cells and buttons under it**, and the old subtitle's facts
  moved to a "Details" line.
- **History (55k) keeps the mode word ("COMPETITIVA") leading the second line**, which the
  maintainer asked for earlier, and an unrated card keeps that line above its reason instead of
  replacing it, so the hour and the mode survive. The day headers read HOY / AYER as drawn.
- **The highlights need 10 rated matches in the month before the Rooms page shows any** — a
  launcher threshold; the server has none. 55l's "the month has just started" empty state is gone
  with the card it belonged to. Since design 60 the Rooms page shows three of them (the
  biggest climb, most matches, best streak) as facts of the community block's data strip, and
  **Ranking › Highlights** shows all seven in depth — the top five of each, with a figure and a
  detail line (`design_comunidad_bloque/`). A highlight with nobody to name is not drawn on
  either page.
- **The Discord post (55m) is text only**, without the 1200 × 675 image.
- **The refund notification (55n) is a row of the existing bell**, not the 360-px card: the bell
  shows its own time and has no column for "+34 · 1578 → 1612". The profile banner is as drawn;
  its "Got it" uses the settings' secondary button, whose colours are the ones 55n names.
- **`docs/ELO.md` is English first**, with the Spanish pointer line leading, per the rule in
  `docs/BUILDING.md` that every bilingual page follows.
- **The ranking table fills the page instead of the 820-px column** (the maintainer's call: the
  column left more than half a maximised window empty). Since design 59 the page is shared 60/40
  with the match list and every column grows in proportion (`design_salas_y_ranking/`).
- **Every name in the ranking carries the player's picture**, which 55a does not draw (the
  maintainer asked for it).

## Where `design_salas_y_ranking/` was deliberately not followed

- **There is no letter-spacing** on the uppercase labels: WPF's `TextBlock` has none.
- **The folded rail's chat button is the Segoe MDL2 message glyph**, not the 💬 emoji: an
  emoji is drawn by a font the launcher does not control, and the house rule is no emoji in
  controls.
- **The room table keeps its columns** (room · host 208 · players 88 · ping 66 · action 96).
  57a draws 210 / 70 / 64 / 104 without saying so in its text, and the current widths are what
  the sort headers and the Spanish captions were measured against.
- **The room rows keep their own padding (12 px)**, so the column headings sit 23 px into the
  card rather than 57a's 16: the headings have to line up with the cells under them.
- **57a's highlights strip and its ACTIVITY bar are gone**, replaced by design 60's one
  community block (see `design_comunidad_bloque/` below). For the record, while they lived: the
  strip sat under the community panel rather than above it (the maintainer's call), and grew to
  seven cells with a "+N" window — both retired with it.
- **The cards over the list (57a) are an exception the player asks for.** With no choice made,
  cards that do not fit fold the block to its one line; only "Show activity" pressed puts them
  over the bottom of the list, with the folded block still under it saying "Hide activity".
- **At 1440 × 810 the block may still open.** 57b's numbers keep four rows and then open the
  block when it fits; the launcher's own height (window chrome, the Radmin banner) is what
  decides it on a real laptop, so the rule is followed, not the drawing.
- **The ranking's stacked layout (under ~1000 px) is unreachable today**: `UiScale` never lays
  the tab out narrower than ~1100 logical px. It is implemented anyway.
- **The footnote's link and its text keep the raised text ramp** (`MpTextFaint`, `MpTextLabel`),
  not 59's `#6d829d` / `#61779a`, which are the old values that failed 4.5:1.

## Where `design_comunidad_bloque/` was deliberately not followed

- **There is no letter-spacing** on the facts' labels: WPF's `TextBlock` has none.
- **The labels are `MpTextLabel` (`#8394B1`)**, the raised ramp, not 60's `#61779a`, which is the
  old value that failed 4.5:1 — the same call as on every other multiplayer surface.
- **"Biggest climb · Oct" leads the strip when the month has one.** 60's sample month simply had
  none; the maintainer asked for it, and it is left out when nobody climbed.
- **The four highlights added after 57 (most wins, best win rate, biggest upset, civilization of
  the month) are NOT on the Rooms page** — the maintainer chose "the mockup's facts". They live
  in **Ranking › Highlights**, a third option beside 1v1 and Teams that takes the whole page:
  one card per highlight with its top five, the rule under its title, a month capsule, and a
  "1v1 · Teams" switch on the climb and streak cards. **No design covers that view**; it is
  built from the Ranking page's own tokens (cards `MpPanel` + `MpRimFaint`, radius 10,
  `MpTextLabel` titles, the ladder's own-row tint and "YOU"). Its data comes from its own route,
  `GET /stats/highlights`, fetched only when the view opens.
- **60's two-line strip (label over value, wrapping) is gone**: design 61 moved the facts onto
  the title line, one line each — see below.

## Where `design_comunidad_compacta/` was deliberately not followed

- **There is no letter-spacing** on the facts' labels, and they are `MpTextLabel` (`#8394B1`)
  rather than 61's `#6d829d` — the same two calls as for 60.
- **The matches are TWO lines, not 61's one** — the maintainer's correction: each row is the
  Ranking's «Latest matches» row (the same builder), with line 2 — "COMPETITIVE 1v1 · map ·
  length", the label bold — 24 px in under the first name, and a padding of 6 on a laptop up to 9
  on a big screen. So a laptop card shows three matches and a big one six, not 61b's eight.
- **The kind-of-room label is bold `#e6c06a` (competitive) / `#a8bcd2` (casual) in the Ranking
  list and the profile History too**, which now follow design 59's gold where they had drawn
  `#f5e4b0` SemiBold — one match reads the same everywhere.
- **The spacing is one margin and one gap, 12 below 1600 px and 16 from there** (the
  maintainer's rule, `PageSpacing`), not 61's 10-px gap: around the panels, between them and
  between the community cards, with the sub-bar on the same margin and 16 px of padding inside
  every panel (Rooms, Community activity, Chat). It applies to every Multiplayer subtab.
- **The block's height ends at the fifth player** (the maintainer's call): the cards are exactly as
  tall as the Ranking card with its five rows, on any screen, where 61's two frames show about a
  third of the column (36 % and 34 %) — on a tall screen a third left a band of nothing under the
  fifth row. The peak bars fill the card from a fixed 44-px minimum, the matches card shows the
  whole rows that fit (about three), and the block folds when even that height does not fit beside
  the rooms' four rows.
- **The ranking card's rows carry the player's Discord avatar again**, between the badge and the
  name, as the full Clasificación table does — the maintainer's call; 61 draws rank, name and
  rating only. It follows the page (20-28 px) and always stays at least 6 px under the row, so
  the row height 61 draws, and the card's five-row height, are unchanged.
- **"Biggest climb" leads the facts when the month has one**, as in 60; so with a very long name
  the laptop line drops more facts than 61a's three.
- **The folded block (its header line alone, with "Show ▴") is not drawn by 61**; it keeps 60's
  meaning — the player's remembered choice — and the cards still open over the list when shown
  and too tall to fit, as 57a asks.
- **The ranking rows keep their age banner's 2-px edge**, where 61 draws 3.
- **The empty notice's button is the solid `MpPrimaryButton`** at 34 px, and its title and text
  follow 61's sizes (15-19, 12.5-15).

## Where `design_guia_rangos_equipos/` was deliberately not followed

- **The account block reaches the guide through a ROW in its menu** ("How ranks work", between
  Profile and Sign out), not by clicking the badge in the chip. The block's click IS that menu;
  an 18-px badge inside it would be a second target too small to aim at, splitting one control
  in two. The row still opens the guide on the tab of the badge the chip wears (rule 1).
- **The selector's tray is a new brush, `MpGuideSegmentTray` (#0B1526)**, and the segment a new
  APP-WIDE style, `MpGuideSegment`. The Ranking's segmented control lives in
  `MultiplayerTab.xaml`'s resources, which the guide cannot reach when it opens over the room
  window; the active fill and the idle text reuse `MpSegmentActiveBg` and `MpNavTabIdle`.
- **The guide keeps its own type sizes**; only the selector uses the prototype's 12 px
  (`MpMetaSize`). The 1v1 tab stays as it was, as the prompt asks.
- **The team badges are drawn a little narrower at the front** (34 in the header, 25 in a row,
  19 in the notice, against 36 / 26 / 20 for 1v1): the double shield is 7/24 wider than its
  front, and this keeps it inside the columns the 1v1 tab already uses.
- **A team Discovery is inferred from a COMPLETE team table.** The deployed server sends no
  `ladder_rank_team` in `/matches/elo` yet, so by the place alone the Teams tab would never
  appear. When the public team table holds the whole ladder (empty, or as many rows as the
  server counts) and the viewer is not in it, the viewer is Discovery; a partial page, or a
  server that sends no team table at all, still means "unknown" and no selector (rule 3).
- **The notice says no number when the server gives none** ("Win or lose a team match…"). The
  prompt asks for the server's number; inventing one when it is missing would be the failure
  rule 3 exists to prevent.
- **The "You are…" line wraps when the selector is beside it.** The Discovery sentence is long,
  and trimmed it lost exactly the words that say what to do.
- **"Open ranking" on the 1v1 tab now pins the Clasificación to 1v1** when the guide has a
  selector, as "Open team ranking" pins it to TEAMS, so the button never lands on the other ladder.

## Where `design_botones_secundarios/` was deliberately not followed

- **The variant is 52a ("suave"), not the 52b the README calls chosen.** 52b was built first
  and shown on screen; the maintainer preferred the softer 52a for every button. The values
  are 52a's MARKUP, which differs from its prose in two places — the prose says "el borde de
  siempre", the markup draws the neutral rim at .22 (it was .20) and the blue one at .55 (it was
  .50) — and the markup wins, as everywhere. It also carries what the README's table omits for
  52a: the footer fill (#1F334F / #263D5E), the hovers of the blue and red buttons, and the
  captions (#D3DFEE, #9EC6F6, #EDB0B0).
- **No top sheen** — 52a draws none. The attached property built for 52b's sheen was removed with
  it rather than left behind unused.
- **The brushes are `UiButton*`, not the suggested `SetButtonFill*`** — new shared brushes take
  the `Ui*` prefix (`CLAUDE.md`). The red rim reuses `UiRimDangerStrong`, which is already .50.
- **Pressed is computed** (the hover darkened 4 %): the handoff names the rule and no value.
- **Disabled is the fill at `Opacity` 0.5**, as the README asks — against the launcher-wide
  "disabled is a colour" rule, by the maintainer's call. The solid styles derived from the same
  base reset it to 1.
- **The footer variant is a separate style, `SetFooterBarGhostButton`**, used only where the
  button stands on the #16263E footer bar (Launcher settings' Close). `SetFooterGhostButton` is
  also used inside cards (Search for my AoE3, the patch generator's How it works), where the
  footer's lighter fill would be wrong.
- **"Uninstall from my PC" got a style of its own, `SetActionButtonDanger`.** It was a neutral
  button painted red with local values, which beat every trigger of the style.
- **52d (Install another copy / Add a folder you already have) changed `SetActionCard` and
  `SetIconTile` themselves**, which only those two cards use. Their old brushes (`UiAddCardRim`,
  `UiIconTileBg`) are shared with other elements and were left alone.
- **52d's description colour is a new brush, `UiButtonCardDesc`,** although the prompt says to
  reuse one when it exists: #9FB3CD exists as `UiToggleThumbOff` and `MpBadgePreviewText`, which
  mean other things, and tying this text to a toggle thumb would chain two unrelated surfaces.
  The title colour (#F0F5FB) IS reused, as `MpTextHeading`.
- **The 52d icon square stays 26 px** with the launcher's radius; the prototype draws 28, and the
  prompt keeps the size unchanged.

## Where `design_share_diagnostics/` was deliberately not followed

- **The rail box carries no search keywords**, though the README lists them for it. It sits
  in no section panel, so the search never filters it, and as a results-list entry it would
  only duplicate the card's button.
- **One string key per element holds BOTH languages' keywords** (`SearchKwShareDiagnostics`,
  `SearchKwViewLogs`) instead of the suggested `_es`/`_en` pairs — the string table's normal
  shape. `SectionSearch.KeywordsFor` always loads both, as asked. The element's own caption
  key goes in too: without it "Share diagnostics" itself still found nothing.
- **Two solid buttons in one card.** The rest of the launcher keeps one; the handoff asks for
  a solid blue Verify and a solid teal Share diagnostics, and that is what was built. Teal is
  used nowhere else.
- **No letter-spacing** on "PROBLEMS?" and the card header — WPF has none (the known
  deviation recorded in `CLAUDE.md`).
- **The solid teal has hover and pressed shades the handoff does not give** (`UiDiagHover`,
  `UiDiagPressed`, derived from #3CC6C9): a solid button with no hover reads as dead.

## Where `design_insignia_equipos/` was deliberately not followed

- **There are no 4v4 rooms.** A competitive room is 1v1, 2v2 or 3v3 (`RoomFormats`), so the
  "4v4" rows of 51b do not exist; 2v2 and 3v3 share one team ladder.
- **A casual room and a room of unknown format use the player's CHOICE**, not a guess from the
  seat count. A casual room's size says nothing about how it will be played (`RoomFormats.Resolve`
  answers `Casual` on purpose), and the handoff's own rule is "if the format is unknown, use the
  player's choice".
- **Sizes stay the launcher's existing widths:** 19 in the Players list (the handoff draws 20)
  and 34 in the profile header (it draws 48). `RankBadge.BuildTeam` supports 48; nothing asks for it.
- **The back shield is CLIPPED, not covered.** The prototype paints a background-coloured
  silhouette behind the front shield to separate the two; the launcher cuts the front's outline
  out of the back shield instead. On a solid panel the two look the same, and the cut stays right
  over the Ranking's gradient banners and the translucent rows, where a painted silhouette would
  show as a dark patch.
- **The tooltip is split into clauses**, not the single `MpBadgeTipBody` template: the other badge
  can be unplaced (Discovery) or unknown (an older server), and a fixed template would print "#0"
  or a ladder nobody reported.
- **A room member's badge is read when they JOIN**, like their rating. Changing the choice shows
  in an open casual room only after rejoining — except on your own row, which follows at once.
- **Two colours the handoff does not give:** the light age colours for the four ages it does not
  show in a ladder box (`RankLabel*` in `Badges.xaml`, from the rank guide's), and the selector's
  hover state.

## Where `design_handoff_salas_laptop/` was deliberately not followed

**Turns 38-39 replace turn 36**, which was built and committed first. 36 merged the tabs into the
title bar (WORKSHOP ended up under the Update pill), folded the community panel away on every
laptop window, and drew it as an overlay over the room list when unfolded. The principle 38-39
states and the code now follows: *a small window shows the same blocks as a big one, in the same
order; only how much fits in each changes.* `PROMPT-38-39.md` and `salas-laptop-38-39.html` are
the reference; `README.md` and `salas-laptop-36.html` are kept as the record of 36.

**Turn 40 (`PROMPT-40.md`, `salas-laptop-40.html`: 40a with no rooms, 40b with eight) refines
38-39 and wins where they disagree.** The open panel is 248 px whatever the room count — 38a's
panel that grew into whatever a short list left over is gone, and with it twelve matches and
fifteen ranks — the cards show at most four matches and the top five, line 2 of a match leads
with "COMPETITIVE 2v2" (mode and format as one label), and the PEAK HOURS sentences wrap to two
lines instead of trimming.

1. **The new list/panel split applies at EVERY window size, not only on laptops.** The
   maintainer's call. The list measures its rows or scrolls in its own viewport
   (`RoomsListScroll`), the open panel takes 248 px (turn 40 — it used to fill what a short
   list left) and the spare height stays in the list, and Show/Hide is always there. The rule is `Services/Multiplayer/RoomsActivityLayout.Decide`, pure and tested.
   Only geometry stays compact-only: the 34/42/44 bars, the 54-px rows, the 12-px margins.
2. **The RANKING card keeps its rank badges and age banners** (designs 45/47a) at the 30-px row
   height 38-39 draws — also the maintainer's call. The badge and the avatar are 22 px.
3. **34 and 42 are not whole device pixels at 125 %** (42.5 and 52.5). The heights are the
   handoff's; the main nav's wide 54 has the same property and nobody has seen a seam from it.
4. **The peak bars are a fixed 34 px.** They were `max(34, 0.11 × card height)` while 38a's
   panel could grow; at a fixed 248 px that is always 34, so the formula went.
5. **The cards show at most 4 matches and the top 5** — the handoff's own caps (turn 40),
   applied before the rows reach `Controls/FitStackPanel`, which still drops any that do not fit
   WHOLE at a larger text size. The viewer's own rank is never appended below the five.
6. **"Hide activity ▾" shows in every expanded state**, so the choice can always be undone
   where it was made.
7. **The match row's format is derived from the sides** — there is no stored format —
   `MatchParticipantsView.FormatOf`: two sides give "NvM", more sides of one player each give
   "FFA", two players with no teams give "1v1". **More than two players with no team data give
   NO format**, not "FFA": every team game stored before teams were recorded looks exactly like
   that. The label is then the mode word alone ("COMPETITIVE"), and the format alone when the
   room's mode is unknown. Undecided, "no result" follows the label. **No mod name on line 2**,
   as drawn: the panel is already scoped to the room's mods. The folded strip carries no mode
   label, so turn 40's "same rule there" has nothing to apply to.
8. **The match row's age keeps its "ago"**, so the row's age and its duration are not two bare
   "N min" side by side.
9. **The civilization's name is only in the flag's tooltip**, as the handoff says — printed
    inline it is what made a 2v2 wrap to several lines.
10. **The functional changes from 36 that 38-39 keep are unchanged**: the room code pasted into
    the search, Refresh as a 32×32 icon, the "Connected ▾" dropdown holding the IP and "Help
    connecting" (with its two accepted consequences — no manual door while signed out, and none
    when `RadminAssistantMode` is "Never"), one occupancy bar per
    seat, the 60/120 ping colours, the Join / In game look, and Re-enter keeping a solid fill.
    **The CASUAL chip still carries no format**, although 38-39 draw "CASUAL · 3v3": a casual
    room's size says nothing about how it will be played (`RoomFormats`), so the format would
    claim what the competitive flag exists to prevent.
11. The mini histogram in the folded strip has 24 bars of 2 px, as 39a draws it (36a had 14).
12. **No map in the room's sub-line** — `LobbySummary` carries none.
13. Deviations of platform and of earlier decisions, as elsewhere: no letter-spacing on uppercase
    labels; the active tab is not bolded (re-weighting reflows the label on every switch); the
    title bar keeps the existing chrome brushes; the faint text colours map onto the raised AA
    ramp (`MpTextDim`, `MpTextLabel`, `MpTextFaint`); the mockup's 10.5 / 12 / 12.5 / 9.5 sizes
    map onto `MpPillSize` / `MpMetaSize` / `MpBodySize` / `MpSectionLabelSize`.

## Where `design_archivos_antivirus/` was deliberately not followed

1. **The cards are always side by side, split 1 : 1 rather than 1.35 : 1, and the mod window
   now opens at 1040 wide instead of 900.** The maintainer asked for the cards never to stack.
   Measured in Spanish, an even split at 1040 is what lets "Abrir carpeta / Reparar /
   Desinstalar…" and "Ver registros | Compartir diagnóstico" each share one line. Narrowed by
   hand, the content wraps INSIDE its card; the troubleshooting card never gets narrower than
   the Discord pill (`SupportLink`'s caption cannot wrap or trim), and row titles wrap rather
   than trim.
2. **The troubleshooting grid is Verify (full row) / View logs | Share diagnostics / Discord
   (full row)**, not 2×2: the Discord pill's caption is `SupportLink`'s own, which the handoff
   says not to change, and it is wider than half the card. (A single row with Discord as a
   header link was tried and rejected by the maintainer.)
3. **Uninstall… does not close the Properties window** (it used to, to uncover the progress
   strip); the uninstall window opens over it and the page refreshes when it finishes.
4. **"Change mod folder" stays, as a third row in FOLDERS.** The reference drops it; it is the
   only way to repoint the ACTIVE copy at a folder the player moved, which neither "Make
   active" nor "Add a folder" does.
5. **The saved-games option does not say "leave this off to keep your saves".**
   `UninstallService` only ever removes files the launcher seeded AND that are still
   byte-identical — saved games are never touched — so the reference's wording would have
   been false. It says what it actually does.
6. **Reset launcher settings is removed, not moved to its own card.** Measured: it deleted the
   whole `launcher-config.json`, then the uninstall flow saved the in-memory config straight
   back, so it reset nothing while claiming to. The maintainer chose removal.
7. **The row-label column of the antivirus card is as wide as its longer label, minimum 92.**
   "Carpeta del mod" does not fit the reference's 92 px.
8. Deviations of platform, as elsewhere: no letter-spacing on the uppercase labels; the serif
   is the existing `DisplayFont`.

## Where `design_insignias_pantallas_guia/` was deliberately not followed

1. **The account block does not open the guide.** The handoff makes that click open it; it
   already opens the account menu (Perfil / Cerrar sesión), which the maintainer asked to keep.
   The block does show the badge and the age ("Colonial · 1383 ELO"). The guide's entries are the
   "? How ranks work" button (MpSecondaryButton, the handoff's allowed alternative to a link) on the Ranking subtab and a click on any badge.
2. **The guide's ranges are the share-of-the-ladder cut**, not the prototype's fixed 2-3 / 4-6 /
   7-10 — the same `RankAges` rule every badge wears, so the guide cannot contradict one.
3. **The footer says "N players on the ladder", not "the last 30 days".** The ELO ladder has no
   time window; only the community totals do.
4. **Community matches stay two lines per match.** The handoff asks for one; the two-line row is
   deliberate (see `.claude/rules/multiplayer.md`, and `AnUndecidedMatchIsNoTallerThanADecidedOne`).
5. **The players panel's rows are 20 px, not the 34 the prototype drew from a screenshot**; the
   19-px badge borrows the row's margin so it does not grow.
6. **The account block's badge needs `ladder_size` from `/matches/elo`** as well as the position —
   the ages are cut by share, and the community payload that also carries the size can land after
   the chip is painted, with no new `PushAccountChip` allowed to repaint it.

## Where `design_ranking_card_banner/` was deliberately not followed

1. **"Two Sovereigns" is not a bug and was not "fixed".** The handoff reads the card's 1.º and 2.º
   both wearing Sovereign as an off-by-one. It is the share-of-the-table split the maintainer
   chose (`RankAges.For(position, ladderSize)`, top 10 % rounded up = 2 at ~15-18 players), and
   the card and the full table go through that one method. Pinned by
   `RankAgeTests.PlacesOneToSevenOnAnEighteenPlayerLadder`.
2. **Rows are 34 px, not 44** — the maintainer's call: the strip's height is paid for out of the
   rooms list under it. Badges 22/24 px (not 25/28) and avatar 20 (not 24) to fit; the 30-px slot,
   the banner, the 2-px edge, the 1-px top line, the 7-px radius and the Sovereign's 9-s light are
   as specified, in the badge's own `RankGlow*` colours.
3. **Every Sovereign row carries the light**, not only 1.º, since there can be two.
4. **The banner is on the full Clasificación table too** (asked for after the card shipped), capped
   at 640 px so it fades out before the rating bar, with its top line fading with the fill. It
   replaced first place's white bar and red wash there as well.

## Where `design_insignias_rango/` was deliberately not followed

1. **The age thresholds are a SHARE of the table, not fixed positions.** Its README proposes
   1 / 2-3 / 4-6 / 7-10 / rest. Fixed positions left exactly one red badge however many played,
   and the maintainer asked for a wider spread, so the cuts are cumulative shares of the ladder
   (`Services/Multiplayer/RankAges.cs`): Sovereign 10 %, Imperial the next 15 %, Industrial the
   next 20 %, Fortress the next 25 %, Colonial the rest — each rounded up and at least one place
   wide. With 18 players: 1-2 / 3-5 / 6-9 / 10-13 / 14-18. The size is the server's
   `ranked_players`; unknown falls back to 1 / 2 / 3-4 / 5-6 / rest. At the same time the ladder
   entry bar went back to ONE rated match (`MIN_DECIDED` on the backend), so everybody who plays
   is on the table. Discovery is still "not on the ladder" (no rated match).
2. **The HOST column is 208 px, not 152.** The handoff was measured on an older build. The
   badge still takes the avatar's place and the column width is untouched.
3. **Rooms and the room panel get the position from the server** (`ladder_rank` on the host
   and on every member), rather than from the ranking the launcher has loaded, which only
   carries the first 50.

## What the two deck handoffs say that the code no longer matches

Both folders are kept verbatim, so they go on naming things that are gone.

1. **`design_mazo_tamano/`'s table names `BuildDeckCivGroup`, `BuildDeckCardRow` and
   `BuildDeckCountCell`.** All three were deleted by the pass BEFORE it — its own README
   says it did not re-read the code. The rule they carried, that nothing is drawn below the
   sample minimum, is alive and lives in the tail row.
2. **Neither prototype draws the balloon's frame, and 26's does not draw the civilization
   strip.** The strip is 25a's (`design_mazo_comunidad/`), built two passes later than the
   rest of it, and its pill geometry — 32 tall, 12 of side padding, radius 8, a 7 gap, the
   flag at 8 from the label — is read off that prototype's markup and nowhere else.
3. **26a asks for the card's own name under it to go and says the name moves to the
   balloon.** That is done; the balloon's own chrome (the gold frame, the age line, the
   monospace footer) is the one part of 26 still outstanding.

## What `design_sala/` assumes, and is not so

It was written from a screenshot of a running build, which its own README says. Three of its
premises did not survive contact with `LobbyWindow`, and they are recorded here because the
folder is kept verbatim and will go on stating them:

1. **There was no fixed height.** It suspects "a `ScrollViewer` with a fixed `Height`" and
   says removing it may be the whole fix. There is no `Height` or `MaxHeight` anywhere on
   that panel or above it. The roster was one **star** row above three `Auto` rows, so the
   three cards below were paid in full first and the roster got the remainder — nothing on
   a short window. The fix was to make the cards scroll together and keep the actions out
   of the scroller.
2. **The free seat was already drawn.** `RenderRoomMembers` has added one row per unfilled
   seat for a long time. It was below the fold, which is why the panel showed a scrollbar.
   What was missing was the code and the Copy button *inside* the row.
3. **`HOST` and the status were already in separate columns.** They collided because the
   name sat in a star column that took the whole width and shoved the pill against the
   right edge, not because they shared a flow.

## Where `design_grabaciones/` was deliberately not followed

1. **No server route that serves the file.** The handoff asks for `GET /matches/{id}/replay`
   returning the recording with a Content-Length. That would route every byte through the lobby
   server, which the storage design forbids. The launcher keeps `GET /matches/:id/replay-url` (a
   10-minute signed GET on the bucket, 404 `no_replay` once the file is gone); the bucket sends
   the Content-Length, so the progress ring works the same.
2. **No mod version.** The server records none (`mod_combined_hash` is a fingerprint, not a
   version), so the tooltip and the notice name the mod alone — which the handoff allows — and
   the "another version installed" notice is not built. A mod the launcher does not know is saved
   to Documents and says so, since nothing names its Savegame folder.
3. **The counter says "{n} matches", not "in the last 12 months".** The prototype's own 63e lists
   matches older than a year under that same counter, so the list is not capped and the phrase
   would be false. The month headings count the matches LOADED in that month, not the month's
   total, which no endpoint sends.

Each folder holds a `README.md` (the design contract), zero or more `SPEC-*.md` (per-screen
detail), an HTML prototype, and a `PROMPT.md` (the text used to kick the work off).

## If you read them

- **Read the prototype markup, not only the prose.** The README omits values the markup
  carries, and where a handoff contradicts *itself* the markup is the version somebody
  actually looked at. Both of the third handoff's self-contradictions were resolved that
  way.
- **The prototypes are static references.** They were never wired to anything; the
  multiplayer one is a design-canvas export whose runtime (`support.js`) is not part of
  this repo, so it renders as a plain page.
- **Where the reference was overruled, the reasons are recorded elsewhere** — in
  `CLAUDE.md` for the settings, Workshop and dialog handoffs, and in
  `.claude/rules/multiplayer.md` for the ranking one. A difference between a prototype and
  the shipped launcher is more likely to be a decision than a defect.
