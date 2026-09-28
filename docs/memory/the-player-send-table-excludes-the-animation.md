---
name: the-player-send-table-excludes-the-animation
description: "TF2 strips sequence, cycle, layers, pose params and playback rate from a player's send table and the client rebuilds all of it — covers why every player is client-side animated, why gestures arrive only as temp entities and a POV lacks the recorder's own, why a delta animation is not a pose and every densifying step needs to know it, why one keyframe cannot serve both an interpolated quantity and a state that changes on its own schedule, and why a tick-encoded value must be converted at receipt against the server's own tick."
metadata: 
  node_type: memory
  type: reference
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-10T22:52:39.827Z
---

**A TF2 player's animation is almost entirely absent from the wire, on purpose** —
`SendPropExclude` strips pose parameters, playback rate, sequence, overlay vars, and cycle from
`CTFPlayer`'s send table. `CTFPlayerAnimState` rebuilds every one client-side.

**Why this matters:** measuring what a demo carries for these fields correctly reports zero — easy to
mistake for a decode bug or an unused value. The question is never "why is the decode wrong"; it's
"which client mechanism fills this in, and have we implemented it".

| What | Where the client gets it |
|---|---|
| sequence | computed from activity and speed |
| cycle | client-side animation flag plus frame advance |
| animation layers | temp entities |
| pose parameters | computed from aim/movement |
| playback rate | left at 1 unless a taunt changes it |

What IS sent: the client-side-animation bit, eye angles, flags, position/velocity.

Related: [[nothing-is-closed]], [[parity-is-the-search-not-the-defence]].

---

## `a-player-is-client-side-animated`

**`CTFPlayer::CTFPlayer` unconditionally sends the client-side-animation bit** (`tf_player.cpp:953`),
one unsigned bit in `DT_BaseAnimating` (`baseanimating.cpp:250`); `C_BaseAnimating::UpdateClientSideAnimation`
(`c_baseanimating.cpp:5134`) latches and calls `FrameAdvance` for every member of
`g_ClientSideAnimationList` each frame: `addcycle = interval * cyclerate * playbackRate`
(`c_baseanimating.cpp:5493`). All three factors matter — playback rate was missing from one draw path
for a long time (B281).

**A VIEWMODEL advances by a DIFFERENT mechanism** (`c_baseviewmodel.cpp:197`) — computed
unconditionally, never joins the client-side-animation list, clamps a finished one-shot to 0.999 (the
only place in the engine that does).

**Both reach one gate in this project, and it was dropped twice** building new prop paths from
scratch — once making every player slide through the map in one pose for weeks (B280), once meaning no
first-person animation ever played (B283). Neither was visible to any test, since every test either called
the advance directly or built its own prop with the flag already set.

Related: [[output-level-assertion-or-it-is-not-done]].

---

## `gestures-arrive-as-temp-entities`

**A player's reload/flinch/attack animations arrive as TEMP ENTITIES** — `CTEPlayerAnimEvent`
(`tf_player.cpp:324`) — the ONLY source, since overlay vars are excluded from the send table. Looking
for them in the entity stream finds nothing, correctly.

**The POV asymmetry is a fact about the format:** the broadcast filter removes the recording player as
recipient for most such events (a player predicts their own), so a POV recording carries every OTHER
player's gestures and none of its own; SourceTV carries all of them. A first-person POV viewer
legitimately shows no gestures on the recorder.

**Two lookups are not interchangeable** — a gesture names an ACTIVITY, resolved by weighted-sequence
matching; a label lookup returns nothing for every gesture on every model, silently, with a green
suite either side.

**Not every event is a gesture** — jump events drive the main sequence; mapping to a layer would hang
a jump animation on the arms.

Related: [[output-level-assertion-or-it-is-not-done]].

---

## `a-delta-animation-is-not-a-pose`

**Every TF2 player gesture is a DELTA, and composing one as a pose lays the player flat.**
`SlerpBones` splits on the delta flag before anything else (`bone_setup.cpp:1434`): add the delta on
top, don't blend toward it.

