# 51 — Source's physics is IVP, and the binary says which file every function came from

**`vphysics.dll` is not opaque.** It ships with the game, it is 1.4 MB at `bin/x64/`, and Valve left
enough in it to navigate: the assert strings carry full build paths, so a function's `__FILE__` names
its own source file. This is the map (B58, D142).

*Evidence class: read from the decompiled binary throughout, with the SDK headers for the interface
shapes. Ghidra project `tf2vphysics` under `D:\ghidra-proj`, which is outside every git tree.*

---

## It is IVP, and the paths prove it

```
C:\buildworker\rel_hl2_win64\build\src\ivp\ivp_intern\ivp_friction.cxx
C:\buildworker\rel_hl2_win64\build\src\ivp\ivp_collision\ivp_mindist_minimize.cxx
C:\buildworker\rel_hl2_win64\build\src\ivp\ivp_controller\ivp_actuator_spring.cxx
C:\buildworker\rel_hl2_win64\build\src\vphysics\physics_environment.cpp
```

**IVP is Ipion Virtual Physics**, the engine Havok acquired; Source wraps it. The `.cxx` extension
and the `ivp_` prefixes are its own, not Valve's, and the two halves are visible in the paths:
`src/vphysics/*.cpp` is Valve's wrapper, `src/ivp/**/*.cxx` is the solver.

**Sixteen IVP files are named**, being the ones with asserts:

| directory | files |
|---|---|
| `ivp_intern` | `ivp_ball`, `ivp_friction`, `ivp_friction_gaps`, `ivp_mindist_friction`, `ivp_object`, `ivp_physic_private` |
| `ivp_collision` | `ivp_compact_ledge_solver`, `ivp_mindist`, `ivp_mindist_event`, `ivp_mindist_minimize`, `ivp_mindist_recursive` |
| `ivp_controller` | `ivp_actuator`, `ivp_actuator_spring` |
| `ivp_compact_builder` | `ivp_object_polygon_tetra` |
| `ivp_physics` | `ivp_time_event.hxx` |
| `ivp_utility` | `ivu_vhash` |

Each of those strings is referenced by the functions that assert, so **a path is a set of function
addresses with a known source file** — the cheapest orientation available in a stripped binary.

---

## The trail from a game tick to the solver

`IPhysicsEnvironment::Simulate` is slot 34 of the interface, and its implementation is
`FUN_180015310` — identified not by a symbol but by the file and line its own mutex reports:

```c
local_a0 = "C:\\buildworker\\rel_hl2_win64\\build\\src\\vphysics\\physics_environment.cpp";
local_98 = 0x5da;                       // line 1498
```

It takes the environment mutex, clamps the timestep against two constants, and then makes exactly
one of two calls:

```c
if ((param_2 <= DAT_1800ea988) && (DAT_1800ec278 < (double)param_2)) {
    if (DAT_1800ec280 < (double)param_2) { param_2 = DAT_1800ea968; }
    ...
    if ((*(char *)((longlong)param_1 + 0xcf) == '\0') ||
        (param_2 != (float)*(double *)(*plVar3 + 0x108))) {
        FUN_180082540(*plVar3, (double)param_2);      // IVP_Environment::simulate_dtime
    } else {
        FUN_180082780(*plVar3, DAT_1800ec288);        // the fixed-step sibling
    }
}
```

`param_1[1]` is the `IVP_Environment`. **A timestep outside the clamp simulates nothing at all** —
the whole block is inside that `if`, so a frame too long or too short is skipped rather than
sub-stepped, which is a behaviour worth knowing before reproducing it.

`simulate_dtime` is one line:

```c
void FUN_180082540(longlong env, double dtime)
{
    FUN_180089f30(*(longlong *)(env + 8), env, *(double *)(env + 0x188) + dtime);
}
```

— the time manager at `env+8`, the current time at `env+0x188`, and a call that means
*simulate until this absolute time*. That is `IVP_Time_Manager`'s event loop, and it is where the
next slice of this reading starts.

---

## The interface vtable, and how it was pinned

`CPhysicsEnvironment`'s vtable is at **`1800ebbd8`**, and it is pinned rather than guessed: slot 34
of `IPhysicsEnvironment` is `Simulate` in the SDK header, and slot 34 of that table is
`FUN_180015310` — the function already identified by its own `physics_environment.cpp:1498` mutex.
One independent identification landing on the slot the header predicts is what makes the rest of the
table usable.

| slot | method | address |
|---|---|---|
| 3 | `SetGravity` | `1800150f0` |
| 7 | `CreatePolyObject` | `180012d40` |
| 15 | `CreateRagdollConstraint` | `180012e10` |
| 23 | `CreateConstraintGroup` | `180012a40` |
| 34 | `Simulate` | `180015310` |

