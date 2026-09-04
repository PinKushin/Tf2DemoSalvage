---
name: shipped-data-settles-what-closed-code-cannot
description: When the code is closed, an authored asset can still decide the behaviour — Valve did not ship a bump map that draws on no hardware; and a block name is not evidence about what is inside it.
metadata:
  type: project
---

**A question the closed engine would answer can often be settled by what Valve AUTHORED instead.**
Measured 2026-09-04 on B328.

403 shipped materials carry a block named `LightmappedGeneric_DX9`, and **no shader is registered
under that name anywhere in `source-sdk-2013`** — only helper types and functions carry the
spelling. The material system that would resolve it is closed. So "does this block apply?" looked
like a decompiler question.

It was not. One file answers it:

```
"LightmappedGeneric"
{
	"$basetexture" "Tile/tilefloor018a"
	 "LightmappedGeneric_DX9"
	{
		"$bumpmap" "tile/tilefloor018a_normal"
		"$envmap"  "env_cubemap"
	}
}
```

Under "the block does not apply", Valve authored a bump map that draws on **no hardware at all**.
That is not a tenable reading of shipped content, so the block applies. The argument is about the
ASSET's authorship, not about the code, and it is as decisive here as reading the function would
have been.

**The general form: ask what the content would have to mean for your reading to be true.** Shipped
assets are made by people who tested them; a reading that makes an artist's work invisible is
usually the wrong reading.

## And the mistake this corrected: a name is not evidence about its contents

These blocks were first written off — in a risk entry, in a finding, and in a source comment — as
"all low-end fallbacks", safe to ignore. That came from reading the block NAMES and knowing that a
fallback is what runs on weaker hardware. Nothing inside one had been looked at.

Inside `LightmappedGeneric_DX9`: `$bumpmap` in 89 materials, `$envmap` in 49, `$parallaxmap` in 8 —
**and every one of those declares the key ONLY there**, so ignoring the block loses it outright.

Two columns, not one, when censusing a container: **what it contains**, and **what is declared
solely inside it**. The first says how much is in there; only the second says what skipping it
costs. See [[an-empty-search-needs-a-control]] and
[[print-what-was-added-not-how-many]] — same family, different disguise.

## The honest scope, which was nearly overstated

The fix changes **nothing on TF2's own content**: `cp_process_final` reports the same 55 of 412
materials carrying a cubemap before and after. Every affected material is Half-Life 2 content TF2
mounts — `TILE/TILEFLOOR018A_C17`, `MODELS/PROPS_VEHICLES/CAR002A_01`. It was done anyway, because a
divergence is a defect whatever it costs, and it will matter to a community map built on HL2 assets
— a population this corpus contains none of.

"403 materials fixed" would have been true and misleading. Report the population the change reaches,
not the population that declares the key.
