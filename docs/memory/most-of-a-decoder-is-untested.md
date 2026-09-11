---
name: most-of-a-decoder-is-untested
description: "Real files take one path through a format decoder, so sabotage each branch to find which — and a sabotage itself can lie, by not compiling, by not testing the claim it names, by landing outside the algorithm's domain, or by predicting a value that sits on a float boundary."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-10T22:52:52.428Z
---

A decoder written from a specification handles every case the specification allows. **The real
files take one path.** A green suite therefore verifies that one path and says nothing about the
rest, while reading exactly like proof that all of it works.

**Why:** measured on the studio animation decoder, 2026-08-13. It supports six encodings from
`studio.h`. Four sabotage checks, in order:

- wrong `Quaternion48` z scale — **green**, that path is never taken
- wrong run-length index past `valid` — **green**, at frame zero the other branch always runs, so
  the edit was unreachable
- flipped sign in `AngleQuaternion` — **green**, the Euler path is never taken
- wrong `Quaternion64` z scale — **three failures**, exactly the posed-model tests, six controls
  still green

All nine TF2 player models pose exactly one bone at frame zero — the root — carrying
`STUDIO_ANIM_RAWROT2`. Everything else inherits. So one sixth of the decoder is proven and five
sixths are unproven code that will meet its first real input in production.

**How to apply:** after writing a format decoder, sabotage each branch and record which ones the
corpus can actually kill. Two of the three green results above were *unreachable-condition*
failures, not weak assertions — strengthening the assertion would have done nothing, and the
instinct to do that is the wrong move ([[fixtures-are-the-weak-point]], and the four routes to an
insensitive test). Then **write the coverage limit into the class comment**, because the next
reader's default assumption is that a passing suite covers the file.

Related: [[mutation-score-is-not-the-goal]] — the point is knowing which mutants are reachable,
not killing them; [[fixtures-are-the-weak-point]] is the same asymmetry from the
input side; [[logs-are-the-debugger]] is how the one live path got identified (logging the posed
bone count and its values, rather than guessing which branch ran).

**Six more memories were folded in on 2026-09-04**, all about the sabotage itself going wrong in
one of four ways: it does not compile, it changes behaviour without testing the claim, it gets
inverted rather than disabled, it lands outside the algorithm's domain, or the prediction it is
checked against sits on a float boundary. Their names are kept as headings below.

---

## `a-sabotage-must-compile` — a build failure is not a red test

Verification by sabotage is the house rule: break the code on purpose, watch the RIGHT test fail,
restore with a precise inverse edit. Two ways it silently fails to verify anything, both measured
2026-09-03.

**A sabotage that does not compile is not a red test.** Deleting an expression stranded two
parameters, and SonarAnalyzer promoted "unused parameter" to a build error, so no assembly was
produced and no test ran. The result reads as failure and is not evidence: nothing was measured. The
delegated agent reported it honestly as inconclusive rather than counting it. Rewrite the sabotage to
keep every symbol used — invert the value, clamp it to a constant, swap an index — so the code still
builds and only the BEHAVIOUR changes.

**A fixture must set the field production reads, not the one that looks canonical.** Writing
`STUDIO_AUTOPLAY` into a hand-built `.mdl` body was the faithful-looking choice and set a field
nothing on that path reads: the model's sequence flags reach the draw path through hand-built
`StudioSequence` records, not through the bytes. The test stayed red with the implementation
correct — which is indistinguishable from a wrong fix, and is the failure mode that sends you back
to rewrite working code.

**Why:** both turn "no evidence" into something that looks like evidence. A red test is a claim about
behaviour; a build failure and a mis-fed fixture are claims about the toolchain and the harness, and
neither says whether the test can detect the thing it names.

**How to apply:** before believing a sabotage, confirm the run produced a PASS/FAIL summary and that
the failing test names are the ones predicted — a compile error in the output means start over. When
a test stays red after a fix you believe in, check the fixture reaches the field under test before
suspecting the code. See [[instrument-bugs-outnumber-decoder-bugs]] and
[[output-level-assertion-or-it-is-not-done]].

