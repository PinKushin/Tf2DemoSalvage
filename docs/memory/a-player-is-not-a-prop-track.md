---
name: a-player-is-not-a-prop-track
description: "A field carried on ScenePropTrack never reaches a player; players come through ScenePlayer and PlayerProps.Add, and the tests stay green either way."
metadata: 
  node_type: memory
  type: project
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:52:53.752Z
---

**When adding a networked field to the scene, ask which POPULATION carries it before choosing where
to put it.**

B346 added `m_ubInterpolationFrame` to `ScenePropTrack`/`ScenePose`, matching
`m_nNewSequenceParity`'s home. The timeline then stamped zero discontinuities across 570 prop tracks
on a demo whose wire carries 332 changing sends — all 332 belong to `CTFPlayer`, whose `SceneProp` is
built by `PlayerProps.Add` from a `ScenePlayer`, not a prop track.

| population | timeline record | reaches renderer via |
|---|---|---|
| props | `ScenePropTrack` → `ScenePose` | `timeline.PropsAt` |
| players | `ScenePlayer` | `PlayerProps.Add`, field by field |

**Why invisible:** every unit test passed — the prop path was correct and fully tested; the player
path just had no assignment, a default rather than an error. `PlayerProps.cs` already warns (after
B312 lost three fields the same way): *"A value with no assignment here is one the renderer never
sees whatever the timeline decoded."*

**How to apply:**
- Grep the wire census (`awk` over a trace by update type + class) before choosing a home.
- A field on `DT_BaseEntity`/`DT_BaseAnimating` is on players AND props — both paths need it.
- Adding to `ScenePlayer` is three edits: the record's parameter, its doc, and `PlayerProps.Add`.
- The output-level assertion is what catches this ([[output-level-assertion-or-it-is-not-done]]).

**Guarded as a class now**: `PlayerPoseWiringCompletenessTests` (Scene.Tests) walks every shared
property, gives a distinctive value, requires the pose back non-default. Exemptions go in its
`Computed` table with a reason (`Yaw`, `Skin`, `Airwalking`, `Slot`) — landing there without a real
reason is the bug. Adding a `ScenePlayer` field: `PlayerCompletenessTests`/`PoseCompletenessTests`
fail immediately if unwired (working as intended); the wiring suite above is the third check.

Related: [[a-dropped-field-falls-to-a-computed-default]], [[measure-the-output-not-the-capability]],
[[instrument-bugs-outnumber-decoder-bugs]].
