using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// That a model which has not moved is not lit again.
/// </summary>
/// <remarks>
/// **B99, and the measurement that justifies it.** Per second of wall time while playing, posing
/// costs about 900 ms of which lighting is 320, against 3.4 ms to draw the entire uncalled map. Most
/// of that lighting recomputes a value that cannot have changed: map lights never move, and most
/// entities in a demo are standing still at any moment.
///
/// **The cost per lookup is why it matters rather than being a micro-optimisation.** A cube comes
/// from an inverse-squared-distance average over the sixteen ambient samples in the model's leaf,
/// then `LocalLights` ranks all 477 of the map's world lights to pick four and evaluates a falloff
/// per light for each of six cube faces. The sun on top of that traces a ray through the BSP to ask
/// whether the sky is visible. None of it changes while the model stands still.
///
/// **Correctness first: this must return the SAME value, not merely fewer of them.** The lighting
/// path took most of a session to get right, and a cache that quietly answers differently would undo
/// that while every existing test still passed.
/// </remarks>
public sealed class LightingCacheTests
{
    /// <summary>Counts how often the expensive lookups are asked.</summary>
    private sealed class Probe
    {
        public int AmbientCalls { get; private set; }

        public int SunCalls { get; private set; }

        public AmbientCube Light(float x, float y, float z)
        {
            AmbientCalls++;

            // Varies with position, so a cache that returned a stale value for a MOVED model would
            // be caught by the value rather than only by the call count.
            float shade = (x + y + z) / 1000f;

            return new AmbientCube(
                (shade, shade, shade), (shade, shade, shade), (shade, shade, shade),
                (shade, shade, shade), (shade, shade, shade), (shade, shade, shade));
        }

        public SunLight? Sun(float x, float y, float z)
        {
            SunCalls++;

            // Carries the position too, so a stale sun is as visible as a stale cube.
            return new SunLight((x + y + z) / 1000f, 1f, 1f, 0f, 0f, -1f);
        }
    }

    private static SceneProp Prop(float x) =>
        new(
            1,
            "models/props/crate.mdl",
            SceneModelKind.Studio,
            new ScenePose { X = x, Scale = 1f },
            null);

    private static PropModels.ModelFrames OneTriangle(string path) =>
        new(
            [[
                new PropVertex(0f, 0f, 0f, 0f, 0f, 0),
                new PropVertex(1f, 0f, 0f, 1f, 0f, 0),
                new PropVertex(1f, 1f, 0f, 1f, 1f, 0),
            ]],
            new Dictionary<int, (int, int, float)>(),
            [],
            []);

    [Test]
    public void AStationaryModel_IsLitOnce()
    {
        EntityModelSet models = new();
        Probe probe = new();
        List<ModelInstance> instances = [];

        SceneProp[] props = [Prop(100f)];

        models.Add(props, OneTriangle);

        models.Instances(props, instances, probe.Light, probe.Sun, 0d);
        models.Instances(props, instances, probe.Light, probe.Sun, 0.016d);
        models.Instances(props, instances, probe.Light, probe.Sun, 0.032d);

        // Three frames, one lookup. The seconds differ because animation advances with time, which
        // must not invalidate lighting: a spinning health pack is lit by the same leaf throughout.
        probe.AmbientCalls.ShouldBe(1);
        probe.SunCalls.ShouldBe(1);
    }

    [Test]
    public void AMovedModel_IsLitAgain()
    {
        // **The control, and the half that makes the test about correctness.** Without it a cache
        // that never refreshed would pass the test above perfectly while freezing every moving
        // entity's lighting at wherever it first appeared.
        EntityModelSet models = new();
        Probe probe = new();
        List<ModelInstance> instances = [];

        SceneProp[] first = [Prop(100f)];
        SceneProp[] moved = [Prop(200f)];

        models.Add(first, OneTriangle);

        models.Instances(first, instances, probe.Light, probe.Sun, 0d);
        models.Instances(moved, instances, probe.Light, probe.Sun, 0.016d);

        probe.AmbientCalls.ShouldBe(2);
        probe.SunCalls.ShouldBe(2);
    }

    [Test]
    public void AMovedModel_IsLitByWhereItNowStands()
    {
        // The value, not the call count. A cache keyed on the entity but not refreshed on position
        // would return the same count as a correct one here and the wrong colour.
        EntityModelSet models = new();
        Probe probe = new();
        List<ModelInstance> instances = [];

        SceneProp[] first = [Prop(100f)];
        SceneProp[] moved = [Prop(900f)];

        models.Add(first, OneTriangle);

        models.Instances(first, instances, probe.Light, probe.Sun, 0d);

        // **Asserted non-null rather than defaulted, because null now means something.** A brush
        // entity carries no cube at all (B131), and a `?? default` here would report a studio model
        // that stopped being lit as a cube of zeroes — which is a number, and would compare.
        float near = instances[0].Light.ShouldNotBeNull().PositiveX.Red;

        models.Instances(moved, instances, probe.Light, probe.Sun, 0.016d);
        float far = instances[0].Light.ShouldNotBeNull().PositiveX.Red;

        // The probe's shade rises with position, so these must differ and in the right direction.
        far.ShouldBeGreaterThan(near);
    }

    [Test]
    public void TwoEntitiesAtOnePosition_AreCachedApart()
    {
        // Keyed by entity as well as position: sharing one slot would let a second model take the
        // first's lighting, which is the kind of fault that shows only when two things overlap.
        EntityModelSet models = new();
        Probe probe = new();
        List<ModelInstance> instances = [];

        SceneProp[] pair =
        [
            new(1, "models/props/crate.mdl", SceneModelKind.Studio,
                new ScenePose { X = 100f, Scale = 1f }, null),
            new(2, "models/props/crate.mdl", SceneModelKind.Studio,
                new ScenePose { X = 100f, Scale = 1f }, null),
        ];

        models.Add(pair, OneTriangle);
        models.Instances(pair, instances, probe.Light, probe.Sun, 0d);

        probe.AmbientCalls.ShouldBe(2);
    }
}
