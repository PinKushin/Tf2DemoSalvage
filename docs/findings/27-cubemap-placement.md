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

**Every baked cubemap is ImageFormat 24, `RGBA16161616F`** — four half-floats per texel, eight bytes.
That is the HDR pipeline, and it has to be: a reflection carries values above one, which an 8-bit
format cannot hold. This is the same reason `$color` is unclamped while `$alpha` is not
([26](26-material-modulation.md)).

**So half-float decode is a hard prerequisite, and it is not implemented.** `VtfTexture` throws
*"VTF pixel format 24 is not supported"*. No amount of shader work reaches a picture until that
lands — which is worth knowing before writing the shader rather than after, and is the kind of thing
that is cheap to measure and expensive to discover. There is an assertion holding that fact, so the
day it changes the test says so.

Valve's own axis note is filed for whoever wires the sampler up, because it inverts the obvious
mapping onto D3D's `+X −X +Y −Y +Z −Z`:

```cpp
CUBEMAP_FACE_BACK,	// NOTE: This face is in the +y direction?!?!?
CUBEMAP_FACE_FRONT,	// NOTE: This face is in the -y direction!?!?
```

The punctuation is Valve's. Not resolved here; recorded so it is not rediscovered.

## Still open

The lump is read and the assignment turns out to be free. What remains is the picture:

1. **Half-float VTF decode** (`RGBA16161616F`), which everything else waits on.
2. **Seven faces to six**, discarding the spheremap, with Valve's `+y`/`−y` note above resolved
   against D3D's face order.
3. **The shading**, specified by `EnvmapConformanceTests` — six of its eight assertions still
   skipped.

B55.
