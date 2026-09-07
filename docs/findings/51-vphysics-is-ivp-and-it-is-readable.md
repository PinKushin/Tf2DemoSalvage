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

**Four — `useClockwiseRotations` negates every limit AND exchanges each pair.** The SDK declares it
with only *"HACKHACK: Did this wrong in version one. Fix in the future."* to explain it. What it
does, at `constraint_ragdollparams_t` offset `0xB2`:

```c
if (*(char *)((longlong)param_4 + 0xb2) != '\0') {
    _local_1e0  = CONCAT44((uint)local_1e0  ^ 0x80000000, uStack_1dc      ^ 0x80000000);
    local_1d8   = CONCAT44((uint)local_1d8  ^ 0x80000000, local_1d8._4_4_ ^ 0x80000000);
    uStack_1d0  = CONCAT44((uint)uStack_1d0 ^ 0x80000000, uStack_1d0._4_4_ ^ 0x80000000);
}
```

`CONCAT44(hi, lo)` reassembles each pair with the halves **swapped** as well as sign-flipped — so all
three axes get `[a, b] → [−b, −a]`, the same rule the single per-axis negation follows. A
transcription that negated without exchanging would invert every limit into an empty range.

**Five — the friction torque is scaled by the reference body's MASS.** Each axis writes a 24-byte
record, and the value comes from a virtual call on the constraint's reference object:

```c
fVar14 = (float)(**(code **)(**(longlong **)(param_1 + 0x10) + 0xe8))();
afStack_124[lVar13 * 6]  = (float)param_4[0x22] * 0.0174533f;      // angularVelocity, deg -> rad
(&uStack_128)[lVar13 * 6] = (uint)(fVar14 * param_4[0x23]) & 0x7fffffff;   // |mass * torque|
auStack_137[lVar13 * 0x18] = fVar14 * param_4[0x23] != 0.0;        // enabled at all?
```

`param_1 + 0x10` is the reference `IPhysicsObject` the constraint stored in its constructor, and
`0xE8` is slot 29 — **`GetMass`**, counted off the published `IPhysicsObject` and checked by its
return type being the float this arithmetic needs. `0x7fffffff` is dumped and is an absolute-value
mask.

So a joint's `xfriction` in the `.phy` is not a torque in any absolute unit: it is a coefficient the
engine multiplies by the body's mass, takes the magnitude of, and switches the axis's resistance off
entirely when the product is zero. **Every ragdoll constraint in `soldier.phy` has all three
frictions at `0.000000`**, so on TF2's own players that switch is off — which is worth knowing
before building a solver around a term that is always disabled.

`angularVelocity` (`axes[i].angularVelocity`) is converted degrees to radians like the limits.

*Evidence class: read from the decompiled binary, with `0x7fffffff` dumped and `GetMass` counted off
the published header. The `1.0` in the mass-scale test is **interpolated**: the constant
`DAT_1800ea988` is shared with `Simulate`'s maximum timestep, and 1.0 is the only value that reads
sensibly in both places — it has not been dumped.*

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

---

## The construction half, measured against real data

The transcribed half — `RagdollCreateObjects` and its two helpers — is exercised on every shipped
player model by the `ragdoll` probe, and it is the check no unit test can make: the conformance
suite is synthetic, and what it cannot answer is whether a REAL `.phy` maps onto a REAL skeleton,
since solids are matched to bones **by name** and one unmatched name refuses the whole body.

**All eighteen player models build** — the nine classes and their HWM variants — with no refusals
and exactly one root each. **The body count is not uniform**, which is the fact worth having:

| model | bodies | joints |
|---|---|---|
| `pyro`, `demo` | 15 | 14 |
| `heavy` | 16 | 15 |
| `scout`, `soldier`, `sniper`, `spy` | 17 | 16 |
| `engineer` | 18 | 17 |
| `medic` | **24** | **23** |

Generalising "seventeen bodies" from the soldier would have been wrong for five of the nine.

The soldier's topology is anatomically exactly what it should be — pelvis → hips → knees → feet,
pelvis → spine → upper arms → lower arms → hands, spine → neck → head — and the recorded offsets are
real limb lengths in inches: hip to knee 16.92, knee to foot 17.96, upper arm to lower arm 14.48,
lower arm to hand 10.64. Its masses total about 101.

**Linear damping is zero on every element and rotational damping is not**, ranging 4 to 16 per
joint. A solver that read one and not the other would settle at the wrong rate, which no still
picture shows.

*Evidence class: measured, through the production reader.*

---

---

## Where the joint object itself lives, and how IVP lays its classes out

`InitRagdoll`'s tail allocates the IVP constraint and stores it on the wrapper. There are **two
constructors and TF2's corpses take the second**:

```c
lVar13 = g_pMemAlloc->Alloc(0x177);
if (bVar3) { plVar10 = FUN_1800368c0(...); plVar10 = FUN_18000cf90(...); }   // breakable
else       { plVar10 = FUN_1800369e0(...); }                                  // not
*(longlong **)(param_1 + 0x20) = plVar10;
*(longlong *)(*(longlong *)(param_1 + 0x20) + 0x28) = param_1;    // back-pointer to the wrapper
```

`bVar3` is the breakability test from earlier in the same function, and every ragdoll constraint in
`soldier.phy` fails it — zero force and torque limits, unit mass scales — so `FUN_1800369e0` is the
one that matters. It is a **0x2E0-byte object whose vtable is at `1800ee9d0`**, and it initialises
six blocks to identity (`0x3f800000`) before handing the converted limits to `FUN_180037890`.

**IVP lays a constraint class out as an eight-entry vtable immediately followed by its NAME**, which
is what makes the classes enumerable rather than guessed at. Dumping past the ragdoll's vtable:

```
SLOT 8   6c6c6f64676172        "ragdoll"
SLOT 9   ...40c90fdb           6.2831855 — two pi
SLOT 10  180031aa0             the next class's vtable starts here
SLOT 18  65676e6968            "hinge"
```

So the eight slots are the controller interface, and the string after them names the class. The
ragdoll's:

| slot | address | bytes |
|---|---|---|
| 0 | `180031aa0` | shared with other classes — a common base |
| 1 | `180036b10` | 105 |
| 2 | `180037870` | small |
| 3 | **`180038620`** | **1772 — the per-step solve** |
| 4 | `180038d10` | 637 |
| 5–7 | `180037860`, `1800346e0`, `180037880` | small |

**Slot 3 is where a corpse's joints are actually resolved**, and it is 1772 bytes of heavily
vectorised float arithmetic — the constraint's Jacobian built from both bodies' transforms, with the
per-axis records `InitRagdoll` wrote. That is the next thing to transcribe, and it is a careful job
rather than a quick one: decompiled SIMD hides which lane is which, and a transcription that is
wrong in one lane produces a corpse that settles smoothly into the wrong shape.

*Evidence class: read from the decompiled binary; the class names are ASCII in the data section.*

---

**Still to read, in the order the work needs it:** the ragdoll constraint's per-step solve
(`180038620`), `IVP_Core`'s integration step, the time manager's event loop, and `ivp_mindist*` for
collision against the world.

---

## Reading the solve: the constant pool first, because the lanes are unreadable without it

**The per-step solve (`180038620`) is unreadable as decompiled, and the way in is not the code.**
Nearly every line of it is a mask — `(uint)x & _DAT_1800ff0f0`, `~uVar6 & (uint)a | (uint)b & uVar6`
— and a mask whose value you do not know is a line you cannot read. Dumping the constant pool it
draws from turns the whole function over at once:

| address | value | what it is |
|---|---|---|
| `1800ff070` | `{0, 0, 0, 0}` | zero; `DAT_1800ff070 - x` in the code is **negate** |
| `1800ff080` | `{1, 1, 1, 1}` | one |
| `1800ff090` | `{3, 3, 3, 3}` | the 3 of the `rsqrtps` Newton step |
| `1800ff0b0` | `{0.5, …}` | its half |
| `1800ff0c0` | `{1.1920929e-7, …}` | `FLT_EPSILON` — the guard on a squared length |
| `1800ff0d0` | `{0x7fffffff, …}` | **absolute value** mask |
| `1800ff0e0` | `{0x80000000, …}` | the sign bit; `~mask & x` is also `fabs` |
| `1800ff0f0` | `{~0, ~0, ~0, 0}` | **keep xyz, clear w** |
| `1800ff100` / `110` / `120` / `130` | one lane each | select lane 0 / 1 / 2 / 3 |

**One of those corrected a reading that was already written down.** `1800ff0f0` was read as the
absolute-value mask on first pass — `(uint)x & _DAT_1800ff0f0` looks exactly like one — and it is
`{~0, ~0, ~0, 0}`: a W-clear, not an ABS. Every `fabs` inferred from it was wrong, and the two masks
sit thirty-two bytes apart. **This is precisely the failure the last section warned about**, caught
by dumping rather than by reading more carefully.

So `(float)(*(uint *)(param_1 + 0x30) & _DAT_1800ff0f0)` is not `fabs(axis.x)`; it is the axis
vector with its fourth lane zeroed, three of these lines together forming a 3-vector out of a
16-byte load. And `param_3[0x40..0x43]` is not four related values — it is four scalars gathered one
lane at a time through `1800ff100`–`130`, which is a horizontal shuffle the decompiler cannot name.

## IVP keeps its transforms in DOUBLES

`FUN_180037620`, called on each body at the top of the solve, is the body-to-world matrix fetch:

```c
lVar15 = *(longlong *)(param_1 + 0xe8);          // the body's core
dVar1 = *(double *)(lVar15 + 0x90);  ...          // a 4x4 of DOUBLES at +0x90
*(float *)*param_2        = (float)dVar1;         // row 0 = source COLUMN 0
*(float *)(*param_2 + 4)  = (float)dVar3;         // (+0xb0)
*(float *)(*param_2 + 8)  = (float)dVar4;         // (+0xd0)
```

Three facts, and each is a thing a transcription would otherwise guess:

- **The core's matrix is `double`, at core+0x90, rows 32 bytes apart** (0x90, 0xb0, 0xd0, 0xf0).
  Source's own `matrix3x4_t` is float; IVP's `IVP_U_Matrix` is not, and the narrowing happens here,
  at the boundary into the solver.
- **It transposes on the way out** — destination row 0 is source column 0 — which is the transpose
  `docs/memory/ivp-is-a-third-convention.md` already records, now seen at the exact line that does
  it rather than inferred from the convention.
- **A guarded shift by a local anchor.** `if ((*(uint *)(param_1 + 0x78) & 0x800) == 0)` the
  translation is offset by the vector at body+0x60 rotated into world. Bit 0x800 means "no shift" —
  a flag whose absence changes the matrix, so a reader who skipped the `if` would place every joint
  by the body's origin instead of by its anchor.

## `FUN_180036b80` is an angle, computed the SIMD way

The solve builds two broadcast scalars — a dot product and a triple product, the cosine-like and
sine-like halves of a joint's current angle — and hands both to `FUN_180036b80`, which does:

```c
a = fabs(param_1[i]);  b = fabs(param_2[i]);          // via the 0x80000000 mask
rcpps( max(a, b) );
rcpps( a + b );
```

`min/max` of the two magnitudes followed by a reciprocal is the opening of the standard fast
`atan2`, so this is the joint's angle rather than anything about forces.

**The decompilation dropped both returns and one guess off it was wrong, so the disassembly
settles it.** `RCPPS` alone is the ~12-bit approximate reciprocal, and the first reading here was
that Valve takes it raw. It does not — each `RCPPS` is followed immediately by a Newton–Raphson
step, `r' = 2r − r²x`, spelled out in four instructions:

```asm
180036bdb  RCPPS XMM5,XMM1        ; r ≈ 1/max
180036be2  MOVAPS XMM0,XMM5
180036be5  MULPS XMM0,XMM5        ; r²
180036be8  ADDPS XMM5,XMM5        ; 2r
180036beb  MULPS XMM0,XMM1        ; r²x
180036bf4  SUBPS XMM5,XMM0        ; 2r − r²x
180036bde  CMPLTPS XMM4,XMM1      ; and FLT_EPSILON < max …
180036c13  ANDPS XMM5,XMM4        ; … or the answer is zeroed
```

So a plain `1.0f / x` IS the faithful transcription of the reciprocal, and the epsilon mask beside
it is the divide-by-zero guard. **The wrong version is kept here because it is the exact shape this
document warns about**: a claim about precision, inferred from an instruction name, that would have
been transcribed as a deliberate approximation nobody could later justify.

**The whole of `FUN_180036b80` is a four-lane `atan2`**, and these are its coefficients — read out
of `180124f10` onward, each broadcast across all four lanes:

| address | value | role |
|---|---|---|
| `180124f10` | `0.16591105` | z⁴ coefficient |
| `180124f20` | `-0.33080792` | z² coefficient |
| `180124f30` | `0.9999531` | z coefficient |
| `180124f40` | `3.1415927` | π, the `x < 0` fix-up |
| `180124f50` | `1.5707964` | π/2, for the swapped octant |
| `180124f60` | `0.7853982` | π/4, for the other branch |

```
atan(z) ≈ z * (0.9999531 + z² * (-0.33080792 + z² * 0.16591105))
```

with `z` selected per lane as `min(|x|,|y|)/max(|x|,|y|)` or `(|x|−|y|)/(|x|+|y|)`, whichever is
smaller in magnitude, then `π/2 −` it for the swapped case, the sign of `y` XOR'd back in, and `π`
added where `x < 0`. `CMPLTPS XMM10, [1800ff070]` is that last test — a comparison against the zero
vector, which the constant table above is what makes readable.

**This is a joint angle, computed to about seven digits**, and it is the number every limit in
`InitRagdoll`'s per-axis records is compared against.

*Evidence class: read from the decompiled binary, its disassembly and its data section; the two
constant tables are verbatim dumps of `1800ff070`–`1800ff13c` and `180124f10`–`180124f60`.*

## The joint is solved as ONE axis plus TWO, not as three

`FUN_180036e10`, called from the solve once the Jacobian rows are written, is short and its shape is
the finding:

```c
FUN_180036f80( ..., param_3 + 0x150, ..., (byte *)(param_1 + 0xb0), ... );   // axis 1
FUN_1800372c0( ..., param_3 + 0x1d0, ..., (byte *)(param_1 + 0xe8), ... );   // axis 2
FUN_1800372c0( ..., param_3 + 0x250, ..., (byte *)(param_1 + 0xcc), ... );   // axis 3
```

**Three per-axis records at constraint+0xb0, +0xcc and +0xe8** — 0x1c apart, so 28 bytes each,
which is the stride `InitRagdoll` writes. **The first axis goes through a different routine from
the other two**, and the second and third are solved in the order 0xe8 then 0xcc rather than in
address order.

**One axis is solved by a different routine from the other two**, which is the shape of a ragdoll
joint — a twist and two swings — but which of the three is the twist is NOT established by this and
must not be assumed. A transcription that treated the three symmetrically would produce a corpse
whose limbs settle plausibly and whose shoulders rotate about the wrong one: the expensive kind of
failure, because it looks like physics rather than like a bug.

