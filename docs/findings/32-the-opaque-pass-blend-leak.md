# 32 — Every static prop was alpha-blended for two days, and it read as four art faults

**2026-08-23.** Evidence class: measured on the corpus, then confirmed by the owner looking at the
running viewer. The engine citation is read from published source.

## The picture

Flying the free camera around `cp_process_f12`, the owner reported four things at once:

- pipes rendering "partially transparent", like glass tubes
- the RED and BLU signs see-through, with the wall's edge visible as a hard line across them
- the observatory dome glassy, its own far inner surface visible through it
- a silo's upper collar **absent entirely**, leaving the dome floating above a gap of sky

Every one of those is a different-looking defect. They are one line of render state.

## The bug

`WorldRenderer.Draw` follows the engine's pass order, transcribed from
`CBaseWorldView::DrawExecute` (`game/client/viewrender.cpp:5487`):

```
DrawOpaqueBatches(_batches)   // world surfaces
DrawDecals(context)           // their overlay fragments
DrawOpaqueBatches(_props)     // static props
DrawTranslucent(context)
DrawAdditive(context)
```

`DrawDecals` calls `OMSetBlendState(_alphaBlend, …)` and never restores it. The next reset lives
inside `DrawTranslucent`, two passes later. So `DrawOpaqueBatches(_props)` — every static prop in
every map — ran with alpha blending switched on, blending each prop against the frame using its base
texture's alpha channel.

## Why it hid for two days

**Because of which channel that is.** In a TF2 *model* material the base texture's alpha is
ordinarily an ENVMAP MASK — `$basealphaenvmapmask` — and not opacity at all. Valve's own helper
reads it inverted, "an opaque texel reflects least". So:

| the prop | its base alpha | what the leak did |
|---|---|---|
| polished pipe, dome, sign face | low, because it is shiny | ghosted, or vanished |
| wooden crate, concrete, dirt | high, because it is not | looked perfect |

The bug therefore did not present as "everything is transparent", which anyone would have
recognised in a second. It presented as an apparently unrelated set of art problems on exactly the
surfaces a person notices — metal and signage — while the map around them was correct.

**And the world was never affected**, because brushwork draws in the pass *before* the decals. Only
props were downstream of the leak. That made it look like a model or material problem, and four
separate investigations went into the model pipeline: alpha-test classification, DXT block upload,
VTX strip winding, and back-face culling. All four were innocent and each took a build-and-look
cycle to clear.

## What the commit was, and why it was correct

`e7b95cf` (2026-08-21 19:57, B135) moved static props to draw **after** the overlays, because that
is what the engine does. That change was right and stays. Under the previous order — world, props,
decals — nothing ran between the decal pass and the reset, so the leak existed but had nowhere to
land. Moving the props gave it somewhere.

**That is the general shape worth keeping: a latent state leak is invisible until a reordering puts
something in the gap, and then it surfaces as a defect in the code that MOVED rather than in the
code that leaked.** Every investigation started from the props, because the props were what changed.

## What found it

Not a test — none could fail on it, see below. The owner found it with two screenshots of the same
view:

- **wireframe**: the collar's region is empty sky
- **wireframe + surface colours**: a large orange prop cylinder exactly where the collar belongs

Same draw call, same triangles, different colour path. The category view returns from the pixel
shader at `return float4(input.vc, 1.0f)` — before the lighting, after the clip, with **alpha forced
to one**. So the fragment demonstrably survived the alpha test and reached the output merger, and the
only thing left between there and the screen is the blend. That narrowed a two-day hunt to one
question in a single pair of pictures.

## Why the existing tests could not catch it

`OverlayOcclusionRenderTests` draws a wall, a marking and an occluder, all hand-built, all with a
vertex alpha of one. **Blending against an alpha of one is arithmetically identical to not
blending**, so the correct renderer and the broken one predict the same pixel. That is this
project's first named way for a test to be unable to fail — a wrong condition, not a weak assertion,
and strengthening the assertion would not have helped.

`OpaquePassBlendStateRenderTests` fixes the condition rather than the assertion. It searches the
map's own materials for one that is neither translucent nor alpha-tested and still carries a low
alpha across its image — an opaque material with a masking alpha, which is the exact class that
broke — and skips loudly if the map has none, rather than passing.

Verified by manipulation: with the fix removed the test reports `PROP PIXEL 0,3,8` and fails on its
control, which is the defect stated precisely — the prop did not draw.

## The rule that already existed

From `docs/memory/build-time-shortcuts-assume-the-camera.md`, written weeks earlier after
`DrawTranslucent` leaked a *depth* state onto models:

> let a pass establish the state it needs rather than trusting the previous pass to have restored it

Same file, same failure, different piece of state. The rule was written down, and the pass added
afterwards did not follow it. A rule recorded in memory is not a rule enforced by anything — which is
why the fix here is that `DrawOpaqueBatches` sets its own blend state on entry, and the test above
now enforces it.
