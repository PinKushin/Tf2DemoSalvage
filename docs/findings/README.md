# Findings — a reverse-engineering history of TF2's demo system

This folder is the **narrative record**: how each part of the format was worked out, what was
believed first, what turned out to be wrong, and which piece of evidence settled it. It is written
to be readable end to end and to be quotable in a write-up.

It is deliberately *not* a specification. The repository already separates those jobs:

| Document | Answers |
|---|---|
| `docs/SPEC.md` | **What the format is.** The current, correct description, by layer. |
| `docs/findings/` | **How we know, and what we got wrong.** Chronological, with the evidence. |
| `docs/RISKS.md` | **What is still open.** Numbered bugs and unknowns, each with its status. |
| `docs/DECISIONS.md` | **Why the project is built this way.** Engineering choices, numbered D1…. |
| `docs/TIMELINE.md` | **The era axis.** Which build shipped which protocol, and how that was dated. |
| `docs/verification/` | **What a command prints today.** Superseded numbers are replaced, not appended. |

**This table is the one copy** — `CLAUDE.md` and `docs/verification/README.md` point here rather than
restating it, because the version that lived in all three drifted apart by a row.

When a finding lands, the spec gets the conclusion and this folder gets the story. Neither should
restate the other at length — where detail lives elsewhere, these files link to it rather than copy
it, because a second copy is a copy that goes stale.

**The distinction that matters is findings against verification.** A finding keeps the wrong turn and
the evidence that killed it; a verification entry keeps only the number a rerun would produce, and
when the number changes the old one is gone.

## Contents

| File | Covers |
|---|---|
| [01-container.md](01-container.md) | The `.dem` envelope: header, command stream, `democmdinfo`, tick zero |
| [02-net-messages.md](02-net-messages.md) | The message layer inside `dem_packet`, and why it is a dependency chain |
| [03-string-tables.md](03-string-tables.md) | String tables, their history encoding, and the 64 KiB writer cap |
| [04-entities.md](04-entities.md) | `SendTable` flattening, delta decode, and the removal list |
| [05-user-messages.md](05-user-messages.md) | The user message layer: ids, layouts, and the era break in `Damage` |
| [06-protocol-eras.md](06-protocol-eras.md) | What changed at each protocol number, and how each boundary was measured |
| [07-writing-demos.md](07-writing-demos.md) | Re-encoding, byte-identical round trips, and generating demos the engine plays |
| [08-method.md](08-method.md) | The techniques that actually worked, and the ones that misled |
| [09-valve-implementation.md](09-valve-implementation.md) | What Valve's own engine and game code say — behaviours, not format |
| [10-maps.md](10-maps.md) | Reading a BSP: compressed lumps, and what counts as "the map" |
| [11-models.md](11-models.md) | The `.mdl`/`.vvd`/`.vtx` chain, static prop placement, and baked vertex light |
| [12-shader-parity.md](12-shader-parity.md) | Which material features the renderer still lacks, by drawn area |
| [13-settings-parity.md](13-settings-parity.md) | TF2's graphics options, and where ours stand against each |
| [14-playback-parity.md](14-playback-parity.md) | What TF2 gives you when it plays an STV demo natively |
| [15-detail-textures.md](15-detail-textures.md) | The `$detail` chain: twelve combine modes, and three traps in them |
| [16-bumped-lighting.md](16-bumped-lighting.md) | `$bumpmap`: four lightmaps per face, and why set 0 is not one of them |
| [17-translucency.md](17-translucency.md) | What makes a surface see-through, and why `$alphatest` cancels `$translucent` |
| [18-decals.md](18-decals.md) | Overlays: a basis smuggled through unused corner components, and the published depth bias |
| [19-model-indices.md](19-model-indices.md) | Where a networked entity's model comes from, and the packing protocol ≤ 20 applied to it |
| [20-interpolation.md](20-interpolation.md) | Turning stored keyframes into motion: three quantities, three rules, one gap left open |
| [21-player-animation.md](21-player-animation.md) | The demo carries no animation state, and the four things that had to be right before a player moved |
| [22-bone-merged-attachments.md](22-bone-merged-attachments.md) | Why weapons and cosmetics carry no position of their own |
| [23-drawing-what-the-entity-says.md](23-drawing-what-the-entity-says.md) | Defects that looked like renderer bugs and were this project overriding what the demo already said |
| [24-reference-capture.md](24-reference-capture.md) | Configuring TF2 so a screenshot is a valid ground truth: why capture must be LDR, and what drifts |
| [25-gesture-layer.md](25-gesture-layer.md) | How a demo says a player fired: a temp-entity trigger, and the enum ordinal that turned out to be append-only across every era |
| [26-material-modulation.md](26-material-modulation.md) | `$color` and `$alpha`: a correct citation that produced a wrong implementation, and a coverage instrument that accused the code twice in the same shape |
| [27-cubemap-placement.md](27-cubemap-placement.md) | A struct three bytes bigger than its declaration, and ten synthetic tests that could not catch it because the fixture was built from the same belief |
| [28-vertex-colour.md](28-vertex-colour.md) | A census's largest remaining entry that turned into an open question when measured, and the half of it that was already implemented |
| [29-full-screen-focus.md](29-full-screen-focus.md) | Full screen took the foreground and lost the focus, and why a rectangle assertion cannot see it |
| [30-viewmodel-drawing.md](30-viewmodel-drawing.md) | The viewmodel packs, poses, draws, and is not on screen |
| [31-game-audio.md](31-game-audio.md) | What a demo asks the sound system for, and what the game actually ships |
| [32-the-opaque-pass-blend-leak.md](32-the-opaque-pass-blend-leak.md) | Every static prop was alpha-blended for two days, and it read as four separate art faults |
| [33-the-rocket-lives-in-the-arms.md](33-the-rocket-lives-in-the-arms.md) | The rocket lives in the arms, and nothing can hide it |
| [34-a-carried-weapon-is-two-models.md](34-a-carried-weapon-is-two-models.md) | A carried weapon is two models, and `m_nModelIndex` is the wrong one |
| [35-the-bone-pipeline-audit.md](35-the-bone-pipeline-audit.md) | How far the pose loop diverged: the engine has no ordering step, and 177 of 200 lines are not pose work |
| [36-the-frame-order.md](36-the-frame-order.md) | The camera and the ears come from one `viewEye`; we drew tick T+1 through tick T's eye |
| [37-the-engines-demo-vocabulary.md](37-the-engines-demo-vocabulary.md) | `demo_*` declarations read from `engine.dll`: most are ConCommands, `fps_max` is 400 and unarchived, `engine_no_focus_sleep` is 50 and archived |
| [38-which-players-can-be-shown.md](38-which-players-can-be-shown.md) | TF2 OVERRIDES `CanPlayerBeSeen` and drops the origin check; `mp_forcecamera`, `userid` vs `entindex`, and a correction to a Valve citation |
| [39-the-engines-frame-clocks.md](39-the-engines-frame-clocks.md) | Six engine clocks and which of ours maps onto each; `CalcDemoViewOverride` flies by `absoluteframetime`, and `cl_showfps` reads the same quantity |
| [40-the-game-ships-its-own-cvar-list.md](40-the-game-ships-its-own-cvar-list.md) | `tf/cvarlist.log`: 3,668 convars with defaults and flags, in plain text — a cheap instrument for finding 37's job, and the impossibility claim it found still standing in our code |

