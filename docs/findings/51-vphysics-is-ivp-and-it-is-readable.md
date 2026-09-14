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
`0x421d7af6` = `39.3700790`, the float reciprocal of `0.0254f`, in the adjacent dword. *This line said `39.37` until the
dword was dumped on 2026-09-13; see the correction under `SetGravity` below.*

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
way**: it converts a tolerance back for its own `DevMsg` with `DAT_18011f004`. *This paragraph first called that
"the 39.37 dword — not a reciprocal of 0.0254, and the two differ by two parts in a million".* **Wrong, and killed by
dumping the bits** (2026-09-13, `tolerance_init.log`): the dword is `0x421d7af6`, `39.3700790` — exactly `1f / 0.0254f`
in float, checked by evaluating it — while `39.37f` is `0x421d7ae1`. The decompiler prints a float's short decimal, `39.37`
was taken for the value, and `IvpTransform.InchesPerMetre` carried the wrong float until then;
`IvpWorldCollision.SourceUnitsPerMetre`, written as `1f / 0.0254f`, held the right one all along.

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
  `docs/memory/two-matrix-conventions-on-purpose.md#ivp-is-a-third-convention` already records, now
  seen at the exact line that does it rather than inferred from the convention.
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

**Read again from the disassembly for the port (2026-09-13)** (`event_loop_8a110.log`), three points the text above does
not carry. **The loop runs while the minimum is under `(float)(target − base)` or either is NaN** — `COMISS` then `JNC`
to skip it, `JC` to repeat — the limit narrowed with `CVTPD2PS` at the top and `CVTSD2SS` at the bottom, which round
alike. **The minimum is captured before the unlink**, and both clocks are set from it. **`FUN_180082460` is `env+0x1a0
+= 1; env+0x188 = time`**, so every clock set is counted, the final snap to the target included. The slot unlinked is
the head element's own `+0x8`. **Ported as `IvpTimeManager`, and the fire routine as `IvpMindistFire`** with the
minimize, the scheduler and the impact handed in; the fire routine's profiler marks are not carried.

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
0x18-0x19  short           -- NOT read by the loader
0x1A-0x1B  short type      -- 0 builds, 1 "Null physics model", anything else NULL
0x1C-0x1F  int32 dataSize  -- how many bytes of surface the loader copies; checked against nothing
0x20-0x2B  three floats    -- copied into the collide at +0x10..+0x18; MEANING NOT DECODED
0x2C-0x2F  0 in every sample
0x30..     IVP_Compact_Surface, then the plaintext KeyValues tail
```

**Corrected 2026-09-12 (B404).** This block first named `0x18` the type and `0x1A` reserved, and hung the
`< 0x30` guard on `dataSize`. The disassembly of `FUN_18000a100` kills all three: the type is read with
`MOVSX ECX, word ptr [RDI + 0x6]` — the solid's `+6`, file `0x1A` — the word at `+4` is never touched,
`[RDI + 0x8]` goes straight to the copy as its length, and the `CMP R14D, 0x30` is against the size PREFIX, on
the branch for a solid with no tag. **And the word it named the type is `0x0100` in every one of the 36,917
solids TF2 ships** (the census under *What the loader does with a solid it cannot use*), so a reader acting on
that layout would have nulled every hull in the game. It survived because nothing here read either word.

### `IVP_Compact_Surface`, 0x30 bytes

| offset | field |
|---|---|
| `+0x1C` | packed: **byte size is `value >> 8`**; the low byte is unidentified |
| `+0x20` | int32 offset from the SURFACE's own base to the ledge-tree root |
| `+0x2C` | magic, read by the loader ONLY for a solid with no `VPHY` tag: `IVPS`, `SPVI` (byte-swapped) and `0` (an old `.PHY`) build; `MOPP` (Havok's own tree) and anything else is NULL |

**The `>> 8` is verified three times over:** `barrel01` gives `0x00049cd3 >> 8` = 1180, exactly the
`VPHY` dataSize; `ladder001` gives 4628; `barrel_flatbed01` gives 2668. Each matches its own file.

### What the loader does with a solid it cannot use

**Read from the decompile of `FUN_18000a100`** (2026-09-12, B403), **then settled in its disassembly**
(B404, `D:\ghidra-proj\out\solid_load_a100_c600.log`). It is `IPhysicsCollision::VCollideLoad( vcollide_t
*pOutput, int solidCount, const char *pBuffer, int size, bool swap )` (`vphysics_interface.h:265`): it writes
`solidCount & 0x7FFF` and a zero `descSize` into the two words of `vcollide_t` (`vcollide.h:16-22`), a pointer
array of collides at `+8`, and the text tail after the last solid at `+0x10`. `FUN_18000c600` is
`UnserializeCollide( pBuffer, size, index )` (`vphysics_interface.h:223`) — the same branches, in the same
order, for one solid. Per solid, with its size prefix:

| the solid | what the loader does |
|---|---|
| `VPHY` tag, type `0` | builds the collide from `+0x1C` (`FUN_18000bcf0`), **with no size check and no magic check** |
| `VPHY` tag, type `1` | `DevMsg(2, "Null physics model")`; the collide is NULL |
| `VPHY` tag, any other type | NULL, silently |
| no tag, size under `0x30` | `Error("Corrupt physics model")` — the load stops |
| no tag, magic `MOPP` | NULL |
| no tag, magic `IVPS` or `SPVI` | builds from the solid's first byte |
| no tag, magic `0` | `DevMsg(1, "Old format .PHY file loaded!!!")`, then builds |
| no tag, any other magic | NULL |

**A NULL collide keeps its slot**, so later solids keep their indices, and nothing in the loop refuses the
file. `RagdollAddSolid` then passes the NULL to `CreatePolyObject` and dereferences what comes back
(`ragdoll_shared.cpp:200-201`); *what `CreatePolyObject` does with it is not read*.

**What the disassembly adds to the table**, each read off the instruction rather than the C:

- **The type is the word at `+6`** — `MOVSX ECX, word ptr [RDI + 0x6]`, signed, so a negative word is "other".
  The word at `+4` is never read. The first account of the container above had these two swapped.
- **A tagged solid is built from `[RDI + 0x8]` bytes**, the header's data size, passed to `FUN_18000bcf0` as
  the length it allocates and copies (`FUN_180072aa0( size, 0x20 )`, then `FUN_1800e7c40( copy, source, size )`);
  the size prefix is used only to step to the next solid. An untagged solid is built from its size prefix.
- **The tagged branch then copies `+0x0C`, `+0x10` and `+0x14` into the collide at `+0x10..+0x18`**, where
  `FUN_18000bcf0` has just written `1.0f` into each — so an untagged solid keeps three ones. *What the three
  floats mean is not decoded.*
- **The loop counter goes to `FUN_18000bcf0` as its fourth argument**, which writes it into the surface copy
  at `+0x24`, the first of `IVP_Compact_Surface`'s three spare words.
- **The byte-swap follows the caller's `swap` flag, not the magic**: `SPVI` takes the same branch as `IVPS`,
  and only `FUN_18000bcf0`'s `if (swap)` reaches `FUN_18007b1f0` — *that `FUN_18007b1f0` is the swap is inferred*
  from `studiobyteswap.cpp:572` loading with `swap` true "to let ivp swap the ledge tree".
- **`DevMsg` is `[0x1800ea308]` and `Error` is `[0x1800ea310]`**, with the level in `ECX`: 2 for `"Null physics
  model"`, 1 for `"Old format .PHY file loaded!!!"`; the `Error` is followed by `INT3`.

**Does anything past the loader refuse a tagged solid whose magic is not `IVPS`?** No, by three routes.
`FUN_18000c1c0`, which `FUN_18000bcf0` ends by calling, reads the surface's packed size at `+0x1C` and asks the
collide's virtual `+0x10` for its convexes — `FUN_18000baf0`, a two-instruction thunk to the ledge-tree walk
`FUN_18007d260` on the surface — and validates each ledge's point indices against the size (`Error("vphysics:
Invalid collide map")`); none of the three reads `+0x2C`. And a search of every decompiled function for the
`MOPP` immediate, `0x50504f4d`, found exactly `FUN_18000a100` and `FUN_18000c600` in 2811 with none failing —
the two loaders being the control that the search can see the constant at all. The `IVPS` immediate had already
turned up only in those two and the two builders that write it (`FUN_180008dd0`, `FUN_180009b30`).
*Evidence class: disassembly for every branch, operand and constant of `FUN_18000a100`, `FUN_18000c600`,
`FUN_18000c1c0` and `FUN_18000baf0` above; decompile for what `FUN_18000bcf0` writes and for `FUN_18007d260`.*

**`PhysicsHull` follows this table since B404.** `PhysicsHull.Load` names the branch; `Read` and
`MassProperties` answer nothing for a NULL, read a tagged surface from `+0x1C` for its data size whatever its
magic, and read an untagged one from its first byte. `PhysicsModel.Read` refuses a file carrying a corrupt
solid with `InvalidDataException`, and the map's collision reader stops before the model carrying one — a map's
solids are `VCollideLoad`'s input too, which Valve's own lump swapper shows (`bsplib.cpp:1681`). *That
`engine.dll` loads `LUMP_PHYSCOLLIDE` through `VCollideLoad` is not read.* Two departures, both D32: a tagged
solid too short for its own header is NULL where the loader reads past it, and a data size larger than the
solid is cut to the solid.

**Which rows shipped content takes: the first, every time.** The `phy-solids` probe (2026-09-12) walked the
4,755 `.phy` files in `tf2_misc_dir.vpk`, holding 5,338 solids, and the collision lumps of all 234 installed
maps, holding 31,579. On both populations alike:

| field | every shipped solid |
|---|---|
| tag | `VPHY` — no solid is untagged |
| word at `+4`, unread | `0x0100` |
| type at `+6` | `0` |
| data size at `+8` | the size prefix less `0x1C` |
| surface magic, unread on this branch | `IVPS` — **the control** that the offsets beside it are right |
| `PhysicsHull.Load` | `Collide`, with mass properties and at least one ledge |

The walk's own controls hold: `PhysicsModel` walked the same solid count in all 4,755 files and refused none,
and the maps' models declare exactly the 31,579 solids found. **So the table's other seven rows are for content
TF2 did not compile** — an old `.phy`, a stranger's map — and are pinned by synthetic bytes alone. *Evidence
class: measured, on this install.*

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

**What "not a twin" did and did not test** (2026-09-12, B369). The measured hop is `fc8` FIRST — to the
triangle's previous edge, which ENDS at the start point — and the 15-bit field of THAT edge second. A field
holding each edge's classic twin predicts exactly the 132 of 132 (the twin of an edge ending at `P` starts at
`P`, in the neighbouring triangle) and the 0 of 132 (it is not the reverse of the edge started from). So the
measurement rules out the field being the twin of the edge the walk started on, not the twin of the edge it
is stored on; *whether it is the latter is not established*. The vertex-face search does not need the answer
— it takes the far end of the edge it lands on, `start(next(hop))`, as its neighbour of `P` — and
`PhysicsLedge.EdgeOffsets` now carries the field as stored, `(int)(word << 1) >> 17` per edge, so the walk is
the engine's address arithmetic rather than a reconstruction.

### The ledgetree node's twenty unidentified bytes are a TIGHT bounding sphere

**Filed above as "plausibly a bounding volume, unconfirmed", and the files settled it without a
decompiler.** The node's centre is at `+0x08` as three floats and its radius at `+0x14`, which makes
the 28-byte node header `offset_right_node`, `offset_compact_ledge`, centre, radius and four bytes
still unaccounted for.

Every point of every leaf ledge on `koth_harvest_final` — 41 solids, 3,030 ledges, 24,050 points —
measured against its own node's sphere:

| reading | points inside | outside | worst overshoot |
|---|---|---|---|
| centre `+0x08`, radius `+0x14` | 17,063 | 6,987 | **0.000008** |
| the same, shifted one float | 70 | 23,980 | **447.9985** |

**The 6,987 "outside" are the finding, not a failure.** A worst overshoot of eight millionths of a
metre across twenty-four thousand points means the points lie exactly ON the sphere and fall either
side of float rounding — so it is the MINIMAL enclosing sphere, not a loose bound. A bound with any
slack would have put every point strictly inside.

**The shifted control is what makes this a measurement.** Reading the centre one float late gives
overshoots of 448 metres, so "the numbers look plausible" was never available as an explanation.

*Evidence class: measured, over one full map, with a deliberate wrong-offset control. NOT
ESTABLISHED: the four bytes at `+0x18`, and whether internal (non-leaf) nodes carry a sphere
enclosing their whole subtree — only leaves were tested, because only leaves have a ledge to test
against.*

### What a corpse still falls through, and everything ruled out

**One of eight corpses on `z1800` at tick 14270 free-falls where the other seven settle**, and the
cause is a hole in this project's physics world rather than in the simulation. What has been
eliminated, each by measurement:

| ruled out | how |
|---|---|
| terrain is incomplete | 533 of 533 displacements built; per-displacement triangle count is `(2^power)² × 2`, which is Valve's own `GetTriSize()` (`dispcoll_common.h:192`) — 496 power-2 and 37 power-3 account for all 20,608 |
| the world's hull is partly read | model 0 declares **2** solids and both read: 2,983 ledges, 35,400 triangles, none empty |
| the ledge-tree walk stops early | depth budget raised 64 → 4096, identical 3,030 ledges |
| brush entities are missing | all 39 accounted for by class — 12 `func_brush`, 8 `func_door`, and the rest triggers and visualizers; including them changes nothing at the spot |
| the corpse rests on a static prop | nearest static prop is 119 units away |
| the corpse starts inside geometry | `Penetration` at the point says outside everything |

**What is left is that the camera's own sweep is stopped at `z ≈ 33` where the physics world holds
nothing**, and the drawn scene there is a building's wooden interior floor.

**A census gives it a denominator — and a confound.** Dropping a ray at each of 1,089 grid nodes
around that spot: **777 found ground in both worlds, 312 in the camera's alone, 0 in the physics
world alone.** The physics world is a strict SUBSET of the camera's, which is the shape of a missing
category rather than a stray hole. **But the camera's test is leaf-contents based, and every leaf
outside the map is `CONTENTS_SOLID`** — so a drop that misses real ground and leaves the map is
counted as a camera hit. How much of the 312 is that is NOT established, and it is the next thing to
measure.

*Evidence class: measured, with the camera's independently-built world as the control and its own
confound stated.*

### Damping, read — and it is what the gravity note said was missing

**`IvpGravity`'s own remarks named two unread calls and one of them is the damping.**
`FUN_180074c80` makes `FUN_180078250(core, dt)` and `FUN_180077950(core)` before adding `g·dt`,
inside the same `0x10` gate. The first fetches the terms and the second is still unread.

```c
void FUN_180078250(longlong core, double dt)
{
  if (1 < *(byte *)(core + 1)) {
    local_18 = *(float *)(core + 0x30) + DAT_1800ea968;   // 0.1
    local_10 = *(float *)(core + 0x38) + DAT_1800ea968;
    local_14 = *(float *)(core + 0x34) + DAT_1800ea968;
    FUN_180077a20(core, dt, &local_18, (double)(*(float *)(core + 0x50) + DAT_1800ea968));
    return;
  }
  FUN_180077a20(core, dt, (float *)(core + 0x30), (double)*(float *)(core + 0x50));
}
```

So **`core+0x30/0x34/0x38` is a three-axis rotation damping and `core+0x50` a single speed
damping**, which is exactly the pair a `.phy` spells as `rotdamping` and `damping`.

`FUN_180077a20` applies them:

| what | when | factor |
|---|---|---|
| angular, per axis | `Σ (rot·dt)² ≥ 0.5` | `exp(−rot·dt)` |
| angular, per axis | below that | `1 − rot·dt` |
| linear | `speed·dt ≥ 0.25` | `exp(−speed·dt)` |
| linear | below that | `1 − speed·dt` |

writing the first into `core+0x130/0x134/0x138` and the second into `core+0x140/0x144/0x148`.

**Which of those is angular is read from the other side rather than assumed**: gravity accumulates
into `+0x140`, and an acceleration is added to a linear velocity.

**Every constant settled in the disassembly**, per
`docs/memory/nothing-is-closed.md#settle-a-constant-in-the-disassembly`: `DAT_1800ea984` = `0.5`,
`DAT_1800ea988` = `1.0`, `DAT_1800efdf8` = `0.25` (double), `DAT_1800ea9b8` = `1.0` (double), and
both XOR masks are sign bits — which is what makes those calls `exp(−x)` rather than `exp(x)`.

**Still open:** the byte at `core+1`. When it is 2 or more, every damping term gains `0.1` — the
same `0.1` as `g_PhysDefaultObjectParams`, which is suggestive and is not evidence. Nothing traced
writes it. `FUN_180077950(core)` remains unread.

*Evidence class: read from the decompiled binary, with every constant re-read in the disassembly;
the angular/linear split is differential against the gravity site.*

### The PSI's phases, and the lookahead used as a DISTANCE GATE

**`FUN_180082560` was read as a call list; here it is as phases.** The `(**...)(profiler, N)` calls
number the stages, so the structure is not guesswork:

| phase | call | what |
|---|---|---|
| 1 | the `env+0x158[]` reverse loop, slot 0 per entry | the controller list — gravity is one of these |
| 2 | `FUN_180075a90(env+0x10, psi, buffer)` | collect into a 0x80-entry buffer |
| 3 | `FUN_18009a590(psi, buffer1, buffer2)` | buffer 1 in, buffer 2 out |
| 4 | `FUN_18009a690(psi, buffer2)` | the contact-pair re-check scheduler |
| 5 | `FUN_1800983e0(env+0x20)` | walk the mindist list, `FUN_180095cb0` per record |
| 6 | `FUN_1800985a0(env+0x20)` | walk it again, `FUN_180099380(record, 1, 1)` per record |

**`FUN_180099380` is where the lookahead is spent, and it settles what the parameter MEANS:**

```c
local_a8 = (double)(*(float *)(lVar3 + 0x254) + *(float *)(lVar2 + 0x254));
local_90 = (double)*(float *)(lVar2 + 0x1dc) + local_a8 + (double)*(float *)(lVar3 + 0x1dc);
…
dVar5  = (double)*(float *)(&DAT_18012d548 + (ulonglong)(byte)(uVar6 >> 0x16) * 4);   // margin
dVar13 = (double)*(float *)(param_1 + 0xa8);                                          // distance
if ((double)(float)*(double *)(lVar4 + 0x108) * local_90 * _DAT_1800f5108 + dVar5 < dVar13) {
    … reschedule …
} else {
    … escalate: FUN_180098dd0 … 
}
```

So a pair is EXAMINED when its distance falls below `lookAheadTime × (a per-core speed bound summed
over both) + the material margin`. **The lookahead is a gate on when to look, not a force applied
early** — which is what `lookAheadTimeObjectsVsWorld = 1.0f` means in practice, and it is why
transcribing the prediction as an impulse stopped a falling body dead sixty-six units above a floor.

`lVar4+0x108` is the environment's own lookahead time; the margin table `DAT_18012d548` is the
256-entry per-material float already noted above, whose base `DAT_18012d664` dumps as `0.0`.

**`FUN_180098dd0` was called the escalation above and it is not one — it is a REMOVAL.** Read, it
unlinks a mindist from four doubly-linked lists and from an array at `manager+0x20` whose count sits
at `+0x1a`; there is no impulse anywhere in it. And the branch it sits in is the FAR one, not the
near one: the test reads `if (lookAheadTime × speedBound + margin < distance)`, so the pair taken
out of the list is the pair too far apart to matter, and what follows installs a travel allowance
per object (`FUN_180097bd0`, split between the two by their speed bounds) saying how far either may
move before the pair must be looked at again.

So both halves of that sentence were wrong: the deactivation was read as an escalation, and the
point where a contact becomes an impulse is not on this path at all. It is in the next section.

*Evidence class: read from the decompiled binary; the phase numbering is read from the profiler
argument rather than inferred. The correction to `FUN_180098dd0` is read from the function itself.*

### The NEAR branch of `FUN_180099380`, and why a pair never penetrates (B369)

**The section above read the far branch. The other branch is the answer to "how does TF2 keep a limb
above zero-thickness terrain", and it is conservative advancement per pair.** Read 2026-09-12 while
chasing corpses with limbs eighty units under the ground:

1. **The actual closing speed**, not the bound: the stored contact normal at `mindist+0xb0..0xb8`
   dotted with both cores' linear velocities (`core+0x140..0x148`), plus each core's angular term
   (`core+0x1c0..0x1c8` against the normal, scaled by `core+0x254`). If it is below two small
   thresholds the pair is left alone.
2. **Can it close the gap before this PSI ends?** `(env+0x190 − env+0x188) × closingSpeed + margin <=
   distance` returns: it cannot touch this step.
3. **Otherwise an exact time of impact**, through a dispatch table indexed by the two synapses'
   feature kinds (`DAT_18012d910`, the kinds at `+0x5a` in each 0x38-byte synapse record). When it
   finds an impact inside the PSI, the pair is inserted into the time manager at that moment
   (`FUN_1800aaed0`); the time-ordered event loop then resolves it before anything moves past it.
4. **No impact found:** the next check is scheduled at `(distance − ε) / speedBound` from now, with a
   small floor, so the pair is looked at again no later than the earliest moment it could meet.

**The minimize step recovers from a backside.** `FUN_180095cb0` walks a second feature-pair table
(`DAT_18012d4b0`), up to two retries. A result of `3` where a synapse's kind is `5` replaces that synapse's
feature with a triangle `FUN_180094e30` walks to, sets the kind to `2`, and retries. **An earlier draft of this
paragraph called that a hull descent, "replacing that node with its child"; it is not.** Kind `5` marks a point
found BEHIND a face, and `FUN_180094e30` starts from the face's opposite triangle and walks the ledge toward the
point — read in full in *The minimize, routine by routine* below.

**Ours is a fixed step with speculative contacts and a push after penetration.** That is the
structural divergence under B369: IVP never needs `TerrainDepth`, because no pair is ever allowed
past the moment it could touch.

**The constants, dumped.** Most are `float`s widened to `double`, which is why their low 32 bits are
zero:

| address | value | role in `FUN_180099380` |
|---|---|---|
| `1800f5108` | 2.1 | the far gate's factor on `step × speedBound` |
| `1800f4f20` | ≈1e-19 | closing-speed floor |
| `1800eb140` | 1e-6 (float) | an impact this close to now is taken as now |
| `1800f4f28` | 1e-12 | distance floor before dividing by the bound |
| `1800fd578` | 0.1 | recheck scale on `distance / speedBound` |
| `1800f5100`, `1800fdf68`, `1800fdf60`, `1800f50f8` | 0.001, 1e-5, 1e-7, 1e-4 | time-floor factors on the two recheck paths |
| `1800ea938` | 1e-10 (float) | added to each speed bound |
| `1800ea968` | 0.1, 0.2 (floats) | the travel-allowance split between the two objects |
| `1800fdf78` | 1.001 | `sqrt(1.001 − x²)` in the angular term |

**And five that CANNOT be read from the file.** `18012d540`, the margin table `18012d548`,
`18012d654`, `18012d664` and `18012d670` — with both dispatch tables, `DAT_18012d910` and
`DAT_18012d4b0` — all read **0 on disk**, and all sit in `.data`, filled at startup. A zero there is
the value before initialisation, not the value the solver uses. **This document already recorded
`DAT_18012d664` as "dumps as 0.0" above, and that is the same trap:** it is not established. The
writers are to be read before any of these is used as a number.

**Both dispatch tables, from their initialisers**, each indexed `kindA × 4 + kindB` over the two
synapses' feature kinds 0–3:

| table | filled by | default | set entries |
|---|---|---|---|
| minimize, `DAT_18012d4b0` | `FUN_180094700` | `FUN_180094e10` | `(0,0) (0,1) (0,2) (1,1) (2,2)` → `FUN_180094c70`; `(1,0) (2,0)` → `FUN_180094f70`; `(0,3)` → `94c50`, `(1,3)` → `94c30`, `(2,3)` → `94c10` (six-instruction assertions); row 3 → `FUN_180094ad0`, `(3,3)` → `FUN_180094860` — read slot by slot from the initializer's disassembly, 2026-09-12. **`FUN_180094c70` builds both sides as the time-of-impact dispatch does and routes again**: `(0,0)` → `FUN_1800b1b80`, `(0,1)` → `FUN_1800b1aa0`, `(0,2)` → `FUN_1800b1910`, `(1,1)` → `FUN_1800afa40`, anything else — `(2,2)` — → `FUN_180094f80` (696 instructions); all unread |
| time of impact, `DAT_18012d910` | `FUN_1800a3aa0` | `FUN_1800a4200` | `(0,0) (0,1) (0,2) (1,1)` → `FUN_1800a3fe0`; `(3,0) (3,1) (3,2)` → `FUN_1800a3d30`; `(3,3)` → `FUN_1800a3b60` |

**Only eight of sixteen kind pairs are LEGAL at event time, and the default is not "no event" — it
is a fault.** `FUN_1800a4200` is `Error("IVP Failed at %s %d", "...ivp_mindist_event.cxx", 0x4ea)`
followed by a breakpoint trap. An earlier draft of this paragraph said the other combinations "raise
no impact event"; they cannot occur, and reaching one stops the process.

`FUN_1800a3fe0`, the entry for the polyhedral kinds, resolves each synapse's ledge and routes again:

| kinds | routine |
|---|---|
| (0,0) | `FUN_1800a2b30` |
| (0,1) | `FUN_1800a1ff0` |
| (0,2) | `FUN_1800a1b50` |
| (1,1) | `FUN_1800a1420` |
| anything else | the same assertion, at lines `0x4cf`, `0x4da`, `0x4df` |

**Read again for the port, instruction by instruction (2026-09-13), with the ball entries beside it**
(`scheduler_near_99380.log`, `toi_table_others.log`). `FUN_1800a3fe0` writes `0` to the context's event kind before it
routes, and releases both cache objects' reference counts (`+0xc4`) after. **The table does not reorder a pair** — an
edge against a point is `FUN_1800a4200` — so synapse A must already hold the lower kind, which the minimize's flips
arrange. **A point against a backside indexes `0·4 + 5`, the `(1,1)` entry**, and faults inside `FUN_1800a3fe0` at
line 1231 rather than at the table's 1258; a backside first indexes past the table's eighteen filled slots. Neither can
occur, because the minimize resolves a backside before it returns.

**The ball routines read synapse records 0 and 1 directly**, not the ones the flags select. `FUN_1800a3b60` (ball-ball)
sets `time := end` and runs `FUN_1800b6210` over the point-point evaluator with both points at their objects' origins
and `u` the difference of the two cache objects' translations (`+0xa0`) scaled with five steps, to `(double)extra +
(double)margin`, tolerance `(double)(0.5f·extra + 0.1·d)`, no known distance: `0x10`, and nothing more. `FUN_1800a3d30`
(ball first) routes the ledge synapse's kind: `0` to `FUN_1800a0fc0`, `1` to `FUN_1800a0930`, both unread, and `2`
inline — the point-plane evaluator from the ball's centre to the face, **handed the known distance `(double)(extra +
length)`** as the vertex-face search is, raising `0x20` — and **it never sets `time := end`**, which only the scheduler's
own reading of the time could expose; it reads the time only when a kind was written. Anything else is line 1165.

These are the four routines this document named earlier as the narrow phase. **The legal set has no
(1,2) and no (2,2)** — which is exactly the minimal closest-feature pair set of the V-Clip method
*if* kinds 0, 1 and 2 are vertex, edge and face. The absent pairs are what V-Clip reduces to the
others. That reading gains real support from the table's shape; nothing in the binary names the
kinds, so it remains INFERRED.

**The (0,2) routine, `FUN_1800a1b50`, is a root-find over the step, not a sweep** — and (0,2) is the
case a corpse's hull vertex arriving at a terrain triangle takes:

1. Each object's motion over the PSI is built (`FUN_1800a0800` on both cores); the vertex comes from
   the ledge's point array, the face's plane from `FUN_18007b940`, normalised.
2. **The generic root finder `FUN_1800b6210`** is handed a point-to-plane evaluator (vtable
   `1800fe720`) and asked for the moment the distance reaches **the material margin plus the pair's
   extra radius** (`mindist+0x98`), to a tolerance of `DAT_1800ea984 × extra + ε`, between the
   interval's start (`mindist+0x30`) and end (`mindist+0x38`). A root is **event `0x20`**, a collision,
   written at `mindist+0x48`.
3. **Then the vertex's ring of edges is walked** through the half-edge offset tables. Any edge closing
   on the plane faster than the remaining interval allows gets the refining finder `FUN_1800b6590` with
   an edge evaluator (vtable `1800fe750`) and a target built from the margin, `0.1 × extra` and
   `DAT_18012d664 ×` a core term. A root there is **event `0x21`**: the closest feature leaving the
   vertex for an edge inside the step, which is the tracking half of a closest-feature pair.

`DAT_1800ea9b8` dumps as `1.0` exactly, so `DAT_18012d670`, set from it at startup, is `1.0`; and
`DAT_1800ea984` is `0.5`, so the (0,2) tolerance is `0.5 × extra + ε`.

**The root finder `FUN_1800b6210` is conservative advancement on a discrete time lattice.** Its
evaluator is an object whose slot 0 returns the pair's distance for two given transforms (point-plane
`1800fe720` = {`a3470`, `a3a30`, `a31e0`}; edge `1800fe750` = {`a3660`, `a32b0`, `a33e0`}) and whose
`[1]` holds the pair's maximum approach speed:

1. The distance at the interval's start, or the one passed in. **If it is already above the target, the
   whole search is handed to `FUN_1800b6590` at once.**
2. Otherwise — the pair starts inside the target — a step of `(distance − tolerance) × (1.0 /
   evaluator+0x08)`; if `(float)(t − end)` plus the step is past zero it becomes `(float)(end − t) + 1e-8`,
   and otherwise it is held at zero or above (`MAXSD` against zero, which also turns NaN into zero).
3. **Quantised to whole lattice ticks**, `(int)(step × 200.0)` truncated, at least one; the clock
   advances by `(double)((float)ticks × 0.005f)`, and a running tick total indexes the motion caches.
4. Both objects' transforms at that index, taken from the cache or computed there with
   `FUN_1800734e0` and kept, and the distance evaluated.
5. **A distance above the target** hands `(t, distance, tick total)` to `FUN_1800b6590` for the rest of the
   interval, unless `t` is already past the end. **A distance at or below the one at the START** is an
   event at the PREVIOUS lattice time, returned as 1. Anything between — inside the target but farther
   than at the start — carries on until `(float)(t − end)` reaches zero, which is no event.

**Corrected from the disassembly:** this list first read as a search for the distance falling to the
target. The routine marches only while the pair starts inside the target, compares each distance with
the one at the start rather than the previous one, and delegates any approach from outside to the
refining finder.

**Each object's motion cache is built by `FUN_1800a0800`**: 21 transform slots. When the OBJECT's
movement-state byte at `object+0x78` is 8 or more, every slot points at the matrix at `+0x40` of the
structure the cache is built from — the body is not moving, *inferred* from that shortcut — and
otherwise slot 0 alone points there and the rest are null, filled on first use. **A slot is keyed by its
tick index only**: whatever time first fills index `n` is what every later lookup of `n` in the same
search gets, and a refinement handed the tick total carries on in the same caches. **Corrected from the
disassembly:** this paragraph first put the state byte on the core; the cache reaches it through
`+0xc8`, which is the object (its `+0xe8` is the core `FUN_1800734e0` reads).

**`FUN_1800734e0` composes the object into its core** after the core's own transform at `t`: unless
flag `0x800` at `object+0x78`, the translation becomes the float offset at `object+0x60` put through that
matrix (`FUN_180070b20`, the same grouping as `FUN_180070bc0`); then, if `object+0x58` is set, the
rotation is rebuilt from the interpolated quaternion times the one it points at (`FUN_180070d60`, a
Hamilton product `a ⊗ b` in doubles). **This project treated a body and its core as one thing**, which
drops both — and the next section shows that premise is false for any hull whose mass center is not its
origin (B403). **The `+0x58` half never runs for an object `FUN_180073df0` makes**: its object-from-core
rotation is built from an all-zero quaternion, which is the identity, so `FUN_180074380` always frees and
nulls that pointer (step 3 below). The translation half is ported in `IvpMotionCache.Fresh`.

**An IVP object's core sits at the hull's mass center, and its inertia is the hull's, per axis.** Read
from the disassembly of the object initializer `FUN_180073df0` (in `ivp_object.cxx`) and what it calls:

1. The mass center comes from the surface manager at `object+0xc8`, virtual `+8`, unless the template's
   `+0x78` points at an override, whose doubles at `+0x60..+0x70` are narrowed instead.
2. An object-from-core matrix is built with an all-zero quaternion — which `FUN_180071330` turns into the
   identity rotation — and that mass center, widened, as its translation.
3. That matrix goes to the object's virtual `+0x10`, `FUN_180074380`: it inverts it (`FUN_180070290`, the
   rigid inverse, so the translation is `−massCenter`); if the squared length of that translation, `(x² +
   y²) + z²`, is below `DAT_1800fcf98` = `1e-16` it SETS bit `0x800` (`BTS ECX, 0xb`) and zeroes the offset,
   otherwise it CLEARS the bit (`BTR`) and stores the translation narrowed to float at `object+0x60`; and if
   the rotation's diagonal is exactly `1.0` three times (`UCOMISD`) it frees and nulls `object+0x58`, else
   allocates the quaternion there.
4. The core's inertia at `core+0x20/+0x24/+0x28`: when the template's `+0x28` is set, a polygon object
   asks the surface manager's virtual `+0x18` for the hull's rotational inertia (a ball uses `0.4 · r²` on
   all three), multiplies each by the template's factor at `+0x30/+0x34/+0x38` in float, widens, multiplies
   by the mass in double and narrows; otherwise the template's three values are taken as they are. The
   mass is the template's `+0x20`, replaced by `1.0` below `1e-8`. A nonzero template `+0x40` floors every
   axis at `FUN_18006e120` of the three times that factor.
5. `FUN_1800790a0` places the core: `core+0x90 := core+0x90 · objectFromCore`, the position copied out of
   that matrix and the orientation converted from it into both `+0x180` and `+0x1a0`.

**What vphysics puts in the template, `FUN_18001c9d0`**, from `objectparams_t` (whose field offsets match
`vphysics_interface.h:1062` exactly): mass `MINSS(MAXSS(mass, 0.1f), 50000f)` widened to `+0x20`; `1` at
`+0x28`, so the three inertia values are factors on the hull's; `inertia` kept only when `COMISS` finds it
above zero (otherwise `1.0f`), then `MINSS` against `1e18f`, written to all of `+0x30/+0x34/+0x38`;
`rotInertiaLimit` copied raw to `+0x40`; `damping` widened to `+0x48`; `rotdamping` widened to `+0x50`,
`+0x58` and `+0x60`. `FUN_18006e120`, whose result the floor multiplies, is a LENGTH — `sqrt` of the float
sum of squares, widened — not a largest component.

**Which hull bytes the surface manager returns.** `CreatePolyObject` (`FUN_18001b340`) asks the collide
object's virtual `+8` (`FUN_18000b750`) for the manager, which allocates sixteen bytes: vtable `1800eae60`,
then the `IVP_Compact_Surface` pointer. Its slots, from raw disassembly:

| slot | reads | so |
|---|---|---|
| `+8` | `surface+0x00, +0x04, +0x08` copied out | the mass center |
| `+0x10` | `surface+0x00..0x08` against a given center, then `+0x18` and the byte at `+0x1C` | a radius and deviation |
| `+0x18` | `surface+0x0C, +0x10, +0x14` copied out | the rotation inertia |

With the byte size read as `surface+0x1C >> 8` by `FUN_18000c1c0`, the 0x20 bytes before the ledge-tree
offset are accounted for: mass center, rotation inertia, radius, and a deviation byte beside the size. **All
of them are in IVP's own object frame and units** — metres, IVP axes — as the ledge points are.

*Evidence class: read from the disassembly for `FUN_180073df0`, `FUN_180074380`, `FUN_18001c9d0`,
`FUN_18006e120` and the surface manager's slots; from the decompiler for the shapes of `FUN_1800790a0`,
`FUN_180070290`, `FUN_18001b340` and `FUN_18000b750`.*

**The joint's twist axis.** `FUN_1800393d0` chooses which of a ragdoll joint's three axes is the twist by a
score, read from its disassembly (`1800395f6..180039ae5`):

1. Both objects' matrices, offset composed (`FUN_180032740`); each anchor put through its own
   (`FUN_180033720`), less its CORE's position narrowed to float (`core+0xf0..+0x100`, `CVTPD2PS` then
   `SUBSS`) — so each arm is measured from the mass center.
2. For each axis `i`, the reference frame's column `i` and the attached frame's, each put into the world
   through its object's matrix (`FUN_18003ec30`).
3. A purely angular constraint row per body, the world axis taken back into that core's frame, handed to
   `FUN_18003d320`, the general `J · M⁻¹ · Jᵀ` accumulator: its angular lanes are weighted by the core's
   inverse inertia at `+0x40/+0x44/+0x48` and its linear lanes by the inverse mass at `+0x4c`. Both calls add
   into the same element.
4. `score = |r_A × a_A|² · invMass_A`, then `+` that element, then `+ |r_B × a_B|² · invMass_B`, all in
   float.
5. The best starts at `DAT_1800ea9f8` = `−1.0`; `COMISS` then `CMOVBE` keeps the previous index unless the
   score is strictly higher, and `MAXSS` carries the best.

**This project scored `|anchor × a|` for the attached side alone, unsquared, from the bone, by raw mass**
(B306's rule). That picks the same axis as the engine only while the reference anchor sits at its core and
the inertia is the same on every axis — both of which B403 undid. Ported as `RagdollSimulation.Turning`, in
each body's frame: a rotation changes neither a cross product's length nor a vector's components in its own
frame, so only the last bits of the engine's round trip through the world can differ.

*Evidence class: read from the disassembly for the score, the start value and the comparison; that the
accumulated element is `Σ a_k² · invInertia_k` for a purely angular row is read from `FUN_18003d320`'s lanes
and the row's zeroed linear part.*

**The lattice is 200 ticks a second.** `DAT_1800feb78` is `200.0` and `DAT_1800fd748` is `0.005`
(float), and the refinement gives up at **20 ticks** — a tenth of a second, which is why the motion
cache has 21 slots, the start and twenty.

**A transform at time `t`**, `FUN_1800734e0`, is linear in position and interpolated in rotation:

```
position(t) = core+0x150 + core+0x170..0x178 × (t − core+0x1d0)
rotation(t) = interpolate(core+0x180, core+0x1a0, (t − core+0x1d0) × core+0x1d8)
```

then composed with the object's offset inside its core (`object+0x60`, unless flag `0x800`) and an
optional further transform (`object+0x58`).

**`core+0x1d8` is the inverse PSI step, from its three writers.** A whole-program search of the
decompiled `vphysics.dll` found it: the integrator `FUN_180099a00` sets `core+0x1d8 = param_2[1]` beside
`dt = param_2[0]`, immediately before stamping `core+0x1d0` with the environment clock, advancing
position by the committed velocity and committing `0x180 := 0x1a0`; and the two reset routines,
`FUN_180078bd0` (which zeroes every velocity) and `FUN_180077670`, set it from `env+0x110` and from a
parameter respectively, each with `0x1a0 := 0x180`. **So `rotation(t)` turns from the committed
orientation to the predicted one by the fraction of the current step elapsed** — `0` at the stamp, `1`
a whole step later. The integrator also sets `core+0x1dc = |linear velocity|`, the linear speed bound
the pair scheduler sums.

**The search itself needed a control, and got one.** Its first run printed nothing and its second
printed only a usage line: a `)` inside the needle does not survive `analyzeHeadless.bat`, which
dropped the arguments entirely. Only because the second run also searched for `0x1d0` — which three
functions are already known to touch — was the silence recognisable as a broken instrument rather
than an absent writer. The working run found 24 functions for the control and 17 for `0x1d8`.

**The integrator's `param_2` is computed per step, not copied.** The island driver `FUN_1800909d0`
builds it for every awake core:

```c
fVar9 = (float)(env[0x190] - env[0x188]);                 // dt, the island's nominal step
local_864 = (dt <= DAT_1800fcfa0) ? 1e10 : (float)(1.0 / dt);
FUN_180099a00(core, {fVar9, local_864}, ...);             // core+0x1d8 := local_864
```

So the inverse is taken from the step actually being run, with a guard against a vanishing one — which
matters here, because this project sub-steps and its slice varies. **`DAT_1800fcfa0` is `1e-10`** (a
float widened). **The second caller, `FUN_18009a590`** (phase 3, three call sites), applies the same
rule to `dt = (float)env+0x108`, the environment's PSI step: `inverse = dt ≤ 1e-10 ? 1e10 :
(float)(1.0 / dt)`. Both callers agree, so that is the rule.

**And the sleep reset's copy is unguarded.** `FUN_180078bd0` takes `core+0x1d8` from `env+0x110`, and
`CPhysicsEnvironment::SetSimulationTimestep` — slot 37 of the table at `1800ebbd8`, `1800152f0` —
forwards to `FUN_180082470`:

```c
env[0x108] = step;                        // double, from the float argument
env[0x110] = 1.0 / step;                  // no guard
env[0x1b0] = FUN_1800d3cf0(step * DAT_1800fd4e0);   // not needed here; unread
```

So a core that goes to sleep gets `(float)(1.0 / step)`, while a core the integrator steps gets the
guarded `dt ≤ 1e-10 ? 1e10 : (float)(1.0 / dt)` — the same number for any real step, and different
only for a vanishing one. **Two controls pin the slot arithmetic:** slot 34 of that table is
`FUN_180015310`, this document's `Simulate`, and slot 36 is exactly `return (float)env[0x108]`, the
getter. An earlier dump of "slot 34" landed on slot 22 through an address slip, which is why the
control was run twice.

**The reset that copies the predicted orientation back has three callers, and one is sleep.**
`FUN_180078bd0` — velocities zeroed, `core+0x1d8 := env+0x110`, `0x1a0 := 0x180` — is called from:

| caller | when |
|---|---|
| `FUN_180078c90` | every object of the core gets `+0x78 = 8` and its synapses are re-timed |
| `FUN_180079300` | the core's static bit, `& 2`, is set |
| `FUN_1800791a0` | the core's last object is removed, while its state is below 8 |

*That `FUN_180078c90` is putting a core to SLEEP is INFERRED*, from two readings that agree: it sets
state 8, and the time-of-impact motion cache treats an object state above 7 as not moving.

**Which exposes a divergence, not yet observable.** Sleeping a core leaves `rotation(t)` pinned to the
committed orientation, because both ends of the interpolation are now equal. This project's sleep
(`RagdollSimulation`'s `Asleep`) zeroes the velocities and leaves `WorkingOrientation` one step ahead of
`Orientation`. Nothing evaluates a body inside a step yet, so nothing shows it; the moment the
time-of-impact search does, a sleeping corpse would turn toward a predicted orientation it will never
reach.

**The reset's fields, mapped onto `IvpRigidBody` by offsets this project already cites:**

| engine | field | this project's sleep |
|---|---|---|
| `+0x110..0x118` | `PendingAngularVelocity` | **left alone** |
| `+0x120..0x128` | `PendingVelocity` | **left alone** |
| `+0x130..0x138` | `AngularVelocity` | zeroed |
| `+0x140..0x148` | `Velocity` | zeroed |
| `+0x170..0x178` | `PreviousVelocity` | zeroed |
| `+0x1d8` | the inverse step, from `env+0x110` | not carried |
| `0x1a0 := 0x180` | `WorkingOrientation := Orientation` | **not done** |

**So the same reset holds a second divergence:** the engine discards velocity STAGED for the next step,
and ours keeps it, so a push staged just before a corpse sleeps lands on the first step after it wakes.
Not mapped, because nothing here names them: `+0x80`, `+0x1dc` (written by the integrator as
`|linear velocity|`), `+0x254`, `+0x1c0..0x1c8` (reset to `1.0, 0, 0`) and the state byte at `+1`.

*Evidence class: read from the decompiled binary for all three writers and the island driver; that
`env+0x110` is the inverse step is checked against this document's own reading of the environment.*

**The refining finder `FUN_1800b6590`**, as the disassembly has it (`RCX` the evaluator, `XMM1` the
target, `R8` the start, `R9` the end, then on the stack the tick total so far, both motion caches, an
optional known distance and the out time):

1. A distance at or under the target: the event is at the start, and it returns 1.
2. If `(float)(start − end)` plus `(distance − target) × evaluator+0x10` is already past zero, there is
   no event.
3. Otherwise march. **Each step is TWICE `(distance − target) × evaluator+0x10`, recomputed from the
   current distance** — not a step that keeps doubling — replaced by `(float)(end − t) + 1e-8` when it
   would overshoot, times `200.0`, truncated to whole ticks, at least one. The time advances by
   `(double)((float)ticks × 0.005f)` and the tick total indexes the motion caches. A distance still
   above the target at a tick total of exactly 20 gives up, and so does a next step past the end.
4. Once a lattice point is at or under the target, **regula falsi** between the last time above
   (`t_a`, `d_a`) and the first at or under (`t_b`, `d_b`):
   `t = ((double)(float)(t_b − t_a) · (target − d_a)) / (d_b − d_a) + t_a`. On every pass where
   `iteration & 3 == 3` that point is replaced by `((double)(float)(t_a − t) + (double)(float)(t_b − t)) ·
   0.375 + t`, and on those passes alone the cap is checked: past 64 it stops with `t_a`, the last time
   still above the target — so the cap bites at iteration 67. Both transforms at `t` are computed fresh,
   not from the caches. It stops when `|distance − target| < 1e-8`; otherwise a distance under the target
   replaces `(t_b, d_b)` and any other replaces `(t_a, d_a)`.
5. A final time past the end is no event; otherwise it is written out and the routine returns 1.

**Corrected from the disassembly:** this list first said the step doubled every iteration and gave up
after 64 iterations. The step is re-derived from each distance and doubled once; the cap is checked only
every fourth pass, and what it returns is the last time above the target, not the latest estimate.
**`1e-8` is an absolute distance in IVP's metres**, so a port running in inches carries it scaled.

**The evaluators are signed distances.** Point-plane `FUN_1800a3470` transforms the vertex by object
A's transform and the plane's point and normal by object B's, and returns
`dot(vertex − planePoint, normal)`. Edge `FUN_1800a3660` takes the face normal stored in B's frame,
rotates it into the world with B's matrix (`FUN_1800709f0`) and back out into **A's** frame with the
transpose of A's (`FUN_1800706c0`), and dots that with a unit edge direction stored in A's frame.
**Corrected from the disassembly:** this line first said the direction was transformed "into B's frame"
and dotted with a stored normal, which has the stored and the transformed vectors the wrong way round.

**The four routines under the point-plane evaluator**, all in doubles:

- **`FUN_180071330` fills a 4×4's rotation from a quaternion** `(x, y, z, w)`:
  `m0 = 1 − (z·2z + y·2y)`, `m1 = x·2y − w·2z`, `m2 = w·2y + x·2z`; `m4 = w·2z + x·2y`,
  `m5 = 1 − (z·2z + x·2x)`, `m6 = y·2z − w·2x`; `m8 = x·2z − w·2y`, `m9 = w·2x + y·2z`,
  `m10 = 1 − (y·2y + x·2x)`. **This is the fill `IvpQuaternion.Rotate`'s own remarks record as unread**
  — that method reaches the same rotation by a vector formula, so the difference is rounding, and the
  gap can now be closed. Closing it touches every constraint and contact that rotates through it, so it
  is a change of its own, measured separately.
- **`FUN_180070bc0` puts a local point in the world**, `out[i] = ((p.x·m[i,0] + p.y·m[i,1]) + p.z·m[i,2]) + t[i]`.
  **Corrected from the disassembly.** This line first read `p.z·m[i,2] + p.x·m[i,0] + p.y·m[i,1] + t[i]`,
  "summed in that order", taken from the decompiled C — and `IvpMatrix.ToWorld` was ported exactly so.
  The instructions `ADDSD` the `x` and `y` products together first and add the `z` product to that. The
  two groupings differ in the last bit for ordinary inputs: `(0.1 + 0.2) + 2.2` is exactly `2.5`, and
  `(2.2 + 0.1) + 0.2` is `2.5000000000000004`. This is the decompiler rule in
  `docs/DECOMPILING.md` biting a third time, and the conformance test that pins it was red against the
  committed port before the fix.
- **`FUN_18007b940` builds a face's normal** as `cross(B − A, C − A)` in doubles from the ledge's float
  vertices, not scaled to unit length: `A` is the face's own point, `B` and `C` are reached through the half-edge
  offset tables `DAT_180124fb8` and `DAT_180124fc8`.
- **`FUN_18006e080` normalizes only a vector long enough to have a direction**: if `|n|² ≥ DAT_1800f4f20`
  (≈`1e-19`) it scales by `FUN_18006ecf0(|n|²)` and returns true; otherwise it leaves the vector alone
  and returns false.
- **`FUN_18006ecf0` is a reciprocal square root in doubles, four Newton steps from a bit-built guess.**
  The guess takes the argument's high word and sets `((0x7ff00000 − hi) >> 1) + 0x1ff00000`, with the low
  word from `1.0`; then four times `r = r · ((0.5 − r² · ½x) + 1.0)`, which is the textbook
  `r · (1.5 − ½x · r²)` in the order the instructions compute it.

**Which three vertices `FUN_18007b940` takes.** `A` is the start point of the edge it is given; `B` is
the start of the edge `DAT_180124fb8` reaches, which the table above shows is the NEXT edge of the same
triangle (4→8→12→4); `C` is the start of the edge `DAT_180124fc8` reaches — and its offsets (`4: +8`,
`8: −4`, `12: −4`) from a triangle's own edge words land on the PREVIOUS edge of that triangle. So the
normal is `cross(P₁ − P₀, P₂ − P₀)` over the triangle's three start points in their own order: the
triangle's stored winding. **That does not contradict the vertex-fan measurement above**, which follows
`fc8` and then the 15-bit field — a second hop; `FUN_18007b940` takes only the first.

**The point-plane evaluator, from the disassembly.** `FUN_1800a1b50` fills it on its stack: the vtable
`1800fe720` at `+0x00`; the pair's approach speed from `mindist+0x10` at `+0x08` and `1.0` over it at
`+0x10`; the vertex at `+0x28`, widened from body A's ledge points; the face normal at `+0x48`, written by
`FUN_18007b940` and passed to `FUN_18006e080` **whose return value is never read**; and the face's own
first point at `+0x68`, widened from body B's. `FUN_1800a3470` (`RDX` A's matrix, `R8` B's) then returns

```
v = FUN_180070bc0(A, +0x28)                         -- a call
p[i] = ((m[i,0]·p.x + m[i,1]·p.y) + m[i,2]·p.z) + t[i]    -- inlined, B
n[i] = (n.y·m[i,1] + n.x·m[i,0]) + n.z·m[i,2]            -- inlined, B, no translation
distance = ((v.y − p.y)·n.y + (v.x − p.x)·n.x) + (v.z − p.z)·n.z
```

Every sum there groups the `x` and `y` terms before `z`, so one rotation routine and one dot serve all
three bit for bit. **`FUN_18007b940` widens each float point before subtracting** (`CVTPS2PD`, then
`SUBSD`) and crosses in the textbook component order; **`FUN_18006e080` sums `(x² + y²) + z²`** and
branches on `COMISD`/`JNC`, so NaN takes the no-direction path. **`FUN_18006ecf0`'s steps are
`r · ((0.5 − (r·r)·(x·0.5)) + 1.0)`**, and replicating them from the instructions leaves `1/√3` at
`0.5773502691896244` against the converged `…258` — a value no library square root reproduces, and the one
`IvpVectorConformanceTests` pins. Ported as `IvpVector`, `IvpMatrix.Rotate` and `IvpPointPlaneEvaluator`.

*Evidence class: read from the disassembly for all five routines and the fill; the bit values are
arithmetic, replicating the instruction sequence.*

**The edge evaluator, from the disassembly.** For each edge of the vertex's ring, `FUN_1800a1b50` fills a
second evaluator: vtable `1800fe750`; at `+0x08` the two motion caches' objects' `+0x80` floats added in
float, widened, plus `1e-19`, and `1.0` over that at `+0x10`; the face normal copied to `+0x48`; and — only
for an edge that passes the slope check below — a unit direction at `+0x28`. `FUN_1800a3660` (`RDX` A's
matrix, `R8` B's) returns

```
w     = FUN_1800709f0(B, +0x48)     -- B·n; each row (x·m0 + y·m1) + z·m2
u     = FUN_1800706c0(A, w)         -- Aᵀ·w; each column (x·m0 + y·m4) + z·m8
value = (u.x·dir.x + u.y·dir.y) + u.z·dir.z
```

`FUN_1800709f0` is the same rotation the point-plane evaluator inlines, so `IvpMatrix.Rotate` is both;
`FUN_1800706c0` is its transpose, `IvpMatrix.RotateInverse`. **The direction is built unlike the face
normal:** `d = Q − P` is subtracted in FLOAT (`SUBSS`) and only then widened; `s = (d.x² + d.y²) + d.z²` in
double is narrowed with `CVTPD2PS` and handed to `FUN_18006edb0`, which is nothing but
`(float)FUN_18006ecf0((double)s)`; that float is widened and multiplied in. On every integer from 2 to
5000 the float route rounds to the same bits as a correctly rounded `1/√s`, so what a test can pin is the
narrowing itself — the edge `(3, 4, 0)` gets `0.6000000089406967`, not `0.6`. Ported as `IvpEdgeEvaluator`.

**What the same routine does around it, left for the vertex-face step.** `P` is the vertex; `Q` is the start
of the next edge after hopping the 15-bit twin field and then `DAT_180124fb8`; the walk steps back with
`DAT_180124fc8` and stops on returning to the starting edge. Before filling, the slope against a normal
taken into A's frame with the objects' CURRENT matrices at `object+0x40` is compared, `COMISD`/`JNC`,
against `(double)(float)(eventTime − intervalStart) × (speed sum + 1e-19)`; only a smaller slope is refined,
to a target of `((min(mindist+0xa8, DAT_18012d548[material]) + 0.1f·mindist+0x98) · −(DAT_18012d664 · f)) /
DAT_18012d548[material]`, where `f` is the float at `+0x54` behind the face side's `[+0x18]+0xe8`.
`DAT_1800ea968` dumps as `0.1f` and `DAT_1800ea5e0` is the float sign mask; the table and `DAT_18012d664`
read zero on disk and are runtime-initialized, so their values are not established here.

*Evidence class: read from the disassembly for `FUN_1800a3660`, `FUN_1800709f0`, `FUN_1800706c0`,
`FUN_18006edb0` and the whole of `FUN_1800a1b50`; the float-route comparison is arithmetic, by exhaustive
replication over 2–5000.*

### `FUN_1800a1b50` field by field, read again for the port (2026-09-12)

**The routine's first argument is not the mindist, and three paragraphs above say it is.** Re-read from the
disassembly (`D:\ghidra-proj\out\toi_a1b50_disasm.log`): `RCX` is a search context whose `+0x10` is the pair's
approach speed, `+0x20` a pointer to the mindist, `+0x30`/`+0x38` the interval's start and end, `+0x40` the event
kind and `+0x48` the event time. **The extra radius `+0x98`, the length `+0xa8` and the flags `+0x20` are the
MINDIST's**, reached through `[RCX+0x20]`. The other arguments: `RDX` the vertex's edge in ledge A, `R8` the face's
edge in ledge B, `R9` side A — its point array at `+0`, its cache object at `+0x10` — and on the stack side B,
with the same two fields plus its compact ledge at `+8` and its real object at `+0x18`.

In order, with `time` the context's `+0x48`:

1. Both motion caches built; **`time := end`**.
2. The point-plane evaluator: speed `ctx+0x10` and `1.0 / speed`; the vertex, the face's first point, and its
   normal from the face edge and the triangle's next and previous edges.
3. `tolerance = (double)(0.5f·extra + DAT_18012d540)`, `target = (double)margin + (double)extra` with `margin =
   DAT_18012d548[(mindist+0x20 >> 22) & 0xFF]`, and a KNOWN starting distance `(double)(extra + length)`, all float
   sums widened. `FUN_1800b6210` gets these and `time`; on a root, **`kind := 0x20`**.
4. The edge evaluator's speed: `(double)(coreA+0x80 + coreB+0x80)` summed in float, plus `1e-19`; and `1.0 /
   speed`. Its target: `((double)MINSS(length, margin) + (double)(0.1f·extra)) × (double)(−(DAT_18012d664 ·
   coreB+0x54)) / (double)margin`.
5. `u = A.RotateInverse(B.Rotate(normal))` through the two cache objects' CURRENT matrices at `+0x40`, and the
   slope limit `(double)(float)(time − start) × speed` — taken once, after step 3, so an `0x20` root shortens it.
6. **The ring** (`IvpLedgeTopology.Ring`): for each edge leaving the vertex, `d = Q − P` in float, `slope =
   ((d.x·u.x + d.y·u.y) + d.z·u.z) × (double)rsqrt_f((float)|d|²)`; unless `slope ≥ limit` (`COMISD`/`JNC`, so NaN
   is refined), the edge evaluator is filled with the scaled direction and `FUN_1800b6590` runs from `start` to
   `time`, handed the slope as its known distance; on a root, **`kind := 0x21`** and `time` moves earlier for every
   edge after.

**The three runtime globals are the collision-tolerance block**, already mapped above: `DAT_18012d540` is
`block[0] = 0.1·d`, `DAT_18012d548[i]` is `block[2 + i]` — the flat ramp, `d` for `i` from 0 to 63 — and
`DAT_18012d664` is `block[0x49] = 0.1·d`. *What sets the mindist's byte at bits 22–29 is not read*; past 63 it
would index the block's later fields.

**The two core fields**, from their writers:

- **`core+0x80` is an angular speed bound**, written by `FUN_180099d60(core, v)`, which the integrator calls
  every step: `x = |v|` by a reciprocal square root of **five** Newton steps — one more than `FUN_18006ecf0`,
  from the same guess; an earlier draft of this line said four — (zero, with axis `(1, 0, 0)`, when `|v|² ≤
  1e-19`), the unit axis into `core+0x1c0..0x1c8`, and `core+0x80 = (float)((2x + x³/3) + 2·0.40414·x⁵) ×
  core+0x1d8`, the inverse step; `core+0x254 = core+0x80 × core+0x8`. **`v` is the vector part of the step's
  rotation quaternion**: `FUN_180099fc0`, already ported as `IvpIntegrator.Rotate`, writes it to the stack slot
  the call passes, so `|v|` is `sin(θ/2)` and the series bounds the step's angle — INFERRED from the arithmetic.
  Ported as `IvpCoreSpeedBound.From`.
- **`core+0x54 = 0.5f / core+0x4`**, written by `FUN_180076f80` beside the inverse inertia. **`core+0x4` and
  `core+0x8` are set once, by `FUN_180078b90`**, from `FUN_180073df0`: the surface manager's slot `+0x10`
  (`18007aeb0`) returns `radius = (float)((double)surface+0x18 + dist)` and `deviation = (float)((double)(byte
  surface+0x1C × 0.004f × surface+0x18) + dist)`, `dist` being how far the surface's mass center is from the
  centre asked about, and the object's float at `+0xe0` is added to the radius. *That `+0x18` is the ledge's upper
  radius is INFERRED* from that use; `0.004f` is `DAT_1800fd1fc`, dumped.

**Ported as `IvpVertexFaceSearch.Search`**, over `IvpLedgeTopology`, the two evaluators and `IvpRootFinder`.
One parity point the tests pin that is easy to lose: **the point-plane search trusts the mindist's length**. It
is handed `extra + length` as its starting distance and never measures slot 0, so a vertex whose mindist says it
is an inch away is searched from an inch, wherever the vertex actually is.

*Evidence class: read from the disassembly for `FUN_1800a1b50`, `FUN_180099d60`, `FUN_180076f80`,
`FUN_180078b90`, the call in `FUN_180073df0` and `18007aeb0`; constants dumped.*

### The scheduler's near branch and the dispatch into the search, instruction by instruction (2026-09-12)

**Read from the disassembly of `FUN_180099380(mindist, removeFar, recheckMode)` and `FUN_1800a3fe0`**
(`D:\ghidra-proj\out\scheduler_near_99380.log`), which replace the decompiler-level summary above in
*The NEAR branch of `FUN_180099380`*.

**The two synapses.** The mindist's flags at `+0x20` pick them: synapse `A` is record `(flags >> 8) & 3` and `B` is
`((flags ^ 0x100) >> 8) & 3` — bit 8 flipped — each a `0x38`-byte record from `mindist+0x48` whose `+0x48` is its real
object (the core is the object's `+0xe8`), whose `+0x50` is its feature pointer into a compact ledge, and whose word at
`+0x5a` is its feature kind.

**The search context is built on the scheduler's stack**, and it is the struct `FUN_1800a1b50` receives:

| offset | holds |
|---|---|
| `+0x00` | `(double)(coreB+0x254 + coreA+0x254)`, the surface bounds summed in float |
| `+0x08` | the linear closing speed, `(double)(n·vB) − (double)(n·vA)`, `n` the mindist's normal at `+0xb0`, `v` each core's `+0x140` |
| `+0x10` | the closing speed: `(√(1.001 − (n·axisB)²)·coreB+0x254 + √(1.001 − (n·axisA)²)·coreA+0x254) + linear`, the axes at `+0x1c0` |
| `+0x18` | the total bound, `((double)coreA+0x1dc + surface sum) + (double)coreB+0x1dc` |
| `+0x20` | the mindist |
| `+0x28` | the environment |
| `+0x30`, `+0x38` | `env+0x188` (now) and `env+0x190` (the next PSI) |
| `+0x40`, `+0x48` | the event kind and time the search writes |

Each dot sums its `y` and `x` terms before `z`, in float.

**The branch, in order:**

1. A mindist still queued (`+0x8 ≠ 0xffff`) is taken out of the time manager (`FUN_180089f90`) and marked `0xffff`.
2. **Far** when `length > (double)(float)env+0x108 × totalBound × 2.1 + margin`; the travel-allowance path read earlier.
3. **Near:** left alone when the closing speed is under `1e-19` or NaN — the test against `block[0x45]` (the
   closing-speed threshold) only decides whether the `1e-19` test is made, and a speed between the two goes on.
4. Left alone when `length ≥ (double)(float)(end − now) × closing + margin`: it cannot touch this PSI.
5. Left alone when `(flags & 0x3000) == 0x1000`.
6. **The margin class decays.** If the class byte at bits 22–29 is not zero, `env+0x13c` is incremented, and when its
   old value was over 2 the class is decremented and the counter zeroed — a pair drops one margin class every fourth
   examination.
7. **The time of impact** through `DAT_18012d910[kind(B) + kind(A)·4]`, handed the context.
8. No kind written: done. Otherwise, **an event within `1e-6` of now** (`(float)(time − now)`, `COMISS`/`JNC`) is
   handled by `recheckMode`: `0` puts the event at now; any other mode replaces the time with a recheck — `(length −
   ε)` against a floor of `1e-12`, and then `now + (length − ε)·0.1 / totalBound + 1e-7·step` for mode 1 on an event
   kind whose low four bits are zero, `now + (length − ε) / totalBound + 1e-4·step` for mode 2 or any other kind, and
   `now + 1e-5·step` or `now + 0.001·step` respectively under the floor — each `step` being `(double)(float)env+0x108`
   times `DAT_18012d670` (`1.0`). A recheck at or past the next PSI is dropped.
9. **The event goes into the time manager** at `(float)(time − tm+0x28)` (`FUN_1800aaed0`), its slot stored at
   `mindist+0x8` and the kind in the flags' low byte.

**`FUN_1800a3fe0` builds the two sides and routes by kind.** For each synapse: the cache object (`FUN_180094680`,
reference-counted and released at the end), the real object, and the compact ledge found FROM THE FEATURE POINTER — the
feature's triangle is `pointer − (pointer & 0xF)`, and **the triangle's header word's low twelve bits are its index in
the ledge**, so `triangle − (index + 1)·16` is the ledge and `ledge + [ledge]` its points. Then `(0,0)` → `FUN_1800a2b30`,
`(0,1)` → `FUN_1800a1ff0`, `(0,2)` → `FUN_1800a1b50`, `(1,1)` → `FUN_1800a1420`, with synapse `A`'s feature as the first
argument — **so in `(0,2)` the vertex is synapse `A`'s** — and anything else the assertion at `0x4cf`, `0x4da` or `0x4df`.

**Corrected by this:** `PhysicsHull` says a triangle's header word *"is skipped and never read… nothing in the traced
mindist path reads it either"*. The dispatch reads it on every search. *What else the header's upper twenty bits hold is
not read.*

*Evidence class: read from the disassembly; constants read beside their instructions (`2.1`, `1.001`, `1e-6`, `1e-12`,
`0.1`, `1e-7`, `1e-5`, `1e-4`, `0.001`, all floats widened except `1e-12`).*

### When a queued mindist fires

**Its fire routine is `FUN_1800992e0(mindist, env)`**, slot 1 of both mindist vtables found — the plain one whose
table has `FUN_180096250` before it and `FUN_18008ecb0` eight slots on, and the recursive one at `1800fe960` —
read from the disassembly (`D:\ghidra-proj\out\mindist_fire_992e0.log`), between two profiler marks (`8`, `0xe`):

1. **`FUN_180095cb0(mindist)` first** — the minimize, which walks the feature-pair table `DAT_18012d4b0` and
   descends a hull node into its triangles — so the length, normal and features are recomputed at the event's
   time before anything is decided.
2. Flags `& 0xc000` set: nothing more.
3. **An event kind whose low four bits are set** — `0x21`, the edge event — is rescheduled at once,
   `FUN_180099380(mindist, 0, 1)`.
4. **Otherwise the collision test:** `DAT_18012d664 + margin[class]` — `0.1·d + d` — against the new length. **Over
   the length, the mindist's virtual `+0x40` runs** (`FUN_18008ecb0` in the plain table, `FUN_1800b2460` in the
   recursive one); at or under it, or NaN, the pair is rescheduled with `FUN_180099380(mindist, 0, 2)`.

**So an impact happens only when a vertex-face event's re-minimized length is inside `1.1·d`**, and a feature
change never collides directly — it re-minimizes and re-queues. `FUN_18008ecb0` calls `FUN_18008ef60` between
bookkeeping on both objects and cores; *that it is the impact solver is INFERRED from where it sits, and it is unread*.

*Evidence class: read from the disassembly for `FUN_1800992e0` and the call list of `FUN_18008ecb0`; the vtable
slots from a table scan.*

### The event queue, and where the far branch hands a pair off (2026-09-13)

**Read from the disassembly** (`time_manager.log`, `scheduler_callees.log`), for the port of `FUN_180099380`.

**The queue is a min-list of `0x18`-byte entries** (`FUN_1800aaed0` adds, `FUN_1800ab1b0` removes): a capacity word at
`+0x0`, a free-list head at `+0x2`, the entries at `+0x8`, the minimum at `+0x10`, a long-list head at `+0x14`, the head
at `+0x18` and a count at `+0x1c`; each entry a long-list next and previous at `+0x0`/`+0x2` (`0xfffe` for an entry the
long list skips), a next and previous at `+0x4`/`+0x6`, a float value at `+0x8` and the element at `+0x10`.

- **An add at or under the minimum becomes the head** (`COMISS`/`JA`, so a NaN does too, and so does a tie). Any other
  walks the long list and then the short one while the value is over each entry's, and goes in before the first it is
  not over: **a new event goes before every queued event of equal time**. The long list only shortens the walk —
  rebalanced once a walk passes three hops — and cannot change where an entry lands.
- **A free slot is reused last-freed-first**; with none free, the list grows to `min(2·capacity + 1, 0xfffc)` and the new
  entry takes index `capacity`, the rest chained in order. *The constructor, and so the first capacity, is not read.*
- **A remove unlinks in place**; removing the head makes the next entry's value the minimum, or `0x501502f9` —
  `1e10f` — when the list empties. **An add over `1e10f` to an empty list walks from index `0xffff`**, past the entries;
  a queue of event times never holds one.

**The far branch, past its threshold test**, when `removeFar` is set: `FUN_180098dd0` takes the mindist out of the
time manager if queued, out of the environment's exact-mindist list (`+0xc8`/`+0xd0` links, head at the manager's
`+0x10`), out of both synapse records' object lists (`+0x38`/`+0x40` and `+0x70`/`+0x78`, heads at each object's
`+0x40`), and swap-removes it from the manager's array (`+0x20`, count `+0x1a`). Then, with `gap = (float)(length −
margin)` and records 0 and 1 taken **directly, not through the flags**:

- record 0's object's byte `+0x78 & 7` zero: flags `&= ~0x280000`, `|= 0x140000`, and `FUN_180097c40` for record 0 with
  `0` and for record 1 with `gap`, the mindist's `+0xa0` being their sum;
- record 1's zero instead: `FUN_180097bd0(mindist, gap, 0)`, which does the same;
- neither: `s = core+0x254 + core+0x1dc + 1e-10f` per record, and `FUN_180097bd0(mindist, gap·(0.1·s₁ + s₀)/Σ,
  gap·(0.1·s₀ + s₁)/Σ)`, `Σ` the two shares summed — all in float.

**`FUN_180097c40(record, allowance)` files a synapse in its OBJECT's own min-list at `+0xa0`**, keyed `(float)((double)((float)(now −
object+0x80) · object+0x88 + object+0x90) + allowance)`, stores the slot at the record's `+0x8`, and returns
`(double)((object+0x88 − object+0x8c)·t + (object+0x90 − object+0x94))`. **That is IVP's hull manager**, a subsystem of
its own: *what writes those object fields, and what fires when an object's list comes due, is not read.*

**The fire routine, `FUN_1800992e0`, read again whole**: profiler marks `8` and `0xe` through the environment's `+0x50`
object's slot 1 around it; the minimize; flags `& 0xc000` ends it; a kind with low bits set reschedules with
`FUN_180099380(mindist, 0, 1)`; otherwise `(float)(0.1·d + margin)` over the length (`COMISS`/`JBE`, so a NaN
reschedules) calls the mindist's virtual `+0x40`, and anything else reschedules with mode `2`.

**The near branch's constants are floats widened, read beside their instructions**: `2.1f`, `1.001f`, `0.1f`, `1e-7f`,
`1e-5f`, `1e-4f`, `0.001f` and the `1e-6f` a float compares against; `1e-12` and `1e-19` are doubles; `DAT_18012d670`
is `1.0`, set at runtime beside the tolerance block. **Carried in inches, `1e-12` (a gap) and `1e-19` (a closing speed)
are metres and are converted**; the rest are times, ratios or already inches.

*Evidence class: read from the disassembly for all six routines. Not established: the min-list's constructor, the hull
manager beyond the two routines named, and what the object byte `+0x78`'s low three bits mean — the motion cache
reads `≥ 8` as not moving, which says nothing about `& 7`.*

**What the fire routine's collision call does, read to its first layer of callees (2026-09-13)** (`impact_8ef60.log`,
`impact_callees.log`), because it is the next thing under the ported scheduler and it turns out to be a subsystem, not a
routine:

- **`FUN_18008ecb0`, the plain mindist's `+0x40`**: wakes both synapse objects (`FUN_180074360` — state `8` only, through
  `FUN_1800758e0`), counts itself in the environment arena's user count (`env+0xf8`, `+0x20`; the arena is reset by
  `FUN_180072970` when the count returns to zero), revives each core whose state byte `+0x1` is under `8` and whose
  flags lack `0x10` (`FUN_180078d60`: a `+0x260` record from the arena, the rotation slerped and filled to now through
  `FUN_180071060` and `FUN_180071330`, the position advanced `+0x150 + (float)(now − +0x1d0)·+0x170`, and — unless flags
  `& 8` — the angular velocity rebuilt from the step's rotation through `FUN_1800d392c` three times, scaled by `2·+0x1d8`),
  increments `env+0x1a4`, and calls `FUN_18008ef60(mindist, object 0, object 1)`.
- **`FUN_18008ef60`**: finds or builds the pair's friction system (`FUN_180090e50`, which merges systems, allocates
  `0x90`/`0x18` records, and hands islands between cores' `+0x1f8`), finds the pair's record in it by both cores
  (`FUN_1800850b0`), stamps the record's time and the event's float `dt`, calls the environment's collision listeners
  flagged `8` (`FUN_180082170`, slot 0, a list at `env+0x148`, count `+0x142`) and each object's listeners flagged `8`
  when its flags hold `0x2000` (`FUN_180088800`), computes a float from both cores' angular velocities `+0x130`, the
  material at `+0x70` and `DAT_18012d544` (`FUN_18008fca0`), runs the impact through `FUN_18008ed60` — the entry to the
  solver `IvpContact` already carries — negates the pair's normal for a core flagged `2` around **`FUN_180090700`, which
  adds the pair to the friction system and loops its solve up to `0x1388` times** before tailing into `FUN_1800909d0`,
  restores the normal, and calls the same listeners flagged `1` (slot 1: `FUN_180082110`, `FUN_1800886c0`).

**None of that is ported, and it is named here so the gap is honest**: `IvpContact` was ported from the decompiled solver
before any of this was read, and the friction system around it — creation, merging, the solve loop, the listeners — has
no counterpart. *`FUN_18008fca0`'s float being an impact strength is INFERRED from its inputs; `FUN_1800d392c` is taken
to be an inverse trigonometric helper from its use, not from a name.*

### The hull manager, and how a far pair is told to look again (2026-09-13)

**Read from the disassembly** (`hull_update.log`, `hull_helpers.log`, `hull_psi.log`, `hull_core.log`, `hull_gradient.log`,
`hull_far.log`, `hull_thunks.log`, `hull_ctor*.log`, `anomaly*.log`, `perf_settings.log`), for the port of the far branch.
**A far pair is not rechecked on a clock: each of its two synapse records is filed in its object's hull manager, keyed
at a hull value, and told when the object's hull passes it.**

**The manager, at `object+0x80`**, zeroed by `FUN_1800943d0`: a double time at `+0x0`; floats for the gradient `+0x8`,
the center gradient `+0xc`, the value `+0x10`, the center value `+0x14` and the next PSI's value `+0x18`; an int reset
time at `+0x1c`; and a min-list at `+0x20` built with capacity `8`. **`FUN_1800aae10`, the min-list constructor**, takes
`min(capacity, 0xfffc)`, chains the entries free in index order, heads both lists at `0xffff` and sets the minimum to
`1e10f` — so the time manager's slot numbering, carried as a list that grows from empty, was right.

- **Each PSI, per core** (`FUN_18009a590` → `FUN_180099a00`, cores and then objects walked last first): after the
  integrator, `FUN_180099d60` rebuilds the angular bound from the step's rotation `r` — over `|r|² > 1e-19`, a five-step
  inverse square root gives `a = |r|` and the bound `(2a + a³·(1/3) + 2·0.40414·a⁵)·(1/dt)`, whose `a⁵` coefficient makes
  `a = 1` give `3.14161`, at least π; the surface bound `+0x254` is that times `core+0x8`, and the axis `+0x1c0` is the
  core's rotation rows dotted with `r/|r|`; under the floor the axis is `(1, 0, 0)` and both bounds zero. **The gradient is
  `(surface bound + linear speed)·1.00001f`**, and each object's manager then takes `dt = (float)(now − time)`, moves the
  center value along the old center gradient and the value along the old gradient, takes the linear speed as the center
  gradient, the time as now and the new gradient, and projects `next = gradient·step + value` — all in float. **A manager
  whose list minimum less `next` is under zero (`COMISS`/`JNC`, so a NaN too) is pushed.**
- **`FUN_18009a690` walks the pushed managers from the last to the first.** Each tells its head synapse's listener slot 1,
  handing it the minimum less `next`, while that is under zero; then, when `(double)` its reset time is under its time
  (`COMISD`/`JNC`), it rebases and sets the reset to `(int)(time + 10.0)`, truncated.
- **The telling is budgeted** by the environment's anomaly limits `env+0x48`, int `+0x18`: counted down per telling, and
  once under zero the environment's anomaly manager `env+0x40`, slot `+0x20`, is asked with the checks done so far and its
  answer added back; still under zero, the pass stops, and otherwise the checks done become `done + 1 + remaining`.
  **For TF2's client the budget is 250 and the extension zero**: vphysics's environment constructor `FUN_1800114f0` calls
  its `SetPerformanceSettings`, `FUN_180015200`, with `physics_performanceparams_t::Defaults()` (`6, 250, 2000, 3600, 1,
  0.5, 10, 2500`, `vphysics/performance.h:30`), which writes `maxCollisionChecksPerTimestep` to `+0x18` — beside
  `maxCollisionsPerObjectPerTimestep` at `+0x10`, `0.0254·maxVelocity` at `+0xc`, `maxAngularVelocity·0.017453292f·psi` at
  `+0x14` and both friction masses clamped to `[1, 50000]` at `+0x1c`/`+0x20`, over IVP's own `FUN_180089550` defaults
  (`2000`, `70000`, π/2, `1000`, `10`, `2500`). vphysics's anomaly manager's slot `+0x20`, `FUN_180016e60`, asks the
  collision solver's `AdditionalCollisionChecksThisTick` (`vphysics_interface.h:511`), which the client's `CCollisionEvent`
  answers `0` (`game/client/physics.cpp:77`), and the client never calls `SetPerformanceSettings` itself. **So a client
  manager tells at most 251 synapses in one pass.**
- **The rebase, `FUN_180094490`**, walks the list head first adding `−value` to each key and telling each listener's slot 3
  `(−value, −center)`; then adds `−value` to `next` and to the list's minimum — **an empty list's `1e10f` drifts with it** —
  and zeroes the value and center.
- **A core coming to rest** (`FUN_1800791a0`, `FUN_180078c90`) sets each object's byte `+0x78` to `8`, runs the broad phase
  `FUN_180098880` for it, folds both gradients into the values over `(float)(now − time)`, zeroes the gradients, leaves
  the time, and rebases unconditionally.
- **A teleport** (`FUN_18009a870`, from `FUN_1800733c0`) adds the core's movement bound to every object's `+0x98`, takes
  that as the value with zero gradients, adds the moved distance to the center, tells and rebases as the pass does, and
  zeroes the core's speed bounds.

**Filing.** `FUN_180097c40(record, allowance)` keys at `(float)((double)((float)(now − time)·gradient + value) +
allowance)` — the allowance added in double — and returns `(double)((gradient − center gradient)·t + (value − center
value))`, the hull past its center, in float. `FUN_180099970(manager, record, time, allowance)` takes the record out and
files it again at the same key over a given time. `FUN_180097e20(mindist, a₀, a₁)` sets the flags `& ~0x280000 |
0x140000` and files both records at `next + aᵢ` in float, writing nothing to `+0xa0`; `FUN_180097d60(mindist, gap)` calls
it with `1e-10f` for a side whose object's `+0x78 & 7` is zero and the gap for the other, or with the far branch's split.

**The listener table the records carry, `1800fdea0`**: slot 0 returns zero; slot 1, `FUN_180097570`, finds the mindist
from the record's `+0x30` word and tails into `FUN_180097f00` with the shortfall; slot 2, `FUN_180097580`, deletes the
mindist — `FUN_180094420`, the manager's teardown, calls it for every entry; slot 3, `FUN_1800975a0`, adds `(double)(value
shift − center shift)` to the mindist's `+0xa0`; slot 4 destroys a `0x38`-byte record. **The mindist's own table follows
at slot 5**: its deleting destructor, the fire routine `FUN_1800992e0` at `+0x8`, `+0x28` `FUN_1800947e0` (the anomaly
manager's slot `+0x38` for a float and then its `+0x10`), `+0x38` `FUN_180097440`, and `+0x40` `FUN_18008ecb0`.

**`FUN_180097f00`, told that a hull passed**: state `0x100000` tails into `FUN_1800b28a0`, *not read*. Flags `& 0x30000`
hand the pair off at once. Otherwise each core's position at now is `+0x150 + (double)(float)(now − +0x1d0)·+0x170`;
`d = ((y₀ − y₁)·n_y + (x₀ − x₁)·n_x) + (z₀ − z₁)·n_z` in double; each side's hull past its center is the float
`(gradient − center gradient)·(float)(now − time) + (value − center value)`, summed `h` in double; **the new length is
`length − (h − +0xa0) − (+0x9c − d)`**; each side's speed is `(double)(+0x254 + +0x1dc) + 1e-19`. When the shortfall
plus the new length is over `(double)(float)step·(s₀ + s₁)·6.0`, the mindist keeps `h` at `+0xa0`, `(float)d` at `+0x9c`
and `(float)` the new length at `+0xa8`, and each record is filed again over now with `((shortfall + new length)/(s₀ +
s₁))·sᵢ`. **Otherwise both records leave their managers and the pair is handed off**: to `FUN_180097940` when `flags &
0x3000` is `0x1000`, and to `FUN_1800977f0` otherwise.

**`FUN_1800977f0`, becoming exact**: flags `& ~0x300000 | 0xc0000`; the mindist at the head of the mindist manager's
exact list (`+0xc8`/`+0xd0` links, head `+0x10`) and each record at the head of its object's `+0x40` list; the minimize
`FUN_180095cb0`; appended to the manager's array (`+0x18` capacity, `+0x1a` count, `+0x20`) that `FUN_180098610` walks
each PSI when either core's `+0x58` is set; then, with flags `& 0xc000`, the mindist's `+0x38` — `FUN_180097440`, which
unfiles it, sets the flags `& ~0x340000 | 0x80000` and lists it and its records on the manager's `+0x28` and the objects'
`+0x48` — and otherwise `FUN_180099380(mindist, (core₀ byte +0x1 | core₁ byte +0x1) < 0x21, 0)`.

**`0x1000` is the phantom state**: the constructor `FUN_1800975d0` sums both objects' `+0xe0` into `+0x98` and becomes
exact unless either object has a `+0x38` pointer, when it sets `0x1000` and goes through `FUN_180097940` — the minimize
with a budget of zero (`FUN_180095ad0`), the phantom's float `+0x10` widening the gap, and the phantom listeners
`FUN_18008ae50`/`FUN_18008b0a0`. **The states under `0x3c0000`**: `0x140000` filed with the hull managers, `0xc0000`
exact, `0x80000` invalid, `0x100000` recursive.

**How an exact pair goes back to far, read from the PSI's phases** (`hull_manager.log`, `exact_phases.log`). *The plan
carried into this reading had `FUN_180097d60` as the way an exact pair is filed again; it is not, for a plain pair.*

- **Phase 3, `FUN_1800983e0`, right after the hull pass**, walks the exact list head first, reading each next link before
  it calls anything, and minimizes every mindist (`FUN_180095cb0`). A plain pair — flags `& 0x3000` clear — whose minimize
  left bits of `0xc000` goes to the mindist's `+0x38` (`FUN_180097440`, invalid); nothing else happens to it. **Only a
  pair with bits of `0x3000` goes on to `FUN_180098dd0` and `FUN_180097d60`**, through the phantom listeners.
- **The rechecked array**, walked last first by `FUN_180098610` before the step, runs `FUN_180098710` on each entry: the
  same minimize and the same split — invalid for a frozen plain pair, the phantom path otherwise — and then, for a pair
  one of whose records is kind 3, the resting-contact routine `FUN_180096460`.
- **Phase 4, `FUN_1800985a0`, walks the exact list again and hands every mindist to `FUN_180099380(mindist, 1, 1)`** — the
  scheduler with `removeFar` set and recheck mode 1, the next link read first. **That is the plain pair's way back to
  far**: a pair past its threshold is unfiled and filed with its objects' hull managers.
- **So `FUN_180097d60` and `FUN_180097e20` serve the phantom path** (`FUN_180097940`, `FUN_180098710`, `FUN_1800983e0`)
  and the recursive mindist's `FUN_1800b2460` and `FUN_1800b2700` — not a plain pair.

**Carried in inches, the speed floors are metres a second and are converted**: `1e-10f` in the far split and
`FUN_180097d60`, `1e-19` in `FUN_180097f00`. *The first port of the split left `1e-10f` in metres* — a difference only a
pair creeping at about a nanometre a second can show, where the split is `0.546 : 0.454` in inches and `0.841 : 0.159`
unconverted — and a case that tells the two apart now pins it.

*Evidence class: read from the disassembly, and from the SDK for the performance defaults and the client's solver. Not
established: what an object's `+0x78 & 7` and a core's `+0x58` and byte `+0x1` mean; the recursive mindist
`FUN_1800b28a0`; the phantom path past its first layer; and the broad phase `FUN_180098880`, which files its own
listener in the same managers through `FUN_18009de80`.*

### The minimize, routine by routine (2026-09-12)

**Read from the disassembly of every routine below** (`D:\ghidra-proj\out\minimize_*.log`,
`cache_object_fill.log`). Two assertion strings name the source files: `FUN_180095cb0` and `FUN_180094f80` assert in
`ivp_mindist_minimize.cxx`, and `FUN_18007bbd0` asserts at line 674 of `ivp_compact_ledge_solver.cxx`; *that its
neighbors from `18007b300` to `18007d480` are in the same file is INFERRED from their addresses*. Every dot product
sums `x` and `y` before `z` unless a line says otherwise — the disassembly sometimes adds `y` first, which is the same
bits — and every comparison's NaN case is written out, because the eight feature routines branch on `COMISD`/`JNC`,
`JBE`, `JA` and `JC` in no consistent pattern.

#### The solver, the sides, and what the minimize writes

**The solver is a stack structure** in `FUN_180095cb0`: `+0x00` the mindist; `+0x08` a step budget of **20**,
decremented by four of the eight feature routines on entry; `+0x10..0x20` a point in doubles, written when a routine
reports a backside; `+0x30` up to **256** pair keys of eight bytes each, and `+0x830` their count.

**A side** — one per synapse, built by `FUN_180094c70` exactly as the time-of-impact dispatch builds them — holds the
ledge's point array at `+0x00`, the ledge at `+0x08`, the object's CACHE OBJECT at `+0x10`, the real object at
`+0x18`, and its synapse record at `+0x20`, whose `+0x28` is the feature (an edge word's address) and whose word at
`+0x32` is the feature's kind: `0` point, `1` edge, `2` triangle, `5` a triangle the point was found behind.

**The cache object is the object at the environment's current time**, filled by `FUN_180080a60`: with `dt =
(float)(now − core+0x1d0)`, the rotation is `FUN_180071060`'s slerp from `core+0x180` to `core+0x1a0` at
`(double)(dt × core+0x1d8)` and the position is `core+0x150 + (double)core+0x170 × (double)dt` per component — or both
copied straight from `core+0x180` and `core+0x150` when `dt` compares equal to zero, which `UCOMISS`/`JNZ` also says of
NaN. **The core position goes to `+0x00`**; the matrix at `+0x40` is `FUN_180071330` of the two, with its translation
replaced by the object offset put through it unless bit `0x800` is set — the same composition `IvpMotionCache.Fresh`
ports.

**What the minimize writes to the mindist:** the length at `+0xa8` (less the extra radius at `+0x98`), the normal at
`+0xb0`, `+0x9c` = `(float)(coreFirst − coreSecond)` dotted in float with that normal, both synapses' feature and kind,
bit 8 of the flags — **flipped whenever a routine is about to call another with the other side first**, so synapse A
is always the first argument's — and bits 14–15.

#### The entry and the two dispatchers

```
FUN_180095cb0(mindist):
  if mindist+0xc0 == env+0x1a0: return 4            -- once per PSI
  mindist+0xc0 = env+0x1a0
  retries = 0
  loop:
    r = DAT_18012d4b0[kind(B) + 4·kind(A)](solver)  -- A = synapse (flags >> 8) & 3, B the other
    if r == 1: flags &= ~0xC000; return 1
    flags = (flags & ~0x8000) | 0x4000
    if r == 2: break
    if r != 3: assertion at line 0x138
    s = synapse A if its kind is 5, else synapse B
    s.feature = FUN_180094e30(s.feature, solver+0x10); s.kind = 2
    if ++retries >= 2: break
  unless (flags & 0x3000) == 0x1000 or (flags & 0x3C0000) == 0x100000: mindist.vtable[+0x28](mindist)
  return r
```

`FUN_180094f70` is three instructions — flip bit 8, jump to `FUN_180094c70` — so `(1,0)` and `(2,0)` become `(0,1)`
and `(0,2)`. `FUN_180094c70` builds both sides and routes on `kind(B) + 4·kind(A)`: `0` → PP, `1` → PK, `2` → PF, `5` →
KK, anything else → FF, each called `(solver, featureA, featureB, sideA, sideB)`. *The result names — `1` settled, `2`
gave up, `3` backside, `4` already done — are INFERRED from what each path does*; the virtual at `+0x28` is unread.

**The loop check, `FUN_180094600(solver, typeA, featureA, typeB, featureB)`**, is consulted only once the budget has
gone negative: it ORs each feature's low 32 bits with its type, orders the pair by signed value, returns `1` if the
pair is already among the keys (scanned newest first) or if 256 are stored, and otherwise stores it and returns `0`.

#### The compact-ledge helpers

| routine | does |
|---|---|
| `18007ba70`, `18007d480` | a point of one side into the other side's frame: `T.RotateInverse(S.ToWorld(p) − T.t)`, each sum grouped as `IvpMatrix` groups it; the first takes an edge, the second a point |
| `180080720` via `18007d2e0` | an edge's start point into the world, `ToWorld` of the widened float point |
| `180080670` | a world point into a frame, `RotateInverse(p − t)` |
| `18007d070` | an edge's two weights for a point `p` (`S` its start, `E` its end, `d = (double)(E − S)` subtracted in float): `a = (float)(((p.y − S.y)·d.y + (p.x − S.x)·d.x) + (p.z − S.z)·d.z + FLT_MIN)`, `b = (float)(((E.y − p.y)·d.y + (E.x − p.x)·d.x) + (E.z − p.z)·d.z + FLT_MIN)`; the points widened before subtracting `p` |
| `18007cdf0` | a triangle's weights for `p`: barycentric about the start `A` of the triangle's SECOND edge word, whatever edge is passed — `u = B − A`, `v = C − A` (float subtraction, widened), `w = p − A`, `det = uu·vv − uv²`, `s = wu·vv − wv·uv`, `t = wv·uu − wu·uv` — each output `(float)(weight + FLT_MIN)`, and written so that **`out[0]` is the passed edge's, `out[4]` the next edge's, `out[8]` the previous edge's**, and `out[0xc] = (float)det` |
| `18007d300` | an edge line's squared distance: `|(p − S) × (E − S)|² / (|S − E|² + 1e-18f)`, the cross's edge subtracted in DOUBLE and the divisor's in FLOAT |
| `18007c1a0` | an edge SEGMENT's squared distance: if `(bits(a) | bits(b)) ≥ 0`, the line's; else the endpoint's — the start when `a < 0` or NaN, the end otherwise — as `((dy² + dx²) + dz²)` |
| `18007b940` | `IvpVector.FaceNormal` over the passed edge, the next and the previous |
| `18007b780` | a triangle's plane: `18006ddb0` crosses `(next − p0) × (prev − p0)`, the same bits as `FaceNormal`, with `d = −((n.y·p0.y + n.x·p0.x) + n.z·p0.z)`; `18006f6c0` scales all four by `1.0 / SQRTPD(|n|²)` — an exact root, no threshold |
| `18006dd30` | the cross product, every term read before any is written |
| `18006db60` | a vector perpendicular to `v`: swap the largest-magnitude component (strict `>`, checked `z`, `y`, `x`) with the one before it cyclically, negating the moved one, then cross that with `v` |
| `18006e080`, `18006f730` | scale to unit length at or above `1e-19` — four Newton steps and FIVE respectively |
| `18006edd0` (and `18006ece0`, a jump to it) | the reciprocal root with FIVE steps; `18006ecf0` takes four |
| `180070050` | `(1 − t)·a + t·b` |
| `18007b300` | the edge-edge input: `+0x00`/`+0x08` pointers to `L`'s two float points; `+0x10`, `+0x30` `K`'s two points in `L`'s frame; `+0x50` `K`'s float edge rotated into the world by `K` and back by `L`; `+0x70` `L`'s float edge widened; `+0x90`, `+0x98` the edges; `+0xa0`, `+0xa8` the sides; `+0xb0` the cross of `+0x50` and `+0x70` |
| `18007c870` | the edge-edge weights: if `|cross|² > 0x3C32725DD1D243AB` — **one ulp under `1e-18`** — `a = +0x50 × cross` and `b = +0x70 × cross` give `out[8]`, `out[0xc]` for `L` WITHOUT `FLT_MIN` and `out[0]`, `out[4]` for `K` with it, and return `1`; otherwise a sampled search, below, returning `0` |
| `18007c2a0` | the edge-edge squared distance from the input: `(cross·Ks − cross·Ls)² / |cross|²` above `1e-24`, else `18007c1a0` of `Ks` against `L` |
| `18007bbd0` | the edge-edge squared distance from two edges, below |
| `180094e30` | the backside walk, below |

**The parallel edge-edge search** samples `K` at the floats `−1, 0.5, 2, 0, 1, −0.001, 0.001, 0.999, 1.001, −1e-6,
1e-6` against `L`'s line, keeping the least `18007d300` (`<`, NaN taken) with `out[0] = f`, `out[4] = 1 − f` and `L`'s
`18007d070` weights; then samples `L` — its points put into `K`'s frame by `18007d480` — at the first NINE of those
against `K`'s line with the running minimum, writing `out[8] = f`, `out[0xc] = 1 − f` and `K`'s weights inline as
`(float)(FLT_MIN − (S − q)·d)` and `(float)(((E − q)·d) + FLT_MIN)`, the second's `z` term added to `FLT_MIN` first.
The table's last two floats, `0.999999` and `1.0000009`, are never sampled.

**`18007bbd0(K, L)`**: with the weights, `L` inside and `K`'s start weight not `> 0` → the segment distance of `K`'s
start to `L`; its end weight not `> 0` → of `K`'s end; else the line distance as `18007c2a0` takes it. `K` inside
instead → `L`'s start or end against `K`, and an assertion when both of `L`'s weights are `> 0`. Both outside → the
least of four segment distances, `L`'s start and `K`'s start then `L`'s end and `K`'s end, combined with `MINSD`.

#### The eight feature routines

`seen(...)` is the loop check under a spent budget; `In(e, S→T)` is `18007ba70`; `W(e)` is `180080720`; weights are
`18007d070` and `18007cdf0`; `bits(x) ≥ 0` is a float's sign bit clear, as the `OR`/`JL` tests read it; and **"each edge
ending at P"** is the walk `prev(P)`, hop, `prev`, stopping on reaching `prev(P)` again — the previous edge of each
edge `IvpLedgeTopology.Ring` visits, in the same order.

```
PP  FUN_1800b1b80(P, Q):
  if --budget < 0 and seen(0:P, 0:Q): return 2
  wp = W(P); wq = W(Q); qInP = P.ToObject(wq); pInQ = Q.ToObject(wp)
  d² = ((wp.y − wq.y)² + (wp.x − wq.x)²) + (wp.z − wq.z)²
  if not d² > 1e-12: return 2
  r = rsqrt5(d²); length = (float)(r·d² − extra); normal = (float)((wp − wq)·r); +0x9c
  best = none, bestSlope = 0
  around P, w = qInP − P, base = (P.y·w.y + P.x·w.x) + P.z·w.z:
    for each edge E ending at P, N = start(E):
      s = ((N.y·w.y + N.x·w.x) + N.z·w.z) − base
      if s > 0: s ·= rsqrt_f((float)((e.y² + e.x²) + (e.z² + 1e-18f))), e = N − P in float
                if s > bestSlope: best = E, other = (qInP, Q, sideQ)
  around Q the same, w = pInQ − Q, other = (pInQ, P, sideP)
  if none, or not b > 0 of best's weights for other's point: synapses (P,0), (Q,0); return 1
  if not a ≥ 0: flip for best's side;  return PP(best, other feature)
  flip for other's side;               return PK(other feature, best)

PK  FUN_1800b1aa0(P, K):
  (a, b) = weights of K for In(P)
  not a ≥ 0 → PP(P, K);  not b ≥ 0 → PP(P, next(K));  else → PK-proximity(P, K)

PF  FUN_1800b1910(P, F):
  p = In(P); w = weights of F for p
  all bits ≥ 0 → PF-proximity(P, p, F)
  d0 = segment²(p, F):        m = (d0 < 1e101 or NaN) ? d0 : 1e101;   best = (d0 ≥ 1e101) ? null : F;  m0 = m
  d1 = segment²(p, next(F)):  m = (d1 < m or NaN) ? d1 : m;           best = (m0 > d1) ? next(F) : best
  d2 = segment²(p, nn(F)):                                            best = (m > d2) ? nn(F) : best
  return PK(P, best)

KK  FUN_1800afa40(K, L):
  w = KK weights
  L inside (bits(w2)|bits(w3) ≥ 0): not w0 ≥ 0 → PK(K, L); not w1 ≥ 0 → PK(next(K), L); else KK-proximity
  K inside: flip; not w2 ≥ 0 → PK(L, K); else PK(next(L), K)
  kNear = (w0 ≥ w1) ? next(K) : K;  lNear = (w2 ≥ w3) ? next(L) : L;  kFar, lFar the others
  (a, b) = weights of L for In(kNear); both bits ≥ 0 → PK(kNear, L)
  (c, d) = weights of K for In(lNear); both bits ≥ 0 → flip; PK(lNear, K)
  not a·w2 ≥ 0 (float product) → PP(kNear, lFar)
  not c·w0 ≥ 0 → flip; PP(lNear, kFar)
  → PP(kNear, lNear)
```

```
PK-proximity  FUN_1800b11c0(P, K):
  if --budget < 0 and seen(0:P, 1:K): return 2
  p = In(P); S, E, C1 = start(prev(K)); Kt = twin(K), C2 = start(prev(Kt))
  kd = E − S, c1 = C1 − S, c2 = C2 − S (float subtraction, widened); w = p − S
  a1 = cross(kd, c1)·w; a2 = cross(c2, kd)·w
  t1 = F-weight of K for p; t2 = F-weight of Kt for p
  t1 > 0: → PF(P, (t2 > 0 and a2 > 0) ? Kt : K)
  t2 > 0: → PF(P, Kt)
  not a1 ≥ 0 and not a2 ≥ 0: synapses (P,0), (K,5); solver point = p; return 3
  c = cross(kd, w); inv = 1.0 / ((kd.y² + kd.x²) + kd.z²); d² = ((c.x² + c.y²) + c.z²)·inv
  d² > 1e-19: r = rsqrt5(d²); length = (float)(r·d² − extra)
              v = K.Rotate(cross(kd, c)); normal = (float)(v·(−(r·inv)));  dir = v
  otherwise:  length = −extra; o = perpendicular(kd), unit5; normal = (float)o — IN K's FRAME;  dir = c
  +0x9c; u = P.RotateInverse(dir); limit = d²·1e-12; best = none
  for each edge E ending at P: e = N − P (float, widened); s = u·e
    if s > 0: s ·= rsqrt_f((float)((e.y² + e.x²) + e.z²)); if s > limit: limit = s, best = E
  none: synapses (P,0), (K,1); return 1
  limit < 1e-8 or NaN: in = KK input(K, best); if 18007c870 returned 0, or not its w2 ≥ 0 → synapses (P,0), (K,1); return 1
  → KK(best, K)

PF-proximity  FUN_1800b0c20(P, p, F):
  if --budget < 0 and seen(0:P, 2:F): return 2
  n = FaceNormal(F), unit4; wn = F.Rotate(n); u = P.RotateInverse(wn)
  length = (float)(((n.y·p.y + n.x·p.x) + n.z·p.z) − ((F0.y·n.y + F0.x·n.x) + F0.z·n.z));  normal = (float)wn
  if 0 > length: u = −u      -- the normal is NOT flipped
  length −= extra (float); +0x9c
  best = none, bestCos = 0
  for each edge E ending at P: e = N − P; s = u·e
    if not s ≥ 0: s ·= rsqrt_f((float)((e.y² + e.x²) + e.z²)); unless s ≥ bestCos: bestCos = s, best = E
  none: synapses (P,0), (F,2)
        if not (length + extra) ≥ 0 (float): solver point = p; F's kind = 5; return 3
        return 1
  q = In(best); w = weights of F for q, F's ledge found from its header
  all ≥ 0 → PF-proximity(best, q, F)
  exactly one of the three not ≥ 0: the first of F, next, nn with 0 > weight → KK(best, that edge)
  m = 1e101: each of F, next, nn whose weight is not > 0 → edge²(best, it), taken when < m or NaN — the last only when m > it
  → KK(best, the pick)

KK-proximity  FUN_1800b0280(K, L, in, w):
  if --budget < 0 and seen(1:K, 1:L): return 2
  n = in.cross (L's frame); s = (float)(((Ls − Ks)·n)), Ls widened; sign = its sign bit
  r = rsqrt5((n.x² + n.y²) + n.z²); wn = L.Rotate(n); nK = K.RotateInverse(wn)
  length = (float)|(double)s·r| − extra;  σ = (((float)sign − 0.5f) + (float)sign) − 0.5f
  normal = (float)(wn·((double)σ·r)); +0x9c;  LsK, LeK = L's points in K's frame
  records:  0 triangle twin(K) (K), point Ls → LsK (L, edge L)       flag against nK
            1 triangle K       (K), point Le → LeK (L, edge twin(L)) flag against nK
            2 triangle twin(L) (L), point Ks        (K, edge K)       flag against n, inverted
            3 triangle L       (L), point Ke        (K, edge twin(K)) flag against n, inverted
  f[i] = sign bit of (float)(FaceNormal(tri_i)·against) [inverted for 2, 3] XOR sign
  best = none, bestCos = −4e-12
  for i in 0..3: j = f[i] ^ sign ^ i; e = point[sign ^ i] − point[sign ^ i ^ 1]; s = normal_i·e
    if not s ≥ 0: cos = rsqrt_f((float)((e.y² + e.x²) + e.z²))·s·rsqrt_f((float)((m.x² + m.y²) + m.z²))
      unless cos ≥ bestCos: t = weights of tri_i for point[j]; if t.F > 0: best = (edge[j], side[j], tri_i), bestCos = cos
  none: synapses (K,1), (L,1)
        f0 + f1 == 2: synapses (K,5), (L,2); solver point = lerp(LsK, LeK, (double)(w2 / (w2 + w3))); return 3
        f2 + f3 == 2: synapses (K,2), (L,5); solver point = lerp(Ks, Ke, (double)(w0 / (w0 + w1)));   return 3
        return 1
  flip for best's point side
  all of t ≥ 0 → PF-proximity(edge, In(edge), tri)
  t.prev ≥ 0 → KK(edge, next(tri));  t.next ≥ 0 → KK(edge, prev(tri))
  → KK(edge, edge²(edge, next(tri)) > edge²(edge, twin(prev(tri))) ? prev(tri) : next(tri))

FF  FUN_180094f80(F1, F2) — no budget, no loop check:
  m = 1e101; k = 1.000000000001
  every start of F1 against every start of F2, F1 outer: d² of W(a), W(b); not d² ≥ m → (a,0), (b,0)
  (A's F1 points, B's F2 face), then (B's F2 points, A's F1 face): p = In(v); all weights ≥ 0 →
      h = ((pl.x·p.x + pl.y·p.y) + pl.z·p.z) + pl.d; not h²·k ≥ m → m = h²; (v,0), (face,2)
  (B's F2 points, A's F1 edges), then (A's F1 points, B's F2 edges): both weights ≥ 0 →
      not line²·k ≥ m → m = line²; (edge,1), (v,0)
  every F1 edge against every F2 edge: both pairs of weights ≥ 0 → not 18007c2a0·k ≥ m → m; (k,1), (l,1)
  if B's kind is 0 and A's is not, B goes first; flip for the first
  (0,0) → PP, (0,1) → PK, (0,2) → PF, (1,1) → KK; any other pair is an Error and a breakpoint trap
```

**The backside walk, `FUN_180094e30(feature, point)`**, starts from the triangle whose index is **bits 12–23 of the
feature's triangle's header word** — the triangle on the other side of the ledge, *INFERRED from its use* — marks each
triangle it enters in a byte array as long as the ledge's signed 16-bit triangle count, takes that triangle's weights
for the point, and for each of its edge, next and previous in turn whose weight is at or below zero (`COMISS 0, w;
JC`, so NaN does not move) hops to the twin edge if that triangle is unmarked. It returns the edge it stopped in.

*Evidence class: read from the disassembly for all thirty routines named here; the constants `1e101`, `FLT_MIN` as a
double, `1e-18f`, the sub-`1e-18` threshold, `1e-12`, `−4e-12`, `1e-8`, `1e-19`, `1e-24` and `1.000000000001` dumped
and compared bit for bit with C# literals — only the `1e-18` threshold differs from its literal, by one ulp. The
purpose of result `4`, of the flags bits, of the `+0x9c` value and of kind `3` routines `FUN_180094ad0` and
`FUN_180094860` (which take no ledge features; *INFERRED to be balls*) is not established.*

**Ported as `IvpCompactLedgeSolver` and `IvpMindistMinimize`** (2026-09-13), with a triangle header's bits 12–23
carried as `PhysicsLedge.PierceTriangles`. **The plain mindist's virtual `+0x28` is `FUN_1800947e0`**, read to find
what the minimize's failure path calls: it asks the environment's `+0x40` object's slot 7 for a float and hands
that object's slot 2 the mindist, both real objects and the float — the recursive mindist's slot 5 is
`_guard_check_icall`, a no-op. The port reports that the call happens; what the object does with it is unread. Two
readings carried into the port that are easy to lose, and that **no test yet pins**: **the point-edge routine's
degenerate normal is written in the edge's own frame**, never turned into the world, and **the face-face routine
handicaps every candidate after the point pairs by `1.000000000001`** while storing the unhandicapped distance, so
a later candidate must be strictly closer by that factor.

**`DAT_1800feb70` is `0.375`**, the blend applied on every fourth regula-falsi iteration.

**`interpolate` is `FUN_180071060`, a shortest-path slerp that falls back to a normalised lerp.** It
takes the dot product of the two rotations; at or below zero it negates the dot and flips the second
rotation's sign, so the path is always the short one. When the dot reaches `DAT_1800fcea0` — the two
nearly parallel — it lerps component by component and renormalises with two Newton steps of a
reciprocal square root; otherwise it is a true slerp through `acos` and two `sin`s. Dumped: the
cut-over `DAT_1800fcea0` is **`0.999`** (a float widened), the signs `DAT_1800ea988` and
`DAT_1800ea9f8` are `+1` and `−1`, and the Newton constants `DAT_1800ee388` and `DAT_1800ea9c0` are
`0.5` and `1.5`.

**Read from the disassembly, because the decompiler dropped the `sin` arguments** (`RCX` out, `RDX`
from, `R8` to, `XMM3` the fraction):

```
dot  = from · to                        -- all four lanes
sign = dot > 0 ? +1 : (dot = −dot, −1)
if dot ≥ 0.999:                         -- JNC, so the threshold itself takes this branch
    out = from + (sign·to − from) · t
    s   = 0.5 · |out|²
    x   = 1.5 − s
    x   = x + (0.5 − x²·s)              -- twice
    x   = x + (0.5 − x²·s)
    out = out · x
else:
    θ      = f(dot)                     -- 1800cce64
    invSin = 1 / √(1 − dot²)
    out    = g((1 − t)·θ)·invSin · from + sign · g(t·θ)·invSin · to     -- 1800c8020, twice
```

**The renormalisation is NOT the textbook Newton step** `x · (1.5 − s·x²)`; it is the linearised
`x + 0.5 − s·x²`, from a start of `1.5 − s`. Near unit length the two agree closely, and this is the one
read. **The slerp branch does not renormalise at all.** *That `f` is `acos` and `g` is `sin` is
INFERRED from the arithmetic:* `√(1 − dot²)` is `sin θ` only when `θ = acos(dot)`.

**Neither the cut-over nor the renormalisation variant can be seen in a float.** Running the read
sequence in doubles at dots of 0.9991, 0.99991 and 0.99999 with a fraction of one half: lerp and slerp
differ by at most `8e-12`, and the linearised renormalisation differs from the textbook one by `9e-17`.
So the branch matters to the engine's doubles and to nothing this project stores as a float. What a
wrong port DOES change is the slerp below the cut — at a fraction of one quarter, where a normalised
lerp lands on `0.1875` against the slerp's `0.19509` for a quarter of a quarter turn — and the sign flip
on a negative dot. Those are what `IvpQuaternionInterpolateConformanceTests` pins. **This project's `IvpQuaternion` does not port this routine** —
only the product, the normalise and the angular step.

**And a defect found on the way.** `IvpQuaternion.UnitTolerance` is `1e-9`, documented as *"ours, not
the engine's — `DAT_1800f4f28` was not dumped"*. It was dumped above: **`1e-12`**. The comparison's
shape matches `FUN_180070c60`; only the number was invented. **Fixed to `1e-12`.**

**And no test can tell the two apart, which is arithmetic rather than a gap.** A float quaternion in
the band exists — `(0.3631, 0, 0, 0.9317502)` is `7.8e-10` off unit, found by search — but rescaling it
moves a component near `0.93` by about `4e-10`, under half a float's `6e-8` spacing, so the output
rounds back to identical bits. The same bound holds for any input under `1e-9`. Through the float
`Normalise` the constant is unobservable; it is carried because it is the engine's.

*Evidence class: read from the decompiled binary; `1e-12` read from the image; the unobservability is
arithmetic on float spacing, with the in-band input found by search.*

*Evidence class: read from the decompiled binary. The labels "collision" for `0x20` and "feature
change" for `0x21` are INFERRED from which search raises each.* **The margin and threshold block at `18012d540` is set twice**: at startup by
`FUN_180098fd0(block, DAT_1800eb150, DAT_1800ec290)`, and again at runtime through
`FUN_1800824c0(a, b)`, so its values depend on a caller. `DAT_18012d670` = `DAT_1800ea9b8` and
`DAT_18012d66c` = 1000 are set beside it.

**The block is linear in one collision tolerance `d`, plus gravity `g`.** Read from `FUN_180098fd0`,
with its multipliers dumped (`0.1`, `0.9`, `1/64` exactly, `0.3`, `0.01`, `2.5`, `20`):

| field | address | value | used by |
|---|---|---|---|
| `[0]` | `18012d540` | `0.1·d` | the recheck's `ε`, `(distance − ε) / speedBound` |
| `[1]`, `[0x42]` | `544`, `648` | `1.0·d` | the two ends of the margin ramp |
| `[2..0x41]` | `548..644` | 64 margins, `[1] + ([0x42] − [1])·i/64` | the per-material margin — flat at `1.0·d` until its ends differ |
| `[0x43]`, `[0x44]` | `64c`, `650` | `2·d`, `2.3·d` | |
| `[0x45]` | `654` | `√(2·(2.3·d − 1.0·d)·g)` = `√(2.6·d·g)` | the closing-speed threshold in `FUN_180099380` — the speed of a fall through that height |
| `[0x46]` | `658` | `0.01·d` | |
| `[0x47]`, `[0x48]` | `65c`, `660` | `4.5·d`, `22·d` | |
| `[0x49]` | `664` | `0.1·d` | read in the event routines |
| `[0x4a]` | `668` | `2·d` | |

**`d` and `g` at runtime.** The startup call passes `0.01` and `9.81`. The environment constructor,
`FUN_1800114f0`, then calls `FUN_1800824c0((DAT_18011f008 − DAT_1800eb144) × DAT_18011f000, 9.81)`, and
`CPhysicsEnvironment::SetGravity` (`FUN_1800150f0`) re-calls it with the current `d` and the new
gravity's magnitude, printing `"Set Gravity %.1f (%.3f tolerance)"` with `d × 39.37`. `DAT_18011f000`
is the metres-per-inch constant `IvpTransform` already carries, so **`d` is a tolerance in inches
converted to metres.**

**Dumped, with two controls.** `DAT_18011f000` reads `0.0254` and `DAT_18011f004` reads `39.37`, the
pair `IvpTransform` already holds, so the addresses are the right ones. Then `DAT_18011f008` is
**`0.25`** and `DAT_1800eb144` is **`1e-4`**, both floats:

```
d = (0.25 − 0.0001) × 0.0254 = 0.00634746 m  =  0.2499 inch      -- printed "0.250 tolerance"
```

So in inches, the units this project's simulation runs in: **the collision margin is 0.2499, the
recheck's `ε` is 0.025, and the closing-speed threshold is `√(2.6·d·g)`** — with `g` in metres per
second squared, because `SetGravity` multiplies each component by `0.0254` before the block sees it
(`IvpTransform`'s own citation of `1800150f0`). At `sv_gravity 800`, the default this project already
cites from source in `PhysicsEnvironment.DefaultGravity` alongside
`physenv->SetGravity( Vector(0, 0, -GetCurrentGravity()) )`, that is `g = 20.32` and a threshold of
`√(2.6 × 0.00634746 × 20.32)` = **0.579 m/s, 22.8 inches a second** — *arithmetic on those two
readings.*

**This project's `IvpContact.Slop` is 0.25** — the same number, used the other way round. IVP holds a
pair a margin APART and never lets it close further; ours lets a pair PENETRATE by that much before
the solve pushes it back. Whether `Slop` was copied from the tolerance or arrived at independently, it
answers the opposite question.

*Evidence class: read from the decompiled binary for both functions and all three initialisers;
constants read from the image where the image holds them, and explicitly NOT for the startup- and
runtime-initialised block. **The feature kinds are unnamed.** An earlier draft of this section called
kind 5 a ledge-tree or hull node; the code only shows kind 5 being replaced by kind 2 through
`FUN_180094e30`, and that is all that is claimed.*

### The other three times of impact, instruction by instruction (2026-09-13)

**Read from the disassembly of `FUN_1800a2b30` (0,0), `FUN_1800a1ff0` (0,1) and `FUN_1800a1420` (1,1)**
(`D:\ghidra-proj\out\toi_other_kinds.log`), with every evaluator they fill (`toi_evaluators.log`, `toi_helpers.log`),
the cache object's three transforms (`toi_cacheobj.log`) and the motion cache builder (`toi_cache_a0800.log`). They
share `FUN_1800a1b50`'s shape — the same search context, the same two sides, both motion caches built first, `time :=
end`, and every root moving `time` earlier and overwriting the kind — and the same two finders. Each argument order is
the dispatch's: synapse A's feature first, then B's, then side A, then side B on the stack.

**Three fields none of the earlier routines read.** The context's `+0x18`, the total bound the scheduler builds
(*The scheduler's near branch*); the mindist's float normal at `+0xb0`; and per core `+0x4` (the radius `+0x54` is
half the reciprocal of), `+0x1dc` (the integrator's `|linear velocity|`) and `+0x254` (`+0x80 × +0x8`). **A motion
cache's `+0x8` is its core**: `FUN_1800a0800` stores the cache object's real object (`+0xc8`) at `+0x0` and that
object's `+0xe8` at `+0x8`, a dword from `+0xc0` at `+0x18`, and the 21 slots from `+0x20`.

**The evaluators.** Vtables `1800fe720` to `1800fe760` are one slot each, eight in a row: `a3470` point-plane and
`a3660` edge, already ported, and seven more. `first` and `second` are the two transforms the finder hands slot 0.

| vtable | slot 0 | fields | distance |
|---|---|---|---|
| `1800fe730` | `a31e0` | `+0x28` P, `+0x48` Q, `+0x68` u | `d = Q' − P'`; `s = 1.2f·((d.y·u.y + d.x·u.x) + d.z·u.z)`; **`s` unless `|s|·s ≥ (d.y² + d.x²) + d.z²`, then `√` of that** |
| `1800fe738` | `a3990` | `+0x28` P, `+0x48` Q, `+0x68` u | `((Q'.y − P'.y)·u'.y + (Q'.x − P'.x)·u'.x) + (Q'.z − P'.z)·u'.z`, `u' = second·u` |
| `1800fe748` | `a36d0` | `+0x30` P, `+0x40` Q (floats), `+0x50` e, `+0x70` v, `+0x28` h | `c = (Q' − P') × e'`; `x = ((c·v') + h)`; **`x` unless `x ≥ |c|`, then `|c|`** (`JC`, so NaN keeps `x`) |
| `1800fe740` | `a37f0` | `+0x28` P, `+0x48` a, `+0x68` Q, `+0x88` e | `w = P' − Q'`; `r = (e' × w) × e'`, **each component narrowed to float**, scaled to unit length with five steps; `(a'.y·r.y + a'.x·r.x) + a'.z·r.z`, `a' = first·a` |
| `1800fe758` | `a32b0` | `+0x28` P, `+0x48` a, `+0x68` Q, `+0x88` b, `+0xa8` sign | `c = a' × b'`; `(rsqrt_f((float)|c|²) · (c·P' − Q'·c)) · sign` |
| `1800fe760` | `a33e0` | `+0x28` a, `+0x48` b | `|a' × b'|²` |
| `1800fe728` | `a3a30` | `+0x28` a, `+0x48` n | `(n'.x·a'.x + n'.y·a'.y) + n'.z·a'.z`, `a' = first·a`, `n' = second·n` |

A primed point is `ToWorld` and a primed direction `Rotate`, each by the transform of the side named — P, a by
`first`, Q, b, e, u, v, n by `second` unless the row says otherwise. Dots and squared lengths group `x` and `y` first;
every cross product is `FUN_18006dd30`. `1.2f` is `DAT_1800eed28`, a float widened.

**Point-point, `FUN_1800a2b30`:**

1. The point-point evaluator: P = A's point, Q = B's, `u = −n` negated in float and widened, speed `ctx+0x10`.
   `FUN_1800b6210` with target `(double)extra + (double)margin`, tolerance `(double)(0.5f·extra + 0.1·d)` and **no
   known distance** — slot 0 is measured — raises **`0x10`**.
2. `reach = (double)(float)(time − start)·ctx+0x18 + (double)length`, after that search.
3. `speed(X) = (double)core+0x1dc + |X|·(double)core+0x80`, with `|X|` = `FUN_18006e120`, `√` of the float sum of
   squares; `sum = speed(B) + speed(A)`.
4. `factor = (MINSD(margin², reach²) · −0.5) / (double)MAXSS(coreA+0x4, coreB+0x4)`.
5. **B's ring**, from B's edge, in `IvpLedgeTopology.Ring` order: vtable `1800fe738`, P = A's point, Q = B's, speed
   `coreB+0x80·reach + sum`. For each edge, `d` its float-subtracted direction, `s = (d.y² + d.x²) + d.z²` in double,
   `r = (double)rsqrt_f((float)s)`: the direction `d·r` and the target `(s·factor)·r`. `FUN_1800b6590` from `start`
   to `time`, no known distance: **`0x11`**.
6. **A's ring**, the same with the roles exchanged — P = B's point, Q = A's, speed `coreA+0x80·reach + sum` — and
   **the two motion caches handed over the other way round**, so B's transform is `first`: **`0x11`**.

**Point-edge, `FUN_1800a1ff0`:**

1. `angular = (double)(coreB+0x80 + coreA+0x80)`, in float.
2. The point-line evaluator: P = A's point, Q = the edge's start, `e` the float-subtracted edge scaled with four steps,
   `h = ((double)margin + (double)extra) · 0.5`, speed `ctx+0x10`; and **`v` from the cache objects' CURRENT
   matrices**: `v = B⁻¹·((B·Q − A·P) × B·e)` scaled with five steps — `FUN_180080720` for the two points,
   `FUN_1800809d0` for `e`, `FUN_180080890` for the transpose. `FUN_1800b6210`, target `(double)margin +
   (double)extra`, tolerance `(double)(0.9f·extra + 0.1·d)`, no known distance: **`0x30`**.
3. **Two planes, the edge's own triangle and then its twin across the offset field** (`IvpLedgeTopology.Hop`). For
   each, with `s₀ s₁ s₂` the triangle's start points from that edge: `m = (s₁ − s₀)_float × FaceNormal(s₀, s₁, s₂)`,
   scaled with five steps; the point-plane evaluator over P, `m` and `s₀`, **speed `ctx+0x18`**. `FUN_1800b6590` to
   `(double)(−(float)(0.1·d))`: **`0x31`**.
4. `gap = MAXSD((double)length − (double)(float)(time − start)·ctx+0x18, 1e-8)`; the speed `((double)(float)(coreB+0x254
   · coreB+0x80 + coreB+0x1dc) + speed(A)) / gap + angular`; the target `(MINSD((double)margin, (double)length) ·
   (double)(coreB+0x54 + coreB+0x54)) · −0.3f`.
5. **A's ring**: vtable `1800fe740`, P = A's point, Q the edge's start, `e` as in (2), and each ring edge's
   float-subtracted direction scaled with four steps as `a`. `FUN_1800b6590` from `start` to `time`: **`0x32`**.

**Edge-edge, `FUN_1800a1420`:**

1. `angular = (double)(coreA+0x80 + coreB+0x80)`, in float. `a` and `b` are the two edges' float-subtracted
   directions, each scaled with four steps.
2. `c = A·a × B·b` through the CURRENT matrices; `dot = ((double)n.y·c.y + (double)n.x·c.x) + (double)n.z·c.z`;
   **`sign = +1` when `dot ≥ −0.0`** (`COMISD`/`JNC`), `−1` otherwise and for NaN.
3. The line-line evaluator over A's start and `a`, B's start and `b`, `sign`, speed `ctx+0x10`. `FUN_1800b6210` to
   **`(double)margin`, tolerance `(double)(0.1·d)` — neither carries the extra radius** — no known distance: **`0x40`**.
4. The cross evaluator over `a` and `b`, speed `angular + angular + 1e-19`. `FUN_1800b6590` to `1e-19`: **`0x41`**, the
   edges turning parallel.
5. **Four face checks**, vtable `1800fe728`, speed `angular + 1e-19`, each face normal `FUN_18007b940` scaled with four
   steps, each target `(double)(−(float)(0.1·d · core+0x54))` **of the core whose EDGE is dotted** — the side whose
   transform is `first` — and each raising **`0x42`**:

   | order | direction, as `first` | face, as `second` | direction negated when |
   |---|---|---|---|
   | 1 | B's `b` | A's twin triangle | `sign` is `−1` |
   | 2 | B's `b` | A's own triangle | `sign` is `+1` |
   | 3 | A's `a` | B's twin triangle | `sign` is `−1` |
   | 4 | A's `a` | B's own triangle | `sign` is `+1` |

**Two of these targets are not in consistent units, and a port in inches has to say so.** Point-point's ring target
`|d| · min(margin², reach²) · −0.5 / radius` is a length squared compared with a length, so the engine's answer
belongs to metres: carried in inches it is scaled by `0.0254`. Point-edge's `1e-8` floor is a distance in metres,
carried converted as `FUN_1800b6590`'s tolerance already is. Every other target and speed is either dimensionless or
linear in length — `coreB+0x254 · coreB+0x80 + coreB+0x1dc` is `ω²·deviation + v`, odd in time but linear in length —
and needs nothing. *Arithmetic.*

**What the kinds say, read from the fire routine** (*When a queued mindist fires*): a kind with its low four bits clear
— `0x10`, `0x30`, `0x40`, as `0x20` — is tested for a collision; any other is re-minimized and rescheduled. *So the
three new "collision" events and six "feature change" events are INFERRED labels, by the same rule as `0x20` and
`0x21`.* **Ported as `IvpPointPointSearch`, `IvpPointEdgeSearch` and `IvpEdgeEdgeSearch`**, over the seven
evaluators and `IvpCoreBounds`.

*Evidence class: read from the disassembly for the three routines, all seven evaluators, `FUN_18006e120`,
`FUN_18006fc60` (`√((x² + y²) + z²)` in double), `FUN_18006f730` (five steps, as read before), `FUN_180070b20` (a
float point widened, grouped as `FUN_180070bc0`), the cache object's `FUN_180080720`, `FUN_1800809d0` and
`FUN_180080890` (its `+0x40` matrix: `ToWorld`, `Rotate`, `RotateInverse`), and `FUN_1800a0800`; dumped:
`DAT_1800eed28` `1.2f`, `DAT_1800f1fc8` `−0.5`, `DAT_1800ee18c` `0.9f`, `DAT_1800ee388` `0.5`, `DAT_1800fe7c8`
`−0.3f`, `DAT_1800fe7c0` `−0.0`, `DAT_1800eaa00` `−1.0`, `DAT_1800fb100` `1e-8`. **Not established:** what the
face checks' `+0x54` choice is for.*

**What the synthetic tests do not pin, found by sabotaging each branch** (four rounds, twenty-nine breaks, all caught):
the float narrowing inside `a37f0`; the ring, plane and face speeds — the rings' formulas, the planes' use of the total
bound, the `1e-8` gap floor — because every fixture that reaches them is still, so none of those refinements takes a
step (only the three falling searches do, at the context's approach speed); that point-point
measures its start rather than taking a known distance, since the fixtures' length is the distance; that edge-edge's
line search omits the extra radius, since every fixture's is zero; and the cache order handed to each finder wherever
both bodies are unturned. **Point-point's ring cache order IS caught, but not where a reader would look**: exchanged,
the untouched edges point at the other point and raise the same `0x11` the ring tests expect, and the falling and
rising controls are what redden.

### The contact point and its record, instruction by instruction (2026-09-13)

**Read from the disassembly** (`contact_point.log`, `contact_helpers.log`, `friction_callees.log`, `impact_callees.log`)
and **ported as `IvpContactPoint`, `IvpContactGeometry` and `IvpContactRecord`** — not yet on the running path. This is the
layer `FUN_180090e50` builds before anything is solved: a contact point that persists for an exact pair, and a fresh record
of where and how the pair touches every time it collides.

#### Finding or building the contact point — `FUN_18008c4b0(mindist, &built)`

```
if (flags & 0x3C0000) != 0xC0000: built = 0; return null           -- only an exact mindist has one
for each friction synapse on record 0's object's list (+0x50), newest first:
    cp = synapse + (short)synapse[+0x18]
    if (cp+0x20 or cp+0x48 is record 1's object) and FUN_1800869a0(cp, mindist): built = 0; return cp
cp = allocate 0xd0; FUN_180082ed0(cp, mindist); built = 1; return cp  -- a failed allocation returns null with built = 1
```

**`FUN_1800869a0(cp, mindist)`** matches both synapses in either order — cp's first against record 0 and its second against
record 1 when the objects line up that way, crossed otherwise. **`FUN_180086a50(friction synapse, mindist synapse)`**
compares one: the kinds must be equal; a point needs the same ledge (the address of its first triangle,
`triangle − (header & 0xfff)·16`) and the same start point (the edge words' low sixteen bits); an edge, the same edge word
or its opposite (`edge + ((int)(word << 1) >> 17)·4`); a triangle, the same triangle (`(a ^ b) & ~0xF == 0`); a ball always
matches; any other kind asserts at line `0x90a`.

#### The contact point — `FUN_180082ed0`

```
A = (flags >> 8) & 3,  B = ((flags ^ 0x100) >> 8) & 3
+0x10  friction synapse 0 = record A: object +0x20, back offset −0x10 at +0x28, kind +0x2a, edge +0x30;
       linked at the head of object A's list at +0x50
+0x38  friction synapse 1 = record B, back offset −0x38, the same shape, at the head of object B's list
each synapse's ledge handed to its object's surface manager (object+0xc8), slot 7    -- a reference; nothing for polygons
+0x98 = env+0x188, read through record 0's object
if record B's kind is 2:  +0x80 = (float)(1.0 / (FUN_18006fc60(FUN_18007b940(B's edge)) + (double)1e-18f))
+0x68 = 0 (8 bytes), +0x84 = 0 (8), +0x7c = 0, +0x91 = 1, +0xa4 = 0 (8), +0xa0 = 0, +0xac = 0, +0x92 = 0 (16 bits),
+0xc0 = 0 (8), +0x64 = 0, +0x8c = DAT_18012d64c, +0x90 = 20
```

**`DAT_18012d64c` and `DAT_18012d650` are the tolerance block's `2·d` and `2.3·d`.** *This paragraph first said both were
zero*: both read zero in the image, and a search of every instruction's text found readers and no instruction storing to
either — with the caveat that *a store through a computed base address is not excluded*. That caveat is what happened; see
*A correction first* under *The impact solver and the friction system's bookkeeping* below. **`+0x80` is written only for a triangle**; for any other second kind it keeps whatever the
allocation held, and only the point–triangle measure reads it.

#### The record — `FUN_18008d0c0(cp, environment)`

`FUN_180090e50` passes the environment, reached through a core's `+0x10`. The record is `0x110` bytes bumped from the arena at
`env+0xf8` (the cursor rounded up to 32 bytes; `FUN_180072ae0` when the block is full), zeroed only at `+0x20..0x2b`,
`+0x72..0x75` and `+0x76`, left at `cp+0x70`, and counted at `env+0xc4`. Each object's cache object (`object+0x70`, taken by
`FUN_1800805a0` from the manager at `env+0xd8` when absent) is referenced and — when the object's state byte is under 8 and
the cache's time code `+0xc0` is behind `env+0x1a0` — refreshed by `FUN_180080a60`. The sides are built as the minimize's are.

```
kind of cp's synapse 0:  0 point → P = its edge's start in the world (FUN_18007d2e0)
                         1 edge  → FUN_18008cbc0(cp, edge0, edge1, side0, side1, record, &scratch); skip the next switch
                         3 ball  → P = cache0+0xa0, the object's position;  cp+0x91 = 1
                         else    → assertion, line 0x1c4
kind of cp's synapse 1:  0 point → Q = its edge's start;  FUN_18008cab0(cp, &P, &Q, record)
                         1 edge  → FUN_18008c7c0(cp, &P, edge1, side1, record)
                         2 tri   → FUN_18008c5b0(cp, &P, edge1, side1, record)
                         3 ball  → FUN_18008cab0(cp, &P, cache1+0xa0, record)
                         else    → assertion, line 0x1ba
r0 = object0+0xe0:  record+0x00 += (double)n·(double)r0, each component
cp+0x8c = cp+0x8c − (r0 + object1+0xe0) in float;  COMISS/JNC: zero unless ≥ 0, so a NaN becomes zero
both caches' references released;  FUN_18006dff0(record+0xb0)
record+0xc0 = (s.z·n.y − n.z·s.y,  n.z·s.x − s.z·n.x,  s.y·n.x − n.y·s.x) in float          -- normal × span
core0 = object0+0xe8; unless its byte 0 & 2:
    record+0xd0 = FUN_1800708a0(core0+0x90, record)             -- (float) world → core frame, grouped as RotateInverse
    n' = (float)((n.y·m20 + n.x·m00) + n.z·m40,  (n.x·m08 + n.y·m28) + n.z·m48,  (n.x·m10 + n.y·m30) + n.z·m50)
    record+0xf0 = (r.y·n'.z − r.z·n'.y,  n'.x·r.z − n'.z·r.x,  n'.y·r.x − n'.x·r.y)           -- arm × normal
    v = FUN_180077fa0(core0, record+0xd0, core0+0x140, core0+0x130)
    record+0x94 = (((t.y·I.y)·t.y + (t.x·I.x)·t.x) + (t.z·I.z)·t.z) + core0+0x4c;  record+0x98 = core0
  else: v = 0, record+0x94 = 0, record+0xd0 = 0, record+0xf0 = 0, record+0x98 = null
core1 the same into +0xe0, +0x100 and +0xa0, then v −= v1 and record+0x94 = (its terms + m1) + record+0x94;
  a static core1 zeroes +0xe0 and +0x100, leaves +0xa0 null, and leaves v alone
record+0x90 = 1.0f / record+0x94
dt = (float)(env+0x188 − cp+0x98);  cp+0x98 = env+0x188
cp+0x68 = (float)((double)cp+0x68 − (double)((v.x·s.x + v.y·s.y) + v.z·s.z)·(double)dt)
cp+0x6c = (float)((double)cp+0x6c − (double)((v.y·c.y + v.x·c.x) + v.z·c.z)·(double)dt)
cp+0xa0..0xa8 = (float)record+0x00;  cp+0xb0 = n.x, cp+0xb4 = n.y, cp+0xac = n.z
```

**`FUN_180077fa0(core, r, v, ω)` is the velocity of a point fixed to a core**: `ω × r` in float, turned by the core's
matrix at `+0x90` in double — each row grouped as `IvpMatrix.Rotate` groups it — narrowed, and `v` added in float. The other
helpers: `FUN_180080920` and `FUN_1800809d0` turn a float and a double vector by a cache object's matrix (`+0x40`), the first
narrowing; `FUN_18006dff0` scales a float vector whose float-summed square, widened, reaches `1e-19`, with the four-step
root in double; `FUN_18006fd40` scales a double vector with the FIVE-step root and returns `r·s`; `FUN_18006fc60` is
`SQRTPD((x² + y²) + z²)`. `FUN_180077840(core, r)`, read here and called elsewhere, is `1.0 / (max((x²+z²)·I.y,
(y²+z²)·I.x, (x²+y²)·I.z) + (core byte 0 & 0x10 ? 1.0 : m))`, the maxima taken with `MAXSD` in that order.

#### The four measures

**Point–point, `FUN_18008cab0(cp, P, Q, record)`:** `d = Q − P` scaled by `FUN_18006fd40`, whose return is the gap; the
normal is `(float)d`; if `n.x²` in float compares below `0.9f`, or unordered, the span is `n × (1, 0, 0)`, otherwise
`n × (0, 0, 1)`, both computed as `(n.y·b, n.z·a − n.x·b, −(n.y·a))` and scaled by `FUN_18006dff0`; the position is `P`. No
range.

**Point–edge, `FUN_18008c7c0`:** the edge's two ends in the world; `d = (float)(E₁ − E₀)` and `o = (float)(P − E₀)`;
`r = FUN_18006ece0` — a jump to the five-step root — of `(d.y² + d.x²) + d.z²` summed in float; `c = d × o` in float; the
gap `FUN_18006e120(c)·r`; the span `c·(r / gap)`, narrowed, when `gap² > 1e-19`, else `(1, 0, 0)`; the normal `d × span` in
float, scaled; the position `P`. **On a first measure** (`cp+0x91 == 1`, cleared as it is checked) the record is outside
when `(double)((d.x·o.x + d.y·o.y) + d.z·o.z)·r²` is below zero, unordered, or above one.

**Point–triangle, `FUN_18008c5b0`:** the position `P`; `p` = `P` put into the side's frame (`FUN_180080670`); on a first
measure, outside when any of `FUN_18007cdf0`'s three weights for `p` has its sign bit set; `n` = `FUN_18007b940(edge)`
times `(double)cp+0x80`; the gap `(p·n) − (e·n)`, `e` the edge's start widened and each dot's `x` and `y` terms added before
`z`; the normal `(float)(−1 · FUN_1800809d0(n))`; the span the edge's float vector turned out by `FUN_180080920`, unscaled.
**No root anywhere**: the stored reciprocal is what makes the normal unit length.

**Edge–edge, `FUN_18008cbc0`:** `a`, `b` the edges' world directions scaled by `FUN_18006e080`; `c = a × b`; degenerate unless
`|c|² > (double)1e-10f`. `u = a × c` and `w = b × c`; `tQ = (u·Q₀ − u·P₀) / (u·Q₀ − u·Q₁)` and `tP = (w·P₀ − w·Q₀) / (w·P₀ −
w·P₁)`, each denominator degenerate when under `1e-19` in magnitude or unordered; `A`, `B` the points at `tP`, `tQ`
(`FUN_180070050`); `g = FUN_18006fc60(B − A)`: above `1e-19`, the gap is `g` and the normal `(B − A)/g`, otherwise the gap
is zero and the normal `c / SQRTPD(|c|²)`; the position `A`; the span `(float)a`; on a first measure, outside when `tQ` or
`tP` is below zero, unordered, or above one. **Degenerate**: outside, the gap `DAT_18012d650`, the position `P₀`, the normal
`(1, 0, 0)`, the span `(0, 1, 0)`, the first-measure byte untouched — and the caller's scratch vector zeroed, which the
caller then overwrites with the second core's velocity or, for a static core, never reads.

**Every normal points from the contact point's first feature toward its second**, and **touching edges can flip it**: with
no gap the normal is `a × b`, which for a first edge above a second is the opposite of the `B − A` a gap gives.

#### After the record — `FUN_1800908d0(cp, record)`, and a correction to the impact's cone

`FUN_180090e50` calls it straight after the builder. It writes `record+0x60` and `+0x68` with each synapse's material — the
object's `+0xd0` when the triangle's material bits (`FUN_1800863d0`: `triangle+3 & 0x7f`) are zero, else slot 1 of the
environment's material manager (`env+0xe8`) with the object and the index — `record+0x40..0x58` with both objects and both
edges, **`record+0x80 = (float)` the manager's slot 3, and `cp+0x78 = (float)` its slot 2**.

**That settles the producers *The impact solver, found* names as untraced.** `FUN_18008ed60`'s first argument is the record
and its fourth the contact point, so its `(√(record+0x80) + 1)·cp+0x78` is built from slots 3 and 2 of the material manager.
*Which slot is elasticity and which the friction factor is INFERRED*: `cp+0x78` is what the friction driver's Coulomb product
multiplies, so slot 2 is taken as friction and slot 3 as elasticity; vphysics' implementations of both are unread.

**And its series is not the one that section names.** `1 − x²/2 + C·x⁴` has `C = DAT_1800fd858 = 0.041666668`, which is
`1/24` — the fourth-order term of `cos x` — and its `x` is `FUN_1800d4398(s)`, not `s`. The series for `1/√(1 + s²)` would
have `3/8`. So the pair is `(cos x, cos x · s)` with `x = FUN_1800d4398(s)`: the `(cos θ, sin θ)` that section describes if
`FUN_1800d4398` is the arc tangent, *which is INFERRED from its sign-bit handling and the pair, not read*. `IvpContact` uses
`1/√(1 + s²)` exactly, which is not the engine's arithmetic whatever the helper is.

#### Corrections to earlier readings

- **`record+0x94` is the arm crossed with the normal, and each lane takes its own axis.** *Contact response is accumulated,
  not applied* and `IvpContact`'s remarks read the decompiled `rx*rx * core[+0x44] + ry*ry * core[+0x40] + …` as the lever
  arm, with no cross product and its `x` lane on `+0x44` "by the engine's axis order". The disassembly squares
  `record+0xf0..0xf8` — `arm × normal` in the core's frame — and pairs `x` with `+0x40`, `y` with `+0x44`, `z` with `+0x48`.
  The decompiler named the first product after the lane it loaded first.
- **The record is written at every field above**, not only at the seven offsets *There are TWO contact solvers* lists, which
  is what a decompiled field search could see.
- **The friction-system contact whose `+0x60`, `+0x64`, `+0x68`/`+0x6c` and `+0x78` that section reads is this contact
  point**, the `0xd0` object — a different structure from the record, as it says. **`+0x68`/`+0x6c` are distances**: the
  builder moves them by a velocity times a time, so the friction solve's `param_2[1]·stored` is a velocity only if
  `param_2[1]` is an inverse time — *INFERRED*.

**Ported** (B369, not on the running path): `IvpContactPoint` (the constructor and the state the builder reads and writes),
`IvpContactGeometry` (the four measures), `IvpContactRecord` (the builder, with `IvpContactBody` carrying each side, its core,
the core's matrix at `+0x90` and the object's extra radius), `IvpVector`'s float scaling, five-step scaling and double
length, and `IvpCollisionObject.ContactPoints` for the lists at `+0x50`. Synthetic conformance tests with exact expectations:
`IvpContactGeometryConformanceTests` (23), `IvpContactRecordConformanceTests` (12), `IvpContactPointConformanceTests` (7),
`IvpVectorConformanceTests` +5. **Not ported**: the find-or-create walk and its matchers, which need a synapse to know its
ledge; the cache object's allocation and reference counts, for which each side stands; `FUN_1800908d0`'s materials; and
everything above the record.

*Not established:* what reads `cp+0x64`, `+0x7c`, `+0x84`, `+0x90`, `+0x92` and `+0xc0`; what `env+0xc4` counts beyond
records built; and whether the point–edge measure's five-step root differs from a four-step one in any output, since every
output it feeds is narrowed to float and no fixture separates them.

*Evidence class: read from the disassembly for every routine, constant and offset named; the zero globals by a whole-program
instruction search, its limit stated; the material slots' meanings and the arc tangent INFERRED.*

### The impact solver and the friction system's bookkeeping, instruction by instruction (2026-09-13)

**Read from the disassembly** (`friction_solve3.log`, `impact_solver4.log`), for the layers above the contact point. **None of it
is ported yet**; it is written down in full first, because a decode that lives only in a session's context is lost with it.

#### A correction first: the two "zero" globals are tolerance-block fields

*The contact point* above first called `DAT_18012d64c` and `DAT_18012d650` zero, with the caveat that a store through a
computed base address was not excluded. **That is exactly how they are written**: a search over the block's range finds
`FUN_180002540` and `FUN_1800824c0` each loading `LEA RCX,[0x18012d540]` before calling the initialiser, which stores by
offset, and *The block is linear in one collision tolerance* already tabulates both — `[0x43]` at `64c` is `2·d`, `[0x44]` at
`650` is `2.3·d`. The image holds zero because the block is filled at startup. **So a fresh contact point's gap is `2·d` and the
edge–edge measure's degenerate gap `2.3·d`.** The same search names this layer's other readers of the block: `660` (`22·d`) in
`FUN_18008db40`, `668` (`2·d`) in `FUN_18008e290`, `648` (`d`) in `FUN_180090bd0`, `544` (`d`) in `FUN_18008fca0` and
`FUN_180082250` (which returns it widened), and `65c` (`4.5·d`) in `FUN_180084490`, `FUN_180086500`, `FUN_180098610` and
`FUN_1800a9bf0`.

#### A record's estimate — `FUN_18008db40(cp)`

```
record = cp+0x70;  record+0x74 (short) = 1
if !(block[0x48] (22·d) >= cp+0x8c):  record+0x7c = 1e20f;  return                -- COMISS/JNC: a NaN gap takes this
f = FUN_18008fca0(cp, object0+0x30);  record+0x78 = f
s = 0
core0 = record+0x98, if any:  s = (double)((t.y·ω.y + t.x·ω.x) + t.z·ω.z) + (double)((n.y·v.y + n.x·v.x) + n.z·v.z)
core1 = record+0xa0, if any:  s += (double)−((t'.y·ω.y + t'.x·ω.x) + t'.z·ω.z) − (double)((n.y·v.y + n.x·v.x) + n.z·v.z)
record+0x7c = (float)((double)cp+0x8c − ((double)(0.5f·f) + s)·(double)(float)env+0x108)
```

`t` and `t'` are the record's turns (`+0xf0`, `+0x100`), `n` its normal, `ω` and `v` each core's `+0x130` and `+0x140`, every
product and sum in float before its widening. `FUN_180090bd0` picks the record with the smallest `+0x7c` under `block[0x42]`.

#### Removing a contact point — `FUN_180083e40(system, cp)`, and its inline copy in `FUN_180083b30`

```
core0 = object0+0xe8;  core1 = object1+0xe8
FUN_180078820(core0); FUN_180078820(core1)          -- core+0x200 = core+0x208 = env+0x188
FUN_180088ce0(system, cp)                           -- unlink: cp+0x0 next, cp+0x8 previous, head system+0x40; system+0x7a −= 1
if FUN_180088130(system, cp) == 1:  system+0x80 = 1  -- the pair emptied and was deleted
info0 = FUN_180077f00(core0, system);  info1 = FUN_180077f00(core1, system)
FUN_180075130(info0, cp)                            -- ordered removal from the info's contact vector
if info0's count (+0x2) is 0:  FUN_180077c10(core0, info0);  FUN_180088c80(system, core0);
                               core0+0x1f8's word: bit 9 cleared, bit 8 set
the same for info1 and core1
FUN_180083210(cp);  free(cp, 0xd0)
```

**`FUN_180083b30(pair, system)`** walks the pair's contacts (`+0x2` count, `+0x8` elements) from last to first: the record is
rebuilt (`FUN_18008d0c0(cp, core+0x10)`, the core being the pair's `+0x38`) and its materials set (`FUN_1800908d0`), and a
contact whose record's `+0x76` is `1` — outside — is removed exactly as above, inline. It returns how many are left.

**`FUN_180088130(system, cp)`** finds the pair of the two cores (`FUN_1800863f0`, asserting line `0x299` when there is none),
removes the contact from it (`FUN_180083da0`), and unless `FUN_180086b30(pair)` answers nonzero removes the pair from the system
(`FUN_180083db0`), destroys (`FUN_180054600`) and frees it (`0x50`), and returns `1`; otherwise `0`.

#### What a core keeps per system, and taking a core out

**The per-system record is `0x18` bytes**: a contact vector (capacity word `+0x0`, count `+0x2`, elements `+0x8`) and the system at
`+0x10`. **`FUN_180077c10(core, info)`** removes it from the core — from the hash at `core+0x60` (`FUN_1800726e0`, keyed by the
system) when the core's byte `0` has bit `2`, the unmovable flag, since an unmovable core can sit in many systems; otherwise by
nulling `core+0x60` — then clears the vector (freed unless its elements pointer is the record's `+0x10`, the vector class's
inline-storage test) and frees the record.

**`FUN_180088c80(system, core)`**: unless the core is unmovable, it leaves the movable cores (`FUN_180075130(system+0x58, core)`)
and loses the system's three controller bases (`FUN_180074fb0(core, system+0x20)`, `(core, system)`, `(core, system+0x10)`);
then it leaves the cores (`system+0x48`) and `system+0x78 −= 1`. **`FUN_180074fb0(core, controller)`** removes the controller
from the core's (`+0x1e2` count, `+0x1e8` elements) and, in the core's simulation unit at `+0x1f8`, removes the core from that
controller's `0x28`-byte entry (controller `+0x0`, a core vector at `+0x8`/`+0xa`/`+0x10` with inline storage `+0x18`) in the
unit's vector (`+0x3a`, `+0x40`), freeing and removing the entry when it empties. **`FUN_180075130(vector, element)`** finds
the element last-first and removes it in order.

#### The union-find — `FUN_1800877b0(system)`

```
for each core (system+0x50, count +0x4a):  core+0x258 = null
for each pair (system+0x70, count +0x6a), last first:  a = pair+0x38, b = pair+0x40
    unless either is unmovable:  ra, rb = the roots along +0x258;  if ra != rb:  rb+0x258 = ra
r = the root of the lowest-indexed movable core
answer = null;  for each movable core, last first:  if its root != r:  answer = its root
return answer                               -- a root outside the first core's set, or null when all are joined
```

#### The contact point's destructor, the listeners, and re-looking at a core's pairs

- **`FUN_180083210(cp)`** releases each synapse's ledge through its object's surface manager (`object+0xc8`, slot `8`), builds an
  event on the stack — the environment (`object0+0x30`), `cp+0xa0..0xa8` widened as the position, the contact point, both objects
  and both edges, the rest from `FUN_18008c590` — hands it to the environment's listeners (`FUN_180081eb0`) and, for an object
  whose `+0x78` has `0x2000`, to `FUN_180088450(env+0x18, object, event)`, then unlinks both friction synapses from their objects'
  `+0x50` lists.
- **`FUN_180081f10` and `FUN_180081f70(env, event)`** walk the environment's listeners (`+0x148`, count `+0x142`) last first,
  calling slot `+0x28` or `+0x30` of each whose byte `+0x8` has bit `4`.
- **`FUN_1800792b0(core)`** stamps `core+0x250` with `env+0x1a4` — the counter `FUN_18008ecb0` advances per impact — and hands each
  of the core's objects (`+0x70`, count `+0x6a`, last first) to **`FUN_1800746c0(object)`**, which, for each mindist on the
  object's `+0x40` list (next at the record's `+0x10`, the mindist at the record plus its word `+0x30`), unless both records'
  objects' cores carry the same `+0x250`, minimizes it (`FUN_180095cb0`) and, unless its flags have `0xc000`, reschedules it
  (`FUN_180099380(mindist, 0, 2)`). A pair whose two cores were both stamped this impact is looked at once, not twice.
- **`FUN_180079120(core)`** restores the angular velocity (`+0x130..0x138`) and sixteen bytes each at `+0x1a0` and `+0x1b0` from
  the `+0x260` record revival allocates, and nulls `+0x260`. *What `+0x1a0..0x1bf` hold is not read.*

#### The impact solver — `FUN_18008e290(solver, cores, p3, p4, float p5)`

```
solver+0x18 = p3
e = 1f − (1f − solver+0x130)/((float)p4·0.5f + 1f)                 -- float, then widened
solver+0x0 = (p5 + block[0x4a] (2·d))·1.2f
cores[0] = A = solver+0x110;  cores[1] = B = solver+0x118;  A's env+0x94 += 1
solver+0x30 = &A+0x90;  solver+0x38 = &B+0x90                          -- the cores' matrices
solver+0x40 = A+0x130 + A+0x110;  solver+0x60 = A+0x140 + A+0x120      -- ω and v plus their pending changes, float
solver+0x50 = B+0x130 + B+0x110;  solver+0x70 = B+0x140 + B+0x120
solver+0x28 (8 bytes) = 0
FUN_18008fc00(solver)                                                   -- the relative velocity c into +0xc0
*(solver+0x148) = c;   n = *(solver+0x140)
solver+0x10 = FUN_1800770f0(B, solver+0x128, FUN_180070620(B's matrix, n), n)
solver+0x8  = FUN_1800770f0(A, solver+0x120, FUN_180070620(A's matrix, −n), −n)    -- −n as n·−1f
if A unmovable:  solver+0x8 = solver+0x10·1e5;   if B unmovable:  solver+0x10 = solver+0x8·1e5
u = (n.y·c.y + n.x·c.x) + n.z·c.z                                        -- float
k = (((double)−0.1f/(mA + mB))·mA)·((mB + mB)·(double)u)
if !(u <= −1e-4f):                                                       -- COMISS/JBE: a NaN solves instead
    FUN_18008f570(solver, −n, 1);   if p4 > 10:  solver+0x18 = 0
else:
    FUN_180090240(solver);   solver+0xe0..0xeb = 0
    s = (double)−u;   b = √(double)p4 · (double)0.01f + √e·s                -- both roots SQRTPD
    i = 0
    while s > 0 and i < 100:   i += 1;  FUN_18008f1c0(solver, k);  FUN_18008fc00(solver)
                               s = (double)−((c.x·n.x + n.y·c.y) + n.z·c.z);  FUN_180090240(solver)
    t = s + b;   solver+0xd0 = −n
    if t > 0 and i != 100:                                               -- COMISD/JBE, then the count
        FUN_18008f1c0(solver, 1.0);  FUN_18008fc00(solver)
        δ = (double)((c.x·n.x + n.y·c.y) + n.z·c.z) + s
        j = |δ| > (double)1e-4f ? t/δ : 0
        unless A unmovable:  solver+0x40 −= solver+0x80;  solver+0x60 −= solver+0xa0      -- undo the unit test push
        unless B unmovable:  solver+0x50 −= solver+0x90;  solver+0x70 −= solver+0xb0
        FUN_18008f1c0(solver, j)
    FUN_18008f570(solver, solver+0xd0, 0)
FUN_18008dd00(A, solver+0x60, solver+0x40);   FUN_18008dd00(B, solver+0x70, solver+0x50)
FUN_18008deb0(solver, cores)
if (A's byte 0 | B's byte 0) & 0xc0:
    A's env+0xac += 1
    for A, then B:  flags' bits 6–7 = unmovable ? 0 : 1;   +0x120 = +0x140 + +0x120;  +0x110 = +0x130 + +0x110;
                    +0x130..0x13b = 0;  +0x140..0x14b = 0
    cores[0] = A;  cores[1] = B
```

Constants read beside their instructions: `1.2f`; `1e5` and `1.0` as doubles; `−0.1f`, `1e-4f` and `0.01f` widened; `−1e-4f`;
`−1.0f`. **The loop is capped at a hundred pushes and a capped loop skips the final correction.**

**`FUN_18008f570(solver, d, clamp)`** — push the pair apart along `d` until it separates at `solver+0x0`:

```
FUN_180070b20 of each arm through its core's matrix                         -- computed, never read
vA = FUN_180077fa0(A, armA, solver+0x60, solver+0x40);   vB = … B, solver+0x70, solver+0x50
a = (double)−(((vB.y − vA.y)·d.y + (vB.x − vA.x)·d.x) + (vB.z − vA.z)·d.z)
if clamp == 1:  a = MINSD(a, 0)
g = (double)solver+0x0 − a;   if g < 0 or NaN:  return
m = 0
unless B unmovable:  (Δv, Δω) = FUN_180078f50(B, armB, FUN_180070620(B's matrix, −d), −d)
                     q = FUN_180077fa0(B, armB, Δv, Δω);   m = (double)((q.y·−d.y + q.x·−d.x) + q.z·−d.z)
unless A unmovable:  the same for A along d;   m += (double)((q.y·d.y + q.x·d.x) + q.z·d.z)
j = g/(m + (double)1e-15f);   if j < 0 or NaN:  return
unless A unmovable:  p = (float)((double)−d·−j);  (solver+0xa0, solver+0x80) = FUN_180078f50(A, armA, FUN_180070620(A's matrix, p), p)
                     solver+0x40 += solver+0x80;  solver+0x60 += solver+0xa0
unless B unmovable:  p = (float)((double)−d·j);   (solver+0xb0, solver+0x90) = … B;   solver+0x50 += +0x90;  solver+0x70 += +0xb0
```

**`FUN_18008dd00(core, v, ω)`** — the anomaly limits, `env+0x48`, through the environment's `+0x40` object:

```
unless core+0x58 is set and core+0x8 == 0f (UCOMISS/JZ: a NaN also skips):
    if (double)((ω.x² + ω.y²) + ω.z²) > ((double)((float)env+0x110 · limits+0x14))²:  env+0x40 slot 1 (limits, core, ω)
if (double)((v.x² + v.y²) + v.z²) > ((double)limits+0xc)²:  env+0x40 slot 0 (limits, core, v)
```

**`FUN_18008deb0(solver, cores)`** — committing, or holding back the heavier side:

```
A+0x2 += 1 unless A unmovable;   B+0x2 += 1 unless B unmovable                  -- words
if solver+0x18 != 0:
    h = solver+0x8 <= solver+0x10 ? 1 : 0                                          -- COMISD/SETBE: a NaN picks 1
    if cores[h] is not unmovable:
        o = 1 − h
        w = FUN_180077d70(core h, core o, arm h, arm o, core h's +0x140, core h's +0x130, the solver's v and ω of o)
        x = (double)((w.y·n.y + w.x·n.x) + w.z·n.z);   if h == 1:  x = −x
        if !(x >= (double)(solver+0x0·−0.8333333f)):                               -- COMISD/JNC: a NaN takes this
            A's env+0xa4 += 1;   cores[h] = null
            A+0x110..0x12b = 0;  B+0x110..0x12b = 0
            FUN_18008ddf0(solver, o)
            H = core h:  H+0x120 = the solver's v of h − H+0x140;  H+0x110 = the solver's ω of h − H+0x130;  H+0x2 −= 1
            return
A+0x110..0x12b = 0;  B+0x110..0x12b = 0;   FUN_18008ddf0(solver, 0);  FUN_18008ddf0(solver, 1)
```

**`FUN_1800904a0(solver, double p)`** — a direction held inside a cone:

```
v = (float)((double)n·−p + (double)solver+0xd0) per lane;   ℓ = (float)FUN_18006fc90(&v), v read back after
q = |(v.y·s.y + v.x·s.x) + v.z·s.z|, s = solver+0x100;   r = asinf(q)
c = (1f − r²·0.5f) + (r²·(1/24f))·r²
m = q²·(solver+0xf4)² + (solver+0x138)²·c²
if ℓ² > m:   σ = SQRTSS(m);  r' = asinf(σ);  c' = (0.5f − r'²·(1/24f))·r'² − 1f
             solver+0xd0 = (float)((double)v·(double)σ + (double)(float)((double)n·(double)c')) per lane
```

#### Four small helpers and the damping applier read again

- **`FUN_180070130(out, a, n)`**: `k = (n.x·a.x + n.y·a.y) + n.z·a.z` in float, `out = (float)((double)n·(double)−k + (double)a)` per
  lane — `a` less its part along `n`.
- **`FUN_18006dcb0(out, a, b)`** is `a × b` in float: `(b.z·a.y − a.z·b.y, a.z·b.x − b.z·a.x, b.y·a.x − a.y·b.x)`.
- **`FUN_180070950(m, v, out)`** turns a float vector by a double matrix, each row `((v.x·m0 + v.y·m1) + v.z·m2)`, narrowed.
- **`FUN_180078820(core)`** sets `+0x200` and `+0x208` to `env+0x188`.

**`FUN_180077a20(core, double dt, rotation, double speedDamping)`**, which `IvpDamping` ports:

```
a = ((float)((double)r.x·dt), (float)((double)r.y·dt), (float)((double)r.z·dt))
f = !((a.y² + a.x²) + a.z² ≥ 0.5f) ? (1f − a.x, 1f − a.y, 1f − a.z)                     -- COMISS/JNC: a NaN takes this
                                  : (expf(−a.x), (float)exp((double)−a.y), (float)exp((double)−a.z))
k = dt·speedDamping;   k' = k < 0.25 ? 1.0 − k : exp(−k)
ω.x = f.x·ω.x;  ω.y = f.y·ω.y;  ω.z = f.z·ω.z;   v = (float)((double)v·k') per lane
```

**The `x` lane calls `expf` and the other two call `exp` on a widened argument**, so three equal damping values need not give
three equal factors. **`IvpDamping` diverges on four counts**: one factor for all three lanes from `MathF.Exp`; the threshold as
`3·s²` rather than the grouped float sum; the products in float from a float step rather than from the double one; and the
speed factor in float rather than double. *And `MathF.Exp` is the platform library, not this image's `expf`.*

#### The math routines, identified

- **`FUN_1800d4f9c` is `asinf`** — its domain error passes the name `"asinf"` (`180104808`). Under `2^−14` it returns `x`, `±1`
  gives `±π/2` (`3fc90fdb`), and over one is the domain error. Otherwise, with `z = x²` under `0.5` and `z = (1 − |x|)·0.5`, `s =
  √z` at or over it: `w = p/q`, `p = (((−0.013381929 − z·0.0039613745)·z − 0.05652987)·z + 0.1841616)·z`, `q = 1.1049696 −
  z·0.8364113`; under `0.5` the answer is `|x|·w + |x|`; over, with `f` = `s` with its low sixteen bits cleared and `c = (z −
  f²)/(f + s)`, it is `π/4ʰ − ((2s·w − (π/4ˡ − 2c)) − (π/4ʰ − 2f))` (`3f490fda`, `33a22168`), signed like `x`. All float; the
  under-one-half branch is taken by reusing the carry flag of the exponent compare across the rational function.
- **`FUN_1800d40f0` is `expf` and `FUN_1800d3cf0` is `exp`**, the table-driven kind: `n` from `x·64/ln 2` (`expf` rounds it to
  nearest, `exp` truncates), `r = x − n·ln 2/64` (`exp` with the constant in two parts), `2^(j/64)` from a 64-entry table at
  `180108740` (`exp` adds two more tables' entries), and a scale by `2^(n>>6)` applied to the bits. **Each has two paths chosen
  at run time by `DAT_180136418`** — one with separate multiplies and adds, one with fused multiply-adds — and they group the
  series differently: `exp`'s plain path sums `(r + r²(1/2 + r/6)) + r⁴((r/720 + 1/120)r + 1/24)`, its fused path nests all five
  terms in one Horner chain. `DAT_180136418` is set at startup by `__acrt_initialize_fma3`, below.

- **`FUN_1800d4398` is `atan`, with fdlibm's breakpoints and not fdlibm's series.** With `a = |x|`: over `2^62·1.0537`
  (`43d0dc00…`) the answer is `±π/2` (`3ff921fb54442d18`) outright, and a NaN goes to the error path; over `39/16`, `t = −1/a`
  against `π/2` and its low part `3c91a62633145c06`; over `19/16`, `t = (a − 1.5)/(a·1.5 + 1)` against `3fef730bd281f69b` and
  `3c7007887af0cbbc`; over `11/16`, `t = (a − 1)/(a + 1)` against `π/4` (`3fe921fb54442d18`) and `3c81a62633145c06`; over
  `7/16`, `t = ((a + a) − 1)/(a + 2)` against `3fddac670561bb4f` and `3c7a2b7f222f65e0`; otherwise `t = a` against zero. Then
  `z = t·t` and `atan = hi − (((t·z)·P(z))/Q(z) − lo − t)`, **a rational function in place of fdlibm's eleven-term
  polynomial**: `P` nests `z·3f22a75ce41b9f87 + 3f9f2d2116f053f2`, then `3fcc3de43db425c0`, `3fdca6be4c993b3c`,
  `3fd12bcb0a9169f3`; `Q` nests `z·3fa3f197f1e85ed9 + 3fdb2cb05bf9beff`, then `3ff699c644c48d2e`, `3ffd372a17cdf5a0`,
  `3fe9c1b08fda1eec`; each step a multiply by `z` and an add, the order as listed. The sign is restored last. **The low
  parts are one below fdlibm's** in their last hex digit, which is reason enough to carry the image's constants rather
  than fdlibm's.

#### Valve's binary as the oracle (2026-09-13)

**The shipped `vphysics.dll` can be called in process, and that makes it the instrument for every leaf routine.** The
`vphysics-math` probe loads the game's x64 library with `NativeLibrary.Load`, calls routines at their addresses against the
image base `0x180000000`, and writes the fused-path flag in the loaded image to take either path. **Controls first**:
`expf(0)` and `exp(0)` come back exactly one, `asinf(1)` exactly `π/2f` and `atan(1)` exactly `π/4`, so the addresses fit
the installed build; on this machine the flag reads one.

**A sweep against `IvpMath`** — 1.43 million `expf` and `asinf` arguments, three million `exp` arguments on each path, two
million `atan` arguments — **found no difference after one fix**: the fused `exp` path hands every argument under `−744.03`
to its underflow handler, which answers zero, where the plain path answers the least denormal down to `−745.13`; the first
port had not separated them. The binary's two `exp` paths disagree on 3,641 of the three million (`−2.997306`: `…eb17`
against `…eb16`), and its two `expf` paths on none of 1.43 million — so no test can tell `expf`'s paths apart.

**`FUN_180077a20` itself was then called on a stand-in core** — it reads its factors through a pointer and writes only
`+0x130..0x148` — and `IvpDamping.Damp` is pinned to the bits it left, for factors and steps straddling both thresholds.

*Evidence class: differential against the shipped binary, on one machine. What the unread error handlers answer is taken
from what the binary returned, not from reading them.*

#### The impact solver's entry, and the push-out estimate

**`FUN_18008ed60(record, cores, float p5, cp)`** fills a solver on its stack and calls `FUN_18008e290(solver, cores, 1,
(short)record+0x72, p5)` — so the solver's `p3` is always one and its `p4` is the record's impact count:

```
if record+0xa0 (core B) is null:           -- a static second body
    A = record+0x48's core (+0xe8);  B = record+0x98;  armA = &record+0xe0;  armB = &record+0xd0;  n = −record+0x20
else:
    A = record+0x98, or record+0x40's core when that is null;  B = record+0xa0;  armA = &record+0xd0;  armB = &record+0xe0
    n = &record+0x20
solver+0x110/+0x118 = A/B;  +0x120/+0x128 = the arms;  +0x140 = n;  +0x148 = &record+0x30;  +0x130 = record+0x80;  +0xf0 = 0
if A+0x58 or B+0x58 is set:  solver+0x134 = 1f, +0x138 = 0f
else:  t = (√(double)record+0x80 + 1.0)·(double)cp+0x78;  x = (float)atan(t);  x² in float
       c = (1f − x²·0.5f) + (x²·(1/24f))·x²;  solver+0x134 = c;  solver+0x138 = (float)((double)c·t)
       if cp+0x64:  FUN_18008fe70(solver, cp)
```

**The static body is always the solver's `A`**, the normal turned to match. The cone pair `(+0x134, +0x138)` is `(cos x, cos x·t)`
with `x = atan(t)` — *Corrections to earlier readings* above already records that the series is cosine's.

**`FUN_18008fca0(cp, env)` — the float `FUN_18008db40` stores at `record+0x78`**:

```
gap = (double)cp+0x8c;  m = (double)block[1]
p = gap < m ? (m − gap)·((double)(float)env+0x110 + same) : 0, and record+0x78 = 0 when not     -- COMISD/JNC
q = 0
for A = record+0x98 unless cp's first kind (+0x2a) is a ball, then B = record+0xa0 unless the second (+0x52) is:
    s = (double)((ω.x² + ω.y²) + ω.z²)·2.5e-5, MINSD 0.25, SQRTPD
    q = (float)((1.0 − FUN_1800d33b0(√s))·(double)core+0x4·(double)(float)env+0x110 [+ (double)q for B])
return (float)((double)(q + q) + p)
```

*`2.5e-5` is `DAT_1800fd860`, a double.* ~~`FUN_1800d33b0` is another runtime-library routine, unidentified; `core+0x4` is
unread~~ — **both settled since**: `FUN_1800d33b0` is `cos`, identified by calling it in process and ported as `IvpMath.Cos` —
under `π/4` first, whole since (*vphysics' own `sin`, `cos` and `acos`*, below; the push-out's argument never exceeds `√0.25`), and
`core+0x4` is the core's radius `FUN_180078b90` sets (*core+0x54 = 0.5f / core+0x4* above).

#### The entry, ported and pinned (2026-09-13)

**`FUN_1800908d0`, `FUN_18008db40`, `FUN_18008fca0` and `FUN_18008ed60` with `FUN_18008fe70` are ported** —
`IvpContactPoint.SetMaterials`, `Estimate`, `PushOut` and `IvpImpactSolver.Enter` — and the `vphysics-impact` probe's `entry`
mode calls all four in that order on a fabricated contact point, record, two objects with their triangles and friction cores,
three materials and a material manager whose slots are callbacks. **20,000 random entries agree on every lane, with no call
reaching a trap**; `IvpImpactEntryConformanceTests` replays 96 of them and five targeted cases. **Each case also calls
`FUN_18008fe70` alone** on a zeroed solver holding only the record's elasticity, and compares `+0xf0`, `+0xf4` and `+0x100`
— because a sabotage round reddened 12 of 13 and the thirteenth, the cone series' product regrouped from `(x²·(1/24f))·x²` to
`(x²·x²)·(1/24f)`, survived both every drawn case and that direct lane: the two groupings round alike for nearly every `x²`,
and a one-ulp cosine rounds away inside a solve. Four cases found by search, an axis of length exactly one with only the first
material's block running and the two groupings' tangents apart, redden under it and nothing else does. What the reading
needed that earlier sections left open:

- **`block[0x48]` is `(float)(d·20.0 + (double)block[0x43])`** — `DAT_1800fcfb0` is `20.0`, read from `FUN_180098fd0`'s tail;
  `22·d` above was its value, not its arithmetic.
- **A triangle's material index is its header word's bits 24–30** (`FUN_1800863d0`: the edge's address masked to its triangle,
  byte 3, `& 0x7f`). `PhysicsHull` now carries it per triangle beside the pierce field.
- **`FUN_180070130` is the arithmetic the solver inlines for its slide** — each lane `(float)((double)n·(double)−k + (double)v)`
  with `k` the float dot — so the port has one `Across` for both.
- **`FUN_18008fe70` calls both materials' slots before its length test**, so a material is asked even when its axis is skipped;
  the calls are pure in the fabricated materials, and whether vphysics' are is unread.
- **The entry reads through a null core when neither of the record's cores is movable** (`CMP [R8+0x58]` with `R8 = 0`); the port
  refuses that case rather than answering it.

*Not read yet*: vphysics' material manager and material classes, whose answers decide every friction and elasticity; what
increments `record+0x72`; what sets `cp+0x64`.

*Evidence class: read from the disassembly; differential against the shipped binary for every lane the four routines write,
with the materials' answers fixed on both sides.*

#### vphysics' material manager and its materials (2026-09-13)

**The manager at `env+0xe8` is one global, `180120be8`**, built at load by `FUN_180001840`: IVP's base constructor
(`FUN_1800911f0`, vtable `1800ec4f0`), then vphysics' vtable `1800ec560`; `+0x10` is the surface-props object at
`180120b38`, and `+0x18` a table of 128 words filled with their own indices. vphysics overrides slots 1–3 and keeps IVP's 4
and 5:

```
slot 1  FUN_180019370(manager, object, position, index):  index ≤ 0x7f → index = table[index]
        m = props slot 10 (+0x50)(index);  null → m = props slot 10(props slot 3 (GetSurfaceIndex)("default"))
slot 2  FUN_1800192e0(manager, record):  f = 1f;  FUN_180024080(&f, record+0x40, record+0x48, &record+0x20) → return (double)f
        p = FUN_180091340 = m₀.slot1 · m₁.slot1;  p < 0 or NaN → 0 (COMISD/JC);  else MINSD 1.0
slot 3  FUN_1800192b0(manager, record):  e = FUN_1800912f0 = m₀.slot3 · m₁.slot3;  MAXSD 0 (a NaN gives 0), MINSD 1.0
```

Slot 3 of the props object is `GetSurfaceIndex` in `IPhysicsSurfaceProps`'s order (`public/vphysics_interface.h:968`); slot 10
is past the public interface, and *that it hands out the surface's material is INFERRED from its use*. IVP's own slots, which
vphysics replaces: slot 1 (`FUN_180091390`) lazily builds one `IVP_Material_Simple` (`0x38` bytes, vtable `1800fd888`, name
"Simple material") with `+0x10` and `+0x20` at `0.5`, `+0x28`, `+0x30` and `+0xc` zero; slots 2 and 3 the same products
unclamped; slots 4 and 5 the sums of the materials' slots 4 and 5.

**`FUN_180024080` zeroes the friction** when either object's core has `+0x58` set: the first object — the first synapse's,
then the second's — whose physics object (`object+0x100`) has bit 6 of its byte `+0x48`, with the normal's float `|n|²` at
least `1e-4f`, turned into that object's core frame (`FUN_180070620`), `|x|` widened above `DAT_1800ee1a8` —
`0.25881904f`, `sin 15°`, widened — writes `0f` and answers true. *What that bit is, is not read.* A ragdoll's cores have no
`+0x58`, so for a corpse it never fires.

**vphysics' material is vtable `1800ec528`, one inside each 0x78-byte surface entry** `FUN_180018740` parses: slot 1 answers
`(double)+0x14`, slot 3 `(double)+0x18`, slot 4 `(double)+0x24`, slots 2 and 5 zero (`FUN_180007e40`: `XORPS`, `RET`), and
slot 6 the name through the word at `+0x10`. **`+0x14..+0x24` is `surfacephysicsparams_t`** — friction, elasticity, density,
thickness, dampening (`public/vphysics_interface.h:882`) — *matched by order and by the parser's 16-byte copies, not by reading
its key strings.* So **a corpse on the world rubs at `clamp(f₀·f₁, 0, 1)` and bounces at `clamp(e₀·e₁, 0, 1)`** of the two
surfaces' `surfaceproperties` values, and with slot 2 answering zero the axis friction of `FUN_18008fe70` has nothing to
multiply. *Whether any entry's `+0xc` is set, so that its blocks run at all, is not read.*

**The surface-props object is `180120b38`** (vtable `1800ec598`; the manager is its `+0xb0`, so the manager's table at `+0x18` is
the props' `+0xc8`). Its entries are a vector at `+0x70` with the count at `+0x80`, and **each 0x78-byte entry is itself the
material** — slot 10 (`FUN_1800181d0`, `GetIVPMaterial`) answers `&entries[index]`:

```
index > 0x7f:  index = (index == 0xf000) ? props+0x1cc : 0          -- 0xf000 is GetSurfaceIndex("$MATERIAL_INDEX_SHADOW")
index < 0 or index > count − 1:  null;  else  entries + index·0x78
```

Slot 11 (`FUN_180018210`) is its inverse, `−1` off the vector; slot 9 (`GetPhysicsParameters`) copies the 20 bytes at `+0x14`;
slot 8 (`FUN_180019220`, `SetWorldMaterialIndexTable`) writes `min(size, 128)` words of the caller's ints into that shared
table; and slot 3 (`FUN_180018500`, `GetSurfaceIndex`) answers `0xf000` for `$MATERIAL_INDEX_SHADOW`, otherwise finds the name's
symbol and walks the entries for the one whose word `+0x10` holds it. The symbol table is built case-insensitive
(`FUN_180001840` passes `1` as the fourth argument to its constructor `FUN_1800b9b40`).

**An object's own material — `object+0xd0` — is the surface its creator names**: `FUN_18001c9d0` fills the IVP template's
`+0x18` with `GetIVPMaterial(index)` for a non-negative surface index, else with `GetIVPMaterial(GetSurfaceIndex("default"))`
(`18001ca0c..18001ca39`). vphysics reaches props through the pointer at `180120b30`. And the world's table is
`game/shared/physics_shared.cpp:674-680`: a map's `materialtable` block becomes 128 surface indices, zero where unnamed.

**How `ParseSurfaceData` (`FUN_180018740`, props slot 1) builds an entry** — read by a delegated pass over the whole routine
and checked here at the three places that decide values:

- **A block starts from the surface of the same name, else from `default`, else from zeros** (`18001883a..1800188cc`: slot 3 on
  the block's name, then on `"default"`, through `GetIVPMaterial`'s index rules, copying `+0x14..+0x73` into the staging entry).
- **Keys apply in file order, and `base` is one of them** (`180018901`): it copies the named surface's whole `+0x14..+0x73` at the
  point it appears, so keys before it are overwritten and keys after it win. Float keys go through `atof` and are narrowed
  (`CVTSD2SS`): `friction` `+0x14`, `elasticity` `+0x18`, `density` `+0x1c`, `thickness` `+0x20`, `dampening` `+0x24`.
- **The closing brace writes the staging entry back over an existing surface of that name, or appends a new one** — a later
  file redefines in place, keeping every field it does not name.
- **Once, at the end of the first parse** (`18001907c`: the byte `props+0x1c8`), it appends a shadow surface named by slot 14 —
  `$MATERIAL_INDEX_SHADOW` for `0xf000` — copied from `default` with friction `0.8f` and elasticity `0x3a83126f` (`0.001f`), and
  stores its index at `props+0x1cc`: the writer `GetIVPMaterial`'s shadow index reads.
- A new entry's `+0xc` is zero (`180018781`), so **no vphysics surface has an axis friction** and `FUN_18008fe70`'s blocks never
  run for a surface's material.

**Ported: the manager and the lookups** — `VphysicsSurfaceProps` and `VphysicsSurface`, with the object's physics-object flag as
`IvpCollisionObject.PhysicsFlag48Bit6`. The `vphysics-materials` probe calls `FUN_1800181d0`, `FUN_180019220`, `FUN_180019370`,
`FUN_1800192e0` and `FUN_1800192b0` on fabricated props, surfaces, records and objects with the image's own manager and material
tables: **200,000 drawn cases agree on every answer**, after one fix the sweep itself found — the override's threshold written
as the float literal `0.25881904f` rather than the dumped bits differed on 2. `VphysicsSurfacePropsConformanceTests` pins 23
rows the probe's `list` printed.

**The parser is ported too** — `VphysicsSurfaceProps.ParseSurfaceData`, with `FUN_18002e6c0` (a key and a value, each lowercased by
`FUN_1800b9a10`; a lone `}` reads no value) and `FUN_180003640`, Source's `ParseFile`: every byte up to `0x20` **and every byte
from `0x80`** is whitespace, because the bytes are compared signed; `//` and `/* */` are comments; `{}()':` are tokens of their
own; a token stops at `0x3ff` bytes. **Its oracle is the library's own props object**, the global behind `180120b30`, which the
loaded image built at load: `vphysics-materials parse` hands it the game's `surfaceproperties_manifest.txt` files in order —
`surfaceproperties.txt`, `_hl2`, `_tf`, 83, 93 and 100 surfaces with the shadow at 82 — then eight edge texts, and reads every
surface back through slots 7 and 9. **All agree, after one fix**: the C runtime's `atof("nan")` is positive with every payload
bit, `0x7fffffff` once narrowed, where `double.NaN` narrows to `0xffc00000`. `VphysicsSurfaceDataConformanceTests` pins the
edge texts parsed from nothing. `SurfaceTable` still diverges from all of it — no `base`, no copy from `default`, `1.0` where the
engine starts from `default` or zero, no shadow surface, archive order instead of the manifest's — and was what the running
path read. **It is gone**: `GameContent` now parses the manifest's files in order through `ParseSurfaceData`, and a ragdoll body's
friction is `VphysicsSurfaceProps.ObjectMaterial(surfaceprop)` — the name, else `default`, as `ragdoll_shared.cpp:194-197` and
`FUN_18001c9d0` resolve it. **Measured on the game's files, and it moves a number the owner can see:** TF2's `flesh` block names
no `friction`, `elasticity` or `base` in any of the three files, so the engine's `flesh` is a copy of `default` — friction `0.8`,
elasticity `0.25` — where `SurfaceTable` closed the block with its own `1.0`. **Every player corpse element rubbed a quarter harder
than the engine's** on the running path until now. (`concrete` 0.8/0.2 and `ice` 0.1/0.1 name their keys and did not move.)

#### The impact loop — `FUN_180090700`, `FUN_180090bd0` and `FUN_18008da40` (2026-09-13)

**Read from the disassembly.** `FUN_18008ef60` hands `FUN_180090700` an empty block on its stack — `+0x0` the environment, `+0x8`
a pass count, three vectors `{word capacity, word count, pointer}` at `+0x10` (cores added), `+0x20` (cores brought to the event)
and `+0x30` (pairs), and `+0x40` the friction system:

```
FUN_180090700(block, mindist, system, pair, cp):
    block+0x8 = 0;  block+0x40 = system;  block+0x0 = pair+0x38's core's +0x10
    core of cp's first object, unless flags & 0x12:  FUN_18008da40(block, core, pair)
    pair+0x38, unless flags & 0x12:  push on +0x20
    the same for cp's second object's core and pair+0x40
    push pair on +0x30
    every contact of the pair but cp, last to first:  FUN_18008d0c0(contact, env);  FUN_1800908d0(contact, record)
                                                     record+0x76 == 1 → FUN_180083e40(system, contact)
    while FUN_180090bd0(block) == 1:  block+0x8 += 1;  n += 1;  block+0x8 > 0x1388 → mindist slot 0(mindist, 1), stop
    env+0x98 += n + 1;  tail into FUN_1800909d0(block)

FUN_180090bd0(block):  best = (double)block[0x42];  none
    every pair of +0x30, last to first, unless BOTH cores' (flags >> 5 | flags & 2) & 6:
        every contact, last to first:  record+0x74 != 1 → env+0xa8 += 1, FUN_18008db40(contact)
            e = (double)record+0x7c;  best > e → this contact and pair;  best = MINSD(best, e)    -- a NaN e makes best NaN
    none → 0
    record+0xa0's core, then +0x98's:  unless +0x260 is set:  push on +0x20;  state +0x1 < 8 and not flags & 0x10 → FUN_180078d60
    record+0x72 += 1;  FUN_18008ed60(record, cores, record+0x78, contact)
    each core the solve left, second then first, unless flags & 0x12:
        +0x260 set and its +0x30 zero → FUN_18008da40(block, core, pair)
        else every contact of FUN_180077f00(core, system):  record+0x74 = 0
    → 1

FUN_18008da40(block, core, pair):  push core on +0x10;  core+0x260's +0x30 = 1
    every pair of the system (+0x6a count, +0x70 array), last to first, touching core, not pair, not already on +0x30:
        FUN_180083b30(that pair, system) > 0 → push it on +0x30
```

**So the loop is event-ordered by estimate, not by contact order**: every pass re-estimates what the last solve invalidated,
solves the one contact predicted to close first below the margin, and pulls in each newly moved core's other pairs. **A NaN
estimate ends the search for that pass** — `MINSD` answers its second operand, so the best becomes NaN and no later compare can
beat it. The cap of 5,000 passes asks the mindist's slot 0 with one; *what that slot does is not read.* **Not ported.**

**`FUN_1800909d0(block)`, the tail, read whole:**

```
every core of +0x20, last to first:  +0x260 set and its +0x30 zero → FUN_180079120(core)  (put back);  core+0x260 = null
dt = (double)(float)(env+0x190 − env+0x188);  a local vector of 256 inline entries
every core of +0x10, last to first, unless flags & 2:
    FUN_180099a00(core, {(float)dt, dt > 1e-10 ? (float)(1.0/dt) : 1e10f}, &local)     -- COMISD/JBE: a NaN dt takes 1e10f
    core+0x260 = null;  every contact of FUN_180077f00(core, system):  record+0x74 = 0
FUN_18009a690(env, &local);  free the local vector unless it is still inline
every core of +0x10, last to first, unless flags & 2:  FUN_1800792b0(core)
```

**So a core the loop only brought to the event and never moved is put back exactly as it was**, and only the cores an impact
actually moved are stepped — each over the environment's whole remaining PSI span, through the same integrator a PSI uses — then
re-checked by the scheduler and stamped with the impact counter. `FUN_180077f00(core, system)` is the per-system record: through
the hash at `core+0x60` for an unmovable core, else `core+0x60` itself when its `+0x10` is the system.

#### Filing a contact into a friction system — `FUN_180090e50(mindist, &system, &built, unit, rebuild)` (2026-09-13)

**Read from the disassembly**, with every callee named below:

```
A, B = the mindist's synapse objects ((flags >> 8) & 3 and its flip);  their friction cores, object+0xf0
M = the one of the two NOT flagged unmovable (bit 1) — A's when both or neither;  S = the other
info = M+0x60 (FUN_180078460);  env = M+0x10
cp = FUN_18008c4b0(mindist, &made)
not made:  *system = info+0x10;  *built = 0;  rebuild and cp → FUN_18008d0c0(cp, env), FUN_1800908d0(cp, record);  return cp
made:  rebuild → the same two calls;  the environment's listeners (FUN_180081e50) and each object flagged 0x2000 (FUN_180088310)
       hear {env, record, cp};  *built = 1
    info set, system T = info+0x10:
        S's record in T (FUN_180077f00) → file
        S movable with its own record → merge S's system into T (FUN_180086240) → file
        else a new 0x18 record for S in T (FUN_180076690), and S joins T (FUN_180087bf0) → file
    info null:  a new record for M
        S movable with its own record, system T:  M's record in T;  M joins T → file
        else  T = a new 0x90 system (FUN_1800879e0, env);  a new record for S;  both records attached;
              cp into T (FUN_180087c90, FUN_180088090);  M joins T;  S joins T
    file:  cp into T's contact list (FUN_180087c90) and its pair (FUN_180088090)
    cp onto both records' contact vectors (FUN_180054640);  *system = T;  FUN_180083a60(cp)
    unless either core is unmovable or they share one:  merge the two cores' simulation units (+0x1f8) into the one passed
        as unit (FUN_180074e40), then destroy (FUN_1800747a0) and free (0x48) the other
```

- **`FUN_180076690(core, record)`**: an unmovable core keeps its records in a hash at `+0x60` (`0x20` bytes, `FUN_180071e80`,
  inserted by `FUN_180072070` under the record's system); a movable core holds its one record there.
- **`FUN_180087bf0(system, core)`**: onto the cores (`+0x48` capacity, `+0x4a` count, `+0x50` elements); unless unmovable, onto the
  movable cores (`+0x58`, `+0x5a`, `+0x60`) and into the system's three controller bases (`FUN_1800748b0` with `+0x10`, the system
  itself and `+0x20`); `+0x78 += 1`.
- **`FUN_180087c90(system, cp)`**: `cp+0xc0 = system`; `cp` at the head of the list at `+0x40` (`cp+0x0` next, `cp+0x8` previous);
  `+0x7a += 1`.
- **`FUN_180088090(system, cp)`**: the pair of the two contact objects' `+0xe8` cores, found in either order (`FUN_1800863f0`) or
  made (`0x50` bytes, `FUN_1800830d0`, `+0x38`/`+0x40` the cores, added by `FUN_1800833d0`); `cp` onto its contact vector. **The
  pair is keyed by the physical cores, the system by the friction cores.**
- **`FUN_1800850b0(system, a, b)`** is the same search as `FUN_1800863f0`, answering null rather than asserting.
- **`FUN_180083a60(cp)` writes `cp+0x64`** — one when either synapse's material (`FUN_18008fb60`, the lookup `FUN_1800908d0` makes)
  has `+0xc` set, the reader `FUN_18008ed60` gates `FUN_18008fe70` on — **and `cp+0x60`**: `1 / (m₀m₁ / (m₀ + m₁))` over the two
  physical cores that move (`flags & 0x12` clear), or `1 / m` when one does, each `m = FUN_180077840(core, arm)`.
- **`FUN_180077840(core, r)`**: with `x², y², z²` the arm's squares in float, `1.0 / (MAXSD(MAXSD((double)((x² + z²)·Iy⁻¹),
  (double)((y² + z²)·Ix⁻¹)), (double)((x² + y²)·Iz⁻¹)) + m⁻¹)` — `1.0` in place of `m⁻¹` for a core flagged `0x10`. A contact's
  mass along its worst axis, not along its normal.

**The records themselves:**

- **A friction system is `0x90` bytes** (`FUN_1800879e0(system, env)`): three controller tables — `1800fd638` at `+0x0` with the
  environment at `+0x8`, `1800fd5f8` at `+0x10` and `1800fd5a8` at `+0x20`, each followed by the system itself — the contact list
  head `+0x40`, the cores `+0x48`, the movable cores `+0x58`, the pairs `+0x68` (count `+0x6a`, elements `+0x70`), the core count
  `+0x78`, the contact count `+0x7a` and a byte `+0x80`. **The three tables differ in slot 4** (`1800843c0`, `180084320`,
  `180084240`) and slot 5 (`180088be0`, `18000a6f0`, `180088bd0`); slot 7 of the first is the deleting destructor.
- **A pair is `0x50` bytes** (`FUN_1800830d0`): its contact vector at `+0x0`/`+0x2`/`+0x8`, `+0x20 = 1`, `+0x28` a time starting at
  `DAT_1800fd590` = `−1000.0`, `+0x30 = 0`, and its two physical cores at `+0x38`/`+0x40`; `FUN_1800833d0(system, pair)` appends it
  and tells the environment's listeners (`FUN_180081f10`).
- **`FUN_180086240(target, source)` merges two systems**: every contact of `source`'s list is unlinked (`FUN_180088ce0`), taken out
  of its pair (`FUN_180088130`) and filed into `target` (`FUN_180087c90`, `FUN_180088090`); then every core of `source`, last first —
  a core already in `target` moves its contacts into its `target` record and loses the `source` one (`FUN_180077c10`); otherwise
  its record is detached (`FUN_180079180`), pointed at `target`, reattached, and the core leaves `source` (`FUN_180088c80`) and
  joins `target` — and `source` deletes itself through slot 7.
- **`FUN_1800748b0(core, controller)`** appends the controller to the core's (`+0x1e0` capacity, `+0x1e2` count, `+0x1e8`), finds or
  makes (`FUN_180074820`) the controller's `0x28`-byte entry in the core's simulation unit (`+0x1f8`; entries `+0x3a`/`+0x40`),
  appends the core to that entry's cores (`+0x8`/`+0xa`/`+0x10`) and tails into `FUN_180075990(unit)`.

**The system is a controller named `sys:friction`** (slot 6, `1800fd5e8`), filed three times at different priorities (slot 5),
and each filing's slot 4 is what it does every PSI — `event` its argument, `+0x8` the environment and `+0x10` the simulation unit:

```
priority 600,  FUN_1800843c0(system, event):
    one contact or none:  cp = the list head;  FUN_180083970(cp, ((event[0]² · cp+0x60) · (cp+0x88 · cp+0x78)))  -- all float
                          cp+0x64 → FUN_180085100(cp, event)  else  FUN_1800857c0(cp, event)
    more:  FUN_1800836b0(system, event);  every pair, last first:  pair+0x20 −= 1;  reaching 0 → FUN_180084680(pair, env+0xf0), pair+0x20 = 5
priority 0,  FUN_180084320(system+0x10, event):
    one contact or none → FUN_180084490(that)  else  FUN_1800a9bf0
    no contacts left:  forget the system and delete it (slot 7)
    else, when +0x80 is set:  clear it;  r = FUN_1800877b0(system);  r → FUN_180086e80(system, r), r = its unit
    either way the unit's dword loses bit 9 and gains bit 8
priority 2000,  FUN_180084240(system+0x20, event):
    one contact or none → FUN_18008d0c0(list head, env)            -- the record rebuilt, nothing else
    else  FUN_180088ae0(system);  unit dword & 0x3000 → every pair's +0x30 = 0;  unless & 0xc00 → FUN_180086b40(system)
```

**The other controllers a corpse's unit holds, and the order a PSI runs them** (slot 5 read as bytes, 2026-09-14):

| controller | table | slot 4 (the PSI's work) | slot 5 | priority |
|---|---|---|---|---|
| friction, base `+0x20` | `1800fd5a8` | `180084240` | `180088bd0`: `B8 D0 07 00 00 C3` | 2000 |
| gravity, the environment's `+0x0` | `1800ea728` | `180074c80` | `180007e90`: `B8 E8 03 00 00 C3` | 1000 |
| friction, base `+0x0` | `1800fd638` | `1800843c0` | `180088be0`: `B8 58 02 00 00 C3` | 600 |
| vphysics' constraints (`FUN_18003c780`'s table, from `1800eeb10`) | `1800eeb10` | `18003cdf0` | `18003cf80`: `B8 95 01 00 00 C3` | 405 |
| friction, base `+0x10` | `1800fd5f8` | `180084320` | `18000a6f0`: `33 C0 C3` | 0 |

`FUN_180075990` sorts a unit's entries ascending and `FUN_180075c80` walks them last first, so **one PSI is: the records rebuilt and
the pushes' work banked, gravity, friction, the constraints, then the normal pushes** — top to bottom as the table reads. A core
gets the gravity controller from its constructor (`FUN_1800782d0`, when its third argument is set: `[[core+0x10]]`, the
environment's first field), a unit of its own (`FUN_180074770`, state `8`), and `core+0x1` = 8. *The constraint table's start is
inferred from the slots every controller table above shares — `180007ea0` at slot 1 and `1800073e0` at slot 3.* Its slot 4 is
`48 8B 01 48 FF 60 40` — `MOV RAX,[RCX]; JMP [RAX+0x40]`, a tail call into its own slot 8, which is `FUN_18003c780`, the solve
`IvpConstraintGroup` ports, held at `1800eeb50`. *The routine's three other data references (`18011306c`, `1801130e0`,
`18013a5a0`) are not read.*

**So the pair counter at `+0x20` fires every fifth PSI** (it starts at one, so the first fires at once), and a lone contact takes a
different, cheaper path at every priority. *Not read yet: the routines each branch calls.*

#### One resting contact, instruction by instruction (2026-09-13)

**`FUN_1800857c0(cp, event)`** — `event[0]` is scaled into a limit and `event[1]` multiplies a distance into a velocity, so they
read as the step and its inverse, **and they are**: the unit manager's PSI `FUN_180075a90` builds the event on its stack as
`(float)env+0x108`, `(float)env+0x110` and the environment, then hands it to `FUN_180075c80` for each unit in its list:

```
L = (double)((cp+0x88 · cp+0x78) · event[0])                          -- float products
L < (double)1e-6f → 0f                                                -- COMISD/JC: NaN too
record = cp+0x70;  A = record+0x98;  B = record+0xa0
A with +0x58 set, no B, and cp's next contact (cp+0x0) absent or with +0x88 zero → FUN_180085a80(cp, A, record, event, &out); true → out
frame = FUN_18009ca70(A, B, record, &record+0xb0, &record+0xc0)       -- the two tangents: span and cross span
t = ((double)(float)(event[1]·cp+0x68 − frame v₀), (double)(float)(event[1]·cp+0x6c − frame v₁))
M⁻¹ = FUN_1800868d0(frame+0xf0, frame+0xf8, frame+0xf8, frame+0x118);  singular → 0f
p = ((float)(M⁻¹₁·t₁ + M⁻¹₀·t₀), (float)(M⁻¹₃·t₁ + M⁻¹₂·t₀))
|p|² in float, widened, > L² → p ·= (double)FUN_18006edb0(|p|²)·L, each lane narrowed
FUN_18009c620(frame, A, B, p)
e = (float)(√(double)((x² + y²)·(float)(((double)event[0]·(double)|p|²)·(double)event[0]))·0.5)   -- x, y the slide pair
return e − cp+0x84;  cp+0x84 = e
```

**`+0x68` and `+0x6c` are the slide distances** `FUN_18008d0c0` advances along the record's two spans — *Friction is a
WARM-STARTED 2×2 SOLVE* below read them from the decompiler as a stored impulse. **So static friction is a spring back to where
the contact began**: the target velocity is the slide times the inverse step, less what the pair already moves at.

- **`FUN_18009ca70(frame, A, B, record, s, c, third)`** keeps the directions, zeroes the matrix `+0xf0..0x14f` and the velocities
  `+0x150`/`+0x154`, and runs `FUN_18009d010` for A with sign `1f` into rows `+0x20` and masses `+0x90`, and for B with
  `DAT_1800ea9f8` = `−1f` into `+0x50` and `+0xc0`.
- **`FUN_18009d010(frame, record, core, rows, masses, sign)`**, per direction `d`: `r = (float)(record position − core+0xf0)` in
  double; `w = r × d` in float, turned into the core's frame by the transpose of its matrix at `+0x90` (each lane
  `((w.x·m₀ᵢ + w.y·m₂ᵢ) + w.z·m₄ᵢ)` in double, narrowed); `rows = (w′, 1f)`; `masses = (w′ ⊙ I⁻¹, m⁻¹)`, each narrowed. The diagonal
  term `+0xf0` (or `+0x118`) gains `(double)((m₀r₀ + m₁r₁) + (m₃r₃ + m₂r₂))`; the off-diagonal `+0xf8` gains the second direction's
  masses against the first's rotational rows only; the velocity `+0x150` (or `+0x154`) gains `sign·((d·v) + (ω·w′))` in float.
- **`FUN_1800868d0(a, b, c, d)`**: `det = a·d − b·c`; `det² < DAT_1800fd3e8` (about `1e-38`, NaN too) → refuse; else
  `(d, −c, −b, a)/det`, each a double product.
- **`FUN_18009c620(frame, A, B, p)`** writes straight into velocity and spin: A's `v += (float)((double)(float)(s·(p₀·m⁻¹)) +
  c·(p₁·m⁻¹))` per lane, `ω += (float)((double)(float)(masses₀·p₀) + masses₁·p₁)`; B the same with `p` negated.
- **`FUN_180083970(cp, budget)`**, the lone contact's first call: when `|slide|²` (float, widened) exceeds `(double)(budget² +
  1e-6f)`, `k = FUN_18006edb0(|slide|²)`; `cp+0x91 = 1`; `cp+0x7c += (float)(((k·|slide|² − budget)·cp+0x78)·cp+0x88)`; the slide
  pair `·= budget·k` — **it lets the contact slip**, pulling the spring's anchor to the friction limit and keeping the work done.

**`FUN_1800836b0(system, event)`, the many-contact driver, read from the disassembly — and the budget is per PAIR**, not per
system as *There are TWO contact solvers* read it:

```
every pair of the system (+0x6a, +0x70), last first:
    sum = Σ over the pair's contacts, last first, of (cp+0x88 · cp+0x78) · cp+0x60        -- float, from +0.0
    budget = (sum · event[0]) · event[0];  limit² = (double)(budget² + 1e-6f)
    every contact, last first:  the slip of FUN_180083970 inlined with this budget
                                cp+0x64 → FUN_180085100(cp, event)  else  energy += FUN_1800857c0(cp, event)
    energy > 0 → pair+0x30 += energy                                                          -- COMISS/JBE: NaN skips
```

**The lone contact forms its budget in another grouping** — `(event[0]² · cp+0x60) · (cp+0x88 · cp+0x78)` against the driver's
`((cp+0x88 · cp+0x78) · cp+0x60) · event[0] · event[0]` — and float products round apart, so the two paths are two ports.

**`FUN_180084490(system+0x10, event)`, the lone contact at priority 0, is the writer of `cp+0x88`:**

```
s = the closing speed FUN_18008db40 forms (n·v and t·ω per movable core, the second negated)
g = (double)(float)(block[0x43] − cp+0x8c);  k = g ≥ 0 ? 1.0 : 20.0 (DAT_1800fcfb0)          -- COMISD/JNC: NaN takes 20
f = ((k · g) + s) · (double)record+0x90
f > 0 → cp+0x88 = (float)((double)event[1] · f);  FUN_180083420(cp)      else  cp+0x88 = 0
!(block[0x47] > cp+0x8c) or record+0x76 == 1 → FUN_180083e40(system, cp)                   -- COMISS/JBE: NaN removes
```

**So `cp+0x88` is the normal push a resting contact needs this step** — the gap's shortfall below `block[0x43]` (twenty times as
stiff once it penetrates) plus the closing speed, through the record's virtual mass, per unit time — and the friction limit is
that push times the friction factor. A contact is dropped once its gap reaches `block[0x47]`.

`block[0x47]` is `(float)(d·DAT_1800fdf80 + (double)block[0x43])` with `DAT_1800fdf80` = `2.5` — `4.5·d`, read from
`FUN_180098fd0`'s tail.

**`FUN_180083420(record, f)` applies that push at once, into velocity and spin** — `f` the double before `event[1]`:

```
first core (+0x98):   ω += (float)((double)(float)(t·I⁻¹) · −f) per lane;   k = −((double)m⁻¹ · f);   v += (float)((double)n·k) per lane
second core (+0xa0):  ω += (float)((double)(float)(t′·I⁻¹) · f);            k = (double)m⁻¹ · f;       v += (float)((double)n·k)
```

**`FUN_180084680(pair, arena)`, every fifth PSI, shares the slides of parallel contacts**: the pair's contacts go into an arena
array and a zeroed stack vector per contact; for each `i < j` whose normals satisfy `||nᵢ·nⱼ| − 1| < DAT_1800f5100` (the dot
in float, its absolute value widened), `FUN_180084c70(cpⱼ, cpᵢ, …, 1/(count + 1e-19))`; then each contact's slide gains its
vector turned onto the record's spans — `+0x68 += (s·u)` and `+0x6c += (c·u)` in float. Read whole:

```
FUN_180084680(pair, arena):
    n = pair+0x2;  cps = the pair's contacts copied into n arena pointers;  k = 1.0 / ((double)n + 1e-19)
    u = n three-float vectors on the stack, zeroed
    every i < n−1, every j in i+1 .. n−1   (R = cp+0x70, the record):
        d = (Rⱼ.n.y·Rᵢ.n.y + Rⱼ.n.x·Rᵢ.n.x) + Rⱼ.n.z·Rᵢ.n.z              -- float, normal at +0x20
        |(double)|d| − 1.0| < DAT_1800f5100 → FUN_180084c70(cpⱼ, cpᵢ, &uⱼ, &uᵢ, k)
    every r:  cp+0x68 = (R.b4·u.y + R.b0·u.x) + (R.b8·u.z + cp+0x68)
              cp+0x6c = (R.c4·u.y + u.x·R.c0) + (R.c8·u.z + cp+0x6c)       -- all float
    (DAT_1800f5100 = (double)0.001f)

FUN_180084c70(a, b, ua, ub, k) — two near-parallel contacts meet halfway along the line between them:
    σ = (a+0x20)+0xe8 == (b+0x20)+0xe8 ? 1.0 : −1.0                        -- the same core on both first sides, or swapped
    d = (float)(Ra.p − Rb.p) per lane, p the record's double position at +0x0;  FUN_18006dff0(&d)
    wa_l = (float)((double)(float)((double)Ra.s_l·(double)a+0x68) + (double)Ra.c_l·(double)a+0x6c)   -- s at +0xb0, c at +0xc0
    pa = (wa.y·d.y + wa.x·d.x) + wa.z·d.z;   qa_l = (float)((double)d_l·(double)pa)
    wb, pb the same for b;   qb_l = (float)((double)d_l·((double)pb·σ))
    m_l = (qb_l + qa_l)·0.5f
    ua_l = (float)((double)(m_l − qa_l)·k) + ua_l;   ub_l = (float)((double)(m_l − qb_l)·(σ·k)) + ub_l
FUN_18006dff0(v) → 0 leaving v alone when (double)(float)((x² + y²) + z²) < 1e-19 (NaN too), else
    s = FUN_18006ecf0(that);  v_l = (float)((double)v_l·s);  → 1
```

**The priority-2000 routines:**

```
FUN_180088ae0(system) — the work the normal pushes did:  every pair, last first:
    acc = 0f;  every contact, last first:  g = cp+0x8c;  FUN_18008d0c0(cp, env);  acc = acc + (g − cp+0x8c)·cp+0x88   -- float
    acc > 0 → pair+0x30 = acc + pair+0x30
FUN_180086b40(system) — that work paid back as damping:  every pair, last first:
    e = (float)((double)pair+0x30·(double)env+0x1b0);  pair+0x30 = e          -- env through the first core's +0x10
    e < 0 (NaN too), or either core's +0x58 set → next
    FUN_180086670(&L, pair)
    for (a, b, c, d, f) = (L+0x58, L+0x70, L+0x78, L+0x68, L+0x60) and then (L+0x30, L+0x48, L+0x50, L+0x40, L+0x38):
        t = a/(b + c);  r = a − c·t;  E = MAXSD(((d·a)·a + 1e-19) − (((f·(b·t))·(b·t)) + ((d·r)·r)), 0)·0.5
    total = E₂ + E₁;  x = MINSD(total·(double)0.1f, (double)e)
    total < 1e-19 (NaN too) → x = 0   else   FUN_180083f70(&L, x/total)
    env+0x78 = x + env+0x78;   pair+0x30 = (float)((double)pair+0x30 − x)
FUN_180086670(L, A, B) — the pair's relative motion:
    B flagged 0x12 → P = B, Q = A;  else P = A, Q = B;   L+0x90 = P, L+0x98 = Q
    L+0x0 = Q.v − P.v (float, +0x140);  L+0x30 = FUN_18006fc90(&L+0x0)                    -- unit direction, its length
    Δω = FUN_180070950(Q+0x90, Q.ω) − FUN_180070950(P+0x90, P.ω) (float);  L+0x58 = FUN_18006fc90(&Δω)
    L+0x10 = FUN_180070620(P+0x90, Δω);  L+0x20 = FUN_180070620(Q+0x90, Δω·−1f)
    FUN_180086440(P, &L+0x10, &L+0x60, &L+0x70);  FUN_180086440(Q, &L+0x20, &L+0x68, &L+0x78)
    L+0x40 = (double)Q+0x2c (mass);  L+0x50 = (double)Q+0x4c (inverse mass)
    P not flagged 0x12:  L+0x38 = (double)P+0x2c;  L+0x48 = (double)P+0x4c
    otherwise:           L+0x38 = L+0x40·10000.0;  L+0x60 = L+0x68·10000.0;  L+0x48 = L+0x50·1e-4;  L+0x70 = L+0x78·1e-4
FUN_180086440(core, u, I, J):  w = I⊙u in float (core+0x20..0x28);  I = FUN_18006e120(&w)
    I < 1e-19 (NaN too) → I = J = 1.0   else  J = 1.0/I
FUN_180083f70(L, f) — the pair's relative motion damped by the fraction f of its energy:
    jω = (L+0x58 − √|L+0x58² − (2·(f·L+0x80))·(L+0x78 + L+0x70)|) / (L+0x78 + L+0x70)
    jv = (L+0x30 − √|L+0x30² − (2·(f·L+0x88))·(L+0x48 + L+0x50)|) / (L+0x48 + L+0x50)
    P not flagged 2 nor 0x10:  P+0x120 += (double)L+0x0·(L+0x48·jv);  P+0x110 += (double)L+0x10·(jω·L+0x70)   -- per lane, narrowed
    L+0x0 = −L+0x0 (float)
    Q+0x120 += (double)L+0x0·(jv·L+0x50);  Q+0x110 += (double)L+0x20·(jω·L+0x78)
    (L+0x80 is the rotational energy E₁ and L+0x88 the linear E₂ that FUN_180086b40 left there)
```

**So every fifth-PSI's slide sharing and every PSI's damping are what keeps a heap of contacts from creeping**: the normal pushes'
work (`FUN_180088ae0`) is banked per pair and paid out, at most a tenth of the pair's releasable relative kinetic energy a PSI, as
equal and opposite impulses along the relative velocity and the relative spin — a static partner counting as ten thousand times the
other's mass.

**The split, `FUN_180086e80(S, r)`**, run at priority 0 when `S+0x80` was set and `FUN_1800877b0` named a root:

```
do:
    T = a new system (0x90 bytes, FUN_1800879e0(T, S+0x8))
    every core c of S, last first:
        c flagged 2 (unmovable):  a 0x18-byte record {count 0, no elements, +0x10 = T};  FUN_180087bf0(T, c);  FUN_180076690(c, record)
        else, c's root (+0x258 followed to its end) is r:  FUN_180088c80(S, c);  FUN_180087bf0(T, c);  FUN_180077f00(c, S)+0x10 = T
    every pair p of S, last first:  A = p+0x38, B = p+0x40
        one of them unmovable → s = that one, m = the other;  RS = FUN_180077f00(s, S), RT = FUN_180077f00(s, T)
        otherwise m = A and no records
        m's root is not r → next
        FUN_180081f70(env, p);  p removed from S's pairs (the last match, the rest moved down);  appended to T's;  FUN_180081f10(env, p)
        every contact cp of p, last first:  cp must be in S's list (assert at line 0x5ef)
            FUN_180088ce0(S, cp);  FUN_180087c90(T, cp);  RS → FUN_180075130(RS, cp), cp appended to RT
    every unmovable core c of S, last first:
        its T record empty → FUN_180077c10(c, it), FUN_180088c80(T, c);   its S record empty → the same against S
    T has fewer than two cores → its first core's T record removed (FUN_180077c10) and freed;  T deletes itself (slot 7);  return
    S has fewer than two cores → the same for S;  return
    r = FUN_1800877b0(S)
while r

FUN_1800877b0(S) → the root of a part that no longer touches the rest, or null:
    every core's +0x258 = null
    every pair, last first, neither core unmovable:  ra = A's root, rb = B's root;  ra != rb → rb+0x258 = ra        -- no path compression
    R = the root of the lowest-index movable core (the cores walked from the last, each movable one overwriting)
    return the root of the lowest-index movable core whose root is not R (walked the same way), or null
```

*Not read: `FUN_180085a80`, which a contact reaches only when its first core has `+0x58` set and there is no second core. Nothing
read so far writes `core+0x58`; that it is IVP's car-wheel pointer is a guess from the shape (a one-sided special friction), not
a reading, and until the writer is found the routine counts as reachable.*

**The simulation units** (a core's `+0x1f8`): `+0x0` a state that picks the manager's list (`≥ 8` → `+0x338`, else `+0x18`),
`+0x8`/`+0x10` that list's links, the cores at `+0x18`/`+0x1a`/`+0x20`, and the controller entries at `+0x38`/`+0x3a`/`+0x40`
(`0x28` bytes each: the controller, then its cores at `+0x8`/`+0xa`/`+0x10`, inline room for two at `+0x18`).

```
FUN_180074820(unit, controller):  a new entry for the controller, appended
FUN_180075990(unit):  the entries insertion-sorted by the controller's slot 5 (priority), ascending, a later one moving down while
    its neighbour's priority is strictly greater
FUN_180074ba0(unit):  every entry freed, last first;  the entry vector emptied (freed unless inline)
FUN_1800747a0(unit):  FUN_180074ba0;  both vectors emptied
FUN_180076350(unit, other):  other's cores appended, each core's +0x1f8 = unit;  other unlinked from its manager list
FUN_180075470(unit):  every core, last first, every controller of the core (+0x1e8, count +0x1e2), last first:
    the controller's entry (searched from the last, made when missing) gains the core;  then FUN_180075990(unit)
FUN_180074e40(unit, other):  FUN_180074ba0(unit);  FUN_180076350(unit, other);  FUN_180075470(unit)      -- the merge
FUN_180075130(vector, item):  the last match removed, the rest moved down — a missing item removes the FIRST element
FUN_180079180(core, system):  core flagged 2 → FUN_1800726e0(core+0x60, system+0x10), else core+0x60 = null
```

**A unit's PSI, `FUN_180075c80(unit, event, list)`** — what `FUN_180075a90` calls for every awake unit:

```
env+0xf8's +0x20 += 1;  k = unit+0x3a (the entry count, taken now);  now = env+0x188;  bits = 0
every core, last first (the first core last):
    dt = (double)(float)(now − core+0x1d0)
    FUN_180071330(core+0x1a0, core+0x90)                                   -- the rotation from the quaternion
    core+0xf0/+0xf8/+0x100 = (double)core+0x170/+0x174/+0x178·dt + core+0x150/+0x158/+0x160
    FUN_180077950(core)                                                    -- the pending changes committed
    core word +0x0 &= 0xff3f;  core word +0x2 = 0
    bits |= the bits of 1f − ((v.x² + v.y²) + v.z²)                      -- float, v = +0x140
bits' sign set (some core faster than 1 m/s) → unit dword: bit 11 cleared, bit 10 set
otherwise → bits 12–13 = bits 10–11;  bits 12–13 now set → every core, last first: FUN_180078820(core);  bits 10–11 cleared
env word +0x1a8 −= 1;  reaching 0 → +0x1a8 = 15 − (int)(FUN_18007d5c0()·−5f), and the rest test runs this PSI
every entry, LAST FIRST (so highest priority first):  entry's controller, slot 4 (controller, event, &entry+0x8)
every core appended to list, last first
the rest test:  s = 3;  every core, last first: core byte +0x1 = FUN_180077220(core, now), s &= it
    s == 3 → every core, last first: FUN_180088930(core);  the unit leaves its manager list, unit+0x0 = 8, and goes to the head of
             the manager's +0x338 list
every core, every object of it (+0x70, count +0x6a), last first:  FUN_180074240(object)
unit dword & 0x300 → FUN_180074ba0(unit);  FUN_180075470(unit);  FUN_180074e80(unit);  bits 8–9 cleared
env+0xf8's +0x20 −= 1;  reaching 0 → FUN_180072970
```

```
FUN_18007d5c0() — the process-wide generator:  seed (0x180124fe8, starting at 1) = seed·75;  → (float)(seed & 0xffff)·(1/65536f)
FUN_180077220(core, now) → 1 moved, 2 still but not for long enough, 3 at rest     (r = (double)core+0x4, the radius)
    P = the position (+0x150, doubles);  Q = the quaternion (+0x1a0);  Q′ = the one before (+0x180)
    |P − A|² > 0x3f1a36e2d7731900 (≈1e-4), A the float anchor at +0x230 → goto re-anchor
    2·(1 − ((q.w·Q.w + q.z·Q.z) + (q.y·Q.y + q.x·Q.x))²)·r·r > 0x3efa36e2d7731900 (≈1e-5), q the float anchor at +0x210 → goto re-anchor
    t = (float)(now − core+0x200);  t ≤ env+0xc8 (NaN too) → 2
    (ω.x² + ω.y²) + ω.z² ≤ ((float)(3π/4 / (double)env+0xc8))² → 3
    the same angle test with Q′ narrowed to float in place of q ≤ 1e-5 → 3
re-anchor:  q = (float)Q;  A = (float)P;  core+0x200 = now
    |P − B|² > 0x3f847ae151eb8520 (≈0.01), B at +0x240, or the angle test of q₂ (+0x220) against Q′ > 0x3fa47ae151eb8520 (≈0.04)
        → q₂ = (float)Q′;  B = (float)P;  core+0x208 = now;  → 1
    (float)(now − core+0x208) > 4f → 3,  else → 1
FUN_180088930(core) — frozen:  FUN_180078c90(core);  every object, last first: every listener the environment's hash holds for it,
    last first, slot 3 ({env, object}), stopping when the object's entry is gone;  then FUN_180082070({env, object})
FUN_180074240(object):  every synapse of the object's list (+0x48, linked by +0x10):  m = the synapse's mindist (its +0x30 offset);
    FUN_180095ad0(m);  unless (m+0x20 & 0xc000) == 0x4000:  FUN_180098f30(mgr, m), FUN_180097ae0(mgr, m)   -- mgr = (object+0x30)+0x20
```

**What `FUN_180074240` calls, read from the disassembly (2026-09-14)** — ported as `IvpCollisionObject.RecheckInvalid`,
`IvpMindistManager.UnlinkInvalid` and `Revalidate`, and `IvpMindistMinimize.Minimize` with a budget:

```
FUN_180095ad0(m):  FUN_180095cb0 instruction for instruction but for MOV [RSP+0x28], 0 — a step budget of zero where the
    minimize has 0x14, so its loop check (FUN_180094600) is consulted from the first step
FUN_180098f30(manager, m):  m off the invalid list (+0xc8 next, +0xd0 previous, head manager+0x28);  record 0 (+0x28) and
    record 1 (+0x60) off their objects' +0x48 lists (+0x10 next, +0x18 previous, the object at the record's +0x20);  flags untouched
FUN_180097ae0(manager, m):  flags & ~0x300000 | 0xc0000;  m at the head of the exact list (+0x10);  each record at the head of its
    object's +0x40 list;  appended to the rechecked array (+0x18) when record 0's object's core (+0xe8) has +0x58 — the second's
    read only when it has not — and flags & 0x3000 is not 0x1000.  No minimize, unlike FUN_1800977f0
```

*Not established: a case that tells the zero budget from twenty — it needs a minimize that revisits a pair of features.*

**The broad phase, `FUN_180098880(manager, object)`, first reading (2026-09-14)** — what a core coming to rest runs for each
of its objects (`FUN_180078c90`, `FUN_1800791a0`), and a teleport and an object's creation too (`FUN_18009a870`,
`FUN_180073700`, `FUN_180073a90`, `FUN_180073b00`, `FUN_1800742c0`, `FUN_180074530`, `FUN_18008a320`). It is IVP's OV tree: a
sphere per object, filed in a hierarchy of integer-keyed cells.

```
node = object+0xd8;  none → return
FUN_18009efc0(env+0x28 tree, node)                              -- out of its cell
env+0xc0 += 1;  node+0x20..0x28 = (float)core+0xf0..0x100       -- the unit PSI's extrapolated position
manager+0x0 set (a pair creation is running) → FUN_18009ecb0(tree, node, core+0x4, core+0x4, no list);
    FUN_18009de80(node, object+0x80, DAT_1800f4f20);  return
r = env+0x38's slot 2(object) + (double)core+0x4
env+0x58 set and object+0x78 & 7 → manager+0x0 = 1;  env+0x58's slot 0(object, &node+0x20, r);  manager+0x0 = 0
g = FUN_18009ecb0(tree, node, r, r, a 0x80-entry list of the nodes it overlaps);  FUN_18009de80(node, object+0x80, g − core+0x4)
the node's partner records (+0x40 count +0x42, elements +0x48), each slot 2 → its two objects, indexed in an open table of
    shorts by (a ^ b ^ object) + ((a ^ b ^ object) >> 8)·0x3ff, sized a power of two over 4·(partners + found), at most 0x400
every overlapping node, last first — other = its +0x38:
    both objects' +0x78 & 7 clear → skip;  same friction core (+0xf0) → skip;  object's core & 0x12 and other's core & 0x12 → skip
    object+0x78 bit 9 with other's bit 9 → skip;  object bit 10 (not 9) with other's bit 9, or bit 9 with other's bit 10 → kept
        without the filter;  otherwise env+0x30's slot 0(object, other) zero → skip
    FUN_1800962c0(table, other, object) finds the partner record and swaps it into the kept prefix;  none → other listed as new
every partner record past the kept prefix, last first: slot 0(record, 1)          -- a pair no longer overlapping deleted
every new other, last first: env+0x1d0's creators (count +0x1ca), last first, slot 5(creator, object, other) until one answers
```

```
FUN_18009efc0(tree, node):  cell = node+0x10;  node+0x10 = null;  node removed from the cell's nodes (+0x32, +0x38; the last
    match, the rest moved down);  while the cell has no nodes (+0x32) and no children (+0x22):  no parent (+0x18) → tree+0x58 =
    null;  the cell out of tree+0x48's hash;  FUN_18009dc50(cell) — out of its parent's children (+0x22, +0x28), vectors freed;
    freed;  cell = its parent
FUN_18009df30(key, node, r, r₂):  the level e from the exponent of r + r less 0x3fe, at least −40, scale = the loaded table below
    18012d7c8;  per axis lo = floor((float)((c − r)·scale)), hi = ceil((float)((c + r)·scale)), until every hi ≤ lo + 2, e rising;
    key = {lo.x, lo.y, lo.z, e − 1, e};  r₂ > r → the same at the next level from 18012d680, taken when it fits;  returns the radius used
FUN_18009ecb0(tree, node, r, r₂, list):  key = FUN_18009df30;  node+0x30 = (float)that radius;  the cell by key in tree+0x48
    (FUN_1800b5c10 — the CRC-32 of the 20-byte key with bit 31 set — and FUN_1800722b0);  found → the node joins it;  else a new
    0x40-byte cell with the key and the node:  no root (+0x58) → it is the root, hashed;  else the root grown (FUN_18009e920, a
    parent one level up whose coordinates are the child's halved, rounded by 18012d680+0x140/+0x148) until it holds the cell, the
    two merged when they are one cell, or the cell hashed and hung under its deepest existing ancestor (FUN_18009eb20) through new
    cells one level at a time (FUN_18009e730);  a list → tree+0x40 = list, FUN_18009e3d0(tree, node, root, cell)
FUN_18009e3d0(tree, node, cell, target):  every node of the cell, last first, whose centre is within the two radii —
    ((Δy² + Δx²) + Δz²) of float differences widened, against ((float)(r + r'))² — appended to the list;  then every child, first
    to last, the target itself through FUN_18009e630 and any other whose box, shifted to a common level, overlaps the target's
    recursed into
FUN_1800962c0(table, other, object):  probe by (other >> 8)·0x3ff + other;  each entry's record slot 2 → its objects; a record
    naming other → swapped with the entry at the kept count (+0x18), both records' back-indices (+0x18/+0x1c) rewritten, the
    count raised, the record returned
FUN_180071f90 / FUN_180072570:  Robin-Hood insert and delete in the cell hash (16-byte slots, the hash's low bits the home)
```

**The level tables at `18012d680` and `18012d7c8` are zeros in the file, and still zeros once the library is loaded** — the
`vphysics-data` probe read 82 zero doubles there from the game's own `vphysics.dll`. They are written by `FUN_18009d820`, the OV
tree's constructor, which the environment's construction `FUN_180080d90` calls: 80 doubles copied from `.rdata` `1800fe120`
into `18012d680`, and `1800fe118` into `18012d900`. **Every entry is a power of two, `18012d680[i] = 2^(i − 40)`** for `i` from 0
to 80 (`0x3d70…` to `0x4270…`, read from `.rdata`), so the key's scale `18012d7c8 − 8e` is `2^(1 − e)`, a cell `2^(e − 1)` wide,
and `FUN_18009ebf0` turns a key back into the world as `lo·2^level` with radius `2^e`. *Evidence: read from `.rdata`; the
copy read from the disassembly.*

**What the environment's construction `FUN_180080d90(env, manager list, template)` installs** — the classes the broad phase
calls through, read from the disassembly (2026-09-14):

```
+0xc8 = 0.3f (0x3e99999a)                      -- the rest test's delay, written outright, not from the template
+0x1ac = 5;  +0x80 = env;  +0x13c = 0
+0x108 = 1/66 (0x3f8f07c1f07c1f08);  +0x110 = 66.0;  +0x1b0 = 0x3feff2eed61b4202   -- the step until vphysics sets its own
+0x0   a 0x30-byte gravity controller (table 1800ea728), its +0x10..0x18 from FUN_1800824e0 and 1800fd4f0
+0x8   FUN_180089dc0 (0x30)          +0x10  the unit manager FUN_180074750 (0x340)     +0x18  FUN_180087900 (0x30)
+0x20  the mindist manager FUN_180095f90 (0x30)      +0x28  the OV tree FUN_18009d820 (0x60)    +0xd0  FUN_1800a4560 (0x40)
template's +0x18 or FUN_1800911f0 → +0xe8;   +0x50 or FUN_18009f490 → +0xe0;   +0x20 → +0x30 (the pair filter, no default)
template's +0x38 or FUN_180089590 → +0x40 (the anomaly manager);   +0x40 or FUN_180089550 → +0x48 (its limits)
template's +0x48 or FUN_1800a0650 (8 bytes, table 1800fe6e8) appended to the creators at +0x1c8/+0x1ca/+0x1d0 — slot 5 is
    FUN_1800a06f0, the object-pair watcher's maker
template's +0x28 → +0x58 (no default);   +0x58 or FUN_1800a0420 (0x68) → +0x38;   +0x30 or FUN_1800a4220 (0xa8) → +0x50
```

*Not established: whether vphysics writes `+0xc8` after construction.*

Three more from the same reading. **The cell hash compares a key's level (`+0xc`) and then `x`, `y`, `z`, and never `+0x10`**
(slot 0 of table `1800fe058`, `1800b5be0`, which Ghidra has no function for — read from its bytes); its hash is the reflected
CRC-32 (table `180127000`, `0x77073096` second) of the 20-byte key with bit 31 set. **`FUN_180096eb0` is the broad phase
that makes the node**: the object's old node (`+0xd8`) deleted through its slot 4, a 0x50-byte node built by
`FUN_18009d7b0(node, object)` and stored, and then everything `FUN_180098880` does — `FUN_180073970`, adding an object to
the simulation, calls it. **And a core's inverse inertia is written as a reciprocal**: `FUN_180074530`, which moves a core
between movable and not, sets `+0x40..0x4c = 1f / +0x20..0x2c` with four `DIVSS` before running the broad phase — the
division the port had filed as not found.

**The tolerance block's loaded values**, before vphysics sets its own: `18012d540` `0.001`, the margin table from `18012d544`
`0.01` throughout, and past it `18012d640`–`18012d648` `0.01`, **`DAT_18012d64c` `0.02`** (the gap a contact point starts with,
`block[0x43]`, and the recursive mindist's length threshold), `0.023`, `0.505`, `0.0001`, `0.045`, `0.22`, `0.001`, `0.02`, and
**`DAT_18012d66c` the int `1000`**, the most ledge pairs a recursive mindist refreshes. *Evidence: read from the loaded image.*

**Where a pair's mindists come from** — `FUN_180096680(objectA, objectB, gap, pair vector, ledgeA, ledgeB, …)`, called by
`FUN_1800b29b0` and `FUN_1800b6080`, and the constructor it ends in:

```
FUN_18009e630(tree, node, cell):  every node of the cell within the two radii appended, as FUN_18009e3d0 does;  then every child,
    last first, recursed with no box test                                            -- the target's whole subtree
FUN_180096680:  for a side given no ledge, the other object's core extrapolated to env+0x188 over (float)(now − +0x1d0) by
        its previous velocity (+0x170, each lane the product first), r = (float)(this object's +0xe0 + that core's +0x4) + gap,
        FUN_18008c420 and FUN_180070800 put the point in this object's frame, and its surface manager (+0xc8) slot 4 lists the
        ledges within r;  a given ledge is the list
    the pair vector's mindists indexed by (ledgeB·75 ^ ledgeA) + that >> 8·0x3ff in an open table of shorts, a power of two
        over 2·(A's·B's + existing) + 2, at most 0x400
    every ledge of A, last first, against every ledge of B, last first:  an existing mindist → swapped into the kept prefix, its
        back-indices (+0x18/+0x1c) rewritten;  otherwise a new one:  either ledge's +0x8 & 3 → 0x100 bytes through FUN_1800b21f0;
        else 0xe0 bytes — table 1800fdec8, both records' listener tables 1800fdea0, +0x18 = −1, +0x8 = 0xffff, flags
        & 0xcfc000ff | 0x0fc00000, +0xa0, +0xc0 and +0xd8 zeroed, env+0xb0 and +0xb4 counted — then FUN_1800975d0(m, A, B,
        ledgeA+0x14, ledgeB+0x14);  listed as new
    every mindist past the kept prefix, last first: slot 0(m, 1);  every new one, last first, appended with its back-index
FUN_1800975d0(m, A, B, featureA, featureB):  each object's kind (+0x8): 2 a polygon — the record's feature the pointer with its low
    four bits cleared, less (header & 0xfff + 1)·16 handed to the surface manager's slot 7 (a reference), kind word 0;
    3 a ball — kind word 3, and of two balls the record order follows the objects' +0x100 addresses;  anything else asserts
    record: +0x20 the object, +0x28 the feature, +0x30 the word back to the mindist, +0x32 the kind
    +0x98 = A+0xe0 + B+0xe0 (float);  neither object has +0x38 → FUN_1800977f0 (becoming exact);  else flags bit 13 cleared,
    bit 12 set, FUN_180097940 (the phantom)
```

**The two things that call it.** A pair of objects the broad phase finds gets a watcher, and a mindist can stand for many:

```
FUN_1800b5dd0(watcher, creator, A, B):  table 1800feb28, +0x10 the creator, +0x18 = −1, +0x20 table 1800feb50;  two hull
    records at +0x28 and +0x48 with listener table 1800feb00, filed over A and B (FUN_1800b61a0);  a pair vector at +0x68
    (capacity 8, elements +0x70);  then FUN_1800b6080
FUN_1800b6080(watcher):  env+0xbc counted;  env+0x38's slot 1(A, B, &rA, &rB);  FUN_180096680(A, B, rA + rB, watcher+0x68,
    no ledges);  each record filed again over now (FUN_180099970) with rA and rB
FUN_1800b28a0(m) — what FUN_180097f00 tails into for state 0x100000, a mindist standing for its objects' ledges:
    FUN_180095ad0(m);  no bits of 0xc000 and DAT_18012d64c < +0xa8 (COMISS/JNC: a NaN takes the other branch) →
        FUN_1800b23a0(m), FUN_180098ef0(manager, m), FUN_180097ae0(manager, m), flags &= ~0x3000
    otherwise → env+0xbc counted;  env+0x38's slot 1(A, B, &rA, &rB);  FUN_1800b29b0(m, rA + rB);  both records filed again
FUN_1800b29b0(m, gap):  its object pair's count against DAT_18012d66c (over → nothing);  the features of synapse A and B each
    handed back to their ledges (pointer with its low bits cleared, less (header & 0xfff + 1)·16);  FUN_180096680(A, B, gap,
    m+0xe8's vector, ledgeA, ledgeB);  m+0xe0's slot 2(count added)
```

```
FUN_1800a06f0(creator, A, B) — a creator's slot, from the table at 1800fe710:  a 0x78-byte watcher through FUN_1800b5dd0;  it
    registered on both objects' OV nodes (+0xd8) through FUN_18009de20;  returned
FUN_1800b61a0(record, owner, object):  +0x10 the object;  filed in the object's hull manager (+0xa0) at
    (float)(now − object+0x80)·+0x88 + +0x90 + DAT_1800eb920 (float, the product first);  +0x8 its slot;  +0x18 the owner
FUN_180098ef0(manager, m):  both records out of their objects' hull managers (+0xa0) by their slots (+0x30, +0x68)
FUN_1800b23a0(m):  every mindist of its vector (+0xea, +0xf0), last first, slot 0(it, 1);  +0xe0's slot 2(−count);  the vector freed
```

**The node's filing and its watchers**, read from the disassembly (2026-09-14). The node's first 0x20 bytes are a hull
listener — `+0x8` its slot in a hull manager, `+0x18` the manager — so the broad phase files the sphere in the object's own
hull manager (`object+0x80`: `+0x0` a time base, `+0x8` and `+0x10` floats, `+0x20` the min-list), the one a mindist's
records are filed in:

```
FUN_18009de80(node, hull, double gap):  now = node+0x38 (the object) → +0x30 (the environment) → +0x188, read first
    node+0x18 set → FUN_1800ab1b0(its +0x20, node+0x8), out of it;  else node+0x18 = hull
    t = (float)(now − manager+0x0) · manager+0x8 + manager+0x10     -- float, the product first, the manager node+0x18
    node+0x8 = FUN_1800aaed0(manager+0x20, node, (float)((double)t + gap))
FUN_18009de20(node, watcher):  the watcher appended to node+0x40 (grown through FUN_180072ba0 when full);  its +0x18 still −1 →
    +0x18 = the index, else +0x1c = the index                   -- a watcher sits on two nodes and keeps both slots
```

**What vphysics hands the construction, and the two classes the broad phase asks** (read from the disassembly, 2026-09-14).
`FUN_1800114f0`, vphysics' own environment constructor, fills a template on its stack through `FUN_180080d30` — every field
zero but `+0x0 = 0x100` — and sets only three: `+0x18` from `[180120b30]`'s slot 12, and `+0x20` and `+0x38` from one 0x40-byte
object of vphysics' own (tables `1800ebf78` and, at its `+0x8`, `1800ebf90` over the anomaly manager `FUN_180089590`). **So
`env+0x58` is null and the broad phase's `env+0x58` branch never runs in vphysics, and `env+0x38` is always IVP's default
range manager**, `FUN_1800a0420(rm, env, 1)`:

```
+0x8 = 1 (the policy, read by neither slot below);  +0x10 = env
+0x18 0.5  +0x20 (double)0.9f  +0x28 (double)0.8f  +0x30 10.0  +0x38 (double)0.06f
+0x40 1.0  +0x48 5.0  +0x50 0.5  +0x58 15.0  +0x60 (double)0.06f
slot 2(rm, object) — the broad phase's range:  core = object+0xe8
    s = (double)(core+0x254 + core+0x1dc) + 1e-20 (DAT_1800f4f20);  r = (double)core+0x4;  dt = (double)(float)env+0x108
    a = min(s·+0x40, r·+0x48);  a = max(a, +0x50);  a = min(a, +0x58);  a −= dt·s  (dt the destination)
    result max(a, s·+0x60 + r)
slot 1(rm, A, B, &rA, &rB) — a pair's range:  sA, sB as s above;  m = (double)MINSS(coreA+0x4, coreB+0x4);  sum = sB + sA
    g = min(m·+0x20, sum·+0x18);  g = max(g, +0x28);  g = min(g, +0x30);  g −= dt·sum;  g = max(g, sum·+0x38)
    wA = sA + sB·(double)0.2f (DAT_1800f4f40);  wB = sB + wA·(double)0.18f (DAT_1800fe6c0)
    rA = g·wA · 1/(wB + wA);  rB = g·wB · 1/(wB + wA)      -- every product and sum with the left operand the destination
```

**`env+0x30`, the pair filter, is vphysics' bridge to the game** — slot 0 `FUN_1800161e0(filter, A, B)`: no game solver at
`filter+0x18`, or either object without its `IPhysicsObject` at `+0x100` → collide. Otherwise the two objects' callback flags
(`IPhysicsObject+0x48`): one with `CALLBACK_ENABLING_COLLISION` (`0x800`) and the other with `CALLBACK_MARKED_FOR_DELETE`
(`0x400`), in either order → no pair. Else the game's `IPhysicsCollisionSolver::ShouldCollide(A, B, A's game data, B's game
data)`, B's game data fetched first, non-zero → collide. On the client that is `CCollisionEvent::ShouldCollide`, which
`source-sdk-2013` publishes (`game/client/physics.cpp`). *Evidence: read from the disassembly; the flag names from
`vphysics_interface.h`.*

**The range manager is ported and pinned (2026-09-14)** as `IvpRangeManager`. The `vphysics-range` probe builds one with
`FUN_1800a0420(rm, env, 1)` and calls both slots on fabricated cores, a tenth of the fields NaNs of two payloads, infinities,
negative zero and the largest float: **100,000 cases agree on every lane**, and `IvpRangeConformanceTests` replays 400. The
first sweep differed in 81% of cases by one unit in the last place, and the second in 1.4%: **the constants had been typed as
decimals** — `0.0599999986588955` for `(double)0.06f`, and `1e-20` for `DAT_1800f4f20` — and neither decimal names the
binary's bits. Written as `0.06f` widened, and the floor by its bits `0x3bfd83c94fb6d2ac`, both sweeps went to zero.

**The OV tree's sabotage round**, run by a subagent over 22 mutants: 17 reddened most of the suite, two hung it (a containment
margin and a root never cleared, each leaving `Grow`'s loop without an end), and three survived — the collect walk's
level-equal boundary, the strictness of one axis of the box test, and the radius sum added in double instead of float. The
fixture now carries eight searched cases: two unit spheres tangent on each axis in each order, a node filed into the root's own
cell while the root has children, and radii `1f` and `2^−24`, whose float sum is `1f`, with centres between the two squares.
**The tangent spheres were predicted to be pruned by the strict box test and are not** — the binary finds both, and so does
the port — so the walk's boxes are wider than the prediction assumed; *not established: which reading of the box was wrong.*

**The watcher's three tables and the creator's**, read from the disassembly (2026-09-14) — what keeps a broad-phase pair alive
and what ends it:

```
creator FUN_1800a0650, table 1800fe6e8:
    slot 0 FUN_1800a0690(creator, watcher) — a watcher going away:  its slot 2 names its objects;  FUN_18009ef40 takes the watcher
        off each object's OV node (+0xd8), first then second
    slot 1 the destructor (8 bytes);  slot 2 nothing;  slot 3 answers −1;  slot 5 FUN_1800a06f0, the maker;  slot 6 deletes itself
    slot 4 FUN_1800a07a0(creator, object) — an object leaving:  every watcher on its node (+0x42, +0x48), last first, its slot 4
        (FUN_180017b40: slot 0 with 1, the deleting destructor)
watcher, table 1800feb28:
    slot 0 FUN_1800b5e80 — the destructor:  every mindist of its pair vector (+0x6a, +0x70), last first, slot 0 with 1;  the
        creator's slot 0 (off both nodes);  the vector freed unless inline;  the second record out of its object's min-list
        (object+0xa0, by +0x50), then the first (by +0x30);  0x78 bytes freed
    slot 1 a pure virtual (FUN_180082760 asserts);  slot 2 its objects (+0x38, +0x58);  slot 3 no ledges (two nulls);
    slot 4 FUN_180017b40
watcher+0x20, table 1800feb50 — the delegator a pair's mindists hold:
    slot 0 FUN_1800b5fd0(delegator, mindist) — a mindist going away:  out of the pair vector by its back-index (+0x18, or +0x1c
        when +0x18 does not name it), the last element moved into its place and that element's index rewritten
    slot 1 the destructor through the outer object
each hull record, table 1800feb00 — IVP's hull listener:
    slot 0 its type, 2;  slot 1 FUN_1800b6170, the hull passed:  the watcher's FUN_1800b6080 — ranges asked again, the pair's
    mindists refreshed, both records filed again;  slot 2 FUN_1800b6180, the manager going away:  the watcher deleted;
    slot 3 nothing;  slot 4 FUN_1800b5f80, the record's destructor, out of its object's min-list
```

So **a broad-phase pair is re-examined only when one object's hull passes its record**, at a time the range manager's pair
range set — never on a schedule — and it ends when either object leaves or its hull manager does.

**The ledges a pair is built from: the surface managers' slot 4** (read from the disassembly, 2026-09-14). A `.phy` solid and the
world's brushes use vphysics' 16-byte polygon manager (table `1800eae60`, `+0x8` the `IVP_Compact_Surface`); a displacement
uses the virtual-mesh manager (table `1800ee220`). Slot 7 is nothing for the polygon manager, and for the mesh manager
`FUN_180025330`, the cache entry's reference.

```
slot 4(sm, &centre (doubles), double r, ledge, a5, a6, list) — FUN_180096680 passes a5 and a6 (0 or a pointer, and an object);
    neither manager below reads them, and the list is the seventh argument
FUN_18007ada0 — the polygon manager:
    no ledge → FUN_18007afb0(…, the root node (surface + surface+0x20), centre, r, list)
    a ledge → its node (ledge + ledge+0x4):  FUN_18007afb0 on the node's left child (node+0x1c), then on its right (node + node+0x0)
FUN_18007afb0(…, node, centre, r, list):
    d = (double)node+0x8..0x10 − centre per axis;  ((d.y² + d.x²) + d.z²) > ((double)node+0x14 + r)² (COMISD/JA) → return
    loop:  s = (float)((double)node+0x14 · 0.004);  any axis |d| ≥ (double)((float)box byte (+0x18..0x1a) · s) + r → return
                                                                 -- COMISD/JNC: a NaN returns
        the node has a ledge (+0x4 non-zero) → that ledge appended (FUN_18007ad60, a big vector: +0x0 capacity, +0x4 count);  return
        no ledge and terminal (+0x0 zero) → the node read as a ledge: two triangles (+0xc) and FUN_18007bea0's squared distance to
            them > r² → return;  else appended;  return               -- only a malformed tree reaches it
        FUN_18007afb0 on the left child (node+0x1c);  node = node + node+0x0;  the sphere test again, passed → loop, else return
FUN_1800261a0 — the mesh manager:
    no ledge → the cache entry (FUN_180025330):  its first MIN(entry+0x12, 2) hull ledges, from entry+0x8 + entry+0x10 stepping
        0x10 + 16·(+0xc) each, appended in order;  the entry released (FUN_1800259e0)
    a ledge → FUN_180025bc0(mesh, centre, r, ledge, list), the triangles within r
```

**A node's ledge stops the walk**: an inner node's hull is returned in place of everything beneath it, which is the ledge whose
`+0x8 & 3` (IVP's `has_chilren_flag`) sends `FUN_180096680` to the larger mindist `FUN_1800b21f0` — a mindist that opens its
ledge up when the pair comes close. *Not read yet: `FUN_18007bea0` and `FUN_180025bc0`.*

**The polygon manager's query is ported and pinned (2026-09-14)** as `IvpLedgeTree`, over a tree `PhysicsHull.Tree` now reads
whole — the flat reader kept only terminal ledges, and the query answers an inner node's hull in their place. The
`vphysics-ledge-tree` probe lays random trees out as the compiler does (surface header, a stub per ledge, nodes in preorder),
hands them to a manager built on the binary's own table, and asks slot 4 from the root and from beneath a hull: **20,000 cases
of four queries, the binary finding 96,952 ledges, agree on every lane**, and `IvpLedgeTreeConformanceTests` replays 300. The
first sweep's queries found under one ledge in eight — the box bytes were drawn anywhere in 0–255 and pruned nearly every walk
— and were redrawn before any case was kept.

**A second sabotage round**, over the survivors' killers and the range manager: the OV tree's level-equal boundary is
**equivalent** — at the tie both shifts are zero and the box test is symmetric at shift zero; three of the range manager's
operand orders are **equivalent** too — a NaN speed reaches both outputs through the other operand whichever way the first
`MINSD`, the step's product or the weights' sum is written. One order was not: which speed `ADDSS` keeps, seen when both are
NaNs with different payloads, and the fixture now carries that case. The box test's strict lower edges survived the tangent
spheres, which were predicted wrong — two tangent spheres at one level always land in adjacent keys, since `2r·scale` is under
one at the first level that fits. Random case 218 mirrored onto the lower edges survived too, because floor and ceiling do
not mirror. **What reaches a box edge is a node filed at its outer radius**, which spans two key units: two spheres of radius
`0.5`, outer `2`, centred at `∓2` on an axis, fill boxes meeting at `0`, and the second walk tests the first's cell with its
lower edge — each axis a searched case. The ledge tree's round left ten more, and each now has a case the binary answered as
predicted: both tests at equality (`d = r_node + r`, `|d| = box reach`), the reach summed in double (`1 + 0.3` above
`1f + 0.3f`, a tie that rounds to even), the box unit narrowed after its product, a box byte's product kept in float, a
point whose squared distance straddles the reach between the two groupings, and a hull whose `+0x4` names another node.
**A third round killed all ten, each by its own case.**

**The larger mindist's tables beside the plain one's** (read from the disassembly, 2026-09-14). Both start from the base
constructor `FUN_180095f20(m, env, delegator)`: `+0x10` the delegator, `+0x18 = −1`, both records' listener table `1800fdea0`,
flags byte 0 cleared then `& 0xcfc000ff | 0x0fc00000`, `+0x8 = 0xffff`, `+0xa0`, `+0xc0` and `+0xd8` zeroed, `env+0xb0` and
`+0xb4` counted. `FUN_1800b21f0` adds table `1800fe960`, a delegator of its own at `+0xe0` (table `1800fe9a8`), and an empty
mindist vector at `+0xe8`.

```
slot   plain 1800fdec8      larger 1800fe960
0      FUN_180096250        FUN_1800b2250 — its mindists deleted (FUN_1800b23a0), the vector freed, the base destructor
1–4    shared: FUN_1800992e0, FUN_180097550, FUN_180097510, FUN_180017b40
5      FUN_1800947e0        nothing
6      FUN_18000a6f0        FUN_180028aa0
7      FUN_180097440        FUN_1800b2700 — its own delegator's count above DAT_18012d66c (1000) → FUN_180098dd0, FUN_180097ce0
8      FUN_18008ecb0        FUN_1800b2460 — the count against 1000, then a switch on record 0's kind word (+0x5a)
+0xe0 delegator:  slot 0 FUN_1800b2320, a child mindist out of the vector by its back-index;  slot 2 FUN_1800b2300, the count
    (+0xfc) moved and the outer delegator's slot 2 told;  slot 3 FUN_1800b2860, the outer delegator's slot 3
```

*Not read yet: the larger mindist's slots 6 and 8 in full.*

**The pair's mindists, instruction by instruction** (read from the disassembly, 2026-09-14) — `FUN_180096680` takes nine
arguments, where the first reading named six:

```
FUN_180096680(A, B, double gap, pair vector, ledgeA, ledgeB, rootA, rootB, delegator):
    side A:  a ledge → it alone;  else B's core (+0xe8) moved to now (env+0x188) over dt = (float)(now − core+0x1d0), each lane
        (double)core+0x170..0x178 · dt then + core+0x150..0x160;  r = (double)(A+0xe0 + coreB+0x4) + gap (the float sum A's
        first);  FUN_18008c420(A) — A's motion cache, made through env+0xd8 when absent (FUN_1800805a0) and refreshed
        (FUN_180080a60) when A+0x78 < 8 and env+0x1a0 is past the cache's +0xc0;  FUN_180070800(cache+0x40, the point) — the
        point less the matrix's +0x60..0x70, then ((d.y·m[1][c] + d.x·m[0][c]) + d.z·m[2][c]) per column c, rows 0x20 apart;
        A's surface manager slot 4(the point, r, rootA, 0, ledgeB, list A)
    side B:  the same with the objects swapped, slot 4(…, rootB, 0, ledgeA, list B)
    the table:  size 0x400 halved while it exceeds 2·(|A|·|B| + existing) + 2, then doubled until over twice the existing count
        (from the heap when it had to grow, else the stack);  every existing mindist, last first, indexed by its slot 3's
        two ledge pointers, (ledgeB·75 ^ ledgeA) + ((…) >> 8)·0x3ff, linear probing over shorts, −1 empty
    every ledge of A, last first, against every ledge of B, last first:
        found (its slot 3 names both) → swapped with the entry at the kept count, both back-indices (+0x18 or +0x1c, whichever
            named the old place) rewritten, the table's two shorts swapped, the count raised
        not found → either ledge's +0x8 & 3 → FUN_1800b21f0 (0x100);  else 0xe0 bytes inline — the base constructor's writes with
            the delegator at +0x10;  FUN_1800975d0(m, env? — its +0x30 and +0x38 saved arguments, ledgeA+0x14, ledgeB+0x14)
            — then listed as new (a vector of capacity 0x80 on the stack)
    every mindist past the kept count, last first:  slot 0 with 1, deleted
    every new mindist, last first:  appended to the pair vector, its back-index +0x18 when −1, else +0x1c
FUN_1800975d0(m, A, B, featureA, featureB):  the two records +0x28 and +0x60 — for two balls ordered by their +0x100 pointers
    kind 2 (polygon):  record +0x80.. = A, +0x88 the feature, +0x90 the back-word, +0x92 = 0; the feature's ledge
        (pointer with its low four bits cleared, less (header & 0xfff + 1)·16) handed to A's surface manager slot 7
    kind 3 (ball):  record +0x20 = A, +0x28 the feature, +0x30 the back-word, +0x32 = 3
    +0x98 = B+0xe0 + A+0xe0 (float, B's first);  neither object's +0x38 set → FUN_1800977f0(env+0x20, m);  else flags bit 13
    cleared and bit 12 set, FUN_180097940(env+0x20, m)
FUN_1800977f0(manager, m) — becoming exact at birth:  flags & 0xffcfffff | 0xc0000;  m at the head of the manager's exact list
    (+0x10, through m+0xc8/+0xd0);  record 0 at the head of its object's +0x40 list, then record 1;  FUN_180095cb0(m);
    either object's core has +0x58 set → m appended to the manager's rechecked vector (+0x18);  flags & 0xc000 clear →
    FUN_180099380(m, (coreA+0x1 | coreB+0x1) < 0x21, 0);  else m's slot 7(manager)
FUN_180097940(manager, m) — the phantom:  FUN_180095ad0(m);  flags & 0xc000 set, or +0xa8 ≤ 0 → another path (not read);
    else flags & 0xc00 → FUN_180098380(m);  then linked exact as above, appended to the rechecked vector unless
    flags & 0x3000 is 0x1000, and tailed into FUN_180099380(m, 1, 2)
FUN_180025bc0(mesh manager, &centre, double r, root, list) — a displacement's triangles:  the point scaled into Source's
    units by DAT_18011f004 (float) with its axes turned (x, z, −y), the radius (float)r times the same scale;  the game's
    virtual-mesh query (mesh+0x8, slot 2) fills up to a stack's worth of triangle indices;  when the entry holds two hulls,
    the triangles are split in half by index and the root names which half;  each triangle (0x30-byte records from the entry's
    +0x8) whose FUN_18007bea0 squared distance is not above r² (COMISD/JA) appended
```

*Not read yet: `FUN_18007bea0`, the phantom's other path, and the game's virtual-mesh query, which lives in the engine.*

**The OV tree is ported and pinned (2026-09-14)** as `IvpOvTree`: the insert, its key, growth, descent, path, both overlap
walks and the removal. The `vphysics-ov-tree` probe builds a real tree with `FUN_18009d820` and sixteen nodes with
`FUN_18009d7b0`, runs cases of 24 drawn inserts and removals through `FUN_18009ecb0` and `FUN_18009efc0`, and compares each
step's returned radius, the node's `+0x30` and cell key, the nodes found in order, and a digest of the whole tree walked from
`+0x58`: **50,000 cases, the binary finding 1,601,646 nodes, agree on every lane**, and `IvpOvTreeConformanceTests` replays
300. The rounding helpers' negative-floor sequence is settled by that agreement: `CVTTSS2SI` of the narrowed product, one
taken off (floor) or added (ceiling) when the truncation differs from the value, and the result truncated again. Three
things the sweep showed on the way. **The found list includes the node itself** — the probe's control, two unit spheres a
quarter apart, finds two nodes — so the broad phase's skip of an overlapping node on the same friction core is what keeps an
object from pairing with itself. **The node constructor allocates its watcher vector** (`0x80` bytes through
`FUN_180072b90`, capacity `0x10` at `+0x40`), so the probe resets the fields it writes rather than calling it per case.
**And removal leaves `+0x30` as it was**: a node taken out still carries the radius it was filed with, which the first
sweep reported as 190 differing cases before the probe stopped carrying one case's nodes into the next. *Evidence:
differential against the shipped binary. Not established: what the binary does with a non-finite centre or radii far enough
apart to read past the 81-entry level table — the draws keep out of both, and the port throws there.*

**The rest test depends on a generator shared by the whole process.** The countdown between tests is 15 to 19 PSIs drawn from
`seed·75`, and every draw anywhere in the process advances it, so which PSI a corpse's heap is tested on depends on how many
tests ran before it since the game started. *A replay can match the rule but not the phase; not established: whether anything
else draws from the same seed.* The multiplier is `DAT_1800ee1c8` = `0xc0a00000`, `−5f`, and the generator's scale
`DAT_1800fd2a0`; the seed is multiplied with `IMUL EAX,[seed],0x4b` and only its low word is used.

**`FUN_180077220` and `FUN_18007d5c0` are ported and pinned (2026-09-14)** as `IvpRigidBody.TestRest` and `IvpRandom`. The
`vphysics-rest` probe calls both in process on a fabricated core: 100,000 cases — the binary answering moving 36,740 times,
still 29,046 and resting 34,214, a fifth seeded with NaNs and infinities — agree on every lane, and `IvpRestConformanceTests`
replays 400. Read with them: the thresholds are not the round numbers above but `0x3f1a36e2d7731900`, `0x3efa36e2d7731900`,
`0x3f847ae151eb8520` and `0x3fa47ae151eb8520`; and **the time arrives by value in `RDX`**, an `IVP_Time` struct, not in `XMM1`.
*Not established: `env+0xc8`'s writer.* **A sabotage round found four orders random cases could not see**, and the fixture carries
cases searched for each: an elapsed time a quarter of a float step past the delay, which only the `(float)` narrowing holds
under it; turns straddling the threshold between the dot's `(w + z) + (y + x)` and `((w + z) + y) + x`, and between narrowing
`Q′` or `Q`; and spins straddling the limit between `(x² + y²) + z²` and `x² + (y² + z²)`. A fifth — the operand destinations
inside the sums — is unobservable by construction, since every one feeds a comparison.

**The PSI around the units, `FUN_180082560`, read from the disassembly (2026-09-14)** — `env+0x1ac` records the phase:

```
profiler 1;  +0x1ac = 0;  +0x162 set → FUN_180089210(env);  +0x58 set → FUN_180087e50(env+0x18, +0x58);  (env+0xe0) slot 10(env)
    the controllers at +0x158 (count +0x152), last first, slot 0(&env);  FUN_180098610(env+0x20)
profiler 2;  FUN_180075a90(env+0x10, env, a 0x80-entry buffer)      -- every awake unit's PSI, the cores collected
profiler 3;  FUN_18009a590(env, that buffer, a second)             -- every core integrated, FUN_180099a00, last first
profiler 4;  +0x1ac = 2;  FUN_18009a690(env, the second buffer)   -- the collision event walk
profiler 5;  +0x1ac = 3;  FUN_1800983e0(env+0x20)
profiler 6;  +0x1ac = 4;  FUN_1800985a0(env+0x20)
profiler 7;  +0x1ac = 5
```

`FUN_18009a590`'s event is `{(float)env+0x108, dt > DAT_1800fcfa0 ? (float)(1.0/dt) : 1e10f}` (`0x501502f9`), and gravity's
slot 4 `FUN_180074c80` walks the cores of its own entry in the unit (`R8`, last first), skipping a core flagged `0x10`: damping
`FUN_180078250(core, (double)event[0])`, the flush, then `v += g·dt` in double, each lane the product first — `g` the controller's
`+0x20..0x28` when the core is flagged `0x20`, else `+0x10..0x18`. **So a contact's friction and normal pushes are solved in
phase 2, inside each unit's PSI, before any core is integrated or any collision walked.**

**The unit's own bookkeeping:**

```
FUN_180074770(unit):  the core vector inline (capacity 2, elements +0x28);  no entries;  dword = 8, bits 8–9 and 10–13 clear
FUN_1800749b0(unit, core):  appended to the cores (+0x18)
FUN_180074ba0(unit):  every entry, last first, freed (its core vector freed unless inline);  the entry vector emptied
FUN_180075470(unit):  every core, last first, every controller of the core (+0x1e8, +0x1e2), last first: the controller's entry
    (searched last first, appended when missing) gains the core;  then FUN_180075990
FUN_180074e80(unit) — whether the unit came apart:  every core's +0x258 = null;  every entry, last first: the controller's own
    cores (its slot 2) joined — each root (FUN_1800878d0, along +0x258, no compression) that differs from the first core's root
    gets +0x258 = that root;  then the unit's first core's root R;  the first core in order whose root is not R →
    FUN_180074ba0(unit), FUN_1800761c0(unit, that root), FUN_180075470(unit)
FUN_1800761c0(unit, r):  repeat:  a new unit, dword 1, onto the manager (FUN_1800749f0);  every core of unit whose root is r
    moves to it (removed in order, +0x1f8 pointed at it), and the first other root seen is kept;  FUN_180075470(new);
    until no third root was seen, r = the root kept
FUN_180076350(unit, other):  other's cores appended, each +0x1f8 = unit;  other unlinked from its manager list (+0x18, or +0x338
    when its state is 8 or more)
```

#### A core's rotation, instruction by instruction (2026-09-14)

**Read because the rest test reads the quaternions as doubles** — `FUN_180077220`'s `MOVSD [core+0x1a0..0x1b8]` — while this
project stores them as floats. Every routine below takes and writes doubles:

```
FUN_180070d60(out, a, b):  out0 = ((b0·a3 + a0·b3) + b2·a1) − a2·b1      out1 = ((b1·a3 + b3·a1) + a2·b0) − a0·b2
                           out2 = ((a2·b3 + a3·b2) + b1·a0) − a1·b0      out3 = ((a3·b3 − a0·b0) − b1·a1) − b2·a2
FUN_180070c60(q):  n = (w·w + z·z) + (x·x + y·y);  !(|1 − n| > 1e-12) → unchanged   (COMISD/JBE: a NaN leaves q)
                   s = 1.5 − n·0.5;  repeat s = s + (1 − (s·s)·n)·0.5  while |1 − (s·s)·n| > 1e-12;  q = q·s
FUN_180071680(out, &ω float, dt double):  h = dt·0.5;  θ = (f)((d)ω.l·h);  l = (d)(θ − (θ·θ)·(θ·0.16666667f))   per lane
                   out3 = √(1 − ((y·y + x·x) + z·z))                   -- double, no clamp: a negative gives NaN
FUN_180070f50(out, &ω, dt):  l = sin((d)ω.l·(dt·0.5)) per lane;  s = (y·y + x·x) + z·z;  s > 1 →
                   each l ·= 0x3feffffffaa19c47/√s and s summed again;  out3 = √(1 − s)
FUN_180071330(q, M):  the terms FUN_180071330's port already carries, from the double quaternion
FUN_180071060(out, a, b, t):  dot = (a3·b3 + a2·b2) + (a1·b1 + a0·b0);  dot > 0 → σ = 1f  else dot = −dot, σ = −1f
                   dot ≥ (double)0.999f → out = (σ·b − a)·t + a, then n = (w² + z²) + (x² + y²), h = n·0.5,
                       s = 1.5 − h;  s = s + (0.5 − (s·s)·h), twice;  out ·= s
                   else θ = acos(dot) (FUN_1800cce64);  k = 1/√(1 − dot²);  out = b·(σ·(sin(tθ)·k)) + a·(sin((1 − t)θ)·k)
FUN_180099fc0(core, dt float, out):  bit 0x8, or env+0x1ac == 5 → the second route below
    I′ = ((Iy − Iz)·I⁻¹x, (Iz − Ix)·I⁻¹y, (Ix − Iy)·I⁻¹z) in float;  s = (ωy² + ωx²) + ωz² in float
    h = (double)dt;  (d)s·h·h > 1/36 → k = (int)√(that·144) + 1,  h = h / (double)(float)k
    out = FUN_180071680(ω, h);  ωx = (f)((d)(f)((d)(ωz·ωy)·(d)I′x)·h + (d)ωx), and ωy, ωz the same with ωz·ωx and ωy·ωx
    each further sub-step:  d = FUN_180071680(ω, h);  out = FUN_180070d60(d, out) inlined — the NEW delta on the left;  ω again
second route:  core+0x58 set and +0x8 zero → one axis (the +0x58 object's +0x48) through FUN_180070f50 and sin, composed
    with FUN_180070d60;  otherwise out = FUN_180070f50(ω, (double)dt)
```

**`FUN_180099a00`, the integrator, calls it and then**: `+0x1d8 = event[1]`, `+0x1dc = (float)|v|` (`FUN_18006e120`), the
position moved by the last velocity through `(double)(float)(now − +0x1d0)`, `+0x170 = v`, `+0x180 = +0x1a0`,
`FUN_180070d60(+0x1a0, +0x1a0, delta)` — the working orientation on the left — and `FUN_180070c60(+0x1a0)`; before any of it,
unless the core has `+0x58` with a zero `+0x8`, the anomaly manager's slot 1 when `(ωx² + ωy²) + ωz²` exceeds
`((float)env+0x110 · limits+0x14)²` and slot 0 when `|v|²` exceeds `limits+0xc²`.

*Not read: what sets a core's bit `0x8`.* vphysics' `sin` and `acos` are read and ported since, in the next section.

**Ported and pinned (2026-09-14).** `IvpQuaternion.Product`, `Normalise`, `Interpolate`, `Delta` and `SineDelta` and
`IvpIntegrator.Rotate` carry the routines above in doubles, B369's seven divergences gone, and the `vphysics-rotation` probe
calls all six in process on both `sin` paths: 50,000 random cases — a quarter seeded with NaNs and infinities — agree on every
lane, and `IvpRotationConformanceTests` replays 400 of them and 102 NaN pairs, the fixture's own control asserting both of the
interpolation's branches, a sub-stepped step and both second routes. What the port met that the reading above did not say:

- **`FUN_180070c60` never returns for a squared length of four or more, for an infinity, or for zero.** Its loop is
  `s = s + (1 − s²n)/2`, whose slope at the root is `1 − √n`. This project's own test normalized `(0, 0, 0, 4)` while the port
  divided by the length; a faithful port hangs on it, and the binary would. *Evidence: arithmetic.*
- **The inlined sub-step product is not `FUN_180070d60` with its operands swapped**: four of its multiplications take the
  other operand as the destination, which only a pair of NaNs can see.
- **The sub-step count is `CVTTSD2SI` plus one**, which truncates a count too large for an int to `int.MinValue`, so the
  step runs once over a negative sub-step. .NET's own cast saturates since .NET 9, and the port truncates by hand to carry
  the instruction — though no output can tell: `(float)` rounds both negative counts to `−2³¹`.
- **Three orders a sabotage could not redden, and why none can** (2026-09-14): the Euler coefficient's product taken in
  double and narrowed equals the float product, since two floats' mantissas multiply exactly in 48 bits; the saturating cast
  above; and the slerp lane's `wa·a` operand order, because a NaN in `a` makes the dot, `θ` and the other lane's product NaN
  first, and that NaN is the sum's destination. *Evidence: arithmetic.* Two further survivors needed inputs random draws
  never reach — a spin whose sub-step count moves when its squares are summed in double, and a dot whose `Math.Acos`
  differs from vphysics' — and the fixture now carries cases searched for each.
- **`FUN_1800734e0`'s fraction is `MULSS` with the elapsed time the destination**, widened for the interpolation's double.

#### vphysics' own `sin`, `cos` and `acos`, ported whole (2026-09-14)

**`FUN_1800c8020` `sin`, `FUN_1800d33b0` `cos` past `π/4`, and `FUN_1800cce64` `acos` are ported as `IvpMath.Sin`, `Cos` and
`Acos`**, with the reductions a large argument is handed to: the plain routines reduce inline under 500000 and through
`FUN_1800daa70` above it, the fused ones through `FUN_1800dafb0` under `2·10⁷` and `FUN_1800dadc0` above, both large routines
taking `2/π` from a byte table. The `vphysics-math` probe's sweep compared them with the binary over six million `sin` and
`cos` arguments on each path — every branch's range, near every multiple of `π/2`, random bits with the infinities and NaNs
among them — and four million `acos` arguments, and found no difference. **The binary's own two paths disagree on 44,881 of
those `cos` arguments and 44,510 of the `sin`**; `IvpMathConformanceTests` carries pairs of both. *Evidence: differential.*

**A wrong conclusion, kept: "the same instruction bytes give a different answer."** Three arguments came back one ulp from the
binary, on the plain path only. The binary's kernel entered directly at `d3495` with the port's reduced pair answered the
binary's bits, so the reduction was right and the kernel was the suspect — and yet the C# kernel, a replica in SSE2
intrinsics one instruction per call, and an exact dyadic simulation of the decoded thirty steps all answered the port's
bits. Three instruments agreed with one another and not with the binary. What settled it was copying the loaded kernel
beside the module, its RIP-relative operands rebased, and cutting it after each step: the carried term agreed and the series
already differed. **The twelfth coefficient at `180105f60` is `0x3e21eeb690382eec`; the port carried `0x3e21eeb69037ec2e`**,
and every replica had been written from the port's literal. The loaded bytes had been printed beside Ghidra's and matched —
a check of the image against the listing, never of the port against the image.

**A frozen core, `FUN_180088930`**: `FUN_180078c90(core)`; then every object, last first — every listener the environment's hash at
`env+0x18` holds for it, last first, slot 3 with `{env, object}`, stopping once the object's entry is gone after a call — and
`FUN_180082070(env, {env, object})`, the environment's own listeners at `+0x1c0` (count `+0x1ba`), last first, slot 3.

**`FUN_180078c90`'s object half**, after `FUN_180078bd0` resets the core: every object, last first — `+0x78 = 8`;
`FUN_180098880(env+0x20, object)`; its hull manager at `+0x80` folded to the time — `t = (float)(now − +0x80)`,
`+0x94 = t·+0x8c + +0x94`, `+0x90 = t·+0x88 + +0x90` (float, the product first), `+0x88` and `+0x8c` zeroed, then
`FUN_180094490(object+0x80)`; and `FUN_180080650(object)` when `+0x70` is set.

**`FUN_1800a9bf0(system, event)`, the many-contact priority-0 routine, first half:**

```
(env+0xf0)+0x20 += 1
the contact list insertion-sorted by (cp+0x90 & 0xffff0000) — the signed word cp+0x92, most negative first; a contact moves back
    while its predecessor's key is strictly greater (signed), keeping +0x40 the head
every contact, the next saved first (gap = cp+0x8c, float):
    gap ≥ block[0x47] (ordered; COMISS/JNC — a NaN gap does NOT take this) or the record's word +0x76 == 1 → FUN_180083e40(system, cp)
    else gap > block[0x46] + block[0x43] (ADDSS, block[0x46] the destination) and (byte (cp+0x20)+0xf0 & byte (cp+0x48)+0xf0) & 1
        → FUN_180088ce0(system, cp) then FUN_180087c90(system, cp): unlinked and linked again at the head
    -- cp+0x20 and cp+0x48 are the two synapses' objects; +0xf0 each object's friction core
system+0x7c = 0
more than 150 contacts (0x96) and the anomaly manager's slot 5 (env+0x40, +0x28) answers for the cores (+0x50, +0x4a):
    every movable core's word gains bit 0;  a core in more than one pair with a mover (neither core flagged 2) has its
    velocity and spin zeroed (+0x130..0x13b, +0x140..0x14b);  the arena's +0x20 −= 1, reset at zero (FUN_180072970);  return
otherwise:  a solver on the stack (FUN_180083100(solver, system, event))
    every contact in the sorted order, counted from 0 (record = cp+0x70):  the count < system+0x7c (just zeroed, so never) → record+0x70 = 0xffff
        else  record+0x78 = FUN_180077f00((cp+0x20)+0xe8, system);  record+0x80 = FUN_180077f00((cp+0x48)+0xe8, system)
              -- the objects' PHYSICAL cores (+0xe8) here, where the re-link test above read their friction cores (+0xf0)
              record+0x8c = cp+0x8c (the dword copied);  record+0x88 = (int)(short)cp+0x92
              record appended to the solver's vector (capacity +0x70, count +0x72, elements +0x78, inline +0x80);  record+0x70 = its position
    an arena array of 4 bytes per contact;  n = FUN_1800a9520(solver, system, array);  FUN_1800aa5c0(solver, system, array, n, arena)
    the arena's +0x20 −= 1, reset at zero;  the solver's vector freed unless inline
```

**A record's memory is the many-contact solve's scratch every PSI**: `+0x78` and `+0x80` become 8-byte pointers over the push-out,
the estimate and the elasticity the collision entry left, and `+0x88`/`+0x8c` two dwords — so what the impact loop reads there is
only what it wrote since the last PSI. **Past 150 contacts the anomaly manager may freeze a heap outright**, zeroing its movers.

**The three block fields the heap solve reads, from `FUN_180098fd0`'s tail** (`d` the tolerance, double; each stored narrowed):

```
block[0x43] (+0x10c) = (float)((double)block[0x42] + d)                      -- the contact gap, as already ported
block[0x46] (+0x118) = (float)(d · DAT_1800eb150)       DAT_1800eb150 = 0x3f847ae140000000, (double)0.01f — nothing added
block[0x47] (+0x11c) = (float)(d · DAT_1800fdf80 + (double)block[0x43])   DAT_1800fdf80 = 2.5 — the product the destination
block[0x48] (+0x120) = (float)(d · 20.0 + (double)block[0x43])            -- DAT_1800fcfb0, as already ported
```

So a contact is dropped from a heap once its gap reaches `2.5·d` past the contact gap, and it is moved to the front of the list
once its gap passes `block[0x46] + block[0x43]` — `0.01·d` past the contact gap — with both friction cores flagged.

**The many-contact normal solve's setup:**

```
FUN_1800a4600(matrix):  +0x0 = 1e-9 (0x3e112e0be826d695);  +0x8 = 0;  +0x10 = +0x18 = +0x20 = null        -- FUN_1800a46f0 masks +0x10 to 8
FUN_180083100(solver, system, event):  FUN_1800a4600(solver);  records vector +0x40 capacity 0x200, +0x48 = inline +0x50
    +0x30 = env;  +0x38 = event;  n = system+0x7a − system+0x7c;  +0x8 = +0xc = n
    from the arena (env+0xf0):  +0x10 an n×n matrix of doubles,  +0x18 n doubles,  +0x20 n doubles;  FUN_1800a46f0(solver)

FUN_1800a9520(solver, system, active) → how many are active:
    the matrix zeroed
    every record i (the solver's vector):
        s = the closing speed FUN_180084490 forms;  g = (double)(float)(block[0x43] − record+0x8c);  k = g ≥ 0 ? 1.0 : 20.0
        +0x18[i] = k·g + s;   record+0x88 != 0 → active gains i
        the first core (+0x98), if any:  n′ = (float)((double)n · −m⁻¹) per lane;  u = t ⊙ I⁻¹ in float
            every contact j of record+0x78's vector (the core's contacts in this system) whose record index is not negative:
                σ = its record's +0x98 is this core ? −1.0 (DAT_1800eaa00) : 1.0;  τ = that record's +0xf0 or +0x100 accordingly
                matrix[j·n + i] += σ·((double)(float)((n′·nⱼ)) − (double)(float)((u·τ)))           -- dots in float
        the second core (+0xa0), if any:  n′ = (float)((double)n · m⁻¹);  u = t′ ⊙ I⁻¹;  the same over record+0x80's vector with +
    the list's records must carry their indices in order, else an assert at line 0x3a6
```

**`FUN_1800a9520` read again for porting, every grouping and destination** (floats `f`, doubles `d`; `a·b` names `a` as the
destination):

```
matrix zeroed (rows·columns)
every record i of the solver's vector (+0x42 count, +0x48 elements), i ascending:
    s = 0.0
    A = record+0x98:  s = (d)((t.y·A.ω.y + t.x·A.ω.x) + t.z·A.ω.z) + (d)((n.y·A.v.y + n.x·A.v.x) + n.z·A.v.z)
                      -- t = record+0xf0, n = +0x20, ω = core+0x130, v = +0x140; each product the record's lane first, in float
    B = record+0xa0:  s = s + ((d)(−((n.y·B.v.y + n.x·B.v.x) + n.z·B.v.z)) − (d)((t′.y·B.ω.y + t′.x·B.ω.x) + t′.z·B.ω.z))
                      -- t′ = record+0x100; the linear dot is negated in float, then the turn's subtracted in double
    g = (d)(f)(block[0x43] − record+0x8c);  k = g ≥ 0 ? 1.0 : 20.0 (NaN: 20.0);  rhs[i] = k·g + s
    record+0x88 (dword) != 0 → active gains i
    A:  q = (d)(−A+0x4c);  u = (t.x·A+0x40, t.y·A+0x44, t.z·A+0x48) in float;  n′ = (f)((d)n·q) per lane
        every contact of record+0x78's vector (+0x2 count, +0x8 elements), ascending:
            j = (short)(contact+0x70)+0x70;  j < 0 → next;  R = the solver's record j
            σ = R+0x98 == A ? −1.0 : 1.0;  that side's core pointer (R+0x98 or R+0xa0) null → next;  τ = R+0xf0 or R+0x100
            dn = (n′.y·R.n.y + n′.x·R.n.x) + n′.z·R.n.z;  dt = (u.y·τ.y + u.x·τ.x) + u.z·τ.z      -- float, n′ and u first
            matrix[j·columns + i] = (((d)dn − (d)dt)·σ) + matrix[j·columns + i]
    B:  q = (d)B+0x4c;  u = (t′.x·B+0x40, …);  n′ = (f)((d)n·q);  the same over record+0x80's vector, but ((d)dn + (d)dt)
then every contact of system+0x40, in list order, counted from 0:  its record's index non-negative and not its position → assert 0x3a6
return how many are active
```

`FUN_180077f00(core, system)` — a core's record in a system: an unmovable core (byte `+0x0` bit `2`) looks the system up in the
hash at `+0x60` (`FUN_180072350`, none when `+0x60` is null); a movable core answers `+0x60` itself when its `+0x10` is the system,
else null.

**So the matrix is each contact's response to a unit push at every other contact that shares a moving core**, built from the
same arms and turns the impact solver pushes through, and the right-hand side is the same stiffness-and-closing-speed target a
lone contact meets. *Not read: `FUN_180085a80`, which only a core with `+0x58` reaches.*

**The many-contact normal solve itself, `FUN_1800aa5c0(solver, system, active, m, arena)`** (virtual; its one direct caller is
`FUN_1800a9bf0`, where `system+0x7c` was just zeroed, so `n = system+0x7a`):

```
FUN_1800aa2c0(solver)                                         -- equilibrate the whole system in place
    a = the largest diagonal element, walked from the last:  blocks of four from the top take MAXSD(a,d), MAXSD(a,d),
        MAXSD(d,a), MAXSD(a,d);  the remainder MAXSD(d,a)          -- MAXSD(x,y) = x > y ? x : y, so a NaN's fate is per slot
    s = a > 1e-19 ? 1.0/a : 1.0
    b = the largest |rhs|, walked from the last, b = MAXSD(|r|, b) every step;  b > 1e-19 ? t = 1.0/b : (b = 1.0, t = 1.0)
    solver+0x28 = s·b;   every matrix element ·= s (s·m);   every rhs ·= t (t·r)
a sub-system on the stack (FUN_1800a4600), m×m, its matrix and two vectors from the arena
solver+0x20 (the result) zeroed
FUN_1800a4d40(sub, solver, active, m):  sub[i][j] = solver[activeᵢ][activeⱼ], sub.rhs[i] = rhs[activeᵢ]
    (when active[m−1] == m−1 it copies whole leading rows instead — the same values)
four more matrices initialised (FUN_1800a4600) for the constraint solver's object on the stack
FUN_1800a80a0(sub) == 1  and  FUN_1800aa9f0(solver, sub.result, active, m, arena) == 1  → solved
otherwise:
    k = how many contacts lead the (sorted) list with a negative cp+0x92
    FUN_1800a5e60(lcs, solver.matrix, solver.rhs, solver.result, n, k, arena) != 1 → return, nothing written
solved:
    result[i] ·= solver+0x28 (f·x), every i
    before = FUN_1800aa1a0(system)
    every contact of the list, index i (cp; record = cp+0x70):
        i < system+0x7c → x = 0
        x = result[i]:
            x > 0:   cp+0x92 = cp+0x92 ≥ 0 ? −1 : cp+0x92 − 1
            x == 0 (and NaN, UCOMISD):  cp+0x92 = 0;  no push
            x < 0:   cp+0x92 < 0 → 0;  then +1;  above 9 → 0
            x != 0:  FUN_1800a9280(record, x);  x = MAXSD(x, 0)
        cp+0x88 = (float)((double)event+0x4 · x)
    after = FUN_1800aa1a0(system);  allowance = FUN_1800aa010(system)
    after > allowance + before  →  every movable core (system+0x60, count +0x5a, last first): FUN_180076670 — pending changes dropped
    otherwise                   →  the same cores: FUN_180077950 — pending changes committed
```

The helpers:

```
FUN_1800a80a0(sub) — elimination without row exchange, then FUN_1800a8c90(sub):
    every column i:  FUN_1800a4f20(sub, i);  p = M[i][i];  |p| < sub+0x0 (1e-9) or NaN → next column
        q = −1.0 / p   (DAT_1800eaa00)
        every row j > i with |M[j][i]| > 1e-9:  f = M[j][i]·q;  FUN_1800a5150(&M[j][i], &M[i][i], f, n − i, 0);  rhs[j] = f·rhs[i] + rhs[j]
FUN_1800aa9f0(solver, x, active, m, arena) → whether the sub-system's answer holds for the whole:
    flags = n zeroed ints from the arena;  bad = 0
    every active i:  flags[activeᵢ] = 1;  solver.result[activeᵢ] = xᵢ
        (double)(float)(env+0x138 · 0.01f) > (xᵢ·solver+0x28)·(double)record+0x94 → bad    -- COMISD/CMOVA: NaN is not bad
    every i with flags[i] == 0:  FUN_1800a7270(solver, i) == 0 → return 0
    return bad == 0
FUN_1800a9280(record, x) — one push through the record, into the pending changes:
    A = record+0x98, if any:  A+0x110 += (double)(float)(record+0xf0 ⊙ A+0x40)·(−x);  A+0x120 += (double)n·−((double)A+0x4c·x)
                              FUN_180076710(A)
    B = record+0xa0, if any:  B+0x110 += (double)(float)(record+0x100 ⊙ B+0x40)·x;   B+0x120 += (double)n·((double)B+0x4c·x)
                              FUN_180076710(B)          -- n = record+0x20; each lane (float)(product + (double)old)
FUN_180076710(core) — the limits (core+0x10 → env, +0x48):
    limits+0xc > 0 (NaN passes):  |v| (+0x140) > it → v ·= (double)(float)(limit/|v|);  then |pending v| (+0x120) the same
    limits+0x14 > 0:  w = (float)env+0x110 · limits+0x14;  |ω| (+0x130) > w → ω scaled;
                      then |pending v| (+0x120!) > w → pending ω (+0x110) ·= w/|pending v|
    -- |u| = sqrt((double)(float)((u.x² + u.y²) + u.z²)), narrowed to float before comparing
FUN_1800aa1a0(system) = Σ over the cores (+0x50, count +0x4a, last first) of FUN_180077e80(core, v + pending v, ω + pending ω)
FUN_180077e80(core, v, ω) = ((double)(float)((ω.x²·I.x + ω.y²·I.y) + ω.z²·I.z) + (double)(float)((v.x² + v.y²) + v.z²)·(double)core+0x2c)·0.5
FUN_1800aa010(system) = Σ over the cores not flagged 2 (byte core+0x0) of (double)(float)(c·env+0x138)·(double)0.1f,
    c = min(max(core+0x2c, limits+0x1c), limits+0x1c) — MAXSS then MINSS against the same value, so c is limits+0x1c whatever the mass
FUN_180077950(core):  ω += pending ω;  v += pending v  (float);  pending zeroed        FUN_180076670(core):  pending zeroed
FUN_1800a4f20(sub, i) — the pivot:  best = |M[i][i]|;  rows r = n−1 down to i+1:  |M[r][i]| > best → best, p = r   (ties keep the lower row)
    a pivot found → rows i and p exchanged (n elements), then rhs[i] and rhs[p]
FUN_1800a5150(d, s, f, count, 0):  d[k] = f·s[k] + d[k]    (the 0 aligns both pointers down to 8, a no-op on the arena's arrays)
FUN_1800a8c90(sub) — back substitution, returns 1, or 0 with the result zeroed:
    i = n−1 down to 0:  s = rhs[i];  k = n−1 down to i+1:  s −= rhs[k]·M[i][k]   (rhs[k] already holds x[k])
        |M[i][i]| ≥ eps → x = s/M[i][i];   else |s| ≥ eps·1000.0 → fail;   else x = 0          -- COMISD/JNC: NaN pivot → the else
        rhs[i] = x
    result = rhs
FUN_1800a7270(solver, i) → whether contact i is not left pulling:
    s = Σ M[i][k]·x[k] (x the result, each product x·M):  when n ≥ 4 the first 4⌊n/4⌋ in two SSE pairs,
        ((Σ k≡0 + Σ k≡2) + (Σ k≡1 + Σ k≡3)), each Σ from +0.0 in order;  the rest added one by one after
    return |rhs[i]·(double)1e-5f| + s ≥ rhs[i]            -- SETNC: NaN → 0
```

**The constraint solver's setup, `FUN_1800a5e60(lcs, M, b, x, n, k, arena)`:**

```
lcs+0x78 = lcs+0x7c = n;  FUN_1800a4700(lcs, arena):
    +0x68, +0x70: n ints each;  one block of doubles: +0x48, +0x50, +0x40, +0x58, +0x60 (n each), +0xb0, +0xb8 (n·n each),
    +0xd0, +0xd8, +0xe0, +0xe8 (n), +0x108 (n·n), +0x110, +0x118 (n), +0x180 (n·n), +0x188, +0x190 (n)
+0x80 = 0;  +0x0 = +0x8 = +0xf8 = +0x120 = +0x170 = 0x3e7ad7f2a0000000 ((double)1e-7f)
+0x10 = 0x3f1a36e2f0400000;  +0x18 = 10000.0;  +0xa8 = 0x3eb0c6f7a4000000        -- neither of these two is a widened float
+0x30 = +0xe0;  +0x138 = +0x40;  +0x140 = +0x50;  +0x88 = +0xa4 = 0;  +0xf0 = n;  +0xf4 = 0
+0x128 = +0x12c = n;  +0x130 = M                                            -- a matrix view at +0x120 over M, rhs +0x40, result +0x50
every i < n:  +0x68[i] = +0x70[i] = i;  x[i] = 0;  +0x48[i] = −b[i]
+0x20 = M;  +0x28 = b;  +0x38 = x;  +0x8c..+0xa3 = 0
FUN_1800a9010(lcs, k);  r = FUN_1800a8200(lcs)
the views' pointers cleared (+0x108..0x11f, +0x130..0x147, +0x180..0x197);  return r

FUN_1800a9010(lcs, k) — the warm start:
    +0x80 = +0x84 = +0x88 = +0xf4 = k;  +0x58[i] = 0 every i
    FUN_1800a7e80(lcs) != 1 → +0x80 = +0x88 = +0xf4 = 0;  return
    +0xa4 = 0
    loop:
        FUN_1800a8be0(lcs+0xa8)
        every j < +0x80:  +0x58[+0x68[j]] = +0xd8[j]
        FUN_1800a59e0(lcs)
        the last j < +0x80 with 0 > +0x38[+0x68[j]] (NaN is not);  none → return
        p = +0x68[j]:  +0x38[p] = +0x58[p] = 0;  +0x88 −= 1;  a = +0x80 − 1;  +0x80 = a
            exchange +0x68[a] and +0x68[j], and +0x70 of both to match
        +0xa4 == 0 ? (FUN_1800a4870(lcs+0xa8) != 1 → +0xa4 = 2) : +0xf4 −= 1
        +0xa4 > 0 → +0x48[i] = +0x38[i] = 0 every i < +0x78;  +0x80 = +0x88 = +0xf4 = 0;  return
        every j < +0x80:  +0xd0[j] = +0x28[+0x68[j]]
```

**The active set's inverse lives in an object at `lcs+0xa8`** — call it `inv`: `+0x0` its epsilon, `+0x8` the inverse `I` and
`+0x10` the working copy `A` (both `lcs+0xf0`-stride, n·n), `+0x28` a gathered rhs, `+0x30` a solution, `+0x40` scratch,
`+0x48` the stride, `+0x4c` the size (`lcs+0xf4`, the active count). `lcs` indexes the full system through `+0x68` (active
position → contact) and `+0x70` (its inverse); the first `+0x80` positions are the active contacts.

```
FUN_1800a7e80(lcs) → 1 when the active set's matrix inverts:
    every active row r:  inv.rhs[r] = b[+0x68[r]];  A[r][c] = M[+0x68[r]][+0x68[c]] every active c
    size 0 → 1
    I = identity (rows from the last: size zeros, then the diagonal 1.0)
    every s = 1 .. size−1, column c = s − 1:
        FUN_1800a7ca0(inv, c);  FUN_1800a7990(inv, c) == 0 → return 0
        rows r = size−1 down to s:  f = A[r][c];  f != 0 (UCOMISD: NaN skipped too) → FUN_1800a4630(inv, c, r, f)
    return FUN_1800a7990(inv, size−1) != 0
FUN_1800a7ca0(inv, c) — the pivot:  best = |A[c][c]|, p = c;  rows r = size−1 down to c+1:  |A[r][c]| > best → best, p = r
    p != c → FUN_1800a5210(&A[c][c], &A[p][c], size − c, 0);  FUN_1800a5210(&I[c][0], &I[p][0], size, 0)
FUN_1800a7990(inv, c):  d = A[c][c];  |d| < inv+0x0 or NaN → 0
    q = 1.0/d;  I[c][k] ·= q every k < size;  A[c][k] ·= q every k in c+1 .. size−1;  A[c][c] = 1.0;  → 1
FUN_1800a4630(inv, c, r, f):  g = −f;  A[r][k] = g·A[c][k] + A[r][k] for k in c+1 .. size−1;  I[r][k] = g·I[c][k] + I[r][k] every k;  A[r][c] = 0
FUN_1800a8be0(inv):  inv+0x40[i] = inv.rhs[i] every i;  FUN_1800a7870(inv);  then FUN_1800a8ea0(inv)
FUN_1800a7870(inv):  i = size−1 down to 0:  inv+0x38[i] = Σ I[i][k]·inv+0x40[k], k from size−1 down, from +0.0
FUN_1800a8ea0(inv):  i = size−1 down to 0:  s = Σ A[i][k]·x[k] for k from size−1 down to i+1, from +0.0 (x = inv+0x38);
    x[i] = x[i] − s;  inv+0x30[i] = x[i]
FUN_1800a5210(a, b, count, 0):  a[k] ⇄ b[k]
FUN_1800a4870(inv, j) → 1, or 0 when the last pivot vanishes — contact j leaves the active set; size −= 1 either way:
    every row r < size:  I[r][j] ⇄ I[r][size−1];  then every row: A[r][j] ⇄ A[r][size−1]
    rows r = size−2 down to j+1:  f = A[r][j];  f != 0 (NaN skipped) → I[r][k] = −f·I[size−1][k] + I[r][k] every k;  A[r][j] = 0 always
    FUN_1800a7990(inv, j) != 1 → I[j][k] = 1.0·I[size−1][k] + I[j][k] every k;  A[j][j] = 1.0
    columns c = j .. size−2:  f = A[size−1][c];  f != 0 → FUN_1800a4630(inv, c, size−1, f)
    d = I[size−1][size−1];  |d| < eps or NaN → size −= 1, return 0
    I[size−1][k] ·= 1.0/d every k;  I[size−1][size−1] = 1.0
    rows r = size−2 down to 0:  f = I[r][size−1];  f != 0 → I[r][k] = −f·I[size−1][k] + I[r][k] every k, then I[r][size−1] = 0
    size −= 1;  return 1
FUN_1800a4be0(lcs) — the anti-cycling shuffle, nothing when fewer than two are active:
    +0x94 += 1, +0x98 += 2, each brought below +0x80 by repeated subtraction;  exchange +0x68 at those two positions (+0x70 to match)
    +0x9c += 1, +0xa0 += 2;  e = +0x78 − +0x88 − 1;  e < 2 → return
    each brought below e the same way;  exchange +0x68 at +0x88 + +0xa0 + 1 and +0x88 + +0x9c + 1 (+0x70 to match)
FUN_1800a7530(lcs) — the change in every inactive residual per unit step:
    every position i from +0x80 to +0x78−1, c = +0x68[i]:
        s = Σ +0x40[p]·M[c][p] over the active p = +0x68[j], j ascending, from +0.0;   +0x50[c] = s + M[c][+0x68[+0x88]]
FUN_1800a7af0(lcs) → whether the answer holds:
    every i < +0x78:  +0x30[i] = (Σ M[i][k]·x[k], k from the last, from +0.0) − b[i]
    any active p:  |+0x30[p]| > +0x10 → 0;   any other position's p:  |+0x30[p] − +0x48[p]| > +0x10 → 0;   → 1   (NaN passes)
```

**The constraint solver's loop, `FUN_1800a8200(lcs)` → 1 solved, 0 given up.** Positions `[0, +0x80)` are active (pushing, residual
held at zero), `[+0x80, +0x88)` are settled inactive (push zero, residual positive), `+0x88` is the one being brought in, and the
rest wait. `x` is `+0x38`, the residual `w` is `+0x48`, the step's direction `+0x40` (x) and `+0x50` (w); `eps` is `+0x0`, `big`
is `+0x18` (10000.0). Its locals: `total` 0, `small` 0, `countdown` 7, `stepped` 0.

```
top:  stepped → total += 1;  total > 250 → return 0
      countdown == 0 → (FUN_1800a7af0(lcs) ? countdown = 7 : goto restart)  else countdown −= 1
next: j = +0x88;  j ≥ +0x78 → return 1;   c = +0x68[j]
      +0xa4 == 1 and stepped → FUN_1800a4be0(lcs); FUN_1800a7e80(lcs) == 1 → +0xa4 = 0
      +0xa4 > 0 otherwise → +0xa4 = 1
      |w[c]| < eps (NaN too) → |x[c]| < eps (NaN too) ? goto settle : goto join(j)
      w[c] ≥ 0 → goto settle
      FUN_1800a5740(lcs) == 0 → goto restart
      +0x40[c] = 1.0;  FUN_1800a7530(lcs);  +0x50[p] = 0 for every active p
      −w[c] < +0x50[c]·big ? (t = (−1.0/+0x50[c])·w[c], best = j) : (t = DAT_1800eedc0 = 0x54e6dc186ef9f45c ≈ 1e101, best = −1)
      every active position i, p = +0x68[i], d = +0x40[p]:  d < −eps (NaN too):
          r = (−1.0/d)·x[p]
          |r| < eps (NaN too) and x[p] < eps (NaN too) → best = i, s = r, goto chosen            -- no clamp at zero on this exit
          r < t + eps → t = r, best = i
      every settled position i, p:  d = +0x50[p] < −eps:  r = (−1.0/d)·w[p];  r < t − eps → t = r, best = i
      s = MAXSD(t, 0);  best < 0 → return 0
chosen:
      s > big → goto restart
      s ≥ eps → small = 0  else  small += 1, small > (+0x78 >> 1) + 2 → goto restart
      stepped = 1
      every i < +0x78:  w[i] = s·+0x50[i] + w[i];  x[i] = s·+0x40[i] + x[i]
      settled p with 0 > w[p] → w[p] = 0;   active p with 0 > x[p] → x[p] = 0
      best < +0x80 (an active contact's push reaches zero):
          x[+0x68[best]] = 0;  +0x80 −= 1;  exchange positions best and +0x80 (+0x70 to match)
          +0xa4 == 0 ? (FUN_1800a4870(inv, best) != 1 → +0xa4 = 2) : +0xf4 −= 1;   goto top
      best < +0x88 (a settled contact's residual reaches zero):
          s > eps → drop every active contact with eps > x (below)
          w[+0x68[best]] = 0;  exchange positions best and +0x80;  goto grow
      otherwise goto join(best)                                                              -- best == j
join(b):
      drop every active contact with eps > x
      p = +0x68[b]:  w[p] = 0;  0 > x[p] → x[p] = 0;  exchange positions b and +0x80;  +0x88 += 1
      +0x88 ≥ +0x78 → +0x80 += 1, countdown = 0, goto top
grow: q = +0x68[+0x80];  +0x80 += 1
      +0xa4 != 0 → +0xf4 += 1, goto top
      A[+0xf4][r] = M[q][+0x68[r]] every r < +0x80;  inv.rhs[r] = M[+0x68[r]][q] every r < +0x80 − 1
      FUN_1800a5b80(inv) != 1 → +0xa4 = 2;   goto top
settle:
      stepped = 0;  p = +0x68[+0x88]:  x[p] = 0;  0 > w[p] → w[p] = 0;  +0x88 += 1;  +0x88 ≥ +0x78 → countdown = 0;  goto top
restart:
      total += 1;  total > 250 → return 0;  small = 0;  FUN_1800a52d0(lcs) == 0 → return 0;  countdown = 7;  goto next
drop every active contact with eps > x:   i from 0 while i < +0x80:  p = +0x68[i];  eps > x[p] (not NaN):
      x[p] = 0;  exchange positions i and +0x80 − 1;  +0x80 −= 1
      +0xa4 == 0 ? (FUN_1800a4870(inv, i) != 1 → +0xa4 = 2) : +0xf4 −= 1;   look at position i again
```

Its three larger helpers:

```
FUN_1800a5740(lcs) → 1, or the elimination's answer — the direction for bringing +0x68[+0x88] = q in:
    +0x40[i] = 0 every i < +0x7c;   no active contact → 1
    +0xa4 == 0:  inv.rhs[r] = −M[+0x68[r]][q] every active r;  FUN_1800a8be0(inv);  result 1
    otherwise:   +0x90 += 1;  the view at +0xf8 sized +0x80 × +0x80:  its rhs[r] = −M[p][q], its M[r][c] = M[p][+0x68[c]]  (p = +0x68[r])
                 result = FUN_1800a80a0(view);  +0xd8[r] = the view's result[r]
    +0x40[+0x68[r]] = +0xd8[r] every active r;  +0x40[q] = 1.0;  return result
FUN_1800a5b80(inv) → FUN_1800a7990's answer — the inverse grows by the row and column the caller left:
    inv+0x40[i] = inv.rhs[i];  FUN_1800a7870(inv)
    A[r][size] = inv+0x38[r] every r, from the last;  I[r][size] = 0 every r;  I[size][k] = 0 every k < size;  I[size][size] = 1.0
    size += 1;  every c < size−1:  FUN_1800a4630(inv, c, size−1, A[size−1][c])       -- no zero test here
    return FUN_1800a7990(inv, size−1)
FUN_1800a52d0(lcs) → 1, or 0 when even elimination fails — the restart:
    do:
        +0xf4 = +0x80;  +0x58[i] = 0 every i;  FUN_1800a4be0(lcs)
        FUN_1800a7e80(lcs) == 1:  +0xa4 = 0;  FUN_1800a8be0(inv);  +0x58[+0x68[r]] = +0xd8[r] every active r
        otherwise:  +0xa4 = 2;  FUN_1800a6160(lcs);  FUN_1800a4be0(lcs)
                    the view at +0xf8 over the active block, its rhs[r] = b[+0x68[r]];  FUN_1800a80a0(view) != 1 → return 0
                    +0x58[+0x68[r]] = the view's result[r]
        FUN_1800a59e0(lcs)
    while FUN_1800a5520(lcs) > 0
    return 1
FUN_1800a6160(lcs) — the active positions sorted by x, largest first:  insertion sort, a later one moving down while its x is
    strictly greater than its neighbour's (+0x70 kept matching)
FUN_1800a5520(lcs) → how many active contacts were pushing BACKWARDS:
    every active position i:  p = +0x68[i];  x[p] ≥ eps → keep
        x[p] > −eps:  x[p] = 0;  exchange positions i and +0x80 − 1
        otherwise (NaN too):  position i moved to the very end, everything after it one down;  count += 1;  +0x88 −= 1
        either way:  +0x80 −= 1;  +0xa4 = 1;  +0xf4 −= 1;  look at position i again
    every settled position i:  0 > w[+0x68[i]] (not NaN) → moved to the very end the same way;  +0x88 −= 1;  look again
    return count
```

**It is a Dantzig-style principal pivoting loop**: bring each contact in, move along the direction that keeps the active
contacts' residuals at zero until the first active push or settled residual hits zero, swap it, repeat. The inverse of the active
block is kept incrementally (`FUN_1800a5b80` grows it, `FUN_1800a4870` shrinks it), and when that bookkeeping fails `+0xa4` marks it
stale so the next pass rebuilds it from scratch (`FUN_1800a7e80`), shuffling the order first to break a cycle. Every seven steps
the answer is checked against the full system, and a failed check or a runaway step restarts from the current answer.

**Ported and pinned, 2026-09-13.** `IvpLinearSystem` (the `FUN_1800a4600` object: `Equilibrate`, `Gather`, `Solve`, `Holds`,
`Multiply`), `IvpActiveInverse` (the `+0xa8` object: `Invert`, `Solve`, `Grow`, `Shrink`) and `IvpComplementaritySolver`
(`FUN_1800a5e60`, the warm start, the loop and its helpers). The `vphysics-contact-solve` probe writes each system at the offsets
above, gives the solver a megabyte of arena, and calls the five routines in `FUN_1800aa5c0`'s order: **20,000 random systems —
Gram matrices of short rows, often singular, general and integer-valued ones, nearly vanishing ones, NaN and infinity — agree
on every lane**, the permutation at `+0x68` and the counters at `+0x80..+0xf4` included. The identity with right-hand side
`(1, 2, −1)` is the control, solved to `(0.5, 1, 0)` after scaling by both.

**One sweep case differed, and only in a NaN's sign — and it is fixed, not licensed.** With a NaN in both a matrix element and
the right-hand side, the binary left `0xfff8…` in a residual and the port `0x7ff8…`. `DOTNET_JitDisasm` on the step shows why:
the binary adds `s·dw + w` with the product as SSE's destination, whose NaN survives two NaNs, while the JIT — unoptimised, at
Tier0 — loaded `w` first. **Which operand of a commutative `+` or `*` is the destination is the JIT's choice, not the
source's.** It was first filed as a difference C# could not avoid, and that was wrong: the ECMA rules leave only the
two-NaN case open, so `Addsd(d, s) = IsNaN(d) ? d + d : d + s` (and `Mulsd` alike) returns the destination's NaN, quieted,
whatever order the JIT emits, and every other case carries the one NaN either way. Every addition and multiplication in the
three ports now names the binary's destination first.

**The destinations were mapped twice, independently, and they are not uniform.** Two passes per file read every `ADDSD`,
`MULSD`, `ADDPD` and `MULPD` against the C#, and an adjudicator settled the eight sites they disagreed on; the grouping claims
they raised were then checked by hand, and both were wrong (the sums are in order). What held:

- `FUN_1800a8c90`'s back substitution, four columns at a time from the last, makes the **third** product of each four with the
  value as destination and the other three with the right-hand side; the scalar tail uses the right-hand side.
- `FUN_1800a8ea0`'s back substitution, four at a time, adds the first two products with **the product** as destination
  (`1800a8f5f`, and `1800a8f73` after `MOVAPS XMM2,XMM1` — which one pass read as the running sum), multiplies those two with
  the right-hand side first and the last two with the matrix first; the tail adds onto the sum with the matrix first.
- `FUN_1800a7990` and `FUN_1800a4870` scale a run four at a time with each element as `MULPD`'s destination, and the remaining
  elements with the factor as `MULSD`'s.
- Everything else keeps one destination on every path: the scalar before the array in each scaling and step, the running sum
  before the product, and in the SSE pair sums the accumulators.

**Checked by a sweep built to make NaNs meet**: 30,000 systems with one to four values replaced by quiet or signalling NaNs of
either sign, a payload, or an infinity agree with the binary on every lane, NaN bits included, alongside 30,000 random and
30,000 near-singular ones.

**The first fixture could not fail on five rules, and random draws do not reach them.** Sabotage showed 160 random systems
blind to the vanished pivot's `1000·eps`, the row test's `(double)1e-5f`, the step's tie margin, the restart's sort and the
restart's near-zero push; 200,000 heaps of near-identical contacts reached the restart's fallback once. **The instrument that
found the inputs was the oracle itself, pointed at a port broken on purpose**: `sweep n generator path` writes every case whose
lanes differ from the binary, so with one rule sabotaged the file holds exactly the inputs that rule decides. Two rules were
settled by hand-built cases (a singular `2×2` whose residual lands either side of `1000·eps`; a row `r − r·9.9999999e-6` that
holds under `(double)1e-5f` and not under `1e-5`), and the other three by generators aimed at them — a tie-heavy heap
`a·I + b·11ᵀ` with grouped right-hand sides (1,062 of 30,000 told the tie margin apart), and NaN-seeded systems. Swapping one
NaN destination in the step separated 934 of 30,000 NaN-seeded systems from the binary and one random one — the case that first
showed the difference. The sabotage-found cases are listed in the probe by generator and index, so the fixture regenerates
them. **One sabotage cannot redden at its site in the binary either**: `FUN_1800a9010` writes `2` to `+0xa4` and then zeroes
the state whenever it is positive, so `1` and `2` are indistinguishable there. On the restored port, 120,000 systems across the
four generators agree on every lane.

**Fifteen sabotages, then two more inputs.** An independent run against the 206-case fixture reddened twelve; the thirteenth
was that dead `2`. The other two were the path-dependent NaN destinations: the third product of `FUN_1800a8c90`'s four-wide
block, which 30,000 NaN-seeded systems told apart once (kept), and the first add of `FUN_1800a8ea0`'s block, which none of
120,000 did — **and the arithmetic says why a sweep cannot find it**: the second add names the next product as destination, so a
NaN there overwrites whichever NaN the first add kept; the inverse must also be valid, which a NaN on its diagonal or below
refuses. The input had to be built: an identity of nine or ten contacts warm-started whole, with NaNs of different sign and
payload only in its first row — at the second add of the first four and the first add of the second four, the next product
finite. It inverts untouched, and back substitution leaves the binary's NaN in that contact's push: `0xfff8…05` against the
broken port's `0x7ff8…08`, exactly as worked out beforehand. With three such cases the fixture holds 210 (160 random, 30
targeted, 20 sabotage-found), and every sabotage that can redden does.
FUN_1800a59e0(lcs) — the full system's residual at the current x:
    the view at lcs+0x120 (M, +0x128 rows, +0x12c columns) pointed at +0x58 → +0x60:  FUN_1800a76c0(view);  pointed back at +0x40 → +0x50
    +0x60[i] −= b[i] every i < +0x78;  +0x60[+0x68[j]] = 0 every active j
    i from the last:  +0x48[i] = +0x60[i];  x[i] = +0x58[i]
FUN_1800a76c0(view):  result[i] = Σ M[i][k]·x[k] — the same two-pair SSE sum as FUN_1800a7270, its length the ROW count (+0x8)
```

**So a heap of contacts first tries the contacts that pushed last time as an exact system**, keeps that answer when every push
is firm enough and no other contact is left pulling, and only otherwise hands the whole set to the constraint solver, warm-started
with the contacts that have pushed longest. **Then it refuses its own answer if the heap gained more energy than a tenth of `g`
times a per-core constant** — the changes are pending until that test. `record+0x94` is the contact's inverse effective mass along
its normal (the record's own setup, above: arm terms plus both inverse masses, `+0x90` its reciprocal), so the firmness test reads
`x·scale·m⁻¹` — the speed the push makes — against `g·0.01`. `limits+0x1c` is the **minimum** friction mass `SetPerformanceSettings`
clamps to `[1, 50000]`; `+0x20`, the maximum, is never read here, so the allowance is `n · (float)(minFrictionMass·g) · 0.1f` for
the heap's movable cores whatever their masses. The client never calls `SetPerformanceSettings`, so its `+0x1c` is IVP's own
default from `FUN_180089550`: `10.0f` (`0x41200000`; `+0x20` is `2500.0f`), the same numbers as
`physics_performanceparams_t::Defaults()`.

**The core-level routines are ported and pinned, 2026-09-14**: `FUN_1800a9280` as `IvpContactRecord.Push`, `FUN_180076710` as
`IvpPush.Limit`, `FUN_180077950` and `FUN_180076670` as `IvpPush.Flush(IvpRigidBody)` and `IvpPush.Drop`, and `FUN_180077e80`
as `IvpRigidBody.KineticEnergy`, beside `IvpRigidBody.Mass` for `core+0x2c` (written by `FUN_180073df0`'s port). The
`vphysics-heap-core` probe writes two cores, a record and an environment at the offsets above and runs each routine on fresh
copies: 20,000 cases, a quarter of them seeded with NaNs of both signs, signalling NaNs and infinities, agree on every lane.
Two things the destination map settled there: **the flush adds the real velocity to the staged one in its `x` lanes and the
staged to the real in `y` and `z`**, and **the push's first core multiplies the turn by the inertia in `x` but the inertia by
the turn in `y` and `z`**, while its second core takes the turn first in all three. `FUN_18006e120` — the float length the
limits measure with — sums onto the running total, and `IvpVector.Length` now says so.

**The first fixture reddened on five of ten sabotages, and the NaN seeding was why.** Two of the five that stayed green are
equivalent mutants — a NaN limit and a length exactly equal to the limit both leave the vectors unchanged either way the test
is written. The other three swapped the operands of an add or multiply, and a quarter of cases seeded with NaNs never put two
NaNs of different payload on one operation, so no case could tell which survived. **45 NaN-pair cases** — one per add and
multiply these routines meet two inputs at, each with a different-payload NaN on both sides — took the fixture to 285 cases.
An independent run then swapped six operand orders (the push's first-core `y` and second-core `x` multiply, its first-core
spin add, the flush's velocity `x` and spin `y` adds, and the energy's mass multiply): **all six reddened**, each by one or two
NaN-pair cases whose diagnostic showed the two payloads exchanged, and none by any other case.

**The heap solve itself is ported and pinned, 2026-09-14**: `FUN_1800a9bf0`'s sort and its solve from `+0x7c = 0` on — the
freeze past 150 contacts, `FUN_180083100`'s records, `FUN_1800a9520`, `FUN_1800aa5c0`, `FUN_1800aa9f0`, `FUN_1800aa1a0` and
`FUN_1800aa010` — as `IvpFrictionSystem`, beside `IvpFrictionInfo` (a core's `0x18`-byte share), `IvpFrictionPair`, the contact
point's list links, streak and push, and the record's row, shares and copies. The `vphysics-heap-solve` probe writes a whole
friction system at the offsets above — cores, objects, contact points, records, shares, pairs, the environment with its limits,
an eight-megabyte arena and an anomaly manager whose slot 5 is a managed callback — settles the tolerance block with the binary's
own `FUN_180098fd0` run as `IvpCollisionTolerance` runs it (`block[0x43]` comes out `0x3c4ffe5a` both ways), and calls
`FUN_1800a9bf0`: **30,000 random systems of up to eight contacts among up to four cores, a quarter NaN-seeded, agree on every
lane**, and `IvpHeapSolveConformanceTests` replays 208 the binary wrote: 200 random, three 152-contact heaps (each answer from slot 5,
and a frozen one whose mover pairs with an immovable core named second) and five sweep cases found against sabotaged ports.

**Twenty-four sabotages, then more inputs.** The first 202-case fixture reddened on thirteen; three sabotages were malformed and
broke the build. Of the eight that stayed green, four are dead by construction: `k·0` is `+0` whichever stiffness takes a zero
gap, and a NaN's payload never reaches an output from the closing-speed sum or the energy sums — the energies meet only a
comparison, and a NaN push is treated as no push. The other four named missing inputs: **no case had a pull** (a sub-system answer
below zero survives `FUN_1800aa9f0` only when gravity's `0.01` share is not above it, so the generator now draws negative gravity
too), none started a pull streak at 8, none changed the active rows enough to round differently from the constraint solver, and
none froze a heap where the pair test's second immovable check mattered. Sweeping the binary against a port broken on each found
the cases the probe now lists as `Killers`. **An independent run of those four and the three rewritten well-formed reddened all
seven** against the 208-case fixture, each by its own subset.

**The lone contact and the controller routine over both paths are ported too, 2026-09-14**: `FUN_180084320`'s dispatch as
`IvpFrictionSystem.SolveNormalPushes`, `FUN_180084490` as its lone-contact branch, and `FUN_180083420` as
`IvpContactRecord.Apply` — `FUN_1800a9280`'s arithmetic instruction for instruction, into the velocity and spin themselves and
with no limits after, so the two now share one body. The probe calls `FUN_180084320` on the controller base at `system+0x10`
(whose `+0x8` is the system) with the event's unit at `+0x10`: 30,000 random systems, an eighth of them one contact, agree.

- **The lone contact's closing speed adds the first core's normal term to its turn term** (`ADDSD XMM6,XMM0` with the normal
  dot in `XMM6`), the other way round from the heap's matrix build, and it reads the contact's own gap rather than a record copy.
- **A lone contact's NaN gap drops it** (`COMISS block[0x47], gap; JBE`), where the heap's filing pass keeps one. The sweep
  found that first as a crash: a poisoned gap sent the binary into `FUN_180083e40` on fabricated objects. The fixture keeps a
  lone contact's gap finite or `−∞` until the drop is ported.

Eight sabotages of the lone path and the shared push body — the dispatch's `≤ 1`, the gap, the virtual mass, the push's sign on
either core, the zero written for no push, the base spin, and one turn product's operand order — each redden the heap-solve and
heap-core fixtures; the operand order only through the heap-core NaN pair built for it.

*Not carried yet: the drop, the empty system's deletion, and the split `FUN_180084320` runs when `+0x80` is set.*

What reading it for porting settled, beyond the decode above:

- **A contact is active when its streak copy is nonzero as a dword** — `CMP [R8+0x88],0` is `41 83 B8`, not the byte form `41 80
  B8` — so a contact pushed 256 PSIs running, whose low byte is zero, still starts active. The fixture's streaks include `−256`.
- **The heap's energy sums each core's velocity with its `x` lane staged first and `y`, `z` real first, and its spin real first
  in every lane** (`FUN_1800aa1a0`), which is not the order the flush adds them in.
- **A zero or NaN push leaves `+0.0` behind whatever its sign** — `MOVAPS XMM6,XMM7` before the `MAXSD` — so the contact's
  `+0x88` is the event's float times positive zero.
- **Slot 5 does not check for a game solver** (`FUN_180016ec0`): each core's first object's `CPhysicsObject` goes into a list
  handed straight to `ShouldFreezeContacts`, where slot 3 answers yes when there is none. The client's answers yes
  (`game/client/physics.cpp:78`).
- **The record's `+0x78` and `+0x80` shares are written over its push-out, estimate and elasticity.** The port keeps them as
  separate fields; the two readings agree while every reader of those bytes rebuilds the record first, *which is not established.*

**The filing pass between the sort and the solve is read but not ported.** `FUN_180083e40(system, cp)`, which drops a contact:

```
FUN_180078820 on both objects' physical cores:  core+0x200 = core+0x208 = env+0x188 (the time)
FUN_180088ce0: unlinked, +0x7a − 1 (the head moved when it was first)
FUN_180088130: its pair found (FUN_1800863f0; none → assert, line 0x299); FUN_180083da0 takes the contact off the pair;
    FUN_180086b30 answers whether any remain; none → FUN_180083db0 takes the pair off the system, destructor, 0x50 freed, answer 1
    answer 1 → system+0x80 = 1
each object's share (FUN_180077f00):  FUN_180075130 takes the contact off it (last match, the rest moved down)
    a share left empty → FUN_180077c10 takes it off its core (an immovable core's hash, FUN_1800726e0; a movable core's +0x60 zeroed),
        FUN_180088c80 takes the core off the system (a movable one off +0x58 and three more of the system's vectors at +0x20, +0x0
        and +0x10 by FUN_180074fb0; every core off +0x48; +0x78 − 1), and the core's unit (+0x1f8) has bit 9 cleared and bit 8 set
FUN_180083210: the contact point's destructor;  0xd0 freed
```

`FUN_180088ce0` then `FUN_180087c90` is the move to the head: unlinked with the count taken down, then linked at the head with
`cp+0xc0` pointed at the system and the count put back.

**So vphysics' surfaces never set `cp+0x64`** (a surface entry's `+0xc` is zero), and the axis friction is dead for them — the
entry's port keeps it because the routine has it. The merge, the controller bases and the simulation units are read below and
above. **Nothing here is ported.**

**A wrong citation found on the way, kept here because it was repeated in four places:** `SurfaceTable`, `GameContent`,
`CorpsePhysics` and `IvpRigidBody.Friction` all call their friction of `1` `g_PhysDefaultObjectParams`' friction. That struct has
no friction: its first `1.0` is **mass** and its second **inertia** (`game/shared/physics_shared.cpp:43-56`). The engine's answer
for a solid whose `surfaceprop` names nothing is the `default` surface — `ragdoll_shared.cpp:194-197` asks `GetSurfaceIndex` for
it, and `FUN_18001c9d0` asks again for any negative index — so the `1` was this project's own number, attributed to Valve.

*Evidence class: read from the disassembly; the props slot 10 and the parameter layout INFERRED as marked. Not ported yet.*

#### The collision's own path, instruction by instruction (`impact_entry.log`)

**`FUN_18008ecb0` reads as the summary under *What the fire routine's collision call does* gives it**, with the state byte
compared unsigned (`JNC`). What that summary compresses, and one thing it has wrong:

**`FUN_180078d60(core)`**: the `0x40` arena record at `core+0x260` holds the angular velocity at `+0x0` (the second and third
lanes round-tripped through a double), the thirty-two bytes at `core+0x1a0` at `+0x10`, and a zeroed dword at `+0x30` — what
`FUN_180079120` puts back. `FUN_1800712b0(&v, core+0x180, core+0x1a0)` leaves three doubles. The slerp parameter is
`(double)((float)(now − core+0x1d0)·core+0x1d8)`, a float product; the matrix's translation `+0xf0..0x100` is
`(double)core+0x170..0x178·(double)(float)(now − core+0x1d0) + core+0x150..0x160` in double; and unless flags `& 8`, each
angular velocity lane is `(float)(FUN_1800d392c(v)·(double)(core+0x1d8 + core+0x1d8))`, the doubling in float.

**`FUN_18008ef60(mindist, object0, object1)`**:

```
unit = object0+0x78 < 8 (a signed byte) ? core1+0x1f8 : core0+0x1f8
cp = FUN_180090e50(mindist, &system, &cp, unit, 1);   record = *(cp+0x70)
pair = FUN_1800850b0(system, core0, core1)
event: dt = (float)(now − pair+0x28), then pair+0x28 = now;  env;  record
FUN_180082170(env, event);  for each object whose +0x78 has 0x2000: FUN_180088800(env+0x18, object, event)
f = FUN_18008fca0(cp, env);   FUN_18008ed60(record, cores, f, cp)          -- cores: two pointers on this frame
s = record+0x30, negated when core1's flags have 0x2
FUN_180090700(block, mindist, system, pair, cp)       -- block: three vectors {word, word, pointer}, empty
free each vector's storage (FUN_180003e20) unless it points just past that vector
record+0x30 = s
FUN_180082110(env, event);  for each object whose +0x78 has 0x2000: FUN_1800886c0(env+0x18, object, event)
```

*That summary says the call negates "the pair's normal" and restores it around `FUN_180090700`.* What is saved is
`record+0x30` — the relative velocity `FUN_18008e290` wrote through `solver+0x148` — and it is written back after
`FUN_180090700` negated when core 1 is flagged `0x2`, so a flagged pair leaves the call with it negated, not restored.

**`FUN_18008fe70(solver, cp)`**, which `FUN_18008ed60` calls when `cp+0x64` is set, is two blocks, one per synapse, each run
when its material's `+0xc` is nonzero. A synapse's material is its object's `+0xd0` when `FUN_1800863d0` answers zero, else
slot 1 of the manager at `object0+0x30` → `+0xe8`, called with the object and that answer:

```
axis = FUN_180070130((float)object+0xf0's +0x90, +0xb0, +0xd0, n = record+0x20)   -- its first matrix column, less its part along n
len = FUN_18006e120(axis)                                                    -- a double
if !(len >= 1e-19): skip the block                                            -- COMISD/JC: a NaN skips
p = other.slot1() · this.slot2()                                              -- vtable +0x8 and +0x10, doubles
t = (√(double)solver+0x130 + 1.0)·(double)(float)((double)cp+0x78 − ((double)cp+0x78 − p)·len)
x = (float)atan(t);  c = (1f − x²·0.5f) + (x²·(1/24f))·x²
solver+0x100 = axis scaled by FUN_18006dff0;  solver+0xf4 = (float)((double)c·t)
```

**The second block overwrites the first's axis and `+0xf4`**, and `solver+0xf0` is one when either block got past its length
test, zero otherwise.

*Evidence class: read from the disassembly for all four routines. Not established: what the materials' slots 1 and 2 and
`+0xc` are, what `cp+0x78` holds, and `FUN_1800d392c`.*

#### The solver's direction, its push, and the velocities it reads (`friction_solve2.log`)

- **`FUN_18008fc00(solver)`**: `solver+0xc0 = vB − vA` in float, each from `FUN_180077fa0(core, arm, v, ω)` over the solver's own
  velocities — A's `+0x60`/`+0x40`, B's `+0x70`/`+0x50`.
- **`FUN_180090240(solver)` — which way to push**:

```
d = solver+0xc0, scaled by FUN_18006dff0 (float, four steps, over 1e-19)          -- d is solver+0xd0
k = (double)((d.x·n.x + d.y·n.y) + d.z·n.z)
if k > 0:  d = solver+0xe0, scaled the same way;  return                            -- COMISD/JBE
if solver+0xf0 != 0:  FUN_1800904a0(solver, k);  return
if k > (double)−(solver+0x134):                                                     -- inside the cone's cosine
    u = (float)((double)n·(double)−k + (double)d) per lane, scaled;  u = (float)((double)u·(double)solver+0x138)
    d = (float)((double)n·(double)−(solver+0x134) + (double)u) per lane
```

- **`FUN_18008f1c0(solver, double j)` — the push along `d`**: `p = (float)((double)d·j)`; unless A is unmovable, `p` turned into A's
  frame through its matrix's transpose, each lane `((y·m2i + x·m0i) + z·m4i)`, then `(solver+0xa0, solver+0x80) = FUN_180078f50(A,
  armA, p′, p)` and `solver+0x40 += +0x80`, `solver+0x60 += +0xa0`; unless B is unmovable, the same with `p·−1f` into `+0xb0`/`+0x90`
  and B's `+0x50`/`+0x70`. **The turn is `FUN_180070620` inlined**, with the same grouping.
- **`FUN_180070620(m, v, out)`** turns a float vector by a double matrix's transpose — `((y·m20 + x·m00) + z·m40, (x·m08 + y·m28) +
  z·m48, (x·m10 + y·m30) + z·m50)` — and narrows.
- **`FUN_18006fc90(v)`** scales a float vector in place with the five-step root when its float-summed square, widened, reaches
  `1e-19` (`DAT_1800f4f20`), and returns the length `s·squared`; under that, or NaN, it returns zero and leaves the vector alone.
- **`FUN_1800770f0(core, r, d, w)` — a virtual mass**: `1.0` for a core flagged `0x10`; otherwise the unit push's effect —
  `Δω = (r × d) ⊙ +0x40..0x48` in float and `Δv = (float)((double)w·(double)+0x4c)` — becomes a point velocity through
  `FUN_180077fa0`, and the answer is `1.0 / FUN_18006e120(that velocity)`: **the reciprocal of the whole response's length, not
  of its component along `d`**.

#### The solver's helpers, and where the block and the fused paths are decided

- **`FUN_180078f50(core, r, d, w, out Δv, out Δω)` — the effect of a unit push**: `Δω = (r × d) ⊙ (+0x40, +0x44, +0x48)` in float, the
  cross product taken `(d.z·r.y − d.y·r.z, d.x·r.z − r.x·d.z, r.x·d.y − d.x·r.y)` with `d` in the core's frame, and
  `Δv = (float)((double)w·(double)+0x4c)` per lane with `w` the world direction — `+0x40..0x4c` being the inverse inertia and mass.
- **`FUN_180077d70(A, B, rA, rB, vA, ωA, vB, ωB, out)`**: each point's velocity by `FUN_180077fa0`, zero for a core whose byte `0`
  has `0x12`, and `out = pA − pB` in float.
- **`FUN_18008ddf0(solver, i)` — committing one side**: core `i`'s `+0x140` and `+0x130` take the solver's velocity and angular
  velocity for that side; then, when the core's word `+0x2` — the impacts counted this step — exceeds the anomaly limits' `+0x10`,
  the environment's `+0x40` object's slot 3 `(limits, core)` answers, and its low two bits become the core's flag bits `6–7`.
- **`FUN_180070b20(m, p, out)`**: a float point widened, turned and moved by a double matrix — each row `((x·m0 + y·m1) + z·m2) + t`.
- **`FUN_1800863f0(system, a, b)`** finds, last first, a pair whose `+0x38` and `+0x40` hold both cores in either order;
  **`FUN_180086b30(pair)`** is its contact count at `+0x2`, so a pair begins with its contact vector; **`FUN_180083da0`** is the
  vector removal; **`FUN_180083db0(system, pair)`** tells the environment's listeners (`FUN_180081f70`) and removes the pair from
  `system+0x70`.
- **The tolerance block is derived twice before a simulation reads it.** The environment constructor `FUN_1800114f0` passes
  `(double)((0.25f − 1e-4f) × 0.0254f)` and `9.81`; `CPhysicsEnvironment::SetGravity` (`FUN_1800150f0`) passes `(double)block[1]`
  (`FUN_180082250`) and the length of the gravity it converted — `0.0254f` times each component in float, the down lane negated
  into `y`, widened, measured by `FUN_18006fc60`. The client calls `SetGravity` once, at physics init
  (`game/client/physics.cpp:177`). For this preset the second derivation returns the first's block unchanged — *arithmetic,
  evaluated over the dumped bits*.
- **`DAT_180136418` is written by `__acrt_initialize_fma3`**: one when CPUID leaf 1's `ECX` has FMA, OSXSAVE and AVX
  (`& 0x18001000`) and `XCR0` enables XMM and YMM state (`& 6`), zero otherwise. So on a machine with FMA and OS-enabled AVX,
  `exp` and `expf` take their fused paths.
- **The `exp` tables are dumped** (`impact_solver5.log`): `2^(j/64)` at `180108740`, its low parts at `180107d60` and high parts at
  `180107b60`, 64 doubles each; the series coefficients at `180104480..1801044c8`; `−ln 2/64` in two parts at `180104530` and
  `180104538`; the range limits `709.78`, `−744.03` and `−745.13`. `expf`'s `64/ln 2`, `ln 2/64`, `1/6` and `0.5` sit at
  `1801045a0..1801045d0`, its limits `8192` and `−9600` at `180104580`/`180104590`.

#### The anomaly manager, and the limits a client environment runs (2026-09-13)

**The environment constructor makes the anomaly manager itself.** `FUN_1800114f0` allocates `0x40` bytes at
`CPhysicsEnvironment+0xb0` with two tables, `1800ebf78` at `+0x0` and `1800ebf90` at `+0x8`, and hands the application
environment `+0x8` — so IVP's `env+0x40` is that half, and `SetCollisionSolver` leaves the game's solver at its `+0x10`
(`env_ctor.log`, `anomaly_manager.log`, `anomaly_slots.log`):

| slot | routine | what it does |
|---|---|---|
| 0 | `FUN_180017010` | asks the core's first object's `CPhysicsObject` (`[core+0x70]` → `+0x100`) for `GetShadowController`, `IPhysicsObject` slot 70; with none, the base `FUN_180089ae0` |
| 1 | `FUN_180089a50` | the base, not overridden |
| 2 | `FUN_180016cc0` | with a game solver and both `CPhysicsObject`s present, neither's word `+0x48` holding `0x400`: when both are movable (slot 10, `IsMoveable`), a pair already in the list at `+0x18` (count `+0x28`, ordered by address) returns at once, and an absent one is filed; then — movable or not — asks `ShouldSolvePenetration` (`IPhysicsCollisionSolver` slot 1) with both objects' `GetGameData` (slot 17) and `(float)env+0x108`; a yes, or no game solver, calls the base `FUN_1800896b0` |
| 3 | `FUN_180016e80` | `ShouldFreezeObject` (slot 2) of the core's first object, or one with no game solver |
| 4 | `180016e60` | tail-calls `AdditionalCollisionChecksThisTick` (slot 3), or answers zero |
| 5 | `FUN_180016ec0` | `ShouldFreezeContacts` (slot 4) over each core's first object |

*Slots 6 (`180016530`) and 7 (`180089660`) are unread. The slot numbers on `IPhysicsObject` and `IPhysicsCollisionSolver` are
counted from the published declarations (`public/vphysics_interface.h:683-858`, `:500-516`); `IsMoveable` at 10 and
`GetGameData` at 17 fit the same count, which is the check on it.*

**The base clamps scale to a share of the limit, not to the limit.** `FUN_180089ae0` takes `s = ((double)limits+0xc ·
(double)0.99f) / √(double)((v.x² + v.y²) + v.z²)` and `FUN_180089a50` takes `s = ((double)((float)env+0x110 · limits+0x14) ·
(double)0.9f) / √(double)((ω.x² + ω.y²) + ω.z²)`, the squares and the product in float, each lane `(float)((double)v·s)`.

**`SetPerformanceSettings` (`FUN_180015200`) fills the limits at `env+0x48`**: `+0xc = 0.0254f · maxVelocity`, `+0x10 =
maxCollisionsPerObjectPerTimestep`, `+0x18 = maxCollisionChecksPerTimestep`, `+0x14 = (maxAngularVelocity · 0.017453292f) ·
(float)env+0x108`, and `+0x1c`, `+0x20` the two friction masses clamped by `MAXSS` against one and `MINSS` against fifty
thousand; the two look-ahead times go, widened, to `[env+0x38]+0x40` and `+0x18`.

**The spin limit is per step of the PSI in force when the settings were given, and nothing takes it again.**
`SetSimulationTimestep` (`1800152f0`) jumps to `FUN_180082470`, which writes the step at `+0x108`, its reciprocal at `+0x110`
and `exp(step · ln 0.9)` at `+0x1b0` (`bfbaf8e892d15de8`) — and not the limits. The check multiplies by the CURRENT reciprocal
(`FUN_18008dd00`), so an environment built at one step and run at another holds spin to the ratio of the two.

**A client's environment gets its settings only from its own constructor.** `FUN_1800114f0` calls `FUN_180015200` with `{6,
250, 2000, 3600, 1, 0.5, 10, 2500}` on its stack — `physics_performanceparams_t::Defaults()` (`public/vphysics/performance.h:30-40`)
— before anything sets the step. The server raises the collisions to 10 before its own call (`game/server/physics.cpp:222-226`);
the client's `PhysicsLevelInit` never makes one (`game/client/physics.cpp:163-187`), and its `CCollisionEvent::ShouldFreezeObject`
answers `true` (`:76`). **So a corpse's environment allows 6 collisions, not 10** — this project had carried the server's number
as the one TF2 runs.

**Then the whole solver, called in process** (`vphysics-impact`). The probe writes the solver, both cores, the environment and
the limits at the offsets above, gives the manager the image's own table, and answers the two calls that leave the library —
the shadow controller as none, the freeze as each case says — with callbacks, counting any other call on a trap. **Controls**:
slot 0 of the image's table is `FUN_180017010`; a core flagged `0x10` presents a virtual mass of exactly one; a point on a core
with no spin moves at exactly the velocity given; and the image's `block[0x4a]` is `IvpCollisionTolerance.TwiceToleranceMetres`,
`0x3c4ffe5a`. **`FUN_180077fa0`, `FUN_180078f50`, `FUN_1800770f0` and `FUN_180070620` agree with the port on 200,000 random calls
each, and 20,000 random impacts agree on every lane** — 9,923 approaching, 7,915 holding a core back, 6,835 freezing — with no
call reaching a trap. `IvpImpactSolverConformanceTests` replays 96 of them and four found by searching with the port's
instruments — the push loop at its cap, a heavier core closing inside the hold-back band, a response the `1e-15f` term
decides, a NaN velocity — each failing alone under its own sabotage. **A branch audit found the divergence the sweeps
could not reach**: the port wrote the first branch `!(u ≤ −1e-4f)`, which sends a NaN to the separating push, where
`COMISS`/`JBE` sends it to the approaching one. Fixed; 2,000 impacts with a NaN lane now agree too.

**The first sweep differed on every impact, and not because of the port.** The loaded image holds the block `FUN_180002540`
fills at load with `d = 0.01` until an environment is built, and none was, so every separating speed was off by exactly
`1.2 · (0.02 − block[0x4a])` while the four helpers agreed. The probe now runs `FUN_180098fd0` as the constructor and
`SetGravity` run it, and the last control proves the block it leaves.

**The solver runs in IVP's units, and that is Valve's layering, not a choice here.** vphysics converts at its own interface:
`SetGravity`, `SetPerformanceSettings` and the constructor's tolerance all multiply by `0.0254f`, the position converters turn
Source's `(x, y, z)` into `(x·k, −z·k, y·k)`, and a `.phy` stores its hulls in metres. Everything above the interface is Hammer
units; everything below it — every routine read in this document — computes in metres. Fed metres, the port agrees with the
binary on every bit; fed Source units with the thresholds scaled, no routine could.

*Evidence class: read from the disassembly for the manager, the clamps and both settings routines; published source for the
settings, the interfaces and the client; differential against the shipped binary for the solver. Not established: whether a
client's `env+0x108` at construction is exactly `1/66` in bits, which decides `+0x14`.*

#### Not established

What reads and clears the core flags' bits 6–7 after a solve; what writes `core+0x58`; what the materials' slots 1 and 2 and
`+0xc` are; and anomaly slots 6 and 7. *(Since settled: `FUN_1800d33b0` is `cos`, under* The entry, ported and pinned*; and
`core+0x8` was already read above as the deviation `FUN_180078b90` sets, which the port still names `Offset08`.)*

*Evidence class: read from the disassembly for every routine, offset and constant named; `asinf`, `expf` and `exp` identified by
their structure and, for `asinf`, by the name its error path passes.*

## There are TWO contact solvers, and a resting corpse uses the other one

**This is the correction that makes the section below only half the story, and it was found by
following a cost.** `FUN_18008e290` is the IMPACT solver — one mindist, one arrival, a hundred
passes to kill one approach. A body already lying on a floor never goes near it. Resting contacts
live in the **friction system** (`ivp_intern\ivp_friction.cxx`), and its driver is `FUN_1800836b0`:

```c
uVar11 = (ulonglong)*(ushort *)(param_1 + 0x6a);              // how many friction systems
do {
    lVar1 = *(longlong *)(*(longlong *)(param_1 + 0x70) + uVar11 * 8);   // one system
    fVar15 = 0.0;
    …
    fVar15 += contact[0x88] * contact[0x78] * contact[0x60];  // summed over EVERY contact first
    …
    fVar15 = fVar15 * *param_2 * *param_2;                    // the system's shared budget
    do {
        plVar9 = system.contacts[lVar8];
        if (limit exceeded) { … clamp this contact against fVar15 … }
        if (*(char *)((longlong)plVar9 + 100) == '\x01') { FUN_180085100(plVar9, param_2); }
        else { fVar16 += FUN_1800857c0(plVar9, param_2); }    // ONE application, per contact
        lVar8 = lVar8 + -1;
    } while (-1 < lVar8);
} while (true);
```

**Three things, and each one contradicts something this project built:**

- **One pass over the contacts, not a hundred and not two.** The walk is a single descending loop.
- **The set is solved as a SET.** A scalar is accumulated across every contact of the system
  BEFORE any impulse is applied, and each contact is then clamped against that shared budget. Our
  solve has no notion of a group at all — each contact converges alone, knowing nothing about its
  neighbours.
- **The grouping is the friction SYSTEM**, which is why `FUN_180086e80` exists to split and merge
  them when an object's contact list changes — a fact already recorded above and filed as an
  identity predicate rather than as the thing that defines the solve's scope.

**This is what "our contact set is not the engine's mindist set" turns out to mean**, and it
explains both failures on the way to it. Wrapping the impact solver's hundred-pass bound around
every contact of a ragdoll re-solved settled contacts a hundred times a slice — six ticks of one
corpse past four hundred seconds. Moving the bound inside one contact stopped the hang and then
overshot, resting a two-unit cube 2.04 above a plane it can be at most `sqrt(3)` above, because
each contact drove its own approach to zero with no shared budget to divide.

**Neither shape was the engine's, and no amount of tuning either would have got there.** The
resting solve is a different function, reached from a different place, over a different set.

**What is NOT established:** what `contact+0x60`, `+0x78` and `+0x88` hold — the three factors whose
product forms the budget — and what the byte at `contact+0x64` selects, which sends a contact to
`FUN_180085100` instead of `FUN_1800857c0`. `param_2[0]` is squared into the budget and its
provenance is unread. Those are the next things to read, and none of them is guessable.

**And the obvious way to look them up does NOT work, which is worth writing down before someone
spends the run on it.** The friction-system contact is a DIFFERENT structure from the 0x110-byte
record `FUN_18008d0c0` allocates — that one is written at `+0x24`, `+0x72`, `+0x76`, `+0x94`,
`+0xc4`, `+0xd4` and `+0xf4`, and never at any of the four above. Nor does a whole-program grep for
the offsets find the writer: searching all 2,813 functions for `0x60`, `0x64`, `0x78` and `0x88`
together returns **zero**, because the decompiler renders a field by the type of the pointer holding
it — the same byte appears as `*(float *)(param_1 + 0x78)` through one and as `plVar9 + 0xf` through
another, and `FUN_1800857c0` does both within a dozen lines.

So the route in is the structure's allocation, or a trace out of `FUN_180086e80`, which splits and
merges these systems and therefore has to know how one is built. Recorded as a failed search rather
than left for the next reader to repeat.

*Evidence class: read from the decompiled binary. The identification of `FUN_1800836b0` as the
friction-system driver is read from its own structure — a loop over `param_1+0x70` indexed by a
count at `+0x6a`, calling the per-contact solve already traced — and from `ivp_friction.cxx` being
the only IVP source file named by the two functions beside it.*

## Friction is a WARM-STARTED 2×2 SOLVE, not an impulse opposing the slide

**Read after a per-contact Coulomb pass measured worse**, which is what sent me back to the
function rather than to another adjustment. `FUN_1800857c0` solves one resting contact and its
shape is nothing like "push back along the slide":

```c
FUN_18009ca70(local_1b8, coreA, coreB, mindist, mindist + 0x16, mindist + 0x18, 0);
dVar6 = param_2[1] * f(contact + 0x6c) - local_64;      // TARGET, from the STORED impulse
dVar5 = param_2[1] * f(contact + 0xd)  - local_68;
bVar7 = FUN_1800868d0(local_c8, local_c0, local_c0, local_a0, …);   // invert a 2x2
local_1e8 = local_1d0 * dVar6 + local_1d8 * dVar5;      // the pair of magnitudes
local_1e4 = local_1c0 * dVar6 + local_1c8 * dVar5;
if (dVar4 * dVar4 < local_1e4*local_1e4 + local_1e8*local_1e8) { … scale both to dVar4 … }
FUN_18009c620(local_1b8, coreA, coreB, &local_1e8);     // apply BOTH at once
```

**Four things, and each one is a departure from what this project does:**

- **Two tangent directions solved TOGETHER**, through a symmetric 2×2 effective-mass matrix
  inverted by `FUN_1800868d0` (its arguments are `a, b, b, d` — the same value twice, which is what
  makes it symmetric). A single impulse along the slide direction is a different operator, and it
  is what measured worse: a sliding body went from twelve units a second to 17.8.
- **The target is warm-started from a STORED impulse.** `contact+0x68` and `+0x6c` hold the
  tangential pair from the previous solve, and the right-hand side is
  `weight × stored − current velocity`. So friction converges across steps rather than being
  rediscovered each one, which is exactly what a resting body needs and what a stateless contact
  cannot do.
- **The cone clamp is on the PAIR**, after the solve, scaling both components to the limit
  `f(+0x78) · f(+0x88) · dt` rather than clamping a single magnitude.
- **The clamp in the driver is a SUM, and it is a ceiling rather than a division.**
  `FUN_1800836b0` accumulates `Σ contact[0x88]·contact[0x78]·contact[0x60]` over the whole friction
  system, squares the step into it, and caps each contact's stored pair against that total. A system
  with more contacts has a LARGER ceiling — this bounds runaway accumulation, it does not share a
  fixed budget out.

**So the missing structure is persistence, and that is the honest size of it.** A friction contact
in IVP survives between PSIs carrying its tangential impulse; ours is rebuilt from scratch every
slice. Warm starting is not an optimisation here, it is where the resting force comes from, and a
stateless transcription of the surrounding arithmetic cannot stand in for it.

**What is NOT established:** the identity of `contact+0x60`, `+0x78` and `+0x88` individually — only
that `+0x78 · +0x88` forms the Coulomb limit, because the pair is used together and never apart.
`param_2[0]` and `param_2[1]` are the step and the relaxation weight by their use and not by a
producer. `FUN_18009ca70` builds the two tangent Jacobians and has not been read.

*Evidence class: read from the decompiled binary. The symmetry of `FUN_1800868d0`'s matrix is read
from its duplicated argument; the warm start is read from the right-hand side referencing the same
fields the solve writes back.*

## The impact solver, found — and it is a fixed sub-impulse in a friction cone

**`FUN_18008e290` is the consumer of the contact record**, reached from the mindist event through
`FUN_18008ed60`, which builds the solver's ~0x150-byte working struct on the stack and hands it
over. It is the piece the section above and *Contact response is accumulated, not applied* both
named as missing, and it was found by asking which functions read the record's mass term and write a
core's velocity — six in the whole binary, and this is the one between them.

**The working struct, from the two functions together:**

| byte | holds |
|---|---|
| `+0x30`, `+0x38` | each core's 3×3 rotation, as doubles |
| `+0x40`/`+0x60`, `+0x50`/`+0x70` | each core's working angular / linear velocity |
| `+0x80`/`+0xa0`, `+0x90`/`+0xb0` | the per-application delta for each, so it can be undone |
| `+0xc0` | the relative velocity at the contact |
| `+0xd0` | **the impulse direction** |
| `+0xe0` | the fallback direction, used when the pair is separating |
| `+0x110`, `+0x118` | the two cores |
| `+0x120`, `+0x128` | each core's contact anchor |
| `+0x130` | the elasticity input |
| `+0x134`, `+0x138` | **`cos θ` and `sin θ` of the friction cone** |
| `+0x140` | the normal — `mindist+0x20`, or a negated copy when the roles are swapped |

**The working velocity is `core+0x130/+0x140` plus `core+0x110/+0x120`, and the first pair is the
live one.** `FUN_180099a00`, the integrator, advances the position by `core+0x170` and only then
copies `core+0x140` into it, which is the one-step lag this project already reproduces — and it
proves `+0x140` is the velocity rather than a delta accumulator. The `+0x110/+0x120` pair is a
second velocity added on top, folded in and cleared by this function's tail under a `flags & 0xc0`
gate.

**The loop is the finding.** Every constant in it was dumped in the disassembly rather than read out
of the decompiled expression, per `docs/memory/nothing-is-closed.md#settle-a-constant-in-the-disassembly`:

```c
fVar17 = dot(relativeVelocity, normal);
if (fVar17 <= _DAT_1800ee398) {                    // -1.0E-4, a float: genuinely approaching
    FUN_180090240(param_1);                        // choose the direction
    dVar12 = DAT_1800fd880 / (dVar19 + dVar18);    // -0.1 (double) over the two mass terms
    dVar24 = -dot(relativeVelocity, direction);
    for (; (0.0 < dVar24 && (iVar15 < 100)); iVar15 = iVar15 + 1) {
        FUN_18008f1c0(param_1, dVar12 * dVar19 * (dVar18 + dVar18) * (double)fVar17);
        FUN_18008fc00(param_1);                    // recompute the relative velocity
        dVar24 = -dot(relativeVelocity, direction);
        FUN_180090240(param_1);                    // re-choose the direction
    }
```

Three things follow, and each contradicts what a textbook sequential-impulse solver does.

- **The magnitude is fixed and comes from the approach speed measured BEFORE the loop.** `fVar17` is
  never recomputed; only the test reads the live velocity. The expression reduces to
  `-0.2 · mA·mB/(mA+mB) · v₀`, so each pass removes a fifth of the original approach and the loop
  converges as `0.8ⁿ`. That is what a bound of a hundred is for.
- **A static partner is `1.0e5 ×` its partner's mass term.** `DAT_1800fd870` dumps as `100000.0`
  (double), substituted for whichever core carries `flags & 2` — so the harmonic mean collapses to
  the moving body's own effective mass. Infinite mass by a large number rather than by a branch.
- **There is ONE impulse and friction is a constraint on its DIRECTION.** `FUN_180090240` sets the
  direction to the normalised relative velocity; if that leans further from the normal than the
  cone allows — `if (-cos θ < dot)` — it is clamped onto the cone edge,
  `dir = normal·(−cos θ) + tangent·sin θ`. There is no tangential impulse anywhere.

**The cone pair is built in `FUN_18008ed60` and it is a series, not a table lookup:**

```c
dVar3 = (sqrt(*(float *)(param_1 + 0x80)) + 1.0) * *(float *)(param_4 + 0x78);   // tan θ
dVar1 = FUN_1800d4398(dVar3);
fVar2 = (1.0 - dVar1*dVar1 * 0.5) + dVar1*dVar1 * _DAT_1800fd858 * dVar1*dVar1;
local_44 = CONCAT44((float)((double)fVar2 * dVar3), fVar2);                       // (cos θ, sin θ)
```

`1 − s²/2 + C·s⁴` is the series for `1/sqrt(1 + s²)`, and the partner is that times `s`, which makes
the pair `(cos θ, sin θ)` with `tan θ = s`. **`mindist+0x80` has no traced producer**, so the
`(sqrt(x) + 1)` factor on the material's friction is between one and unknown — flagged rather than
rounded off, and this project takes it as one, which is the minimum of the engine's range.

**After the loop there is a calibrated impulse, and it is measured rather than solved.** The target
separating speed is `dVar24 + sqrt(n)·0.01 + sqrt(1 − (1−e)/(n·0.5 + 1))·dVar24` — with
`DAT_1800eb150` dumping as `0.01` (double), metres per second, a floor that grows with the impact
count so a pair cannot chatter forever. If that target is positive **and the loop did not hit its
bound**, the solver applies a unit impulse, remeasures, subtracts its own accumulated deltas back
out of both bodies, and reapplies the exact multiple the measurement calls for. `DAT_1800f50f8` =
`1.0e-4` guards the division.

**What is NOT established:** the elasticity source at `mindist+0x80` and what `param_4` counts —
read as an impact or recursion count from its use under two square roots, not from a producer.
`FUN_1800d4398` is taken to be a small-angle helper from the series that consumes it, not from a
name. The tail's `flags & 0xc0` gate has no traced writer either.

**And it names the divergence that mattered.** This project solved contacts with the joint group's
`additionalIterations + 2`, a number read for joints and never for contacts, with a separate Coulomb
friction pass — a shape invented here because this function was unread. Both are now the engine's:
`IvpEnvironment.Resolve` runs the condition-terminated loop bounded at a hundred, and
`IvpContact.Direction` is the cone clamp.

*Evidence class: read from the decompiled binary for every function; every constant re-read as a
raw lane in the disassembly. The `(cos θ, sin θ)` identification is ARITHMETIC — the series is
matched to `1/sqrt(1+s²)` — rather than read from a name.*

### The point array

Sixteen-byte stride at `ledge + c_point_offset`: three little-endian floats and four bytes that were
**zero in every sampled point**. Indexed by the plain 16-bit start index — confirmed in the reader
itself, `pfVar13 = (float *)((ulonglong)*param2 * 0x10 + *param4)`.

### Still open, named rather than guessed

- `IVP_Compact_Surface` `+0x00..0x1B` and `+0x24..0x2B` — real data, no consumer traced.
- Ledgetree node `+0x18..0x1B`, four bytes — see the sphere below; still unidentified.
- Ledge `+0x04` (always zero) and `+0x08` (low byte constant, upper bytes unresolved).
- The triangle's own header word — never read by anything traced.
- Bit 31 of an edge, deliberately excluded from the fan delta.
- The three floats at container `+0x20`.

*Evidence class: read from the decompiled binary for every function and both dumped tables;
**file-verified** against three shipped `.phy` files for the container, the surface header, the tree,
eleven ledges, the point array and the 132-edge fan test; NOT ESTABLISHED and listed above for the
unidentified fields.*

## The ball-and-socket, found — and it is what holds a ragdoll together

**The correction below is right about `FUN_180036f80` and was read as saying more than it does.**
All three axes `FUN_180036e10` dispatches ARE angular; the conclusion drawn from that — that the
ragdoll constraint has no translation solve at all — does not follow, because the translation solve
is not in the dispatcher. It is in the dispatcher's CALLER, `FUN_180038620`, immediately after it
returns, behind a flag:

```c
FUN_180036e10(param_1, param_2, (longlong)param_3);       // the three angular axes
if (*(char *)(param_1 + 0x157) != '\0') {
    // each body's anchor, carried into world space by its own rotation
    fVar39 = az * Rz + origin + ay * Ry + ax * Rx;                    // body A
    fVar22 = bz * Rz + origin + by * Ry + bx * Rx;                    // body B
    *param_3     = fVar22 - fVar39;                                   // THE POSITION ERROR
    …
    FUN_180038070(param_3 + 4, &local_b8, coreA, coreB, &armA, &armB);  // the 3x3 K matrix
    fVar17 = *(float *)(param_1 + 0x14c);                              // gain on the velocity
    fVar22 = *(float *)(param_1 + 0x150) * fVar51 * param_2[1];        // gain on the error
    fVar21 = (0.0 - fVar17 * fVar52 * local_b8) + fVar22 * fVar21;     // per axis
    …                                                                  // times K inverse
    if ((*pbVar3 & 0x12) == 0) { core[0x130] += …;  core[0x140] += …; }   // BOTH bodies,
    if ((*pbVar4 & 0x12) == 0) { core[0x130] += …;  core[0x140] += …; }   // equal and opposite
}
```

**Two things make this the missing piece.** It is the only place in the constraint path that writes
LINEAR velocity — the three angular axes never touch `+0x140` — and it is the only term anywhere
that reads a POSITION rather than a velocity. A solver made only of velocity constraints has no way
to notice that two bodies have drifted apart; this is the term that pulls them back.

`FUN_180038070` builds the standard ball-socket effective-mass matrix, reading each core's inverse
inertia at `+0x40..0x48` and inverse mass at `+0x4c` and forming the cross-product terms — the same
quantities the contact record precomputes, as a full 3×3 rather than one scalar.

**This project implements the three angular axes and nothing else**, so its ragdolls are seventeen
bodies that fall independently and stay together only because they started together. It is what the
measurement had been pointing at without naming: on `z1800` the escaping corpses' ROOT body sits
outside all geometry at z −50 while the corpse still reports six to eleven contacts — the limbs are
resting on a floor the pelvis has already gone through, which is a ragdoll coming apart rather than
a body sinking.

**Both gains are 1.0 and translation is ON, and all three are settled in the disassembly.** The
chain is `CreateRagdollConstraint` → `FUN_18000d510` → `FUN_18000eac0`, which builds a template on
its stack with `FUN_18000c750` and hands it to `FUN_1800368c0`; `FUN_180037890` then copies
template `+0xc8`/`+0xcc`/`+0xd3` to constraint `+0x14c`/`+0x150`/`+0x157`. The template's own
constructor writes them literally:

```
18000c7cb  MOV dword ptr [RBX + 0xc8],0x3f800000     ; the velocity gain = 1.0
18000c7d5  MOV dword ptr [RBX + 0xcc],0x3f800000     ; the error gain    = 1.0
18000c7df  MOV byte ptr  [RBX + 0xd3],0x1            ; translation ENABLED
```

Nothing between there and the constraint overwrites any of the three, so **every ragdoll joint TF2
creates is a ball-and-socket with unit gains** — and the error term is additionally scaled by the
driver's relaxation weight, `param_2[1]`, which is the same `0.4` the angular axes use.

**What is NOT established: the two per-body scales** `fVar51` and `fVar52`, selected by a lane mask
between the function's `param_4`/`param_5` arguments and a value from an `rsqrt` Newton refinement.
Their ordinary value for a normalised axis is one, and that is what this project takes with the
assumption stated rather than buried.

*Evidence class: read from the decompiled binary for the solve; the three constants re-read as raw
instruction operands in the disassembly per
`docs/memory/nothing-is-closed.md#settle-a-constant-in-the-disassembly`. The identification of
`FUN_180038070` as the ball-socket K matrix is ARITHMETIC — matched to the standard form from the
fields it reads — rather than read from a name.*

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

## The two swing deflections, traced to the vectors they dot

`FUN_180038620` takes each body's rotation rows (`FUN_180037620`, one call per body) and rotates
three stored constraint axes through them:

| constraint field | rotated by | cached at | used by |
|---|---|---|---|
| `+0x30` | body A | `geom+0x120` | the `flags+0xcc` axis |
| `+0x50` | body A | `geom+0x140` | the `flags+0xe8` axis |
| `+0x70` | body B | `geom+0x130` | the shared basis for both |

and the two deflections are dots against that shared body-B vector:

```c
fVar17 = fVar44 * fVar48 + fVar43 * fVar47 + fVar45 * fVar49;   // B[+0x70] · A[+0x30]
fVar21 = fVar44 * fVar41 + fVar43 * fVar40 + fVar45 * fVar42;   // B[+0x70] · A[+0x50]
```

**Where those three fields come from settles what kind of quantity each dot is.**
`FUN_1800393d0` writes the descriptor's two frames row by row under the axis permutation — frame A's
rows for `uVar20`, `iVar19`, `iVar16` at descriptor `0x00`, `0x10`, `0x20`, and frame B's same three
rows at `0x40`, `0x50`, `0x60` — and `FUN_180037890` copies those to constraint `+0x30`, `+0x40`,
`+0x50` and `+0x70`, `+0x80`, `+0x90`. So:

- `fVar17` dots **the same axis index taken from the two different frames**, and
- `fVar21` dots **two different axis indices**, one from each frame.

**Two different axes of an orthonormal frame are perpendicular, so the second dot is a SINE**; the
same axis from two frames is a cosine unless the frames are built a quarter turn apart.

**`FUN_180032740` and `FUN_180033720` turned out not to be the frame builders.** The second is a
plain point-by-4×4 transform — three rows of four floats with the translation at `[0xc]`, `[0xd]`,
`[0xe]` — and the pair is used on descriptor `+0x30` and `+0x70`, which are therefore the two
ANCHORS rather than axes. That fixes the descriptor layout:

| descriptor | holds |
|---|---|
| `0x00`, `0x10`, `0x20` | frame A's three axes, in the joint's axis permutation |
| `0x30` | anchor A |
| `0x40`, `0x50`, `0x60` | frame B's three axes |
| `0x70` | anchor B |

**So the frames ARE built a quarter turn apart, deliberately, and the function that does it is the
one thing left to read.** `FUN_180037890` ends by rotating the frame at `+0x30` about axis 2 by the
midpoint of that axis's range:

```c
fVar5 = (*(float *)((longlong)param_2 + 0x9c) + *(float *)(param_2 + 0x14)) * DAT_1800ea984;
if (DAT_1800ea944 < (float)((uint)fVar5 & uVar3)) {
    FUN_18003dde0((float *)(param_1 + 0x30), 2, (uint)fVar5);
}
```

and `FUN_1800393d0` calls the same `FUN_18003dde0(&frame, axis, midpoint)` on its own copy. That is
why the second block's bounds are re-centred to `±range/2`: the offset has been moved into the
frame.

**`FUN_18003dde0` read: it is an ordinary axis rotation, and it does NOT make the two dots the same
kind of quantity.** It builds an identity, fills the two off-axis diagonal entries with the cosine
and the off-diagonal pair with `±sin`, pins the rotation axis's own entry to 1, and multiplies the
frame by it:

```c
iVar1 = (param_2 + 1) % 3;  iVar2 = (iVar1 + 1) % 3;
uVar3 = FUN_1800bc110(param_3);                       // sincos, packed
M[iVar1][iVar1] = cos;  M[iVar2][iVar2] = cos;
M[iVar1][iVar2] = sin;  M[iVar2][iVar1] = -sin;
M[param_2][param_2] = 1.0;
```

A rotation about an axis leaves that axis fixed and turns the other two within their plane, so it
cannot convert a same-index dot into a different-index one.

### Where this stands, stated as a contradiction rather than a conclusion

Pinning the permutation makes the three deflections explicit. `FUN_1800393d0` writes frame A's rows
for `iVar9`, `iVar19`, `iVar16` and frame B's SAME three rows, so:

| block | deflection | at the rest pose |
|---|---|---|
| `flags+0xb0` | `0 − atan2(…)`, bounds `−hi`, `−lo` | consistent — both negated |
| `flags+0xe8` | `B[iVar9] · A[iVar16]`, different indices | a SINE, ≈ 0 |
| `flags+0xcc` | `B[iVar9] · A[iVar9]`, same index | a COSINE, ≈ 1 |

**The last row does not fit its bounds and that is the open problem.** Its bounds are `range × ±0.5`
in radians: for the demoman's widest joint that is `±1.187`, which a cosine can never leave, so the
limit never fires; for his narrowest it is `±0.393`, which a cosine at rest already exceeds, so the
limit fires permanently and hard. Neither is a working joint, and TF2's corpses visibly are.

**So one input is still wrong, exactly as it was when the threshold was assumed to be π.** The rest
of this section is that input, run down.

## The axis permutation, read — and the one block that is still not explained

`local_2d8` and `local_2a8` are read straight back out of the descriptor:

```c
local_2d8 = *param_1;      uStack_2d0 = param_1[1];    // frame A, bytes 0x00..0x1f
local_2c8 = param_1[2];    local_2b8 = param_1[4];
local_2a8 = param_1[8];    uStack_2a0 = param_1[9];    // frame B, bytes 0x40..0x5f
local_298 = param_1[10];   local_288 = param_1[0xc];
FUN_18003dde0((float *)&local_2d8, iVar16, (uint)local_268[iVar16]);   // frame A only
```

so they are whatever is in the descriptor at that moment, permuted and written back. **Frame B is
never rotated; frame A is, about `iVar16`, by that axis's range midpoint.**

**The three indices are chosen by MECHANICS, not by position**, which is the part that makes the
rest legible. The loop above picks `iVar9` as the axis maximising

```c
fVar23 = |r_A × axis_A|² · invMass_A + … + |r_B × axis_B|² · invMass_B;   // core+0x4c = inverse mass
```

— the axis whose rotation moves the two anchors most. Then `iVar16` is the WIDER of the two
remaining ranges and `iVar19` the narrower, and all three are recorded in the descriptor at `0xd0`,
`0xd1`, `0xd2` and copied to `constraint+0x154..0x156`. `uVar20` is `iVar9`; they are the same
register.

That resolves the apparent index mismatch, and two of the three blocks come out clean:

| block | solve axis | deflection | bounds | at rest |
|---|---|---|---|---|
| `+0xb0` | the twist | `0 − atan2(…)` | `−hi[iVar9]`, `−lo[iVar9]` | consistent — both negated |
| `+0xe8` | `A[iVar16]` | `B[iVar9] · A[iVar16]` | `lo[iVar19]`, `hi[iVar19]` | a SINE, and its bounds are the axis it turns about |
| `+0xcc` | `A[iVar9]` | `B[iVar9] · A[iVar9]` | `range[iVar16] × ±0.5` | still unexplained |

**The third row also has a second tell that the first two do not**: its solve axis is
`cross(A[iVar9], B[iVar9])`, and `FUN_1800372c0` explicitly detects that cross going degenerate and
retires the axis for the step (`cache[0x1c] = 2`). If the two vectors were parallel at rest, this
axis would be dead exactly when the corpse is settled — which is when it matters most.

### What that points at, stated as a mechanism rather than a conclusion

**`FUN_18000c750` sets both frames to the IDENTITY** — `FUN_18003ddb0(param_1)` and
`FUN_18003ddb0(param_1 + 8)`, the same identity-setter the caller uses on the two matrices. If the
frames were still identity when they are read back, `A[iVar9]` and `B[iVar9]` would be the same
vector, their dot a cosine and their cross zero, and the block above would be both degenerate and
mis-bounded.

**They are not still identity, and the answer was in the first twenty lines of the function.**
`FUN_1800393d0` opens by copying its `param_2` straight over them:

```c
param_1[1] = param_2[1];   param_1[2] = param_2[2];   …   param_1[5] = param_2[5];   // frame A
param_1[8] = param_2[8];   param_1[9] = param_2[9];   …   param_1[0xd] = param_2[0xd];  // frame B
```

and `param_2` is the buffer `FUN_18000eac0` filled with `FUN_18000ca70(param_4 + 6, …)` and
`FUN_18000ca70(param_4 + 0x12, …)` — **`constraintToReference` and `constraintToAttached`**, at
`constraint_ragdollparams_t + 0x18` and `+ 0x48`, converted to IVP's convention. The identity from
`FUN_18000c750` is a default that is immediately overwritten.

**The loop that looked like it might build the frames does not.** `FUN_18003ec30` is a plain
rotate-vector-by-matrix, and the block around it only measures each candidate axis to pick `iVar9` —
`FUN_18003d320` writes into locals, never into `param_1`.

**So the cosine is real, and the published semantics of the two matrices confirm it rather than
rescue it.** `constraintToReference` maps constraint space into the reference object's, and
`constraintToAttached` into the attached object's; the point of shipping both is that
`R_ref · (toReference · e_k)` and `R_att · (toAttached · e_k)` are the SAME world vector at the bind
pose. So `A[iVar9] · B[iVar9]` is `1` at rest by construction.

### So `flags+0xcc` is a CONE limit, and this is the most probable reading of it

`A[iVar9] · B[iVar9]` is the cosine of the total angle between the joint's primary axis as the two
bones see it — one number covering deflection in every direction, which is a cone. Its bounds are
`range[iVar16] × ±0.5`, the half-range of the WIDER of the two swing axes. So the engine limits the
cone by comparing a cosine against a half-angle in radians, which is dimensionally wrong and
monotonically right: a narrower declared swing gives a tighter cone.

Worked on the demoman's own joints, whose ranges are 45°, 50° and 136°:

| `range[iVar16]` | bound | cone it permits |
|---|---|---|
| 2.374 rad (136°) | ±1.187 | never fires — a cosine cannot leave ±1 |
| 0.873 rad (50°) | ±0.436 | fires below about 64° |
| 0.785 rad (45°) | ±0.393 | fires below about 67° |

**And at the bind pose the cosine is 1, which is outside every one of those bounds.** So for any
joint whose wider swing is under about 115°, this axis is clamping hardest exactly when the corpse
is at rest, and relaxes as the joint deflects — the opposite sense to the other two limits.

**Recorded as the most probable reading rather than as a fact**, with the arithmetic in the open so
it can be judged. What would falsify it: a scale or an `acos` applied to `geom+0x104` between
`FUN_180038620` writing it and `FUN_1800372c0` reading it — there is none in either function, but
`FUN_180036b80`'s four-lane output is only partly consumed and the unused lanes were not traced.

**It is also the first reading in this document that PREDICTS the thing the owner describes.** A
limit that pushes hardest at rest and eases off under deflection is a corpse that will not settle
quietly — *"the ragdolls do funny things thats why they are fun"*. That is not evidence, but it is
the first time the arithmetic and the observed behaviour have pointed the same way.

*Evidence class: read from the decompiled binary for the frame copy, the matrix sources, the
mechanical axis selection, the index recording and the bound assignment, with every constant dumped;
the cone interpretation of `flags+0xcc` is the MOST PROBABLE READING, with its falsifier named.*

## The twist deflection, read in full — and it is zero at the bind pose

The falsifier named above is now closed rather than open: the unused lanes of `FUN_180036b80`'s
four-lane return are anded with `_UNK_1800ff104`, `_UNK_1800ff108` and `_UNK_1800ff10c`, all dumped
as `0`. They cannot reach anything.

The twist is built from three vectors, in order:

```c
fVar33 = fVar43 + fVar47;  fVar34 = fVar44 + fVar48;  fVar35 = fVar45 + fVar49;
  // m = normalise( A[iVar9]_world + B[iVar9]_world ) — the BISECTOR, cached at geom+0x110
fVar35 = fVar42 * fVar37 - fVar41 * fVar38;  …
  // p = normalise( A[iVar16]_world × m ), zeroed when the cross is degenerate
fVar22 = fVar21 * local_e8 + fVar24 * local_d8 + fVar17 * local_f8;   // q = R_bodyB · constraint[+0x90]
local_158 = q · (p × m);
local_148 = q · p;
```

so **`twist = −atan2( q·p , q·(p×m) )`**, where `m` bisects the two bodies' primary axes, `p` is
perpendicular to it in the plane of the wider swing, and `q` is body B's `iVar16` axis in world
space.

**Which corrects a label used earlier in this document.** `geom+0x110` was written up as the joint's
anchor — it is not; it is the bisector, and it is what `FUN_180036f80` receives as the axis for its
Jacobian. The anchors live at `constraint+0x60` and `+0xa0`.

**Checked at the bind pose, where `A[k] = B[k]` for every k by construction:** `m = A[iVar9]`,
`p = ±A[iVar19]`, `q = A[iVar16]`, so `q·p = 0` and `q·(p×m) = ±1`. The twist is `atan2(0, ±1)` —
**zero**, against bounds `−hi[iVar9]`, `−lo[iVar9]` which straddle zero. Consistent.

**That also pins the argument order of `FUN_180036b80`**, which was not established when the
four-lane `atan2` was first transcribed: `param_1` is the numerator and `param_2` the denominator.
Taken the other way the twist would read `±π/2` at rest, far outside every ragdoll bound — so the
bind-pose check is what settles it, not the disassembly.

**And it leaves `flags+0xcc` as the only block that does not read zero-or-consistent at rest**, with
one mitigation now visible and one problem still standing. The mitigation: its solve axis is
`cross(A[iVar9], B[iVar9])`, which is exactly zero at the bind pose, so `FUN_1800372c0` retires the
axis (`cache[0x1c] = 2`) before reaching the clamp — the corpse at rest is not fighting it. The
problem: a few degrees off the bind pose the cross is no longer degenerate, the cosine is still near
1, and the clamp does fire. Whether the resulting impulse pushes the joint back toward alignment or
away from it depends on the sign the cross-derived row carries, and that has NOT been worked
through.

*Evidence class: read from the decompiled binary for all three vectors and both `atan2` terms;
the argument order of `FUN_180036b80` is settled by the bind-pose check, which is ARITHMETIC on a
read expression rather than a second reading; the sign the cone impulse carries is NOT established.*

**It matters because the two answers are visibly different.** A sine compared against a radian bound
tightens the limit as the angle grows — four per cent at 25°, twenty-nine at 79° — which is a corpse
whose big joints stop short. A cosine compared against a radian bound would clamp constantly, which
is a corpse locked rigid. The bounds are the same either way, so nothing in the output distinguishes
them except how the body looks.

*Evidence class: read from the decompiled binary for the rotation sites, the cache offsets, the two
dot products, the descriptor-to-constraint copy and the descriptor layout; `FUN_18003dde0` — the
frame rotation by the range midpoint — is the one unread step, and whether each dot is a sine or a
cosine follows from it.*

## The reference body is the CHILD, and the frames are per side

This one is published, needed no decompiler, and had been transcribed backwards.

```cpp
childElement.pConstraint = pPhysEnv->CreateRagdollConstraint( childElement.pObject,
    ragdoll.list[constraint.parentIndex].pObject, ragdoll.pGroup, constraint );
```

`ragdoll_shared.cpp:253`, against the declaration it calls:

```cpp
// Create a constraint in the space of pReferenceObject which is attached by the constraint to
// pAttachedObject
virtual IPhysicsConstraint *CreateRagdollConstraint( IPhysicsObject *pReferenceObject,
    IPhysicsObject *pAttachedObject, IPhysicsConstraintGroup *pGroup,
    const constraint_ragdollparams_t &ragdoll ) = 0;
```

`vphysics_interface.h:572`. So the **child** is the reference and the **parent** is attached —
the opposite of what `childElement.parentIndex` two lines above suggests, and the reason it is worth
writing down is that nothing downstream reports getting it wrong.

**It decides three things.** The frames are per side, so `constraintToReference` — the identity
`SetIdentityMatrix` writes at `:246` — belongs to the CHILD, and `constraintToAttached`, the
bone-to-bone transform, belongs to the PARENT. The joint friction is scaled by the reference
object's `GetMass` (`FUN_18000eac0`, above), which is therefore the limb's own mass rather than
whatever it hangs from. And the sign of every deflection follows from which body is A.

**Read the pair as a pair and the bind pose becomes a control.** `constraintToReference` maps
constraint space into the reference object's and `constraintToAttached` into the attached object's,
so `R_ref · (toReference · e_k)` and `R_att · (toAttached · e_k)` are the SAME world vector at
rest — which is why a joint measures `0`, `0`, `1` at the bind pose whatever its bones are turned
to, and why a two-bone fixture with one bone turned a quarter turn can predict all three exactly.

**What made this survive:** with both frames the identity, swapping the two bodies is invisible in
every test that starts both bodies at the same orientation — and every test did. An unrotated
fixture is the condition where the correct code and the broken code predict the same observation,
which is route 2 of *a test that cannot fail*: the fix is the input, not the assertion.

**A frame is used as `matrix · e_k`, which is a COLUMN.** `matrix3x4_t` is row-major and
`VectorTransform` dots each row against the input, so the k-th column is the image of the k-th axis.
Taking rows instead yields the transpose — the inverse rotation, orthonormal and plausible, and
wrong in the way nothing in a corpse's pose reports.

*Evidence class: read from published SDK source for the call and the declaration; the friction
consequence is read from the decompiled binary; the bind-pose invariant is arithmetic on the two
published matrix semantics, and is pinned by a synthetic test with a turned skeleton.*

## The map's own collision is not the map's brushes, and a corpse falls through the difference

**A corpse on `koth_harvest_final` free-falls a thousand units at (-972.6, -1400.3), and the solver
is not at fault.** `corpse-drop` drops a straight ray before it simulates anything now, and the ray
says there is nothing between z 77.5 and z -1024. The corpse comes to rest at -1015, on the floor of
the world. Given that world, that is the correct answer.

**vbsp builds the world's physics from three passes and a mesh, and each one bounds what a corpse
can land on** (`utils/vbsp/ivp.cpp:1314-1336`):

```cpp
ConvertWorldBrushesToPhysCollide( collisionList, shrinkSize, mergeTolerance, MASK_SOLID );
ConvertWorldBrushesToPhysCollide( collisionList, shrinkSize, mergeTolerance, CONTENTS_PLAYERCLIP );
ConvertWorldBrushesToPhysCollide( collisionList, shrinkSize, mergeTolerance, CONTENTS_MONSTERCLIP );
…
if ( g_bNoVirtualMesh || !physcollision->SupportsVirtualMesh() )
    Disp_AddCollisionModels( collisionList, &dmodels[0], MASK_SOLID );
else
    Disp_BuildVirtualMesh( MASK_SOLID );
```

Harvest's world model declares exactly two solids — `"contents" "33570827"` (`MASK_SOLID`) and
`"contents" "65536"` (`CONTENTS_PLAYERCLIP`) — plus `virtualterrain {}`. There is no monsterclip
solid because the map has no monsterclip brushes, which is why the count is two and not three.

**The world is built with `NO_SHRINK`, and only brush ENTITIES are shrunk** — `BuildWorldPhysModel(
collisionList[i], NO_SHRINK, VPHYSICS_MERGE )` against `ConvertModelToPhysCollide( …,
VPHYSICS_SHRINK, VPHYSICS_MERGE )`, `ivp.cpp:1531` and `:1535`, where `VPHYSICS_SHRINK` is `0.5f`
*"shrink BSP brushes by this much for collision"* (`:37`). So a systematic half-unit gap under the
world was a candidate and is not the answer; under a `func_` brush it would be.

**World brushes are MERGED, which is why a ledge is a box the size of a quarter of the map.** One of
harvest's solid ledges spans x -4032..1472, y -3008..0, z -1056..-1024 — that is not a misplaced
hull, it is a merged floor slab, and reading it as a placement error cost an hour. 2,844 of the
map's 3,030 ledges have six faces, with 4, 5, 7 and 8 also present, which is what merged
axis-aligned brushwork looks like.

**`Disp_BuildVirtualMesh` tesselates from the ALLOWED verts, and power 4 turns the whole mechanism
off** (`utils/vbsp/disp_ivp.cpp:268-320`):

```cpp
helper.m_pActiveVerts = pDispInfo->GetAllowedVerts().Base();
::TesselateDisplacement( &helper );
…
params.buildOuterHull = true;
```

and `ivp.cpp:1320` — *"Map using power 4 displacements, terrain physics cannot be compressed"* —
sets `g_bNoVirtualMesh`, after which the terrain is baked into the polysoup as ordinary ledges
instead. **So a map's terrain is in one of two entirely different places depending on its highest
displacement power**, and harvest is power 2..3, so its terrain is a virtual mesh and is absent from
`LUMP_PHYSCOLLIDE` by design.

### What is measured, and what is still open

Our terrain is COMPLETE: **20,608 triangles built against 20,608 the lump's powers ask for**, over
all 533 displacements — arithmetic from `2 * 4^power`, not a second reading. The terrain sweep is
not at fault either: dropped through the middle of its own nearest triangle it hits at 0.492, which
is the control an absence claim needs.

**So the hole is real and it is small: 26 of 6,331 floored columns on harvest, 107 of 6,041 on
2fort, 138 of 6,561 on dustbowl.** Scattered rather than in a block, which rules out a truncated
ledge-tree walk — and that was checked directly as well, by raising the reader's depth guard from
64 to 4,096 and measuring the same 3,030 ledges.

**A larger figure from the same session is NOT this one and should not be quoted as it.** The
neighbourhood census reports 698 of 1,089 columns where the two worlds stop more than 32 units
apart, 696 of them with the physics world lower. That question is looser: a `func_` brush entity is
in `LUMP_PHYSCOLLIDE` and not in the world's leaf tree, so the two legitimately disagree in both
directions. The figure that means "a corpse has nothing to land on" is the whole-map one, and it is
0.4% rather than 64%.

### The mindist pair's shape, read from `vphysics.dll` — and where the trail stops

**A mindist has TWO feature-kind fields, one per synapse, not one.** `FUN_1800b2460`
(`ivp_mindist_recursive.cxx`) dispatches on a short at `+0x5a` for one object and another at
`+0x92` for the other, each 0–3, with the "none of these" branch calling `Error(...)` at line 0x5d
and 0x32 respectively — an assert, not a real case. That is the pair, explicit in the struct: this
object's closest feature and that object's closest feature, held independently.

**Recompute happens through a vtable, not inline.** When both features read as "unknown"
(`FUN_1800b2460`'s fallthrough), it calls `FUN_18008ecb0` directly; when at least one kind is
resolved it instead calls through `(**(code**)(*(longlong*)(lVar2+0x20)))(...)` — a virtual
dispatch on the mindist's own vtable at `+0x20` — then converts the result via
`FUN_180097d60` and reschedules with `FUN_1800b29b0`. So updating an established pair and creating
a fresh one are genuinely different code paths, which is the "track vs. derive" split the sticky-face
attempt collapsed into one.

**The reschedule (`FUN_1800b29b0`) is bookkeeping, not geometry**: it reads two bodies' positions at
`+0x158`/`+0x150`/`+0x160` relative to each body's own transform time-delta at `+0x1d0`, extrapolates
by velocity (`+0x174`/`+0x170`/`+0x178`), and hands the extrapolated points to `FUN_180096680`.

**`FUN_180096680` is where the trail stops, and it is not a distance formula — it is GJK/EPA.** The
decompile shows a hashed vertex cache (open-addressed, `puVar12[(int)uVar17]` probing on a computed
hash of a support-point id), building a simplex/polytope over calls to each body's own support
function (`(**(code**)(*plVar13+0x18))`), with per-entry state cached across STEPS so the walk does
not restart from nothing every call. That is IVP's actual narrow phase — a real convex-convex
closest-points solver with warm-started simplex state — not a formula this project can transcribe
into a few lines.

**What this settles:** the project's "one retained pair" model was never going to be the fix by
itself, because the engine's pair is backed by a stateful GJK solver whose OWN warm start is the
thing doing the work session after session — re-deriving the shallowest ledge face each step (what
this project does) and re-deriving a GJK simplex each step (what a naive port would do) are the same
class of mistake for the same reason.

**What is NOT established, and is where this stops rather than where it is finished:** porting a
convex-convex GJK/EPA solver is a multi-week feature on its own, is exactly what
`docs/DECISIONS.md`'s "feature-based narrow phase, not a smaller list" already names as the real
fix, and per this project's decompiler rule nothing here gets pasted into source — only the shape
above. Building it is future work, not a same-session divergence to close.

### A retained face is not a mindist — built, measured worse, reversed

**The missing closest-feature pair has been named by five measurements, so it was built. It lost.**
The attempt: keep the world feature each hull point landed on, and re-measure the point against
THAT face rather than re-deriving the shallowest one every step.

| | before | after |
|---|---|---|
| corpses settling and sleeping (`corpse-drop`, five drops) | 4 of 5 | **3 of 5** |
| free lift added per tick | 5,551–21,198 | 6,383–17,431 |
| slope conformance test | 33.5 units a second | unchanged |
| deepest penetration, sampled ticks | 2.66, 6.44 | 1.29, 4.80 |

Penetration is the one thing that behaved as predicted. Everything else was flat or worse.

**Why it lost, and the distinction is the whole finding: a retained face is STICKY, and a mindist is
not.** IVP tracks which pair of features is closest and updates that pair incrementally as the two
bodies move — *track and update*. Freezing one face and continuing to ask about it is only the
second half of that. On a seventeen-body ragdoll the genuinely closest feature changes several times
a second, so a frozen face measures a surface the body has already left, for as long as the pair is
held. That is worse than re-deriving, which is at least measuring something the body is near.

**So "keep the pair" is not implementable as "remember the answer".** It needs the incremental
tracking that makes the kept pair still be the closest one — which is the part of the mindist system
`docs/findings/51` has always listed as unread, and it is unread still.

**Two pieces of the attempt survive because they are correct independently of it:**

- **Terrain features are identified PER TRIANGLE**, where every terrain contact in the map used to
  share one id (`int.MaxValue`). That was documented as a known coarseness and it made a body
  crossing from one triangle to the next indistinguishable from one staying put — on exactly the
  surface corpses land on. Ids are negative now, below `Speculative`'s −1, so the three kinds cannot
  collide without anyone bounding the ledge count.
- **`IvpWorldCollision.Against(feature, point)`** re-measures a NAMED feature and returns a SIGNED
  distance — positive outside, negative penetrating, null when the pair is dead. Any correct version
  of the mindist needs exactly this, and it is what the sticky version was built on.

### The word "hole" was wrong, and so was the subject

**The owner, on the first version of this: *"they literally cant have holes because hammer doesnt
allow holes, so idk what you mean by holes"*. He is right and the correction is worth more than the
finding was.** The map has no hole in it. What has a gap is a reading of the map, and the whole
question is WHICH reading.

**Our physics world is complete, and this is the measurement that settles it.**
`koth_harvest_final`'s `LUMP_BRUSHES` declares **2,722 solid and 314 playerclip brushes** against
**3,030 ledges** read out of `LUMP_PHYSCOLLIDE` — and vbsp writes one convex per referenced brush,
`VisitLeaves_r( planes, dmodels[0].headnode ); planes.AddBrushes();` (`ivp.cpp:1278-1279`). Model 0's
two solids read 2,671 and 312 against those 2,722 and 314. Nothing is being dropped.

**Corrected 2026-09-12: that measurement is still right and the sentence it supports was not.** It
was taken while every ledge in the project was rotated 180° about X (B400, below), and a rotation
preserves counts exactly — so "our physics world is complete" was true while "our physics world is
correct" was false, and the two were read as one claim. Complete is a statement about the
denominator; correct needs an identity the format guarantees, which is what the box census
eventually supplied. The conclusion below about displacement base brushes survives independently —
it is read from `virtualterrain {}` and vbsp, not from these counts — but it was also doing work it
should not have been asked to do, because a genuine physics-side fault was being explained away as
a legitimate disagreement between two readings.

**So the 26 columns are the CAMERA's reading, not the corpse's.** Our `BspLeafTree.Sweep` stops on
brushes that vphysics has no collision for — a displacement's base brush is solid in `LUMP_BRUSHES`
and absent from `LUMP_PHYSCOLLIDE` by design, because `virtualterrain {}` hands that ground to the
virtual mesh instead. A corpse and a camera therefore SHOULD disagree there, and the instrument was
reporting the disagreement as a defect in the corpse's world.

**Which also disposes of the corpse that started this.** Its seed is where the demo's ragdoll
SPAWNS, not where it comes to rest; the probe drops it from rest at a point the real corpse only
passes over. The instrument's own defaults were the error, not the world — the fourth time a probe
here has produced a confident wrong answer and the reason
`docs/memory/instrument-bugs-outnumber-decoder-bugs.md` exists.

**What is genuinely open**: `LUMP_PHYSICS_DISPLACEMENT` (28), where vbsp writes each displacement's
virtual-mesh collide, is not read by this project at all — we rebuild the mesh from `LUMP_DISPINFO`.
Since the tesselation is now Valve's own, the two should agree, and comparing them is the check that
would prove it rather than assume it.

**A second defect is separable from the first and both are present.** Seeded at y -1416, ON good
ground with a floor at z 0 beneath it, a corpse still travels 63 units north into the hole before
falling. Ground that is missing and a corpse that will not stay put are different faults.

### The slope test's mechanism, traced to a number — uncommanded spin-up, not rocking

Two attempts to loosen `Separate`'s `total = Max(0, carried + extra)` clamp were built and measured
worse (linear velocity for the release decision, 31→67 units/sec; bounding the release to half of
`carried` per step, 31→42). Both assumed the zero-out was itself the fault. A third, decisive trace
— body velocity and angular velocity printed alongside `arm` on every rub — settles what the first
two could only guess at.

**The body is not rocking. It is being spun up, continuously and without a physical cause.**
`Body.AngularVelocity` about Y, sampled across the run: −32.36, −32.51, −32.53, −33.46, −33.43,
−33.94, −34.36, −34.58 rad/s — over five full rotations a second, and MONOTONICALLY GROWING. A box
sliding down a real 1-in-10 slope under gravity and friction alone does not spontaneously spin up;
something in the solve is injecting angular momentum every step, the rotational counterpart of the
`Lifted` linear-energy pump `IvpEnvironment.Lifted` already measures.

**The cause is visible in the same trace: `arm` — the manifold's representative contact point —
jumps between genuinely different corners of the box every step**, not a stable pair: 0.769, 0.107,
0.443, 0.397, 0.489, 0.659, 0.208, 0.328 (X-components across eight consecutive rubs). A single
torque applied at a wandering, arbitrarily-ordered moment arm does not converge to damping rotation
the way a torque applied at a FIXED contact point would — each step's correction is computed as if
it opposes the current slip, but the arm it is applied through has no continuity with the arm the
PREVIOUS step corrected through, so nothing here can ever integrate to zero net torque. Over enough
steps that produces a random walk with an apparent bias, which is exactly a monotonic climb.

**This is the sixth independent measurement pointing at the same root cause**, and the first with a
number precise enough to say WHAT the missing narrow phase costs rather than only that it is
missing: a stable multi-point manifold — the actual feature-based narrow phase five earlier
sections already name — would apply every step's torque through the SAME small set of contact
points, which is the only thing that can make repeated torque corrections converge instead of
random-walking. `IvpWorldLedge.Support` and `Gjk.Distance`, built and wired this session, give a
single closest-point PAIR per body-ledge pair; they do not yet give the multi-point manifold this
symptom needs. That remains the open, scoped, multi-week item.

### Two real fixes narrowed the gap; the residual is not a convergence-time problem

Two fixes landed on the mechanism the spin-up trace pointed at: friction solved at every manifold
member's own arm instead of one collapsed point (31.1 → 20.0 units/sec), and each member keyed to
its own warm-start slot rather than colliding on one shared by normal alone (20.0 → 9.3). Both
measured with zero regression across the suite, the gate, and all four real corpse-drop seeds.

**A scratch run at 1,600 steps — four times the test's 400 — corrects an over-read of the second
fix's own trace.** A sixteen-line sample taken right after the second fix showed angular velocity
falling steadily, which looked like ordinary convergence still in progress. Extending the run
disproves that: speed at step 400 is 7.95, close to the committed measurement, but by step 800 it is
back up to 22.86, then 18.8, then 18.2 at 1,599. **This is not decaying toward rest — it is
oscillating, net roughly flat, well past the point four times as much simulated time as the failing
test already allows.**

**The likely mechanism is a genuine physical one this solver cannot arrest by construction, not a
bug still to find.** A rigid box that starts tumbling on a one-directional slope, rather than
sliding flat, can keep re-gaining energy each time it rolls over one of its own corners — gravity
does work on the fall, and Coulomb friction only opposes SLIP at a contact, never rotation. A body
in a genuine tumble can have near-zero slip at its instantaneous contact point the whole time (which
matches the measured `wanted` values staying tiny, well under the cone's `allowed` budget, for most
of the run) while still carrying substantial linear and angular kinetic energy from the tumble
itself. The two fixes reduced how BADLY the spurious contact churn spins the body up; they did not
stop the spin-up from starting, because that requires the actual stable manifold — a real narrow
phase never lets the body tumble in the first place, holding a face-face contact through the
churn that currently starts the tumble.

**So the remaining gap is not "needs more steps" and not a fifth tunable in the friction layer.**
It is the same feature-based narrow phase five earlier sections and the spin-up trace all name,
now confirmed as the actual bottleneck by a direct measurement that a longer run does not converge.

### Two more leads, both checked and ruled out before another patch

**Iterating the friction solve, the way `IvpConstraintGroup.Solve()` iterates ragdoll joints, was
considered and rejected by READING rather than by measuring.** `IvpConstraintGroup`'s twelve-entry
relaxation table (`0.4, 0.4, 0.4, 0.4, 1.0, 1.0, 0.8, 0.6, 0.8, 0.8, 0.8, 0.8`) looked like exactly
what a body touching several points at once would need — a Gauss-Seidel pass repeated within one
step, letting several simultaneous contacts converge against each other the way real multi-point
manifolds do. But that table is `FUN_18003c780`'s JOINT relaxation, a different constraint group
from contacts entirely, and this project's own earlier reading already establishes the contact
system's real cardinality: `FUN_1800836b0` "walks each system exactly once" per PSI — single-pass,
not iterated. Building an iterated friction solve would have been a FABRICATED divergence, adding
behaviour the engine does not have, not fixing one it does. Caught before any code was written,
which is what reading the engine before designing is for.

**The initial impact was checked for a spurious kick and found clean.** If the tumble's true origin
were the very first touchdown imparting more spin than a real impact would, no amount of ongoing
friction tuning could ever have helped — a real tumbling box cannot be arrested by friction either,
so the fix would have to be at `Oppose`, not `Rub`. Traced directly: angular velocity during the
arrival passes climbs to roughly 4 rad/s by the end of the first `Simulate()` call, for a box
landing at an angle on a slope. That is not an obviously wrong number for an impact of that kind,
and it is far short of the eventual tumble's magnitude — so the spin-up compounds during the
ONGOING resting phase, consistent with everything measured above, not from one bad initial kick.

**A third lead looked promising and dissolved on closer reading — recorded so it is not re-raised.**
`IvpEnvironment._resting` is literally `_contacts` (`private List<IvpContact> _resting => _contacts;`)
and `_contacts` is cleared at the top of every slice's `Advance()` call, so `Rub()` — called once
after the whole interval is walked — only ever sees whatever the LAST slice's `Find()` populated.
That looked like a real bug: a point that mattered in an earlier slice but not the final one would
never reach friction at all. It is not, because `IvpContact.Find` is not incremental — every call
unconditionally re-tests every hull point against the world from scratch, so the final slice's
`_contacts` already IS the body's complete current touching set, not a partial one missing earlier
slices' points. Checked by reading `Find`'s loop bound again rather than by writing an accumulator
and measuring whether it helped.

### Discovery-time churn is real, and hysteresis for it measured worse

A fourth trace — logging which hull point indices `Find` reports touching on every single call, not
just the group-level summary — found something none of the grouping-level fixes could have reached:
some `Find` calls report ZERO points touching entirely, sandwiched between calls (same tick, same
body) that find one. That is a complete miss for a whole slice: no contact object is created, so
neither `Separate` nor `Rub` runs for the body that slice, regardless of the manifold or warm-start
fixes already landed — those only ever improve the solve for a point that DID register as touching.

**A hysteresis margin was built and measured worse.** `IvpWorldCollision.Touching` gained an
overload accepting a margin (a point up to that far OUTSIDE a face still counts), and
`IvpRigidBody.HasRestedAt(point)` gave `Find` a way to apply that margin only to a corner with an
established rest identity — never to a genuinely new point, and never widening the shared test's
default for any other caller. Landing speed went from 9.3 units a second to 14.8. Reverted in full;
verified bit-identical to the prior commit afterward.

**Why it lost, worth recording precisely:** preventing a point from dropping OUT of the touching set
matters less than what happens once a widened test admits one. A point at a shallower angle than the
real contact gets solved as if it were fully resting rather than barely so, and the false positives
this creates apparently cost more than the true dropouts they prevent. The discovery-time churn this
was built to fix is real and measured; this particular remedy for it is not the answer.

### An analytic same-plane fallback within one call — also measured worse

A fifth attempt, distinct in kind from the hysteresis just reverted: instead of widening one point's
OWN spatial query, let a point whose query fails be tested directly against the PLANE an earlier
point in the SAME `Find()` call already confirmed — no memory across steps, pure within-call
geometric consistency, the thing a real face-face contact gives for free by having one shared plane
rather than several independent point queries.

**Measured worse still: 9.3 units a second became 23.6, the largest regression of any attempt so
far.** Reverted in full; bit-identical to the prior commit confirmed afterward (9.306977f exactly).

**A pattern is now visible across all three per-point remedies for the discovery-time churn**
(hysteresis, this same-plane fallback, and by extension anything else that makes MORE points count
as touching within a single `Find()` call): each one that admits more contacts than the current
code independently finds makes the tumble WORSE, not better. That is the opposite of what "the
discovery-time churn is dropping real contacts" would predict, and it is worth stating plainly:
whatever is actually driving the spin is not well-modelled as "not enough contacts are recognised".
The two changes that DID help (solving friction at every already-found point, and giving each its
own warm-start slot) both worked with the EXISTING contact set rather than trying to enlarge it.
That is the shape the next attempt should have, if there is a sixth: solve what is already found
better, do not find more of it.

### Weighting depth to match the arm's own weighting - also measured worse, and a real prior warning explains why

The pattern from the last section suggested a further instance: `Separate` receives `deepest`, the
group's plain MAXIMUM depth, applied through `arm`, the group's now depth-WEIGHTED centroid — two
different combinations of the same set, magnitude and lever no longer describing the same effective
point. Weighting depth the same way `arm` is weighted (rather than taking the max) looked like the
same fix applied a second time.

**Measured worse: 6.15 units a second became 12.7.** Reverted; bit-identical to the prior commit
confirmed (6.1484184f exactly).

**A comment already in this file, from an earlier session's own reversal, explains why before this
one repeats the mistake.** `IvpEnvironment._resting`'s remarks record that POOLING contacts across
slices was tried, improved the slope test (33.5 → 19.2), and simultaneously dropped corpse-drop's
real settling rate from four of five to two — because "a pooled contact carries the DEPTH and the
ARM it was measured at, from a position the body has since left. `Separate` takes the deepest of a
manifold, so stale depths make the position correction larger and the free lift with it." **`deepest`
being the MAX rather than an average is not an oversight the weighted centroid change should have
carried over to — it is a deliberate guard against under-correction**, and averaging it away
reintroduces exactly the failure mode that specific guard exists for. The pattern from the previous
section ("solve the set better, don't find more of it") is real but not unconditional: not every
combination that is MORE consistent with the arm's own weighting is more correct, when the original
choice of combination was already a considered trade-off rather than an oversight.

### The cone's full-share budget is correct as it stands — a third division scheme also lost

Two prior measurements bracket how the friction cone's summed budget is handed to a manifold's
members: full share to every member (kept, committed) beat an equal division by count (reverted).
A depth-proportional share — the same weighting the centroid uses, giving a barely-touching point a
small fraction of the budget and a pressed-in point most of it — sat between those two conceptually
and looked like the natural next refinement.

**Measured worse than either: 6.15 units a second became 17.1.** Reverted; bit-identical to the
prior commit confirmed. Between the three measured points — full share, equal division, and
depth-proportional share — full share is not merely the best of the two tried before; it beats a
THIRD, more physically-motivated scheme too. Whatever makes full-share work is not "it happens to
avoid under-dividing the budget", since a scheme that divides MORE generously than equal division
(depth-proportional gives most of the budget to whichever point is deepest, more like full share for
that one point) still loses badly. Recorded so this exact family of idea — divide the SAME cone
budget some other way — is not retried a third time without a new reason to expect a different
result.

### An analytically-derived incident face — genuinely different in kind, and a NULL result

Every earlier attempt to admit more contacts shared one property: each re-queried the WORLD for a
point the world's own per-point pass had already answered once, inheriting whatever noise made that
answer flicker. A materially different mechanism was built: given the dominant contact normal this
step, transform it into the body's OWN local frame, find which local hull vertices sit lowest along
it (the box's own incident face, purely a function of current orientation), and raise a contact for
any of those vertices the per-point pass missed — asking the BODY's geometry instead of the world
a second time.

**Measured bit-identical to the committed baseline: 6.1484184f, unchanged to the last decimal.** Not
a regression — a true null. The likely reason: during an active tumble the box's instantaneous
incident face relative to a fixed external normal is not a flat face at all, it is close to a single
point or edge, which is exactly what the per-point pass already finds on its own — there is nothing
left over to backfill until the body is closer to resting flat, which this run may never reach
within the window measured. Reverted for zero benefit at real added complexity; verified bit-
identical to the prior commit.

**What this does establish, positively**: the "admit more contacts" family is now ruled out by a
FOURTH, structurally distinct mechanism, closing off the last obvious variant of it — geometric
derivation, not just repeated world queries in different shapes. Nothing about contact MEMBERSHIP,
tried four separate ways, has moved this test. Whatever remains is in the SOLVE, or in aggregate
dynamics (rotational energy from repeated impacts, a genuinely tumbling rigid body) that only a
real, EPA-capable narrow phase with actual penetration resolution — not a differently-selected point
set — can address.

### Stale-slot reactivation across many revolutions — also a clean null

A genuinely different axis from every prior check: `Body.Sliding` is never pruned, and the measured
spin is a steady, single-axis roll rather than an oscillation — meaning a tumbling box cycles through
a small, repeating set of orientations, and a slot from several revolutions ago could be reactivated
by a later normal that happens to match closely, reading back `Holding`/slip measured at a
completely different velocity. A small capacity cap (`Body.EvictStaleSlots`, evicting the oldest
slot past eight) was built to test this.

**Bit-identical to the baseline again: 6.1484184f.** Either `Sliding` never reaches the cap in this
run, or stale reactivation was not occurring at a magnitude this test can show. Reverted; verified
bit-identical.

**Fifth confirmation, on a fifth genuinely distinct axis** — after four membership mechanisms and
now warm-start staleness — that the remaining gap survives every angle this session can construct
from the existing architecture without the actual persistent, feature-based manifold.

**Correcting my own later mis-citation of this same finding.** Several commits after this section
was written, `corpse-drop`'s default report of "4 of 5 settle, the fifth leaves the world" was cited
repeatedly as an open, unrelated ground-hole divergence — as if a fourth defect remained beside the
three above. It does not: the fifth seed IS this section's 2277 spawn point, and this section
already explains why dropping it from rest there proves nothing about the demo. Read every later
"corpse-drop: 4 of 5" in this branch's commit history with that correction in mind.

### The per-member `Rub` fix does not transfer to `Oppose` — a real asymmetry, not an oversight

`Manifolds`'s resting branch calls `Rub` once per manifold member at that member's own arm (Fix 1,
this session) rather than once at a single blended arm — treating a body touching several points as
touching several points, not one. The arriving branch still called `contact.Oppose(arm)` once, at
the blended representative arm, the whole time Fix 1 was landing on the other half of the same
method. That looked like a missed spot: the same reasoning ("a manifold is several points, not one")
should apply to the impact solve too.

Built the obvious mirror — loop `members[member].Begin(); Passes += members[member].Oppose(members[member].Arm);`
per member, same shape as the `Rub` loop.

**Measured WORSE: 13.608729f, more than double the 6.1484184f baseline.** Reverted to the single
`contact.Oppose(arm)` call; rebuilt; re-ran; confirmed bit-identical to baseline. `git diff --stat`
empty before rebuilding.

**Why this is a real result and not just another null.** `Oppose` is the impact/approach solver —
it fires per-slice during the interval walk, once per contact still arriving, and is meant to
resolve a COLLISION event, not distribute a resting load. Splitting one impact into N independent
per-member impulses double- (or N-fold-) counts the impact response where the resting case is
correctly summed as separable per-point support. The two solves are not the same kind of problem
just because they share a manifold data structure — `Rub`'s "solve what is already found, at every
point that has it" principle is about distributing SUSTAINED load across contact points that persist
across a step; `Oppose` is answering "how hard did this body just hit", and hitting is a property of
the approach as a whole, not additively decomposable across points sharing a normal. This is the
first attempt this session where the general pattern ("solve the existing found set more precisely
rather than finding more of it") actively made a result worse instead of null — the pattern has a
real boundary at the arrive/rest split itself, not just at membership.

*Evidence class: measured, exact revert confirmed bit-identical.*

### Extending the ledge GJK manifold to terrain — the same one-point failure, now on terrain

The ledge GJK manifold pass in `IvpContact.Find` (one `Gjk.Distance` per (body, ledge) pair,
replacing the per-vertex walk) explicitly leaves terrain per-point — its own comment names this as
the remaining gap: *"terrain and the speculative/tunnel-prevention path below are what remain
per-point."* A triangle is already a three-vertex convex hull, so `IvpWorldLedge.Support`'s brute
force over `Vertices` needed no new primitive — it was built to answer exactly this the moment a
triangle was wrapped in the same record type.

Added `IvpWorldCollision.NearbyTriangles(centre, radius)`, gathering terrain triangles near a body
from the existing `_triangleGrid` and exposing each as a degenerate `IvpWorldLedge` (its three
vertices, centroid as `Center`, max vertex distance as `Radius`). Wired it into `Find` right after
the ledge loop: one `Gjk.Distance(BodySupport, triangleLedge.Support)` call per nearby triangle,
covering it (skipping the per-vertex fallback for that triangle) exactly the way a covered ledge is
skipped.

**Measured WORSE: 11.153114f, nearly double the 6.1484184f baseline.** Reverted in full — the new
`NearbyTriangles` method, the `TerrainFeatureForContact`/`TriangleOf` accessors, and the `Find` wiring
— confirmed `git diff --stat` empty, rebuilt (0 warnings), re-ran, bit-identical baseline.

**Why this failed for exactly the reason already on record two sections up.** This file's own
comment at the per-vertex loop says it outright: *"Every touching point raises a contact, and ONE
PER BODY was tried instead… reproducing the count alone measured worse: penetration went from 7 to
27."* GJK's closest-point-pair answers "where are these two convex shapes nearest", which for a box
resting flat on a triangle is ONE point — the same collapse the ledge manifold already accepts for a
convex BRUSH (a box on a flat brush face has the same problem, and is not fully immune to it either,
it is just less commonly hit because brush ledges are rarely a single thin triangle a box spans).
Terrain is triangulated far more finely relative to a resting box than most ledges are, so nearly
every resting contact hit exactly this collapse: a box spanning 2-4 triangles got 2-4 independent
single-point GJK contacts, each blind to the others, instead of either the four-corner per-vertex
patch it had before or one true multi-point manifold across the covered triangles combined.

**What this establishes**: the GJK-manifold mechanism itself is not the general answer to "stop
sampling more points than the engine would" — it is specifically an answer for a SINGLE dominant
contact per pair, and a resting box on flat ground has FOUR simultaneous ones. Extending it blindly
to a surface triangulated finer than the resting footprint reproduces the single-point-per-pair
regression on a new surface, not a manifold. The real fix, per the pattern across every failed
lever this session, needs a true multi-point manifold: either merge GJK results across
CO-PLANAR nearby triangles into one shared support set before calling `Gjk.Distance` once, or an
actual SAT/EPA face-clip between the box's incident face and the merged terrain patch — not a
per-primitive GJK call, however the primitive is chosen.

*Evidence class: measured, exact revert confirmed bit-identical.*

*Evidence class: read from published SDK source for vbsp's passes, the shrink sizes and the power-4
switch; measured on `koth_harvest_final`, `ctf_2fort` and `cp_dustbowl` for every count; arithmetic
for the terrain denominator.*

### The engine collides the HULL against each triangle; we sample vertices against planes (B306)

**Read from published source, and it names the substitution exactly.** `virtualmesh.h` declares the
callback vphysics uses to reach a displacement:

```c
virtual void GetTrianglesInSphere( void *userData, const Vector &center, float radius,
                                   virtualmeshtrianglelist_t *pList ) = 0;
```

The engine asks the displacement for the triangles within a SPHERE around the object, then collides
the object — a convex hull — against those triangles as shapes. `virtualmeshparams_t` carries
`buildOuterHull` beside it and `virtualmeshlist_t` carries a `pHull`.

**Ours inverts both halves.** `IvpContact.Find` walks the body's own hull VERTICES and asks
`IvpWorldCollision.Touching` which triangle PLANE each vertex is least far behind, within a 64-unit
slab. A vertex against a plane is not a hull against a triangle: it cannot report an edge-edge
touch, it knows nothing of the triangle's extent beyond a containment test, and its "depth" is a
plane distance rather than a penetration between two shapes. Everything this project has had to
invent — `Separate`, `Slop`, `Recovery`, `MaximumRecovery`, `TerrainDepth` — exists to clean up
after that substitution, and none of it appears in the engine.

**The primitive to port with is already here.** `Gjk.Distance` and `IvpWorldLedge.Support` give the
closest-feature pair between two convex shapes, and a triangle is a three-vertex convex hull — so
this needs no new primitive, only the engine's question (hull vs triangle) instead of ours (vertex
vs plane).

**And it explains the attempt already lost.** Extending the GJK pass to terrain measured
6.1484184f → 11.153114f because it REPLACED the per-vertex path through a covered-triangle set: a
box spanning two triangles then got two single closest points where it previously had four corners.
The primitive was right and the wiring was wrong. A correct port replaces the vertex sampling
wholesale, and keeps a persistent closest-feature pair per (body, triangle) the way IVP's mindist
does rather than re-deriving one each step — only then are the compensators removable.

*Evidence class: read from published SDK source for the callback and the params; measured for the
6.1484184f → 11.153114f attempt.*

### Terrain's outer hull is lump 28, handed to vphysics as `pHull` (B369)

**The slab this project uses for terrain thickness was filed as standing in for `buildOuterHull`.
That was half right: the engine does close the mesh, but it does not build the hull at runtime at
all when the map carries one — it reads it out of the BSP.** Read from the shipped
`bin/x64/engine.dll`, Ghidra project `tf2enginex64` under `D:\ghidra-proj`, and from published SDK
source where it exists.

**vbsp writes it** (`utils/vbsp/disp_ivp.cpp:314-350`): each displacement becomes a virtual mesh with
`params.buildOuterHull = true`, serialised by `CollideWrite` into `LUMP_PHYSDISP`, lump 28 —
`bspfile.h:459`, *"the binary blob for each displacement surface's virtual hull"* — as a `ushort`
count, one `short` size per displacement (`-1` for none), then the blobs back to back.

**The game asks for it** (`game/shared/physics_shared.cpp`): a `virtualterrain` block in the world's
physics keys sets `bCreateVirtualTerrain` (`:682-685`), and `PhysCreateVirtualTerrain` makes one static
object per displacement, named `vdisp_%04d`, out of `modelinfo->GetCollideForVirtualTerrain(i)`
(`:563-586`), declared *"Gets a virtual terrain collision model (creates if necessary)"*
(`public/engine/ivmodelinfo.h:164-166`).

**The engine loads it, in `FUN_18016f6d0`**, called from `CMod_LoadDispInfo` (`FUN_18016d520`) as
`FUN_18016f6d0(lumpData, lumpLength)` for lump `0x1c`:

```c
if (*lump != displacementCount)
    Error("LevelInit: Bad map data - displacement data does not match displacement collision data");
// size table -> offset table: 0xffff becomes -1, otherwise a running sum
blobs = alloc(total); memcpy(blobs, lump + 1 + count, total);
for each displacement i (stride 0x158, one CDispCollTree):
    if (!(tree[i].flags@+0x34 & 2))
        mesh[i] = physcollision->vtable[+0x170]({ &handler, i, lumpLength < 1 });
```

**`+0x170` is `CreateVirtualMesh`, and two readings agree.** Slot arithmetic over
`vphysics_interface.h` puts it at slot 46, which is `0x170`; and the argument is a three-field struct
laid out exactly as `virtualmeshparams_t { pMeshEventHandler, userData, buildOuterHull }`. So
**`buildOuterHull` is true only when the lump is empty** — a map that carries lump 28 never has its
hull built at runtime.

**The handler supplies the blob as the hull.** Its vtable is at `1803a2a50`, three slots in
`IVirtualMeshEvent`'s declaration order:

| slot | function | what it does |
|---|---|---|
| `GetVirtualMesh` | `FUN_18016f190` | fills the list from `CDispCollTree::GetVirtualMeshList` — which sets `pHull = NULL` (`dispcoll_common.cpp:1480`) — then **`pHull = blob + offset[i]`** when the map had the lump and this displacement's offset is not −1 |
| `GetWorldspaceBounds` | `18016f200` | copies a 24-byte mins/maxs record for displacement `i` |
| `GetTrianglesInSphere` | `FUN_18016f250` | the tree's sphere query with a cap of `0xc00`, which is `MAX_VIRTUAL_TRIANGLES * 3` = 3,072 (`virtualmesh.h:14`) |

`pHull` at `+0x18` is `virtualmeshlist_t`'s field order by arithmetic: a pointer, four ints, then the
hull pointer. **The unload, `FUN_18016f940`, frees the blobs and calls `physcollision` slot 16,
`DestroyCollide`, on every mesh.**

**So the chain is closed from compiler to collision:** vbsp builds and writes a hull per displacement;
the engine loads it, rebuilds the triangle mesh from the displacement tree, and hands vphysics the
stored hull beside the triangles. **This project reads the triangles and not the hull**, and a
512-unit slab stands where the hull goes — which is where `corpse-drop` finds limbs resting eighty
units under the ground.

**The blobs are measured, not yet decoded** (`phys-disp` probe): 533 of 533 on `koth_harvest_final`
and 135 of 135 on `cp_granary`, counts equal to the dispinfo count and declared sizes summing exactly
to the bytes after the table, each 37–611 bytes. That is far too small to be a triangle mesh; the
format is vphysics' own and is read from `CreateVirtualMesh`, the consumer of `pHull`.

**vphysics' side of the call, located in `vphysics.dll`** (project `tf2vphysics`). The
`VPhysicsCollision007` interface registers factory `18000c740`, which is `LEA RAX,[0x18011f0d8]; RET`
— a singleton whose image-initialised vptr is **`1800eaa40`**. Slot 46 of that table is checked
against its neighbours rather than trusted by position:

| slot | expected by declaration | what the bytes are |
|---|---|---|
| 19 | `UnserializeCollide(buffer, size, index)` | a thunk forwarding three arguments |
| 45 | `ThreadContextDestroy` | the shared bare `RET` an empty function folds to |
| **46** | **`CreateVirtualMesh(params)`** | **`MOV RCX,RDX; JMP 0x180025880`** |
| 47 | `SupportsVirtualMesh` | `MOV AL,1; RET` |

`FUN_180025880` allocates a 0x38-byte object and hands it and `params` to the constructor
`FUN_1800250e0`. **The constructor builds a hull only when `buildOuterHull` is set:** it calls the
handler's `GetVirtualMesh`, builds one convex over all the triangles, and if that fails a test it
builds two, one per half of the triangle range; `FUN_180003c50` packs the result into the object at
`+0x20`. With lump 28 present the flag is false and nothing is built there — so the stored hull is
consumed by a method of the object's own vtable (`1800ee278`, fifteen slots; slot 11 is the orphan
`Virtual mesh!` site at `180026090`), not by the constructor.

**The packer defines the blob, because its output is what vbsp serialised.** Read from
`FUN_180003c50`:

```
u32   hullCount            -- every blob on harvest and granary opens 01 00 00 00
per hull, 5 bytes           -- byte vertexCount, ..., byte (vertexCount * 3) / 2
per hull, a body            -- FUN_180004110(header, stream, hull, meshList)
```

**Confirmed from the writer and the size function, then over every blob.** The body writer
`FUN_180004110` and the object's own `CollideSize`, `FUN_180025e40`, agree on:

```
u32  hullCount
per hull, 5 bytes:  [0] triangles T   [1] a triangle subclass count
                    [2] edges E       [3] edges emitted   [4] base vertex
per hull, body:     T × 4 bytes  -- three edge indices and one byte per triangle
                    E × 2 bytes  -- two vertex bytes per edge, each (vertex index − base)
size = 4 + 5·hulls + Σ(4·T + 2·E)
```

The vertex bytes index **the displacement's own vertex list** — the engine already holds the
coordinates, so a hull costs a hundred bytes. The `phys-disp` probe checks the formula against
every blob: **exact on 533 of 533 on `koth_harvest_final` and 135 of 135 on `cp_granary`**, every
displacement storing exactly one hull, at most 86 triangles and 129 edges.

**What the hull is FOR is a separate question, and the first write-up of this section answered it
without evidence.** It said the hull "closes the mesh", so ground below terrain is inside something.
The unpacker, `FUN_180025330`, does not show that: on first use it calls `GetVirtualMesh`, takes
`pHull` (falling back to the object's `+0x20`), and builds ONE cache entry sized
`hullSize + 16·vertices + 48·triangles` holding both, through `FUN_180025f10`. The object's slot 2
then hands IVP ledges by walking that entry in 48-byte **triangle** records. So contacts come from
the triangles; the hull sits beside them in the same structure, and whether it acts as a solid, an
envelope for the radius query, or a filter is read from the surface manager (vtable `1800ee220`),
not assumed.

**Read: the hull is the ROOT of a two-level structure, not a solid.** `FUN_180025f10` lays out the
cache entry as triangles (0x30 each), then vertices converted to IVP metres and axes, then the hull
unpacked behind them, with the hull count at `+0x12`. The surface manager's radius query,
`FUN_1800261a0`, branches on its fourth argument: **null returns the hull ledge**; anything else runs
`FUN_180025bc0` and returns the **triangles** within the radius. Slot 0, `FUN_180026390`, returns the
hull as the single convex. So a body is collided against the displacement's hull first and against
its triangles once IVP descends. *That the fourth argument is IVP's root-ledge context is INTERPOLATED
from the two branches; nothing names it.*

**Which withdraws the premise this section opened with.** The hull does not make ground below terrain
solid, and the engine has no terrain thickness at all — contacts end on zero-thickness triangles
exactly as ours do. A TF2 limb does not end up under the ground because IVP never lets a pair
penetrate: the mindist is watched before surfaces meet. So two divergences are separated here:

- **We do not read lump 28**, so a displacement has no hull root and every triangle is queried
  directly. Real, and a parity gap, but it changes which triangles are asked about rather than what
  a contact does.
- **Our narrow phase lets a point pass a triangle and then invents a thickness to push it back** —
  `TerrainDepth` and `TerrainReach`, both 512. That is what buries limbs, and it is this document's
  standing prescription: a closest-feature pair that is tracked, so a pair is never allowed to
  penetrate, and then the compensators deleted.

*Evidence class: read from the decompiled `engine.dll` for `FUN_18016f6d0`, its caller's arguments,
the handler's three slots and the unload; read from published SDK source for vbsp, the game and the
interface declarations; arithmetic for slot 46 and for `pHull`'s offset, each agreeing with an
independent reading; measured for the lump counts and sizes. **The blob format is NOT established.***

### And the faces are already parsed — they are discarded one line before the physics (B306)

**`PhysicsLedge` carries `Points` AND `Triangles`** — the real faces out of the `IVPS` compact-ledge
structure, already read from a `.phy` and from a map's `LUMP_PHYSCOLLIDE`. Then
`RagdollSimulation` builds a body with

```csharp
Hull = Points(element.Hull),
```

which flattens every ledge to a bare point cloud and drops the triangles before the physics ever
sees them.

**That one line forced everything downstream.** With no faces on a body there is no incident face to
clip, no edge to test another edge against, and no way to ask the engine's question at all — so the
narrow phase became the only thing a point cloud supports: sample each vertex against a triangle
PLANE inside a slab. `Separate`, `Slop`, `Recovery`, `MaximumRecovery` and `TerrainDepth` are all
cleanup for that, and none of them exists in vphysics.

**So the port is not blocked on reading a closed format.** Valve's face data is in memory and is
being thrown away. Carrying `PhysicsLedge` through to `IvpRigidBody` instead of flattening it is the
first step, and it is what makes hull-vs-triangle contact — the engine's own question, per
`virtualmesh.h`'s `GetTrianglesInSphere` — expressible at all.

**Order of work, so it is not started from the wrong end again:** carry the faces onto the body;
add hull-vs-triangle contact using them and the existing `Gjk`/`Support`; keep the closest-feature
pair per (body, triangle) across steps as the mindist does; remove the vertex sampling; then delete
the compensators, which have nothing left to compensate for.

*Evidence class: read from this project's own source.*

## An inverse is not the map applied again, and a rotation hides that it is

**Everything above was read correctly and then used backwards for weeks.** `IvpTransform.Position`
sends `Source (x, y, z)` to `IVP (x, −z, y)`; its inverse sends `IVP (x, y, z)` to `Source (x, z, −y)`.
`IvpWorldCollision.ToSource` — the single seam where every collision hull in the project crosses into
Source space — spelled `(x, −z, y)` a second time. Two 90° rotations about X is a 180° one, so every
world brush, brush entity, static prop and `.phy` ragdoll body loaded upside down and back to front.
That is B400, and what it looked like from outside was corpses falling through the floor in about one
column in seven.

**A wrong ROTATION is the hardest kind of wrong transform to see, and a symmetric map makes it worse.**
Nothing was mirrored, nothing was scaled, nothing left the map's coordinate range. The extents were
map-sized. The ledge count tracked the brush count. The plane-count histogram was brush-shaped —
4,063 hulls with six planes or fewer. Every hull contained its own vertex average, so the normals were
outward. Every bounding sphere contained its hull. The contents masks were right. Individually printed
ledges were clean axis-aligned eight-point boxes. And because a 5CP map is symmetric about a diagonal,
much of the misplaced geometry landed where other geometry legitimately is, so even a nearest-neighbour
search returned a plausible slab.

**The measurement that could not be fooled was an identity, not a similarity.** The world's convexes
are built with `NO_SHRINK` — `BuildWorldPhysModel( collisionList[i], NO_SHRINK, VPHYSICS_MERGE )`
(`utils/vbsp/ivp.cpp:1531`), the `VPHYSICS_SHRINK 0.5` applying only to brush entity models — so a
six-plane axis-aligned world brush and its convex have the same eight corners exactly. Counting brushes
whose bounds equal some ledge's bounds needs no tolerance, no plane matching and no assumption about
normal direction:

| `cp_process_final`, 2,083 axis-aligned solid box brushes | matched |
|---|---|
| as read | 0 |
| the correct inverse | 1,964 |
| `mirror x and z` | 1,771 |
| every other flip | 0–95 |

**Print the whole table, never the winner.** `mirror x and z` scoring 1,771 is the map's own symmetry
answering, and a probe that reported only its best candidate would have named the wrong transform with
an impressive number beside it.

### The wrong turns, kept

- **The shrink was blamed for the mismatch.** An exact plane census reported 3,799 of 4,045 solid
  brushes unmatched, and that was written off as an artefact of `VPHYSICS_SHRINK`, with the tolerance
  loosened to 1.5 to compensate. The world is compiled with NO shrink, so the census had been right
  the first time and the loosening destroyed the only signal in it.
- **The map data was suspected.** The owner stopped that: *"you can never treat tf2's stock stuff as
  having a mistake, because it basically never does outside of a few bugs, but even those must be
  parity first, then fix."* Turning back to our own reader is what found it.
- **The tests had already made the same mistake.** `IvpWorldContactConformanceTests.Ivp` wrote the
  Source→IVP map out by hand, with a remark saying this was deliberate *"so this fixture cannot agree
  with a wrong reader by sharing its arithmetic"* — and reproduced the identical 180° error, because
  the same belief wrote both. Ten tests round-tripped through it and passed. **Independence of code is
  not independence of belief**; a fixture is only independent if its authority is a different SOURCE,
  and the fixtures now call the function that was read from the binary.

*Evidence class: the transform is read-from-source, from two functions in `vphysics.dll` that share no
code; the identification of the defect is measured, with the identity transform as its control.*
