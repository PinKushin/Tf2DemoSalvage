---
name: material-variables-split-three-ways
description: Source declares material variables in three unrelated places, and SdkCoverageTests has accused correct code twice for knowing only two
metadata:
  type: project
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
makes this test pass more easily, not less. See [[an-empty-search-needs-a-control]] and
[[an-uncoverable-gap-is-usually-your-reader]]; the modulation story is
`docs/findings/26-material-modulation.md`.
