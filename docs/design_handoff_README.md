# UI design handoffs — index

`docs/` carries thirteen design-handoff folders — the four named `design_handoff_*` plus
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

## Where `design_insignias_pantallas_guia/` was deliberately not followed

1. **The account block does not open the guide.** The handoff makes that click open it; it
   already opens the account menu (Perfil / Cerrar sesión), which the maintainer asked to keep.
   The block does show the badge and the age ("Colonial · 1383 ELO"). The guide's entries are the
   "How ranks work" link on the Ranking subtab and a click on any badge.
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
