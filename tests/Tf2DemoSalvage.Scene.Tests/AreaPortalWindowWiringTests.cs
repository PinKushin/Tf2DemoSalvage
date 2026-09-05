using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The areaportal window's blend reaches the drawn instance, not just its own tests (B358).
/// </summary>
/// <remarks>
/// **`AreaPortalWindowConformanceTests` says the arithmetic is Valve's; this says the scene runs
/// it.** The defect it guards is the one this project has shipped three times — a value decoded,
/// retained, unit-tested and never read — and here it would look exactly like the original bug: a
/// solid black panel in every spawn window, with a green suite.
///
/// **The prop is a brush model with a `PortalWindow` tuple**, which is the only thing that marks an
/// entity as one: `DT_FuncAreaPortalWindow` is the sole table sending those three floats, so their
/// presence identifies the class without a name comparison.
/// </remarks>
public sealed class AreaPortalWindowWiringTests
{
    /// <summary>Harvest's own numbers, so a failure reads against a real map.</summary>
    private const float FadeStart = 1200f;

    /// <summary>Harvest's own.</summary>
    private const float FadeEnd = 1500f;

    /// <remarks>
    /// **The case that was broken.** Standing a few hundred units from the glass, every harvest
    /// window has `TranslucencyLimit 0`, so the black brush draws at alpha ZERO and the opening is
    /// see-through. Before this it drew at 255 and the window was a black rectangle.
    /// </remarks>
    [Test]
    public void Instances_ForAWindowCloserThanItsFadeStart_CarryZeroAlpha()
    {
        EntityModelSet models = new() { ViewOrigin = (0f, 0f, 0f) };
        List<ModelInstance> instances = [];

        SceneProp[] props = [Window(x: 300f, limit: 0f)];

        models.Add(props, OneTriangle);
        models.Instances(props, instances);

        instances.Count.ShouldBe(1);
        instances[0].Alpha.ShouldBe(0, "inside the fade start it draws at the translucency limit");
        models.PortalWindowsFaded.ShouldBe(1, "counted where the blend is written");
    }

    /// <remarks>
    /// The other end, and the half that gives the panel its purpose: far away it is solid, which is
    /// what hides the room the areaportal is culling. Without this, "always invisible" passes the
    /// test above and the window never occludes anything.
    /// </remarks>
    [Test]
    public void Instances_ForAWindowBeyondItsFadeDistance_CarryFullAlpha()
    {
        EntityModelSet models = new() { ViewOrigin = (0f, 0f, 0f) };
        List<ModelInstance> instances = [];

        SceneProp[] props = [Window(x: 4000f, limit: 0f)];

        models.Add(props, OneTriangle);
        models.Instances(props, instances);

        instances[0].Alpha.ShouldBe(255);
    }

    /// <remarks>
    /// **The bystander, and it is the control that matters most here.** An ordinary prop at the
    /// same distance must be untouched — a blend applied to everything would black out the map at
    /// range and look like a culling fault, which is the very symptom this fix was chasing.
    /// </remarks>
    [Test]
    public void Instances_ForAnOrdinaryPropBesideAWindow_AreUnaffected()
    {
        EntityModelSet models = new() { ViewOrigin = (0f, 0f, 0f) };
        List<ModelInstance> instances = [];

        SceneProp[] props = [Window(x: 300f, limit: 0f), Plain(x: 300f)];

        models.Add(props, OneTriangle);
        models.Instances(props, instances);

        instances.Count.ShouldBe(2);
        instances[0].Alpha.ShouldBe(0);
        instances[1].Alpha.ShouldBe(255, "a prop that is not a portal window keeps its own alpha");
        models.PortalWindowsFaded.ShouldBe(1, "one window, not two props");
    }

    /// <remarks>
    /// **The window's own render amount decides nothing**, which is Valve's arrangement rather than
    /// an accident: `ComputeFxBlend` sets 255 with the comment *"We reset our blend down below"*
    /// and `DrawModel` replaces it. Harvest's brushes say `renderamt 255`, so a wiring that
    /// multiplied the two would keep the panel solid at every distance — passing the far test and
    /// failing the near one for a reason nobody would look for.
    /// </remarks>
    [Test]
    public void Instances_ForAWindowDeclaringFullRenderAmount_StillTakeTheDistanceBlend()
    {
        EntityModelSet models = new() { ViewOrigin = (0f, 0f, 0f) };
        List<ModelInstance> instances = [];

        SceneProp[] props = [Window(x: 300f, limit: 0f) with { }];

        models.Add(props, OneTriangle);
        models.Instances(props, instances);

        instances[0].Alpha.ShouldBe(0, "renderamt 255 is exactly what the fixture declares");
    }

    /// <remarks>
    /// **A translucency limit above zero is smoked glass**, and it is what separates this from a
    /// plain distance fade: the floor is the limit rather than nothing, so the panel never fully
    /// clears however close you stand.
    /// </remarks>
    [Test]
    public void Instances_ForAWindowWithATranslucencyLimit_NeverClearBelowIt()
    {
        EntityModelSet models = new() { ViewOrigin = (0f, 0f, 0f) };
        List<ModelInstance> instances = [];

        SceneProp[] props = [Window(x: 10f, limit: 0.5f)];

        models.Add(props, OneTriangle);
        models.Instances(props, instances);

        instances[0].Alpha.ShouldBe(128, "0.5 of 255, rounded");
    }

    /// <remarks>
    /// **A viewer with no eye must not blend anything**, the same rule `EntityFadeWiringTests`
    /// states: `ViewOrigin` is null until a frame has been posed, and treating that as the world
    /// origin would put every window in the map inside its own fade band.
    /// </remarks>
    [Test]
    public void Instances_BeforeAViewOriginIsKnown_LeaveTheWindowOpaque()
    {
        EntityModelSet models = new();
        List<ModelInstance> instances = [];

        SceneProp[] props = [Window(x: 300f, limit: 0f)];

        models.Add(props, OneTriangle);
        models.Instances(props, instances);

        instances[0].Alpha.ShouldBe(255);
        models.PortalWindowsFaded.ShouldBe(0, "nothing was blended, so nothing is counted");
    }

    /// <summary>A brush entity carrying the three floats only this class sends.</summary>
    private static SceneProp Window(float x, float limit) =>
        new(
            1,
            "*29",
            SceneModelKind.Brush,
            new ScenePose
            {
                X = x,
                RenderAlpha = 255,
                PortalWindow = (FadeStart, FadeEnd, limit),
            },
            null);

    /// <summary>The same placement with no window tuple, for the bystander.</summary>
    private static SceneProp Plain(float x) =>
        new(
            2,
            "models/props/crate.mdl",
            ScenePropTrack.Classify("models/props/crate.mdl"),
            new ScenePose { X = x },
            null);

    /// <summary>The smallest model that draws.</summary>
    private static PropModels.ModelFrames OneTriangle(string model) =>
        ModelFramesFixture.OneTriangle(model);
}