---

## `a-sabotage-that-reddens-nothing-names-the-missing-input`

**A sabotage that reddens nothing is a result, not a null result.** It says the suite contains no
input for which correct and broken differ — and because you know precisely what you broke, it also
tells you what that input would have to look like. Write that test.

Twice in one session, both on code that was correct:

- **`RagdollFade`.** Removing the engine's early `return` from the visible branch left
  `Gone_ForACorpseWatchedThroughout_IsNeverTrue` green: watching a corpse from before its deadline
  re-arms the timer ahead of the clock on every call, so the stale expiry check never fires. The
  distinguishing input is a corpse first checked while visible AFTER its unseen deadline.
- **`QuatInterpBones`.** Removing `MathF.Abs` from the dot product left all four conformance tests
  green: every fixture's half-angles fall in [-π/2, π/2], so no dot product was ever negative and
  the call was a no-op on the whole suite. The distinguishing input is a trigger stored as its own
  NEGATION — the same rotation, opposite sign, raw dot −1.

Both tests looked like they covered the line. Neither could.

**The instinct to resist is strengthening the assertion.** Both suites already asserted exact
values; nothing about the assertions was weak. It is the CONDITION that was wrong — `CLAUDE.md`'s
second failure route — and no amount of tightening a prediction fixes an input for which both
answers agree.

**So the routine after writing a conformance suite is: sabotage each line the suite claims to cover,
and treat a green run as a to-do rather than a pass.** Cheap, and it found two real gaps in one day.
Delegating it works — the sabotage-verifier reached the same missing input from the opposite
direction, having watched nothing redden while I predicted it from the edit.

**Two ways the sabotage itself can be at fault, and both happened here**, so rule them out before
believing the finding:

- **It did not test the claim.** An edit that changes behaviour is not automatically one that
  removes the property under test — see the entry below on a sabotage that changes behaviour
  without testing the claim.
- **It did not compile.** Strict analyzers refuse many of the obvious edits: `if (false)` is CS0162
  and orphans a constant (S1144/CA1823), deleting a branch trips S1199, and removing the only call
  to a private method makes it unused. **"It would not build" is a reason to find another edit, not
  a reason to conclude the test is weak** — the fallback branch that resisted three sabotages was
  proved sensitive by pointing it at the LAST trigger instead of the first.

**A zero from a new instrument is the same shape and gets the same treatment.** The magnitude added
to prove `QUATINTERP` mattered reported `furthest move 0 units` across ten driven bones. That is not
evidence the rule does nothing — it measured the bone's TRANSLATION, and `hlp_forearm` is a twist
that rotates about a fixed origin, so translation is identical by construction. Measuring the axes
as well gives 0.72 units at unit distance, a 42-degree twist. **An instrument written that same hour
to separate "it ran" from "it mattered" was itself measuring the one quantity that could not
change** — so before believing a zero, ask which variable actually carries the effect.

### A fifth diagnosis: the check is right and the CLAIM about it is wrong

**Added 2026-09-10.** The four causes above all locate the fault in the suite or in the sabotage.
There is a fifth, and it is the one to reach for when the thing sabotaged is a *guard* rather than a
decoder: the guard works, the sabotage was a fair test of it, and what the green run refutes is the
sentence written above the guard about what it catches.

`build/assert-risk-citations.sh` was written after renaming B386 to B389 across nine files, with a
header saying it *"is the check that would have caught that miss"*. Sabotage: revert one citation to
B386. **It passed** — the other session's B386 entry exists, so a stale citation resolves, at
somebody else's bug. Nothing textual separates that from resolving correctly. The second sabotage, a
number nobody has taken, failed correctly and named the file and line.

So the check catches a citation that arrives *nowhere*, which is a narrower and still useful thing,
and the false half was the header. **The instinct to resist here is widening the check** — there is
no version of it that detects a collision between two live numbers after the fact.

