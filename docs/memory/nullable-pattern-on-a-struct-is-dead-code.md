---
name: nullable-pattern-on-a-struct-is-dead-code
description: FirstOrDefault plus `is not { }` compiles against a record struct and never fires; this codebase is full of record structs, so the idiom is a live hazard.
metadata:
  type: project
---

```csharp
DemoCommand? tables = commands.FirstOrDefault(c => c.Type == DemoCommandType.DataTables);
if (tables is not { } dataTables) { return new DemoTimeline([]); }   // never taken
```

`DemoCommand` is a `readonly record struct`, so `FirstOrDefault` returns `default(T)` rather than
`null`, and the implicit conversion wraps that default in a **non-null** `DemoCommand?`. The guard
compiles, reads exactly like the reference-type idiom everyone writes, and is dead.

**Why:** found 2026-08-19 in `DemoTimeline.Build`. A demo carrying no `dem_datatables` fell past the
guard into `SendTableParser` with an empty payload and threw *"the payload ends mid-table after 0
bytes"* instead of returning an empty timeline. Every real demo has that command, so the corpus
could not reach the path — it took a synthetic demo built deliberately without one. See
[[author-the-specimen-the-corpus-lacks]].

**How to apply:** this repository is full of record structs — `DemoCommand`, `SendProperty`,
`DecodedProperty`, `ScenePlayer`, `GameEventField`, `ServerClass`. Whenever `FirstOrDefault`,
`SingleOrDefault` or `ElementAtOrDefault` runs over a sequence of them, test a **field** that cannot
hold a valid default rather than testing the reference:

```csharp
DemoCommand tables = commands.FirstOrDefault(c => c.Type == DemoCommandType.DataTables);
if (tables.Type != DemoCommandType.DataTables) { ... }
```

`DemoCommandType` has no zero member on purpose, which is what makes that check sound — a defaulted
struct cannot collide with a genuine command. Where an enum does have a zero member, use
`.Cast<T?>().FirstOrDefault()` or `Where(...).Select(x => (T?)x).FirstOrDefault()` instead.

The compiler will not warn. Nothing here is a type error; it is a correct program that means
something other than what it looks like, which is why it survived review and a full test suite.
