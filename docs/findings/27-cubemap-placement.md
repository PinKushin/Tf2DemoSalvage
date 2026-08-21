# Cubemap placement — a struct that is bigger than its declaration

`LUMP_CUBEMAPS` is the smallest lump this project has read: an array of four fields, three of them
the same type. It took two wrong answers to get right, and the first one passed ten tests.

## What the lump is (evidence: read from published source)

`dcubemapsample_t`, `bspfile.h:992`, is the whole of it:

```cpp
struct dcubemapsample_t
{
    DECLARE_BYTESWAP_DATADESC();
    int           origin[3];   // position of light snapped to the nearest integer
                               // the filename for the vtf file is derived from the position
    unsigned char size;        // 0 - default
                               // otherwise, 1<<(size-1)
};
```

Two of the four lines of comment are load-bearing, and both invert an obvious reading.

**A cubemap has no name. The position is the name.** That comment is the specification for
resolving `$envmap "env_cubemap"`: a material says "the nearest one", and the renderer has to find
it by position and then construct a filename from those three integers.

**`size` of 0 means the default, not a size.** `DEFAULT_CUBEMAP_SIZE` is 32 (`vbsp/cubemap.cpp:280`).
Passing the escape value through the shift anyway is not subtly wrong — `1 << (0 - 1)` in C# is
`1 << 31`, because the shift count is masked to five bits. The first run of this reader reported a
cubemap of 1,073,741,824 pixels a side.

## The stride, which is not 13

Three 4-byte ints and one `unsigned char` is thirteen bytes of content, and thirteen is what this
reader was written to. It is wrong. C++ pads a struct to its own alignment — four, from the ints —
so `sizeof(dcubemapsample_t)` is **16**, with three unnamed bytes at the end, and the lump is
written with

```cpp
SwapLumpToDisk<dcubemapsample_t>( LUMP_CUBEMAPS );    // bsplib.cpp:4891
```

which writes `sizeof` per element. The padding is on disk.

The `DECLARE_BYTESWAP_DATADESC()` at the top of the struct is a red herring worth ruling out
explicitly, since it looks like it might add a vtable or a member. It expands to
`DECLARE_SIMPLE_DATADESC()`, which is `static` members and friend templates only
(`datamap.h:318`) — no instance data.

**The arithmetic settles it without any of that reasoning.** On `cp_process_final` the lump is 688
bytes. 688 = 43 × 16 exactly, and is not divisible by 13. One division would have answered the
question before a line of code was written — the same move as
[length arithmetic identifies a layout](../memory/length-arithmetic-identifies-a-layout.md), not
made here until after the fact.

## How it failed, and why ten tests said it hadn't

The failure mode is the interesting part. Reading a 16-byte record at 13 does not produce garbage
from the start; it produces **one correct answer and then drift**, because each subsequent record is
composed from the tail of one and the head of the next:

```
43 cubemaps on cp_process_final
  (0, 0, 608)                                    <- correct
  (-2147483648, -2147483642, 1879048200)         <- not
```

A first entry that is plainly plausible is exactly the shape that stops someone looking further.

Ten synthetic tests passed against this. They covered the position, the sign, both ends of the size
range, the escape value, an absent lump, and a truncated record. Several used three entries
specifically to catch a stride error, and the file's own remarks said so — *"a stride bug is
invisible at one"*.

They could not catch it, because **the fixture builder was 13 bytes wide too**. The tests and the
reader were built from one belief, so the whole suite was a single hypothesis wearing ten
assertions. This is [fixtures are the weak point](../memory/fixtures-are-the-weak-point.md) in its
purest form: not a fixture with a bug in it, but a fixture that is a faithful expression of the bug.

What falsified it was one test reading a map vbsp actually compiled. Not a count — a count of 52 is
as plausible as 43 — but **whether the positions are somewhere a map could be**. A stride error puts
coordinates outside Source's own ±16384 world limit, and a correct one cannot, because vbsp took
these positions from entities the compiler had already bounds-checked.

That is the general form worth keeping: when the synthetic data is authored by whoever authored the
reader, the assertion has to be against a property the real data must satisfy and the wrong reading
cannot.

