---
name: most-of-a-decoder-is-untested
description: "Real files take one path through a format decoder, so sabotage each branch to find which — and a sabotage itself can lie, by not compiling, by not testing the claim it names, by landing outside the algorithm's domain, or by predicting a value that sits on a float boundary."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-10T22:52:52.428Z
---

A decoder written from spec handles every case the spec allows; **real files take one path**. A
green suite verifies that one path and reads like proof for the rest.

**Measured on studio animation decode** (six encodings possible): four sabotages, three came back
green because the sabotaged branch is never taken (all nine TF2 player models pose exactly one bone
at frame zero); the fourth (wrong `Quaternion64` z scale) failed correctly. **One sixth of the decoder
is proven; five sixths is unproven code.**

**How to apply:** sabotage each branch, record which the corpus can actually kill. Two of the three
green results were unreachable-condition failures, not weak assertions — strengthening wouldn't help.
Write the coverage limit into the class comment.

Related: [[mutation-score-is-not-the-goal]], [[fixtures-are-the-weak-point]], [[logs-are-the-debugger]].

---

## `a-sabotage-must-compile` — a build failure is not a red test

**A sabotage that doesn't compile is not a red test** — SonarAnalyzer promoted an "unused parameter"
to a build error, so no assembly was produced and nothing was measured; treated correctly as
inconclusive, not a pass. **A fixture must set the field production reads, not the canonical-looking
one** — writing a flag into a hand-built `.mdl` body that nothing on the actual draw path reads left a
test red with correct code, indistinguishable from a wrong fix.

**How to apply:** confirm the run produced a real PASS/FAIL naming the predicted tests; a compile
error means start over. Check the fixture reaches the field under test before suspecting the code.

---

## `a-sabotage-that-reddens-nothing-names-the-missing-input`

**A sabotage that reddens nothing is a result, not a null result** — it says the suite has no
distinguishing input, and since you know what you broke, it tells you what that input looks like.
Two examples where correct code stayed green under sabotage: a corpse-fade timer's early `return`
removed left green because no fixture checked a corpse first seen while visible AFTER its deadline; a
`MathF.Abs` removed from a dot product left green because every fixture's half-angles fell inside a
range where the dot product was never negative.

**The instinct to resist is strengthening the assertion** — it's the CONDITION that's wrong, not the
prediction. Sabotage each line a conformance suite claims to cover; treat a green run as a to-do.

**Two ways the sabotage itself is at fault:** it didn't test the claim (see next section); it didn't
compile (strict analyzers refuse many obvious edits — find another edit, that's not evidence the test
is weak).

**A zero from a new instrument gets the same treatment** — a magnitude check reporting "furthest move
0 units" measured the wrong axis (translation on a pure-rotation twist); measuring the right axis gave
0.72 units.

### A fifth diagnosis: the check is right and the CLAIM about it is wrong

Sometimes the guard works, the sabotage is fair, and what's refuted is the sentence written above the
guard about what it catches. `build/assert-risk-citations.sh` was written after renaming B386 to B389
across nine files, with a header claiming it "is the check that would have caught that miss" —
sabotage (revert one citation to B386) passed, because the other session's own B386 entry exists, so
a stale citation resolves, at somebody else's bug. The check catches a citation resolving nowhere,
narrower and still useful; the false half was the header's overclaim. **Ask whether a test could
exist that reddens** — if no, correct the sentence, not the check.

Related: [[instrument-bugs-outnumber-decoder-bugs]], [[boundaries-find-what-tests-cannot]],
[[state-the-assumptions-the-owner-can-falsify]].

---

## `a-sabotage-can-change-behaviour-without-testing-the-claim`

**A sabotage that changes behaviour is not automatically one that tests the claim.** B313: the claim
was that masking an entity handle resolves a dangling handle to a real, different entity (B231). The
sabotage made a guard irrefutably true (`is var` always matches), returning null for every input —
reddening the HAPPY PATH while the actual invalid-handle test stayed green. **It also exposed the test
couldn't have failed anyway** — masking landed on an empty slot, giving the same null as correct code
for a different reason. Fix: put a bystander at the masked slot so masking and resolving disagree.

**For an absence claim, the wrong answer must be REACHABLE** — same shape as the empty-search rule
applied to a dereference.

**Read which sabotage a subagent actually performed, not which one was asked for** (B269) —
inverting a condition (swapping two branches) proves something different from disabling it (forcing
one branch always); a test reading only the flag's PRESENCE survives inversion, one reading its
EFFECT survives neither. **Say what the sabotaged code must DO, not which line to touch.**

**A sabotage also tells you what a test was actually measuring** (B273) — severing a timestamp's
stamping left two "covering" tests green, because both asserted on a histogram measured beside the
stamping rather than through it.

---

## `a-fixture-can-be-outside-the-algorithms-domain`

