# 75 — A TF2 face is the scene, not the wire (B513)

*Evidence classes are marked per claim: **source** (read in source-sdk-2013), **measured** (probe or test on shipped
files or the corpus), **interpolated** (not read in the closed code it depends on).*

## What was believed first

B512 filed vertex flex as "the viewer applies no flex at all", and the task that followed it named **blink** as the
first driver to port: `C_BaseFlex` networks `m_blinktoggle`, and the client times a 0.2 s blink from it
(`c_baseflex.cpp:1228-1257`). That would have been the cheapest thing to wire, and it would have been dead code.

## What killed it — twice over

1. **The wire.** `DT_TFPlayer` excludes all three of `DT_BaseFlex`'s props — `m_flexWeight`, `m_blinktoggle`,
   `m_viewtarget` (`tf_player.cpp:782-784`, *source*). A TF2 player's toggle never changes, so the blink never starts,
   and the networked weights stay zero — which `SetupGlobalWeights` rescales to each controller's **minimum**, not to
   zero (`:1222`, *source*).
2. **The rules.** Even a blink would move nothing. TF2's player models stack the `blink` controller's index into the
   `STUDIO_DME_LOWER/UPPER_EYELID` ops at `stack[k-2]`, and `RunFlexRules` only *validates* that slot
   (`studio.cpp:1584`, `:1615`); the lid is `CloseLidV × CloseLid` and the eye's pitch (*source*). The `flex` probe
   confirms no op on scout, heavy or the hwm heavy names `blink` directly (*measured*).

## What does move a face

The scene the player is in. Every taunt and voice line is a compiled VCD whose **`EXPRESSION`** events name a `.vfe`
(`player\soldier\emotion\emotion`) and a setting (`happyBig`); `ProcessFlexSettingSceneEvent` scales it by the event's
ramp times the scene's (`choreoevent.cpp:1758`) and `AddFlexSetting` blends each weight into `g_flexweight`:
`g = g × (1 − s) + weight × s`, `s = clamp( intensity × influence )` (`c_baseflex.cpp:1882-1883`, *source*). The
archive census (`flex scenes`): 9,222 `EXPRESSION` events against 1,373 `FLEXANIMATION` events, and the
`FLEXANIMATION` tracks overwhelmingly name HL2's FACS controllers (`inner_raiser` 622, `outer_raiser` 569) that TF2's
models do not carry (*measured*). `UseHWMorphVCDs` returns `false` (`c_sceneentity.cpp:89`), so the `player/hwm`
redirect never happens (*source*).

## The pipeline, as ported

`EXPRESSION` → `.vfe` setting → controllers → `RunFlexRules` (op for op, including the `CHECK` macros that turn a bad
op into a no-op rather than ending the rule) → descriptor weights → each mesh flex's target ramp → per vertex
`lerp( lerp(delayed, current, speed), lerp(partnerDelayed, partner, speed), side )` (`morphaccumulate_ps30.fxc`,
*source*) → position and normal deltas → the shader adds them **before skinning**, and adds the normal's delta to the
tangent too (`ApplyMorph`, `common_vs_fxc.h:384-387`, *source*).

- **Deltas are float16 on disk.** `STUDIOHDR_FLAGS_VERT_ANIM_FIXED_POINT_SCALE` is "flagged on load"
  (`studio.h:2091`); no shipped player model carries it (*measured*), so the union's float16 half is the file's.
- **`speed` is 255 on every vertex of every player model read** (*measured*), so `RunFlexDelay` — frame-rate state
  — cannot change a vertex, and the delayed weights are passed as the current ones.
- **Interpolated:** the target ramp (`RampFlexWeight`, studiorender's closed CPU path). The GPU accumulator leaves it
  to the CPU and the Ghidra MCP's analysis tools were not reachable from this session to read it; with TF2's targets
  of 0/1/10/11 it is the identity on [0, 1] and zero below, so only a negative weight could tell.

## On a real recording

`tf2-2026-pub-pov-clean`, soldier 9, `taunt_laugh` from tick 1074: at tick 1204, 7,965 of 26,922 buffer vertices move,
the furthest 1.79 units, and the drawn head changes (`FaceFlexDemoRenderTests`, *measured*). Before the scene the
soldier's resting face moves nothing — the control.

## Not ported, named

`FLEXANIMATION` tracks (they write `m_flexWeight`, which `ProcessSceneEvents( true )` decays by 0.95 per **frame**,
`c_baseflex.cpp:1703`); `SPEAK` lip sync from the `.wav` phoneme chunk (`ProcessVisemes`); more than one scene per
actor at once (this viewer tracks the VCD gesture slot's scene); the one-frame lag of `EventThink` testing the previous
frame's time; event names `NULL`; wrinkle maps; flex on baked (unskinned) models.