**How to tell this apart from a missing input:** ask whether a test could exist that reddens. For a
missing input the answer is yes and you write it. Here it is no, and the fix is to correct the
sentence — in the script header, in the risk entry, and in the gate comment that repeated it.

**And wiring it up reddened the gate on the check itself:** the file is inside its own search, and
the header spelled the sabotage number out, making the header a dangling citation. A guard held to
its own promise will catch its own documentation.

Related: [[instrument-bugs-outnumber-decoder-bugs]], [[boundaries-find-what-tests-cannot]],
[[state-the-assumptions-the-owner-can-falsify]].

A second section under this name, further down, sorts a null result into its three possible causes
and carries the `ShouldCollide` worked example.

---

## `a-sabotage-can-change-behaviour-without-testing-the-claim`

**A sabotage that changes behaviour is not automatically one that tests the claim.** Check that the
mutation reproduces the SPECIFIC defect, and that the test which reddened is the one whose claim it
attacks.

B313, 2026-09-04. The claim was that dereferencing an entity handle by MASKING resolves a dangling
handle to a real, different entity (B231). The sabotage written for it was:

```csharp
(handle & 2047) is var student        // irrefutable — always true
```

`is var` always matches, so the guard became unconditionally true and the method returned null for
every input. That reddened the HAPPY PATH — the same shape as any broken-key mutation — while the
invalid-handle test it was aimed at stayed green. **Behaviour changed, a test failed, and nothing
about masking was exercised.**

The real sabotage keeps the mask AND the lookup: `int student = handle & 2047;` then look it up.

**And it exposed that the test could not have failed anyway.** Masking gives slot 2047; nothing
occupied it, so the lookup found nothing and answered null — **the same null correct code returns,
for a different reason**. Correct and broken agreed on every observation. The fix was the fixture:
put a bystander at 2047 on the other team, so masking answers RED where resolving answers nothing.

**The general rule: for an absence claim, the wrong answer must be REACHABLE.** A test asserting
"resolves to nothing" is vacuous unless something is standing where the broken code would look —
the same shape as [[instrument-bugs-outnumber-decoder-bugs]]'s empty-search rule, applied to a dereference.

**A subagent that flags its own sabotage as inconclusive is doing the job.** It would have been
easy — and wrong — to substitute an edit that produced the expected red.

---

## `an-inverted-flag-is-not-a-disabled-flag`

**Read which sabotage a subagent actually performed, not which one it was asked for.** Two edits to
the same condition can prove opposite things, and the report reads identically.

B269. The instruction was: make the loop-aware blend *always take the plain branch*, so that
`At_ALoopingPoseParameterAcrossTheWrap_TakesTheShortWay` — the test the whole loop-flag seam exists
for — is shown to be failable. What the agent did was **invert** the condition, so looping and
non-looping swapped. That reddened `At_ANonLoopingPoseParameterAcrossTheSameGap_Interpolates`, the
CONTROL, and left the looping case still passing. It reported "sensitive" and it was not wrong about
what it measured; it measured something else.

**Why the distinction is not pedantic.** Disabling asks "does anything depend on this being on?".
Inverting asks "does anything depend on this being right way round?". A test that only reads the
flag's *presence* survives inversion; a test that reads its *effect* survives neither. Only the
first question tells you whether the feature is load-bearing.

**Two things followed, and both are the practice now:**

- **Say what the sabotaged code must DO, not which line to touch.** "Force the plain branch" is a
  specification; "change the condition on line 1692" invites any edit to that line.
- **The analyzers can block the obvious sabotage, and that is a hint to move up a level.** Replacing
  the condition with `false` here tripped CA1822/S2325 — the method no longer touched instance data
  and had to be static. Widening `LoopingLerp`'s own `>= 0.5f` threshold to `>= 2f` was the clean
  inverse edit: it makes the wrap unreachable without changing any signature, and it reddened the
  right test plus three animation-cycle tests that share the helper.

