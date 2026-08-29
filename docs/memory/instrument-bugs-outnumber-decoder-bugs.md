---
name: instrument-bugs-outnumber-decoder-bugs
description: On this project the tests have been wrong far more often than the readers they test — with the worked examples of every way an instrument goes blind.
metadata:
  type: project
---

**Across this project the code under test was right almost every time and the measurement was wrong
repeatedly.** Recorded because the instinct on a bad number is to suspect the code, and here that
instinct has been wrong more often than not.

This is the project's casebook for the global standard's *"a test that cannot fail is an experiment
insensitive to the manipulation"*. **Six memories were merged into it on 2026-08-27** — the index
had grown a separate entry per example, which buried the one thing they all say. Their names are
kept as headings below, because that is what they were and several were linked by name.

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
rigour. It is the mirror of [[an-empty-search-needs-a-control]] — an absence observed without
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
forgetting to update it. See [[the-denominator-is-already-written-down]].

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

## How to apply, across all of it

When a measurement comes out wrong, check what the measurement is actually sensitive to before
touching the reader. Ask whether an input exists where correct and broken differ, and whether this
input is one. Prefer checks that cannot be satisfied by accident — lengths that must tile exactly,
vectors that must be unit length, bases that must be orthonormal. Those have caught real defects
here; "the number looks plausible" has not.

Related: [[fixtures-are-the-weak-point]], [[differential-beats-fixtures]],
[[measure-the-output-not-the-capability]], [[a-test-can-outlive-its-design]],
[[an-empty-search-needs-a-control]], [[real-data-hides-bugs-small-inputs-expose]],
[[author-the-specimen-the-corpus-lacks]], [[boundaries-find-what-tests-cannot]].
