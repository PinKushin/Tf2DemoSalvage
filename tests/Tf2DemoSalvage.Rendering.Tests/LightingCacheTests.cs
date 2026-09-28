using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>How a drawn model asks for its light, frame to frame.</summary>
/// <remarks>
/// **B99's per-entity memo is gone (B423): the engine's light cache is the memo.** Every drawn model asks every frame,
/// as `LightcacheGet` (`0x1801b9cd0`) is called every frame, and the cost B99 measured (320 ms a second) now falls on a
/// cache hit — a leaf walk and a lookup — with full sampling only on a miss, capped per frame by `lightcache_maxmiss`.
/// These fixtures pass a bare probe, so they count asks, not cache hits.
/// </remarks>
public sealed class LightingCacheTests
{
    /// <summary>Counts how often the expensive lookups are asked.</summary>
    private sealed class Probe
    {
        public int AmbientCalls { get; private set; }

        public int SunCalls { get; private set; }

        public PointLighting Light(float x, float y, float z)
        {
            AmbientCalls++;

            // Varies with position, so a cache that returned a stale value for a MOVED model would
            // be caught by the value rather than only by the call count.
            float shade = (x + y + z) / 1000f;

            return PointLighting.Bounce(new AmbientCube(
                (shade, shade, shade), (shade, shade, shade), (shade, shade, shade),
                (shade, shade, shade), (shade, shade, shade), (shade, shade, shade)));
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
    public void Instances_AStationaryModelOverThreeFrames_AsksThreeTimes()
    {
        EntityModelSet models = new();
        Probe probe = new();
        List<ModelInstance> instances = [];

        SceneProp[] props = [Prop(100f)];

        models.Add(props, OneTriangle);

        models.Instances(props, instances, probe.Light, probe.Sun, 0d);
        models.Instances(props, instances, probe.Light, probe.Sun, 0.016d);
        models.Instances(props, instances, probe.Light, probe.Sun, 0.032d);

        // Three frames, three asks: the light cache behind the probe answers the repeats.
        probe.AmbientCalls.ShouldBe(3);
        probe.SunCalls.ShouldBe(3);
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
