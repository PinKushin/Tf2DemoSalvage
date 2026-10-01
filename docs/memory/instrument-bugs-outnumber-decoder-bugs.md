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
repeatedly.** Casebook for the global standard's "a test that cannot fail is an experiment insensitive
to the manipulation".

---

## The original five, all 2026-08-12/13

- **A control that could not fail** — a set-count term cancelled at style zero; length arithmetic
  (each face's span reaches the next face's offset) is what could fail.
- **A picture test passed on the headline defect** — shader read the wrong lightmap set but twelve
  ssbump materials still changed; threshold miscalibrated.
- **A winding assumption** — point-in-polygon assumed fixed winding, reporting 0% decal coverage;
  `BspSurface.Normal` is corrected for the face's side but vertex order isn't.
- **An overstated verification** — "placement verified" covered origin/normal, not extent.
- **A distinguishing case absent from a fixture** — a `.mdl` divisor sabotage passed because
  props-only fixtures all had `vertexindex == 0`.

---

## `set-the-opposite-state-first` — the precondition already equals the assertion

**If the asserted state is already true before the call, the test holds against an empty-bodied
method.** Two found in one hour: a test setting sources to null then asserting them null after
`Open` — blind to an `if` that only assigns when there's something to assign. A second test's
"no accumulation" assertion was decided by a stub's own clearing, not the code under test.

**Before writing `ShouldBeNull`/`ShouldBeEmpty`/`ShouldBe(0)`, ask what the value is immediately
before the call** — if the same, set the opposite first. When an assertion's value comes from a
stub rather than the subject, the stub is what you're testing.

---

## `a-walking-test-cannot-see-a-deletion` — generate the denominator

**A test walking a collection and checking each member cannot detect a missing member.** Moving 363
lines of menu construction, three tests covered it and none could see an item fail to arrive —
confirmed by dropping one item: the collision test **passed**.

**Fix: a generated denominator** — read every `*ItemId` constant by reflection, require each
reachable in the strip. The generated instrument catches what's MISSING; the hand-written one
catches what's WRONG — only the first survives someone forgetting to update it. A hand-written exact
count is still worth having beside it — it goes stale by design, which is the point.

---

## `a-count-cannot-see-past-a-pruner` — compare the set, never the count

**A test waiting for "more files than before" stops working once something prunes the folder** — at
the cap, a new file replaces an old one and the count is identical. Fixed by comparing the SET
(`Except`), never the count. Intermittent by construction — correct until the folder fills, then
wrong forever. Applies anywhere a directory has retention.

---

## `a-layout-driven-by-its-own-length-cannot-fail` — the guard was decorative

`UserMessageBody` makes layouts safe by consuming the body's stated length exactly. `TextMsg` opted
out without anyone deciding to — reading NUL-terminated strings `while (offset < length)` consumes
the length by construction, so the guard could never fail. A 512-byte body of zeros decoded as 511
empty strings.

**The check is only a check when width is decided INDEPENDENTLY of the body.** Get the width from
the source (`UTIL_ClientPrintFilter`, `CBaseHudChat::MsgFunc_TextMsg`: five strings, always). Found
by a test written over ALL registered names at once, feeding every one a 4096-bit body — per-message
tests would have given `TextMsg` the same too-short/too-long pair as everything else and passed both.

---

## `a-faithful-fixture-can-be-blind` — real data is well formed, and that is the problem

**A fixture in the shipped data's exact shape is the right instinct and not sufficient — the input
still has to be one where correct and broken differ.** A `//`-commented manifest line fixture copied
Valve's shape exactly, and passed with comment handling sabotaged — because an unhandled `//` shifts
the pairing and the script fails to load in BOTH worlds. One extra token ahead of the key fixes it.

- Ask in order: is there an input where correct and broken differ? THEN does my assertion detect it?
- Real-data faithfulness and sensitivity are different properties — a fixture can satisfy the first
  while failing the second, because real data is well formed.
- The only way to find this is running the sabotage and watching WHICH tests go red.

---

## `cancelling-sabotages-mean-coupled-tests` — one sabotage at a time

**Sabotage ONE thing at a time; when two cancel, fix the test's input rather than adding a third
test.** Two sabotages went in together (a height term zeroed, a comparison loosened) and only one
test went red — the axis test built specifically to catch a Z-blind search stayed green, because its
two placements shared X/Y and collapsed to a tie the other sabotage then resolved.

Owner asked: *"if two sabotages cancel should we have a third test to catch that?"* — no: a third
test covers this pair, not the next one, and combinations are unbounded. **Fix the condition**
instead — offset the placements so no tie exists; re-running the double sabotage then reddened both.

