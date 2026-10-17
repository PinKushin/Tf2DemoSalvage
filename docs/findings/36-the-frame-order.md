# 36 — The order of a frame, and why ours was wrong for months

**Written 2026-08-26.** This one was found the way the owner has been saying to find them:

> *"all the bugs are guaranteed diffs to valve, i bet on it"*
>
> *"its just small random parity problems from where you think somethings to small to look at the
> sdk or decomp and try to reason through."*

The frame loop looked exactly like something too small to check. It is a list of eight calls. Two of
them were in the wrong place, and the reason nobody noticed is worth more than the fix.

**Evidence class: read from published source** throughout — `game/client/view.cpp`,
`game/client/viewrender.cpp`, `game/client/cdll_client_int.cpp` and `game/shared/igamesystem.h`.
Nothing here is interpolated.

---

## 1. What Valve actually does

The engine's per-frame walk over game systems is three hooks, declared together
(`game/shared/igamesystem.h:89-91`):

```cpp
static void PreRenderAllSystems();
static void UpdateAllSystems( float frametime );
static void PostRenderAllSystems();
```

The interesting part is that **they are not called from the same place.** Finding where each one is
invoked is what establishes the order, and the three call sites are in three different files.

| stage | called from | file |
|---|---|---|
| `UpdateAllSystems( frametime )` | `CHLClient::HudUpdate` | `cdll_client_int.cpp:1308` |
| `PreRenderAllSystems()` | `CViewRender::ViewDrawScene`, after `SetupCurrentView` | `viewrender.cpp:1411` |
| `PostRenderAllSystems()` | same, after `clienteffects->DrawEffects` | `viewrender.cpp:1469` |

So **simulation is not part of rendering.** `HudUpdate` runs before the view is built at all; by the
time `ViewDrawScene` computes a camera, the world has already been moved to the moment being drawn.

## 2. The camera and the ears come from one eye

This is the sharper citation, because it is not a matter of two distant call sites — it is four
consecutive statements in one function (`view.cpp:778-796`):

```cpp
// Compute the world->main camera transform
ComputeCameraVariables( viewEye.origin, viewEye.angles,
    &g_vecVForward, &g_vecVRight, &g_vecVUp, &g_matCamInverse );

// set up the hearing origin...
AudioState_t audioState;
audioState.m_Origin = viewEye.origin;
audioState.m_Angles = viewEye.angles;
audioState.m_bIsUnderwater = pPlayer && pPlayer->AudioStateIsUnderwater( viewEye.origin );

ToolFramework_SetupAudioState( audioState );
...
engine->SetAudioState( audioState );
```

`viewEye` is written once and read by both. There is no version of this where the listener can be a
frame behind the camera, because there is no opportunity for it to be: the same local supplies both.

**A second thing falls out of the same lines, and it settled an unrelated question.** Our listener's
right-vector is `(sin yaw, −cos yaw, 0)`, which had never been checked against anything. Valve's
`AngleVectors` (`mathlib/mathlib_base.cpp:936`) gives

```cpp
right->x = (-1*sr*sp*cy + -1*cr*-sy);
right->y = (-1*sr*sp*sy + -1*cr*cy);
right->z = -1*sr*cp;
```

which at roll zero (`sr = 0`, `cr = 1`) reduces to exactly that. **Pitch drops out of `right`
entirely when roll is zero** — `sp` appears only multiplied by `sr` — so ignoring pitch is correct
rather than a simplification. That is now a test, and it is the one test of the five that a plausible
future "fix" would break.

## 3. What we were doing

```
PlaySounds()                 <- listener, from the PREVIOUS frame's camera
FlyCamera(); UploadCamera()  <- camera for THIS frame
ProjectMap()/ReprojectScene()
AdvancePlayback()            <- the world moves to tick T+1, AFTER the camera was sent
TakeAutomaticShot()
BuildOverlay()
DrawFrame(...)               <- draws T+1's entities through T's eye
```

Two divergences, independent of each other:

1. **The listener was a frame stale.** Sound ran first, so the ears were wherever the eye had been
   last frame. By ear this is indistinguishable from a wrong panning law, which is why it survived
   the entire audio effort.

2. **The simulation ran after the camera.** Entities were drawn at T+1 through a camera built at T.
   And the viewmodel is worse than the world here, not better: `ShowMoment` passes
   `FirstPersonCamera()` into `MomentInfo`, and `ShowMoment` runs *during* the advance — so **the
   viewmodel camera was T+1 while the world camera was T.** The weapon and the world it is drawn
   over disagreed by a tick, every frame.

## 4. Why it survived

**A window cannot be asked what order it does things in.** Every stage had tests and every one of
them passed, because each tested a stage in isolation and the defect was entirely in the
relationships between them. This is the shape already recorded as
*three test levels, and the third is missing* — except that here even a UI test would not have caught
it, because the wrong output is a 15 ms disagreement that looks like ordinary interpolation.

The fix is therefore not "reorder three lines". It is **moving the order somewhere it can be
asserted**: `FrameSequence` + `IFrameSteps` in `Tf2DemoSalvage.Presentation`, with `MainForm`
implementing the stages and deciding none of them.

