using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Only the entities that need it are interpolated — <c>g_InterpolationList</c>.
/// </summary>
/// <remarks>
/// **`C_BaseEntity::ProcessInterpolatedList` walks a LIST, not the entity array**
/// (`c_baseentity.cpp:3123`), and its own comment says why: *"Interpolate the minimal set of
/// entities that need it."* Membership is `ShouldInterpolate` (`c_baseentity.cpp:3029`):
///
/// <code>
/// if ( render->GetViewEntity() == index ) return true;
/// if ( index == 0 || !GetModel() )        return false;
/// if ( IsVisible() )                      return true;   // always interpolate if visible
/// // if any movement child needs interpolation, we have to interpolate too
/// C_BaseEntity *pChild = FirstMoveChild();
/// while( pChild )
/// {
///     if ( pChild->ShouldInterpolate() )
///         return true;
///     pChild = pChild->NextMovePeer();
/// }
/// return false;                                          // don't interpolate
/// </code>
///
/// **The list was a SET the renderer passed in, and every clause of it was wrong** (B385). The
/// presenter handed `MomentScene.PosedEntities` — the entities that had reached `SetupBones` on the
/// previous frame — on the strength of a claim written into this very file: *"`IsVisible()` is the
/// LAST render's answer, so the engine gates this frame's interpolation on the previous frame's
/// visibility."* It is not. `IsVisible()` is
/// <c>m_hRender != INVALID_CLIENT_RENDER_HANDLE</c> (`c_baseentity.h:691`), which
/// <c>UpdateVisibility</c> sets from <c>ShouldDraw() &amp;&amp; !IsDormant()</c>
/// (`c_baseentity.cpp:1421`) — the render mode, the model, `EF_NODRAW` and the index, and nothing
/// else. No frame, no frustum, no skeleton.
///
/// Three populations were therefore excluded from a list the engine has them on:
///
/// - **every brush entity**, because the set was filled inside the skinned branch of `Instances` and
///   brushwork has no bones — so no door in any map was ever interpolated;
/// - **everything the frustum rejected**, which the engine keeps on the list;
/// - **the whole world on any frame with a viewmodel**, because that pass calls `Instances` again and
///   `Instances` clears the set.
///
/// So the timeline answers it, and only `render->GetViewEntity()` still arrives from the window.
///
/// **What an ungated entity gets is its last STATED pose**, not a wrong one — the engine leaves a
/// non-member at whatever its variables last held. Position, not extrapolation.
/// </remarks>
public sealed class InterpolationListTests
{
    /// <summary>A track that moves 200 units between two keyframes, drawn normally.</summary>
    private static ScenePropTrack Moving(int entity)
    {
        ScenePropTrack track = new(entity, "models/props/crate.mdl");

        track.Add(0, new ScenePose { X = 0f, Y = 0f, Z = 0f });
        track.Add(14, new ScenePose { X = 200f, Y = 0f, Z = 0f });

        return track;
    }

    /// <summary>The same mover, declaring <c>kRenderNone</c> — which is granary's own doors.</summary>
    /// <remarks>
    /// **The manipulation is the CAUSE now, not the list.** The old fixture passed a set naming who
    /// blends, which tests the parameter rather than the rule; a mover that declares
    /// <c>kRenderNone</c> is what `ShouldDraw` actually refuses (`c_baseentity.cpp:1444`), and it is
    /// what eighteen `func_door`s on `cp_fulgur` declare.
    /// </remarks>
    private static ScenePropTrack Invisible(int entity)
    {
        ScenePropTrack track = new(entity, "*40");

        track.Add(0, new ScenePose { X = 0f, RenderMode = RenderModes.None });
        track.Add(14, new ScenePose { X = 200f, RenderMode = RenderModes.None });

        return track;
    }

    /// <summary>A prop hung on <paramref name="parent"/> that moves nowhere of its own accord.</summary>
    /// <remarks>
    /// **Flat on purpose, and that is the granary door.** The visible leaf of every one of these
    /// pairings has a local offset it keeps for the whole map — <c>door_slide_door.mdl</c> measured at
    /// <c>(0 -1 5)</c> against its `func_door`, entity 207 on 205 — so all of its motion is its
    /// parent's, and a child that moved on its own could not tell the two apart.
    /// </remarks>
    private static ScenePropTrack Hanging(int entity, int parent, int mode = RenderModes.Normal)
    {
        ScenePropTrack track =
            new(entity, "models/props_gameplay/door_slide_door.mdl") { AttachedTo = parent };

        track.Add(0, new ScenePose { X = 0f, Y = -1f, Z = 5f, RenderMode = mode });

        return track;
    }

