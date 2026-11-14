# 108 — A particle effect is a renderable, not a material batch

**B734, 2026-11-14.** This finding came out of the head-label order: B549 asked where `RenderPlayerSprites` draws
relative to the particles. The question it ended on is bigger than the labels.

## What was believed

From B373 onward, particles were "last of the world". Every visible effect's quads were merged by material name into one
batch per material, and those batches drew once, after the translucent pass. In the main view they drew after the
viewmodels. The comment over the merge said *"The engine batches the same way"*. That was never read; it was a
plausible guess about draw calls.

## What the source says

*Read from published source* (`source-sdk-2013`, `src/`):

- `CNewParticleEffect` is a `CDefaultClientRenderable` (`game/client/particles_new.h:26`). Its constructor registers
  it with `ParticleMgr()->AddEffect`, and that calls `ClientLeafSystem()->CreateRenderableHandle`
  (`particlemgr.cpp:1270-1281`). That function files it in `RENDER_GROUP_TRANSLUCENT_ENTITY` when `IsTransparent()`
  (`clientleafsystem.cpp:651`). **Every effect is its own entry in the same list as a cloaked spy or a window.**
- That list is collated at each renderable's nearest listed leaf and sorted per leaf by the box centre's projection on
  the view axis (`SortEntities`, `:1758-1810`). `DrawTranslucentRenderables` then walks it back to front, interleaved
  with the translucent world and the detail sprites (`viewrender.cpp:4465-4702`).
- `RenderView` draws the view, then `RenderPlayerSprites`, then `DrawViewModels` (`viewrender.cpp:2116-2160`).
- A definition with "view model effect" set is moved to `RENDER_GROUP_VIEW_MODEL_TRANSLUCENT` (`particlemgr.cpp:1277`)
  and draws only inside `DrawViewModels`. *Measured with the probe:* TF2's muzzle flashes have it 0, so a first-person
  flash is a world translucent. It sits at the muzzle the forward `FormatViewModelAttachment` already moved.

## What changed

The main view now draws each effect as its own entry of the translucent walk. Smoke behind a window draws under the
glass, and two effects blend far to near rather than in material order.

The 3D sky, water and monitor views have the same structure and are not ported yet. Nor are entity sprites, strips and
beams, which the engine also sorts into the walk. RISKS B734 lists what is open.

## The lesson

A merge that saves draw calls looks like a performance decision. Here it was a structural one, because the draw order
is the output. "The engine batches the same way" was a claim about Valve's code that nobody had read, and it is the
same shape as the claims `docs/memory/nothing-is-closed.md` warns about.