## The filename derivation (evidence: read from published source)

vbsp builds it, so this is transcription (`vbsp/cubemap.cpp:508-525`):

```cpp
const char *pSeparator = bMaterialName ? "_" : "";
int nLen = Q_snprintf( pBuffer, nMaxLen, "maps/%s/%s%s%d_%d_%d", info.m_pMapName,
    pMaterialName, pSeparator, info.m_pOrigin[0], info.m_pOrigin[1], info.m_pOrigin[2] );
...
BackSlashToForwardSlash( pBuffer );
Q_strlower( pBuffer );
```

Called two ways, and the difference is one character:

| Call | Separator | Result |
|---|---|---|
| `GeneratePatchedName( pMaterialName, info, true, … )` | `_` | `maps/<map>/<material>_<x>_<y>_<z>` — the patch **VMT** |
| `GeneratePatchedName( "c", info, false, … )` | *(none)* | `maps/<map>/c<x>_<y>_<z>` — the baked **VTF** |

This project had already seen the material form without reading the lump: `MapAssetsTests` records
`maps/cp_process_final/icarus/glasschrome001_544_1952_929.vmt` in the map's own pakfile, and noted
at the time that "the numbers are the cubemap's position". Copying that shape across to the texture
gives `c_544_…`, which exists nowhere.

The trailing `Q_strlower` matters here in a way it would not on a filesystem: these archives are
matched by name rather than by an OS, so the case is ours to get right.

**Verified against the real thing, which is the only reason to believe any of it:** all 43 derived
names resolve inside `cp_process_final`'s 3,413-entry pakfile. A wrong separator, a wrong case or a
dropped sign finds zero, so this is one assertion that cannot be nearly right.

That test's own first version looked in the **game's** archives and found zero of 43 — which was a
fact about the instrument, not about the naming, and for a few minutes looked like the naming was
still wrong. A baked cubemap is baked from *this map's* geometry and exists nowhere but this map's
pakfile. Compare
[instrument bugs outnumber decoder bugs](../memory/instrument-bugs-outnumber-decoder-bugs.md).

## What is measured on cp_process_final

- **43 cubemaps**, every one inside the world bounds.
- **Every one at the default size**, 32 — so `size` is 0 in all 43 records, and the escape value is
  not a corner case on this map but the only case. A reader that got it wrong would produce
  1,073,741,824 forty-three times.
- **43 of 43 textures present** in the pakfile under their derived names.

## The assignment does not need to be computed

The obvious design for `$envmap` is a nearest-by-position search at load: read the 43 placements,
and for each surface find the closest. **vbsp already did it, at compile time.**

`Cubemap_CreateTexInfo` (`vbsp/cubemap.cpp:600`) clones the face's texdata under a patched material
name carrying the cubemap's origin, writes a Patch VMT whose `$envmap` is that cubemap's baked
texture, and repoints the texinfo at the clone. So a face's material *already names the exact
cubemap it reflects*, and this project reads that name for every surface as it is.

Measured, and it is two independent recordings agreeing: **51 patched materials on
cp_process_final, all 51 naming one of the 43 placements**, and all 51 with the position in the
material's name matching the position in its `$envmap` value. Those come from different parts of the
compiler and cross-check each other without reference to our reader at all.

**Brush faces are patched; static props are not**, because `Cubemap_CreateTexInfo` works on texinfo
and a prop has none. 26 materials on this map still read the literal `env_cubemap` and every one is
a `models/props_*` — those the engine binds at runtime by proximity to the prop's origin. The test
asserting this started life as "no resolved material still asks for `env_cubemap`", which is simply
false; the assertion was wrong, not the data.

## The patch that never applied

Chasing that produced the largest finding of the three. **Every `Patch` material this project has
ever resolved was a no-op.**

The parser kept keys only at depth 1, which is correct and deliberate — a `Proxies` block carries
its own `$basetexture` naming the texture a proxy animates, and taking that as the surface's draws
the wrong picture. But a patch's overrides are at depth **2**:

```
"patch"
{
	"include"		"materials/ICARUS/GLASSCHROME001.vmt"
	"replace"
	{
		"$envmap"		"maps/cp_process_final/c1568_1728_976"
	}
}
```

