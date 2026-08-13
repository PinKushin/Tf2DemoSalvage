# The model chain: what a static prop actually is

Static props are placements (see `10-maps.md`); this is what they place. Drawing one needs three
files that must agree with each other:

| File | Holds |
|---|---|
| `.mdl` | structure — body parts, models, meshes, material names |
| `.vvd` | the vertices: position, normal, texture coordinate |
| `.dx90.vtx` | the indices, and which LOD each belongs to |

## Offsets, read from Valve's published header

*Evidence class: read from published source.* `src/public/studio.h` in `ValveSoftware/source-sdk-2013`.
Nothing here was decompiled and nothing needs to be — see the standing rule about decompiler output
never entering a repository.

Computed for a 32-bit build, which is what the shipped files are. Member functions occupy no space;
`Vector` is 12 bytes, `Vector2D` 8, a pointer 4.

### `studiohdr_t`

```
   0  int    id                 'IDST'
   4  int    version
   8  int    checksum           must match the .vvd and .vtx
  12  char   name[64]
  76  int    length
  80  Vector eyeposition        illumposition, hull_min, hull_max,
                                view_bbmin, view_bbmax follow, 12 each
 152  int    flags
 156  int    numbones,           160 boneindex
 164  int    numbonecontrollers, 168 bonecontrollerindex
 172  int    numhitboxsets,      176 hitboxsetindex
 180  int    numlocalanim,       184 localanimindex
 188  int    numlocalseq,        192 localseqindex
 196  int    activitylistversion
 200  int    eventsindexed
 204  int    numtextures,        208 textureindex
 212  int    numcdtextures,      216 cdtextureindex
 220  int    numskinref
 224  int    numskinfamilies
 228  int    skinindex
 232  int    numbodyparts,       236 bodypartindex
```

**Every index field is relative to the struct that contains it**, not to the file. That is uniform
across the format and it is the single easiest thing to get wrong, because for `studiohdr_t` — which
sits at offset zero — the two are the same number. A reader that works on the header and then reads
garbage from a mesh has found this.

The gap between `localseqindex` and `numbodyparts` is worth calling out: five fields
(`activitylistversion`, `eventsindexed`, and the texture and skin counts) sit in it, so a layout that
skips them lands `numbodyparts` 36 bytes early. An abridged reading of the header will do exactly
that, silently.

### The rest of the chain

```
mstudiobodyparts_t   16 bytes   0 sznameindex  4 nummodels  8 base  12 modelindex
mstudiomodel_t      148 bytes   0 name[64]  72 nummeshes  76 meshindex
                                80 numvertices  84 vertexindex
mstudiomesh_t       116 bytes   0 material  8 numvertices  12 vertexoffset
mstudiotexture_t     64 bytes   0 sznameindex  4 flags
```

`mstudiomesh_t.vertexoffset` is relative to its model's `vertexindex`, which is itself an index into
the `.vvd`'s vertex array — the meshes of one model partition that model's vertices in order.

### `.vvd`

```
vertexFileHeader_t   64 bytes   0 id 'IDSV'  4 version  8 checksum  12 numLODs
                                16 numLODVertexes[8]  48 numFixups
                                52 fixupTableStart  56 vertexDataStart  60 tangentDataStart
vertexFileFixup_t    12 bytes   0 lod  4 sourceVertexID  8 numVertexes
mstudiovertex_t      48 bytes   0 boneWeights (float[3], char[3], byte)
                                16 position  28 normal  40 texCoord
```

Unlike everything in the `.mdl`, `vertexDataStart` and `tangentDataStart` **are file offsets.** The
format mixes both conventions, in adjacent files, with no marker distinguishing them.

**The fixup table is not optional.** When `numFixups` is non-zero the vertex array is not in LOD
order, and the fixups say which runs belong to which LOD; a reader that ignores them gets a vertex
array that is the right length and the wrong contents, which draws a recognisable-but-wrong model.