See [[instrument-bugs-outnumber-decoder-bugs]] for the family this belongs to, and
[[one-subagent-and-prefer-cheap-models]] for what a cheap model is fine to be trusted with —
sabotage still qualifies, provided the result is read rather than accepted.

### A sabotage also tells you what a test was actually measuring

Same session, B273. Two corpus tests were written to cover the applied-time stamping and both
looked right. Severing the stamping — dropping the lag from `track.Add` — left **both green**: they
asserted on the lag HISTOGRAM, which is measured beside the stamping rather than through it.

Nothing about reading those tests suggests that. They name the right subject, use real demos, and
would have been believed. The sabotage is what separated "covers the change" from "mentions the
change", and the fix was a third test reading the number the interpolation actually used out of the
track — which reddens.

So run the sabotage even when the tests are yours and you are confident. The question it answers is
not "did I write a test" but "does anything fail when the feature stops working".

---

## `a-fixture-can-be-outside-the-algorithms-domain`

**When a numeric test misses by a little, check whether the fixture asked for something the
algorithm is entitled to refuse — before assuming the code is wrong.**

B311, 2026-09-04: an IK lock test predicted the effector back at y = 0 and measured 0.054. That
looks exactly like a real IK bug. It was not. The fixture's chain had links of ±2, giving a reach of
20.40, and moving the root five units put the pinned target 20.62 away — **out of reach**, so
`Studio_SolveIK` correctly placed the foot as close as it could get. Links of ±5 give 22.36 and the
same test lands exactly.

**The tell is a SMALL, non-zero error in a solver, clamp, or search.** Those all have a domain and
all degrade gracefully at its edge, which is precisely what makes the failure look like a bug:
a wrong implementation and a refused input both land near the answer.

**Ask what the algorithm does at its limit, then check whether the fixture is inside it.** Reach,
range, a `StraightEnough` refusal, a clamp, a maximum iteration count — each is a documented edge,
and a fixture built without arithmetic tends to sit on one because the round numbers a person picks
are also the degenerate ones.

**Build fixtures with SLACK, and say in the comment how much.** The same file already needed a chain
that was not perfectly straight, because the solver refuses one at full extension — two different
edges of one algorithm, both hit by the obvious fixture.

Cousin of the entry below on predictions sitting on a float boundary — there the input itself was
outside the domain, here the arithmetic about the input was wrong. Both present as "the code is
slightly off".

---

## `predictions-must-not-sit-on-a-boundary`

**A test prediction computed in exact decimal, measured on a float path, must not land on an
integer boundary.** Twice in two days, both times the code was right and the prediction was wrong:

- B307: window 0.2 to 0.7 at cycle 0.25 gives `0.05f / 0.5f` = 0.099999994, so 30 frames of it is
  2.9999998 and the index floors to **2**, not 3.
- B309: `frac(3.30)` is `3.3f - 3` = 0.29999995, so 30 frames of it is 8.999998 and floors to **8**,
  not 9.

Both were investigated as defects first. Neither was.

**Pick inputs whose answer lands mid-frame.** 0.1 to 0.9 at cycle 0.25 gives 5.625; time 3.35 gives
10.5. Rounding cannot reach a neighbour from there, so the prediction survives any reasonable
change in float ordering.

**The tell is a predicted value that is a round number**, especially a whole frame index with a
fraction of exactly 0 or 1. That is where to suspect the prediction before the code — see
[[nothing-is-closed]] for the same rule from the other side, where the input was wrong rather than
the arithmetic about it.

Assert the fraction as well as the index when the subject is a position in an animation: it turns a
one-off boundary coincidence into a two-number prediction that cannot be satisfied by accident.

---

## `a-sabotage-that-reddens-nothing-names-the-missing-input` — a null result is a measurement

**Break the code on purpose, watch nothing fail, and the instinct is to call the sabotage a dud.**
It is a measurement. It says: for every input the suite supplies, correct and broken predict the
same observation. That is a statement about the INPUTS, so write the input rather than strengthen
an assertion.

**Three outcomes, and they need different answers:**

