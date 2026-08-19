# `$vertexcolor` — a census number that turned into a question

With `$envmap` implemented, the largest remaining entry in `cp_process_final`'s unimplemented-parameter
census is `$vertexcolor` at 66 of 410 materials, tied with `$vertexalpha` at the same 66.

Measuring it before building anything turned a gap into an open question. That is the whole of this
file, and it is worth writing down because the measurement cost one probe and the implementation it
replaced would have cost considerably more — for something that may be correct already.

## What the engine does (evidence: read from published source)

**The gate is in the vertex shader, and the pixel shader has none.** `lightmappedgeneric_vs20.fxc:213`:

```hlsl
if (!g_bVertexColor)
{
    o.vertexColor = float4( 1.0f, 1.0f, 1.0f, cModulationColor.a );
}
else
{
    o.vertexColor = v.vColor;
}
```

and then, unconditionally, at `lightmappedgeneric_ps2_3_x.h:427-429`:

```hlsl
albedo.xyz *= i.vertexColor;
alpha *= i.vertexColor.a * g_flAlpha2; // not sure about this one
```

The comment on the alpha line is Valve's.

So **the flag chooses the value, not whether the multiply happens**. A material without
`$vertexcolor` is multiplied by white. That distinction matters for an implementation: "skip the
multiply when the flag is absent" and "multiply by white when the flag is absent" produce the same
picture, but only the second describes what the engine does, and only the second stays correct when
someone later adds a modulation term to the same channel.

## What this project does

It multiplies by its own per-vertex colour with **no gate at all**. For a brush face that channel
holds white, so the two agree for every material that does not declare the flag — which is the great
majority.

The channel is not unused, though, and that is worth knowing before anyone "implements
`$vertexcolor`" by writing into it: for a **static prop** it carries the compiler's baked per-vertex
lighting, which is where a prop's light comes from. A prop's material goes through `VertexLitGeneric`
and a different lighting path, so the two uses do not currently collide — but they share one
interpolant.

## The question the measurement produced

Which materials declare it, on `cp_process_final`:

| Shader | `$vertexcolor` | `$vertexalpha` |
|---|---|---|
| `LightmappedGeneric` | 64 | 64 |
| `UnLitGeneric` | 2 | 2 |

**Always as a pair**, and they are overlays, decals, signs and stains — `overlays/stain016`,
`overlays/floor_stain003`, `signs/factory_label02`, `OVERLAYS/DUST_GRADIENT01` and `02`, plus
`TOOLS/TOOLSINVISIBLEDISPLACEMENT`.

**And the BSP supplies no colour for any of them.** `doverlay_t` (`bspfile.h:1007`) is an id, a
texinfo, a face list and texture coordinates. No colour, no alpha.

So either the engine supplies `v.vColor` for these surfaces at runtime, or the declarations are
inert. The published source does not settle it, because the world mesh builder is engine code the
SDK does not ship.

## The decompiler, and where it pointed

This was first written up as an open question ending "a decompiler would answer it". That was the
wrong place to stop — the decompiler is a normal tool here and the binaries were already on the
disk. Two scans answered it, and neither needed the analyser to run.

**Every colour-mesh construct in `engine.dll` is static-prop-only.** The strings are
`CColorMeshData::CreateResource`, `colormeshparams_t`, `CPooledVBAllocator_ColorMesh`, and a convar
whose help text reads *"0 - off, 1 - static prop color meshes are allocated from a s…"*. A colour
mesh in Source is per-vertex prop lighting. There is no world or brush equivalent in the string
table.

**And the engine's only use of the parameter BY NAME is debug drawing.** `$vertexcolor` and
`$vertexalpha` sit in engine.dll among their immediate neighbours:

```
wireframe
$vertexcolor
__utilWireframe
$vertexalpha
__utilWireframeIgnoreZ
unlitgeneric
__utilVertexColor
__utilVertexColorIgnoreZ
DrawScreenSpaceRectangle
C:\buildworker\rel_hl2_win32\build\src\tier2\renderutils.cpp
```

Those are `tier2`'s render utilities: procedurally-created materials named `__utilVertexColor` and
`__utilWireframe`, built on `unlitgeneric`, for debug lines and screen-space rectangles. Nothing to
do with world surfaces.

**Corroborated by shipped data, which is the part worth noticing.** `tier2`'s source is not in the
SDK, but its compiled library is — and `src/lib/public/x64/tier2.lib` carries the identical mangled
symbols in the same grouping:

