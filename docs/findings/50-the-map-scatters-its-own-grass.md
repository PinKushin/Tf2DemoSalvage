# 50 — The map scatters its own grass, and it is not in the prop lump

**How a Source outdoor map reads as ground rather than as a texture**, and why this project drew
none of it until 2026-09-05 (B360, B362).

---

## Two prop systems, one lump

Game lump 35 is a directory of sub-lumps, not a structure array. Everyone who reads it reads
`sprp` — the static props, the rocks and crates and fences — and this project was no exception. The
directory on `koth_harvest_final` has four entries:

```
'sprp' at 19,154,300, version 6,  55,356 bytes    static props
'dprp' at 19,209,656, version 4, 1,492,552 bytes  DETAIL props
'dplt' at 20,702,208, version 0,           4      detail prop lightstyles, LDR
'dplh' at 20,702,212, version 0,           4      detail prop lightstyles, HDR
```

**`dprp` is nearly thirty times the size of `sprp` on that map.** It holds 28,699 objects against
1,050-odd static placements. The static props are what a person names when asked what a map places;
the detail props are what makes the ground look like ground.

They are a different system end to end — different lump, different structure, different draw path,
different lighting rule — and they are compiled from a different source. A mapper places a
`prop_static` by hand. Detail props are scattered by `vbsp` from a `%detailtype` key on the ground
material itself, so nobody places them and nobody counts them.

**Which maps have them is not obvious and is worth measuring rather than assuming:**

| map | detail props |
|---|---|
| `koth_harvest_final` | 28,699 |
| `cp_granary` | 19,513 |
| `cp_process_f12` | 18,841 |
| `cp_badlands` | 0 |
| `koth_badlands` | 0 |

*Evidence class: measured, `game-lumps` probe.*

---

## The structure declares its own padding, which is unusual and useful

`DetailObjectLump_t` (`gamebspfile.h:87`) is 52 bytes:

```c
Vector          m_Origin;            //  0
QAngle          m_Angles;            // 12
unsigned short  m_DetailModel;       // 24
unsigned short  m_Leaf;              // 26
ColorRGBExp32   m_Lighting;          // 28
unsigned int    m_LightStyles;       // 32
unsigned char   m_LightStyleCount;   // 36
unsigned char   m_SwayAmount;        // 37
unsigned char   m_ShapeAngle;        // 38
unsigned char   m_ShapeSize;         // 39
unsigned char   m_Orientation;       // 40
unsigned char   m_Padding2[3];       // 41
unsigned char   m_Type;              // 44
unsigned char   m_Padding3[3];       // 45
float           m_flScale;           // 48
```

**The padding is written as fields**, so the stride is the field sum and there is no compiler
alignment to reason about — the trap `docs/memory/struct-padding-is-on-disk.md` records does not
apply here. What does apply is the placement of `m_Type`: **44, not 45**. `m_Padding3` follows the
type rather than preceding it. A reader one byte late reads a padding zero and reports every detail
prop in the game as `DETAIL_PROP_TYPE_MODEL` — which is not an error, produces no warning, and
draws nothing, because the model dictionary on these maps is empty.

*Evidence class: read-from-source, pinned by a conformance test that asserts the byte at 44 is the
type and the byte at 45 is zero.*

---

## Below version 4 the engine refuses the lump, and that branch is worth carrying

```cpp
if (engine->GameLumpVersion( GAMELUMP_DETAIL_PROPS ) < 4)
{
    Warning("Map uses old detail prop file format.. ignoring detail props\n");
    return;
}
```
`detailobjectsystem.cpp:1449`

A map at version 3 therefore draws **no detail props at all** in TF2. Reading one anyway would put
grass on ground the real game leaves bare — a divergence that looks like an improvement, which is
the kind that survives review.

---

## The quad: three things that read backwards and are Valve's

`CDetailModel::DrawTypeSprite` (`detailobjectsystem.cpp:1012`):

```cpp
AngleVectors( m_Angles, NULL, &dx, &dy );
Vector2DMultiply( dict.m_UL, scale, ul );
Vector2DMultiply( dict.m_LR, scale, lr );
VectorMA( m_Origin, ul.x, dx, vecOrigin );
VectorMA( vecOrigin, ul.y, dy, vecOrigin );
dx *= (lr.x - ul.x);
dy *= (lr.y - ul.y);
```

