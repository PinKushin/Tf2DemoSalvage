# 04 — Entities: schema and delta decoding

The layer that makes the whole project viable, and the one Valve publishes least about — the delta
engine lives in `engine.dll`, which is closed. See `docs/SPEC.md` Layer 3 for the current
description.

## The demo carries its own schema, which is why this is tractable at all

`dem_datatables` embeds the server's `SendTable` definitions: every networked class, its
properties, their types, bit widths and flags. **A parser that decodes generically off whatever
schema the file provides does not need to know any particular TF2 version.**

That is the founding insight of the project. The alternative — hardcoding one era's field layout —
is what makes other tools break when Valve changes the schema, and it is why demos the live client
can no longer play are still readable here. What actually has to be era-aware is the *container and
bit-packing*, which changes far less often ([06](06-protocol-eras.md)).

## Flattening order was wrong, and only a differential could have caught it

Properties are declared in nested tables and must be **flattened** into the linear order the wire
uses. Getting that order wrong does not throw: it reads the right number of bits into the wrong
fields, producing complete, plausible entities that are silently mislabelled.

No hand-built fixture could have found this, and none did. A fixture encodes what its author
already believes, so the test and the implementation shared the misunderstanding and agreed
perfectly. It died in a single differential run against `demostf/parser`, across **204,000
properties**.

**This is the strongest argument in the project for differential testing over fixtures**, and it
generalises: where you are testing your *reading of a spec* rather than your code, you need a
second independent implementation, not a better test.

## The deletion list can end without its terminator

A long-standing mystery: some snapshots re-encoded *longer* than the original, and some deletion
lists simply stopped.

The cause is not in the format — it is in the writer. `bf_write` **abandons a field that does not
fit rather than truncating it**, so when the buffer fills mid-list the terminator is never written
and the message just stops. Detail and the published source in
[09-valve-implementation.md](09-valve-implementation.md).

Two consequences:

- A decoder must treat "ran out of stated length" as a legitimate end of list. Guarding the read
  with `if (lengthBits - reader.BitsRead < RemovedIndexBits) break;` recovered 366 snapshots that
  previously failed outright.
- A faithful **re-encoder must reproduce the giving-up**, not politely finish the list. Writing the
  terminator Valve omitted produces bytes Valve never wrote.

## Numeric encodings fail as plausible numbers, never as errors

The recurring hazard in this layer. Every one of these produces a number rather than an exception
when read wrongly:

- **Range-encoded floats** — a value packed into N bits between a stated min and max. Read with the
  wrong width and you get a real number in a believable range.
- **Sign extension** — a signed field read as unsigned is only wrong for negative values, so it
  passes on most data.
- **Derived square roots** — a third component reconstructed from two others produces `NaN` when
  the inputs are slightly wrong, and `NaN` propagates silently.

The defence is plausibility bounds drawn from the format itself rather than from taste: coordinates
inside the world extent, entity indices inside `MAX_EDICTS`, volumes in 0…1, sound indices inside
the precache table's own size. The last is the sharpest, because the index comes from the bit
stream and the table comes from `svc_CreateStringTable` by a completely independent path — there is
no way to land inside it by accident across thousands of sounds.

## Coordinates are the unit of measurement for this whole format

From `public/coordsize.h`: 14 integer bits, 5 fractional. A `ReadBitCoord` is two presence bits, a
sign bit if either is set, then the parts that were sent — so an axis is 22 bits with a fraction,
17 integer-only, 8 fraction-only, 2 absent.

Those four numbers identify message layouts from body lengths alone, before any payload is read.
They are used that way repeatedly in [05](05-user-messages.md) and [08](08-method.md).

## Where it stands

All corpus demos decode end to end with no stops, across every protocol held. Entity snapshots
re-encode byte-identically except for a residue of roughly a thousand, which remains open and is
tracked in `docs/RISKS.md`.
