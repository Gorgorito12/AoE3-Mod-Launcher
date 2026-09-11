# UI design handoffs — index

`docs/` carries seven design-handoff folders — the four named `design_handoff_*` plus
`design_generar_parche/`, `design_publicar_e_instalar/` and `design_simetria/`, which arrived
later and kept their own names. **All six have already been implemented.** They are kept as *historical
reference*, not as work to do.

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
