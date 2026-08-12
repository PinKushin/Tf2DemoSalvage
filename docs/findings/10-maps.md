# 10 — Maps: reading a BSP, and what "the map" turns out to mean

This layer exists because the viewer draws demos over the map they were recorded on. Everything
here was worked out on 2026-08-12 against the 233 maps in a real TF2 install; the numbers below
are measured on those files unless marked otherwise.

The demo format and the map format are unrelated, but the failure modes rhyme: both hand you
numbers that are wrong rather than errors that are obvious.

## Every lump in a shipped TF2 map is LZMA compressed, and the directory does not say so

The first attempt at reading geometry produced this:

```
System.IO.InvalidDataException: Face 0 names plane 23116 of 1824.
```

**Evidence class: measured.** That error is the lucky case. A bounds check fired, so the failure
announced itself. Nothing in the format required it to: the bytes being read as `dface_t` were
compressed data, and compressed data makes perfectly ordinary-looking integers.

The lump directory gives an offset, a length and a version, and none of those change when a lump
is compressed. The announcement is inside the lump, in a 17-byte header:

```c
struct lzma_header_t {
    unsigned int  id;            // 'LZMA', so 0x414D5A4C little-endian on disk
    unsigned int  actualSize;    // decompressed length
    unsigned int  lzmaSize;      // compressed length, excluding this header
    unsigned char properties[5];
};
```

Note what is **absent**: the eight-byte uncompressed-size field that a standard `.lzma` file
carries between the properties and the stream. `actualSize` replaces it. A decoder fed this as a
`.lzma` file reads the first eight bytes of real compressed data as a length and produces
nonsense — the size has to be passed in separately.

Measured on `cp_process_final.bsp`, every geometry lump:

| lump | on disk | decompressed | ratio |
|---|---|---|---|
| planes (1) | 36,486 | 248,640 | 6.8x |
| vertexes (3) | 51,012 | 298,596 | 5.9x |
| texinfo (6) | 33,721 | 327,168 | 9.7x |
| faces (7) | 147,154 | 773,976 | 5.3x |
| edges (12) | 107,689 | 288,400 | 2.7x |
| surfedges (13) | 123,455 | 447,264 | 3.6x |

All six, and the entity lump too. There is no partially-compressed map in the sample.

### The arithmetic identified it before any byte was decoded

**Evidence class: arithmetic.** This is the general form of the rule in
`docs/memory/length-arithmetic-identifies-a-layout.md`, and it is the part worth remembering:

> A lump of fixed-size structures has a length that is a whole multiple of that size.

`dface_t` is 56 bytes. The faces lump is 147,154 bytes. 147,154 / 56 = **2,627.75**. Edges and
surfedges are 4-byte entries and neither length divides by 4 either. That is not a lump of those
structures, and no decoding was needed to know it.

Decompressed, all six divide exactly — faces to 13,821, which is precisely the count the reader
went on to produce.

**The code had the information and threw it away.** It computed `count = length / stride`, and
integer division silently discarded the remainder, turning "this is not a face lump" into a
plausible face count. The check must be `length % stride == 0`, refusing the lump. This is the
same `==` versus `<=` distinction that decided the container layout question earlier in the
project, arriving in a completely different file.

### Why the decoder is a vendored SDK rather than an implementation

**Evidence class: decision, recorded because the alternative was tried.** Snappy and LZSS are
hand-written in this project, because they are part of the format being reverse engineered. LZMA
is not: Valve simply calls a general-purpose codec, so a hand-written range decoder would add risk
without adding understanding.

`SharpCompress` was the first choice and was rejected for two reasons. It carries rar, 7z, zip,
tar, ACE, ARJ, PPMd, ZStandard and more — 1,100 types — to supply one decoder. And it declares a
**`public BitReader` in the global namespace**, which broke this project's compilation on the
package reference alone:

```
'BitReader' does not contain a definition for 'ReadUInt32'
```

