---
name: output-level-assertion-or-it-is-not-done
description: A green unit suite says a component works when called; it never says production calls it. Three no-ops shipped this way in one session — covers the three test levels and why only driving the real UI catches missing wiring, why a code move breaks the assignment that used to be implicit, why a superseded type's tests keep passing while its replacement has none, and why extracting a helper without deleting the copies is not DRY.
metadata:
  type: feedback
---

**Anything that produces output is not finished until an assertion has read that output on a real
demo.**

**Why:** a unit test proves a component behaves when handed values the test chose. It says nothing
about whether production calls it, or with what. That gap shipped **three no-ops in one session**,
each with a fully green suite:

| What shipped | Why the tests missed it |
|---|---|
| Dumper kill annotation matched `int` | game event fields are typed by their definition; `customkill` arrives as a **byte**, so the pattern matched nothing |
| Kill feed annotated nothing at all | the section resolved every field through a renderer returning **strings**, so the numeric lookup returned null on all 407 lines |
| `m_flPlaybackRate` never applied | decoded, retained and unit-tested — and read by no production code, so every animation played at rate 1 |
Every one was found by **looking at the output**. None was found by the tests covering the code, and
in the first two cases those tests kept passing while the feature did nothing.

## "Decoded but not drawn" is NOT this bug — the distinction cost a wrong filing

**Owner's correction, 2026-08-21**, after fog and gestures were filed as further instances:

> *"the decode should basically be completely done for the most part, the core parser got to 100%
> demo decode before i even started anythign else, I required a real demo to be parsed to our quake
> code then recompiled byte identical into a new demo file"*

So the decoder was finished and validated by round trip before any drawing existed. **Every value
the format carries is decoded, and a long list of them is not yet drawn. That is the architecture
working, not a defect.**

| | what happened | how it is found |
|---|---|---|
| **a no-op** (this entry) | production code was SUPPOSED to read a value and did not, so a feature believed finished silently does nothing | looking at the output |
| **not yet drawn** | decode complete by design, drawing not started | reading the backlog |

`m_flPlaybackRate` is the first kind: the animation path existed, should have used it, and every
animation ran at rate 1 while the feature was thought done. Fog and gestures are the second — no fog
or gesture rendering code exists to have missed anything.

**The test cannot tell them apart, and neither can a grep. What tells them apart is whether anything
CLAIMED the feature was done.** For fog something did: four conformance tests counted as *parity* in
`docs/CONFORMANCE.md`, asserting Valve's shader source and then arithmetic transcribed into helpers
in the same file. That is the defect worth filing (B139) — a gap ledger reporting parity for a
feature with no implementation.

**How to apply:** the consumer sweep — asking what reads each decoded type — is a **backlog query**
and a good one, seconds to run. Treat a result as a bug only when something already asserts the
feature works. Related: [[decode-must-be-total]], [[engine-accepts-authored-demos]].

**How to apply:** write the component tests as usual, then add **one** assertion against the rendered
artefact for a corpus demo — the text the dump produces, the poses the timeline builds, the frame the
renderer picks. One test, and the only one that can fail when the wiring is wrong. When it exists,
verify it by manipulation: break the wiring and watch the output test go red while the unit tests
stay green. That pair is the proof it measures something the others cannot.

The same rule from the other side: **a passing test whose inputs were written by whoever wrote the
code proves the two agree**, not that either matches the demo.

Distinct from [[measure-the-output-not-the-capability]], which is about a *report* built from a
predicate rather than from the artefact. This one is about the *test suite* — the failure is not a
wrong number, it is a feature that never ran.

Related: [[real-data-hides-bugs-small-inputs-expose]], [[logs-are-the-debugger]].

**Four more memories were folded into this one on 2026-09-04**, all instances of the same wiring gap
at a different scale: the three test levels and why only the third can fail when wiring is removed, a
whole day of code-move regressions that were never logic errors, a superseded type whose eleven tests
kept passing while its replacement ran unwatched, and a shared helper that did not reduce the
duplicate count it was extracted to fix. Their names are kept as headings below.

---

## `three-test-levels-and-the-third-is-missing`

**A feature can have eleven conformance tests and three real-data tests and still do nothing.** That
was B145: spectator target cycling was declared, bound to `MOUSE1`/`MOUSE2`, given the Source command
names, and covered by three tests — with no production code reading it. Clicking cycled nothing.

**The tests were not wrong.** They asserted that a binding table held what it should, and it did.
*Nothing about a binding table can tell you whether anything consults it*, and a unit test of the
search cannot either, because the search is fine — it is never called.

Three levels are needed, and it is always the third that is skipped:

1. **Conformance**, from the source with citations, written before the code. Says what the engine
   does.
2. **Real data** — a corpus demo, real bytes. Says the rules meet reality. Both of these pass on a
   feature nothing invokes.
3. **The wiring**: drive the real thing. Click the real button in the real window and ask whether
   the code ran. **This is the only level that can fail when the wiring is removed.**

**Verify level 3 by removing the wiring and watching it, and only it, go red.** Done here by
deleting the `CycleTarget` call from the action switch: one UI test failed, thirteen passed. Without
that check, a level-3 test can be as decorative as the others.

**Choosing the level-2 specimen is where this goes quietly wrong.** A POV demo would have passed the
cycling tests while measuring nothing — the committed era POVs are solo recordings, so a cycle finds
one target and stops, which is indistinguishable from a broken search. See
[[author-the-specimen-the-corpus-lacks]]. The claim about *which* player got cycled to belongs to
z1800, a nine-versus-nine match; the UI test only claims the code ran, because the demo it opens
cannot show more.

Related: [[measure-the-output-not-the-capability]].

---

## `a-moves-regressions-are-wiring`

**Moving code does not break the code. It breaks the assignment that used to be implicit.**

Measured across one day of extracting ~1,100 lines out of `MainForm` (B188, B193). Every regression
was the same shape and **not one was a logic error**:

| what moved | what broke | caught by | shipped? |
|---|---|---|---|
| `EnsureWeaponRoles` | the call was dropped; every weapon suffix answered null | an analyzer noticing the method had become unreachable | no |
| `AddViewmodel` | `MomentScene.Viewmodels` was never assigned; **the first-person weapon never drew** | reading the wiring, two commits later | **yes** |
| `ShowMoment`'s upload | `MomentScene.Upload` was assigned NOWHERE; **no entity geometry ever reached the GPU** | the audit below | **yes** |

The viewer suite reported **620/620 green** through all three.

**Why the logic is safe and the wiring is not.** A moved method's body is covered by the tests
written with it, and a compiler catches a broken call. But `new TimelineViewmodels(timeline)` written
INLINE becomes `Viewmodels` written as a property — and a property nobody sets is null, which is a
legal state the guard already handles. The guard was written for "no demo open yet". It cannot tell
that from "nobody wired this".

### The audit, which is mechanical

Enumerate every settable collaborator on the extracted types, then count assignments in the caller:

```bash
grep -rn "public .* { get; set; }" managed/<extracted files>
for p in "_moment.Upload" "_moment.Viewmodels" ...; do
  printf "%-24s %s\n" "$p" "$(grep -c "$p *=" MainForm.cs)"
done
```

**Zero is a regression. One is usually right. Two or three means several lifetimes** (construction,
map load, teardown) and each needs checking separately.

### Three more passes, each of which found something

- **Diff the log STRINGS before and after**, normalising interpolations:
  `git show main:File.cs | grep -oE '"[^"]{12,}"' | sed 's/{[^}]*}/~/g' | sort -u`, and the same over
  the new file plus every file the code moved to. Found a lost `players` column in the slow-moment
  ledger and a lost denominator on a debug line. Most differences are prose inside comments — read
  them, do not count them.
- **Diff the moved BODY against the original**, not its shape. Found `EnsureWeaponRoles` moved
  INSIDE a timer it had been outside of, where its one-off ICE decryption would report as an
  enormous `sampling` spike.
- **Check that a counter which kept its NAME kept its MEANING.** `_samplingTicks` was fed
  `phases.DrawList` for one commit — the draw-list build under a name that means timeline sampling.
  See [[logs-are-the-debugger]].

### How to apply

- **A null or default collaborator must REPORT itself, once there is work it would have done.** The
  null object stays — a real object beats a null field (D83) — but silence is what let three of these
  through. Guard the report on there being something to do (`Vertices.Count > 0`, `FirstPerson`,
  `players.Count > 0`), or it fires from an idle viewer and stops being read. Write the control test
  for that; my first upload warning fired on an empty scene and a control caught it.
- **Assign a demo's sources in ONE place, where the demo arrives**, not wherever each collaborator
  happens to be constructed. Two of the three were missed because the assignments were scattered.
- **Run the audit at the END of a move, not only when something looks wrong.** The worst of the
  three was invisible: nothing drew, no test failed, and no analyzer fired.

Related: [[logs-are-the-debugger]], [[a-partial-thin-view-is-worse-than-none]].

---

## `a-superseded-type-keeps-its-tests`

**When a type is superseded, the call sites move and the tests do not.** The old type keeps a green
suite describing behaviour nothing executes, and the new type inherits the responsibility with no
coverage at all. Nothing fails at the moment the tests stop meaning anything.

