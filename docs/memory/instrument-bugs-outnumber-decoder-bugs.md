---
name: instrument-bugs-outnumber-decoder-bugs
description: "On this project the tests and diagnostics have been wrong far more often than the code they check — the casebook of every way an instrument goes blind, cause-surviving defects, unread instruments, sampled and mis-keyed logs, thresholds blind to sums, counts hiding identity, execution mistaken for effect, ledgers with uncovered exits, re-derived cameras, and a clean-checkout rerun mistaken for a second instrument."
metadata: 
  node_type: memory
  type: project
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-10T22:52:31.207Z
---

**Across this project the code under test was right almost every time and the measurement was wrong
repeatedly.** Recorded because the instinct on a bad number is to suspect the code, and here that
instinct has been wrong more often than not.

This is the project's casebook for the global standard's *"a test that cannot fail is an experiment
insensitive to the manipulation"*. **Six memories were merged into it on 2026-08-27** — the index
had grown a separate entry per example, which buried the one thing they all say. Their names are
kept as headings below. **Ten more were folded in on 2026-09-04** for the same reason, at ten times
the scale — see the sections below the original six.

---

## The original five, all 2026-08-12/13

- **A control that could not fail.** "The flat lightmap set must read byte-identically" is blind
  to the set count, because the sets term cancels when the style is zero. Forcing every face to
  four sets passed it. Length arithmetic — each face's span must reach the next face's offset —
  was what could fail.
- **A picture test that passed on the headline defect.** It survived a shader reading lightmap
  set 0 three times instead of sets 1, 2 and 3, because the twelve ssbump materials still changed
  under that sabotage. Threshold recalibrated between two measured values, 9,502 and 24,096.
- **A winding assumption.** A point-in-polygon test assumed a fixed winding, reporting 0% decal
  coverage with a bimodal 56/162 split that read exactly like a placement defect. `BspSurface.Normal`
  is corrected for the face's side and the vertex order is not corrected with it.
- **An overstated verification.** "Placement verified" covered origin-on-plane and normal
  alignment, neither of which constrains the quad's *extent*.
- **A distinguishing case absent from a fixture.** A `.mdl` byte-versus-vertex divisor sabotage
  passed because props-only fixtures all had `vertexindex == 0`.

---

## `set-the-opposite-state-first` — the precondition already equals the assertion

**If the state you assert is already true before the call, the test holds against a method with an
empty body.** Two of these were found in one hour on 2026-08-26, in code written months apart, and
both were in tests whose own comments named the exact failure they could not detect.

**The older one, in `DemoSystemsTests`:**

```csharp
spectator.Eyes = null;          // precondition
moment.Viewmodels = null;
Systems(...).Open(timeline: null, ...);
spectator.Eyes.ShouldBeNull();  // assertion — identical to the precondition
moment.Viewmodels.ShouldBeNull();
```

Its comment says *"easy to write as an `if` that only assigns when there is something to assign"* —
which is precisely the defect it was blind to. Setting each source to a **stub** first, so `Open`
has something to clear, made it fail against exactly that `if`. Confirmed by writing the bad shape
and watching it go red.

**The newer one, in a test written the same hour**, asserted that two `Show` calls did not accumulate
into a shared buffer. Clearing is the *source's* job, and the stub cleared — so the count it asserted
was decided by the stub, not by the code under test. Its subject was the stub. Replaced with the
claim the design actually encodes: the same buffer instance reaches the source each time.

Null is the default of every reference field and empty the default of every collection, so "assert it
is null/empty afterwards" is the single easiest unfalsifiable test to write by accident. It reads as
rigour. It is the mirror of the `an-empty-search-needs-a-control` section below — an absence observed without
establishing that presence was possible.

**Before writing `ShouldBeNull`, `ShouldBeEmpty` or `ShouldBe(0)`, ask what the value is immediately
before the call.** If it is the same, put the opposite there first. And when the assertion's value
comes from a stub rather than from the subject, the stub is what you are testing. **This is route 3
of the four in the global standards** — no control — and it generalises past deletion tests to any
assertion on a default value.

---

## `a-walking-test-cannot-see-a-deletion` — generate the denominator

**A test that walks a collection and checks a property of each member cannot detect a member that is
gone.** One fewer item is one fewer to check, and the assertion still holds.

Measured 2026-08-26, moving 363 lines of menu construction out of `MainForm`. Three tests covered
that menu and none of them could have seen an item fail to arrive:

- `ShortcutCollisionTests` — walks every menu item, asserts no two claim the same key. Deleting an
  item removes a *potential collision*, so it passes more easily.
- `DebugMenuWiringTests` — addresses six items by name. Silent about a seventh.
- The UI suite — presses F11 and invokes the screenshot item. Two of twenty.

**Confirmed by manipulation rather than argued:** with one item dropped from the View menu,
`ShortcutCollisionTests` **passed**.

**The fix is a generated denominator.** The new test reads every `*ItemId`/`*MenuId` constant off the
type by reflection and requires each to name something reachable in the strip; a second uses the
`bool` property count of the `DebugModes` record as the expected submenu size. A constant added
without an item fails; an item deleted fails; nobody maintains a number, so it cannot go stale.

This is the same split `SdkCoverageTests` uses in this repo — **the generated instrument catches what
is MISSING, the hand-written one catches what is WRONG** — and only the first kind survives someone
forgetting to update it. See [[nothing-is-closed]].

**When a test iterates a collection, ask what it would do if the collection were empty.** If the
answer is "pass", the missing half is a count or a set comparison against a denominator that comes
from somewhere else — preferably somewhere the compiler already knows about: constants, record
fields, an enum. **A hand-written exact count is still worth having beside it.**
`view.DropDownItems.Count.ShouldBe(12)` goes stale by design: it fails when the menu changes, which
is the moment a person should look.