So `Parse` returned a material carrying `include` and nothing else. `ApplyPatch` drops `include` and
overlays the rest — and the rest was empty, so it overlaid nothing and the merged material was the
included stock one, exactly. On this map that is 51 materials.

**`ApplyPatch`'s own documentation asserted the flattening**: *"this reader flattens those into the
top level, so applying the patch is a straight overlay"*. It did not. Nothing in `ApplyPatch` was
wrong — it faithfully applied what it was handed — which is precisely why the bug survived having a
test:

```csharp
VmtMaterial patch = Parse(
    "\"Patch\"\n{\n" +
    "  \"include\" \"materials/models/base.vmt\"\n" +
    "  \"$color\" \"[1 0 0]\"\n}\n");
```

The keys are at the patch's **top level**, a shape real VMTs never use. Third time in this session
that a fixture written from the same belief as the code confirmed it — after the 13-byte cubemap
record and the census's missing third axis. The pattern is now specific enough to name: **when a
format has a real-world example available, put the real one in the fixture.** `VmtPatchBlockTests`
uses the file above byte for byte.

The fix keys on depth *and* name, not name alone: a `replace` block nested inside `Proxies` is a
proxy's, and matching the name anywhere would swap one bug for a rarer one. There is a test for that
too, because a fix whose failure mode is "works on everything I tried" needs the negative case
written down.

**What it did not change:** material resolution is 211 of 211 either way. That was checked by
reverting the fix rather than assumed — the surrounding comment says 208 of 211, and it would have
been easy and wrong to claim the improvement.

## The baked texture, measured because the loader is not published

`src/vtf/` is not in the SDK, so the question that decides the whole read — how many faces are on
disk — cannot be answered by reading Valve's loader. The header states an answer:

```cpp
enum CubeMapFaceIndex_t
{
    CUBEMAP_FACE_RIGHT = 0, LEFT, BACK, FRONT, UP, DOWN,
    CUBEMAP_FACE_SPHEREMAP,          // This is the fallback for low-end
    // NOTE: Cubemaps have *7* faces; the 7th is the fallback spheremap
    CUBEMAP_FACE_COUNT
};
```

Seven — but that comment is old, the spheremap fallback served hardware that has not shipped in
twenty years, and a header comment is not a statement about what a 2026 TF2 map contains. So the
arithmetic settles it, and it is exact:

```
32x32 format 24 mips 6 frames 1 flags 0x0000600c header 96 file 76536
76440 image bytes / 10920 per face = 7.000 faces
```

`10920 = 8 × (32² + 16² + 8² + 4² + 2² + 1²)`. **Seven, on all 43 cubemaps.** Six would leave a
remainder, so this is a division that cannot come out right for both answers — which is the whole
reason it is worth writing as a test rather than as a note.

Three things fell out of the same measurement.

**The VTF is 32×32 and the lump said `size` 0.** A third independent recording of one number: the
placement's escape value resolves to `DEFAULT_CUBEMAP_SIZE` 32, and the texture baked from that
placement declares 32 in its own header. Every one of the 43 records carries 0, so an implementation
passing it through `1 << (size - 1)` claims 1,073,741,824 while the file plainly says 32.

### A prerequisite that was not one — and the instrument again

**First conclusion, and it was wrong:** every baked cubemap is ImageFormat 24, `RGBA16161616F`, four
half-floats per texel — so half-float decode is a hard prerequisite, `VtfTexture` throws *"VTF pixel
format 24 is not supported"*, and no shader work reaches a picture until it lands.

That was written down, committed, and is a statement about **the probe's own file preference**
rather than about the format. The reader tried `c<x>_<y>_<z>.hdr.vtf` first and fell back to the
plain name — so of course every result was HDR. It never asked whether the other file existed.

It does. **vbsp bakes both, and cp_process_final carries 43 of each.** The engine samples whichever
matches the mode it is running in, so which one to read is a *choice*, not a fallback chain — and
this project draws LDR deliberately ([24](24-reference-capture.md)).

```
first LDR: 32x32 format 13 mips 6 frames 1 flags 0x0000400c header 96 file 4968
```

