---
name: a-null-literal-is-a-comptr
description: "`cond ? view : null` with a ComPtr<T> is a ComPtr wrapping nothing, not a null Nullable; every material drew black (B754)"
metadata:
  type: feedback
---

**`null` converts implicitly to Silk's `ComPtr<T>`** (its conversion from `T*`), so a conditional
`cond ? view : null` whose `view` is a `ComPtr<T>` is typed `ComPtr<T>`, and a method returning
`ComPtr<T>?` then answers a non-null `Nullable` holding a null handle. `is { } x` matches it.

Measured on B754's WeaponSkin bind, 2026-11-16: the "no composite" answer replaced every
material's base texture with that empty handle, and every offscreen model drew black — Phong,
Lambert, override and lighting suites all failed (4 of 7 Phong) while the new test passed,
because its own control (the grey base) was black too. Found by bisecting one guarded call.

**Why:** the compiler picks the conversion silently; nothing warns.

**How to apply:** return `ComPtr<T>` with `default` for "none" and test `Handle is not null`
(`is { Handle: not null } x`); never `ComPtr<T>?`. And a control that reads black is a broken
instrument, not a dark scene — check it before believing the comparison
([[instrument-bugs-outnumber-decoder-bugs]]).
