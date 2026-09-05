---
name: a-player-is-not-a-prop-track
description: A field carried on ScenePropTrack never reaches a player; players come through ScenePlayer and PlayerProps.Add, and the tests stay green either way.
metadata:
  type: project
---

**When you add a networked field to the scene, ask which POPULATION carries it before choosing where
to put it.**

B346 carried `m_ubInterpolationFrame` on `ScenePropTrack`/`ScenePose`, matching where
`m_nNewSequenceParity` already lived. The timeline then stamped **zero discontinuities across 570
prop tracks** on a demo whose wire carries 332 changing sends — because **all 332 belong to
`CTFPlayer`**, and a player's `SceneProp` is built by `PlayerProps.Add` from a `ScenePlayer`, not
from a prop track.

Two separate paths reach the renderer:

| population | timeline record | how it becomes a `SceneProp` |
|---|---|---|
| props | `ScenePropTrack` → `ScenePose` | `timeline.PropsAt` |
| players | `ScenePlayer` (a record with positional parameters) | `PlayerProps.Add`, field by field |

**Why it is invisible:** every unit test passed. The prop path was correct and fully tested; the
player path simply had no assignment, and a missing assignment is a default rather than an error.
`PlayerProps.cs` already carries the warning, written after B312 lost three fields the same way —
*"A value with no assignment here is one the renderer never sees whatever the timeline decoded."*

**How to apply:**

- Grep the wire census before choosing the home. `awk` over a trace, grouping by update type and
  class, answers "which entities actually change this" in one command and costs nothing.
- A field on `DT_BaseEntity` or `DT_BaseAnimating` is on players AND props. Both paths need it.
- Adding to `ScenePlayer` means three edits, not one: the record's parameter, its `<param>` doc, and
  the assignment in `PlayerProps.Add`.
- **The output-level assertion is what catches this** — see
  [[output-level-assertion-or-it-is-not-done]]. Nothing below it can.

Related: [[a-dropped-field-falls-to-a-computed-default]], [[measure-the-output-not-the-capability]],
[[an-empty-search-needs-a-control]].

**Guarded as a class now, because a per-field test cannot catch the next field.**
`PlayerPoseWiringCompletenessTests` (Scene.Tests) walks every property `ScenePlayer` and `ScenePose`
share by name, gives the player a distinctive value for each, runs the real `PlayerProps.Add`, and
requires the pose to come back holding something other than its default. Deleting an assignment now
names the exact field.

**Exemptions go in its `Computed` table with the reason**, never as a blanket — `Yaw` (a player
facing due east IS the default, so a non-default fixture cannot distinguish carried from dropped),
`Skin` (`m_nSkin = (team == TF_TEAM_RED) ? 0 : 1`, computed), `Airwalking` (gated by the class
script), `Slot` (resolved through the appearance). A field that lands there without one of those
reasons still holding is the bug, not the exemption.

**When you add a field to `ScenePlayer`, three fixtures need it**, and two of them will tell you:
`PlayerCompletenessTests` and `PoseCompletenessTests` fail immediately with "should be empty but
had", because they require every property to carry a distinctive value. That is them working. The
third is the wiring suite above.