Each is given a scalar of its own from `param_3 + 0x100`, `+0x104`, `+0x108`, broadcast to four
lanes before the call.

**What is NOT established:** what the two routines do differently, and which of the three axes is
the twist. The records' 28-byte layout is also unread — `InitRagdoll` writes it and this reads it,
so the two together will name the fields, and that is the next thing to do.

*Evidence class: read from the decompiled binary.*

## It is sequential impulses, and the accumulator lives in the constraint

`FUN_180036f80` — the single-axis routine — reads its 28-byte record as bytes and floats, and one
of them it WRITES BACK:

```c
fVar16 = *(float *)(param_7 + 0x10);
fVar2  = *(float *)(param_7 + 0x14);
fVar24 = *(float *)(param_7 + 0x18);
...
*(uint *)(param_7 + 0x18) = ...;      // stored again at the end of the step
```

**A value read at the top of a solve, used, and written back at the bottom is an accumulated
impulse**, which makes this a sequential-impulse solver rather than a one-shot Jacobian solve. That
matters for a transcription more than any single formula: the constraint carries state ACROSS steps,
so a corpse's joints converge over several frames and a reimplementation that recomputed from
scratch each step would be soft where the engine is stiff.

The record's shape falls out of the same function — and 0x18 + 4 = **0x1c**, the stride
`FUN_180036e10` uses between the three axes, so the layout is complete:

| offset | use |
|---|---|
| +0x00 | flags; **bit 0 selects a 16-byte offset** into the scratch block (`(*param_7 & 1) * 0x10`) |
| +0x01 | a second flag, gating the whole limit branch |
| +0x04, +0x08, +0x0c | floats, used against the caller's per-axis scalar |
| +0x10, +0x14 | floats, used on the limit path |
| +0x18 | **the accumulated impulse — read and stored** |

**And the two bodies' accumulators are at body+0x130 and body+0x140**, the same offsets the outer
solve wrote to under its `+0x157` branch. Both are written back through the `{~0,~0,~0,0}` mask, so
the fourth lane is deliberately discarded — these are 3-vectors in 16-byte slots, a linear pair and
an angular pair.

**Both routines are scalar, and an argument to the contrary was wrong — kept because it is the
exact trap this document is about.** Three of every four lanes in `FUN_180036f80` are multiplied by
a literal `0.0`:

```c
fVar28 = fVar24 * param_8[1] * param_2[0x15] * 0.0;
fVar30 = fVar24 * param_8[2] * param_2[0x16] * 0.0;
```

— the compiler vectorised a scalar and left the dead lanes in. That was read here as independent
confirmation that this routine solves ONE axis while `FUN_1800372c0`, called twice, solves the other
two together. **It is not: `FUN_1800372c0` has the same dead lanes.** Both are one-axis-per-call, and
the structure is one call plus two calls, not one axis plus a pair.

The lesson is the one the constant table already taught, in a second form: **a dead lane says the
compiler vectorised a scalar, and nothing whatever about how many axes the algorithm has.** Reading
it as evidence produced a confident architectural claim from a code-generation artefact, and the
only thing that killed it was decompiling the sibling and looking for the same pattern — the control
this project's own rules ask for before believing any absence or any difference.

**Part of what differs is now read.** `FUN_1800372c0` takes nine arguments to the other's eight — a
matrix and an extra vector — and multiplies its error term by a constant the first never touches:

```c
auVar32._0_4_ = (*param_9 * _DAT_1800ee9b0 * fVar23 * fVar26 - fVar31 * fVar30) * param_2[0x14];
```

`1800ee9b0` is **`{0.8, 0.8, 0.8, 0.8}`**, with `{0.1, …}` immediately after it at `1800ee9c0`. A
factor under one applied to a positional error is a relaxation term — the fraction of the error a
step is allowed to correct — so **two of the three axes are relaxed at 0.8 and the third is not**.
That is a number a reimplementation would never guess and would never miss either, because at 1.0 a
joint overshoots and jitters rather than failing outright.

Both routines share `FUN_180037bd0` and the same integer state check.

**One decompiler artefact worth naming**, because it reads as nonsense otherwise:
`if (param_2[0x1c] == 1.4013e-45)` is not a float comparison. `1.4013e-45` is the smallest denormal,
bit pattern `0x00000001`, and `2.8026e-45` is `0x00000002` — this is an INTEGER state field the
decompiler typed as float. The constraint has a small state machine at `+0x70` of its scratch block,
and reading those constants as floats would put a wildly implausible threshold into the
transcription.

*Evidence class: read from the decompiled binary.*

## `FUN_180037bd0` is the cached Jacobian row, and the state field is a memo

The helper both axis routines call opens by writing the state field the callers test:

```c
param_1[0x1c] = 1.4013e-45;     // = 0x00000001
```

So `if (state == 1)` in the callers is *"this row has already been built this step"*, and the
`== 2` inside it is a second stage. **The state machine is a memo, not a mode** — three axes share
one scratch block, and only the first of them pays for the transform.

What it builds is the axis expressed in each body's frame:

```c
fVar29 = a.y * core3[0xb0] + a.z * core3[0xd0] + a.x * core3[0x90];   // body A
...
fVar24 = 0 - a.x;  fVar26 = 0 - a.y;  fVar27 = 0 - a.z;               // negated for body B
```

Two facts fall out, and both are structural rather than numeric:

- **`param_3` and `param_4` are the CORES, not the objects** — the doubles are at +0x90 again, the
  same matrix `FUN_180037620` reads through `body+0xe8`. Three independent sightings of that offset
  now.
- **The second body gets the negated axis**, which is Newton's third law written as a sign flip
  rather than as a subtraction later. A transcription that applied the same row to both bodies would
  make a joint push both halves the same way — a corpse that drifts.

*Evidence class: read from the decompiled binary.*

## The effective mass, and the core's data model

The second half of `FUN_180037bd0` is the constraint's denominator — `Jᵀ M⁻¹ J` — and reading it
names four fields of `IVP_Core` at once:

```c
if ((*param_3 & 0x12) == 0) {                        // body A participates
    fVar27 = *(float *)(param_3 + 0x40);             // inverse inertia, x
    fVar28 = *(float *)(param_3 + 0x44);             //                  y
    fVar17 = *(float *)(param_3 + 0x48);             //                  z
    param_1[8]  = fVar29 * fVar27;                   // M⁻¹J, cached for the impulse
    param_1[9]  = fVar30 * fVar28;
    param_1[10] = fVar31 * fVar17;

    fVar24 = fVar30 * fVar28 * fVar30                // Σ aᵢ² · invInertiaᵢ
           + fVar29 * fVar27 * fVar29
           + fVar31 * fVar17 * fVar31;

    fVar32 = fVar30 * *(float *)(param_3 + 0x134)    // a · ω, the current rate
           + fVar29 * *(float *)(param_3 + 0x130)
           + fVar31 * *(float *)(param_3 + 0x138);
}
```

| offset in `IVP_Core` | what it is | how it was identified |
|---|---|---|
| `+0x00` | flags; **bits `0x12` mean "does not participate"** | the guard above, and the same test in the outer solve |
| `+0x40` | **inverse inertia**, three floats and a fourth lane | multiplied into the axis and summed as squares — that is only ever `M⁻¹` |
| `+0x90` | the 4×4 **transform, in doubles** | read by `FUN_180037620` and again here |
| `+0x130` | **angular velocity** | dotted with the axis to get the rate, and written back by the solve |
| `+0x140` | the second accumulator, linear | written beside `+0x130` in the outer solve's `+0x157` branch |

**`M⁻¹J` is cached beside the row rather than recomputed**, which is what makes the memo at
`param_1[0x1c]` worth having: three axes share one scratch block, and the first of them pays for the
transform, the inverse-inertia product and the effective mass together.

**The inverse inertia is a DIAGONAL**, not a matrix. IVP keeps the body in its own principal frame,
which is why the axis has to be rotated into that frame first — the two matrix multiplies at the top
of this function — rather than the tensor being rotated into the world.

**What is NOT established:** the constant `+0x50` region the outer solve reads for its second body,
and whether `+0x40`'s fourth lane is the inverse MASS or padding. The arithmetic here only ever uses
three of the four, so the fourth is unconstrained by anything read so far.

*Evidence class: read from the decompiled binary.*

## The impulse, and the thing that is not a clamp

`FUN_180036f80`'s middle section reads as a clamp against two magic constants, and dumping them
turns it into something else entirely:

| address | value |
|---|---|
| `1800ee990` | **3.1415927** — π |
| `1800ee9a0` | **6.2831855** — 2π |

It is **angle unwrapping**, not clamping:

```c
d = accumulated - current;                       // param_5 is the joint's running angle
correction = (d >  π) ?  2π
           : (d < -π) ? -2π
           :             0;
d          -= correction;
*param_5   += correction;                        // the running angle keeps the wrap
```

**So the solver tracks a CONTINUOUS joint angle rather than one that jumps at ±π.** Without it a
joint passing the wrap point receives an error of nearly 2π in one step and snaps — the corpse's
elbow spinning once round for no reason. The whole thing is written branchlessly with compare masks,
which is why it reads as arithmetic; `π` and `2π` are the only things in it that say what it is.

**And the impulse itself is one line:**

```c
fVar29 = *param_8 * _DAT_1800ee9b0;                          // gain × 0.8
...
fVar29 = (fVar29 * fVar25 * fVar23 - fVar33 * fVar34) * param_2[0x14];
//        └ bias × error × k ┘   └ damping × rate ┘     └ 1 / effective mass ┘
```

with `fVar34` the rate — computed at the top of the function as
`ωA · J_A + ωB · J_B`, straight off `core+0x130` for both bodies — and `param_2[0x14]` the inverse
effective mass the Jacobian helper cached. That is a sequential impulse with a Baumgarte bias:
**correct a fraction of the position error, oppose the current rate, divide by the effective mass,
accumulate.**

**The state field is a three-way memo, and one of its states is "skip".** `param_2[0x1c]` reads 1
when the row is already built for this step — in which case only the rate is recomputed, from the
velocities that other axes have since changed — and **2 means return immediately**. So an axis can
retire for the remainder of a step, which a transcription that treated the field as a bool would
lose.

**`1800ee970` and `1800ee980` are `{0,0,0,0}` and `{~0,~0,~0,~0}`**, selected by `flags & 1` and
stored into `param_2[0x18..0x1b]`: a mask pair meaning "this axis is limited" or "free", kept for
branchless selection later rather than tested.

**What is NOT established:** what `param_8`'s four gains are and where they come from — they arrive
from `FUN_180036e10`'s caller as a scratch value derived from the timestep — and whether the 0.8 is
a fixed bias or a per-step factor. Both are needed before this becomes code, and neither is
guessable from this function alone.

*Evidence class: read from the decompiled binary; the constant table is a verbatim dump of
`1800ee970`–`1800ee9ac`.*

## The two routines differ in the GAINS they are handed, not only in the relaxation

`FUN_180036e10` prepares the gains before it dispatches the three axes, and the four lines that do
it answer half of what the last section left open:

```c
local_48  = *(float *)(param_3 + 0x2d0);      // four floats at +0x2d0
fStack_44 = *(float *)(param_3 + 0x2d4);
fStack_40 = *(float *)(param_3 + 0x2d8);
fStack_3c = *(float *)(param_3 + 0x2dc);

local_38  = fStack_3c * local_48;             // the vector scaled by its OWN fourth lane
fStack_34 = fStack_3c * fStack_44;
fStack_30 = fStack_3c * fStack_40;
fStack_2c = fStack_3c * fStack_3c;
```

and then hands **`&local_38` — the scaled copy — to `FUN_180036f80`**, and **`&local_48` — the
unscaled one — to both calls of `FUN_1800372c0`**.

So the single-axis routine works in gains multiplied by the block's fourth lane and the pair does
not, which is a second difference between them on top of the 0.8 relaxation only the pair applies.
**Two independent differences means the axes are not three of a kind with a parameter**; they are
genuinely different solves, and a transcription that shared one routine with flags would have to
reproduce both differences to be right.

The fourth lane multiplying the other three is the shape of a timestep — a rate gain converted to a
per-step one — but that is an inference from the arithmetic and NOT established: nothing read so far
shows where `+0x2d0` is filled.

**Also here:** each axis is handed its own scalar from `+0x100`, `+0x104`, `+0x108`, broadcast to
four lanes before the call, in the order 1, 3, 2 — the same crossed order the axis records are read
in.

**What is NOT established, restated because it is now the whole blocker:** who fills `+0x2d0` and
`+0x100`, and whether the 0.8 is fixed. Those live in whatever prepares the scratch block each step,
which is above the constraint entirely — the time manager's event loop, still unread because
`ivp_core.cxx` carries no asserts and so names no file in the binary.

*Evidence class: read from the decompiled binary.*

## The time model: a double clock, float events, and a periodic rebase

**Both simulate paths funnel into one dispatcher.** `FUN_180082540` (variable) and `FUN_180082780`
(fixed step) each call `FUN_180089f30(timeManager, env, targetTime)`, differing only in how they
compute the target:

```c
// variable
FUN_180089f30(*(env + 8), env, *(double *)(env + 0x188) + dtime);

// fixed step
FUN_180089f30(*(env + 8), env,
              *(double *)(env + 0x198) + (double)((float)*(double *)(env + 0x108) * count));
```

so **`env+0x108` is the PSI step** — the same field `CPhysicsEnvironment::Simulate` compares the
requested delta against before choosing between the two — and the two paths advance from **different
bases**, `+0x188` and `+0x198`.

**`FUN_18008a020` says why there are two, and it is the most consequential thing in the time model:**

```c
dVar2       = *(double *)(env + 0x188);          // absolute time, a DOUBLE
env[0x198]  = dVar2;                             // rebase point
env[0x190]  = (float)env[0x108] + dVar2;         // next PSI

lVar4 = *(longlong *)(tm + 0x10);                // the event list
uVar7 = *(uint *)(lVar4 + 0x18);
while ((int)uVar7 != 0xffff) {                   // 0xffff terminates
    lVar1  = *(longlong *)(lVar4 + 8) + uVar7 * 0x18;    // 0x18 bytes per event
    uVar7  = *(ushort *)(... + 4 + uVar7 * 0x18);        // next index, 16-bit
    *(float *)(lVar1 + 8) -= (float)dVar2;               // event time is a FLOAT
}
*(float *)(*(longlong *)(tm + 0x10) + 0x10) -= (float)dVar2;
*(undefined8 *)(tm + 0x28) = *(undefined8 *)(env + 0x198);
*(undefined8 *)(tm + 0x20) = 0;
```

**The absolute clock is a double and every scheduled event's time is a float measured from a base**,
and this walks the whole list subtracting the current time to move that base forward. It is the
standard defence against a float clock losing resolution as a session runs — and it is a behaviour
with consequences rather than an implementation detail: an event's time is only ever precise
relative to the last rebase, so a transcription that stored absolute float times would drift apart
from the engine the longer a demo ran, in a way that looks like jitter rather than like a bug.

