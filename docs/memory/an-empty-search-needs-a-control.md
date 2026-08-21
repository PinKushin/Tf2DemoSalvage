---
name: an-empty-search-needs-a-control
description: A search returning nothing is evidence about the search until a positive control proves it could have found something.
metadata:
  type: project
---

**A grep that returns nothing is not a fact about the format. It is a fact about the grep**, until a
positive control in the same sweep shows the search was capable of finding something.

Six instances in this project, all of them an empty result recorded as knowledge:

| Recorded as | Actually |
|---|---|
| "TF2's game code is not public" | 1,318 files under `game/{shared,client,server}/tf` |
| `$modblend` "needs a decompiler" | declared in three shipped VMTs, read by a commented-out proxy |
| `moveparent` "will never appear in a SENDINFO" | it is a `SENDINFO_NAME`, which sends its *second* argument |
| haptics "nothing in the SDK hints at it" | `public/haptics/haptic_msgs.cpp` registers all six, with sizes |
| the container "established by measurement" | `public/demofile/demoformat.h` declares the whole header |
| `ScenePose.Hidden` "read by no renderer", so `EF_NODRAW` is ignored (B133) | read by `DemoTimeline.PropsAt`, one layer up, with a passing test |

Each failed differently, which is why no single fix covers them: wrong directory, wrong file type,
an aliased name, a search scoped to `game/` when the file was in `public/`, and a strong true claim
(no `.dem` reader) whose next sentence quietly widened to cover things that were published.

**The sixth is the one worth studying, because the search was scoped to OUR OWN code and was still
wrong in the same way.** The question asked was "does the renderer read this", so only
`managed/Tf2DemoSalvage.Viewer3D/` was searched. Zero hits — true, and the opposite of what it was
taken to mean: hidden poses are filtered in the timeline, so the renderer never receives one.
`SceneProp` has no `Hidden` member **because the design is right**, and that absence was read as
evidence the design was missing. Filed as a bug, retracted within the hour when the owner said from
memory that pickups already vanish in the viewer.

Two things follow. **An absence caused by correct upstream handling looks exactly like a gap**, so
before filing one, find where the value IS consumed rather than confirming where it is not. And **an
owner's recollection of using the program outranks a grep** — it is an observation of the running
system, which is the thing the grep is a proxy for.

**A third thing, found by asking afterwards what WOULD have caught the alleged bug: nothing.**
Sabotaging the filter left the whole suite green, because the test that read as covering it measured
`props[0].Pose.Hidden` — a field on the object handed over — instead of whether it was handed over at
all. So the claim was unfalsifiable from the suite, which is itself the finding. **When a search
suggests a defect, sabotage the code before filing it**: if nothing reddens, the coverage gap is
real even when the defect is not.

**Two of them were then written into tests**, which is the expensive form — an assertion defending
the wrong conclusion during review.

## The rule

**Put a positive control in the same sweep.** When measuring that `$modblend` appears in zero
published shaders, measure `$envmap` and `$detail` in the same call and assert they are large. If
the controls come back zero the instrument is broken, and the interesting result is an artefact.

That is what `DeadShaderParameterConformanceTests` does, and it caught its own threshold being set
from a `.cpp`-only sweep when the real answer spans `.h` and `.fxc` too.

## Before trusting an absence

- **Search for the string, not the identifier** — see [[wire-names-are-strings]].
- **Widen the root once.** `public/` sits beside `game/`, and shared code lives there.
- **Try a file type you did not think of.** `.res`, `.vmt`, `.fxc` and VPK contents are sources; see
  [[shipped-data-is-a-source]].
- **State the scope in the claim.** "Not in `game/`" is checkable and survives; "not in the SDK" is a
  claim about 40,000 files that nobody verified. This applies to our own code too: "no renderer reads
  it" was a claim about one project directory, and the answer was in the next one up.
- **For "nothing consumes X", go and find what DOES.** Searching only the layer you expected the
  consumer to be in cannot tell a missing feature from a correctly-placed one.

An absence CAN be the answer — `demo_interpolateview` really is an engine ConVar with nothing in the
tree. The difference is that the claim is worth making only once the search has been shown to work.

Related: [[an-uncoverable-gap-is-usually-your-reader]], [[tf2-game-code-is-in-the-sdk]],
[[binaries-answer-what-the-sdk-cannot]].
