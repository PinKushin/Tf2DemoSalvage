---
name: a-computed-offset-is-a-guess-the-file-can-answer
description: When a format has a table saying where its parts are, read the table — a computed offset is right only while nothing optional is present, and it fails by producing a picture rather than an error.
metadata:
  type: project
---

**`VtfTexture` found image data at `headerSize + thumbnail` for years.** That is the 7.2 layout. From
VTF 7.3 the header is followed by a resource table, the images are two of its entries
(`VTF_LEGACY_RSRC_LOW_RES_IMAGE` `0x01`, `VTF_LEGACY_RSRC_IMAGE` `0x30`, `src/public/vtf/vtf.h`), and
**anything else the file carries sits between them** — a sprite sheet, a CRC, an LOD clamp.

**288 of TF2's 34,246 textures decoded to noise**, every one an effect, and it went unnoticed because
it never threw. VTF stores mips *smallest first*, so the largest mip is at the end of the file and a
1,436-byte shift moves it by a fraction of its own size: `smokelit`'s smoke puffs kept their
silhouettes, in the right places, at the right sizes, and filled with rainbow speckle.

**Why:** a computed offset encodes an assumption about what the file contains. It is correct exactly
while nothing optional is present, so it passes on the common case and fails on the interesting one —
here, on every texture that carries the sheet the particle system needs.

**How to apply:**

- **If a format has a directory, read the directory.** Lumps, resources, chunks, string tables. A
  formula is a fallback for versions that have none, not the primary route.
- **Suspect the offset before the codec when output is structured but wrong.** Recognisable shape
  with garbage content means the right region read at the wrong place, or a neighbouring region read
  as this one. See [[address-a-struct-by-name-not-from-its-end]] — same family, fourth time here.
- **Do not let a correlation in the sample become the mechanism.** DXT1 textures decoded correctly
  and DXT5 ones did not, which sent this to the DXT decoder, `BlockFormat`, `BlockPitch` and the GPU
  upload — all correct. Sheet-carrying textures are DXT5 because they are effects and effects have
  alpha. Ask which property is *causal*, not which one separates the two groups
  ([[ask-which-input-differs-before-bisecting]]).
- **A mean cannot tell grey from rainbow — it averages to grey.** `(78 68 68)` was read as evidence
  of smoke. The per-pixel channel SPREAD separates them (112.5 wrong, 10.8 right), and printing the
  texture as a coarse character grid ended the argument in one run
  ([[print-a-value-somebody-can-recognise]], [[a-picture-is-assertable]]).

`vtf census` in the probe counts the affected files; `vtf <path>` prints the spread and the grid.