**One.** `AngleVectors( angles, NULL, &dx, &dy )` asks for **right and up**. The three-output form
is (forward, right, up) and the first is discarded by the `NULL`. Names of `dx` and `dy` invite
reading them as forward and right; doing so builds every quad in the wrong plane, and the symptom is
grass lying flat on the ground rather than standing in it.

**Two.** The rectangle runs the other way vertically. Harvest's six entries:

```
sprite rect 0: ul (-6.00, 16.00) lr (6.00, -0.00)
sprite rect 4: ul (-10.50, 48.00) lr (10.50, -0.00)
```

`ul.y` is the TOP at 16 or 48 and `lr.y` is the ground at 0, so `VectorMA` displaces the first corner
*upward* and `lr.y - ul.y` is **negative**. The quad grows downward from its top corner. Code that
"fixes" the sign puts every sprite underground.

**Three.** The texture coordinates are swapped when the sprite is **not** flipped:

```cpp
if ( !m_bFlipped )
{
    texul.x = dict.m_TexLR.x;
    texlr.x = dict.m_TexUL.x;
}
```

**`m_bFlipped` is not a field of the lump, and the first reading of that was wrong.** It looked like
a flag that only the shape path ever sets, which would make the swap universal. It is not: it
ALTERNATES with position in the file.

```cpp
bool bFlipped = true;
while ( --count >= 0 )
{
    bFlipped = !bFlipped;
    DetailObjectLump_t lump;
    buf.Get( &lump, sizeof(DetailObjectLump_t) );
```
`detailobjectsystem.cpp:1771`

The toggle is at the TOP of the loop, so object 0 reads `false` and every second object after it
reads `true`; and it counts every OBJECT, models included, rather than every sprite. The engine's
fast path expresses the same thing a second way — a whole second dictionary with the x coordinates
pre-swapped (`m_DetailSpriteDictFlipped`, `detailobjectsystem.cpp:1621`), reached for under the same
`!bFlipped` test — which is why the two look like different mechanisms and are one.

**This shipped wrong for a few hours**, applying the swap to everything, which mirrors half a map's
grass the wrong way. Nothing in a picture would ever have named it: a mirrored blade of grass is a
blade of grass. It was found by reading `UnserializeModels` for a different question entirely, and
that is the general case — **a flag with no field to read is one whose rule lives in the loop that
sets it**, not at the site that tests it.

*Evidence class: read-from-source, all three.*

---

## Detail props roll, and almost all of them do

`vbsp` has two placement rules (`detailobjects.cpp:568-600`), chosen by `MODELFLAG_UPRIGHT`:

```cpp
if (model.m_Flags & MODELFLAG_UPRIGHT)
{
    // If it's upright, we just select a random yaw
    angles.Init( 0, 360.0f * rand() / (float)VALVE_RAND_MAX, 0.0f );
}
else
{
    // It's not upright, so it must conform to the ground.
    ...  // an arbitrary basis perpendicular to the surface normal, then a random spin about it
}
```

The second branch produces a full orientation, so a sprite on any slope carries pitch and roll.
**Measured on `koth_harvest_final`: 27,686 of 28,699 carry both**, and the 1,013 that carry neither
are the upright ones on flat ground.

This mattered because this project's `AngleVectors` carried only roll-free reductions, with a note
on them saying exactly what to do if that ever stopped being true:

> Nothing in this viewer rolls — the free camera clamps pitch and never rolls, and a recorded view's
> roll is not read. If one ever is, the full three-line form above is what replaces this, and it
> needs a `roll` parameter rather than a correction.

That note was written for the camera and it was right. Detail props are the first thing in the
project that rolls, so the three-argument forms are the implementation now and the reduced ones
delegate to them — one copy of Valve's six lines rather than two.

*Evidence class: read-from-source for the placement rule, measured for the 27,686.*

---

## Lighting: a detail prop is NOT halved, where a static prop is

Both are `ColorRGBExp32` and both are read with `TexLightToLinear` — `channel * 2^exponent`, with a
**signed** exponent. Read unsigned, a routine `0xFF` becomes a multiplier of 2^127 and the grass is
pure white on a map at dusk.

