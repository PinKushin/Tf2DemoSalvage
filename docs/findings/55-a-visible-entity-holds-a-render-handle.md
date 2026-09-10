# 55 — a visible entity holds a render handle

**Subject:** what `IsVisible()` means in Source, why a set named for it served two rules that ask
different questions, and how a sentence nobody quoted survived in four files for a month.

**Evidence class:** read-from-source throughout, with `file:line`. The door measurements are measured on
`20130518_0313_cp_granary_blu_blu`.

---

## 1. The rule that decides who is interpolated

`C_BaseEntity::ProcessInterpolatedList` walks a list rather than the entity array, and its own comment
says why — *"Interpolate the minimal set of entities that need it."* Membership is `ShouldInterpolate`
(`c_baseentity.cpp:3029`), four clauses:

```cpp
if ( render->GetViewEntity() == index )  return true;
if ( index == 0 || !GetModel() )         return false;
if ( IsVisible() )                       return true;   // always interpolate if visible

// if any movement child needs interpolation, we have to interpolate too
C_BaseEntity *pChild = FirstMoveChild();
while( pChild )
{
    if ( pChild->ShouldInterpolate() )   return true;
    pChild = pChild->NextMovePeer();
}
return false;
```

Three of those four are cheap to read. The one that carries all the weight is the third.

## 2. `IsVisible()` is one line, and it is not about the picture

```cpp
inline bool IsVisible() const { return m_hRender != INVALID_CLIENT_RENDER_HANDLE; }
```

`c_baseentity.h:691`. A render handle is a slot in the client leaf system, and the only thing that
grants or removes one is `UpdateVisibility` (`c_baseentity.cpp:1370`):

```cpp
if ( ShouldDraw() && !IsDormant() && ( !ToolsEnabled() || IsEnabledInToolView() ) )
    AddToLeafSystem();
else
    RemoveFromLeafSystem();
```

and `ShouldDraw` (`:1435`) is:

```cpp
// Some rendermodes prevent rendering
if ( m_nRenderMode == kRenderNone )
    return false;

return (model != 0) && !IsEffectActive(EF_NODRAW) && (index != 0);
```

**So `IsVisible()` involves no frustum, no PVS, no previous frame and no skeleton.** It is four facts
about the entity's own state — render mode, model, `EF_NODRAW`, index — and `UpdateVisibility` is driven
by data updates, not by rendering. An entity standing behind the camera is `IsVisible()`. An entity
whose model has not arrived is not.

## 3. Three predicates, no shared inputs

The mistake this finding is about is treating "visible" as one question. Source has at least three, and
they agree on nothing:

| asks | tests | where |
|---|---|---|
| `IsVisible()` | leaf-system membership, per §2 | `c_baseentity.h:691` |
| `ShouldDraw()` | `kRenderNone`, model, `EF_NODRAW`, index | `c_baseentity.cpp:1435` |
| `IsRagdollVisible()` | `engine->IsBoxInViewCluster`, then `engine->CullBox`, on a ±1 box at the origin | `c_tf_player.cpp:1350` |

The third is a live PVS-and-frustum test, run inside `ClientThink` for the corpse fade timer. It has the
same shape as a name as the first and nothing else in common with it.

## 4. What we had, and the four things it got wrong

One `HashSet<int>` — `EntityModelSet.PosedEntities`, forwarded through `MomentScene`, handed by
`MomentPresenter` to `DemoTimeline.PropsAt` as the interpolation list *and* passed on to
`RagdollProps.Fill` for the fade. It was filled on this line, immediately after
`animating.SetupBones(...)`:

```csharp
Posed++;
_posedEntities.Add(prop.EntityIndex);
```

inside `if ( _frames.TryGetValue(...) && entry.Skinned is { } skinned && ... )` — the skinned branch.

That produced four divergences simultaneously:

1. **No brush entity was ever interpolated, in any map.** Brushwork has no skeleton, so it never reached
   that line. Every `func_door`, `func_movelinear` and `func_tracktrain` in every demo was sampled
   through `Held` for its entire life, along with every baked prop.
2. **The whole world dropped out on any frame with a viewmodel.** `AddViewmodel` calls `Instances` a
   second time with two props, and `Instances` clears the set. The next sample received the arms and the
   gun.
