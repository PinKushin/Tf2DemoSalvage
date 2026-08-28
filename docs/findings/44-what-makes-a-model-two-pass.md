# 44 — What makes a model two-pass, and why we were doing it to everything

*2026-08-28. Evidence: read from published source throughout, plus one census measured over TF2's
own archives. Nothing here is interpolated.*

## The claim that started it, and how it was wrong

The handoff written the previous evening filed two-pass models as the next task, in these words:

> *"This project has no two-pass concept and draws every model once."*

Both halves are false, and they are false in the same direction — the renderer was doing **more**
two-pass work than the engine, not less.

`Device3D.RenderFrame` drew every model **twice**: once with `blended: false` and once with
`blended: true`. `WorldRenderer.DrawModel` filtered each pass by material:

```csharp
bool wantsBlending = _additive.Contains(material) || _translucent.Contains(material) ||
    _modulate.ContainsKey(material);

if (wantsBlending != blended) { continue; }
```

That filter is not an approximation of two-pass drawing. It **is** two-pass drawing —
`STUDIORENDER_DRAW_OPAQUE_ONLY` and `STUDIORENDER_DRAW_TRANSLUCENT_ONLY`, `istudiorender.h:101-102`
— correct machinery, pointed at every model in the scene.

The missing piece was the *question*: which models does the engine split? The answer is 0.62% of
them.

**The lesson is not "read the code before believing the handoff", though that is true.** It is that
a gap can be filed backwards. "We do not do X" and "we do X unconditionally" produce the same
next task — *implement X* — and only one of them is a starting point that leads anywhere. What
distinguishes them is reading the code, and the previous session's note was written from the SDK
alone.

## The engine's decision, in three steps and three files

### 1. Classify the entity — `C_BaseEntity::GetRenderGroup`, `c_baseentity.cpp:5677-5701`

```cpp
if ( nFXBlend == 0 ) return RENDER_GROUP_OPAQUE_ENTITY;  // Don't need to sort invisible stuff

RenderGroup_t renderGroup = (modelType == mod_brush) ? RENDER_GROUP_OPAQUE_BRUSH
                                                     : RENDER_GROUP_OPAQUE_ENTITY;
if ( ( nFXBlend != 255 ) || IsTransparent() )
    renderGroup = ( m_nRenderMode != kRenderEnvironmental ) ? RENDER_GROUP_TRANSLUCENT_ENTITY
                                                            : RENDER_GROUP_OTHER;

if ( ( renderGroup == RENDER_GROUP_TRANSLUCENT_ENTITY ) &&
     ( modelinfo->IsTranslucentTwoPass( model ) ) )
    renderGroup = RENDER_GROUP_TWOPASS;
```

**Two-pass is reachable only from translucent.** An opaque entity carrying a two-pass model is drawn
once, whole. That single fact is what the old code had no way to express.

The two inputs are asymmetric, and the asymmetry matters:

```cpp
bool C_BaseEntity::IsTransparent( void )        // c_baseentity.cpp:1823
{
    bool modelIsTransparent = modelinfo->IsTranslucent(model);
    return modelIsTransparent || (m_nRenderMode != kRenderNormal);
}

bool C_BaseEntity::IsTwoPass( void )            // :1829
{
    return modelinfo->IsTranslucentTwoPass( GetModel() );
}
```

Being two-pass is a property of the **model alone**. Being translucent is the model's materials
**or** the entity's render mode.

### 2. Store it — `CClientLeafSystem`, `clientleafsystem.cpp:713` and `:1331`

`RENDER_GROUP_TWOPASS` is a **request, never a stored state**. Both `AddRenderable` and
`SetRenderGroup` rewrite it immediately:

```cpp
if ( group == RENDER_GROUP_TWOPASS )
{
    group = RENDER_GROUP_TRANSLUCENT_ENTITY;
    flags |= RENDER_FLAGS_TWOPASS;
}
```

and `SetRenderGroup` **clears** the bit for every other group (`:1343`), so the flag cannot survive a
reclassification. A model that stops being translucent stops being two-pass in the same step.

### 3. Emit list entries — `CollateRenderablesInLeaf`, `clientleafsystem.cpp:1701-1714`

```cpp
bool bTwoPass = ((renderable.m_Flags & RENDER_FLAGS_TWOPASS) != 0) && ( nAlpha == 255 );

if ( info.m_bDrawTranslucentObjects )
    AddRenderableToRenderList( … (RenderGroup_t)renderable.m_RenderGroup, handle, bTwoPass );

if ( bTwoPass )   // Also add to opaque list if it's a two-pass model...
    AddRenderableToRenderList( … RENDER_GROUP_OPAQUE_ENTITY, handle, bTwoPass );
```

**The alpha is tested twice, and against different questions.** Step 1 asked "is it fully opaque" to
choose a group; step 3 asks again to decide whether the split applies at all. So a two-pass model
that fades draws **once**, wholly, in the translucent pass. One test standing in for both is wrong
for exactly the models the flag exists to help.

Two smaller details worth having:

- **Alpha zero joins neither list.** `if ( nAlpha == 0 ) continue;` (`:1631`) fires before the group
  is consulted, with Valve's note that *"OPAQUE objects can have alpha == 0. They are made to be
  opaque because they don't have to be sorted."* So zero is neither opaque nor translucent; it is
  not drawn.