    /// <summary>What one entity's pose is at a tick, through the production sampler.</summary>
    private static float DrawnX(
        int entity, IEnumerable<ScenePropTrack> tracks, int? viewEntity = null)
    {
        List<SceneProp> props = [];

        DemoTimeline.ForTracks([.. tracks]).PropsAt(14d, props, viewEntity);

        return props.Single(prop => prop.EntityIndex == entity).Pose.X;
    }

    /// <remarks>
    /// The control, and it has to come first: without it a test showing the gated case is flat cannot
    /// tell "not interpolated" from "this fixture never interpolates".
    ///
    /// **85.714 exactly.** The delay is `(int)( 0.5 + 0.1/0.015 ) + 1` = 8 ticks (`shareddefs.h:17`),
    /// so tick 14 samples target 6 and the pair is the keyframes at 0 and 14: a fraction of 6/14 of
    /// 200 units. A range assertion here would pass against a wrong spline as readily as the right
    /// one.
    /// </remarks>
    [Test]
    public void PropsAt_ForAVisibleEntity_BlendsBetweenKeyframes() =>
        DrawnX(3, [Moving(3)]).ShouldBe(85.71429f);

    /// <remarks>
    /// **`ShouldDraw`'s first test, which is the only clause a door mover ever fails**
    /// (`c_baseentity.cpp:1444`, *"Some rendermodes prevent rendering"*). With nothing hanging off it
    /// the fourth clause has nowhere to go, so this holds — and it is the counterpart that stops the
    /// test below passing against an implementation that simply blends everything.
    /// </remarks>
    [Test]
    public void PropsAt_ForAnInvisibleEntityWithNoChildren_HoldsItsLastStatedPose() =>
        DrawnX(3, [Invisible(3)]).ShouldBe(200f);

    /// <remarks>
    /// **The fourth clause, and it is `cp_fulgur`'s gates exactly** (B385): a `kRenderNone`
    /// `func_door` carrying a `prop_dynamic` that draws. The oracle is EQUALITY with the mover
    /// interpolated on its own account, not a range — the engine's clause returns plain <c>true</c>,
    /// so a mover forced onto the list by its child is on it in the same sense a visible one is, and
    /// any answer between held and blended would be a third behaviour Valve does not have.
    /// </remarks>
    [Test]
    public void PropsAt_ForAnInvisibleParentWhoseChildDraws_InterpolatesTheParentToo() =>
        DrawnX(3, [Invisible(3), Hanging(4, 3)]).ShouldBe(85.71429f);

    /// <remarks>
    /// **The clause's own `return false`.** A child that does not draw either forces nothing, so the
    /// mover holds — without this, "interpolate any parent that has children" would pass the test
    /// above and put every mover in the map back on the list.
    /// </remarks>
    [Test]
    public void PropsAt_ForAnInvisibleParentWhoseChildIsAlsoInvisible_HoldsItsLastStatedPose() =>
        DrawnX(3, [Invisible(3), Hanging(4, 3, RenderModes.None)]).ShouldBe(200f);

    /// <remarks>
    /// **Clause TWO, asked of the child** — <c>if ( index == 0 || !GetModel() ) return false;</c>
    /// (`c_baseentity.cpp:3034`). The fourth clause calls `ShouldInterpolate` on the child, so the
    /// child's own model decides; a wearable whose model index has not arrived yet forces nothing.
    ///
    /// **This test exists because a sabotage reddened nothing.** Swapping the walk's
    /// <c>Visible(child, …)</c> for <c>Visible(parent, …)</c> left all five tests green — both tracks
    /// in every other fixture carry a model and a non-zero index, so the only field that discriminated
    /// was the render mode, and that comes off the pose either way. A sabotage that reddens nothing
    /// names the missing input (`docs/memory/most-of-a-decoder-is-untested.md`), and the missing input
    /// was a child whose TRACK differs from its parent's rather than whose pose does.
    /// </remarks>
    [Test]
    public void PropsAt_ForAnInvisibleParentWhoseChildHasNoModel_HoldsItsLastStatedPose()
    {
        ScenePropTrack nameless = new(entityIndex: 4, string.Empty) { AttachedTo = 3 };

        nameless.Add(0, new ScenePose { X = 0f, Y = -1f, Z = 5f });

        DrawnX(3, [Invisible(3), nameless]).ShouldBe(200f);
    }

    /// <remarks>
    /// **Clause one, <c>render-&gt;GetViewEntity() == index</c>** (`c_baseentity.cpp:3031`). It is
    /// tested BEFORE the model and the render mode, so it outranks every reason not to interpolate —
    /// the entity the view is attached to is interpolated even while it is invisible, which is what a
    /// first-person camera on a dead or hidden entity needs.
    /// </remarks>
    [Test]
    public void PropsAt_ForTheViewEntity_BlendsEvenWhileInvisible() =>
        DrawnX(3, [Invisible(3)], viewEntity: 3).ShouldBe(85.71429f);
}