3. **Anything culled fell off a list the engine keeps it on**, so an entity returning to view rejoined
   the lerp late.
4. **The fourth clause did not exist.** It was quoted in `InterpolationListTests` and truncated at
   *"// if any movement child needs interpolation, we have to interpolate too"* with nothing under it.
   It is the only clause an invisible mover can pass, and `cp_fulgur` has eighteen `func_door`s at
   `kRenderNone` carrying the grate props.

## 5. The wrong conclusion, and what killed it

Everything above rested on one sentence, which appeared in `InterpolationListTests`,
`EntityModels.PosedEntities`, `MomentScene.PosedEntities` and `RagdollFade`, worded slightly differently
each time:

> *"`IsVisible()` is the LAST render's answer, so gating this frame's interpolation on the previous
> frame's visibility is not an approximation of what Valve does — it is what Valve does."*

It was never a quote. Nothing cited a line. And it is the kind of claim that defends itself: it names a
frame-latency subtlety, which sounds like the product of careful reading, and it makes the resulting
code look deliberate. What killed it was one grep for the accessor — a single inline line in a header.

**The general shape: a latency argument hides an identity question.** "The engine uses last frame's
answer" is a claim about *when* a predicate is evaluated. It cannot be assessed at all until you know
*what* the predicate is, and stating the *when* confidently makes the *what* look settled.

## 6. Where the question moved to

`ShouldInterpolate`'s clauses 2, 3 and 4 are all facts about the recording, so `DemoTimeline` answers
them. `PropsAt`'s third parameter became `int? viewEntity` — clause 1, the only one a demo cannot
answer — and `PosedEntities` was deleted from three files. `RenderModes` moved from
`Tf2DemoSalvage.Scene` down to `Tf2DemoSalvage.Core.Scene`, because `ShouldDraw`'s first test is
`kRenderNone` and Core cannot see Scene.

The child walk is iterative with a visited set rather than recursive. `SetParent` refuses a cycle in the
engine; our decode accepts whatever parent a demo states, and decoding must be total, so a cycle has to
be a wrong answer rather than a hang.

**The corpse fade kept a set, and got the right one.** `IsRagdollVisible` genuinely wants the cull, so
`EntityModelSet.InView` is filled where the cull's answer is — and its clear is guarded on the world
pass, which fixed divergence 2 for the fade too. Until then, every corpse in the match reported unseen
on any frame with a viewmodel drawn, and expired on the 15-second never-seen timer instead of the
4.95-second one.

## 7. What it did not fix, and two instruments that lied

The report that started this was a door on `cp_granary` that does not open. **This is not that fix.**
Granary's door 205 is `mode 0` — it passes clause 3 outright and was interpolated all along. With the
entity forced off the list entirely, the drawn path over ticks 27,660–27,740 is **113.9 units against
`At`'s 114.6**. The timeline opens that door on either path; the defect is downstream of it.

Two instruments were wrong on the way here, and both are the same fault as B370's:

- **`jitter`'s drawn-vs-sampled check passed no interpolation set**, so it exercised a path the viewer
  does not take and reported 0 disagreements in 1,380,850 comparisons. The check was written *because*
  measuring `At` instead of `PropsAt` had wasted a day; it then measured `PropsAt` with the wrong
  argument.
- **"The child prop is composed once and never recomposed" was a window artifact.** Entity index 205
  owns seven tracks over that recording — `[736..3262]`, `[8252..37032]`, `[38231..48343]`,
  `[50287..55762]`, `[57089..59381]`, `[63750..71617]`, `[72912..84254]` — and the measurement had
  picked the first while the viewer looked at the second, in a 14-second window containing no door
  motion at all. The motion is at tick 27,684. `docs/memory/an-entity-index-does-not-name-a-track.md`
  was written about exactly this and did not prevent it, because the instrument accepted a bare index
  and silently chose an occupant.

## 8. Still open

The fade reads the previous frame's cull where `IsRagdollVisible` runs live, so a corpse in its final
second can expire one frame late. And B259's optimisation is gone — nearly every prop blends now, where
the old set held a few dozen — with nothing yet measured about what that costs.