## Which of this is original

Worth separating, because the two categories carry different weight in a write-up.

**Transcribed** — read out of published source and verified against the corpus. Most user message
layouts, the bit-level primitives, the protocol change *list*. Valuable as confirmation; not a
discovery. The contribution here is that it has been checked against real files across five eras.

**Original, as far as this project can establish** — not in `proto_version.h`, not in the SDK, not
in any public writeup found:

- **Dates for the protocol boundaries.** Valve publishes what changed, never when. Protocols 11,
  14, 15, 16 and 24 are now pinned to exact build dates by running period clients
  ([06](06-protocol-eras.md)).
- **The cadence of change** — three protocol bumps in the first five months, then two in three
  years, then eight in under two, then thirteen years frozen. This has consequences for where to
  hunt for the missing specimens.
- **The `Damage` user message layout below protocol 15** ([05](05-user-messages.md)).
- **`dem_stringtables` is absent at protocol 14 and below** — an era difference `proto_version.h`
  does not mention.
- **The writer's 64 KiB schema cap**, established by comparing a POV and a SourceTV recording of
  the same session ([03](03-string-tables.md)).
- **How `bf_write` behaves under pressure in practice**, and what that means for anyone
  re-encoding ([09](09-valve-implementation.md)).
- **`PlayerAnimEvent_t` is append-only across TF2's whole history** — the gesture-trigger enum's
  ordinals 0–29 are identical from the Orange Box era to today, so one event→gesture mapping decodes
  every protocol. Established by comparing three SDK generations, not assumed ([25](25-gesture-layer.md)).
- **A corpse's whole appearance is seven bits** — `DT_TFRagdoll` is `NOBASE` and sends no model, no
  skin and no body; the client rebuilds all three from a 4-bit class and a 3-bit team. The model
  paths are not in Valve's code at all but in shipped script files, so a viewer without the game
  installed genuinely cannot know them ([48](48-a-corpse-describes-itself-in-two-integers.md)).