---

## `a-count-cannot-see-past-a-pruner` — compare the set, never the count

**A test that waits for "more files than before" stops working the moment something prunes the
folder.** The viewer keeps its twenty most recent captures and prunes *after* writing, so at the cap
a new picture replaces an old one and the count is identical on both sides.
`F12WritesAPictureOfWhatTheViewerDrew` waited on `Shots().Length > before.Length` and timed out with
"F12 produced no picture" against a picture sitting on disk.

Fixed by comparing the set: `Shots().Except(before).Any()`, and taking the newest by name rather than
`Single()`, since the prune can also remove one between two listings.

It is intermittent by construction — correct until the folder fills, then wrong for ever — so it
reads as flake, and it appeared here only after a second capture test made the cap arrive sooner. The
count is not faithful to the question, which is "did a file that was not there before appear".

Anywhere a directory has retention ([[one-place-or-it-drifts]] put the prune next to the writer,
which is right and is also what creates this), compare identities, never counts. The same shape
applies to log files and to `~/measurements/` on the boxes.

---

## `a-layout-driven-by-its-own-length-cannot-fail` — the guard was decorative

`UserMessageBody` makes every layout safe with one rule: a correct layout consumes the body's
stated length **exactly**. `TextMsg` opted out of that rule without anyone deciding to — it read
NUL-terminated strings `while (offset < length)`, and reading to the end consumes the body exactly
**by construction**. The guard was still there, still evaluated, and could never fail. Measured
2026-08-19: a 512-byte body of zeros decoded as 511 empty strings and came back with fields and the
name `TextMsg` attached.

The check is only a check when the layout's width is decided **independently of the body**. Anything
whose reading is driven by the body itself — a loop to the end, a count read out of the body, a
trailing variable-length blob — has made itself unfalsifiable, and it will accept garbage while every
other layout in the same file is held to a real standard.

**When adding or reviewing a layout, ask what decides where it stops.** If the answer is "the body",
the exact-consumption guard below it is decorative. Get the width from the source instead —
`UTIL_ClientPrintFilter` in `src/game/server/util.cpp` and `CBaseHudChat::MsgFunc_TextMsg` in
`src/game/client/hud_basechat.cpp` both say five strings, always, with empty parameters sent as empty
strings. Tightening cost nothing: 19 of 19 `TextMsg` bodies across the nine era specimens still
decode, protocol 11 through 24.

Two follow-ons worth keeping:

- **Four existing fixtures asserted one, two or three strings and all four passed**, because the
  same belief wrote the fixture and the code. No server has ever sent those bodies. See
  [[fixtures-are-the-weak-point]].
- **It was found by a test written over ALL registered names at once** — feed every name a 4096-bit
  body, assert none decodes. Per-message tests would have given `TextMsg` the same
  too-short/too-long pair as everything else and it would have passed both, because its real defect
  was that its length was whatever it was handed. See [[most-of-a-decoder-is-untested]].

---

## `a-faithful-fixture-can-be-blind` — real data is well formed, and that is the problem

**Writing a fixture in the shipped data's exact shape is the right instinct and it is not
sufficient. The input still has to be one where correct and broken differ.**

Measured 2026-08-22. `Load_ACommentedOutEntry_IsNotRead` proved that a `//`-commented manifest line
does not load its script. Its fixture copied Valve's shape exactly:

```
	"precache_file"		"scripts/live.txt"
//	"precache_file"		"scripts/disabled.txt"
```

**It passed with comment handling sabotaged.** With `//` unhandled it becomes a token itself and
shifts the pairing to `("//", "precache_file")`, leaving `"scripts/disabled.txt"` orphaned in key
position — so the script fails to load in *both* worlds. The assertion was true either way and blind
to the thing it was written for.

One extra token ahead of the key fixes it, because an unhandled comment then pairs `("//", "x")` and
`("precache_file", "scripts/disabled.txt")`, which loads:

```
//	x	"precache_file"		"scripts/disabled.txt"
```

This is case 2 of the four insensitivity routes — a wrong CONDITION, where the fix is the input and
never the assertion. The instinct on seeing a test that cannot fail is to assert harder, and here
that would have produced a stronger-looking test that was equally blind.

- **Ask the question in the required order.** *Is there an input where correct and broken differ?*
  comes first; *does my assertion detect it?* second. Only the second one is about assertions.
- **Real-data faithfulness and sensitivity are different properties**, and a fixture can satisfy the
  first while failing the second — *because* real data is well formed. Valve's manifest cannot
  distinguish the two worlds; a file Valve would never write can.
- Both properties are still wanted. Keep the faithful case for what it proves, and add the
  distinguishing one beside it.
- The only way to find this is to actually run the sabotage and watch **which** tests go red. Here
  the whole-suite run showed the reader's own comment test failing and the catalog's real-data test
  failing, while the one test named for the behaviour stayed green — and that gap was the finding.

---

## `cancelling-sabotages-mean-coupled-tests` — one sabotage at a time

**Sabotage ONE thing at a time, and when two cancel, fix the test's input rather than adding a
third test.**

On 2026-08-20, verifying `BspCubemaps.Closest` by manipulation, two sabotages went in together:
the height term zeroed (`dz * dz * 0`) and the comparison loosened (`<` to `<=`). Only one test
went red. The axis test — the one written specifically to catch a search blind on Z — stayed
**green against a search that was blind on Z**.

