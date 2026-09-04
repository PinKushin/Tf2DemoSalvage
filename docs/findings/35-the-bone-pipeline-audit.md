# 35 — How far the pose loop diverged, measured against the SDK

**Written 2026-08-24.** The owner, twice:

> *"that loop just reeks and i dont trust how far its deverged from valves implementation"*
>
> *"a loop of 150 lines is a huge smell, wtf does a loop need 150 lines of code for?"*

This is the audit that answers both. It is B182's denominator, read out of
`game/client/c_baseanimating.cpp` and `game/client/bone_merge_cache.cpp` before looking at our code,
so the engine's stage list is not contaminated by what we happen to have built.

**Evidence class: read from published source**, except where a line is marked *measured* (a count
taken from our own tree) or *unverified* (a structural claim not yet seen on screen).

---

## 0. The size, exactly

The complaint said 150 lines. *Measured* on `managed/Tf2DemoSalvage.Scene/EntityModels.cs`:

| region | total | code | comment | blank |
|---|---|---|---|---|
| the loop body, `foreach (SceneProp prop in _ordered)` | **401** | **200** | 152 | 49 |
| the whole `Instances` method | 513 | 260 | 190 | 63 |

So it is 200 code lines in one loop body, not 150.

**And the answer to "wtf does a loop need 200 lines for" is that it is not a loop over one job.** By
line count, what the body actually does:

| job | code lines | is this in Valve's pose path? |
|---|---|---|
| lighting — illumination point, cache probe, sample | ~60 | **no.** Lighting is not in `SetupBones` at all |
| logging — dark, brush height, lit, animating, posed extents | ~79 | **no** |
| rejection accounting — `notStudio`, `noBatches`, per-name tallies | ~38 | **no** |
| pose — sequence, cycle, frame, skeleton | ~42 | yes |
| merge + attachment placement | ~35 | yes, but not here (see §2, §4) |
| wearer record | ~5 | **no — the engine has no such step** (see §1) |
| emit | ~18 | yes |

**Roughly 177 of 200 lines are not pose work.** The loop is five subsystems sharing an iteration
variable. That is the smell, and it is worse than "a long function": the pose stages are interleaved
with instrumentation, so the stage boundaries the engine has are not visible even to somebody
looking for them.

---

## 1. The largest divergence: Valve has no ordering step

This is the finding that reframes B181, and it was not what the previous session expected.

The handoff said Valve uses recursion in `C_BaseAnimating::DrawModel`, and it does —
`c_baseanimating.cpp:3238`:

```cpp
C_BaseAnimating *follow = FindFollowedEntity();
if ( follow )
{
    int baseDrawn = follow->DrawModel( 0 );   // flags 0: set up bones, do not render
    if ( baseDrawn )
        drawn = InternalDrawModel( STUDIO_RENDER|extraFlags );
}
```

**But that is not where the ordering is solved.** It is solved inside the merge itself,
`bone_merge_cache.cpp:130`:

```cpp
// Have the entity we're following setup its bones.
bool bWorked = m_pFollow->SetupBones( NULL, -1, m_nFollowBoneSetupMask, gpGlobals->curtime );
```

The merge **demands its parent's bones on the spot**. There is no list, no pass, no sort, no depth,
no two-phase anything. An entity that needs a parent asks for it, and the parent asks for *its*
parent, to whatever depth the chain runs.

**What makes that safe rather than quadratic is a per-frame idempotence guard**, two of them in
`SetupBones`:

- `c_baseanimating.cpp:2874` — `if ( m_iMostRecentModelBoneCounter != g_iModelBoneCounter )`, the
  once-per-frame reset. `g_iModelBoneCounter++` is at `c_baseanimating.cpp:3153`.
- `c_baseanimating.cpp:2911` — `if( ( m_BoneAccessor.GetReadableBones() & boneMask ) != boneMask )`,
  which is the actual early-out: bones already built to the requested mask this frame are returned
  as they stand.

So a player worn by six items is posed **once**, and the other five merges are a pointer read.

