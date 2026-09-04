---
name: most-of-a-decoder-is-untested
description: Real files take one path through a format decoder, so sabotage each branch to find which — and a sabotage itself can lie, by not compiling, by not testing the claim it names, by landing outside the algorithm's domain, or by predicting a value that sits on a float boundary.
metadata:
  type: feedback
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
instinct to do that is the wrong move ([[differential-beats-fixtures]], and the four routes to an
insensitive test). Then **write the coverage limit into the class comment**, because the next
reader's default assumption is that a passing suite covers the file.

Related: [[mutation-score-is-not-the-goal]] — the point is knowing which mutants are reachable,
not killing them; [[real-data-hides-bugs-small-inputs-expose]] is the same asymmetry from the
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

Related: [[instrument-bugs-outnumber-decoder-bugs]], [[boundaries-find-what-tests-cannot]].

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
the same shape as [[an-empty-search-needs-a-control]], applied to a dereference.

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
