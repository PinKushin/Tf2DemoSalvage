---
name: measure-the-output-not-the-capability
description: A coverage report that asks "can this be handled" instead of "was it handled" reads clean while most of the work is undone.
metadata:
  type: project
---

A progress report has to count what the code **produced**, not what it is **able** to
produce. Those diverge exactly when something can fail per instance.

Measured on 2026-08-11: the assembly writer's "still raw" report asked
`MessageAssembly.CanWrite(type)` and printed an empty queue — while 6.3 million bits were
still hex, because the writer verifies each candidate and silently falls back to `raw` on a
mismatch. A type whose text form declined on every single instance looked identical to one
that was fully promoted. The report was believed for two commits and stated as a finished
result before the owner asked "are you sure that's the floor".

The fix has two parts and the second is what makes it stick:

- Count the emitted output, so the number cannot disagree with the file.
- Make the output say why. Each `raw` line now carries a comment naming the message type
  and whether a text form existed and *declined* — a queue and a defect are different
  findings and had been sharing one keyword.

Attributing the declines then took one measurement, not a guess: four of the five causes
were a single bug (verification state rebuilt per packet instead of carried across the demo).

**How to apply:** whenever a report is built from a predicate rather than from the artefact,
ask what happens when the predicate is true and the operation still fails. If that is
possible, the report is measuring the wrong thing. Related:
[[mutation-score-is-not-the-goal]], [[ask-whether-the-data-arrived]],
[[round-trip-needs-the-encoding-shape]].
