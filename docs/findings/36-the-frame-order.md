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
