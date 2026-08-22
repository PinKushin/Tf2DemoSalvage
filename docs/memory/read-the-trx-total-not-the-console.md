---
name: read-the-trx-total-not-the-console
description: The gate's floors come from the trx total, which is larger than the console's total — never bump a floor from the console line.
metadata:
  type: project
---

**`build/assert-test-count.sh` reads `total=` out of the `.trx`. The console prints a different,
smaller number. Bump floors from the trx.**

Measured on Content.Tests, 2026-08-22, same run:

| source | number |
|---|---|
| console `Total:` | 623 (610 passed + 13 skipped) |
| trx `<Counters total=>` | **638** |
| trx `executed=` | 610 |

The gap is `[Explicit]` tests, which are discovered and counted in the trx total but not run. The
floors in `build/gate.sh` are therefore all trx numbers, and this is why the file's comments can say
things like "613: SoundFormatProbe, `[Explicit]`" — an `[Explicit]` probe raises the floor even
though it never executes.

**Why this is worth a memory:** reading the console after adding 9 tests gave 623 against a floor of
628, which looks exactly like *tests were silently lost* — the precise failure the floors exist to
catch. Several minutes went into hunting a regression that was not there. The real count was 638,
which is 628 + 9 conformance + 1 probe, matching to the unit.

**How to apply:**

```bash
bash build/assert-test-count.sh "**/content.trx" <old-floor> content
```

It prints `content: 638 executed, 0 failed (floor 628)` — that first number is the new floor. Run it
rather than doing arithmetic on the console line.

Related: [[a-floor-must-track-the-number-it-guards]] (the floor must be the exact count, not a
comfortable distance below it) and [[a-skip-is-not-a-pass-or-a-failure]] (skips count toward the
total, so a floor cannot see a disarmed test).
