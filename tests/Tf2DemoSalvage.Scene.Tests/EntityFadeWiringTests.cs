using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The distance fade reaches the drawn instance, not just <see cref="EntityFade"/>.
/// </summary>
/// <remarks>
/// **This file exists because B268 was a wiring defect, not an arithmetic one.**
/// <c>FxBlend.Compute</c> already took <c>clientSideFade</c>, already multiplied it in, and was
/// already unit-tested — and no caller ever passed it, so every entity drew opaque at any distance.
/// A test of <see cref="EntityFade.DistanceAlpha"/> alone would have passed throughout, which is
/// exactly the gap `docs/memory/output-level-assertion-or-it-is-not-done.md` describes.
///
/// So these assert on <c>ModelInstance.Alpha</c> coming out of <c>EntityModelSet.Instances</c> —
/// the call the renderer makes — and predict the exact byte rather than checking it moved.
/// </remarks>
public sealed class EntityFadeWiringTests
{
    /// <summary>A band whose falloff lands on whole numbers, so the test can predict one.</summary>
    private const float Near = 100f;

    /// <summary>The far end of that band.</summary>
    private const float Far = 300f;

    [Test]
    public void Instances_WithTheEyeInsideTheFadeBand_CarryThePartialAlpha()
    {
        EntityModelSet models = new() { ViewOrigin = (0f, 0f, 0f) };
        List<ModelInstance> instances = [];

        // 200 units out, so squared: near 10,000, far 90,000, here 40,000.
        // 255 / (90,000 - 10,000) * (90,000 - 40,000) = 159.375, truncated to 159.
        SceneProp[] props = [Faded(x: 200f)];

        models.Add(props, OneTriangle);
        models.Instances(props, instances);

        instances.Count.ShouldBe(1);
        instances[0].Alpha.ShouldBe(159);
    }

    [Test]
    public void Instances_WithTheEyeInsideTheNearBound_CarryFullAlpha()
    {
        EntityModelSet models = new() { ViewOrigin = (0f, 0f, 0f) };
        List<ModelInstance> instances = [];

        SceneProp[] props = [Faded(x: 50f)];

        models.Add(props, OneTriangle);
        models.Instances(props, instances);

        instances[0].Alpha.ShouldBe(255, "nothing inside m_fadeMinDist fades at all");
    }

    [Test]
    public void Instances_PastTheFarBound_CarryZeroAlpha()
    {
        EntityModelSet models = new() { ViewOrigin = (0f, 0f, 0f) };
        List<ModelInstance> instances = [];

        SceneProp[] props = [Faded(x: 400f)];

        models.Add(props, OneTriangle);
        models.Instances(props, instances);

        instances[0].Alpha.ShouldBe(0, "past m_fadeMaxDist the engine draws nothing of it");
    }

    /// <remarks>
    /// **The control for the three above, and it is the one that would have caught the defect.**
    /// The eye is at the same place and the entity declares no band, so the fade must not touch it.
    /// If the wiring multiplied in something other than the distance fade — a stale value, a
    /// mis-keyed lookup — this is where an entity that should be opaque comes back dimmed.
    /// </remarks>
    [Test]
    public void Instances_WithNoFadeBandDeclared_CarryFullAlphaAtAnyDistance()
    {
        EntityModelSet models = new() { ViewOrigin = (0f, 0f, 0f) };
        List<ModelInstance> instances = [];

        SceneProp[] props = [Prop(x: 4000f)];

        models.Add(props, OneTriangle);
        models.Instances(props, instances);

        instances[0].Alpha.ShouldBe(255);
    }

    /// <remarks>
    /// **A viewer with no eye must not fade everything to nothing.** `ViewOrigin` is null until a
    /// frame has been posed, and `Instances` is reachable before that — from a test, and from the
    /// first frame after a level change. Zero would be a plausible-looking origin and would put the
    /// whole map inside the band of anything near the world origin.
    /// </remarks>
    [Test]
    public void Instances_BeforeAViewOriginIsKnown_CarryFullAlpha()
    {
        EntityModelSet models = new();
        List<ModelInstance> instances = [];

        SceneProp[] props = [Faded(x: 4000f)];

        models.Add(props, OneTriangle);
        models.Instances(props, instances);

        instances[0].Alpha.ShouldBe(255);
    }

    /// <summary>A prop that declares the fade band this file predicts against.</summary>
    private static SceneProp Faded(float x) =>
        new(
            1,
            "models/props/crate.mdl",
            ScenePropTrack.Classify("models/props/crate.mdl"),
            new ScenePose
            {
                X = x,
                FadeMinimumDistance = Near,
                FadeMaximumDistance = Far,
            },
            null);

    /// <summary>The same prop with no band, for the control.</summary>
    private static SceneProp Prop(float x) =>
        new(
            1,
            "models/props/crate.mdl",
            ScenePropTrack.Classify("models/props/crate.mdl"),
            new ScenePose { X = x },
            null);

    /// <summary>The smallest model that draws.</summary>
    private static PropModels.ModelFrames OneTriangle(string model) =>
        ModelFramesFixture.OneTriangle(model);
}