| field | what it is |
|---|---|
| `env+0x108` | the PSI step, a double read as float |
| `env+0x188` | absolute current time, double |
| `env+0x190` | the next PSI's time |
| `env+0x198` | the rebase base the fixed-step path advances from |
| `tm+0x10` | the event list; entries 0x18 bytes, float time at +8, 16-bit next index at +4, `0xffff` terminates |
| `tm+0x20`, `tm+0x28` | the list's own counter and base |

**What is still NOT found: the event loop itself.** `FUN_180089f30` reaches it through
`(**(code **)(**(longlong **)(param_1 + 8) + 8))(…)` — vtable slot 1 of the object at
`timeManager+8` — and that object's type cannot be resolved statically from here. Two routes remain
and neither is a guess: find whoever WRITES `timeManager+8` (the environment's constructor), or find
the vtable by its shape. Searching the neighbouring address range was tried and produced collision
code, not the loop; `ivp_time_event.hxx`'s only reference is a shared assert thunk with no recorded
callers, so the string route is exhausted for this one.

*Evidence class: read from the decompiled binary.*

## The decisive one: a TF2 corpse is SIMULATED BY THE CLIENT, so a demo carries no pose for it

**This settles what B316 actually requires, and it is not what the entry assumed.**

`C_TFRagdoll::CreateTFRagdoll` calls the ordinary client ragdoll path —
`InitAsClientRagdoll( boneDelta0, boneDelta1, currentBones, boneDt, m_bFixedConstraints )`
(`c_tf_player.cpp:920`) — which is `C_BaseAnimating`'s, the same one `C_ClientRagdoll` uses. From
there: `CRagdoll::Init` → `RagdollCreate` → bodies and constraints in the **client's own**
`IPhysicsEnvironment`.

**And the send table proves the wire carries no pose.** `DT_TFRagdoll` sends

```cpp
RecvPropVector( RECVINFO(m_vecRagdollOrigin) ),
RecvPropEHandle( RECVINFO( m_hPlayer ) ),
RecvPropVector( RECVINFO(m_vecForce) ),
RecvPropVector( RECVINFO(m_vecRagdollVelocity) ),
RecvPropInt( RECVINFO( m_nForceBone ) ),
RecvPropBool( RECVINFO( m_bGib ) ),      // and the rest of the appearance flags
```

— `c_tf_player.cpp:519`. **Initial conditions and nothing else.** Compare the OTHER ragdoll family,
`C_ServerRagdoll` / `DT_Ragdoll`, which networks `m_ragPos` and `m_ragAngles` as per-element arrays
(`ragdoll.cpp:423`) and whose `GetElement` returns `NULL` unconditionally because it owns no physics
objects at all (`ragdoll.cpp:646`). That is the read-the-positions family. **TF2's death ragdoll is
not in it.**

**So there is no shortcut, and the physics work is not optional.** A demo hands us a position, a
force, a velocity and a force bone; every pose after the first frame is something the client
computed. A viewer that wants a corpse to lie correctly has to run the simulation the client ran —
which is why B316's current behaviour (resting the corpse in `ACT_DIERAGDOLL`) is a stopgap and
cannot be finished into correctness.

**Two numbers the client's environment is set up with, and we have both:**

```cpp
physenv->SetGravity( Vector(0, 0, -GetCurrentGravity() ) );
// 15 ms per tick
// NOTE: Always run client physics at this rate - helps keep ragdolls stable
physenv->SetSimulationTimestep( IsXbox() ? DEFAULT_XBOX_CLIENT_VPHYSICS_TICK
                                         : gpGlobals->interval_per_tick );
```

`physics.cpp:177-180`. The step is **the demo's own tick interval**, which this project already
decodes rather than assuming, and the gravity is `sv_gravity`. Valve's comment is worth keeping:
running client physics at a fixed rate is deliberate, *"helps keep ragdolls stable"* — so the step
is not the frame time.

*Evidence class: read-from-source, and the two load-bearing claims independently confirmed — the
call site at `c_tf_player.cpp:920` and the send table at `:519`, which agree.*

## Found: the event loop, at `18008a110`

**Pinned by two independent routes that agree, plus Ghidra's own xref table** — not by shape.

- **The vtable.** The object at `timeManager+8` has its vtable at `1800fd498`: three function
  pointers followed immediately by ASCII, exactly the layout the constraint family uses. Slots 3-4
  read `6f5f636974617473` / `7463656a62` — **`static_object`**. Slot 1 is `18008a110`, and
  `WhoPoints` confirms a data reference from `1800fd4a0`, which is that slot's own address.
- **The constructor chain.** `FUN_180080d90` (environment init) allocates 0x30 bytes, calls
  `FUN_180089dc0`, and stores the result at `env+8` — the time manager. `FUN_180089dc0` then
  allocates 0x10, writes `&PTR_FUN_1800fd498` as its vtable, and stores it at `this+8` — which is
  `timeManager+8`. The destructors mirror it exactly.

The loop itself, verbatim:

```c
lVar2  = *(longlong *)(param_2 + 0x10);            // the event queue
fVar3  = *(float *)(lVar2 + 0x10);                 // earliest queued time
if (fVar3 < (float)(param_4 - *(double *)(param_2 + 0x28))) {   // target, in base-relative float
  do {
    plVar1 = *(longlong **)(*(longlong *)(lVar2 + 8) + 0x10 +
                            (ulonglong)*(uint *)(lVar2 + 0x18) * 0x18);   // pop the earliest
    FUN_1800ab1b0(lVar2, *(uint *)(plVar1 + 1));   // unlink it
    *(undefined4 *)(plVar1 + 1) = 0xffff;          // mark "not queued"
    *(double *)(param_2 + 0x20) = (double)fVar3;   // the manager's own clock
    FUN_180082460(param_3, (double)fVar3 + *(double *)(param_2 + 0x28));  // env clock := absolute
    (**(code **)(*plVar1 + 8))(plVar1, param_3);   // FIRE: the event's OWN vtable slot 1
    if (*(int *)(*(longlong *)(param_2 + 8) + 8) == 1) break;             // stop flag
    lVar2 = *(longlong *)(param_2 + 0x10);
    fVar3 = *(float *)(lVar2 + 0x10);
  } while (fVar3 < (float)(param_4 - *(double *)(param_2 + 0x28)));
}
FUN_180082460(param_3, param_4);                   // snap the clock to exactly the target
```

**So the whole of simulation is a priority queue drained by time**, and each event fires through its
own vtable slot 1. The physics step is not a special case in this loop — it is an event like any
other, which is what makes IVP event-driven rather than fixed-stepped internally, even though the
environment above it is handed a fixed step.

**Three things a transcription would get wrong without this:**

- **The clock is set BEFORE the event fires**, to that event's time, and events therefore observe a
  clock that walks forward inside one call rather than jumping at the end.
- **The stop flag is checked AFTER each fire** (`*(tm+8)` field `+8` == 1), so an event can halt the
  remainder of a step. A loop that only tested the queue would run events the engine skipped.
- **The clock is snapped to the target afterwards regardless**, so time always ends exactly where
  the caller asked even if the last event fired earlier.

**It also confirms the time model read independently earlier**, which is now three agreeing
sightings: `tm+0x28` is the base, event times are floats relative to it, `tm+0x10` is the queue with
`0x18`-byte entries, and `0xffff` means "not queued".

**What is NOT established:** which event type performs the physics step — that is one of the event
objects' own slot 1, and finding it means identifying the event the environment schedules per PSI.
That is now the single remaining link between this loop and the constraint solve already read.

*Evidence class: read from the decompiled binary; vtable slot, xref and both constructor/destructor
chains independently confirmed.*

## From reading to code: what is now transcribed

**The physics work has crossed from reading into implementation**, and only the parts that were read
verbatim have crossed. What exists:

| type | transcribes | citation |
|---|---|---|
| `RagdollJointLimits` | the axis remap, degrees→radians with axis 2 negated and its pair exchanged, the `useClockwiseRotations` flag, and "unlimited" as a magnitude test | `CPhysicsConstraint`'s constructor, `18000eac0` |
| `PhysicsTimeManager` | the event loop, the queue, and the periodic rebase | `FUN_18008a110`, `FUN_18008a020` |
| `PhysicsEnvironment` | the clock, the fixed step and the simulate target | `physics.cpp:177-180`, `FUN_180082540` |
| `RagdollBody`, `IvpTransform` | bodies from the `.phy`, and IVP's axis/unit/transpose convention | `RagdollCreateObjects`, `RagdollGetBoneMatrix` |

**Named for the engine's classes, not for what they hold.** `PhysicsTimeManager` was first called
`PhysicsEventQueue`, which the analysers reject and which was the worse name anyway: it describes the
data structure where IVP describes the job. The queue is a detail; being the thing that decides when
everything happens is not.

**What is still missing to make a corpse settle**, in the order the work needs it:

1. **Which event performs the step.** The loop fires each event through its own vtable slot 1, so the
   PSI is one particular event class and its slot 1 is the step. Not yet identified.
2. **The integration** — gravity into velocity, velocity into the transform at `core+0x90`, with the
   damping the `.phy` supplies and `g_PhysDefaultObjectParams`' 0.1/0.1 underneath it.
3. **The constraint solve, assembled.** Every piece is read — the Jacobian row and its memo, the
   effective mass `Jᵀ M⁻¹ J`, the accumulated impulse at `record+0x18`, the 0.8 relaxation on two of
   three axes, the angle unwrap, the atan2 — but two inputs are not: what fills `+0x2d0` and
   `+0x100`, and whether the 0.8 is fixed. Both live above the constraint.
4. **Collision against the world**, `ivp_mindist*`, entirely unread.

**Nothing about the integrator has been written**, deliberately. Its shape is guessable and a guess
would be a divergence — the same rule that stopped `useClockwiseRotations` being "fixed". The
transcribed types above stop exactly where the reading stops.

*Evidence class: read-from-source for every line cited; the code is transcription rather than design.*

## Correction: `FUN_18008a020` is the PSI EVENT, and the rebase happens every step

**Recorded earlier in this document as "the rebase function", and that was half of it.** The function
is the master PSI event's own fire routine — the thing the event loop dispatches through
`(**(code **)(*event + 8))(event, env)` — and it does three jobs in one call.

**The evidence is the time manager's own constructor.** `FUN_180089dc0` allocates the queue (0x20
bytes, which becomes `timeManager+0x10`), allocates a 0x10-byte object, writes
`&PTR_FUN_1800fd738` as its vtable, and **immediately inserts it into the queue it just built**,
storing the returned slot index at the object's `+8` — the same "an event keeps its own queue index
at +8" convention the loop relies on. The vtable is two slots:

```
SLOT 0  180081920   (destructor)
SLOT 1  18008a020   <- the fire function
SLOT 2  3ba3d70a    <- not a code address; the vtable ends at two
```

So the simulation's heartbeat is **an event that reschedules itself**:

```c
env[0x198] = env[0x188];                          // rebase base := now
env[0x190] = (float)env[0x108] + env[0x188];      // next PSI := now + step
for (each queued entry) entry.time -= (float)now; // rebase EVERY pending time
tm[0x28] = env[0x198];  tm[0x20] = 0;
FUN_180082560(env);                                // the whole physics pipeline
slot = FUN_1800aaed0(queue, this,                  // requeue itself one PSI later
         (float)(env[0x190] - tm[0x28]));
*(uint *)(this + 8) = slot;
```

**Two things this changes about what was written here before:**

- **The rebase is not periodic maintenance — it happens on EVERY PSI.** The queue's float times are
  re-zeroed to "now" sixty-odd times a second, which is a stronger statement than "occasionally, to
  protect precision": an event's stored time is never more than one step old. A transcription that
  rebased lazily would hold larger offsets than the engine ever does.
- **The physics step is an event, and now it is named.** The earlier note said the step "is not a
  special case in the loop"; that is true and this is the specific event it was talking about.

**The step's default is 1/66 exactly.** `env+0x108` holds `0.0151515151515152` and `env+0x110` its
reciprocal, `66.0` — vphysics' own default rate. That is not in conflict with the client setting
`SetSimulationTimestep( gpGlobals->interval_per_tick )` (`physics.cpp:180`): the binary's default is
what the environment starts with, and the client overwrites it with the demo's tick interval. **Both
numbers are real and they are answers to different questions** — what vphysics does if nobody says,
and what TF2's client says.

## The pipeline, `FUN_180082560`, and where the trail currently ends

The PSI event delegates everything to `FUN_180082560(env)`, which is bracketed by profiler markers
into phases. Read directly: a dirty-list flush, a budgeted work-queue walk, a listener fan-out, a
contact/material-pair pass driving friction state, then three unnamed calls in the shape of broad
phase → narrow phase → island solve, then a second friction pass and a re-prediction of every
contact's next check time (which is one of the nine callers of the queue's insert function).

**And the honest part: none of it writes `IVP_Core` directly.** No reference to `core+0x40`,
`+0x90`, `+0x130` or `+0x140` appears anywhere in that body. The integration is another layer down.

**Two leads were named here and one of them was WRONG.** Recorded rather than quietly deleted,
because the shape of the mistake is the useful part:

- ~~**The island solve dispatches through yet another vtable**~~ — `(**(code **)(*ev + 8))(ev, this,
  dt)` inside `FUN_18009a690`/`FUN_18009a4f0`. **That is not an island solve.** Both functions are a
  **contact-pair re-check scheduler**: `FUN_1800985a0` calls `FUN_180099380`, which computes the
  relative velocity between two cores and re-queues the next broad-phase check time for the pair.
  The label came from the SHAPE — a vtable dispatch taking a float time budget, inside the physics
  step — and that shape is IVP's generic event convention, which is precisely why it says nothing
  about what the objects are. **A vtable dispatch with a time argument is not evidence of a solver;
  in this binary it is evidence of the event queue, which is everywhere.**
- **`env+0xE0`'s sub-object at `+8`**, dispatched at its vtable `+0x10`, is NULL in the constructor
  (`FUN_18009f490`) and populated at runtime. Still unchased, and no longer needed.

**The integrator is found, and it was not behind either lead.** See the next section.

*Evidence class: read from the decompiled binary for the constructor, the vtable, the fire function
and the pipeline's call list; the phase LABELS are inferred and flagged; the 1/66 constants are read
bit patterns; the struck-out island-solve label was inferred, and is now falsified by reading.*

## The integrator, found: `FUN_180099a00`, and it is three functions deep

**The chain from the physics step to a moved body**, each address read rather than inferred:

```
FUN_18008a020    the PSI event's fire routine        (above)
  FUN_180082560  the seven-phase pipeline            (above)
    FUN_180090700    island assembly
      FUN_1800909d0  integrate every awake core in this island
        FUN_180099a00  THE per-core integrator
          FUN_180099fc0  build the step's delta rotation, and free-rotate the angular velocity
            FUN_180070d60  quaternion product
            FUN_180070c60  quaternion normalize
```

