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
/// </code>
///
/// **`IsVisible()` is the LAST render's answer**, so the engine gates this frame's interpolation on
/// the previous frame's visibility and accepts that an entity becoming visible is interpolated one
/// frame late. That is what makes this implementable here at all: our own cull runs after the view,
/// in `Pose`, and its result is available to the next frame's sampling exactly as Valve's is.
///
/// **What an ungated entity gets is its last STATED pose**, not a wrong one — the engine leaves a
/// non-member at whatever its variables last held. Position, not extrapolation.
/// </remarks>
public sealed class InterpolationListTests
{
    /// <summary>A track that moves 200 units between two keyframes.</summary>
    private static ScenePropTrack Moving(int entity)
    {
        ScenePropTrack track = new(entity, "models/props/crate.mdl");

        track.Add(0, new ScenePose { X = 0f, Y = 0f, Z = 0f });
        track.Add(14, new ScenePose { X = 200f, Y = 0f, Z = 0f });

        return track;
    }

    /// <remarks>
    /// The control, and it has to come first: without it a test showing the gated case is flat
    /// cannot tell "not interpolated" from "this fixture never interpolates".
    /// </remarks>
    [Test]
    public void PropsAt_ForAnInterpolatedEntity_BlendsBetweenKeyframes()
    {
        DemoTimeline timeline = DemoTimeline.ForTracks([Moving(3)]);

        List<SceneProp> props = [];
        timeline.PropsAt(14d, props, interpolate: new HashSet<int> { 3 });

        float x = props.Single().Pose.X;

        x.ShouldBeGreaterThan(0f, "sampled between the keyframes, so it must have moved");
        x.ShouldBeLessThan(200f, "and must not have arrived");
    }

    [Test]
    public void PropsAt_ForAnEntityNotOnTheList_HoldsItsLastStatedPose()
    {
        DemoTimeline timeline = DemoTimeline.ForTracks([Moving(3)]);

        List<SceneProp> props = [];
        timeline.PropsAt(14d, props, interpolate: new HashSet<int>());

        // **The last pose STATED at tick 14, which is 200, and this asserted 0** (B370). The old answer
        // came from `Held` subtracting the interpolation delay — so it held the keyframe at tick 0 and
        // called that "last stated". But `cl_interp` belongs to `CInterpolatedVar`, and an entity off
        // `g_InterpolationList` never reaches one: its `m_vecOrigin` is whatever the last update assigned,
        // read live. This class's own remarks say so — *"the engine leaves a non-member at whatever its
        // variables last held. Position, not extrapolation."* — so the assertion contradicted the
        // principle it was written to demonstrate.
        //
        // The delay also put the two samplers a window apart, which is what the scheduler tripped on:
        // it woke a track at its keyframe's own tick while `Held` changed answer eight ticks later.
        props.Single().Pose.X.ShouldBe(200f);
    }

    /// <remarks>
    /// **No list at all means interpolate everything**, which is what every existing caller and
    /// every test that does not care about this relies on — and it is the safe direction, since
    /// drawing a stale pose is a visible defect and drawing a fresh one never is.
    /// </remarks>
    [Test]
    public void PropsAt_WithNoListGiven_InterpolatesEverything()
    {
        DemoTimeline timeline = DemoTimeline.ForTracks([Moving(3)]);

        List<SceneProp> props = [];
        timeline.PropsAt(14d, props);

        props.Single().Pose.X.ShouldBeGreaterThan(0f);
    }
}