Against that, our apparatus:

| ours | purpose |
|---|---|
| `_ordered`, `_worn` | the two lists being ordered |
| `_parents` | entity → parent, rebuilt per frame |
| `Depth(prop)` | walk up the chain, counting |
| `_worn.Sort(...)` | shallowest first |
| `_wanted` | which entities are somebody's parent |
| `_wearerBones` | the results, keyed by entity |

**Six fields and a sort, existing only to guarantee an ordering that Valve gets for free by asking.**
The depth sort is not a worse implementation of Valve's recursion — it is a solution to a problem
the engine's structure does not create. That is the honest answer to "how far has it diverged": on
this axis, completely. Not a different algorithm, a different shape.

It also explains why the previous session's defence of the sort felt wrong to the owner immediately.
The argument was "matching Valve would mean restructuring a 150-line loop body". The engine's
version *has no ordering code at all*, so what was being defended was not a trade-off against
Valve's approach — it was ~40 lines of machinery against zero.

**What the recursion must not lose:** the depth sort gets the once-per-parent guarantee for free.
Valve buys it with `m_iMostRecentModelBoneCounter`. A recursive rewrite without that cache poses a
shared parent once per child.

---

## 2. Our merge throws the item's own animation away

`Merge` (`EntityModels.cs:1236`) takes the item's own posed matrices as `own`, and **uses them only
on the early return** for a wearer with no skeleton. Every other path returns
`StudioBones.MergeOnto(skinned.Bones, wearer.Bones, map)`, which never receives `own` at all.

Inside `MergeOnto` (`StudioBones.cs:376`), an unmatched bone is rebuilt from
`bone.Rotation` / `bone.Position` — **the rest pose local**.

Valve's is `c_baseanimating.cpp:1595`, inside `BuildTransformations`:

```cpp
ConcatTransforms( GetBone( hdr->boneParent(i) ), bonematrix, GetBoneForWrite( i ) );
```

where `bonematrix` came from `QuaternionMatrix( q[i], pos[i], bonematrix )` — `q` and `pos` being
**the animated locals** that `StandardBlendingRules` just produced.

Two consequences, both structural (*unverified* on screen):

1. **A merged item's own moving parts are frozen at rest.** For a hat this is nearly invisible. For
   a weapon merged into a player's hand it is not: the weapon's own animated bones are unmatched by
   definition — no player has them — so they take rest positions instead of whatever the weapon's
   sequence says.
2. **`Skeleton()` is computed per merged prop per frame and discarded.** `EntityModels.cs:1007`
   builds the item's skeleton with its sequence, frame and pose parameters; line 1076 replaces it
   wholesale. The posing cost is real (the blend grid and frame decode) and the result is dropped.
   This is exactly the no-op shape `CLAUDE.md` warns about — unit-tested, wired up, and never
   reaching output.

---

## 3. B180 is confirmed by reading, not merely suspected

B180 was filed as *"unverified and stated as such"*. It can now be settled from the source without a
run, because the mechanism is visible on both sides.

**Valve merges into the same array children read from.** `MergeMatchingBones` writes with
`m_pOwner->GetBoneForWrite( iOwnerBone )` (`bone_merge_cache.cpp:167`), and `BuildTransformations`
runs the merge **first** (`c_baseanimating.cpp:1496`) and then skips merged bones in the per-bone
loop (`:1519`) while building every unmerged one from `GetBone( parent )` (`:1595`) — the same
accessor. So a bone whose parent was merged rides the merged position, automatically.

**Ours records the unmerged skeleton.** `EntityModels.cs:1154`:

```csharp
_wearerBones[prop.EntityIndex] = new Worn(
    prop.ModelPath, boneToWorld ?? [], transform, lightX, lightY, lightZ);
```

`boneToWorld` is `posed.BoneToWorld` from line 1014 — the prop's **own** skeleton, in the prop's own
model space — while `transform` was overwritten at line 1077 with the *wearer's*. So for an
attachment on a weapon on a player, the recorded pair is:

- bones in **weapon** model space, and
- a transform in **player** model space.

Those are not the same space, so the third link of the chain is placed by mixing two. Confirmed
structurally; *unverified* as an observation, because the only chained case in the corpus is the
weapon attachment that currently draws as the magenta chequer for an unrelated reason (a missing
material), which hides where it is.

**The fix is not a one-liner, and that is worth knowing before starting.** `MergeOnto` returns
*skinning* matrices (bone-to-world folded with `poseToBone`), and what the next link needs is
bone-to-world. It has to return both, the way `StudioBones.Skeleton` already does with
`new StudioSkeleton(skinning, boneToWorld)`.

---

## 4. Attachment placement is the right arithmetic in the wrong owner

Ours is inline in the child's iteration (`EntityModels.cs:1087`), resolving one attachment against
the wearer's table.

Valve's is `SetupBones_AttachmentHelper` (`c_baseanimating.cpp:2055`): a separate pass over the
entity's **whole** attachment table, run once, after `BuildTransformations`, gated on
`BONE_USED_BY_ATTACHMENT` never having been asked for before (`:3006`), with results stored by
`PutAttachment( i + 1, world )`.

**The arithmetic matches and should be left alone:**

```cpp
ConcatTransforms( GetBone( iBone ), pattachment.local, world );
```

against our `AttachmentPlacement.Matrix(worn.Bones[attachment.Bone], attachment.Local, ...)`. The
world-align branch matches too, and our one-based `point - 1` is correct against Valve's
`PutAttachment( i + 1, ... )`.

What differs is only ownership and caching: ours recomputes per child rather than once per parent.
That is a small cost and a real reason the loop body is long.

---

## 5. The pose pipeline itself, stage by stage

`StandardBlendingRules` (`c_baseanimating.cpp:1953`) and what `SetupBones` runs around it. The
*measured* column is a search of `managed/` for any equivalent, with a control: the same search
finds `numbones` and `numlocalhierarchy` in `StudioLayout.cs`, so it is reaching source rather than
returning empty because the pattern is wrong.

| stage | SDK | ours |
|---|---|---|
| `InitPose` / `AccumulatePose` | `:1957` | **present** — sequence, frame, blend grid, pose parameters |
| `MaintainSequenceTransitions` | `:1815` | **absent.** A sequence change snaps |
| `AccumulateLayers` | `:1902` | partial — `StudioGestureWeights` exists; layers are not accumulated |
| `CalcAutoplaySequences` | `:1957` region | **absent** |
| `CalcBoneAdj` (bone controllers) | `:1957` region | **absent** — the parser never reads `bonecontrollerindex` |
| `UnragdollBlend` | `:1873` | not applicable — no ragdolls yet |
| IK: `m_pIk->Init` / `UpdateTargets` / `CalculateIKLocks` / `SolveDependencies` | `:2969`–`:2998` | **absent.** The parser never reads `ikchainindex`. Feet do not plant |
| `CalcProceduralBone`, incl. jiggle | `:1527`, `:1546` | **absent** |
| `BuildTransformations` parent concat | `:1595` | present, but from rest locals for merged models — §2 |
| `ApplyBoneMatrixTransform` | `:1602` | **absent** |
| model scale in the bone path | `:1653` | **absent** in the bone path |
| `SetupBones_AttachmentHelper` | `:2055` | present, different owner — §4 |
| per-frame bone cache | `:2874`, `:2911` | **absent** — replaced by the depth sort, §1 |
| `ChildLayerBlend` | `:1907` | **nothing, correctly** — the engine's own copy is dead, §5a |

### 5a. `ChildLayerBlend` is dead in the shipped engine, and that is a finding

**This table was hand-written and it omitted `ChildLayerBlend` entirely.** The extraction added it,
which is the whole case for extracting. Then reading it turned a would-be gap into a would-be
*defect*: the function's first statement is `return;`.