**`FUN_180099a00` does three things in order, and the first is the one worth noticing:**

1. **Position integrates against the PREVIOUS step's velocity, not the current one.**
   `core+0x150/0x158/0x160` (doubles) `+= core+0x170/0x174/0x178 * dt`. That is explicit Euler
   deliberately lagged by one step — a body's velocity change this step does not move it until next
   step.
2. **Then the cache is refreshed**: `+0x170/0x174/0x178 := +0x140/0x144/0x148`, the current linear
   velocity.
3. **Orientation is COMMITTED first and integrated afterwards**, which is the opposite of what a
   first reading of this suggested and is corrected below. `core+0x180..0x198` (current) is
   overwritten from `core+0x1a0..0x1b8` (predicted) BEFORE `FUN_180070d60`/`FUN_180070c60` advance
   the predicted one. **Two quaternions, and the visible one is deliberately a step behind.**

**`FUN_180099fc0` also integrates the angular velocity itself** — Euler's rigid-body equation, with
cross terms shaped like `(Iy − Iz)/Ix · ωy·ωz` off `core+0x40/0x44/0x48`, and it **sub-steps** when
`|ω|²·dt²` passes a threshold held at `DAT_1800fdf90`. So a fast-spinning limb is integrated more
finely than a slow one, inside one PSI. A transcription that stepped rotation once per PSI would
diverge exactly where a corpse's arm is whipping.

**The quaternion normalise is double precision with a hand-rolled Newton-Raphson reciprocal square
root** (`FUN_180070c60`), not an `rsqrtss`. Worth stating because it was searched for the other way
round first: a whole-binary scan found only twelve `RSQRT*` instructions in 2,938 functions and none
of them is this.

### The `IVP_Core` field map, as far as it is read

| offset | field | how it is known |
|---|---|---|
| `+0x40/0x44/0x48` | inertia terms used by the free-rotation cross products | `FUN_180099fc0` |
| `+0x4c` | inverse mass | `FUN_1800778c0`: `vel += impulseDir * core[+0x4c] * scale` |
| `+0x130/0x134/0x138` | angular velocity | four independent readers agree |
| `+0x140/0x144/0x148` | linear velocity | four independent readers agree |
| `+0x150/0x158/0x160` | position, as DOUBLES | `FUN_180099a00` |
| `+0x170/0x174/0x178` | previous-step velocity cache | `FUN_180099a00` |
| `+0x180..0x198` | current world orientation quaternion | `FUN_180099a00` commits here |
| `+0x1a0..0x1b8` | predicted/working orientation quaternion | integrated in place, then committed |
| `+0x1d0` | last-synced absolute environment time | `FUN_1800783c0`, `FUN_180099a00` |

### Gravity is written and read; the moment it enters a velocity is still unread

`CPhysicsEnvironment::SetGravity` is `FUN_1800150f0` — vtable slot 3 — and it converts Source's
vector into IVP's before storing it: scale by `DAT_18011f000`, negate Z by XOR against
`DAT_1800ea5e0`, and reorder to X, Z, Y. **That is the same axis-and-unit convention already
recorded for the ragdoll transform**, arrived at independently from a different function, which is
the first cross-check this project has on it.

It calls `FUN_1800824e0`, which writes `env+0x118/0x120/0x128` as doubles and caches the magnitude at
`env+0x138` as a float. **The field's identity is confirmed by an unrelated reader** — a
vehicle-wheel weight-transfer function, `FUN_18008bce0`, dereferences the environment and reads all
three — so this is not `SetGravity` agreeing with itself.

**What is NOT established: where gravity is added to a core's velocity each step.** A whole-binary
decompile-and-search over 2,813 non-thunk functions, for anything touching the linear velocity at
`+0x140/0x144/0x148` together with the absolute clock at `env+0x188`, returned eight functions and
none of them contains `velocity += gravity * dt`. The nearest candidate is `FUN_180019cc0`, a
per-core effector dispatcher whose third case adds a **mass-independent, world-space acceleration**
straight into the velocity using the exact unit scale and sign-flip constants `SetGravity` uses:

```c
local_a8 = (float)local_c8 * DAT_18011f000;
local_a4 = (float)((uint)(local_c0 * DAT_18011f000) ^ uVar5);
*(float *)(lVar1 + 0x140) = local_a8 * fVar14 + *(float *)(lVar1 + 0x140);   // no inverse-mass scale
```

**Mass-independence is the tell** — every other effector path in that same function multiplies by
the inverse mass at `core+0x4c` first. But the vector reaching case 3 arrives through a virtual call
on a per-core controller list, and **that call has not been traced back to a gravity object**, so
this is INFERRED and is written down as inference. It is equally consistent with a generic
actuator/spring/wind path with gravity applied somewhere still unfound.

*Evidence class: read from the decompiled binary for the whole chain, the field map, `SetGravity`
and the gravity field's second reader; INFERRED and flagged for `FUN_180019cc0` being the gravity
application.*

## The integrator, read line by line — and everything in it lags by one step

**The summary above was written from a description; this is written from the decompiled bodies**, and
one thing in it was backwards. Both halves of the state a viewer reads are **deliberately one step
old**, and they are old in the same way:

```c
// FUN_180099a00, verbatim, in execution order
dVar9 = *(double *)(*(longlong *)(param_1 + 0x10) + 0x188);   // env.now
dVar2 = *(double *)(param_1 + 0x1d0);                          // core.lastSynced
*(undefined8 *)(param_1 + 0x1d0) = *(undefined8 *)(*(longlong *)(param_1 + 0x10) + 0x188);
dVar9 = (double)(float)(dVar9 - dVar2);                        // dt for POSITION

*(double *)(param_1 + 0x150) = (double)*(float *)(param_1 + 0x170) * dVar9 + *(double *)(param_1 + 0x150);
*(double *)(param_1 + 0x160) = (double)*(float *)(param_1 + 0x178) * dVar9 + *(double *)(param_1 + 0x160);
*(double *)(param_1 + 0x158) = (double)*(float *)(param_1 + 0x174) * dVar9 + *(double *)(param_1 + 0x158);

*(undefined4 *)(param_1 + 0x170) = *(undefined4 *)(param_1 + 0x140);   // cache := current velocity
*(undefined4 *)(param_1 + 0x174) = *(undefined4 *)(param_1 + 0x144);
*(undefined4 *)(param_1 + 0x178) = *(undefined4 *)(param_1 + 0x148);

*(undefined8 *)(param_1 + 0x180) = *(undefined8 *)(param_1 + 0x1a0);   // current := predicted
*(undefined8 *)(param_1 + 0x188) = *(undefined8 *)(param_1 + 0x1a8);
*(undefined8 *)(param_1 + 400)   = *(undefined8 *)(param_1 + 0x1b0);
*(undefined8 *)(param_1 + 0x198) = *(undefined8 *)(param_1 + 0x1b8);

FUN_180070d60((double *)(param_1 + 0x1a0),(double *)(param_1 + 0x1a0),local_48);  // predicted *= delta
FUN_180070c60((double *)(param_1 + 0x1a0));                                       // normalise
```

- **Position uses the PREVIOUS step's velocity** and only then refreshes the cache, so a body does
  not move on the step its velocity was first set — it moves on the next one.
- **The visible orientation is the PREVIOUS step's predicted one.** The copy down happens before the
  multiply, not after. **The earlier note in this document had that the other way round**, which
  would put the drawn corpse a step ahead of the engine rather than level with it.

**The two clocks are different, and that is not a tidiness detail.** The orientation's `dt` is a
PARAMETER, computed once per island in `FUN_1800909d0` as `env[0x190] − env[0x188]` — target minus
now — and handed to every core in that island. The position's `dt` is computed per core, inside
`FUN_180099a00`, as `env[0x188] − core[0x1d0]`. **A core that has been asleep therefore catches its
POSITION up in one long step while its ORIENTATION advances by one nominal step.** A transcription
using one `dt` for both is right for a body that never sleeps and wrong for every corpse that
settles and is nudged again.

### The rotation integrator, `FUN_180099fc0`, and its two constants

```c
fVar9  = (*(float *)(param_1 + 0x24) - *(float *)(param_1 + 0x28)) * *(float *)(param_1 + 0x40);
fVar16 = (*(float *)(param_1 + 0x28) - *(float *)(param_1 + 0x20)) * *(float *)(param_1 + 0x44);
fVar15 = (*(float *)(param_1 + 0x20) - *(float *)(param_1 + 0x24)) * *(float *)(param_1 + 0x48);

dVar10 = (double)(wy*wy + wx*wx + wz*wz) * dVar11 * dVar11;      // |w|^2 * dt^2
if (_DAT_1800fdf90 < dVar10) {
    auVar13 = sqrtpd(dVar10 * _DAT_1800fdfa0, ...);
    iVar6   = (int)auVar13._0_8_ + 1;                            // sub-step count
    dVar11  = dVar11 / (double)iVar6;
}
```

**Both constants were dumped rather than guessed.** `DAT_1800fdf90` is **0.027777777777777776**,
which is 1/36, and `DAT_1800fdfa0` is **144.0**, which is 12². So the test is `|ω|·dt > 1/6` radian
— about 9.55 degrees of turn in one step — and the count is `floor( 12·|ω|·dt ) + 1`. **A
transcription that stepped rotation once per PSI is right for a settling corpse and wrong for a limb
that is whipping**, which is exactly the frame anybody looking at a demo is looking at.

**The Euler free-rotation terms are `Δωx = (Iy − Iz)·(1/Ix)·ωy·ωz·dt`** and its two rotations, with
`Ix,Iy,Iz` at `core+0x20/0x24/0x28` and the reciprocals at `core+0x40/0x44/0x48`. *The reciprocal
reading is INFERRED* — it is what makes the expression equal to the classical torque-free equation,
and it matches the inverse mass living at `+0x4c` in the same block, but the division that produces
it was not found.

### The delta quaternion is a per-axis Taylor half-angle, not a first-order update

`FUN_180071680` builds each sub-step's rotation:

```c
dVar2 = param_3 * DAT_1800ee388;                                  // dt/2, DAT_1800ee388 = 0.5
fVar4 = (float)((double)*param_2 * dVar2);                        // theta_x = wx * dt/2
dVar6 = (double)(fVar4 - fVar4*fVar4*fVar4*DAT_1800eb148);        // sin(theta) ~ theta - theta^3/6
*param_1 = dVar6;                                                 // ... same for y and z
auVar3._0_8_ = DAT_1800ea9b8 - (dVar5*dVar5 + dVar6*dVar6 + dVar2*dVar2);
param_1[3] = sqrtpd(auVar3, auVar3)._0_8_;                        // w = sqrt(1 - |xyz|^2)
```

`DAT_1800eb148` is 0.16666667 as a **float** — a third-order sine series, not a library call — and
the real part is recovered from the unit-length identity rather than from a cosine. **Each axis gets
its own half-angle independently**, which is not the same rotation as one half-angle about the
combined axis; it is IVP's approximation and it is what has to be reproduced.

**The quaternion is laid out (x, y, z, w), index 3 being the real part**, established from
`FUN_180070d60`'s own arithmetic rather than from Source's `Quaternion` struct — every one of its
four output expressions matches the Hamilton product only under that assignment.

**The normalise is not a normalise unless it has to be** (`FUN_180070c60`): it computes
`1 − |q|²`, and only if that exceeds a tolerance does it run a Newton-Raphson reciprocal square root
to convergence. A quaternion already close to unit length is left untouched.

### Two clamps found on the way, and one field still unread

`FUN_18001c9d0`, which `CreatePolyObject` reaches, clamps what a `.phy` declares:

- **mass into `[0.1, 50000]`** — `if ( mass <= 0.1 ) mass = 0.1; if ( 50000.0 <= mass ) mass = 50000.0;`
- **inertia into a constant-pool pair**, then written to three fields, which is an isotropic seed.

**`rotInertiaLimit` is copied through untouched** into the build parameters at offset `0x40` of that
struct, and **nothing that was read dereferences it**. It survives four more calls without being
looked at; the next candidate is a mass-matrix builder reached from `FUN_180072c70`, undecompiled.
So the answer to "what does `rotInertiaLimit = 0.1` do" is still **not established**, and the audit
entry stays open rather than being closed with a plausible story.

*Evidence class: read from the decompiled binary for every body, every offset and both dumped
constant pairs; INFERRED and flagged for `core+0x40/0x44/0x48` holding reciprocals, and for the
environment pointer at `core+0x10` being the same object the island driver holds.*

## IVP left its own source file names in the binary

**The assert strings carry `__FILE__`, and they name the modules.** Recovered verbatim, each tied to
functions by a code cross-reference from the string:

| module | what it owns here |
|---|---|
| `ivp_collision\ivp_mindist.cxx` | `FUN_1800975d0`, the `IVP_Mindist` constructor |
| `ivp_collision\ivp_mindist_event.cxx` | `FUN_1800a3d30`, `FUN_1800a3fe0`, `FUN_1800a4200` |
| `ivp_collision\ivp_mindist_minimize.cxx` | `FUN_180094ad0`, `FUN_180095ad0`, `FUN_180095cb0`, `FUN_180094e10`, `FUN_180094f80` |
| `ivp_collision\ivp_mindist_recursive.cxx` | `FUN_1800b2460` |
| `ivp_collision\ivp_compact_ledge_solver.cxx` | referenced from data only |
| `ivp_intern\ivp_mindist_friction.cxx` | `FUN_18008d0c0` |
| `ivp_intern\ivp_friction.cxx` | `FUN_180086a50`, `FUN_180086e80` |
| `ivp_intern\ivp_object.cxx` | `FUN_180073df0`, `FUN_180072e90` |
| `ivp_intern\ivp_ball.cxx` | `FUN_18009f0d0/0f0/180/1a0` |
| `ivp_compact_builder\ivp_object_polygon_tetra.cxx` | `FUN_180055130`, `FUN_180055490` |

Two ordinary strings survive as well: `IVP_SurfaceBuilder_Pointsoup::convert_pointsoup_to_template_polygon`
and `IVP_SurfaceBuilder_Ledge_Soup::insert_ledge()` — both compact-BUILDER, not runtime.

**This is a navigational win rather than a behavioural one**, and it is worth having for the same
reason `CL_CopyNewEntity: GetClassBaseline(%d) failed.` was: a string names its own function, so a
question about friction now starts at a known module instead of at a call graph.

*Evidence class: read, from a string dump with a control — the same search returned 18 unrelated
source paths, so an empty answer would have been about the subject rather than the instrument.*

## Collision: the mindist family, and what closes the loop

**`IVP_Mindist`'s vtable is at `0x1800fe960`, thirteen slots.** Slot 0 is the destructor
(`FUN_1800b2250`, recognisable by the MSVC set-vtable-then-hand-off-to-base pattern); **slot 1,
`FUN_1800992e0`, is the collision event**; slot 8 is `FUN_1800b2460` from
`ivp_mindist_recursive.cxx`.

