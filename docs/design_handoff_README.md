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
| `design_grabaciones/` | Downloading competitive recordings: the button in Ranking's «Latest matches» rows, its states and tooltip, the notices, and the Matches view (a year of matches, search, filters, month groups, paging); then 64, how a row of that view shows where it ends | 63a-63f, 64b (built; 64a not) |
| `design_elo/` | Rating v3: the ranking with placement, the profile per mode, odds and teams in the room, the team countdown, the result card, History, the month's highlights, refunds | 55a-55l, 55n (built); 55m is the server's |
| `design_salas_y_ranking/` | Rooms: the height shared by priority, the chat at 280/320 px and foldable to a rail, the room list in a card, the highlights as a strip; Ranking: the page shared 60/40, proportional columns, sizes that grow with the width, placement segments; 18 × 12 flags | 57a, 57b, 57c, 59a-59c, 58b (built); 58a replaced by 59; 56a's empty notice and strip (built, through 57b); 57a's highlights strip and ACTIVITY bar replaced by 60 |
| `design_comunidad_bloque/` | Rooms: the month's highlights and the community's figures as ONE data strip inside the community block, open (60a) and folded to one line (60b); the separate highlights strip, the figures beside the title and the ACTIVITY bar are gone | 60a, 60b (built; the two-line strip under the title replaced by 61) |
| `design_salas_filas/` | Rooms: each row of the list as a raised card tinted with its group's colour, the column headings as a bar, and drawn cells (host photo, a dot per seat, signal bars) | 104a, 104b (built; 104c and 104d are discarded alternatives). Kept over it: the host's rank badge. Changed: rows are 52 high with a 36-px icon and a 14-px name, the headings' bar 28, not 72/48/15/32 (the maintainer found them too big). Not built: the coloured shadow under each row and the glow under "Ver sala" (an Effect on a row costs its text ClearType); letter-spacing (WPF has none) |
| `design_comunidad_compacta/` | Rooms: the community block compact and ANCHORED AT THE BOTTOM — the facts on the title line, one line each, dropped from the end when short; shorter cards (the matches two lines each, the maintainer's correction); the rooms take everything above, and an empty list centres its notice | 61a, 61b (built) |
| *(none — Claude Design only)* | Rooms: the row's action button in the Discord announcement's colours — green Join, blue In game, amber Full, each with a dot | 66 (built, colours only — see below) |

## Where design 66 was deliberately not followed

Design 66 ("Boton En partida") lives in a Claude Design project and was never exported to
`docs/`, which is why it has no folder in the table above.

- **Its SIZE was not taken, by the maintainer's choice — colours only.** The prototype draws a
  taller button with larger type; the launcher keeps the 96-px action column, the 30-px
  `MpControlHeight`, 12-px SemiBold and radius 7, and takes the fills, the rims, the text
  colours, the dot and Join's hover. The 8-px dot gap is the design's; it leaves "En partida"
  about 2 px of slack in that column (`RoomActionButtonTests` pins it).
- **Only Join, In game and Full changed.** Re-enter stays the solid blue, Your room and a Join
  for a mod that is not installed stay the inert grey with no dot, and Watch is unchanged — the
  maintainer kept every other state as it was.
- **Discord is not changed by it.** The webhook marks a full room 🟡; the design calls it amber
  (🟠). The launcher now says amber; matching the channel is a one-line backend change.

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
- **The ranking's columns are `# · PLAYER · ELO · PEAK`, not 55a's `… · ELO · W-L · %`** (the
  maintainer's call, 2026-10-28). PEAK is the highest rating that ladder entry reached after
  placement, read from `rating_peak` on each ladder row (the server's `src/stats/ratingPeaks.ts`,
  the same rule as the peak on the profile); a server that does not send it gets no PEAK column.
  **The record and the win rate moved to the Profile**: the ranked mode card (55d/55f) gains a
  line "Record: 15–11 · 58 % of 26 decided" under its peak and low, which neither card draws. So
  another player's record is no longer shown anywhere.

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
- **The ranking table's last columns are ELO · PEAK, not 59's `ELO · V-D · %`** (2026-10-28):
  the same call as for 55a above. 59's proportions are kept for the columns that remain — PEAK
  takes the record column's `minmax(64, .45fr)`, left-aligned — and the % column is gone.

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
  Ranking's «Latest matches» row (the same builder), with line 2 — "COMPETITIVE 1v1 · mod · map ·
  length", the label bold — 24 px in under the first name, and a padding of 6 on a laptop up to 9
  on a big screen. So a laptop card shows three matches and a big one six, not 61b's eight. (The
  mod has been on line 2 since 2026-10-28; see turn 40's item 7 below.)
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
   room's mode is unknown. Undecided, "no result" follows the label. **The mod IS named on line
   2, against the drawing** (the maintainer's call, 2026-10-28): it was left out on the belief
   that the panel is scoped to the room's mods, and it is not — `/stats/community` and
   `GET /matches` mix mods, so "ESOC Florida · 24 min" did not say which game was played. It comes
   after "no result" and before the map; an id the catalogue does not know is left out, never
   shown raw. The folded strip carries no mode label, so turn 40's "same rule there" has nothing
   to apply to.
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
4. **No "Load 30 more" button: the next page loads by itself as the reader scrolls**, at the
   maintainer's request — at 210 matches the button was six clicks, each one a trip to the end of
   the list. The handoff chose the button so the reader keeps their place; that still holds, since
   pages are appended below what is on screen and the server pages by keyset. The footer says what
   is happening instead ("Loading more matches…", "Showing 60 of 210", the end of the list), a
   failed page offers Retry (the button keeps 63's look) and nothing retries by itself, and a
   "↑" button takes the reader back to the top.
5. **The mod sits before the map on line 2, not after the length** (2026-10-28): every match row
   in the launcher now names its mod in that one place (see turn 40's item 7), and a tile that
   appended it again at the end would print it twice.

Design 64 (same folder) offered two ways to make the Matches view's rows read as separate:
64a, a stronger line and stripes, and 64b, every match as a card of its own. The maintainer
picked **64b** and it is built exactly; 64a is not. 64b's 8 px / 6 px between cards replace
63d's 14 px column gap, and its cards replace the group panel.

Each folder holds a `README.md` (the design contract), zero or more `SPEC-*.md` (per-screen
detail), an HTML prototype, and a `PROMPT.md` (the text used to kick the work off).

## Where `design_base_launcher/` was deliberately not followed

Screen 68 (Biblioteca unificada):

- **The download pill names one operation.** "2 descargas" cannot happen: the launcher runs one
  operation at a time. The dropdown keeps its one row.
- **A copy is named after its folder**, not "Principal" or "Torneo": renaming was removed on purpose,
  so "Renombrar o quitar en Ajustes" reads "Gestionar en Ajustes".
- **The sort button is a real menu** (recent, name, status); the prototype only draws its caption.
- **Uninstall keeps its own window** until screen 79 redoes it, not the veil in 68a.
- **Only the tabs, the list and the search sit on real blur.** The chips and pills are small enough
  that their 8-10 px blur cannot be told from their fill, and each would add to the clip.
- **The chevron of the brand menu and the bell stay in the bar.** Both lead somewhere nothing else
  does; screen 73 covers the bell.
- **68b needs the catalog repository:** the proposed `accentColor`s are data in `mod.json`. The
  normalisation (`ModAccent.Normalize`) is here and runs on whatever the catalog says. The five
  proposed colours (Age of Dynasties teal, AoE1 blue, Hundred Days olive, King's Return violet,
  The Ancient Age green) were written into the catalog on 2026-11-10; Age of Empires III stays gold.
- **The pictures under the buttons are the mod's screenshots, else its cover and banner**, which is
  what the prototype shows. No mod in the catalog declares screenshots yet; one picture alone (Wars
  of Liberty has no banner) is no gallery. Up to eight, and a GIF animates in the viewer.
- **The ⋯ beside the gear stays** (103n): a right click may not be the only way to anything.

Screen 69 (Ajustes › Interfaz):

- **The text size stays a dropdown with Automatic, 105 and 115 %.** The prototype draws 100, 110 and
  125 % as a segmented control and defaults to 100, but Automatic is the launcher's default on
  purpose (CLAUDE.md, "Design handoffs") and it is what keeps a laptop from the smallest type.

Screen 74 (Dónde se instala):

- **The copy box starts unticked.** The prototype draws it ticked, but that is a mock state, and a
  row that arrives already agreeing to write into the player's profile is not a question. The
  preview ticks it so the comparison matches.
- **The hint keeps saying when the settings arrive** ("from the first launch" or "the second"). The
  prototype's "the first time you open the mod, not when installing" is no longer how it works.
- **No space line while the mod has no folder.** With no AoE3 the requirement is not known; the
  prototype prints a fixed "11 GB".
- **The glass over the image is denser, with no live blur.** `ModDialogChrome` raises `GlassPanel`
  to .8 when the window has an image, because BASE forbids a live blur behind a panel.

Screen 77 (Actualización del launcher):

- **"Ready" lasts one launcher session.** `CleanupOldVersion` sweeps the staged file at every start,
  because nothing proves a file left beside the exe is the one that was checked. After a restart the
  pill says "Update" again and the download runs again.
- **The pill keeps pulsing:** the handoff does not ask for that to stop.
- **The pill sits in the title bar, beside the bell**, not in the tab row beside "Conectado" as 77k
  draws it: the maintainer's call (2026-11-10), where it always was before 68a. It keeps 77k's blue
  look, 26 px tall for the 40-px bar.

Screen 78 (Asistente de Radmin):

- **The window closes itself on reaching the network only when the launcher opened it,** as it did.
- **"Hide while the game runs" was only a suggestion and is not done.**

Screen 73 (Publicar mi mod):

- **Many labels and hints keep the existing text.** The README says the prototype deduced them from
  their keys, and some are wrong: "Marcador" is a file or folder the mod ships, not "a text in your
  version of the file", and "Idiomas cubiertos" is a list of covered files.
- **The hint under the type and the mechanism does not change with the choice.** The existing ones
  carry the "does it have UHC" test, which the prototype's lose.
- **The list of what is missing stays the review's** (probe file, executable, description, icon),
  plus the release check. It is deliberately not the prototype's id, name and repository.
- **No minimise button:** it is modal, and a modal must not minimise away from its blocked owner.
- **The release check accepts any `.zip`,** not only the prototype's `<id>-<tag>.zip`, as the
  launcher does.

Screen 79 (Menú de cuenta y Conectado):

- **"Falta la red" comes from the assistant's probe, every 30 s while Radmin runs.** Where Radmin's
  log cannot be read, the probe falls back to pinging a peer, so a false alarm is possible.

Screen 71 (Ajustes del mod):

- **No "Renombrar…" in a copy's menu and no custom copy names.** Renaming was removed because a
  label that disagrees with the folder misleads, so a copy is named after its folder.
- **No "Argumentos de inicio" row.** The launcher has no such setting to edit.
- **A list of home cities appears on the METRÓPOLI row** when the player has more than one. The
  prototype draws one, and a player has one per civilization played.
- **A backup has no "Borrar" and every row restores the newest.** The launcher has no per-backup
  restore or delete, and adding them is a separate decision.
- **Catalog addons that are not downloaded yet keep "Descargar y activar"** rather than a switch, and
  keep their "Página ↗" link: the switch would promise something that needs a download first.
- **Statistics rows keep more than the prototype's:** the units of an AI game, and the deck saved
  with a match. Durations of games against people are absent because no file records them.
- **The "newer copy" notice compares versions only when both can be ordered.** The prototype compares
  strings, which gets dates written day first wrong.

Screen 72 (Ajustes del launcher):

- **Kept although the prototype has no row:** "No molestar", "Descargar solo lo que cambió", "Buscar
  actualizaciones al iniciar", "Abrir la página de novedades" and "Quitar de esta PC". All of them
  work, and removing them would remove a feature.
- **Hidden because nothing reads them:** "Canal", "Límite de descarga" and "Mostrar mi ELO".
- **"HUELLA OK / DISTINTA" compares the three key files with the install's own baseline**
  (`ModFingerprintCheck`, the same reading version recognition makes, so an applied translation does
  not count as a touched file). No baseline or an unreadable file shows no chip: nothing is claimed.
- **Installed mods show no size.** It would mean walking every mod's folder to open a settings page.
- **"Ajustes del mod…" replaces the row's old "Actualizar"**, as the prototype draws, and the old
  button had no click handler.
- **"Reiniciar y actualizar" opens the update window and starts it**, because that window is what
  checks the download, the hash and the signature.

## Where `design_base_launcher_2/` was deliberately not followed

Screen 97 (Inicio de sesión):

- **The link's 15 minutes are the server's `expires_in`**, with 15 as the fallback; the prototype
  only states the number.
- **A refused sign-in has its own text.** The prototype knows "no answer" and "expired", but a server
  that answers no is neither, so there is a third failure.
- **The address breaks at `&`.** WPF has no break-anywhere.
- **While the link is being prepared (97a) Copy is hidden, not shown disabled.** There is nothing
  to copy yet, and only "Open browser" keeps its place with the reason in its tooltip.

Screen 100 (Launcher en este PC):

- **The window keeps sentences the code can back:** what the data folder holds, recordings still
  waiting to upload, leftover registry keys, and the removal of auto-start and `wol-launcher://`.
- **The space shortfall is computed**, not a fixed "50 MB".

Screen 101 (Generar parche):

- **The button is enabled once both zips are set**, so the amber mark on a missing tag can be reached.
- **A path field has no ellipsis:** a `TextBox` cannot trim and its text is what gets read back.
- **Cancel also removes a patch of that run** that had already finished writing.
- **While it generates, Cancel is the footer's button and Generate is hidden**, rather than a
  Cancel inside the progress card beside a Generate that stays: one button in the footer, and the
  click cancels the token either way.

Screen 102 (Empaquetar traducción):

- **"Faltan 3 cosas" is written with a digit**, and the mod picker is a `ComboBox` with a template.
- **The preview shows no flag:** a pack declares a language, not a country.
- **"Generate anyway" is the primary button relabelled**, not a second button inside the amber
  box: the box says why, and the one button that generates is the one that confirms it.

Screen 99 (Ver una partida):

- **The chat stays read-only** until the server admits a supervisor. Four doors are shut, listed in
  `.claude/rules/multiplayer.md`, so the window remains a preview offered only under the sample
  tournaments.

Screen 96 (Partida rápida):

- **It is the room window, not a screen of its own.** Design 87, which the prototype builds on, is
  not in this repository, so the search was added to the existing room.
- **The footer shows a spinner**, not the prototype's dot, because it is what says the search is alive.
  With Radmin not ready it stays beside the amber title, because the search goes on.
- **Still the existing room header** (and "ANTES DE EMPEZAR", not "PARA QUE CUENTE"): both come from
  design 87. The search itself matches 96a to 96d. Moving a player to an older room (96b) only adds a
  chat line and was read in the code, not seen.

Screen 98 (Estadísticas):

- **The heading counts rated matches where the prototype prints the total** under "puntuadas". The
  server's total carries no rated filter, and the page has shipped that mistake once.
- **"Sin resultado leído" is worded only for `no_decided_result`.** Any other reason reads
  "No contaron: <code>", the server's own identifier.
- **The empty state says "muchas civilizaciones"**, with no count: nothing on this page knows it.
- **The decks line quotes the real label**, "Compartir mis mazos…", not the prototype's "barajas".
- **The cards keep their art, against the prototype, by the maintainer's decision:** number, picture
  and name in Barajas, a small picture in the chips here and in the Profile. A card with no picture
  shows only its text, never an empty square.
- **The flags stayed in the tables and the civilization pills** for the same reason, and in the
  Profile's Civilizations too. They come from the mod's art, so a civilization with none shows no flag.
- **The cards "Cómo se mide", "Partidas que contaron" and the 2v2/3v3 split are gone**, because the
  reference has none of them. What they said moved into notes and the activity card, except the
  split, which no longer appears anywhere.
- **The sub-tab bar and the search on it are unchanged:** they belong to design 87.

Screen 95 (Perfil):

- **`MinDecidedForPercent` stays 5.** The prototype's note says 6; the code and three other screens
  say 5, and the note reads the constant.
- **"Partidas" (recordings on this PC) lost its door here.** The reference has four sections and this
  was a fifth. Those matches remain in the mod window's STATISTICS section.
- **There is no "Cargar más partidas".** `GET /matches/history` has no paging, so nothing could load.
- **The players of a match show no flag and the rating range only for the viewer:** a participant
  carries a change, not a before and after.
- **A deck chip shows the card's name only.** Real names often carry the number already ("2
  Settlers"), so the prototype's "3 Colonos" would double it.
- **Another mod's deck chip has no count until it is opened:** counting means reading its folder.
- **The community comparison needs the Statistics page on the same mod.** Its figures are scoped to
  that page's mod and the Profile does not fetch its own.
- **The light under the portrait is omitted** and the age's colour is on the ring and the pill: a
  blurred glow behind the picture would redraw with the badge beside it.

Screen 103 (Diálogos pequeños, menús y popovers):

- **All 41 message boxes went, not the nine the README counts**: 32 of them were in `MainWindow`. The
  two inside `OnClosing` stay, as the README says.
- **No blur, so floating surfaces are denser:** .97 / .93 / .88 against the design's .95 / .74 / .6.
  Those numbers come with 18 to 32 px of blur because a menu sits over text. WPF cannot blur what is
  behind a popup, and at .74 the text behind read through the menu's own.
- **103d has no "don't ask again" box.** The question is asked once whatever the answer, so the box
  would change nothing, which the base forbids. `CompatibilityLayerDialog` keeps the one it has, and
  that one works.
- **103e keeps its own text.** The prototype's "Instalar más tarde" is a stand-in: this dialog removes
  a compatibility mode Windows set, and installs nothing. The third button is "Abrir propiedades".
- **103f drops "Copiar rutas" as drawn.** The paths are selectable text.
- **103j counts to 80 and 103k's tag takes 8,** the server's numbers, where the prototype draws 40 and 3.
- **103n keeps Favorito, Página del mod and Desinstalar,** which have no other place, and gained a
  visible ⋯ button beside the gear, since a right click may not be the only way to anything.
- **103o has no servers, latency, "Reconectar" or "Cambiar servidor…".** There is one backend. It says
  how many players are online, and keeps the Radmin IP and the help.
- **103p does not replace the brand menu,** whose About, Community and Exit have no other place and
  which a signed-out player still needs. It keeps the badge selector and the rank guide.
- **A small dialog of a mod shows its glow and tint, not its image:** that is all the prototype draws.
- **`StartupUpdateWindow`'s "Reintentar" spends the same attempts as a launch** (`MaxAttemptsPerTag`),
  so it cannot walk past the loop guard. With none left, the launcher simply opens.


Compared frame by frame with `Diálogos pequeños v2.dc.html` afterwards. What still differs, on
purpose: the text of 103d, 103e and 103f is the launcher's own, because those dialogs say something
else than the prototype's sample (103d asks about starting with Windows, so it carries no "don't ask
again"); a dialog that names no bar title of its own reads "AoE3 Mod Launcher"; 103h has no avatars,
since a backup has no author, and shows the date as its title; 103l has no "en Salas" line, which
needs a presence field the server does not send. Windows Segoe UI has no Medium weight, so the
prototype's 500 renders as Regular in WPF and looks lighter than the browser's.


**Screen 87 (Sala)**, compared with `Sala v2.dc.html` in 87a to 87l, the three glass levels and
English. What differs, on purpose: the rank badge stays in the rows (design 51) and so does the
ping, as a tooltip; the host keeps the 1 | 2 picker on other players and Remove on observers,
because neither has another door; WHAT'S AT STAKE has no +19 / -11, since no such figures exist
(the anti-farm is not announced) and carries the odds when the server sends them; the recording
and abandonment lines stay in the panels, because a guest reads them nowhere else; with AoE3 closed
the host reads that the countdown starts again, since the prototype's "the match goes on" is true
only for a guest; the rival's row has no rank, which the report does not send, and takes his
previous ELO from the room; the chat keeps its emoji button. TRAFFIC, CONNECTION and RECORDING are
not in the prototype and are not drawn.

**Screen 88 (Crear sala)**, compared with `Crear sala v2.dc.html` in 88a to 88c, the mod list, the
three glass levels and English. What differs, on purpose: the proposed name is `WoL · Ranked 1v1`
and not «Wars of Liberty 1v1 competitiva», because a room title is an English network value
(`RoomTitleProposal`); the observer rows only exist in developer mode, as before; the room needs a
password when the switch is on (an extra footer error, since an empty one makes a public room); the
forfeit and recording promises went from the page to the type hint's tooltip; the mod's icon in the
bar has no tile behind it (shared title bar). Text is one notch lighter than the browser's.

**Screen 85 (Sala de torneo)**, compared with `Sala de torneo v2.dc.html` in 85a to 85i, two glass
extremes, the smallest window and English. What differs, on purpose: a game that does not count
offers View bracket instead of I'm ready, because the server closes the room after every report,
whatever it says, so 85e's "repeat it in the same room" does not exist (replaying goes through the
bracket); the abandon warning says what really happens per format (in a 1v1 the walkout can lose the
match, decided by the rival's recording; a team game is never decided that way); a host with AoE3
closed reads that the countdown starts again; a player who has not entered shows the seed and no
rating, since an entrant carries none until they are in the room; "se lleva el torneo" and "Quedas
fuera del torneo" are generic, because a tournament's name gives no article ("la liga", "la Copa");
the stored-recording line of PARA QUE CUENTE became that panel's tooltip, for room; the version
number comes from the installed mod, so a mod that is not installed shows its name alone.

**Screen 81 (Nuevo torneo)**, compared with `Nuevo torneo v2.dc.html` in 81a to 81c, the mod list, the
glass extremes and English. What differs, on purpose: the mod's colour goes through
`ModAccent.Normalize` like every other window, so Wars of Liberty's red is softer than the prototype's
raw `#c8102e`; a selected segment has no glow, since an `Effect` over its caption would cost ClearType;
32 and 64 appear only when the server sends `max_capacity`; a mod with a cover shows it, where the
prototype's 81c has none. Every mod is offered, installed first, because an organiser is not always
a player.

**Screen 82 (Inscribirse)**, compared with `Inscribirse v2.dc.html` in 82a to 82l, the glass extremes
and English. What differs, on purpose: the captain's own row in the line-up cannot be unticked,
because the server refuses a line-up without its captain; "you have version 1.1.9, update it" is not
drawn, since a tournament records a mod and no version; "played with you" appears only when the match
history is already loaded, and nothing is fetched for it; the mod's colour is normalised as everywhere;
a tournament whose places are full says you go on the waiting list, which the prototype has no state
for; a mod with a cover shows it. The facts' values are drawn at regular weight where the
prototype asks for 500: Segoe UI has no 500, and WPF and Edge resolve the gap differently.

**Screen 83 (Mis equipos)**, compared with `Mis equipos v2.dc.html` in 83a to 83m, the minimum size,
solid glass and English; 83l, the toast, by rendering the card from a test. What differs, on purpose:
83m's "Editar…", "Hacer capitán" and "INSCRITO EN" are not drawn, because the server has no route for
them; only the sent invitations are, and only with a server that sends `sent_invites`. Avatars use the
launcher's own colours. The toast has no blur behind it: it is a window over the desktop, so the card
is the solid level. Who invited comes from the team's roster, a second request, so an invitation whose
team cannot be read says "You were invited". A refused create keeps the form with what was typed. The
sheet's list shows its scrollbar where the prototype's headless render hides it.
`CreateTeamDialog` is gone, and with it `--preview-dialog=small:k`.

**Screen 80 (Torneos)**, compared with `Torneos v2.dc.html` (80a) at its own 1600 x 960, then at
1366 and 1920 in Media, 1920 in Solid and Maximum, 2560, reduced effects and English. What differs,
on purpose: the subtab bar keeps the launcher's pills until screen 86 changes it for every subtab;
the sample data carries an amber strip saying it is not real; Replay is offered only on a match not
yet decided, because the server refuses it on a decided one (the prototype offers it on both); a tile
keeps a `⋯` with Disqualify for the organiser; the list states a tournament's round and winner only
for the one that is open, because the list payload does not carry them; "your turn" in the list needs
the server's `my_entries`; a co-organiser is picked among the captains already entered; the accent is
normalised like everywhere else; the cover is blurred once, never live; 32 and 64 places need the
server to raise its limit. Under reduced effects the champion has no glow.

**Screen 86 (Salas)**, compared with `Salas v2.dc.html` in 86a (1920) and 86b to 86e (1366), then
English, 2560 and the Solid and Maximum glass. What differs, on purpose: the PING column is the
ping to each host over Radmin and appears only when the server publishes the hosts' addresses
(`GET /lobbies/radmin`); a host that does not answer shows "—" with the reason, and the viewer's own
internet ping is said once above the list ("Tu conexión · 62 ms"); "Entrar a mirar" appears only in developer mode, so without it the observers' room reads
"Llena"; "Ver partida" opens the players peek rather than a "watching a match" screen; the "no ranked
ladder" state of the quick match card appears only after the first search for that mod, because only
the server knows it, at create time;
the activity facts keep their own count instead of 2/3/4 by width; the sample peak hours are the
fixture's. The subtab bar (capitals, a blue line) is now the same for all four subtabs.
**Two later departures from 86, both the maintainer's (2026-11-10):** the quick match card became a
one-line bar of about 56 px with a mod picker and a Rated / Casual / Either segment (no kicker, no
version chip, no foot line); and the host cell of a room wears his Discord photo and his rank badge
again, with icons on PLAYERS and PING and a blue rim on "Unirse".

**Screen 93 (Clasificación)**, compared with `Clasificación v3.dc.html` in 93a (1920), 93b, 93d and 93e
(1366), 93c (2560, Teams) and 93f (Highlights), then English, reduced effects and the Solid and
Maximum glass. What differs, on purpose: the ages are the launcher's own shares of the ladder
(`RankAges`), so a table of nine does not cut where the prototype's sample does; the badge is the
launcher's `RankBadge`, larger and with its own light, and Discovery's is the dull plate it is
everywhere else rather than a shield with an "I"; a group's range is the age's places on the whole
ladder; only the viewer's own row, podium card and "Your position" open a profile, because there is
no profile page for another player; the month's climb ("+41 in November") is drawn only for a player
the server's biggest-climb list names, there being no figure for anybody else; the glows that are
box-shadows in the prototype are left out (no live effect over lists) and the cards' fill is the
launcher's glass level, not a live blur; the "Clasificación" title and the "How ranks work" button are
gone from the header (the guide opens from any badge and from the account menu); Highlights keeps
its month capsule where the prototype has a "see last month" link; the Matches view is unchanged.
With no peak sent for anybody (93d) the table has three columns, so its ELO column starts further
right than the prototype's four.

**Closing the set (2026-11-10).** Every screen of this handoff was compared again at 1920 x 1080 in
Spanish and English after the last one landed, to catch a later screen moving an earlier one
through a shared piece; none had. Still outside the launcher: the organiser's chat in a bracket
match (99d) waits for the server to admit him into a game under way, and the server changes the
redesign needs (per-host Radmin addresses, `started_at`, `tournament_id`, `my_entries`, sent
invitations, 32 and 64 places) are listed, with their order, in the backend's `DEPLOY.md`. Only two
real accounts can prove the rematch, an entry that needs approval, a real bracket match, a team
invitation and the per-host ping between two PCs.

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