**The cause was the test's input, not its assertion.** Its two placements shared X and Y exactly,
at `(0,0,0)` and `(0,0,500)`, measured from `(0,0,480)`. Isolating the height term that cleanly
looks like good practice and is the trap: with Z dropped both distances collapse to **zero**, so
the answer stops being decided by distance at all and is decided by how a tie resolves — which
was the other sabotage. The two tests were coupled through one input, and the second sabotage
supplied exactly the tie-break the first one needed.

The owner asked the right question — *"if two sabotages cancel should we have a third test to
catch that?"* — and the answer is no. A third test would cover this pair and not the next one;
the combinations are unbounded, and Stryker only generates first-order mutants anyway.

**Fix the condition.** Offsetting the placements 30 units on X removes the tie: with Z counted the
near one is 1,300 units² away against 230,400, and with Z dropped it is 900 against 0 — opposite
answers, no tie, no dependence on the comparison operator. Re-running the same double sabotage
then reddened **both** tests.

- **One sabotage at a time.** Two at once can cancel, and a green suite then reads as proof the
  code is right when it is proof of nothing. This is the manipulation step's own failure mode.
- **A test whose verdict depends on another behaviour being correct is not measuring what it
  names.** Ask of every test: with the thing I am testing broken, does the observation differ —
  or does it merely become *undetermined*, and get decided by something else?
- **A tie is the specific shape to watch for.** Breaking a comparison, a distance, a sort key or a
  score usually makes two candidates EQUAL rather than misordered, and equality is then resolved
  by a tie-break the test never meant to exercise. Perturb the other axes so the broken version
  gets a definite wrong answer.

---

## `a-greedy-match-reads-the-wrong-word` — the log line contained the answer twice

**2026-08-29, checking whether autoplay actually played.** The per-second frame report is one line
carrying the playback state and, much later in the same line, a garbage-collection summary:

```
245.5 frames a second, longest 11.85 ms, playing; drawing 147 ms; ... gc 3/0/0 paused 0.5 ms
```

Both `playing` and `paused` occur in it. A `sed -E 's/.*(paused|playing).*/\1/'` takes the LAST
occurrence, because the leading `.*` is greedy — so every line reported `paused`, and the conclusion
drawn was that playback stopped a second after starting and stayed stopped. The fix had in fact
worked: all 31 reports said `playing`, and the tick advanced 1900 → 3900 in thirty seconds, which is
66.7 a second and exactly right.

**What makes this its own case rather than a typo.** The wrong reading was *plausible* — it agreed
with the bug that had just been fixed, and it agreed with the earlier broken run. An instrument that
confirms what you already believe gets no scrutiny. It was caught only because a separate extraction
of the same log (`grep -n`, printing whole lines) showed `playing` at timestamps the first
extraction called `paused`, and two readings of one file cannot both be right.

- **Anchor on the field, not on the value.** `ms, (playing|paused);` is unambiguous where
  `(playing|paused)` is not. The value is what you are measuring; the surrounding text is what
  identifies it.
- **A vocabulary shared between two subsystems on one line is a hazard in the log's design**, not
  only in the reader. It is a cheap reason to prefer one fact per line.
- The general form is the first entry in this file, arriving through a different door: **the
  instrument was not faithful to the variable.** Here it measured a real quantity — GC pause — and
  reported it as playback state.

---

## `a-defect-that-survives-its-cause-is-in-the-instrument` — the census that outlived its own cause

**A control that removes the cause and does not change the reading is evidence about the
INSTRUMENT.** Hunting upside-down players (B298), a census reported *every* skeleton on the map
collapsed — and still reported it with every animation layer disabled. That total, surviving its
own cause, was the tell. The census was reading `ModelInstance.Bones`, which is the SKINNING
palette: `Concatenate(boneToWorld, poseToBone)`, whose translation column is a mixture of placement
and bind offset and is not a bone's position.

**Why:** a real defect has a cause; remove it and the number moves. A number that will not move, or
that indicts 100% of a population, is usually measuring something that was never the variable.
B222 had already recorded this exact mistake on the viewmodel size check, in a doc comment in the
same file, and it was not read first.

**How to apply:**

- **Pick the variable the symptom is about.** "Upside down" is not size — an inverted skeleton has
  the same bone spread as an upright one. Spread found nothing; head-above-foot found seven of
  fifteen.
- **Print a control the instrument cannot fake**, next to the measurement, every run. Here: the
  BIND-pose rise beside the posed rise. It immediately caught a second error — bind space is Y-up
  and world space is Z-up, so the first version read the wrong axis and reported a bind rise of 4
  on a model whose bind rise is 71.
- **A denominator of ALL is a warning, not a finding.**

See the `an-empty-search-needs-a-control` section below, and [[measure-the-output-not-the-capability]].

---

## `an-instrument-unread-is-not-an-instrument` — a plan is not a measurement

**Adding a diagnostic is half the work. Read it on a real run, in the same session, or it is not an
instrument yet.**

**Why:** `SoundPresenter.ReportAudioOutput` was written days before it was read — submitted against
dropped-for-zero-gain, reported at 1, 10, 100, 1000 so a broken run says so immediately. The handoff
carried it as *"added and has never been read on a run"*. Reading it cost one launch, and **the line
was simply absent**: 23,772 sounds on the timeline, 542 precached, 110 frames drawn, nothing
submitted. The whole sound path had been dead (B228).

An unread instrument is worse than none, because it looks like coverage. The intent to measure gets
remembered as a measurement.

**How to apply:**

- **Check the instrument ran before believing what it says.** The first attempt here reported
  "no audio" from a run that drew **zero frames** — a 45-second timeout against a map that takes 40
  seconds to load. An absent line means "never reached" as readily as "reached and zero", and those
  are opposite findings. Confirm the surrounding activity — frames drawn, ticks advanced — first.