C# resolves the enclosing namespace chain out to global *before* it consults `using` directives,
so a global-namespace type displaces a namespaced one of the same name everywhere, in files that
never mention the package. 58 other types sit in that namespace with it.

The LZMA SDK (Igor Pavlov, public domain, 56 KB, 52 types, no dependencies beyond netstandard)
has a clean `SevenZip` namespace and an API shaped exactly like Valve's container: properties
separately, output size passed in.

**Two of its behaviours are worth recording, both measured here:**

1. **Its output size is not a hard stop.** The decode loop tests the limit once per symbol, so a
   match that *begins* below the limit is copied in full and can overshoot by up to a maximum
   match length. Against an exactly-sized `MemoryStream` this surfaces as `Memory stream is not
   expandable`, which reads like a caller bug rather than a documented property of the decoder.
2. **It raises its own exception types**, including a bare `InvalidOperationException`. Hostile
   input must not surface as something that reads like a defect in the program consuming it.

## A map is hostile input, and both sizes in that header come out of the file

**Evidence class: decision, following D32.** `actualSize` is four bytes, so a lump of a few
hundred bytes can ask for 4 GB — the same allocate-before-validate shape already fixed twice in
this codebase, in `Lzss` and in `CopyBits`. It is checked against a 256 MB cap before anything is
allocated, and `lzmaSize` is checked against the bytes that actually follow the header.

## The 3D skybox is real geometry, and it is not "the map"

The first render of `cp_process_final` put the map in a third of the viewport with a small
detached structure far away in the corner. That structure is the **3D skybox room**: ordinary
world brushes, built at reduced scale, placed well outside the playable space, and drawn by the
engine through a `sky_camera` entity to fake distance. Nothing in the geometry marks it.

Three rules were tried. The two that failed are more instructive than the one that worked.

### Rejected: trim to a vertex percentile

**Evidence class: measured, and decisive against.** Discard the outer 1% of vertices on each axis
and fit to what is left. Measured over eight maps, the surviving extent was:

| map | full extent | 1–99 percentile | shrink |
|---|---|---|---|
| cp_process_final | 21360 x 19808 | 11632 x 6592 | 54% x 33% |
| cp_gullywash_final1 | 26428 x 19779 | 13941 x 5289 | 53% x 27% |
| pl_upward | 24108 x 19516 | 11232 x 5632 | 47% x 29% |

It deletes a third to a half of every real map. **Vertex density is not extent**: detail
concentrates in the middle of a map and the outskirts are sparse, so a percentile keeps the
busiest room and throws away the map around it.

### Rejected: use the `sky_camera` entity as an exact marker

**Evidence class: measured, and the most interesting negative result here.** This one is
genuinely appealing, and the reasoning behind it is sound as far as it goes:

```
{
"origin" "-4374 -3786 229.5"
"scale" "16"
"classname" "sky_camera"
}
```

`sky_camera` is not a naming convention. It is an entity class registered in the engine's own code
and offered to mappers through the FGD that Hammer loads, so it is picked from a list rather than
typed. A community map can rename its brushes, its textures and its targetnames freely and still
cannot rename this. Where a spatial rule is a guess, this looked like a fact.

It is still wrong, because it answers a different question. **The entity is placed to *view* the
skybox room, not to sit inside it.** Measured across nine maps by clustering geometry on a
256-unit grid and asking which cluster contains the camera:

| map | where sky_camera falls |
|---|---|
| cp_process_final | outside every cluster of geometry |
| cp_badlands | cluster #1, 2.3% of points |
| cp_gullywash_final1 | cluster #1, 1.3% of points |
| cp_snakewater_final1 | outside every cluster |
| pl_upward | cluster #1, 3.7% of points |
| ctf_2fort | cluster #1, 0.7% of points |
| cp_dustbowl | outside every cluster |
| plr_hightower | cluster #2, 0.1% of points |
| koth_viaduct | outside every cluster |

