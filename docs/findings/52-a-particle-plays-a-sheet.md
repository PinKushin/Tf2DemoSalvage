# 52 — A particle plays a sheet, and the format is measured while the clock is published

**A smoke puff is not a texture, it is a frame of one.** `effects/smoke/smokelit.vtf` is 512×256 and
holds five 128×128 tiles; a rocket trail's particles each play a five-frame animation across them,
crossfading between consecutive frames, on one of four sequences drawn at birth. Every one of those
four facts was absent from this project until 2026-09-09 — the renderer stretched the whole texture
over every particle, so a trail was four columns of smoke drawn identically on every puff (B373).

*Evidence class: mixed, and marked per claim. The container and the clock are read from published
source; the sheet payload is measured on shipped files with a control; the random table's CONTENTS
are the one interpolation and are flagged where they occur.*

---

## `CSheet` is forward-declared and defined nowhere

*Read from published source.* `src/public/particles/particles.h:41` is the whole of it:

```cpp
class CSheet;
```

The manager that owns them is declared and not defined either — `FindOrLoadSheet( IMaterial * )` at
`particles.h:375`, a `CUtlStringMap< CSheet* >` cache at `:465`, and a `CUtlReference< CSheet >` on
the definition at `:1244`. So the SDK says a sheet EXISTS, says which material it hangs off, and
never says what is in it.

**The container around it is published in full.** `src/public/vtf/vtf.h` gives a 7.3 header a
`numResources` count followed by entries of

```cpp
struct ResourceEntryInfo { unsigned int eType; unsigned int resData; };
```

with the sheet's id `VTF_RSRC_SHEET = MK_VTF_RSRC_ID( 0x10, 0, 0 )`. **The high byte of `eType` is
FLAGS**, and `RSRCF_HAS_NO_DATA_CHUNK` (`0x02 << 24`) means `resData` IS the data rather than an
offset to it. `smokelit` carries two such resources — its CRC and its LOD clamp — so a reader that
follows every `resData` as an offset walks into the pixel data on two of five entries.

**Two wrong offsets came first, and both reported plausibly.** `numResources` was read at `0x4C` and
answered "0 resources" for a file that has five; the hex dump of `0x38`–`0x58` settled it at `0x44`.
And the material's own name was used to find the texture: `effects/rocketrailsmoke.vmt` is a
`SpriteCard` whose `$basetexture` is `effects/smoke/smokelit` — a different folder — so asking for a
`.vtf` beside the `.vmt` reported a texture that ships as absent.

## The payload, measured, with tiling as the control

*Measured on `materials/effects/smoke/smokelit.vtf` through `dotnet run --project
tools/Tf2DemoSalvage.Probe -c Release -- particles sheet`.*

```
int32 size, int32 version, int32 sequenceCount
  per sequence: int32 id, int32 clamp, int32 frameCount, float totalTime
  per frame:    float duration, then FOUR sets of (u0, v0, u1, v1)      // stride 68
```

**The control is that the frames tile.** Read that way, `smokelit`'s twenty frames land on exact
128-pixel boundaries of a 4×2 grid, and its four sequences are four different PERMUTATIONS of the
same five tiles:

```
sequence 0: (1,1)  (129,1)  (257,1)  (385,1)  (1,129)
sequence 1: (385,1) (129,1) (257,1)  (1,1)    (1,129)
sequence 2: (257,1) (1,129) (385,1)  (1,1)    (129,1)
sequence 3: (385,1) (1,129) (129,1)  (385,1)  (257,1)
```

A wrong structure or a wrong stride does not produce that. It produces overlapping rectangles,
coordinates outside 0..1, or floats of 3e+38 — all of which were seen while the offsets were wrong.
Four permutations of one five-tile set is not a shape a misread can fake.

**The differential control is a different file read by a different reader.** `particles/rockettrail.pcf`
declares a `Sequence Random` initializer with `sequence_min = 0` and `sequence_max = 3`. Four
sequences in the texture, drawn 0..3 by the definition: two independently measured answers agreeing.

**Four coordinate sets per frame, not one**, and that is why the stride is 68 rather than 20. The
vertex format says why (`src/materialsystem/stdshaders/spritecard.cpp:271`): texcoord 0 is "sheet
bounding uvs, frame0", 1 is "frame 1", 4 is "texture 2 bounding uvs", 5 and 6 are a second sequence.
Only the first set is read here, and that is a stated limit rather than a finished job.

### The wrong turn worth keeping: an instrument's cap read as the format's

