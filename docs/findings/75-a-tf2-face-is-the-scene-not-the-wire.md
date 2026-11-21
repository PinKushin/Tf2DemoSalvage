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
  (`MAX( FindFlexController, 0 )`, `:1987`). Of the 5,308 scene names TF2's `scripts/` mention, 4,910 resolve and 46
  carry a flex animation — three of them a TF player's: `Player/Medic/low/605` and `687` (twenty viseme tracks) and
  `Player/Spy/low/3032` (*measured*, `flex flexanim`). `687` plays twice in lcor (koth_product, ticks 61085 and 80988):
  sixty ticks in, 3,785 medic vertices differ from the same playback with the animation removed
  (`FaceFlexAnimationDemoTests`). An earlier sweep of three demos found none and called it synthetic-only — wrong,
  because the denominator was three demos.
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

## The last four, closed (B513, final pass)

- **`m_flexWeight` is interpolated, and a TF player latches it every frame.** `AddVar( m_flexWeight, &m_iv_flexWeight,
  LATCH_ANIMATION_VAR )` (`c_baseflex.cpp:134`). A TF player animates client-side, so the server never sends
  `m_flAnimTime` (`SendProxy_ClientSideAnimation`, `baseentity.cpp:156`); the latch is `UpdateClientSideAnimation`'s,
  every frame, stamped with the previous frame's anim time, and `FrameAdvance` then sets it to now
  (`c_baseanimating.cpp:5143`, `:5525`). The frame order decides what that means: `InterpolateServerEntities` writes
  `m_flexWeight` from the history, `UpdateClientSideAnimations` latches it, and only then does a draw run the scene
  (`cdll_client_int.cpp:2156`, `:2189`). **So the history only ever holds what interpolation left — a scene's write
  lasts the frame it is made in**, except when the history had settled and the next interpolation was skipped. Ported
  as the engine has it: the three-entry `Reset`, `NoteChanged`'s flush and "differs" arming, `GetInterpolationInfo`
  with the Hermite third sample and `frac` capped at 2, `RemoveEntriesPreviousTo` keeping three, and the extrapolation
  `cl_extrapolate` allows — reached when a frame is longer than half the interpolation amount, since the history's head
  is always the frame before (*source*; `FaceFlexTests` predicts both, exactly). Mid-event at full weight it changes
  nothing (the medic count is 3,785 with and without it, by sabotage); it shapes the ramps and the fade after an event.
- **Which channels feed the mouth, read in disassembly — and the first guess was wrong.** `snd_mix.cpp`'s
  `MIX_MixChannelsToPaintbuffer` (x64 `engine.dll` `0x18003f7f0`) updates a mouth for a channel from the local player,
  OR on `CHAN_VOICE`/`CHAN_VOICE2`, OR **whose source carries a sentence, on any channel**; the update
  (`0x180046cf0`, the "out of voice sources, won't lipsync" message) adds a sentence-bearing source, refusing a fifth,
  and sets an existing one's elapsed time to the mixer's sample position over the source's rate, so pitch speeds it.
  Freeing a channel (`0x1800449d0`) removes its source — and **empties the mouth when the source is not in it**, so a
  voice line without a sentence ending clears every sentence still playing. The remainder pass took the two voice
  channels and nothing else; the burp (`CHAN_STATIC`) moves a mouth too. A channel's end is the cache's sample count
  over its pitch (**interpolated** for sounds the cache does not list); the speaker entity is not decoded.
- **A corpse whose player is alive again wears its own face** — `( pPlayer && pPlayer->IsAlive() ) || !pPlayer` takes
  `BaseClass::SetupWeights` (`c_tf_player.cpp:630`), a C_BaseFlex nothing set, every controller at its minimum, which
  on a class model moves nothing. tf2-2026-pub-pov-clean's corpse 24 (spy, player 2) at tick 22364 draws the bind face;
  reported dead, the same corpse wears the player's (`FaceCorpseDemoTests`). A class model a map places is a prop, not a
  face: only an entity the recording names as a player gets one.