1. **The behaviour is genuinely unreachable from any input.** `.phy` files all end with a trailing
   `editparams` block, so removing the reader's final block-close changes no count on any shipped
   file — and the line is still load-bearing, because the format does not require that block. The
   answer was an authored specimen ending on its last joint. See
   [[author-the-specimen-the-corpus-lacks]].
2. **The two positions are genuinely equivalent today.** IVP's event loop tests its stop flag after
   the fire; moving that test to the top of the body reddens nothing, because the only thing between
   the two placements is a pure read. Keep the engine's placement, say out loud that no test can
   tell, and note what would make it observable again.
3. **The code is wrong and the missing input is what would have shown it.** This is the one that
   gets missed, because a null result feels like nothing happened.

**The worked example of the third, 2026-09-06.** `ShouldCollide` tested a ragdoll's
`selfcollisions` flag before consulting its pair list — which reads as obviously right. Removing
that test reddened nothing. The reason was that the only case exercised had an EMPTY pair list,
where both readings agree; and chasing the missing input showed the check was **wrong**. Nothing in
the engine ever calls `DisableCollisions`, so a pair enabled BEFORE the flag went off stays enabled
for the ragdoll's life. The flag belongs in the parser, where it decides what enters the list, and
testing it a second time would have dropped pairs the engine keeps.

**Why:** a sabotage measures the suite's sensitivity, and insensitivity has a cause. Two of the
three causes are about the tests; the third is about the code, and it is indistinguishable from the
others until the distinguishing input is written. Treating a null result as "nothing to do" throws
away the only signal that pointed at it.

**How to apply:** never move on from a sabotage that reddens nothing. Ask what input would separate
correct from broken, and then write it — the act of constructing it is what exposes case 3. If no
such input can exist, say which of case 1 or case 2 it is, in the source, next to the line. The
`a-duplicated-guard-cannot-be-tested` and `unreachable-can-be-proved-not-just-observed` sections
below are the two disciplined forms of cases 1 and 2. Related:
[[two-accumulators-cannot-see-order]].

---

## `a-duplicated-guard-cannot-be-tested` — fix the input, never the assertion

**A test for a guard that is redundant with a downstream guard cannot fail, and no assertion fixes
it.** Measured on 2026-09-05 while building B353.

The code reproduced Valve's `if ( iBodyOverride > -1 && iBodyStateOverride > -1 )` before calling
`SetBodygroup`. The test wore an item declaring the part but no state and asserted a body of 0.
**Deleting the state clause reddened nothing** — and not because the fixture chose a poor value:
`SetBodygroup` already returns the body unchanged for a negative value, in this code and in Valve's
(`shared/animation.cpp:863` returns early for an out-of-range value). There is no integer for which
the guarded and unguarded versions disagree. The clause is behaviourally dead **in the engine too**.

**Why:** the test asserted "nothing happened", and nothing happening is what BOTH versions do. This
is the `CLAUDE.md` **wrong condition** trap, and the instinct it defeats is the usual one — the
assertion was already exact.

**The fix is to the INPUT.** Setting a part to 0 is only observable from a body that is not already
0, so the item was given a named entry as well: it hides `hat`, a correct read leaves 1, and a
reader treating the missing state as 0 puts the part back and reads 0. That version reddens alone
under exactly the mutation it was written for, which was then verified by making it.

**Ask this before writing the assertion**, and it is a different question from "is my assertion
tight enough":

> Is there an input for which the correct and broken versions predict different observations?

**Keep the guard.** It is where Valve writes it and the citation is the point — but document it as
redundant with the downstream check rather than leaving the next reader believing it load-bearing.
That is [[a-guard-you-remove-may-be-the-mechanism]] read from the other end: there the narrow version
refused something, here it refuses nothing.

**The sabotage that found this was a subagent's**, run against tests that all passed.