**The red step was run on purpose.** `FrameSequence` was first implemented with the *shipped* order,
and the three order assertions failed:

```
Failed Run_OverAFrame_FollowsTheEnginesStageOrder
Failed Run_OverAFrame_HearsFromTheEyeItJustPlaced
Failed Run_OverAFrame_SimulatesBeforePlacingTheCamera
```

while the timing, overlay hand-off and null-guard tests stayed green — correctly, since those are not
order claims. So the suite is known to detect this specific bug, rather than merely known to pass.

## 5. The half-correction that hid the other half

Worth recording because it is the more general lesson.

On 2026-08-25 `ReportSlowFrame`'s eight `long` parameters were replaced by a `FramePhases` record,
on the grounds that eight positional `long`s can be passed in the wrong order silently. That was
right, and it was **half of a correction**.

The other half sat untouched for another day: `FramePhases.Between` still took the eight cumulative
timestamps and subtracted adjacent pairs, so its *parameter list* was a second statement of the
frame's order —

```csharp
public static FramePhases Between(
    long frameAt, long soundedAt, long flownAt, long projectedAt,
    long advancedAt, long shotAt, long hudAt, long finishedAt)
```

— and reordering the stages without reordering those arguments would have relabelled every stall
column, quietly, reporting this fix as a performance regression in some other phase. Each phase is
now named at the call that produces it, so mislabelling is not a mistake that can be made.

**The general form: wrapping a call in a type stops the CALL being wrong. It does not stop the type
from encoding the same ordering fact a second time.** Ask what else knows the order.

## 6. Not yet judged by eye

The suites are green and the citations are unambiguous, but nothing here has been *looked at*. What
the reorder does to the picture is a question for the owner, per the standing rule that a UI claim
which cannot be verified by looking is a question and not a statement.

It is plausible that this contributes to some of B198's reports — the spy animation looping, the
door grates — and **it is not established.** Recording the temptation, since the tidy version of this
story would claim the fix and move on.

## 7. A render pass is not a frame: the corpse world was rebuilt every frame (2026-10-17)

**What the engine does** (read from source): `CPhysicsSystem::PhysicsSimulate` (`game/client/physics.cpp`) steps
`physenv` once a frame, at `SetSimulationTimestep( gpGlobals->interval_per_tick )` (`physics.cpp:177-180`) — one IVP step a
tick, independent of how many views or passes draw. A ragdoll is never re-created except by its entity.

**What we did.** `EntityModelSet.Instances` is called three times a frame in first person: the world pass, the
first-person player's own undrawn pass (B511), and the viewmodel pass. Every call ran `AdvanceCorpses` with ITS props —
and the two small passes carry no corpse. `CorpsePhysics.Advance` retires a corpse the moment no longer carries (that is
how a fade leaves the world), so each small pass retired every corpse, and the next world pass found a corpse born before
the world's tick and not running: "unreachable", rebuild, replay from the death.

**Why it hid.** It was filed as settling. The owner's f12 line read `corpses 13.3 ms over 60.6 steps`, and a corpse that
has just died is expensive, so the number looked like physics being physics. The number that gives it away is the STEPS:
at 66 ticks a second and ~38 frames a second the engine takes 1.7 a frame. The second clue needed a column that did not
exist — `advance` (all of `AdvanceCorpses`) against `corpses` (the step inside it): f12 at 30000 read `advance 48.7
(corpses 23 over 51.3 steps, 11 held)`, so half the corpse cost was not even stepping. Adding a rebuild count settled it
on gullywash_f9 with ONE corpse: `217.6 steps … 28 rebuilt` and `232.9 steps … 47 rebuilt` per hundred frames.

