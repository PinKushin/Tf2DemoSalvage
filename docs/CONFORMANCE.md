# Conformance with Source

What this project reproduces of the engine, what it does not, and **what you would see** in each
case. Covers the whole surface rather than one subsystem — the demo format, the world, models,
materials and effects — because a viewer is only as convincing as its weakest layer, and the layers
fail in completely different ways.

**The executable half is 40 test classes**, 208 entries, currently **88 asserting parity and 120
naming a gap**. Measured 2026-08-16, not estimated:

```bash
dotnet test <project> --filter 'FullyQualifiedName~Conformance'
```

| Project | Parity | Gaps | Total |
|---|---:|---:|---:|
| `Core.Tests` | 51 | 46 | 97 |
| `Viewer3D.Tests` | 22 | 60 | 82 |
| `Content.Tests` | 15 | 14 | 29 |
| **Total** | **88** | **120** | **208** |

**The gap count going UP is the suite working.** It was 38 when this document was first written and
is 120 now, and nothing regressed — eighteen batches went looking for things that were never
implemented and wrote each one down as a runnable claim. A conformance suite whose gap count only
falls is one that has stopped looking.

The counts exclude the derived-number suites below, which are named for what they measure rather
than for conformance and are listed separately.

The original four classes:

| Suite | Covers | Source it is written against |
|---|---|---|
| `DemoConformanceTests` | messages, schema, entity deltas, interpolation | `public/inetmsghandler.h` |
| `WorldConformanceTests` | BSP lumps, lighting, water, skybox | `public/bspfile.h` |
| `ModelConformanceTests` | bones, attachments, flexes, IK, ragdolls | `public/studio.h` |
| `SourceConformanceTests` + `EffectConformanceTests` | materials and shading; particles, beams, decals, shadows | `stdshaders/`, `game/client/c_te_*` |

The skipped count IS the score.

**Seeded from the material census originally** — which reports, at every map load, the parameters and
shaders a real map asks for that this project does not implement — and then checked one at a time
against `source-sdk-2013`.

**That seed is exhausted, and what replaced it is worth knowing**, because the census could only ever
report gaps in things a map *declares*. The later batches came from three other questions, in
increasing order of what they turned up:

1. **Which declared fields does a conformance test already derive that no reader consumes?** A test
   pinning a structure's layout is simultaneously an inventory of what is being skipped over. Four
   gaps in one lump, from reading a test we already had.
2. **Which systems does the client RUN that leave no trace in any file?** A PVS is a lump read past;
   Hermite interpolation is a default whose absence is the opt-out; a soundscape is an index into a
   file never opened. None of these is a field going unread, so no inventory finds them.
3. **What did we write down as impossible without checking?** The largest single yield. "TF2's game
   code is closed" appeared in three places, was true of none of them, and reopened the item system,
   the HUD, player conditions and the capture-point rule in one afternoon.

The third question is the one to ask first on any new area.

## The derived half: numbers computed from the SDK, not typed twice

The suites above name behaviour. A second group checks the **numbers**, and it does not hold a copy
of them — each value is computed from Valve's own declaration and compared against the constant the
readers actually use.