```cpp
void C_BaseAnimating::ChildLayerBlend( Vector pos[], Quaternion q[], float currentTime, int boneMask )
{
	return;
	…
```

What follows is unreachable and full of Valve's own doubts — `// FIXME: needs a new type of
EF_BONEMERGE (EF_CHILDMERGE?)`, `// FIXME: these should Inherit from the parent`, `// FIXME: needs
some kind of sequence`, and `// probably needs an IK merge system of some sort =(`. The intent is
legible: let a bone-merged child's own sequence write back into its parent's pose. It was never
finished, so a merged child cannot animate anything on its wearer.

**Matching the engine here means implementing nothing.** Had the typed list happened to include this
stage, it would have been filed as a gap and cost around forty lines reproducing behaviour that
never runs — work that would have read as parity while being a departure from it. That is the
failure mode a denominator is supposed to prevent, and it nearly happened in the direction nobody
watches for.

*Evidence class: read from published source.*

**The jiggle-bone gap is already documented here under a different name.** `StudioBones.cs:352`
records that a `ghostly_gibus` matched 1 bone of 8 and *"the other seven stayed at the model
origin"*. Those seven are jiggle bones. Valve does not merge them either — it **simulates** them,
`m_pJiggleBones->BuildJiggleTransformations` at `c_baseanimating.cpp:1586`. So our own comment
describes the symptom of a missing feature without naming the feature, which is precisely the state
B182 says a denominator prevents.

---

## 6. What is faithful, said explicitly

An audit that only lists gaps is not an audit.

- **Bone matching is Valve's**, by name, cached on the pair of skeletons. `CBoneMergeCache::UpdateCache`
  does `Studio_BoneIndexByName( m_pFollowHdr, pOwnerBones[i].pszName() )` (`bone_merge_cache.cpp:83`)
  and invalidates on a model swap; ours keys `_mergeMaps` on `worn|wearer`, which is the same
  invalidation expressed as a key.
- **"Only draw if the parent drew"** — Valve's `if ( baseDrawn )` (`:3247`), ours the `continue` on a
  missing `_wearerBones` entry. Same rule, same reason.
- **The attachment arithmetic and its one-based index**, §4.
- **Pose parameters are matched by name and normalised**, and `ComputePoseParam_MoveYaw`'s two-pass
  speed rescale is reproduced including the `flMaxSpeed > flSpeed` guard.
- **Cycle is advanced locally** rather than trusted from the wire, as `C_BaseAnimating::FrameAdvance`
  does.

---

## 7. What this changes about B181

B181 said: split the loop, then replace the depth sort with recursion. §1 says that is the right
destination but the wrong description of the second step. **There is no ordering step to replace —
there is one to delete.**

The shape to land on, from the engine:

1. `PoseOf(entity)` — idempotent per frame, keyed by entity, returning the built skeleton. This is
   `SetupBones` plus its two guards.
2. Inside it, when the entity merges, call `PoseOf(parent)` first. This is
   `MergeMatchingBones`' `m_pFollow->SetupBones(...)`.
3. Merge into the same bone array the unmatched bones are then built from, so the chain rides it.
   This is `BuildTransformations` running the merge before its loop.
4. Lighting, logging and accounting move out of the pose path entirely — they are not in it in the
   engine, and they are 177 of the 200 lines.
5. The threaded pre-pass, §8. Owner direction under D88: *"go for the threading too, full parity."*

The recursion needs a depth bound. Valve does not have one because the engine's parent links are
built by the engine; a demo this project exists to open may carry a cycle, and the current `Depth`
already guards against it by stopping at the prop count.

### 7a. Where `poseToBone` is applied, which decides a boundary

Worth settling before the loop is rewired, because getting it wrong puts a multiply in the pose path
that does not belong there.

**`IStudioRender::DrawModel` takes `matrix3x4_t *pBoneToWorld`** (`public/istudiorender.h:329`), and
so do `AddDecal` and `GetTriangles`. The engine hands the renderer bone-to-world matrices and
nothing else. `poseToBone` appears in the published SDK only in `studio.h`'s declaration and in
`Studio_CalcBoneToBoneTransform` (`bone_setup.cpp:1775`), which uses it for a bone-space conversion
rather than for skinning — the skinning composition lives inside `studiorender`, which is closed.