```c
void FUN_1800992e0(longlong *param_1,longlong param_2)
{
  (**(code **)(**(longlong **)(param_2 + 0x50) + 8))();
  FUN_180095cb0(param_1);                       // recompute the minimum distance
  uVar1 = *(uint *)(param_1 + 4);
  if ((uVar1 & 0xc000) == 0) {
    if ((uVar1 & 0xf) == 0) {
      if ( /* cached distance crossed a per-material margin */ ) {
        (**(code **)(*param_1 + 0x40))();       // its OWN slot 8 — the recursive refine
        goto LAB_18009935c;
      }
      iVar2 = 2;
    } else { iVar2 = 1; }
    FUN_180099380((longlong)param_1,0,iVar2);   // re-queue this pair
  }
  ...
}
```

**This closes a loop that was open in this document.** `FUN_18009a690`/`FUN_18009a4f0` were
retracted above from "island solve" to "contact-pair re-check scheduler", and the obvious next
question was what the scheduler schedules. **It schedules this.** Slot 1 recomputes the pair's
distance and then either escalates into the recursive refine or re-queues itself for a later check —
so the whole broad-phase side of IVP is one self-rescheduling event per pair, the same shape as the
PSI step itself.

The margin is looked up per material: `DAT_18012d548[byte(flags >> 0x16)]`, a 256-entry float table,
added to `DAT_18012d664`, which dumps as **0.0**.

**The narrow phase is a closest-feature search over half-edges, not a triangle soup.** Four routines
dispatch by feature kind out of `ivp_mindist_event.cxx` — `FUN_1800a2b30`, `FUN_1800a1ff0`,
`FUN_1800a1b50`, `FUN_1800a1420` — each walking a compact-ledge half-edge structure with the same
pointer idiom against the offset tables `DAT_180124fb8`/`DAT_180124fc8`. They share one
bisection/line-search core, `FUN_1800b6210` with `FUN_1800b6590` for refinement, which is generic
over feature type because the geometry arrives as a **one-slot evaluator vtable**: `FUN_1800a3470`
and its neighbours are each a few lines of plane arithmetic, and `FUN_1800a3470` is a Hesse-plane
point distance. Dumped constants: `DAT_1800fb100` = 1.0E-8, `DAT_1800ea9b8` = 1.0.

*The four routines being the classic vertex-vertex / vertex-edge / edge-edge / vertex-face cases is
INFERRED from the dispatch shape and the half-edge walk, not from a name.*

## The static world is an ordinary body with one bit set — and two independent readings agree

**This is the answer that decides whether a corpse can be made to land on the floor at all**, and it
came out of two sessions that were not talking to each other.

Reading the CONTACT builder (`FUN_18008d0c0`, `ivp_mindist_friction.cxx`), each of the two colliding
objects is tested before it contributes anything:

```c
pbVar10 = *(byte **)(*(longlong *)(param_1 + 0x20) + 0xe8);   // object -> core
if ((*pbVar10 & 2) == 0) {
    /* r x n cross products, and the effective inverse mass from
       core+0x40/0x44/0x48 and core+0x4c */
} else {
    /* zero this object's rotational Jacobian AND its mass term entirely */
}
```

Reading the ISLAND DRIVER (`FUN_1800909d0`), reached from an entirely different direction, the same
bit decides whether a core is integrated at all:

```c
pbVar2 = *(byte **)(param_1[3] + uVar7 * 8);
if ((*pbVar2 & 2) == 0) { ... FUN_180099a00(pbVar2, &local_868, ...); ... }
```

**So bit 1 of the byte at `core+0x0` means: do not integrate me, and contribute no mass or inertia
to any contact.** That is infinite mass and a fixed transform, which is exactly what static map
geometry is. Neither site names it, but the two together are stronger than either — one says it does
not move, the other says nothing can push it.

**And the public headers agree from the third side.** `IPhysicsEnvironment::CreatePolyObjectStatic`
takes the identical `CPhysCollide*` as `CreatePolyObject` (`vphysics_interface.h:555`), and the map's
own baked collision is fed through the static variant at level load
(`physics_shared.cpp:602-667`). **There is no separate static-geometry type at the API boundary**,
which is what a BSP or mesh path would need.

**What is NOT established:** the code that SETS that bit. Nothing found writes it, so "static objects
are flagged this way" is read from two consumers and not from the producer.

## Contact response is accumulated, not applied

`FUN_18008d0c0` allocates a ~0x110-byte record per contact from a per-environment pool, caches it at
`mindist+0x70`, builds an orthonormal contact frame by explicit cross products, and computes the
per-axis effective inverse mass:

```c
*(float *)((longlong)pdVar7 + 0x94) =
     rx*rx * core[+0x44] + ry*ry * core[+0x40] + rz*rz * core[+0x48] + core[+0x4c];
```

**It writes nothing to `core+0x130..0x148`.** Neither does `FUN_180086a50` (a synapse-identity
predicate) or `FUN_180086e80` (friction-system split and merge when an object's contact list
changes). So a contact does not become an impulse where it is found; it becomes a **persistent
record with its effective-mass terms precomputed**, and something later consumes it.

**That "something later" is the one piece still missing**, and it is the same gap gravity's
application point is in. Both are downstream of the contact/friction records and upstream of the
integrator, in the part of `FUN_180082560` that has been read as a call list and not yet as
behaviour.

*Evidence class: read from the decompiled binary throughout; INFERRED and flagged for the friction
anchor's drift correction and for the four narrow-phase routines being the V-Clip feature cases.*

## The ragdoll constraint solve, found — and it is sequential impulses

**`CreateRagdollConstraint` is `FUN_180012e10`**, slot 15 of `IPhysicsEnvironment`'s vtable. **The
base is `0x1800ebbd8`, not `0x1800ebbf0`** — the interface has no base class, so slot 0 is the
destructor; `0x1800ebbf0` is merely where slot 3's pointer sits. Cross-validated twice over: slot 1
is the only xref of `"VPhysicsDebugOverlay001"`, and slot 10 the only xref of `"Deleted NULL
vphysics object"`.

**The parameter block maps onto the published struct byte for byte**, which is the check that says
the whole slot count is right:

| `constraint_ragdollparams_t` | byte | read at |
|---|---|---|
| `constraintToReference` | 0x18 | `FUN_18000ca70((float *)(param_4 + 6), …)` |
| `constraintToAttached` | 0x48 | `FUN_18000ca70((float *)(param_4 + 0x12), …)` |
| `axes[3]` | 0x30.. | a permutation loop through `FUN_180002bb0(index)` |
| `onlyAngularLimits` | 0xB0 | `*(char *)(param_4 + 0x2c) == '\0'` |
| `isActive` | 0xB1 | `*(char *)((longlong)param_4 + 0xb1) != '\0'` |
| `useClockwiseRotations` | 0xB2 | `*(char *)((longlong)param_4 + 0xb2) != '\0'` |

**Two things this project already built are confirmed by it.** `useClockwiseRotations` — the field
Valve's own header calls *"HACKHACK: Did this wrong in version one. Fix in the future."* — drives a
sign flip through the same `DAT_1800ea5e0` mask used everywhere else, which is what
`RagdollJointLimits.Convert` reproduces. And the axes are read through a **permutation**, which is
the axis remap `RagdollJointLimits.Slot` already carries. Both were transcribed from the SDK and the
`.phy` text months before this function was found; the binary agrees.

**The solver object's vtable is `0x1800ee9d0`, eight slots**, bounded above by the literal
`"ragdoll"`. Slots 3 and 4 are the solve:

- **`FUN_180038620`** rebuilds each body's rotation matrix, forms a cross-product axis and
  normalises it with `rsqrtps` — the setup-and-solve pass.
- **`FUN_180038d10`** skips the rebuild and reuses cached geometry — the cheap re-solve.

**Both end in the same block, and it is the finding:**

```c
if ((*pbVar1 & 0x12) == 0) {
  *(uint *)(pbVar1 + 0x130) = (uint)(… + *(float *)(pbVar1 + 0x130)) & MASK;   // angular +=
  …
  *(uint *)(pbVar1 + 0x140) = (uint)(… + *(float *)(pbVar1 + 0x140)) & MASK;   // linear  +=
  …
}
if ((*pbVar2 & 0x12) == 0) {                       // the other body, with negated terms
  *(uint *)(pbVar2 + 0x140) = (uint)((CONST - fVar8) * fVar21 + *(float *)(pbVar2 + 0x140)) & MASK;
  …
}
```

**Accumulate in place, straight into velocity, equal and opposite on the two bodies.** That is a
**sequential-impulse solver**, not a system-matrix or Jacobian solve — which matters because the two
are not interchangeable and the matrix version is the one somebody would reach for from a textbook.
And it explains what the contact records are for: `FUN_18008d0c0` precomputes each contact's
effective inverse mass and stores it, and a solver of this shape consumes exactly that.

The shared inner routine `FUN_180036e10` does **one translation solve and two independently gated
per-axis angular solves** per call (`FUN_180036f80` then `FUN_1800372c0` twice), which is the
box-limit-per-axis shape `constraint_axislimit_t` describes.

**A flag difference worth not smoothing over.** The constraint solve skips a body on
`(*flags & 0x12) != 0` — two bits. The integrator (`FUN_1800909d0`) and the contact builder
(`FUN_18008d0c0`) test only `& 2`. So there is a second bit, `0x10`, that stops a body being pushed
by a constraint while still letting it integrate and take contacts. Its meaning is **not
established**; recorded as a difference rather than rounded off to "the immovable flag".

**What is NOT established: the iteration count.** Both solve slots are the sole occupants of their
vtable entries, so static analysis finds no callers — the outer "solve every constraint N times"
driver was not located, and no number is inferred in its place. A sequential-impulse solver's
behaviour depends on that count, so this is a real gap rather than a detail.

## Gravity: a named dead end, with the candidate stated as a candidate

**`FUN_180019cc0` is a per-core effector dispatcher with four modes**, decompiled in full:

| mode | what it does |
|---|---|
| 1 | local-frame ACCELERATION — rotates through the core's own matrix at `+0x90` first |
| 2 | local-frame impulse — same rotation, then through `FUN_1800778c0`, so mass-DEPENDENT |
| 3 | **world-frame acceleration, mass-INDEPENDENT** — added straight into `core+0x140/0x144/0x148` |
| 4 | world-frame impulse, the mass-dependent twin of 3 |

```c
local_a8 = (float)local_c8 * DAT_18011f000;
local_a4 = (float)((uint)(local_c0 * DAT_18011f000) ^ uVar5);          // uVar5 = DAT_1800ea5e0
*(float *)(lVar1 + 0x140) = local_a8 * fVar14 + *(float *)(lVar1 + 0x140);
```

**~~Mass-independence is the tell~~ — and the fingerprint argument below was WRONG.** See the
correction that follows; the constants are general, and this dispatcher is not gravity.

**And it is still not established, for three reasons that were checked rather than assumed:**

- `FUN_180019cc0` is itself a vtable slot — exactly ONE occurrence of its address exists anywhere in
  readable memory, at `0x1800ec880` — so it is only ever reached by virtual dispatch and static
  caller-finding returns nothing.
- **No code reference to that vtable address exists either**, so the constructor that installs it was
  not found.
- The vector reaching case 3 arrives from a virtual call on a different object again, and nothing
  ties it to `env+0x118/0x120/0x128`.

**So the honest state is: the best candidate, with the right shape, the right units and the right
mass behaviour, and no chain of custody to gravity's storage.** Writing it up as "gravity is applied
here" would be the confident-wrong-conclusion this document exists to avoid.

**One thing that IS newly established about gravity's storage:** `FUN_18008bce0` reads all three
doubles at `env+0x118/0x120/0x128` and feeds them into a friction and weight computation. That is a
second, independent confirmation of the field's identity — and it is explicitly NOT the integration
path, noted so it is not mistaken for one later.

*Evidence class: read from the decompiled binary for the constraint chain, the field mapping, the
solver vtable and both solve slots; read for `FUN_180019cc0`'s four modes; NOT ESTABLISHED, and
labelled so, for gravity's call site, the effector object, and the constraint solver's iteration
count.*

## Correction: `FUN_180019cc0` is the MOTION CONTROLLER, and my evidence for gravity was bad

**The candidate above is dead, and the way it died is the useful part.** The argument for it was
that its unit-conversion fingerprint was "byte-for-byte `SetGravity`'s" — the same scale
`DAT_18011f000` and the same sign mask `DAT_1800ea5e0`. **That is not evidence of anything.** Those
constants are dumped as **0.0254** (inches to metres, exactly) and **0x80000000** (the IEEE sign
bit), and they have now been found in three unrelated places: `SetGravity`, the constraint group's
`errorTolerance` conversion, and this dispatcher. **Every vector crossing the Source-to-IVP boundary
needs them.** A fingerprint shared by everything identifies nothing — the same mistake as reading a
vtable dispatch as a solver, one layer down.

**What it actually is, established from a published enum.** `FUN_180019cc0` switches on a callback's
return code taking values 1 to 4, and `vphysics_interface.h:464` declares:

```cpp
enum simresult_e { SIM_NOTHING = 0, SIM_LOCAL_ACCELERATION, SIM_LOCAL_FORCE,
                   SIM_GLOBAL_ACCELERATION, SIM_GLOBAL_FORCE };
```

Every branch matches: 1 rotates into world space and skips the mass divide (local acceleration), 2
rotates and divides (local force), 3 neither rotates nor divides (global acceleration — the
"candidate"), 4 divides only (global force). And the call feeding it has the exact shape of
`IMotionEvent::Simulate( IPhysicsMotionController *, IPhysicsObject *, float, Vector &, AngularImpulse & )`.

**So this is `IPhysicsMotionController`** — the generic machinery that lets GAME code register an
`IMotionEvent` and have its returned force applied per tick. It is not gravity and never was.

**What was eliminated, and how**, because a negative result is only worth having with its method:

- **Zero `LEA` installs of the vtable `0x1800ec880`**, across 221,934 instructions and 13,374 `LEA`s.
  The search needed a purpose-built instrument, since a `LEA` encodes a RIP-relative displacement
  rather than the absolute address — and the first version of that instrument was silently wrong
  (`getOpObjects` returns a `Scalar`, not an `Address`) and was caught against a known control
  before being trusted.
- **Zero occurrences of the vtable's address** anywhere in aligned readable memory, with the same
  instrument correctly finding slot 0's function address in `.rdata` as its control.
- **The environment constructor `FUN_180080d90`, decompiled in full** — about fifteen sub-objects,
  and it installs this vtable on none of them.

**Gravity's integration site was still unfound at this point.** It is found now — see below — and it
turned out to be behind a dispatcher after all, just not that one.

## The constraint solver runs exactly TWO iterations

**Established by an exact inverse pair, which is as good as this gets without a symbol.** The group
constructor stores the count with a bias and the getter removes it:

```c
*(int *)(param_1 + 4) = *(int *)(param_3 + 8) + 2;   // FUN_18003c330: additionalIterations + 2
param_2[2] = *(int *)(param_1 + 0x20) + -2;          // FUN_18003d240: the exact inverse
```