- One sabotage at a time — two can cancel and a green suite reads as proof of nothing.
- A test whose verdict depends on another behaviour being correct isn't measuring what it names.
- A tie is the shape to watch for — perturb other axes so the broken version gets a definite wrong
  answer.

---

## `a-greedy-match-reads-the-wrong-word` — the log line contained the answer twice

A per-second frame report line carried both `playing` and `paused` (playback state, then a GC
summary later in the line). A greedy `sed` regex took the LAST occurrence, so every line reported
`paused` though playback was fine (31/31 said `playing`). Caught only because a separate `grep -n`
extraction of the same log disagreed.

**Anchor on the field, not the value** (`ms, (playing|paused);`). A vocabulary shared between two
subsystems on one line is a hazard in the log's design, not just the reader.

---

## `a-defect-that-survives-its-cause-is-in-the-instrument` — the census that outlived its own cause

**A control that removes the cause and doesn't change the reading is evidence about the
INSTRUMENT.** Hunting upside-down players (B298), a census reported every skeleton collapsed, even
with all animation layers disabled —
because it read the SKINNING palette (a mixture of placement and bind offset), not bone position.

- Pick the variable the symptom is about ("upside down" is not size).
- Print a control the instrument cannot fake, every run.
- A denominator of ALL is a warning, not a finding.

---

## `an-instrument-unread-is-not-an-instrument` — a plan is not a measurement

**Adding a diagnostic is half the work — read it on a real run, in the same session, or it isn't an
instrument yet.** A sound-output report sat unread for days; reading it cost one launch and the line
was simply absent — the whole sound path had been dead (B228). An unread instrument is worse than none: the
intent to measure gets remembered as a measurement.

- Check the instrument ran before believing what it says — an absent line means "never reached" as
  readily as "reached and zero".
- Absence is a reading only against a control (multiple absences agreeing, plus a live frame count).
- Prefer instruments reporting on the FIRST occurrence.

**A shape worth its own line:** a later call silently undoing an earlier one — three of these in one
day, all assignments, none logged. When a feature "does nothing", grep for everything that WRITES the
field it depends on.

Related: [[logs-are-the-debugger]], [[output-level-assertion-or-it-is-not-done]],
[[conformance-test-before-implementation]].

---

## `log-the-event-not-a-sample-of-it` — six ways a diagnostic log went blind

Six failures in one evening hunting a viewmodel that vanished for a few frames (B222):

1. **A sampled log can't see an event shorter than the sample** — reported once a second while the
   gap lasted ~60ms. Fix: a TRANSITION log, firing on change, not interval.
2. **A degeneracy test checking the wrong degeneracies** — bones tested for all-zero/non-finite, not
   a zero-length basis row that collapses vertices identically.
3. **A COUNT is not an IDENTITY** — "2 drawn" stayed correct while the second silently became a
   different weapon. Log what a thing IS, not how many.
4. **Comparing measurements from different moments** — a 4,400-unit "misplacement" was really the
   player running across the map between two log timestamps.
5. **A threshold chosen without asking what effect size must survive it** — 100-unit buckets missed
   a viewmodel-scale (tens of units) displacement; fixed with 5-unit buckets.
6. **A global report budget where the subject is per-model** — two noisy animating props spent an
   entire 200-report budget, and the actual subject never reported once.

**How to apply:** prefer transition logs, name the subject not just the count, test the property that
makes the symptom. Related: [[measure-the-output-not-the-capability]], [[logs-are-the-debugger]].

---

## `a-threshold-instrument-cannot-see-a-sum` — six slow frames, one stall logged

**A per-event threshold instrument is blind to accumulation.** Six of eleven slow frames were
dominated by the sound step (27-91ms), and exactly ONE decode stall was logged, because each of
several decodes per frame individually fell under the threshold. The frame LEDGER (timing a phase
between two timestamps, printing every bucket plus an `unaccounted` residual) saw it immediately.

**How to apply:** read the phase ledger FIRST before optimising any named counter. The residual
column is the important one. Accumulate per-frame stall totals rather than logging only the single
worst case. **This was a repeat (B163)** — the exact counter named there had already been fixed once,
and a whole session's first half was spent re-optimising it instead of reading the ledger.

---

## `print-what-was-added-not-how-many` — a rising count reads as success either way