**Format 13 is DXT1, which `VtfTexture` already decodes completely.** The same arithmetic confirms
the same shape: DXT1 over a 32×32 chain is `512 + 128 + 32 + 8 + 8 + 8 = 696` bytes a face, and
`4872 / 696 = 7`. Seven faces, as before.

So there is no half-float prerequisite. The reflection needs **face iteration and nothing else** —
a substantially smaller piece of work than the one that had just been scheduled.

Two things worth keeping from this. The wrong answer was *measured*, on real bytes, and was still
wrong — a measurement is only as good as the question, and "what format are the cubemaps" quietly
became "what format are the files I chose to open". That is the third instrument bug in this one
feature, after the game-archives lookup and the underscore split. And the cost of asking one more
question before building was one test; the cost of not asking would have been a half-float decoder
that nothing needed.

The HDR files remain the right source the day this renderer becomes HDR — that conformance gap is
already recorded as an open skip — so both facts are held by assertions rather than one replacing
the other.

Valve's own axis note is filed for whoever wires the sampler up, because it inverts the obvious
mapping onto D3D's `+X −X +Y −Y +Z −Z`:

```cpp
CUBEMAP_FACE_BACK,	// NOTE: This face is in the +y direction?!?!?
CUBEMAP_FACE_FRONT,	// NOTE: This face is in the -y direction!?!?
```

The punctuation is Valve's. Not resolved here; recorded so it is not rediscovered.

## Face iteration, and a test that could not fail

Reading one face is two changes: the face count multiplies every mip's stride, and it selects within
the chosen mip.

```csharp
for (int smaller = mipCount - 1; smaller > level; smaller--)
    at += SizeOf(format, MipSize(width, smaller), MipSize(height, smaller)) * frames * faces;

at += face * bytes;
```

The `* faces` on the mip skip is the whole difficulty. Without it every offset on a 32×32 cubemap
lands **1,104 bytes early** — and that is the interesting number, because 1,104 bytes early is still
*inside the file*. The data decodes. It yields six different-looking images. Nothing throws.

**Five tests were written against real baked cubemaps and all five passed with that bug applied.**
301 faces decoded across 43 files, every one the right size, six distinct images per cubemap, the
spheremap distinct from all of them — every assertion satisfied by a reader reading the wrong bytes.

The one that looked strongest was the worst of them:

> **The decisive assertion is that the last face ends exactly at the end of the file.** Any error in
> the mip stride, the face stride, or the face count moves that boundary.

True, and useless, because it computed the boundary *from the header* and never asked the reader
where it had read. It tests the understanding of the format, not the implementation of it. That is
the *wrong instrument* case: measuring a faithful proxy for something that is not the variable.

What works is putting the reader on the boundary and then moving it:

```csharp
Should.NotThrow(() => VtfTexture.Decode(file, face: 6));

Should.Throw<InvalidDataException>(() => VtfTexture.Decode(file[..^1], face: 6));
```

If the last face genuinely ends at the last byte, removing one byte makes it unreadable. If the
offsets are short, the truncated file still satisfies them. The pair pins the boundary *through* the
code. Under the sabotage it is the only test in the file that goes red.

Worth stating generally, because the instinct on finding an insensitive test is to strengthen the
assertion and that was not the fix here: **an assertion computed alongside the code rather than
through it can only ever check your arithmetic against itself.** The independent computation is
still worth keeping — it catches a misunderstanding of the format — but it is a different
experiment, and it is now named as one.

## The face names are wrong; the face order is not

The one thing filed as unresolved above turns out to be answerable from the same header, and the
answer is the reassuring one.

Valve's face enum is annotated in visible bafflement:

```cpp
enum CubeMapFaceIndex_t
{
	CUBEMAP_FACE_RIGHT = 0,
	CUBEMAP_FACE_LEFT,
	CUBEMAP_FACE_BACK,	// NOTE: This face is in the +y direction?!?!?
	CUBEMAP_FACE_FRONT,	// NOTE: This face is in the -y direction!?!?
	CUBEMAP_FACE_UP,
	CUBEMAP_FACE_DOWN,
```

