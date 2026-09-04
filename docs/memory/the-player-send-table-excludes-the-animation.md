---
name: the-player-send-table-excludes-the-animation
description: TF2 strips sequence, cycle, layers, pose params and playback rate from a player's send table and the client rebuilds all of it — covers why every player is client-side animated, why gestures arrive only as temp entities and a POV lacks the recorder's own, why a delta animation is not a pose and every densifying step needs to know it, why one keyframe cannot serve both an interpolated quantity and a state that changes on its own schedule, and why a tick-encoded value must be converted at receipt against the server's own tick.
metadata:
  type: reference
---

**A TF2 player's animation is almost entirely absent from the wire, on purpose.** `tf_player.cpp`,
in `CTFPlayer`'s send table:

```
SendPropExclude( "DT_BaseAnimating", "m_flPoseParameter" ),      // 769
SendPropExclude( "DT_BaseAnimating", "m_flPlaybackRate" ),       // 770
SendPropExclude( "DT_BaseAnimating", "m_nSequence" ),            // 771
SendPropExclude( "DT_BaseAnimatingOverlay", "overlay_vars" ),    // 774
SendPropExclude( "DT_ServerAnimationData", "m_flCycle" ),        // 779
SendPropExclude( "DT_AnimTimeMustBeFirst", "m_flAnimTime" ),     // 780
```

`CTFPlayerAnimState` rebuilds every one of them on the client.

**Why this matters more than it looks.** A measurement of what a demo carries for a player will
report zero for all of these and be RIGHT, so it is easy to conclude the decode is broken, or that
the value is simply unused. Neither. The field is not sent, and the answer has to be reconstructed
the way the client reconstructs it.

**Where each one comes from instead:**

| What | Where the client gets it |
|---|---|
| sequence | `CMultiPlayerAnimState::ComputeSequences`, from the activity and speed |
| cycle | `m_bClientSideAnimation` plus `FrameAdvance` — see the entry below |
| animation layers | `CTEPlayerAnimEvent` temp entities — see the entry below |
| pose parameters | `ComputePoseParam_MoveYaw` / `_AimPitch` / `_AimYaw` |
| playback rate | left at 1; only a taunt or the item-testing bot changes it |

**What IS sent about a player's animation** is small: `m_bClientSideAnimation` itself, the eye
angles, the flags, and the entity's position and velocity the state machine reads.

**Note the shape of the mistake this prevents.** Three separate defects this session were
"the value reaches the renderer as zero" — and for a player, zero is what the wire says because the
wire says nothing. The question is never "why is the decode wrong"; it is "which client mechanism
fills this in, and have we implemented it".

Related: [[nothing-is-closed]], [[parity-is-the-search-not-the-defence]].

**Six more memories were folded into this one on 2026-09-04** — the mechanisms that fill each gap in
the table above, and the traps in reproducing them. Their names are kept as headings below.

---

## `a-player-is-client-side-animated`

**`CTFPlayer::CTFPlayer` calls `UseClientSideAnimation()` unconditionally** (`tf_player.cpp:953`),
so every TF player sends `m_bClientSideAnimation` — one unsigned bit in `DT_BaseAnimating`
(`baseanimating.cpp:250`). `C_BaseAnimating::UpdateClientSideAnimation`
(`c_baseanimating.cpp:5134`) then latches and calls `FrameAdvance( 0.0f )` for every member of
`g_ClientSideAnimationList` each frame.

**So a player's `m_flCycle` is never a driving value.** It decodes to zero and stays there — it is
excluded from the send table anyway. Everything that moves the model is the client's own advance:

```
float addcycle = flInterval * cyclerate * m_flPlaybackRate;    // c_baseanimating.cpp:5493
```

All three factors matter. The playback rate was missing from this project's skinned path for a long
time and only the baked vertex path multiplied by it (B281).