**When new work makes a number go up, print the NAMES of what it added, not the number.** B320: a
demoman corpse's drawn-item count going 4→24 looked exactly like success (four corpses, five items each).
Printing model names showed all four were his WEAPONS, holstered ones included — the scan walked
every bone-merged child instead of the econ wearable list.

**Cheap enough there's no trade** — cap the list at a handful, print beside the count.

---

## `it-ran-and-it-mattered-are-two-claims` — a counter that proves execution cannot prove effect

**A counter proving a stage RAN cannot say it changed anything.** B311: `IkLocks.Applied` reported 88 locks
running — useless, since a lock whose remembered position already equals the sequence's foot solves
to the same place, indistinguishable from never running. Adding the distance settled it: `88 moved,
furthest 3.81 units`.

**Report the COUNT and the MAXIMUM, never one alone.** Carry both out of the loop that did the work —
a second derivation of either is free to be wrong.

---

## `look-for-the-instrument-before-building-one` — grep the logs before writing a counter

**Three times in one session a measurement about to be built already existed** — answered by
existing log lines nobody had read: B254's "every prop is posed" by `posed N of M selected` in the
moment cost log; B258's "sample is 2.0ms" by `--measure`, one flag, returning 0.3; B262's "count
second-cull rejections" by an `opaque draw order: 152 of 152 kept` line already printed every run.
Building a second instrument makes the two answers independent, and when they differ nothing says
which is right.

**How to apply:** grep the logs for the quantity before writing a counter; read the WHOLE line, not
just the part you came for. See [[filing-a-divergence-is-not-fixing-it]].

---

## `a-ledger-must-cover-every-exit` — a ledger wired into two of three exits

**A ledger missing one exit reports a clean bill of health, worse than no ledger.** A face-drop
counter was wired into two of three skip paths and missed the one discarding geometry BY POSITION —
the exact rule being hunted — reporting "1,556 dropped, all tool materials", which sent the search
elsewhere for hours. A second instrument in the same hunt measured its INPUT instead of its output
(a prop-triangle count that couldn't move when a cull was removed, and didn't — read as corroboration).

**How to apply:** enumerate every exit before adding a counter to a loop with several `continue`
paths, or state which are counted. Count on the way OUT, never on the way in. When a ledger reports a
whole category empty, check whether it can SEE that category at all.

---

## `one-camera-or-the-cull-lies` — pass the camera, never re-derive it

**When a second thing gets derived from the camera, pass THE CAMERA, not the thing already derived
from it.** Adding frustum culling to a matrix-taking `SetCamera` tempted a second `SetFrustum` call or
inverting the matrix back into planes — both a second derivation. Take the camera object, produce
both from it in one place; make "which camera is this frame seen through" one function everything
routes through.

**Why:** invisible until dramatic — a frustum built from the free camera while drawing through a
player's eyes culls exactly what's being looked at, only once the two diverge. Same family as
[[build-time-shortcuts-assume-the-camera]].

---

## `two-agreeing-measurements-can-share-one-instrument` — a clean-checkout rerun is not a control

**When a measurement contradicts a written-down number, suspect the two INSTRUMENTS before the
subject — a second run of the same command is not a second instrument, however clean the checkout.**
`build/gate.sh` said the floor was 726; `dotnet test` reported 725. A worktree rebuild of the setting
commit "confirmed" 718, twice — two readings from ONE instrument.

**Cause:** console `Total:` and the `.trx` counters count different things (passed+skipped vs.
executed+not-executed) — `[Explicit]` tests are in one and not the other. 726 was right.

**How to not spend an hour:** measure the way the disputed thing measures (grep the trx directly).
Reproducing a reading is not controlling it — a control uses a DIFFERENT route to the same value.

**Three replicas can share one literal** (B369): a ported `cos` came back one ulp off on three
arguments; a C# kernel, an SSE2 replica and an exact dyadic simulation all agreed with each other and
disagreed with the binary — all three had copied one mistyped constant from the same port source. A
replica is a second route only if it takes its inputs from the subject (read constants from the
loaded image); when replicas agree against an oracle, cut the oracle into steps to find the divergence.

Related: [[read-the-trx-total-not-the-console]], [[fixtures-are-the-weak-point]].

---

## How to apply, across all of it

Check what the measurement is actually sensitive to before touching the reader. Ask whether an input
exists where correct and broken differ. Prefer checks that can't be satisfied by accident (lengths
that must tile exactly, unit-length vectors, orthonormal bases).

**A test can name a claim its assertion doesn't check, and only sabotage finds it.** A test named for
"holds the tip one length out" asserted on a forward axis normalised one line BEFORE the constraint
ran — a unit vector whether the constraint fires or not, blind to the actual claim. Fix was an
accessor exposing the simulated tip. Ask what the output CAN CARRY before asserting on it.