The difference is on the writer's side. vrad stores a static prop's per-vertex lighting halved so the
shader's overbright can double it back. A detail prop's base colour is written straight:

```cpp
Vector totalColor;
VectorAdd( directColor[0], ambColor[0], totalColor );
VectorToColorRGBExp32( totalColor, prop.m_Lighting );
```
`vraddetailprops.cpp:801`

— and the halving four lines below applies only to the **lightstyle** entries, not to this one.
Applying a static prop's doubling here would blow every field out.

Measured samples on harvest run from `(64, 34, 10)` to `(312, 224, 118)`. Both ends matter: the first
is a sprite in shadow, and the second is over 255, which is what the exponent is for and what the
clamp is for.

*Evidence class: read-from-source for both writers, measured for the samples.*

---

## Two orientations, and the split is not the same on two maps

`DETAIL_PROP_ORIENT_NORMAL` is a fixed quad. The two screen-aligned kinds have their angles
recomputed from `CurrentViewOrigin()` **every frame** (`CDetailModel::ComputeAngles`,
`detailobjectsystem.cpp:950`), so no static buffer can hold them.

| map | fixed | screen-aligned |
|---|---|---|
| `koth_harvest_final` | 20,117 | 8,582 |
| `cp_process_f12` | 14,506 | 4,335 |
| `cp_granary` | **0** | **19,189** |

**Granary is the reason to count rather than to sample.** A static implementation draws 70% of
harvest's grass and **none at all** of granary's. Reading the split off one map would have ranked
the per-view path as a small follow-up and been wrong on the map that needs it most.

**And the first version of this table was wrong in a way worth keeping.** It read granary as *324
fixed, 19,189 screen-aligned* — a count of ORIENTATION, taken to be a count of sprites. Granary has
no fixed-orientation sprite at all: its 324 fixed-orientation objects are
`DETAIL_PROP_TYPE_MODEL`, `models/props_foliage/grass_02_detailmodel.mdl`, a studio model that
neither the old path nor the new one draws.

The tell was in the same probe output and was read past: those 324 reported a `m_flScale` of
−181,657,600 and 0.00. A model does not use `m_flScale` — `CDetailModel::Init` for a model never
takes it — so the field holds whatever `vbsp` left there. **A nonsense number beside a plausible one
is the instrument telling you which rows it should not have selected**, and it took a screenshot of
the wrong hillside to go back and look at it.

*Evidence class: measured, and corrected once.*

---

## What TF2 does NOT have, and it is a third of the file

`USE_DETAIL_SHAPES` is defined for Day of Defeat and Counter-Strike only
(`detailobjectsystem.cpp:30`). Everything it guards — `SHAPE_CROSS`, `SHAPE_TRI`, the sway mechanic,
the player-avoidance push — is absent from TF2 despite the fields for it (`m_SwayAmount`,
`m_ShapeAngle`, `m_ShapeSize`) sitting in every map's lump. Every detail prop measured on harvest,
granary and process is a plain `SPRITE`, which agrees.

**This is the same shape as `$modblend`** (finding 12): a field present in shipped data that no code
in this game reads. The correct implementation is nothing, and knowing that is worth more than the
implementation would have been.

---

## The part that cost the afternoon: a chain of correct counts is not a chain of custody

Everything above was built, and the frame had no grass in it. What the instruments said:

- the game-lump directory: `dprp` found, 1,492,552 bytes.
- the reader: 28,699 objects, 6 sprite rectangles, all `SPRITE`.
- the builder: 20,117 quads, 8,582 skipped as screen-aligned, 0 of another type.
- the material: `detail/detailsprites` resolved, 512×512, DXT5, at table index 206.
- `MapWorld`: `398595 of 398595 prop triangles drawn`.
- the renderer's blend census: material 206 present in the translucent set.

Six correct numbers, and a bare hillside.

**The gap was between the last two.** `WorldRenderer` keeps the world's batches and the static prop
batches in two lists, because the engine draws them in that order — `DrawWorld`, the overlays, then
`DrawOpaqueRenderables` (`CBaseWorldView::DrawExecute`). `DrawOpaqueBatches` skips any batch whose
material blends, as it must, and the sorted translucent list was built from `batches` alone. A prop
run with a translucent material fell between the two passes and **was never issued**.

