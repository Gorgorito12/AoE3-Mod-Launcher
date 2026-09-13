# UI design handoffs — index

`docs/` carries ten design-handoff folders — the four named `design_handoff_*` plus
`design_generar_parche/`, `design_publicar_e_instalar/`, `design_simetria/`,
`design_sala/`, `design_mazo_comunidad/` and `design_mazo_tamano/`, which arrived later
and kept their own names. **Nine of the ten are fully implemented** and are kept as
*historical reference*, not as work to do.

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
