# Conformance with Source

What this project reproduces of the engine, what it does not, and **what you would see** in each
case. Covers the whole surface rather than one subsystem — the demo format, the world, models,
materials and effects — because a viewer is only as convincing as its weakest layer, and the layers
fail in completely different ways.

**The executable half is four test classes**, 60 entries, currently **22 asserting parity and 38
naming a gap**:

| Suite | Covers | Source it is written against |
|---|---|---|
| `DemoConformanceTests` | messages, schema, entity deltas, interpolation | `public/inetmsghandler.h` |
| `WorldConformanceTests` | BSP lumps, lighting, water, skybox | `public/bspfile.h` |
| `ModelConformanceTests` | bones, attachments, flexes, IK, ragdolls | `public/studio.h` |
| `SourceConformanceTests` + `EffectConformanceTests` | materials and shading; particles, beams, decals, shadows | `stdshaders/`, `game/client/c_te_*` |

Run them with `dotnet test --filter Conformance`. The skipped count IS the score. Seeded from the material census — which reports, at every map load, the parameters and
shaders a real map asks for that this project does not implement — and then checked one at a time
against `source-sdk-2013`.

## Why the census counts are not the priority order

The census is honest about what a map *declares* and says nothing about what a map *shows*. Three
kinds of entry hide inside one list, and only the first is work:

| Kind | Example | What you see if it is missing |
|---|---|---|
| **Per-frame feature** | `$phong` | every model dull, all the time |
| **Capability flag** | `$cloakPassEnabled` | nothing, until the one moment it fires |
| **Not ours to implement** | `%compilenodraw` | nothing, ever — it was obeyed by vbsp before shipping |

`$cloakPassEnabled` is the case that proves the distinction. It sits on **307** of cp_process's 1,034
prop and model materials, which is alarming until you read it: `vertexlitgeneric_dx9.cpp:288` calls
it "If material supports cloaking", and the pass is gated per frame on
`CLOAKFACTOR > 0.0f && CLOAKFACTOR < 1.0f`. Every player material declares the capability; almost
none uses it in a given second. **Sorting the census by count would have put a day's work at the top
of the list for something invisible unless a spy cloaks on camera.**

So every entry below carries its source in the SDK and a plain statement of what it costs to be
without it. An entry with no "what you would see" is not ready to be worked on.

## Implemented

| Feature | Source | Notes |
|---|---|---|
| `$basetexture`, `$basetexture2` | — | with `WorldVertexTransition` mixing by vertex alpha |
| `$bumpmap`, `$ssbump` | — | self-shadowing bump distinguished from normal maps |
| `$detail` and its blend modes | `lightmappedgeneric_dx9.cpp` | twelve modes, tint, scale |
| `$translucent`, `$alphatest`, `$additive` | — | alpha test wins when a material declares both, as Valve's clause does |
| `$selfillum`, `$selfillumtint` | — | tint defaults to (1,1,1) as the engine does |
| `Modulate`, `$modblend`, `$mod2x` | shader name | multiplies the framebuffer; was drawn opaque until 2026-08-15 |
| `UnLitTwoTexture`, `$texture2` | `unlittwotexture_ps2x.fxc` | `baseColor * baseColor2 * g_DiffuseModulation`, alpha forced to 1 |
| `$nocull` | `imaterial.h:369` | `MATERIAL_VAR_NOCULL`, per material; everything else culls back faces |
| back-face winding | `imaterialsystem.h:180` | `MATERIAL_CULLMODE_CCW` — front faces are clockwise |
| `$halflambert` | `common_vs_fxc.h:826` | `(N·L * 0.5 + 0.5)²` on the direct term only |
| skin families | `pSkinref(skin * numskinref + material)` | team colours are a skin family, not a tint |
| bodygroups | `shared/animation.cpp:876` | `(body / base) % nummodels`, selected per entity at draw |
| bone merge | `bone_merge_cache.cpp:122` | matched **by name**; unmatched bones walk their own hierarchy |

## Not implemented, ordered by what it costs

### `$phong` — every model is dull
330 materials, with `$phongboost` 329, `$phongfresnelranges` 329, `$phongexponent` 323,
`$basemapalphaphongmask` 102. This is Source's specular for characters, and its absence is the
single largest visual difference on players. **Read `vertexlitgeneric_dx9_helper.cpp` and
`phong_dx9_helper` before starting** — the mask channel is chosen by
`$basemapalphaphongmask` versus the normal map's alpha, and picking the wrong one produces a
plausible sheen in the wrong places.

### `$envmap` — no reflections
133 prop materials and 42 brushwork ones, with `$envmaptint`, `$envmapcontrast`,
`$envmapsaturation`, `$basealphaenvmapmask`, `$normalmapalphaenvmapmask` alongside. Needs cubemaps
read from the BSP. Filed as B55.

### Material proxies — nothing moves
`TextureScroll` and `Sine` are ported and tested (`MaterialProxies`), and the texture transforms and
modulation colour are plumbed to the shader, but nothing parses the `Proxies` block from a VMT or
evaluates it per frame — so every transform sits at identity. **A capture point's beam does not
scroll and its sign does not pulse.** Filed as B80.

### `$lightwarptexture` — lighting curve is linear where TF2's is authored
308 materials. A one-dimensional ramp the engine looks up with `N·L`, which is a large part of
TF2's flat, illustrative look.

### `$rimlight` — no edge light
301 materials, with boost and exponent. Cheap next to `$phong` and visible on silhouettes.

### `EyeRefract` — 13 materials
The only unimplemented **shader** on cp_process. Already falls back to the iris texture
(`VmtMaterial.PrimaryTexture`), which is why eyes are not the missing-texture chequer, but the
refraction and the cornea are absent.

### `$basetexturetransform`, `$texture2transform`
19 and 5 on brushwork. The transform machinery exists; nothing parses the matrix form
(`center … scale … rotate … translate`) out of a VMT yet.

### Attachment points — cosmetics at the wearer's feet
Not a material at all. `mstudioattachment_t` and `m_iParentAttachment` are unread, so an item whose
bones match nothing — a halo, an MvM canteen — is placed by the wearer's transform alone. Measured:
`hwn_spellbook_complete.mdl` has one bone, named `mvm`, a root. Filed as B82.

## Deliberately not ours

`%compile*` flags are instructions to vbsp, obeyed before the map shipped. `$surfaceprop` picks a
footstep sound. `%keywords` is a Hammer search tag. These are counted separately by the census so a
later reader can tell "ignored on purpose" from "not got to yet".
