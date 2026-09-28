---
name: material-variables-split-three-ways
description: "Source declares material variables in three unrelated places, and SdkCoverageTests has accused correct code twice for knowing only two"
metadata: 
  node_type: memory
  type: project
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:53:19.697Z
---

Source splits material variables three ways, each declared somewhere unrelated:

| Kind | Declared in | Examples |
|---|---|---|
| Shader parameter | `SHADER_PARAM(...)` in `stdshaders/*.cpp` | `$detail`, `$bumpmap`, `$envmap` |
| Material flag | `MATERIAL_VAR_*`, `imaterial.h:355` | `$translucent`, `$alphatest` |
| Standard var | `ShaderMaterialVars_t`, `BaseShader.h:31` | `$color`, `$alpha`, `$basetexture` |

`SdkCoverageTests`'s denominator builds from all three — **wrong twice, both times accusing correct
code**, once knowing only shader parameters (flagged flags as undeclared) and once also knowing flags
but not standard vars (flagged `$color`/`$color2`/`$alpha`).

Standard var names are interpolated, not read (`s_StandardParams` lives in closed
`CBaseShader.cpp`) — four of thirteen confirmed by string in shipped code
(`FindVar("$alpha")` in `alphamaterialproxy.cpp:42`, `FindVar("$color")` in
`thermalmaterialproxy.cpp:50`, `"$color2"` in `item_import.cpp:1328`, `$basetexture` everywhere).

**Why:** a generated denominator never goes stale across the axes it models — a MISSING axis reads as
a defect in the code, with a citation attached, not a gap in the instrument.

**How to apply:** when the test accuses a parameter, check which of the three kinds it is before
touching the census. A missing fourth kind needs a new `SdkInventory` method with a positive control.
See [[instrument-bugs-outnumber-decoder-bugs]], [[the-denominator-decides-what-can-be-lost]];
`docs/findings/26-material-modulation.md`.

---

## `a-parameter-can-be-gated-by-a-sub-block` — the file having the key is not the material having it

**"Implemented" and "arrives" are different claims.** `$selfillum` had a reader for the project's
life, and 5,415 of 30,684 TF2 materials declare it inside a `">=DX90"` block the parser didn't
descend into — every such surface drew unlit, unnoticed (B326).

VMTs gate keys on DirectX level; only four spellings exist (`>=DX90` 5,688, `<dx90` 281, `>=dx90_20b`
10, `<dx90_20b` 5). This project reported level 95, took every `>=`, refused every `<` — whose keys are
the cheap-hardware path — so flattening everything was the OPPOSITE bug.

**When a parameter looks unused or a material looks wrong beyond its declared keys, read the RAW
file next to the parse.** Count before scoping — the original guess at spellings/scope was both wrong
(some spellings appear nowhere; the noticed bug was 59 materials against the real one's 5,415).

Evidence: shipped data plus convention, not SDK source. See [[nothing-is-closed]],
[[measure-the-output-not-the-capability]].

---

## `a-proxy-is-per-entity-per-draw` — a proxy's value cannot live on the material

**`IMaterialProxy` has `Init`/`OnBind`/`Release`, no tick** — a proxy runs per DRAW, and what it
computes belongs to the entity, not the material. Two players in the same paint sharing one hat
material would fold paint into load-time material state and pass every arithmetic test while giving
both hats the same colour.

**TF2's paint needs the PAIR, plus a variable table:** `ItemTintColor` writes ZERO for an unpainted
item, deliberately, and is left there (`econ_wearable.cpp:465-543`), so the next proxy
(`SelectFirstIfNonZero`) falls back to the material's own colour; `$colortint_tmp` is not a shader
constant, so the proxy chain needs a small named-variable table alive for the bind, seeded from the
material. `IsZero` checks all three channels (`mathproxy.cpp:1050`), so pure black paint is
indistinguishable from no paint (Valve's behaviour, reproduce it). `$color` and `$color2` stay
separate — the chain replaces only the second.

**`$blendtintbybasealpha`** confines modulation to the base texture's alpha region; without it a
painted hat dyes end-to-end. **Self-illumination wins over both** — a pixel-shader limit, not an art
decision (`skin_dx9_helper.cpp:269`).

Related: [[instrument-bugs-outnumber-decoder-bugs]], [[parity-is-the-search-not-the-defence]].