**Four places have to agree, each wrong-alone looking like a different bug:** the composition (add,
not blend); the SEED — `CalcVirtualAnimation` (`bone_setup.cpp:933`) branches on the animation's flags:
a delta's untouched bone is identity/zero, an ordinary animation's is bind pose — seeding wrong
stretches every limb by its rest offset; any densifying step between (must fill absent bones the same
way); the quaternion scale (scales the ANGLE, carries the sign of `w` across per Valve's own comment,
`mathlib_base.cpp:1757` — not a component multiply).

**The flag lives in two DIFFERENT fields** (sequence flags vs. animation flags) tested by different
functions — reading one and calling it the other cost an hour. **Index spaces differ too** — a merged
sequence number is not the root model's own.

Related: [[one-look-can-be-two-mechanisms]], [[a-property-name-needs-its-declaring-table]].

---

## `every-densifying-step-needs-the-delta-flag`

**A delta pose passes through more than one expansion step, and every one must seed the same way** —
`CalcVirtualAnimation` (`bone_setup.cpp:933`) makes the choice once, and each later step repeats it or
destroys it. `SkinnedModel.Locals` had TWO such steps and B284 fixed only the first (frame blend); the
GRID blend called a four-argument overload that silently defaulted `additive: false` (B298,
2026-09-03), and every TF2 player's aim matrix IS a delta blend grid — seven of fifteen players stood
on their heads.

**The convenience overload is deleted, not documented** — it had a three-paragraph doc comment
explaining the exact branch it got wrong, the argument against comments as a guard
([[parity-is-the-search-not-the-defence]]).

**The defect was older than the symptom** — nothing reached the grid as a LAYER until an unrelated
wiring landed the same day, so the wrong seeding had nothing to add itself to yet.

---

## `one-keyframe-bundles-what-the-engine-keeps-apart`

**The engine keeps one interpolation history PER VARIABLE; this project keeps one keyframe per
entity per packet.** B273: keying a keyframe by the engine's own applied-time clock (`GetSimulationTime()`
for origin/angles, `GetAnimTime()` for cycle/pose) broke immediately — an
entity that doesn't simulate keeps one simulation time for minutes, collapsing every state change onto
one tick.

**A `ScenePose` is two kinds of thing at once:** position/angles are interpolated quantities wanting
the engine's changetime; visibility/render-mode/skin are current values that must stay in DEMO order.
One timestamp can't serve both.

**Fix: key the list by ARRIVAL, carry the applied time alongside** — arrival is the only monotonic
key, and a parallel field dates the interpolated quantities. The animation clock disagrees with the
simulation clock by more than eight ticks on 95.5% of updates carrying both, filed as B274 rather than
bodged into the same list. **Before changing what a timestamp
MEANS, list everything the field is used for** — here it was the list key, ordering, lifetime bound,
AND wake schedule; only one wanted the new meaning.

Related: [[a-pass-must-establish-its-own-state]], [[wire-faithful-is-not-state-faithful]].

---

## `a-tick-encoded-value-expires`

**A tickcount-encoded value stops meaning anything once the packet ends.** `m_flSimulationTime` is
eight unsigned bits holding an offset re-centred within ±127 ticks of now (`server/baseentity.cpp:265`,
`client/c_baseentity.cpp:344`). This decoder RETAINS
properties across packets by design, so decoding an offset a packet late yields a plausible tick up
to 128 out — must convert AT RECEIPT, moved into the apply step.

**The base is the SERVER's tick, not the demo's command tick** — a demo's own ticks start near zero
while the server's have run for hours; conflating them went unnoticed for a while since the server tick
was decoded and used by nothing.

**Signature: bimodal at the clamp** — a wrong-base value looks uniform, and a clamp turns uniform
into two spikes at the clamp edges, reading exactly like a real finding. **Before believing a spread,
check whether the ends are the clamp** — label end buckets `<=-8`/`>=+8`, not `-8`/`+8`.

Related: [[instrument-bugs-outnumber-decoder-bugs]]#an-empty-search-needs-a-control,
[[a-dropped-field-falls-to-a-computed-default]], [[demo-ticks-do-not-start-at-zero]].
