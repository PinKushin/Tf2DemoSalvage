# 17 — Translucency

5,039,041 drawn units over 94 materials on `cp_process_final` — the largest remaining renderable
gap now that `$detail` and `$bumpmap` are done. Today every one of them is approximated by the
alpha-test clip, which is right for a grate and wrong for glass.

## What decides that a surface is translucent

`CBaseVSShader::EvaluateBlendRequirements`, `BaseVSShader.cpp`:

```c
bool isTranslucent = IsAlphaModulating();
isTranslucent = isTranslucent || (CurrentMaterialVarFlags() & MATERIAL_VAR_VERTEXALPHA);
isTranslucent = isTranslucent || ( TextureIsTranslucent( textureVar, isBaseTexture ) &&
                                   !(CurrentMaterialVarFlags() & MATERIAL_VAR_ALPHATEST ) );
...
return isTranslucent ? BT_BLEND : BT_NONE;
```

**Three things here are not what `$translucent 1` on its own suggests.**

1. **The texture's own alpha decides, not only the material key.** `TextureIsTranslucent` asks the
   VTF. A material declaring `$translucent` over a texture with no alpha channel is not blended.
2. **`$alphatest` cancels it.** The clause is explicit: texture alpha counts *only* when the
   alpha-test flag is absent. The two are alternatives, never both — which is exactly the
   distinction this project currently collapses, since `IsTransparent` returns true for either.
3. **`$alpha` and vertex alpha reach the same conclusion by other routes**, so a material can be
   translucent without mentioning `$translucent` at all.

## What the engine then does

From the helper, `lightmappedgeneric_dx9_helper.cpp`:

```c
bool bFullyOpaqueWithoutAlphaTest = (nBlendType != BT_BLENDADD) && (nBlendType != BT_BLEND) && ...;
bool bFullyOpaque = bFullyOpaqueWithoutAlphaTest && !bIsAlphaTested;
...
pShaderShadow->EnableAlphaWrites( bFullyOpaque );
```

So a translucent surface does not write alpha, and — being neither opaque nor alpha-tested — is
excluded from the opaque pass.

**The blend function itself is not in `source-sdk-2013`.** `SetDefaultBlendingShadowState` is
declared in `BaseVSShader.h` and defined inside the closed `materialsystem`, so the exact factors
are **interpolated**: `BT_BLEND` is source-alpha over one-minus-source-alpha, which is what the
name means everywhere else and what the surrounding code implies. Flagged rather than asserted.

**Sorting is the engine's job, not the shader's**, and it lives in the same closed code. Nothing
about ordering can be read from the SDK.

## What this renderer has to do differently

The viewer draws straight down with depth standing in for height, which is not a case Source ever
handles — so the ordering rule here is ours to choose and cannot be checked against anything.

- **A third pass**, after the opaque one and alongside the existing additive pass.
- **Depth test on, depth write off.** A translucent surface must not stop what is behind it from
  drawing, which is the same reason the engine turns off alpha writes.
- **Sorted far to near**, which in this projection means largest depth first, since height is
  inverted into depth.

**Per batch, not per triangle, and that is a real limitation.** Batches are one material each, so
two translucent materials overlapping each other resolve by material order rather than by actual
depth. Per-triangle sorting needs the translucent geometry rebuilt whenever the camera moves,
which the camera-matrix design deliberately avoids. Recorded here rather than hidden: it is
correct for the common case of glass on a wall seen from above, and wrong where two panes overlap.

## Status

Researched 2026-08-13, not implemented. The blend factors are interpolated; everything else is read
from published source.