- **Absence is a reading, but only against a control.** No `sound output:` line AND no `loop '...'`
  line AND a live frame count is three facts agreeing. One of them alone is not.
- Prefer an instrument that reports on the FIRST occurrence. At 1, 10, 100 a healthy run says one
  line and goes quiet, while a dead one says nothing at all — which is exactly the signal wanted.

**And the shape it found is worth its own line: a later call silently undoing an earlier one.** Three
of these turned up in one day — autoplay switched off by `SetDemoLength`, a stale clock left by an
early return, and the demo's sounds deleted by a map read. All three are assignments, so none of them
logged anything. When a feature "does nothing", grep for everything that WRITES the field it depends
on, and check the order against the caller — do not start by reading the feature.

Related: [[logs-are-the-debugger]], [[output-level-assertion-or-it-is-not-done]],
[[conformance-test-before-implementation]].

---

## `log-the-event-not-a-sample-of-it` — six ways a diagnostic log went blind

**A log added to catch a defect can be blind to that defect, and it looks exactly like evidence of
absence.** Six ways this happened in one evening, 2026-08-28, hunting a viewmodel that vanished for
a few frames at a time (B222). Every one produced a confident wrong conclusion first.

**1. A sampled log cannot see an event shorter than the sample.** The viewmodel pass was reported
once a second, and the weapon vanished for ~60 ms. The log read `drawing 2` on both sides of every
gap, so the gap never happened as far as it was concerned. The owner: *"why tf are you using a
second timeout jesus fning christ that is stupid"* — and he was right; the caveat had already been
written down and then reasoned straight past.

**The fix is not a shorter interval, it is a TRANSITION log.** Fire when the value changes, and a
two-frame flicker writes a line while a stable state writes nothing. That removes the blind spot and
the spam problem at once, and it is the right shape for any state whose *changes* are the signal.
Cap it per subject so a pathological flip cannot fill the disk.

**2. A degeneracy test that checks the wrong degeneracies.** Bones were tested for all-zero and
non-finite. A matrix that is finite and non-zero with a **zero-length basis row** collapses its
vertices just as thoroughly, and the first version reported `0 degenerate` on a confirmed
reproduction. The failure mode was named in that method's own doc comment and not implemented.

**3. A COUNT is not an IDENTITY.** The pass instrument reported `2 drawn` throughout, and was
correct: two props were drawn. The second had silently become a *different weapon*. `MomentScene`
warns about this in as many words eight lines from the code being instrumented — *"the count says
two and cannot say two of WHAT"* — and the instrument was written anyway. **Log what a thing IS, not
how many there are.**

**4. Comparing two measurements from different moments.** A weapon's bone centre at 17:37:37 was
compared against the arms' centre logged at 17:37:34 and the 4,400-unit difference reported as a
misplaced viewmodel. The player had run across the map in between. Measured against the arms **in
the same frame** it was correct, and the owner confirmed by looking. A difference between two
timestamps is not a difference between two things.

**5. A threshold chosen without asking what size of effect must survive it.** A weapon's distance
from the hands was bucketed as "over 100 units = AWAY". A viewmodel lives within tens of units of the
eye, so a weapon fifty units out — completely off screen — reported `with the hands`. The fix was
5-unit buckets, and the fault was choosing the resolution before asking what had to be visible
through it.

**6. A global report budget where the subject is per-model.** A "what did this model submit"
line was capped at 200 reports total, in a method that runs for every one of 250 props a frame. Two
animating props — `cappoint_hologram` and `demo_scotchbonnet` — spent the entire budget within
seconds, and the viewmodel, the only subject it was built for, never reported once. A whole
reproduction was wasted. **A shared budget is a resolution choice too**, and the noisiest subject
spends it.

**Why:** each of these is the "wrong instrument" failure from the testing section applied to a LOG
rather than to a test, and a log has no red state to warn you — silence reads as "the thing did not
happen". So the question to ask of any diagnostic before believing it is the same one:
**is there a state where broken and working produce different output, and can this line see it?**

**How to apply:** prefer transition logs to sampled ones. Name the subject, never just count it.
Test the property that makes the symptom, not one adjacent to it. And when a proxy is unavoidable —
bone-translation span standing in for "does the model have size" — say so where it is read, because
the next person to look will otherwise treat the number as the finding.

Related: [[measure-the-output-not-the-capability]], [[logs-are-the-debugger]].

---

## `a-threshold-instrument-cannot-see-a-sum` — six slow frames, one stall logged

**A per-event instrument with a threshold is blind to accumulation.** `Sample()` logged
`STALL decoding '<name>' took N ms` when a single decode passed `StallSeconds` (0.03). Measured
2026-08-25 on cp_process: **six of eleven slow frames were dominated by the sound step at 27–91 ms,
and exactly ONE decode stall was logged.** A frame that starts three sounds pays three decodes that
each fall under 30 ms, so the event instrument reported almost nothing while frames visibly froze.

The frame **ledger** saw it immediately, because it times a *phase* between two timestamps and
prints every bucket plus an `unaccounted` residual:

```
SLOW FRAME 99 ms: sound 90.7, camera 0, project 0, advance 6.2, capture 0, hud 0, draw 1.7
```

**Why:** a threshold answers "was any ONE occurrence expensive". The question a stall asks is "was
this FRAME expensive, and where did it go" — a different question, and no per-event threshold can
be tuned into answering it. Lowering the threshold makes it log constantly without ever summing.