## What is still missing

`.vtx` is described by `optimized_model.h`, and **that header is not in `source-sdk-2013`** — only
`studio.h` ships. So the index data is the one part of this chain with no first-party published
source available, and it has to come from another open-source parser instead. Recorded here so the
difference in evidence class is not lost: everything above is read from Valve's own header, and
whatever fills this gap will not be.

## Shipped models contain degenerate normals

*Evidence class: measured on the corpus.* `bot_heavy.vvd` has **two vertices out of 9,401 whose
normal is exactly zero**. Every other normal in the 200 models checked is unit length to within
0.01.

It looked like a parse fault and is not. Three things settle it, and they are the shape of check
worth reaching for whenever a reader produces one odd value:

- The vertex count came out at exactly the header's declared 9,401 for LOD 0.
- 9,401 × 48 is exactly 451,248, which is exactly the distance from `vertexDataStart` to
  `tangentDataStart`. A wrong stride does not land on the boundary.
- The two offending vertices carry perfectly ordinary positions and texture coordinates.

So the compiler emits degenerate normals for collapsed or unused vertices and the engine tolerates
them. **The test allows exactly zero and nothing in between**, which keeps it sharp — read at a
wrong offset the lengths are arbitrary, and arbitrary is neither 1 nor 0 — and separately requires
them to stay rare, because the quiet way this could go wrong is running off the end of the data into
padding, which would produce zeros wholesale and satisfy the first check on every one of them.

Both sabotages were run: moving `NormalOffset` by four bytes fails one test, and changing the vertex
stride from 48 to 44 fails five.

## Three things about `.mdl` that only real files tell you

*Evidence class: measured on 400 shipped models.* Each of these broke an assertion written from the
header alone, and each turned out to be Valve's data rather than a misread.

**A material name is sometimes a path.** Most texture entries are bare — `rock_02` — and resolve by
joining one of the model's folders. But 14 of 200 props carry a full relative path instead, like
`models/props_2fort/window005`, and **those models list an empty string among their folders.** The
empty entry is not corruption; it is the compiler saying the name is already the path. A reader that
filters empty folders loses exactly those models, silently, because every other model still resolves.

**Separators are mixed inside a single file.** The same model lists `models\props_2fort\` and
`models\props_2fort/`.

**`..` in a material name is legitimate and Valve uses it.** `bot_medic.mdl` names
`..\..\effects\invulnfx_red`, relative to its own material folder. This one matters twice over,
because a model arrives inside a downloaded map and is untrusted input (D32):

- Refuse every candidate containing `..` and those materials are lost — the surface draws untextured.
- Allow `..` unresolved and a hostile file names anything on disk.

Both are satisfied by **resolving rather than matching**: walk the segments, pop on `..`, and refuse
only when the stack would go empty. That is the point at which a path has actually left the folder,
rather than merely mentioned leaving it.

**Not every `.mdl` has geometry.** `bot_demo_animations.mdl` has zero body parts and zero materials:
it exists to share sequences between models. Zero meshes is a real state, and asserting that every
model has geometry reports the engine's own arrangement as a defect.

## The sabotage that passed, and why the fixture was the bug

`mstudiomodel_t.vertexindex` is a **byte** offset while `mstudiomesh_t.vertexoffset` is a **vertex**
count. Confusing them scales every index by 48.

Changing the divisor from 48 to 24 **passed the whole suite** on the first attempt. The fixture was
props only, and nearly every prop is a single model whose `vertexindex` is **zero** — and zero
divided by either number is zero. Correct and broken predicted the same observation, so no assertion
could have caught it.

Widening the selection to all models brought in characters and weapons, which carry several models
per file and therefore non-zero offsets; the same sabotage then failed, on exactly the range check
written for it. **A guard test now asserts the fixture still contains a model whose vertices do not
start at zero**, so narrowing the selection later cannot quietly remove the sensitivity again.