The engine has no such gap: `DrawTranslucentRenderables` walks the world's translucent surfaces and
the renderables together.

**And it was never only about the grass.** Five of `koth_harvest_final`'s eleven translucent batches
are prop runs, and all five had been drawn by nothing since the two lists were separated.

**The lesson generalises past this bug.** Every instrument here reported on a stage that had
genuinely succeeded; not one of them reported on whether a draw call was issued, which is the only
question that mattered. The rule this project already keeps — *"anything that produces output is not
done until an assertion has read that output on a real demo"* — is usually stated against a wrong
value. This is the other failure it covers: no value at all, with every upstream count healthy. The
instrument that could see it was a screenshot at a camera chosen from the lump's own coordinates,
which is why the `game-lumps` probe now prints the densest 512-unit cell of fixed-orientation
sprites. A picture is only evidence if it is pointed at the right place.

---

---

## The eye is an input, and baking it drew nothing on one map in three

**Two of a detail sprite's properties are functions of the view**, which is what makes this geometry
per-view rather than static:

```cpp
// EnumerateLeaf, detailobjectsystem.cpp:2762 — the alpha
if ( sqDist < m_flCurMaxSqDist )
    model.SetAlpha( sqDist > m_flCurFadeSqDist
        ? m_flCurFalloffFactor * ( m_flCurMaxSqDist - sqDist ) : 255 );
else
    model.SetAlpha( 0 );

// ComputeAngles, detailobjectsystem.cpp:950 — the facing
case 2:
    VectorSubtract( CurrentViewOrigin(), m_Origin, vecDir );
    vecDir.z = 0.0f;
    VectorAngles( vecDir, m_Angles );
```

**Three things in the fade arithmetic are worth carrying verbatim**, and all three look like defects:

- The FOV factor divides the maximum AFTER squaring and the fade BEFORE, so a factor of one half
  pushes one limit out by two and the other by four. At the default FOV the factor is exactly 1 and
  the asymmetry is invisible — which is precisely how it would survive being tidied.
- `MIN( fade, maximum - 1 )` looks like a guard against a degenerate divide and IS the mechanism by
  which `cl_detaildist 0` draws nothing. `low.cfg` ships that value.
- `SetAlpha` takes an `unsigned char`, so the fade truncates. Halfway across the band is 127.

**And `ComputeAngles` case 2 flattens the direction rather than the result.** Zeroing z before
`VectorAngles` keeps the sprite upright while it turns about the vertical; clamping the pitch
afterwards does not, because the yaw of a steeply inclined direction is not the yaw of its
horizontal part once the pitch is thrown away.

**`VectorAngles` normalises into [0, 360), which is not the inverse of `AngleVectors` as written.** A
direction rising at 45 degrees reports pitch **315**, and the vertical case has no `atan2` in it at
all and answers 270 for straight up. Nothing downstream of `AngleVectors` can tell 315 from −45; a
clamp or a comparison can.

*Evidence class: read-from-source throughout.*

---

## Still open

- **Detail props that are MODELS** (B363). `DETAIL_PROP_TYPE_MODEL` is a studio model scattered like
  a sprite. `cp_granary` places 324 of `models/props_foliage/grass_02_detailmodel.mdl`; harvest,
  process and both badlands place none.
- **The FOV factor** is passed as 1. A zoomed sniper's field of view would push the fade range out,
  and nothing wires one in.
- **`env_detail_controller`**, which lets a map override both distances. Measured absent from
  `koth_harvest_final` and `cp_granary` with `worldspawn` as the control; unmeasurable on
  `cp_process_f12`, whose entity lump is compressed.
- **The shipped quality configs.** `low.cfg` sets `cl_detaildist 0` and `ultra.cfg` sets 8592, so the
  shipped range of this one setting spans zero to seven times the default. Neither is read.
- **`dplt` / `dplh`.** The lightstyle lumps are read by nothing. `koth_harvest_event` carries 335 KB
  of them; `koth_harvest_final` declares none.
- **`cl_detail_multiplier`.** Every detail object is instantiated that many times, the extras
  displaced by `RandomVector( -50, 50 )` (`detailobjectsystem.cpp:1808`). It defaults to 1 and is
  `FCVAR_CHEAT`, so nothing changes today — but a config setting it would multiply the grass and
  this reads the lump's count as the answer.