| Suite | Derives | From |
|---|---|---|
| `BspLumpTests` | 29 lump indices | `public/bspfile.h` |
| `BspStructTests` | 19 strides and 20 field offsets | structure declarations in `bspfile.h`, `mathlib.h` |
| `StudioStructTests` | 55 header offsets and strides | `public/studio.h` |
| `VertexFileStructTests` | VTX and VVD strides, byte-packed | `public/optimize.h`, `public/studio.h` |
| `SurfaceFlagTests` | 12 `SURF_*` bits, and names the 4 ignored | `public/bspflags.h` |
| `EngineConstantConformanceTests` | `EF_NODRAW`, `EF_BONEMERGE`, handle widths | `public/const.h` |
| `UserCommandConformanceTests` | the usercmd field order and 14 widths | `WriteUsercmd` in `game/shared/usercmd.cpp` |
| `StudioFlagTests` | 6 animation format selectors, 2 sequence bits | `public/studio.h` |
| `ImageFormatConformanceTests` | 8 VTF pixel formats, implicitly numbered | `bitmap/imageformat.h` |
| `DetailCombineConformanceTests` | 12 detail blend modes | `stdshaders/common_ps_fxc.h` |
| `PlayerInfoConformanceTests` | `player_info_t`, padding included | `public/cdll_int.h` |
| `NetMessageConformanceTests` | message names and the numbering's gaps | `public/inetmsghandler.h` |
| `NetFieldWidthConformanceTests` | entity, model and class index widths | `public/const.h` |
| `GameEventConformanceTests` | event id width and the documented field types | `public/igameevents.h` |
| `StaticPropConformanceTests` | 4 versioned prop lumps | `public/gamebspfile.h` |
| `DisplacementConformanceTests` | the terrain record and its neighbour chain | `public/bspfile.h` |
| `CapacityGuardTests` | no safety cap is stricter than the engine allows | `studio.h`, `bspfile.h` |
| `WireEncodingConformanceTests` | the 4 coordinate widths, string and flag widths | `coordsize.h`, `dt_common.h` |
| `EntityMessageConformanceTests` | two message ids that collide at 1 | `game/shared/base*_shared.h` |

**The sweep that closed it.** Every constant in this project whose own doc comment cites an
ALL_CAPS engine identifier was collected — each of those comments is a claim — and checked against a
test. Everything on that list is now covered.

> **Correction, 2026-08-16.** This paragraph previously excepted `TF_CLASS_UNDEFINED` and
> `TF_FIRST_NORMAL_CLASS`, and called them "a decompile target … not a gap that can be closed from
> source", on the grounds that TF2's own game code is not public.
>
> **Both halves were false.** The identifiers are defined in `src/game/shared/tf/tf_shareddefs.h`,
> at lines 205 and 198. And TF2's game code *is* published — `source-sdk-2013` carries **1,318 files**
> under `game/shared/tf`, `game/client/tf` and `game/server/tf`, including all 125 HUD sources, the
> full player-condition enumeration, and the econ item schema.
>
> The mistake was searching one directory (`client/replay/`), finding a reference without a
> definition, and concluding the definition did not exist anywhere rather than that it was elsewhere.
> **An absence found by a search is a fact about the search.** Same shape as the level-name filter in
> `findings/24-reference-capture.md` and the "Econ" substring count in
> `UnimplementedItemConformanceTests` — three instances now, which is why it is written here rather
> than only in the entry it came from.
>
> The expensive part is not the wrong constant, it is that naming a decompiler as the next step made
> a five-minute lookup look like a project. Nothing was blocked visibly; it was just deferred.
> Whatever else "TF2 is closed" seemed to justify deferring is worth revisiting — the item system,
> the HUD and the material overrides were all reopened by this and are now specified in batch nine.

The coordinate widths are the ones to care about. A position is an integer part plus a fraction and
there are **two** of each — multiplayer origins use a narrower range and a coarser precision — so a
decoder using one pair everywhere is right near a map's middle and drifts at its edges. For a
documented surf or jump run that is the difference between a record and a fabrication.

**Why derived rather than compared.** A test asserting `56 == 56` against a header tests typing.
`CStruct` reads `struct dface_t`, sums its members under C's alignment rules, and asserts that total
against the reader's stride — so it also produces the field offsets, which is the half that matters:
a stride can be right while the fields inside it are read from the wrong places, and the sum is
identical either way.

**It caught its own author twice.** `LUMP_FACES_HDR` was written as 54 from memory (it is 58), and
the parser initially counted both branches of `#ifdef PLATFORM_64BITS`, which made
`mstudiotexture_t` 96 bytes and failed a correct constant. Both are written up in
[`findings/08-method.md`](findings/08-method.md).

### The gaps, and what happened to them

