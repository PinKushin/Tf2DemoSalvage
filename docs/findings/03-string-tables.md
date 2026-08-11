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

## Everything is UTF-8

Player names are arbitrary client bytes and routinely carry Cyrillic and CJK. An ASCII decoder
turns those into question marks — and produces a *plausible* name, not an error.

Found because one demo's recording player has a non-ASCII character in their name and the dump
printed it two different ways in one output: mangled from the header, correct from the userinfo
table. **The disagreement between two decoders was the only signal**; either alone looked fine.
Every string decoder in the project is UTF-8 as a result.