- **Flex on baked props is unreachable, by census.** Over the 239 maps the install ships (VPK, loose and
  `download/maps`) — 380,679 static-prop and entity placements of 10,287 distinct models — 18 placed models carry vertex
  flex (class and `hwm` models as statues, one workshop cosmetic) and **none moves at zero weight**, which is the only
  way a prop with no `SetupWeights` could show flex (*measured*, `flex placed`). The 88 HL2 models that would are placed
  by no TF2 map.

## The last three (B513, last pass)

- **A face is kept current off screen.** `UpdateClientSideAnimations` walks the whole client-animation list every frame
  (`c_baseanimating.cpp:6372`), and every scene thinks every frame, so the latch, the history and the scene clock
  `EventThink` reads move whether or not the player is drawn; only `SetupGlobalWeights` waits for a draw. Counting only
  drawn frames starts an event a frame early on the first frame back (`FaceFlexTests`).
- **The speaker entity was decoded all along and dropped.** `nSpeakerEntity` is the last field of every sound message
  on every protocol (`soundinfo.h:366`) and a stop clears it (`:153`); the mouth update takes it before the source
  (`engine.dll` `0x180046cf0`). Now carried; 0 of 1,129,637 sounds in 69 corpus demos name one (*measured*).
- **A sound the cache does not count ends at its file.** The cache stores no MP3 count (`0x180047c80` records the rate
  and the file size), so the channel ends when the decoder runs dry. `SoundLength` reads a wave's `data` over its block
  size and walks an MP3's frames; against the cache's 2,815 WAV counts and a full decode of 300 voice MP3s, all within
  half a millisecond (*measured*, `flex lengths`).
- **And a wrong turn it caught.** That control first disagreed by exactly two on every file: the cache's rate is
  `info >> 14 & 0x1ffff` (`CAudioSourceWave` from the cache, `0x18004f870`), and the remainder pass had shifted by 15 —
  every sentence length it reported was doubled, which nothing downstream could have shown.

## The studio render's face state, beyond the wire (B725, item 5)

What the eye and teeth shaders are handed at the draw, read after the shaders themselves were ported.

- **The mouth is a material flag, not the Teeth shader.** It was believed — and built — that a mouth is set up for any
  Teeth material on a model with a mouth, with the mesh draw's gating argument at `0xa0` "not identified". TF2's x64
  `studiorender.dll` names it: the function at `0x1800041c0` is `R_StudioSetupSkinAndLighting`, `0xa0(%rsp)` its fifth
  argument `materialFlags`, which both callers fill from `pMaterialFlags[ skinref[ mesh->material ] ]`, and the material
  load sets each to `stricmp( $clientShader, "MouthShader" ) == 0` (`0x180058172-0x1800581cf`, *read*). So the VMT key
  `$clientShader`, which no shader reads, is what lets `R_MouthSetupVertexShader` write `$forward`/`$illumfactor`. Every
  shipped Teeth material states it (15 in HL2's VPKs, *measured*), which is why the wrong belief drew nothing wrong.
- **A model panel's CMDL starts zeroed and is never clamped.** `CMDL::CMDL` (tier3, linked into TF2's `client.dll`
  at `0x180936e20`) zeroes the 0x600 bytes of flex controls, the view target and its world-space flag; `CMDL::Draw`
  (`0x180936f70`) transforms a non-world-space target by the matrix it is drawn with and runs the flex rules on those
  controls (*read*). A merge model is drawn with an identity matrix (`mdlpanel.cpp:853`, *source*), so its eyes look at
  the world origin; a CMDLPanel's controls stay zero, which is NOT where a C_BaseFlex rests (each controller's minimum,
  `c_baseflex.cpp:1222`).