So the split is:

| layer | works in |
|---|---|
| entity — attachments, hitboxes, bone merge, IK, `GetBone` | **bone-to-world** |
| renderer — vertex skinning | bone-to-world **×** `poseToBone` |

`BoneAccessor` holding bone-to-world is therefore the engine's arrangement rather than a compromise,
and composing with `poseToBone` at the emit is not new work: that multiply already happens once per
bone in `StudioBones.RestPose` and again in `MergeOnto`. It is the same count, moved to the boundary
that owns it.

**And the ambiguity it removes is what B180 was.** `StudioSkeleton` carries both arrays, so every
consumer has to know which one it wants — and the code recorded the wrong one. One array per entity
plus one composition at the render boundary leaves nothing to choose wrongly.

*Evidence class: read from published source.*

---

## 8. The threading is a speculative prefetch, not a parallel draw loop

Worth writing down separately, because the name misleads and the assistant initially proposed
skipping it as "merely an optimisation". `c_baseanimating.cpp:2749`–`:2793` and its call site at
`cdll_client_int.cpp:2210`.

**Nothing is threaded at the moment bones are needed.** During the draw, `SetupBones` merely
*enrols* an entity that is expensive and independent:

```cpp
if ( g_bDoThreadedBoneSetup && !g_bInThreadedBoneSetup && ( nBoneCount >= 16 ) &&
     !GetMoveParent() && m_iMostRecentBoneSetupRequest != g_iPreviousBoneCounter )
{
    m_iMostRecentBoneSetupRequest = g_iPreviousBoneCounter;
    g_PreviousBoneSetups.AddToTail( this );
}
```

Then at the **start of the next frame** — after `SimulateEntities()` and `PhysicsSimulate()`, before
rendering — `ThreadedBoneSetup()` parallel-processes that list, posing each with `boneMask = -1`,
which `SetupBones` resolves to `m_iPrevBoneMask`: *whatever was asked for last frame*. The draw pass
then hits the readable-bones early-out at `:2911` and pays nothing.

So the parallelism is over **last frame's expensive roots, prefetched speculatively**. Three
properties make it safe, and a port that drops any of them is not the same thing:

| property | line | why it matters |
|---|---|---|
| only roots enrolled (`!GetMoveParent()`) | `:2897` | the merge recursion and the parallelism never overlap |
| threaded mode `TryLock`s and **bails** rather than blocking | `:2845` | a contested entity falls back to the draw pass instead of stalling it |
| model cache held across the whole region | `:2756`, `:2761` | `mdlcache->BeginLock()` / `EndLock()` |

`InitBoneSetupThreadPool` and `ShutdownBoneSetupThreadPool` are **empty** in this SDK —
`ParallelProcess` uses the engine's global pool. The .NET thread pool is the faithful equivalent.

**The expectation this sets:** the win is not that posing becomes parallel, it is that posing for
heavy roots leaves the critical path, and it only pays when the same entities are expensive frame to
frame. A demo viewer is the good case — players persist. Measured against B99's ~420 ms of posing per
second, that is where the headroom is.

## 9. Procedural bones: five rules, two of which TF2 actually uses

`mstudiobone_t` carries `proctype` and `procindex`, and `CalcProceduralBone` dispatches on the first
of them with a `switch` (`bone_setup.cpp:4932-4965`). Five rules exist. **Counted over every `.mdl`
TF2 ships — 14,109 models — it uses two of them and the other three on nothing at all:**

| rule | bones | skinned | models |
|---|---|---|---|
| `STUDIO_PROC_JIGGLE` (5) | 3,472 | **3,472** | 1,535 |
| `STUDIO_PROC_QUATINTERP` (2) | 14 | **14** | 7 |
| `STUDIO_PROC_AXISINTERP` (1) | 0 | — | 0 |
| `STUDIO_PROC_AIMATBONE` (3) | 0 | — | 0 |
| `STUDIO_PROC_AIMATATTACH` (4) | 0 | — | 0 |