- **The opaque half is NOT size-bucketed.** The other branch runs `DetectBucketedRenderGroup` and
  lands the renderable in one of four buckets by size; the two-pass branch writes the literal
  `RENDER_GROUP_OPAQUE_ENTITY`, which `IClientLeafSystem.h:37` annotates *"Opaque entity (smallest
  size, or default)"*. A huge two-pass model therefore draws with the crates, not with the trees.

## What the flag is, and what an author types

`STUDIOHDR_FLAGS_TRANSLUCENT_TWOPASS = 0x00000008`, `studio.h:2035`, with Valve's own comment as the
specification:

> *"Use this when we want to render the opaque parts during the opaque pass and the translucent parts
> during the translucent pass"*

It sits at byte **152** of `studiohdr_t`, between `view_bbmax` (140) and `numbones` (156). This
reader had described that offset in prose for months — `StudioLayout`'s note on
`HeaderBoneCountOffset` reads *"flags sits between view_bbmax and numbones"* — while decoding
nothing from it.

In the QC it is **`$mostlyopaque`**. TF2's own workshop importer records the content rule as a
to-do (`tf/workshop/item_import.cpp:10`):

> *"QC with any $translucent 1 VMT should have $mostlyopaque"*

Its sibling settles a question that would otherwise need measuring. `STUDIOHDR_FLAGS_FORCE_OPAQUE`
(`0x4`) is commented *"Use this when there are translucent parts to the model but we're not going to
sort it"* — a flag whose whole job is to suppress an answer, which is only meaningful if the answer
is **any material**, not all of them. So `IVModelInfo::IsTranslucent` is an OR over the model's
materials. And `RecomputeTranslucency( model, nSkin, nBody, … )` (`ivmodelinfo.h:125`) takes skin and
body, so it is an OR over the materials **currently shown** — a hidden bodygroup's glass visor does
not drag the model into the translucent pass while it is hidden.

## The census: 88 of 14,109

Measured over `tf2_misc_dir.vpk` and `tf2_textures_dir.vpk` by `StudioModelFlagCensus`:

| flag | models | share |
|---|---:|---:|
| `TRANSLUCENT_TWOPASS` | 88 | 0.62% |
| `FORCE_OPAQUE` | 1 | 0.01% |
| `STATIC_PROP` | 2,541 | 18.01% |

**So the change is: 99.4% of models stop being split.** They are drawn whole, in one pass — the
opaque one if no material blends, the translucent one if any does.

It is not an academic 0.62%. The list includes `models/player/sniper.mdl` — a player model present
in most matches — along with `c_flamethrower`, `c_syringegun`, `c_proto_medigun`, `urinejar`, both
`lantern001` props and the whole `models/vgui/` family.

**`FORCE_OPAQUE` appearing exactly once in 14,109 models is worth recording for its own sake.** The
flag is real, documented, and TF2 essentially does not use it.

## What this costs, and why it is right anyway

A model with translucent materials and **no** `$mostlyopaque` now goes wholly into the translucent
pass — its solid meshes included, drawn late, in distance order with its blended ones rather than
early with the world. Splitting it anyway, which is what this renderer did, produces a **tidier
picture**: the solid half fills depth early and occludes properly.

That is precisely the trade D89 says is not ours to make. And it is not a trade Valve overlooked —
`$mostlyopaque` is the mechanism by which an author opts into the tidier picture, and the engine
honours the author rather than deciding for them. A renderer that splits everything has quietly
decided that all 14,109 models are `$mostlyopaque`, including the 14,021 whose authors did not say
so.

## What is still missing, and it is named

`nAlpha` and `m_nRenderMode` are **parameters** of the transcription and every caller passes their
neutral values, because nothing decodes `m_clrRender`, `m_nRenderFX` or `m_nRenderMode` from the
demo. `ComputeFxBlend` (`c_baseentity.cpp:3343`) is a ~210-line time-based switch over the render-FX
kinds, and it is its own task.

The consequence is specific and small: no entity in this viewer can fade, so no two-pass model can
lose its split by fading, and no opaque-material entity can become translucent by render mode. Both
paths exist in the code and are covered by tests; neither can fire until the properties are read.
See RISKS.

## Verified by manipulation

Four sabotages, each predicted before it was run, each reverted with a precise inverse edit:

| broken | what failed |
|---|---|
| `HeaderFlagsOffset` 152 → 156 | the shipped-model correlation, immediately |
| the "only from translucent" guard removed | exactly two `For` cases: the opaque-model-with-flag one and the environmental one |
| `&& alpha == FullyOpaque` dropped from `Lists` | exactly the alpha-254 case |
| `model.IsTranslucentTwoPass` → `false` in the loader | the sniper/scout wiring test |

The offset sabotage is the one worth keeping in mind. Reading `numbones` as the flags word still
produced a **plausible** split — 19 static props against 181 — so the split is not the measurement.
The correlation is: `STATIC_PROP` means *"there's no bones and no transforms"* (`studio.h:2038`), so
the bit at 152 and the count at 156 must agree on every shipped model, and at the wrong offset they
do not.
