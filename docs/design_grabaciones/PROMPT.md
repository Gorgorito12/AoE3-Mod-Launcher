# Handoff 63 · prompt

The handoff was produced in Claude Design (project "Mejora de interfaz del launcher",
`docs/handoff_grabaciones/` in that project) and imported with the `claude_design` MCP, with
the instruction to implement `Grabaciones.dc.html` — its README and `ReplayRow.dc.html` are
the contract. The request behind it, from the maintainer: every match in Clasificación ›
Últimas partidas that has a recording should carry a download button, and since recordings are
kept for a year there has to be a way to find any of them, not only the latest thirty.

## Design 64 · prompt

Added later to the same project, in `Grabaciones.dc.html` (section 64, "Vista Partidas · que se
note dónde termina cada fila") together with a `variant` on `ReplayRow.dc.html`. The
maintainer asked for **64b**: every match in the Matches view as a card of its own. It is built;
64a (stronger lines and stripes) is not. The same update moved the row's right column to 38 px
with a −4 px bottom margin, recorded in the README, which applies to every row with a button.
