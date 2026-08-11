# 08 — Method: what worked, and what misled

The techniques below are ordered by how much they actually settled, not by how sophisticated they
are. The cheapest one is first, and it is not close.

## Arithmetic on lengths, before reading any byte

**The highest-yield technique in the whole project.** A message states its length; the differences
between the lengths one message takes across a corpus constrain its layout hard enough to identify
it.

Worked example, `Damage` at protocol 14. Bodies were 77 and 72 bits. A `BitVec3Coord` is three
presence bits plus its axes, and an axis is 22 bits with a fraction or 17 without — so a full
vector is 69 and one bare axis makes it 64, leaving **exactly 8 bits of header either way**. One
byte. The modern era's 118 and 113 show the same five-bit step, which is what says both eras share
the vector encoding and differ only ahead of it.

Three rules fall out:

- **A constant body length falsifies any layout with optional fields.**
- **The gap between two observed lengths names the optional field.**
- **A candidate whose width cannot produce an observed length is eliminated outright**, without
  reading a single byte of payload.

The standing hypothesis this replaced — that TF2 inherited HL2's `Damage` — died to one
subtraction: HL2's message is a fixed 144 bits and was never a candidate for a 77-bit body.

## Check the length with `==`, never `<=`

Stated separately because it is the single most expensive mistake this project has made twice.

A lenient bound does not tolerate rounding — **it accepts every layout short enough.** The modern
`Damage` layout fits *under* a protocol-14 body, so `BitsRead <= bodyBits` passed it and reported
`damage=16164` for 20 of 24 messages.

The bodies end mid-byte, which is the proof that the stated length is exact rather than padded.
Where a format states an exact length, exact consumption is not pedantry — it is the only free
check on a guessed layout. Valve's own readers agree: `CTFStatPanel::MsgFunc_PlayerStatsUpdate`
tests `0 == msg.GetNumBytesLeft()` and bails "rather than risk polluting player stats with
garbage".

## Predict the width, then measure it

Every layout transcribed from Valve's source was used to **predict a body width before any body was
read**. `Fade` is three shorts and four bytes, so 80 bits — and all 20 Fades in the corpus are 80
bits. `Shake` is 104. `PlayerStatsUpdate` is 48 + 32n, and the six widths present are exactly that.

Prediction before measurement is what makes a transcription a transcription rather than a curve
fit. A layout fitted *after* seeing the widths would have exactly as much evidence and much less
meaning.

## Differential comparison beats fixtures

A hand-built fixture can only encode what you already believe. It cannot falsify your reading of a
spec, because you wrote both sides.

The SendTable **flattening order** was wrong for a long time and no fixture could have caught it —
every test agreed with the implementation because both came from the same misunderstanding. It died
in one diff against `demostf/parser`, across 204,000 properties.

Corollary, learned the hard way: **fixtures caused more bugs than the decoders did.** Where an
encoder exists, prefer a round-trip property over a hand-written byte array.

## Three independent decoders agreeing is a real control

The `Damage` origin at protocol 14 was verified against two decoders that share no code with it:

| source | value |
|---|---|
| camera, from the container's `democmdinfo` prologue | (-1012.4, 6068.7, -398.5) |
| explosion, from `svc_Sounds` | (-1008, 6064, -352) |
| damage origin, from the layout under test | (-1061.5, 6127.0, -355.0) |

Nothing is shared between those paths, so agreement is not a tautology. Extended across the corpus
it becomes a distribution — damage origins sit a median 57 units from the camera and never beyond
140 — and a distribution is much harder to satisfy by accident than a single case.

## Record both points of view

Recording a POV *and* a SourceTV capture of the same session is a genuine control, because a
difference between two recordings of one session is an era or mode difference, not a coincidence of
what happened.

It settled two questions that one file could not have: the missing `dem_stringtables` at protocol
14 is an era property rather than a POV quirk, and the 64 KiB schema cap is the **writer's**, not
the parser's.

## Manufacture the evidence

The corpus cannot be extended backwards by searching — pre-2013 demos barely exist. But an era's
client can be made to emit whatever is needed.

Protocol 11's `Damage` rule rested on nothing, because no committed protocol-11 demo contains a
`Damage` message. Fix: play soldier, stand by a resupply cabinet, rocket-jump into yourself for 52
seconds. 43 messages, 460 KB, boundary closed. **For any era whose client runs, a missing message
is a recording task, not a search.**