It is never in the largest cluster — that part of the hypothesis held. But on four of nine it is
in no cluster at all, so it cannot be used to *locate* the room.

**A parser bug hid inside this measurement, and it is the finding above coming back around.** The
first run of it reported "no sky_camera" for `pl_upward`, `ctf_2fort` and `cp_dustbowl` — maps
that certainly have one. The probe was reading the entity lump raw. Compressed bytes contain no
`{`, so the parse returned zero entities, cleanly and without error. An empty result is what a map
with no entities looks like.

### Accepted: the largest connected cluster of geometry

**Evidence class: measured.** Occupancy on a 256-unit grid, flood fill with eight-way neighbours,
take the biggest component:

| map | clusters | largest holds |
|---|---|---|
| cp_process_final | 24 | 97.3% |
| cp_badlands | 32 | 96.5% |
| cp_gullywash_final1 | 11 | 97.7% |
| cp_snakewater_final1 | 31 | 98.8% |
| pl_upward | 95 | 94.2% |
| ctf_2fort | 17 | 99.1% |
| cp_dustbowl | 29 | 99.7% |
| plr_hightower | 62 | 91.1% |
| koth_viaduct | 66 | 98.1% |

91.1% to 99.7% across the sample, with outliers in single digits. The largest connected piece
identifies the map without needing to know what the other pieces are, and a detached skybox room
is excluded by construction rather than by recognition.

Nothing is discarded: every edge is still drawn, and outlying geometry falls outside the view.

**A control test found a real bug in the implementation.** Occupancy was marked at segment
*endpoints* only, so a 500-unit edge skipped the cells between it and split a single connected map
into pieces at every long wall. Real maps hide this completely — their vertex density means the
gaps never appear — which is exactly why the test that caught it is three quads and not a map.
Segments are rasterised at half-cell steps.

### The rule this is standing in for

**Evidence class: stated intent, not yet implemented.** Geometry clustering answers "where is
there brushwork", and the question actually worth answering is **"where can a player go"** —
anything unreachable is outside the map regardless of how it is built. The demo itself states
that, in its entity positions. When tick-accurate playback lands, the play area should come from
the recording rather than from the map file, and this rule becomes the fallback for the first
frame.

**Clustering cannot close this gap, by construction.** Looking at the rendered overview of
`cp_process_final`, the geometry behind the last-point spawn is visible in the outline and is not
visible in the game — a player cannot reach it or see it. It is nevertheless *attached* to the
map, so it is in the main cluster and no connectivity rule will ever exclude it. Two things put it
there: a Source map has to be sealed against the void for `vvis` to compute visibility, and mappers
pad behind spawn so the skybox meets the ground cleanly.

(The sealing requirement is about the visibility precompute, not about keeping players in. `vvis`
floods outward from a point entity to work out which leaves can see which, and that flood assumes
a closed world; a hole lets it escape into the void, which is the `leaked!` error, and `vrad` then
leaks light through the same gap. It is not a hard failure — visibility degrades toward drawing
everything always — so a leaked map runs badly rather than refusing to run.) The same applies to the back faces of boundary
cliffs.

The two cases are not equally wrong, and the difference sharpens the rule. The padding behind
spawn is never seen by anyone. The boundary cliff at the back of second **is** seen — from the air,
by a soldier or demo mid-jump, which is a normal thing to be doing there. So the criterion is not
"where a player stands" but "what a player can see from anywhere they can get to", and in TF2 the
jumping classes set that horizon well above the floor.

That is the distinction that matters: connectivity finds the map's *body*, reachability finds its
*interior*. Only the second is what a viewer wants to frame, and only the demo can supply it —
which is convenient, because a demo of a jumping class is exactly the recording that samples the
horizon.

## See also

- `docs/DECISIONS.md` D32 — treating a downloaded map as hostile input
- `docs/findings/08-method.md` — length arithmetic, and controls that fail on small inputs
- `docs/RENDERING_NOTES.md` §7 — why the overhead view keeps upward-facing surfaces only