**A COUNTER placed upstream of the work measures intent, not the work** (B347) — an increment fired
as soon as a caller had the inputs, before handing off to the code that does the actual write.
Sabotaging the write reddened nothing. **The number the caller reports must be carried out of the code
that did the work** — the fix B243 already states, applied one layer further in.

**Same session's contrast:** a conformance test matched two macro spellings and missed a third,
making 251 send tables invisible — reporting a table the SDK genuinely declares as "no such send
table". Punishes correct work, worse than not checking.

---

## `an-empty-search-needs-a-control` — absence is a fact about the search until a control says otherwise

**A grep returning nothing is a fact about the grep, not the format, until a positive control in the
same sweep shows the search could find something.** Six instances, each recorded as knowledge and
each wrong: "TF2's game code is not public" (1,318 files under `game/{shared,client,server}/tf`);
`$modblend` "needs a decompiler" (declared in three shipped VMTs); `moveparent` "will never appear in
a SENDINFO" (it's a `SENDINFO_NAME`); haptics "nothing hints at it" (`haptic_msgs.cpp` registers all
six); the container "established by measurement" (`demoformat.h` declares the whole header);
`ScenePose.Hidden` "read by no renderer, so `EF_NODRAW` is ignored" (B133) (read one layer up, with a passing test).

**The sixth is worth studying — the search was scoped to OUR OWN code and still wrong the same way.**
Searching only the renderer for `Hidden` found zero — true, and opposite of what it meant: hidden
poses are filtered upstream, so the renderer never receives one. Absence caused by correct upstream
handling looks exactly like a gap.

**What would have caught the alleged bug: nothing** — sabotaging the filter left the suite green,
since the covering test measured a field on the object handed over, not whether it was handed over at
all. **When a search suggests a defect, sabotage the code before filing it.**

### The rule

**Put a positive control in the same sweep.** Measuring `$modblend` in zero shaders, also measure
`$envmap`/`$detail` and assert they're large.

### Before trusting an absence
- Search for the string, not the identifier ([[wire-names-are-strings]]).
- Widen the root once — `public/` sits beside `game/`.
- Try a file type you didn't think of — `.res`, `.vmt`, `.fxc`, VPK contents.
- State the scope in the claim — "not in `game/`" is checkable; "not in the SDK" is a claim about
  40,000 files nobody verified.
- For "nothing consumes X", find what DOES.

### A detector shipped as a TEST needs the control permanently — B196

A field-seeding scanner found two shipped regressions, then failed TWICE silently: a regex lookahead
backtracked past `null` on a false negative; a code COMMENT mentioning a deleted field's assignment
tripped the same guard, masking the real bug. **A partially-blind detector is worse than no
detector** — validate against the real historical defect (`git stash` the pre-fix source, confirm the
scan names it), not only a synthetic one.

### A truncated search is an empty search with a plausible tail

A `grep | head -6` cut off the seventh line, which was the actual call site (B279) — "no call site"
led to a duplicate call added to production. Same shape twice more: a `head -8` histogram hid a third
of the mass; a `sed` line-range on a KeyValues block missed a declaration a few lines past the range
(B415).

**Never cap a search whose ABSENCE you're about to act on.** If a result must be short, count first
(`grep -c`), then slice.

**The tool itself can be the absence** — a VPK search for two strings returned zero for both AND for
a control that must match, because there was no `strings` binary on the machine; `grep -a` worked.
**Uniform zeros (including the control) are the tell that the tool, not the pattern, is broken.**

Related: [[the-denominator-decides-what-can-be-lost]], [[nothing-is-closed]],
[[output-level-assertion-or-it-is-not-done]].

---

## `run-the-control-before-arguing` — build the pre-change tree instead of reasoning about authorship

