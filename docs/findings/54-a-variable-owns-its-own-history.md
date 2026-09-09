# 54 — a variable owns its own history, and the prune keeps two stale entries

*B382, B383, B94. Evidence class: read-from-source throughout, arithmetic where a value is predicted,
measured where a count or a demo is named.*

The client's interpolation is not one mechanism over one buffer of past states. It is **one buffer per
registered variable**, and a lot of this project's interpolation trouble came from not knowing that.
This is the account of working it out, including two beliefs that had been written down as fact and were
wrong.

## What registers, and what that means for the shape

`C_BaseEntity::AddVar` (`c_baseentity.cpp:875`) attaches a watcher — a `CInterpolatedVar` — to a member
and files it in the entity's var map under a latch group. `OnLatchInterpolatedVariables` then walks the
whole map and appends to every watcher whose group fired (`:2814`):

```cpp
float changetime = GetLastChangeTime( flags );
int c = m_VarMap.m_Entries.Count();
for ( int i = 0; i < c; i++ )
{
    IInterpolatedVar *watcher = m_VarMap.m_Entries[ i ].watcher;
    int type = watcher->GetType();
    if ( !( type & flags ) )   continue;
    if ( type & EXCLUDE_AUTO_LATCH )   continue;
    watcher->NoteChanged( changetime, bUpdateLastNetworkedValue );
}
```

So the count of histories is the count of registrations, and the registrations for an animating entity
are:

| member | watcher | flags | width |
|---|---|---|---|
| `m_vecOrigin` | `m_iv_vecOrigin` | `LATCH_SIMULATION_VAR` | 3 |
| `m_angRotation` | `m_iv_angRotation` | `LATCH_SIMULATION_VAR` | 3 |
| `m_flCycle` | `m_iv_flCycle` | `LATCH_ANIMATION_VAR` (+ `EXCLUDE_AUTO_INTERPOLATE` when client-side animated) | 1 |
| `m_flPoseParameter` | `m_iv_flPoseParameter` | `LATCH_ANIMATION_VAR` | `MAXSTUDIOPOSEPARAM` = 24 |
| `m_flEncodedController` | `m_iv_flEncodedController` | `LATCH_ANIMATION_VAR` | `MAXSTUDIOBONECTRLS` |
| each `m_AnimOverlay[i]` | one watcher per element | — | — |

`m_vecVelocity`'s registration is commented out (`c_baseanimating.cpp:912`), which matters: a reader that
assumes velocity is interpolated is reading a member nothing latches.

Two registrations may share one of our histories only when they share a latch group AND carry no further
flags, because then they are appended in the same call with the same changetime and their lists are
parallel entry-for-entry. Origin and angles qualify. The cycle and the pose parameters do **not** — the
cycle picks up `EXCLUDE_AUTO_INTERPOLATE` for a client-side-animated entity and the pose parameters never
do — so they are separate here as they are there.

## The entry, and the two things it is keyed on

An entry is a changetime and `m_nMaxCount` values. **The changetime is the latch group's clock**, not the
tick the packet arrived: `GetLastChangeTime( flags )` returns `GetSimulationTime()` for the simulation
group and `GetAnimTime()` for the animation group. Measured on the 2013 SourceTV foundry recording, the
two disagree by more than eight ticks on **95.5%** of the updates that carry both, so a reader that keys
both on arrival is not approximating the engine, it is answering a different question.

`NoteChanged` appends **unconditionally** (`interpolatedvar.h:649`). Its "differs / identical" return is a
hint that lets the caller skip interpolation work; it is not a reason to omit the entry. That single fact
is what makes a held-open door work: several entries carry the same position at different changetimes, so
the spline's third sample equals its second and the curve leaves that position with no incoming velocity.
Collapse the repeats and the third sample becomes the previous *distinct* pose — for a door, a mid-opening
height — and the close inherits the opening's velocity.

## The pair is found on the changetime, walking newest-first

`GetInterpolationInfo` (`:815`) walks from the head and compares changetimes, not arrivals:

```cpp
for ( int i = 0; i < varHistory.Count(); i++ )
{
    pInfo->older = i;
    float older_change_time = m_VarHistory[ i ].changetime;
    if ( older_change_time == 0.0f )   break;
    if ( targettime < older_change_time ) { pInfo->newer = pInfo->older; continue; }
    if ( pInfo->newer == varHistory.InvalidIndex() ) { pInfo->newer = pInfo->older; return true; }
    ...
    int oldestindex = i+1;
    if ( !(m_fType & INTERPOLATE_LINEAR_ONLY) && varHistory.IsIdxValid(oldestindex) ) { ... }
}
```

Three consequences worth having in front of you:

