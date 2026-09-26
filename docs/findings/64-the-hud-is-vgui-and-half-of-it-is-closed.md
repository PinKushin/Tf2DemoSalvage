# 64 — The HUD is VGUI, and half of it is closed

**Question.** What does it take to draw TF2's HUD the way the game draws it, custom HUDs included?

The published SDK holds the controls (`vgui2/vgui_controls`: Panel, EditablePanel, BuildGroup, Label, TextImage,
ImagePanel, ScalableImagePanel) and the client's HUD classes. It does not hold the parts under them: the panel tree and
the scheme (`vgui2.dll`), the surface and the fonts (`vguimatsurface.dll`), or the localised strings. Those were read from
the shipped binaries, in Ghidra projects `tf2vgui2` and `tf2vguimatsurface`. The code remarks name each function by
address. What follows are the findings that someone would otherwise "fix".

## Engine behaviour worth keeping

- **Positions measure against the screen, not the parent**, unless the block sets `proportionalToParent`.
  `proportional_int` and `proportional_float` scale even on a panel that is not proportional, and the float passes
  through an int. *Read from published source.*
- **The first `GetCharABCwide` answer is GDI's own, and later answers are widened** by the font's effects. So the first
  string measured in a new font is measured narrower than every later one. *Disassembly.*
- **The blur's far-edge window never reaches the centre pixel.** *Disassembly.*
- **`CFPSPanel` sits at x = wide − 300, with no clamp.** On a narrow window it runs off the left edge. *Published source.*
- **A `Bitmap` with a rotation draws at the panel's origin, not at its position.** `Paint` (0x180002290) builds the
  rotated polygon from 0,0 and never adds `SetPos`. *Disassembly; no stock HUD file sets `rotation`.*
- **A localisation conditional's `!` is read in two places, and one of them never sees it.** `AddFile` (0x180008f80)
  accepts a conditional only as a token beginning `[$`. For a language name it reads `!` after the `$`. For anything else
  it passes the token to `EvaluateConditional` (0x18001e920), which reads `!` only straight after the `[`. A `[$` token
  never has one there, so `[$!X360]` names no platform the evaluator knows and comes out false. The shipped
  `tf_english.txt` has no conditionals at all. *Disassembly, and a count of the shipped file.*
- **A localisation file without the UTF-16 byte order mark is ignored**, with the message "Ignoring non-unicode close
  caption file", whatever the file is. A token added twice keeps the later value (`AddString`, 0x180009ed0).
  *Disassembly.*

## A wrong turn: an empty scheme is white

The first frame with the HUD root wired was solid white. The viewer builds VGUI before the install is open, so
`ClientScheme.res` was read as nothing. An empty scheme answers `Panel.BgColor` with the white fallback, and the
viewport painted it across the screen. It then kept it, because only a new screen size loads a scheme again. This is how
the engine behaves too: `Panel::ApplySchemeSettings` asks with a white default. So the fix is to build no VGUI until the
files exist, not to change the fallback. A `hud` probe — the production `VguiHud` on the install — drew no quads, which
ruled out the layout. One logged frame of the viewer showed a single fill the size of the window. *Measured, with a
control shot taken with the HUD switched off.*

## Whose health the HUD shows, and when it shows none

The first health number the viewer drew on f12 was "1". f12 is a SourceTV recording, and there the local player is the
SourceTV client itself: entity 1, team 1, `lifeState` 2, `m_iHealth` 1. The game does not draw that 1, because
`StartObserverMode` sets `m_Local.m_iHideHUD = HIDEHUD_HEALTH` (player.cpp:2298), and the demo carries it: 2058 on
every tick sampled. The HUD reads the player's own `m_iHealth`, not the player resource's (which the scoreboard reads).
`GetMaxBuffedHealth` is the resource's `m_iMaxBuffedHealth` — "actually m_iMaxHealthForBuffing, but we can't fix it now
because of demos" (c_tf_playerresource.h:81) — times 1.5, floored to a multiple of 5. On the 2013 POV specimen at tick
1000 the recorder is at 125 of 125 with `m_iHideHUD` 2050, and the viewer draws 125. *Measured on the corpus.*

## Custom HUDs are ordinary `.res` files

The owner's custom HUD draws its crosshair as a `CExLabel` created from `ControlName` in `hudlayout.res`, with a font
from its own `CustomFontFiles`. Nothing about it is special-cased. It is the build group making a control it was told to
make. That is why the control factory has to know TF's client classes, not just `vgui_controls`. *Measured: `hud custom`
lists the five crosshair labels.*