The first probe printed `Math.Min(frames, 4)`, and the header said `frames 5`. That mismatch was
written down as an open question about the FORMAT — whether `frames` counted something else — when
the fifth frame was simply not printed. It sits at (1,129)–(128,256), the first tile of the lower
row, exactly where a 4×2 grid puts it. The cap was in the probe the whole time
(`docs/memory/instrument-bugs-outnumber-decoder-bugs.md`).

## The clock is published, and it is not a life fraction

*Read from shipped data — `particles/rockettrail.pcf`, through the same probe.* Its
`render_animated_sprites` declares:

```
use animation rate as FPS   = 1
animation rate              = 3
animation_fit_lifetime      = 0
```

So a particle's frame is **`age × 3`, wrapping** — about 1.7 loops of a five-frame sequence across
the 0.8–1.2 second life the definition gives it. The reading that had been written into this
project's own comments first, before the file was asked, was `animation_fit_lifetime` — one sequence
stretched across a life. That would have played the five frames once, slowly, and looked like a
morph rather than billowing smoke. **The `.pcf` is shipped data and it simply says which**
(`docs/memory/shipped-data-settles-what-closed-code-cannot.md`).

## The crossfade is not optional

*Read from published source.* `spritecard.cpp:143`:

```cpp
if ( !params[BLENDFRAMES]->IsDefined() )
    params[ BLENDFRAMES ]->SetIntValue( 1 );
```

and `rocketrailsmoke.vmt` is four lines that do not mention it, so it is on. The pixel shader samples
both frames and mixes (`spritecard_ps2x.fxc:77`):

```hlsl
float4 blended_rgb = lerp( baseTex0, baseTex1, i.blendfactor0.x );
```

**Which is why the vertex carries two texture coordinate sets and a blend, and why detail sprites
now carry them too.** A detail sprite passes the same coordinates twice with a blend of zero, making
the lerp an identity — one sprite pipeline rather than two that must agree about blending.

## Sequence and lifetime are drawn, and the engine's draw needs no seed

*Read from published source.* `particles.h:1779`:

```cpp
float flRand = s_pRandomFloats[ ( m_nRandomSeed + nRandomSampleId ) & RANDOM_FLOAT_MASK ];
flRand *= ( nMax + 1 - nMin );
int nRand = (int)flRand + nMin;
```

A 4,096-entry table, and the sample id is **the particle's own id plus a per-operator offset** —
`s_pRandomFloats[ ( nOfs + ParticleID.m_nValue[0] ) & RANDOM_FLOAT_MASK ]` at `:1801`. A given
particle's draw is a pure function of its id: independent of frame rate, of how many particles came
before it, and of the order operators ran in.

**That resolves a trade this project thought it had to make.** D136 asks that replaying a demo
produce the same picture, and the code that read `Lifetime Random` had taken the MIDPOINT of
`lifetime_min` and `lifetime_max` to honour it, justified in a comment saying the two bounds were
equal. They are not — `rockettrail` declares 0.8 and 1.2 — so every particle in a trail lived exactly
one second and the plume died all at once. Valve's own scheme is deterministic, so drawing properly
and replaying identically were never in conflict; the divergence was bought for nothing.

*Interpolated, and this is the one:* the CONTENTS of `s_pRandomFloats` are filled in code that ships
only as a binary. This project's table is its own — uniform on [0,1) and stable across runs, which is
every property the arithmetic depends on, but not float-for-float Valve's. It can change which puff
got which lifetime; it cannot change the distribution or the reproducibility. Falsifiable: `particles.lib`
ships in the SDK at `src/lib/public/x64/` (`docs/memory/absent-from-the-sdk-is-not-unreadable.md`).

## The sheet's own presence was moving the pixels

*Measured, and it is the largest thing this finding turned up.* With every parameter above correct,
the trail still drew as saturated rainbow noise. It was not the sheet, the frames, the blend or the
clock — it was that **the reader computed where the image data was instead of asking**.

`VtfTexture` located pixels at `headerSize + thumbnail`, which is the 7.2 layout. From 7.3 the header
is followed by a resource table and the images are two entries in it; **whatever else the file
carries sits between them.** `smokelit`'s sheet is 1,432 bytes at offset 184 and its pixels start at
1,620, so the computed offset landed on the sheet — and the decoder read a table of UV floats as DXT
blocks.

**It survived because it produced a picture, and nearly the right one.** VTF stores mips *smallest
first*, so the largest mip is at the end of the file and a 1,436-byte shift moves it by a fraction of
its own size. The five puffs kept their silhouettes and filled with rainbow speckle.