**And this was a repeat.** B163's commit message already said: *"No counter named it, because it
sits outside both `_posingTicks` and `_drawTicks`. Every performance investigation had been reading
numbers structurally incapable of seeing it."* This session then spent its whole first half
optimising `posing` — the exact counter named there — and bought ~20 ms of 545 while the real cost
sat in a bucket nothing was reading. The lesson had been written down and was not applied.

**How to apply:**

- **Read the phase ledger FIRST**, before optimising any named counter. If `posing` and `draw` read
  1.7–2.6 ms on the slow frames, the stall is not in posing or drawing, and no amount of work there
  will move it.
- **The residual is the important column.** A large `unaccounted` says the cost is somewhere nobody
  has thought to measure, which is where every one of these has been found.
- When adding a stall log for an operation that can happen several times per frame, **accumulate
  per frame and report the total**, or accept it can only ever catch the single worst case.
- Ask the owner what the previous fix actually was — "check the commits where we fixed the stutter
  and hiccup the last time" located this in one step after a long stretch of guessing.

Related: [[measure-the-output-not-the-capability]], [[logs-are-the-debugger]], [[nothing-is-closed]].

---

## `print-what-was-added-not-how-many` — a rising count reads as success either way

**When new work makes a number go up, print the NAMES of what it added, not the number.** A count
rising is exactly what a working feature and a wrong one both look like.

Measured (B320). Hanging a corpse's cosmetics on it took the drawn count at one tick from 4 to 24.
That is the right shape — four corpses, twenty items, five each — and it is what a success looks
like. Printing the model names took one more line and said:

```
c_grenadelauncher.mdl   c_stickybomb_launcher.mdl   c_bottle.mdl   c_pickaxe.mdl
```

All four of a demoman's WEAPONS, holstered ones included, hung on his corpse. The scan had walked
every bone-merged child of the dead player; the engine walks the econ wearable list, and a weapon is
not in it. Twenty items was never going to reveal that. Four names did, instantly.

**The general rule: a count answers "did something happen", and the thing that goes wrong is usually
WHICH something.** Wrong set, wrong owner, wrong era's field, the same item twice. None of those
changes the magnitude in a way anyone would notice, and several make it look better.

**Cheap enough that there is no trade.** Cap the list at a handful and print it beside the count —
the count still shows the scale, the names still show the identity. Every probe in this repository
that earns its keep does both.

Related: [[measure-the-output-not-the-capability]].

---

## `it-ran-and-it-mattered-are-two-claims` — a counter that proves execution cannot prove effect

**A counter that proves a stage RAN cannot say it changed anything, and the difference is usually
the whole question.** Instrument the effect, not the execution.

B311, 2026-09-04. `IkLocks.Applied` reported 88 sequence locks running on a real demo — good enough
to prove the wiring, and useless for the thing anyone cares about. A lock whose remembered position
already equals where the sequence left the foot solves to the same place: **the bracket runs, the
pose is unchanged, and on screen that is indistinguishable from the lock never running at all.**

Adding the distance settled it: `88 moved, furthest 3.81 units`, on an 83-unit-tall player — about a
foot's width, which is the slide being removed.

**This is how a "needs a person looking" question becomes answerable.** A screenshot cannot show
that a foot stopped sliding without a before and an after of the same motion. A bone position is
deterministic, so the correction has a magnitude, and a magnitude can be asserted.

**Report the COUNT and the MAXIMUM, never one alone.** A hundred corrections of a thousandth of a
unit is arithmetic running, not a foot being held — and a threshold (0.01 here) is what separates a
correction from float noise.

**Carry both out of the loop that did the work:** the pre-solve value is the one the solve was
handed, the post-solve one comes from re-reading what the solve wrote. A second derivation of
either is free to be wrong and looks authoritative.

---

## `look-for-the-instrument-before-building-one` — grep the logs before writing a counter

**Three times in one session (2026-09-03) a measurement was about to be built that already existed.**

- B254's *"every prop is posed"* is answered by `posed N of M selected` in the moment cost log,
  which reports 9 of 567.