Three things were written down as uncoverable. **Two of them were not.**

| Claimed gap | Outcome |
|---|---|
| `ddispinfo_t` "embeds classes, not structs" | C++ makes `class` and `struct` identical for layout. One keyword in a regex; the whole chain now derives, ending at 176. |
| static props have "a per-version layout" | Four declared versions that only append. The reader's real assumption — origin, angles and prop type at fixed offsets in all four — is now checked. |
| VTX topology fields | **Genuinely uncoverable.** Added under a define the published SDK does not carry, so only the eight-byte difference between the two strides is checkable. |

The pattern is worth naming: an exclusion that sounds like a property of the FORMAT is often a
property of the reader, and it goes unexamined because it is written in the same confident tone as
everything around it. Both of these had been recorded, correctly-sounding, in the file they excluded.

**Still genuinely outside the SDK**, and pinned by other means: the `svc_` message numbering and the
netmessages field widths (binary scanning, held up by the corpus decoding).

> **Correction, 2026-08-16.** This paragraph also listed the game event type numbering as outside the
> SDK, "pinned by arithmetic instead". It is not, and it was not when that was written.
>
> **The ordering is stated in `igameevents.h:52`** — *"Valid data types are string, float, long,
> short, byte & bool. If a data field should not be broadcasted to clients, use the type `local`"* —
> which is 1 through 6, then 7. The enum in `GameEventDefinition.cs` had been citing that line the
> whole time; only this document was out of date.
>
> **The widths and signedness were genuinely missing, and they are published too** — in the comment
> block at the top of a shipped game resource file, `game/mod_hl2mp/resource/modevents.res`:
> `bool` is 1 bit unsigned, `byte` 8 unsigned, `short` **16 signed**, `long` **32 signed**, `float`
> 32. Signedness was previously assumed, and getting it backwards yields a plausible number rather
> than an error — a negative score reading as 65,000-odd.
>
> **The transferable part is where the answer was.** The source menu in `CLAUDE.md` lists the SDK,
> the Rust parser, the wiki and a decompiler. A comment block in a `.res` file is in none of those
> categories, and it settled a question filed as closed. When a question is about a *format the game
> reads*, the game's own data files are a source — and they ship with prose explaining themselves.
>
> Arithmetic is still what rules out the rival hypothesis (CS:GO's protobuf ordering needs four
> bits), so it remains in `GameEventTypeWidthConformanceTests` as corroboration rather than as the
> whole case.

## Why the census counts are not the priority order

The census is honest about what a map *declares* and says nothing about what a map *shows*. Three
kinds of entry hide inside one list, and only the first is work:

| Kind | Example | What you see if it is missing |
|---|---|---|
| **Per-frame feature** | `$phong` | every model dull, all the time |
| **Capability flag** | `$cloakPassEnabled` | nothing, until the one moment it fires |
| **Not ours to implement** | `%compilenodraw` | nothing, ever — it was obeyed by vbsp before shipping |

`$cloakPassEnabled` is the case that proves the distinction. It sits on **307** of cp_process's 1,034
prop and model materials, which is alarming until you read it: `vertexlitgeneric_dx9.cpp:288` calls
it "If material supports cloaking", and the pass is gated per frame on
`CLOAKFACTOR > 0.0f && CLOAKFACTOR < 1.0f`. Every player material declares the capability; almost
none uses it in a given second. **Sorting the census by count would have put a day's work at the top
of the list for something invisible unless a spy cloaks on camera.**

So every entry below carries its source in the SDK and a plain statement of what it costs to be
without it. An entry with no "what you would see" is not ready to be worked on.

## Implemented

