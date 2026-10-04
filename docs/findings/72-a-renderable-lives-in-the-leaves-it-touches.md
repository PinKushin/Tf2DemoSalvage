# 72 — A renderable lives in the leaves it touches

B262 filed the draw side as re-culling, re-classifying and comparison-sorting a flat list the engine never builds.
This is the port of the structure that list stood in for, `CClientLeafSystem`, and what reading it turned up.

## What the engine keeps (read from published source)

`src/game/client/clientleafsystem.cpp`, source-sdk-2013:

- **Renderables are linked into every leaf their world box touches, and stay there across frames.** `AddRenderable`
  (`:708`) puts a new handle on the dirty list; `RenderableChanged` (`:1274`) puts a moved one there, once.
  `PreRender` (`:528`) unlinks every dirty renderable, then re-inserts walking the dirty list **backwards**
  (`:557`). Each insert goes to the **head** of the leaf's list (`CBidirectionalSet::AddElementToBucket`,
  `utlbidirectionalset.h:205-208`). The two reversals cancel: a leaf holds renderables added in one frame in the
  order they were added, and one that moves later goes to the front.
- **Collation walks the view's leaf list in order** (`BuildRenderablesList`, `:1813`), and within a leaf its list
  head first (`CollateRenderablesInLeaf`, `:1574`). An opaque renderable is taken at the first listed leaf it is met
  in (`m_RenderFrame2`, `:1607-1613`). A translucent one is taken only at its render leaf (`:1620`), which
  `ComputeTranslucentRenderLeaf` set to its first encounter in the same list (`:1444-1455`). Without
  `RENDER_FLAGS_ALTERNATE_SORTING` (`:1457`) those are one rule, so the port keeps one stamp. *Arithmetic*; the
  falsifier is that flag, which nothing here sets.
- **Then the frustum test, then the bucket, at collation.** `CullBox` (`:1647`), then for an opaque renderable
  `DetectBucketedRenderGroup` on the world box's longest axis (`:1683-1694`). The draw side walks buckets that are
  already filled (`viewrender.cpp:4188`) and does no sort.
- **A two-pass translucent renderable joins `RENDER_GROUP_OPAQUE_ENTITY` too** (`:1710-1713`). That is the
  unbucketed group, so it is the smallest bucket whatever the model's size. The per-frame sort had bucketed it by
  size, which draws a large two-pass model's solid half earlier than the engine does.

## The wrong turn: which box walk files a renderable

The first version linked boxes with `BspLeafTree.LeavesTouchingBox`, the port of the engine's displacement-to-leaf
walk (B457). A differential on cp_process came back with 3,882 boxes expected and 3,863 reached, plus wrong places.
The old per-frame walk (`TouchesAny`/`NearestRank`) and this one break a tie at a node plane differently. The test
boxes were leaf centres ±20, and those sit on the map grid, so they lay on node planes all the time.

**Neither of the two was the leaf system's walk.** The published copy of `EnumerateLeavesInBox_R`
(`utils/common/bsplib.cpp:3461-3499`) takes both children whenever the box is within `TEST_EPSILON` (1/32, `:3403`)
of the plane. A box touching a plane goes in the leaves on both sides. It is now
`BspLeafTree.EnumerateLeavesInBox`. The engine's own `ISpatialQuery` implementation is closed and has not been
disassembled, so treating the tools copy as the engine's is **interpolated**.

With the test boxes moved 0.1 unit off the grid, the leaf system and the old walk reach exactly the same set at
exactly the same translucent places (*differential*, `ClientLeafSystemMapTests`). The contact case is pinned on its
own, at a real cp_process node plane, against the old rule as a control.

## The second wrong turn: the tools copy is not the client's

The 1/32 band came from the published `utils/common/bsplib.cpp`, which is what the map compiler links. Asked
directly, engine.dll says otherwise (*disassembly*). Its `CEngineBSPTree` vtable (`0x18038e768`, found from the
RTTI string `.?AVCEngineBSPTree@@`) puts `EnumerateLeavesInBox` at `0x1800d96a0`. That function converts the box to
a centre and half extents and walks `0x1800dd690`, which does three things neither earlier walk here did:

- it has **no epsilon**: back only when the far corner is at or behind the plane, front only when the near corner
  is at or in front of it;
- it **tests every node's and leaf's own box first** (`0x180172540`, rejected only when `|c1 − c2| > e1 + e2`);
- it **never files a renderable in a solid leaf** (contents 1).

Running that against cp_process turned up a third thing: **this project's view lists solid leaves**, where the
engine's world walk does not. The old per-frame walk had been reaching solid leaves through on-plane contact, and
had been ranking translucent models against them. The world walk fix is filed under B262.

That was fixed in the world walk once it had been read: `0x1800e0600` returns on contents 1, so no solid leaf is
ever listed. Its marking pass `0x1801c8cb0` also answered the eye-in-solid question. A view cluster of −1 marks
every leaf and node visible, the same as `r_novis`, which is the rule this project already had, now confirmed by
reading rather than assumed.

## Translucency belongs to the model, not the entity

An entity's group is recomputed every frame (`ComputeFxBlend` → `SetRenderGroup( GetRenderGroup() )`), but the
material half of it is one bit on the model. `IsTranslucent` (`0x1801cabf0`) is `[model + 0x24] & 2`, and nothing
but `Mod_RecomputeTranslucency` (`0x1801046f0`) writes it. The SDK client calls that for detail models alone. So
an entity's skin or body never re-asks it. The second pass here had guessed that they did; the guess was replaced
by this reading.

## The sky room has its own entities

`CSkyboxView::DrawInternal` (`viewrender.cpp:4920-4932`) builds the sky view's world lists and renderable lists,
then draws opaque and translucent renderables, all through the sky camera. The sky room's props are that view's,
collated over the sky leaves with the sky frustum. Before this port, the main view's visibility test admitted
them, and they were drawn at full size in the main view or not at all.

## What is ours

- **The scene hands a fresh list each frame.** Device3D reconciles registration against that list: an instance keeps
  its handle while (entity, model, occurrence) persists, is re-linked only when its box moved, and is removed when
  it is gone. A seek is a frame where many boxes moved, so the index cannot outlive a frame that contradicts it.
  That answers D131's hazard by construction, not by an invalidation hook.
- **A box with no extent is never linked and never culled**, and it is collated at place 0. An empty box says nothing
  about where its model is.
- **A map that cannot be culled is one leaf.** Before any view walk has listed leaves, every leaf is listed.
