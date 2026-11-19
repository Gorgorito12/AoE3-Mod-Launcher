# Design 116 — the competitive 1v1 room: "what's at stake" in the header

`Sala competitiva 1v1.dc.html`, frame **116b** (open it in a browser; `support.js` and `mods/` are
only what it needs to draw). Two controls at the top switch the language and the transparency level.

## What it changes in the room window (design 87)

1. **"What's at stake" is a card in the header**, left of *Invite* and bottom-aligned with it,
   300 wide: two halves (green, an up arrow, "IF YOU WIN" and the gain; red, a down arrow, "IF YOU
   LOSE" and the loss), then the win chance with both percentages around a split bar, then the
   anti-farm note as an amber band with a drawn triangle. No title row.
2. **The automatic-entry card is a one-line strip**: the label and a short sentence; the full
   sentence is its tooltip.
3. **The abandonment sentence** moved under "FOR IT TO COUNT": the column's stake panel no longer
   shows in a normal room (a bracket match keeps it for its own lines).

Built in `Controls/MultiplayerTab.Stake.cs` (`BuildStakeCard`), `LobbyWindow.xaml`
(`RoomStakeCard`, `RoomLanCard`) and `Controls/MultiplayerTab.LanHelper.cs`.

## The reference contradicts itself: the markup was followed

The page carries hidden notes "Para Claude Code" that describe an EARLIER card (radius 12, a title
row with "20 % a favor", figures at 19 px, a 3-px bar as the card's bottom edge). The frame that
is drawn, and the page's own introduction ("la tarjeta elegida, propuesta 1, con flechas… ya no
lleva la fila del título"), is the two-halves card. From the notes only what the frame does not
contradict was taken: where the card goes, tournament and team rooms, the abandonment sentence
and the strip.

## Not as drawn, and why

- **No letter-spacing** on the captions and labels: WPF's `TextBlock` has none.
- **The card's blur** is the launcher's floating-glass level (`GlassMenu`), not a live blur.
- **The strip's switch** is the shared 38×22 switch drawn at 34×20 through a transform, so the
  launcher keeps one switch style.
- **The shadow** under the card is a blurred, cached sibling clipped outside it, never an effect
  on the card (that would cost its text ClearType); none with the effects reduced.
- **Short sentences**: the design writes the first; the other four were shortened the same way.

| State | es | en |
|---|---|---|
| On (from the design) | Tu juego entra solo a la sala LAN. | Your game enters the LAN room by itself. |
| Off | Apagada: entras a la sala LAN a mano. | Off: you enter the LAN room by hand. |
| Not set up | Sin preparar para este juego: entras a mano. | Not set up for this game: you enter by hand. |
| Cannot | Este juego no puede entrar solo: entras a mano. | This game can't enter by itself: you enter by hand. |
| No home city | Crea primero tu metrópoli: esta vez entras a mano. | Create your home city first: this time you enter by hand. |

## Seeing it

`--demo-room=87b` (figures), `87b-farm` (the amber band), `87b-hidden`, `87b-private`,
`87nocity` (the amber strip), `85a` / `85c` (a bracket match: its lines in the column, the card in
the header), `87f` (a result: no stake card).

---

# Design 117 — "Rival found", the quick match's dialog

`Partida encontrada.dc.html`, frames **117b** (the proposal), **117c** (I accepted), **117d** (the
rival in placement, accepted, four seconds left) and **117e** (casual). 117a is a mock of the
window before this change, not a capture.

## What it changes in `MatchFoundDialog`

- The crossed swords in a green tile (the pictogram of the "rival found" toast) and the kind of
  match as a small label under the headline: gold when rated, grey when casual.
- **You and the rival face to face**: photo, badge, name and rating on each tile, mine in the mod's
  colour and his mirrored; under each, Accepted or Pending; a HOST tag on whoever hosts, whose
  tooltip is the whole sentence the window used to print.
- **What's at stake** is the room's card (design 116) in this window's dress: the body's width, a
  darker well, no shadow, figures at 25.
- **The clock** grows: the seconds in large over a thick bar, blue until five seconds are left and
  amber from there.
- The buttons at the right, Accept the wider one, and the strike note under them at full width.

Nothing changes in what the buttons do, the 20 seconds, when the window opens or what the server
sends.

## The three questions the page left open, and the answers (2026-11-19)

| Question | Answer |
|---|---|
| Who accepted — the server sends only a count | **Deduced**: whatever the count holds beyond my own answer is the rival's (`MatchAcceptView`). The "1 of 2 accepted" sentence is gone |
| The headline's tone | **Green**, as drawn. Accept keeps the mod's colour |
| Badges in a casual match (117e draws none) | **Kept**: a casual match shows the badge each player chose, as before |

## Not as drawn, and why

- **Casual badges**: the answer above.
- **"In placement 3/10" wraps** to a second line in the rival's tile. In the frame it is one line
  that does not fit the tile's text column at 480; cutting it would hide the one figure in it.
- **The waiting Accept button** is the shared primary button's disabled state, not a colour of its own.
- **No letter-spacing** on the capitals (WPF has none), and no drop shadow under the small badge.
- The badges are the launcher's real ones, not the frame's plain shields.

## Seeing it

`--preview-dialog=matchfound:guest` (117b), `matchfound` (I host), `:accepted` (the rival already
said yes: press Accept for 117c, wait for the amber clock of 117d), `:placing`, `:challenge`,
`:casual` (117e). The buttons only close the window.
