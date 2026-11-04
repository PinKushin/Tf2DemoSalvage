---
name: a-silk-ref-comptr-does-not-pin
description: Silk.NET's ref ComPtr overloads and GetAddressOf hand native code an unpinned address; on a heap field a compacting GC makes it stale. PinnedComCalls shadows them, a new overload shape falls through silently.
metadata:
  type: project
---

**`ref _field` on a `ComPtr<T>` is not pinned by Silk.NET.** `ComPtr<T>.GetAddressOf()` is `(T**)Unsafe.AsPointer(ref Unsafe.AsRef(in this))`, and every generated `ref ComPtr<T>` overload passes that on, where its other `ref` parameters use `fixed`. It reads like normal interop and compiles clean. A collection on another thread moves the object, and native code reads (a set) or writes (a create) where it used to be: `0xC0000005` under `PSSetConstantBuffers`, on a call the same field had just survived one line earlier (B706).

**How to apply:** `PinnedComCalls` (namespace `Tf2DemoSalvage.Render`) shadows the overloads a field reaches, because extension lookup asks the enclosing namespace before the file's `using`. **A call whose shape matches nothing there falls through to Silk.NET without a word**, and `UnpinnedComPtrConformanceTests` is what catches it: when it names a call, add the overload. A `ref local` or a by-value parameter is on the stack and fine. Do not "fix" this by removing the post-load `GC.Collect` (D199): it only makes the fault louder, and any gen0 or gen1 collection can move a young object.

**Why it hid:** it needs a collection to land in a window of a few instructions, which tier 0 code (the first frames after a load) and a two-core runner widen. A stale read of a region not yet decommitted gets the old, correct handle and nothing happens. Both local gates passed on the same commit, and CI's rerun of the failed job passed too.

**The instrument that found it:** a process with no viewer in it, one thread making the call and one collecting, with a control differing only in where the address lives. See `docs/measurements/2026-11-04-unpinned-comptr.md`.