**A VIEWMODEL advances too, and it is a different mechanism.** `C_BaseViewModel`
(`c_baseviewmodel.cpp:197`) computes `elapsed_time * GetSequenceCycleRate(…) * GetPlaybackRate()`
unconditionally; it never joins `g_ClientSideAnimationList` and has no `m_bClientSideAnimation`. It
also clamps a finished one-shot to **0.999f** rather than to 1, which is the only place in the
engine that does.

**Both reach one gate in this project** — `SceneProp.ClientSideAnimated`, read by
`EntityModelSet.Simulate` — so anything that builds a prop must set it. **Every place that builds a
prop from scratch is a place it can be dropped**, and it was dropped twice:

- `PlayerProps.Add` built every player's prop and had no parameter for it (B280). Every player slid
  through the map in one pose, twice reported by the owner, for weeks.
- `ViewmodelScene.Build` built its three props without it (B283). No draw, reload or fire ever
  played in first person.

**Neither was visible to any test**, because every test either called the advance directly or built
its own prop with the flag already set. The assertion that catches it reads the frame the SKELETON
was handed — `EntityModelSet.FrameOf`, carried rather than recomputed — across two times.

Related: [[output-level-assertion-or-it-is-not-done]].

---

## `gestures-arrive-as-temp-entities`

**A player's reload, flinch and attack animations arrive as TEMP ENTITIES.** `CTEPlayerAnimEvent`
(`tf_player.cpp:324`, `DT_TEPlayerAnimEvent`) carries the player, a `PlayerAnimEvent_t` and a data
word; `TE_PlayerAnimEvent` broadcasts it to everyone who can see that player.

**They are the ONLY source**, because `overlay_vars` is excluded from the player's send table, per
the exclusions listed above. Looking for a player's `m_AnimOverlay` in a demo finds nothing, ever,
and that is not a decode failure.

**Measured, `z1800.dem`: 40,288 of them** — the most common temp entity in the file by an order of
magnitude, ahead of 3,601 `CTEFireBullets`. 762 plain reloads, 925 reload loops, 287 reload ends,
4,228 primary attacks, 2,298 jumps.

**That distribution is the control on the enum offset.** The loop/end pair beside a smaller plain
count is the shotgun and sniper reload shape, which is what a real match looks like; a misread
enum would not land on a plausible one.

**The POV asymmetry, and it is a fact about the format rather than a gap.** `TE_PlayerAnimEvent`
calls `filter.RemoveRecipient( pPlayer )` for every event except the custom gestures and
`SNAP_YAW`, because a player predicts their own. **So a POV recording carries every other player's
gestures and none of its own; a SourceTV recording carries all of them.** A first-person viewer
following the recorder of a POV demo will see no gestures on that one player and should not treat
it as a bug.

**Two lookups that are not interchangeable.** A gesture names an ACTIVITY
(`ACT_MP_GESTURE_FLINCH_CHEST`), and the engine resolves it with `SelectWeightedSequence`, which
matches activity and breaks ties on `actweight`. `Studio_LookupSequence` matches a LABEL. No
sequence is labelled with an activity name, so asking the label lookup returns −1 for every gesture
on every model — silently, with a green suite either side of the gap.

**Not every event is a gesture.** `PLAYERANIMEVENT_JUMP` drives the main sequence; mapping it to a
layer would hang a jump on every player's arms. It is the second most common event in the corpus.

Related: [[output-level-assertion-or-it-is-not-done]].

---

## `a-delta-animation-is-not-a-pose`

**Every TF2 player gesture is a DELTA, and composing one as a pose lays the player flat.** Measured
on `scout.mdl`: `PRIMARY_reload_start` and `jumpland_primary` both carry `STUDIO_DELTA` on the
sequence AND on the animation behind it, and both carry `STUDIO_POST`.

**`SlerpBones` splits on it before anything else** (`bone_setup.cpp:1434`):

