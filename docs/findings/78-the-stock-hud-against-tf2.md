# 78 — The stock HUD against TF2's own

**Question.** The owner, watching a UI run: *"the hud isnt right for stock"* — *"the ammo font color, the background
blocks on the ammo, and player model"*. What does TF2 draw there, and why did the viewer draw something else?

**The reference.** TF2's modern client with the owner's HUD moved out of `tf/custom` (D222), on `cp_process_f12` loaded
with `map`, 1280x720; a POV demo recorded in the same session is what the viewer plays
(`hudref-cp_process_f12-2026-10-17-pov.dem`). Captures: a BLU soldier at 200 and at 40 health, the scoreboard, a medic
holding the medigun, a suicide's death notice; and the f12 SourceTV recording's spectator view. The goldens the tests
read are the soldier and the medic (`HudGoldenImageUiTests`).

The first thing the game's own files said: every difference below was in a layer UNDER the HUD element. The elements
(`CTFHudWeaponAmmo`, `CTFHudPlayerClass`) were faithful ports; the panel tree, the material, the colour space and the
model's sequence lookup were not.

## What differed, and why

| What the owner saw | Cause | Evidence class |
|---|---|---|
| Ammo and class backgrounds are flat blocks | `materials/hud/ammo_blue_bg.vmt` and `character_*_bg_clipped.vmt` are `UnlitGeneric` with `$distancealpha` from a `$detail` mask: the shape, a soft edge, a cream outline and a drop shadow all come from the mask's distance field. The VGUI renderer drew the base gradient as a rectangle. | read from the shipped VMTs and `vertexlit_and_unlit_generic_ps2x.fxc:257-327` |
| The class background is RED on a BLU player | `localplayer_changeteam` fired when the team differed from `HudViewport.State` — but `VguiHud.Frame` stores the frame's state (`SetState`) before the next frame thinks on it, so the change was never seen. Every `CTFImagePanel` kept its `.res` default image, red. | measured: a frame at team 0 then team 3 left `LocalTeam` 0 |
| The class model stands in its reference pose, weapon at its side | `CBaseModelPanel::FindSequenceFromActivity` walks `CStudioHdr::GetNumSeq()`, the VIRTUAL model. A class model's own table is `ref` and `ragdoll`; `ACT_MP_STAND_PRIMARY` lives in `soldier_animations.mdl`. Ours read the root group alone, found nothing, and stayed on sequence 0. | read from published source (basemodel_panel.cpp:229-244) |
| HUD colours too light | TanLight (235 226 202) text read back 246 242 230. A VGUI vertex colour is gamma: `o.color.rgb = GammaToLinear( v.vColor.rgb )` (vertexlit_and_unlit_generic_vs20.fxc:238), `pow( x, 2.2 )`, before the frame's sRGB write. TF2's frame reads 236 227 203 — exactly what pow 2.2 then the sRGB curve gives, which neither an exact sRGB round trip (235) nor none at all (246) does. | measured on both frames; arithmetic |
| The ammo number is its shadow | `AmmoInClip` and `AmmoInClipShadow` share `zpos 5`, the number listed first. TF2 paints the number OVER its shadow; ours painted the shadow last. | measured (pixels below) — see the open question |
| No ÜberCharge meter on a medic | `CHudMedicChargeMeter` (tf_hud_mediccharge.cpp) had never been ported, and the medigun's `m_flChargeLevel` was not decoded. | read |

**The ammo number, measured.** A column through the stem of TF2's "4": TanLight (236 227 203) for every pixel of the
glyph, then ONE pixel of 42 39 37 (the scheme's `Black`, 46 43 42) at the bottom edge, then the background. The shadow is
drawn under the number, offset down and right by one. Ours had the number's 1-pixel rim at its top and left and the
shadow's colour everywhere else. The scoreboard's team scores, also main-first at equal `zpos`, show the same in TF2.

## Wrong turns, kept

- **"The shadow is translucent."** Our "4" read 118 114 113 inside, between Black and TanLight, which looked like a
  blend. It is `Black` (46 43 42) written as linear light into the sRGB view: 46 → 118. The gamma fault and the
  paint-order fault together made one grey.
- **"The sibling order comes from `AddToHead`."** `vgui2.dll`'s `VPanel::SetParent` (0x18001c3e0) appends the child and
  then calls `SetZPos` with its own z; `SetZPos` (0x18001c610) moves a panel past a neighbour only while strictly out of
  order; `Panel::PaintTraverse` (Panel.cpp:1128) paints children first to last. That predicts the shadow on top — what
  ours did — so the order TF2 draws is not explained by the panel tree. *Disassembly and published source.*
- **"The modern client can be the stock reference as it is."** It carried the owner's HUD (`Garm3n-VIP-Quad`) and his
  config; D222 moved the HUD out. TF2 also exits silently right after boot when Steam is not running.

## Residual, measured

- The class portrait's model is about 20 luma darker than TF2's at the helmet (cell means 125 against 101) while the
  pose, framing and background match. The panel's light rig is `CPotteryWheelPanel`'s (potterywheelpanel.cpp:741-758);
  not yet compared.
