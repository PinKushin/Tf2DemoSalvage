---
name: two-agreeing-measurements-can-share-one-instrument
description: A number disagreeing with a written-down one is a dispute between instruments first; repeating the same command in a clean checkout is not a control.
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-04T19:53:33.857Z
---

**When a measurement contradicts a number somebody wrote down, suspect the two INSTRUMENTS before
suspecting the subject** — and a second run of the same command is not a second instrument, however
clean the checkout.

Measured 2026-09-04. `build/gate.sh` said the rendering floor was **726**. A plain
`dotnet test tests/Tf2DemoSalvage.Rendering.Tests` reported `Total: 725` with seven tests just
added, so the assembly looked eight short. The check that "confirmed" it was a `git worktree` at the
very commit that set 726, built clean, run the same way: **718**, twice, agreeing.

Everything about that reads like proof. It was two readings from one instrument.

**`dotnet test`'s console summary and the `.trx` counters count different things.** Console
`Total: 725` is 672 passed + 53 skipped. The trx's `total="733"` is 672 executed + 61 not-executed.
The eight `[Explicit]` tests are in one and not the other, and `build/assert-test-count.sh` greps
`total="…"` out of the trx on purpose — its own comment says why. 726 was right the whole time.

**How to not spend an hour on this:**

- Measure the way the thing you are disputing measures. The floor comes from the trx, so ask the
  trx: `grep -oE 'total="[0-9]+"' tests/<Project>/TestResults/<name>.trx`.
- Reproducing a reading is not controlling it. A control uses a DIFFERENT route to the same value —
  see [[two-recordings-of-one-value]] and [[an-empty-search-needs-a-control]].
- Rendering is the only project here where the two totals disagree, which is why it is the one that
  catches the mistake. Other projects agree, so the wrong habit passes everywhere else.

Related: [[read-the-trx-total-not-the-console]], which says to read the trx and did not say that the
console's number is a different quantity rather than a truncation of the same one.