- **A hat removes the head it replaces, and the number it edits is mixed-radix** — 747 shipped items
  switch off a part of the player they are worn on, resolved by NAME against the wearer because one
  hat serves nine class models with different parts. Adding each item's contribution is correct until
  two items name the same part, which 457 of them do; then the digit overruns and a *different*
  piece vanishes ([49](49-a-hat-removes-the-head-it-replaces.md)).
- **The map scatters its own grass, and it is nearly thirty times the size of the prop lump** —
  `dprp` holds 28,699 objects on harvest against about a thousand static placements, compiled by
  `vbsp` from a `%detailtype` key rather than placed by anyone. Its quad reads backwards three ways
  and all three are Valve's; almost every sprite ROLLS, because a non-upright detail's orientation
  is built from the ground's surface normal. Then six correct counts reported success for a draw
  call that was never issued ([50](50-the-map-scatters-its-own-grass.md)).
- **Source's physics is IVP, and `vphysics.dll` says which source file every function came from** —
  the assert strings carry full build paths, so `ivp_intern\ivp_friction.cxx` names a set of
  addresses. `CPhysicsEnvironment::Simulate` clamps its timestep and simulates NOTHING outside the
  clamp; `IVP_Environment::simulate_dtime` is one line into the time manager. RTTI is nearly absent
  and a naive vtable scan finds Control Flow Guard's table instead
  ([51](51-vphysics-is-ivp-and-it-is-readable.md)).
- **A particle plays a sheet, and its clock is `age × 3` rather than a life fraction** — `CSheet` is
  forward-declared and defined nowhere, so the payload was measured off `smokelit.vtf` with tiling as
  the control: four sequences that are four permutations of the same five 128×128 tiles, agreeing
  with the `.pcf`'s own `sequence_max = 3`. `BLENDFRAMES` defaults to on, so the crossfade is not
  optional. Then a probe's `Math.Min(frames, 4)` got written down as an open question about the
  format, and a midpoint taken "because the two bounds are equal" turned out to be reading 0.8 and
  1.2. Then the sheet's own PRESENCE turned out to be moving the pixels: a 7.3 VTF says where its
  image data is and the reader computed it, so 288 shipped effects decoded to rainbow noise that kept
  its silhouettes — mips are stored smallest first, so the largest one barely shifts
  ([52](52-a-particle-plays-a-sheet.md)).
- **A taunt names its own sequence inside a compiled scene** — `SaveToBinaryBuffer`'s top-level event
  list holds only the events with NO actor (`choreoscene.cpp:3711`), so a walk that reads it finds
  nothing for every taunt in the game; the gestures are down the actor → channel → event tree.
  `StartGestureSceneEvent` then resolves the parameter with `LookupSequence`, matching a sequence LABEL
  and not an activity, and `StopGestureSceneEvent` clears the slot only for a scene containing a LOOP
  ([53](53-a-taunt-names-its-sequence-in-a-scene.md)).
- **A variable owns its own interpolation history, and the prune keeps two STALE entries** — the count
  of histories is the count of `AddVar` registrations, and their changetimes are their latch group's
  clock, which disagrees with the other group's by over eight ticks on 95.5% of updates carrying both.
  `RemoveEntriesPreviousTo` keeps `Truncate( i + 3 )`, so a spline's third sample can be seconds old and
  `TimeFixup2_Hermite` will happily extrapolate it — `Lerp( 1-200, 600, 584 ) = 3784`, putting a closing
  door at 579.085 against a shut height of 584, **in the engine**. A "hermite window" had been built on
  the opposite belief and was this project's rule, not Valve's. What the engine really refuses is a
  sample it has not RECEIVED, which is structural in a live client and therefore invisible as a rule
  ([54](54-a-variable-owns-its-own-history.md)).
- **A visible entity holds a render handle, and "visible" is at least three different questions** —
  `IsVisible()` is one inline line, `m_hRender != INVALID_CLIENT_RENDER_HANDLE`, granted by
  `UpdateVisibility` from the render mode, the model, `EF_NODRAW` and the index. No frustum, no PVS, no
  previous frame, no skeleton. One set stood for it here, was filled at BONE SETUP, and served both
  `ShouldInterpolate` and the corpse fade — which wants `IsRagdollVisible`, a live `CullBox`. Result: no
  brush entity was ever interpolated in any map, so no door anywhere ever moved smoothly, and on any
  first-person frame the viewmodel pass cleared the set and dropped the whole world off both lists. The
  belief that held it up was a sentence repeated in four files and quoted from nowhere: *"`IsVisible()`
  is the LAST render's answer … it is what Valve does."* A latency claim about *when* a predicate runs
  had concealed the question of *what* it tests
  ([55](55-a-visible-entity-holds-a-render-handle.md)).