The punctuation is theirs, and it is the tell. Source's convention is X forward, Y left, Z up — so a
face called BACK pointing at `+y` looks like a defect in the format, and anyone mapping these names
onto D3D's `+X −X +Y −Y +Z −Z` has to guess.

**The enum eleven lines below settles it**, and nobody has to guess:

```cpp
enum LookDir_t
{
	LOOK_DOWN_X = 0,
	LOOK_DOWN_NEGX,
	LOOK_DOWN_Y,
	LOOK_DOWN_NEGY,
	LOOK_DOWN_Z,
	LOOK_DOWN_NEGZ,
};
```

Same length, same positions, and the two entries Valve annotated agree exactly: BACK at index 2 with
`LOOK_DOWN_Y`, FRONT at index 3 with `LOOK_DOWN_NEGY`. The face order is plainly `+X, −X, +Y, −Y,
+Z, −Z`. **The names are wrong; the order never was.**

That is D3D11's `TextureCube` order exactly, so the upload is the identity for faces 0–5 and needs
no swizzle — provided the reflection vector is computed in Source's own space, which this renderer
does work in: its height cut reads `input.pos.z` as height, so Z is up and nothing has been
converted.

Two things this is worth as a method. **A confusing comment is a signal to look at what is declared
next to it**, not a reason to start guessing — the annotation and its resolution were eleven lines
apart. And *"no mapping is needed"* is exactly the kind of conclusion that gets quietly reversed
later by someone reading only the face names, so it is held by an assertion rather than a note.

The spheremap's position is asserted too, for a reason that is not obvious: uploading "the first six
in order" is also what a reader that never noticed the seventh face would do, and that reader is
right by accident only as long as the spheremap stays last. It is last — after DOWN, immediately
before the count — and now something says so.

## Drawn, and measured through the GPU

All of it landed. The census, which reported `$envmap` on 79 of 410 materials as the map's largest
unimplemented parameter, now reports 43 unimplemented with the largest at 66 — `$envmap`,
`$envmaptint` and `$basealphaenvmapmask` are gone from it. All ten of `EnvmapConformanceTests`
activated.

**The shader is the one part of this that map data cannot falsify**, and it turned out to be
measurable anyway. This project renders offscreen and reads pixels back, so the reflection can be
observed through the real pipeline:

| Material | Normal up | Normal side |
|---|---|---|
| 95, reflective | `(129, 115, 125)` | `(69, 68, 69)` |
| 0, matte | `(32, 29, 26)` | `(32, 29, 26)` |

**The discriminator is the surface normal**, because a reflection vector is the view direction
mirrored about it — two otherwise identical surfaces facing different ways sample different texels.
And the control is exact: a material with no cubemap comes back byte-identical, so nothing else in
the world path varies with the normal and the difference above *is* the reflection. Forcing the
envmap branch off reddens the first row and leaves the second untouched.

Two things nearly stopped this being measurable at all.

**The existing offscreen tests use an identity camera matrix, and an identity matrix has no eye
position.** Inverting it and taking the third row gives `w = 0` — parallel rays converging nowhere —
so `EyePosition` correctly reports no camera and the shader correctly skips the reflection. A test
written on the established harness would have measured nothing and passed. It needed a real
perspective camera.

**And a shader resource slot keeps whatever was bound last.** A material with no cubemap would have
sampled the previous material's, so the slot is set on every draw — null when there is nothing —
rather than only when there is something. The shader's guard is what stops the read; the binding is
what stops the staleness. Getting only one of those right produces reflections on matte surfaces
that follow draw order, which is about as hard to diagnose as this project's defects get.

**What is still not verified: whether it looks right.** A pixel that changes with the normal is
evidence that the cube is sampled, not that the picture is correct. Brightness, falloff and whether
a given wall reflects the room it is in are questions for someone looking at the screen.

B55 closed.

## The one case where the assignment DOES need to be computed (evidence: read, then interpolated)

The section above — *the assignment does not need to be computed* — is right about brushwork and was
quietly assumed to be right about everything. It is not, and the two shaders say so themselves.

`LightmappedGeneric`, which draws brush faces, refuses the literal outright
(`lightmappedgeneric_dx9_helper.cpp:83`):