**Analyzers at error level make a lazy sabotage impossible**, which cost three attempts in the same
session: `&& false` is S1125, dropping a call left a private method unreferenced (S1144), and
`x = 0` on an int field is CA1805. A sabotage must compile, so pick one that keeps every symbol
used — OR-ing `int.MaxValue` into a flag set, or `+ 500` on an index. See
[[tests-before-codecs]].

---

## `a-stability-test-needs-seventeen-items` — .NET hands short runs to a stable sort

A test asserting that a sort is **stable** proves nothing unless the input is **larger than
sixteen items**. `Array.Sort` / `List.Sort` are introsort, and `ArraySortHelper`'s
`IntrosortSizeThreshold = 16` hands any partition of sixteen or fewer to **insertion sort**, which
is stable — so a short run comes out in order whether or not the comparison carries a tiebreak.

Measured 2026-08-27 on `OpaqueBuckets.InDrawOrder`. A six-element test survived deleting the
`Order.CompareTo(...)` tiebreak entirely. At twenty-four it failed immediately, because
`PickPivotAndPartition` swaps the middle element to `hi - 1` before comparing anything, so an
all-equal run is reordered on the very first partition.

**Why:** this is [[boundaries-find-what-tests-cannot]]' "wrong condition" case — an input for
which the correct and broken implementations predict the *same* observation. The instinct on
finding a test that will not go red is to strengthen the assertion; here the assertion was already
exact (`ShouldBe` on the full sequence) and only the input was too small. See
[[fixtures-are-the-weak-point]] for the mirror image, where the input was too *large*.

**How to apply:** any test whose subject is ordering-among-equals needs at least 17 items, and 24
is a safer round number. Before trusting it, delete the tiebreak and watch it fail — a stability
test that has never been red is measuring insertion sort, not your comparison. The same threshold
question applies to any claim about a library's algorithm: ask what size the implementation
switches strategies at, because that size is where the test becomes sensitive.

---

## `sample-between-the-knots` — every curve agrees at its own control points

**Never assert a curve's shape at one of its own control points.** At a knot the interpolation
parameter is exactly 0 or 1, so the basis functions collapse and *every* scheme — Hermite, Catmull-
Rom, cosine, a plain lerp — is mathematically forced to return the stored value. The assertion reads
the table back and never touches the curve.

B348, 2026-09-05: `Degrees_AtTheMiddleControlPoint_OvershootsPastSixty` sampled `0.7519`, which IS
the middle control point's X. Replacing Valve's `Hermite_Spline` with a one-line lerp left it green —
and left the whole eight-test conformance suite green, because six of the eight never called the
function at all. **The overshoot was the entire reason the entry existed and nothing pinned it.**

**Two failure modes from `CLAUDE.md`, in one suite:**

- **Wrong condition** — the knot. Fix the INPUT: sample strictly between control points. At 0.4 the
  spline gives 34.818° where a lerp gives 33.806°, a full degree apart.
- **Effect size below resolution** — the neighbouring boundary test at `0.9999` *is* strictly
  inside a segment, but there the curves differ by 0.0014° against a 1e-2 tolerance. Being inside a
  segment is not enough; the sample has to be where the difference is large.

**How to apply, to any interpolation:**

1. Sample at a fraction with no special relationship to the control points — mid-segment, not an
   endpoint and not a knot.
2. Compute the expected value BY HAND from the engine's formula and assert it exactly. Do not read
   it off a run; that fits the test to the code.
3. State what the wrong implementation would give, in the message. `"a plain lerp gives 33.806, a
   full degree lower"` makes the margin visible instead of implied by a tolerance nobody re-derives.
4. Check the tolerance against the DIFFERENCE, not against floating-point noise.

**And count how many tests actually call the function.** Six of eight conformance tests exercised
`Spline`, `Angle` or `Fraction` directly and never `Degrees`, so a change scoped inside `Degrees`
was invisible by construction. A suite named for a mechanism is not a suite that covers it.

Related: [[fixtures-are-the-weak-point]], [[instrument-bugs-outnumber-decoder-bugs]],
[[output-level-assertion-or-it-is-not-done]].

---

