---
name: A callee's signature is the caller's TypeRef
description: The thin-view reach list counts every type in the signature of every member the view calls, named or not; sign a presenter the view calls in types it already holds.
type: project
---

**The thin-view instrument (D214, B540) lists a domain type the view never names, if a member the view calls has it
in its signature.** A call compiles to a `MemberRef`, the `MemberRef` carries the callee's signature, and each type in
that signature is a `TypeRef` row in the CALLER's assembly (ECMA-335 II.23.2). `ThinViewConformanceTests` reads the
view's `TypeRef` table, so the type is on the list.

**Why it matters:** B540 first said the opposite — that a signature type shows only when the view names it — and
slice 1 was designed on that. A presenter constructor taking the new `Scene.IPosedEntities` would have ADDED a line
to a list that may only shrink, for a type that exists to make the presenter testable.

**How to apply:**

- A presenter member the view calls is signed in types the view already holds, or in none. Check the list before
  choosing a parameter type.
- When the presenter needs a narrow interface for its tests, give it two constructors: the one the view calls takes
  the concrete type already on the list and chains to the one taking the interface. `BurstEffects` and
  `BulletStrikes` do this with `EntityModelSet` and `IPosedEntities`.
- A frame-input record (`BurstFrame`) is cheap for the same reason: its fields are types the view already reads.
- Removing the LAST use of a type in the view is what removes its line. A type still in a signature the view calls
  stays, which is correct: the view still depends on it.