| Feature | Source | Notes |
|---|---|---|
| `$basetexture`, `$basetexture2` | — | with `WorldVertexTransition` mixing by vertex alpha |
| `$bumpmap`, `$ssbump` | — | self-shadowing bump distinguished from normal maps |
| `$detail` and its blend modes | `lightmappedgeneric_dx9.cpp` | twelve modes, tint, scale |
| `$translucent`, `$alphatest`, `$additive` | — | alpha test wins when a material declares both, as Valve's clause does |
| `$selfillum`, `$selfillumtint` | — | tint defaults to (1,1,1) as the engine does |
| `Modulate`, `$modblend`, `$mod2x` | shader name | multiplies the framebuffer; was drawn opaque until 2026-08-15 |
| `UnLitTwoTexture`, `$texture2` | `unlittwotexture_ps2x.fxc` | `baseColor * baseColor2 * g_DiffuseModulation`, alpha forced to 1 |
| `$nocull` | `imaterial.h:369` | `MATERIAL_VAR_NOCULL`, per material; everything else culls back faces |
| back-face winding | `imaterialsystem.h:180` | `MATERIAL_CULLMODE_CCW` — front faces are clockwise |
| `$halflambert` | `common_vs_fxc.h:826` | `(N·L * 0.5 + 0.5)²` on the direct term only |
| skin families | `pSkinref(skin * numskinref + material)` | team colours are a skin family, not a tint |
| bodygroups | `shared/animation.cpp:876` | `(body / base) % nummodels`, selected per entity at draw |
| bone merge | `bone_merge_cache.cpp:122` | matched **by name**; unmatched bones walk their own hierarchy |

## Not implemented, ordered by what it costs

### `$phong` — every model is dull
330 materials, with `$phongboost` 329, `$phongfresnelranges` 329, `$phongexponent` 323,
`$basemapalphaphongmask` 102. This is Source's specular for characters, and its absence is the
single largest visual difference on players. **Read `vertexlitgeneric_dx9_helper.cpp` and
`phong_dx9_helper` before starting** — the mask channel is chosen by
`$basemapalphaphongmask` versus the normal map's alpha, and picking the wrong one produces a
plausible sheen in the wrong places.

**Implemented 2026-08-21, B128.** What remains of it is `$phongexponenttexture` — the per-texel
exponent — and with it `$phongalbedotint`, which reads its tint from that texture's green channel and
so cannot do anything without one.

**And a limit worth knowing before reading the picture**: the term is driven by the SUN alone. The
engine sums it over the light cache's local lights as well, and those do not reach a model here, so a
highlight appears where the sun reaches and nowhere else. That is smaller than TF2's and it is what
the decoded data supports.

### `$normalmapalphaenvmapmask` — reflective props shine everywhere at once
15 prop materials on cp_badlands, `cap_point_base` and its two team skins among them. The three
reflection masks are mutually exclusive by construction — the shader declares
`SKIP: $NORMALMAPALPHAENVMAPMASK && $BASEALPHAENVMAPMASK` — and this project implements the
base-alpha one and not this. **A material with a bump map cannot use `$basealphaenvmapmask` at all**
(`lightmappedgeneric_dx9_helper.cpp:197` warns and drops the envmap outright), which is why TF2's
model materials use this one.

WHAT YOU SEE: a capture point reflects uniformly, across the painted and worn parts the artist
masked out. **Too shiny rather than too dark** — the opposite of B83's original symptom, and only
reachable now that reflections draw at all.

### Material proxies — the entity-state half
`TextureScroll` and `Sine` are ported, the `Proxies` block is parsed, and proxies are evaluated at
BIND on both the world and the entity model paths — which is what the engine does, since
`IMaterialProxy` has `Init`, `OnBind` and `Release` and no tick.

What remains reads the ENTITY the material is drawn on: `Subtract`, `PlayerProximity`, `Clamp`,
`PlayerTeamMatch`, `Divide` and `Multiply` are functions of team and distance, not of time, and the
material layer has no entity. An unrecognised proxy leaves the material at its resting value rather
than guessing. Filed as B80.

### `$lightwarptexture` — lighting curve is linear where TF2's is authored
308 materials. A one-dimensional ramp the engine looks up with `N·L`, which is a large part of
TF2's flat, illustrative look.