- **The item names the model, and no track does** — `CEconEntity::UpdateModelToClass` lets an item's
  `GetPlayerDisplayModel` win over the networked one, so the path that reaches the renderer appears on
  no track, while the asset load list is built by walking tracks. The two can never meet: the loader
  reported `ASKED FOR 166; HAVE 166; MISSING 0` and was telling the truth, while thirteen cosmetics a
  frame packed no geometry, because `MapAssets.Geometry` is a dictionary lookup and a path never in the
  list has no key to miss. The diagnosis was one extra clause in a census — *no track names it* —
  which separates "the walk is wrong" from "the path is derived elsewhere". **And 86% of the original
  count was the instrument**: the census hand-rolled a subset of `MomentScene.Build` without
  `WeaponModels.Resolve`, so every item-resolved weapon read as having no model
  ([56](56-the-item-names-the-model-and-no-track-does.md)).
- **A hitscan shot is a seed** — `DT_TEFireBullets` sends no bullet paths at all, so every pellet's
  direction is rebuilt by reseeding Valve's own RNG per bullet. Which meant reading
  `CUniformRandomStream` out of `vstdlib.dll`'s disassembly, because `random.h` declares `m_idum`,
  `m_iy` and `m_iv[NTAB]` and not one constant. The priming loop runs forty advances and not
  thirty-nine, and starting one lower gave a generator that passed every self-consistency test in the
  suite while disagreeing with the shipped DLL on **1,767 of 1,800 draws** — only an in-process oracle
  found it ([57](57-the-shot-is-a-seed.md)).
- **An explosion sends a normal, not a trace** — so "went off in mid air" costs no ray cast: it is
  `fabs(x) < 0.05 && fabs(y) < 0.05 && fabs(z) < 0.05`, and the send table proves Valve's comment
  literal, since six bits over `[-1, 1]` cannot represent zero at all. **And the sentinel is 65535**:
  `INVALID_STRING_INDEX` is `(unsigned short)-1`, so reading it as −1 made 2,492 of one match's 2,786
  explosions claim a custom particle effect. The true figure is zero, and the wrong one would have
  made a string table nothing uses into the priority
  ([58](58-an-explosion-sends-a-normal-not-a-trace.md)).
- **A tracer is a counter and two control points.** One static `tracerCount` for the whole client decides
  which bullet of a pair draws, so a demo's tracers can only be answered all at once, in fire order.
  The particle's life is the trip's length over its speed, read out of `client.dll`, whose start-offset
  branch writes y and z into the neighbouring particles
  ([59](59-a-tracer-is-a-counter-and-two-points.md)).
- **A bullet hole is a translated name.** The world asks for `"Impact.Concrete"` whatever it is, and a table in
  `decals_subrect.txt` swaps it for the struck surface's group. The surface's material is set per texdata at load, and
  a texture with no `$surfaceprop` impacts as surface zero rather than nothing. The first holes sat in dark squares
  because the engine's mod2x multiplies gamma values and this renderer blends linear ones
  ([60](60-a-bullet-hole-is-a-translated-name.md)).
- **Debris is the old particle system.** `cl_new_impact_effects` defaults to 0, so dust, flecks and sparks are the
  hand-written emitters, not PCF. Their colour is a face's average light — `r_avglight` is 1 — times its texture's
  thumbnail, read out of three closed functions. On the way: every finished burst was being replayed every frame
  ([61](61-debris-is-the-old-particle-system.md)).
- **An effect without a class is a delta.** Within one message, an effect that repeats the previous effect's class
  sends only what differs from it. So a sentry's muzzle flash, sent right after its tracer, omits the sentry and the
  attachment. Reading the wire list as the whole effect had given every such flash entity 0
  ([62](62-an-effect-without-a-class-is-a-delta.md)).
- **An impact sound is a frame's list.** vphysics merges a frame's impacts by surface before it plays them, and a frame
  can cross several ticks, so the number of sounds depends on the frame rate. TF2's reference ran at about 16 fps. Four
  defects were in the way, one of them a friction solve that treated every body as one kilogram
  ([63](63-an-impact-sound-is-a-frames-list.md)).
- **The HUD is VGUI, and half of it is closed.** The controls are published, but the panel tree, scheme, surface, fonts
  and localisation live in `vgui2.dll` and `vguimatsurface.dll`. Among the findings: a localisation conditional's `!`
  is read in two places and one never sees it, and an empty scheme paints the screen white
  ([64](64-the-hud-is-vgui-and-half-of-it-is-closed.md)).