```
if ( seqdesc.flags & STUDIO_DELTA )
{
    if ( seqdesc.flags & STUDIO_POST ) QuaternionMA( q1[i], s2, q2[i], q1[i] );  // q1 * (s2*q2)
    else                               QuaternionSM( s2, q2[i], q1[i], q1[i] );  // (s2*q2) * q1
    pos1[i] = pos1[i] + pos2[i] * s2;
    return;
}
```

**Four places have to agree, and getting any one wrong looks like a different bug:**

1. **The composition** — add, do not blend toward.
2. **The seed.** `CalcVirtualAnimation` (`bone_setup.cpp:933`) branches on the ANIMATION's flags:
   a delta's untouched bone is identity and zero, an ordinary animation's is the sequence model's
   bind pose. Seeding a delta from the rest pose makes every unanimated channel a whole bone
   transform, and adding that to a base stretches every limb by its own rest offset.
3. **Any densifying step in between.** A blend that fills absent bones to interpolate two frames
   must fill them the same way. `jumpland_primary` animates twelve bones of seventy-eight; expanded
   against the rest pose it became a seventy-six-bone difference and threw the arms over the head.
4. **`QuaternionScale` is not a component multiply.** It scales the ANGLE —
   `sinsom = sin( asin( sinom ) * t )` — and carries the sign of `w` across, which Valve comments
   as *"keep sign of rotation"* (`mathlib_base.cpp:1757`).

**The flag lives in two places and they are different fields.** `seqdesc.flags` is what `SlerpBones`
tests; `animdesc.flags` is what `CalcVirtualAnimation` tests. Reading one and calling it the other
cost an hour here — the SEQUENCES named in `<class>_animations.mdl` carry `STUDIO_HIDDEN` (`0x400`)
and reading those made "not a delta" look established.

**And the index spaces are different too.** A merged sequence number is not the root model's own
sequence number. Comparing merged 243 against the `model` probe's root list gave the right label
for the wrong reason and sent the whole investigation sideways; ask the merged table for its own
label.

Related: [[one-look-can-be-two-mechanisms]], [[a-property-name-needs-its-declaring-table]].

---

## `every-densifying-step-needs-the-delta-flag`

**A delta pose passes through more than one expansion, and every one of them has to seed the same
way.** `CalcVirtualAnimation` (`bone_setup.cpp:933`) makes the choice once — a delta's unlisted bone
is identity and zero, an ordinary animation's is the bind pose — and each later step that names
every bone repeats that choice or destroys it.

`SkinnedModel.Locals` has **two** such steps and B284 fixed only the first:

1. the FRAME blend, between two frames of one animation — told;
2. the GRID blend, across the up-to-three corners of a blend grid — **not told**, because it called
   a four-argument overload that quietly meant `additive: false` (B298, 2026-09-03).

**Every TF2 player's aim matrix is a delta blend grid**, so this was not an edge case:
`PRIMARY_aimmatrix_idle` is 3x4, delta on the sequence and on the animation, reached from
`stand_PRIMARY` by autolayer. Seeded from the bind pose, its root came back as a 63° rotation and a
14-unit offset instead of a near-identity difference, and `QuaternionMA` added that over the body at
full weight. Seven of fifteen players stood on their heads.

**The convenience overload is deleted, not documented.** `additive` is a required argument now. It
had a three-paragraph doc comment explaining the exact branch it was getting wrong, which is the
argument against comments as a guard — see [[parity-is-the-search-not-the-defence]].

**The defect was older than the symptom.** Nothing reached a delta grid as a LAYER until autolayers
were wired the same day, so the wrong seeding had nothing to add itself to. When a visual bug
appears right after a change, the change may only have made an existing fault reachable — see
[[parity-is-the-search-not-the-defence]].

---

## `one-keyframe-bundles-what-the-engine-keeps-apart`

**The engine keeps one interpolation history PER VARIABLE; this project keeps one keyframe per
entity per packet.** That difference is invisible until something needs a timestamp, and then it
decides the design.

