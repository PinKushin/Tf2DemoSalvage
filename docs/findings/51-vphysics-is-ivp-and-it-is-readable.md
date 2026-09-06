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

**Mass-independence is the tell** — gravity is the one effector that must not scale with mass — and
the unit-conversion fingerprint is byte-for-byte `SetGravity`'s: same scale `DAT_18011f000`, same
sign mask `DAT_1800ea5e0`, on the same component.

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