### `$rimlight` — no edge light
301 materials, with boost and exponent. Cheap next to `$phong` and visible on silhouettes.

### `EyeRefract` — 13 materials
The only unimplemented **shader** on cp_process. Already falls back to the iris texture
(`VmtMaterial.PrimaryTexture`), which is why eyes are not the missing-texture chequer, but the
refraction and the cornea are absent.

### `$basetexturetransform`, `$texture2transform`
19 and 5 on brushwork. The transform machinery exists; nothing parses the matrix form
(`center … scale … rotate … translate`) out of a VMT yet.

## A gap list rots, so it is now policed

**Five entries in this document were false on 2026-08-21**, and one of them cost a feature being
built twice. `$envmap`, LUMP_CUBEMAPS, attachment points and viewmodels were all implemented while
their conformance markers went on skipping with "not implemented" — and a skipped test is invisible
in a green run, so nothing said otherwise.

`ConformanceGapAuditTests` now fails when a marker outlives its gap. Each row names a marker and the
evidence that would settle it: a parameter is checked against `MaterialCensus.ImplementedParameters`,
which is maintained for its own reasons, and a feature is checked by loading a real map and asking
whether it produced anything. **It is policed in both directions** — a row naming a marker that no
longer exists fails too, because otherwise the audit quietly checks nothing.

See `docs/DECISIONS.md` D45.

## Deliberately not ours

`%compile*` flags are instructions to vbsp, obeyed before the map shipped. `$surfaceprop` picks a
footstep sound. `%keywords` is a Hammer search tag. These are counted separately by the census so a
later reader can tell "ignored on purpose" from "not got to yet".

---

## The conformance tests B135 needed and did not have

**An evening went into why a pipe drew behind the stripe on the wall behind it** — two reverted bias
changes, a depth-format change, a decompiler import — and the answer was a pass order Valve publishes
in `game/client/viewrender.cpp`. None of it was measurable by any existing test, because every
conformance suite here compares *parameters* and *formats*, and nothing compared **the shape of the
frame**.

`ScenePassOrderConformanceTests` now pins Valve's order. It cannot go red on ours, which is the
limitation to fix next; the list below is what would.

### What Valve does, and where

| claim | citation |
|---|---|
| world (with its overlay fragments) is drawn before opaque renderables | `CBaseWorldView::DrawExecute`, `viewrender.cpp:5487` |
| static props and brush models ARE opaque renderables | `DrawOpaqueRenderables_DrawStaticProps`, `_DrawBrushModels` |
| translucent renderables come after both | same function |
| a decal-flagged surface does not write depth | `EnableDepthWrites( false )`, `DecalModulate_dx9.cpp:66` |
| the decal bias constants | `m_DepthBias_Decal = -262144`, `m_SlopeScaleDepthBias_Decal = -0.5f`, `materialsystem_config.h:223` |
| an overlay's face list carries no orientation test | `Overlay_AddFaceToLists`, `vbsp/overlay.cpp:171` |
| an entity at index zero is never drawn | `C_BaseEntity::ShouldDraw`, `c_baseentity.cpp:1450` |

### The tests that would have gone red, in the order they would have paid

1. **Pass order, ours against Valve's.** This project draws world surfaces **and static props**
   together, then overlays, then models. Valve draws world **and overlays**, then props and models.
   So a prop is in the depth buffer before an overlay is drawn here and after it there — and with any
   bias on the overlay pass, the overlay wins against a pipe that is genuinely nearer. **This is
   B135.** Observable today: `MapWorld` has `Batches` and `Decals` and no third list, so prop
   geometry is provably inside the pass that precedes the overlays.

2. **Static props are renderables, not world geometry.** The merge is what makes (1) impossible to
   fix by reordering passes alone — the prop vertices are in the same buffer and the same batches as
   the surfaces. A test asserting that a prop's triangles land in a list distinct from the world's
   would be red now and green when the architecture matches.