B273. The engine stamps a history entry with the entity's own clock — `GetSimulationTime()` for
origin and angles, `GetAnimTime()` for the cycle and pose parameters — never with the packet. The
obvious fix was to key our keyframe list by that applied time. **It broke immediately, and a corpus
test named the reason**: an entity that does not simulate keeps one simulation time for minutes, so
every state change it made collapsed onto a single tick. `NoDrawTrackTests` failed with *"entity 654
was never handed over — hiding that never ends is deletion wearing a flag"*.

**Because a `ScenePose` is two kinds of thing at once.** Position and angles are interpolated
quantities that want the engine's changetime. Visibility, render mode, skin, body and weapon state
are current values that change on their own schedule and must stay in the order the demo stated
them. One timestamp cannot serve both, and the engine never has to choose because those fields are
not in a history at all — they are just fields on the entity.

**The shape that works: key the list by ARRIVAL, carry the applied time alongside.** Arrival is the
only monotonic key and it dates the state; a parallel `_appliedAt` dates the interpolated
quantities. The causality rule — a client cannot be pulled toward an update it has not received —
still tests arrival. Two questions, two numbers, neither standing in for the other.

**And the same reasoning bounds what is left undone.** The animation clock disagrees with the
simulation clock by more than eight ticks on 95.5% of the updates carrying both, so honouring it
needs a second history rather than a different stamp — filed as B274 with its measured cost rather
than bodged into the same list.

**Before changing what a timestamp MEANS, list everything the field is used for.** Here the
keyframe tick was also the list key, the ordering of state changes, the lifetime bound, and the wake
schedule. Only one of those wanted the new meaning.

Related: [[a-pass-must-establish-its-own-state]], [[wire-faithful-is-not-state-faithful]].

---

## `a-tick-encoded-value-expires`

**`SPROP_ENCODED_AGAINST_TICKCOUNT` means the value stops meaning anything when the packet ends.**
`m_flSimulationTime` is eight unsigned bits holding an offset from
`100 * floor( (tick − entindex % 32) / 100 )`, re-centred within ±127 ticks of now
(`server/baseentity.cpp:265`, `client/c_baseentity.cpp:344`). Two consequences, and both were
learned by getting them wrong:

**Convert at receipt, never on read.** This decoder RETAINS properties across packets by design, so
an offset read one packet later is decoded against a different base and yields a plausible tick up
to 128 out. The engine cannot make this mistake — its receive proxy runs during decode and the raw
offset never survives the packet. Ours had to move into `EntityStateTable.Apply` for the same
reason.

**The base is the SERVER's tick, from `net_Tick` — not the demo's command tick.** A demo's own ticks
start near zero while the server has been up for hours, so the two are unrelated numbers of similar
shape. `net_Tick` was decoded by this project and used by nothing, which is why the difference had
never surfaced. Same family as [[demo-ticks-do-not-start-at-zero]], one level up: there the demo's
ticks do not start at zero; here they are not the server's ticks at all.

### The signature: bimodal at the clamp

Both mistakes produced the same picture — a histogram of "packet tick minus decoded tick" with
roughly half the mass in each end bucket of a ±8 clamp and almost nothing between:

```
  delta  -8:  1503 (50.02%)
  delta  -1:    37 (1.23%)
  delta   0:    43 (1.43%)
  delta   8:  1421 (47.29%)
```

**That is noise wearing the shape of a distribution.** A quantity decoded against the wrong base is
uniform over its window, and a clamp turns uniform into two spikes — which reads like a finding.
With the base right, the same demo shows clusters: 81% at −4, 6% at 0, 13% at ≥ +8.

**So: before believing a spread, check whether the ends are the clamp.** Label the end buckets
`<=-8` and `>=+8` rather than `-8` and `+8`, so a clamp cannot be read as a measurement. And keep a
control bucket for "the value never arrived" — while that is zero, the distribution describes the
demo rather than describing which entities happened to answer.

Related: [[instrument-bugs-outnumber-decoder-bugs]], [[an-empty-search-needs-a-control]],
[[a-dropped-field-falls-to-a-computed-default]].