- **An unbaked static prop is lit once, not every frame.** A model compiled `$staticprop` with no `.vhv` gets a
  colour mesh the engine lights on the CPU at load, from the handle's static state, which holds no styled light — so
  the engine's own "rebake" of it reproduces the same colours ([65](65-an-unbaked-static-prop-is-lit-once.md)).
- **TF2 barely uses dynamic lights.** Explosions, muzzle flashes and burning players allocate none — the explosion
  light is commented out in the SDK and TF2 overrides every muzzle-flash elight away. The one allocator a match
  reaches is the recorder's own Dragon's Fury fireball, and 19 demos drove none
  ([66](66-tf2-barely-uses-dynamic-lights.md)).
- **A baked static prop keeps its colours under a changing light; the light is added on top.** The first port
  dropped the colours for full lighting whenever a nearby style animated — a branch of the model draw that only
  non-DX9 hardware takes. On DX9 the colours stay and the handle's styled and dynamic lights are summed onto them in
  the shader ([67](67-a-baked-prop-adds-its-lights.md)).
- **A POV demo's view is interpolated by the demo player, between packets it parses ahead for.** The engine keeps a
  window of packets from 32 ticks back to the first more than 8 ahead, slerps between the pair around the rolled-back
  tick, and snaps on a cut, a fast move, or a stop in the window. The recorder's body is placed and animated from the
  same viewpoint ([68](68-demo-view-interpolation.md)).
- **The view's fog is the local player's, and a model's cube holds every light it has no slot for.** Fog comes from the
  controller the player's `m_PlayerFog.m_hCtrl` names, the 3D sky's from the player's own `m_skybox3d.fog`, and the
  server forces radial fog on official maps. The lightcache ranks lights by luminance and folds every loser into the
  ambient cube rather than dropping it ([69](69-fog-is-the-players-and-a-cube-holds-the-rest.md)).
- **A translucent surface belongs to the leaf the world walk is in when it reaches it.** `R_DrawSurface` files it
  under the world list's newest entry, so a node's glass goes with the last leaf of the near subtree and glass facing
  away is never filed at all — which the LEAFFACES stand-in submitted
  ([70](70-a-translucent-surface-joins-the-leaf-the-walk-is-in.md)).
- **Water is up to four views, and the shader's LOD lives in a material proxy.** The client decides per frame which
  water views run from one material's parameters; the engine picks the visible volume by walking the tree from the
  eye; the `WaterLOD` proxy hands the view's cheap-water distances to every water material it binds, so the cheap
  pass covers the expensive one as the distance runs out ([71](71-the-water-views.md)).
- **A renderable lives in the leaves it touches, across frames.** The client leaf system re-links only what moved,
  collates the view's leaves in order and fills the size buckets as it goes, so the draw side never sorts. A box
  touching a plane goes in both sides' leaves, which neither of this project's earlier walks did
  ([72](72-a-renderable-lives-in-the-leaves-it-touches.md)).
- **Beams, trails and ropes are strips the client builds, and two of the three have NOBASE tables.** `DT_Beam` and
  `DT_RopeKeyframe` declare their own model index, so a gate that read `DT_BaseEntity`'s dropped every one. A beam is
  rebuilt from the entity each frame, and its strip comes from `CBeamSegDraw`, which is closed and was read in
  disassembly. A trail is points the client samples from where its projectile is drawn, and a rope is a Verlet chain
  the client hangs between two entities. No demo in either corpus carries a temp-entity beam
  ([73](73-beams-trails-and-ropes-are-strips-the-client-builds.md)).
- **A cloak is a second pass, and a screen overlay is a slot with a history.** A cloaked spy is his own materials with
  `$cloakpassenabled`, warping a copy of the frame drawn before him, the standard pass dropped past a factor of 4/9;
  nobody but an enemy ever reaches full invisibility. The overlay is one variable the condition hooks write and only
  clear when it holds their own material — and the spy's own cloak has none
  ([74](74-a-cloak-is-a-second-pass-and-an-overlay-is-a-slot.md)).
- **A TF2 face is the scene, not the wire.** A player sends no flex weights and no blink, and TF2's eyelid rules never
  read the blink controller; taunt and voice scenes move faces through `EXPRESSION` events and their `.vfe` settings,
  run through the model's flex rules and added to position, normal and tangent before skinning
  ([75](75-a-tf2-face-is-the-scene-not-the-wire.md)).