**When a defect surfaces right after a change, run the PRE-CHANGE build on the same input before
reasoning about whether the change caused it** (`git worktree add <tmp> <commit>` + build). An
evening was spent arguing authorship (the commit touched no relevant file, both failure modes require
X, the model's material made the change provably a no-op) — all true, none of it evidence. One
launch of the control: the dropout happened on the pre-change build too. Question closed.

**Why:** an argument that a change CANNOT have caused something reasons about a mechanism already
assumed. The control tests the claim itself and needs no mechanism.

**Corollary, costing more than the control:** the owner then noticed the arms weren't drawn either —
every measurement had aimed at the wrong subject. **Ask what ELSE is missing before instrumenting the
reported symptom.**

Related: [[ask-which-input-differs-before-bisecting]], [[the-f12-demo-is-the-parity-reference]].

---

## `correct-counts-are-not-a-chain-of-custody` — six green counts, nothing drawn

A feature can be absent from the frame with EVERY instrument reporting success, because each measures
a genuinely-working stage. Detail sprites (B360): directory found the lump, reader returned objects,
builder made quads, material resolved, world log said all triangles drawn, blend census listed the
material as translucent. The hillside was bare — the gap was between the last two: the opaque batcher
skipped translucent materials, but the sorted translucent list was built from world batches only,
never prop batches (five of eleven translucent prop batches on harvest issued by nothing, B362).

**A chain of correct counts is not a chain of custody** — the last link (was a draw call actually
ISSUED) is the one nobody instruments. Confirm with a picture, camera pointed from the data.

---

## `two-margins-are-not-the-table` — print the cross, and read every column of your own output

A census reporting two margins (screen-aligned vs. fixed-orientation counts) was misread as "324
fixed sprites" — granary has NO fixed sprites; those 324 are `DETAIL_PROP_TYPE_MODEL`, drawn by
nothing here. The tell sat in the same output row: an `m_flScale` of −181,657,600, a field a model
doesn't use.

**When two categorical fields both gate the same behaviour, print the CROSS**, not two totals — one
line per (type, orientation) pair.

---

## `print-a-value-somebody-can-recognise` — a name a human knows is the control a count cannot be

**When a decode produces a value the world has a NAME for, print the value.** A count says the code
ran; a recognisable value says it ran correctly. Implementing TF2's paint (`ItemTintColor`, B330),
printing hex colours against known paint names ("Pink as Hell", "Radigan Conagher Brown") confirmed
correctness in a way "12 painted of 51 items" never could — the same count would have been equally
true of a bit-reinterpretation bug (reinterpreting the attribute's bits instead of truncating gives
`0x4B67B53B` for `0xE7B53B`, still "a colour", still non-zero, still counts as 12).

**Corollary: a rare branch shows up in real data or not at all.** Two paints came back as Valve's old
team-colour sentinel constants (`RGB_INT_RED`/`RGB_INT_BLUE`), live in a 2026 match — the attribute's
value 1 selects two constants rather than encoding a colour (`GetModifiedRGBValue`,
`econ_item_view.cpp:1612-1615`); a synthetic test only covers a branch if someone thought of it;
running the probe on real demos proved it's REACHED.

**A probe that resolved an entity index WITHOUT a tick, and named its subject from a literal** (B389):
printed a full animation table headed "scout.mdl" for an entity that was actually a door at that
tick — the model name was hardcoded from an earlier investigation, printed regardless of what was
asked for. **An instrument must name its subject FROM its subject.**

---

## `a-root-that-sleeps-is-not-a-body-at-rest` — report the worst member, not the first

**`corpse-drop` reported "3 of 5 seeds settle" for weeks while measuring one bone (the root).** Three
roots came to rest; asked which body held the deepest contact instead, a limb was found 73 units
into the terrain while the root looked fine. Every number printed was true — the proxy was the
best-behaved member of a set.

**When the thing under test is a SET, report its WORST member and name it, never the first or the
average.**

## `a-walk-must-read-what-production-reads` — B443, an "encoder defect" that was the test's walk

**`EntityRoundTrip` blamed two removal lists for a week; the encoder was right.** Its walk read no
packet until it had a decoder, so the signon string tables (created BEFORE `dem_datatables`) never
reached the decode state; later `svc_UpdateStringTable`s misaligned their packets, the snapshots behind
them threw, and a `catch { continue; }` dropped them uncounted. Production walk: 894 of 894 exact.

**How to apply:** a corpus harness walks the demo through the SAME helper production's order uses
(`DemoCorpus.EntitySnapshots`), and an undecodable item is a counted failure, never a skip. Before
filing a decode defect from a harness, run production on the same demo (`timeline-cost`): if production
decodes it cleanly, the harness is the suspect.

## A byte search over an assembly reads its resources too (B24, 2026-09-30)

`SchemaGap.AnyProductionAssemblyMentions` searched the whole DLL image for a wire name. Embedding build
3258's `dem_datatables` in Core put every wire name of that schema into the image, and a gap marker
(`m_nType`) flipped to "implemented" with no code reading it. **How to apply:** search the metadata block
(`PEReader.GetMetadata()`), where literals and member names live — never the file — and expect any new
embedded data file to be able to fool a raw byte search.