- B258's *"sample is 2.0 ms"* is re-taken by `--measure`, one flag, and comes back 0.3.
- B262's *"count second-cull rejections"* is answered by `opaque draw order: 152 of 152 models
  kept` — a line that had been printed on every run and read by nobody.

For the third, a counter and a second log line were actually written before the existing line was
noticed. Two routes to one number, free to disagree, which is what B243 is about — the fix was
to delete the new one.

**Why:** this project instruments heavily, so the prior probability that a number already exists is
high. Building a second one costs more than the code: it makes the two answers independent, and
when they differ nothing says which is right.

**How to apply:**

- **Grep the logs for the quantity before writing a counter.** `grep -i <thing>` over a viewer log
  is seconds; the alternative is a divergent instrument.
- **Read the whole line, not the part you came for.** `opaque draw order` was printing "152 of 152"
  next to bucket counts that were being read for something else.
- **A stale entry that asks for a measurement often predates the instrument that answers it.** Take
  the measurement first; it may close the entry outright.

See [[filing-a-divergence-is-not-fixing-it]].

---

## `a-ledger-must-cover-every-exit` — a ledger wired into two of three exits

**A ledger that misses one exit reports a clean bill of health, and that is worse than no ledger.**
Hunting missing geometry, a counter was added to the world build to name every dropped face by
reason and material. It was wired into the visibility skip and the tool-material skip and **not**
into the play-area skip — the only rule that discards geometry by POSITION, which is what was being
looked for. It reported "1,556 faces dropped, every one a tool material", which reads as proof that
nothing structural is being culled, and the search moved elsewhere for several hours.

**A second instrument in the same hunt measured its input instead of its output.** The world log
reported `props.Count / 3` prop triangles — the number handed to `AppendProps`, not the number
`AppendProps` appended. Removing a cull therefore could not move that figure, and did not, and the
figure was never wrong; it simply was not measuring the thing it was being read for. The brush-face
count beside it moved by exactly the 133 the ledger predicted, which made the pair look like
corroboration.

**Three instruments were wrong in one session** — those two plus a category view whose white was
read first as "uncoloured surface" and then as "the sign", when it meant overlays.

**How to apply:** when adding a counter to a loop with several `continue` paths, enumerate the exits
first and cover all of them, or state in the log which are counted. Count on the way OUT, never on
the way in: a total taken before the filters cannot observe a filter. And when a ledger reports that
a whole category is empty, check whether it can see that category at all before believing it — an
absence produced by not looking is identical to an absence produced by nothing being there.

Related: the `an-empty-search-needs-a-control` section below, [[measure-the-output-not-the-capability]],
[[logs-are-the-debugger]].

---

## `one-camera-or-the-cull-lies` — pass the camera, never re-derive it

When a second thing starts being derived from the camera, pass **the camera**, not the thing you
already derived from it. `Device3D.SetCamera` took a `float[]` matrix; adding frustum culling meant
it needed six planes as well, and the tempting shapes — a second `SetFrustum` call, or inverting the
matrix back into planes — are both a **second derivation of the camera**.

Take the camera object and produce both from it in one place. Upstream, make "which camera is this
frame seen through" a single function (`ViewCamera.Active`) that the matrix path also goes through,
so the two cannot answer differently.

**Why:** the failure is invisible until it is dramatic. A frustum built from the free camera while
the picture is drawn through a player's eyes culls exactly the geometry the viewer is looking at —
in first person only, and only once the two cameras diverge. This project has already shipped the
neighbouring version: a build-time top-down culling shortcut that broke the moment the free camera
moved. See [[build-time-shortcuts-assume-the-camera]] and [[one-place-or-it-drifts]].

**How to apply:** the test is not "are these values equal now" but "can they ever disagree". If two
call sites each compute a camera-derived value from the same raw inputs, they can. Keep the raw
inputs behind one accessor and derive everything past it. Where an old overload must stay — here the
viewmodel pass, which has a camera of its own — have it leave the derived state ALONE rather than
clearing it or reconstructing it, and say in the doc comment which callers rely on that.

---

## `two-agreeing-measurements-can-share-one-instrument` — a clean-checkout rerun is not a control

**When a measurement contradicts a number somebody wrote down, suspect the two INSTRUMENTS before
suspecting the subject** — and a second run of the same command is not a second instrument, however
clean the checkout.

Measured 2026-09-04. `build/gate.sh` said the rendering floor was **726**. A plain
`dotnet test tests/Tf2DemoSalvage.Rendering.Tests` reported `Total: 725` with seven tests just
added, so the assembly looked eight short. The check that "confirmed" it was a `git worktree` at the
very commit that set 726, built clean, run the same way: **718**, twice, agreeing.

Everything about that reads like proof. It was two readings from one instrument.

**`dotnet test`'s console summary and the `.trx` counters count different things.** Console
`Total: 725` is 672 passed + 53 skipped. The trx's `total="733"` is 672 executed + 61 not-executed.
The eight `[Explicit]` tests are in one and not the other, and `build/assert-test-count.sh` greps
`total="…"` out of the trx on purpose — its own comment says why. 726 was right the whole time.

**How to not spend an hour on this:**

- Measure the way the thing you are disputing measures. The floor comes from the trx, so ask the
  trx: `grep -oE 'total="[0-9]+"' tests/<Project>/TestResults/<name>.trx`.
- Reproducing a reading is not controlling it. A control uses a DIFFERENT route to the same value —
  see [[fixtures-are-the-weak-point]] and the `an-empty-search-needs-a-control` section below.
- Rendering is the only project here where the two totals disagree, which is why it is the one that
  catches the mistake. Other projects agree, so the wrong habit passes everywhere else.

Related: [[read-the-trx-total-not-the-console]], which says to read the trx and did not say that the
console's number is a different quantity rather than a truncation of the same one.

---

## How to apply, across all of it

When a measurement comes out wrong, check what the measurement is actually sensitive to before
touching the reader. Ask whether an input exists where correct and broken differ, and whether this
input is one. Prefer checks that cannot be satisfied by accident — lengths that must tile exactly,
vectors that must be unit length, bases that must be orthonormal. Those have caught real defects
here; "the number looks plausible" has not.

Related: [[fixtures-are-the-weak-point]],
[[measure-the-output-not-the-capability]], [[a-test-can-outlive-its-design]],
[[author-the-specimen-the-corpus-lacks]], [[boundaries-find-what-tests-cannot]].

**A test can name a claim its assertion does not check, and only sabotage finds it.** Measured
2026-09-03. `Build_WithALengthConstraint_HoldsTheTipOneLengthOut` asserted on the length of a bone
matrix's forward axis — which the code normalises one line BEFORE the constraint runs, so it is a
unit vector whether the constraint fires or not. Deleting the constraint's own reprojection left the
whole suite green. The variable the name is about, the tip's distance from the base, never reaches
the matrix at all: only the normalised direction and the base position do.

**The fix was the instrument, not the assertion.** An accessor was added exposing the simulated tip —
which is also what the engine exposes, for the same reason, under its own debug cvar. Reaching for a
stronger assertion on the same proxy would have produced a tighter test that still could not fail.

**Ask what the output CARRIES before writing an assertion on it.** A normalised vector cannot carry a
magnitude; a boolean cannot carry a count; a clamped value cannot carry what it was clamped from.
When the claim is about something the output discards, no assertion on that output is a test of it.

**A COUNTER placed upstream of the work measures intent, not the work** (B347, 2026-09-05).
`EntityModels.SpinBarrel` incremented `SpunBarrels` as soon as it had a weapon state and a bone
index — then handed both to `SkeletonPose.SpinBarrel`, which does the actual rotation write.
Sabotaging that write reddened NOTHING: four wiring tests, three of them controls, all green while
the barrel never turned.

The fix is the one B243 already states, applied one layer further in: **the number the caller reports
is carried out of the code that did the work.** `SkeletonPose` now counts its own writes and
`EntityModels` sums them, which is exactly the shape `AppliedIkLocks` already had beside it —
`posed.AppliedLocks`, not a tally kept where the call was made.

**How to spot it before a sabotage does:** ask where the counter is incremented relative to the
thing it names. If the increment can run when the named action does not, it is a second route.
`SpunBarrels++` sat three lines above the call that spins a barrel, and read as obviously correct.

**And the same session's other instrument fault, for contrast:** `SendTableConformanceTests` matched
two macro spellings and not `BEGIN_NETWORK_TABLE`, so **251 send tables were invisible — 185 of them
TF2's own.** It reported `DT_WeaponMinigun (no such send table in the SDK)` about a table the SDK
declares and a demo sends 462 times. That one failed in the direction that *punishes correct work*:
it argues a right name is wrong, which is worse than not checking at all. Same file already carried
a note about a regex that "reported every fog property as declared-nowhere — a fact about the
pattern rather than about Valve's tables". Second time, same file, same cause.

---

## `an-empty-search-needs-a-control` — absence is a fact about the search until a control says otherwise

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

### The rule

**Put a positive control in the same sweep.** When measuring that `$modblend` appears in zero
published shaders, measure `$envmap` and `$detail` in the same call and assert they are large. If
the controls come back zero the instrument is broken, and the interesting result is an artefact.

That is what `DeadShaderParameterConformanceTests` does, and it caught its own threshold being set
from a `.cpp`-only sweep when the real answer spans `.h` and `.fxc` too.

### Before trusting an absence

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

### A detector shipped as a TEST needs the control permanently, not just once — B196, 2026-08-25

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

### A truncated search is an empty search with a plausible tail

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

**The tool itself can be the absence, 2026-09-05.** Searching a VPK index for `vcd` and
`scenes.image` returned zero for both — and zero for the control, `mdl`, in a VPK this project reads
14,109 models from. The cause was that **there is no `strings` binary on this machine**, so every
search had been reporting on a missing program rather than on the file. `grep -a` works and shows a
top-level `scenes` entry.

**So the control has a control: does the TOOL run at all.** `command -v strings` would have said so
in one line, and the shape to watch for is *every* query returning zero, including ones that must
match. A pattern that is merely wrong gives a mixed result; a broken instrument gives a uniform one.

Same session, same shape, twice: a `grep -E` with escaped alternation inside a `for` loop returned
empty for three subjects at once, and autolayers turned out to be implemented in eleven files.
**Uniform zeros are the tell.**

Related: [[the-denominator-decides-what-can-be-lost]], [[nothing-is-closed]],
[[output-level-assertion-or-it-is-not-done]].

---

## `run-the-control-before-arguing` — build the pre-change tree instead of reasoning about authorship

**When a defect surfaces right after a change, run the PRE-CHANGE build on the same input before
reasoning about whether the change caused it.** `git worktree add <tmp> <commit>` and build — the
tree does not have to be clean, and nothing in the working copy is disturbed.

Measured 2026-08-28. A viewmodel dropped out during a session that had just landed two-pass drawing.
An evening went into arguing authorship from evidence:

- the commit touched no bone, animation, merge or pose file
- both candidate failure modes make a model invisible under any pass or material
- the model's only material was opaque, so the change was provably a no-op for it

**All true, all correct, and none of it was evidence about the symptom.** The owner eventually said
*"lets run the control"*. One launch: the dropout still happened on the pre-change build. Question
closed.

**Why:** an argument that a change *cannot* have caused something is reasoning about a mechanism you
have already assumed. The control tests the claim itself, needs no mechanism, and cannot be wrong
about authorship. It is also cheap — the build was already sitting in a worktree from earlier in the
same session and went unused for hours while better arguments were made.

**How to apply:** the moment "is this mine?" is asked out loud, build the control. Do it before
instrumenting, before reading the SDK, and certainly before explaining why it cannot be yours. If the
symptom is visual, the control needs the owner's eyes for one playthrough — cheaper than any of the
alternatives.

**The corollary, and it cost more than the control did:** the owner then observed *"the arms are not
being drawn either during the dropout, its not just the weapon"* — which meant every measurement of
the weapon had been aimed at the wrong subject, and the clean results were clean because that model
was fine. **Ask what ELSE is missing before instrumenting the thing that was reported.** A symptom is
reported as the part that was noticed, not as its full extent.

Related: [[ask-which-input-differs-before-bisecting]], [[the-f12-demo-is-the-parity-reference]],
[[nothing-is-closed]].

---

## `correct-counts-are-not-a-chain-of-custody` — six green counts, nothing drawn

A feature can be absent from the frame with **every instrument reporting success**, because each one
measures a stage that genuinely worked. Detail sprites (B360): the game-lump directory found `dprp`,
the reader returned 28,699 objects, the builder made 20,117 quads, the material resolved at index
206, `MapWorld` logged `398595 of 398595 prop triangles drawn`, and the renderer's blend census
listed material 206 as translucent. The hillside was bare.

The gap was between the last two: `DrawOpaqueBatches` skips translucent materials and the sorted
translucent list was built from the world's batches only, so every translucent PROP batch — five of
eleven on harvest — was issued by nothing (B362).

**Why:** each count answered "did this stage produce output", and none answered "was a draw call
issued". A chain of correct counts is not a chain of custody; the last link is the one nobody
instruments, and it is the only one that decides whether anything appears.

**How to apply:** when output is missing and every counter is green, stop adding counters — the next
one will be green too. Ask which pass actually ISSUES the work and whether the new thing is in that
pass's input list. Then confirm with a picture, pointed at coordinates taken from the data itself
rather than guessed: see [[point-the-camera-from-the-data]]. Related:
[[output-level-assertion-or-it-is-not-done]].

---

## `two-margins-are-not-the-table` — print the cross, and read every column of your own output

A census reported `cp_granary`'s detail props as two margins — *19,189 screen-aligned, 324 fixed* —
and that was read as "324 fixed sprites". Granary has **no** fixed-orientation sprite: its 324
fixed-orientation objects are `DETAIL_PROP_TYPE_MODEL`, a studio model nothing here draws. The two
variables correlate perfectly on that map and not at all on `koth_harvest_final`, so one map's
margins cannot be read as the other's.

The claim shipped into three documents before a screenshot of the wrong hillside sent me back to the
probe output — where the tell had been sitting all along: those 324 rows printed a `m_flScale` of
**−181,657,600**. A model does not use that field. **A nonsense number beside a plausible one is the
instrument saying which rows it should not have selected.**

**Why:** margins lose the interaction, and the interaction is usually the finding. "How many are
screen-aligned" and "how many are sprites" answer different questions, and the one that decides what
gets drawn is the cross.

**How to apply:** when two categorical fields both gate the same behaviour, print the CROSS, not the
two totals — one line per (type, orientation) pair. And read every column of a probe's own output
before quoting one of them: an impossible value in a neighbouring field is evidence about the row.
Related: [[the-denominator-decides-what-can-be-lost]].

---

## `print-a-value-somebody-can-recognise` — a name a human knows is the control a count cannot be

**When a decode produces a value the world has a NAME for, print the value.** A count says the code
ran; a recognisable value says it ran correctly, and nothing else available does.

Measured 2026-09-04 implementing TF2's paint (`ItemTintColor`, B330). The `paint` probe prints hex
rather than a total, and what came back was:

| colour | paint |
|---|---|
| `#FF69B4` | Pink as Hell |
| `#E6E6E6` | An Extraordinary Abundance of Tinge |
| `#7D4071` | Noble Hatter's Violet |
| `#141414` | A Distinctive Lack of Hue |
| `#694D3A` | Radigan Conagher Brown |

