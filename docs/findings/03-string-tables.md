# 03 — String tables

String tables map indices used throughout the stream — sound names, model names, player info,
precache lists — to their contents. They arrive as `svc_CreateStringTable` during signon and are
amended by `svc_UpdateStringTable`. See `docs/SPEC.md` for the field-level description.

## Entries are delta-coded against a small history

An entry is not simply a string. It may **copy a prefix from one of the last few entries**, with
the number of characters shared and the remainder written out. This is why a naive decoder produces
plausible-looking but wrong names: the mechanism is a compression scheme, and skipping it yields
strings that are almost right.

The history is also the reason re-encoding needed the *shape* recorded rather than just the values.
Which earlier entry a string copied from, and how much it copied, is not recoverable from the
finished string — several encodings produce the same output. Recording the history reference
alongside the decoded text is what makes a byte-identical round trip possible. This is the same
pattern that recurs for sounds, coordinates and entity indices; see
[07-writing-demos.md](07-writing-demos.md).

## The 64 KiB cap is the writer's, not the parser's

**Established by a control, not by reasoning.** A large schema appeared to be truncated at 64 KiB,
which had two possible explanations: this parser was mis-reading a length field, or the game's
writer genuinely stops there.

A POV and a SourceTV recording of the *same session* settle it. Both show the same cap. A parser
bug would have to affect both identically despite different containers; a writer limit explains it
directly. See [08-method.md](08-method.md) on recording both points of view — the pair is the
control, and one file could not have distinguished the cases.

This is a general shape worth naming: **when a limit appears, ask whether it belongs to the reader
or the writer**, and find a comparison that separates them.

## Era differences

| Protocol | Behaviour |
|---|---|
| 14 and below | **no `dem_stringtables` command exists at all** — tables arrive only via `svc_CreateStringTable` |
| 14 and below | no compression flag on `svc_CreateStringTable` (`PROTOCOL_VERSION_14`) |
| above 23 | lengths become varints (`PROTOCOL_VERSION_23`); fixed 20-bit below |

The first is **not in `proto_version.h`** and was not predicted — it was discovered when a
protocol-14 demo simply did not contain the command. Confirmed as an era property rather than a
recording-mode quirk by the POV/SourceTV pair. The other two were written from `proto_version.h`
before any demo existed that could exercise them, and were later confirmed by demos that had never
run through those branches.

## The table count dates nothing; the names do

Sixteen string tables at protocols 11, 14, 15, 16 and 24 alike — eighteen years and the number
never moves. It was briefly treated as an era fingerprint and is worthless as one.

The table **names** do change across eras, and `max_classes` moves (216, 216, 232, 256, 275), so
those are the discriminators. Recorded because "a quantity that looks like a fingerprint" is worth
checking against more than one era before relying on it.

## A user id is a connection counter, not a player slot

From the `userinfo` table. The server increments it for **every client that has ever joined**, so
it says nothing about how many players are present and has no small ceiling.

Measured: the committed corpus's rosters sit in the 490–530 range, while a 2026 pub server that had
been up for hours runs **1090–1147 across 23 players**. Both are correct.

This was worth catching because it had been asserted otherwise — a plausibility check bounded user
ids at 1024, which held only because every corpus demo was recorded on a freshly started listen
server where the counter had barely moved. The same shape as the "more than six players" assumption
that preceded it: **a corpus of one kind of recording makes accidental invariants look structural.**

What *is* structural is the entity index, which MAX_EDICTS genuinely bounds. The user id's only
real constraint is that it is a non-negative int, so the useful check is one only a bit-level
misread could fail — billions, not thousands.

## Everything is UTF-8

Player names are arbitrary client bytes and routinely carry Cyrillic and CJK. An ASCII decoder
turns those into question marks — and produces a *plausible* name, not an error.

Found because one demo's recording player has a non-ASCII character in their name and the dump
printed it two different ways in one output: mangled from the header, correct from the userinfo
table. **The disagreement between two decoders was the only signal**; either alone looked fine.
Every string decoder in the project is UTF-8 as a result.

## Valve's header contradicts itself about the table id, and arithmetic settles it

`networkstringtabledefs.h:20`, one line, both halves published:

```c
#define MAX_TABLES	32  // Table id is 4 bits
```

**Thirty-two identifiers need five bits. Four bits address sixteen.** The constant and its comment
cannot both describe the wire, and no new evidence is needed to decide which one is wrong — the
arithmetic excludes four outright.

Five is what this project reads, and it decodes every era in the corpus, protocols 11 through 24. A
four-bit field would shift every subsequent field in the message by one bit, so the eras would not
merely be wrong, they would fail to parse at all. **Evidence class: arithmetic, confirmed
differentially against five measured protocols.**

The likely history is that `MAX_TABLES` was raised at some point and the comment was not touched.
That part is **inference and is flagged as one** — nothing in the published tree dates either half.

**Why this is worth a section rather than a footnote.** A stale comment inside a header is more
dangerous than no comment at all: it sits beside a real constant, it reads as documentation of the
wire, and it is wrong. An implementation written from that line produces a decoder that fails on
nothing in particular — no exception, no obviously bad value, just every field after the id read one
bit early. That is the same failure shape as the two send-prop flag traps in `04-entities.md`, and
the same defence applies: derive the width from the limit rather than trusting a number that was
typed by hand next to it.

`StringTableWidthConformanceTests` holds both halves — the derived width, and the fact that the
comment still says four. If Valve ever corrects it, the second test fails; the right response is to
rewrite this section in the past tense rather than delete it, because the fact that it *was* wrong is
why the width was worth checking.
