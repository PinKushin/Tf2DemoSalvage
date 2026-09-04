---
name: a-parameter-can-be-gated-by-a-sub-block
description: A key can be in the file and absent from the parsed material because it sits in a conditional sub-block; count the shipped data before scoping the gap.
metadata:
  type: project
---

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
