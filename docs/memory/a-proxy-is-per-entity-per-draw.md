---
name: a-proxy-is-per-entity-per-draw
description: A material proxy runs at BIND for one entity, so a value it produces cannot live on the material — and TF2's paint chain needs two proxies plus a variable table, because one's output is the other's input.
metadata:
  type: project
---

**`IMaterialProxy` has `Init`, `OnBind` and `Release` and no tick.** A proxy therefore runs when a
material is bound for a DRAW, and what it computes belongs to the entity being drawn — not to the
material.

That is the whole argument against the obvious design. Two players wearing the same hat in different
paints share one material; folding the colour into the material at load gives them the same hat and
passes every test that only checks the arithmetic.

## TF2's paint needs the PAIR, and a variable table

```
"ItemTintColor"        { "resultVar" "$colortint_tmp" }
"SelectFirstIfNonZero" { "srcVar1" "$colortint_tmp"  "srcVar2" "$colortint_base"  "resultVar" "$color2" }
```

- **`ItemTintColor` writes ZERO for an unpainted item** — its result starts at `Vector( 0, 0, 0 )`
  and is left there (`econ_wearable.cpp:465-543`). That is not a fallback; it is what makes the
  proxy beside it choose the material's own colour.
- **`$colortint_tmp` is not a shader constant.** One proxy's output is the other's input, so a proxy
  system that can only write constants cannot run the chain. It needs a small named-variable table
  alive for the bind, seeded from the material — a `SelectFirstIfNonZero` reading a missing variable
  as zero paints every unpainted cosmetic black.
- **`IsZero` is all three channels** (`mathproxy.cpp:1050`), so a paint of pure black is
  indistinguishable from no paint. Valve's behaviour; reproduce it.

**And `$color` must be kept apart from `$color2`.** The modulation a shader consumes is their
product, and the chain replaces only the second — recovering the first afterwards means dividing by
a value that is legally zero.

## The companion that decides where the colour lands

`$blendtintbybasealpha` confines the modulation to the region the base texture's ALPHA marks. Without
it a painted hat is dyed end to end rather than on its band, which reads as a wrong colour rather
than a missing feature. `$blendtintcoloroverbase` lerps between multiplying the tint in and replacing
the albedo, and **self-illumination wins over both** — a pixel-shader limit, not an art decision
(`skin_dx9_helper.cpp:269`).

Related: [[print-a-value-somebody-can-recognise]] — how the paint decode was verified.
[[half-a-mechanism-is-not-parity]] — implementing one proxy of the two is the same fault.
