# 45 — A mesh names a skinref, not a texture

*Written 2026-08-29, from B229.*

A `.mdl` mesh carries an integer called `material`. It is natural to read that as "which of the
model's textures paints this mesh", and for the overwhelming majority of props that reading gives
exactly the right answer. It is still wrong, and where it is wrong it is wrong completely.

## What the field actually is

`mstudiomesh_t` opens with it (`public/studio.h:1365`, **read from published source**):

```c
struct mstudiomesh_t
{
    DECLARE_BYTESWAP_DATADESC();
    int                 material;

    int                 modelindex;
    ...
```

and the header carries a separate table beside the textures (`public/studio.h:2237`):

```c
    // replaceable textures tables
    int                 numskinref;
    int                 numskinfamilies;
    int                 skinindex;
    inline short        *pSkinref( int i ) const { return (short *)(((byte *)this) + skinindex) + i; };
```

The engine's own indexing lives in the closed `studiorender`, so the flat `pSkinref( int i )` does
not by itself say how a two-dimensional table is addressed. **Valve says it anyway, in a comment,
in an open file** — `utils/motionmapper/motionmapper.h:134`:

```c
EXTERN  int g_numskinref;
EXTERN  int g_numskinfamilies;
EXTERN  int g_skinref[256][MAXSTUDIOSKINS]; // [skin][skinref], returns texture index
```

`[skin][skinref], returns texture index`. That is the whole rule: a mesh's `material` is a
**skinref**, the entity's or placement's skin selects the row, and the entry is the index into
`textures[]`.

This is worth recording as a source lesson on its own. The question looked closed — the mapping is
implemented in a module Valve never released — and the answer was sitting in a comment in a
utility nobody thinks to read, one directory away from the tools that *are* usually consulted.
Which is the fifth-source point CLAUDE.md makes about shipped data, applied to shipped *code*:
the open tree is much larger than the parts of it that get read.

## Why the wrong reading survives almost everything

Nearly every prop has one skin family, and a one-family table is the identity: `skinref[0][r] == r`.
So "the mesh's `material` is a texture index" and "the mesh's `material` is a skinref resolved
through family zero" agree on every model in that majority, and disagree only on a model that has
several families **and** whose first row is not the identity.

TF2 has plenty of multi-family models — a team colour is a skin family, not a tint
(`tf_player_shared.cpp:4849`: `m_nSkin = ( team == TF_TEAM_RED ) ? 0 : 1`) — but their family-zero
rows are identities too, so the difference stays hidden until the row itself is unusual.

## The condition that exposed it: a map that packs only the skins it places

`cp_fulgur` is a community map. Its props come from community asset packs, packed into the BSP's
own pakfile, and the author packed **only the textures the map actually uses**. Measured
(**measured on the corpus**, via `ChequeredPropMaterialProbe`):

```
props_aquatic/pipe_256.mdl: 15 families x 15 references, 15 textures, 1 mesh
    texture[0]  'pipes01'  absent
    texture[1]  'pipes02'  SHIPPED
    texture[12] 'pipes13'  SHIPPED
    ... the other twelve absent
    skin[n] -> n, 1, 2, 3, ...          (only reference 0 varies by family)
    meshes reference: 0
    placements ask for skins: 1, 12
```

Nothing about that is broken. The model offers fifteen pipe finishes, the map uses two, the author
packed two. TF2 loads the model, finds thirteen missing materials, substitutes the error material
for each — and never draws any of them, because no placement asks for those families.

`props_antiquity/skycards_jungle256bump.mdl` is the same shape at skins 4 and 5: the flat panels
standing on edge in the 3D skybox.

## What this project did instead, and why it produced magenta

The loader resolved **family zero** for each mesh and expressed every other family as a *swap from
that resolved index*. On `pipe_256.mdl` family zero is `pipes01`, which the map does not pack, so it
resolved to −1; a swap keyed on −1 was refused as meaningless; and every pipe on the map drew in the
missing-material chequer — 19,274 triangles — while the game draws them correctly, because the game
never asks family zero anything.

The design has a second fault with no symptom yet. Keying a swap on the *resolved material* asks
"what does texture X become at skin 1", and that question has two answers as soon as two meshes
share a texture at family zero and differ above it. Nothing in the corpus does, so it would have
appeared as a mesh painted with a neighbour's texture — plausible, and much harder to notice than
magenta.

Both faults come from the same substitution: treating one family's *answer* as the identity of the
thing being looked up. The engine keys on the *question* — the skinref — and family zero is a row
like any other.

## Two smaller engine facts picked up on the way

- **An out-of-range skin falls back to family zero rather than being refused.**
  `props_shared.cpp:1079` (**read from published source**):
  ```c
  int nActualSkin = nSkin;
  if ( nActualSkin > studioHdrModel.numskinfamilies() )
      nActualSkin = 0;
  ```
  Note the `>` rather than `>=`, which is Valve's own off-by-one; the last family is reachable
  either way, so nothing depends on it. The behaviour worth copying is the fallback, not the
  boundary: a placement naming a family the model does not have is untrusted input (D32), and the
  engine's answer is family zero.

- **A model may carry no skin table at all**, and that is ordinary rather than malformed. For those
  the mesh's reference already *is* the texture index, so the identity is the correct answer and a
  refusal would lose the model.

## The wrong turn, kept

Four hypotheses were spent before this, each killed by an instrument, and the second of them —
*"`Register`'s two warnings both fired zero times"* — was the one that pointed away from the truth.
Those warnings name the model whose mesh cannot resolve a material. They fired zero times because
`PropModels.Load` took an optional `ILogger` that its single caller never passed, so the entire
static-prop path wrote to a `NullLogger`.

The log looked healthy: the same `props` area carried 125 `pairing` lines, which is exactly the
count of *entity* models — a different code path, handed a real logger twenty lines away in the same
method. "Did that subsystem say anything" answered yes for the whole period one half of it was mute.

The general lesson is filed in `docs/memory/an-instrument-unread-is-not-an-instrument.md` and
`a-null-object-default-hides-a-missed-wiring.md`. The specific one is narrower and worth stating
plainly: **an optional logger parameter with exactly one caller is not a convenience, it is an
unwired sink**, and the null-object default is what makes it silent instead of obvious.
