---
name: material-variables-split-three-ways
description: "Source declares material variables in three unrelated places, and SdkCoverageTests has accused correct code twice for knowing only two"
metadata: 
  node_type: memory
  type: project
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:53:19.697Z
---

Source splits material variables three ways, and each lives somewhere unrelated:

| Kind | Declared in | Examples |
|---|---|---|
| Shader parameter | `SHADER_PARAM(...)` across `stdshaders/*.cpp` | `$detail`, `$bumpmap`, `$envmap` |
| Material flag | `MATERIAL_VAR_*` bits, `imaterial.h:355` | `$translucent`, `$alphatest`, `$nocull` |
| **Standard var** | `ShaderMaterialVars_t`, `public/shaderlib/BaseShader.h:32` | `$color`, `$color2`, `$alpha`, `$basetexture` |

`SdkCoverageTests.EveryMaterialParameterWeClaim_IsOneTheEngineDeclares` builds its denominator from
all three. **It has been wrong twice, in the same shape, and both times it accused correct code**:
knowing only shader parameters it reported eight flags as undeclared; knowing parameters and flags
it reported `$color`, `$color2` and `$alpha` the same way (2026-08-18).

Its message reads like a fact about the engine — *"Source declares no such shader parameter"* — so
the natural response is to delete the entry from the census. That is the wrong response both times
so far.

The standard vars' *names* are interpolated, not read: `s_StandardParams` is in the closed
`CBaseShader.cpp`, and `BaseShader.h:31` says so in a comment. Four of thirteen are confirmed by
string in shipped game code (`FindVar("$alpha")` in `alphamaterialproxy.cpp:42`, `FindVar("$color")`
in `thermalmaterialproxy.cpp:50`, `"$color2"` in `item_import.cpp:1328`, `$basetexture` everywhere).

**Why:** a generated denominator can never go stale, which is its whole value — but only across the
axes it models. A missing axis does not read as a gap in the instrument; it reads as a defect in the
code, with a citation attached.

**How to apply:** when that test accuses a parameter, check which of the three kinds it is before
touching the census. If it is a fourth kind nobody has modelled yet, the fix is a new
`SdkInventory` method with a positive control asserting it found what it must — an empty scrape
makes this test pass more easily, not less. See [[instrument-bugs-outnumber-decoder-bugs]] and
[[the-denominator-decides-what-can-be-lost]]; the modulation story is
`docs/findings/26-material-modulation.md`.

---

## `a-parameter-can-be-gated-by-a-sub-block` — the file having the key is not the material having it

**"The parameter is implemented" and "the parameter arrives" are two claims.** `$selfillum` had a
reader — `VmtMaterial.IsSelfIlluminated` — for the life of the project, and **5,415 of the 30,684
materials TF2 ships declare it inside a `">=DX90"` block** the parser did not descend into. Every
one of those surfaces drew unlit, on every map, and nothing said so (B326).

A VMT gates keys on the DirectX support level:

```
"LightmappedGeneric"
{
	"$basetexture" "signs/exit"
	">=DX90" { "$selfillum" "1" }
}
```

Only four spellings exist in TF2: `>=DX90` (5,688), `<dx90` (281), `>=dx90_20b` (10), `<dx90_20b`
(5). This project reports level **95** and takes every `>=`, refusing every `<` — whose keys are the
cheap-hardware path (`$fallbackmaterial`, `$outlinecolor`), so flattening everything is the opposite
bug.

**The lesson that generalises past VMTs: the file having the key is not the material having it.**
When a parameter looks unused, or a material looks wrong in a way its declared keys cannot explain,
read the RAW file next to the parse — `vmt-blocks` and the `vmt` probe print both.

**And count before scoping.** This was filed from one material (`gold_player.vmt`, gated `$envmap`)
with a guess at the spellings and the scope. Both guesses were wrong: `>=DX80`, `>=DX70` and the
`if($...)` forms appear nowhere, and the cubemap that got noticed is 59 materials against
`$selfillum`'s 5,415. The bug worth naming was not the one that was seen.

```bash
dotnet run --project tools/Tf2DemoSalvage.Probe -c Release -- vmt-blocks ">=DX90"
```

Evidence: shipped data plus convention, not read-from-source — the SDK publishes `shaderapidx9` and
`stdshaders`, never the VMT loader. See [[nothing-is-closed]] for the search order and
[[measure-the-output-not-the-capability]] for the shape of the failure.

---

## `a-proxy-is-per-entity-per-draw` — a proxy's value cannot live on the material

**`IMaterialProxy` has `Init`, `OnBind` and `Release` and no tick.** A proxy therefore runs when a
material is bound for a DRAW, and what it computes belongs to the entity being drawn — not to the
material.

That is the whole argument against the obvious design. Two players wearing the same hat in different
paints share one material; folding the colour into the material at load gives them the same hat and
passes every test that only checks the arithmetic.

### TF2's paint needs the PAIR, and a variable table

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

### The companion that decides where the colour lands

`$blendtintbybasealpha` confines the modulation to the region the base texture's ALPHA marks. Without
it a painted hat is dyed end to end rather than on its band, which reads as a wrong colour rather
than a missing feature. `$blendtintcoloroverbase` lerps between multiplying the tint in and replacing
the albedo, and **self-illumination wins over both** — a pixel-shader limit, not an art decision
(`skin_dx9_helper.cpp:269`).

Related: [[instrument-bugs-outnumber-decoder-bugs]] — how the paint decode was verified.
[[parity-is-the-search-not-the-defence]] — implementing one proxy of the two is the same fault.