```cpp
if( stricmp( params[info.m_nEnvmap]->GetStringValue(), "env_cubemap" ) == 0 )
{
    Warning( "env_cubemap used on world geometry without rebuilding map. . ignoring: %s\n", pMaterialName );
    params[info.m_nEnvmap]->SetUndefined();
}
```

That is the engine telling a mapper their map is stale: brush faces are supposed to have been
patched by vbsp, and one that was not reflects nothing. Which is exactly why the world path here
needs no search — the compiler already did it.

`VertexLitGeneric`, which draws models, has **no such block anywhere in the file**. It calls
`pShader->LoadCubeMap( info.m_nEnvmap, ... )` on whatever the material says. So on a model the
literal is not a leftover to discard; it is the request, and it resolves against whatever the engine
has bound as the local cubemap (`BindLocalCubemap`, `imaterialsystem.h:1200`).

**The reason a model cannot be patched is in vbsp's own structure.** `Cubemap_CreateTexInfo` clones a
*texinfo* and repoints a brush side at the clone. A model has no texinfo, so there is nothing for the
compiler to rewrite — the mechanism is not merely unused for props, it is inapplicable to them.

### Which cubemap "local" means, and where the published trail stops

`BindLocalCubemap` is an interface method. Every caller that picks the texture for a world model is
inside the closed engine; the published client tree binds one only in `basemodelpanel.cpp`, and binds
a fixed default. So the runtime rule is not readable.

What *is* published is Valve's nearest-cubemap rule, in the compiler
(`Cubemap_FindClosestCubemap`, `vbsp/cubemap.cpp:835`). Two passes:

```cpp
// Look for cubemaps in front of the surface first.
float flDist = vecDelta.NormalizeInPlace();
float flDot = DotProduct( vecDelta, pPlane->normal );
if ( ( flDot >= 0.0f ) && ( flDist < flMinDist ) )
...
// Didn't find anything in front search for closest.
```

**The first pass cannot apply to a model, and the source says why rather than leaving it to
judgement**: it needs `pPlane->normal`, the plane of one brush side, and the function returns -1
before doing anything when handed no side at all. A model is a few thousand triangles facing every
direction. So the second pass — nearest by straight-line distance — is the whole of the applicable
rule, which is also what the function reduces to for any surface with nothing in front of it.

**Flagged, because the two halves are different evidence.** The rule is *read from published source*.
That the engine applies this same rule at runtime for a model is *interpolated* from Valve applying
it at compile time to the same question. A leaf-based selection would agree with it everywhere except
near a leaf boundary; nothing here can distinguish the two. See `docs/DECISIONS.md` D44.

### Measured

- **43** placements baked into cp_process_final, **43** decoded.
- **29 of 413** materials ask for the literal `env_cubemap` — every model material that reflects,
  including `cap_point_base`, `cap_point_base_red` and `cap_point_base_blue`.
- Drawn offscreen at the two placements whose cubes differ most, the same model reads
  **(192, 168, 152)** at one and **(13, 3, 1)** at the other. With the search forced to ignore the
  model's position, both read (13, 3, 1).

## A draw with no vertex shader does not fail; it removes the device

Found while writing the offscreen test above, and it is a fact about D3D11 rather than about Valve.

`DrawModel` bound no shaders, input layout, topology or samplers. In an ordinary frame the world is
drawn first and leaves all of that bound, so a model draw inherited it and everything worked. The
test posed a model with no world, and the result was not an error at the draw call — it was

```
System.Runtime.InteropServices.COMException : The GPU device instance has been suspended.
```

thrown several calls later, out of the staging-buffer read-back. The stack trace names `PixelAt`,
which is the one function that had nothing to do with it.

**The same trap was live in the application**, not only in the test: `Draw` returns early when the
map has no geometry, so a frame with no map loaded would have issued model draws into an unbound
pipeline. Both paths now call one `BindPipeline`.

Worth generalising: **an API whose correctness depends on another call having happened first, with
nothing to enforce it, fails at the distance of whatever notices**. The offscreen path found it
because it was the first caller that did not satisfy the precondition by accident.
