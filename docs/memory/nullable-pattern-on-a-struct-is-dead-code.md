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

`DemoCommand` is a `readonly record struct`, so `FirstOrDefault` returns `default(T)` not `null`, and
the implicit conversion wraps that default in a NON-NULL `DemoCommand?`. The guard compiles, looks
like the reference-type idiom, and is dead.

**Found in `DemoTimeline.Build`:** a demo with no `dem_datatables` fell past the guard and threw
instead of returning empty — every real demo has that command, so the corpus couldn't reach the
path; needed a synthetic demo built without one. See [[author-the-specimen-the-corpus-lacks]].

**How to apply:** this repo is full of record structs. Test a FIELD that can't hold a valid default,
never the reference:
```csharp
DemoCommand tables = commands.FirstOrDefault(...);
if (tables.Type != DemoCommandType.DataTables) { ... }
```
Where an enum HAS a zero member, use `.Cast<T?>().FirstOrDefault()` instead. The compiler never
warns — this is a correct program meaning something other than what it looks like.

## The same shape by implicit conversion

```csharp
Func<string, ReadOnlyMemory<byte>?> read =
    path => files.TryGetValue(path, out string? text) ? Encoding.UTF8.GetBytes(text) : null;
```
`byte[]` converts implicitly to `ReadOnlyMemory<byte>`, so the ternary's natural type is `byte[]` and
the null branch becomes `default(ReadOnlyMemory<byte>)` — an EMPTY memory wrapped non-null. Every
absent file arrived as present-and-empty.

**How to apply: prefer `byte[]?` over `ReadOnlyMemory<byte>?` in any API where null means absent** —
`byte[]?` has no implicit conversion that swallows null. Same caution for any `T?` whose `T` has an
implicit conversion FROM a reference type (`ReadOnlySpan`, `Memory`, `ImmutableArray<T>`).

## It happened AGAIN, twice, with this note already written

Two more instances shipped in two assemblies despite the note naming the exact pattern. One was
invisible on every dev machine, failing only on CI with a message reading like a broken reader — four
days red. The other was in production with NO symptom at all, because a downstream reader happened to
handle the null case anyway — worse than a visible bug, since it will start mattering the moment that
reader gains a fixture it can parse.

**What actually works:**
- `grep -rn "ReadOnlyMemory<byte>?" --include=*.cs` — small enough set to read every hit; found both
  bugs in a minute.
- Reproduce the absent case locally (name an archive that can't exist) rather than reasoning about
  it — turns a CI-only failure into a local one in one edit.

CA1819 forbids an array property, so an affected property became `(ReadOnlyMemory<byte>?)null` at the
assignment instead.