**So the base is 2 and `additionalIterations` adds to it one for one, unscaled.** A ragdoll is built
with `group.Defaults()`, which sets `additionalIterations = 0` (`ragdoll_shared.cpp:274-276`), so
**a TF2 corpse's joints are solved with exactly two sweeps per step.**

`CreateConstraintGroup` is slot 23, a thunk at `0x180012a40` that loads `environment+0x8` and
tail-jumps to `FUN_18000d330`. The solve driver is slot 8 of a **different** vtable from the
per-constraint one — `0x1800eeb10`, twelve slots — at `FUN_18003c780`:

```c
if (0 < *(int *)(param_1 + 0x20)) {
    do {
      fVar23 = *(float *)((longlong)&uStack_269f0 + uVar22);   // per-pass weight
      if (fVar23 == 0.0) break;
      // each attached constraint's slot 4 (the cheap re-solve), once descending then once ascending
      uVar22 = uVar22 + 4;
    } while ((int)uVar21 < *(int *)(param_1 + 0x20));
}
```

**Each pass walks the constraint list forwards and then backwards** — a symmetric Gauss-Seidel
sweep, which is what stops a chain of joints biasing toward whichever end is solved first. And each
pass carries a **relaxation weight from a hardcoded table** at `0x1800eeb70`, dumped as
`0.4, 0.4, 0.4, 0.4, 1.0, 1.0, 0.8, 0.6, 0.8, 0.8, 0.8, 0.8, …`. At the stock two iterations, both
weights are **0.4**.

**`errorTolerance` and `minErrorTicks` do NOT gate the loop**, which is the thing a reader would
assume. They drive a counter AFTER it, and that counter is what `IsInErrorState` reports — the same
call `CRagdoll::VPhysicsUpdate` makes before running `RagdollSolveSeparation`:

```
MOV EAX,[RDX+0x48]      ; minErrorTicks
CMP [RDX+0x4c],EAX      ; errorTickCounter >= minErrorTicks
SETGE AL
```

**And in this build that counter can never accumulate.** The value it is compared against is copied
from `_DAT_1800ff070`, dumped as **0.0**, refreshed unconditionally at the top of every call — so
`0.0 <= errorTolerance²` always holds and the counter resets each time. *INFERRED, and flagged: a
`RefsTo` on that global found readers only and no writer, which is consistent with a compiled-in
constant but does not exhaustively rule out an unresolved indirect write.*

*Evidence class: read for the slot derivation, the thunk, the constructor/getter inverse pair, the
loop and the weight table, all with dumped constants; INFERRED and flagged for the error counter
being unreachable in this build.*

## FOUND: gravity is a controller object, and `FUN_180074c80` is where it enters velocity

**The way in was a published method name, for the third time on this binary.**
`IPhysicsObject::EnableGravity( bool )` exists in the header, so a per-object gravity flag must
exist, and whatever reads it must be the application site. Both halves paid out.

**`EnableGravity` is `FUN_18001ba30`**, slot 13, and it does not set a flag at all — it adds the
object to or removes it from a LIST:

```c
cVar2 = (**(code **)(*param_1 + 8))();               // IsStatic()
if (cVar2 == '\0') {
  cVar2 = (**(code **)(*param_1 + 0x38))(param_1);   // IsGravityEnabled()
  if (param_2 != cVar2) {
    lVar1 = *(longlong *)(param_1[2] + 0xe8);        // the gravity controller
    if (param_2 != '\0') { FUN_1800748b0(lVar1, …); return; }
    FUN_180074fb0(lVar1, …);
  }
}
```

**So gravity is not a per-core field — it is membership of a set.** A static object is refused
outright, which is consistent with the `core+0x0 & 2` immovable bit found from the collision side.

**The controller is a per-environment singleton at `env+0x0`**, a 0x30-byte object whose vtable is
`0x1800ea728` — eight slots, and the bound is a good one: slot 8 would land on the string
`"sys:gravity"` at `0x1800ea768`. **Slot 4 is `FUN_180074c80`, and it is the answer:**

```c
void FUN_180074c80(longlong param_1, float *param_2, longlong param_3)
{
  uVar5 = count - 1;                          // param_3+2 == controller+0x1e2
  while (uVar5-- >= 0) {
    pbVar4 = array[uVar5];                    // param_3+8 == controller+0x1e8, an IVP_Core*
    if ((*pbVar4 & 0x10) == 0) {
      FUN_180078250((longlong)pbVar4, (double)*param_2);
      FUN_180077950((longlong)pbVar4);
      fVar1 = *param_2;                       // dt
      if ((*pbVar4 & 0x20) == 0) {
        fVar2 = *(float *)(param_1 + 0x14); fVar3 = *(float *)(param_1 + 0x18);
        fVar6 = *(float *)(param_1 + 0x10);
      } else {
        fVar2 = *(float *)(param_1 + 0x24); fVar3 = *(float *)(param_1 + 0x28);
        fVar6 = *(float *)(param_1 + 0x20);
      }
      *(float *)(pbVar4 + 0x140) = fVar6 * fVar1 + *(float *)(pbVar4 + 0x140);
      *(float *)(pbVar4 + 0x148) = fVar3 * fVar1 + *(float *)(pbVar4 + 0x148);
      *(float *)(pbVar4 + 0x144) = fVar2 * fVar1 + *(float *)(pbVar4 + 0x144);
    }
  }
}
```

**`v += g * dt`, on the established linear-velocity fields, with no mass term** — which is what
gravity is and what drag is not, since nothing here is velocity-dependent.

**Two per-core bits that were not in the field map:**

- **`0x10` skips gravity entirely** for that core.
- **`0x20` selects a SECOND gravity vector**, held at `controller+0x20/0x24/0x28` beside the default
  at `+0x10/0x14/0x18`. So IVP supports per-object gravity, and a transcription with one global
  vector would be right for TF2 and wrong for the engine.

**The vector reaches the controller by a copy, which is why nothing in the pipeline reads
`env+0x118`.** `SetGravity` converts to IVP units and calls `FUN_1800824e0`, which stores the
doubles at `env+0x118/0x120/0x128`, caches the magnitude at `env+0x138` — and then calls
`FUN_180075320(*env, g)`, which writes the same vector as **floats** into the controller at
`+0x10/0x14/0x18`. The environment constructor does the same thing at startup, which is what proves
`env[0]` is this object.

**`env+0x138` is confirmed by name.** The string `"m_gravityLength"` is in `.rdata` at `0x1800eda70`.

**What is NOT established, and it is the last thread:** the per-tick CALL to slot 4. The dispatch
`(**(code **)(*env[0] + 0x20))(env[0], &dt, env[0]+0x1e0)` was not located — `FUN_18008a020`,
`FUN_180082560`, `FUN_1800909d0`, `FUN_180090700`, `FUN_18009a690`, `FUN_1800983e0`,
`FUN_1800985a0` and `FUN_180075a90` were each read in full and none contains it, and the address
appears nowhere as a literal. So gravity is applied once per step from a site still unfound.

**Also inferred rather than read:** that `EnableGravity`'s `realobj+0xe8` is literally `env[0]`. The
structural match is strong — both are the same `+0x1e0`/`+0x1e2`/`+0x1e8` list shape, manipulated by
the same add/remove pair, and slot 4's own list argument has that shape — but the assignment that
populates `realobj+0xe8` was not found.

**Two leads were chased and eliminated**, recorded so they are not re-run: `FUN_180089660` reads a
`+0x118` on an unrelated drag structure rather than on the environment, and `FUN_180090700`'s
`realobj+0xe8` reads turn out to be an `IVP_Core*` rather than a controller. **`0xe8` is reused
across unrelated structures exactly as `0x118` is** — the same trap, twice.

*Evidence class: read from the decompiled binary for `EnableGravity`, the controller, its vtable,
the application function and the copy chain; the `"m_gravityLength"` and `"sys:gravity"` strings are
read; INFERRED and flagged for `realobj+0xe8` being `env[0]`; NOT ESTABLISHED for the per-tick call
site.*

## The constraint solve's arithmetic, and one thing it does NOT do

**The three-axis solve writes ANGULAR velocity only.** That was not expected and is worth stating
first: ~~`FUN_180036f80` consumes a point-to-point anchor offset — the thing anyone would call the
translation solve~~ — **and that description is WRONG; see the correction below.** Every terminal
write in it lands on `core+0x130..0x13c`. Linear velocity is never touched there. The only place the solve path writes `core+0x140..0x148` is a **separate,
conditional** block in both dispatchers, gated on `*(char *)(corePair + 0x157) != 0` and reached
through `FUN_180038070`, which is an anchor-drift correction rather than part of the axis solve.

### The cached geometry, `FUN_180037bd0`

Built once per constraint per tick and reused by every sweep — which is the entire point of the
slot 3 / slot 4 split:

```c
r_A = R_bodyA · anchor          → cache[0..3]     // anchor read from constraint+0x110
r_B = R_bodyB · (−anchor)       → cache[4..7]     // note the negation: equal and opposite
cache[8..11]  = r_A ⊙ invInertia_A
cache[12..15] = r_B ⊙ invInertia_B
```

using each core's own rotation rows at `core+0x90..0xe8`.

**The immovable gate lives HERE, not in the solve.** Each body is included only when
`(*coreFlags & 0x12) == 0`; a static one gets zeroed cache entries, which is what makes the
unconditional accumulate later net to zero for it. That is a tidier arrangement than a branch in the
inner loop and it is worth copying rather than "improving".

**The effective mass, and its reciprocal:**

```c
K = Σ_bodies ( r_i · (invInertia_i ⊙ r_i) )
y = rcpps(K); y = y + y − y*y*K;                      // one Newton-Raphson refinement
cache[0x14..0x17] = (K > FLT_EPSILON) ? y : 0.0;      // zeroed rather than divided by
```

**A near-degenerate joint yields a zero multiplier rather than an infinity**, which is a behaviour
and not a guard — every later impulse for that axis multiplies out to nothing.

### The impulse

Both `FUN_180036f80` and `FUN_1800372c0` compute the same shape:

```
bias    = ( axisSign * 0.8 * error * scale ) − ( torque * relativeVelocity )
impulse = bias * cache[0x14]                          // × 1/K
```

then clamp the correction against `1.0` — never apply more than the error itself — and accumulate:

```c
core.angularVelocity += impulse * (r ⊙ invInertia)    // cache[8..11] / cache[12..15]
```

**`0.8` is a bias constant at `0x1800ee9b0`, and it is NOT the group's 0.4.** Those are two
different numbers from two different tables: 0.4 is the per-sweep relaxation weight the group driver
carries, 0.8 is the error-reduction term inside one axis solve. Reading either as the other would be
easy and wrong.

**The torque field is a DAMPING COEFFICIENT, not a clamp.** `constraint+0x2d0..0x2dc` is passed
unmodified into all three inner solves, and its second lane multiplies the relative velocity in the
expression above. So Valve's `SetAxisFriction( rmin, rmax, friction )` — which leaves the angular
velocity at zero and puts the number in `torque` — produces a term that resists relative motion
while the bias drives the error to zero. A transcription treating it as a cap on impulse magnitude
would be a different joint.

**The running target is warm-started.** Each pass advances a tracked value at `flags+0x18` by a
fraction of the remaining error rather than recomputing it from a fresh transform, which is what
makes two sweeps meaningful rather than two identical corrections.

### The state tags, and a degenerate axis

`cache[0x1c]` holds a small integer in a float slot: **1 means the geometry is cached and valid**,
and **2 means the axis is degenerate and is skipped entirely**. The only place 2 is written is
inside `FUN_1800372c0`:

```c
auVar24 = |cross(referenceAxis, axis)|²;
if (movmskps(auVar24 <= 1.1920929e-7) != 0) { cache[0x1c] = 2; return; }
```

So a joint whose two axes have gone near-parallel is dropped for that axis rather than solved with a
near-zero cross product — the gimbal case, handled by refusing.

### Two gaps, named rather than filled

**1. The min/max comparison itself was not found.** The machinery that APPLIES a limit correction is
read in full, and the flag that enables it (`flags+0x1`) and the target it drives toward (`*param_7`)
both arrive already resolved. No `angle < minRotation` / `angle > maxRotation` test appears in
`FUN_180036f80`, `FUN_1800372c0`, `FUN_180036e10`, or either dispatcher. `FUN_180036b80` computes
`1/max(|a|,|b|)` and `1/sum(a,b)` per lane — the shape of a cheap angle ratio — and writes into a
DIFFERENT cache field from the one the solves read, but what it produces was not resolved. **Found
the code that applies a limit; did not find the code that decides one is needed.**

**2. There are only TWO angular solves, not three.** `FUN_180036e10` makes one anchor call and two
angular calls, with the two differing in their reference vector (`geom+0x140` vs `geom+0x120`) and
their per-axis flags (`flags+0xe8` vs `flags+0xcc`) while sharing a basis at `geom+0x130`. A ragdoll
joint declares THREE axis limits. **The twist axis is unaccounted for**, and that is recorded as
absent rather than assumed to be handled somewhere convenient.

**The relaxation weights are read in a permuted order** — the anchor call takes `geom+0x100`, the
first angular call `geom+0x108`, the second `geom+0x104` — which is consistent with the axis
permutation already known from `constraint_ragdollparams_t`.

*Evidence class: read from the decompiled binary for every expression and every dumped constant;
NOT ESTABLISHED, and labelled, for the limit comparison and for the third angular axis.*

## The collision hull format, decoded — and checked against real files

**This was the last thing standing between a corpse and the floor.** A hull is Havok/Ipion's
`IVPS` compact-ledge format, carried inside a model's `.phy` and inside a map's
`LUMP_PHYSCOLLIDE`, and both readers here previously skipped the bytes.

**The way in was a raw byte scan, not a string table.** `IVPS` is not a Ghidra-defined string — it
appears as a 32-bit immediate compared inside four functions, which led straight to the deserialiser
chain `FUN_18000a100` → `FUN_18000c600` → `FUN_18000bcf0` → `FUN_18000c1c0`.

### The container, file-verified

```
0x00-0x0F  phyheader_t { int size = 16; int id; int solidCount; int32 checksum }
0x10-0x13  per-solid size prefix (uint32); the solid's data follows at +4
0x14-0x17  "VPHY"
0x18-0x19  short type      (0 normal, 1 "Null physics model")
0x1A-0x1B  reserved        (0 in every sample)
0x1C-0x1F  int32 dataSize  -- guarded by `if (param_2 < 0x30) Error("Corrupt physics model")`
0x20-0x2B  three floats    -- structure confirmed, MEANING NOT DECODED
0x2C-0x2F  0 in every sample
0x30..     IVP_Compact_Surface, then the plaintext KeyValues tail
```

### `IVP_Compact_Surface`, 0x30 bytes