- **The global flex number looked like session history and is not — a wrong turn, kept.** CTFPlayerModelPanel writes
  `m_pFlexControls[ localToGlobal ]` (`tf_playermodelpanel.cpp:2317-2319`), and the first reading, that `RunFlexRules`
  reads by LOCAL index (the bounds check at `studio.cpp:1456` names `numflexcontrollers`), concluded each rule read
  whatever controller happened to own that global number this session. Three lines further, FETCH1 reads
  `src[ pFlexcontroller( index )->localToGlobal ]` (`studio.cpp:1485-1486`, *source*; TF2's `client.dll` the same at
  `0x180317816`, *read*): the rules index by the global number too, so each reads its own controller. The client's own
  path agrees — `C_BaseFlex::RunFlexRules` hands the rules `g_flexweight`, the global array (`c_baseflex.cpp:336`). What
  IS session history: a model no C_BaseFlex has linked keeps `localToGlobal` −1, and a CMDL drawing it reads
  `m_pFlexControls[ −1 ]` — the CMDL's `m_flTime`, four bytes before the array (*read*, the layout from `CMDL::CMDL`).
- **A prop's eyes see whatever the last face drawn looked at.** The view target is render-context state that only
  `SetEyeViewTarget` writes (a C_BaseFlex's `SetViewTarget`, only with flex controllers; a CMDL's draw, always); a
  `prop_dynamic` writes none, so its eyes follow the previous draw's target (*source* for the writers; the reader is the
  eyeball setup at `0x18001e1b0`). Ported as one shared target in our draw order; the engine's order across views is B780.
- **Two "gaps" were not.** The face vertex shaders hand `DoLighting` a literal zero static colour, so a colour mesh never
  reaches a face (*source*); and EyeRefract's binormal packed into `COLOR0` as `B · 0.5 + 0.5` and unpacked `· 2 − 1`
  is affine both ways, which linear interpolation commutes with — the unpacked binormal is the interpolated one exactly
  (*arithmetic*).

## A panel's face is flexed by its draw, teeth or none (B779)

What the B725 reading left: the panels computed the descriptor weights only for a mouth, and drew the mesh at rest.

- **The rules run for every model with flex descriptors, and nothing else gates them.** In `CMDL::Draw` (TF2's x64
  `client.dll`, *read* in the disassembly) the only test before the rules is `cmp [rdx+0x104], r15d` (`0x180937138`) —
  the studio header's `numflexdesc` against zero. A wrong turn, kept: our port ran the TF panel's rules only for a model
  with a mouthed Teeth material, reasoned as "nothing else reads them"; that held only because our panels flexed no mesh,
  and the engine flexes it with the same weights.
- **A CMDL's draw has no delayed weights.** `LockFlexWeights` is called with its third argument zeroed (`xor r9d, r9d`,
  `0x180937167`; the call `0x180937173`), and `DrawModel` is handed a null delayed pointer (`[rsp+0x28] = r15 = 0`,
  `0x1809371e8`; the call `0x1809371f2`). Taken to mean the delay is the weights themselves — *interpolated*: the null is
  read, studiorender's handling of it is not.
- **Which weights.** A CMDLPanel's (and its merges', and the StatTrak second-level merge's) are the rules at
  `CMDL::CMDL`'s zero controls; a CTFPlayerModelPanel's root's, the rules at its per-paint decay
  (`tf_playermodelpanel.cpp:2275-2361`); a `CModelPanelModel`'s, a C_BaseFlex's at rest (`c_baseflex.cpp:1220-1222`);
  an attached C_BaseAnimating's (`basemodelpanel.cpp:468`), `LockFlexWeights`' zeros (*source*, the last as B513 read it).
- **A C_BaseFlex's delayed weights are its own, and they start at zero — a review's catch.** The first port took every
  panel's delayed weights as its current ones. A C_BaseFlex with `flex_smooth` on hands the mesh `m_flFlexDelayedWeight`
  (`UsesFlexDelayedWeights`, `c_baseflex.cpp:1123-1126`; `SetupLocalWeights`, `:1308-1311`), zeroed whenever the model is
  set (`:203-213`) and moved toward the weights each frame by `RunFlexDelay` (`:1265-1279`) — the first draw blends
  nothing, since `m_flFlexDelayTime` is written only there (`:1310`). So a `CModelPanelModel`'s slow vertex animations
  rise from rest over its first frames; ported (*source*). Ours steps the delay once per panel draw (once a paint); the
  engine steps it once per draw of the entity — the same while a panel draws its model once a paint, *interpolated*. The world's resting C_BaseFlex has the same divergence, filed
  as B805. Read on the way: a C_BaseFlex whose model has no flex controllers draws zero weights, delayed or not —
  `SetupGlobalWeights` zeroes both and returns false before any rule (`:1198-1207`, `:1152-1155`) (*source*).
