---
name: span-guards-testable-without-allocation
description: Length-guard throws on spans are testable without allocating — fabricate the Length with MemoryMarshal.CreateReadOnlySpan.
metadata:
  type: project
---

A constructor guard (reject spans over ~256MB) carried a Stryker-disable comment claiming the throw
was untestable without allocating a quarter-gigabyte buffer. Wrong: the guard checks `data.Length`
alone and never dereferences, so `MemoryMarshal.CreateReadOnlySpan(ref placeholder, int.MaxValue)`
over one stack byte reaches it for free.

**Why:** the disable survived two review passes because the "256MB" reasoning sounded airtight. Any
guard checking a span/array LENGTH before touching elements is testable with a fabricated length —
the buffer never has to exist.

**How to apply:** before writing a Stryker disable for "input too big to construct", check whether the
guarded code reads only the length. If so, fabricate the length and assert the exact message. Only
genuinely dereferencing paths justify the disable. Related: [[fixtures-are-the-weak-point]],
[[tests-before-codecs]].