**A vtable-only method is never auto-discovered**, so Ghidra had created no function at
`180012e10` at all and a decompile of it returned nothing. Forcing `disassemble` +
`createFunction` first is the difference between "there is no code there" and reading it.

The chain from there is short:

```c
// CPhysicsEnvironment::CreateRagdollConstraint
FUN_18000d510( m_pPhysEnv, pReference, pAttached, pGroup, ragdollParams );

// CPhysicsConstraint::CPhysicsConstraint — 0x40 bytes, TWO vtables (multiple inheritance),
// and each object gets a listener registered unless flag 0x200 is set
FUN_18000eac0( this, pPhysEnv, pGroup, ragdollParams );   // reads constraint_ragdollparams_t
```

---

## What the ragdoll joint actually does to its limits

`FUN_18000eac0` reads `constraint_ragdollparams_t` straight out of the caller's struct, and the
field offsets confirm the SDK layout: the two `matrix3x4_t` are at dword 6 and dword 0x12, which is
exactly `constraint_breakableparams_t` (24 bytes) followed by `constraintToReference` and
`constraintToAttached`.

**Three things happen to the axis limits, and every one of them is invisible if guessed.**

**One — the axes are REMAPPED.** The loop passes each Source axis index through

```c
int FUN_180002bb0(int axis)
{
    if (axis < 4) { return (int)(char)(&DAT_18011f014)[axis]; }
    return 0;
}
```

and the four bytes at `18011f014` are **`00 02 01 03`**. So Source axis 0 → IVP 0, **1 → 2, 2 → 1**,
3 → 3: **Y and Z are exchanged**, which is the Z-up/Y-up difference between Source and IVP. A joint
built without the swap has its twist and its swing on each other's axes — limits of the right size
about the wrong bones.

**Two — degrees become radians, and one axis is NEGATED.** The two scale constants are

| address | value | what |
|---|---|---|
| `1800eb764` | `0x3c8efa35` = **+0.0174532925** | π/180 |
| `1800eb770` | `0xbc8efa35` = **−0.0174532925** | −π/180 |

and the loop multiplies one axis by the negative one. **The negated axis also writes its min and max
in the opposite order**, which is not a separate quirk but the necessary consequence: negating a
range `[a, b]` gives `[−b, −a]`, so a transcription that negated without exchanging the pair would
produce an inverted, empty limit — a joint that either locks solid or flails, depending on which
side the solver clamps first.

**Three — "unlimited" is a magnitude test, not a flag.** The constraint is treated as breakable only
when

```c
(forceLimit  != 0 && forceLimit  < 1e12) ||
(torqueLimit != 0 && torqueLimit < 1e12) ||
(bodyMassScale[0] != 1.0 && bodyMassScale[0] != 0.0) ||
(bodyMassScale[1] != 1.0 && bodyMassScale[1] != 0.0)
```

with `1e12` sitting at `1800eb76c`. Zero means "no limit" and so does anything at or above 1e12 —
two different spellings of the same thing, and `constraint_breakableparams_t::Defaults()` uses the
zero one.

*Evidence class: read from the decompiled binary. The `1.0` in the mass-scale test is
**interpolated**: the constant `DAT_1800ea988` is shared with `Simulate`'s maximum timestep, and 1.0
is the only value that reads sensibly in both places — it has not been dumped.*

---

## The whole Source↔IVP convention, in one function

`FUN_180002cc0` converts a Source `matrix3x4_t` into IVP's form, and it is the single most important
function in the binary for a port: **every position, every rotation and every axis index crosses
here.**

```c
*param_2  = (double)*param_1;                                   // +m00
param_2[1] = (double)(float)((uint)param_1[2]  ^ 0x80000000);   // -m02
param_2[2] = (double)param_1[1];                                // +m01
param_2[4] = (double)(float)((uint)param_1[8]  ^ 0x80000000);   // -m20
param_2[5] = (double)param_1[10];                               // +m22
param_2[6] = (double)(float)((uint)param_1[9]  ^ 0x80000000);   // -m21
param_2[8] = (double)param_1[4];                                // +m10
param_2[9] = (double)(float)((uint)param_1[6]  ^ 0x80000000);   // -m12
param_2[10] = (double)param_1[5];                               // +m11
param_2[0xc] = (double)(0.0254f * param_1[3]);                  // + tx
param_2[0xd] = (double)(float)((uint)(0.0254f * param_1[0xb]) ^ 0x80000000);   // - tz
param_2[0xe] = (double)(0.0254f * param_1[7]);                  // + ty
```

Both constants are dumped, not inferred: `1800ea5e0` is `0x8000000080000000` — the **sign bit**, so
every XOR above is a negation — and `18011f000` is `0x3cd013a9` = **0.0254**, metres per inch, with
`39.37` sitting in the adjacent dword.

**The rule is `Source (x, y, z) → IVP (x, −z, y)`**, applied as a similarity transform `M' = P M Pᵀ`
with