- **The golden comparison runs.** Real TF2 driven to a tick through the MCP and the viewer's `--shot`, diffed per
  region with masks taken from TF2 itself: two TF2 captures of one tick agree to under 1/255, a 5° camera error costs
  35. Against that, outdoors we are 25-35% darker, a left-handed player's viewmodel is on the wrong side, and the STV
  spectator HUD is a different HUD. Second pass: `demoui` prints TF2's tick, a listen server gives exact empty
  cameras, and TF2's own switches isolate exposure and bloom — TF2 sits at 0.70 outdoors and 0.55 indoors where a
  `--shot` is at 1, our bloom adds 1.4-2.1× TF2's, the sky ignores the scale, and our world view is 90° across where
  TF2's is 106° ([76](76-the-golden-comparison.md)).
- **The sun is two overlays asked at the far plane.** `env_sun` is a NOBASE table with no origin; the client draws a
  core and a six-times-larger glow a hundred units in front of the eye, each gated by its own occlusion query sized
  in SCREEN space at `zFar * 0.999`. Only the main view and monitors draw overlays at all, and a monitor's query is
  never issued, so it sees no sun ([77](77-the-sun-is-two-overlays-at-the-far-plane.md)).
- **The stock HUD against TF2's own.** Every difference the owner saw was under the HUD element, not in it: a
  `$distancealpha` material drawn as a rectangle, a team event compared against a state already stored, a sequence looked
  up in the root model instead of the virtual one, and VGUI colours written to the sRGB view without `GammaToLinear`.
  The ammo number's shadow is still painted over it, and the panel tree does not explain why
  ([78](78-the-stock-hud-against-tf2.md)).
- **A dormant effect stops emitting; an undrawn one sleeps.** In `particles.obj`, `SetDormant` is
  `StopEmission`/`StartEmission` and `Simulate` never reads the flag. The pause belongs to the sleep clock, which
  every `Render` pushes forward. Missing that sleep is what replayed effects during plain playback, about 28,000
  ticks each time ([79](79-a-dormant-effect-stops-emitting-and-an-undrawn-one-sleeps.md)).
- **A map keeps what Valve uploads and frees.** f12 keeps 833 MB of managed memory per map after upload, a small part
  of the viewer's 7.6 GB. 329 MB is model vertices, baked once per animation frame for unskinned models, where the
  engine poses one bind pose. 239 MB is every particle definition, held as a DMX tree per map, where the engine
  types them once per process. About 127 MB is texture pixels the device already holds. A `CTexture` keeps no
  texels after `ReconstructTexture` (disassembled) ([80](80-a-map-keeps-what-valve-uploads-and-frees.md)).
- **A map brings its own particles.** `ParseParticleEffectsMap` tries three manifests and uses the first that loads:
  pak `particles.txt`, then pak `maps/<name>_particles.txt`, then the GAME path's copy. It accepts files only from
  directly inside `particles/`, keeps the first 64 accepted, and runs after the stock reload. A repeated name is
  replaced, not kept, with case folded (disassembled, `AddParticleSystem`), so the map's definition wins
  ([81](81-a-map-brings-its-own-particles.md)).
- **The frame outside particles.** `--measure` had counted the opening seek: about 8 of its 20 seconds on f12 were
  three frames of about 2 s each, so the "38–41 ms" frame was 23 ms, and the HUD suspect was the seek. Release is no
  faster than Debug (Debug is compiled optimized). The collector pauses 2.7 ms a frame recovering 11.6 MB allocated a
  frame, where Valve allocates nothing per frame ([82](82-the-frame-outside-particles.md)).
- **The per-frame scope plan.** Each of Valve's per-frame client loops, by the set it walks (every entity, a list,
  the changed, the visible), against ours. Valve walks every entity only in `SimulateEntities`, and static props are
  never on it; we walk every prop, static props included, in five passes. A plan, ordered by milliseconds per risk
  ([83](83-the-per-frame-scope-plan.md)).
- **The first seconds and the periodic stalls.** The ramp over the first seconds of playback is tier-0 code: the
  tiered-JIT worker was the one thread busy early. Turning quick JIT off cost the package 46 s of load and about 17%
  of steady frame rate, so it was reverted, and the ramp is open. The stall every few seconds was a player's every
  past rocket-jump smoke replayed in one frame when he was first posed after a seek, plus surface sounds the precache
  missed. Frames over 100 ms per minute went from 12 to 1. No gen2 collection was involved
  ([84](84-the-first-seconds-are-tier-zero.md)).
- **The load time was an enumerator.** D217 step 3 costs no frame time. The 55 s it added to f12's build was
  `PropertyBag`'s iterator-method enumerator, walked by every entity's pattern scans, where the dictionary it replaced
  had a struct one. A struct enumerator takes the build from 170 s to about 100 s, with every seek identical and the
  heap unchanged ([85](85-the-load-time-was-an-enumerator.md)).