Every one is a paint somebody has equipped. **"12 painted of 51 econ items" would have been equally
true of a wrong implementation** — the attribute's 32 bits are a float whose VALUE is the packed
colour, and reinterpreting the bits instead of truncating gives `0x4B67B53B` for `0xE7B53B`. That is
still "a colour", still non-zero, still counts as 12.

**The technique generalises to anything with a vocabulary outside this project**: a material name, a
map name, a model path, a class name, a hex colour, a known constant. Print it and read it. Where a
value has no such vocabulary — a bit offset, a float from an interpolation — this does not help and
a control has to come from somewhere else.

### The corollary: a rare branch shows up in real data or not at all

Two of the twelve came back `#B8383B / BLU #5885A2`, which is `RGB_INT_RED` 12073019 and
`RGB_INT_BLUE` 5801378 — **Valve's old team-colour sentinel, live in a 2026 match.** The attribute's
value 1 is not a colour; it selects two constants
(`GetModifiedRGBValue`, `econ_item_view.cpp:1612-1615`).

A synthetic test covers that branch only if somebody thought of it. Running the probe over real
demos is what proved the branch is REACHED — and reading its 1 as a colour would have painted two
hats near-black in every demo containing them, which is exactly the kind of defect that gets
reported as "that hat looks wrong" years later.