```
P = [ 1  0  0 ]
    [ 0  0 -1 ]
    [ 0  1  0 ]
```

which is what produces that sign pattern: a minus appears exactly where one of the row or column
index passes through the negated axis and the other does not. **Source is Z-up and IVP is Y-up**, and
IVP works in metres where Source works in inches.

**It cross-checks against the two facts found separately**, which is what makes it trustworthy
rather than a plausible reading:

- The axis remap table is `00 02 01 03` — Source 1→2, 2→1. `P` sends Source Y to IVP +Z and Source Z
  to IVP −Y: the same permutation.
- Exactly one joint axis is negated in `InitRagdoll`, and it is the branch for **Source axis 2**. `P`
  gives Source Z a minus sign and Source Y none. The same axis.

**`FUN_18000ca70` then transposes it.** It writes the three converted rows into the *columns* of a
4×4 and puts the translation in the last ROW:

```c
param_2[0] = r0.x;  param_2[4] = r0.y;  param_2[8]  = r0.z;   // columns, not rows
param_2[12] = t.x;  param_2[13] = t.y;  param_2[14] = t.z;    // translation in the last row
param_2[3] = param_2[7] = param_2[0xb] = 0.0;  param_2[0xf] = 1.0;
```

So a transform crossing into IVP changes **three** things at once — handedness of the axis triple,
units, and matrix storage order. `docs/memory/two-matrix-conventions-on-purpose.md` records that this
project deliberately keeps two conventions and crosses between them once; IVP is a third, and it is
the one where getting any single part right while missing another produces a ragdoll that is subtly
and consistently wrong rather than obviously broken.

*Evidence class: read from the decompiled binary, with both constants dumped. The similarity-transform
reading is **arithmetic** — it was derived from the twelve assignments and then checked against the
axis table and the negated joint axis, which were found independently.*

**Two more functions do the same three lines on a bare vector**, which is what raises this from a
plausible reading of one function to the engine's convention:

```c
// CPhysicsEnvironment::SetGravity, 1800150f0
local_58 = (double)(0.0254f * g[0]);
local_50 = (double)(float)((uint)(0.0254f * g[2]) ^ 0x80000000);
local_48 = (double)(0.0254f * g[1]);
```

`CreatePolyObject` (`18001b340`) repeats it for the object's position and again for
`objectparams_t::massCenterOverride`. **`SetGravity` also settles which constant runs the other
way**: it converts a tolerance back for its own `DevMsg` with `DAT_18011f004`, the 39.37 dword —
not a reciprocal of 0.0254, and the two differ by two parts in a million.

**`CreatePolyObject` refuses a non-finite position rather than passing it on**, and the test is on
the raw bits:

```c
if (((uint)pos[0] & 0x7f800000) == 0x7f800000 || ... )   // exponent all ones: inf or NaN
{
    Warning("Invalid initial position on %s\n", params->pName);
    pos[0] = pos[1] = pos[2] = 0.0;                       // and the same for the angles
}
```

It warns, zeroes, and continues — so a corrupt placement costs one object its position rather than
taking the simulation down. Worth carrying: this project reads `.phy` files from a stranger (D32),
and the engine's own answer here is neither a crash nor silence.

---

## Two traps met on the way in, both worth writing down

**RTTI is nearly absent, so class names cannot be recovered from it.** Searching for MSVC type
descriptors (`.?AV…@@`) returns fifteen names and they are all utility classes — `CPolyhedron`,
`CUtlCharConversion`, `IConVar`. Nothing physics. Only classes something actually `dynamic_cast`s
keep their descriptor, so identification has to come from the build paths and from call shape
instead.

**A naive vtable scan finds the CFG guard table, not a vtable.** Scanning read-only data for runs of
pointers into executable memory turns up a 180-slot "vtable" at `1800eb928` whose first nineteen
entries are all `_guard_check_icall` and whose next 160 are all one thunk. Control Flow Guard's
import table has exactly the shape being searched for. A vtable finder needs to require *distinct*
targets, and even then MSVC vtables are best found from a constructor rather than by pattern.

---

## What this changes about the ragdoll work

**The solver is not ours to invent.** The first version of D142 concluded that because
`src/vphysics` is absent from the SDK, the integrator "is this project's own". The owner:
*"remember you have the decomp so no nothing is ours"*. He is right, and the correction generalises:
**the boundary of the published SDK is not the boundary of what can be read.**

The published half stays the preferred source where it holds the answer — `ragdoll_shared.cpp` gives
construction, the constraint data and the bone read-back, and none of that needs a decompiler. What
needed one is everything past `pPhysEnv->`, and that is now navigable rather than closed.

**Still to read, in the order the work needs it:** the time manager's event loop, `IVP_Core`'s
integration step, the ragdoll constraint's three-axis limit solve, and `ivp_mindist*` for collision
against the world.