- **The pair always brackets the target on the changetime**, whatever order the entries arrived in. An
  arrival-adjacent pair does not, and choosing one was the jitter the owner reported as *"kinda jittery
  and its not a FPS thing"* — two arrival-adjacent entries sharing a simulation time give `dt == 0`, the
  sampler holds, and then it jumps.
- **A target past every entry is not a failure.** `newer` is set to `older` and the function returns
  `true` with `frac` still 0: the value holds. Returning nothing there throws away the other clock's
  answer along with this one.
- **`oldest` is `i+1`, taken unconditionally**, and only whether HERMITE applies is gated, on
  `dt2 = older_change_time - oldest_change_time > 0.0001f`.

## The first wrong belief: that the history is trimmed to the window

It is not. `RemoveEntriesPreviousTo` (`:782`), called at the end of `Interpolate()` (`:1057` — the call at
`:667` is inside `#if 0`):

```cpp
if ( m_VarHistory[i].changetime < flTime )
{
    // We need to preserve this sample (ie: the one right before this timestamp)
    // and the sample right before it (for hermite blending), and we can get rid
    // of everything else.
    m_VarHistory.Truncate( i + 3 );
    break;
}
```

`i` is the first stale entry and `Truncate( i + 3 )` keeps it **and the two beyond it**. The list is
newest-first, so those two are older still. A history pruned during a quiet stretch therefore holds two
arbitrarily old entries, and the spline will use them.

This repository had the opposite written down, in a test class's own documentation: *"`CInterpolatedVar`
keeps a history trimmed to the interpolation window … so its three samples are always recent"*. On that
belief a **hermite window** was built — a rule refusing a spline over a long span — and it is this
project's rule, not Valve's. It is the shape of mistake worth naming: a plausible reading of one function
became a licence to add a guard the engine does not have.

**The arithmetic that killed it.** A door closes at constant speed, stops, and is restated two hundred
ticks later. At tick 209 the client draws `target = 201` and its history holds changetimes 209, 9, 8, 7.
So `older = 9`, `newer = 209`, `oldest = 8`, `dt2 = 1`, hermite on. `TimeFixup2_Hermite` (`:1372`) with
`dt1 = 200`:

```
frac  = dt1 / dt2 = 200
fixup = Lerp( 1 - 200, 600, 584 ) = 600 + (-199)(584 - 600) = 3784
```

— a sample extrapolated two hundred ticks into the past, far outside the range of any real one. Then
`Lerp_Hermite( 0.96, 3784, 584, 584 )` with `d1 = -3200`:

```
584   * (2t³-3t²+1)  = 584 * 0.004672   =    2.728
584   * (-2t³+3t²)   = 584 * 0.995328   =  581.272
-3200 * (t³-2t²+t)   = -3200 * 0.001536 =   -4.915
                                          ---------
                                            579.085
```

**579.085 — five units below shut, in the engine.** A door really can sink through its own frame. The one
switch that would prevent it, `INTERPOLATE_LINEAR_ONLY`, is set on exactly one variable in the entire
client: `m_viewtarget` (`c_baseflex.cpp:133`). Not the origin.

Valve's own comment on the fixup says the quiet part — without it a spline *"overshoots whenever the
packet spacing wobbles"*. Renormalising evens the spacing; it does not keep the curve inside the range of
its samples.

**And the same thing happens on the way up, where the respacing is the CAUSE rather than the cure.** A
door rising at 4.625 units per tick, updated every 4 ticks, is then restated 36 ticks after it stops. So
`frac = 36/4 = 9`, the synthetic sample lands at `changetime 124 - 36 = 88` carrying
`Lerp( 1-9, 92.5, 111 ) = -55.5`, and the slope from it to the older sample is
`(111 - -55.5)/36 = 4.625` — **exactly the door's real speed.** Respacing PRESERVES the velocity; that is
what it is for. A hermite handed a real velocity and a dead stop overshoots before it settles, and the
drawn height reaches 117.395 against a stated maximum of 111. Doors in TF2 are slightly springy for this
reason, and a reader that removes the springiness is not more correct, it is different.

That is worth stating separately because it was the second assertion in a row written on the assumption
that the engine keeps its curve inside the range of its samples. It does not, in either direction, and
both times the assertion looked obviously true.

**And the restatements only matter for four ticks.** Worth stating because the conformance test written
for them took three attempts to become sensitive, and both failures came from the same wrong picture.
While the door is HELD, the next update has not arrived, so the pair is `Older == Newer`, the fraction is
0, and the value holds — whatever the history contains. Every restatement is invisible there. The
identical-third-sample mechanism shows up at exactly one moment: when the next MOVING update lands and the
pair spans the hold's end to it. For a door held to tick 300 and moving again at 304, that is `at` in
`[304, 308)`. A sweep over the hold cannot see it; a sweep beginning at `300 + delay` steps over it. The
interpolation delay had been added to the one window that needed the ticks the delay covers.

