---
name: an-exception-type-can-be-load-bearing
description: "When a handler upstack attaches context to ONE exception type, every other type loses it silently — grep the catch before choosing what to throw."
metadata: 
  node_type: memory
  type: project
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-09T03:54:36.723Z
---

**Ask what catches an exception before deciding its type doesn't matter.** `DemoAssembly.cs:533`
catches only `InvalidDataException`, to rethrow with the offending line attached — the entire
mechanism by which a hand-edited decompiled trace reports which line broke. A typo in a field NAME
reported file/line/field; a typo in the update type three tokens earlier reported a bare
`Enum.Parse` `ArgumentException` with no line at all. Same for `int.Parse` (`FormatException`), a raw
indexer (`ArgumentOutOfRangeException`), a dictionary lookup (`KeyNotFoundException`),
`Convert.FromHexString` (`FormatException`).

**Why:** the type is not a detail when a handler selects on it — invisible from reading either the
contract or the throw site, only from reading the catch. B344 fixed one file; B345 found the same
split in four more (28 sites), including a function getting it right three times and wrong once eight
lines later.

**How to apply:** before writing a `throw`, grep for what catches it upstack. Route every refusal in
a subsystem through helpers that raise the caught type (`TryParse` + a message quoting the offending
text), never a bare `Parse`. One shared helper family, not per-file copies.

**A corpus cannot find this** — every demo is valid, so round-trip suites never exercise refusals;
they exist for hand-edited input (`MessageAssembly.cs:332` hit one on the first demo whose players
spoke, patched at that one site instead of as a contract).

**B345 closed the other four layers**: 28 bare reads across `MessageAssembly`, `EventAssembly`,
`StringTableAssembly`, `DemoAssembly`, `PropertyText`, routed through one shared `AssemblyText`.

**Worst find was not a message: `PropertyText` trusted a declared length** — an array property's
element count went straight to `new List<PropertyValue>(count)` before any element was read, so `a
2000000000` raised `OutOfMemoryException` (measured, not predicted). `docs/FUZZING.md` had already
named the class and symptom without connecting it to this layer.

**Added rule:** when routing a parse through a refusal helper, ask separately whether any value is
used as a SIZE — a length prefix from text needs a ceiling, usually already present in the input
(tokens remaining on the line).

**Two behaviours tightened, not just re-typed:** replacing `uint.Parse` with a wider parse *loosens*
it (`long` accepts a negative below `int.MinValue` and wraps silently) — keep the original range.
`short.Parse`/`byte.Parse` did range checking as a side effect; swapping in `int.Parse` drops it,
restore explicitly (`modevents.res` documents widths).

Related: [[output-level-assertion-or-it-is-not-done]], [[most-of-a-decoder-is-untested]],
[[logs-are-the-debugger]], [[one-place-or-it-drifts]], [[fixtures-are-the-weak-point]].