| offset | field |
|---|---|
| `+0x1C` | packed: **byte size is `value >> 8`**; the low byte is unidentified |
| `+0x20` | int32 offset from the SURFACE's own base to the ledge-tree root |
| `+0x2C` | magic: `IVPS`, `SPVI` (byte-swapped), `MOPP` (a different format this reader REFUSES), or `0` (an old `.PHY`, loaded anyway) |

**The `>> 8` is verified three times over:** `barrel01` gives `0x00049cd3 >> 8` = 1180, exactly the
`VPHY` dataSize; `ladder001` gives 4628; `barrel_flatbed01` gives 2668. Each matches its own file.

### The ledge tree, and the ledge

```
IVP_Compact_Ledgetree_Node
  +0x00  offset_right_node    -- 0 means LEAF, else a byte offset to the right child
  +0x04  offset_compact_ledge -- leaf only, and usually NEGATIVE: ledges precede the tree
  +0x1C  the LEFT child, inline, after a 28-byte node header

IVP_Compact_Ledge
  +0x00  c_point_offset  -- ADD to the ledge's own address; a point array can be SHARED
  +0x04  0 in all eleven samples
  +0x08  varies; low byte 0x04, upper bytes unresolved
  +0x0C  low 16 bits = n_triangles
  +0x10  IVP_Compact_Triangle[n_triangles], sixteen bytes each
```

**`ladder001` is the specimen that proves the tree is real**: nine internal nodes, ten leaves, all
ten ledges stepping by exactly 208 bytes (16 header + 12 triangles × 16) with their
`c_point_offset`s stepping in lockstep onto one **shared** point array.

**A `-0x10` bound in the validator initially suggested the triangles start at `+0x14`, and the file
said otherwise.** The validator's scan pointer begins one word INTO triangle 0 because it only
bounds-checks the three edge words and deliberately skips the triangle's own header — so triangles
start at `+0x10`. **The bytes settled it against a plausible misreading of the code.**

### The edge, and a claim tested with a control

A triangle is a header word — which nothing in the mindist path ever reads — plus three four-byte
edges. An edge's **low 16 bits are the start point index**. Bits 16–30 are a **15-bit signed field**,
which is what the decompiled `(V * 2) >> 17` idiom sign-extends while discarding bit 31.

The two offset tables, dumped, four entries each keyed by `address & 0xC`:

```
DAT_180124fb8   { 0: 0, 4: +4, 8: +4, 12: -8 }     -- walks a triangle's three edges, 4→8→12→4
DAT_180124fc8   { 0: 0, 4: +8, 8: -4, 12: -4 }     -- used before reading the 15-bit field
```

**And here is the part that makes this a measurement rather than a story.** The `fc8` link was run
over all 132 edges of `barrel01`'s ledge:

- **132 of 132** land on another real edge **in the same ledge**.
- **132 of 132** of those targets share the **same start point index**, in a **different triangle**.
- The classic half-edge twin — same edge, direction reversed — was tested explicitly and scored
  **0 of 132**.

So it is a **vertex fan**, not a twin: it enumerates every triangle touching a given point, which is
exactly what the vertex-vertex and vertex-edge feature tests need. **The control is the 0 of 132**,
because without it "132 of 132 hit a real edge" would be satisfied by several wrong readings.

### The point array

Sixteen-byte stride at `ledge + c_point_offset`: three little-endian floats and four bytes that were
**zero in every sampled point**. Indexed by the plain 16-bit start index — confirmed in the reader
itself, `pfVar13 = (float *)((ulonglong)*param2 * 0x10 + *param4)`.

### Still open, named rather than guessed

- `IVP_Compact_Surface` `+0x00..0x1B` and `+0x24..0x2B` — real data, no consumer traced.
- Ledgetree node `+0x08..0x1B`, twenty bytes — plausibly a bounding volume, unconfirmed.
- Ledge `+0x04` (always zero) and `+0x08` (low byte constant, upper bytes unresolved).
- The triangle's own header word — never read by anything traced.
- Bit 31 of an edge, deliberately excluded from the fan delta.
- The three floats at container `+0x20`.

*Evidence class: read from the decompiled binary for every function and both dumped tables;
**file-verified** against three shipped `.phy` files for the container, the surface header, the tree,
eleven ledges, the point array and the 132-edge fan test; NOT ESTABLISHED and listed above for the
unidentified fields.*

## Correction: there is no anchor axis — all THREE constraint axes are angular

**`FUN_180036f80` was written up above as the anchor or translation solve, and that was wrong.**
It is a third rotational axis, solved by a differently shaped routine than the other two. Two
independent readings settle it:

- **Its effective mass has no linear term and no lever arm.** A ball-socket point constraint needs
  `1/mA + 1/mB + (r × n)·I⁻¹·(r × n)`. What `FUN_180037bd0` accumulates for this axis is
  `Σ r·(invI ⊙ r)` with a plain rotated vector and no cross product — the same expression it builds
  for the other two axes, and the rotational form.
- **It shares the degenerate-axis state tag.** `FUN_180038620` resets the same sentinel for all
  three geometry slots, and `FUN_180036f80` tests the same `1.4013e-45` / `2.8026e-45` pair. **A
  degenerate cross product is a meaningless idea for a 3D position constraint** and a necessary one
  for an axis direction.

So `FUN_180036e10` solves **three angular limits**, and the three flag blocks at `+0xB0`, `+0xCC`
and `+0xE8` are structurally identical — 28 bytes each, same layout, all populated by the same code
in `FUN_180037890`. The earlier "one anchor plus two angular" reading, and the worry that a twist
axis had gone missing, are both retracted: nothing was missing.

**Why two of them use a different routine:** the `+0xCC` and `+0xE8` axes build their reference
direction with a live cross product and so need the degenerate fallback, while `+0xB0`'s comes from
a quaternion at `geom+0x2D0..0x2DC` (`w·x, w·y, w·z, w²`) built once per rebuild.

**Still not established: which physical degree of freedom `+0xB0` is** — twist or one of the swings.
What was eliminated is that it is a position constraint, and that any axis is silently dropped. Only
the label is open.

## Where a joint's limits are actually compared — and it is branchless

**The bounds live at flag-block-relative `+0x4` (lower) and `+0x8` (upper)**, and there is no
"is it outside" test anywhere, which is why a search for one found nothing:

```c
auVar19._0_4_ = (fVar34 - fVar16) * fVar23;   // predicted - lower
auVar14._0_4_ = (fVar34 - fVar2 ) * fVar23;   // predicted - upper
auVar15 = minps(auVar19, zero);               // non-zero only BELOW the lower bound
auVar18 = maxps(auVar14, zero);               // non-zero only ABOVE the upper bound
fVar29 = fVar29 - (float)((uint)(auVar18._0_4_ + auVar15._0_4_) & (uint)param_2[0x18]);
```

`min(0, x − lower) + max(0, x − upper)` is zero while the angle is inside its range and is the
signed overshoot when it is outside — folded straight into the impulse with `minps`/`maxps` against
a zero vector. **The comparison was invisible because it is arithmetic rather than a branch.**

**A limit whose range covers a full turn is switched off at construction.** `FUN_180037890`:

```c
if (fVar4 <= fVar1 - fVar5) { *(undefined1 *)(param_1 + 0xb0) = 0; }   // fVar4 = 6.2831855
```

`DAT_1800eea18` dumps as **6.2831855**, which is 2π — so an axis free through 360 degrees has its
limit disabled rather than clamped against bounds it can never reach. Other constants dumped
alongside: `±0.017453292` (degrees to radians, both signs), `57.29578` (radians to degrees), `0.5`,
and `0.001`.

## The ball socket is a SEPARATE mechanism, and it is the only thing writing linear velocity

`FUN_180038070`, gated on `*(char *)(corePair + 0x157) != 0`, measures **how far apart the two
bodies' ideas of the shared pivot have drifted** — each body's rotation applied to its own local
pivot offset, subtracted — then builds a 3×3 coupling matrix from cross products of the cached joint
axes against that drift, inverts it with a Newton-refined reciprocal, and writes the correction
**directly into `core+0x140..0x148`**, bypassing the accumulated-impulse path the three angular axes
use.

**That is why no fourth positional block exists in `FUN_180036e10`: position is not solved there at
all.** It is a one-shot correction per geometry rebuild, beside the two-pass relaxation that handles
the angles.

## RESOLVED: the clamp has no gate at all, and the contradiction was a misreading

**Joint limits run unconditionally — every axis, every call.** The chain below broke at exactly one
link, and it is worth keeping because the broken link looked completely solid.

**The clamp is not inside the `if` at all.** It sits textually and causally AFTER it:

```c
fVar29 = DAT_1800ff070;                  // 0.0, dumped
if (param_7[1] != 0) {
    …                                     // a separate, earlier computation
    *(uint *)(param_7 + 0x18) = …;       // the gated block ENDS here
}
fVar16 = *(float *)(param_7 + 4);        // lower bound — read unconditionally
fVar2  = *(float *)(param_7 + 8);        // upper bound — read unconditionally
auVar15 = minps((fVar34 - fVar16) * fVar23, zero);
auVar18 = maxps((fVar34 - fVar2 ) * fVar23, zero);
fVar29 = fVar29 - ((auVar18 + auVar15) & param_2[0x18]);
```

`FUN_1800372c0` has the identical shape. **The earlier reading attributed the `if` to the wrong
block** — it wraps a position/spring correction that SEEDS the impulse accumulator, and when the
gate is false that accumulator is simply `0.0` (the constant is dumped) while the clamp still fires
and is still applied to both bodies.

**And the byte's source was misattributed too.** It is not `angularVelocity * torque`:

```c
fVar14 = (float)(**(code **)(**(longlong **)(param_1 + 0x10) + 0xe8))();   // a VIRTUAL CALL
fVar1  = (float)param_4[0x23];                                            // torque
auStack_137[lVar13 * 0x18] = fVar14 * fVar1 != 0.0;                       // the +0x1 byte
```

`angularVelocity` (`param_4[0x22]`) goes somewhere else entirely — scaled by `0.017453292`
(degrees to radians) into a different field that never reaches this boolean. **Both operands of the
supposed product were wrong.**

**What the two bytes actually do:**

- **`+0x0`** selects between two 16-byte constant tuples used as a SIMD lane and sign convention —
  not a run/skip gate. It defaults to 1 for every axis (`FUN_1800393d0` sets it unconditionally) and
  is cleared by the writer when the range covers 2π.
- **`+0x1`** gates the spring/friction seed described above, and nothing else.

**The third candidate is also ruled out.** `FUN_1800368c0` and `FUN_1800369e0` — the "driven" and
"plain" constructors — both end with the same `FUN_180037890(param_1, param_3)` on the same buffer
and both install the same vtable, so which one a ragdoll takes cannot affect the limit path.

**So `SetAxisFriction` leaving `angularVelocity` at zero has no bearing on whether a limit fires.**
It only zeroes a friction contribution that defaults cleanly to zero.

**Worth keeping as a lesson about decompiler output:** a gated block and the code after it look
identical in indentation once a decompiler has finished with them, and "this `if` wraps that
arithmetic" is a claim about BRACES that is easy to assert and easy to get backwards. The tell was
that the conclusion implied something observably false about the game — TF2's corpses do have joint
limits — and that is what sent someone back to re-read rather than transcribe.

## The contradiction as it stood before it was resolved

**The clamp block above is gated by the flag byte at `+0x1`, and that byte is reported as being set
from `(angularVelocity * torque) != 0`** (`FUN_18000eac0`). For a ragdoll,
`constraint_axislimit_t::SetAxisFriction( rmin, rmax, friction )` leaves `angularVelocity` at **zero**
and puts the number in `torque` — so the product is zero, the byte is false, and **the limit clamp
would never run for any ragdoll joint in TF2**.

**That cannot be right.** TF2's corpses visibly have working joint limits; a ragdoll without them is
a bag of disconnected parts, which is not what the game draws.

So one of these is wrong, and it is not yet known which:

- the byte at `+0x1` gates the FRICTION term rather than the limit, and the limit is gated by `+0x0`
  (the byte the 2π check clears) — which would make both readings consistent; or
- the `+0x1` source was misattributed, and it comes from somewhere other than that product; or
- `angularVelocity` is not zero for a ragdoll in practice, contrary to what `SetAxisFriction`
  implies.

**Nothing is being transcribed from this until it is settled**, because the two outcomes differ by
whether ragdoll joints have limits at all — and a solver written on the wrong one produces a corpse
that either collapses into a heap or is rigid, with no error anywhere to say which reading was
taken.

*Evidence class: read from the decompiled binary for the limit arithmetic, the three-axis
correction, the 2π disable and the drift block, with all constants dumped; INFERRED and flagged for
which DOF `+0xB0` is, and for the min/max floats' end-to-end link back to
`constraint_ragdollparams_t::axes[]`; the gating contradiction is OPEN and is the next thing to
settle.*

## The four solver constants, run to ground — and three of them are zero for a ragdoll

Four numbers were left unread when the arithmetic was transcribed, each one a term that would change
what a corpse looks like. They were chased to their writers, and the answer in three cases is that
**for a TF2 ragdoll they are zero for the constraint's entire life** — so the solve is far smaller
than the decompiled function looks.

**The per-axis `+0x1` byte is zero for every ragdoll TF2 ships, and the reason is the DATA rather
than the code.** `FUN_18000c750` clears both flag bytes at construction with one 16-bit store:

```c
*(undefined2 *)(param_1 + 0x10) = 0;
```

**An earlier pass reported that nothing writes it afterwards, and that was wrong.**
`FUN_18000eac0` writes it, once per axis, from a virtual call multiplied by the axis's torque:

```c
fVar15 = (float)(**(code **)(**(longlong **)(param_1 + 0x10) + 0xe8))();
fVar14 = (float)param_4[0x27];                            // torque
auStack_137[lVar13 * 0x18] = fVar15 * fVar14 != 0.0;      // the +0x1 byte
(&uStack_128)[lVar13 * 6]  = (uint)(fVar15 * fVar14) & DAT_1800ea5c0;   // the +0x10 field
```

So whether the branch runs is a question about `torque`, and `SetAxisFriction( rmin, rmax, friction
)` puts `friction` straight into it with the angular velocity left at zero (`constraints.h:68-74`).
That makes it answerable by measurement rather than by reading, because the number comes out of each
model's `.phy`.

**Measured over every `.phy` Team Fortress 2 ships — 4,755 files, 37 of them carrying ragdoll
joints: `0 of 1734` joint axes declare a nonzero friction.** All nine player classes read
`"xfriction" "0.000000"` on all three axes of all their joints. So the product is zero, the byte is
false, and the spring/friction branch never executes for any corpse in this game.

**Stated that way because the distinction matters for anyone reading this later.** The branch is not
dead code — Valve's own `physics_prop_ragdoll.cpp:1525` ships `SetAxisFriction( -2, 2, 20 )`, so
another game's ragdoll would take it. It is dead *for TF2's content*, which is what this project
draws, and a `.phy` from elsewhere would need the branch transcribed before it simulated correctly.

Two of the four unknowns fall out of that:

