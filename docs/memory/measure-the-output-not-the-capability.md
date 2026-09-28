---
name: measure-the-output-not-the-capability
description: A coverage report that asks "can this be handled" instead of "was it handled" reads clean while most of the work is undone.
metadata:
  type: project
---

A progress report must count what the code PRODUCED, not what it's ABLE to produce — they diverge
whenever something can fail per instance.

The writer's "still raw" report asked `MessageAssembly.CanWrite(type)` and printed an empty queue
while 6.3 million bits were still hex, because the writer silently falls back to `raw` on a mismatch.
A type declining on every single instance looked identical to one fully promoted — believed for two
commits.

**Fix, and the second part is what makes it stick:** count the emitted output, so the number can't
disagree with the file; make output say WHY (each `raw` line names the message type and whether a
text form declined) — a queue and a defect are different findings sharing one keyword.

**A second instance, same file, same day:** a round-trip report compared over the STATED length of a
body rather than the bits actually written — the encoder zero-fills unhandled bits, so the comparison
measured its own padding as a decoder defect. 96.87% became 99.59% narrowing to content; the 3% never
existed.

**Both cases share a tell: the measurement covered ground the code under test never claimed.**

**How to apply:** whenever a report is built from a predicate rather than the artefact, ask what
happens when the predicate is true and the operation still fails — if possible, the report measures
the wrong thing. Related: [[mutation-score-is-not-the-goal]], [[ask-whether-the-data-arrived]],
[[round-trip-needs-the-encoding-shape]].