```
??_C@_0BA@GOABPCHJ@__utilWireframe@
??_C@_0BC@LOJLMAAH@__utilVertexColor@
??_C@_0N@LPLAFHMO@$vertexalpha@
??_C@_0N@MLLHDDCF@$vertexcolor@
??_C@_0N@OECMIMEL@unlitgeneric@
```

So the decompiler's answer is independently confirmed by a file Valve ships. That is
[nothing is closed](../memory/nothing-is-closed.md) arriving from the other direction: the binary
pointed at a translation unit, and the translation unit's own library was in the SDK all along.

**What this establishes, and what it does not.** The engine has no world-surface vertex-colour
producer that any of these four probes can see, so the likeliest reading is that the flag is inert
for this geometry and the correct implementation is nothing — [`$modblend`](12-shader-parity.md)
again. `BaseVSShader.cpp:964` adds `VERTEX_COLOR` to the vertex *format* when the flag is set, and a
`CMeshBuilder` colour that is never written is white, which is consistent with everything above.

**The residual gap is named rather than papered over:** the flag is tested as a bit
(`IS_FLAG_SET`), not by string, so absence of the *name* is not absence of code. A string scan can
show where a name is used; it cannot prove nothing reads the bit. Settling that last step means
decompiling the world mesh builder itself, and the cost is not currently worth it against a feature
whose visible effect is already believed to be none.

## Not dead — alive somewhere else

Calling this `$modblend` again would be wrong, and the correction is the owner's: this may be a
feature Source uses for other games and other paths, rather than one nothing uses.

It is. Two shipped shaders consume the flag, and neither is on the road a TF2 world surface travels:

```cpp
// cable_dx6.cpp:34 — ropes and cables
if (IS_FLAG_SET(MATERIAL_VAR_VERTEXCOLOR))
    flags |= SHADER_DRAW_COLOR;

// decal.cpp:83 — the FIXED-FUNCTION decal shader
if (IS_FLAG_SET( MATERIAL_VAR_VERTEXCOLOR ))
    pShaderShadow->CustomTextureOperation( SHADER_TEXTURE_STAGE0,
            SHADER_TEXCHANNEL_COLOR, SHADER_TEXOP_MODULATE,
            SHADER_TEXARG_TEXTURE, SHADER_TEXARG_VERTEXCOLOR );
```

Cables are `move_rope`/`keyframe_rope`, which is HL2 content. `decal.cpp` is the pre-DX9
fixed-function path. So `$vertexcolor` is a live, consumed parameter of the engine — it is simply
not reachable from a DX9 `LightmappedGeneric` world face, which is the only thing these 66 materials
are ever drawn as here.

**That difference matters for how the finding is written down.** "Dead parameter" invites someone to
delete it; "live parameter, wrong path" is the accurate statement, and it predicts that a Source
project drawing ropes or running the fixed-function fallback *would* owe an implementation.

There is a larger shape here worth keeping for the history write-up. Source was built on the premise
that it would be *the* engine, updated in place rather than replaced — modular, composed,
component-versioned, with `IMaterial` flags shared across every game built on it. That premise held
for a remarkably long time and then did not, which is why Source 2 exists. What is left behind in
the seams is exactly this: parameters that are meaningful somewhere in the family and inert in the
specific combination of game, shader generation and content pipeline in front of you. Composition,
modularity and single responsibility buy a great deal of that lifetime; they do not survive an
architecture change, and twenty years guarantees one.

**Practically, for this project:** a census counts what a map declares. A declaration is evidence
that some Source game, at some point, had a path that read it — not that this renderer has one to
match.

## The half that is already implemented, and why the number misleads

`$vertexalpha` **already makes a material translucent** — `VmtMaterial.IsTranslucent` reads it, so
those 66 materials are correctly sorted into the blended pass today. It is implemented for the
sorting decision and unimplemented for the colour.

That is exactly the shape `$alpha` had before the modulation work
([26](26-material-modulation.md)): consumed for one decision, ignored for another, and a census that
counts *declarations* cannot see the difference. So "66 materials want this" does not translate into
66 materials drawn wrongly, and the census's ranking overstates this entry relative to `$envmap`,
which really was doing nothing at all.

**The general point, which has now come up three times in this project:** a census reports what a map
asked for. Whether the asking matters is a separate question, and answering it is cheap — one probe
here, against an implementation of unknown size.