- **A `CModelPanel` sets its model up again at every new map.** It listens for `game_newmap` from its constructor
  (`basemodelpanel.cpp:56`) and marks itself dirty "to force the models to re-setup themselves" (:196-205), so the next
  paint builds a new `CModelPanelModel` from the new level's model (*source*). Ours had no such event and kept the old
  level's model, weights and all; ported. And the HUD's precache of the panels' models must not survive the level its
  models die with — a guard that remembered "this list is already packed" across a level change left the same list
  unpacked on the next map, drawing nothing (B779 review 2).
- **A model only a panel draws is packed like any other.** It was filed as drawing unflexed, the set having never packed
  it; the HUD precaches every panel's models into the world's model set each frame the list changes
  (`VguiHud.AddModelsToPrecache`, `FramePresenter.Hud`), and the precache packs them — so the panel's face has the buffer
  order it needs. *Read* for the HUD's route; the pack and the face through the production set *measured* on a synthetic
  model, and the frame's ask counted through the presenter since B779 review 3. The filing was a deduction never checked.
- *Not measured*: whether the flexed face closes any of the class portrait's deficit (B559) stays *interpolated*.

## The Eyes glint (B725, item 3)

The `Eyes` shader adds a 32×32 render target the studio render draws for every eye draw (`studiorender.dll`
`0x18001e020`, `0x18001f510`, *read*). Three things in it are not what the published shaders suggest.

- **The glints are placed in world units on a target one unit across.** `0x18001d600` writes each glint as
  `right · refl · cornearadius + dx` — world units on the eye — and the pass centres it at `(x + 0.5) · 32` texels, so the
  target spans ±0.5 units; `$glintu`, though, is `0.5 / radius` along the view's right, so the target is stretched over
  the eye's diameter. The two agree only for an eye of radius 0.5 (*read*, *arithmetic*). Ported as the engine does it.
- **The second glint is where the light is OUTSIDE the cornea's cap.** It reads like a back-of-the-iris highlight for a
  light "behind" the cornea; the branch (`comiss`, `jae` skips) draws it where `N · L` from the eye's centre is *below*
  `√(1 − ¼)`. Believed the other way round at first, from a summary written before the instruction was re-read.
- **The pass's quads sit a quarter texel inside the grid.** NDC `2X/32 − 0.984375` is a quarter texel, not the half
  that would align D3D9's pixel centres; under D3D9 it covers `floor − 1 … floor + 1` sampled at `i − ¼`, under D3D11's
  centres it would cover `floor − 2 … floor` (*arithmetic*). An earlier note said the two agree; they do not.
- **The sun is a light like any lamp to the glint.** Its `LightDesc_t`, read in `engine.dll`, has no attenuation terms
  and so gets `a0 = 1` from the same all-zero rule as a lamp (`0x1800f2186`), and a range of 1.8e19 whose square
  overflows to infinity and cuts nothing (*read*).
- **`r_glint_procedural` is a second picture, not a slower copy of the first.** The CPU regenerator (`0x18001d090`)
  uses the same five-Gaussian kernel but truncates the centre, evaluates each texel at its own coordinate, and adds in
  linear through mathlib's 2.2 gamma tables — so a dim glint the GPU pass stores as 14 it stores as 130 (*read*,
  *arithmetic*). A review's first answer was to ignore the cvar; it is user-settable, so it was ported.
- **`mat_wireframe` reaches the glint through `sv_cheats`.** The engine's `bWireframe` is `sv_cheats && mat_wireframe`
  (`0x1800f5855`), and under it the studio render skips the glint and draws the model with its own wireframe material
  — the second half is B788 (*read*).