```
before   mean channel spread 112.5 over visible pixels, largest 255
after    mean channel spread  10.8 over visible pixels, largest  25
census   288 of 34,246 shipped textures, every one an effect
```

**The wrong turn worth the most.** Rainbow from a DXT5 file reads as a DXT5 fault, so the DXT decoder
was read line by line (correct), then `BlockFormat` and `BlockPitch` (correct), then the upload, then
`SpriteCard`'s combos. The tell was there the whole time and was read backwards: **DXT1 textures
decoded right and DXT5 ones did not** — which was never about DXT. Sheet-carrying textures are DXT5
because they are effects and effects have alpha. A correlation in the sample was taken for the
mechanism.

**What ended it was looking at the picture rather than at a statistic.** The mean said `(78 68 68)`,
near-grey — which is exactly what a rainbow averages to. Printing the texture as a character grid
showed scattered R/G/B letters where smoke should be smooth shading, and the per-pixel channel
*spread* put a number on it. Full account: **B374**.

## Three initializers the file declared and nothing read

*Measured on `rockettrail.pcf`.* Found while chasing the above, each with no reader anywhere:

| initializer | the file says | we did |
|---|---|---|
| `Color Random` | `(247 194 117)` to `(251 142 0)` — firelight | drew full white |
| `Alpha Random` | `96..128` **of 255** | spawned at 1.0 |
| `Alpha Fade and Decay` | scales the spawn alpha | replaced it |

**The third is settled by the file rather than by taste.** `rockettrail` declares `Alpha Random` and
`start_alpha 1` together; read as an absolute, the operator overwrites the initializer on the first
frame and `Alpha Random` is dead — on this effect and on every other that pairs them. Read as a
scale, both mean something. It is the same rule `RadiusAtBirth` already carries, and
`GetReadInitialAttributes` (`particles.h:602`) is where it comes from.

**And `Color Fade` is why a rocket trail looks grey at all:** it takes the tint to `(195 190 202)`
within the first tenth of a life. Ours was lerping there *from white*, because `Color Random` had
never run — so the one operator that was implemented had been quietly given the wrong starting point.

## A census closed one open item without writing any code

*Measured — `particles materials`, over every shipped `.vmt`.*

```
697 SpriteCard materials of 25,750 shipped
  $texture2  0     $additive2ndtexture  0     $ramptexture  0     $extractgreenalpha  0
  $dualsequence  1
  $additive  304   $addself  41    $addoverblend  3    $blendframes  100
```

**Three of a frame's four coordinate sets are read by nothing**, so implementing them would be dead
code — the `$modblend` case exactly (`docs/findings/12-shader-parity.md`), where the correct
implementation of a shipped-but-unread parameter is nothing at all. `$dualsequence` is one material
in the whole game: a named gap rather than an open question.

**The same census turned an assumed non-issue into a real one.** `$additive` is set by 304 of 697 —
44% — and this project drew every particle translucent. `spritecard.cpp:255-270` picks between three
blends, and the ORDER matters in a way that is easy to get backwards:

```cpp
if ( bAdditive2ndTexture || bAddOverBlend || bAddSelf )
    EnableAlphaBlending( SHADER_BLEND_ONE, SHADER_BLEND_ONE_MINUS_SRC_ALPHA );
else if ( IS_FLAG_SET(MATERIAL_VAR_ADDITIVE) )
    EnableAlphaBlending( SHADER_BLEND_SRC_ALPHA, SHADER_BLEND_ONE );
else
    EnableAlphaBlending( SHADER_BLEND_SRC_ALPHA, SHADER_BLEND_ONE_MINUS_SRC_ALPHA );
```

`$addself` and `$addoverblend` **outrank** `$additive`, so testing the obvious one first gives 41
materials the wrong blend and none of them throws.

## The plume, and the cube root that makes it one

*Read from published source.* `Position Within Sphere Random` was unimplemented, so every particle
was born at the emitter and a trail was a line. Valve's sampler is published in full —
`mathlib_base.cpp:4203`, citing *Graphics Gems III*:

```cpp
float flPhi    = acos( 1 - 2 * u );
float flTheta  = 2 * M_PI * v;
float flRadius = powf( w, 1.0f / 3.0f );
```

**The cube root is the whole of it.** Uniform in VOLUME means half the points lie beyond
r = 0.5^(1/3) = 0.7937, because that radius encloses half the ball; a uniform radius puts only ~21%
there and spawns a dense core with a thin halo. So the test asserts the distribution, not the bound —
a bound-only assertion passes with the cube root removed.

