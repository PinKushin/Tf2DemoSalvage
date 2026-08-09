---
name: arithmetic-settles-disputes
description: A field's bit width constrains which numbering can be in use; check that before treating a format dispute as needing new evidence.
metadata:
  type: project
---

When this parser and the reference implementation disagreed on the meaning of game event field
type 7 (RISKS B14), the dispute was recorded as unsettleable without an old demo — both readings
were plausible, neither was exercised by the corpus, and the note said to go read a Source SDK
header.

It was settled on 2026-08-09 with no new data at all. **The type field is three bits.** The
reading this parser used came from CS:GO's protobuf ordering, which places `val_uint64` eighth
and `val_wstring` ninth — a numbering that does not fit in three bits. The wrong answer was
excluded by counting, not by finding better sources.

The project's own enum carried the comment "Three bits on the wire" two lines above the mistake.

**Why:** a disputed field is often over-constrained already. A width, a terminator value, a
maximum count, or an alignment requirement can rule out a candidate numbering outright, and that
check costs nothing next to sourcing a specimen or reading a codebase. Reaching for new evidence
first is the expensive move, and here it would have blocked on a demo that does not exist.

**How to apply:** before recording a format question as needing external evidence, write down what
the surrounding bits already fix — how wide the field is, what values are reserved, what the
maximum is — and check every candidate against it. Especially suspect any answer imported from a
*later* version of the same format: Source's protobuf era renumbered things that the hand-packed
bit era had no room for, so CS:GO orderings are not evidence about TF2's wire layout.

See [[numeric-decoding-traps]] for the other half of this: values that are wrong but plausible.
Related: [[differential-beats-fixtures]], [[layer2-is-a-dependency-chain]].