**After** (measured, same command): f12 `1.9` and `1` steps a frame, `1 rebuilt` in the window (the seek's own); gullywash
`8.1` (the seek's catch-up window), then `2.7` and `1.1`, `0 rebuilt`. `advance` 48.7/12.5/19.8 ms → 1.2/0.2/0.

**Parity.** Only the world pass advances now, which is the engine's one step a frame. A replay from the death is
deterministic and reproduces the continuous pose (`Instances_SeekingBackwards_RebuildsAndReplaysFromTheDeath` already
relies on that), so the pose drawn should not change — only the work. Held by
`CorpsePhysicsWiringTests.Instances_AnotherPassWithoutTheCorpse_LeavesItInTheWorld`.

**The rest of the residual, for the record.** `rest` (6–20 ms on f12) is now timed out to ~0.2–0.6 ms: `collate` ~1,
`baked` 3–4 ms in the window where static props are first seen and under 1 after (the colour mesh is cached per entity),
`place` 1–4, `undrawn` 1–4, `advance` as above. **`undrawn` is the one remaining piece of work Valve does not do**:
`C_BasePlayer::ShouldDraw` is false for the local player in first person, so the engine reaches his `SetupBones` only from
`AddDecal` (on demand — our decal queries already call `SetupBones` themselves) or a monitor's `ForceTempForceDraw`. We
pose and build instances for him every frame and discard them on any map without a camera target. Not yet removed: the
pose step would need to know whether a monitor draws this frame, which only the device knows today.

## 8. Where the rest of the frame goes, timed (2026-10-17, f12 at 30000, first person, Release)

**Removed: the first-person player's pose when no monitor can draw him.** The device answers `IViewerDevice.DrawsMonitor`
(`cl_drawmonitors` on and a world material binding `_rt_Camera` — `DrawMonitorPass`'s own first refusals); the undrawn
pass then simulates him and registers him in the scene, so `AddDecal`'s on-demand `SetupBones` still finds him, and builds
nothing. `undrawn` 2–3.2 ms → 0.1. Held by `Instances_UndrawnPassWhenPosingIs_BuildsThatManyInstances`.

**An instrument bug it exposed** (B243): `posed N` was read after the undrawn pass, which resets the counter, so "posed 5"
was always the first-person player and his cosmetics, never the world. Read before it now: **posed 22–25 against built
22–26** — one skeleton per posed prop, the readable-bone cache working, and the engine's own rate (one `SetupBones` per
drawn animating entity a frame). Nothing to remove there; "built 24–27 against posed 5" was the instrument.

**`place` is the flex weights** (measured, sub-timed): `place 1.8–5.7 = compose ~0, fade ~0.03, binds 0.13–0.53, matrix
~0.02, swap/tint/box 0.03–0.1, flex 1.6–4.9`. `FlexFor` runs only for drawn models with vertex animation — the engine
also computes flexes for drawn faces only (`C_BaseFlex::SetupWeights`, CPU vertex animation in studiorender) — so this is
not work Valve skips; it is ours done expensively (a delayed-weight comparison and, on any change, a full-vertex delta
array re-laid in buffer order). Filed, not changed. The collation's box is now carried to the instance rather than computed
again (`collatedBox`); it was not the cost.

**`collate` ~1 ms over ~1,860 props** recomputes every box every frame to detect movement; the engine re-links only on
`RenderableChanged`, which an entity raises when it moves, and never for a static prop. Filed: a static prop's box could be
filed once.

**`hud` 5–8 ms is paint, and paint is the tournament spectator GUI** (per element, `ChildPainted`): `specgui 3.0–3.6`
of which twelve `playerpanelN` at ~0.25 ms each — and of each card's 0.25 ms, its children (health cross, name, class
image) are ~0.02, so ~0.23 ms is the card panel's own background/border paint. `HudDeathNotice` 0.5–1.2, `HudChat` 0.35,
`HudCrosshair` 0.3, `HudMatchStatus` 0.3. Think, events, layout are each under 0.1. Not yet explained below the card.

**`camera` against the moment**: `camera` = view 0.03 + setcamera 1.0–1.4 (world cull) + pose 7–11 (the moment's pose plus
the viewmodel) + **particles 3.3–6.1**. The gap between `camera` and the moment cost is the particles and the world cull.

## 9. Below the timers: a profile (2026-10-17, dotnet-trace sampled, f12 at 30000)

The card's 0.23 ms of "own paint" was not paint. **`VguiScalableImageBorder.Paint` asked `DrawGetTextureSize`, and the
answer was resolved out of the VPK — a file open, `OSFileStreamStrategy..ctor` — every frame, for every card**: 40% of
`VguiHud.Frame`. `ISurface::DrawSetTextureFile` keys a texture by name and loads it once; `VguiTextureSizes` now does.
`hud` 5–8 ms → ~1; `specgui` 3.0–3.6 → 0.14–0.31. The wrong turn worth keeping: the per-element timer said "the card's
own background", and the background (`DrawBox`, three rects and four corners) was innocent — a timer names a span, a
profile names a call.

**`FlexFor`'s largest part was `FaceSources.Voices`** (39%): it replayed every sound an actor had made since the start of
the demo, each frame, per drawn face, to rebuild what the mouth holds. The mixer holds its channels and changes them only
when a sound starts or runs out (`MIX_MixChannelsToPaintbuffer`, B513), so the answer is now kept from the last such event
to the next. Held by a differential sweep against a fresh walk (`FaceSourcesVoicesTests`). `flex` 1.6–4.9 → 0.8–2.1. What
remains (`InBufferOrder`, `Deltas`) is per-frame face work the engine also does for a drawn face — filed, not changed.

**Particles are allocation-bound**: `PollGC` 28% and `BulkMoveWithWriteBarrier` 21% of `DrawOver`, `BurstEffects.Step` and
`ParticleEffects.Bursts` the largest callers. **The engine also sleeps a system it has not drawn**: `CNewParticleEffect`
sets `m_flNextSleepTime` to now + `m_flNoDrawTimeToGoToSleep` whenever it is drawn or distance-culled
(`particles_new.cpp:536`), and `CParticleMgr::BuildParticleSimList` skips one past that time with live particles unless
it moved (`particlemgr.cpp:1775-1791`). This viewer simulates every system every frame. Filed, not changed: it needs the
particle systems' bounds in the leaf system to know which were drawn.

**`collate` stayed ~1 ms after the box was filed once** (§8's suspicion, measured false): the remaining cost is the
per-prop bookkeeping — three tuple-keyed dictionaries hashing the model path — not the box.
