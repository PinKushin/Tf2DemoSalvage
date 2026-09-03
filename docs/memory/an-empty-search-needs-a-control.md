---
name: an-empty-search-needs-a-control
description: A search returning nothing is evidence about the search until a positive control proves it could have found something.
metadata: 
  node_type: memory
  type: project
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-08-26T01:21:47.049Z
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
  [[nothing-is-closed]].
- **State the scope in the claim.** "Not in `game/`" is checkable and survives; "not in the SDK" is a
  claim about 40,000 files that nobody verified. This applies to our own code too: "no renderer reads
  it" was a claim about one project directory, and the answer was in the next one up.
- **For "nothing consumes X", go and find what DOES.** Searching only the layer you expected the
  consumer to be in cannot tell a missing feature from a correctly-placed one.

An absence CAN be the answer — `demo_interpolateview` really is an engine ConVar with nothing in the
tree. The difference is that the claim is worth making only once the search has been shown to work.

## A detector shipped as a TEST needs the control permanently, not just once — B196, 2026-08-25

The rule above is usually applied to a one-off grep. It matters more when the search becomes a
standing test, because then the empty result is re-asserted on every run and nobody looks again.

`FieldSeedingTests` scans the viewer's source for a field that is READ but only ever assigned
`null` — the shape a dropped assignment leaves behind after an extraction. It found two shipped
regressions. It also failed **twice, silently, in the direction of reporting nothing**, and each
time the only thing that noticed was a control test feeding it a known-broken input:

1. **`=(?!=)\s*(?!null\s*;)` does not mean "an `=` not followed by null".** When the lookahead
   fails, the engine backtracks the `\s*` to zero width; the lookahead then sees a SPACE rather
   than `null` and succeeds. Every `= null;` counted as a real assignment and the whole scan passed
   vacuously. Needs an atomic group — `(?>\s*)`.
2. **A COMMENT counted as an assignment.** This repo records every deleted field in a note naming
   it, and one reads ``the old catch set `_level = null` alongside…``. The backtick after `null`
   defeats the guard, so the note marked the field seeded — and the scan reported the second bug
   while staying blind to the first, which is the one it was written for. Strip comments before
   asking anything about code.

**A partially-blind detector is worse than no detector**, because it produces findings and
therefore reads as working. The output was "1 item" rather than "0 items", which is the most
convincing possible wrong answer.

**So: validate a detector against the real historical defect, not only a synthetic one.** The
decisive check here was `git stash push -- <the file>`, running the scan against the pre-fix source,
and confirming it named BOTH fields — sensitivity to a case someone invented is weaker evidence
than sensitivity to the case that actually shipped.

Related: [[an-uncoverable-gap-is-usually-your-reader]], [[nothing-is-closed]],
[[a-moves-regressions-are-wiring]],
[[instrument-bugs-outnumber-decoder-bugs]].

## A truncated search is an empty search with a plausible tail

B279's first diagnosis. `grep -rn UpdateClientSideAnimations … | head -6` returned six lines, all
comments and the definition, and "no call site" was concluded — a fix was written for it and a
duplicate call added to production. **The call was the seventh line**, in `MomentScene.Build`.
The `head` was there to keep the tool result short, and it cut off exactly the line that answered
the question.

Second instance the same day: `head -8` on a `simlag` histogram hid the `>=+8` bucket that held a
third of the mass, and the distribution was misread as "mostly −4 and 0" for one round.

**The rule: never cap a search whose ABSENCE you are about to act on.** Cap the ones you are only
skimming. If a result must be short, count it first — `grep -c` — and only then print a slice, so a
truncated list cannot be mistaken for a complete one. A `head` on evidence is a truncated trx total
with the truncation hidden, which is [[read-the-trx-total-not-the-console]] exactly.
