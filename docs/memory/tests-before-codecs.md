---
name: tests-before-codecs
description: "Write unit tests before each decoder, not after — mutation testing has now caught the same lapse three times, and corpus tests cannot substitute"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-10T22:51:01.463Z
---

**Write unit tests before the decoder, every time.** Three codecs written tests-after each passed
their corpus tests and looked finished; each time mutation testing found dozens to 86 survivors in
that file alone. **Read the survivors, not the score** — a low aggregate score reads like a broad
quality problem and is really three files written before their tests in an otherwise-fine codebase.

**Why corpus tests don't cover for it:** a real demo exercises only the paths it happens to use — a
string-table codec has branches for fixed vs. variable user data, back-references, history eviction,
that the corpus touches maybe half of. End-to-end tests prove the demos we have work; they say nothing
about untaken branches, exactly where a silent misread waits.

**How to apply:** before writing a codec, write the synthetic fixture builder and tests for each
branch the format describes, including malformed cases. The builder is reusable and usually the
harder half.

## The fixture trap that cost the most time

**Bit-level fixtures must share one continuous `BitWriter`** — building message A, calling `Build()`
(which pads to a byte boundary), then appending B to a fresh writer desyncs the reader with no error;
it looks exactly like a decoder bug. A helper that writes INTO an existing writer, not one returning
fresh bytes, is required for "what comes after A still decodes" tests.

Related: trailing zero padding decodes as `net_NOP` (message id 0) — fixtures must expect or filter
those. See [[era-axis-is-measured]].

---

## The red step for a NEW type is a compile failure, not a failing assertion

This project's analyzers are strict enough that TDD placeholder stub types (`throw
NotImplementedException` everywhere) don't compile (CA1065, S2325). **Write tests first and implement
directly** — don't stage a stub, don't relax analyzer settings.

**The same strictness makes a lazy sabotage impossible** — pick one that keeps every symbol used
(OR-ing a value into a flag set, `+ 500` on an index). See [[most-of-a-decoder-is-untested]].

Related: [[mutation-score-is-not-the-goal]].