## Verify by manipulation, not by reading

A test that has never been red proves nothing. Break the thing deliberately, watch the *right* test
fail, and put it back with a precise inverse edit.

Applied to the `Damage` fix, both changes were sabotaged independently: reverting the bound failed
the consumption test and the protocol-14 regression; moving the boundary constant failed the two
protocol-14 tests. And returning the code to its exact pre-fix state failed all three corpus tests,
which is the statement that the new tests would have caught the original bug.

One refinement worth stating: **sensitivity is not validity.** Watching a test go red proves it
*can* fail. It says nothing about whether it fails for the right reason.

## Write the failing test even when you expect it to pass

`PlayerStatsUpdate` was thought to refuse a set bit past the end of the stat table. The test
asserting that **failed**, and the reason was a real finding: the set-bit field is 32 bits while
the table has 44 entries, so bit 31 selects stat 32 and stats 33–44 cannot be sent at all. Valve's
own guard is dead code in that build.

A test written to confirm something and failing instead is the cheapest discovery mechanism there
is.

## A uniform corpus manufactures invariants that look structural

The single most productive day's finding, and it cost nothing but adding recordings of a *different
shape* rather than more of the same.

Every threshold below was written against a corpus of competitive 6v6 SourceTV demos on five maps,
plus listen-server era specimens. Each held perfectly until one recording of a different kind
arrived, and each was measuring the corpus rather than the format:

| assertion | broken by | what it actually encoded |
|---|---|---|
| "more than six players" | a listen-server demo recorded alone | every demo is a competitive match |
| "at least 5,000 snapshots", then 2,000 | a 52-second era specimen | every demo is a long match |
| "user id ≤ 1024" | a pub server up for hours | every server is freshly started |
| "at least 50 entity origins" | a 2v2 Ultiduo demo | every match is 6v6 on a full-sized map |
| "tick offset spread ≤ 64" | a demo recorded during a CPU stall | every recording is unbroken |

None of these were sloppy when written — each had a stated rationale, and several carry comments
explaining why the number was chosen. **The flaw is not the number, it is that the corpus could not
falsify it.**

Two rules follow.

**Prefer assertions that are structural over ones that are observed.** An entity index is bounded by
`MAX_EDICTS` and that is a fact about the engine; a user id is a connection counter with no small
ceiling, and any bound on it describes the servers you happened to record. When the only available
check is a magnitude, choose one that only a *misread* could fail — billions, not thousands — and
say so in the comment.

**Vary the corpus along the axis you are not testing.** Era coverage was pursued hard and paid off;
mode and player-count coverage was never pursued at all, and thirteen recordings chosen for variety
found a real encoder defect (RISKS B27) plus three false invariants in an afternoon. The population
you have not sampled is where your assumptions live.

## Things that misled

**Measuring capability instead of output.** A report built from "can this be written?" printed a
clean queue while 6.3 million bits were still raw hex, because every instance was quietly falling
back. Measure what came out, not what the code is able to do.

**Assuming the id shifted before checking.** When protocol 14's `Damage` produced garbage, a
shifted user-message id was the first suspect — plausible, since ids are registration order. One
histogram of id against body width across five eras eliminated it. **Check alignment before
suspecting a layout**; it is one measurement and it halves the search.

**"It works on protocol 11" when protocol 11 had no instances.** The 2007 demo reported zero
`Damage` messages, which was read as passing. Absence of failure is not evidence of correctness —
always check that the data arrived before concluding anything from its behaviour.

**Reading the reader instead of the writer.** A reader tells you what one client did with the
bytes; a writer states intent — which fields exist, under what condition, clamped to what range.
`Damage`'s ignored long looks like padding from the reader and is live `DMG_` flag data from the
writer. `ResetHUD`'s byte looks the same from the reader and genuinely is a placeholder. Same
symptom, opposite truth, and only the writer distinguishes them.

**Probabilistic framing of a deterministic format.** A rule that is "right 99.7% of the time" is a
wrong rule with an unexamined remainder. Demo playback is deterministic; there is no noise to
average away, and a single counterexample is a defect rather than an outlier.