**Velocity has to be written as a position.** Verlet stores none: `MovementBasic` carries
`position - previous` forward as the step's displacement, so a spawn speed IS how far behind the
particle its previous position is placed. That is why `Spawn` needs the step, exactly as the engine's
initializers read the collection's own.

*Measured after:* spread across the flight axis went from 0 to **4.306 units**, and rotation from a
single value to **−44.6° … −0.8°** — which is `rotation_initial -45` plus `0..45`, read straight back
out of the file.

## A rocket is three systems, and the draw call is per material

*Read from published source.* A child particle system shares the parent's control point, and the
engine says so twice — `SetControlPoint` and `SetControlPointOrientation` each walk `m_Children` and
pass their values down (`particles.h:1595`, `:1629`). A parent is also not finished while a child
still has particles: *"make sure all children are finished"* (`:1630`).

```
rockettrail        effects/rocketrailsmoke        translucent   45 particles
  rockettrail_burst  effects/brightglow_y_nomodel     additive   27
  rockettrail_fire   effects/sc_brightglow_y_nomodel  additive   27
```

**The simulation was the easy half.** The hard half was that three systems means three materials,
and `Device3D.SetParticles` took exactly one sheet — a limit its own remarks had already written
down as *"a batching question to answer when a second effect exists rather than now"*. A second
effect existed, so it now takes a list of batches, one per material, with each material's texture
uploaded once for the map's lifetime rather than per frame.

**Two fixes that only pay off together.** Both child materials are `$additive 1`. Had children been
added before the blend selection, they would have drawn as dark translucent patches over the rocket
instead of glows — visibly wrong in a different way, and easy to blame on the children rather than
on the blend.

**A cycle terminates rather than being detected**: a name is removed from the lookup once used, so a
`.pcf` naming itself costs one level of recursion instead of the stack. That file is input this
project does not control.

## What is still not done

- **`ROTATION` is decoded, stored and ignored.** The engine passes it in texcoord 2 beside the frame
  blend (`spritecard.cpp:271`) and rotates the card; `rockettrail` sets `rotation_initial -45` with
  an offset of 0..45, so every puff should be tilted differently and none of them is.
- **The blend MODE is the detail pass's**, not the one `SpriteCard` picks from `$additive` and the
  rest (`spritecard.cpp:255-270`).
- **Only the first of four coordinate sets is read**, so `$additive2ndtexture` and second-sequence
  materials would draw their first sequence alone.
- **`$dualsequence` is unimplemented**, and exactly one shipped material sets it — a named gap
  rather than an open question.
## The comparison was made, and it found something

*Measured.* B161's tool now drives real TF2 to a chosen tick and dumps a frame, so ours and the
engine's can be put side by side at `cp_process_f12`, first person, rocket in flight.

**What agrees:** the smoke is grey, the fire at the head is orange, and the blending reads the same.
Everything this finding is about — the sheet, the frame clock, the crossfade, the tint, the alpha,
the additive children — survives the comparison.

**What does not:** TF2's trail stretches back down the rocket's whole flight path; ours is one dense
puff at the rocket. The cause is not in any of the above — **our particles advance once per rendered
FRAME rather than per demo tick**, so a still at 294 fps steps the trail three hundred times a second
with the emitter frozen at one point. And a seek leaves an effect with no history at all, where TF2
gets one by restarting the demo and fast-forwarding through it. Filed as **B375**, with a first
attempt written, reverted and stashed because it made the picture worse rather than better.

**That is the whole argument for the golden comparison.** Six separate divergences in this finding
were caught by reading files and the engine. Those could not have been: every parameter was right,
every test green, and the picture still wrong — because the faults were in *when* the simulation
runs and in a lookup keyed on something that does not identify the thing, neither of which any
amount of reading the `.pcf` would have shown. Full account in **B375**.

**And a caution the same comparison earned.** A seventh divergence — "TF2's fire sits closer to the
rocket head" — was asserted from two captures taken at different moments, from different cameras, of
different rockets, and then withdrawn: the children declare a 0.2-second lifetime against the
smoke's 0.8–1.2, so fire in the near fifth of the trail is what the file asks for, and the simulator
measures 27 alive against the 25.6 that rate and lifetime predict. **A picture is assertable only
when the two pictures are comparable**, and `spec_next` attaching TF2 to an arbitrary player means
these were not. Naming the player on both sides is the outstanding work on the capture tool.
