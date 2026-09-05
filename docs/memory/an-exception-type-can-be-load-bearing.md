---
name: an-exception-type-can-be-load-bearing
description: "When a handler upstack attaches context to ONE exception type, every other type loses it silently — grep the catch before choosing what to throw."
metadata: 
  node_type: memory
  type: project
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-05T08:20:50.971Z
---

**Ask what catches it before deciding it does not matter which type you throw.**

`DemoAssembly.cs:533` catches `InvalidDataException` and nothing else, for a single purpose: to
rethrow it with the offending line attached — `$"{failure.Message} (assembling: {line})"`. That is
the entire mechanism by which somebody hand-editing a decompiled trace learns *which line* they
broke.

So in `EntityAssembly` a typo in a field NAME reported the file, the line and the field, while a
typo in that same line's update type — three tokens earlier — reported
`Requested value 'entre' was not found` and arrived with no line at all. A bare `Enum.Parse` raises
`ArgumentException`, which walks straight past the handler. Same for `int.Parse`
(`FormatException`), a raw indexer (`ArgumentOutOfRangeException`), a raw dictionary lookup
(`KeyNotFoundException`) and `Convert.FromHexString` (`FormatException`).

**Why:** the type is not a detail when a handler selects on it. A file can state its contract
perfectly in five places and lose it in fourteen others, and nothing about that is visible from
reading either place — only from reading the catch. B344 fixed it in one file; B345 found the same
split in four more, twenty-eight sites, including a function
(`DemoAssembly.ParseCommand`) that gets it right three times and wrong once eight lines later.

**How to apply:** before writing a `throw`, grep for what catches it upstack. When any handler
selects on a type, route EVERY refusal in that subsystem through helpers that raise it — `TryParse`
plus a message quoting the offending text, never a bare `Parse`. Prefer one shared helper family
over per-file copies; the subject noun is the only part that differs.

**A corpus cannot find this.** Every demo is a valid recording, so the round-trip suites only ever
hand well-formed text; the refusals exist for hand-edited input. The class already reached a real
demo once — `MessageAssembly.cs:332` records `tokens[4]` throwing on the first demo whose players
spoke — and was patched at that one site rather than as the contract.

**B345 closed the other four layers, and the worst find was not a message.** Twenty-eight bare reads
across `MessageAssembly`, `EventAssembly`, `StringTableAssembly`, `DemoAssembly` and `PropertyText`,
all routed through one shared `AssemblyText` with the subject noun passed by the caller — so each
file keeps the voice it already had while the logic lives once.

**`PropertyText` trusted a declared length.** An array property's element count went straight to
`new List<PropertyValue>(count)` *before* any element was read, so `a 2000000000` raised
`OutOfMemoryException` — measured, not predicted. `docs/FUZZING.md` had already named the class
("length-prefix decoders are where unbounded allocations come from") and the symptom ("an
`OutOfMemoryException` ... a caller cannot reasonably defend against") without connecting either to
this layer.

**How to apply, added:** when you route a parse through a refusal helper, ask separately whether any
value is used as a SIZE. A length prefix from text needs a ceiling, and the honest ceiling is
usually already present in the input — here, the number of tokens the line has left, which no valid
array can exceed and which needs no constant chosen.

**And two behaviours were tightened, not merely re-typed** — both worth checking for whenever a bare
parse is replaced:

- Replacing `uint.Parse` with a wider parse *loosens* it. `long` would accept a negative below
  `int.MinValue` and wrap it silently where `uint` refused. Keep the original range.
- `short.Parse`/`byte.Parse` were doing range checking as a side effect. Swapping in `int.Parse`
  drops it, so the range has to be restored explicitly — `modevents.res` documents the widths.

Related: [[output-level-assertion-or-it-is-not-done]], [[most-of-a-decoder-is-untested]],
[[logs-are-the-debugger]], [[one-place-or-it-drifts]], [[real-data-hides-bugs-small-inputs-expose]].