3. **Depth-write behaviour per pass.** Nothing asserts which passes write depth. Overlays wrote it
   until tonight; B72 was a leaked read-only state in the model pass. Both are the same missing test.

4. **The height cut clips a world coordinate.** The shader clips `SV_POSITION.z`, which is NDC depth
   and equals height only under a top-down orthographic camera. Red under any perspective camera.
   **This is B136**, and `wpos` is already in the same shader struct.

5. **A depth constant is meaningless without its format.** `D24_UNORM` scales `DepthBias` by a fixed
   `1/2^24`; `D32_FLOAT` scales it by a data-dependent factor. A test pinning the buffer format
   beside any test that pins a bias constant. **This is D48**, found only because the constant
   misbehaved.

6. **One owner per render state.** `SetDecalBias` disposed and replaced the rasteriser state at map
   load, so every experiment that edited the constant where it is *created* measured nothing — zero
   and Valve's `-262144` produced identical pictures because neither was ever in effect. A test that
   the state a pass uses is the state that was built for it would have caught it in seconds.

### The general lesson for this project's conformance suites

**They measure what the engine *is*, and not what the renderer *does with it*.** `SdkCoverageTests`
generates a denominator of 489 shader parameters, 66 lumps and 54 studio structures, and every
hand-written suite checks a value against a citation. None of them describes a frame: which passes
exist, in what order, writing and testing what.

That is the gap B135 fell into, and it is where the next conformance tests belong.

---

## Audit: 107 conformance tests assert nothing about this project

**Counted 2026-08-21, after the owner named the flaw:**

> "the conf tests have to test our code against valves or its really not testing anything because im
> pretty sure valve tested their code themselves, a lot, so us retesting the unchanging sdk is
> worthless."

Measured by import: a file that imports `Tf2DemoSalvage.SdkReference` and **no** production
namespace cannot be comparing anything of ours.

| | files | tests |
|---|---|---|
| assert only against the SDK | **29** | **107** |
| SDK **and** our code | 38 | — |
| our code only | 364 | — |

An SDK checkout does not change. Valve tested that code. So those 107 can fail for exactly two
reasons — the checkout moved, or the grep was wrong — and neither is a fact about this renderer.

**This is why none of them caught B135.** Four divergences in the overlay path, and the suite that
exists to catch divergence was asserting that Valve's own file still says what it says.
`ScenePassOrderConformanceTests` states the limitation in its own remarks — *"it cannot go red on
ours"* — and was committed that way.

### What a conformance test has to look like instead

`OverlayOcclusionRenderTests` is the pattern: **Valve's rule is the citation, our pixels are the
assertion.** It renders a wall, a marking on it, and something in front, then names which surface won
each pixel by its colour.

Getting it to measure anything took four corrections, all of them classic:

1. **Wrong instrument.** First version asserted the centre pixel was non-black — "something drew",
   where the variable is "which thing drew". Passed identically with the defect restored.
2. **Wrong fixture.** Hand-built quads wound anticlockwise, which the now-culled overlay pass
   discards, so the marking never drew. The CONTROL caught it.
3. **Wrong material.** Depth state is per material now, so a marking whose material lacks `$decal`
   correctly gets the opaque state and loses to its own wall. The fixture has to use a material the
   map really declares as a marking.
4. **Effect size below resolution.** The occluder was placed 0.4 in front of the marking while the
   bias under test is 0.0156 — far beyond its reach, so both arrangements drew the same picture. The
   gap has to be smaller than the bias for the two to differ at all.

Only after the fourth did it go red under sabotage. **Three of those four produced a green test that
proved nothing**, which is the same failure the 107 have, arrived at from a different direction.

### The work this implies

Each of the 29 files should either compare an SDK-derived value against **ours**, or be re-stated as
what it actually is — a gap marker, which is a different thing with a different job (D45). Not
attempted here; it is a sweep, and it wants doing with the list above rather than opportunistically.