It was checked on two demos — 12 of 51 and 10 of 102. The `print-what-was-added-not-how-many` section
above is the same rule, one step less specific.

**A probe that resolved an ENTITY INDEX without a tick, and named its subject from a literal**
(B389, 2026-09-10). `cycle 20130518_0313_cp_granary_blu_blu 141 8200` reported a complete animation
table headed `models/player/scout.mdl, client-side animated True`. Entity 141 at that tick is
`main_entrance_door.mdl`, a `prop_dynamic`; the index owns seven tracks, one per round restart, and
`DemoTimeline.TrackFor(entity)` returns the last one written.

**Two faults reading as one answer, which is why it was believed.** The keyframes came from the wrong
occupant of the right slot — a door's numbers from the wrong hour — and the model name came from a
scout path hardcoded in the probe by an earlier session's investigation, printed under whatever entity
was asked for. Neither is visible on its own: the table was coherent, the model was plausible, and
nothing was missing.

**The rule this adds:** *an instrument must name its subject from its subject.* A probe that takes an
identifier must print what that identifier resolved TO — here the model path and the track's window —
because a reader who cannot see the subject cannot notice it is the wrong one. And a literal left in a
diagnostic outlives the investigation that put it there; it does not stop being printed when the
question changes.

**`jitter` had already fixed the same lookup and `cycle` had not**, which is the other half: a rule
solved in one instrument and absent from the next is not solved. The selection moved into
`DemoTimeline.TrackFor(index, tick)` with `EntityTracks.Select` reporting it, so the two probes cannot
disagree about which entity they opened.
