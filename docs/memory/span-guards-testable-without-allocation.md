---
name: span-guards-testable-without-allocation
description: Length-guard throws on spans are testable without allocating — fabricate the Length with MemoryMarshal.CreateReadOnlySpan.
metadata:
  type: project
---

BitReader's constructor guard (reject spans over `MaxByteLength` ≈ 256 MB) carried a
`// Stryker disable next-line Statement,String` comment claiming the throw was unreachable
without allocating a quarter-gigabyte buffer per test run. That claim was wrong: the guard
rejects on `data.Length` alone and never dereferences, so
`MemoryMarshal.CreateReadOnlySpan(ref placeholder, int.MaxValue)` over a single stack byte
reaches it for free. Test added 2026-08-08
(`Constructor_SpanOverTheAddressableLimit_ThrowsBeforeTouchingTheBuffer`), disable comment
removed.

**Why:** a Stryker disable is a claim that a mutant is untestable; this one survived two
review passes because the "256 MB" reasoning sounded airtight. The trap generalizes: any
guard that checks a span/array *length before touching elements* is testable with a
fabricated length — the buffer never has to exist.

**How to apply:** before writing a Stryker disable for "input too big to construct", check
whether the guarded code reads the length only. If it does, fabricate the length
(`MemoryMarshal.CreateReadOnlySpan` / `CreateSpan`) inside the throwing lambda and assert
the exact message. Only genuinely dereferencing paths justify the disable. Related:
[[fixtures-are-the-weak-point]], [[tests-before-codecs]].