**Measured, 2026-08-26 (B206).** `FreeLookState` had `Drag`, `PlaceAt`, `Unplace`, `Fly` and
**eleven tests**, and no production caller anywhere — the only outside reference was a stale name in
a doc comment. `FreeCameraController` had superseded it (D66 created the first, D90/D91 replaced it),
took over every call site, and had **zero** tests. Meanwhile the drag the viewer actually performed
was written longhand inside `MainForm.OnViewportMouseMove` with its own copy of `DegreesPerPixel`.

So the position was exactly inverted: **the mouse look that ran was unwatched, and the one with
eleven tests never executed.**

**Why this is worse than ordinary dead code**, which is the point of the entry. Dead code is waste.
Dead code *with a passing test suite* is a **false negative**: the question "is the drag tested?"
answers yes, correctly, about the wrong object. It is [[measure-the-output-not-the-capability]] one
level up — the instrument reports a capability that exists and is not connected to anything.

**How to apply:**

- **When you supersede a type, grep the old one for production callers before leaving it.** Zero
  callers plus a test file is the signature. This is cheap and nobody does it, because the suite is
  green and the new work is finished.
- **Migrate coverage selectively, and let the arithmetic check you.** Of eleven tests, six `Fly`
  cases were already covered on the live path by `CameraFlightTests` and `FreeFlightPathTests`, so
  porting them would have been duplication; four `Drag` cases and one clamp were not covered
  anywhere and were ported. Net −11 +5, and `build/gate.sh`'s exact floor refused the drop until the
  reasoning was written beside the new number.
- **A floor drop is the moment to justify a deletion, not a step to get past.** The gate's message
  says "if removed, lower the floor" — the comment recording *which* tests went and *why nothing was
  lost* is the artefact that makes the deletion reviewable later.

Related: [[unreachable-can-be-proved-not-just-observed]] (prove it dead, do not assume),
[[one-place-or-it-drifts]] (the duplicate constant that came with it), and B196, where two shipped
features were only ever assigned `null` and the compiler could not see it either.

---

## `extraction-without-adoption-is-not-dry`

Extracting a helper and leaving the copies in place does not remove the duplication — it adds one
more implementation. Measure the count **after** the extraction; if it did not fall, nothing was
fixed.

**Why:** measured here on 2026-08-27. `SdkReference.GameInstall` was extracted precisely because
test files each carried their own Steam-library lookup, and its own remarks record the count at the
time: **seventy-three**. When the shared skip helper was added (D109), the count was **ninety-four**
— the extraction had been done, the old copies had been left as "not this change's business", and
new files had gone on copying a neighbour rather than finding the shared type. Eight files used
`GameInstall`; ninety-four did not.

Worse, the copies had **diverged**. Three of them were independent locators, and two accepted
`TF2_FOLDER` on the folder merely existing while `GameInstall` required the recogniser VPK inside
it. So a stale or mistyped `TF2_FOLDER` made two suites run against the wrong install while every
other suite skipped — the divergence was invisible because each copy worked.

The cost landed as two red CI runs the same day: with the check written from memory in every file,
one of them said `Assert` where it should have said `Assert.Ignore`, and CI — the only machine
without the game — reported a missing install as a defect in the renderer.

**And forty of the ninety-four were not copies of the locator at all — they were a bare
`"F:/SteamLibrary/..."` with no override and no fallback.** Those work on exactly one computer; on
any other the `File.Exists` beside them fails and the test takes its skip branch, so it stops
measuring anything and reports as a skip. Counting the duplicates is what surfaced them; no test
could, because they all pass where they were written.

**How to apply:**

- When extracting, either delete the duplicates in the same change or **record the count and the
  deadline**. "The existing copies are worth migrating and are not this change's business" is how
  seventy-three became ninety-four.
- `grep -c` the pattern the helper replaces, before and after. A DRY change that does not move that
  number has not happened yet.
- Prefer a shape the compiler enforces over one that must be remembered. Deleting the duplicate type
  outright is what makes the next file use the shared one — a `using` that fails to compile is a
  reminder, a helper sitting in another project is not.
- Suspect divergence before assuming the copies are equivalent. They all pass; that is what let them
  drift.
- **Sweep with the counts as the control.** Content.Tests read 682 passed / 13 skipped / 695 total
  before and after; finding the same install by a different route must change nothing, and a moved
  number means a test quietly stopped running.

Related: [[one-place-or-it-drifts]] is the rule this is the failure mode of —
[[edit-files-with-the-file-tools]] is why the sweep has to be done by hand, and
[[read-the-trx-total-not-the-console]] is what the copies were getting wrong.