A diagnosis offered for the same failure — *"the interpolation system has other sources of information it
consults"* — is wrong and is kept here as wrong. There is no fallback. There is a degenerate pair, which
is the engine's own answer for a value nothing newer has restated.

## What the engine DOES refuse, and it is the only bound worth copying

**A sample it has not received.** The history contains arrived entries and nothing else, so a lookup
cannot reach into the future. That is structural in a live client and invisible as a rule — which is
exactly why a reader that holds the whole recording loses it without noticing. Skipping it is B94: a
shutter on cp_process drifting upward for ten seconds toward an update that had not been sent, and sinking
below its own frame on the way back, drawing black because a model whose illumination point is inside
solid geometry samples no ambient light.

So the licensed difference between a viewer's history and a client's is **what is RETAINED, never what is
ANSWERED**: keep every entry so a scrub backwards still has data, store the arrival tick on each, and
bound the search by it. The owner set the requirement — *"we should be able to get valve parity there and
still scrub and rewind the demo, we just have to make it work in both directions."*

## The second wrong belief: that an unused method is dead

Replacing the structure left two methods with no callers, and the build said so:

```
error S1144: Remove the unused private method 'Renormalise'.
error S1144: Remove the unused private method 'Neighbours'.
```

`Neighbours` was superseded. `Renormalise` was `TimeFixup_Hermite` — and the rewrite had simply stopped
calling it. Deleting it on the analyzer's word would have shipped a spline with no respacing, at zero
warnings, with a green suite.

Two more losses from the same refactor produced no diagnostic at all. The causality gate above was one.
The other was the wake scheduler: `Motion` predicts every tick at which `At` changes answer, and after the
move its candidates came from a side-table `At` no longer read. `NoDrawTrackTests` caught it — entity 648
on `tf2-2007-build3258-pov-cp_granary` at tick 5334, the pose saying drawable and the drawn set not having
it — which is the **third** time that one entity has caught a scheduler drifting from its sampler. What a
scheduler must name and no changetime falls on is the STATE boundary: `Hidden`, `Sequence`, `RenderMode`
and the rest come from the keyframe at the delayed target, so the answer changes shape at
`keyframe.Tick + delay`.

**A refactor's real damage is the calls it stops making, and only one of those three was visible to a
tool.**

## The reset, which is still open

`C_BaseAnimating::PostDataUpdate` clears the cycle history when an animation starts
(`c_baseanimating.cpp:4747`):

```cpp
// reset prev cycle if new sequence
if (m_nNewSequenceParity != m_nPrevNewSequenceParity)
{
    ...
    if ( hdr && !( hdr->flags() & STUDIOHDR_FLAGS_STATIC_PROP ) )
        m_iv_flCycle.Reset();
}
```

Three details in one block, all of which a same-sequence comparison gets wrong: the trigger is a
networked **parity counter**, so it fires on a restart of the *same* animation and not on a renumber the
server did not announce; `Reset()` **discards**, so the cycle snaps to the new value rather than holding
the old one; and a `STUDIOHDR_FLAGS_STATIC_PROP` model is exempt, for a CPU reason Valve states in the
comment rather than a behavioural one.

The faithful form of a reset, for a reader that can scrub, is a generation boundary the neighbour search
refuses to cross rather than a deletion. It was built and taken back out, because it exposed a prior
divergence: it pairs the NEW animation's cycle with the OLD sequence. Our state fields come from the
keyframe at the delayed target; the engine's `m_nSequence` is simply the latest received, undelayed. That
state delay is the question underneath, and it is filed as B383 with the reset rather than guessed at
here.

## What is still not established

- Whether any real recording contains the undershoot shape above. It needs a restatement long after a
  door stops with nothing between, and a `func_door` that has stopped also stops simulating, so its
  updates stop entirely.
- Whether `m_nNewSequenceParity` is decoded at all. `m_nResetEventsParity` beside it was decoded, given a
  citation, and had exactly one reference in the whole repository — its own declaration.
- Pose parameters spline in the engine (`_Interpolate_Hermite` runs over the array like any other,
  `:1438`) and blend linearly here.
- Per-parameter and per-sequence looping. The engine sets `SetLooping` from the model
  (`m_iv_flPoseParameter.SetLooping( Pose.loop != 0.0f, i )`, `:1130`;
  `m_iv_flCycle.SetLooping( IsSequenceLooping( GetSequence() ) )`, `:4472`); ours is set once, to agree
  with the blend that follows rather than to contradict it.
- The encoded controllers and the overlay layers have registrations and no history here yet.