## `unreachable-can-be-proved-not-just-observed` — prove it by arithmetic, or write the input

Closing the last of this repository's reachable coverage (2026-08-19) split every gap into exactly
two kinds, and treating them the same is what leaves both unresolved.

**Kind one: nothing has written the input yet.** Most gaps. They look unreachable because a demo
cannot produce them — a stated count a body cannot support, an assembly cut mid-block, a property
definition no schema emits. The right answer is to build the input, and the fact that a recording
cannot is the reason the branch matters: it is what decides whether a wrong file gets diagnosed or
silently mis-decoded.

**Kind two: the branch is genuinely dead, and it can be shown.** `LoopingCurve`'s re-check has an
`else` arm that cannot run. It is reached only after `p1` has been raised into `[1, 2)`, and every
path there leaves `p0` below `p1`: either `p0` was untouched and is under 1, or the first pass
raised it, which happens only when `p0 < p1` and raising both preserves the order. A third case
would need the first pass to have raised `p1`, but then `p1 >= 1` and the `p1 < p2` test guarding
the block cannot hold against a `p2` in `[0, 1)`. That is a proof, not an observation, and it does
not go stale the way "no demo does this" does.

**Why:** the two kinds are indistinguishable in a coverage report and demand opposite work. Chasing
kind two writes contorted tests that never pass; dismissing kind one as "unreachable" is how a
guard ships untested. See [[the-denominator-decides-what-can-be-lost]] — the default assumption
should still be kind one.

**How to apply:** for a gap that resists, do the arithmetic on what can reach it. If it is dead,
**keep the code** when it is a transcription (Valve's own `LoopingLerp_Hermite` has the same arm,
and deleting it makes the two harder to compare) and put the reasoning in the remarks beside it, so
the gap reads as a recorded conclusion rather than an oversight. If it is not dead, the input is
writable — see [[author-the-specimen-the-corpus-lacks]].

Two other things this pass established, both worth reusing:

- **State the property over every case at once when the cases share a code path.** "No registered
  user-message name decodes a 4096-bit body" is one test covering forty layouts, and it covers the
  forty-first the day it is added. It found a real defect that forty per-message tests would each
  have passed.
- **Every refusal test needs a sensitivity control in the same file.** Assertions that something
  did NOT happen are all satisfied by a method that fails unconditionally, and a decoder that
  refused everything would look identical.

---

## `an-environment-only-setting-is-untested` — a process-wide variable has no per-test observer

**Before adding a setting that only an environment variable can reach, ask which test will set it.
If the answer is "a test would have to change the whole run", it is an option, not a variable.**

**Why:** a process-wide variable is process-wide. A test that sets one sets it for every other test
in the same process — including the ones whose whole point is that the behaviour is OFF. So the
setting ends up with no coverage, and the absence is invisible because everything around it is
green.

Measured on 2026-08-29: `TF2VIEW_AUTOPLAY` had **exactly one reference in the entire repository —
its own declaration.** No script set it, no test set it, no CI job set it, no document mentioned it.
Its ordering requirement then broke **three separate times**, twice recorded in `DemoSystems.Open`'s
own remarks and the third found only by launching the viewer and reading the log (B223, D118).

The trap is that the reasoning *for* the variable is sound at every step. The comment beside it read
*"a system that read one could not be tested without setting it for the whole run"* — correct — and
concluded that the WINDOW should be the one place that reads the environment. Also correct, and it
answers a different question. Nothing in that chain asks whether the SETTING is tested, only where
the read belongs.

**How to apply:** make it an option or a config command; keep the variable working alongside if one
already exists, because a shell somewhere may export it and dropping it is a silent regression. Then
the test is `new MainForm("--autoplay", path)` — one line, isolated, per-launch.

The general shape: **a design that is defensible locally can still leave a feature with zero
observers.** Count the references before trusting the design.

Related: [[output-level-assertion-or-it-is-not-done]],
[[measure-the-output-not-the-capability]],
[[logs-are-the-debugger]], [[one-place-or-it-drifts]].
