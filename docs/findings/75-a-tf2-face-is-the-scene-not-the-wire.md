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

## The remainder, closed (B513, second pass)

- **The target ramp, read in disassembly.** `R_StudioFlexVerts` (x64 `studiorender.dll` `0x18001eb90`): 0 at or
  outside t0/t3, `(w-t0)/(t1-t0)` rising, 1 to t2, `(t3-w)/(t3-t2)` falling. A flex is skipped only when all four of its
  weights sit inside (−0.001, 0.001), compared as doubles; speed and side are bytes × 0.003921569; the weight is
  `((1-s)·w2 + s·w1)·(1-side) + ((1-s)·w4 + s·w3)·side` (*disassembly*). The earlier reading was right on [0, 1].
- **Fixed point is not a file format, it is a load step.** `datacache` `0x180009ff0` converts every float16 delta to
  `(short)(int)(half / scale)` once per model, with scale the header's if flagged else 1/4096, and marks the header
  `0x4000`; drawing multiplies back. So every delta is quantised at 1/4096 even on unflagged models (*disassembly*).
- **A TF player's face does not rest at the bind pose.** `ResetFlexWeights` sets each controller to 0 *in its own
  range* (`c_tf_player.cpp:5298`), so a −1..1 lid rests half shut by the rules: 398 soldier vertices move at rest,
  0.25 units at most (*measured*). The first pass's "resting = minimum" was the answer for a C_BaseFlex nothing set — a
  worn item off a player, for instance — and is kept for that.
- **FLEXANIMATION** writes `m_flexWeight` after a ×0.95 decay **per SetupGlobalWeights call** — and a worn item and a
  dead player's corpse each call the player's (`econ_entity.cpp:1377`, `c_tf_player.cpp:636`), so the decay runs once
  per drawing entity per frame (*source*). A track naming a controller the model lacks writes controller 0
  (`MAX( FindFlexController, 0 )`, `:1987`). No scene any lcor demo we probed plays carries one (*measured*,
  `flex played`), so this is synthetic-only.
- **Every scene, not the gesture slot's.** A real match runs idle loops, attack and voice scenes on one player at once:
  1,417 of 1,824 runs in tf2-2026-pub-pov-clean overlap another on the same actor (*measured*). A scene's stop is now
  recorded — `m_bIsPlayingBack` false, the slot switching scenes, or the entity deleted.
- **EventThink's lag**: an event is live while the scene's time at the PREVIOUS frame is inside it
  (`choreoscene.cpp:2529`), read at this frame's time. **`NULL`-named events** never start (`c_sceneentity.cpp:463`).
- **Lip sync.** TF2's voice lines are MP3s with no phoneme chunk; the sentences live in the VPKs' `.sound.cache`
  (`CAudioSourceCachedInfo::Restore`, `engine.dll` `0x180053740`, *disassembly*). Only 51 sounds carry one —
  scout head-left/right and taunt lines, soldier tank lines, a burp — so ordinary voice lines move the mouth through
  their scene's expressions. One cached line is played in the lcor sweep: `vo/scout_HeadRight03.wav`, granary 2013,
  tick 58230; forty ticks in, the visemes move 2,548 more vertices than the same moment without the sentence
  (`FaceLipSyncDemoTests`). The phoneme file is named from the header's `pszName` (`player/scout.mdl`), not the load
  path — the first run used the path, found no file, and moved nothing.
- **Wrinkle maps are unreachable.** Of 16,608 shipped models, 8 carry wrinkle flexes, all `models/player/hwm/*`, which
  `UseHWMorphModels()` — hardcoded `false` (`baseplayer_shared.cpp:104`) — never selects; the CPU flex path ignores the
  wrinkle delta anyway (*measured*, *disassembly*).
- **Non-player flex models** draw with the zero weights `LockFlexWeights` leaves (`0x1800584b0`). 88 shipped HL2
  character models move at zero weight (their ramp is not zero there); none is a TF2 player.

## Still open, named

The interpolation history `m_flexWeight` carries as a `LATCH_ANIMATION_VAR` (`c_baseflex.cpp:134`); the engine's mouth
registration (which channels, pitch) was not located in `engine.dll`, so the voice channels are an **interpolation**;
a corpse whose player has respawned should switch to its own unset face (`c_tf_player.cpp:630`); flex on baked
(unskinned) props is not applied — the only models it would change are the 88 HL2 ones.
