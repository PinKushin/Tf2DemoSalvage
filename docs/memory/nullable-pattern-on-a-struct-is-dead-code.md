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

## The same shape by implicit conversion, found 2026-08-22

A second route to the identical bug, and this one needs no `FirstOrDefault` at all:

```csharp
Func<string, ReadOnlyMemory<byte>?> read =
    path => files.TryGetValue(path, out string? text) ? Encoding.UTF8.GetBytes(text) : null;
```

`byte[]` converts implicitly to `ReadOnlyMemory<byte>`, so the conditional takes **`byte[]`** as its
natural type and the `null` branch is a null *array*. Converting that to the target produces
`default(ReadOnlyMemory<byte>)` — an EMPTY memory, wrapped in a **non-null** nullable. Every absent
file arrived as a present, empty one, and `if (read(path) is not { })` never fired.

Caught by `SoundScriptCatalogConformanceTests.Load_AListedScriptThatIsAbsent_IsSkippedRatherThanFatal`,
which reported two scripts loaded against a single file that existed.

**How to apply: prefer `byte[]?` over `ReadOnlyMemory<byte>?` in any API where null means absent.**
`byte[]?` has no implicit conversion that can swallow the null, so the mistake becomes
inexpressible rather than merely documented. The same caution applies to any `T?` whose `T` has an
implicit conversion FROM a reference type — `ReadOnlySpan`, `Memory`, and `ImmutableArray<T>` (whose
`default` is the notorious case) all behave this way.

Note that `VpkArchive.ReadFile` already returns `byte[]?`, so the real call site had the same latent
bug purely from the delegate's signature — the trap was in the API's shape, not in either caller.

## It happened AGAIN, twice, with this note already written — 2026-09-04

The section above ends with *"prefer `byte[]?` over `ReadOnlyMemory<byte>?` in any API where null
means absent"* and names `VpkArchive.ReadFile` as the exact call site. Two more instances were
written anyway, in two different assemblies, and neither author was stopped by the note:

```csharp
// tests/…/StudioIkLockTests.cs — the game-not-installed guard
… is { } archive && … .ReadFile(path) is { } bytes ? bytes : null;

// managed/Tf2DemoSalvage.Scene/EntityModels.cs:746 — the jiggle bones' root model
posed.JiggleSource = skinned.Models.Count > 0 ? skinned.Models[0] : null;
```

**The first was invisible on every developer machine and failed only on CI**, with
`sequences should be greater than 0 but was 0` — a message that reads as a broken READER rather
than as a dead guard. Four days of red. See [[ci-is-the-machine-without-tf2]]; this is the first
defect that note has caught which was a wrong ANSWER rather than a missing-install crash.

**The second is in production and had no symptom at all**, because `StudioJiggleBones.Read` is
total and answers null for a span too short to describe itself — the guard's job was being done one
call further down, by accident. That is worse than a visible bug, not better: it will start
mattering the moment that reader gains a fixture it can parse.

**So the rule is not enough, and here is what to do instead.** Nothing in the compiler, the
analyzers or a local test run can see this; only a machine without the data can, and only for the
sites that read data. Two things that DO work:

- **Grep for the shape when touching any of it**:
  `grep -rn "ReadOnlyMemory<byte>?" --include=*.cs` returned four declarations in the whole
  repository. That is a small enough set to read every one, and doing so found both bugs in a
  minute.
- **Reproduce the absent case locally rather than reasoning about it.** Naming an archive that
  cannot exist (`GameInstall.Vpk("tf2_absent")`) turned a CI-only failure into a local one in a
  single edit, and the three states — absent+broken FAILS, absent+fixed SKIPS, present+fixed
  PASSES — settled it with no speculation at all.

CA1819 forbids an array *property*, so `JiggleSource` could not simply become `byte[]?`; there the
burden falls on the assignment, spelled `: (ReadOnlyMemory<byte>?)null`.