```bash
dotnet run --project tools/Tf2DemoSalvage.Probe -c Release -- procedural-bones
```

**Every procedural bone in the game carries vertices.** Not one of the 3,486 is a helper nothing is
skinned to, which is worth stating because that was the question that decided whether QUATINTERP was
worth implementing at all — and had the answer gone the other way for jiggle bones, a thousand
cosmetics would have been simulated for nothing.

**Three of Valve's five rules are dead in TF2's content.** They exist in the engine, `studio.h`
numbers them, `CalcProceduralBone` dispatches them, and no model in the game asks for one.
`AIMATBONE` and `AIMATATTACH` are Half-Life 2 mechanisms — a turret barrel tracking a target — and
TF2's sentry does its aiming with pose parameters instead. Implementing them would be writing code
for content that does not exist, which is the same trap as the `$modblend` shader parameter
(`12-shader-parity.md`).

**QUATINTERP is on seven models, not nine.** TF2 has nine classes, so two class models do not carry
the forearm helper. Which two, and whether their forearms pinch in the game as a result, is not
established here.

The one-tick figures this section first carried — 18 jiggle bones and 4 quatinterp across 44 models —
came from `bone-flags <demo>`, which measures what a demo draws rather than what the game contains.
Both are useful and they answer different questions.

**Four bones out of 540 sounds like nothing, and the count is the wrong number to look at.** What
decides whether an unimplemented rule is visible is whether any vertex is weighted to the bone it
drives — a procedural bone nothing is skinned to computes a transform that reaches no mesh. All four
report `SKINNED`. The probe was taught to say so for this question, and the answer flipped the
priority: this is a forearm that does not twist with the wrist, on every player in every demo.

**The control bone is the HAND.** `demo.mdl:hlp_forearm_L` is driven by `bip_hand_L`, three authored
triggers each. That is the mechanism the rule exists for — spreading a wrist's roll along the
forearm so the mesh does not pinch — and reading it out of the file confirms the decode is right in
a way a struct dump cannot.

**How much it moves the bone: 0.72 units at unit distance, which is a 42-degree twist.** Worth
recording as a magnitude rather than a yes, because the first attempt at that number was **zero** —
it measured the bone's translation, and a twist rotates about a fixed origin, so its translation is
identical by construction. An instrument written that same hour to separate "it ran" from "it
mattered" was itself measuring the one quantity that could not change. The fix is to take the
furthest of the origin AND the three unit axes, which covers rotation and translation together.

**A dispatch detail that matters and looks like tidiness.** `CalcProceduralBone` switches on
`proctype` — an exact match — while the JIGGLE path is reached by a separate `&` test further down
`BuildTransformations` (`c_baseanimating.cpp:1546`). `STUDIO_PROC_JIGGLE` is 5, whose low bits
include 2, so a reader that used a bitwise test for QUATINTERP would hand a spring's stiffnesses to
the interpolation rule and read them as quaternions. The two readers here reproduce each test as
written rather than agreeing with each other.

**And the rule REPLACES the animated transform rather than adjusting it.** `CalcProceduralBone`
returns true and the engine's loop `continue`s, so a procedural bone's keyframed rotation never
reaches the skeleton. Applying the rule on top of the animation blends two answers where the engine
takes one — an easy mistake, because every other pass in this pipeline is additive.

**One Valve bug, recorded because it cannot be reproduced.** `DoQuatInterpBone` declares
`matrix3x4_t bonematrix;` uninitialised, fills it only inside
`if (pProc && pbones[pProc->control].parent != -1)`, and concatenates it into the skeleton *outside*
that guard — so a rule whose control is the root bone writes stack garbage. Measured unreachable on
every model that carries the rule: each control has a parent. This project leaves such a bone at its
animated transform.
