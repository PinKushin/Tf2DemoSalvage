using System;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// What light a model is drawn with, and how often that is recomputed.
/// </summary>
/// <remarks>
/// **The cache is the reason this type exists and it is not a micro-optimisation.** Lighting cost
/// 320 ms of every second against 3.4 ms to draw the whole map (B99), and nearly all of it
/// recomputed an unchanged answer: a cube is an inverse-squared average over sixteen ambient
/// samples, <c>LocalLights</c> ranks all 477 of a map's world lights to pick four and evaluates a
/// falloff per light for six faces, and the sun traces a ray through the BSP.
///
/// So the assertions here are call COUNTS on the sampler, not values. "Sampled once" and "sampled
/// every frame" produce identical cubes, and no assertion on a cube can separate them.
/// </remarks>
public sealed class ModelLightingTests
{
    [Test]
    public void For_AModelThatHasNotMoved_IsSampledOnce()
    {
        CountingSampler sampler = new();
        ModelLighting lighting = new(Origin, new RecordingLogger());

        for (int frame = 0; frame < 5; frame++)
        {
            lighting.For(Prop(entity: 4, x: 100f), sampler.At, null);
        }

        sampler.Calls.ShouldBe(1);
    }

    [Test]
    public void For_AModelThatMoved_IsSampledAgain()
    {
        // The control for the test above. A cache that never invalidates satisfies "sampled once"
        // perfectly and lights every moving player at wherever it first stood.
        CountingSampler sampler = new();
        ModelLighting lighting = new(Origin, new RecordingLogger());

        lighting.For(Prop(entity: 4, x: 100f), sampler.At, null);
        lighting.For(Prop(entity: 4, x: 101f), sampler.At, null);

        sampler.Calls.ShouldBe(2);
    }

    [Test]
    public void For_TwoModelsStandingInOnePlace_EachGetsItsOwnEntry()
    {
        // **Keyed on the entity as well as the point**, because two models can share a position and
        // must not share a slot — a hat and its wearer are at the same illumination point by
        // construction once the hat borrows it.
        CountingSampler sampler = new();
        ModelLighting lighting = new(Origin, new RecordingLogger());

        lighting.For(Prop(entity: 4, x: 100f), sampler.At, null);
        lighting.For(Prop(entity: 5, x: 100f), sampler.At, null);

        sampler.Calls.ShouldBe(2);
    }

    [Test]
    public void For_ABrushEntity_TakesNoCubeAndNoSun()
    {
        // **A brush entity is lightmapped (B131).** Its faces were lit by vrad exactly as the
        // wall's were and the samples travel on the vertices; the shader's ambient-cube branch
        // OVERWRITES the lightmap sample rather than adding to it, so supplying a cube here is
        // precisely what made an open door a flat panel against a shaded corridor.
        CountingSampler sampler = new();
        ModelLighting lighting = new(Origin, new RecordingLogger());

        ModelLight lit = lighting.For(
            Prop(entity: 9, x: 100f, kind: SceneModelKind.Brush), sampler.At, null);

        lit.Light.ShouldBeNull();
        lit.Sun.ShouldBeNull();

        // And it costs nothing, which is the other half: the sampler is never called at all.
        sampler.Calls.ShouldBe(0);
    }

    [Test]
    public void For_AModelLitByNothing_IsReportedOncePerModel()
    {
        // **A model lit by nothing draws black, and that is worth saying out loud.** A player's
        // origin is at its FEET, so a point resting exactly on a floor plane can land in the solid
        // leaf below it, which carries no light. It shows as a player turning black in some places
        // and recovering in others — a lighting quirk rather than a lookup landing in solid.
        RecordingLogger log = new();
        ModelLighting lighting = new(Origin, log);

        lighting.For(Prop(entity: 4, x: 100f), Dark, null);
        lighting.For(Prop(entity: 5, x: 200f), Dark, null);

        log.Count("is lit by nothing").ShouldBe(1);
    }

    [Test]
    public void For_AModelThatIsLit_IsNotReportedAsDark()
    {
        // The control: a warning that fired for everything would satisfy the test above.
        RecordingLogger log = new();
        ModelLighting lighting = new(Origin, log);

        lighting.For(Prop(entity: 4, x: 100f), Bright, null);

        log.Count("is lit by nothing").ShouldBe(0);
    }

    [Test]
    public void For_WithNoSampler_ReportsNothingRatherThanEveryModelAsDark()
    {
        // **Null means "this caller does not do lighting", not "everything is black".** The
        // offscreen target and several tests pass null, and a warning per model there would bury a
        // real one — the same overlogging shape as B163.
        RecordingLogger log = new();
        ModelLighting lighting = new(Origin, log);

        lighting.For(Prop(entity: 4, x: 100f), null, null);

        log.Count("is lit by nothing").ShouldBe(0);
    }

    [Test]
    public void Ticks_AfterSampling_AreAccumulated()
    {
        // The measurement that separated bones from lighting inside a 900 ms second (B99). Asserted
        // as "more than nothing" rather than as a duration, because a stopwatch reading is not a
        // prediction — what is being pinned is that the meter runs at all.
        ModelLighting lighting = new(Origin, new RecordingLogger());

        lighting.Ticks = 0;
        lighting.For(Prop(entity: 4, x: 100f), Bright, null);

        lighting.Ticks.ShouldBeGreaterThan(0);
    }

    /// <summary>A cube sampler that counts how often it was asked.</summary>
    /// <remarks>
    /// A type rather than a lambda over a <c>ref</c> counter, which does not compile in a form that
    /// works — the first attempt here incremented a boxed array and never wrote the count back, so
    /// every assertion would have read zero and the tests would have "passed" measuring nothing.
    /// </remarks>
    private sealed class CountingSampler
    {
        public int Calls { get; private set; }

        /// <summary>The sampler to hand to <c>For</c>.</summary>
        /// <remarks>
        /// A lambda rather than a method, so the three coordinates can be discarded. As a method
        /// they are unused parameters and S1172 objects — correctly, since a reader cannot tell
        /// from the signature that they are ignored on purpose.
        /// </remarks>
        public Func<float, float, float, AmbientCube> At => (_, _, _) =>
        {
            Calls++;
            return Lit;
        };
    }

    private static AmbientCube Bright(float x, float y, float z) => Lit;

    private static AmbientCube Dark(float x, float y, float z) => default;

    private static readonly AmbientCube Lit = new()
    {
        PositiveX = (1f, 1f, 1f),
        NegativeX = (1f, 1f, 1f),
        PositiveY = (1f, 1f, 1f),
        NegativeY = (1f, 1f, 1f),
        PositiveZ = (1f, 1f, 1f),
        NegativeZ = (1f, 1f, 1f),
    };

    /// <summary>Lights a model at its own origin, which is what a model with no offset gets.</summary>
    private static (float X, float Y, float Z) Origin(SceneProp prop, ScenePose pose) =>
        (pose.X, pose.Y, pose.Z);

    private static SceneProp Prop(
        int entity, float x, SceneModelKind kind = SceneModelKind.Studio) =>
        new(entity, "models/props/crate.mdl", kind, new ScenePose { X = x }, null);
}