- **The camera phase, ranked.** In steady seconds, `camera` is 9.4 ms of a 14 ms first-person frame. The part no
  counter named was the burst search: every stopless entity particle fired since the demo began was re-offered each
  frame, 434 at tick 30000 and 1,169 at 90000, where Valve stops updating an effect once it is finished. A capturing
  lambda in the IK clear was the largest single allocation. Both are fixed, and `camera` is now 8.5 ms. Frames over
  100 ms: 1 per minute in two runs of three (the frame that ends the seek), with one unexplained run of 9
  ([86](86-the-camera-phase-ranked.md)).
- **The sound stall was a blocked read.** The ~395 ms "decoding" stalls were a synchronous VPK read on the render thread
  that ran no CPU: 0.2 ms on a quiet machine, ~395 ms on a busy one. They were a viewmodel's animation-event sounds,
  which the engine precaches with the model (`ModelSoundsCache`) and we never did. Misses per minute on f12 went
  12 → 4 ([87](87-the-sound-stall-was-a-blocked-read.md)).
- **Bytes a frame, and the 'between'.** `--measure` now reports the render thread's bytes a frame. The 1.5 ms `between`
  was a 1 ms spin waiting for UI Automation traffic, run every frame because the transport bar's own repaint ended every
  render burst. Bone poses now build in storage each skeleton owns, as `CBoneSetupMemoryPool` does. The render thread
  went from 4.0 to 2.2 MB a frame and every heap from 7.7 to 5.1; the first-person frame went from 16.5–17.1 to
  14.5–15.3 ms, and the empty view from 7.3 to 5.6. With a heartbeat thread beside each stall, none of the remaining
  100–190 ms stalls was a process-wide stop ([88](88-bytes-per-frame-and-the-between.md)).
- **The struct copies were the collector.** `BulkMoveWithWriteBarrier`'s 17% of the render thread had no leaf time of its
  own. It was a GC poll site, so the wait for the collector landed there. Three allocations behind that collector are
  gone: a sorted copy of the roster six times a frame, three arrays per terrain triangle in the sun trace, and an empty
  dictionary per activity lookup. The render thread went from 1.85–1.89 to 1.20 MB a frame over 60 s, and frames over
  100 ms from 2–7 to 0 in two clean runs ([89](89-the-struct-copies-were-the-collector.md),
  measurements/2026-10-21-struct-copies-and-allocations.md).
- **A first sound play is loaded off the frame.** A play-time miss is read and decoded on the thread pool, the shape of
  the engine's deferred mixer path, and the sound starts late when it arrives, as the engine's waiting channel does. On f12, decodes in play
  went 5 → 0 and the same five were loaded off the frame; a second precache lists only new names, 12 not 1,394
  ([90](90-a-first-sound-play-is-loaded-off-the-frame.md), measurements/2026-10-21-sound-first-play-off-frame.md).
- **A fix that the caller undid.** Seven more allocations are gone: per-search motion caches, per-frame layer lists,
  placement matrices, a heal-beam closure built for every beam in the demo, and the HUD's vertex array. The corpse copy
  needed a second fix, because the corpse record re-fed each original every tick, so the first fix never fired. The
  render thread went from 1.21-1.26 to 0.94-1.01 MB a frame and every heap from 2.50-2.98 to 1.54-1.83. The effect on
  stalls is inconclusive on a loaded machine ([91](91-a-fix-that-the-caller-undid.md),
  measurements/2026-10-21-corpse-and-beam-allocations.md).
- **The steady frame is the processor's.** The viewer's own GPU engine is 6-10% busy on f12 first person; the frame is
  CPU work. A sampled `PollGCWorker` leaf is mostly the sampler's own suspension, not the collector, which reopens finding
  89's reading. A talking face no longer rewrites the two thirds of its flex stream no flex reaches; no output changes
  ([92](92-the-steady-frame-is-the-processors.md), measurements/2026-10-22-f12-steady-frame.md and 2026-10-22-flex-spread.md).
- **The camera phase is a long tail.** No part of `camera` is above 0.8 ms. The model decals no longer walk the whole match
  each frame (0.53 → 0.19 ms). The dynamic-light merge never runs on f12, and a struct copy's sampled share proved not to
  be its cost. The finding ends with a design note on which parts could run on other threads with identical output
  ([93](93-the-camera-phase-is-a-long-tail.md)).