- **The `+0x10` scale** is `|virtualCall × torque|` — the same product as the gate byte, kept as a
  magnitude by `& DAT_1800ea5c0` at the writer and used through `& _DAT_1800ff0d0` (dumped as
  `0x7fffffff`) at the reader. Zero wherever the gate is zero, so unreachable for a TF2 ragdoll by
  the same measurement.
- **The `+0x18` warm-start value** is READ at the top of the function and WRITTEN only in the last
  line of the dead branch. Constructed to `0.0`, never reset per tick, never advanced. So there is
  no accumulator carried between passes or between ticks, and nothing to seed — which removes the
  "the running target is warm-started" sentence from the earlier transcription note above.

**And one of the four was answered WRONG the first time, which is why the function was re-read
rather than transcribed from notes.** The 0.4 per-pass weight was reported as reaching the axis
solve only inside the dead branch. It does not:

```c
  if (param_7[1] != 0) { … }                 // the dead branch ENDS here
  fVar24 = *(float *)(param_7 + 0xc);        // per-axis scale
  fVar2  = *(float *)(param_7 + 8);          // upper bound
  fVar16 = *(float *)(param_7 + 4);          // lower bound
  fVar23 = fVar24 * *param_8 * fVar23 * param_2[0x14];
//         └ axis scale ┘ └ 0.4 ┘  └ gain ┘  └ 1/K ┘
```

`*param_8` is the pass weight, and it multiplies the scale the overshoot is measured in — **on the
live path, outside the branch, every pass.** So the relaxation is real for a ragdoll: each of the
two sweeps applies its fraction of the limit correction rather than all of it. Reported as dead, it
would have produced corpses that snap to their limits in one step.

**The lesson is the same one this document has already recorded once**, in the section on the gating
contradiction: after a decompiler has finished, a gated block and the code following it are
indistinguishable by indentation, and "this line is inside that `if`" is a claim about braces that
is easy to make and easy to get backwards. It has now been got backwards twice on the same
function, in both directions.

**The fourth is not a constant at all: it is a binary enable.** The `+0x0` byte selects a 16-byte
tuple by address arithmetic —

```c
lVar1 = (*param_7 & 1) * 0x10;
```

— and the two tuples are **all-ones and all-zeros**. Anded against the overshoot, all-ones lets the
clamp through and all-zeros deletes it. That is the mechanism behind the 2π disable read earlier: an
axis free through a full turn has its byte cleared at construction and its clamp masked to nothing.

**This corrects the earlier description of that byte**, which called it "a SIMD lane and sign
convention". It is neither a lane select nor a sign flip; it is on/off, and the earlier wording
would have sent an implementer looking for a second code path that does not exist.

### The whole live path, for a ragdoll, in six lines

With the branch dead, what remains in both `FUN_180036f80` and `FUN_1800372c0` is short enough to
state completely:

```
rate     = ωA · Ja + ωB · Jb                        // cached by FUN_180037bd0, off core+0x130
θ        = angle ± rate · gain                      // gain = param_1[0]; sign per routine
scale    = flags[0xc] · passWeight · param_1[1] · (1/K)
overshoot= min(0, (θ − lower)·scale) + max(0, (θ − upper)·scale)
impulse  = ∓ (overshoot & enableMask)
ωA += impulse · cache[8..0xb];  ωB += impulse · cache[0xc..0xf]
```

**The two bodies get the SAME impulse with a `+`, and equal-and-opposite comes out of the cache**:
`FUN_180037bd0` builds body B's row from the negated anchor, so the sign is baked into
`cache[0xc..0xf]` rather than applied here.

**The fourth lane of angular velocity is forced to zero on every write.** `_DAT_1800ff0f0` dumps as
`{~0, ~0, ~0, 0}` and each store is anded with it, so `core+0x13c` is cleared rather than carried —
worth copying, because a transcription that keeps a W component would let junk accumulate in a lane
the engine wipes.

**The two routines have opposite sign conventions and they cancel.** `FUN_180036f80` builds
`θ = rate·gain + angle` and subtracts the overshoot; `FUN_1800372c0` builds `θ = angle − rate·gain`
and adds it. Both compute `min(0, θ−lower) + max(0, θ−upper)` from the same two fields at `+0x4`
and `+0x8`. **So the axes are the same physics measured in opposite directions**, and an
implementation that copied one routine's signs onto the other's angle would drive that joint the
wrong way — a limb that pushes itself further out of its limit the harder it is pushed in.

**Constants dumped for this pass**, all four lanes each: `1800ee970` = `0x00000000`, `1800ee980` =
`0xffffffff` (the enable pair, selected by `(*flags & 1) * 0x10` — so a cleared byte reads the ZERO
tuple and deletes the clamp), `1800ff070` = `0.0` (the vector `minps`/`maxps` compare against, and
the impulse seed), `1800ff080` = `1.0`, `1800ff0d0` = `0x7fffffff`, `1800ff0f0` = `{~0,~0,~0,0}`,
`1800ff100` = `{~0,0,0,0}`.

**Lanes 1 to 3 are multiplied by literal `0.0` throughout both functions.** The decompiler has
folded a constant tuple, and the effect is that each call solves exactly ONE axis in lane 0 while
the SIMD width goes unused. A transcription that solved four axes per call would be four times the
code for the same answer.

**Honest gap:** the branch is disabled by the shipped data, so anything that changes an axis's
friction after construction revives it. `CRagdollProp` has no such call and TF2's models declare
zero, but a `physcannon`-style game, or `physics_prop_ragdoll`'s own
`SetAxisFriction( -2, 2, 20 )`, would need the branch. It is recorded as "not transcribed because
TF2 never reaches it", which is a different and weaker claim than "unreachable".

**The other half of the gap is what makes this checkable:** the friction census lives in
`ragdoll-constraints`, runs the production `PhysicsModel` reader over the whole archive, and carries
`solid` and `ragdollconstraint` as controls — so if a future TF2 update ships a joint with friction,
re-running one probe says so instead of the corpse quietly simulating wrong.

*Evidence class: read from the decompiled binary — both solve routines decompiled in full for this
pass rather than quoted from earlier notes, with every constant dumped four lanes at a time; the
negative — that no other writer of the gate byte exists — is bounded by the search above and is
flagged as such rather than claimed exhaustively.*

## Correction: `geom+0x100/0x104/0x108` are the three JOINT ANGLES, not relaxation weights

**Recorded twice above as "each axis is handed its own scalar", read "in a permuted order" and
taken for a per-axis relaxation weight.** They are the angles. `FUN_180038620` writes all four lanes
immediately before dispatching:

```c
uVar13 = FUN_180036b80(&local_148,&local_158);          // the four-lane atan2
param_3[0x40] = … (DAT_1800ff070 - (float)uVar13) & _DAT_1800ff100 …;   // +0x100
param_3[0x41] = …;   param_3[0x42] = …;   param_3[0x43] = …;            // +0x104, +0x108, +0x10c
FUN_180036e10(param_1,param_2,(longlong)param_3);
```

and `FUN_180036e10` broadcasts one of them per axis — `+0x100` to the routine at `flags+0xb0`,
`+0x108` to `flags+0xe8`, `+0x104` to `flags+0xcc`. That is the same permutation already known from
`constraint_ragdollparams_t`, and it is the ANGLES being permuted, not weights.

**Which relocates the relaxation weight.** It is not `param_8` — that is the constraint descriptor
at `+0x2d0`, handed scaled to the twist axis and unscaled to the two swings. The weight arrives in
`param_1`, the per-sweep vector the group driver builds, and reaches the scale as `param_1[1]`:

```c
fVar23 = fVar24 * *param_8 * fVar23 * param_2[0x14];
//       └ +0xc ┘ └ +0x2d0 ┘ └ p1[1] ┘  └ 1/K ┘
```

**How the driver composes that vector from the weight and the timestep is NOT established.**

### And only ONE of the three axes is measured as an angle

The four lanes are not four copies of the atan2. Two dumped masks pick a different quantity per
lane — `_DAT_1800ff100` = `{~0,0,0,0}` and `_DAT_1800ff120` = `{0,0,~0,0}` — so:

| slot | axis solved | what it holds |
|---|---|---|
| `+0x100` | `flags+0xb0`, via `FUN_180036f80` | `0 − atan2(…)`, a true angle |
| `+0x104` | `flags+0xcc`, via `FUN_1800372c0` | `fVar17`, a dot product |
| `+0x108` | `flags+0xe8`, via `FUN_1800372c0` | `fVar21`, a different dot product |

**So the twist limit is compared in radians and the two swing limits are compared against
projections.** That is consistent with the two-routines-are-genuinely-different reading already
recorded — the difference is not only relaxation and gains, it is the QUANTITY being limited.

**What this opens, and it is not closed:** `constraint_ragdollparams_t::axes[]` carries
`minRotation`/`maxRotation` in DEGREES. The section below establishes that all six bounds reach the
constraint as RADIANS, so a dot product cannot be compared against them unless it is a SINE — which
would be true if the two vectors dotted are perpendicular at the joint's rest pose, and would make
the engine's swing limits progressively tighter than their nominal degrees (`sin 25°` = 0.42 against
0.436 rad, four per cent; `sin 79°` = 0.98 against 1.38, twenty-nine per cent).

**That is a hypothesis with an obvious shape and it has NOT been checked.** What the two dotted
vectors are — `fVar40..fVar49` in `FUN_180038620`, each a basis vector rotated by one body's frame —
is the next thing to read. **Guessing it would give two joints that clamp at the wrong deflection
with nothing in the output to say so**, which is exactly the failure this document exists to avoid.

*Evidence class: read from the decompiled binary for the write site, the dispatch order and the
per-lane selection, with both lane masks dumped; whether the two projections are sines is a NAMED
HYPOTHESIS, not a reading.*

## Chasing that open question found a whole construction path nobody has read

Pulling on "where do the swing bounds come from" produced four facts and one much better question.

**The conversion to radians is real and covers all six bounds.** `FUN_18000eac0` walks the three
axes and scales each `minRotation`/`maxRotation` by one of two dumped constants —
`DAT_1800eb764` = `0.017453292` and `DAT_1800eb770` = `-0.017453292`. The negative branch also
**swaps min with max**, which is what a negation requires if the interval is to stay ordered. So
degrees in, radians out, with a per-axis sign convention.

**`FUN_180037890` converts nothing.** It is a straight copy of the descriptor into the constraint,
which is why looking there for a conversion found none — including for the two swing axes.

**`FUN_1800393d0` is where each block's bounds are actually decided, and the three are treated
DIFFERENTLY.** It first zeroes all three blocks' `+0x4`/`+0x8` pairs and sets all three `+0x0`
enables to 1, then:

| block | becomes constraint | bounds written |
|---|---|---|
| `+0x80` | `+0xb0` (the atan2 axis) | `−hi`, `−lo` — negated AND swapped |
| `+0x98` | `+0xcc` | `range × −0.5`, `range × 0.5` — re-centred on zero |
| `+0xb0` | `+0xe8` | `lo`, `hi` — straight through |

**The negation on the first block resolves half of the puzzle.** That axis's deflection is written as
`0 − atan2(…)`, also negated, so the two negations cancel and the comparison is consistent. The
re-centring on the second is paid for elsewhere: `FUN_18003dde0(&basis, axis, midpoint)` rotates the
joint's reference frame by `(lo + hi) × 0.5` first, so bounds symmetric about zero are the same
constraint expressed in a rotated frame.

**Which of the three treatments a joint gets is chosen by counting its MOVABLE axes**, against a
threshold dumped as `DAT_1800eea1c` = `1e-16`:

```c
cVar12 = (fVar28 < local_2e8[0]) + '\x01';
if (local_2e8[1] <= fVar28) { cVar12 = fVar28 < local_2e8[0]; }
cVar6  = cVar12 + '\x01';
if (local_2e8[2] <= fVar28) { cVar6 = cVar12; }
if (cVar6 == '\0') {                                          // nothing moves at all
    *(undefined1 *)(param_1 + 0x13) = 1;
    *(undefined4 *)((longlong)param_1 + 0x9c) = 0xbdcccccd;   // -0.1
    *(undefined4 *)(param_1 + 0x14)           = 0x3dcccccd;   // +0.1
    return 1;
}
```

A TF2 ragdoll's three axes all have a nonzero range, so `cVar6` is **3** and it takes the branch
that writes all three blocks — the table above. The `±0.1` is the degenerate fallback for a joint
welded shut on every axis.

**And the fork at the top of `FUN_18000eac0` is a HINGE test, which a ragdoll fails.**
`FUN_18000cc20` opens by counting axes with `max != min` and refuses unless there is exactly one:

```c
cVar5 = (float)param_2[0x21] != (float)param_2[0x20];
if ((float)param_2[0x25] != (float)param_2[0x24]) { cVar5 = cVar5 + '\x01'; }
if ((float)param_2[0x29] != (float)param_2[0x28]) { uVar8 = 2; cVar5 = cVar5 + '\x01'; }
if (cVar5 != '\x01') { return 0; }
```

One free axis is a hinge and gets `FUN_18000e090`; three free axes is a ragdoll joint and falls
through to everything described above. So the general path IS the corpse's path.

**So the original question is answered: all six bounds a ragdoll joint carries are RADIANS**,
converted once in `FUN_18000eac0`, redistributed across the three blocks by `FUN_1800393d0` with a
negation on one and a re-centring on another, and copied unchanged by `FUN_180037890`.

### The misread that produced a dramatic wrong answer, kept because it was so cheap to make

The paragraph above originally said the threshold was **π** and concluded that a TF2 ragdoll cannot
reach this function at all — that every axis is narrower than a half turn, so the count is zero, the
limits are discarded, and the corpse must be built somewhere else entirely. That was written up,
committed, and was wrong.

**The constant was never dumped; it was assumed from its neighbour.** `DAT_1800eea18` is
`6.2831855` — genuinely 2π, and already established as the 2π disable test earlier in this document.
`DAT_1800eea1c` is the next four bytes, and reading a π-shaped comparison beside a known 2π made
"the adjacent one is π" feel like recall rather than a guess. It is `1.0e-16`, which is not a nearby
value or a plausible variant — it is a different KIND of constant, an is-it-zero epsilon.

**The tell was there and pointed the wrong way.** The conclusion contradicted the game, which is
supposed to send you back to re-read — and it did, but to re-read the *fork*, on the theory that the
subject was wrong. The subject was right; one constant in it was invented. **A conclusion that
contradicts the game means re-derive every input to it, starting with the ones that were not
measured**, rather than assuming the whole reading is aimed at the wrong function.

This document already carries `a-constant-carries-no-scope` and "dump it, do not recall it" in
several forms. This is the version where the wrong value came from an *adjacent address*, which is
the one case where the habit of dumping feels redundant.

*Evidence class: read from the decompiled binary for the conversion constants, the three bound
treatments, the movable-axis count and the hinge fork, with every constant in them dumped this time;
the joint ranges are MEASURED off the shipped `.phy` files.*