**When a numeric test misses by a little, check whether the fixture asked for something the
algorithm is entitled to refuse.** B311: an IK test predicted an effector position and measured 0.054
off — looked like a real bug. The fixture's chain couldn't physically reach the pinned target, so the
solver correctly placed the foot as close as possible. **The tell is a SMALL, non-zero error in a
solver/clamp/search** — those all degrade gracefully at their domain edge, which looks exactly like a
bug. Build fixtures with slack, and say how much in the comment.

---

## `predictions-must-not-sit-on-a-boundary`

**A test prediction computed in exact decimal, measured on a float path, must not land on an integer
boundary.** Twice, both times the code was right: B307's window computation landed at 2.9999998,
flooring to 2 not the "expected" 3; B309's fraction computation similarly floored one short. **Pick inputs whose
answer lands mid-frame** so rounding can't reach a neighbour. Tell: a predicted value that's a round
number, especially a frame index with fraction exactly 0 or 1. Assert the fraction as well as the
index to make the prediction two numbers, immune to accidental satisfaction.

---

## `a-duplicated-guard-cannot-be-tested` — fix the input, never the assertion

**A test for a guard redundant with a downstream guard cannot fail, and no assertion fixes it.**
Measured building B353. Deleting a guard clause reddened nothing, because the downstream function ALSO handles the invalid
case identically — the clause is behaviourally dead in the engine too. **Fix is to the INPUT**: give
the test a value where the guarded and unguarded paths would actually disagree. **Keep the guard**
(it's where Valve writes it) but document it as redundant, not load-bearing.

Ask: is there an input for which correct and broken predict DIFFERENT observations? — different from
"is my assertion tight enough".

---

## `a-stability-test-needs-seventeen-items` — .NET hands short runs to a stable sort

**A test asserting a sort is stable proves nothing under sixteen items** — `IntrosortSizeThreshold =
16` hands short partitions to insertion sort (stable regardless of tiebreak). A six-element test
survived deleting the tiebreak entirely; at twenty-four it failed immediately. **Any
ordering-among-equals test needs 17+ items** — delete the tiebreak and watch it fail first.

---

## `sample-between-the-knots` — every curve agrees at its own control points

**Never assert a curve's shape at one of its own control points** — at a knot, every interpolation
scheme (Hermite, lerp, cosine) is forced to return the stored value. B348: a test sampling exactly at
the middle control point stayed green replacing a Hermite spline with a lerp, along with six of eight
"covering" tests that never called the function at all.

**How to apply:** sample at a fraction with no special relationship to control points; compute the
expected value BY HAND from the engine's formula; state what the wrong implementation would give in
the message; check tolerance against the DIFFERENCE, not float noise; count how many tests actually
call the function under test.

Related: [[fixtures-are-the-weak-point]], [[instrument-bugs-outnumber-decoder-bugs]],
[[output-level-assertion-or-it-is-not-done]].

---

## `unreachable-can-be-proved-not-just-observed` — prove it by arithmetic, or write the input

Every coverage gap splits into two kinds:

**Kind one: nothing has written the input yet.** Most gaps — a demo simply can't produce them. Build
the input; the fact a recording can't is why the branch matters (deciding whether a wrong file is
diagnosed or silently mis-decoded).

**Kind two: the branch is genuinely dead, provable by arithmetic.** A re-check `else` arm proven
unreachable by tracing every path that reaches it — a proof, not an observation, that doesn't go
stale.

**How to apply:** for a resistant gap, do the arithmetic on what can reach it. If dead, keep the code
(if it's a Valve transcription — deleting makes comparison harder) and record the reasoning beside
it. If not dead, the input is writable — [[author-the-specimen-the-corpus-lacks]].

**Also established:** state a property over every case at once when cases share a code path (one test
covering forty layouts finds real defects forty per-message tests would each pass). Every refusal
test needs a sensitivity control in the same file.

---

## `an-environment-only-setting-is-untested` — a process-wide variable has no per-test observer

**Before adding a setting only an environment variable can reach, ask which test will set it — if
the answer is "a test would have to change the whole run", it's an option, not a variable.** An
autoplay env var had exactly ONE reference in the entire repo (its own declaration); no test, script
or CI job set it, and its ordering requirement broke three times before being caught by launching the
viewer and reading the log (B223, D118).

**The trap:** the reasoning FOR the design is sound at every step ("a system reading it couldn't be
tested without setting it for the whole run" → "the WINDOW should read the environment") — correct,
and answers a different question than whether the SETTING is tested.

**How to apply:** make it an option/config command, keep the env var working alongside for
compatibility (D118). Count references before trusting a design — a locally defensible choice can
still leave a feature with zero observers.

Related: [[output-level-assertion-or-it-is-not-done]], [[measure-the-output-not-the-capability]],
[[logs-are-the-debugger]], [[one-place-or-it-drifts]].