- **Threading the particles saved about a seventh of the estimate, and bone setup cannot be threaded as it stands.** The played
  bursts step on workers with byte-identical output (step 0.36 → 0.28 ms); the threaded build does not move. Static
  pose counters, cross-tree follow chains and the draw loop's reach block threaded bone setup (the model caches, first
  named as the blocker, turned out to be concurrent already) ([94](94-threads-buy-little-of-the-camera.md),
  measurements/2026-10-22-threaded-particles.md).
- **The exposure histogram is one occlusion query a frame over seventeen entries, and the goal is pushed every frame.**
  Replacing the processor's read-back with the engine's shape moved the scale by at most 0.27%, only after a reset, and
  the frame by about 1% ([95](95-the-histogram-is-seventeen-queries.md), measurements/2026-10-22-exposure-queries.md).
- **No single routine earns native code yet.** No render-thread self time is above 3.2%, and arithmetic the engine also
  does is at most 1.3-2.3 ms of 12 ms. Native code is estimated at 0.1-0.2 ms beyond a C# span/SIMD rewrite. C# first,
  benchmark-gated ([96](96-where-native-code-could-help.md), from the T1 trace in
  measurements/2026-10-22-f12-camera-breakdown.md).
- **TF2 draws on a second thread and we draw on one.** It ships `mat_queue_mode -1` and queues material-system calls
  to a render thread; we have only its mode 0 (B581). Only `draw` can move, not `hud`: an estimated 1-1.5 ms of 13.4,
  and possibly one frame of latency, still to be read in `materialsystem.dll` ([98](98-the-queued-material-system.md)).
- **TF2's queue is one frame deep and holds no frame.** In `materialsystem.dll`, `-1` is mode 2 on 2+ physical cores;
  `EndFrame` waits for the previous frame's play, swaps two queued contexts and hands the new one to a one-thread
  `MatQueue` pool, which presents as soon as it finishes; `Lock` is a full drain ([99](99-the-queue-in-materialsystem-dll.md)).
- **The engine does the same work for less, not less work.** Stage by stage against the engine's frame, the places it
  caches, skips or waits for a change and we do not hold at most 1.8-2.1 ms of a 12 ms frame (bursts, visibility
  filters, the HUD event feed, detail props; B578-B581), estimated at 0.6-1.2 ms to fix. The unsplit `draw` phase
  (2.1 ms) is the one stage not seen into ([97](97-the-per-frame-pipeline-against-the-engines.md),
  measurements/2026-10-22-f12-camera-breakdown.md).
- **The viewer's busy helper threads were the opening, not the frame.** The 30-36% pool worker is the one-off corpse record
  pass and the 15-16% runtime thread is tiered JIT; both fall under 1.5% after the first ~10 s of playback, leaving the render
  thread and the driver at 114-116% of a core, and no gen2 collection ran
  ([100](100-the-helper-threads-were-the-opening.md), measurements/2026-10-22-viewer-helper-threads.md).
- **Managed glue is one layout choice, not the gap.** On real soldier inputs, reading a large bone struct through
  `IReadOnlyList` per bone is 62% of the ordinary bone blend (flat arrays 2.6x faster, bitwise equal, at the C# floor)
  and 7% of the additive one, which never reads it. A flat pose is estimated (a ceiling) at about 1 ms of a 6-8 ms
  gap; native code is not indicated
  ([101](101-where-the-main-thread-goes.md), measurements/2026-10-22-pose-blend-glue.md).
- **The empty view's floor is mostly work the engine also does.** Of ~5 ms with nothing in view, the one large whole-set
  walk the engine skips was building every entity's layers, autoplay and IK rules outside `SetupBones`
  (`c_baseanimating.cpp:1953`, `:2796`). It now runs at bone setup: `simulate` 0.5-0.6 → 0.3-0.4 ms. Attachment bone
  setups and effect control points are parity (`:2094`, `particlemgr.cpp:1841`)
  ([102](102-the-empty-view-floor-stage-by-stage.md), measurements/2026-10-23-blend-at-bone-setup-f12.md).

## Conventions used throughout

**Every claim states its evidence class.** These are not equal and the difference has repeatedly
mattered:

- **Read from published source** — `ValveSoftware/source-sdk-2013`. The strongest, because it
  states intent rather than implying it. Never a decompile; see `CLAUDE.md`.
- **Measured on the corpus** — observed across real demos, with counts.
- **Arithmetic** — ruled in or out by bit widths and lengths before any byte was read.
- **Differential** — agreement or disagreement with `demostf/parser`.
- **Interpolated** — believed but not exercised, because no specimen exists. Always flagged.

**Wrong turns are kept, not tidied away.** A conclusion recorded without the reasoning that failed
is the kind that gets confidently repeated. Several sections below exist mainly to record a wrong
belief and what killed it.

**Dates are absolute.** "Recently" ages badly in a document meant to be read later.
