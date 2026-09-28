---
name: a-computed-offset-is-a-guess-the-file-can-answer
description: "When a format has a table saying where its parts are, read the table — a computed offset is right only while nothing optional is present, and it fails by producing a picture rather than an error."
metadata: 
  node_type: memory
  type: project
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-10T22:49:04.336Z
---

`VtfTexture` found image data at `headerSize + thumbnail` for years — the VTF 7.2 layout. From 7.3
on, a resource table follows the header and images are two entries in it
(`VTF_LEGACY_RSRC_LOW_RES_IMAGE` `0x01`, `VTF_LEGACY_RSRC_IMAGE` `0x30`, `src/public/vtf/vtf.h`),
with anything else the file carries (a sprite sheet, a CRC, an LOD clamp) sitting between them.

**288 of TF2's 34,246 textures decoded to noise**, every one an effect, silently — no exception was
ever thrown. VTF stores mips smallest-first, so a 1,436-byte shift moves the largest (last) mip by a
fraction of its own size: `smokelit`'s smoke kept its silhouette but filled with rainbow speckle.

**Why:** a computed offset encodes an assumption about file contents — correct until something
optional is present, so it passes the common case and fails the interesting one.

**How to apply:**
- If a format has a directory (lumps, resources, chunks, string tables), read it; a formula is a
  fallback for versions with none, not the primary route.
- Recognisable shape with garbage content means the right region read at the wrong place, or a
  neighbour read as this one — see [[address-a-struct-by-name-not-from-its-end]].
- Don't let a sample correlation become the mechanism: DXT1 decoded fine, DXT5 didn't — not because
  of the codec, but because effects (which carry the sheet) happen to use DXT5 for alpha
  ([[ask-which-input-differs-before-bisecting]]).
- A mean can't tell grey from rainbow (`(78 68 68)` read as "smoke"); per-pixel channel SPREAD does
  (112.5 wrong vs 10.8 right) — print the texture as a character grid to end the argument in one run.

`vtf census` in the probe counts affected files; `vtf <path>` prints the spread and the grid.
